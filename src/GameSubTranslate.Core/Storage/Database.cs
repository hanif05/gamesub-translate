using Microsoft.Data.Sqlite;

namespace GameSubTranslate.Storage;

/// <summary>
/// Owns the SQLite connection factory and one-time schema creation.
/// SqliteConnection is created per use (Dapper style) — no long-lived connection.
/// </summary>
public sealed class Database
{
    public string DbPath { get; }

    public Database(string? dbPath = null)
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        DbPath = dbPath ?? Path.Combine(dir, "GameSubTranslate", "profiles.db");
    }

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        return conn;
    }

    /// <summary>Creates the DB file and all tables if they don't exist. Safe to call at every startup.</summary>
    public void EnsureSchema()
    {
        // In-memory SQLite (":memory:" or shared-cache URIs) has no parent directory —
        // skip the mkdir to keep the schema-init path testable without a tmp file.
        var dir = Path.GetDirectoryName(DbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS GameProfile (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                ExecutableName TEXT NULL,
                SourceLang TEXT NOT NULL DEFAULT 'auto',
                TargetLang TEXT NOT NULL DEFAULT 'id',
                OcrEngine INTEGER NOT NULL DEFAULT 0,
                CaptureIntervalMs INTEGER NOT NULL DEFAULT 800,
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS CaptureRegion (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProfileId INTEGER NOT NULL REFERENCES GameProfile(Id) ON DELETE CASCADE,
                RegionName TEXT NOT NULL,
                X INTEGER NOT NULL,
                Y INTEGER NOT NULL,
                Width INTEGER NOT NULL,
                Height INTEGER NOT NULL,
                MonitorIndex INTEGER NOT NULL DEFAULT 0,
                IsActiveDefault INTEGER NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS TranslationCache (
                TextHash TEXT PRIMARY KEY,
                SourceText TEXT NOT NULL,
                TranslatedText TEXT NOT NULL,
                SourceLang TEXT NOT NULL,
                TargetLang TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_CaptureRegion_ProfileId ON CaptureRegion(ProfileId);
            """;
        cmd.ExecuteNonQuery();

        // F87: idempotent column add. SQLite has no IF NOT EXISTS for ALTER TABLE ADD COLUMN,
        // so we probe pragma_table_info first. Old databases (pre-Fase 6) lack this column;
        // running EnsureSchema on them must backfill it without breaking the app.
        EnsureColumn(conn, "GameProfile", "PaddleUseGpu", "INTEGER NOT NULL DEFAULT 0");
    }

    private static void EnsureColumn(SqliteConnection conn, string table, string column, string definition)
    {
        using var probe = conn.CreateCommand();
        probe.CommandText = $"SELECT 1 FROM pragma_table_info('{table}') WHERE name = @c";
        probe.Parameters.AddWithValue("@c", column);
        var exists = probe.ExecuteScalar();
        if (exists is not null) return;

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }
}
