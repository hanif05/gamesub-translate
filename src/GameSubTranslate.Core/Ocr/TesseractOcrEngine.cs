using Tesseract;

namespace GameSubTranslate.Ocr;

/// <summary>
/// Tesseract wrapper. One TesseractEngine instance is reused per process
/// (it's expensive to load tessdata).
/// </summary>
public sealed class TesseractOcrEngine : IOcrEngine, IDisposable
{
    private readonly TesseractEngine _engine;
    private readonly object _lock = new();

    public TesseractOcrEngine(string? tessdataPath = null, string lang = "eng")
    {
        // Built output ships traineddata under a tessdata/ subfolder (see .csproj Content Link).
        var path = tessdataPath ?? Path.Combine(AppContext.BaseDirectory, "tessdata");
        _engine = new TesseractEngine(path, lang, EngineMode.Default);
    }

    public string Recognize(byte[] pngBytes)
    {
        lock (_lock)
        {
            using var img = Pix.LoadFromMemory(pngBytes);
            using var page = _engine.Process(img);
            return page.GetText().Trim();
        }
    }

    public void Dispose() => _engine.Dispose();
}
