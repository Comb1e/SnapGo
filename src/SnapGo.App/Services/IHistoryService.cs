using SnapGo.App.Models;

namespace SnapGo.App.Services;

public interface IHistoryService
{
    Task<IReadOnlyList<HistoryItem>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
    Task<HistoryItem?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<long> AddAsync(HistoryItem item, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<HistoryItem>> SearchAsync(string keyword, CancellationToken ct = default);
}
