using System.Text;
using TarjumanSari3.Core.Translation;

namespace TarjumanSari3.Core.Offline;

public sealed record LanguagePack(string Id, string TitleAr, string TitleEn, string[] Pairs, int ApproxMb);

/// <summary>Downloads, lists and removes offline model packs (Opus-MT quantized ONNX, one pair = one folder).</summary>
public sealed class ModelStore(string rootDirectory, HttpClient http)
{
    private const string Repo = "https://huggingface.co/Xenova/opus-mt-";

    // X -> en packs that exist for Opus-MT; with the base pack (en <-> ar) any of them can be translated to Arabic.
    private static readonly string[] Sources = { "fr", "de", "es", "ru", "it", "tr", "zh", "ja", "ko", "nl", "pl", "uk", "hi", "id", "sv", "vi", "th", "cs", "hu", "da", "fi" };

    public static IReadOnlyList<LanguagePack> Catalog { get; } = BuildCatalog();

    private static List<LanguagePack> BuildCatalog()
    {
        var list = new List<LanguagePack> { new("base", "الأساس: الإنجليزية ⇄ العربية", "Base: English ⇄ Arabic", new[] { "en-ar", "ar-en" }, 210) };
        foreach (var code in Sources)
        {
            var lang = Languages.Find(code);
            list.Add(new($"{code}-en", $"{lang.Arabic} ← الإنجليزية (للترجمة إلى العربية)", $"{lang.English} → English (to reach Arabic)", new[] { $"{code}-en" }, 105));
        }
        return list;
    }

    public string Root => rootDirectory;
    public string PairDir(string pair) => Path.Combine(rootDirectory, pair);

    public bool IsInstalled(string pair) =>
        Directory.Exists(PairDir(pair)) && MarianModel.RequiredFiles.All(f => File.Exists(Path.Combine(PairDir(pair), f)));

    public bool IsInstalled(LanguagePack p) => p.Pairs.All(IsInstalled);
    public bool AnyInstalled() => Catalog.SelectMany(p => p.Pairs).Distinct().Any(IsInstalled);

    public IEnumerable<string> InstalledPairs() => Catalog.SelectMany(p => p.Pairs).Distinct().Where(IsInstalled);

    public async Task InstallAsync(LanguagePack pack, IProgress<double>? progress, CancellationToken ct)
    {
        var pending = pack.Pairs.Where(p => !IsInstalled(p)).ToList();
        for (var i = 0; i < pending.Count; i++)
        {
            var offset = i / (double)pending.Count; var span = 1.0 / pending.Count;
            await DownloadPairAsync(pending[i], new Progress<double>(v => progress?.Report(offset + v * span)), ct).ConfigureAwait(false);
        }
        progress?.Report(1);
    }

    private async Task DownloadPairAsync(string pair, IProgress<double> progress, CancellationToken ct)
    {
        var dir = PairDir(pair);
        Directory.CreateDirectory(dir);
        var files = new (string Remote, string Local)[]
        {
            ("onnx/encoder_model_quantized.onnx", "encoder_model_quantized.onnx"),
            ("onnx/decoder_model_quantized.onnx", "decoder_model_quantized.onnx"),
            ("source.spm", "source.spm"), ("vocab.json", "vocab.json"), ("config.json", "config.json"),
        };
        for (var i = 0; i < files.Length; i++)
        {
            var target = Path.Combine(dir, files[i].Local);
            if (File.Exists(target)) { progress.Report((i + 1) / (double)files.Length); continue; }
            var tmp = target + ".partial";
            using var resp = await http.GetAsync($"{Repo}{pair}/resolve/main/{files[i].Remote}", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? 0;
            await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = File.Create(tmp))
            {
                var buf = new byte[81920]; long done = 0; int n;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    done += n;
                    if (total > 0) progress.Report((i + done / (double)total) / files.Length);
                }
            }
            File.Move(tmp, target, true);
        }
    }

    public void Remove(LanguagePack pack)
    {
        foreach (var p in pack.Pairs) try { if (Directory.Exists(PairDir(p))) Directory.Delete(PairDir(p), true); } catch { }
    }
}

/// <summary>Guesses the source language of a text from its script (and stop-words for Latin text).</summary>
public static class LanguageGuess
{
    private static readonly Dictionary<string, string[]> Stop = new()
    {
        ["en"] = new[] { "the", "and", "is", "are", "you", "to", "of", "that", "this", "with", "for", "it", "in" },
        ["fr"] = new[] { "le", "la", "les", "des", "et", "est", "une", "vous", "que", "pour", "dans", "pas", "je" },
        ["de"] = new[] { "der", "die", "das", "und", "ist", "nicht", "ich", "sie", "mit", "ein", "eine", "zu", "auf" },
        ["es"] = new[] { "el", "los", "las", "y", "es", "que", "una", "por", "con", "para", "no", "se", "del" },
        ["it"] = new[] { "il", "gli", "che", "di", "non", "una", "per", "con", "sono", "è", "della", "lo" },
        ["nl"] = new[] { "de", "het", "een", "en", "van", "ik", "je", "niet", "dat", "is", "op", "te" },
        ["tr"] = new[] { "ve", "bir", "bu", "için", "ile", "ne", "çok", "da", "de", "mi", "ben" },
        ["pl"] = new[] { "nie", "się", "i", "w", "na", "to", "jest", "z", "że", "do", "jak" },
        ["sv"] = new[] { "och", "att", "det", "som", "en", "på", "är", "av", "för", "inte" },
        ["da"] = new[] { "og", "at", "det", "som", "en", "på", "er", "af", "for", "ikke" },
        ["id"] = new[] { "yang", "dan", "di", "ini", "itu", "dengan", "untuk", "tidak", "dari", "saya" },
        ["vi"] = new[] { "và", "của", "là", "có", "không", "một", "những", "trong", "được" },
        ["cs"] = new[] { "a", "je", "se", "na", "to", "že", "v", "s", "do", "nebo" },
        ["hu"] = new[] { "a", "az", "és", "hogy", "nem", "egy", "is", "van", "meg" },
        ["fi"] = new[] { "ja", "on", "ei", "että", "se", "oli", "mutta", "kun", "ovat" },
    };

