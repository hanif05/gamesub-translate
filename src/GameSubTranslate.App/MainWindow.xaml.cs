using System.Windows;
using System.Windows.Controls;
using GameSubTranslate.App.Profiles;
using GameSubTranslate.Cache;
using GameSubTranslate.Config;
using GameSubTranslate.Logging;
using GameSubTranslate.Ocr;
using GameSubTranslate.Pipeline;
using GameSubTranslate.Profiles;
using GameSubTranslate.Storage;
using GameSubTranslate.Translation;

namespace GameSubTranslate.App;

public partial class MainWindow : Window
{
    private readonly ProfileRepository _repo;
    private readonly ProfileService _service;
    private readonly Database _db;
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly Overlay.OverlayWindow? _overlay;
    private readonly FileLogger? _logger;
    private TranslatePipeline? _pipeline;
    private bool _updating; // guard against event feedback during Refresh

    public MainWindow() : this(new Database(), null, null, null) { }

    public MainWindow(Database db, Window? owner, Overlay.OverlayWindow? overlay = null,
        GameSubTranslate.Logging.FileLogger? logger = null)
    {
        InitializeComponent();
        _db = db;
        _db.EnsureSchema();
        _repo = new ProfileRepository(db);
        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        _logger = logger;
        _service = new ProfileService(_repo, _settingsStore, _settings);
        _overlay = overlay;
        if (owner is not null) Owner = owner;
        LoadVersion();
        Refresh();
    }

