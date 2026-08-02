using System.Windows;
using GameSubTranslate.App.Overlay;
using GameSubTranslate.Config;
using GameSubTranslate.Hotkeys;
using GameSubTranslate.Storage;

namespace GameSubTranslate.App;

public partial class App : System.Windows.Application
{
    private OverlayWindow? _overlay;
    private GlobalHotkeyManager? _hotkeys;
    private MainWindow? _main;

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
        _main = new MainWindow(new Database(), null, _overlay);

        _hotkeys = new GlobalHotkeyManager();
        if (GlobalHotkeyManager.TryParse(settings.HotkeyToggleOverlay, out var mods, out var key))
            _hotkeys.Register("ToggleOverlay", mods, key, ToggleOverlay);
        if (GlobalHotkeyManager.TryParse(settings.HotkeyPauseCapture, out mods, out key))
            _hotkeys.Register("PauseCapture", mods, key, TogglePause);
        if (GlobalHotkeyManager.TryParse(settings.HotkeyOpenSettings, out mods, out key))
            _hotkeys.Register("OpenSettings", mods, key, OpenSettings);
        if (GlobalHotkeyManager.TryParse(settings.HotkeyManualCapture, out mods, out key))
            _hotkeys.Register("ManualCapture", mods, key, ManualCapture);

        _main.Show();
    }

    /// <summary>T19: hotkey toggles overlay visibility. Hide keeps the text state.</summary>
    private void ToggleOverlay()
    {
        if (_overlay is null) return;
        if (_overlay.IsVisible) _overlay.HideOverlay();
        else _overlay.ShowOverlay();
    }

    /// <summary>T20: hotkey toggles pipeline pause/resume.</summary>
    private void TogglePause() => _main?.TogglePause();

    /// <summary>T21: hotkey focuses the main window (placeholder until T23's SettingsWindow).</summary>
    private void OpenSettings() => _main?.ShowAndFocus();

    /// <summary>T22: hotkey triggers a single capture → OCR → translate cycle.</summary>
    private void ManualCapture() => _main?.TriggerManualCapture();

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _main?.Dispose();
        base.OnExit(e);
    }
}
