using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickTranslate.Core.Offline;
using QuickTranslate.Core.Settings;
using QuickTranslate.Core.Translation;
using QuickTranslate.Platform;

namespace QuickTranslate.App;

public sealed record Option<T>(string Label, T Value);

public sealed class HotkeyRow : INotifyPropertyChanged
{
    private HotkeyBinding _binding;
    public HotkeyAction Action { get; }
    public string Title => App.Describe(Action);
    public HotkeyBinding Binding { get => _binding; set { _binding = value; PropertyChanged?.Invoke(this, new(nameof(Display))); } }
    public string ResetLabel => Loc.T("افتراضي");
    public string Description => SettingHelp.ForHotkey(Action);
    public string Display => Binding.IsEmpty ? Loc.T("(بدون اختصار)") : Binding.ToString();
    public HotkeyRow(HotkeyAction a, HotkeyBinding b) { Action = a; _binding = b; }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class PackRow : INotifyPropertyChanged
{
    public LanguagePack Pack { get; }
    public string Title => Loc.English ? Pack.TitleEn : Pack.TitleAr;
    private bool _installed;
    public string Status => Describe(_installed);
    public Visibility InstalledVisibility => _installed ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MissingVisibility => _installed ? Visibility.Collapsed : Visibility.Visible;
    public PackRow(LanguagePack p, bool installed) { Pack = p; _installed = installed; }
    public void SetInstalled(bool installed)
    {
        _installed = installed;
        foreach (var n in new[] { nameof(Status), nameof(InstalledVisibility), nameof(MissingVisibility) }) PropertyChanged?.Invoke(this, new(n));
    }
    public static string Describe(bool installed) => installed ? Loc.T("مثبّت") : Loc.T("غير مثبّت");
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed class ProfileRow
{
    public AppProfile Profile { get; }
    public string Label =>
        $"{Profile.Process}   ·   {(string.IsNullOrEmpty(Profile.TargetLanguage) ? "—" : Profile.TargetLanguage)}   ·   {(Profile.FontSize is > 0 ? Profile.FontSize + "px" : "—")}";
    public ProfileRow(AppProfile p) => Profile = p;
}

public sealed class EngineRow : INotifyPropertyChanged
{
    public string Id { get; }
    public string Name { get; }
    public bool Enabled { get; set; }
    private string _status = "";
    public string Status { get => _status; set { _status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); } }
    public EngineRow(string id, string name, bool enabled, string status) { Id = id; Name = name; Enabled = enabled; _status = status; }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class SettingsWindow : Window
{
    private readonly AppServices _svc;
    private readonly List<HotkeyRow> _rows;
    private readonly List<EngineRow> _engines = new();
    private readonly List<PackRow> _packs;
    private readonly List<AppProfile> _profiles;
    private CancellationTokenSource? _packCts;

    public void SelectTab(int index) => SettingsTabs.SelectedIndex = Math.Clamp(index, 0, SettingsTabs.Items.Count - 1);

    // ---------- DeepL ----------
    private async Task TestDeepLAsync()
    {
        var key = DeepLKey.Password;
        if (string.IsNullOrWhiteSpace(key)) { DeepLStatus.Text = Loc.T("اكتب المفتاح أولاً (من حسابك في DeepL ← API Keys)."); return; }
        DeepLStatus.Text = Loc.T("جارٍ الفحص…"); DeepLTest.IsEnabled = false;
        try
        {
            var (used, limit) = await _svc.DeepL.UsageAsync(key);
            var pct = limit > 0 ? used * 100.0 / limit : 0;
            DeepLStatus.Text = $"{Loc.T("المفتاح يعمل")} — {used:N0} / {limit:N0} ({pct:0.#}%)";
        }
        catch (Exception ex)
        {
            DeepLStatus.Text = ex is Core.Translation.TranslationException { IsPermanent: true }
                ? Loc.T("المفتاح غير صحيح. تأكد أنه مفتاح API (ينتهي غالباً بـ :fx) وليس رقم الاشتراك.")
                : Loc.T("تعذّر الفحص: ") + ex.Message;
        }
        finally { DeepLTest.IsEnabled = true; }
    }

    // ---------- profiles ----------
    private void InitProfiles()
    {
        var langs = new List<Option<string>> { new(Loc.T("(افتراضي)"), "") };
        langs.AddRange(Languages.All.Select(l => new Option<string>(l.ToString(), l.Code)));
        ProfTarget.ItemsSource = langs;
        ProfSource.ItemsSource = new[] { new Option<string>(Loc.T("(افتراضي)"), "") }.Concat(Languages.WithAuto.Select(l => new Option<string>(l.ToString(), l.Code))).ToList();
        foreach (var box in new[] { ProfTarget, ProfSource }) { box.DisplayMemberPath = "Label"; box.SelectedValuePath = "Value"; box.SelectedIndex = 0; }

        try
        {
            ProfProcess.ItemsSource = Process.GetProcesses().Where(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; } })
                .Select(p => p.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
        }
        catch { }
        RefreshProfiles();
        ProfileList.SelectionChanged += (_, _) =>
        {
            if (ProfileList.SelectedItem is not ProfileRow r) return;
            var p = r.Profile;
            ProfProcess.Text = p.Process; ProfTarget.SelectedValue = p.TargetLanguage ?? ""; ProfSource.SelectedValue = p.SourceLanguage ?? "";
            ProfFontSize.Text = p.FontSize?.ToString() ?? ""; ProfTextColor.Text = p.TextColor ?? ""; ProfBg.Text = p.Background ?? "";
        };
        ProfSave.Click += (_, _) =>
        {
            var name = ProfProcess.Text.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            if (name.Length == 0) return;
            var existing = _profiles.FirstOrDefault(p => string.Equals(p.Process, name, StringComparison.OrdinalIgnoreCase));
            if (existing == null) { existing = new AppProfile { Process = name }; _profiles.Add(existing); }
            static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            existing.TargetLanguage = Blank(ProfTarget.SelectedValue as string); existing.SourceLanguage = Blank(ProfSource.SelectedValue as string);
            existing.FontSize = double.TryParse(ProfFontSize.Text, out var fs) && fs > 0 ? fs : null;
            existing.TextColor = Blank(ProfTextColor.Text); existing.Background = Blank(ProfBg.Text);
            RefreshProfiles();
        };
        ProfDelete.Click += (_, _) =>
        {
            if (ProfileList.SelectedItem is ProfileRow r) { _profiles.Remove(r.Profile); RefreshProfiles(); }
        };
    }

    private void RefreshProfiles() { ProfileList.ItemsSource = _profiles.Select(p => new ProfileRow(p)).ToList(); ProfileList.DisplayMemberPath = "Label"; }

    private void RefreshRegions()
    {
        RegionList.ItemsSource = _svc.Settings.LiveRegions.Select(kv =>
            new Option<string>($"{kv.Value.Process} — {kv.Value.Width}×{kv.Value.Height} @ {kv.Value.X},{kv.Value.Y}", kv.Key)).ToList();
        RegionList.DisplayMemberPath = "Label";
    }

    private async Task InstallPackAsync()
    {
        if (PackList.SelectedItem is not PackRow row) return;
        _packCts = new CancellationTokenSource();
        PackInstall.IsEnabled = false; PackCancel.IsEnabled = true; PackProgress.Value = 0;
        PackStatus.Text = Loc.T("جارٍ التنزيل…");
        try
        {
            await _svc.Models.InstallAsync(row.Pack, new Progress<double>(v => PackProgress.Value = v), _packCts.Token);
            PackStatus.Text = Loc.T("اكتمل التثبيت.");
        }
        catch (OperationCanceledException) { PackStatus.Text = Loc.T("أُلغي التنزيل."); }
        catch (Exception ex) { Log.Error("PackInstall", ex); PackStatus.Text = Loc.T("فشل التنزيل: ") + ex.Message; }
        finally
        {
            row.SetInstalled(_svc.Models.IsInstalled(row.Pack));
            PackInstall.IsEnabled = true; PackCancel.IsEnabled = false;
        }
    }

    public SettingsWindow(AppServices svc)
    {
        _svc = svc;
        InitializeComponent();
        Loc.Apply(this);
        var s = svc.Settings;

        // General
        UiLangBox.ItemsSource = new[] { new Option<string>("العربية", "ar"), new Option<string>("English", "en") };
        var trayOptions = new[]
        {
            new Option<TrayAction>(Loc.T("لا شيء"), TrayAction.None), new Option<TrayAction>(Loc.T("فتح النافذة"), TrayAction.OpenWindow),
            new Option<TrayAction>(Loc.T("فتح الإعدادات"), TrayAction.OpenSettings),
            new Option<TrayAction>(Loc.T("ترجمة منطقة من الشاشة"), TrayAction.TranslateRegion),
            new Option<TrayAction>(Loc.T("ترجمة النص تحت المؤشر"), TrayAction.TranslateUnderCursor),
            new Option<TrayAction>(Loc.T("ترجمة مباشرة لمنطقة"), TrayAction.LiveTranslate),
            new Option<TrayAction>(Loc.T("ترجمة النص المحدد"), TrayAction.TranslateSelection),
            new Option<TrayAction>(Loc.T("ترجمة الحافظة"), TrayAction.TranslateClipboard),
            new Option<TrayAction>(Loc.T("ترجمة صورة من الحافظة"), TrayAction.TranslateClipboardImage),
        };
        foreach (var box in new[] { TrayClickBox, TrayDoubleBox }) { box.ItemsSource = trayOptions; box.DisplayMemberPath = "Label"; box.SelectedValuePath = "Value"; }
        TrayClickBox.SelectedValue = s.TrayClickAction; TrayDoubleBox.SelectedValue = s.TrayDoubleClickAction;
        UiLangBox.DisplayMemberPath = "Label"; UiLangBox.SelectedValuePath = "Value"; UiLangBox.SelectedValue = s.UiLanguage;
        StartWithWindows.IsChecked = s.StartWithWindows; StartupDelay.Text = s.StartupDelaySeconds.ToString();
        StartMinimized.IsChecked = s.StartMinimized; ShowTray.IsChecked = s.ShowTrayIcon; CloseToTray.IsChecked = s.CloseToTray;
        RunAdmin.IsChecked = s.RunAsAdministrator; CheckUpdates.IsChecked = s.CheckForUpdates;
        if (UpdateChecker.Repo.Length == 0) { CheckUpdates.IsEnabled = false; CheckUpdates.ToolTip = Loc.T("غير متاح في هذه النسخة (لم يُحدَّد مستودع الإصدارات)"); }
        AutoClipboard.IsChecked = s.AutoTranslateClipboard; SaveHistory.IsChecked = s.SaveHistory;
        IgnoredProcs.Text = string.Join(", ", s.IgnoredProcesses);
        DataPathText.Text = Loc.T("مجلد البيانات: ") + svc.Store.DataDirectory;
        OpenDataBtn.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{svc.Store.DataDirectory}\"") { UseShellExecute = true });
        ClearCacheBtn.Click += (_, _) => { svc.Cache.Clear(); Dialogs.Info(Loc.T("ترجمان سريع"), Loc.T("تم مسح الكاش."), Loc.English ? "OK" : "حسناً", !Loc.English, this); };

