using System.Diagnostics;
using System.Runtime.InteropServices;
using Timer = System.Threading.Timer;

namespace GameSubTranslate.Profiles;

/// <summary>
/// Polls the foreground window's process name and auto-loads the profile whose
/// ExecutableName matches (case-insensitive, base name without path). Fires once per
/// transition, so the same game doesn't retrigger on every poll. Frontend windows
/// (MainWindow/Settings) are skipped so they don't auto-switch profiles.
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private readonly Func<string?> _foreground;
    private readonly Func<IEnumerable<GameProfile>> _profiles;
    private readonly Action<int> _onProfileLoaded;
    private readonly Func<bool> _isFrontendWindow;
    private Timer? _timer;
    private string? _lastExe;

    /// <summary>Name patterns (base name) treated as "not a game" — never auto-loads for these.</summary>
    private static readonly string[] FrontendWindows =
    {
        "GameSubTranslate.App", "GameSubTranslate", "explorer", "ApplicationFrameHost",
        "SearchHost", "ShellExperienceHost", "LockApp", "powershell", "cmd", "code",
        "devenv", "rider", "WindowsTerminal", "Taskmgr", "TextInputHost", "StartMenuExperienceHost",
    };

    public ForegroundWatcher(
        Func<string?> foreground,
        Func<IEnumerable<GameProfile>> profiles,
        Action<int> onProfileLoaded,
        Func<bool>? isFrontendWindow = null)
    {
        _foreground = foreground;
        _profiles = profiles;
        _onProfileLoaded = onProfileLoaded;
        _isFrontendWindow = isFrontendWindow ?? (() => false);
    }

    /// <summary>Starts polling the foreground process name every <paramref name="intervalMs"/> ms.</summary>
    public void Start(int intervalMs = 2000) => _timer = new Timer(_ => Check(), null, 0, intervalMs);

    private void Check()
    {
        string? exe;
        try
        {
            if (_isFrontendWindow()) return;
            exe = _foreground();
        }
        catch
        {
            return; // polling must never crash the app
        }

        if (string.IsNullOrWhiteSpace(exe)) return;
        if (string.Equals(exe, _lastExe, StringComparison.OrdinalIgnoreCase)) return;
        _lastExe = exe;

        try
        {
            var profile = _profiles().FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(p.ExecutableName) &&
                string.Equals(p.ExecutableName.Trim(), exe, StringComparison.OrdinalIgnoreCase));
            if (profile is not null) _onProfileLoaded(profile.Id);
        }
        catch
        {
            // no DB access in polling
        }
    }

    public void Dispose() { _timer?.Dispose(); _timer = null; }

    /// <summary>Returns the foreground window's process base name, or null if unavailable.</summary>
    public static string? GetForegroundExe()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        if (GetWindowThreadProcessId(hwnd, out uint pid) == 0) return null;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
