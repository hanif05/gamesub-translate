using GameSubTranslate.Prototype.Capture;
using GameSubTranslate.Prototype.Config;
using GameSubTranslate.Prototype.Ocr;
using GameSubTranslate.Prototype.Translation;

namespace GameSubTranslate.Prototype.Pipeline;

public sealed class TranslatePipeline
{
    private readonly CliArgs _args;
    private readonly IOcrEngine _ocr;
    private readonly TranslationClient? _translator;
    private readonly bool _translationEnabled;

    public TranslatePipeline(CliArgs args, IOcrEngine ocr, AppConfig cfg)
    {
        _args = args;
        _ocr = ocr;
        _translationEnabled = cfg.TranslationEnabled;
        _translator = _translationEnabled
            ? new TranslationClient(cfg.ApiKey!, cfg.BaseUrl!, cfg.Model!, cfg.SourceLang, cfg.TargetLang)
            : null;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        byte[]? lastPng = null;
        string? lastText = null;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                byte[] png = ScreenCapture.CaptureRegion(_args.X, _args.Y, _args.W, _args.H);

                if (!ChangeDetector.IsChanged(png, lastPng))
                {
                    await Task.Delay(_args.IntervalMs, ct);
                    continue;
                }
                lastPng = png;

                string text = _ocr.Recognize(png);
                if (string.IsNullOrWhiteSpace(text) || text == lastText)
                {
                    await Task.Delay(_args.IntervalMs, ct);
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

            await Task.Delay(_args.IntervalMs, ct);
        }
    }
}
