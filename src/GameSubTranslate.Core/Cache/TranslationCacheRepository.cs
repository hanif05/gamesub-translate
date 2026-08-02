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

    public void Put(string sourceText, string translatedText, string targetLang)
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
            CreatedAt = DateTime.UtcNow.ToString("o"),
        });
    }
}