    public static string Guess(string text, IReadOnlyCollection<string> candidates)
    {
        int ar = 0, cyr = 0, kana = 0, hangul = 0, han = 0, dev = 0, thai = 0, latin = 0;
        foreach (var ch in text)
        {
            if (ch is >= '؀' and <= 'ۿ') ar++;
            else if (ch is >= 'Ѐ' and <= 'ӿ') cyr++;
            else if (ch is >= '぀' and <= 'ヿ') kana++;
            else if (ch is >= '가' and <= '힯') hangul++;
            else if (ch is >= '一' and <= '鿿') han++;
            else if (ch is >= 'ऀ' and <= 'ॿ') dev++;
            else if (ch is >= '฀' and <= '๿') thai++;
            else if (char.IsLetter(ch)) latin++;
        }
        var max = new[] { ar, cyr, kana, hangul, han, dev, thai, latin }.Max();
        if (max == 0) return "en";
        if (max == ar) return "ar";
        if (max == cyr) return candidates.Contains("uk") && !candidates.Contains("ru") ? "uk" : "ru";
        if (kana > 0) return "ja";
        if (max == hangul) return "ko";
        if (max == han) return "zh";
        if (max == dev) return "hi";
        if (max == thai) return "th";

        var words = text.ToLowerInvariant().Split(new[] { ' ', '\n', '\t', ',', '.', '!', '?', ';', ':', '"', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
        var best = "en"; var bestScore = -1;
        foreach (var (lang, stops) in Stop)
        {
            if (lang != "en" && !candidates.Contains(lang)) continue;
            var score = words.Count(w => stops.Contains(w));
            if (score > bestScore) { best = lang; bestScore = score; }
        }
        return best;
    }
}

/// <summary>Offline engine: no internet, no quota. Chains through English when no direct model exists.</summary>
public sealed class OfflineTranslator(ModelStore store) : ITranslator, IDisposable
{
    private readonly Dictionary<string, MarianModel> _loaded = new();
    private readonly List<string> _lru = new();
    private readonly object _lock = new();

    public bool PreferOffline { get; set; }

    public string Id => "offline";
    public string DisplayName => "Offline";
    public bool FallbackOnly => !PreferOffline;
    public bool IsConfigured => store.AnyInstalled();
    public int MaxChars => 100000;
    public TimeSpan MinInterval => TimeSpan.Zero;

    private static string Norm(string code) => code.ToLowerInvariant() switch
    {
        "zh-cn" or "zh-tw" or "zh" => "zh", var c => c.Split('-')[0],
    };

    private List<string>? Plan(string source, string target)
    {
        var s = Norm(source); var t = Norm(target);
        if (s == t) return null;
        if (store.IsInstalled($"{s}-{t}")) return new() { $"{s}-{t}" };
        if (s != "en" && t != "en" && store.IsInstalled($"{s}-en") && store.IsInstalled($"en-{t}")) return new() { $"{s}-en", $"en-{t}" };
        return null;
    }

    public bool Supports(string source, string target, string text)
    {
        var src = source == "auto" ? LanguageGuess.Guess(text, store.InstalledPairs().Select(p => p.Split('-')[0]).ToHashSet()) : source;
        return Plan(src, target) != null;
    }

    public Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken ct) => Task.Run(() =>
    {
        var src = source == "auto" ? LanguageGuess.Guess(text, store.InstalledPairs().Select(p => p.Split('-')[0]).ToHashSet()) : Norm(source);
        var plan = Plan(src, target) ?? throw new TranslationException("Offline: no installed model for this language pair", permanent: false);
        var output = new StringBuilder();
        foreach (var sentence in SplitSentences(text))
        {
            ct.ThrowIfCancellationRequested();
            var current = sentence;
            foreach (var pair in plan) current = Get(pair).Translate(current, ct);
            if (output.Length > 0) output.Append(' ');
            output.Append(current);
        }
        if (string.IsNullOrWhiteSpace(output.ToString())) throw new TranslationException("Offline: empty result");
        return new TranslationResult(output.ToString(), src, Id);
    }, ct);

    private MarianModel Get(string pair)
    {
        lock (_lock)
        {
            if (_loaded.TryGetValue(pair, out var m)) { _lru.Remove(pair); _lru.Add(pair); return m; }
            m = new MarianModel(store.PairDir(pair));
            _loaded[pair] = m; _lru.Add(pair);
            while (_lru.Count > 3) { var old = _lru[0]; _lru.RemoveAt(0); _loaded[old].Dispose(); _loaded.Remove(old); }
            return m;
        }
    }

    private static IEnumerable<string> SplitSentences(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.Replace("\r", ""))
        {
            sb.Append(ch);
            var end = ch is '.' or '!' or '?' or '\n' or '。' or '！' or '？' or '؟';
            if ((end && sb.Length > 3) || sb.Length > 400) { var s = sb.ToString().Trim(); if (s.Length > 0) yield return s; sb.Clear(); }
        }
        var rest = sb.ToString().Trim();
        if (rest.Length > 0) yield return rest;
    }

    public void Dispose() { foreach (var m in _loaded.Values) m.Dispose(); _loaded.Clear(); }
}

