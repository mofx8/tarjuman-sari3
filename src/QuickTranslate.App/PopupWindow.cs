using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using QuickTranslate.Platform;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace QuickTranslate.App;

internal static class Win
{
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int cx, uint flags);
    private static readonly IntPtr TopMost = new(-1);

    /// <summary>Places a window using physical pixels, avoiding DPI conversion mistakes on mixed-DPI monitors.</summary>
    public static void PlacePhysical(Window w, int x, int y, int width, int height)
    {
        var h = new WindowInteropHelper(w).EnsureHandle();
        SetWindowPos(h, TopMost, x, y, width, height, 0x0040 /*SHOWWINDOW*/);
    }

    /// <summary>Calls <paramref name="onLeave"/> once the mouse pointer moves to a different monitor than the one it started on.</summary>
    public static System.Windows.Threading.DispatcherTimer WatchMonitorLeave(Window w, Action onLeave)
    {
        var start = Screens.UnderCursor().Handle;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => { if (Screens.UnderCursor().Handle != start) { timer.Stop(); onLeave(); } };
        w.Closed += (_, _) => timer.Stop();
        timer.Start();
        return timer;
    }

    public static void MoveTopMost(Window w, int x, int y)
    {
        var h = new WindowInteropHelper(w).EnsureHandle();
        SetWindowPos(h, TopMost, x, y, 0, 0, 0x0001 | 0x0040); // NOSIZE | SHOWWINDOW
    }

    public static Color ParseColor(string s, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(s); } catch { return fallback; }
    }
}

/// <summary>Small floating card with a translation, shown next to the mouse pointer.</summary>
internal sealed class PopupWindow : Window
{
    private readonly TextBlock _result;
    private readonly AppServices _svc;
    private string? _speakText;
    private readonly System.Windows.Threading.DispatcherTimer? _timer;

    private PopupWindow(AppServices svc, string original)
    {
        _svc = svc;
        var s = svc.Effective;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; Topmost = true; SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 520; ShowActivated = true; FlowDirection = System.Windows.FlowDirection.RightToLeft;

        var bg = Win.ParseColor(s.OverlayBackground, Color.FromRgb(16, 24, 39));
        var fg = new SolidColorBrush(Win.ParseColor(s.OverlayTextColor, Colors.White));
        var accent = new SolidColorBrush(Win.ParseColor(s.AccentColor, Colors.DodgerBlue));

        _result = new TextBlock
        {
            Text = "…", TextWrapping = TextWrapping.Wrap, Foreground = fg, FontSize = s.OverlayFontSize,
            FontFamily = new FontFamily(s.OverlayFontFamily), FlowDirection = System.Windows.FlowDirection.RightToLeft,
        };
        var orig = new TextBlock
        {
            Text = original.Length > 200 ? original[..200] + "…" : original, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), FontSize = 12, Margin = new Thickness(0, 0, 0, 6),
            FlowDirection = System.Windows.FlowDirection.LeftToRight, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 40,
        };
        var copy = new Button { Content = Ui.IconText(MahApps.Metro.IconPacks.PackIconLucideKind.Copy, Loc.T("نسخ"), 14, 6), Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };
        copy.Click += (_, _) => { try { Clipboard.SetText(_result.Text); } catch { } Close(); };
        var speak = new Button { Content = Ui.Icon(MahApps.Metro.IconPacks.PackIconLucideKind.Volume2, 15), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand, ToolTip = Loc.T("نطق") };
        void RefreshSpeakIcon() => Dispatcher.BeginInvoke(() => speak.Content = Ui.Icon(Speech.IsSpeaking ? MahApps.Metro.IconPacks.PackIconLucideKind.Square : MahApps.Metro.IconPacks.PackIconLucideKind.Volume2, 15));
        speak.ToolTip = Loc.T("نطق / إيقاف");
        speak.Click += (_, _) => Speech.Toggle(_speakText ?? _result.Text, _svc.Effective.TargetLanguage);
        Speech.StateChanged += RefreshSpeakIcon;
        Closed += (_, _) => { Speech.StateChanged -= RefreshSpeakIcon; if (Speech.IsSpeaking) Speech.Stop(); };
        Win.WatchMonitorLeave(this, Close);

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        if (s.ShowOriginalCard) stack.Children.Add(orig);
        stack.Children.Add(_result);
        stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { copy, speak } });

        Content = new Border
        {
            Background = new SolidColorBrush(bg) { Opacity = s.OverlayOpacity }, CornerRadius = new CornerRadius(s.CornerRadius),
            BorderBrush = accent, BorderThickness = new Thickness(1.5), Padding = new Thickness(14),
            Child = stack, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, Opacity = 0.4, ShadowDepth = 2 },
            Margin = new Thickness(10),
        };

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close(); // right-click cancels, same as Esc
        Deactivated += (_, _) => { if (IsVisible) Close(); };
        if (s.AutoCloseSeconds > 0)
        {
            _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(s.AutoCloseSeconds) };
            _timer.Tick += (_, _) => Close();
        }
        Loaded += (_, _) => PositionNearCursor();
    }

    private void PositionNearCursor()
    {
        var (cx, cy) = Screens.CursorPosition();
        var mon = Screens.UnderCursor();
        var scale = mon.Scale;
        var w = (int)(ActualWidth * scale); var h = (int)(ActualHeight * scale);
        var x = Math.Clamp(cx + 16, mon.X, mon.X + mon.Width - w);
        var y = cy + 20 + h > mon.Y + mon.Height ? cy - h - 16 : cy + 20;
        Win.MoveTopMost(this, x, Math.Max(mon.Y, y));
        Activate();
    }

    /// <param name="speakText">What the speaker reads when it differs from the displayed text (dictionary view reads only the main translation).</param>
    public void SetResult(string text, string? speakText = null)
    {
        _result.Text = text; _speakText = speakText;
        _timer?.Start();
        Dispatcher.BeginInvoke(PositionNearCursor);
    }

    public static PopupWindow ShowLoading(AppServices svc, string original)
    {
        var p = new PopupWindow(svc, original);
        p.Show();
        return p;
    }

    public static void ShowMessage(AppServices svc, string message)
    {
        var p = new PopupWindow(svc, "");
        p.Show();
        p.SetResult(message);
    }
}









