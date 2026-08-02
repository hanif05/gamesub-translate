using System.Drawing;
using GameSubTranslate.Prototype.Capture;

Console.WriteLine("hello");

var png = ScreenCapture.CaptureRegion(0, 0, 200, 100);
File.WriteAllBytes("test.png", png);

using var img = Image.FromFile("test.png");
Console.WriteLine($"test.png: {img.Width}x{img.Height}, {png.Length} bytes");
