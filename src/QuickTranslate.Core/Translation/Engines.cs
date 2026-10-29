using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace QuickTranslate.Core.Translation;

internal static class Http
{
    public static HttpClient CreateClient()
    {
        var c = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        }) { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) QuickTranslate/1.0");
        return c;
    }

    public static void ThrowIfBad(HttpResponseMessage r, string engine)
    {
        if (r.IsSuccessStatusCode) return;
        var code = (int)r.StatusCode;
        bool limited = code is 429 or 403 or 456 or 503 or 408;
        throw new TranslationException($"{engine}: HTTP {code}", rateLimit: limited, permanent: code is 401 or 400 && !limited);
    }
}

/// <summary>Google Translate web endpoint (gtx client). Unofficial; may be throttled.</summary>
public sealed class GoogleTranslator(HttpClient http) : ITranslator
{
    public string Id => "google";
    public string DisplayName => "Google";
    public bool IsConfigured => true;
    public int MaxChars => 4500;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(150);

    public async Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken ct)
    {
        var url = "https://translate.googleapis.com/translate_a/single?client=gtx&dt=t&sl=" + Uri.EscapeDataString(source)
                  + "&tl=" + Uri.EscapeDataString(target);
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("q", text) })
        };
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        Http.ThrowIfBad(resp, DisplayName);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var sb = new StringBuilder();
        foreach (var seg in doc.RootElement[0].EnumerateArray())
            if (seg.ValueKind == JsonValueKind.Array && seg[0].ValueKind == JsonValueKind.String)
                sb.Append(seg[0].GetString());
        var lang = doc.RootElement.GetArrayLength() > 2 && doc.RootElement[2].ValueKind == JsonValueKind.String
            ? doc.RootElement[2].GetString()! : source;
        if (sb.Length == 0) throw new TranslationException("Google: empty response");
        return new(sb.ToString(), lang, Id);
    }
}

/// <summary>Second Google route (Chrome-extension style client), used as an independent quota pool.</summary>
public sealed class GoogleAltTranslator(HttpClient http) : ITranslator
{
    public string Id => "google-alt";
    public string DisplayName => "Google (alt)";
    public bool IsConfigured => true;
    public int MaxChars => 3000;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(250);

    public async Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken ct)
    {
        var url = "https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl=" + Uri.EscapeDataString(source)
                  + "&tl=" + Uri.EscapeDataString(target) + "&q=" + Uri.EscapeDataString(text);
        using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
        Http.ThrowIfBad(resp, DisplayName);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var root = doc.RootElement;
        // Shape: [["translated","src"]] or ["translated"] depending on variant.
        var first = root[0];
        string? tr; string lang = source;
        if (first.ValueKind == JsonValueKind.Array) { tr = first[0].GetString(); if (first.GetArrayLength() > 1) lang = first[1].GetString() ?? source; }
        else tr = first.GetString();
        if (string.IsNullOrEmpty(tr)) throw new TranslationException("Google alt: empty response");
        return new(tr!, lang, Id);
    }
}

/// <summary>Google's lightweight mobile page; a third independent Google route with its own quota.</summary>
public sealed class GoogleMobileTranslator(HttpClient http) : ITranslator
{
    public string Id => "google-mobile";
    public string DisplayName => "Google (mobile)";
    public bool IsConfigured => true;
    public int MaxChars => 1800;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(400);

