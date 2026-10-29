using TarjumanSari3.Core.Settings;
using TarjumanSari3.Core.Storage;
using TarjumanSari3.Core.Translation;

namespace TarjumanSari3.Tests;

file sealed class FakeEngine(string id, Func<int, string> behave) : ITranslator
{
    public int Calls;
    public string Id => id;
    public string DisplayName => id;
    public bool IsConfigured => true;
    public int MaxChars => 4000;
    public TimeSpan MinInterval => TimeSpan.Zero;
    public Task<TranslationResult> TranslateAsync(string text, string s, string t, CancellationToken ct)
    {
        var n = ++Calls;
        var r = behave(n);
        if (r == "429") throw new TranslationException("limited", rateLimit: true);
        return Task.FromResult(new TranslationResult(r + ":" + text, "en", id));
    }
}

public class PoolTests
{
    public sealed class EchoPublic(string id) : TarjumanSari3.Core.Translation.ITranslator
    {
        public string Id => id; public string DisplayName => id; public bool IsConfigured => true; public int MaxChars => 4000; public TimeSpan MinInterval => TimeSpan.Zero;
        public Task<TarjumanSari3.Core.Translation.TranslationResult> TranslateAsync(string t, string s, string tg, CancellationToken ct) => Task.FromResult(new TarjumanSari3.Core.Translation.TranslationResult(t, "en", id));
    }
    public sealed class FailingPublic(string id) : TarjumanSari3.Core.Translation.ITranslator
    {
        public string Id => id; public string DisplayName => id; public bool IsConfigured => true; public int MaxChars => 4000; public TimeSpan MinInterval => TimeSpan.Zero;
        public Task<TarjumanSari3.Core.Translation.TranslationResult> TranslateAsync(string t, string s, string tg, CancellationToken ct) => throw new TarjumanSari3.Core.Translation.TranslationException("boom");
    }

    [Fact]
    public async Task FallsThroughWhenEngineIsRateLimited()
    {
        var bad = new FakeEngine("a", _ => "429");
        var good = new FakeEngine("b", _ => "ok");
        var pool = new TranslatorPool(new ITranslator[] { bad, good }) { Rotate = false, UseCache = false };
        var r = await pool.TranslateAsync("hello", "auto", "ar");
        Assert.Equal("b", r.EngineId);
        Assert.Equal(1, bad.Calls);
        // engine "a" is now cooling down and must not be hit again
        await pool.TranslateAsync("world", "auto", "ar");
        Assert.Equal(1, bad.Calls);
    }

    [Fact]
    public async Task CooldownExpires()
    {
        var now = DateTime.UtcNow;
        var flaky = new FakeEngine("a", n => n == 1 ? "429" : "ok");
        var other = new FakeEngine("b", _ => "ok");
        var pool = new TranslatorPool(new ITranslator[] { flaky, other }, now: () => now) { Rotate = false, UseCache = false };
        await pool.TranslateAsync("one", "auto", "ar");
        now += TimeSpan.FromMinutes(5);
        var r = await pool.TranslateAsync("two", "auto", "ar");
        Assert.Equal("a", r.EngineId);
    }

    [Fact]
    public async Task CacheAvoidsSecondRequest()
    {
        var e = new FakeEngine("a", _ => "ok");
        using var cache = new TranslationCache(null);
        var pool = new TranslatorPool(new ITranslator[] { e }, cache);
        await pool.TranslateAsync("Hello   world", "auto", "ar");
        var r = await pool.TranslateAsync("Hello world", "auto", "ar");
        Assert.True(r.FromCache);
        Assert.Equal(1, e.Calls);
    }

    private sealed class Echo(string id, bool fallback) : ITranslator
    {
        public string Id => id; public string DisplayName => id; public bool IsConfigured => true;
        public bool FallbackOnly => fallback; public int MaxChars => 4000; public TimeSpan MinInterval => TimeSpan.Zero;
        public Task<TranslationResult> TranslateAsync(string t, string s, string tg, CancellationToken ct) => Task.FromResult(new TranslationResult(t, "en", id));
    }

    [Fact]
    public async Task EchoedTextIsNotCachedAndNextEngineIsTried()
    {
        using var cache = new TranslationCache(null);
        var pool = new TranslatorPool(new ITranslator[] { new Echo("bad", false), new FakeEngine("good", _ => "ok") }, cache) { Rotate = false };
        var r = await pool.TranslateAsync("home", "auto", "ar");
        Assert.Equal("good", r.EngineId);
        Assert.StartsWith("ok:", r.Text);
    }

