using GameSubTranslate.Config;
using GameSubTranslate.Profiles;
using GameSubTranslate.Storage;
using Xunit;

namespace GameSubTranslate.Core.Tests.Profiles;

/// <summary>F87: PaddleUseGpu field roundtrips through CRUD and survives a schema migration
/// that runs against a pre-Fase 6 database (no PaddleUseGpu column yet).</summary>
public class PaddleUseGpuProfileTests : IDisposable
{
    private readonly string _dbPath;
    private readonly Database _db;
    private readonly ProfileRepository _repo;

    public PaddleUseGpuProfileTests()
    {
        // File-backed DB so we can exercise the real ALTER TABLE migration path. In-memory
        // SQLite would skip it (no persistent schema to upgrade).
        _dbPath = Path.Combine(Path.GetTempPath(), "gst-paddle-gpu-" + Guid.NewGuid().ToString("N") + ".db");
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        _db = new Database(_dbPath);
        _db.EnsureSchema();
        _repo = new ProfileRepository(_db);
    }

    public void Dispose()
    {
        // SQLite holds the file via the connection pool; clear it so the temp file
        // can be deleted on the next run.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public void Create_ThenGetById_PreservesPaddleUseGpu()
    {
        var id = _repo.Create(new GameProfile
        {
            Name = "Paddle CUDA profile",
            OcrEngine = OcrEngineKind.PaddleOcr,
            PaddleUseGpu = true,
        });

        var loaded = _repo.GetById(id);

        Assert.NotNull(loaded);
        Assert.Equal(OcrEngineKind.PaddleOcr, loaded!.OcrEngine);
        Assert.True(loaded.PaddleUseGpu);
    }

    [Fact]
    public void Update_FlipsPaddleUseGpu()
    {
        var id = _repo.Create(new GameProfile
        {
            Name = "Toggle GPU",
            OcrEngine = OcrEngineKind.PaddleOcr,
            PaddleUseGpu = false,
        });

        var loaded = _repo.GetById(id)!;
        loaded.PaddleUseGpu = true;
        _repo.Update(loaded);

        var reloaded = _repo.GetById(id);
        Assert.True(reloaded!.PaddleUseGpu);
    }

    [Fact]
    public void EnsureSchema_OnPreFase6Database_BackfillsPaddleUseGpuColumn()
    {
        // Simulate a pre-Fase 6 install: build a DB with the old schema (no PaddleUseGpu),
        // then run EnsureSchema again and confirm the column was added without error.
        var legacyPath = Path.Combine(Path.GetTempPath(), "gst-legacy-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={legacyPath}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE GameProfile (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        ExecutableName TEXT NULL,
                        SourceLang TEXT NOT NULL DEFAULT 'auto',
                        TargetLang TEXT NOT NULL DEFAULT 'id',
                        OcrEngine INTEGER NOT NULL DEFAULT 0,
                        CaptureIntervalMs INTEGER NOT NULL DEFAULT 800,
                        CreatedAt TEXT NOT NULL
                    );
                    """;
                cmd.ExecuteNonQuery();

                // Insert one pre-Fase 6 row.
                using var ins = conn.CreateCommand();
                ins.CommandText = "INSERT INTO GameProfile (Name, OcrEngine, CaptureIntervalMs, CreatedAt) " +
                    "VALUES ('legacy', 0, 800, '2026-01-01 00:00:00')";
                ins.ExecuteNonQuery();
            }

            // Now upgrade: open the legacy DB and run EnsureSchema.
            var upgradeDb = new Database(legacyPath);
            upgradeDb.EnsureSchema();

            // Column must be present + legacy row readable.
            var repo = new ProfileRepository(upgradeDb);
            var all = repo.GetAll().ToList();
            Assert.Single(all);
            Assert.Equal("legacy", all[0].Name);
            Assert.False(all[0].PaddleUseGpu, "missing column should default to false on legacy rows");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(legacyPath)) File.Delete(legacyPath);
        }
    }
}
