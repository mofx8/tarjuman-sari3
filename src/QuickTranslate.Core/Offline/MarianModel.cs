using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace QuickTranslate.Core.Offline;

/// <summary>
/// One Marian (Opus-MT) translation direction running on ONNX Runtime: SentencePiece tokenizer,
/// encoder, then greedy decoding with a no-repeat guard. Files come from a downloaded language pack.
/// </summary>
public sealed class MarianModel : IDisposable
{
    private readonly InferenceSession _encoder, _decoder;
    private readonly SentencePieceTokenizer _spm;
    private readonly Dictionary<string, int> _vocab;
    private readonly string[] _inverse;
    private readonly int _eos, _pad, _unk, _startId;
    private readonly string _encIn, _encMask, _encOut, _decIds, _decHidden, _decMask, _decOut;
    private readonly object _run = new();

    public static readonly string[] RequiredFiles =
        { "encoder_model_quantized.onnx", "decoder_model_quantized.onnx", "source.spm", "vocab.json", "config.json" };

    public MarianModel(string directory)
    {
        _vocab = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Path.Combine(directory, "vocab.json")))!;
        _inverse = new string[_vocab.Values.Max() + 1];
        foreach (var (piece, id) in _vocab) _inverse[id] = piece;

        using var cfg = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "config.json")));
        _eos = cfg.RootElement.GetProperty("eos_token_id").GetInt32();
        _pad = cfg.RootElement.GetProperty("pad_token_id").GetInt32();
        _startId = cfg.RootElement.TryGetProperty("decoder_start_token_id", out var ds) ? ds.GetInt32() : _pad;
        _unk = _vocab.TryGetValue("<unk>", out var u) ? u : 1;

        using (var fs = File.OpenRead(Path.Combine(directory, "source.spm")))
            _spm = SentencePieceTokenizer.Create(fs, false, false);

        var opts = new SessionOptions { IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 2, 6), GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        _encoder = new InferenceSession(Path.Combine(directory, "encoder_model_quantized.onnx"), opts);
        _decoder = new InferenceSession(Path.Combine(directory, "decoder_model_quantized.onnx"), opts);

        var ein = _encoder.InputMetadata.Keys.ToList();
        _encIn = ein.First(k => k.Contains("input_ids")); _encMask = ein.First(k => k.Contains("mask"));
        _encOut = _encoder.OutputMetadata.Keys.First();
        var din = _decoder.InputMetadata.Keys.ToList();
        _decIds = din.First(k => k == "input_ids"); _decHidden = din.First(k => k.Contains("hidden"));
        _decMask = din.First(k => k.Contains("mask")); _decOut = _decoder.OutputMetadata.Keys.First();
    }

    /// <summary>Translates one sentence-sized piece of text.</summary>
    public string Translate(string text, CancellationToken ct = default)
    {
        text = text.Trim();
        if (text.Length == 0) return "";
        // Marian models were trained on full sentences: without closing punctuation they often emit nothing at all.
        var addedStop = !EndsWithPunctuation(text);
        var ids = Encode(addedStop ? text + "." : text);
        if (ids.Length <= 1) return "";
        string result;
        lock (_run) result = Decode(ids, ct);
        result = CollapseRepeats(result);
        return addedStop ? result.TrimEnd('.', '\u06D4', ' ') : result;
    }

    private static bool EndsWithPunctuation(string s) => ".!?\u2026:;\u3002\uFF01\uFF1F\u061F\u06D4".Contains(s[^1]);

    /// <summary>Greedy decoding sometimes loops ("save save save"): drop a trailing phrase that merely repeats the one before it.</summary>
    private static string CollapseRepeats(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        bool changed;
        do
        {
            changed = false;
            for (var k = 1; k <= 4 && words.Count >= 2 * k; k++)
            {
                var same = true;
                for (var i = 0; i < k && same; i++) same = words[^(i + 1)].Trim('.', '\u060C') == words[^(i + 1 + k)].Trim('.', '\u060C');
                if (same) { words.RemoveRange(words.Count - k, k); changed = true; break; }
            }
        } while (changed);
        return string.Join(' ', words);
    }

    private long[] Encode(string text)
    {
        var tokens = _spm.EncodeToTokens(text, out _);
        var ids = new List<long>(tokens.Count + 1);
        foreach (var t in tokens) ids.Add(_vocab.TryGetValue(t.Value, out var id) ? id : _unk);
        if (ids.Count > 250) ids.RemoveRange(250, ids.Count - 250);
        ids.Add(_eos);
        return ids.ToArray();
    }

    private string Decode(long[] src, CancellationToken ct)
    {
        var s = src.Length;
        var mask = Enumerable.Repeat(1L, s).ToArray();
        DenseTensor<float> hidden;
        using (var r = _encoder.Run(new[]
               {
                   NamedOnnxValue.CreateFromTensor(_encIn, new DenseTensor<long>(src, new[] { 1, s })),
                   NamedOnnxValue.CreateFromTensor(_encMask, new DenseTensor<long>(mask, new[] { 1, s })),
               }))
            hidden = r.First().AsTensor<float>().ToDenseTensor();

        var output = new List<long> { _startId };
        var maxNew = Math.Min(400, (int)(s * 2.2) + 8); // output is rarely much longer than the input; also stops loops early
        for (var step = 0; step < maxNew; step++)
        {
            ct.ThrowIfCancellationRequested();
            var t = output.Count;
            using var r = _decoder.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor(_decIds, new DenseTensor<long>(output.ToArray(), new[] { 1, t })),
                NamedOnnxValue.CreateFromTensor(_decHidden, hidden),
                NamedOnnxValue.CreateFromTensor(_decMask, new DenseTensor<long>(mask, new[] { 1, s })),
            });
            var logits = r.First(v => v.Name == _decOut).AsTensor<float>();
            var vocab = logits.Dimensions[2];
            var next = ArgMax(logits, t - 1, vocab, output);
            if (next == _eos) break;
            output.Add(next);
        }
        var sb = new StringBuilder();
        foreach (var id in output.Skip(1))
            if (id >= 0 && id < _inverse.Length && _inverse[id] != null) sb.Append(_inverse[id]);
        return sb.ToString().Replace('▁', ' ').Trim();
    }

    private long ArgMax(Tensor<float> logits, int pos, int vocab, List<long> history)
    {
        // Block tokens that would repeat an existing trigram, which stops greedy loops.
        HashSet<long>? banned = null;
        if (history.Count >= 3)
        {
            var a = history[^2]; var b = history[^1];
            for (var i = 0; i + 2 < history.Count; i++)
                if (history[i] == a && history[i + 1] == b) (banned ??= new()).Add(history[i + 2]);
        }
        long best = -1; var bestVal = float.NegativeInfinity;
        for (var v = 0; v < vocab; v++)
        {
            if (v == _pad || (banned != null && banned.Contains(v))) continue;
            var x = logits[0, pos, v];
            if (x > bestVal) { bestVal = x; best = v; }
        }
        return best;
    }

    public void Dispose() { _encoder.Dispose(); _decoder.Dispose(); }
}



