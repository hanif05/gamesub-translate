namespace GameSubTranslate.Config;

public enum OcrEngineKind
{
    Tesseract,
    VisionAi,
}

/// <summary>T40: one translation provider endpoint. Users can add a fallback so a dead primary
/// auto-switches to a backup (see TranslationClient failover). The legacy ApiKey/BaseUrl/Model
/// fields on AppSettings remain the primary provider for back-compat.</summary>
public sealed class ProviderConfig
{
    public string Name { get; set; } = "";
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
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
    /// <summary>T51: cycles TargetLang through id→en→ja→ko→zh→fr→de→es.</summary>
    public string HotkeyCycleTargetLang { get; set; } = "Ctrl+Alt+L";

    // Last-active state (T9) so the active region survives restarts.
    public int? ActiveProfileId { get; set; }
    public int? ActiveRegionId { get; set; }

    /// <summary>T40: fallback providers tried in order after the primary fails 3x consecutive
    /// (Network/Provider only — Auth/BadRequest/RateLimit never failover).</summary>
    public List<ProviderConfig> Providers { get; set; } = new();

    /// <summary>T45: false until the first-run welcome wizard is finished. Gates the wizard so
    /// returning users skip it.</summary>
    public bool SetupCompleted { get; set; }

    public bool TranslationEnabled =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(Model);
}
