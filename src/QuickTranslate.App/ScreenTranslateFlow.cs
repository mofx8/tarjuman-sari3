using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using QuickTranslate.Core.Settings;
using QuickTranslate.Platform;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace QuickTranslate.App;

/// <summary>Opens the capture overlay on the monitor that holds the mouse pointer.</summary>
public sealed class ScreenTranslateFlow(AppServices svc)
{
    private OverlayWindow? _overlay;
    private (MonitorInfo Monitor, System.Drawing.Rectangle Region)? _last;

    public Task StartAsync(SelectionMode mode)
    {
        _overlay?.Close();
        var (cx, cy) = Screens.CursorPosition();
        var monitor = Screens.UnderCursor();
        var shot = Screens.Capture(monitor);
        _overlay = new OverlayWindow(svc, shot, monitor, mode, (cx - monitor.X, cy - monitor.Y), null, r => _last = (monitor, r));
        _overlay.Closed += (_, _) => _overlay = null;
        _overlay.Show();
        return Task.CompletedTask;
    }

    private LiveTranslateWindow? _live;

    /// <summary>Starts live translation: the user draws the area, then it is re-read and translated continuously.</summary>
    public void ToggleLive(string? process = null, bool forcePick = false)
    {
        if (_live != null) { _live.Close(); return; }
        _overlay?.Close();
        var s = svc.Settings;
        var key = (process ?? "").ToLowerInvariant();

        // Reuse the area remembered for this application, if it is still on a connected monitor.
        if (!forcePick && s.LiveReuseSavedRegion && key.Length > 0 && s.LiveRegions.TryGetValue(key, out var saved)
            && Screens.All().FirstOrDefault(m => m.X == saved.MonitorX && m.Y == saved.MonitorY && m.Width == saved.MonitorWidth && m.Height == saved.MonitorHeight) is { } savedMonitor)
        {
            StartLive(savedMonitor, new System.Drawing.Rectangle(saved.X, saved.Y, saved.Width, saved.Height));
            return;
        }

        var monitor = Screens.UnderCursor();
        var shot = Screens.Capture(monitor);
        _overlay = new OverlayWindow(svc, shot, monitor, SelectionMode.DragRectangle, (0, 0), null, _ => { })
        {
            PickCallback = rect =>
            {
                if (key.Length > 0)
                {
                    s.LiveRegions[key] = new SavedRegion(key, monitor.X, monitor.Y, monitor.Width, monitor.Height, rect.X, rect.Y, rect.Width, rect.Height);
                    svc.Store.Save();
                }
                StartLive(monitor, rect);
            },
        };
        _overlay.Closed += (_, _) => _overlay = null;
        _overlay.Show();
    }

    private void StartLive(MonitorInfo monitor, System.Drawing.Rectangle rect)
    {
        _live = new LiveTranslateWindow(svc, monitor, rect);
        _live.Closed += (_, _) => _live = null;
        _live.Show();
    }
    /// <summary>Re-reads the previously translated area (handy for game dialogue).</summary>
    public Task RepeatAsync()
    {
        if (_last is not { } last) return StartAsync(svc.Settings.DefaultSelectionMode);
        _overlay?.Close();
        var monitor = Screens.All().FirstOrDefault(m => m.X == last.Monitor.X && m.Y == last.Monitor.Y) ?? Screens.UnderCursor();
        var shot = Screens.Capture(monitor);
        _overlay = new OverlayWindow(svc, shot, monitor, SelectionMode.DragRectangle, (0, 0), last.Region, r => _last = (monitor, r));
        _overlay.Closed += (_, _) => _overlay = null;
        _overlay.Show();
        return Task.CompletedTask;
    }
}

internal sealed class OverlayWindow : Window
{
    private readonly AppServices _svc;
    private readonly Bitmap _shot;
    private readonly MonitorInfo _mon;
    private readonly Action<System.Drawing.Rectangle> _remember;
    private readonly (int X, int Y) _cursor;
    private readonly System.Drawing.Rectangle? _initialRegion;
    private readonly SelectionMode _mode;

    /// <summary>When set, the overlay only lets the user draw a rectangle and hands it back (live translation).</summary>
    public Action<System.Drawing.Rectangle>? PickCallback { get; init; }

    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly Canvas _canvas = new();
    private readonly Canvas _results = new();
    private readonly Rectangle _hover = new();
    private readonly Rectangle _drag = new();
    private readonly TextBlock _hint = new();
    private readonly Border _card = new();
    private readonly TextBlock _cardText = new();

    private IReadOnlyList<OcrBlock>? _blocks;
    private Task<IReadOnlyList<OcrBlock>>? _ocrTask;
    private Point? _dragStart;
    private bool _dragging;
    private readonly List<string> _lastTranslations = new();
    private System.Drawing.Rectangle? _lastRegion;
    private CancellationTokenSource _cts = new();
    private bool _closed;

