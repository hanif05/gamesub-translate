using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace GameSubTranslate.Hotkeys;

/// <summary>
/// Global hotkey registration via user32 RegisterHotKey. Hosts a hidden message window so
/// WM_HOTKEY callbacks fire even when no app window has focus. Register/Unregister must be
/// called on the UI thread that created the manager.
/// </summary>
public sealed class GlobalHotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int IdBase = 0xC000; // 0xC000-0xFFFF is the range reserved for applications

    private readonly HwndSource _source;
    private readonly Dictionary<string, Registration> _regs = new();
    private int _nextId = IdBase;

    private sealed record Registration(int Id, Action Callback);

    public GlobalHotkeyManager()
    {
        _source = new HwndSource(new HwndSourceParameters("GameSubTranslate.HotkeyHost"));
        _source.AddHook(WndProc);
    }

    public bool Register(string id, ModifierKeys modifiers, Key key, Action callback)
    {
        if (_regs.ContainsKey(id)) return false;
        int hkId = _nextId++;
        // ModifierKeys bits (Alt=1, Control=2, Shift=4, Windows=8) already match Win32 MOD_*.
        if (!Native.RegisterHotKey(_source.Handle, hkId, (uint)modifiers, (uint)KeyInterop.VirtualKeyFromKey(key)))
        {
            _nextId--;
            return false;
        }
        _regs[id] = new Registration(hkId, callback);
        return true;
    }

    public bool Unregister(string id)
    {
        if (!_regs.Remove(id, out var reg)) return false;
        Native.UnregisterHotKey(_source.Handle, reg.Id);
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _regs.Keys.ToList()) Unregister(id);
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    /// <summary>
    /// Fires a synthetic WM_HOTKEY for a registered id — used by self-checks to simulate the OS.
    /// Invokes the dispatch handler directly: the real OS path is identical but arrives via the
    /// Win32 message pump, which a self-check thread never runs.
    /// </summary>
    internal void FireForTest(string id)
    {
        if (!_regs.TryGetValue(id, out var reg)) return;
        bool handled = false;
        WndProc(_source.Handle, WM_HOTKEY, (IntPtr)reg.Id, IntPtr.Zero, ref handled);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return IntPtr.Zero;
        var reg = _regs.Values.FirstOrDefault(r => r.Id == wParam.ToInt32());
        if (reg is not null)
        {
            handled = true;
            reg.Callback();
        }
        return IntPtr.Zero;
    }

    /// <summary>Parses a "Ctrl+Alt+T"-style spec into modifier + key. Last '+' token is the key.</summary>
    public static bool TryParse(string? spec, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(spec)) return false;
        var parts = spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;
        foreach (var part in parts.AsSpan(0, parts.Length - 1))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win": modifiers |= ModifierKeys.Windows; break;
                default: return false;
            }
        }
        return Enum.TryParse(parts[^1], ignoreCase: true, out key) && key != Key.None;
    }

    private static class Native
    {
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