        // Hotkeys
        _rows = s.Hotkeys.OrderBy(k => k.Key).Select(k => new HotkeyRow(k.Key, k.Value)).ToList();
        HotkeyList.ItemsSource = _rows;
        ResetAllHotkeys.Click += (_, _) => { var d = AppSettings.DefaultHotkeys(); foreach (var r in _rows) r.Binding = d[r.Action]; };
        ShowHotkeyProblems();

        // Translation
        TargetLang.ItemsSource = Languages.All; TargetLang.SelectedItem = Languages.Find(s.TargetLanguage);
        SourceLang.ItemsSource = Languages.WithAuto; SourceLang.SelectedItem = Languages.Find(s.SourceLanguage);
        SpeechBox.ItemsSource = new[]
        {
            new Option<string>(Loc.T("تلقائي (الأوضح: عبر الإنترنت ثم صوت ويندوز)"), "auto"),
            new Option<string>(Loc.T("عبر الإنترنت فقط (صوت Google)"), "online"),
            new Option<string>(Loc.T("صوت ويندوز المثبّت فقط"), "windows"),
        };
        SpeechBox.DisplayMemberPath = "Label"; SpeechBox.SelectedValuePath = "Value"; SpeechBox.SelectedValue = s.SpeechEngine;
        NotifyFallback.IsChecked = s.NotifyEngineFallback;
        DeepLTest.Click += async (_, _) => await TestDeepLAsync();
        DictionaryMode.IsChecked = s.DictionaryMode;
        ReplaceLang.ItemsSource = Languages.All; ReplaceLang.SelectedItem = Languages.Find(s.ReplaceTargetLanguage);
        RotateEngines.IsChecked = s.RotateEngines; UseCache.IsChecked = s.UseCache;
        BuildEngineList();
        EngineUp.Click += (_, _) => MoveEngine(-1);
        EngineDown.Click += (_, _) => MoveEngine(1);
        RefreshStatus.Click += (_, _) => RefreshEngineStatus();
        DeepLKey.Password = svc.GetKey("deepl"); AzureKey.Password = svc.GetKey("azure"); LibreKey.Password = svc.GetKey("libre");
        AzureRegion.Text = s.ApiKeys.GetValueOrDefault("azure-region", ""); LibreUrl.Text = s.LibreTranslateUrl;
        GlossaryBox.Text = string.Join("\n", s.Glossary.Select(g => $"{g.Key}={g.Value}"));