    private void LoadVersion()
    {
        // F58: version surfaced from version.txt (sits next to the .csproj). Trim + skip on
        // missing so a fresh build with no resource doesn't show "v?".
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "version.txt");
            if (System.IO.File.Exists(path))
            {
                var v = System.IO.File.ReadAllText(path).Trim();
                if (v.Length > 0) VersionText.Text = $"v{v}";
            }
        }
        catch { /* best-effort, leave empty */ }
    }

    private void Refresh()
    {
        _updating = true;
        try
        {
            var profiles = _repo.GetAll().ToList();
            ProfileList.ItemsSource = profiles;
            CountText.Text = $"({profiles.Count})";
            // F58: empty state visibility.
            if (EmptyState is not null)
                EmptyState.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (EmptyHint is not null)
                EmptyHint.Text = "click + New to add one";

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
        {
            _service.SetActiveRegion(r.Id);
            // T26 scenario 5: a running pipeline was built against the old region's coords. Drop it
            // so the next Start rebuilds over the newly-selected region (region switch = new coords).
            if (_pipeline is not null && _pipeline.IsRunning)
            {
                ResetPipeline("Region switched — click Start to resume over the new region.");
            }
        }
    }

    /// <summary>
    /// Stops + disposes a running pipeline so the next Start rebuilds it with fresh region/config.
    /// Called when the capture geometry changes under a live pipeline (region switch, profile edit).
    /// </summary>
    private void ResetPipeline(string? status = null)
    {
        if (_pipeline is null) return;
        _pipeline.Stop();
        _pipeline.Dispose();
        _pipeline = null;
        SetButtons(running: false, paused: false);
        if (status is not null) SetStatus(status);
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

    /// <summary>T49: tray-facing accessors. App reads these to build the tray tooltip + region submenu.</summary>
    public string? ActiveProfileName() => _service.ActiveProfile?.Name;

    public IReadOnlyList<CaptureRegion> ActiveProfileRegions()
        => _service.ActiveProfile?.Regions ?? (IReadOnlyList<CaptureRegion>)Array.Empty<CaptureRegion>();

    public int? ActiveRegionId() => _service.ActiveRegionId;

    public void SetActiveRegion(int regionId) => _service.SetActiveRegion(regionId);

    /// <summary>T49: forward provider failover signals to the App so it can repaint the tray icon.</summary>
    public event Action<string>? TranslatorFailoverSignal;

    /// <summary>T49: current translation client (null when no pipeline yet). Used by App to
    /// subscribe to FailoverChanged without poking at internals.</summary>
    public TranslationClient? CurrentClient
    {
        get
        {
            // Pipeline keeps the translator private; reflect once. Avoids adding a public Client
            // getter to Core just for tray wiring.
            if (_pipeline is null) return null;
            var f = typeof(TranslatePipeline).GetField("_translator",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return f?.GetValue(_pipeline) as TranslationClient;
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
            // T26: editing regions changes capture geometry — the running pipeline is stale.
            ResetPipeline("Profile edited — click Start to resume with the new region.");
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
        ResetPipeline();
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

        // F87: profile overrides settings for OCR engine + Paddle GPU toggle. Per-game
        // override is the whole point of having profiles — pre-Fase 6 the engine choice
        // in ProfileEditWindow was silently ignored, which made the per-profile OCR
        // picker a dead control. Active profile (always set when region is set, per
        // ProfileService invariants) supplies the override; settings stay the fallback.
        var activeProfile = _service.ActiveProfile;
        var effectiveEngine = activeProfile?.OcrEngine ?? _settings.OcrEngine;
        var effectivePaddleGpu = activeProfile?.PaddleUseGpu ?? _settings.PaddleUseGpu;
        // Source/Target lang follow the same override pattern — T52 already used profile langs.
        var effectiveSource = activeProfile?.SourceLang ?? _settings.SourceLang;
        var effectiveTarget = activeProfile?.TargetLang ?? _settings.TargetLang;
        // Per-game capture interval is a separate profile field; keep using it (was already wired).
        var effectiveInterval = activeProfile?.CaptureIntervalMs ?? _settings.CaptureIntervalMs;

        var cfg = new AppConfig
        {
            ApiKey = _settings.ApiKey,
            BaseUrl = _settings.BaseUrl,
            Model = _settings.Model,
            VisionModel = _settings.VisionModel,
            SourceLang = effectiveSource,
            TargetLang = effectiveTarget,
            Providers = _settings.Providers,
            PaddleUseGpu = effectivePaddleGpu,
        };
        if (!cfg.TranslationEnabled) { SetStatus("Translation not configured — set API key in Settings."); return null; }

        // T38: engine chosen via effective override chain. VisionAI needs a configured
        // provider; factory falls back to Tesseract if that's missing.
        var ocr = OcrEngineFactory.Create(effectiveEngine, cfg);
        _logger?.Info("Pipeline", $"engine={effectiveEngine} gpu={(effectivePaddleGpu ? "on" : "off")} src={effectiveSource} tgt={effectiveTarget} interval={effectiveInterval}ms");
        _pipeline = TranslatePipeline.ForEnvironment(
            region.X, region.Y, region.Width, region.Height, effectiveInterval,
            ocr, cfg, cache: new TranslationCacheRepository(_db),
            onTranslated: t => Dispatcher.Invoke(() =>
            {
                SetStatus($"dst: {t}");
                _overlay?.ShowText(t); // T22: translated text lands on the overlay.
            }),
            onToken: token => Dispatcher.Invoke(() => _overlay?.AppendToken(token)),
            onStreamStart: () => Dispatcher.Invoke(() => _overlay?.BeginStream()),
            onStreamEnd: () => Dispatcher.Invoke(() => _overlay?.EndStream()),
            logger: _logger);

        // T26 scenario 10: surface pipeline/translation errors on the overlay too, so a dead API
        // key is visible over the game instead of the overlay silently staying empty. T39: the
        // message carries a category (e.g. "[translate-error:auth-error: ...]"), surfaced verbatim
        // on the overlay and forwarded to the tray tooltip.
        _pipeline.StatusChanged += s => Dispatcher.Invoke(() =>
        {
            if (s.StartsWith("[") && s.EndsWith("]")) return; // started/paused/resumed — not errors
            SetStatus(s);
            if (s.StartsWith("[translate-error") || s.StartsWith("[tick-error]"))
            {
                _overlay?.ShowText($"⚠ {s}");
                ErrorReported?.Invoke(s);
            }
        });

        // T40: failover to a fallback provider (or back to primary) surfaces a "degraded" marker
        // on the overlay so the user knows the fallback is doing the work.
        _pipeline.TranslatorFailover += name => Dispatcher.Invoke(() =>
        {
            _overlay?.ShowText(name == "primary" ? "✅ back on primary" : $"⚠ degraded: {name}");
            TranslatorFailoverSignal?.Invoke(name); // T49: repaint tray icon to yellow.
        });
        return _pipeline;
    }

    /// <summary>T39: raised when the pipeline reports a categorized (or tick) error. App wires it
    /// to the tray tooltip.</summary>
    public event Action<string>? ErrorReported;

    private void SetButtons(bool running, bool paused)
    {
        StartBtn.IsEnabled = !running;
        StopBtn.IsEnabled = running;
        PauseBtn.IsEnabled = running;
        PauseBtn.Content = paused ? "Resume" : "Pause";
        // F58: status pill color reflects pipeline state.
        if (running && !paused) { StatusPill.Text = "Running"; StatusPill.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Success"); }
        else if (paused) { StatusPill.Text = "Paused"; StatusPill.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Warn"); }
        else { StatusPill.Text = "Idle"; StatusPill.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Text.Muted"); }
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
        // New API key/model → the running TranslationClient holds the old key. Drop the pipeline so
        // the next Start builds one with the fresh config (T26: settings change = rebuild).
        ResetPipeline("Settings changed — click Start to resume with the new config.");
    }

    /// <summary>T51: target-language switch from tray submenu / cycle hotkey. The pipeline carries
    /// the target language in the TranslationClient, so it has to be rebuilt before the next capture.
    /// The active profile's per-profile TargetLang is left alone — global setting wins (matches
    /// existing per-profile-vs-global resolution at EnsurePipeline time).</summary>
    public void SwitchTargetLang(string code)
    {
        _settings.TargetLang = code;
        ResetPipeline($"Target lang switched to {code}. Click Start to resume.");
    }
}
