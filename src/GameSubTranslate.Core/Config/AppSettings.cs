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

    // T33: adaptive capture interval. After IdleActivationThreshold frames with no change,
    // the loop backs off to IdleCaptureIntervalMs (default 3000) to save CPU while the
    // subtitle is still. Any change resets to CaptureIntervalMs.
    public int IdleCaptureIntervalMs { get; set; } = 3000;
    public int IdleActivationThreshold { get; set; } = 3;
    // Window in ms before idle mode engages even if threshold isn't met — keeps the loop
    // responsive when frames are technically different (anti-aliasing jitter) but semantically idle.
    public int IdleActivationWindowMs { get; set; } = 5000;

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