        // Appearance
        ThemeBox.ItemsSource = new[] { new Option<AppTheme>(Loc.T("حسب النظام"), AppTheme.System), new Option<AppTheme>(Loc.T("فاتح"), AppTheme.Light), new Option<AppTheme>(Loc.T("داكن"), AppTheme.Dark) };
        ThemeBox.DisplayMemberPath = "Label"; ThemeBox.SelectedValuePath = "Value"; ThemeBox.SelectedValue = s.Theme;
        AccentBox.Text = s.AccentColor;
        FontBox.ItemsSource = System.Windows.Media.Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(x => x).ToList(); FontBox.Text = s.OverlayFontFamily;
        FontSizeSlider.Value = s.OverlayFontSize; TextColorBox.Text = s.OverlayTextColor; BgColorBox.Text = s.OverlayBackground;
        OpacitySlider.Value = s.OverlayOpacity; DimSlider.Value = s.DimStrength; RadiusSlider.Value = s.CornerRadius;
        ShowOriginal.IsChecked = s.ShowOriginalCard; Animations.IsChecked = s.Animations; ImageMatchBackground.IsChecked = s.ImageMatchBackground; AutoCopy.IsChecked = s.AutoCopyTranslation;
        AutoClose.Text = s.AutoCloseSeconds.ToString();

