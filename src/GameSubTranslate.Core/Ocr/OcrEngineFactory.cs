using GameSubTranslate.Config;

namespace GameSubTranslate.Ocr;

/// <summary>T38: builds an <see cref="IOcrEngine"/> from settings. Callers pick the engine,
/// we wire the config. Unknown kind falls back to Tesseract so a stale persisted value
/// never crashes startup.</summary>
public static class OcrEngineFactory
{
    public static IOcrEngine Create(OcrEngineKind kind, AppConfig cfg, HttpMessageHandler? handler = null)
    {
        return kind switch
        {
            OcrEngineKind.VisionAi when VisionAiOcrEngine.TryCreate(cfg, handler) is { } v => v,
            _ => new TesseractOcrEngine(),
        };
    }
}