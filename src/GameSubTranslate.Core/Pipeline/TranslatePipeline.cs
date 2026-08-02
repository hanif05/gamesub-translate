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

    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile bool _paused;
    private volatile bool _running;
    private byte[]? _lastPng;
    private string? _lastText;

    /// <summary>Raised on state changes ("started", "paused", errors…). May fire off the UI thread.</summary>
    public event Action<string>? StatusChanged;

    public bool IsRunning => _running;
    public bool IsPaused => _paused;
    /// <summary>Last non-fatal pipeline/translation error, or null while healthy.</summary>
    public string? LastError { get; private set; }

    public TranslatePipeline(IScreenCapture capture, IOcrEngine ocr, TranslationClient? translator,
        TranslationCacheRepository? cache, int x, int y, int w, int h, int intervalMs,
        Action<string> onTranslated)
    {
        _capture = capture;
        _ocr = ocr;
        _translator = translator;
        _cache = cache;
        _x = x; _y = y; _w = w; _h = h; _intervalMs = intervalMs;
        _onTranslated = onTranslated;
    }

    /// <summary>Builds a pipeline over the real WGC capture for the monitor containing (x,y).</summary>
    public static TranslatePipeline ForEnvironment(int x, int y, int w, int h, int intervalMs,
        IOcrEngine ocr, AppConfig cfg, TranslationCacheRepository? cache, Action<string> onTranslated)
    {
        TranslationClient? translator = null;
        if (cfg.TranslationEnabled)
            translator = new TranslationClient(cfg.ApiKey!, cfg.BaseUrl!, cfg.Model!, cfg.SourceLang, cfg.TargetLang);
        return new TranslatePipeline(ScreenCapture.ForMonitorAt(x, y), ocr, translator, cache,
            x, y, w, h, intervalMs, onTranslated);
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

    public void Dispose()
    {
        Stop();
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
                    // Keep the newest frame available so resume compares against the latest
                    // subtitle state — no backlog, no burst of API calls.
                    byte[] pausedFrame = _capture.CaptureRegion(_x, _y, _w, _h);
                    if (pausedFrame.Length > 0) _lastPng = pausedFrame;
                }
                else
                {
                    byte[] png = _capture.CaptureRegion(_x, _y, _w, _h);
                    if (png.Length > 0 && ChangeDetector.IsChanged(png, _lastPng))
                    {
                        _lastPng = png;
                        string text = _ocr.Recognize(png);
                        if (!string.IsNullOrWhiteSpace(text) && text != _lastText)
                        {
                            _lastText = text;
                            string? translated = await TranslateAsync(text, ct);
                            if (translated is not null)
                                _onTranslated(translated);
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

            try { await Task.Delay(_intervalMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<string?> TranslateAsync(string text, CancellationToken ct)
    {
        try
        {
            if (_translator is null)
                return text; // passthrough when translation isn't configured

            string? translated = _cache?.Get(text, _translator.TargetLang)
                ?? await _translator.TranslateAsync(text, ct);
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
