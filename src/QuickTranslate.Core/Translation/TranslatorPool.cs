using QuickTranslate.Core.Storage;

namespace QuickTranslate.Core.Translation;

public sealed record EngineComparison(string EngineId, string Name, string? Text, string? Error, long Milliseconds);

public sealed record EngineStatus(string Id, string Name, bool Configured, bool Enabled, bool CoolingDown, TimeSpan Remaining, int Requests, int Failures);

/// <summary>
/// Spreads load across every enabled engine, rests engines that throttle us (circuit breaker with
/// exponential back-off) and transparently falls through to the next one, so a rate limit on one
/// service is never visible to the user. Identical text is served from the cache.
/// </summary>
public sealed class TranslatorPool
{
    private sealed class EngineState(ITranslator engine)
    {
        public ITranslator Engine { get; } = engine;
        public int ConsecutiveFailures;
        public DateTime BlockedUntil;
        public DateTime LastRequest;
        public int Requests, Failures;
        public readonly SemaphoreSlim Gate = new(1, 1);
    }

    private readonly object _lock = new();
    private readonly List<EngineState> _engines;
    private readonly TranslationCache? _cache;
    private readonly Func<DateTime> _now;
    private int _rr;

    /// <summary>Diagnostics (level: info/warn/error, message) for the in-app log.</summary>
    public event Action<string, string>? Trace;
    /// <summary>An engine was put to rest because it refused us (id, display name, rate-limit?, seconds).</summary>
    public event Action<string, string, bool, double>? EngineRested;

    public IReadOnlyList<string> Order { get; set; } = Array.Empty<string>();
    public ISet<string> Disabled { get; set; } = new HashSet<string>();
    public bool Rotate { get; set; } = true;
    public bool UseCache { get; set; } = true;
    public TimeSpan BaseCooldown { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan MaxCooldown { get; set; } = TimeSpan.FromMinutes(15);

    public TranslatorPool(IEnumerable<ITranslator> engines, TranslationCache? cache = null, Func<DateTime>? now = null)
    {
        _engines = engines.Select(e => new EngineState(e)).ToList();
        _cache = cache;
        _now = now ?? (() => DateTime.UtcNow);
        Order = _engines.Select(e => e.Engine.Id).ToList();
    }

    public async Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken ct = default)
    {
        text = text.Trim();
        if (text.Length == 0 || TextNormalizer.IsTrivial(text)) return new(text, source, "none");

        if (UseCache && _cache?.Get(text, source, target) is { } hit && !Untranslated(text, hit.Text, source, target))
            return new(hit.Text, hit.DetectedSource, hit.EngineId, FromCache: true);

        var chunks = Chunk(text, MinMaxChars());
        var parts = new List<string>();
        string detected = source, engineId = "";
        foreach (var chunk in chunks)
        {
            if (UseCache && chunks.Count > 1 && _cache?.Get(chunk, source, target) is { } ch)
            { parts.Add(ch.Text); detected = ch.DetectedSource; continue; }
            var r = await TranslateChunkAsync(chunk, source, target, ct).ConfigureAwait(false);
            parts.Add(r.Text); detected = r.DetectedSource; engineId = r.EngineId;
            if (UseCache && chunks.Count > 1) _cache?.Put(chunk, source, target, new(r.Text, r.DetectedSource, r.EngineId));
        }
        var final = new TranslationResult(string.Join("\n", parts), detected, engineId);
        if (UseCache && !Untranslated(text, final.Text, source, target))
            _cache?.Put(text, source, target, new(final.Text, final.DetectedSource, final.EngineId));
        return final;
    }

    /// <summary>An engine that echoes the input back has not translated anything (unless source == target).</summary>
    private static bool Untranslated(string input, string output, string source, string target) =>
        !string.Equals(source, target, StringComparison.OrdinalIgnoreCase)
        && string.Equals(TextNormalizer.Normalize(input), TextNormalizer.Normalize(output), StringComparison.OrdinalIgnoreCase);

    private async Task<TranslationResult> TranslateChunkAsync(string chunk, string source, string target, CancellationToken ct)
    {
        var tried = new HashSet<string>();
        TranslationException? last = null;
        TranslationResult? echoed = null;
        for (var attempt = 0; attempt < _engines.Count * 2; attempt++)
        {
            var st = PickNext(tried, chunk, source, target);
            if (st == null) break;
            tried.Add(st.Engine.Id);
            try
            {
                await st.Gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var wait = st.LastRequest + st.Engine.MinInterval - _now();
                    if (wait > TimeSpan.Zero) await Task.Delay(wait, ct).ConfigureAwait(false);
                    st.LastRequest = _now();
                    st.Requests++;
                    var res = await st.Engine.TranslateAsync(chunk, source, target, ct).ConfigureAwait(false);
                    st.ConsecutiveFailures = 0;
                    if (Untranslated(chunk, res.Text, source, target) && !res.DetectedSource.StartsWith(target, StringComparison.OrdinalIgnoreCase))
                    { echoed = res; Trace?.Invoke("info", $"{st.Engine.DisplayName} returned the text unchanged - trying another engine"); continue; }
                    return res;
                }
                finally { st.Gate.Release(); }
            }
            catch (OperationCanceledException) { throw; }
            catch (TranslationException ex) { last = ex; RecordFailure(st, ex.IsRateLimit, ex.IsPermanent); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                           or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            { last = new TranslationException(ex.Message, inner: ex); RecordFailure(st, false, false); }
        }
        if (echoed != null) return echoed;
        throw new TranslationException("No translation engine available. " + (last?.Message ?? "All engines are disabled or cooling down."), inner: last);
    }

