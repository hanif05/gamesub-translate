namespace GameSubTranslate.Prototype.Ocr;

public interface IOcrEngine
{
    string Recognize(byte[] pngBytes);
}
