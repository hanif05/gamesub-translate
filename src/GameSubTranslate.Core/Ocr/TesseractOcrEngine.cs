using Tesseract;
using Timer = System.Threading.Timer;

namespace GameSubTranslate.Ocr;

/// <summary>
/// Raised when the Tesseract engine can't be initialised (most often: tessdata file
/// missing or wrong path). Distinct exception type so callers can surface a clear,
/// actionable message — "letakkan file .traineddata di assets/tessdata/" — instead
/// of bubbling up an opaque TesseractException.
/// </summary>
public sealed class OcrEngineLoadException : Exception
{
    public OcrEngineLoadException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Tesseract wrapper with lazy init + idle dispose.
///
/// Why lazy: TesseractEngine ctor loads ~30MB tessdata into RAM (~300ms wall time on
/// cold cache). Deferring it to first Recognize keeps app startup snappy.
///
/// Why idle dispose: Tesseract holds native handles for the loaded language data even
/// when idle. After IdleDisposeMs without a Recognize call we release those handles;
/// next Recognize re-inits in ~300ms. Together with T33's adaptive capture interval,
/// a subtitle-still user pays effectively zero memory + CPU between bursts.
///
/// All public methods are thread-safe. Do NOT call Dispose externally — the idle
/// timer + app shutdown handle it.
/// </summary>
public sealed class TesseractOcrEngine : IOcrEngine, IDisposable
{
    private static readonly TimeSpan IdleDisposeDefault = TimeSpan.FromMinutes(5);

    private readonly string _tessdataPath;
    private readonly string _lang;
    private readonly TimeSpan _idleDisposeAfter;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _idleTimer;

    private TesseractEngine? _engine;
    private bool _disposed;

    public TesseractOcrEngine(string? tessdataPath = null, string lang = "eng",
        TimeSpan? idleDisposeAfter = null)
    {
        _tessdataPath = tessdataPath ?? Path.Combine(AppContext.BaseDirectory, "tessdata");
        _lang = lang;
        _idleDisposeAfter = idleDisposeAfter ?? IdleDisposeDefault;

        // Timer fires once after the idle window; we recreate it on every Recognize so
        // an active session never has its engine yanked mid-use.
        _idleTimer = new Timer(_ => OnIdleFire(), state: null,
            dueTime: Timeout.Infinite, period: Timeout.Infinite);
    }

    public Task<string> RecognizeAsync(byte[] pngBytes, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Tesseract's engine is synchronous + native and blocks the calling thread
        // (~300ms first call, less after). Run it on the thread pool so the pipeline's
        // callers (which may be on the WPF UI thread) never block.
        return Task.Run(() => RecognizeSync(pngBytes), ct);
    }

    /// <summary>Synchronous, blocking OCR call. Thread-safe via _gate. T38: kept as the
    /// private core that <see cref="RecognizeAsync"/> schedules on a thread-pool thread.</summary>
    private string RecognizeSync(byte[] pngBytes)
    {
        // Fast-path read of the field without locking — if engine exists, we still
        // enter the gate for the actual Recognize call (Tesseract internal state is
        // not thread-safe), but we avoid the "engine already up" case's blocking wait
        // by doing the lazy-init inside the lock below.
        _gate.Wait();
        try
        {
            ThrowIfDisposed();
            EnsureEngineLocked();
            using var img = Pix.LoadFromMemory(pngBytes);
            using var page = _engine!.Process(img);
            return page.GetText().Trim();
        }
        finally
        {
            // Restart the idle countdown — release the engine N minutes from now.
            _idleTimer.Change(_idleDisposeAfter, Timeout.InfiniteTimeSpan);
            _gate.Release();
        }
    }

    /// <summary>Initialise the engine if needed. MUST be called under _gate.</summary>
    private void EnsureEngineLocked()
    {
        if (_engine is not null) return;
        try
        {
            _engine = new TesseractEngine(_tessdataPath, _lang, EngineMode.Default);
        }
        catch (Exception ex) when (ex is IOException or TesseractException)
        {
            // Re-throw as a typed exception so the caller can render "letakkan file
            // .traineddata di assets/tessdata/" without parsing Tesseract's internal
            // error message (which is locale-dependent and not always helpful).
            throw new OcrEngineLoadException(
                $"Gagal memuat Tesseract OCR. Pastikan file '{_lang}.traineddata' ada di folder tessdata: {_tessdataPath}",
                ex);
        }
    }

    private void OnIdleFire()
    {
        // Wait synchronously for the gate — if a Recognize is in flight, we wait until
        // it releases the engine, then dispose. Bounded by Recognize latency (~300ms).
        try
        {
            _gate.Wait();
            try
            {
                if (_engine is not null)
                {
                    _engine.Dispose();
                    _engine = null;
                }
            }
            finally { _gate.Release(); }
        }
        catch (ObjectDisposedException)
        {
            // App shutdown raced the timer — nothing to do.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _idleTimer.Dispose();
        _gate.Wait();
        try
        {
            _engine?.Dispose();
            _engine = null;
        }
        finally { _gate.Release(); }
        _gate.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TesseractOcrEngine));
    }
}
