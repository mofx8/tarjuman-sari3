using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MahApps.Metro.IconPacks;
using TarjumanSari3.Core.Storage;
using TarjumanSari3.Core.Translation;

namespace TarjumanSari3.App;

/// <summary>One row of the history list (a history item plus the text shown under it).</summary>
public sealed class HistoryRow
{
    public HistoryItem Item { get; }
    public string Source => Item.Source;
    public string Translation => Item.Translation;
    public string Meta { get; }
    public Visibility StarVisibility => Item.Favorite ? Visibility.Visible : Visibility.Collapsed;

    internal HistoryRow(HistoryItem item, string engineName, string kindName)
    {
        Item = item;
        var parts = new List<string> { kindName };
        if (engineName.Length > 0 && engineName != "-") parts.Add(engineName);
        parts.Add($"{item.From}→{item.To}");
        parts.Add(item.Time.ToString("HH:mm  dd/MM/yyyy"));
        Meta = string.Join("  ·  ", parts);
    }
}

public partial class MainWindow : Window
{
    private readonly AppServices _svc;
    private CancellationTokenSource _cts = new();
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(2500) };
    private readonly ObservableCollection<LogEntry> _logItems = new();
    private (string Text, string Translation, string From, string To, string Engine)? _pending;
    private string _lastSource = "", _plain = "";
    private bool _syncing;

    public MainWindow(AppServices svc)
    {
        _svc = svc;
        InitializeComponent();
        Loc.Apply(this);

        SourceBox.ItemsSource = Languages.WithAuto;
        TargetBox.ItemsSource = Languages.All;
        SourceBox.SelectedItem = Languages.Find(svc.Settings.SourceLanguage);
        TargetBox.SelectedItem = Languages.Find(svc.Settings.TargetLanguage);

        // ---- translate tab
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await TranslateNowAsync(explicitRequest: false); };
        _saveTimer.Tick += (_, _) => FlushPendingHistory();
        InputBox.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        InputBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { _debounce.Stop(); await TranslateNowAsync(true); } };
        TranslateBtn.Click += async (_, _) => { _debounce.Stop(); await TranslateNowAsync(true); };
        ClearBtn.Click += (_, _) => { InputBox.Clear(); OutputBox.Clear(); };
        PasteBtn.Click += (_, _) => { if (Clipboard.ContainsText()) InputBox.Text = Clipboard.GetText(); };
        CopyBtn.Click += (_, _) => { if (OutputBox.Text.Length > 0) Clipboard.SetText(OutputBox.Text); };
        SpeakBtn.Click += (_, _) => Speech.Toggle(_plain.Length > 0 ? _plain : OutputBox.Text, (TargetBox.SelectedItem as Language)?.Code ?? "ar");
        Speech.StateChanged += () => Dispatcher.BeginInvoke(RefreshSpeakButton);
        CompareBtn.Click += (_, _) =>
        {
            var src = (SourceBox.SelectedItem as Language)?.Code ?? "auto"; var tgt = (TargetBox.SelectedItem as Language)?.Code ?? "ar";
            new CompareWindow(_svc, InputBox.Text, src, tgt, t => { OutputBox.Text = t; _plain = t; }) { Owner = this }.Show();
        };
        SaveFavBtn.Click += (_, _) => AddCurrentToFavorites();
        ImgClipBtn.Click += (_, _) => ImageTranslateWindow.ShowFromClipboard(_svc);
        ImgFileBtn.Click += (_, _) => ImageTranslateWindow.ShowFromFile(_svc, this);
        SwapBtn.Click += (_, _) => Swap();
        SettingsBtn.Click += (_, _) => App.Current.ShowSettings();
        PinBtn.Click += (_, _) => Topmost = PinBtn.IsChecked == true;
        SourceBox.SelectionChanged += (_, _) => { if (!_syncing && SourceBox.SelectedItem is Language l) { _svc.Settings.SourceLanguage = l.Code; _svc.Store.Save(); } };
        TargetBox.SelectionChanged += (_, _) => { if (!_syncing && TargetBox.SelectedItem is Language l) { _svc.Settings.TargetLanguage = l.Code; _svc.Store.Save(); } };

        // Press Ctrl+V with an image on the clipboard to translate it.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control) return;
            try { if (!Clipboard.ContainsText() && Clipboard.ContainsImage()) { e.Handled = true; ImageTranslateWindow.ShowFromClipboard(_svc); } } catch { }
        };

        // ---- language switch button: always names the language you would switch TO
        LangBtn.Content = Ui.IconText(PackIconLucideKind.Globe, Loc.English ? "العربية" : "English");
        LangBtn.ToolTip = Loc.English ? "Switch the interface language to Arabic" : "تغيير لغة الواجهة إلى الإنجليزية (English)";
        LangBtn.Click += (_, _) =>
        {
            var next = Loc.English ? "ar" : "en";
            _svc.Settings.UiLanguage = next; _svc.Store.Save();
            if (!SettingsWindow.ConfirmRestart(next)) { _svc.Settings.UiLanguage = Loc.English ? "en" : "ar"; _svc.Store.Save(); }
        };

        // ---- history tab
        SearchBox.TextChanged += (_, _) => RefreshHistory();
        FavOnly.Click += (_, _) => RefreshHistory();
        HistoryList.MouseDoubleClick += (_, _) => UseSelected();
        HistUseBtn.Click += (_, _) => UseSelected();
        HistFavBtn.Click += (_, _) =>
        {
            if (HistoryList.SelectedItem is not HistoryRow r) return;
            _svc.History.SetFavorite(r.Item.Id, !r.Item.Favorite); RefreshHistory();
        };
        HistDelBtn.Click += (_, _) => { if (HistoryList.SelectedItem is HistoryRow r) { _svc.History.Delete(r.Item.Id); RefreshHistory(); } };
        HistClearBtn.Click += (_, _) =>
        {
            if (Dialogs.Confirm(Loc.T("مسح السجل"), Loc.T("مسح السجل (المفضلة تبقى)؟"), Loc.English ? "Clear" : "مسح", Loc.English ? "Cancel" : "إلغاء", !Loc.English, this))
            { _svc.History.Clear(); RefreshHistory(); }
        };
        HistExportBtn.Click += (_, _) => ExportHistory();

        // ---- log tab
        LogFilter.ItemsSource = new[]
        {
            new Option<int>(Loc.T("كل الرسائل"), 0), new Option<int>(Loc.T("التحذيرات والأخطاء"), 1), new Option<int>(Loc.T("الأخطاء فقط"), 2),
        };
        LogFilter.DisplayMemberPath = "Label"; LogFilter.SelectedValuePath = "Value"; LogFilter.SelectedValue = 0;
        LogList.ItemsSource = _logItems;
        LogFilter.SelectionChanged += (_, _) => RefreshLog();
        LogClearBtn.Click += (_, _) => Log.Clear();
        LogCopyBtn.Click += (_, _) =>
        {
            var text = string.Join("\n", _logItems.Select(l => $"[{l.TimeText}] {l.LevelText} {l.Source}: {l.Message}"));
            if (text.Length > 0) try { Clipboard.SetText(text); } catch { }
        };
        LogFolderBtn.Click += (_, _) =>
        {
            Directory.CreateDirectory(Log.Directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.Directory}\"") { UseShellExecute = true });
        };
        Log.Added += e => Dispatcher.BeginInvoke(() => OnLogAdded(e));
        Log.Cleared += () => Dispatcher.BeginInvoke(() => _logItems.Clear());
        RefreshLog();

        Tabs.SelectionChanged += (_, _) => { if (Tabs.SelectedIndex == 1) RefreshHistory(); };
        svc.Store.Changed += _ => Dispatcher.BeginInvoke(SyncFromSettings);
        Closed += (_, _) => FlushPendingHistory();
        UpdateHotkeyHint();
    }

    public void SelectTab(int index) => Tabs.SelectedIndex = Math.Clamp(index, 0, Tabs.Items.Count - 1);

    private void RefreshSpeakButton() =>
        SpeakBtn.Content = Speech.IsSpeaking ? Ui.IconText(PackIconLucideKind.Square, Loc.T("إيقاف"), 15) : Ui.IconText(PackIconLucideKind.Volume2, Loc.T("نطق"), 15);

    // ---------- settings sync ----------
    private void SyncFromSettings()
    {
        _syncing = true;
        try
        {
            SourceBox.SelectedItem = Languages.Find(_svc.Settings.SourceLanguage);
            TargetBox.SelectedItem = Languages.Find(_svc.Settings.TargetLanguage);
            UpdateHotkeyHint();
        }
        finally { _syncing = false; }
    }

    private void UpdateHotkeyHint()
    {
        var h = _svc.Settings.Hotkeys;
        StatusText.Text = $"{Loc.T("ترجمة منطقة")}: {h[Core.Settings.HotkeyAction.TranslateRegion]}   •   {Loc.T("تحت المؤشر")}: {h[Core.Settings.HotkeyAction.TranslateUnderCursor]}   •   {Loc.T("النص المحدد")}: {h[Core.Settings.HotkeyAction.TranslateSelection]}";
    }

    private void Swap()
    {
        if (SourceBox.SelectedItem is Language s && TargetBox.SelectedItem is Language t && s.Code != "auto")
        {
            SourceBox.SelectedItem = t; TargetBox.SelectedItem = s;
            var txt = OutputBox.Text; OutputBox.Text = InputBox.Text; InputBox.Text = txt;
        }
    }

    // ---------- translate ----------
    private async Task TranslateNowAsync(bool explicitRequest)
    {
        var text = InputBox.Text.Trim();
        if (text.Length == 0) { OutputBox.Clear(); return; }
        _cts.Cancel(); _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        StatusText.Text = Loc.T("جارٍ الترجمة…");
        try
        {
            var src = (SourceBox.SelectedItem as Language)?.Code ?? "auto";
            var tgt = (TargetBox.SelectedItem as Language)?.Code ?? "ar";
            var r = await _svc.TranslateAsync(text, src, tgt, ct, saveHistory: false);
            var shown = r.Text; var engine = r.EngineId; _plain = r.Text;
            if (_svc.Settings.DictionaryMode && DictionaryLookup.IsSingleWord(text) && await _svc.LookupWordAsync(text, ct) is { } dict && !ct.IsCancellationRequested)
            { shown = dict.Formatted; _plain = dict.Translation; engine = "dictionary"; }
            if (ct.IsCancellationRequested) return;

            OutputBox.Text = shown;
            OutputBox.FlowDirection = tgt is "ar" or "fa" or "ur" or "he" ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;
            _lastSource = text;
            var detected = Loc.English ? Languages.Find(r.DetectedSource).English : Languages.Find(r.DetectedSource).Arabic;
            StatusText.Text = $"{Loc.T("اللغة المكتشفة")}: {detected}  •  {Loc.T("المحرك")}: {_svc.EngineName(engine)}{(r.FromCache ? Loc.T(" (من الكاش)") : "")}";
            if (_svc.Settings.AutoCopyTranslation) try { Clipboard.SetText(r.Text); } catch { }

            // History: save at once for an explicit request, otherwise once the text has been stable for a moment.
            _pending = (text, r.Text, r.DetectedSource, tgt, engine);
            if (explicitRequest) FlushPendingHistory(); else { _saveTimer.Stop(); _saveTimer.Start(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusText.Text = Loc.T("تعذّرت الترجمة: ") + ex.Message; }
    }

    private void FlushPendingHistory()
    {
        _saveTimer.Stop();
        if (_pending is not { } p) return;
        _pending = null;
        if (_svc.Settings.SaveHistory && p.Engine != "none") _svc.History.Add(p.Text, p.Translation, p.From, p.To, p.Engine, p.Engine == "dictionary" ? "dictionary" : "text");
    }

    private void AddCurrentToFavorites()
    {
        FlushPendingHistory();
        if (_lastSource.Length == 0) return;
        var item = _svc.History.Search(_lastSource, false, 10).FirstOrDefault(h => h.Source == _lastSource);
        if (item == null)
        {
            _svc.History.Add(_lastSource, OutputBox.Text, (SourceBox.SelectedItem as Language)?.Code ?? "auto", (TargetBox.SelectedItem as Language)?.Code ?? "ar");
            item = _svc.History.Search(_lastSource, false, 10).FirstOrDefault(h => h.Source == _lastSource);
        }
        if (item == null) return;
        _svc.History.SetFavorite(item.Id, true);
        StatusText.Text = Loc.T("أُضيفت إلى المفضلة");
    }

    // ---------- history ----------
    private void RefreshHistory() =>
        HistoryList.ItemsSource = _svc.History.Search(SearchBox.Text, FavOnly.IsChecked == true, 1000)
            .Select(h => new HistoryRow(h, _svc.EngineName(h.Engine), AppServices.KindName(h.Kind))).ToList();

    private void UseSelected()
    {
        if (HistoryList.SelectedItem is not HistoryRow r) return;
        Tabs.SelectedIndex = 0;
        _debounce.Stop();
        InputBox.Text = r.Source; OutputBox.Text = r.Translation; _plain = r.Translation;
        _debounce.Stop(); // showing a saved translation must not trigger a new request
    }

    private void ExportHistory()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV|*.csv", FileName = $"tarjuman-sari3-history-{DateTime.Now:yyyyMMdd}.csv" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            static string Csv(string s) => "\"" + s.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
            var sb = new StringBuilder("time,from,to,engine,favorite,source,translation\r\n");
            foreach (var h in _svc.History.Search("", false, 100000))
                sb.Append($"{h.Time:yyyy-MM-dd HH:mm:ss},{h.From},{h.To},{Csv(_svc.EngineName(h.Engine))},{(h.Favorite ? 1 : 0)},{Csv(h.Source)},{Csv(h.Translation)}\r\n");
            File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
            StatusText.Text = Loc.T("تم التصدير.");
        }
        catch (Exception ex) { Log.Error("Export", ex); StatusText.Text = Loc.T("تعذّر التصدير: ") + ex.Message; }
    }

    // ---------- log ----------
    private bool Passes(LogEntry e) => (LogFilter.SelectedValue as int? ?? 0) switch
    {
        1 => e.Level >= LogLevel.Warning,
        2 => e.Level == LogLevel.Error,
        _ => true,
    };

    private void RefreshLog()
    {
        _logItems.Clear();
        foreach (var e in Log.Snapshot().Where(Passes)) _logItems.Add(e);
        ScrollLogToEnd();
    }

    private void OnLogAdded(LogEntry e)
    {
        if (!Passes(e)) return;
        _logItems.Add(e);
        while (_logItems.Count > 1000) _logItems.RemoveAt(0);
        ScrollLogToEnd();
    }

    private void ScrollLogToEnd()
    {
        if (LogAutoScroll.IsChecked == true && _logItems.Count > 0 && IsVisible) LogList.ScrollIntoView(_logItems[^1]);
    }
}



