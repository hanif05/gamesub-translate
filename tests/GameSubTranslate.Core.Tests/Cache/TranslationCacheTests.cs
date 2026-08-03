using GameSubTranslate.Cache;
using GameSubTranslate.Storage;
using Xunit;

namespace GameSubTranslate.Core.Tests.Cache;

public class TranslationCacheTests : IDisposable
{
    private readonly Database _db;
    private readonly TranslationCacheRepository _repo;

    public TranslationCacheTests()
    {
        // :memory: SQLite is per-connection — pass Mode=Memory + Cache=Shared so the
        // repo's per-call Open() gets the same in-memory DB. The shared cache file
        // is keyed by a unique name to keep tests isolated.
        var memName = "file:test-" + Guid.NewGuid().ToString("N") + "?mode=memory&cache=shared";
        _db = new Database(memName);
        _db.EnsureSchema();
        // Hold one connection open for the test's lifetime so the in-memory DB stays
        // alive across the repo's per-call Open()/Close().
        _hold = _db.Open();
        _repo = new TranslationCacheRepository(_db);
    }

    private readonly Microsoft.Data.Sqlite.SqliteConnection _hold;

    public void Dispose() => _hold.Dispose();

    [Fact]
    public void Put_ThenGet_ReturnsTranslatedText()
    {
        _repo.Put("Hello", "Halo", "id");
        Assert.Equal("Halo", _repo.Get("Hello", "id"));
    }

    [Fact]
    public void Get_UnknownText_ReturnsNull()
    {
        Assert.Null(_repo.Get("never-stored", "id"));
    }

    [Fact]
    public void Hash_IsDeterministic_ForSameInput()
    {
        var a = TranslationCacheRepository.Hash("Hello", "id");
        var b = TranslationCacheRepository.Hash("Hello", "id");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Hash_DiffersByTargetLanguage()
    {
        // Same source, different target lang → distinct keys. (Caches for "Hello"→id
        // and "Hello"→en must not collide.)
        var id = TranslationCacheRepository.Hash("Hello", "id");
        var en = TranslationCacheRepository.Hash("Hello", "en");
        Assert.NotEqual(id, en);
    }

    [Fact]
    public void Put_SameSourceAndLang_OverwritesTranslatedText()
    {
        _repo.Put("Hello", "Halo v1", "id");
        _repo.Put("Hello", "Halo v2", "id");
        Assert.Equal("Halo v2", _repo.Get("Hello", "id"));
    }

    [Fact]
    public void DeleteOlderThan_RemovesEntriesBeforeCutoff_KeepsNewer()
    {
        var old = DateTime.UtcNow.AddDays(-7);
        var recent = DateTime.UtcNow;

        _repo.Put("Old line", "Terjemahan lama", "id", old);
        _repo.Put("New line", "Terjemahan baru", "id", recent);

        var cutoff = DateTime.UtcNow.AddDays(-1);
        var deleted = _repo.DeleteOlderThan(cutoff);

        Assert.Equal(1, deleted);
        Assert.Null(_repo.Get("Old line", "id"));
        Assert.Equal("Terjemahan baru", _repo.Get("New line", "id"));
    }

    [Fact]
    public void DeleteOlderThan_NoOldEntries_ReturnsZero()
    {
        _repo.Put("Recent", "Baru", "id");
        var deleted = _repo.DeleteOlderThan(DateTime.UtcNow.AddDays(-1));
        Assert.Equal(0, deleted);
    }
}
