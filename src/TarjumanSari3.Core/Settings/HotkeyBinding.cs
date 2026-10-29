namespace TarjumanSari3.Core.Settings;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A user-editable global shortcut. <see cref="VirtualKey"/> is a Win32 VK code.</summary>
public sealed record HotkeyBinding(HotkeyModifiers Modifiers, int VirtualKey)
{
    public static HotkeyBinding None { get; } = new(HotkeyModifiers.None, 0);

    public bool IsEmpty => VirtualKey == 0;

    public override string ToString() => IsEmpty ? "—" : Format(this);

    public static string Format(HotkeyBinding b)
    {
        var parts = new List<string>();
        if (b.Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (b.Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (b.Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (b.Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyName(b.VirtualKey));
        return string.Join("+", parts);
    }

    public static string KeyName(int vk) => vk switch
    {
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
        0x20 => "Space",
        0x0D => "Enter",
        0x09 => "Tab",
        0x1B => "Esc",
        0xC0 => "`",
        0x05 => "Mouse4",
        0x06 => "Mouse5",
        _ => $"0x{vk:X2}",
    };
}

/// <summary>Every action a global hotkey can trigger.</summary>
public enum HotkeyAction
{
    TranslateRegion,
    TranslateUnderCursor,
    RepeatLastRegion,
    TranslateSelection,
    TranslateClipboard,
    ToggleMainWindow,
    LiveTranslate,
    LiveTranslatePickNew,
    TranslateClipboardImage,
    ReplaceSelection,
}



