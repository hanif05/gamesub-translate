namespace GameSubTranslate.Config;

public enum OcrEngineKind
{
    Tesseract,
    VisionAi,
}

public sealed class AppSettings
{
    public string? ApiKey { get; set; }
    public string? BaseUrl { get; set; }
    public string? Model { get; set; }
    public string SourceLang { get; set; } = "auto";
    public string TargetLang { get; set; } = "id";
    public int CaptureIntervalMs { get; set; } = 800;
    public OcrEngineKind OcrEngine { get; set; } = OcrEngineKind.Tesseract;
    public string OverlayFontFamily { get; set; } = "Segoe UI";
    public double OverlayFontSize { get; set; } = 20;
    public string OverlayTextColor { get; set; } = "#FFFFFF";
    public string OverlayBgColor { get; set; } = "#CC000000";
    public double OverlayOpacity { get; set; } = 1.0;
    // Optional saved overlay position (T23 Pick Position); null → center-bottom on first show.
    public double? OverlayX { get; set; }
    public double? OverlayY { get; set; }
    public string HotkeyToggleOverlay { get; set; } = "Ctrl+Alt+T";
    public string HotkeyPauseCapture { get; set; } = "Ctrl+Alt+P";
    public string HotkeyOpenSettings { get; set; } = "Ctrl+Alt+S";
    public string HotkeyManualCapture { get; set; } = "Ctrl+Alt+Space";

    // Last-active state (T9) so the active region survives restarts.
    public int? ActiveProfileId { get; set; }
    public int? ActiveRegionId { get; set; }

    public bool TranslationEnabled =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(Model);
}
