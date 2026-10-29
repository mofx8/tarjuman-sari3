namespace TarjumanSari3.Core.Translation;

public sealed record TranslationResult(string Text, string DetectedSource, string EngineId, bool FromCache = false);

public class TranslationException : Exception
{
    /// <summary>True when the engine is throttling/blocking us (429, captcha, quota) and should rest.</summary>
    public bool IsRateLimit { get; }
    /// <summary>True when retrying this engine is pointless (missing key, unsupported pair).</summary>
    public bool IsPermanent { get; }

    public TranslationException(string message, bool rateLimit = false, bool permanent = false, Exception? inner = null)
        : base(message, inner) { IsRateLimit = rateLimit; IsPermanent = permanent; }
}

public interface ITranslator
{
    string Id { get; }
    string DisplayName { get; }
    /// <summary>Low-quality or tiny-quota engines: only used when no other engine can answer.</summary>
    bool FallbackOnly => false;
    /// <summary>Engines the user pays for or registered with a key (DeepL, Azure): preferred over the free web engines.</summary>
    bool Premium => false;
    /// <summary>False when this engine cannot handle the pair (e.g. no offline model installed).</summary>
    bool Supports(string source, string target, string text) => true;
    /// <summary>False when the engine needs a key/URL the user has not provided.</summary>
    bool IsConfigured { get; }
    /// <summary>Approximate max characters per request.</summary>
    int MaxChars { get; }
    /// <summary>Minimum delay between two requests to this engine.</summary>
    TimeSpan MinInterval { get; }

    Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken ct);
}


