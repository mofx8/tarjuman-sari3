using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using QuickTranslate.Platform;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace QuickTranslate.App;

/// <summary>
/// Keeps re-reading a fixed screen area (subtitles, game dialogue, chat) and shows the translation
/// in a caption placed just outside the area, so it never covers (or gets captured with) the source text.
/// </summary>
internal sealed class LiveTranslateWindow : Window
{
    private readonly AppServices _svc;
    private readonly MonitorInfo _mon;
    private readonly System.Drawing.Rectangle _rect; // relative to the monitor, physical px
    private readonly TextBlock _text = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _target; // language/profile captured when live translation starts
    private bool _above;

    public LiveTranslateWindow(AppServices svc, MonitorInfo mon, System.Drawing.Rectangle rect)
    {
        _svc = svc; _mon = mon; _rect = rect;
        var s = svc.Effective; _target = s.TargetLanguage;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false; SizeToContent = SizeToContent.Height;
        Width = Math.Max(rect.Width / mon.Scale, 320); FlowDirection = System.Windows.FlowDirection.RightToLeft;

        _text.Text = Loc.T("مباشر — جارٍ القراءة…");
        _text.TextWrapping = TextWrapping.Wrap; _text.FontSize = s.OverlayFontSize;
        _text.FontFamily = new FontFamily(s.OverlayFontFamily);
        _text.Foreground = new SolidColorBrush(Win.ParseColor(s.OverlayTextColor, Colors.White));
        Content = new Border
        {
            Background = new SolidColorBrush(Win.ParseColor(s.OverlayBackground, Color.FromRgb(16, 24, 39))) { Opacity = s.OverlayOpacity },
            CornerRadius = new CornerRadius(s.CornerRadius), Padding = new Thickness(12, 8, 12, 8), Child = BuildCaption(),
            BorderBrush = new SolidColorBrush(Win.ParseColor(s.AccentColor, Colors.DodgerBlue)), BorderThickness = new Thickness(1.5),
            ToolTip = Loc.T("زر الفأرة الأيمن للإيقاف"),
        };
        MouseRightButtonUp += (_, _) => Close();
        SourceInitialized += (_, _) => Screens.MakeNoActivate(new WindowInteropHelper(this).Handle);
        Loaded += (_, _) => { Reposition(); _ = RunAsync(_cts.Token); };
        SizeChanged += (_, _) => Reposition();
        Closed += (_, _) => _cts.Cancel();
    }

    private UIElement BuildCaption()
    {
        // A small "live" icon in the accent colour, then the translated text.
        var icon = Ui.Icon(MahApps.Metro.IconPacks.PackIconLucideKind.Radio, 16);
        icon.Foreground = new SolidColorBrush(Win.ParseColor(_svc.Settings.AccentColor, Colors.DodgerBlue));
        icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new Thickness(0, 4, 10, 0);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_text, 1);
        grid.Children.Add(icon); grid.Children.Add(_text);
        return grid;
    }

    private void Reposition()
    {
        var scale = _mon.Scale;
        var h = (int)(ActualHeight * scale > 0 ? ActualHeight * scale : 80 * scale);
        var below = _mon.Y + _rect.Bottom + 8;
        _above = below + h > _mon.Y + _mon.Height;
        var y = _above ? _mon.Y + _rect.Y - h - 8 : below;
        var x = Math.Clamp(_mon.X + _rect.X, _mon.X, _mon.X + _mon.Width - (int)(ActualWidth * scale));
        Win.MoveTopMost(this, x, Math.Max(_mon.Y, y));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        byte[]? lastSig = null; string lastText = "";
        var errors = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var shot = Screens.Capture(_mon.X + _rect.X, _mon.Y + _rect.Y, _rect.Width, _rect.Height);
                var sig = Signature(shot);
                if (lastSig == null || Different(lastSig, sig))
                {
                    lastSig = sig;
                    using var crop = Screens.CropForOcr(shot, new System.Drawing.Rectangle(0, 0, shot.Width, shot.Height), _svc.Settings.UpscaleSmallCrops, out var factor);
                    var blocks = await _svc.Ocr.RecognizeAsync(crop, _svc.Settings.OcrLanguage, _svc.Settings.ParagraphMergeSensitivity, factor).ConfigureAwait(false);
                    var original = string.Join(" ", blocks.Select(b => b.Text)).Trim();
                    if (original.Length > 0 && original != lastText)
                    {
                        lastText = original;
                        var r = await _svc.TranslateAsync(original, target: _target, ct: ct, kind: "live").ConfigureAwait(false);
                        _ = Dispatcher.BeginInvoke(() => _text.Text = r.Text);
                    }
                    else if (original.Length == 0 && lastText.Length > 0)
                    {
                        lastText = "";
                        _ = Dispatcher.BeginInvoke(() => _text.Text = "…");
                    }
                }
                errors = 0;
            }
            catch (OperationCanceledException) { return; }
            catch (OcrUnavailableException ex) { _ = Dispatcher.BeginInvoke(() => _text.Text = ex.Message); return; }
            catch (Exception ex)
            {
                Log.Error("Live", ex);
                if (++errors == 3) _ = Dispatcher.BeginInvoke(() => _text.Text = Loc.T("تعذّرت الترجمة: ") + ex.Message);
            }
            try { await Task.Delay(Math.Clamp(_svc.Settings.LiveIntervalMs, 300, 10000), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>16x16 grayscale thumbnail, used to skip OCR when nothing on screen changed.</summary>
    private static byte[] Signature(System.Drawing.Bitmap src)
    {
        using var small = new System.Drawing.Bitmap(src, 16, 16);
        var sig = new byte[256];
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
            {
                var c = small.GetPixel(x, y);
                sig[y * 16 + x] = (byte)((c.R + c.G + c.B) / 3);
            }
        return sig;
    }

    private static bool Different(byte[] a, byte[] b)
    {
        var sum = 0;
        for (var i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]);
        return sum / (double)a.Length > 3;
    }
}






