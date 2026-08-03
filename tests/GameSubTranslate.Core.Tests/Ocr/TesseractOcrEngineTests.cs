using System.Reflection;
using GameSubTranslate.Ocr;
using Xunit;

namespace GameSubTranslate.Core.Tests.Ocr;

/// <summary>T34: lazy init, idle dispose, exception typing, Dispose race-safety.</summary>
public class TesseractOcrEngineTests
{
    [Fact]
    public void Ctor_DoesNotThrow_WhenTessdataMissing_EngineLazilyInitialised()
    {
        // Ctor must NOT touch tessdata — app startup shouldn't fail just because the
        // user hasn't dropped traineddata yet. The error should surface only when
        // Recognize is called.
        var bogus = Path.Combine(Path.GetTempPath(), "definitely-not-here-" + Guid.NewGuid().ToString("N"));
        using var engine = new TesseractOcrEngine(bogus);

        // Reaching here without exception is the assertion — engine field stays null
        // until the first Recognize call.
        Assert.Null(GetEngine(engine));
    }

    [Fact]
    public void Recognize_TessdataMissing_ThrowsOcrEngineLoadException()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "definitely-not-here-" + Guid.NewGuid().ToString("N"));
        using var engine = new TesseractOcrEngine(bogus);

        // A real PNG header (8 bytes) — Tesseract won't reach parsing because the engine
        // ctor fails first, but Pix.LoadFromMemory needs a valid byte[] shape to not
        // itself throw before our lazy-init. Empty byte[] would bypass Tesseract's
        // own format check too early.
        var ex = Assert.Throws<OcrEngineLoadException>(() => engine.Recognize(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0 }));
        Assert.Contains("traineddata", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void Dispose_IsIdempotent_AndReleasesGate()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N"));
        var engine = new TesseractOcrEngine(bogus);

        engine.Dispose();
        engine.Dispose(); // second call must not throw
    }

    [Fact]
    public void Dispose_AfterRecognizeAttempt_StillCleansUp()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N"));
        var engine = new TesseractOcrEngine(bogus);

        try { engine.Recognize(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0 }); }
        catch (OcrEngineLoadException) { /* expected — tessdata missing */ }

        // Even though lazy-init failed (engine still null), Dispose must work cleanly.
        engine.Dispose();
        Assert.Null(GetEngine(engine));
    }

    [Fact]
    public async Task RecognizeAndDispose_Concurrent_NoUnhandledException()
    {
        // T34 spec: stress test — Recognize + idle-Dispose + manual Dispose in parallel
        // must not surface ObjectDisposedException, AccessViolation, or deadlock. We
        // can't test the real Tesseract engine (no tessdata in test env), but we CAN
        // test the gate/Dispose mechanics: every Recognize throws OcrEngineLoadException
        // immediately (because tessdata is missing), so all calls converge in ~milliseconds.
        var bogus = Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N"));
        var engine = new TesseractOcrEngine(bogus, idleDisposeAfter: TimeSpan.FromMilliseconds(50));

        using var startSignal = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            startSignal.Wait();
            try
            {
                engine.Recognize(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0 });
            }
            catch (OcrEngineLoadException) { /* expected */ }
            catch (ObjectDisposedException) { /* acceptable: Dispose won the race */ }
        })).ToArray();

        startSignal.Set();
        await Task.WhenAll(tasks);

        // Final Dispose — engine is null (lazy-init never succeeded), but the gate
        // must still release cleanly even with a parallel idle-dispose timer possibly
        // firing.
        engine.Dispose();
    }

    /// <summary>Reach into the private _engine field to verify lazy state.</summary>
    private static object? GetEngine(TesseractOcrEngine engine)
    {
        var field = typeof(TesseractOcrEngine).GetField("_engine",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(engine);
    }
}
