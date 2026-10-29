using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using TarjumanSari3.Core.Settings;
using TarjumanSari3.Platform;

namespace TarjumanSari3.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private string _hotkeySignature = "";
    private NotifyIcon? _tray;
    private HotkeyManager? _hotkeys;
    private MainWindow? _main;
    private SettingsWindow? _settingsWindow;

    public static new App Current => (App)Application.Current;
    public AppServices Services { get; private set; } = null!;
    public ScreenTranslateFlow Flow { get; private set; } = null!;
    public IReadOnlyList<HotkeyRegistrationResult> LastHotkeyResults { get; private set; } = Array.Empty<HotkeyRegistrationResult>();
    public HotkeyManager Hotkeys => _hotkeys!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Installer hooks (run elevated by Setup): create / remove the no-UAC launch task, then exit.
        if (e.Args.Contains("--install-task") && Environment.ProcessPath is { } self)
        {
            LaunchTask.Create(self, new SettingsStore().Load().StartWithWindows, 0);
            Shutdown(); return;
        }
        if (e.Args.Contains("--remove-task")) { LaunchTask.Delete(); Shutdown(); return; }
        if (ElevationRelauncher.RelaunchIfNeeded()) { Shutdown(); return; }
        _mutex = new Mutex(false, @"Local\TarjumanSari3.SingleInstance");
        bool first;
        try { first = _mutex.WaitOne(e.Args.Contains("--restarted") ? 10000 : 0); }
        catch (AbandonedMutexException) { first = true; }
        // Second launch: ask the running copy to show its window, then quit.
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\TarjumanSari3.ShowWindow");
        if (!first) { try { _showEvent.Set(); } catch { } Shutdown(); return; }
        new Thread(() =>
        {
            while (!_exiting) { _showEvent.WaitOne(); if (_exiting) break; Dispatcher.BeginInvoke(ShowMain); }
        }) { IsBackground = true, Name = "ShowWindowListener" }.Start();

        DispatcherUnhandledException += (_, a) => { Log.Error("UI", a.Exception); a.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log.Error("Fatal", a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error("Task", a.Exception); a.SetObserved(); };

        AppIcon.RegisterForAllWindows();
        Services = new AppServices();
        Loc.Init(Services.Settings.UiLanguage);
        Flow = new ScreenTranslateFlow(Services);
        ApplyTheme();
        Services.Store.Changed += _ => Dispatcher.Invoke(() => { ApplyTheme(); ApplyHotkeys(); UpdateTray(); });
        Log.Info("App", $"Started v{typeof(App).Assembly.GetName().Version} · admin={Elevation.IsAdministrator()} · lang={Services.Settings.UiLanguage} · OCR=[{string.Join(",", WindowsOcr.AvailableLanguages())}]");

        _hotkeys = new HotkeyManager();
        _hotkeys.HotkeyPressed += a => _ = HandleHotkeyAsync(a);
        ApplyHotkeys();
        CreateTray();
        if (!Services.Settings.FirstRunDone)
        {
            Services.Settings.FirstRunDone = true; Services.Store.Save();
            var hk = Services.Settings.Hotkeys;
            _tray?.ShowBalloonTip(9000, Loc.T("ترجمان سريع"),
                $"{Loc.T("ترجمة منطقة")}: {hk[HotkeyAction.TranslateRegion]}  •  {Loc.T("تحت المؤشر")}: {hk[HotkeyAction.TranslateUnderCursor]}  •  {Loc.T("النص المحدد")}: {hk[HotkeyAction.TranslateSelection]}",
                ToolTipIcon.Info);
        }

        var fromTask = e.Args.Contains("--from-task");
        if (Services.Settings.StartMinimized && fromTask || e.Args.Contains("--minimized")) { }
        else ShowMain();
        foreach (var a in e.Args)
        {
            if (a.StartsWith("--image=")) ImageTranslateWindow.ShowPath(Services, a[8..]);
            if (a.StartsWith("--main-tab=") && int.TryParse(a[11..], out var mt)) _main?.SelectTab(mt);
            if (a.StartsWith("--settings=") && int.TryParse(a[11..], out var st)) { ShowSettings(); _settingsWindow?.SelectTab(st); }
        }

        ClipboardWatcher.Start(Services, this);
        FirstRunHelper.EnsureLaunchTask(Services.Settings);
        if (Services.Settings.CheckForUpdates) _ = UpdateChecker.CheckAsync(this);
    }

    public void ApplyTheme()
    {
        ThemeMode = Services.Settings.Theme switch
        {
            AppTheme.Light => ThemeMode.Light,
            AppTheme.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }

    private void ApplyHotkeys()
    {
        var signature = string.Join(";", Services.Settings.Hotkeys.OrderBy(k => k.Key).Select(k => $"{k.Key}:{(int)k.Value.Modifiers}:{k.Value.VirtualKey}"));
        if (signature == _hotkeySignature) return; // unrelated setting changed: keep the registrations (and avoid repeated warnings)
        _hotkeySignature = signature;
        LastHotkeyResults = _hotkeys!.Apply(Services.Settings.Hotkeys);
        foreach (var r in LastHotkeyResults.Where(r => !r.Success)) Log.Warn("Hotkeys", $"{Describe(r.Action)} ({r.Binding}): {r.Error}");
        var failed = LastHotkeyResults.Where(r => !r.Success).ToList();
        if (failed.Count > 0 && _tray != null)
        {
            _tray.ShowBalloonTip(5000, Loc.T("ترجمان سريع"),
                Loc.T("تعذّر تسجيل بعض الاختصارات: ") + string.Join(Loc.T("، "), failed.Select(f => $"{Describe(f.Action)} ({f.Binding})")) + Loc.T(". غيّرها من الإعدادات."),
                ToolTipIcon.Warning);
        }
    }

    private static bool OwnProcess(string? name) => string.Equals(name, "TarjumanSari3", StringComparison.OrdinalIgnoreCase);

    public static string Describe(HotkeyAction a) => a switch
    {
        HotkeyAction.TranslateRegion => Loc.T("ترجمة منطقة من الشاشة"),
        HotkeyAction.TranslateUnderCursor => Loc.T("ترجمة النص تحت المؤشر"),
        HotkeyAction.RepeatLastRegion => Loc.T("إعادة ترجمة آخر منطقة"),
        HotkeyAction.TranslateSelection => Loc.T("ترجمة النص المحدد"),
        HotkeyAction.TranslateClipboard => Loc.T("ترجمة الحافظة"),
        HotkeyAction.ToggleMainWindow => Loc.T("إظهار/إخفاء النافذة"),
        HotkeyAction.LiveTranslate => Loc.T("ترجمة مباشرة لمنطقة (تشغيل/إيقاف)"),
        HotkeyAction.LiveTranslatePickNew => Loc.T("ترجمة مباشرة بمنطقة جديدة"),
        HotkeyAction.TranslateClipboardImage => Loc.T("ترجمة صورة من الحافظة"),
        HotkeyAction.ReplaceSelection => Loc.T("استبدال النص المحدد بترجمته"),
        _ => a.ToString(),
    };

    private async Task HandleHotkeyAsync(HotkeyAction action)
    {
        try
        {
            var fg = SelectionGrabber.ForegroundProcessName();
            Services.SetActiveProfile(OwnProcess(fg) ? null : fg);
            if (Services.Settings.IgnoredProcesses.Contains(fg ?? "?", StringComparer.OrdinalIgnoreCase)
                && action != HotkeyAction.ToggleMainWindow) return;
            switch (action)
            {
                case HotkeyAction.TranslateRegion: await Flow.StartAsync(SelectionMode.DragRectangle); break;
                case HotkeyAction.TranslateUnderCursor: await Flow.StartAsync(SelectionMode.UnderCursor); break;
                case HotkeyAction.RepeatLastRegion: await Flow.RepeatAsync(); break;
                case HotkeyAction.LiveTranslate: Flow.ToggleLive(OwnProcess(fg) ? null : fg, forcePick: false); break;
                case HotkeyAction.LiveTranslatePickNew: Flow.ToggleLive(OwnProcess(fg) ? null : fg, forcePick: true); break;
                case HotkeyAction.ReplaceSelection: await TextActions.ReplaceSelectionAsync(Services); break;
                case HotkeyAction.TranslateClipboardImage: ImageTranslateWindow.ShowFromClipboard(Services); break;
                case HotkeyAction.TranslateSelection: await TextActions.TranslateSelectionAsync(Services); break;
                case HotkeyAction.TranslateClipboard: await TextActions.TranslateClipboardAsync(Services); break;
                case HotkeyAction.ToggleMainWindow:
                    if (_main is { IsVisible: true, IsActive: true }) _main.Hide(); else ShowMain();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Hotkey " + action, ex);
            PopupWindow.ShowMessage(Services, Loc.T("تعذّرت العملية: ") + ex.Message);
        }
    }

    public void ShowMain()
    {
        if (_main == null)
        {
            _main = new MainWindow(Services);
            _main.Closing += (_, a) =>
            {
                if (!_exiting && Services.Settings.CloseToTray && Services.Settings.ShowTrayIcon) { a.Cancel = true; _main!.Hide(); }
                else if (!_exiting) ExitApp();
            };
        }
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    public void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true }) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(Services);
        _settingsWindow.Show();
    }

    private System.Windows.Threading.DispatcherTimer? _clickTimer;
    private string? _balloonUrl;

    public void NotifyInfo(string text)
    {
        _balloonUrl = null;
        _tray?.ShowBalloonTip(6000, Loc.T("ترجمان سريع"), text, ToolTipIcon.Warning);
    }

    /// <summary>Runs the action the user assigned to clicking the tray icon.</summary>
    private void RunTrayAction(TrayAction action)
    {
        switch (action)
        {
            case TrayAction.OpenWindow: ShowMain(); break;
            case TrayAction.OpenSettings: ShowSettings(); break;
            case TrayAction.TranslateRegion: _ = HandleHotkeyAsync(HotkeyAction.TranslateRegion); break;
            case TrayAction.TranslateUnderCursor: _ = HandleHotkeyAsync(HotkeyAction.TranslateUnderCursor); break;
            case TrayAction.LiveTranslate: _ = HandleHotkeyAsync(HotkeyAction.LiveTranslate); break;
            case TrayAction.TranslateSelection: _ = HandleHotkeyAsync(HotkeyAction.TranslateSelection); break;
            case TrayAction.TranslateClipboard: _ = HandleHotkeyAsync(HotkeyAction.TranslateClipboard); break;
            case TrayAction.TranslateClipboardImage: _ = HandleHotkeyAsync(HotkeyAction.TranslateClipboardImage); break;
        }
    }

    /// <summary>Starts a fresh copy of the app and exits this one (used after changing the interface language).</summary>
    public void Restart()
    {
        var exe = Environment.ProcessPath;
        if (exe != null)
            try { Process.Start(new ProcessStartInfo(exe, "--restarted") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("Restart", ex); return; }
        ExitApp();
    }
    public void NotifyUpdate(string text, string url)
    {
        _balloonUrl = url;
        _tray?.ShowBalloonTip(8000, Loc.T("ترجمان سريع"), text, ToolTipIcon.Info);
    }

    private bool _exiting;
    public void ExitApp()
    {
        _exiting = true;
        _tray?.Dispose();
        _hotkeys?.Dispose();
        Services.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { }
        Shutdown();
    }

    private void CreateTray()
    {
        _tray = new NotifyIcon { Icon = AppIcon.Create(), Text = Loc.T("ترجمان سريع") };
        var menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes };
        menu.Items.Add(Loc.T("ترجمة منطقة من الشاشة"), null, (_, _) => _ = HandleHotkeyAsync(HotkeyAction.TranslateRegion));
        menu.Items.Add(Loc.T("ترجمة النص تحت المؤشر"), null, (_, _) => _ = HandleHotkeyAsync(HotkeyAction.TranslateUnderCursor));
        menu.Items.Add(Loc.T("ترجمة مباشرة لمنطقة"), null, (_, _) => _ = HandleHotkeyAsync(HotkeyAction.LiveTranslate));
        menu.Items.Add(Loc.T("ترجمة صورة من الحافظة"), null, (_, _) => _ = HandleHotkeyAsync(HotkeyAction.TranslateClipboardImage));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Loc.T("فتح النافذة"), null, (_, _) => ShowMain());
        menu.Items.Add(Loc.T("الإعدادات"), null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Loc.T("خروج"), null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, ev) =>
        {
            if (ev.Button != System.Windows.Forms.MouseButtons.Left) return;
            var dbl = Services.Settings.TrayDoubleClickAction;
            if (dbl == TrayAction.None) { Dispatcher.BeginInvoke(() => RunTrayAction(Services.Settings.TrayClickAction)); return; }
            // wait one double-click interval so a double click does not also fire the single-click action
            _clickTimer?.Stop();
            _clickTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime) };
            _clickTimer.Tick += (_, _) => { _clickTimer?.Stop(); RunTrayAction(Services.Settings.TrayClickAction); };
            _clickTimer.Start();
        };
        _tray.MouseDoubleClick += (_, ev) =>
        {
            if (ev.Button != System.Windows.Forms.MouseButtons.Left) return;
            _clickTimer?.Stop();
            RunTrayAction(Services.Settings.TrayDoubleClickAction);
        };
        _tray.BalloonTipClicked += (_, _) => { if (_balloonUrl != null) try { Process.Start(new ProcessStartInfo(_balloonUrl) { UseShellExecute = true }); } catch { } };
        UpdateTray();
    }

    private void UpdateTray()
    {
        if (_tray == null) return;
        _tray.Visible = Services.Settings.ShowTrayIcon;
        var tip = $"{Loc.T("ترجمان سريع")} • {Services.Settings.Hotkeys[HotkeyAction.TranslateRegion]}";
        _tray.Text = tip.Length > 63 ? tip[..63] : tip;
    }
}

internal static class AppIcon
{
    private static readonly Uri IcoUri = new("pack://application:,,,/Assets/app.ico");

    /// <summary>Tray icon, picked at the size Windows asks for.</summary>
    public static Icon Create()
    {
        var res = Application.GetResourceStream(IcoUri)!;
        using var stream = res.Stream;
        return new Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
    }

    /// <summary>Gives every window the app icon (title bar and Alt-Tab).</summary>
    public static void RegisterForAllWindows()
    {
        var frame = System.Windows.Media.Imaging.BitmapFrame.Create(IcoUri);
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
        {
            if (s is Window w && w.Icon == null) w.Icon = frame;
        }));
    }
}




