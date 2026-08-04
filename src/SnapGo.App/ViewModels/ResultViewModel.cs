using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnapGo.App.Models;
using SnapGo.App.Services;

namespace SnapGo.App.ViewModels;

public partial class ResultViewModel : ObservableObject
{
    private readonly IClipboardService _clipboardService;

    [ObservableProperty]
    private string _recognizedText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _loadingMessage = "Recognizing text...";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _elapsedInfo = string.Empty;

    [ObservableProperty]
    private string _modelInfo = string.Empty;

    public OcrResult? Result { get; private set; }

    public ResultViewModel(IClipboardService clipboardService)
    {
        _clipboardService = clipboardService;
    }

    public void SetResult(OcrResult result)
    {
        Result = result;
        RecognizedText = result.Text;
        ElapsedInfo = $"Time: {result.ElapsedMs / 1000.0:F1}s";
        ModelInfo = $"Model: {result.ModelUsed}";
        IsLoading = false;
    }

    public void SetError(string message)
    {
        ErrorMessage = message;
        HasError = true;
        IsLoading = false;
    }

    public void SetLoading(string message = "Recognizing text...")
    {
        LoadingMessage = message;
        IsLoading = true;
        HasError = false;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void CopyText()
    {
        if (!string.IsNullOrEmpty(RecognizedText))
        {
            _clipboardService.CopyText(RecognizedText);
        }
    }

    [RelayCommand]
    private async Task SaveAsText()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text File|*.txt",
            DefaultExt = "txt",
            FileName = $"ocr_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };

        if (dialog.ShowDialog() == true)
        {
            await File.WriteAllTextAsync(dialog.FileName, RecognizedText);
        }
    }
}
