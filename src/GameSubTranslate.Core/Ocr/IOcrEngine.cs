namespace GameSubTranslate.Ocr;

/// <summary>
/// T38: OCR engine abstraction. Async so HTTP-backed engines (Vision AI) don't deadlock
/// the WPF UI thread. Tesseract (sync, native) is wrapped via Task.Run to keep the same
/// async surface. Implementations must be safe to Dispose once; all public methods
/// thread-safe.
/// </summary>
public interface IOcrEngine
{
    /// <summary>Recognize text from a PNG frame. Must not block the caller's thread for long.</summary>
    Task<string> RecognizeAsync(byte[] pngBytes, CancellationToken ct = default);
    void Dispose();
}