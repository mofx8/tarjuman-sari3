using System.Diagnostics;
using System.Windows;
using TarjumanSari3.Core.Settings;
using TarjumanSari3.Core.Translation;
using TarjumanSari3.Platform;

namespace TarjumanSari3.App;

internal enum LogLevel { Info, Warning, Error }

internal sealed record LogEntry(DateTime Time, LogLevel Level, string Source, string Message)
{
    public string TimeText => Time.ToString("HH:mm:ss");
    public string LevelText => Level switch { LogLevel.Error => Loc.T("خطأ"), LogLevel.Warning => Loc.T("تحذير"), _ => Loc.T("معلومة") };
    public System.Windows.Media.Brush LevelBrush => Level switch
    {
        LogLevel.Error => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)),
        LogLevel.Warning => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11)),
        _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246)),
    };
}

/// <summary>In-memory ring buffer (shown in the Log tab) that also appends to a daily file under %LocalAppData%.</summary>
internal static class Log
{
    private const int Capacity = 1000;
    private static readonly object Gate = new();
    private static readonly List<LogEntry> Entries = new();
    public static readonly string Directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TarjumanSari3", "logs");

    public static event Action<LogEntry>? Added;
    public static event Action? Cleared;

    public static void Info(string source, string message) => Write(LogLevel.Info, source, message, null);
    public static void Warn(string source, string message) => Write(LogLevel.Warning, source, message, null);
    public static void Error(string source, string message) => Write(LogLevel.Error, source, message, null);
    public static void Error(string source, Exception? ex) => Write(LogLevel.Error, source, ex == null ? "" : $"{ex.GetType().Name}: {ex.Message}", ex);

    /// <summary>Entry point for components that report a level as text (the translation pool).</summary>
    public static void FromText(string level, string source, string message) =>
        Write(level switch { "error" => LogLevel.Error, "warn" => LogLevel.Warning, _ => LogLevel.Info }, source, message, null);

    public static IReadOnlyList<LogEntry> Snapshot() { lock (Gate) return Entries.ToList(); }

    public static void Clear()
    {
        lock (Gate) Entries.Clear();
        Cleared?.Invoke();
    }

    private static void Write(LogLevel level, string source, string message, Exception? ex)
    {
        var entry = new LogEntry(DateTime.Now, level, source, message);
        lock (Gate)
        {
            Entries.Add(entry);
            if (Entries.Count > Capacity) Entries.RemoveRange(0, Entries.Count - Capacity);
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                var file = Path.Combine(Directory, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                File.AppendAllText(file, $"[{entry.TimeText}] {level.ToString().ToUpperInvariant()} {source}: {(ex?.ToString() ?? message)}\n");
            }
            catch { /* logging must never throw */ }
        }
        try { Added?.Invoke(entry); } catch { }
    }
}
internal static class ElevationRelauncher
{
    /// <summary>
    /// When "run as administrator" is on and we are not elevated, start through the highest-privilege
    /// scheduled task (no UAC prompt). Without the task, ask for UAC once; the elevated copy then creates the task.
    /// </summary>
    public static bool RelaunchIfNeeded()
    {
        try
        {
            if (Elevation.IsAdministrator()) return false;
            if (!new SettingsStore().Load().RunAsAdministrator) return false;
            var exe = Environment.ProcessPath;
            if (exe == null) return false;

            if (LaunchTask.Exists())
            {
                var r = Process.Start(new ProcessStartInfo("schtasks.exe", "/Run /TN " + LaunchTask.Name) { CreateNoWindow = true, UseShellExecute = false });
                r?.WaitForExit(5000);
                if (r?.ExitCode == 0) return true;
            }
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; } // UAC declined: run without elevation
        catch (Exception ex) { Log.Error("Elevate", ex); return false; }
    }
}

