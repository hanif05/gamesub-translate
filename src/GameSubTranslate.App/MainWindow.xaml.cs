using System.Windows;
using System.Windows.Controls;
using GameSubTranslate.App.Profiles;
using GameSubTranslate.Config;
using GameSubTranslate.Ocr;
using GameSubTranslate.Pipeline;
using GameSubTranslate.Profiles;
using GameSubTranslate.Storage;

namespace GameSubTranslate.App;

public partial class MainWindow : Window
{
    private readonly ProfileRepository _repo;
    private readonly ProfileService _service;
    private readonly Database _db;
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly Overlay.OverlayWindow? _overlay;
    private TranslatePipeline? _pipeline;
    private bool _updating; // guard against event feedback during Refresh

    public MainWindow() : this(new Database(), null, null) { }

    public MainWindow(Database db, Window? owner, Overlay.OverlayWindow? overlay = null)
    {
        InitializeComponent();
        _db = db;
        _db.EnsureSchema();
        _repo = new ProfileRepository(db);
        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        _service = new ProfileService(_repo, _settingsStore, _settings);
        _overlay = overlay;
        if (owner is not null) Owner = owner;
        Refresh();
    }

    private void Refresh()
    {
        _updating = true;
        try
        {
            var profiles = _repo.GetAll().ToList();
            ProfileList.ItemsSource = profiles;
            CountText.Text = $"({profiles.Count})";

            // Select the active profile if known.
            if (_service.ActiveProfileId is int pid)
            {
                var idx = profiles.FindIndex(p => p.Id == pid);
                if (idx >= 0) ProfileList.SelectedIndex = idx;
            }
            else if (profiles.Count > 0)
            {
                ProfileList.SelectedIndex = 0;
            }

            RefreshRegions();
        }
        finally
        {
            _updating = false;
        }
    }

