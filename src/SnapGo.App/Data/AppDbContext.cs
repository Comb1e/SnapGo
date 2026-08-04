using Microsoft.Data.Sqlite;
using SnapGo.App.Models;

namespace SnapGo.App.Data;

public class AppDbContext : IDisposable
{
    private readonly SqliteConnection _connection;

    public AppDbContext(string connectionString)
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        EnsureCreated();
    }

    private void EnsureCreated()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS HistoryItems (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                ImageData     BLOB    NOT NULL,
                Thumbnail     BLOB,
                OcrText       TEXT,
                Annotations   TEXT,
                SourceWindow  TEXT,
                CreatedAt     TEXT    NOT NULL,
                FilePath      TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_HistoryItems_CreatedAt
                ON HistoryItems(CreatedAt DESC);
            """;
        cmd.ExecuteNonQuery();
    }

    public SqliteConnection Connection => _connection;

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }
}
