using System.Windows;
using GameSubTranslate.App.Overlay;
using GameSubTranslate.Config;
using GameSubTranslate.Hotkeys;

namespace GameSubTranslate.App;

public partial class App : System.Windows.Application
{
    private OverlayWindow? _overlay;
    private GlobalHotkeyManager? _hotkeys;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        // Headless self-checks: run checks then exit before any window is shown.
        if (e.Args.Length > 0 && e.Args[0].StartsWith("--selfcheck"))
        {
            Shutdown(SelfChecks.Run(e.Args[0]));
            return;
        }

        var settings = new SettingsStore().Load();
        _overlay = new OverlayWindow(settings);
        _hotkeys = new GlobalHotkeyManager();
        if (GlobalHotkeyManager.TryParse(settings.HotkeyToggleOverlay, out var mods, out var key))
            _hotkeys.Register("ToggleOverlay", mods, key, ToggleOverlay);

        var main = new MainWindow();
        main.Show();
    }

    /// <summary>T19: hotkey toggles overlay visibility. Hide keeps the text state.</summary>
    private void ToggleOverlay()
    {
        if (_overlay is null) return;
        if (_overlay.IsVisible) _overlay.HideOverlay();
        else _overlay.ShowOverlay();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        base.OnExit(e);
    }
}
