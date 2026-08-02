using System.Drawing;

namespace GameSubTranslate.Pipeline;

public static class ChangeDetector
{
    /// <summary>
    /// Returns false if the two PNGs decode to identical pixel grids.
    /// First capture has no prior — caller passes null and we report "changed".
    /// </summary>
    public static bool IsChanged(byte[]? newPng, byte[]? lastPng)
    {
        if (newPng is null) return false;
        if (lastPng is null) return true;

        using var a = Image.FromStream(new MemoryStream(newPng));
        using var b = Image.FromStream(new MemoryStream(lastPng));
        if (a.Width != b.Width || a.Height != b.Height) return true;

        // Compare 32bpp pixel buffers
        using var ba = new Bitmap(a);
        using var bb = new Bitmap(b);
        var rect = new Rectangle(0, 0, ba.Width, ba.Height);
        var da = ba.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var db = bb.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            int len = Math.Abs(da.Stride) * ba.Height;
            var pa = new byte[len];
            var pb = new byte[len];
            System.Runtime.InteropServices.Marshal.Copy(da.Scan0, pa, 0, len);
            System.Runtime.InteropServices.Marshal.Copy(db.Scan0, pb, 0, len);
            return !pa.AsSpan().SequenceEqual(pb);
        }
        finally
        {
            ba.UnlockBits(da);
            bb.UnlockBits(db);
        }
    }
}
