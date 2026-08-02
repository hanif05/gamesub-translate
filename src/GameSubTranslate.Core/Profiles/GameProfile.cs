using GameSubTranslate.Config;

namespace GameSubTranslate.Profiles;

public sealed class GameProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? ExecutableName { get; set; }
    public string SourceLang { get; set; } = "auto";
    public string TargetLang { get; set; } = "id";
    public OcrEngineKind OcrEngine { get; set; } = OcrEngineKind.Tesseract;
    public int CaptureIntervalMs { get; set; } = 800;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<CaptureRegion> Regions { get; set; } = new();
}
