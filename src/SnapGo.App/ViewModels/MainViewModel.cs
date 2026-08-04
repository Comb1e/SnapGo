using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SnapGo.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusMessage = "SnapGo is running. Press Ctrl+Shift+S to capture.";

    [ObservableProperty]
    private bool _isCapturing;

    [ObservableProperty]
    private BitmapSource? _currentScreenshot;

    public event EventHandler? ScreenshotRequested;

    [RelayCommand]
    private void RequestScreenshot()
    {
        ScreenshotRequested?.Invoke(this, EventArgs.Empty);
    }
}
