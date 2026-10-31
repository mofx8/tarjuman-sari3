namespace TarjumanSari3.Core.Settings;

public sealed record SavedRegion(string Process, int MonitorX, int MonitorY, int MonitorWidth, int MonitorHeight, int X, int Y, int Width, int Height);

/// <summary>Overrides applied automatically while a given program is in the foreground.</summary>
public sealed class AppProfile
{
    public string Process { get; set; } = "";
    public string? TargetLanguage { get; set; }
    public string? SourceLanguage { get; set; }
    public double? FontSize { get; set; }
    public string? TextColor { get; set; }
    public string? Background { get; set; }
}

public enum AppTheme { System, Light, Dark }
public enum SelectionMode { UnderCursor, DragRectangle }
public enum TrayAction { None, OpenWindow, OpenSettings, TranslateRegion, TranslateUnderCursor, LiveTranslate, TranslateSelection, TranslateClipboard, TranslateClipboardImage }

public sealed class AppSettings
{
    /// <summary>A copy with the program profile applied (the original is never modified).</summary>
    public AppSettings WithProfile(AppProfile? p)
    {
        if (p == null) return this;
        var c = (AppSettings)MemberwiseClone();
        if (!string.IsNullOrWhiteSpace(p.TargetLanguage)) c.TargetLanguage = p.TargetLanguage!;
        if (!string.IsNullOrWhiteSpace(p.SourceLanguage)) c.SourceLanguage = p.SourceLanguage!;
        if (p.FontSize is > 0) c.OverlayFontSize = p.FontSize.Value;
        if (!string.IsNullOrWhiteSpace(p.TextColor)) c.OverlayTextColor = p.TextColor!;
        if (!string.IsNullOrWhiteSpace(p.Background)) c.OverlayBackground = p.Background!;
        return c;
    }

    // ---- Startup / behaviour ----
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public bool ShowTrayIcon { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool RunAsAdministrator { get; set; } = true;
    public int StartupDelaySeconds { get; set; } = 0;
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>Bumped when a default changes in a way existing settings files must pick up (see SettingsStore).</summary>
    public int SettingsVersion { get; set; } = 1; // files written before this field existed read as 1
    public string LastUpdateNotice { get; set; } = "";
    public TrayAction TrayClickAction { get; set; } = TrayAction.OpenWindow;
    public TrayAction TrayDoubleClickAction { get; set; } = TrayAction.None;
    public bool FirstRunDone { get; set; } = false;

    // ---- Hotkeys (all user-editable) ----
    public Dictionary<HotkeyAction, HotkeyBinding> Hotkeys { get; set; } = DefaultHotkeys();

    public static Dictionary<HotkeyAction, HotkeyBinding> DefaultHotkeys() => new()
    {
        [HotkeyAction.TranslateRegion] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x54), // T
        [HotkeyAction.TranslateUnderCursor] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x51), // Q
        [HotkeyAction.RepeatLastRegion] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x52), // R
        [HotkeyAction.TranslateSelection] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x43), // C
        [HotkeyAction.TranslateClipboard] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x56), // V
        [HotkeyAction.ToggleMainWindow] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x57), // W
        [HotkeyAction.LiveTranslate] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4C), // L
        [HotkeyAction.LiveTranslatePickNew] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x4C), // Shift+L
        [HotkeyAction.TranslateClipboardImage] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x49), // I
        [HotkeyAction.ReplaceSelection] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x53), // S
    };

    // ---- Translation ----
    public string TargetLanguage { get; set; } = "ar";
    public string SourceLanguage { get; set; } = "auto";
    public List<string> EngineOrder { get; set; } = new() { "google", "google-alt", "google-mobile", "offline", "mymemory", "deepl", "azure", "libre" };
    public List<string> DisabledEngines { get; set; } = new();
    public bool RotateEngines { get; set; } = true;
    public bool PreferOffline { get; set; } = false;
    public bool NotifyEngineFallback { get; set; } = true;
    public List<AppProfile> Profiles { get; set; } = new();
    public bool DictionaryMode { get; set; } = true;
    /// <summary>"auto" = online voice first (clearer), then the installed Windows voice; "windows" or "online" force one.</summary>
    public string SpeechEngine { get; set; } = "auto";
    /// <summary>Language the "replace selection" shortcut writes in (text already in it is translated to <see cref="TargetLanguage"/> instead).</summary>
    public string ReplaceTargetLanguage { get; set; } = "en";
    public bool UseCache { get; set; } = true;
    public bool SaveHistory { get; set; } = true;
    public Dictionary<string, string> ApiKeys { get; set; } = new(); // stored DPAPI-protected (see ProtectedSecrets)
    public string LibreTranslateUrl { get; set; } = "";
    public List<string> DoNotTranslate { get; set; } = new();
    public Dictionary<string, string> Glossary { get; set; } = new();

    // ---- Capture / OCR ----
    public SelectionMode DefaultSelectionMode { get; set; } = SelectionMode.UnderCursor;
    public string OcrLanguage { get; set; } = "auto";
    public bool UpscaleSmallCrops { get; set; } = true;
    public double ParagraphMergeSensitivity { get; set; } = 1.0;
    public int LiveIntervalMs { get; set; } = 1200;
    public bool LiveReuseSavedRegion { get; set; } = true;
    /// <summary>Live-translation area remembered per application (key = lower-case process name).</summary>
    public Dictionary<string, SavedRegion> LiveRegions { get; set; } = new();

    // ---- Appearance ----
    public AppTheme Theme { get; set; } = AppTheme.System;
    public string AccentColor { get; set; } = "#3B82F6";
    public string UiLanguage { get; set; } = "ar";
    public string OverlayFontFamily { get; set; } = "Segoe UI";
    public double OverlayFontSize { get; set; } = 18;
    public string OverlayTextColor { get; set; } = "#FFFFFF";
    public string OverlayBackground { get; set; } = "#E6101827";
    public double OverlayOpacity { get; set; } = 0.92;
    public double DimStrength { get; set; } = 0.45;
    public double CornerRadius { get; set; } = 8;
    public bool ShowOriginalCard { get; set; } = true;
    public bool Animations { get; set; } = true;
    public bool ImageMatchBackground { get; set; } = true;

    // ---- Result behaviour ----
    public int AutoCloseSeconds { get; set; } = 0; // 0 = until click
    public bool AutoCopyTranslation { get; set; } = false;
    public bool AutoTranslateClipboard { get; set; } = false;
    public bool PlaySound { get; set; } = false;
    public List<string> IgnoredProcesses { get; set; } = new();
}









