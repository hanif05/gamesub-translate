using Dapper;
using GameSubTranslate.Config;
using GameSubTranslate.Storage;

namespace GameSubTranslate.Profiles;

/// <summary>
/// CRUD for GameProfile + its CaptureRegions. Parameterized Dapper queries.
/// Validation: Name required, ExecutableName optional.
/// </summary>
public sealed class ProfileRepository
{
    private readonly Database _db;

    public ProfileRepository(Database db) => _db = db;

    public IEnumerable<GameProfile> GetAll()
    {
        using var conn = _db.Open();
        var rows = conn.Query<ProfileRow>(
            "SELECT * FROM GameProfile ORDER BY Name").ToList();
        return rows.Select(r => r.ToModel()).ToList();
    }

    public GameProfile? GetById(int id)
    {
        using var conn = _db.Open();
        var row = conn.QuerySingleOrDefault<ProfileRow>(
            "SELECT * FROM GameProfile WHERE Id = @Id", new { Id = id });
        if (row is null) return null;

        var profile = row.ToModel();
        profile.Regions = conn.Query<CaptureRegion>(
            "SELECT * FROM CaptureRegion WHERE ProfileId = @ProfileId ORDER BY SortOrder, Id",
            new { ProfileId = id }).ToList();
        return profile;
    }

    public int Create(GameProfile p)
    {
        Validate(p);
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            p.CreatedAt = DateTime.UtcNow;
            p.Id = conn.ExecuteScalar<int>(@"
                INSERT INTO GameProfile (Name, ExecutableName, SourceLang, TargetLang, OcrEngine, CaptureIntervalMs, CreatedAt)
                VALUES (@Name, @ExecutableName, @SourceLang, @TargetLang, @OcrEngine, @CaptureIntervalMs, @CreatedAt);
                SELECT last_insert_rowid();", p, tx);

            foreach (var r in p.Regions)
            {
                r.ProfileId = p.Id;
                r.Id = conn.ExecuteScalar<int>(@"
                    INSERT INTO CaptureRegion (ProfileId, RegionName, X, Y, Width, Height, MonitorIndex, IsActiveDefault, SortOrder)
                    VALUES (@ProfileId, @RegionName, @X, @Y, @Width, @Height, @MonitorIndex, @IsActiveDefault, @SortOrder);
                    SELECT last_insert_rowid();", r, tx);
            }
            tx.Commit();
            return p.Id;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void Update(GameProfile p)
    {
        Validate(p);
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            conn.Execute(@"
                UPDATE GameProfile SET Name=@Name, ExecutableName=@ExecutableName, SourceLang=@SourceLang,
                    TargetLang=@TargetLang, OcrEngine=@OcrEngine, CaptureIntervalMs=@CaptureIntervalMs
                WHERE Id=@Id", p, tx);

            // Regions: delete-then-reinsert keeps the model authoritative and avoids diff logic.
            conn.Execute("DELETE FROM CaptureRegion WHERE ProfileId=@ProfileId", new { ProfileId = p.Id }, tx);
            foreach (var r in p.Regions)
            {
                r.ProfileId = p.Id;
                conn.Execute(@"
                    INSERT INTO CaptureRegion (ProfileId, RegionName, X, Y, Width, Height, MonitorIndex, IsActiveDefault, SortOrder)
                    VALUES (@ProfileId, @RegionName, @X, @Y, @Width, @Height, @MonitorIndex, @IsActiveDefault, @SortOrder)", r, tx);
            }
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void Delete(int id)
    {
        using var conn = _db.Open();
        // Regions removed via FK ON DELETE CASCADE.
        conn.Execute("DELETE FROM GameProfile WHERE Id=@Id", new { Id = id });
    }

    private static void Validate(GameProfile p)
    {
        if (string.IsNullOrWhiteSpace(p.Name))
            throw new ArgumentException("Profile Name must not be empty.", nameof(p));
    }

    // Dapper-friendly row shape (column names match DB directly).
    private sealed class ProfileRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? ExecutableName { get; set; }
        public string SourceLang { get; set; } = "auto";
        public string TargetLang { get; set; } = "id";
        public OcrEngineKind OcrEngine { get; set; }
        public int CaptureIntervalMs { get; set; }
        public string CreatedAt { get; set; } = "";

        public GameProfile ToModel() => new()
        {
            Id = Id,
            Name = Name,
            ExecutableName = ExecutableName,
            SourceLang = SourceLang,
            TargetLang = TargetLang,
            OcrEngine = OcrEngine,
            CaptureIntervalMs = CaptureIntervalMs,
            CreatedAt = DateTime.TryParse(CreatedAt, out var dt) ? dt : DateTime.UtcNow,
        };
    }
}
