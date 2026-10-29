using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace TarjumanSari3.Platform;

public sealed record OcrBox(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Contains(double px, double py) => px >= X && px <= Right && py >= Y && py <= Bottom;
    public double DistanceTo(double px, double py)
    {
        var dx = Math.Max(Math.Max(X - px, 0), px - Right);
        var dy = Math.Max(Math.Max(Y - py, 0), py - Bottom);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

public sealed record OcrLine(OcrBox Box, string Text);
/// <summary>A paragraph: neighbouring lines merged so wrapped sentences are translated as one.</summary>
public sealed record OcrBlock(OcrBox Box, string Text, IReadOnlyList<OcrLine> Lines);

public sealed class OcrUnavailableException(string message) : Exception(message);

/// <summary>Offline OCR using the engine built into Windows 10/11.</summary>
public sealed class WindowsOcr
{
    public static IReadOnlyList<string> AvailableLanguages() =>
        OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToList();

    /// <param name="language">"auto" tries every installed OCR language and keeps the best result.</param>
    public async Task<IReadOnlyList<OcrBlock>> RecognizeAsync(Bitmap bitmap, string language, double mergeSensitivity, double scaleBack = 1)
    {
        var engines = CreateEngines(language);
        if (engines.Count == 0)
            throw new OcrUnavailableException("No Windows OCR language pack is installed. Add one in Settings > Time & language > Language & region.");

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Bmp);
        ms.Position = 0;
        using var ras = new InMemoryRandomAccessStream();
        await ras.WriteAsync(ms.ToArray().AsBuffer());
        ras.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(ras);
        using var soft = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        OcrResult? best = null; int bestScore = -1; string bestLang = "";
        foreach (var engine in engines)
        {
            var r = await engine.RecognizeAsync(soft);
            var score = r.Text.Count(char.IsLetter);
            if (score > bestScore) { best = r; bestScore = score; bestLang = engine.RecognizerLanguage.LanguageTag; }
        }
        if (best == null || bestScore <= 0) return Array.Empty<OcrBlock>();

        bool cjk = bestLang.StartsWith("ja") || bestLang.StartsWith("zh") || bestLang.StartsWith("ko");
        var lines = best.Lines.Select(l =>
        {
            var words = l.Words;
            double x1 = words.Min(w => w.BoundingRect.Left), y1 = words.Min(w => w.BoundingRect.Top);
            double x2 = words.Max(w => w.BoundingRect.Right), y2 = words.Max(w => w.BoundingRect.Bottom);
            var text = cjk ? string.Concat(words.Select(w => w.Text)) : l.Text;
            return new OcrLine(new OcrBox(x1 / scaleBack, y1 / scaleBack, (x2 - x1) / scaleBack, (y2 - y1) / scaleBack), text.Trim());
        }).Where(l => l.Text.Length > 0).ToList();

        return Group(lines, mergeSensitivity, cjk);
    }

    private static List<OcrEngine> CreateEngines(string language)
    {
        var list = new List<OcrEngine>();
        if (language != "auto" && !string.IsNullOrWhiteSpace(language))
        {
            try
            {
                var lang = new Language(language);
                if (OcrEngine.IsLanguageSupported(lang) && OcrEngine.TryCreateFromLanguage(lang) is { } e) list.Add(e);
            }
            catch { }
            if (list.Count > 0) return list;
        }
        foreach (var l in OcrEngine.AvailableRecognizerLanguages)
            if (OcrEngine.TryCreateFromLanguage(l) is { } e) list.Add(e);
        return list;
    }

    /// <summary>Merges consecutive lines that sit close together and line up into one paragraph.</summary>
    public static List<OcrBlock> Group(IReadOnlyList<OcrLine> lines, double sensitivity, bool cjk = false)
    {
        var ordered = lines.OrderBy(l => l.Box.Y).ThenBy(l => l.Box.X).ToList();
        var groups = new List<List<OcrLine>>();
        foreach (var line in ordered)
        {
            List<OcrLine>? target = null;
            foreach (var g in groups)
            {
                var last = g[^1];
                var h = Math.Max(last.Box.Height, line.Box.Height);
                var gap = line.Box.Y - last.Box.Bottom;
                var overlapX = Math.Min(last.Box.Right, line.Box.Right) - Math.Max(last.Box.X, line.Box.X);
                var similarHeight = Math.Min(last.Box.Height, line.Box.Height) / h > 0.6;
                var leftAligned = Math.Abs(last.Box.X - line.Box.X) < h * 2.5;
                if (gap < h * 0.9 * sensitivity && gap > -h * 0.5 && overlapX > 0 && similarHeight && leftAligned)
                { target = g; break; }
            }
            if (target == null) groups.Add(new() { line }); else target.Add(line);
        }
        return groups.Select(g =>
        {
            double x1 = g.Min(l => l.Box.X), y1 = g.Min(l => l.Box.Y), x2 = g.Max(l => l.Box.Right), y2 = g.Max(l => l.Box.Bottom);
            var sb = new StringBuilder();
            foreach (var l in g)
            {
                if (sb.Length > 0) sb.Append(cjk ? "" : " ");
                sb.Append(l.Text);
            }
            return new OcrBlock(new OcrBox(x1, y1, x2 - x1, y2 - y1), sb.ToString(), g);
        }).ToList();
    }

    /// <summary>The block under (or nearest to) a point; null if nothing is within a sensible distance.</summary>
    public static OcrBlock? BlockAt(IReadOnlyList<OcrBlock> blocks, double x, double y, double maxDistance = 60)
    {
        var inside = blocks.Where(b => b.Box.Contains(x, y)).OrderBy(b => b.Box.Width * b.Box.Height).FirstOrDefault();
        if (inside != null) return inside;
        return blocks.Select(b => (b, d: b.Box.DistanceTo(x, y))).Where(t => t.d <= maxDistance).OrderBy(t => t.d).Select(t => t.b).FirstOrDefault();
    }
}

