using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Graphics.Canvas;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace GameSubTranslate.Capture;

/// <summary>
/// Fase 2 capture: Windows.Graphics.Capture. One instance per monitor, captures the whole
/// monitor surface, caller crops to a region. Encodes the crop to PNG bytes.
///
/// Output contract (byte[] PNG) matches Fase 1 so callers (pipeline, self-checks) stay the same.
///
/// Uses Win2D's shared CanvasDevice (implements IDirect3DDevice) for both the frame pool and
/// bitmap readback — no SharpDX, no manual D3D11 device creation.
///
/// ponytail: Win2D's only Save API is SaveAsync, so PNG encode falls back to System.Drawing
/// (explicitly allowed by the T10 spec). If a future task needs higher throughput, switch to
/// SaveAsync on the GPU bitmap — the crop math stays identical.
/// </summary>
public sealed class ScreenCapture : IDisposable
{
    private readonly Direct3D11CaptureFramePool _framePool;
    private readonly GraphicsCaptureSession _session;
    private readonly CanvasDevice _canvasDevice;
    private readonly AutoResetEvent _frameReady = new(false);
    private readonly int _monLeft, _monTop;

    private ScreenCapture(GraphicsCaptureItem item, Rectangle monRect)
    {
        _monLeft = monRect.Left;
        _monTop = monRect.Top;
        _canvasDevice = CanvasDevice.GetSharedDevice();
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _canvasDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        _session = _framePool.CreateCaptureSession(item);
        // Frames are pushed asynchronously (≈1 vsync after StartCapture). FrameArrived on the
        // free-threaded pool runs on a worker thread; we just flag an event to block on.
        _framePool.FrameArrived += OnFrameArrived;
        _session.StartCapture();
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args) => _frameReady.Set();

    /// <summary>
    /// Create a capture session for the monitor that contains screen point (screenX, screenY).
    /// Coordinates are in virtual-screen pixels (negative allowed for monitors left/above primary).
    /// </summary>
    public static ScreenCapture ForMonitorAt(int screenX, int screenY)
    {
        var mon = MonitorFromPoint(new POINT { x = screenX, y = screenY }, MONITOR_DEFAULTTONEAREST);
        if (mon == IntPtr.Zero)
            throw new InvalidOperationException($"MonitorFromPoint returned null for ({screenX},{screenY})");
        return ForMonitor(mon);
    }

    /// <summary>Create a capture session for the given monitor handle.</summary>
    public static ScreenCapture ForMonitor(IntPtr hmon) =>
        new(GraphicsCaptureItemInterop.CreateForMonitor(hmon), MonitorRect(hmon));

    /// <summary>
    /// Capture one frame of the whole monitor and return the (x,y,w,h) region cropped + PNG-encoded.
    /// (x,y,w,h) are virtual-screen coordinates; must be inside this session's monitor.
    /// </summary>
    public byte[] CaptureRegion(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0)
            throw new ArgumentException($"Invalid region size: {w}x{h}");

        _frameReady.WaitOne(2000); // wait for the async push (falls through if already signalled)
        using var frame = _framePool.TryGetNextFrame();
        if (frame is null)
            throw new InvalidOperationException("GraphicsCapture returned no frame");

        // Frame surface covers the full monitor starting at (0,0) in monitor-relative pixels.
        int ox = x - _monLeft;
        int oy = y - _monTop;

        using var bitmap = CanvasBitmap.CreateFromDirect3D11Surface(_canvasDevice, frame.Surface);

        int bx = Math.Clamp(ox, 0, (int)bitmap.SizeInPixels.Width);
        int by = Math.Clamp(oy, 0, (int)bitmap.SizeInPixels.Height);
        int bw = Math.Min(w, (int)bitmap.SizeInPixels.Width - bx);
        int bh = Math.Min(h, (int)bitmap.SizeInPixels.Height - by);
        if (bw <= 0 || bh <= 0)
            return Array.Empty<byte>();

        // Tight-packed BGRA region bytes (row stride = bw * 4).
        byte[] regionBytes = bitmap.GetPixelBytes(bx, by, bw, bh);

        using var ms = new MemoryStream();
        using (var bmp = new Bitmap(bw, bh, PixelFormat.Format32bppArgb))
        {
            // Format32bppArgb memory order = B,G,R,A — matches B8G8R8A8.
            var rect = new Rectangle(0, 0, bw, bh);
            var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                Marshal.Copy(regionBytes, 0, data.Scan0, regionBytes.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            bmp.Save(ms, ImageFormat.Png);
        }
        return ms.ToArray();
    }

    public void Dispose()
    {
        _framePool.FrameArrived -= OnFrameArrived;
        _session?.Dispose();
        _framePool?.Dispose();
        _canvasDevice?.Dispose();
        _frameReady.Dispose();
    }

    // ---- Win32 + interop ----

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    private static Rectangle MonitorRect(IntPtr hmon)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfoW(hmon, ref mi))
            return new Rectangle(0, 0, 0, 0);
        return new Rectangle(mi.rcMonitor.Left, mi.rcMonitor.Top,
            mi.rcMonitor.Right - mi.rcMonitor.Left, mi.rcMonitor.Bottom - mi.rcMonitor.Top);
    }

    /// <summary>P/Invoke shim for the Windows.Graphics.Capture.Interop COM activation factory.</summary>
    internal static class GraphicsCaptureItemInterop
    {
        [ComImport]
        [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            IntPtr CreateForWindow(IntPtr hwnd, in Guid iid);
            IntPtr CreateForMonitor(IntPtr hmon, in Guid iid);
        }

        // WinRT class IID for GraphicsCaptureItem (from Windows.Graphics.Capture.Interop header) —
        // NOT typeof(GraphicsCaptureItem).GUID, which is the projected-type IID and fails the
        // QueryInterface in CreateForMonitor with E_NOINTERFACE.
        private static readonly Guid ItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

        public static GraphicsCaptureItem CreateForMonitor(IntPtr hmon)
        {
            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            IntPtr ptr = interop.CreateForMonitor(hmon, ItemGuid);
            if (ptr == IntPtr.Zero)
                throw new InvalidOperationException("CreateForMonitor returned null");
            var item = GraphicsCaptureItem.FromAbi(ptr);
            Marshal.Release(ptr);
            return item;
        }
    }
}
