using GameSubTranslate.Profiles;
using GameSubTranslate.Storage;
using Xunit;

namespace GameSubTranslate.Core.Tests.Profiles;

/// <summary>T53: presets are JSON files under <c>tests/fixtures/profiles/</c>. Each test loads
/// one, imports via <see cref="ProfileRepository.ImportFromJson"/>, and checks the round-trip.</summary>
public class ProfileImportTests : IDisposable
{
    private readonly Database _db;
    private readonly ProfileRepository _repo;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _hold;
    private readonly string _fixturesDir;

    public ProfileImportTests()
    {
        var memName = "file:test-" + Guid.NewGuid().ToString("N") + "?mode=memory&cache=shared";
        _db = new Database(memName);
        _db.EnsureSchema();
        _hold = _db.Open();
        _repo = new ProfileRepository(_db);
        // bin/Release/net8.0-windows10.0.19041.0/ → 4 levels up reaches tests/, then + fixtures/profiles.
        _fixturesDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "profiles"));
    }

    public void Dispose() => _hold.Dispose();

    [Theory]
    [InlineData("tlou.json")]
    [InlineData("god-of-war.json")]
    [InlineData("persona5r.json")]
    public void Import_preset_roundtrips(string fixtureName)
    {
        var path = Path.Combine(_fixturesDir, fixtureName);
        Assert.True(File.Exists(path), $"Fixture missing: {path}");

        var json = File.ReadAllText(path);
        var id = _repo.ImportFromJson(json);
        var loaded = _repo.GetById(id);

        Assert.NotNull(loaded);
        Assert.NotEmpty(loaded!.Regions);

        // Re-export and compare the parsed shape (not raw text — indentation can differ).
        var exported = _repo.ExportToJson(loaded);
        using var doc1 = System.Text.Json.JsonDocument.Parse(json);
        using var doc2 = System.Text.Json.JsonDocument.Parse(exported);
        AssertJsonEqual(doc1.RootElement, doc2.RootElement);
    }

    private static void AssertJsonEqual(System.Text.Json.JsonElement a, System.Text.Json.JsonElement b)
    {
        // SchemaVersion is the only field the re-export omits (we set it to 1 on input but
        // don't read it back into the model, so it isn't rewritten on the output round-trip
        // of an already-persisted profile — unless we re-import the export). For round-trip
        // equality we compare the substantive fields and skip SchemaVersion + Id (Db-assigned).
        foreach (var prop in a.EnumerateObject())
        {
            if (prop.Name is "SchemaVersion") continue;
            Assert.True(b.TryGetProperty(prop.Name, out var bVal), $"missing key {prop.Name}");
            AssertJsonValueEqual(prop.Value, bVal, prop.Name);
        }
    }

    private static void AssertJsonValueEqual(System.Text.Json.JsonElement a, System.Text.Json.JsonElement b, string name)
    {
        switch (a.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                AssertJsonEqual(a, b);
                break;
            case System.Text.Json.JsonValueKind.Array:
                Assert.Equal(a.GetArrayLength(), b.GetArrayLength());
                int i = 0;
                foreach (var item in a.EnumerateArray())
                {
                    AssertJsonValueEqual(item, b[i], $"{name}[{i}]");
                    i++;
                }
                break;
            default:
                Assert.True(a.ValueKind == b.ValueKind, $"{name}: kind mismatch {a.ValueKind} vs {b.ValueKind}");
                Assert.Equal(a.GetRawText(), b.GetRawText());
                break;
        }
    }
}
