// F80 (rewritten on the Sdcb stack): throwaway spike to validate Sdcb.PaddleInference
// on this hardware. Same shape as the original raoyutian spike (cold + 3-sample warm
// median) — only the API surface changed. Sdcb exposes PaddleDevice.Gpu() explicitly so
// RunGpu() actually exercises CUDA instead of silently falling back to CPU like the
// raoyutian free edition did.
using System.Diagnostics;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Online;

namespace GameSubTranslate.Prototype;

internal static class PaddleOcrSpike
{
    public static int Run()
    {
        // Synthetic subtitle: white bg, black text, no anti-alias tricks. Just enough to
        // exercise the pipeline end-to-end. Real subtitle images live in Settings.
        var png = MakeSubtitleFrame("The quick brown fox jumps over the lazy dog", w: 800, h: 80);

        try
        {
            Console.WriteLine("[paddle-spike] downloading EnglishV3 model (cached after first run) ...");
            FullOcrModel model = OnlineFullModels.EnglishV3.DownloadAsync().GetAwaiter().GetResult();
            var engine = new PaddleOcrAll(model, PaddleDevice.Mkldnn())
            {
                AllowRotateDetection = false,
                Enable180Classification = false,
            };
            try
            {
                // Cold run — first Recognize call loads model into RAM. Production lazy-inits
                // at first capture so app startup stays snappy; this is the worst case.
                var swCold = Stopwatch.StartNew();
                Recognize(engine, png);
                swCold.Stop();

                // Warm runs — engine + model already resident. These are what production sees
                // after the first dialog of every session. Take the median of 3 to dampen
                // single-call jitter (GC, thermal, OS scheduling).
                var samples = new long[3];
                for (int i = 0; i < samples.Length; i++)
                {
                    var sw = Stopwatch.StartNew();
                    Recognize(engine, png);
                    sw.Stop();
                    samples[i] = sw.ElapsedMilliseconds;
                }
                Array.Sort(samples);
                var medianWarm = samples[1];

                var finalText = Recognize(engine, png);

                Console.WriteLine($"[paddle-spike] cold={swCold.ElapsedMilliseconds}ms warmSamples=[{string.Join(",", samples)}]ms median={medianWarm}ms");
                Console.WriteLine($"[paddle-spike] text='{finalText}'");

                if (string.IsNullOrWhiteSpace(finalText))
                {
                    Console.Error.WriteLine("FAIL: PaddleOCR returned empty text");
                    return 1;
                }
                // Spec target: <200ms CPU or <100ms GPU. AMD CPU is not MKL-DNN's strong suit
                // (MKL is Intel-tuned), so we allow up to 400ms CPU as a realistic budget.
                // Past 400ms, Tesseract (~100ms warm) is competitive enough to skip this engine.
                if (medianWarm > 400)
                {
                    Console.Error.WriteLine($"FAIL: median warm latency {medianWarm}ms exceeds 400ms CPU budget — abort Fase 6 PaddleOCR path");
                    return 1;
                }
                Console.WriteLine("PASS: PaddleOCR (Sdcb) spike — init + 3 warm calls, latency within budget");
                return 0;
            }
            finally
            {
                engine.Dispose();
            }
        }
        catch (DllNotFoundException ex)
        {
            Console.Error.WriteLine($"FAIL: native runtime missing: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL: init/inference threw {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>T89 (rewritten): same shape as the CPU spike but flips device to Gpu(). Lets
    /// the user verify whether CUDA is actually accelerating on this host (vs silently
    /// falling back to CPU because the driver/runtime can't init). Persistent engine +
    /// 3-sample warm median.</summary>
    public static int RunGpu()
    {
        var png = MakeSubtitleFrame("The quick brown fox jumps over the lazy dog", w: 800, h: 80);

        try
        {
            Console.WriteLine("[paddle-gpu] downloading EnglishV3 model (cached after first run) ...");
            FullOcrModel model = OnlineFullModels.EnglishV3.DownloadAsync().GetAwaiter().GetResult();

            Console.WriteLine("[paddle-gpu] initializing with PaddleDevice.Gpu() ...");
            var engine = new PaddleOcrAll(model, PaddleDevice.Gpu())
            {
                AllowRotateDetection = false,
                Enable180Classification = false,
            };
            try
            {
                var swCold = Stopwatch.StartNew();
                Recognize(engine, png);
                swCold.Stop();

                var samples = new long[3];
                for (int i = 0; i < samples.Length; i++)
                {
                    var sw = Stopwatch.StartNew();
                    Recognize(engine, png);
                    sw.Stop();
                    samples[i] = sw.ElapsedMilliseconds;
                }
                Array.Sort(samples);
                var medianWarm = samples[1];

                var finalText = Recognize(engine, png);

                Console.WriteLine($"[paddle-gpu] cold={swCold.ElapsedMilliseconds}ms warmSamples=[{string.Join(",", samples)}]ms median={medianWarm}ms");
                Console.WriteLine($"[paddle-gpu] text='{finalText}'");

                if (string.IsNullOrWhiteSpace(finalText))
                {
                    Console.Error.WriteLine("FAIL: GPU PaddleOCR returned empty text");
                    return 1;
                }
                // GPU target <100ms; CPU expected 200-300ms. If GPU returns >200ms, the
                // Gpu() device spec was probably rejected and we silently fell back to CPU
                // — or the native stack doesn't actually have CUDA inside it.
                Console.WriteLine(medianWarm < 150
                    ? "PASS: GPU path active (warm <150ms — likely CUDA)."
                    : $"INFO: GPU flag set but warm median {medianWarm}ms > 150ms — driver/runtime may have fallen back to CPU. Check CUDA Toolkit install + nvidia-smi output.");
                return 0;
            }
            finally { engine.Dispose(); }
        }
        catch (DllNotFoundException ex)
        {
            Console.Error.WriteLine($"FAIL: native runtime missing: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            // Catches CUDA init failures: Sdcb native throws InvalidOperationException or
            // similar when the driver isn't usable. Print the message — that's the actual
            // signal the operator needs to fix the install.
            Console.Error.WriteLine($"FAIL: GPU init / inference threw {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static string Recognize(PaddleOcrAll engine, byte[] pngBytes)
    {
        using var mat = Cv2.ImDecode(pngBytes, ImreadModes.Color);
        if (mat.Empty()) return string.Empty;
        var result = engine.Run(mat);
        return (result?.Text ?? "").Replace('\n', ' ').Trim();
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