using GameSubTranslate.Config;
using GameSubTranslate.Storage;

namespace GameSubTranslate.Prototype;

/// <summary>
/// Minimal assert-style self-checks run via CLI (no test framework, per CLAUDE.md).
/// Usage: --selfcheck-t3, --selfcheck-t4
/// </summary>
internal static class SelfChecks
{
    public static int Run(string which) => which switch
    {
        "--selfcheck-t3" => SelfCheckT3(),
        "--selfcheck-t4" => SelfCheckT4(),
        _ => SelfCheckT3(),
    };

    private static int SelfCheckT3()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gst-selfcheck-t3");
        var store = new SettingsStore(Path.Combine(dir, "settings.json"));
        var s = new AppSettings { ApiKey = "sk-test-123", BaseUrl = "https://api.openai.com/v1", Model = "gpt-4o-mini", OverlayFontSize = 24 };
        store.Save(s);

        var loaded = store.Load();
        if (loaded.ApiKey != "sk-test-123" || loaded.BaseUrl != "https://api.openai.com/v1" || loaded.OverlayFontSize != 24)
        {
            Console.WriteLine($"FAIL: round-trip mismatch: key={loaded.ApiKey}");
            return 1;
        }
        var raw = File.ReadAllText(store.FilePath);
        if (raw.Contains("sk-test-123"))
        {
            Console.WriteLine("FAIL: ApiKey plaintext in file");
            return 1;
        }
        // Field named ApiKeyEncrypted must hold a base64 blob, never plaintext.
        var dto = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(raw);
        var encrypted = dto.GetProperty("ApiKeyEncrypted").GetString() ?? "";
        try
        {
            Convert.FromBase64String(encrypted);
        }
        catch (FormatException)
        {
            Console.WriteLine("FAIL: ApiKeyEncrypted is not valid base64");
            return 1;
        }

        // Corrupt file → Load returns defaults, no crash.
        File.WriteAllText(store.FilePath, "{not valid json");
        var defaults = store.Load();
        if (defaults.ApiKey != null || defaults.CaptureIntervalMs != 800)
        {
            Console.WriteLine("FAIL: corrupt load did not return defaults");
            return 1;
        }

        Console.WriteLine("PASS: SettingsStore round-trip + encryption + corrupt-handling");
        return 0;
    }

    private static int SelfCheckT4()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "gst-selfcheck-t4", "profiles.db");
        if (File.Exists(dbPath)) File.Delete(dbPath);

        var db = new Database(dbPath);
        db.EnsureSchema();

        var tables = new List<string>();
        using (var conn = db.Open())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
            using var r = cmd.ExecuteReader();
            while (r.Read()) tables.Add(r.GetString(0));
        }

        var expected = new[] { "CaptureRegion", "GameProfile", "TranslationCache" };
        foreach (var t in expected)
        {
            if (!tables.Contains(t))
            {
                Console.WriteLine($"FAIL: table {t} missing; got [{string.Join(", ", tables)}]");
                return 1;
            }
        }
        // sqlite_sequence is created by AUTOINCREMENT — system table, ignore it.
        var unexpected = tables.Where(t => !expected.Contains(t) && !t.StartsWith("sqlite_")).ToList();
        if (unexpected.Count > 0)
        {
            Console.WriteLine($"FAIL: unexpected tables: [{string.Join(", ", unexpected)}]");
            return 1;
        }

        // EnsureSchema is idempotent.
        db.EnsureSchema();

        Console.WriteLine("PASS: SQLite schema created (3 tables) + idempotent");
        return 0;
    }
}