    private void RecordFailure(EngineState st, bool rateLimit, bool permanent)
    {
        lock (_lock)
        {
            st.Failures++;
            st.ConsecutiveFailures++;
            var factor = Math.Pow(2, Math.Min(st.ConsecutiveFailures - 1, 8)) * (rateLimit ? 2 : 1);
            var cool = permanent ? MaxCooldown : TimeSpan.FromSeconds(Math.Min(BaseCooldown.TotalSeconds * factor, MaxCooldown.TotalSeconds));
            st.BlockedUntil = _now() + cool;
            if (rateLimit || permanent) EngineRested?.Invoke(st.Engine.Id, st.Engine.DisplayName, rateLimit, cool.TotalSeconds);
            Trace?.Invoke("warn", $"{st.Engine.DisplayName} failed ({(rateLimit ? "rate limit" : permanent ? "permanent" : "error")}) - resting {cool.TotalSeconds:0}s");
        }
    }

    private EngineState? PickNext(HashSet<string> tried, string chunk, string source, string target)
    {
        lock (_lock)
        {
            var now = _now();
            var ordered = Order.Select(id => _engines.FirstOrDefault(e => e.Engine.Id == id)).Where(e => e != null)
                .Concat(_engines.Where(e => !Order.Contains(e.Engine.Id)))!
                .Cast<EngineState>()
                .Where(e => !Disabled.Contains(e.Engine.Id) && e.Engine.IsConfigured && !tried.Contains(e.Engine.Id)
                            && e.BlockedUntil <= now && e.Engine.MaxChars >= chunk.Length && e.Engine.Supports(source, target, chunk))
                .ToList();
            if (ordered.Count == 0) return null;
            // Tiers: engines with the user's own key first, then the free web engines, then low-quality fallbacks.
            var tiers = ordered.GroupBy(e => e.Engine.FallbackOnly ? 2 : e.Engine.Premium ? 0 : 1).OrderBy(g => g.Key).First();
            ordered = tiers.ToList();
            // Rotation keeps each free quota well below its throttling threshold.
            return Rotate ? ordered[_rr++ % ordered.Count] : ordered[0];
        }
    }

    private int MinMaxChars()
    {
        lock (_lock)
        {
            var usable = _engines.Where(e => e.Engine.IsConfigured && !Disabled.Contains(e.Engine.Id) && e.Engine.MaxChars >= 1000)
                .Select(e => e.Engine.MaxChars).ToList();
            return usable.Count == 0 ? 450 : Math.Min(usable.Min(), 3000);
        }
    }

    /// <summary>Splits on paragraph/sentence boundaries so each piece fits one request.</summary>
    public static List<string> Chunk(string text, int max)
    {
        if (text.Length <= max) return new() { text };
        var result = new List<string>();
        var cur = new System.Text.StringBuilder();
        foreach (var piece in SplitSentences(text))
        {
            if (cur.Length + piece.Length > max && cur.Length > 0) { result.Add(cur.ToString().Trim()); cur.Clear(); }
            var p = piece;
            while (p.Length > max) { result.Add(p[..max]); p = p[max..]; }
            cur.Append(p);
        }
        if (cur.Length > 0) result.Add(cur.ToString().Trim());
        return result;
    }

    private static IEnumerable<string> SplitSentences(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '.' or '!' or '?' or '\n' or '。' or '؟')
            {
                yield return text[start..(i + 1)];
                start = i + 1;
            }
        }
        if (start < text.Length) yield return text[start..];
    }

    /// <summary>Asks every usable engine the same question at once (no fallback, no cooldown bookkeeping).</summary>
    public async Task<IReadOnlyList<EngineComparison>> CompareAsync(string text, string source, string target, CancellationToken ct = default)
    {
        List<ITranslator> engines;
        lock (_lock)
            engines = _engines.Where(e => e.Engine.IsConfigured && !Disabled.Contains(e.Engine.Id) && e.Engine.Supports(source, target, text))
                .Select(e => e.Engine).ToList();
        var tasks = engines.Select(async e =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (text.Length > e.MaxChars) return new EngineComparison(e.Id, e.DisplayName, null, "text too long for this engine", sw.ElapsedMilliseconds);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(30));
                var r = await e.TranslateAsync(text, source, target, cts.Token).ConfigureAwait(false);
                return new EngineComparison(e.Id, e.DisplayName, r.Text, null, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { return new EngineComparison(e.Id, e.DisplayName, null, ex.Message, sw.ElapsedMilliseconds); }
        }).ToList();
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public IReadOnlyList<EngineStatus> GetStatus()
    {
        lock (_lock)
        {
            var now = _now();
            return _engines.Select(e => new EngineStatus(e.Engine.Id, e.Engine.DisplayName, e.Engine.IsConfigured,
                !Disabled.Contains(e.Engine.Id), e.BlockedUntil > now, e.BlockedUntil > now ? e.BlockedUntil - now : TimeSpan.Zero,
                e.Requests, e.Failures)).ToList();
        }
    }
}




