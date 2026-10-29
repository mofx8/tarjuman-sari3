using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TarjumanSari3.Core.Translation;

public sealed record DictGroup(string PartOfSpeech, IReadOnlyList<string> Words);
public sealed record DictEntry(string Word, string Translation, string? Phonetic, IReadOnlyList<DictGroup> Groups, IReadOnlyList<string> Examples);

/// <summary>Single-word lookup: alternative meanings by part of speech, pronunciation and example sentences.</summary>
public sealed partial class DictionaryLookup(HttpClient http)
{
    [GeneratedRegex("<.*?>")] private static partial Regex Tags();

    /// <summary>One word, no spaces, with at least one letter.</summary>
    public static bool IsSingleWord(string text)
    {
        var t = text.Trim();
        return t.Length is > 0 and <= 40 && !t.Any(char.IsWhiteSpace) && t.Any(char.IsLetter);
    }

    public async Task<DictEntry?> LookupAsync(string word, string source, string target, CancellationToken ct = default)
    {
        var url = "https://translate.googleapis.com/translate_a/single?client=gtx&dt=t&dt=bd&dt=rm&dt=ex&sl=" + Uri.EscapeDataString(source)
                  + "&tl=" + Uri.EscapeDataString(target) + "&q=" + Uri.EscapeDataString(word.Trim());
        using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var root = doc.RootElement;

        string translation = ""; string? phonetic = null;
        if (At(root, 0) is { ValueKind: JsonValueKind.Array } segs)
            foreach (var seg in segs.EnumerateArray())
            {
                if (At(seg, 0) is { ValueKind: JsonValueKind.String } t) translation += t.GetString();
                else
                {
                    // [null, null, target transliteration, source transliteration]
                    phonetic = At(seg, 3) is { ValueKind: JsonValueKind.String } sp ? sp.GetString() : phonetic;
                    phonetic ??= At(seg, 2) is { ValueKind: JsonValueKind.String } tp ? tp.GetString() : null;
                }
            }
        if (translation.Length == 0) return null;

        var groups = new List<DictGroup>();
        if (At(root, 1) is { ValueKind: JsonValueKind.Array } bd)
            foreach (var g in bd.EnumerateArray())
            {
                var pos = At(g, 0)?.GetString() ?? "";
                var words = At(g, 1) is { ValueKind: JsonValueKind.Array } ws
                    ? ws.EnumerateArray().Where(w => w.ValueKind == JsonValueKind.String).Select(w => w.GetString()!).Take(8).ToList() : new();
                if (words.Count > 0) groups.Add(new(pos, words));
            }

        var examples = new List<string>();
        if (At(root, 13) is { ValueKind: JsonValueKind.Array } ex && At(ex, 0) is { ValueKind: JsonValueKind.Array } list)
            foreach (var e in list.EnumerateArray())
                if (At(e, 0) is { ValueKind: JsonValueKind.String } s)
                    examples.Add(Tags().Replace(s.GetString()!, ""));
        return new(word.Trim(), translation, string.IsNullOrWhiteSpace(phonetic) ? null : phonetic, groups, examples.Take(3).ToList());
    }

    private static JsonElement? At(JsonElement el, int i) =>
        el.ValueKind == JsonValueKind.Array && el.GetArrayLength() > i ? el[i] : null;

    private static readonly Dictionary<string, string> PosAr = new(StringComparer.OrdinalIgnoreCase)
    {
        ["noun"] = "اسم", ["verb"] = "فعل", ["adjective"] = "صفة", ["adverb"] = "ظرف", ["preposition"] = "حرف جر",
        ["pronoun"] = "ضمير", ["conjunction"] = "حرف عطف", ["interjection"] = "تعجب", ["article"] = "أداة", ["abbreviation"] = "اختصار",
        ["determiner"] = "محدِّد", ["particle"] = "أداة",
    };

    /// <summary>Plain multi-line text for popups and the output box.</summary>
    public static string Format(DictEntry e, bool arabic, string examplesTitle)
    {
        var sb = new StringBuilder();
        sb.Append(e.Translation);
        if (e.Phonetic != null) sb.Append("   /").Append(e.Phonetic).Append('/');
        foreach (var g in e.Groups)
        {
            var pos = arabic && PosAr.TryGetValue(g.PartOfSpeech, out var ar) ? ar : g.PartOfSpeech;
            sb.Append('\n').Append(pos.Length > 0 ? pos + ": " : "").Append(string.Join(arabic ? "، " : ", ", g.Words));
        }
        if (e.Examples.Count > 0)
        {
            sb.Append("\n\n").Append(examplesTitle).Append(':');
            foreach (var ex in e.Examples) sb.Append("\n• ").Append(ex);
        }
        return sb.ToString();
    }
}
