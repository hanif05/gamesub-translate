using System.Text.Json;
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
                INSERT INTO GameProfile (Name, ExecutableName, SourceLang, TargetLang, OcrEngine, PaddleUseGpu, CaptureIntervalMs, CreatedAt)
                VALUES (@Name, @ExecutableName, @SourceLang, @TargetLang, @OcrEngine, @PaddleUseGpu, @CaptureIntervalMs, @CreatedAt);
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
                    TargetLang=@TargetLang, OcrEngine=@OcrEngine, PaddleUseGpu=@PaddleUseGpu,
                    CaptureIntervalMs=@CaptureIntervalMs
                WHERE Id=@Id", p, tx);

            // Regions: delete-then-reinsert keeps the model authoritative and avoids diff logic.
            // Re-set r.Id from the new rowid so the in-memory model stays in sync with the DB —
            // otherwise the persisted ActiveRegionId (settings) points at a deleted row and region
            // selection silently falls back to the wrong region after an edit (T26 bug).
            conn.Execute("DELETE FROM CaptureRegion WHERE ProfileId=@ProfileId", new { ProfileId = p.Id }, tx);
            foreach (var r in p.Regions)
            {
                r.ProfileId = p.Id;
                r.Id = conn.ExecuteScalar<int>(@"
                    INSERT INTO CaptureRegion (ProfileId, RegionName, X, Y, Width, Height, MonitorIndex, IsActiveDefault, SortOrder)
                    VALUES (@ProfileId, @RegionName, @X, @Y, @Width, @Height, @MonitorIndex, @IsActiveDefault, @SortOrder);
                    SELECT last_insert_rowid();", r, tx);
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

    // ---- T53: JSON preset import/export. Profiles are intentionally API-key-free — the
    // translation credentials live in AppSettings (DPAPI-encrypted). A preset is geometry +
    // language hints only.

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>Serializes one profile (with regions) to JSON. Round-trips through <see cref="ImportFromJson"/>.</summary>
    public string ExportToJson(GameProfile profile)
    {
        var dto = ProfileDto.FromModel(profile);
        return JsonSerializer.Serialize(dto, JsonOpts);
    }

    /// <summary>Persists a preset JSON into the database. Returns the new profile id.</summary>
    public int ImportFromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<ProfileDto>(json)
            ?? throw new ArgumentException("Empty or invalid JSON.", nameof(json));
        var p = dto.ToModel();
        return Create(p);
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
        public bool PaddleUseGpu { get; set; }
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
            PaddleUseGpu = PaddleUseGpu,
            CaptureIntervalMs = CaptureIntervalMs,
            CreatedAt = DateTime.TryParse(CreatedAt, out var dt) ? dt : DateTime.UtcNow,
        };
    }

    // T53 JSON surface. Stable, versioned by hand so the preset docs can refer to the schema.
    // Format deliberately mirrors the DB column names minus Id/CreatedAt (assigned at insert).
    // F87: SchemaVersion bumped 1→2 — PaddleUseGpu field added; old presets parse fine because
    // the field is optional in the JSON deserializer and falls back to false.
    private sealed class ProfileDto
    {
        public int SchemaVersion { get; set; } = 2;
        public string Name { get; set; } = "";
        public string? ExecutableName { get; set; }
        public string SourceLang { get; set; } = "auto";
        public string TargetLang { get; set; } = "id";
        public string OcrEngine { get; set; } = "Tesseract";
        public bool PaddleUseGpu { get; set; }
        public int CaptureIntervalMs { get; set; } = 800;
        public List<RegionDto> Regions { get; set; } = new();

        public static ProfileDto FromModel(GameProfile p) => new()
        {
            Name = p.Name,
            ExecutableName = p.ExecutableName,
            SourceLang = p.SourceLang,
            TargetLang = p.TargetLang,
            OcrEngine = p.OcrEngine.ToString(),
            PaddleUseGpu = p.PaddleUseGpu,
            CaptureIntervalMs = p.CaptureIntervalMs,
            Regions = p.Regions.Select(RegionDto.FromModel).ToList(),
        };

        public GameProfile ToModel() => new()
        {
            Name = Name,
            ExecutableName = ExecutableName,
            SourceLang = SourceLang,
            TargetLang = TargetLang,
            OcrEngine = Enum.TryParse<OcrEngineKind>(OcrEngine, out var k) ? k : OcrEngineKind.Tesseract,
            PaddleUseGpu = PaddleUseGpu,
            CaptureIntervalMs = CaptureIntervalMs,
            Regions = Regions.Select(r => r.ToModel()).ToList(),
        };
    }

    private sealed class RegionDto
    {
        public string RegionName { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int MonitorIndex { get; set; }
        public bool IsActiveDefault { get; set; }
        public int SortOrder { get; set; }

        public static RegionDto FromModel(CaptureRegion r) => new()
        {
            RegionName = r.RegionName,
            X = r.X, Y = r.Y, Width = r.Width, Height = r.Height,
            MonitorIndex = r.MonitorIndex,
            IsActiveDefault = r.IsActiveDefault,
            SortOrder = r.SortOrder,
        };

        public CaptureRegion ToModel() => new()
        {
            RegionName = RegionName,
            X = X, Y = Y, Width = Width, Height = Height,
            MonitorIndex = MonitorIndex,
            IsActiveDefault = IsActiveDefault,
            SortOrder = SortOrder,
        };
    }
}
