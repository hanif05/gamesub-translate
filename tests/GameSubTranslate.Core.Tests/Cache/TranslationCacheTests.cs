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

    // ---- T37 fuzzy match ----

    [Fact]
    public void GetFuzzy_OneCharDiff_ReturnsCachedAboveThreshold()
    {
        _repo.Put("Hello world", "Halo dunia", "id");
        var hit = _repo.GetFuzzy("Hello worlds", "id", similarityThreshold: 0.85);
        Assert.NotNull(hit);
        Assert.Equal("Halo dunia", hit!.Value.translated);
        // 10/11 = 0.909 expected.
        Assert.InRange(hit.Value.similarity, 0.85, 1.0);
    }

    [Fact]
    public void GetFuzzy_TotallyDifferentText_ReturnsNull()
    {
        _repo.Put("Hello world", "Halo dunia", "id");
        var hit = _repo.GetFuzzy("Completely unrelated sentence", "id", similarityThreshold: 0.85);
        Assert.Null(hit);
    }

    [Fact]
    public void GetFuzzy_BelowThreshold_ReturnsNull()
    {
        _repo.Put("Hello", "Halo", "id");
        // "Hello" → "Hi" has distance 4, max 5 → similarity 0.2 < 0.85.
        var hit = _repo.GetFuzzy("Hi", "id", similarityThreshold: 0.85);
        Assert.Null(hit);
    }

    [Fact]
    public void GetFuzzy_ExactMatchAlsoReturnedByExact_HitsFuzzyAtOne()
    {
        // Exact match should also clear the fuzzy bar (similarity = 1.0). The pipeline
        // calls `Get` first, then `GetFuzzy` — so the exact path wins — but if a caller
        // skips exact and goes straight to fuzzy, exact must still hit.
        _repo.Put("Hello", "Halo", "id");
        var hit = _repo.GetFuzzy("Hello", "id");
        Assert.NotNull(hit);
        Assert.Equal("Halo", hit!.Value.translated);
        Assert.Equal(1.0, hit.Value.similarity, 6);
    }

    [Fact]
    public void GetFuzzy_DifferentTargetLang_DoesNotMatch()
    {
        _repo.Put("Hello", "Halo", "id");
        var hit = _repo.GetFuzzy("Hello worlds", "en", similarityThreshold: 0.85);
        Assert.Null(hit); // only "id" rows exist, "en" cache is empty
    }

    [Fact]
    public void GetFuzzy_PicksBestAmongMultipleCandidates()
    {
        // Two close-but-not-identical rows. The closer one must win.
        _repo.Put("Hello world", "Halo dunia", "id");   // sim to "Hello worlds" ≈ 0.91
        _repo.Put("Hello worlds!", "Halo dunia!", "id"); // sim to "Hello worlds" = 12/13 ≈ 0.92
        var hit = _repo.GetFuzzy("Hello worlds", "id", similarityThreshold: 0.85);
        Assert.NotNull(hit);
        Assert.Equal("Halo dunia!", hit!.Value.translated);
    }

    [Fact]
    public void NormalizedLevenshtein_Identical_IsOne()
    {
        Assert.Equal(1.0, TranslationCacheRepository.NormalizedLevenshteinSimilarity("Halo dunia", "Halo dunia"));
    }

    [Fact]
    public void NormalizedLevenshtein_BothEmpty_IsOne()
    {
        // Edge: both empty strings are equal → distance 0 → similarity 1.
        Assert.Equal(1.0, TranslationCacheRepository.NormalizedLevenshteinSimilarity("", ""));
    }

    [Fact]
    public void NormalizedLevenshtein_OneEmpty_IsZero()
    {
        // Either side empty (other not) → no characters to compare → 0.
        Assert.Equal(0.0, TranslationCacheRepository.NormalizedLevenshteinSimilarity("abc", ""));
        Assert.Equal(0.0, TranslationCacheRepository.NormalizedLevenshteinSimilarity("", "abc"));
    }

    [Theory]
    [InlineData("Halo dunia", "Halo Dunia", 9.0 / 10.0)]   // 1 substitution, both 10 chars
    [InlineData("kitten", "sitting", 4.0 / 7.0)]            // canonical Levenshtein example
    [InlineData("flaw", "lawn", 2.0 / 4.0)]                 // 2 substitutions
    public void NormalizedLevenshtein_KnownCases(string a, string b, double expected)
    {
        var sim = TranslationCacheRepository.NormalizedLevenshteinSimilarity(a, b);
        Assert.Equal(expected, sim, 6);
    }
}
