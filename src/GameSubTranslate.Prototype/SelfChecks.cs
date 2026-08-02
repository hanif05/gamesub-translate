using Dapper;
using System.Drawing;
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
        "--selfcheck-t10" => SelfCheckT10(),
        "--selfcheck-t11" => SelfCheckT11(),
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

    private static int SelfCheckT10()
    {
        // Grab a small region near the top-left of the primary monitor and verify the
        // Windows.Graphics.Capture pipeline returns a PNG of the right dimensions.
        var primary = System.Windows.Forms.Screen.PrimaryScreen!;
        var sx = primary.Bounds.X;
        var sy = primary.Bounds.Y;
        const int w = 320, h = 80;

        using var cap = GameSubTranslate.Capture.ScreenCapture.ForMonitorAt(sx, sy);
        byte[] png = cap.CaptureRegion(sx, sy, w, h);

        if (png.Length == 0)
        {
            Console.WriteLine("FAIL: empty capture");
            return 1;
        }
        using var img = Image.FromStream(new MemoryStream(png));
        if (img.Width != w || img.Height != h)
        {
            Console.WriteLine($"FAIL: PNG size {img.Width}x{img.Height}, expected {w}x{h}");
            return 1;
        }

        // Second frame: same region → also decodeable (validates repeated capture).
        byte[] png2 = cap.CaptureRegion(sx, sy, w, h);
        if (png2.Length == 0)
        {
            Console.WriteLine("FAIL: second capture empty");
            return 1;
        }

        Console.WriteLine($"PASS: WGC capture {w}x{h} PNG ({png.Length} bytes) + repeat");
        return 0;
    }

    private static int SelfCheckT11()
    {
        // Synthetic frames: solid bg + a dark "text" bar. Identical copies → no change.
        var imgA = MakeFrame("hello world");
        var imgA2 = MakeFrame("hello world");
        var imgB = MakeFrame("different words here");
        var cd = GameSubTranslate.Pipeline.ChangeDetector.IsChanged;

        // Identical (re-encode same pixels) → not changed.
        if (cd(imgA, imgA2))
        {
            Console.WriteLine("FAIL: identical frames flagged as changed");
            return 1;
        }
        // Different text → changed.
        if (!cd(imgA, imgB))
        {
            Console.WriteLine("FAIL: different text not flagged as changed");
            return 1;
        }
        // First capture (no prior) → changed.
        if (!cd(imgA, null))
        {
            Console.WriteLine("FAIL: first frame (null prior) not flagged");
            return 1;
        }
        // Null new frame → not changed.
        if (cd(null, imgA))
        {
            Console.WriteLine("FAIL: null new frame flagged");
            return 1;
        }

        // Noise tolerance: same frame with a few random pixels flipped → not changed.
        var imgA3 = AddNoise(imgA, flips: 40);
        if (cd(imgA, imgA3))
        {
            Console.WriteLine("FAIL: small noise flagged as changed");
            return 1;
        }

        Console.WriteLine("PASS: ChangeDetector grid-compare identical/different/first-frame/noise-tolerance");
        return 0;
    }

    private static byte[] MakeFrame(string text, float sizeMul = 1f)
    {
        const int w = 400, h = 60;
        using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            using var font = new Font("Arial", 18f * sizeMul, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.Black);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.DrawString(text, font, brush, 10, 10);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>Flip `flips` random pixels to simulate capture noise / anti-aliasing jitter.</summary>
    private static byte[] AddNoise(byte[] png, int flips)
    {
        using var img = Image.FromStream(new MemoryStream(png));
        using var bmp = new Bitmap(img);
        var rnd = new Random(42);
        for (int i = 0; i < flips; i++)
        {
            int x = rnd.Next(bmp.Width);
            int y = rnd.Next(bmp.Height);
            var c = bmp.GetPixel(x, y);
            int delta = rnd.Next(2) == 0 ? -12 : 12;
            bmp.SetPixel(x, y, Color.FromArgb(255, Clamp(c.R + delta), Clamp(c.G + delta), Clamp(c.B + delta)));
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    private static int Clamp(int v) => Math.Clamp(v, 0, 255);

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
