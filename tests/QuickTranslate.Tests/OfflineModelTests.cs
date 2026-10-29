using QuickTranslate.Core.Offline;

namespace QuickTranslate.Tests;

public class OfflineModelTests
{
    /// <summary>Runs only when QT_TEST_MODEL points at an extracted en-ar pack (large download).</summary>
    [Fact]
    public void EnglishToArabicProducesArabic()
    {
        var dir = Environment.GetEnvironmentVariable("QT_TEST_MODEL");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        using var m = new MarianModel(dir);
        var result = m.Translate("Hello, how are you today?");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "qt-offline-out.txt"), result + "\n" + m.Translate("The weather is very nice today and I want to go to the market."));
        Assert.Contains(result, c => c is >= '؀' and <= 'ۿ');
    }
}


public class OfflinePoolTests
{
    [Fact]
    public async Task PoolFallsBackToOfflineAndGuessesSource()
    {
        var root = Environment.GetEnvironmentVariable("QT_TEST_MODELS_ROOT");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(Path.Combine(root, "en-ar"))) return;
        var store = new QuickTranslate.Core.Offline.ModelStore(root, new HttpClient());
        using var offline = new QuickTranslate.Core.Offline.OfflineTranslator(store);
        var pool = new QuickTranslate.Core.Translation.TranslatorPool(new QuickTranslate.Core.Translation.ITranslator[] { offline }) { UseCache = false };
        var r = await pool.TranslateAsync("Hello, how are you today? I hope everything is fine.", "auto", "ar");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "qt-offline-pool.txt"), r.EngineId + "|" + r.DetectedSource + "|" + r.Text);
        Assert.Equal("offline", r.EngineId);
        Assert.Equal("en", r.DetectedSource);
    }
}

public class OfflineShortTextTests
{
    [Fact]
    public void ShortUiStringsWithoutPunctuationAreTranslated()
    {
        var dir = Environment.GetEnvironmentVariable("QT_TEST_MODEL");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        using var m = new QuickTranslate.Core.Offline.MarianModel(dir);
        foreach (var s in new[] { "Translate a screen area", "Save changes", "Show/hide window", "Settings" })
        {
            var r = m.Translate(s);
            Assert.False(string.IsNullOrWhiteSpace(r), $"empty for '{s}'");
        }
    }
}
