using GameSubTranslate.Capture;
using GameSubTranslate.Config;
using GameSubTranslate.Ocr;
using GameSubTranslate.Translation;

namespace GameSubTranslate.Pipeline;

/// <summary>
/// Fase 1 end-to-end pipeline. Refactored into a service in T16 (Fase 2).
/// Kept minimal here so Prototype can still run as a CLI smoke test.
/// </summary>
public sealed class TranslatePipeline
{
    private readonly int _x, _y, _w, _h, _intervalMs;
    private readonly IOcrEngine _ocr;
    private readonly TranslationClient? _translator;
    private readonly bool _translationEnabled;

    public TranslatePipeline(int x, int y, int w, int h, int intervalMs, IOcrEngine ocr, AppConfig cfg)
    {
        _x = x; _y = y; _w = w; _h = h; _intervalMs = intervalMs;
        _ocr = ocr;
        _translationEnabled = cfg.TranslationEnabled;
        _translator = _translationEnabled
            ? new TranslationClient(cfg.ApiKey!, cfg.BaseUrl!, cfg.Model!, cfg.SourceLang, cfg.TargetLang)
            : null;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var capture = ScreenCapture.ForMonitorAt(_x, _y);
        byte[]? lastPng = null;
        string? lastText = null;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                byte[] png = capture.CaptureRegion(_x, _y, _w, _h);

                if (!ChangeDetector.IsChanged(png, lastPng))
                {
                    await Task.Delay(_intervalMs, ct);
                    continue;
                }
                lastPng = png;

                string text = _ocr.Recognize(png);
                if (string.IsNullOrWhiteSpace(text) || text == lastText)
                {
                    await Task.Delay(_intervalMs, ct);
                    continue;
                }
                lastText = text;

                string? translated = null;
                if (_translator is not null)
                {
                    try { translated = await _translator.TranslateAsync(text, ct); }
                    catch (Exception ex) { Console.Error.WriteLine($"[translate-error] {ex.GetType().Name}: {ex.Message}"); }
                }

                var ts = DateTime.Now.ToString("HH:mm:ss.fff");
                Console.WriteLine($"{ts} | src: {text} | dst: {translated ?? "<skipped>"}");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[tick-error] {ex.GetType().Name}: {ex.Message}");
            }

            await Task.Delay(_intervalMs, ct);
        }
    }
}
