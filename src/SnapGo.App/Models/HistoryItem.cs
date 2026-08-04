namespace SnapGo.App.Models;

public class HistoryItem
{
    public long Id { get; set; }
    public byte[] ImageData { get; set; } = Array.Empty<byte>();
    public byte[]? Thumbnail { get; set; }
    public string? OcrText { get; set; }
    public string? Annotations { get; set; }
    public string? SourceWindow { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? FilePath { get; set; }
}
