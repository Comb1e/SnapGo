using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnapGo.App.Models;
using SnapGo.App.Services;

namespace SnapGo.App.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly IHistoryService _historyService;

    [ObservableProperty]
    private ObservableCollection<HistoryItemViewModel> _items = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private HistoryItemViewModel? _selectedItem;

    public HistoryViewModel(IHistoryService historyService)
    {
        _historyService = historyService;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var items = await _historyService.GetPagedAsync(0, 50);
        Items = new ObservableCollection<HistoryItemViewModel>(
            items.Select(ToViewModel));
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            await LoadAsync();
            return;
        }

        IsSearching = true;
        try
        {
            var items = await _historyService.SearchAsync(SearchText);
            Items = new ObservableCollection<HistoryItemViewModel>(
                items.Select(ToViewModel));
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(HistoryItemViewModel? item)
    {
        if (item == null) return;
        await _historyService.DeleteAsync(item.Id);
        Items.Remove(item);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    private static HistoryItemViewModel ToViewModel(HistoryItem item)
    {
        BitmapSource? thumbnail = null;
        if (item.Thumbnail is { Length: > 0 })
        {
            using var ms = new MemoryStream(item.Thumbnail);
            var decoder = new PngBitmapDecoder(ms,
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
            thumbnail = decoder.Frames[0];
        }

        return new HistoryItemViewModel
        {
            Id = item.Id,
            Thumbnail = thumbnail,
            OcrText = item.OcrText,
            SourceWindow = item.SourceWindow,
            CreatedAt = item.CreatedAt,
            ImageData = item.ImageData
        };
    }
}

public class HistoryItemViewModel
{
    public long Id { get; init; }
    public BitmapSource? Thumbnail { get; init; }
    public string? OcrText { get; init; }
    public string? SourceWindow { get; init; }
    public DateTime CreatedAt { get; init; }
    public byte[] ImageData { get; init; } = [];
    public string CreatedAtDisplay => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string Title => string.IsNullOrEmpty(OcrText)
        ? $"Screenshot {CreatedAtDisplay}"
        : (OcrText.Length > 60 ? OcrText[..60] + "..." : OcrText);
}
