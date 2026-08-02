namespace GameSubTranslate.Capture;

/// <summary>
/// Surface captured by the pipeline. Implementation lives over Windows.Graphics.Capture;
/// the interface exists so the pipeline can run against a fake capture in self-checks
/// (no real screen, no real game) and so tests can inject a scripted region.
/// </summary>
public interface IScreenCapture : IDisposable
{
    /// <summary>Capture the (x,y,w,h) virtual-screen region and return it as PNG bytes.</summary>
    byte[] CaptureRegion(int x, int y, int w, int h);
}
