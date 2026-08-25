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
    /// <summary>F87: per-profile Paddle GPU toggle. When OcrEngine == Tesseract (the
    /// ProfileEditWindow default) this field is ignored by the pipeline — the global
    /// AppSettings.PaddleUseGpu takes over. Only meaningful when the user explicitly
    /// sets OcrEngine = PaddleOcr on this profile.</summary>
    public bool PaddleUseGpu { get; set; }
    public int CaptureIntervalMs { get; set; } = 800;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<CaptureRegion> Regions { get; set; } = new();
}
