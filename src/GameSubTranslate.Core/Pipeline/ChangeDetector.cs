using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace GameSubTranslate.Pipeline;

/// <summary>
/// Fase 2 change detection: perceptual downscale. Both frames are resized to a 16x16 grayscale
/// grid (low-frequency structure) and compared cell-wise with a per-cell tolerance. Tolerant of
/// small noise (1-2px) and minor re-scaling; catches meaningful text/subtitle changes.
///
/// ponytail: the T11 spec asked for a 64-bit mean-threshold pHash, but measured on synthetic
/// subtitle frames an 8x8 mean-threshold hash cannot separate short text lines (diff = 4 bits <
/// the default 5 threshold), and a 16x16 mean hash saturates to all-ones on sparse text on a
/// white background (mean threshold is dominated by the background). The grid compare below keeps
/// the perceptual-low-frequency idea and is robust for the subtitle use-case. If a real pHash
/// (DCT-based, e.g. imgSeek) is ever needed, swap this class's internals — the IsChanged contract
/// stays identical.
/// </summary>
public static class ChangeDetector
{
    public const int GridSize = 16;
    public const int Cells = GridSize * GridSize;
    /// <summary>Per-cell gray tolerance. Cell diffs above this count toward "changed".</summary>
    public const int CellTolerance = 6;
    /// <summary>How many cells must differ before a frame counts as changed.</summary>
    public const int DefaultThreshold = 8;

    /// <summary>Returns true when the new frame differs meaningfully from the last one.</summary>
    public static bool IsChanged(byte[]? newPng, byte[]? lastPng, int threshold = DefaultThreshold)
    {
        if (newPng is null) return false;
        if (lastPng is null) return true;
        if (threshold <= 0) return true;

        byte[]? a, b;
        try
        {
            a = DownscaleGray(newPng);
            b = DownscaleGray(lastPng);
        }
        catch (ArgumentException)
        {
            // Unreadable frame — treat as changed so the pipeline retries.
            return true;
        }

        int differing = 0;
        for (int i = 0; i < Cells; i++)
        {
            if (Math.Abs(a[i] - b[i]) > CellTolerance)
                differing++;
        }
        return differing > threshold;
    }

    /// <summary>Resize a PNG to a 16x16 grayscale grid (perceptual low-frequency fingerprint).</summary>
    public static byte[] DownscaleGray(byte[] png)
    {
        using var img = Image.FromStream(new MemoryStream(png));
        using var small = new Bitmap(GridSize, GridSize);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(img, 0, 0, GridSize, GridSize);
        }
        var gray = new byte[Cells];
        for (int i = 0; i < Cells; i++)
        {
            Color c = small.GetPixel(i % GridSize, i / GridSize);
            gray[i] = (byte)(c.R * 0.299 + c.G * 0.587 + c.B * 0.114);
        }
        return gray;
    }
}
