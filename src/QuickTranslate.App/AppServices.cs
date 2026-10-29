using QuickTranslate.Core.Offline;
using QuickTranslate.Core.Settings;
using QuickTranslate.Core.Storage;
using QuickTranslate.Core.Translation;
using QuickTranslate.Platform;

namespace QuickTranslate.App;

/// <summary>Application-wide singletons (settings, translation pool, history, OCR).</summary>
public sealed class AppServices : IDisposable
{
    public SettingsStore Store { get; }
    public AppSettings Settings => Store.Current;
    public TranslationCache Cache { get; }
    public HistoryStore History { get; }
    public TranslatorPool Pool { get; }
    public WindowsOcr Ocr { get; } = new();
    public DictionaryLookup Dictionary { get; }
    public DeepLTranslator DeepL { get; }

    /// <summary>The profile of the program that was in the foreground when the last shortcut was pressed.</summary>
    public AppProfile? ActiveProfile { get; private set; }
    /// <summary>Settings with the active program profile applied (language, font size, colours).</summary>
    public AppSettings Effective => Settings.WithProfile(ActiveProfile);

    public void SetActiveProfile(string? process) =>
        ActiveProfile = string.IsNullOrEmpty(process) ? null
            : Settings.Profiles.FirstOrDefault(p => string.Equals(p.Process, process, StringComparison.OrdinalIgnoreCase));
    public ModelStore Models { get; }
    public OfflineTranslator Offline { get; }

    public AppServices()
    {
        Store = new SettingsStore();
        Store.Load();
        Cache = new TranslationCache(Path.Combine(Store.DataDirectory, "cache.db"));
        History = new HistoryStore(Path.Combine(Store.DataDirectory, "history.db"));

        var http = HttpFactory.Create();
        Dictionary = new DictionaryLookup(http);
        Models = new ModelStore(Path.Combine(Store.DataDirectory, "models"), new HttpClient { Timeout = TimeSpan.FromMinutes(30) });
        Offline = new OfflineTranslator(Models);
        DeepL = new DeepLTranslator(http, () => GetKey("deepl"));
        Pool = new TranslatorPool(new ITranslator[]
        {
            new GoogleTranslator(http),
            new GoogleMobileTranslator(http),
            new GoogleAltTranslator(http),
            DeepL,
            new AzureTranslator(http, () => GetKey("azure"), () => Settings.ApiKeys.GetValueOrDefault("azure-region", "")),
            Offline,
            new MyMemoryTranslator(http),
            new LibreTranslator(http, () => Settings.LibreTranslateUrl, () => GetKey("libre")),
        }, Cache);
        Pool.Trace += (level, message) => Log.FromText(level, "Engines", message);
        Pool.EngineRested += OnEngineRested;
        ApplySettings();
        Store.Changed += _ => ApplySettings();
    }

    /// <summary>Human-readable engine name for an engine id (history, log).</summary>
    public string EngineName(string id) => id switch
    {
        "dictionary" => Loc.T("القاموس"),
        "none" or "" => "-",
        _ => Pool.GetStatus().FirstOrDefault(s => s.Id == id)?.Name ?? id,
    };

    private readonly Dictionary<string, DateTime> _lastNotice = new();

    /// <summary>Tells the user (once in a while) that an engine refused us and another one is being used.</summary>
    private void OnEngineRested(string id, string name, bool rateLimit, double seconds)
    {
        if (!Settings.NotifyEngineFallback) return;
        lock (_lastNotice)
        {
            if (_lastNotice.TryGetValue(id, out var last) && DateTime.UtcNow - last < TimeSpan.FromMinutes(10)) return;
            _lastNotice[id] = DateTime.UtcNow;
        }
        var reason = rateLimit ? Loc.T("وصل حدّ الاستخدام") : Loc.T("غير صالح أو غير متاح");
        var text = $"{name}: {reason}. " + string.Format(Loc.T("يُستخدم محرك آخر تلقائياً ({0} ث راحة)."), (int)seconds);
        Application.Current?.Dispatcher.BeginInvoke(() => App.Current.NotifyInfo(text));
    }

