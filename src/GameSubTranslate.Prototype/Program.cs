using System.Drawing;
using System.Drawing.Imaging;
using GameSubTranslate.Prototype.Ocr;

// Generate a sample image with text to OCR
using (var bmp = new Bitmap(400, 80))
{
    using (var g = Graphics.FromImage(bmp))
    {
        g.Clear(Color.White);
        using var font = new Font(FontFamily.GenericSansSerif, 24, FontStyle.Regular);
        g.DrawString("Hello world 123", font, Brushes.Black, 10, 20);
    }
    bmp.Save("sample.png", ImageFormat.Png);
}

byte[] png = File.ReadAllBytes("sample.png");
using var engine = new TesseractOcrEngine();
string text = engine.Recognize(png);
Console.WriteLine($"OCR: '{text}' (len={text.Length})");
