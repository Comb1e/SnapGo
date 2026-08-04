using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnapGo.App.Data;
using SnapGo.App.Helpers;
using SnapGo.App.Models;

namespace SnapGo.App.Services;

public class HistoryService : IHistoryService
{
    private readonly AppDbContext _db;
    private readonly HistorySettings _settings;
    private readonly ILogger<HistoryService> _logger;

    public HistoryService(IOptions<AppSettings> options, ILogger<HistoryService> logger)
    {
        _settings = options.Value.History;
        _logger = logger;

        var dbPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SnapGo", "history.db");

        var dir = System.IO.Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _db = new AppDbContext($"Data Source={dbPath}");
    }

    public async Task<IReadOnlyList<HistoryItem>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var items = new List<HistoryItem>();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Thumbnail, OcrText, SourceWindow, CreatedAt, ImageData,
                   Annotations, FilePath
            FROM HistoryItems
            ORDER BY CreatedAt DESC
            LIMIT @take OFFSET @skip
            """;
        cmd.Parameters.AddWithValue("@take", pageSize);
        cmd.Parameters.AddWithValue("@skip", page * pageSize);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(MapItem(reader));
        }
        return items;
    }

    public async Task<HistoryItem?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM HistoryItems WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            return MapItem(reader);

        return null;
    }

    public async Task<long> AddAsync(HistoryItem item, CancellationToken ct = default)
    {
        // Generate thumbnail if not provided
        if (item.Thumbnail == null && item.ImageData.Length > 0)
        {
            item.Thumbnail = ImageHelper.CreateThumbnail(
                item.ImageData, _settings.ThumbnailWidth, _settings.ThumbnailHeight);
        }

        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO HistoryItems (ImageData, Thumbnail, OcrText, Annotations,
                                       SourceWindow, CreatedAt, FilePath)
            VALUES (@img, @thumb, @ocr, @annot, @src, @created, @path)
            """;
        cmd.Parameters.AddWithValue("@img", item.ImageData);
        cmd.Parameters.AddWithValue("@thumb", item.Thumbnail as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ocr", item.OcrText as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@annot", item.Annotations as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@src", item.SourceWindow as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created", item.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@path", item.FilePath as object ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);

        // Get the last inserted id
        using var idCmd = _db.Connection.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        var id = (long)(await idCmd.ExecuteScalarAsync(ct))!;

        // Prune if needed
        await PruneIfNeededAsync(ct);

        return id;
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM HistoryItems WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<HistoryItem>> SearchAsync(string keyword, CancellationToken ct = default)
    {
        var items = new List<HistoryItem>();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Thumbnail, OcrText, SourceWindow, CreatedAt, ImageData,
                   Annotations, FilePath
            FROM HistoryItems
            WHERE OcrText LIKE '%' || @kw || '%'
               OR SourceWindow LIKE '%' || @kw || '%'
            ORDER BY CreatedAt DESC
            LIMIT 100
            """;
        cmd.Parameters.AddWithValue("@kw", keyword);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(MapItem(reader));
        }
        return items;
    }

    private async Task PruneIfNeededAsync(CancellationToken ct)
    {
        using var countCmd = _db.Connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM HistoryItems";
        var count = (long)(await countCmd.ExecuteScalarAsync(ct))!;

        if (count > _settings.MaxRecords)
        {
            var toDelete = count - _settings.MaxRecords;
            using var deleteCmd = _db.Connection.CreateCommand();
            deleteCmd.CommandText = """
                DELETE FROM HistoryItems WHERE Id IN (
                    SELECT Id FROM HistoryItems ORDER BY CreatedAt ASC LIMIT @n
                )
                """;
            deleteCmd.Parameters.AddWithValue("@n", toDelete);
            await deleteCmd.ExecuteNonQueryAsync(ct);
            _logger.LogInformation("Pruned {Count} old history items", toDelete);
        }
    }

    private static HistoryItem MapItem(SqliteDataReader reader)
    {
        return new HistoryItem
        {
            Id = reader.GetInt64(0),
            Thumbnail = reader.IsDBNull(1) ? null : (byte[])reader["Thumbnail"],
            OcrText = reader.IsDBNull(2) ? null : reader.GetString(2),
            SourceWindow = reader.IsDBNull(3) ? null : reader.GetString(3),
            CreatedAt = DateTime.Parse(reader.GetString(4)),
            ImageData = (byte[])reader["ImageData"],
            Annotations = reader.IsDBNull(6) ? null : reader.GetString(6),
            FilePath = reader.IsDBNull(7) ? null : reader.GetString(7)
        };
    }
}
