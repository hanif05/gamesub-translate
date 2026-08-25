using GameSubTranslate.Config;

namespace GameSubTranslate.Ocr;

/// <summary>T38: builds an <see cref="IOcrEngine"/> from settings. Callers pick the engine,
/// we wire the config. Unknown kind falls back to Tesseract so a stale persisted value
/// never crashes startup. F82: PaddleOcr case wires <see cref="PaddleOcrEngine"/> with the
/// user's GPU toggle from <see cref="AppConfig.PaddleUseGpu"/>.</summary>
public static class OcrEngineFactory
{
    public static IOcrEngine Create(OcrEngineKind kind, AppConfig cfg, HttpMessageHandler? handler = null)
    {
        return kind switch
        {
            OcrEngineKind.VisionAi when VisionAiOcrEngine.TryCreate(cfg, handler) is { } v => v,
            OcrEngineKind.PaddleOcr => new PaddleOcrEngine(cfg.PaddleUseGpu),
            _ => new TesseractOcrEngine(),
        };
    }
}