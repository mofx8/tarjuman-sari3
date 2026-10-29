using TarjumanSari3.Core.Settings;
using TarjumanSari3.Platform.Native;

namespace TarjumanSari3.Platform;

public sealed record HotkeyRegistrationResult(HotkeyAction Action, HotkeyBinding Binding, bool Success, string? Error);

/// <summary>
/// Global hotkeys through RegisterHotKey on a hidden message-only window. Must be created on a thread
/// that pumps messages (the WPF UI thread). Bindings can be replaced at any time without restarting.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly NativeMethods.WndProc _proc; // keep alive for the native window
    private readonly IntPtr _hwnd;
    private readonly Dictionary<int, HotkeyAction> _ids = new();
    private int _nextId = 1;

    public event Action<HotkeyAction>? HotkeyPressed;

    public HotkeyManager()
    {
        _proc = WndProc;
        var cls = "TarjumanSari3Hotkeys_" + Guid.NewGuid().ToString("N");
        var wc = new NativeMethods.WNDCLASS
        {
            lpfnWndProc = _proc,
            lpszClassName = cls,
            hInstance = NativeMethods.GetModuleHandleW(null),
        };
        NativeMethods.RegisterClassW(ref wc);
        _hwnd = NativeMethods.CreateWindowExW(0, cls, "QTHotkeys", 0, 0, 0, 0, 0,
            NativeMethods.HWND_MESSAGE, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("Could not create the hotkey window.");
    }

    private IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        if (msg == NativeMethods.WM_HOTKEY && _ids.TryGetValue((int)w, out var action))
        {
            HotkeyPressed?.Invoke(action);
            return IntPtr.Zero;
        }
        return NativeMethods.DefWindowProcW(h, msg, w, l);
    }

    /// <summary>Replaces every registration with the given bindings; reports which ones the OS refused.</summary>
    public IReadOnlyList<HotkeyRegistrationResult> Apply(IReadOnlyDictionary<HotkeyAction, HotkeyBinding> bindings)
    {
        UnregisterAll();
        var results = new List<HotkeyRegistrationResult>();
        var used = new Dictionary<(HotkeyModifiers, int), HotkeyAction>();
        foreach (var (action, b) in bindings)
        {
            if (b.IsEmpty) { results.Add(new(action, b, true, null)); continue; }
            if (used.TryGetValue((b.Modifiers, b.VirtualKey), out var other))
            { results.Add(new(action, b, false, $"Already used by {other}")); continue; }
            var id = _nextId++;
            if (NativeMethods.RegisterHotKey(_hwnd, id, ToNative(b.Modifiers), (uint)b.VirtualKey))
            {
                _ids[id] = action;
                used[(b.Modifiers, b.VirtualKey)] = action;
                results.Add(new(action, b, true, null));
            }
            else results.Add(new(action, b, false, "Taken by another application"));
        }
        return results;
    }

    /// <summary>Checks whether a combination is free without keeping it registered.</summary>
    public bool IsAvailable(HotkeyBinding b)
    {
        const int probe = 0x7000;
        if (!NativeMethods.RegisterHotKey(_hwnd, probe, ToNative(b.Modifiers), (uint)b.VirtualKey)) return false;
        NativeMethods.UnregisterHotKey(_hwnd, probe);
        return true;
    }

    private static uint ToNative(HotkeyModifiers m) =>
        NativeMethods.MOD_NOREPEAT
        | (m.HasFlag(HotkeyModifiers.Alt) ? 1u : 0)
        | (m.HasFlag(HotkeyModifiers.Control) ? 2u : 0)
        | (m.HasFlag(HotkeyModifiers.Shift) ? 4u : 0)
        | (m.HasFlag(HotkeyModifiers.Win) ? 8u : 0);

    private void UnregisterAll()
    {
        foreach (var id in _ids.Keys) NativeMethods.UnregisterHotKey(_hwnd, id);
        _ids.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        NativeMethods.DestroyWindow(_hwnd);
    }
}
