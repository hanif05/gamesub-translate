using System.Diagnostics;
using GameSubTranslate.Cache;
using GameSubTranslate.Capture;
using GameSubTranslate.Config;
using GameSubTranslate.Ocr;
using GameSubTranslate.Translation;

namespace GameSubTranslate.Pipeline;

/// <summary>
/// T16: Fase 2 pipeline service. Runs capture → change-detect → OCR → translate → cache →
/// callback on a background loop that Start() spawns and Stop() cancels cleanly.
///
/// Pause()/Resume() (T17) keep the capture ticking while skipping OCR+translate, so on resume
/// the newest frame is compared directly against the last paused frame — no backlog of missed
/// subtitles, no burst of API calls.
///
/// The capture instance is injected (interface over Windows.Graphics.Capture) so the loop is
/// testable with a fake source; ForEnvironment() builds the real WGC capture.
/// </summary>
public sealed class TranslatePipeline : IDisposable
{
    private readonly IScreenCapture _capture;
    private readonly IOcrEngine _ocr;
    private readonly TranslationClient? _translator;
    private readonly TranslationCacheRepository? _cache;
    private readonly Action<string> _onTranslated;
    private readonly int _x, _y, _w, _h, _intervalMs;
    private readonly int _idleIntervalMs;
    private readonly int _idleThreshold;
    private readonly int _idleWindowMs;

    private readonly object _sync = new();
    // Serializes access to the WGC capture source: the capture instance is not thread-safe
    // (frame pool + AutoResetEvent), so the loop's ticking and a manual CaptureOnce (T22)
    // must never call CaptureRegion concurrently.
    private readonly SemaphoreSlim _captureLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile bool _paused;
    private volatile bool _running;
    private byte[]? _lastPng;
    private string? _lastText;
    // T33: idle tracking — consecutive unchanged frames + wall-clock of last change.
    private int _unchangedCount;
    private DateTime _lastChangeAt = DateTime.UtcNow;

    /// <summary>Raised on state changes ("started", "paused", errors…). May fire off the UI thread.</summary>
    public event Action<string>? StatusChanged;

    public bool IsRunning => _running;
    public bool IsPaused => _paused;
    /// <summary>Last non-fatal pipeline/translation error, or null while healthy.</summary>
    public string? LastError { get; private set; }

    public TranslatePipeline(IScreenCapture capture, IOcrEngine ocr, TranslationClient? translator,
        TranslationCacheRepository? cache, int x, int y, int w, int h, int intervalMs,
        Action<string> onTranslated,
        int idleIntervalMs = 3000, int idleThreshold = 3, int idleWindowMs = 5000)
    {
        _capture = capture;
        _ocr = ocr;
        _translator = translator;
        _cache = cache;
        _x = x; _y = y; _w = w; _h = h; _intervalMs = intervalMs;
        _idleIntervalMs = idleIntervalMs;
        _idleThreshold = idleThreshold;
        _idleWindowMs = idleWindowMs;
        _onTranslated = onTranslated;
    }