    public async Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken ct)
    {
        var url = "https://translate.google.com/m?sl=" + Uri.EscapeDataString(source) + "&tl=" + Uri.EscapeDataString(target)
                  + "&q=" + Uri.EscapeDataString(text);
        using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
        Http.ThrowIfBad(resp, DisplayName);
        var html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var m = System.Text.RegularExpressions.Regex.Match(html, "class=\"result-container\">(.*?)</div>", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (!m.Success) throw new TranslationException("Google mobile: unexpected page", rateLimit: true);
        return new(WebUtility.HtmlDecode(m.Groups[1].Value), source, Id);
    }
}
/// <summary>Official DeepL API — needs the user's own key (free plan: 1M characters/month with a key ending in ":fx").</summary>
public sealed class DeepLTranslator(HttpClient http, Func<string> keyProvider) : ITranslator
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "ar", "bg", "cs", "da", "de", "el", "en", "es", "et", "fi", "fr", "he", "hu", "id", "it", "ja", "ko", "lt", "lv",
        "nb", "no", "nl", "pl", "pt", "ro", "ru", "sk", "sl", "sv", "th", "tr", "uk", "vi", "zh",
    };

    public string Id => "deepl";
    public string DisplayName => "DeepL";
    public bool Premium => true;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(keyProvider());
    public int MaxChars => 5000;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(100);

    private static string Base(string code) => code.Split('-')[0];

    public bool Supports(string source, string target, string text) =>
        Supported.Contains(Base(target)) && (source == "auto" || Supported.Contains(Base(source)));

    /// <summary>DeepL wants regional variants for some targets (EN-US, PT-BR) and no region for others.</summary>
    private static string TargetCode(string target) => Base(target).ToLowerInvariant() switch
    {
        "en" => "EN-US",
        "pt" => "PT-BR",
        "zh" => "ZH",
        "no" => "NB",
        var c => c.ToUpperInvariant(),
    };

    private string Host(string key) => key.EndsWith(":fx") ? "api-free.deepl.com" : "api.deepl.com";

    public async Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken ct)
    {
        var key = keyProvider().Trim();
        var form = new List<KeyValuePair<string, string>> { new("text", text), new("target_lang", TargetCode(target)) };
        if (source != "auto") form.Add(new("source_lang", Base(source).ToUpperInvariant() == "NO" ? "NB" : Base(source).ToUpperInvariant()));
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://{Host(key)}/v2/translate") { Content = new FormUrlEncodedContent(form) };
        req.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", key);
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (resp.StatusCode is HttpStatusCode.Forbidden) throw new TranslationException("DeepL: invalid key", permanent: true);
        if ((int)resp.StatusCode == 456) throw new TranslationException("DeepL: monthly quota used up", rateLimit: true);
        Http.ThrowIfBad(resp, DisplayName);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var t = doc.RootElement.GetProperty("translations")[0];
        return new(t.GetProperty("text").GetString()!, t.GetProperty("detected_source_language").GetString()!.ToLowerInvariant(), Id);
    }

    /// <summary>Characters used and allowed this billing period, from the account (also verifies the key).</summary>
    public async Task<(long Used, long Limit)> UsageAsync(string key, CancellationToken ct = default)
    {
        key = key.Trim();
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://{Host(key)}/v2/usage");
        req.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", key);
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) throw new TranslationException("invalid key", permanent: true);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return (doc.RootElement.GetProperty("character_count").GetInt64(), doc.RootElement.GetProperty("character_limit").GetInt64());
    }
}
/// <summary>Official Azure Translator — user's own key and region (free tier: 2M chars/month).</summary>
public sealed class AzureTranslator(HttpClient http, Func<string> keyProvider, Func<string> regionProvider) : ITranslator
{
    public string Id => "azure";
    public string DisplayName => "Azure Translator";
    public bool Premium => true;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(keyProvider());
    public int MaxChars => 50000;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(50);

    public async Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken ct)
    {
        var url = "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&to=" + Uri.EscapeDataString(target)
                  + (source == "auto" ? "" : "&from=" + Uri.EscapeDataString(source));
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(new[] { new { Text = text } }) };
        req.Headers.Add("Ocp-Apim-Subscription-Key", keyProvider().Trim());
        var region = regionProvider();
        if (!string.IsNullOrWhiteSpace(region)) req.Headers.Add("Ocp-Apim-Subscription-Region", region.Trim());
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.Unauthorized) throw new TranslationException("Azure: invalid key", permanent: true);
        Http.ThrowIfBad(resp, DisplayName);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var item = doc.RootElement[0];
        var lang = item.TryGetProperty("detectedLanguage", out var d) ? d.GetProperty("language").GetString()! : source;
        return new(item.GetProperty("translations")[0].GetProperty("text").GetString()!, lang, Id);
    }
}

/// <summary>MyMemory public API — small limit, last-resort fallback. Needs an explicit source language.</summary>
public sealed class MyMemoryTranslator(HttpClient http) : ITranslator
{
    public string Id => "mymemory";
    public string DisplayName => "MyMemory";
    public bool FallbackOnly => true;
    public bool IsConfigured => true;
    public int MaxChars => 450;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(500);

    public async Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken ct)
    {
        if (source == "auto") source = "en";
        var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair={source}|{target}";
        using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
        Http.ThrowIfBad(resp, DisplayName);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (doc.RootElement.TryGetProperty("responseStatus", out var st) && st.ValueKind == JsonValueKind.Number && st.GetInt32() == 429)
            throw new TranslationException("MyMemory: quota", rateLimit: true);
        var tr = doc.RootElement.GetProperty("responseData").GetProperty("translatedText").GetString();
        if (string.IsNullOrEmpty(tr)) throw new TranslationException("MyMemory: empty");
        return new(tr!, source, Id);
    }
}

/// <summary>LibreTranslate on a URL the user supplies (self-hosted or a public mirror).</summary>
public sealed class LibreTranslator(HttpClient http, Func<string> urlProvider, Func<string> keyProvider) : ITranslator
{
    public string Id => "libre";
    public string DisplayName => "LibreTranslate";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(urlProvider());
    public int MaxChars => 3000;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(300);

    public async Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken ct)
    {
        var url = urlProvider().TrimEnd('/') + "/translate";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { q = text, source, target, format = "text", api_key = keyProvider() })
        };
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        Http.ThrowIfBad(resp, DisplayName);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var lang = doc.RootElement.TryGetProperty("detectedLanguage", out var d) && d.ValueKind == JsonValueKind.Object
            ? d.GetProperty("language").GetString()! : source;
        return new(doc.RootElement.GetProperty("translatedText").GetString()!, lang, Id);
    }
}