        // OCR
        ModeBox.ItemsSource = new[] { new Option<Core.Settings.SelectionMode>(Loc.T("تحت المؤشر (ترجمة فورية للنص القريب)"), Core.Settings.SelectionMode.UnderCursor), new Option<Core.Settings.SelectionMode>(Loc.T("سحب مستطيل"), Core.Settings.SelectionMode.DragRectangle) };
        ModeBox.DisplayMemberPath = "Label"; ModeBox.SelectedValuePath = "Value"; ModeBox.SelectedValue = s.DefaultSelectionMode;
        var langs = new List<string> { "auto" }; langs.AddRange(WindowsOcr.AvailableLanguages());
        OcrLangBox.ItemsSource = langs.Select(l => new Option<string>(l == "auto" ? Loc.T("تلقائي (كل اللغات المثبّتة)") : l, l)).ToList();
        OcrLangBox.DisplayMemberPath = "Label"; OcrLangBox.SelectedValuePath = "Value";
        OcrLangBox.SelectedValue = langs.Contains(s.OcrLanguage) ? s.OcrLanguage : "auto";
        OcrInfo.Text = Loc.T("لغات OCR المثبّتة في ويندوز: ") + (langs.Count > 1 ? string.Join(Loc.T("، "), langs.Skip(1)) : Loc.T("لا يوجد — أضف حزمة لغة من إعدادات ويندوز")) +
                       Loc.T(". لقراءة لغة أخرى ثبّت حزمتها من: الإعدادات ← الوقت واللغة ← اللغة والمنطقة.");
        Upscale.IsChecked = s.UpscaleSmallCrops; MergeSlider.Value = s.ParagraphMergeSensitivity; LiveInterval.Text = s.LiveIntervalMs.ToString();

