using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TarjumanSari3.Platform;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;

namespace TarjumanSari3.App;

/// <summary>Translates an image (clipboard screenshot or file): reads the text, then draws the Arabic over the original.</summary>
internal sealed class ImageTranslateWindow : Window
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

    private readonly AppServices _svc;
    private readonly System.Drawing.Bitmap _bitmap;
    private readonly Grid _canvasHost = new();
    private readonly Canvas _layer = new();
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 };
    private readonly List<string> _translations = new();
    private readonly CancellationTokenSource _cts = new();

    // ---------- entry points ----------
    public static void ShowFromClipboard(AppServices svc)
    {
        var bmp = ClipboardBitmap();
        if (bmp == null)
        {
            PopupWindow.ShowMessage(svc, Loc.T("لا توجد صورة في الحافظة (انسخ لقطة شاشة أولاً، مثل Win+Shift+S)."));
            return;
        }
        new ImageTranslateWindow(svc, bmp).Show();
    }

    public static void ShowFromFile(AppServices svc, Window? owner = null)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("ترجمة صورة من ملف"),
            Filter = "Images|" + string.Join(";", ImageExtensions.Select(e => "*" + e)),
        };
        if (dlg.ShowDialog(owner) != true) return;
        ShowPath(svc, dlg.FileName);
    }

    public static bool IsImageFile(string path) => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static void ShowPath(AppServices svc, string path)
    {
        try { new ImageTranslateWindow(svc, LoadFile(path)).Show(); }
        catch (Exception ex) { Log.Error("ImageFile", ex); PopupWindow.ShowMessage(svc, Loc.T("تعذّر فتح الصورة: ") + ex.Message); }
    }

    private static System.Drawing.Bitmap LoadFile(string path)
    {
        try
        {
            using var tmp = new System.Drawing.Bitmap(path);
            return new System.Drawing.Bitmap(tmp);
        }
        catch (Exception) when (Path.GetExtension(path).Equals(".webp", StringComparison.OrdinalIgnoreCase))
        {
            // WebP is not supported by GDI+; the Windows imaging codec (if installed) can decode it.
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(new Uri(path)));
            using var ms = new MemoryStream();
            enc.Save(ms); ms.Position = 0;
            using var tmp = new System.Drawing.Bitmap(ms);
            return new System.Drawing.Bitmap(tmp);
        }
    }

    /// <summary>A bitmap on the clipboard, or the first image file if an image file was copied in Explorer.</summary>
    private static System.Drawing.Bitmap? ClipboardBitmap()
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } src)
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(src));
                    using var ms = new MemoryStream();
                    enc.Save(ms); ms.Position = 0;
                    using var tmp = new System.Drawing.Bitmap(ms);
                    return new System.Drawing.Bitmap(tmp);
                }
                if (Clipboard.ContainsFileDropList())
                {
                    var file = Clipboard.GetFileDropList().Cast<string>()
                        .FirstOrDefault(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                    if (file != null) return LoadFile(file);
                }
                return null;
            }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(60); } // clipboard busy
        }
        return null;
    }

    // ---------- window ----------
    private ImageTranslateWindow(AppServices svc, System.Drawing.Bitmap bitmap)
    {
        _svc = svc; _bitmap = bitmap;
        Title = Loc.T("ترجمة صورة"); Width = 980; Height = 720; MinWidth = 520; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FlowDirection = Loc.English ? System.Windows.FlowDirection.LeftToRight : System.Windows.FlowDirection.RightToLeft;

        var image = new Image { Source = ToSource(bitmap), Stretch = Stretch.Fill };
        _canvasHost.Width = bitmap.Width; _canvasHost.Height = bitmap.Height;
        _canvasHost.Children.Add(image); _canvasHost.Children.Add(_layer);
        // Everything inside the Viewbox uses real image pixels, so OCR boxes map 1:1 and the view scales to fit.
        var viewbox = new Viewbox { Stretch = Stretch.Uniform, Child = _canvasHost, FlowDirection = System.Windows.FlowDirection.LeftToRight };

        var copyText = new Button { Content = Ui.IconText(MahApps.Metro.IconPacks.PackIconLucideKind.Copy, Loc.T("نسخ النص المترجم"), 15), Padding = new Thickness(12, 5, 12, 5) };
        var copyImage = new Button { Content = Ui.IconText(MahApps.Metro.IconPacks.PackIconLucideKind.Image, Loc.T("نسخ الصورة المترجمة"), 15), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0) };
        var toggle = new System.Windows.Controls.Primitives.ToggleButton { Content = Ui.IconText(MahApps.Metro.IconPacks.PackIconLucideKind.Eye, Loc.T("إظهار الأصل"), 15), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0) };
        copyText.Click += (_, _) => { if (_copyText.Length > 0) { try { Clipboard.SetText(_copyText); _status.Text = Loc.T("تم النسخ."); } catch { } } };
        copyImage.Click += (_, _) => CopyRenderedImage();
        toggle.Checked += (_, _) => { _layer.Visibility = Visibility.Hidden; toggle.Content = Ui.IconText(MahApps.Metro.IconPacks.PackIconLucideKind.EyeOff, Loc.T("إظهار الترجمة"), 15); };
        toggle.Unchecked += (_, _) => { _layer.Visibility = Visibility.Visible; toggle.Content = Ui.IconText(MahApps.Metro.IconPacks.PackIconLucideKind.Eye, Loc.T("إظهار الأصل"), 15); };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 12, 10) };
        bar.Children.Add(copyText); bar.Children.Add(copyImage); bar.Children.Add(toggle);
        bar.Children.Add(new Border { Width = 14 }); bar.Children.Add(_status);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(viewbox, 0); Grid.SetRow(bar, 1);
        root.Children.Add(viewbox); root.Children.Add(bar);
        Content = root;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();
        Loaded += async (_, _) => await TranslateAsync();
        Closed += (_, _) => { _cts.Cancel(); _bitmap.Dispose(); };
    }

    private async Task TranslateAsync()
    {
        _status.Text = Loc.T("جارٍ قراءة النص…");
        try
        {
            var blocks = await ReadTextAsync();
            if (blocks.Count == 0) { _status.Text = Loc.T("لم يتم العثور على نص في الصورة"); return; }

            // Wrapped paragraphs are translated as one piece (so the engine sees the whole sentence) and the result is
            // spread back over the original lines; short labels are translated one by one. Shortcuts, identifiers,
            // URLs and text already in Arabic are left untouched.
            var units = new List<List<OcrLine>>();
            foreach (var b in blocks)
            {
                var ls = b.Lines.Where(l => IsTranslatable(l.Text)).ToList();
                var prose = ls.Count == b.Lines.Count && ls.Count >= 2 && ls.Take(ls.Count - 1).All(l => l.Text.Length >= 24);
                if (prose) units.Add(ls); else units.AddRange(ls.Select(l => new List<OcrLine> { l }));
            }
            if (units.Count == 0) { _status.Text = Loc.T("لا يوجد نص قابل للترجمة في الصورة"); return; }
            _status.Text = Loc.T("جارٍ الترجمة…");
            var unitTexts = units.Select(u => string.Join(" ", u.Select(l => l.Text))).ToList();
            var results = await Task.WhenAll(unitTexts.Select(x => _svc.TranslateAsync(x, ct: _cts.Token, saveHistory: false)));

            var drawLines = new List<OcrLine>(); var drawTexts = new List<string>();
            for (var u = 0; u < units.Count; u++)
            {
                var pieces = units[u].Count == 1 ? new List<string> { results[u].Text } : SpreadOverLines(results[u].Text, units[u]);
                drawLines.AddRange(units[u]); drawTexts.AddRange(pieces);
            }
            Draw(drawLines, drawTexts);
            _copyText = string.Join("\n", results.Select(r => r.Text));
            _status.Text = Loc.T("تمت الترجمة") + $" ({drawLines.Count})";

            // One history entry for the whole image (not one per line).
            if (_svc.Settings.SaveHistory)
                _svc.History.Add(Trim(string.Join("\n", unitTexts)), Trim(_copyText),
                    results[0].DetectedSource, _svc.Effective.TargetLanguage, results[0].EngineId, "image");
            Log.Info("Image", $"{units.Count} units ({drawLines.Count} lines) translated · {_bitmap.Width}x{_bitmap.Height}");            if (_svc.Settings.AutoCopyTranslation) try { Clipboard.SetText(_copyText); } catch { }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { } // window closed while the image was being read
        catch (OcrUnavailableException ex) { _status.Text = ex.Message; }
        catch (Exception ex) { Log.Error("ImageTranslate", ex); _status.Text = Loc.T("تعذّرت الترجمة: ") + ex.Message; }
    }

    private string _copyText = "";

    /// <summary>Splits a translated paragraph into one piece per original line, in proportion to each line's length.</summary>
    internal static List<string> SpreadOverLines(string translation, IReadOnlyList<OcrLine> lines)
    {
        var words = translation.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();
        if (words.Length < lines.Count) // too short to split: the whole text on the first line, the rest just covered
        {
            result.Add(translation);
            result.AddRange(Enumerable.Repeat("", lines.Count - 1));
            return result;
        }
        double total = lines.Sum(l => (double)l.Text.Length), acc = 0;
        var start = 0;
        for (var k = 0; k < lines.Count; k++)
        {
            acc += lines[k].Text.Length;
            var end = k == lines.Count - 1 ? words.Length : Math.Clamp((int)Math.Round(acc / total * words.Length), start + 1, words.Length - (lines.Count - 1 - k));
            result.Add(string.Join(' ', words[start..end]));
            start = end;
        }
        return result;
    }

    private static string Trim(string s) => s.Length > 4000 ? s[..4000] : s;

    /// <summary>Skips lines that are not prose: keyboard shortcuts, URLs, identifiers, symbols and Arabic text.</summary>
    internal static bool IsTranslatable(string text)
    {
        var t = text.Trim();
        if (t.Count(char.IsLetter) < 2) return false;
        var singleToken = !t.Any(char.IsWhiteSpace);
        if (singleToken && t.Any(c => c is '+' or '_' or '/' or '\\' or '@' or '#' or '=' or '<' or '>' or '|')) return false; // Ctrl+Alt+T, file.name, a@b
        if (singleToken && t.Any(char.IsDigit) && t.Any(char.IsLetter)) return false; // v1.0, T1
        if (t.Contains("://") || t.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) return false;
        if (t.Count(c => c is >= '\u0600' and <= '\u06FF') > t.Length / 2) return false; // already Arabic
        return true;
    }

    /// <summary>
    /// OCR with a second, enlarged pass when the text is small (Windows OCR is far more accurate on glyphs of ~25 px),
    /// and a downscale for very large images (it has a maximum size).
    /// </summary>
    private async Task<IReadOnlyList<OcrBlock>> ReadTextAsync()
    {
        var s = _svc.Settings;
        var maxSide = Math.Max(_bitmap.Width, _bitmap.Height);
        if (maxSide > 3500)
        {
            var back = 3500.0 / maxSide;
            using var small = Scaled(_bitmap, back);
            return await _svc.Ocr.RecognizeAsync(small, s.OcrLanguage, s.ParagraphMergeSensitivity, back);
        }

        var first = await _svc.Ocr.RecognizeAsync(_bitmap, s.OcrLanguage, s.ParagraphMergeSensitivity, 1);
        var heights = first.SelectMany(b => b.Lines).Select(l => l.Box.Height).OrderBy(h => h).ToList();
        var median = heights.Count > 0 ? heights[heights.Count / 2] : 0;
        if (!s.UpscaleSmallCrops || (median >= 20 && heights.Count > 0)) return first;

        var factor = median > 0 ? Math.Clamp(28.0 / median, 1.5, 4) : 2.5;
        if (maxSide * factor > 4200) factor = 4200.0 / maxSide;
        if (factor < 1.3) return first;
        using var big = Scaled(_bitmap, factor);
        var second = await _svc.Ocr.RecognizeAsync(big, s.OcrLanguage, s.ParagraphMergeSensitivity, factor);
        static int Letters(IReadOnlyList<OcrBlock> b) => b.Sum(x => x.Text.Count(char.IsLetter));
        Log.Info("Image", $"small text (median {median:0}px): enlarged x{factor:0.0} - {Letters(first)} -> {Letters(second)} letters");
        return Letters(second) >= Letters(first) ? second : first;
    }

    private static System.Drawing.Bitmap Scaled(System.Drawing.Bitmap src, double factor)
    {
        var bmp = new System.Drawing.Bitmap((int)(src.Width * factor), (int)(src.Height * factor));
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.DrawImage(src, 0, 0, bmp.Width, bmp.Height);
        return bmp;
    }
    private void Draw(IReadOnlyList<OcrLine> lines, List<string> texts)
    {
        var s = _svc.Effective;
        var typeface = new Typeface(new FontFamily(s.OverlayFontFamily), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var themeBg = Win.ParseColor(s.OverlayBackground, Color.FromRgb(16, 24, 39));
        var themeFg = Win.ParseColor(s.OverlayTextColor, Colors.White);
        _layer.Children.Clear(); _translations.Clear();

        for (var i = 0; i < lines.Count; i++)
        {
            var box = lines[i].Box; var text = texts[i];
            _translations.Add(text);

            Color bgColor = themeBg, fgColor = themeFg;
            if (s.ImageMatchBackground)
            {
                bgColor = SampleBackground(box);
                var lum = 0.299 * bgColor.R + 0.587 * bgColor.G + 0.114 * bgColor.B;
                fgColor = lum > 140 ? Color.FromRgb(24, 24, 24) : Colors.White;
            }

            double Measure(double size) => new FormattedText(text, System.Globalization.CultureInfo.GetCultureInfo("ar"),
                System.Windows.FlowDirection.RightToLeft, typeface, size, Brushes.Black, 1.0).Width;

            // One line, sized to the original line: shrink the text to fit, or grow leftwards if it still does not.
            var fontSize = Math.Clamp(box.Height * 0.72, 9, 140);
            var target = box.Width * 1.12 + 6;
            var width = Measure(fontSize);
            if (width > target) { fontSize = Math.Max(fontSize * target / width, Math.Max(7, fontSize * 0.55)); width = Measure(fontSize); }
            var available = box.Right + 4;
            if (width + 8 > available) { fontSize = Math.Max(7, fontSize * (available - 8) / width); width = Measure(fontSize); }
            var boxWidth = Math.Max(box.Width + 6, width + 8);

            var tb = new TextBlock
            {
                Text = text, Foreground = new SolidColorBrush(fgColor), FontFamily = typeface.FontFamily, FontWeight = FontWeights.SemiBold,
                FontSize = fontSize, FlowDirection = System.Windows.FlowDirection.RightToLeft, TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = System.Windows.VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(4, 0, 4, 0),
            };
            var border = new Border
            {
                Width = boxWidth, Height = box.Height + 4, Background = new SolidColorBrush(bgColor), CornerRadius = new CornerRadius(3),
                Child = tb, FlowDirection = System.Windows.FlowDirection.RightToLeft,
            };
            Canvas.SetLeft(border, Math.Max(0, box.Right + 3 - boxWidth)); Canvas.SetTop(border, box.Y - 2);
            _layer.Children.Add(border);
        }
    }

    /// <summary>Median colour of a thin ring just outside the line, so the translation blends into the picture.</summary>
    private Color SampleBackground(OcrBox b)
    {
        int w = _bitmap.Width, h = _bitmap.Height;
        int x0 = (int)Math.Max(0, b.X - 3), y0 = (int)Math.Max(0, b.Y - 3), x1 = (int)Math.Min(w - 1, b.Right + 3), y1 = (int)Math.Min(h - 1, b.Bottom + 3);
        var rs = new List<int>(); var gs = new List<int>(); var bs = new List<int>();
        void Add(int x, int y) { var c = _bitmap.GetPixel(Math.Clamp(x, 0, w - 1), Math.Clamp(y, 0, h - 1)); rs.Add(c.R); gs.Add(c.G); bs.Add(c.B); }
        var stepX = Math.Max(1, (x1 - x0) / 30); var stepY = Math.Max(1, (y1 - y0) / 8);
        for (var x = x0; x <= x1; x += stepX) { Add(x, y0); Add(x, y1); }
        for (var y = y0; y <= y1; y += stepY) { Add(x0, y); Add(x1, y); }
        static int Median(List<int> v) { v.Sort(); return v[v.Count / 2]; }
        return Color.FromRgb((byte)Median(rs), (byte)Median(gs), (byte)Median(bs));
    }
    private void CopyRenderedImage()
    {
        try
        {
            var rtb = new RenderTargetBitmap(_bitmap.Width, _bitmap.Height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(_canvasHost);
            Clipboard.SetImage(rtb);
            _status.Text = Loc.T("تم نسخ الصورة المترجمة.");
        }
        catch (Exception ex) { Log.Error("CopyImage", ex); _status.Text = Loc.T("تعذّر نسخ الصورة: ") + ex.Message; }
    }

    private static BitmapSource ToSource(System.Drawing.Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit(); bi.Freeze();
        return bi;
    }
}









