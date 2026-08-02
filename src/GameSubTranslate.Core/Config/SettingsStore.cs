using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GameSubTranslate.Config;

/// <summary>
/// Persists AppSettings as JSON at %APPDATA%/GameSubTranslate/settings.json.
/// ApiKey is DPAPI-encrypted (CurrentUser scope) so it never sits in plaintext on disk.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public string FilePath { get; }

    public SettingsStore(string? filePath = null)
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        FilePath = filePath ?? Path.Combine(dir, "GameSubTranslate", "settings.json");
    }

    /// <summary>Load settings. Returns fresh defaults on missing or corrupt file — never throws.</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();

            var raw = File.ReadAllText(FilePath);
            var dto = JsonSerializer.Deserialize<SettingsDto>(raw);
            if (dto is null) return new AppSettings();

            return new AppSettings
            {
                ApiKey = Decrypt(dto.ApiKeyEncrypted),
                BaseUrl = dto.BaseUrl,
                Model = dto.Model,
                SourceLang = dto.SourceLang ?? "auto",
                TargetLang = dto.TargetLang ?? "id",
                CaptureIntervalMs = dto.CaptureIntervalMs > 0 ? dto.CaptureIntervalMs : 800,
                OcrEngine = dto.OcrEngine,
                OverlayFontFamily = dto.OverlayFontFamily ?? "Segoe UI",
                OverlayFontSize = dto.OverlayFontSize > 0 ? dto.OverlayFontSize : 20,
                OverlayTextColor = dto.OverlayTextColor ?? "#FFFFFF",
                OverlayBgColor = dto.OverlayBgColor ?? "#CC000000",
                OverlayOpacity = dto.OverlayOpacity is >= 0 and <= 1 ? dto.OverlayOpacity : 1.0,
                OverlayX = dto.OverlayX,
                OverlayY = dto.OverlayY,
                HotkeyToggleOverlay = dto.HotkeyToggleOverlay ?? "Ctrl+Alt+T",
                HotkeyPauseCapture = dto.HotkeyPauseCapture ?? "Ctrl+Alt+P",
                HotkeyOpenSettings = dto.HotkeyOpenSettings ?? "Ctrl+Alt+S",
                HotkeyManualCapture = dto.HotkeyManualCapture ?? "Ctrl+Alt+Space",
                ActiveProfileId = dto.ActiveProfileId,
                ActiveRegionId = dto.ActiveRegionId,
            };
        }
        catch
        {
            // Corrupt file (bad JSON, wrong DPAPI scope after restore, ...) → start fresh.
            return new AppSettings();
        }
    }

    public void Save(AppSettings s)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);

        var dto = new SettingsDto
        {
            ApiKeyEncrypted = Encrypt(s.ApiKey),
            BaseUrl = s.BaseUrl,
            Model = s.Model,
            SourceLang = s.SourceLang,
            TargetLang = s.TargetLang,
            CaptureIntervalMs = s.CaptureIntervalMs,
            OcrEngine = s.OcrEngine,
            OverlayFontFamily = s.OverlayFontFamily,
            OverlayFontSize = s.OverlayFontSize,
            OverlayTextColor = s.OverlayTextColor,
            OverlayBgColor = s.OverlayBgColor,
            OverlayOpacity = s.OverlayOpacity,
            OverlayX = s.OverlayX,
            OverlayY = s.OverlayY,
            HotkeyToggleOverlay = s.HotkeyToggleOverlay,
            HotkeyPauseCapture = s.HotkeyPauseCapture,
            HotkeyOpenSettings = s.HotkeyOpenSettings,
            HotkeyManualCapture = s.HotkeyManualCapture,
            ActiveProfileId = s.ActiveProfileId,
            ActiveRegionId = s.ActiveRegionId,
        };

        File.WriteAllText(FilePath, JsonSerializer.Serialize(dto, JsonOpts));
    }

    private static string? Encrypt(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    private static string? Decrypt(string? b64)
    {
        if (string.IsNullOrEmpty(b64)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(b64), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    // JSON surface: ApiKey serialized as encrypted blob, everything else plain.
    private sealed class SettingsDto
    {
        public string? ApiKeyEncrypted { get; set; }
        public string? BaseUrl { get; set; }
        public string? Model { get; set; }
        public string? SourceLang { get; set; }
        public string? TargetLang { get; set; }
        public int CaptureIntervalMs { get; set; }
        public OcrEngineKind OcrEngine { get; set; }
        public string? OverlayFontFamily { get; set; }
        public double OverlayFontSize { get; set; }
        public string? OverlayTextColor { get; set; }
        public string? OverlayBgColor { get; set; }
        public double OverlayOpacity { get; set; }
        public double? OverlayX { get; set; }
        public double? OverlayY { get; set; }
        public string? HotkeyToggleOverlay { get; set; }
        public string? HotkeyPauseCapture { get; set; }
        public string? HotkeyOpenSettings { get; set; }
        public string? HotkeyManualCapture { get; set; }
        public int? ActiveProfileId { get; set; }
        public int? ActiveRegionId { get; set; }
    }
}
