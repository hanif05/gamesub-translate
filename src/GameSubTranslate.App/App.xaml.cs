using System.Windows;
using GameSubTranslate.App.Overlay;
using GameSubTranslate.App.Settings;
using GameSubTranslate.Config;
using GameSubTranslate.Hotkeys;
using GameSubTranslate.Storage;

namespace GameSubTranslate.App;

public partial class App : System.Windows.Application
{
    private OverlayWindow? _overlay;
    private GlobalHotkeyManager? _hotkeys;
    private MainWindow? _main;
    private SettingsWindow? _settingsWindow;
    private AppSettings _settings = new();

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

        _settings = new SettingsStore().Load();
        _overlay = new OverlayWindow(_settings);
        _main = new MainWindow(new Database(), null, _overlay);

        _hotkeys = new GlobalHotkeyManager();
        RegisterHotkeys(_settings);

        _main.Show();
    }

    /// <summary>(Re)registers all global hotkeys from settings — called at startup and after settings save.</summary>
    private void RegisterHotkeys(AppSettings s)
    {
        if (_hotkeys is null) return;
        _hotkeys.UnregisterAll();
        if (GlobalHotkeyManager.TryParse(s.HotkeyToggleOverlay, out var mods, out var key))
            _hotkeys.Register("ToggleOverlay", mods, key, ToggleOverlay);
        if (GlobalHotkeyManager.TryParse(s.HotkeyPauseCapture, out mods, out key))
            _hotkeys.Register("PauseCapture", mods, key, TogglePause);
        if (GlobalHotkeyManager.TryParse(s.HotkeyOpenSettings, out mods, out key))
            _hotkeys.Register("OpenSettings", mods, key, OpenSettings);
        if (GlobalHotkeyManager.TryParse(s.HotkeyManualCapture, out mods, out key))
            _hotkeys.Register("ManualCapture", mods, key, ManualCapture);
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

    /// <summary>T21/T23: hotkey opens the settings panel (single instance, reuses on repeat press).</summary>
    private void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_overlay);
            _settingsWindow.Closed += (_, _) =>
            {
                if (_settingsWindow!.DialogResult != false) ReloadSettings();
                _settingsWindow = null;
            };
        }
        if (_settingsWindow.Owner is null) _settingsWindow.Owner = _main;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>After settings save: swap in the new settings, re-apply overlay style/position, re-register hotkeys.</summary>
    private void ReloadSettings()
    {
        _settings = new SettingsStore().Load();
        _overlay?.ApplySettings(_settings);
        _main?.ReloadSettings(_settings);
        RegisterHotkeys(_settings);
    }

    /// <summary>T22: hotkey triggers a single capture → OCR → translate cycle.</summary>
    private void ManualCapture() => _main?.TriggerManualCapture();

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _main?.Dispose();
        base.OnExit(e);
    }
}
