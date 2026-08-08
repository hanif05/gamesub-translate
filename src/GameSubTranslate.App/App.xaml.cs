using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using GameSubTranslate.App.Onboarding;
using GameSubTranslate.App.Overlay;
using GameSubTranslate.App.Settings;
using GameSubTranslate.Config;
using GameSubTranslate.Hotkeys;
using GameSubTranslate.Logging;
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
    private ContextMenu? _trayMenu;
    private MenuItem? _regionMenuItem;
    private MenuItem? _langMenuItem;
    private ForegroundWatcher? _fgWatcher;
    private AppSettings _settings = new();
    private FileLogger _logger = new();
    // T49: tray status flag — set on translator failover, cleared on back-to-primary or fresh start.
    private bool _trayDegraded;
    // T49: last error message surfaced on the tooltip (cleared by the user opening the main window).
    private string? _trayLastError;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        // Surface unhandled exceptions to stderr so self-checks don't fail silently when
        // a Background-thread throw is swallowed by the WPF default handler.
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            var ex = ev.ExceptionObject as Exception;
            Console.Error.WriteLine($"[unhandled] {ex}");
        };
        base.OnStartup(e);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        // Headless self-checks: run checks then exit before any window is shown.
        // Run on the thread pool so .GetAwaiter().GetResult() inside self-checks doesn't
        // deadlock against the WPF dispatcher (sync-over-async on the UI thread = trap).
        if (e.Args.Length > 0 && e.Args[0].StartsWith("--selfcheck"))
        {
            // Run on thread pool so .GetAwaiter().GetResult() inside the self-check doesn't
            // deadlock against the WPF dispatcher (sync-over-async on UI thread = trap).
            int rc = 2; // generic failure if the task itself faults
            try
            {
                rc = Task.Run(() => SelfChecks.Run(e.Args[0])).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[selfcheck-exception] {ex}");
                rc = 2;
            }
            Console.Error.WriteLine($"[selfcheck-exit] rc={rc}");
            Console.Out.Flush();
            Console.Error.Flush();
            // Shutdown must be invoked on the dispatcher thread; marshal back.
            Dispatcher.BeginInvoke(new Action(() => Shutdown(rc)));
            return;
        }

        _settings = new SettingsStore().Load();
        _logger.Info("App", $"starting (settings at {new SettingsStore().FilePath})");

        // T45: first-run wizard. Returning users (SetupCompleted=true) skip it; the
        // wizard is modal so the rest of startup (overlay + main) happens AFTER it
        // dismisses. Skipped → user goes straight into Settings; Completed → they
        // land on the main window with settings persisted.
        var wizardSkipped = false;
        if (!_settings.SetupCompleted)
        {
            var wiz = new WelcomeWindow(_settings);
            wiz.ShowDialog();
            new SettingsStore().Save(_settings);
            wizardSkipped = wiz.Result == WelcomeWindow.Outcome.Skipped;
        }

        _overlay = new OverlayWindow(_settings);
        _overlay.ShowOverlay(); // T14: overlay is visible (transparent) from launch; hotkey hides it.
        _main = new MainWindow(new Database(), null, _overlay, _logger);

        if (wizardSkipped)
        {
            // Funnel into SettingsPanel so the API key gets a chance to be filled
            // on this same run. The main window opens underneath.
            OpenSettings();
        }

        _hotkeys = new GlobalHotkeyManager();
        RegisterHotkeys(_settings);

        InitTray();

        // T39: mirror categorized translation errors onto the tray icon tooltip, so a broken
        // API key is visible even when overlays are hidden mid-game.
        _main.ErrorReported += msg => Dispatcher.Invoke(() =>
        {
            _trayLastError = msg;
            _logger.Error("Pipeline", msg);
            RefreshTrayStatus();
        });

        // T49: failover to a fallback provider turns the tray icon amber.
        _main.TranslatorFailoverSignal += name => Dispatcher.Invoke(() =>
        {
            _trayDegraded = name != "primary";
            RefreshTrayStatus();
        });

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
    /// <remarks>T49: submenu for region switch, tooltip with active profile, color-coded icon,
    /// and double-click that toggles overlay when the main window is already visible.</remarks>
    private void InitTray()
    {
        _tray = (TaskbarIcon)Resources["TrayIcon"];
        _tray.Icon = BuildTrayIcon(TrayStatus.Ok);
        _tray.Visibility = System.Windows.Visibility.Visible;

        _trayMenu = new System.Windows.Controls.ContextMenu();
        var overlay = new MenuItem { Header = "Show / Hide Overlay" };
        overlay.Click += (_, _) => ToggleOverlay();
        var pause = new MenuItem { Header = "Pause / Resume" };
        pause.Click += (_, _) => TogglePause();
        _regionMenuItem = new MenuItem { Header = "Region" }; // rebuilt on profile change
        _langMenuItem = new MenuItem { Header = "Target language" }; // T51: quick switch submenu
        var settings = new MenuItem { Header = "Settings" };
        settings.Click += (_, _) => OpenSettings();
        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => ExitApp();
        _trayMenu.Items.Add(overlay);
        _trayMenu.Items.Add(pause);
        _trayMenu.Items.Add(_regionMenuItem);
        _trayMenu.Items.Add(_langMenuItem);
        _trayMenu.Items.Add(new Separator());
        _trayMenu.Items.Add(settings);
        _trayMenu.Items.Add(new Separator());
        _trayMenu.Items.Add(exit);
        _tray.ContextMenu = _trayMenu;

        _tray.TrayMouseDoubleClick += (_, _) =>
        {
            // T49: double-click doubles as overlay toggle (the common shortcut) when the main
            // window is already visible. Otherwise surface the main window so the user can
            // manage profiles without resorting to the tray menu.
            if (_main is { IsVisible: true, WindowState: System.Windows.WindowState.Normal }) ToggleOverlay();
            else ShowMainWindow();
        };
        RebuildRegionMenu();
        RebuildLangMenu();
        RefreshTrayStatus();
    }

    private enum TrayStatus { Ok, Degraded, Error }

    /// <summary>T49: rebuilds the Region submenu from the active profile's regions. No active
    /// profile, or profile has ≤1 region → hide the menu item entirely.</summary>
    private void RebuildRegionMenu()
    {
        if (_regionMenuItem is null) return;
        _regionMenuItem.Items.Clear();
        var regions = _main?.ActiveProfileRegions();
        if (regions is null || regions.Count <= 1)
        {
            _regionMenuItem.Visibility = System.Windows.Visibility.Collapsed;
            return;
        }
        _regionMenuItem.Visibility = System.Windows.Visibility.Visible;
        var active = _main.ActiveRegionId();
        foreach (var r in regions)
        {
            var mi = new MenuItem
            {
                Header = string.IsNullOrWhiteSpace(r.RegionName) ? r.Display : r.RegionName,
                IsCheckable = true,
                IsChecked = r.Id == active,
            };
            int captured = r.Id;
            mi.Click += (_, _) =>
            {
                _main?.SetActiveRegion(captured);
                RebuildRegionMenu();
            };
            _regionMenuItem.Items.Add(mi);
        }
    }

    /// <summary>T51: ordered list the tray submenu + cycle hotkey iterate over. Matches the
    /// suggestion in the PRD (id is the user's default; the rest cover their multi-target games).</summary>
    private static readonly string[] TargetLangCycle = { "id", "en", "ja", "ko", "zh", "fr", "de", "es" };

    private void RebuildLangMenu()
    {
        if (_langMenuItem is null) return;
        _langMenuItem.Items.Clear();
        foreach (var code in TargetLangCycle)
        {
            var mi = new MenuItem { Header = code, IsCheckable = true, IsChecked = _settings.TargetLang == code };
            string captured = code;
            mi.Click += (_, _) => SwitchTargetLang(captured);
            _langMenuItem.Items.Add(mi);
        }
    }

    /// <summary>T51: switch + save + rebuild pipeline + refresh the menu check marks.</summary>
    private void SwitchTargetLang(string code)
    {
        if (_settings.TargetLang == code) return;
        _settings.TargetLang = code;
        new SettingsStore().Save(_settings);
        _main?.SwitchTargetLang(code); // drops + rebuilds pipeline with new target lang
        RebuildLangMenu();
        _logger.Info("Pipeline", $"target lang switched to {code}");
    }

    /// <summary>T51: hotkey cycles through TargetLangCycle. Order matches the tray menu so the
    /// user sees the same sequence both ways.</summary>
    private void CycleTargetLang()
    {
        var i = Array.IndexOf(TargetLangCycle, _settings.TargetLang);
        var next = TargetLangCycle[(i + 1) % TargetLangCycle.Length];
        SwitchTargetLang(next);
    }

    /// <summary>T49: tooltip string + icon color. Three states (ok/degraded/error).</summary>
    private void RefreshTrayStatus()
    {
        if (_tray is null) return;
        var profile = _main?.ActiveProfileName();
        _tray.ToolTipText = string.IsNullOrEmpty(profile)
            ? "GameSubTranslate — No active profile"
            : $"GameSubTranslate — Active: {profile}";
        if (_trayLastError is not null)
            _tray.ToolTipText += $"\n⚠ {_trayLastError}";
        else if (_trayDegraded)
            _tray.ToolTipText += "\n⚠ degraded — running on fallback provider";

        var status = _trayLastError is not null ? TrayStatus.Error
                   : _trayDegraded ? TrayStatus.Degraded
                   : TrayStatus.Ok;
        _tray.Icon?.Dispose(); // release the previous HICON — FromHandle doesn't free its source
        _tray.Icon = BuildTrayIcon(status);
    }

    /// <summary>T25: foreground game matched → select its profile (first-match). Won't rebuild an already-running pipeline mid-game.</summary>
    private void AutoLoadProfile(int profileId)
    {
        Dispatcher.Invoke(() =>
        {
            if (_main is null) return;
            if (_main.ActiveProfileId() == profileId) return; // already active, nothing to do
            _main.SelectProfile(profileId);
            // T49: profile swap changes the region list + tooltip.
            RebuildRegionMenu();
            RefreshTrayStatus();
        });
    }

    private void ShowMainWindow()
    {
        if (_main is null) return;
        _main.Show();
        _main.Activate();
        _main.WindowState = System.Windows.WindowState.Normal;
        // T49: opening the main window acknowledges the surfaced error.
        if (_trayLastError is not null)
        {
            _trayLastError = null;
            RefreshTrayStatus();
        }
    }

    /// <summary>T24: tray icon with no .ico asset on disk — draw "GS" text onto a 32x32 bitmap at runtime.</summary>
    /// <remarks>T49: color reflects status (green ok / yellow degraded / red error).</remarks>
    private static System.Drawing.Icon BuildTrayIcon(TrayStatus status)
    {
        var bg = status switch
        {
            TrayStatus.Degraded => System.Drawing.Color.FromArgb(210, 160, 30),  // amber
            TrayStatus.Error => System.Drawing.Color.FromArgb(200, 60, 60),      // red
            _ => System.Drawing.Color.FromArgb(40, 160, 80),                      // green
        };
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(bg);
            using var font = new System.Drawing.Font("Arial", 16f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            var size = g.MeasureString("GS", font);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.DrawString("GS", font, brush, (bmp.Width - size.Width) / 2, (bmp.Height - size.Height) / 2);
        }
        // FromHandle wraps the HICON; caller is expected to Dispose() the previous icon.
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
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
        if (GlobalHotkeyManager.TryParse(s.HotkeyCycleTargetLang, out mods, out key))
            _hotkeys.Register("CycleTargetLang", mods, key, CycleTargetLang);
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
                if (_settingsWindow!.Saved) ReloadSettings();
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
        _logger.Info("Settings", "settings saved — reloaded");
        _overlay?.ApplySettings(_settings);
        _main?.ReloadSettings(_settings);
        RegisterHotkeys(_settings);
        RebuildLangMenu(); // T51: TargetLang may have changed in the settings panel.
    }

    /// <summary>T22: hotkey triggers a single capture → OCR → translate cycle.</summary>
    private void ManualCapture() => _main?.TriggerManualCapture();

    protected override void OnExit(ExitEventArgs e)
    {
        _logger.Info("App", "shutting down");
        _fgWatcher?.Dispose();
        _hotkeys?.Dispose();
        _main?.Dispose();
        _tray?.Dispose();
        _logger.Dispose();
        base.OnExit(e);
    }
}
