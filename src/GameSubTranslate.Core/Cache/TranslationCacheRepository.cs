using Dapper;
using System.Security.Cryptography;
using System.Text;
using GameSubTranslate.Storage;

namespace GameSubTranslate.Cache;

/// <summary>Persistent translation cache keyed by SHA-256(sourceText + "|" + targetLang).</summary>
public sealed class TranslationCacheRepository
{
    private readonly Database _db;

    public TranslationCacheRepository(Database db) => _db = db;

    public static string Hash(string sourceText, string targetLang)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceText}|{targetLang}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public string? Get(string sourceText, string targetLang)
    {
        using var conn = _db.Open();
        return conn.QuerySingleOrDefault<string>(
            "SELECT TranslatedText FROM TranslationCache WHERE TextHash = @Hash",
            new { Hash = Hash(sourceText, targetLang) });
    }

    public void Put(string sourceText, string translatedText, string targetLang, DateTime? createdAt = null)
    {
        using var conn = _db.Open();
        conn.Execute("""
            INSERT INTO TranslationCache (TextHash, SourceText, TranslatedText, SourceLang, TargetLang, CreatedAt)
            VALUES (@Hash, @SourceText, @TranslatedText, 'auto', @TargetLang, @CreatedAt)
            ON CONFLICT(TextHash) DO UPDATE SET TranslatedText = @TranslatedText, CreatedAt = @CreatedAt
            """, new
        {
            Hash = Hash(sourceText, targetLang),
            SourceText = sourceText,
            TranslatedText = translatedText,
            TargetLang = targetLang,
            CreatedAt = (createdAt ?? DateTime.UtcNow).ToString("o"),
        });
    }

    /// <summary>
    /// Remove cache entries created strictly before <paramref name="cutoff"/>. Returns the
    /// number of rows deleted. Used for cache size management (T41 hook in the future).
    /// </summary>
    public int DeleteOlderThan(DateTime cutoff)
    {
        using var conn = _db.Open();
        return conn.Execute(
            "DELETE FROM TranslationCache WHERE CreatedAt < @Cutoff",
            new { Cutoff = cutoff.ToString("o") });
    }

    /// <summary>
    /// T37: scan recent cache rows for this target lang and return the entry whose
    /// SourceText is closest to <paramref name="sourceText"/> by normalized Levenshtein
    /// distance, provided the similarity is at least <paramref name="similarityThreshold"/>.
    /// Returns null if no row clears the bar. Recent is bounded by <paramref name="maxScanRows"/>
    /// (default 500) ordered by CreatedAt DESC — keeps the scan cheap as the cache grows.
    /// </summary>
    public (string translated, double similarity)? GetFuzzy(
        string sourceText, string targetLang, double similarityThreshold = 0.85, int maxScanRows = 500)
    {
        if (string.IsNullOrEmpty(sourceText)) return null;

        using var conn = _db.Open();
        var rows = conn.Query<(string SourceText, string TranslatedText)>(
            @"SELECT SourceText, TranslatedText
              FROM TranslationCache
              WHERE TargetLang = @TargetLang
              ORDER BY CreatedAt DESC
              LIMIT @Limit",
            new { TargetLang = targetLang, Limit = maxScanRows })
            .ToList();

        (string Translated, double Sim)? best = null;
        foreach (var row in rows)
        {
            var sim = NormalizedLevenshteinSimilarity(sourceText, row.SourceText);
            if (sim < similarityThreshold) continue;
            if (best is null || sim > best.Value.Sim)
                best = (row.TranslatedText, sim);
        }
        return best;
    }

    /// <summary>
    /// Normalized Levenshtein similarity: <c>1 - editDistance / max(lenA, lenB)</c>.
    /// Both empty → 1.0. Either empty (other not) → 0.0. Identical → 1.0.
    /// Hand-rolled (no NuGet) — T37 ceiling: swap to a BK-tree once cache &gt; ~10k rows.
    /// </summary>
    public static double NormalizedLevenshteinSimilarity(string a, string b)
    {
        if (a == b) return 1.0;
        if (a.Length == 0 || b.Length == 0) return 0.0;

        // Standard 2-row DP. O(min(lenA,lenB)) memory, O(lenA*lenB) time — fine for
        // subtitle-length strings (typically &lt; 200 chars) even with 500-row scans.
        var s = a.Length <= b.Length ? a : b;
        var t = a.Length <= b.Length ? b : a;
        var prev = new int[s.Length + 1];
        var curr = new int[s.Length + 1];
        for (int j = 0; j <= s.Length; j++) prev[j] = j;

        for (int i = 1; i <= t.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= s.Length; j++)
            {
                int cost = s[j - 1] == t[i - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        int dist = prev[s.Length];
        int maxLen = Math.Max(a.Length, b.Length);
        return 1.0 - (double)dist / maxLen;
    }
}