internal static class FirstRunHelper
{
    /// <summary>Keeps the elevated scheduled task in sync with the "start with Windows" option.</summary>
    public static void EnsureLaunchTask(AppSettings s)
    {
        if (!Elevation.IsAdministrator()) return;
        var exe = Environment.ProcessPath;
        if (exe == null) return;
        Task.Run(() =>
        {
            var (ok, output) = LaunchTask.Create(exe, s.StartWithWindows, s.StartupDelaySeconds);
            if (!ok) Log.Error("LaunchTask", new Exception(output));
        });
    }
}

/// <summary>Selection / clipboard translation, in-place replacement and the speech helper.</summary>
internal static class TextActions
{
    /// <summary>Copies whatever is selected in the focused app; returns the text plus what the clipboard held before.</summary>
    private static async Task<(string? Text, string? Backup)> GrabSelectionAsync()
    {
        string? backup = null;
        try { if (System.Windows.Clipboard.ContainsText()) backup = System.Windows.Clipboard.GetText(); } catch { }
        NativeClipboard.IgnoreChangesFor(TimeSpan.FromSeconds(3));
        var seq = NativeClipboard.SequenceNumber();
        SelectionGrabber.SendCtrlC();
        string? text = null;
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(40);
            if (NativeClipboard.SequenceNumber() == seq) continue;
            try { text = System.Windows.Clipboard.GetText(); } catch { }
            if (!string.IsNullOrEmpty(text)) break;
        }
        return (text, backup);
    }

    private static void RestoreClipboard(string? backup)
    {
        if (backup == null) return;
        try { System.Windows.Clipboard.SetText(backup); } catch { }
    }

    public static async Task TranslateSelectionAsync(AppServices svc)
    {
        var (text, backup) = await GrabSelectionAsync();
        RestoreClipboard(backup);
        if (string.IsNullOrWhiteSpace(text)) { PopupWindow.ShowMessage(svc, Loc.T("لم يتم العثور على نص محدد.")); return; }
        await TranslateAndShowAsync(svc, text!, "selection");
    }

    /// <summary>Replaces the selected text with its translation, right where it is (chat, mail, any text field).</summary>
    public static async Task ReplaceSelectionAsync(AppServices svc)
    {
        var (text, backup) = await GrabSelectionAsync();
        if (string.IsNullOrWhiteSpace(text)) { RestoreClipboard(backup); PopupWindow.ShowMessage(svc, Loc.T("لم يتم العثور على نص محدد.")); return; }
        try
        {
            var s = svc.Effective;
            var r = await svc.TranslateAsync(text!, "auto", s.ReplaceTargetLanguage, kind: "replace");
            // Already written in the replace language? Then translate it the other way, to the main target language.
            if (r.DetectedSource.StartsWith(s.ReplaceTargetLanguage, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(s.ReplaceTargetLanguage, s.TargetLanguage, StringComparison.OrdinalIgnoreCase))
                r = await svc.TranslateAsync(text!, s.ReplaceTargetLanguage, s.TargetLanguage, kind: "replace");
            NativeClipboard.IgnoreChangesFor(TimeSpan.FromSeconds(3));
            System.Windows.Clipboard.SetText(r.Text);
            await Task.Delay(60);
            SelectionGrabber.SendCtrlV();
            await Task.Delay(250); // let the target app read the clipboard before we put the old content back
        }
        catch (Exception ex)
        {
            Log.Error("Replace", ex);
            PopupWindow.ShowMessage(svc, Loc.T("تعذّرت الترجمة: ") + ex.Message);
        }
        finally { RestoreClipboard(backup); }
    }

    public static async Task TranslateClipboardAsync(AppServices svc)
    {
        string? text = null;
        try { text = System.Windows.Clipboard.GetText(); } catch { }
        if (string.IsNullOrWhiteSpace(text)) { PopupWindow.ShowMessage(svc, Loc.T("الحافظة لا تحتوي نصاً.")); return; }
        await TranslateAndShowAsync(svc, text!, "clipboard");
    }

    public static async Task TranslateAndShowAsync(AppServices svc, string text, string kind = "clipboard")
    {
        var popup = PopupWindow.ShowLoading(svc, text);
        try
        {
            // A single word gets the dictionary view: meanings by part of speech, pronunciation, examples.
            if (svc.Settings.DictionaryMode && DictionaryLookup.IsSingleWord(text))
            {
                var dict = await svc.LookupWordAsync(text.Trim(), saveHistory: true, kind: kind);
                if (dict != null) { popup.SetResult(dict.Formatted, dict.Translation); return; }
            }
            var r = await svc.TranslateAsync(text, kind: kind);
            popup.SetResult(r.Text);
            if (svc.Settings.AutoCopyTranslation) try { System.Windows.Clipboard.SetText(r.Text); } catch { }
        }
        catch (Exception ex) { popup.SetResult(Loc.T("تعذّرت الترجمة: ") + ex.Message); }
    }
}