        // Per-program profiles
        _profiles = s.Profiles.Select(p => new AppProfile { Process = p.Process, TargetLanguage = p.TargetLanguage, SourceLanguage = p.SourceLanguage, FontSize = p.FontSize, TextColor = p.TextColor, Background = p.Background }).ToList();
        InitProfiles();

        // Saved live regions
        LiveReuse.IsChecked = s.LiveReuseSavedRegion;
        RefreshRegions();
        RegionDelete.Click += (_, _) =>
        {
            if (RegionList.SelectedItem is Option<string> r) { _svc.Settings.LiveRegions.Remove(r.Value); RefreshRegions(); }
        };
        RegionClear.Click += (_, _) => { _svc.Settings.LiveRegions.Clear(); RefreshRegions(); };

        // Offline packs
        PreferOffline.IsChecked = s.PreferOffline;
        _packs = ModelStore.Catalog.Select(p => new PackRow(p, svc.Models.IsInstalled(p))).ToList();
        PackList.ItemsSource = _packs;
        PackInstall.Click += async (_, _) => await InstallPackAsync();
        PackRemove.Click += (_, _) =>
        {
            if (PackList.SelectedItem is not PackRow r) return;
            _svc.Offline.Dispose();
            _svc.Models.Remove(r.Pack); r.SetInstalled(_svc.Models.IsInstalled(r.Pack));
        };
        PackCancel.Click += (_, _) => _packCts?.Cancel();

