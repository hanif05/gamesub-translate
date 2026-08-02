using System.Drawing;
using System.Drawing.Imaging;
using GameSubTranslate.Prototype.Capture;
using GameSubTranslate.Prototype.Pipeline;

// 1) First capture has no prior -> changed
byte[] a = ScreenCapture.CaptureRegion(0, 0, 200, 100);
byte[] b = ScreenCapture.CaptureRegion(0, 0, 200, 100);

Console.WriteLine($"byte lengths: a={a.Length} b={b.Length}");
Console.WriteLine($"byte-equal: {a.AsSpan().SequenceEqual(b)}");
Console.WriteLine($"IsChanged(a, null): {ChangeDetector.IsChanged(a, null)} (expect True)");
Console.WriteLine($"IsChanged(b, a) [identical]: {ChangeDetector.IsChanged(b, a)} (expect False)");

// Make a different capture by drawing into a region that includes the time-tick of a clock
byte[] c = ScreenCapture.CaptureRegion(100, 100, 200, 100);
Console.WriteLine($"IsChanged(c, b) [different region]: {ChangeDetector.IsChanged(c, b)} (expect True)");

// Stress: 100x identical comparisons stay consistent
int flips = 0;
for (int i = 0; i < 100; i++) if (ChangeDetector.IsChanged(b, a)) flips++;
Console.WriteLine($"100x stress flips: {flips} (expect 0)");