/// <summary>
/// Text-to-speech. By default Google's online voice is tried first (much clearer than the old built-in Windows voices,
/// especially for Arabic); if it is unavailable the installed Windows voice for the language is used.
/// Calling <see cref="Toggle"/> while speaking stops the audio.
/// </summary>
internal static class Speech
{
    private static readonly Windows.Media.Playback.MediaPlayer Player = CreatePlayer();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static CancellationTokenSource? _cts;
    private static Windows.Storage.Streams.IRandomAccessStream? _current;
    private static volatile bool _busy;

    /// <summary>Raised (on any thread) when speaking starts or stops.</summary>
    public static event Action? StateChanged;
    public static bool IsSpeaking => _busy;

    private static Windows.Media.Playback.MediaPlayer CreatePlayer()
    {
        var p = new Windows.Media.Playback.MediaPlayer { AudioCategory = Windows.Media.Playback.MediaPlayerAudioCategory.Speech };
        p.MediaEnded += (_, _) => SetBusy(false);
        p.MediaFailed += (_, e) => { Log.Warn("Speech", "playback failed: " + e.ErrorMessage); SetBusy(false); };
        return p;
    }

    private static void SetBusy(bool value)
    {
        if (_busy == value) return;
        _busy = value;
        try { StateChanged?.Invoke(); } catch { }
    }

    public static void Toggle(string text, string languageCode)
    {
        if (_busy) Stop(); else _ = SpeakAsync(text, languageCode);
    }

    public static void Stop()
    {
        _cts?.Cancel();
        try { Player.Pause(); Player.Source = null; } catch { }
        SetBusy(false);
    }

    private static async Task SpeakAsync(string text, string lang)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            var mode = App.Current.Services.Settings.SpeechEngine;
            Windows.Storage.Streams.IRandomAccessStream? stream = null; string contentType = "";

