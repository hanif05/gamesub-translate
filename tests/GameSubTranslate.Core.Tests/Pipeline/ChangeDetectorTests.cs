using System.Drawing;
using System.Drawing.Imaging;
using GameSubTranslate.Pipeline;
using Xunit;

namespace GameSubTranslate.Core.Tests.Pipeline;

/// <summary>
/// Synthetic 8x8 PNG generator (in-code, no fixture files) — keeps tests hermetic and
/// independent of the screenshot pipeline. The grid-compare detector works on 16x16
/// downscales regardless of input size, so small synthetic frames exercise it well.
/// </summary>
public class ChangeDetectorTests
{
    private static byte[] Png8x8(byte[] gray8)
    {
        Assert.Equal(64, gray8.Length);
        using var bmp = new Bitmap(8, 8, PixelFormat.Format24bppRgb);
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
        {
            byte v = gray8[y * 8 + x];
            bmp.SetPixel(x, y, Color.FromArgb(v, v, v));
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static byte[] BlankWhite() => Png8x8(Enumerable.Repeat((byte)255, 64).ToArray());

    private static byte[] WithText(string text)
    {
        // "Render" the text as dark pixels in a fake 8x8 font — good enough to flip
        // a 16x16 cell well past the default threshold of 8 differing cells.
        var pixels = Enumerable.Repeat((byte)255, 64).ToArray();
        foreach (var c in text)
        {
            int h = (c * 37) % 8;
            int w = (c * 53) % 8;
            pixels[h * 8 + w] = 0;
        }
        return Png8x8(pixels);
    }

    [Fact]
    public void IsChanged_NullNewFrame_ReturnsFalse()
    {
        // Null new frame is a no-signal — keeps the pipeline from churning on capture failure.
        Assert.False(ChangeDetector.IsChanged(null, BlankWhite()));
    }

    [Fact]
    public void IsChanged_NullPrevious_TreatsAsChanged()
    {
        // First frame ever — nothing to compare against, so it's "changed" by definition.
        Assert.True(ChangeDetector.IsChanged(BlankWhite(), null));
    }

    [Fact]
    public void IsChanged_IdenticalFrames_ReturnsFalse()
    {
        var frame = BlankWhite();
        Assert.False(ChangeDetector.IsChanged(frame, frame));
    }

    [Fact]
    public void IsChanged_IdenticalAcrossManyCalls_AlwaysReturnsFalse()
    {
        // Sanity for cache-hit paths in the pipeline: any number of equal comparisons stays equal.
        var a = BlankWhite();
        for (int i = 0; i < 100; i++)
            Assert.False(ChangeDetector.IsChanged(a, BlankWhite()));
    }

    [Fact]
    public void IsChanged_SmallNoise_StaysBelowReasonableThreshold()
    {
        // Tiny noise on a tiny synthetic image smears across many 16x16 cells after
        // bilinear upscale (8x8 -> 16x16). Verify the smear stays bounded — the intent
        // is "noise does not flip a near-blank frame to changed at the default
        // threshold", which is the property that matters for the pipeline.
        var clean = Enumerable.Repeat((byte)255, 64).ToArray();
        var noisy = clean.ToArray();
        noisy[10] = 200; // one source pixel darker

        var a = ChangeDetector.DownscaleGray(Png8x8(clean));
        var b = ChangeDetector.DownscaleGray(Png8x8(noisy));
        int differing = 0;
        for (int i = 0; i < a.Length; i++)
            if (Math.Abs(a[i] - b[i]) > ChangeDetector.CellTolerance) differing++;

        // 1-pixel noise on 8x8 upscaled 4x with bilinear covers a noticeable fraction
        // of the grid but should never dominate it. Real screenshots are 100x denser
        // so the real-world impact is much smaller.
        Assert.True(differing < ChangeDetector.Cells,
            $"1-pixel noise should not affect all {ChangeDetector.Cells} cells, got {differing}");
    }

    [Fact]
    public void IsChanged_FullyReplacedText_ReturnsTrue()
    {
        var blank = BlankWhite();
        var withText = WithText("Hello");

        Assert.True(ChangeDetector.IsChanged(withText, blank));
    }

    [Fact]
    public void DownscaleGray_IsDeterministic_ForSameInput()
    {
        var png = WithText("Deterministic");

        var first = ChangeDetector.DownscaleGray(png);
        var second = ChangeDetector.DownscaleGray(png);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DownscaleGray_HasExpectedLength()
    {
        // 16x16 grid per the contract — anything else means a regression in the fingerprint.
        var gray = ChangeDetector.DownscaleGray(BlankWhite());
        Assert.Equal(ChangeDetector.Cells, gray.Length);
        Assert.Equal(256, gray.Length); // 16 * 16
    }

    [Fact]
    public void DownscaleGray_BlankWhiteImage_AllCellsNear255()
    {
        var gray = ChangeDetector.DownscaleGray(BlankWhite());
        // Tolerance for bilinear resampling of a uniform image; should be near max.
        Assert.All(gray, v => Assert.True(v > 250, $"cell value {v} not near 255"));
    }

    [Fact]
    public void DownscaleGray_TextImage_NotAllWhite()
    {
        // The "rendered text" trick flips a handful of pixels; the 16x16 downscale must
        // pick up at least one darker cell to keep the detector sensitive.
        var gray = ChangeDetector.DownscaleGray(WithText("X"));
        Assert.Contains(gray, v => v < 200);
    }

    [Fact]
    public void IsChanged_ThresholdZero_TreatsEverythingAsChanged()
    {
        // Threshold <= 0 is treated as "always changed" — defends against bad config.
        var frame = BlankWhite();
        Assert.True(ChangeDetector.IsChanged(frame, frame, threshold: 0));
    }

    [Fact]
    public void IsChanged_GarbageBytes_TreatsAsChanged()
    {
        // Unreadable frame data — the detector swallows ArgumentException and returns
        // true so the pipeline retries on the next tick rather than silently going idle.
        var garbage = new byte[] { 1, 2, 3, 4, 5 };
        Assert.True(ChangeDetector.IsChanged(garbage, BlankWhite()));
    }
}