    private void RefreshRegions()
    {
        RegionCombo.Items.Clear();
        if (_service.ActiveProfile is { } profile)
        {
            foreach (var r in profile.Regions)
                RegionCombo.Items.Add(r);
            var current = _service.ActiveRegion();
            if (current is not null)
                RegionCombo.SelectedItem = current;
        }
    }

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        if (ProfileList.SelectedItem is GameProfile p)
        {
            _service.SetActiveProfile(p.Id);
            RefreshRegions();
        }
    }

    private void RegionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        if (RegionCombo.SelectedItem is CaptureRegion r)
            _service.SetActiveRegion(r.Id);
    }

    private GameProfile? Selected => ProfileList.SelectedItem as GameProfile;

    /// <summary>T25: current active profile id (null if none).</summary>
    public int? ActiveProfileId() => _service.ActiveProfileId;

    /// <summary>T25: select a profile programmatically (auto-load from foreground watcher).</summary>
    public void SelectProfile(int profileId)
    {
        var profiles = ProfileList.Items.Cast<GameProfile>().ToList();
        int idx = profiles.FindIndex(p => p.Id == profileId);
        if (idx >= 0)
        {
            ProfileList.SelectedIndex = idx;
            // WPF suppresses re-selection of the already-selected index → SelectionChanged
            // won't fire. Set the service state explicitly so repeated auto-loads are idempotent.
            _service.SetActiveProfile(profileId);
            RefreshRegions();
        }
        else
        {
            Refresh(); // profile list stale (created/deleted elsewhere) → reload + try select
        }
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ProfileEditWindow { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            int id = _repo.Create(dlg.Result);
            _service.SetActiveProfile(id);
            Refresh();
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        var sel = Selected;
        if (sel is null) return;
        // Reload fresh from DB so regions list is current.
        var current = _repo.GetById(sel.Id);
        if (current is null) return;
        var dlg = new ProfileEditWindow(current) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _repo.Update(dlg.Result);
            Refresh();
        }
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        var sel = Selected;
        if (sel is null) return;
        var current = _repo.GetById(sel.Id);
        if (current is null) return;
        current.Id = 0;
        current.Name = current.Name + " (copy)";
        foreach (var r in current.Regions) r.Id = 0;
        int id = _repo.Create(current);
        _service.SetActiveProfile(id);
        Refresh();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var sel = Selected;
        if (sel is null) return;
        var ok = System.Windows.MessageBox.Show(this,
            $"Delete profile \"{sel.Name}\"? This removes all its regions.",
            "Confirm", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (ok != MessageBoxResult.OK) return;
        _repo.Delete(sel.Id);
        if (_service.ActiveProfileId == sel.Id)
            _service.ClearActiveProfile();
        Refresh();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    // ---- T16 pipeline controls ----

    /// <summary>Show + focus from a hotkey (T21 settings shortcut targets the main window until T23).</summary>
    public void ShowAndFocus()
    {
        Show();
        Activate();
    }

    /// <summary>T20: hotkey toggles pipeline pause/resume. Builds the pipeline if not yet started.</summary>
    public void TogglePause()
    {
        var pipe = EnsurePipeline();
        if (pipe is null) return;
        if (pipe.IsPaused) pipe.Resume();
        else pipe.Pause();
        SetButtons(running: pipe.IsRunning, paused: pipe.IsPaused);
    }

    /// <summary>T22: hotkey triggers one capture → OCR → translate cycle, bypassing change detection.</summary>
    public async void TriggerManualCapture()
    {
        var pipe = EnsurePipeline();
        if (pipe is null) return;
        await pipe.CaptureOnceAsync();
    }

    /// <summary>
    /// Lazy-builds the pipeline over the active region. No region / no config → status shows why
    /// instead of starting a broken loop. T17 pause/resume added in T20's hotkey wiring.
    /// </summary>
    public TranslatePipeline? EnsurePipeline()
    {
        if (_pipeline is not null) return _pipeline;
        var region = _service.ActiveRegion();
        if (region is null) { SetStatus("No active region — pick a profile first."); return null; }

        var cfg = new AppConfig
        {
            ApiKey = _settings.ApiKey,
            BaseUrl = _settings.BaseUrl,
            Model = _settings.Model,
            SourceLang = _settings.SourceLang,
            TargetLang = _settings.TargetLang,
        };
        if (!cfg.TranslationEnabled) { SetStatus("Translation not configured — set API key in Settings."); return null; }

        var ocr = new TesseractOcrEngine();
        _pipeline = TranslatePipeline.ForEnvironment(
            region.X, region.Y, region.Width, region.Height, _settings.CaptureIntervalMs,
            ocr, cfg, cache: null, t => Dispatcher.Invoke(() =>
            {
                SetStatus($"dst: {t}");
                _overlay?.ShowText(t); // T22: translated text lands on the overlay.
            }));
        return _pipeline;
    }

    private void SetButtons(bool running, bool paused)
    {
        StartBtn.IsEnabled = !running;
        StopBtn.IsEnabled = running;
        PauseBtn.IsEnabled = running;
        PauseBtn.Content = paused ? "Resume" : "Pause";
    }

    private void SetStatus(string msg) => StatusText.Text = msg;

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        var pipe = EnsurePipeline();
        if (pipe is null) return;
        pipe.StatusChanged += s => Dispatcher.Invoke(() => SetStatus(s));
        pipe.Start();
        SetButtons(running: true, paused: false);
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_pipeline is null) return;
        if (_pipeline.IsPaused) _pipeline.Resume();
        else _pipeline.Pause();
        SetButtons(running: true, paused: _pipeline.IsPaused);
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _pipeline?.Stop();
        SetButtons(running: false, paused: false);
        SetStatus("Stopped");
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _pipeline?.Dispose();
    }

    /// <summary>Disposes the pipeline when the app exits without closing the window first.</summary>
    public void Dispose()
    {
        _pipeline?.Dispose();
        _pipeline = null; // idempotent: hotkey handler + tray Exit can both reach here.
    }

    /// <summary>T23: applies freshly-saved settings (pipeline rebuilds on next start to pick up new capture/lang config).</summary>
    public void ReloadSettings(AppSettings fresh)
    {
        _settings.ApiKey = fresh.ApiKey;
        _settings.BaseUrl = fresh.BaseUrl;
        _settings.Model = fresh.Model;
        _settings.SourceLang = fresh.SourceLang;
        _settings.TargetLang = fresh.TargetLang;
        _settings.CaptureIntervalMs = fresh.CaptureIntervalMs;
    }
}
