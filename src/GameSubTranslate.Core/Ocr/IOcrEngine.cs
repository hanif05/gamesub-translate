namespace GameSubTranslate.Ocr;

public interface IOcrEngine
{
    string Recognize(byte[] pngBytes);
}
