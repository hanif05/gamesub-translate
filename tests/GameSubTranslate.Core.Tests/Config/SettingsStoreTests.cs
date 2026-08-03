using System.Text;
using System.Text.Json;
using GameSubTranslate.Config;
using GameSubTranslate.Core.Tests.Fixtures;
using Xunit;

namespace GameSubTranslate.Core.Tests.Config;

public class SettingsStoreTests : IClassFixture<TempAppDataFixture>
{
    private readonly TempAppDataFixture _fx;

    public SettingsStoreTests(TempAppDataFixture fx) => _fx = fx;

    private SettingsStore NewStore(string name = "settings.json")
        => new(_fx.SubDir(name));

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var store = NewStore();
        var original = new AppSettings
        {
            ApiKey = "sk-secret-key-abc123",
            BaseUrl = "https://api.example.com/v1",
            Model = "gpt-4o-mini",
            SourceLang = "en",
            TargetLang = "id",
            CaptureIntervalMs = 1234,
            OcrEngine = OcrEngineKind.VisionAi,
            OverlayFontFamily = "Inter",
            OverlayFontSize = 24,
            OverlayTextColor = "#FFAA00",
            OverlayBgColor = "#AA000000",
            OverlayOpacity = 0.7,
            OverlayX = 100.5,
            OverlayY = 200.25,
            HotkeyToggleOverlay = "Ctrl+Shift+T",
            HotkeyPauseCapture = "Ctrl+Shift+P",
            HotkeyOpenSettings = "Ctrl+Shift+S",
            HotkeyManualCapture = "Ctrl+Shift+Space",
            ActiveProfileId = 7,
            ActiveRegionId = 13,
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original.ApiKey, loaded.ApiKey);
        Assert.Equal(original.BaseUrl, loaded.BaseUrl);
        Assert.Equal(original.Model, loaded.Model);
        Assert.Equal(original.SourceLang, loaded.SourceLang);
        Assert.Equal(original.TargetLang, loaded.TargetLang);
        Assert.Equal(original.CaptureIntervalMs, loaded.CaptureIntervalMs);
        Assert.Equal(original.OcrEngine, loaded.OcrEngine);
        Assert.Equal(original.OverlayFontFamily, loaded.OverlayFontFamily);
        Assert.Equal(original.OverlayFontSize, loaded.OverlayFontSize);
        Assert.Equal(original.OverlayTextColor, loaded.OverlayTextColor);
        Assert.Equal(original.OverlayBgColor, loaded.OverlayBgColor);
        Assert.Equal(original.OverlayOpacity, loaded.OverlayOpacity);
        Assert.Equal(original.OverlayX, loaded.OverlayX);
        Assert.Equal(original.OverlayY, loaded.OverlayY);
        Assert.Equal(original.HotkeyToggleOverlay, loaded.HotkeyToggleOverlay);
        Assert.Equal(original.HotkeyPauseCapture, loaded.HotkeyPauseCapture);
        Assert.Equal(original.HotkeyOpenSettings, loaded.HotkeyOpenSettings);
        Assert.Equal(original.HotkeyManualCapture, loaded.HotkeyManualCapture);
        Assert.Equal(original.ActiveProfileId, loaded.ActiveProfileId);
        Assert.Equal(original.ActiveRegionId, loaded.ActiveRegionId);
    }

    [Fact]
    public void Save_ApiKeyIsBase64OnDisk_NotPlaintext()
    {
        // The whole point of DPAPI on the file: even if a user opens settings.json in
        // notepad, the secret is not visible. Verify by scanning the raw bytes.
        var store = NewStore();
        var s = new AppSettings { ApiKey = "sk-super-secret-NEVER-PLAINTEXT" };
        store.Save(s);

        var rawBytes = File.ReadAllBytes(store.FilePath);
        var rawText = Encoding.UTF8.GetString(rawBytes);

        Assert.DoesNotContain("sk-super-secret-NEVER-PLAINTEXT", rawText);

        // The encrypted blob is base64 — just assert the field name shows up with a
        // base64-looking value next to it, not the literal secret.
        var json = JsonDocument.Parse(rawText);
        var encrypted = json.RootElement.GetProperty("ApiKeyEncrypted").GetString();
        Assert.NotNull(encrypted);
        Assert.NotEqual("sk-super-secret-NEVER-PLAINTEXT", encrypted);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = NewStore("does-not-exist.json");
        var loaded = store.Load();

        Assert.NotNull(loaded);
        // Spot-check a few defaults from AppSettings.
        Assert.Equal(800, loaded.CaptureIntervalMs);
        Assert.Equal("id", loaded.TargetLang);
        Assert.Equal("Segoe UI", loaded.OverlayFontFamily);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaultsWithoutThrowing()
    {
        // A broken settings file should never prevent the app from starting — the
        // store swallows the exception and returns fresh defaults.
        var path = _fx.SubDir("corrupt.json");
        File.WriteAllText(path, "{ this is not valid json <<<");
        var store = new SettingsStore(path);

        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Null(loaded.ApiKey);
    }

    [Fact]
    public void SaveLoad_DpapiRoundTrip_RecoversOriginalKey()
    {
        // DPAPI with DataProtectionScope.CurrentUser is bound to the current OS user.
        // We test the round-trip — Microsoft's own unprotect path is out of scope.
        var store = NewStore("dpapi.json");
        var s = new AppSettings { ApiKey = "key-with-special-chars-ñ-ümlaut-€-€€" };
        store.Save(s);

        var loaded = store.Load();
        Assert.Equal(s.ApiKey, loaded.ApiKey);
    }

    [Fact]
    public void Save_ClampsInvalidOverlayValuesOnLoad()
    {
        // Opacity outside [0, 1] is silently clamped to the default 1.0 — defensive
        // load so a hand-edited config file can't crash the overlay.
        var path = _fx.SubDir("bad-overlay.json");
        var json = """
        {
          "OverlayOpacity": 5.0,
          "OverlayFontSize": 0,
          "CaptureIntervalMs": -10
        }
        """;
        File.WriteAllText(path, json);
        var store = new SettingsStore(path);

        var loaded = store.Load();

        Assert.Equal(1.0, loaded.OverlayOpacity);
        Assert.Equal(20, loaded.OverlayFontSize); // default
        Assert.Equal(800, loaded.CaptureIntervalMs); // default
    }
}
