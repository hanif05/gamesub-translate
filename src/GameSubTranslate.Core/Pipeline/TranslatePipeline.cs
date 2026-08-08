using System.Diagnostics;
using GameSubTranslate.Cache;
using GameSubTranslate.Capture;
using GameSubTranslate.Config;
using GameSubTranslate.Logging;
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
    // T36: incremental token callback for streaming. When set, the loop streams tokens to the
    // overlay instead of waiting for the full response. Null → fall back to single-shot.
    private readonly Action<string>? _onToken;
    private readonly Action? _onStreamStart;
    private readonly Action? _onStreamEnd;
    // Optional diagnostic logger. When set, pipeline writes OCR delta + translate in/out +
    // cache hit/miss to FileLogger so an operator can replay a noisy run. Null → no logging
    // (keeps the existing tests + callers quiet).
    private readonly FileLogger? _logger;
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

    /// <summary>T40: raised when the translator switches to a fallback provider (name) or back to
    /// the primary ("primary"). Lets the UI mark the overlay "degraded". May fire off the UI thread.</summary>
    public event Action<string>? TranslatorFailover;

    public bool IsRunning => _running;
    public bool IsPaused => _paused;
    /// <summary>Last non-fatal pipeline/translation error, or null while healthy.</summary>
    public string? LastError { get; private set; }

    public TranslatePipeline(IScreenCapture capture, IOcrEngine ocr, TranslationClient? translator,
        TranslationCacheRepository? cache, int x, int y, int w, int h, int intervalMs,
        Action<string> onTranslated,
        Action<string>? onToken = null, Action? onStreamStart = null, Action? onStreamEnd = null,
        FileLogger? logger = null,
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
        _onToken = onToken;
        _onStreamStart = onStreamStart;
        _onStreamEnd = onStreamEnd;
        _logger = logger;
    }

    /// <summary>Builds a pipeline over the real WGC capture for the monitor containing (x,y).</summary>
    public static TranslatePipeline ForEnvironment(int x, int y, int w, int h, int intervalMs,
        IOcrEngine ocr, AppConfig cfg, TranslationCacheRepository? cache, Action<string> onTranslated,
        Action<string>? onToken = null, Action? onStreamStart = null, Action? onStreamEnd = null,
        FileLogger? logger = null,
        int idleIntervalMs = 3000, int idleThreshold = 3, int idleWindowMs = 5000)
    {
        TranslationClient? translator = null;
        if (cfg.TranslationEnabled)
            translator = new TranslationClient(cfg.ApiKey!, cfg.BaseUrl!, cfg.Model!, cfg.SourceLang, cfg.TargetLang,
                providers: cfg.Providers);
        var pipeline = new TranslatePipeline(ScreenCapture.ForMonitorAt(x, y), ocr, translator, cache,
            x, y, w, h, intervalMs, onTranslated,
            onToken, onStreamStart, onStreamEnd,
            logger,
            idleIntervalMs, idleThreshold, idleWindowMs);
        // T40: hop events bubble out so the app can flag "degraded" on the overlay.
        if (translator is not null)
            translator.FailoverChanged += name => pipeline.TranslatorFailover?.Invoke(name ?? "primary");
        return pipeline;
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
        string text = await _ocr.RecognizeAsync(png, ct);
        // Fix 2: store the normalized form so the loop's exact-match compare and the manual
        // trigger share one key. Garbage-only frames are dropped before a translation fires.
        string norm = TextCleaning.NormalizeForCache(text);
        if (norm.Length == 0) return null;
        _lastText = norm;
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
                            string text = await _ocr.RecognizeAsync(png, ct);
                            // Fix 2: compare the *normalized* text so frame-to-frame OCR noise on
                            // the same dialog line collapses to one key — one translation, not 3.
                            // Fix 4: garbage-only frames normalize to "" and are dropped.
                            string norm = TextCleaning.NormalizeForCache(text);
                            if (norm.Length > 0 && norm != _lastText)
                            {
                                _lastText = norm;
                                _logger?.Info("OCR", $"recognize text=\"{Truncate(norm, 120)}\"");
                                await TranslateAndShowAsync(text, ct);
                            }
                            else if (norm.Length > 0)
                            {
                                _logger?.Info("OCR", $"skip (same as last) text=\"{Truncate(norm, 120)}\"");
                            }
                            else
                            {
                                _logger?.Info("OCR", "skip (empty/garbage)");
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
            _logger?.Info("Translate", $"request (single-shot) src=\"{Truncate(text, 120)}\"");
            string? translated = LookupCached(text) ?? await _translator.TranslateAsync(text, ct);
            sw.Stop();
            Console.WriteLine($"[latency] {sw.Elapsed.TotalMilliseconds:F0}ms \"{text}\" -> \"{translated}\"");
            _logger?.Info("Translate", $"done {sw.Elapsed.TotalMilliseconds:F0}ms src=\"{Truncate(text, 80)}\" -> \"{Truncate(translated, 80)}\"");
            if (translated is not null && _cache is not null)
                _cache.Put(TextCleaning.NormalizeForCache(text), translated, _translator.TargetLang);
            return translated;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // cancellation propagates out of the loop
        }
        catch (TranslationException ex)
        {
            // T39: categorized — render an actionable hint, not a generic message.
            LastError = $"[translate-error:{Category(ex.Category)}] {ex.Message}";
            StatusChanged?.Invoke(LastError);
            return null;
        }
        catch (Exception ex)
        {
            LastError = $"[translate-error:unknown] {ex.Message}";
            StatusChanged?.Invoke(LastError);
            return null;
        }
    }

    /// <summary>T39: user-facing hint for each error category.</summary>
    private static string Category(ErrorCategory c) => c switch
    {
        ErrorCategory.Auth => "auth-error: cek API key di Settings",
        ErrorCategory.RateLimit => "rate-limit: provider limiting, tunggu lalu retry",
        ErrorCategory.Network => "network: cek koneksi internet",
        ErrorCategory.BadRequest => "bad-request: periksa konfigurasi provider",
        ErrorCategory.Provider => "provider: server translation error",
        _ => "unknown",
    };

    /// <summary>
    /// T37: exact-match first, then fuzzy by Levenshtein against recent cache rows. Returns
    /// null if neither hits — caller falls through to a fresh API call. Logs a [cache-fuzzy]
    /// marker when fuzzy saves a round-trip so the operator can see how often it kicks in.
    /// </summary>
    private string? LookupCached(string text)
    {
        if (_cache is null || _translator is null) return null;
        var exact = _cache.Get(text, _translator.TargetLang);
        if (exact is not null)
        {
            _logger?.Info("Cache", $"exact hit text=\"{Truncate(text, 80)}\"");
            return exact;
        }
        var fuzzy = _cache.GetFuzzy(text, _translator.TargetLang);
        if (fuzzy is { } f)
        {
            _logger?.Info("Cache", $"fuzzy hit sim={f.similarity:F2} text=\"{Truncate(text, 80)}\" -> \"{Truncate(f.translated, 80)}\"");
            return f.translated;
        }
        return null;
    }

    /// <summary>
    /// T36: real-time translation + display. Cache hit → single-shot path (full text immediately).
    /// Cache miss + streaming callback set → stream tokens incrementally to the overlay.
    /// Cache miss + no streaming callback → falls back to <see cref="TranslateAsync"/> for back-compat.
    /// </summary>
    private async Task<string?> TranslateAndShowAsync(string text, CancellationToken ct)
    {
        // Cache hit short-circuits both streaming and the single-shot path — we already have the
        // answer, so stream it as one chunk through the same code path for consistency.
        var cached = LookupCached(text);
        if (cached is not null)
        {
            _onStreamStart?.Invoke();
            _onToken?.Invoke(cached);
            _onStreamEnd?.Invoke();
            _onTranslated(cached);
            return cached;
        }

        if (_translator is null) return text; // passthrough

        if (_onToken is null)
        {
            // No streaming wiring → keep the old single-shot path.
            var translated = await TranslateAsync(text, ct);
            if (translated is not null) _onTranslated(translated);
            return translated;
        }

        // Streaming path: yield tokens to the overlay as they arrive, accumulate for cache write.
        var sw = Stopwatch.StartNew();
        var buffer = new System.Text.StringBuilder();
        DateTime firstTokenAt = default;
        _logger?.Info("Translate", $"request (stream) src=\"{Truncate(text, 120)}\"");
        try
        {
            _onStreamStart?.Invoke();
            await foreach (var token in _translator.TranslateStreamAsync(text, ct).WithCancellation(ct))
            {
                if (firstTokenAt == default) firstTokenAt = DateTime.UtcNow;
                buffer.Append(token);
                _onToken(token);
            }
            _onStreamEnd?.Invoke();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TranslationException ex)
        {
            _onStreamEnd?.Invoke();
            LastError = $"[translate-error:{Category(ex.Category)}] {ex.Message}";
            StatusChanged?.Invoke(LastError);
            return null;
        }
        catch (Exception ex)
        {
            _onStreamEnd?.Invoke();
            LastError = $"[translate-error:unknown] {ex.Message}";
            StatusChanged?.Invoke(LastError);
            return null;
        }

        var full = buffer.ToString();
        sw.Stop();
        if (firstTokenAt != default)
        {
            var firstTokenMs = (firstTokenAt - DateTime.UtcNow.AddMilliseconds(-sw.Elapsed.TotalMilliseconds)).TotalMilliseconds;
            Console.WriteLine($"[latency] first-token~{firstTokenMs:F0}ms total={sw.Elapsed.TotalMilliseconds:F0}ms \"{text}\" -> \"{full}\"");
        }
        else
        {
            Console.WriteLine($"[latency] {sw.Elapsed.TotalMilliseconds:F0}ms \"{text}\" -> \"{full}\" (no tokens)");
        }
        if (full.Length > 0)
        {
            _onTranslated(full);
            // Fix 2: store under the normalized key so future noise-variant frames hit the cache.
            if (_cache is not null) _cache.Put(TextCleaning.NormalizeForCache(text), full, _translator.TargetLang);
        }
        return full.Length > 0 ? full : null;
    }

    /// <summary>Truncate text for log lines so a 200-char subtitle doesn't blow up the log.
    /// Adds "..." when truncated.</summary>
    private static string Truncate(string? s, int max)
    {
        if (s is null) return "";
        if (s.Length <= max) return s;
        return s[..max] + "...";
    }
}
