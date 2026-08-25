using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Online;
using Timer = System.Threading.Timer;

namespace GameSubTranslate.Ocr;

/// <summary>
/// F82 (rewritten on the Sdcb stack): PaddleOCR-backed OCR engine via Sdcb.PaddleInference.
/// Replaces raoyutian PaddleOCRSharp because the latter's free NuGet is CPU-only (its
/// <c>use_gpu=true</c> flag is silently ignored — runtime banner prints "current CPU version").
/// Sdcb exposes <see cref="PaddleDevice.Gpu"/> as a first-class API and ships free
/// cu120 runtime variants. We're targeting cu120-sm61-75 so the bundled native stack
/// supports GTX 10/16 series (sm_61, sm_75). The user's hardware is a GTX 1650 Ti (Turing,
/// sm_75), which lands inside that bracket.
///
/// Lazy init + idle dispose, mirrors <see cref="TesseractOcrEngine"/> so swapping engines in
/// Settings is a no-op for the pipeline. CPU path is mkldnn (Intel-tuned but still beats
/// Tesseract on AMD per T80 spike: warm median 103ms vs Tesseract ~100ms). GPU path is
/// CUDA-only — user must set AppSettings.PaddleUseGpu when they know the host is NVIDIA +
/// driver + CUDA 12 runtime is present.
///
/// Why lazy: <c>PaddleOcrAll</c> ctor loads .nb / .pdmodel files into native memory
/// (~250ms on cold cache). Defer to first Recognize so app startup stays snappy.
/// Why idle dispose: holds native handles + ~120MB model resident even when idle. After
/// <see cref="IdleDisposeDefault"/> without a Recognize call we release; next call
/// re-inits in ~250ms. Production pattern: subtitle-still user pays effectively zero
/// memory + CPU between bursts.
///
/// F82 (confidence): not exposed yet — RecognizeAsync returns plain string. Hybrid
/// fallback (T85) deferred. New with Sdcb: model is downloaded from PaddleOCR's official
/// repo on first init via OnlineFullModels.EnglishV3.DownloadAsync() — cached on disk so
/// only the first launch pays the download cost.
/// </summary>
public sealed class PaddleOcrEngine : IOcrEngine, IDisposable
{
    private static readonly TimeSpan IdleDisposeDefault = TimeSpan.FromMinutes(5);

    private readonly bool _useGpu;
    private readonly TimeSpan _idleDisposeAfter;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _idleTimer;

    private PaddleOcrAll? _engine;
    private FullOcrModel? _model;
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
        // PaddleOCR's Run is synchronous + native and blocks the calling thread. Run
        // it on the thread pool so the WPF UI thread never stalls, same as
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
            // Decode PNG bytes into an OpenCV Mat for Sdcb. ImDecode with Color flag gives
            // 3-channel BGR — what PaddleOCR's Run expects. The Mat is IDisposable but
            // Sdcb.Run doesn't take ownership; we own it for the lifetime of this call.
            using var mat = Cv2.ImDecode(pngBytes, ImreadModes.Color);
            if (mat.Empty())
            {
                return string.Empty;
            }
            var result = _engine!.Run(mat);
            // PaddleOCR joins detected text regions with "\n". Normalize to single spaces
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

    /// <summary>Initialise the engine + download model if needed. MUST be called under _gate.
    /// Throws <see cref="OcrEngineLoadException"/> when the native stack can't load or the
    /// model download fails — same error shape as Tesseract so callers can show one
    /// consistent message regardless of engine.</summary>
    private void EnsureEngineLocked()
    {
        if (_engine is not null) return;
        try
        {
            // OnlineFullModels.EnglishV3.DownloadAsync() downloads the English PP-OCRv3
            // model files (det/cls/rec) to a NuGet-managed cache dir on first call and
            // returns a FullOcrModel that points at them. Subsequent calls hit the
            // cache. DownloadAsync is async-only — we block briefly via .GetAwaiter() on
            // the worker thread, which is acceptable because we're already inside the
            // thread-pool Task.Run that RecognizeAsync scheduled.
            _model ??= OnlineFullModels.EnglishV3.DownloadAsync().GetAwaiter().GetResult();

            var device = _useGpu ? PaddleDevice.Gpu() : PaddleDevice.Mkldnn();
            _engine = new PaddleOcrAll(_model, device)
            {
                // Subtitles aren't rotated; skip the angle-classification forward pass
                // to save ~30% of GPU/CPU time per frame.
                AllowRotateDetection = false,
                Enable180Classification = false,
            };
        }
        catch (DllNotFoundException ex)
        {
            // Native stack missing — happens if the cu120 runtime package didn't deploy
            // its DLLs next to the exe (or user copied only GameSubTranslate.App.exe).
            throw new OcrEngineLoadException(
                "Gagal memuat PaddleOCR runtime native. Pastikan paket Sdcb.PaddleInference.runtime.win64.cu120-sm61-75 terinstal dan file .dll native ada di folder output.",
                ex);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OpenCvSharpException)
        {
            // IOException: model file corrupt on disk. InvalidOperationException: paddle
            // native init failure (e.g. CUDA driver/runtime mismatch). OpenCvSharpException:
            // ImDecode failure (rare — usually means a malformed PNG). All surface as the
            // same user-facing "engine init failed" message.
            throw new OcrEngineLoadException(
                "Gagal memuat PaddleOCR. Periksa driver NVIDIA + CUDA 12 runtime, atau matikan GPU toggle di Settings untuk fallback CPU.",
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
                    // Keep _model cached — re-downloading every idle cycle would be
                    // wasteful. The disk-cached .pdmodel files are small (~120MB total)
                    // and shared across engine lifetimes.
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
            // _model intentionally not disposed: FullOcrModel is a lightweight wrapper
            // around cached file paths; disposing it would just invalidate references.
        }
        finally { _gate.Release(); }
        _gate.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PaddleOcrEngine));
    }
}