    public OverlayWindow(AppServices svc, Bitmap shot, MonitorInfo mon, SelectionMode mode, (int X, int Y) cursor,
        System.Drawing.Rectangle? initialRegion, Action<System.Drawing.Rectangle> remember)
    {
        _svc = svc; _shot = shot; _mon = mon; _mode = mode; _cursor = cursor; _initialRegion = initialRegion; _remember = remember;
        var s = svc.Effective;

        WindowStyle = WindowStyle.None; AllowsTransparency = false; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; Background = Brushes.Black; Cursor = Cursors.Cross;
        WindowStartupLocation = WindowStartupLocation.Manual; FlowDirection = System.Windows.FlowDirection.LeftToRight;
        Left = mon.X / mon.Scale; Top = mon.Y / mon.Scale; Width = mon.Width / mon.Scale; Height = mon.Height / mon.Scale;

        using (var ms = new MemoryStream())
        {
            shot.Save(ms, ImageFormat.Bmp);
            ms.Position = 0;
            var bi = new BitmapImage();
            bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit(); bi.Freeze();
            _image.Source = bi;
        }

        var accent = Win.ParseColor(s.AccentColor, Colors.DodgerBlue);
        _hover.Stroke = new SolidColorBrush(accent); _hover.StrokeThickness = 2; _hover.Fill = new SolidColorBrush(Color.FromArgb(40, accent.R, accent.G, accent.B));
        _hover.Visibility = Visibility.Collapsed; _hover.IsHitTestVisible = false;
        _drag.Stroke = Brushes.White; _drag.StrokeDashArray = new DoubleCollection { 4, 3 }; _drag.StrokeThickness = 1.5;
        _drag.Fill = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)); _drag.Visibility = Visibility.Collapsed; _drag.IsHitTestVisible = false;
        _canvas.Children.Add(_hover); _canvas.Children.Add(_results); _canvas.Children.Add(_drag);
        _results.IsHitTestVisible = false;

        _hint.Foreground = Brushes.White; _hint.FontSize = 13; _hint.FlowDirection = System.Windows.FlowDirection.RightToLeft;
        _hint.Text = Loc.T("جارٍ قراءة النص…");
        var hintBox = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 17, 24, 39)), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 16, 0, 0),
            Child = _hint, IsHitTestVisible = false,
        };

        _cardText.Foreground = Brushes.White; _cardText.TextWrapping = TextWrapping.Wrap; _cardText.FontSize = 13; _cardText.MaxWidth = 480;
        var copy = new Button { Content = Loc.T("نسخ الترجمة"), Margin = new Thickness(0, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3), Cursor = Cursors.Hand };
        copy.Click += (_, _) => { try { Clipboard.SetText(string.Join("\n", _lastTranslations)); } catch { } };
        copy.PreviewMouseDown += (_, e) => e.Handled = true;
        _card.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _cardText, new Border { Width = 12 }, copy }, FlowDirection = System.Windows.FlowDirection.LeftToRight };
        _card.Background = new SolidColorBrush(Color.FromArgb(220, 17, 24, 39)); _card.CornerRadius = new CornerRadius(8);
        _card.Padding = new Thickness(12, 8, 12, 8); _card.HorizontalAlignment = HorizontalAlignment.Center;
        _card.VerticalAlignment = VerticalAlignment.Bottom; _card.Margin = new Thickness(0, 0, 0, 24); _card.Visibility = Visibility.Collapsed;

        var dim = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(s.DimStrength, 0, 0.9)), 0, 0, 0)), IsHitTestVisible = false };
        var root = new Grid();
        root.Children.Add(_image); root.Children.Add(dim); root.Children.Add(_canvas); root.Children.Add(hintBox); root.Children.Add(_card);
        Content = root;

        Loaded += OnLoaded;
        // Moving the mouse to another monitor cancels the capture (and any OCR/translation in progress).
        Loaded += (_, _) => Win.WatchMonitorLeave(this, Close);
        MouseMove += OnMouseMove; MouseLeftButtonDown += OnDown; MouseLeftButtonUp += OnUp;
        MouseRightButtonDown += (_, _) => Close();
        KeyDown += OnKey;
        Closed += (_, _) =>
        {
            _closed = true; _cts.Cancel();
            // the bitmap may still be read by a running OCR pass: free it only when that finished
            if (_ocrTask is { IsCompleted: false } pending) pending.ContinueWith(_ => _shot.Dispose(), TaskScheduler.Default);
            else _shot.Dispose();
        };
    }

    private double K => _image.ActualWidth > 0 ? _shot.Width / _image.ActualWidth : 1; // DIP -> bitmap pixels
    private double ToDip(double px) => px / K;

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Win.PlacePhysical(this, _mon.X, _mon.Y, _mon.Width, _mon.Height);
        Activate(); Focus();
        if (_svc.Settings.AutoCloseSeconds > 0)
        {
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(_svc.Settings.AutoCloseSeconds + 4) };
            t.Tick += (_, _) => { t.Stop(); Close(); };
            t.Start();
        }
        try
        {
            if (PickCallback != null) { _hint.Text = Loc.T(Loc.T("اسحب لتحديد منطقة الترجمة المباشرة • Esc أو زر الفأرة الأيمن للإلغاء")); return; }
            if (_initialRegion is { } region) { await TranslateRegionAsync(region); return; }
            _ocrTask = _svc.Ocr.RecognizeAsync(_shot, _svc.Settings.OcrLanguage, _svc.Settings.ParagraphMergeSensitivity);
            _blocks = await _ocrTask;
            if (_closed) return;
            Log.Info("OCR", $"{_blocks.Count} text blocks on {_mon.Width}x{_mon.Height}");
            ShowDefaultHint();
            if (_mode == SelectionMode.UnderCursor)
            {
                var b = WindowsOcr.BlockAt(_blocks, _cursor.X, _cursor.Y, 90 * _mon.Scale);
                if (b != null) await TranslateBlocksAsync(new[] { b });
                else _hint.Text = Loc.T("لا يوجد نص قرب المؤشر — انقر على نص أو اسحب لتحديد منطقة");
            }
        }
        catch (OcrUnavailableException ex) { _hint.Text = ex.Message; }
        catch (Exception ex) { Log.Error("Overlay", ex); _hint.Text = Loc.T("حدث خطأ: ") + ex.Message; }
    }

    private void ShowDefaultHint() =>
        _hint.Text = Loc.T("انقر على نص لترجمته • اسحب لتحديد منطقة • Space إخفاء/إظهار • R إعادة • Esc إغلاق");

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(_canvas);
        if (_dragStart is { } s)
        {
            if (!_dragging && (Math.Abs(p.X - s.X) > 6 || Math.Abs(p.Y - s.Y) > 6)) _dragging = true;
            if (_dragging)
            {
                _hover.Visibility = Visibility.Collapsed;
                _drag.Visibility = Visibility.Visible;
                Canvas.SetLeft(_drag, Math.Min(p.X, s.X)); Canvas.SetTop(_drag, Math.Min(p.Y, s.Y));
                _drag.Width = Math.Abs(p.X - s.X); _drag.Height = Math.Abs(p.Y - s.Y);
            }
            return;
        }
        if (_blocks == null) return;
        var b = WindowsOcr.BlockAt(_blocks, p.X * K, p.Y * K, 0);
        if (b == null) { _hover.Visibility = Visibility.Collapsed; return; }
        _hover.Visibility = Visibility.Visible;
        Canvas.SetLeft(_hover, ToDip(b.Box.X) - 3); Canvas.SetTop(_hover, ToDip(b.Box.Y) - 3);
        _hover.Width = ToDip(b.Box.Width) + 6; _hover.Height = ToDip(b.Box.Height) + 6;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(_canvas); _dragging = false;
        CaptureMouse();
    }

    private async void OnUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        var start = _dragStart; _dragStart = null;
        if (start is not { } s) return;
        var p = e.GetPosition(_canvas);
        try
        {
            if (_dragging)
            {
                _dragging = false; _drag.Visibility = Visibility.Collapsed;
                var x = (int)(Math.Min(p.X, s.X) * K); var y = (int)(Math.Min(p.Y, s.Y) * K);
                var w = (int)(Math.Abs(p.X - s.X) * K); var h = (int)(Math.Abs(p.Y - s.Y) * K);
                if (w < 8 || h < 8) return;
                if (PickCallback != null) { var cb = PickCallback; Close(); cb(new System.Drawing.Rectangle(x, y, w, h)); return; }
                await TranslateRegionAsync(new System.Drawing.Rectangle(x, y, w, h));
            }
            else if (_blocks != null)
            {
                var b = WindowsOcr.BlockAt(_blocks, p.X * K, p.Y * K, 25 * _mon.Scale);
                if (b != null) await TranslateBlocksAsync(new[] { b });
            }
        }
        catch (OcrUnavailableException ex) { _hint.Text = ex.Message; }
        catch (Exception ex) { Log.Error("Overlay select", ex); _hint.Text = Loc.T("تعذّرت الترجمة: ") + ex.Message; }
    }

    private async Task TranslateRegionAsync(System.Drawing.Rectangle region)
    {
        region.Intersect(new System.Drawing.Rectangle(0, 0, _shot.Width, _shot.Height));
        _hint.Text = Loc.T("جارٍ قراءة المنطقة…");
        using var crop = Screens.CropForOcr(_shot, region, _svc.Settings.UpscaleSmallCrops, out var factor);
        var found = await _svc.Ocr.RecognizeAsync(crop, _svc.Settings.OcrLanguage, _svc.Settings.ParagraphMergeSensitivity, factor);
        if (_closed) return;
        if (found.Count == 0) { _hint.Text = Loc.T("لم يتم العثور على نص في هذه المنطقة"); return; }
        // Move crop-relative boxes back into screen-bitmap coordinates.
        var moved = found.Select(b => new OcrBlock(new OcrBox(b.Box.X + region.X, b.Box.Y + region.Y, b.Box.Width, b.Box.Height), b.Text, b.Lines)).ToList();
        _remember(region);
        _lastRegion = region;
        await TranslateBlocksAsync(moved, remember: false);
    }

    private async Task TranslateBlocksAsync(IReadOnlyList<OcrBlock> blocks, bool remember = true)
    {
        _cts.Cancel(); _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        if (remember && blocks.Count == 1)
        {
            var b = blocks[0].Box;
            var rect = new System.Drawing.Rectangle((int)b.X - 4, (int)b.Y - 4, (int)b.Width + 8, (int)b.Height + 8);
            _remember(rect); _lastRegion = rect;
        }
        _hint.Text = Loc.T("جارٍ الترجمة…");
        _results.Children.Clear();
        var tasks = blocks.Select(b => _svc.TranslateAsync(b.Text, ct: ct, kind: "screen")).ToArray();
        Core.Translation.TranslationResult[] done;
        try { done = await Task.WhenAll(tasks); if (_closed) return; }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { _hint.Text = Loc.T("تعذّرت الترجمة: ") + ex.Message; return; }

        _lastTranslations.Clear();
        var s = _svc.Effective;
        var fg = new SolidColorBrush(Win.ParseColor(s.OverlayTextColor, Colors.White));
        var bg = new SolidColorBrush(Win.ParseColor(s.OverlayBackground, Color.FromRgb(16, 24, 39))) { Opacity = s.OverlayOpacity };
        var accent = new SolidColorBrush(Win.ParseColor(s.AccentColor, Colors.DodgerBlue));
        for (var i = 0; i < blocks.Count; i++)
        {
            var box = blocks[i].Box;
            var text = done[i].Text;
            _lastTranslations.Add(text);
            var lineHeight = box.Height / Math.Max(1, blocks[i].Lines.Count);
            var fontSize = Math.Clamp(ToDip(lineHeight) * 0.78, 11, s.OverlayFontSize * 1.4);
            var tb = new TextBlock
            {
                Text = text, TextWrapping = TextWrapping.Wrap, Foreground = fg, FontSize = fontSize,
                FontFamily = new FontFamily(s.OverlayFontFamily), FlowDirection = System.Windows.FlowDirection.RightToLeft,
                TextAlignment = TextAlignment.Right,
            };
            var border = new Border
            {
                Background = bg, BorderBrush = accent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Math.Min(s.CornerRadius, 8)),
                Padding = new Thickness(6, 3, 6, 3), Child = tb, MinWidth = ToDip(box.Width), MaxWidth = Math.Max(ToDip(box.Width) * 1.4, 260),
                FlowDirection = System.Windows.FlowDirection.RightToLeft,
            };
            if (s.Animations) { border.Opacity = 0; border.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(180))); }
            var left = ToDip(box.X) - 6; var top = ToDip(box.Y) - 3;
            border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            left = Math.Clamp(left, 4, Math.Max(4, ActualWidth - border.DesiredSize.Width - 4));
            top = Math.Clamp(top, 4, Math.Max(4, ActualHeight - border.DesiredSize.Height - 4));
            Canvas.SetLeft(border, left); Canvas.SetTop(border, top);
            _results.Children.Add(border);
        }
        _hover.Visibility = Visibility.Collapsed;
        _cardText.Text = s.ShowOriginalCard ? string.Join(" ", blocks.Select(b => b.Text)).Trim() : "";
        if (_cardText.Text.Length > 220) _cardText.Text = _cardText.Text[..220] + "…";
        _card.Visibility = s.ShowOriginalCard ? Visibility.Visible : Visibility.Collapsed;
        _hint.Text = Loc.T("تمت الترجمة — انقر نصاً آخر، أو Space لإظهار الأصل، أو Esc للإغلاق");
        if (s.AutoCopyTranslation) try { Clipboard.SetText(string.Join("\n", _lastTranslations)); } catch { }
    }

    private async void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Space:
                _results.Visibility = _results.Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
                e.Handled = true; break;
            case Key.R when _lastRegion is { } r:
                try { await TranslateRegionAsync(r); } catch (Exception ex) { _hint.Text = ex.Message; }
                break;
            case Key.N:
                _results.Children.Clear(); _card.Visibility = Visibility.Collapsed; ShowDefaultHint(); break;
        }
    }
}