    /// <summary>API keys are stored DPAPI-encrypted; this returns the plain value for use.</summary>
    public string GetKey(string name) => Secrets.Unprotect(Settings.ApiKeys.GetValueOrDefault(name, ""));

    public void SetKey(string name, string plain)
    {
        if (string.IsNullOrWhiteSpace(plain)) Settings.ApiKeys.Remove(name);
        else Settings.ApiKeys[name] = Secrets.Protect(plain.Trim());
    }

    private void ApplySettings()
    {
        Offline.PreferOffline = Settings.PreferOffline;
        Pool.Order = Settings.PreferOffline ? new[] { "offline" }.Concat(Settings.EngineOrder).ToList() : Settings.EngineOrder;
        Pool.Disabled = new HashSet<string>(Settings.DisabledEngines);
        Pool.Rotate = Settings.RotateEngines && !Settings.PreferOffline;
        Pool.UseCache = Settings.UseCache;
    }

    /// <summary>Applies the user's glossary / do-not-translate list around a translation.</summary>
    public async Task<TranslationResult> TranslateAsync(string text, string? source = null, string? target = null, CancellationToken ct = default, bool saveHistory = true, string kind = "text")
    {
        var effective = Effective;
        source ??= effective.SourceLanguage;
        target ??= effective.TargetLanguage;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await Pool.TranslateAsync(text, source, target, ct);
        if (result.EngineId != "none")
            Log.Info("Translate", $"{EngineName(result.EngineId)}{(result.FromCache ? " (cache)" : "")} · {text.Length} chars · {sw.ElapsedMilliseconds} ms · {result.DetectedSource}→{target}");
        var output = result.Text;
        foreach (var (from, to) in Settings.Glossary)
            if (!string.IsNullOrEmpty(from)) output = output.Replace(from, to, StringComparison.OrdinalIgnoreCase);
        if (saveHistory && Settings.SaveHistory && result.EngineId != "none")
            History.Add(text, output, result.DetectedSource, target, result.EngineId, kind);
        return result with { Text = output };
    }

    /// <summary>Dictionary text for a single word (meanings, pronunciation, examples) plus the plain main translation.</summary>
    public sealed record DictResult(string Formatted, string Translation);

    public async Task<DictResult?> LookupWordAsync(string word, CancellationToken ct = default, bool saveHistory = false, string kind = "dictionary")
    {
        try
        {
            var effective = Effective;
            var target = effective.TargetLanguage;
            var entry = await Dictionary.LookupAsync(word, effective.SourceLanguage, target, ct);
            if (entry == null) return null;
            if (saveHistory && Settings.SaveHistory) History.Add(word, entry.Translation, Settings.SourceLanguage, target, "dictionary", kind);
            return new(DictionaryLookup.Format(entry, target == "ar", Loc.T("أمثلة")), entry.Translation);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Log.Error("Dictionary", ex); return null; }
    }

    /// <summary>Label for what kind of action produced a history entry.</summary>
    public static string KindName(string kind) => kind switch
    {
        "screen" => Loc.T("شاشة"),
        "selection" => Loc.T("نص محدد"),
        "clipboard" => Loc.T("حافظة"),
        "image" => Loc.T("صورة"),
        "live" => Loc.T("مباشر"),
        "replace" => Loc.T("استبدال"),
        "dictionary" => Loc.T("قاموس"),
        _ => Loc.T("نص"),
    };
    public void Dispose()
    {
        Offline.Dispose();
        Cache.Dispose();
        History.Dispose();
    }
}

internal static class HttpFactory
{
    public static HttpClient Create()
    {
        var c = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        }) { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) QuickTranslate/1.0");
        return c;
    }
}