    /// <summary>Builds a pipeline over the real WGC capture for the monitor containing (x,y).</summary>
    public static TranslatePipeline ForEnvironment(int x, int y, int w, int h, int intervalMs,
        IOcrEngine ocr, AppConfig cfg, TranslationCacheRepository? cache, Action<string> onTranslated,
        int idleIntervalMs = 3000, int idleThreshold = 3, int idleWindowMs = 5000)
    {
        TranslationClient? translator = null;
        if (cfg.TranslationEnabled)
            translator = new TranslationClient(cfg.ApiKey!, cfg.BaseUrl!, cfg.Model!, cfg.SourceLang, cfg.TargetLang);
        return new TranslatePipeline(ScreenCapture.ForMonitorAt(x, y), ocr, translator, cache,
            x, y, w, h, intervalMs, onTranslated,
            idleIntervalMs, idleThreshold, idleWindowMs);
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_running) return;
            _running = true;
            _paused = false;
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => LoopAsync(_cts.Token));
        }
        StatusChanged?.Invoke("started");
    }

    public void Stop()
    {
        Task? loop;
        CancellationTokenSource? cts;
        lock (_sync)
        {
            if (!_running) return;
            _running = false;
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
            cts?.Cancel();
        }
        try { loop?.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { /* LoopAsync catches its own failures; nothing to surface. */ }
        _lastPng = null;
        _lastText = null;
        StatusChanged?.Invoke("stopped");
    }

    public void Pause()
    {
        if (_paused) return;
        _paused = true;
        StatusChanged?.Invoke("paused");
    }

    public void Resume()
    {
        if (!_paused) return;
        _paused = false;
        StatusChanged?.Invoke("resumed");
    }

    /// <summary>
    /// T22: run a single capture → OCR → translate → callback cycle, skipping change detection.
    /// Works whether or not the loop is running or paused. Returns the translated text (or null on
    /// empty frame / translation failure). Uses the same region + session as the loop.
    /// </summary>
    public async Task<string?> CaptureOnceAsync(CancellationToken ct = default)
    {
        byte[] png = await CaptureLockedAsync(ct);
        if (png.Length == 0) return null;
        _lastPng = png;
        string text = _ocr.Recognize(png);
        if (string.IsNullOrWhiteSpace(text)) return null;
        _lastText = text;
        string? translated = await TranslateAsync(text, ct);
        if (translated is not null) _onTranslated(translated);
        return translated;
    }

    /// <summary>Capture one frame under the capture lock, so a manual trigger never races the loop.</summary>
    private async Task<byte[]> CaptureLockedAsync(CancellationToken ct)
    {
        await _captureLock.WaitAsync(ct);
        try
        {
            return _capture.CaptureRegion(_x, _y, _w, _h);
        }
        finally
        {
            _captureLock.Release();
        }
    }

    public void Dispose()
    {
        Stop();
        _captureLock.Dispose();
        _capture.Dispose();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_paused)
                {
                    // T33: paused = zero work. Capture itself is the most expensive step
                    // (full screen read + PNG encode); skip it entirely. Resume() will
                    // pick up the next change from the next tick — no backlog, no burst.
                    // Reset idle tracking so resume doesn't think we're already idle.
                    _unchangedCount = 0;
                    _lastChangeAt = DateTime.UtcNow;
                }
                else
                {
                    byte[] png = await CaptureLockedAsync(ct);
                    if (png.Length > 0)
                    {
                        bool changed = ChangeDetector.IsChanged(png, _lastPng);
                        if (changed)
                        {
                            _lastPng = png;
                            _lastChangeAt = DateTime.UtcNow;
                            _unchangedCount = 0;
                            string text = _ocr.Recognize(png);
                            if (!string.IsNullOrWhiteSpace(text) && text != _lastText)
                            {
                                _lastText = text;
                                string? translated = await TranslateAsync(text, ct);
                                if (translated is not null)
                                    _onTranslated(translated);
                            }
                        }
                        else
                        {
                            _unchangedCount++;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LastError = $"[tick-error] {ex.GetType().Name}: {ex.Message}";
                StatusChanged?.Invoke(LastError);
            }

            int delay = CurrentInterval();
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>T33: pick interval based on idle state. Normal unless we have enough
    /// unchanged frames OR enough wall-clock time has passed without a change.</summary>
    private int CurrentInterval()
    {
        if (_paused) return _intervalMs;
        bool windowElapsed = (DateTime.UtcNow - _lastChangeAt).TotalMilliseconds >= _idleWindowMs;
        if (_unchangedCount >= _idleThreshold || windowElapsed)
            return _idleIntervalMs;
        return _intervalMs;
    }

    private async Task<string?> TranslateAsync(string text, CancellationToken ct)
    {
        try
        {
            if (_translator is null)
                return text; // passthrough when translation isn't configured

            // T26 scenario 8: end-to-end latency from subtitle change to translated output.
            var sw = Stopwatch.StartNew();
            string? translated = _cache?.Get(text, _translator.TargetLang)
                ?? await _translator.TranslateAsync(text, ct);
            sw.Stop();
            Console.WriteLine($"[latency] {sw.Elapsed.TotalMilliseconds:F0}ms \"{text}\" -> \"{translated}\"");
            if (translated is not null && _cache is not null)
                _cache.Put(text, translated, _translator.TargetLang);
            return translated;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // cancellation propagates out of the loop
        }
        catch (Exception ex)
        {
            LastError = $"[translate-error] {ex.Message}";
            StatusChanged?.Invoke(LastError);
            return null;
        }
    }
}