            if (mode != "windows")
            {
                try
                {
                    var bytes = await OnlineAsync(text, lang, cts.Token);
                    var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                    await ms.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(bytes));
                    ms.Seek(0);
                    stream = ms; contentType = "audio/mpeg";
                    Log.Info("Speech", $"online voice ({lang})");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    if (mode == "online") throw;
                    Log.Warn("Speech", "online voice unavailable, using the Windows voice: " + ex.Message);
                }
            }
            if (stream == null)
            {
                var prefix = lang.Split('-')[0];
                var voice = Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices
                    .Where(v => v.Language.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(v => v.Gender == Windows.Media.SpeechSynthesis.VoiceGender.Female ? 0 : 1).FirstOrDefault();
                if (voice == null) throw new InvalidOperationException(Loc.T("لا يوجد صوت لهذه اللغة. ثبّت صوتاً من إعدادات ويندوز (الوقت واللغة ← الكلام)."));
                using var synth = new Windows.Media.SpeechSynthesis.SpeechSynthesizer { Voice = voice };
                var s = await synth.SynthesizeTextToStreamAsync(text);
                stream = s; contentType = s.ContentType;
                Log.Info("Speech", $"{voice.DisplayName} ({voice.Language})");
            }
            if (cts.IsCancellationRequested) return;
            _current = stream;
            Player.Source = Windows.Media.Core.MediaSource.CreateFromStream(stream, contentType);
            Player.Play();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error("Speech", ex);
            SetBusy(false);
            _ = Application.Current.Dispatcher.BeginInvoke(() => PopupWindow.ShowMessage(App.Current.Services, Loc.T("تعذّر النطق: ") + ex.Message));
        }
    }

    private static async Task<byte[]> OnlineAsync(string text, string lang, CancellationToken ct)
    {
        using var all = new MemoryStream();
        foreach (var chunk in TranslatorPool.Chunk(text, 180))
        {
            var url = "https://translate.google.com/translate_tts?ie=UTF-8&client=tw-ob&tl=" + Uri.EscapeDataString(lang) + "&q=" + Uri.EscapeDataString(chunk);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0 Safari/537.36");
            using var resp = await Http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            await all.WriteAsync(await resp.Content.ReadAsByteArrayAsync(ct), ct);
        }
        return all.ToArray();
    }
}
internal static class NativeClipboard
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    public static uint SequenceNumber() => GetClipboardSequenceNumber();

    private static long _ignoreUntilTicks;
    /// <summary>Marks clipboard changes in the next few seconds as ours (selection grab, replace) so the watcher skips them.</summary>
    public static void IgnoreChangesFor(TimeSpan span) => _ignoreUntilTicks = DateTime.UtcNow.Add(span).Ticks;
    public static bool IsIgnoring => DateTime.UtcNow.Ticks < _ignoreUntilTicks;
}

/// <summary>Optional: translates whatever the user copies.</summary>
internal static class ClipboardWatcher
{
    public static void Start(AppServices svc, App app)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        var last = NativeClipboard.SequenceNumber();
        timer.Tick += async (_, _) =>
        {
            if (!svc.Settings.AutoTranslateClipboard) return;
            var now = NativeClipboard.SequenceNumber();
            if (now == last) return;
            last = now;
            if (NativeClipboard.IsIgnoring) return;
            try
            {
                if (!System.Windows.Clipboard.ContainsText()) return;
                var t = System.Windows.Clipboard.GetText();
                if (t.Length is > 2 and < 2000) await TextActions.TranslateAndShowAsync(svc, t);
            }
            catch { }
        };
        timer.Start();
    }
}


/// <summary>Looks for a newer GitHub release. Set <see cref="Repo"/> ("owner/name") when publishing.</summary>
internal static class UpdateChecker
{
    /// <summary>GitHub repository ("owner/name") whose latest release is compared with the running version.</summary>
    public const string Repo = "mofx8/tarjuman-sari3";

    public static async Task CheckAsync(App app)
    {
        if (Repo.Length == 0 || !app.Services.Settings.CheckForUpdates) return;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TarjumanSari3");
            using var resp = await http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // a private repository (or one without releases) cannot be read anonymously
                Log.Info("Update", "no public release found (the repository is private or has no release yet)");
                return;
            }
            resp.EnsureSuccessStatusCode();
            using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var tag = doc.RootElement.GetProperty("tag_name").GetString();
            var url = doc.RootElement.GetProperty("html_url").GetString();
            var current = typeof(UpdateChecker).Assembly.GetName().Version;
            if (url == null || !TarjumanSari3.Core.UpdateCheck.IsNewer(tag, current))
            {
                Log.Info("Update", $"up to date (running {current}, latest {tag})");
                return;
            }
            Log.Info("Update", $"new version available: {tag}");
            var s = app.Services.Settings;
            if (s.LastUpdateNotice == tag) return; // already told the user about this one
            s.LastUpdateNotice = tag!; app.Services.Store.Save();
            _ = app.Dispatcher.BeginInvoke(() => app.NotifyUpdate(Loc.T("يتوفر إصدار جديد: ") + tag + Loc.T(" — انقر هنا للتنزيل"), url));
        }
        catch (Exception ex) { Log.Warn("Update", ex.Message); }
    }
}