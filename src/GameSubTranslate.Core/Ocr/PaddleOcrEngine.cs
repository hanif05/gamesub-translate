using PaddleOCRSharp;
using Timer = System.Threading.Timer;

namespace GameSubTranslate.Ocr;

/// <summary>
/// F82: PaddleOCRSharp-backed OCR engine. Lazy in-process init + idle dispose, mirrors
/// <see cref="TesseractOcrEngine"/> so swapping engines in Settings is a no-op for the
/// pipeline. CPU path is mkldnn (MKL is Intel-tuned but still beats Tesseract on AMD per
/// T80 spike: warm median 103ms vs Tesseract ~100ms, with noticeably better accuracy on
/// stylized fonts). GPU path is CUDA-only — user must set AppSettings.PaddleUseGpu when
/// they know the host is NVIDIA + driver is good.
///
/// Why lazy: <c>PaddleOCREngine</c> ctor loads ONNX/nb files into native memory (~250ms
/// on cold cache). Defer to first Recognize so app startup stays snappy.
/// Why idle dispose: holds native handles + ~120MB model resident even when idle. After
/// <see cref="IdleDisposeDefault"/> without a Recognize call we release; next call
/// re-inits in ~250ms. Production pattern: subtitle-still user pays effectively zero
/// memory + CPU between bursts.
///
/// T82: confidence filtering deferred to T85. The OCRParameter here enables
/// classification (cls=true) and standard detection thresholds — good defaults for
/// game subtitles, no per-game tuning needed yet.
/// </summary>
public sealed class PaddleOcrEngine : IOcrEngine, IDisposable
{
    private static readonly TimeSpan IdleDisposeDefault = TimeSpan.FromMinutes(5);

    private readonly bool _useGpu;
    private readonly TimeSpan _idleDisposeAfter;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _idleTimer;

    private PaddleOCREngine? _engine;
    private bool _disposed;

    public PaddleOcrEngine(bool useGpu = false, TimeSpan? idleDisposeAfter = null)
    {
        _useGpu = useGpu;
        _idleDisposeAfter = idleDisposeAfter ?? IdleDisposeDefault;

        // Timer fires once after the idle window; we recreate it on every Recognize so
        // an active session never has its engine yanked mid-use. Matches Tesseract's
        // pattern so the pipeline's thread-safety expectations stay identical.
        _idleTimer = new Timer(_ => OnIdleFire(), state: null,
            dueTime: Timeout.Infinite, period: Timeout.Infinite);
    }

    public Task<string> RecognizeAsync(byte[] pngBytes, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // PaddleOCRSharp's DetectText is synchronous + native and blocks the calling
        // thread. Run it on the thread pool so the WPF UI thread never stalls, same as
        // TesseractOcrEngine.
        return Task.Run(() => RecognizeSync(pngBytes), ct);
    }

    private string RecognizeSync(byte[] pngBytes)
    {
        _gate.Wait();
        try
        {
            ThrowIfDisposed();
            EnsureEngineLocked();
            var result = _engine!.DetectText(pngBytes);
            // Paddle pads with newlines between text regions. Normalize to single spaces
            // so downstream ChangeDetector's text diff doesn't churn on geometry-only
            // variations of the same dialog line.
            return (result?.Text ?? "").Replace('\n', ' ').Trim();
        }
        finally
        {
            _idleTimer.Change(_idleDisposeAfter, Timeout.InfiniteTimeSpan);
            _gate.Release();
        }
    }

    /// <summary>Initialise the engine if needed. MUST be called under _gate. Throws
    /// <see cref="OcrEngineLoadException"/> when the bundled model is missing or the
    /// native stack can't load — same error shape as Tesseract so callers can show one
    /// consistent message ("letakkan model di assets/paddleocr/") regardless of engine.</summary>
    private void EnsureEngineLocked()
    {
        if (_engine is not null) return;
        try
        {
            // Default config = null → engine reads ./inference/PaddleOCR.config.json that
            // PaddleOCRSharp.targets dropped into the output dir. OCRParameter tuned for
            // game subtitles: cls enabled (handles upside-down text in cutscenes), 960px
            // long-side cap (faster than default 1536, plenty for 1920x1080 captures),
            // 10-thread CPU path for parallelism. T82's spike validated this profile.
            _engine = new PaddleOCREngine((OCRModelConfig?)null, new OCRParameter
            {
                use_gpu = _useGpu,
                gpu_id = 0,
                gpu_mem = 4000,
                cpu_math_library_num_threads = 10,
                enable_mkldnn = true,
                max_side_len = 960,
                det = true,
                rec = true,
                cls = true,
                use_angle_cls = false,    // subtitles aren't rotated; skip the extra forward pass
                det_db_thresh = 0.3f,
                det_db_box_thresh = 0.5f,
                rec_batch_num = 6,
            });
        }
        catch (DllNotFoundException ex)
        {
            // Native stack missing — happens if Paddle.Runtime.win_x64 didn't deploy
            // (e.g. user moved the exe without its siblings). Surface a clear message
            // instead of an opaque DllNotFoundException.
            throw new OcrEngineLoadException(
                "Gagal memuat PaddleOCR runtime. Pastikan Paddle.Runtime.win_x64 terinstal dan file .dll native ada di folder output.",
                ex);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // IOException: model files missing/corrupt. InvalidOperationException: Paddle
            // native init failure (e.g. config JSON malformed). Both surface as the same
            // user-facing "letakkan model" message.
            throw new OcrEngineLoadException(
                "Gagal memuat PaddleOCR. Pastikan model ada di folder inference/ di sebelah executable.",
                ex);
        }
    }

    private void OnIdleFire()
    {
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
        if (_disposed) throw new ObjectDisposedException(nameof(PaddleOcrEngine));
    }
}