    [Fact]
    public async Task FallbackOnlyEngineUsedLast()
    {
        var good = new FakeEngine("good", _ => "ok");
        var pool = new TranslatorPool(new ITranslator[] { new Echo("mm", true), good }) { Rotate = true, UseCache = false };
        for (var i = 0; i < 4; i++) Assert.Equal("good", (await pool.TranslateAsync("w" + i, "auto", "ar")).EngineId);
    }

    [Fact]
    public async Task RotationSpreadsLoad()
    {
        var a = new FakeEngine("a", _ => "ok");
        var b = new FakeEngine("b", _ => "ok");
        var pool = new TranslatorPool(new ITranslator[] { a, b }) { UseCache = false };
        for (var i = 0; i < 10; i++) await pool.TranslateAsync("text " + i, "auto", "ar");
        Assert.Equal(5, a.Calls);
        Assert.Equal(5, b.Calls);
    }

    [Fact]
    public async Task AllFailingThrows()
    {
        var pool = new TranslatorPool(new ITranslator[] { new FakeEngine("a", _ => "429") }) { UseCache = false };
        await Assert.ThrowsAsync<TranslationException>(() => pool.TranslateAsync("hi", "auto", "ar"));
    }

    [Fact]
    public void LongTextIsChunked()
    {
        var text = string.Join(" ", Enumerable.Repeat("This is a sentence.", 400));
        var chunks = TranslatorPool.Chunk(text, 1000);
        Assert.All(chunks, c => Assert.True(c.Length <= 1000));
        Assert.True(chunks.Count > 5);
    }

    [Fact]
    public void SettingsRoundTripKeepsHotkeys()
    {
        var dir = Path.Combine(Path.GetTempPath(), "qt-test-" + Guid.NewGuid());
        var store = new SettingsStore(dir);
        store.Load();
        store.Current.Hotkeys[HotkeyAction.TranslateRegion] = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x59);
        store.Save();
        var again = new SettingsStore(dir).Load();
        Assert.Equal(0x59, again.Hotkeys[HotkeyAction.TranslateRegion].VirtualKey);
        Assert.Equal("Ctrl+Shift+Y", again.Hotkeys[HotkeyAction.TranslateRegion].ToString());
        Directory.Delete(dir, true);
    }
}

public class DictionaryTests
{
    private sealed class Stub(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    private const string Sample = """
        [[["يركض","run",null,null,10],[null,null,"yarkud","rʌn"]],
         [["verb",["يركض","يدير","يشغل"],[["يركض",["run","jog"],null,0.5]],"run",2],
          ["noun",["جري","تشغيل"],[["جري",["run"],null,0.3]],"run",1]],
         "en",null,null,null,null,null,null,null,null,null,null,
         [[["He likes to <b>run</b> fast."],["Run the program."]]]]
        """;

    [Fact]
    public async Task ParsesMeaningsPhoneticAndExamples()
    {
        var d = new TarjumanSari3.Core.Translation.DictionaryLookup(new HttpClient(new Stub(Sample)));
        var e = await d.LookupAsync("run", "auto", "ar");
        Assert.NotNull(e);
        Assert.Equal("يركض", e!.Translation);
        Assert.Equal("rʌn", e.Phonetic);
        Assert.Equal(2, e.Groups.Count);
        Assert.Contains("He likes to run fast.", e.Examples);
        var text = TarjumanSari3.Core.Translation.DictionaryLookup.Format(e, true, "أمثلة");
        Assert.Contains("فعل: يركض، يدير، يشغل", text);
        Assert.Contains("اسم:", text);
    }

    [Theory]
    [InlineData("hello", true)]
    [InlineData("hello world", false)]
    [InlineData("12345", false)]
    public void SingleWordDetection(string s, bool expected) =>
        Assert.Equal(expected, TarjumanSari3.Core.Translation.DictionaryLookup.IsSingleWord(s));
}

public class HistoryTests
{
    [Fact]
    public void MigratesOldDatabaseAndStoresEngine()
    {
        var path = Path.Combine(Path.GetTempPath(), "qt-hist-" + Guid.NewGuid() + ".db");
        using (var old = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            old.Open();
            using var cmd = old.CreateCommand();
            cmd.CommandText = "CREATE TABLE history(id INTEGER PRIMARY KEY AUTOINCREMENT, src TEXT, tr TEXT, f TEXT, t TEXT, ts INTEGER, fav INTEGER DEFAULT 0);"
                              + "INSERT INTO history(src,tr,f,t,ts) VALUES('old','قديم','en','ar',1)";
            cmd.ExecuteNonQuery();
        }
        using (var store = new TarjumanSari3.Core.Storage.HistoryStore(path))
        {
            store.Add("home", "المنزل", "en", "ar", "google");
            var items = store.Search("", false);
            Assert.Equal(2, items.Count);
            Assert.Equal("google", items[0].Engine);
            Assert.Equal("", items[1].Engine);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);
    }

