namespace GameSubTranslate.Core.Tests.Fixtures;

/// <summary>
/// xUnit fixture that points %APPDATA% (well, the .NET-mapped ApplicationData folder)
/// at a per-test temp dir so DPAPI-encrypted settings don't touch the user's real profile.
/// Implements IDisposable so xUnit cleans up after each test class.
/// </summary>
public sealed class TempAppDataFixture : IDisposable
{
    public string TempDir { get; }

    public TempAppDataFixture()
    {
        TempDir = Path.Combine(Path.GetTempPath(), "gst-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDir);
        // Environment.SpecialFolder.ApplicationData resolves via Environment.GetFolderPath,
        // which on Windows reads the USERPROFILE/AppData/Roaming path. Redirect the
        // process-wide env var so SettingsStore (no-arg ctor) lands in our temp dir.
        Environment.SetEnvironmentVariable("APPDATA", TempDir);
    }

    public string SubDir(string name) => Path.Combine(TempDir, name);

    public void Dispose()
    {
        try { Directory.Delete(TempDir, recursive: true); } catch { /* best effort */ }
    }
}
