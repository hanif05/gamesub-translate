using Dapper;
using GameSubTranslate.Config;
using GameSubTranslate.Profiles;
using GameSubTranslate.Storage;

namespace GameSubTranslate.Prototype;

/// <summary>
/// Minimal assert-style self-checks run via CLI (no test framework, per CLAUDE.md).
/// Usage: --selfcheck-t3, --selfcheck-t4, --selfcheck-t5
/// </summary>
internal static class SelfChecks
{
    public static int Run(string which) => which switch
    {
        "--selfcheck-t3" => SelfCheckT3(),
        "--selfcheck-t4" => SelfCheckT4(),
        "--selfcheck-t5" => SelfCheckT5(),
        "--selfcheck-t9" => SelfCheckT9(),
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

    private static int SelfCheckT5()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "gst-selfcheck-t5", "profiles.db");
        if (File.Exists(dbPath)) File.Delete(dbPath);
        var db = new Database(dbPath);
        db.EnsureSchema();
        var repo = new ProfileRepository(db);

        // Create with 2 regions.
        var p = new GameProfile
        {
            Name = "Test Game",
            ExecutableName = "test.exe",
            TargetLang = "en",
            Regions = new List<CaptureRegion>
            {
                new() { RegionName = "Subtitle", X = 10, Y = 20, Width = 800, Height = 100, IsActiveDefault = true, SortOrder = 0 },
                new() { RegionName = "Dialog", X = 0, Y = 0, Width = 640, Height = 200, SortOrder = 1 },
            },
        };
        int id = repo.Create(p);
        if (id <= 0)
        {
            Console.WriteLine("FAIL: Create returned non-positive id");
            return 1;
        }

        // GetAll returns the row.
        var all = repo.GetAll().ToList();
        if (all.Count != 1 || all[0].Name != "Test Game")
        {
            Console.WriteLine($"FAIL: GetAll count={all.Count}, name={all.FirstOrDefault()?.Name}");
            return 1;
        }

        // GetById loads regions.
        var loaded = repo.GetById(id);
        if (loaded is null || loaded.Regions.Count != 2)
        {
            Console.WriteLine($"FAIL: GetById regions={loaded?.Regions.Count}");
            return 1;
        }
        if (loaded.Regions[0].IsActiveDefault != true || loaded.Regions[0].ProfileId != id)
        {
            Console.WriteLine("FAIL: region fields not round-tripped");
            return 1;
        }

        // Update name + swap regions.
        loaded.Name = "Test Game Renamed";
        loaded.Regions = new List<CaptureRegion> { loaded.Regions[1] };
        repo.Update(loaded);
        var updated = repo.GetById(id);
        if (updated is null || updated.Name != "Test Game Renamed" || updated.Regions.Count != 1)
        {
            Console.WriteLine($"FAIL: update name={updated?.Name}, regions={updated?.Regions.Count}");
            return 1;
        }

        // Delete.
        repo.Delete(id);
        if (repo.GetById(id) is not null)
        {
            Console.WriteLine("FAIL: Delete did not remove profile");
            return 1;
        }
        using (var conn = db.Open())
        {
            var regionCount = conn.QuerySingle<int>("SELECT COUNT(*) FROM CaptureRegion WHERE ProfileId=@Id", new { Id = id });
            if (regionCount != 0)
            {
                Console.WriteLine($"FAIL: cascade delete left {regionCount} orphan regions");
                return 1;
            }
        }

        Console.WriteLine("PASS: ProfileRepository CRUD + regions + cascade delete");
        return 0;
    }

    private static int SelfCheckT9()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "gst-selfcheck-t9");
        var dbPath = Path.Combine(tmp, "profiles.db");
        if (File.Exists(dbPath)) File.Delete(dbPath);
        var db = new Database(dbPath);
        db.EnsureSchema();
        var repo = new ProfileRepository(db);

        // Profile with 2 regions.
        int id = repo.Create(new GameProfile
        {
            Name = "Region Switcher Test",
            Regions = new List<CaptureRegion>
            {
                new() { RegionName = "A", X = 0, Y = 0, Width = 100, Height = 50, IsActiveDefault = true, SortOrder = 0 },
                new() { RegionName = "B", X = 100, Y = 0, Width = 100, Height = 50, SortOrder = 1 },
            },
        });

        // Service state is persisted to settings.json; fresh service must restore it.
        var settingsFile = Path.Combine(tmp, "settings.json");
        var store = new SettingsStore(settingsFile);
        var app = store.Load();

        var svc = new ProfileService(repo, store, app);
        svc.SetActiveProfile(id);

        // ActiveProfile set, default region (A) auto-selected.
        if (svc.ActiveProfileId != id || svc.ActiveRegion()?.RegionName != "A")
        {
            Console.WriteLine($"FAIL: initial active profile/region. profile={svc.ActiveProfileId}, region={svc.ActiveRegion()?.RegionName}");
            return 1;
        }

        // Switch to region B.
        var regionB = svc.ActiveProfile!.Regions.First(r => r.RegionName == "B");
        svc.SetActiveRegion(regionB.Id);
        if (svc.ActiveRegion()?.RegionName != "B")
        {
            Console.WriteLine("FAIL: SetActiveRegion did not switch");
            return 1;
        }

        // New service instance (simulates restart) restores B.
        var svc2 = new ProfileService(repo, new SettingsStore(settingsFile), store.Load());
        if (svc2.ActiveProfileId != id || svc2.ActiveRegion()?.RegionName != "B")
        {
            Console.WriteLine($"FAIL: restart did not restore. profile={svc2.ActiveProfileId}, region={svc2.ActiveRegion()?.RegionName}");
            return 1;
        }

        // Clearing after delete works.
        svc2.ClearActiveProfile();
        var svc3 = new ProfileService(repo, new SettingsStore(settingsFile), store.Load());
        if (svc3.ActiveProfileId is not null)
        {
            Console.WriteLine("FAIL: ClearActiveProfile not persisted");
            return 1;
        }

        Console.WriteLine("PASS: ProfileService active region switch + persistence across restart");
        return 0;
    }
}
