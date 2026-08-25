using System.Reflection;
using GameSubTranslate.Ocr;
using Xunit;

namespace GameSubTranslate.Core.Tests.Ocr;

/// <summary>T84: PaddleOcrEngine lazy init, Recognize roundtrip, idle dispose, exception typing.
/// Mirrors TesseractOcrEngineTests — same surface, same expectations. The English model is
/// downloaded from PaddleOCR's online model repo on first init via
/// OnlineFullModels.EnglishV3.DownloadAsync() and cached on disk — subsequent runs hit the
/// cache, no network needed.</summary>
public class PaddleOcrEngineTests
{
    [Fact]
    public void Ctor_DoesNotThrow_NativeLazyInit()
    {
        // Ctor must NOT touch the native stack — that mirrors Tesseract's contract and keeps
        // the factory swap cheap (creating an engine you never use costs nothing).
        using var engine = new PaddleOcrEngine();

        Assert.Null(GetPaddleEngine(engine));
    }

    [Fact]
    public async Task RecognizeAsync_ReturnsText_ForSyntheticSubtitle()
    {
        // Real PNG that Paddle can actually parse (Paddle doesn't bail on a 8-byte stub
        // the way Tesseract does — it tries to decode the image). 800x80 white bg, black
        // text, default Arial — same as the T80 spike. First call downloads the model,
        // so allow a long timeout for the cold init.
        using var engine = new PaddleOcrEngine();
        var png = MakeSubtitleFrame("The quick brown fox jumps over the lazy dog", 800, 80);

        var text = await engine.RecognizeAsync(png).WaitAsync(TimeSpan.FromMinutes(2));

        // Paddle normalizes inter-region newlines to spaces, then Trim()s. Assert at
        // least one known word survives — the model is multi-lingual but English should
        // still pass cleanly on Arial.
        Assert.False(string.IsNullOrWhiteSpace(text), $"OCR returned empty text");
        Assert.Contains("quick", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecognizeAsync_SecondCall_UsesWarmEngine()
    {
        // After first Recognize, the engine field is populated. Second call must succeed
        // (catches the case where dispose accidentally nulls the engine on every call).
        using var engine = new PaddleOcrEngine();
        var png = MakeSubtitleFrame("Hello world", 400, 60);

        await engine.RecognizeAsync(png).WaitAsync(TimeSpan.FromMinutes(2));
        Assert.NotNull(GetPaddleEngine(engine));

        var second = await engine.RecognizeAsync(png);
        Assert.False(string.IsNullOrWhiteSpace(second));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        // Same contract as Tesseract — Dispose twice must not throw. App shutdown and
        // the idle-timer can both fire Dispose; we don't want a double-fire crash.
        var engine = new PaddleOcrEngine();
        engine.Dispose();
        engine.Dispose();
    }

    [Fact]
    public async Task Dispose_AfterRecognize_CleansUpNativeHandle()
    {
        // Engine must be nulled after Dispose even when warm — confirms the engine's
        // native resources are released, not leaked. We don't introspect native state
        // (no public API for that); checking the field is enough.
        using var engine = new PaddleOcrEngine();
        var png = MakeSubtitleFrame("Test", 200, 40);
        await engine.RecognizeAsync(png).WaitAsync(TimeSpan.FromMinutes(2));

        engine.Dispose();
        Assert.Null(GetPaddleEngine(engine));
    }

    /// <summary>Reach into the private _engine field to verify lazy state, same pattern
    /// as TesseractOcrEngineTests.</summary>
    private static object? GetPaddleEngine(PaddleOcrEngine engine)
    {
        var field = typeof(PaddleOcrEngine).GetField("_engine",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(engine);
    }

    private static byte[] MakeSubtitleFrame(string text, int w, int h)
    {
        using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.White);
            using var font = new System.Drawing.Font("Arial", 28f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Black);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.DrawString(text, font, brush, 10, 20);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }
}
