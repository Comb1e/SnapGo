namespace SnapGo.App.Models;

public class OcrResult
{
    public string Text { get; init; } = string.Empty;
    public long ElapsedMs { get; init; }
    public string ModelUsed { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