        SettingHelp.Apply(this);
        if (UpdateChecker.Repo.Length == 0)
        {
            CheckUpdates.ToolTip = SettingHelp.Make(Loc.T("غير متاح في هذه النسخة (لم يُحدَّد مستودع الإصدارات)"));
            ToolTipService.SetShowOnDisabled(CheckUpdates, true);
        }
        SaveBtn.Click += (_, _) => Save();
        CancelBtn.Click += (_, _) => Close();
    }

    // ---------- Hotkey recorder ----------
    private void Recorder_Focus(object sender, KeyboardFocusChangedEventArgs e) => ((TextBox)sender).Background = System.Windows.Media.Brushes.LightGoldenrodYellow;
    private void Recorder_Lost(object sender, KeyboardFocusChangedEventArgs e) => ((TextBox)sender).ClearValue(BackgroundProperty);

    private void Recorder_KeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var tb = (TextBox)sender;
        var row = (HotkeyRow)tb.Tag;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None) return;
        var mods = Keyboard.Modifiers;
        if (key == Key.Escape) { Keyboard.ClearFocus(); return; }
        if (key == Key.Delete && mods == ModifierKeys.None) { row.Binding = HotkeyBinding.None; ShowHotkeyProblems(); return; }
        var vk = KeyInterop.VirtualKeyFromKey(key);
        var isFKey = vk is >= 0x70 and <= 0x87;
        if (mods == ModifierKeys.None && !isFKey) { HotkeyWarn.Text = Loc.T("أضف Ctrl أو Alt أو Shift أو Win مع المفتاح."); return; }
        var m = HotkeyModifiers.None;
        if (mods.HasFlag(ModifierKeys.Control)) m |= HotkeyModifiers.Control;
        if (mods.HasFlag(ModifierKeys.Alt)) m |= HotkeyModifiers.Alt;
        if (mods.HasFlag(ModifierKeys.Shift)) m |= HotkeyModifiers.Shift;
        if (mods.HasFlag(ModifierKeys.Windows)) m |= HotkeyModifiers.Win;
        row.Binding = new HotkeyBinding(m, vk);
        ShowHotkeyProblems();
        Keyboard.ClearFocus();
    }

    private void ResetHotkey_Click(object sender, RoutedEventArgs e)
    {
        var row = (HotkeyRow)((Button)sender).Tag;
        row.Binding = AppSettings.DefaultHotkeys()[row.Action];
        ShowHotkeyProblems();
    }

    private void ShowHotkeyProblems()
    {
        var dup = _rows.Where(r => !r.Binding.IsEmpty).GroupBy(r => (r.Binding.Modifiers, r.Binding.VirtualKey)).Where(g => g.Count() > 1).ToList();
        HotkeyWarn.Text = dup.Count > 0 ? Loc.T("تنبيه: اختصار مكرر بين: ") + string.Join(" / ", dup.SelectMany(g => g.Select(r => r.Title))) : "";
    }

    // ---------- Engines ----------
    private void BuildEngineList()
    {
        var statuses = _svc.Pool.GetStatus().ToDictionary(x => x.Id);
        var order = _svc.Settings.EngineOrder.Concat(statuses.Keys.Except(_svc.Settings.EngineOrder)).Where(statuses.ContainsKey);
        foreach (var id in order)
        {
            var st = statuses[id];
            _engines.Add(new EngineRow(id, st.Name, !_svc.Settings.DisabledEngines.Contains(id), StatusText(st)));
        }
        EngineList.ItemsSource = _engines;
    }

    private static string StatusText(EngineStatus st) =>
        !st.Configured ? Loc.T("(يحتاج مفتاحاً/رابطاً)") : st.CoolingDown ? $"({Loc.T("مستريح")} {st.Remaining.TotalSeconds:0}s)" : $"({st.Requests} {Loc.T("طلب")}, {st.Failures} {Loc.T("فشل")})";

    private void RefreshEngineStatus()
    {
        var statuses = _svc.Pool.GetStatus().ToDictionary(x => x.Id);
        foreach (var e in _engines) if (statuses.TryGetValue(e.Id, out var st)) e.Status = StatusText(st);
    }

    private void MoveEngine(int delta)
    {
        var i = EngineList.SelectedIndex; var j = i + delta;
        if (i < 0 || j < 0 || j >= _engines.Count) return;
        (_engines[i], _engines[j]) = (_engines[j], _engines[i]);
        EngineList.ItemsSource = null; EngineList.ItemsSource = _engines; EngineList.SelectedIndex = j;
    }

    /// <summary>Asks for confirmation (in the language being switched to), then restarts the app.</summary>
    public static bool ConfirmRestart(string newLanguage)
    {
        var en = newLanguage == "en";
        var ok = Dialogs.Confirm(
            en ? "Restart required" : "إعادة التشغيل مطلوبة",
            en ? "The app will restart to switch the interface to English. Your settings are saved."
               : "سيُعاد تشغيل التطبيق لتحويل الواجهة إلى العربية. إعداداتك محفوظة.",
            en ? "Restart now" : "إعادة التشغيل الآن", en ? "Later" : "لاحقاً", rtl: !en);
        if (ok) App.Current.Restart();
        return ok;
    }
    // ---------- Save ----------
    private void Save()
    {
        var s = _svc.Settings;
        var startupChanged = s.StartWithWindows != (StartWithWindows.IsChecked == true) || int.TryParse(StartupDelay.Text, out var d0) && d0 != s.StartupDelaySeconds;

        s.StartWithWindows = StartWithWindows.IsChecked == true;
        s.StartupDelaySeconds = int.TryParse(StartupDelay.Text, out var d) ? Math.Clamp(d, 0, 600) : 0;
        var oldLang = s.UiLanguage;
        s.UiLanguage = UiLangBox.SelectedValue as string ?? "ar";
        s.TrayClickAction = TrayClickBox.SelectedValue is TrayAction tc ? tc : TrayAction.OpenWindow;
        s.TrayDoubleClickAction = TrayDoubleBox.SelectedValue is TrayAction td ? td : TrayAction.None;
        s.StartMinimized = StartMinimized.IsChecked == true; s.ShowTrayIcon = ShowTray.IsChecked == true;
        s.CloseToTray = CloseToTray.IsChecked == true; s.RunAsAdministrator = RunAdmin.IsChecked == true;
        s.CheckForUpdates = CheckUpdates.IsChecked == true; s.AutoTranslateClipboard = AutoClipboard.IsChecked == true;
        s.SaveHistory = SaveHistory.IsChecked == true;
        s.IgnoredProcesses = IgnoredProcs.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        s.Hotkeys = _rows.ToDictionary(r => r.Action, r => r.Binding);

        s.TargetLanguage = (TargetLang.SelectedItem as Language)?.Code ?? "ar";
        s.SourceLanguage = (SourceLang.SelectedItem as Language)?.Code ?? "auto";
        s.SpeechEngine = SpeechBox.SelectedValue as string ?? "auto";
        s.NotifyEngineFallback = NotifyFallback.IsChecked == true;
        s.Profiles = _profiles.ToList();
        s.DictionaryMode = DictionaryMode.IsChecked == true;
        s.ReplaceTargetLanguage = (ReplaceLang.SelectedItem as Language)?.Code ?? "en";
        s.PreferOffline = PreferOffline.IsChecked == true;
        s.RotateEngines = RotateEngines.IsChecked == true; s.UseCache = UseCache.IsChecked == true;
        s.EngineOrder = _engines.Select(e => e.Id).ToList();
        s.DisabledEngines = _engines.Where(e => !e.Enabled).Select(e => e.Id).ToList();
        _svc.SetKey("deepl", DeepLKey.Password); _svc.SetKey("azure", AzureKey.Password); _svc.SetKey("libre", LibreKey.Password);
        if (string.IsNullOrWhiteSpace(AzureRegion.Text)) s.ApiKeys.Remove("azure-region"); else s.ApiKeys["azure-region"] = AzureRegion.Text.Trim();
        s.LibreTranslateUrl = LibreUrl.Text.Trim();
        s.Glossary = GlossaryBox.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split('=', 2)).Where(p => p.Length == 2 && p[0].Trim().Length > 0)
            .GroupBy(p => p[0].Trim()).ToDictionary(g => g.Key, g => g.Last()[1].Trim());

        s.Theme = (AppTheme)ThemeBox.SelectedValue; s.AccentColor = AccentBox.Text.Trim();
        s.OverlayFontFamily = string.IsNullOrWhiteSpace(FontBox.Text) ? "Segoe UI" : FontBox.Text.Trim();
        s.OverlayFontSize = FontSizeSlider.Value; s.OverlayTextColor = TextColorBox.Text.Trim(); s.OverlayBackground = BgColorBox.Text.Trim();
        s.OverlayOpacity = OpacitySlider.Value; s.DimStrength = DimSlider.Value; s.CornerRadius = RadiusSlider.Value;
        s.ShowOriginalCard = ShowOriginal.IsChecked == true; s.Animations = Animations.IsChecked == true; s.ImageMatchBackground = ImageMatchBackground.IsChecked == true;
        s.AutoCopyTranslation = AutoCopy.IsChecked == true; s.AutoCloseSeconds = int.TryParse(AutoClose.Text, out var ac) ? Math.Max(0, ac) : 0;

        s.DefaultSelectionMode = (Core.Settings.SelectionMode)ModeBox.SelectedValue;
        s.OcrLanguage = OcrLangBox.SelectedValue as string ?? "auto";
        s.UpscaleSmallCrops = Upscale.IsChecked == true; s.ParagraphMergeSensitivity = MergeSlider.Value;
        s.LiveReuseSavedRegion = LiveReuse.IsChecked == true;
        s.LiveIntervalMs = int.TryParse(LiveInterval.Text, out var li) ? Math.Clamp(li, 300, 10000) : 1200;

        _svc.Store.Save(); // fires Changed -> hotkeys re-registered immediately, theme re-applied
        if (startupChanged) FirstRunHelper.EnsureLaunchTask(s);

        if (s.UiLanguage != oldLang && ConfirmRestart(s.UiLanguage)) return;
        var failed = App.Current.LastHotkeyResults.Where(r => !r.Success).ToList();
        if (failed.Count > 0)
            HotkeyWarn.Text = Loc.T("تعذّر تسجيل: ") + string.Join(Loc.T("، "), failed.Select(f => $"{App.Describe(f.Action)} ({f.Binding}) — {f.Error}"));
        else Close();
    }
}






















