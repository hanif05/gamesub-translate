using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace GameSubTranslate.Prototype.Capture;

/// <summary>
/// Fase 1 capture: GDI+ CopyFromScreen. Bounded by virtual screen bounds.
/// Replace with Windows.Graphics.Capture in Fase 2.
/// </summary>
public static class ScreenCapture
{
    public static byte[] CaptureRegion(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0)
            throw new ArgumentException($"Invalid region size: {w}x{h}");

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
