using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using GameSubTranslate.App.Overlay;
using GameSubTranslate.App.Settings;
using GameSubTranslate.Config;
using GameSubTranslate.Hotkeys;
using GameSubTranslate.Profiles;
using GameSubTranslate.Storage;
using Hardcodet.Wpf.TaskbarNotification;
using WinForms = System.Windows.Forms;

namespace GameSubTranslate.App;

public partial class App : System.Windows.Application
{
    private OverlayWindow? _overlay;
    private GlobalHotkeyManager? _hotkeys;
    private MainWindow? _main;
    private SettingsWindow? _settingsWindow;
    private TaskbarIcon? _tray;
    private ForegroundWatcher? _fgWatcher;
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

        InitTray();

        // T25: auto-load profile when a matching game window comes to the foreground.
        _fgWatcher = new ForegroundWatcher(
            foreground: ForegroundWatcher.GetForegroundExe,
            profiles: () => new ProfileRepository(new Database()).GetAll(),
            onProfileLoaded: AutoLoadProfile);
        _fgWatcher.Start();

        // T24: closing the main window hides it (app keeps running in the tray).
        _main.Closing += (_, e) =>
        {
            e.Cancel = true;
            _main.Hide();
        };
        _main.Show();
    }

    /// <summary>T24: system tray icon + context menu. "Exit" is the only path that truly shuts the app down.</summary>
    private void InitTray()
    {
        _tray = (TaskbarIcon)Resources["TrayIcon"];
        _tray.Visibility = System.Windows.Visibility.Visible;

        var menu = new System.Windows.Controls.ContextMenu();
        var overlay = new MenuItem { Header = "Show / Hide Overlay" };
        overlay.Click += (_, _) => ToggleOverlay();
        var pause = new MenuItem { Header = "Pause / Resume" };
        pause.Click += (_, _) => TogglePause();
        var settings = new MenuItem { Header = "Settings" };
        settings.Click += (_, _) => OpenSettings();
        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => ExitApp();
        menu.Items.Add(overlay);
        menu.Items.Add(pause);
        menu.Items.Add(settings);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);
        _tray.ContextMenu = menu;

        _tray.TrayMouseDoubleClick += (_, _) => ShowMainWindow();
    }

    /// <summary>T25: foreground game matched → select its profile (first-match). Won't rebuild an already-running pipeline mid-game.</summary>
    private void AutoLoadProfile(int profileId)
    {
        Dispatcher.Invoke(() =>
        {
            if (_main is null) return;
            if (_main.ActiveProfileId() == profileId) return; // already active, nothing to do
            _main.SelectProfile(profileId);
        });
    }

    private void ShowMainWindow()
    {
        if (_main is null) return;
        _main.Show();
        _main.Activate();
        _main.WindowState = System.Windows.WindowState.Normal;
    }

    /// <summary>T24: full shutdown — release hotkeys + pipeline, clear tray icon, close overlay.</summary>
    private void ExitApp()
    {
        _hotkeys?.Dispose();
        _main?.Dispose();
        _main?.Close(); // bypasses the Closing-cancel override → real close → OnExit
        _tray?.Dispose();
        Shutdown();
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
        _fgWatcher?.Dispose();
        _hotkeys?.Dispose();
        _main?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