    [Fact]
    public async Task CompareAsksEveryEngineAndReportsFailures()
    {
        var pool = new TarjumanSari3.Core.Translation.TranslatorPool(new TarjumanSari3.Core.Translation.ITranslator[]
        {
            new PoolTests.EchoPublic("a"), new PoolTests.FailingPublic("b"),
        });
        var results = await pool.CompareAsync("hello", "en", "ar");
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.EngineId == "a" && r.Text == "hello");
        Assert.Contains(results, r => r.EngineId == "b" && r.Error != null);
    }
}


public class DeepLTests
{
    private sealed class Capture : HttpMessageHandler
    {
        public string Body = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Body = r.Content == null ? "" : Uri.UnescapeDataString(await r.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"translations":[{"detected_source_language":"AR","text":"hello"}]}"""),
            };
        }
    }

    [Theory]
    [InlineData("en", "target_lang=EN-US")]
    [InlineData("pt", "target_lang=PT-BR")]
    [InlineData("zh-CN", "target_lang=ZH")]
    [InlineData("ar", "target_lang=AR")]
    public async Task UsesTheRegionalTargetCodesDeepLRequires(string target, string expected)
    {
        var h = new Capture();
        var deepl = new TarjumanSari3.Core.Translation.DeepLTranslator(new HttpClient(h), () => "abc:fx");
        var r = await deepl.TranslateAsync("مرحبا", "auto", target, CancellationToken.None);
        Assert.Contains(expected, h.Body);
        Assert.Equal("ar", r.DetectedSource);
    }

    [Fact]
    public void SkipsLanguagesDeepLDoesNotOffer()
    {
        var deepl = new TarjumanSari3.Core.Translation.DeepLTranslator(new HttpClient(), () => "k");
        Assert.True(deepl.Supports("auto", "ar", "x"));
        Assert.False(deepl.Supports("auto", "hi", "x"));
        Assert.False(deepl.Supports("fa", "ar", "x"));
    }

    [Fact]
    public async Task PoolPrefersEnginesWithTheUsersOwnKey()
    {
        var free = new PoolTests.EchoPublic("free");
        var keyed = new KeyedEngine();
        var pool = new TarjumanSari3.Core.Translation.TranslatorPool(new TarjumanSari3.Core.Translation.ITranslator[] { free, keyed }) { UseCache = false, Rotate = true };
        for (var i = 0; i < 4; i++) Assert.Equal("keyed", (await pool.TranslateAsync("text " + i, "auto", "ar")).EngineId);
    }

    private sealed class KeyedEngine : TarjumanSari3.Core.Translation.ITranslator
    {
        public string Id => "keyed"; public string DisplayName => "keyed"; public bool IsConfigured => true; public bool Premium => true;
        public int MaxChars => 4000; public TimeSpan MinInterval => TimeSpan.Zero;
        public Task<TarjumanSari3.Core.Translation.TranslationResult> TranslateAsync(string t, string s, string tg, CancellationToken ct) =>
            Task.FromResult(new TarjumanSari3.Core.Translation.TranslationResult("ok:" + t, "en", Id));
    }
}

public class MigrationTests
{
    [Fact]
    public void MovesDataFromTheFormerFolderNameOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), "ts3-mig-" + Guid.NewGuid());
        var legacy = Path.Combine(root, "QuickTranslate", "models");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(root, "QuickTranslate", "settings.json"), "{}");

        var dir = TarjumanSari3.Core.Settings.SettingsStore.ResolveDirectory(root);

        Assert.Equal(Path.Combine(root, "TarjumanSari3"), dir);
        Assert.True(File.Exists(Path.Combine(dir, "settings.json")));
        Assert.True(Directory.Exists(Path.Combine(dir, "models")));
        Assert.False(Directory.Exists(Path.Combine(root, "QuickTranslate")));
        // a second call must not touch anything
        Assert.Equal(dir, TarjumanSari3.Core.Settings.SettingsStore.ResolveDirectory(root));
        Directory.Delete(root, true);
    }
}
