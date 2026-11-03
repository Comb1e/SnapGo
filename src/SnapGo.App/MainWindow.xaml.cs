using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using SnapGo.App.Helpers;
using SnapGo.App.Models;
using SnapGo.App.Services;
using SnapGo.App.ViewModels;

namespace SnapGo.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IScreenshotService _screenshotService;
    private HotkeyHelper? _hotkeyHelper;
    private ScreenshotOverlay? _currentOverlay;
    private ToolbarAction? _pendingAction;

    public MainWindow(MainViewModel viewModel, IScreenshotService screenshotService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _screenshotService = screenshotService;
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetTrayIcon();
        RegisterHotkey();
        _viewModel.ScreenshotRequested += OnScreenshotRequested;
        _screenshotService.ScreenshotCaptured += OnScreenshotCaptured;
    }

    private void SetTrayIcon()
    {
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "icon", "SnapGo.ico");
        try
        {
            TrayIcon.Icon = System.IO.File.Exists(iconPath)
                ? new Icon(iconPath)
                : SystemIcons.Application;
        }
        catch
        {
            TrayIcon.Icon = SystemIcons.Application;
        }
    }

    private void RegisterHotkey()
    {
        var handle = new WindowInteropHelper(this).Handle;
        _hotkeyHelper = new HotkeyHelper(
            hotkeyId: 9001,
            modifiers: NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT,
            key: NativeMethods.VK_S);
        _hotkeyHelper.HotkeyPressed += (_, _) => OnHotkeyPressed();
        _hotkeyHelper.Register(handle);
    }

    private void TrayIcon_LeftClick(object sender, RoutedEventArgs e) => OnHotkeyPressed();
    private void TraySettings_Click(object sender, RoutedEventArgs e) => OpenSettings();
    private void TrayExit_Click(object sender, RoutedEventArgs e) => ShutdownApp();

    // ============================================================
    // Screenshot flow
    // ============================================================

    private async void OnHotkeyPressed()
    {
        if (_viewModel.IsCapturing) return;

        try
        {
            _viewModel.IsCapturing = true;
            await Task.Delay(50);

            var screenshot = await _screenshotService.CaptureFullScreenAsync();
            var overlay = ((App)Application.Current).Services.GetRequiredService<ScreenshotOverlay>();
            overlay.SetSourceImage(screenshot);
            overlay.Owner = this;

            // When the user finishes dragging, show the toolbar
            overlay.SelectionReady += OnOverlaySelectionReady;

            _currentOverlay = overlay;
            overlay.ShowDialog();

            // Cleanup after overlay closes
            overlay.SelectionReady -= OnOverlaySelectionReady;
            overlay.ToolbarActionRequested -= OnToolbarAction;
            _currentOverlay = null;

            // Execute the action AFTER the overlay's modal loop has fully exited
            if (_pendingAction.HasValue && _viewModel.CurrentScreenshot != null)
            {
                var action = _pendingAction.Value;
                _pendingAction = null;
                await ExecuteActionAsync(action, _viewModel.CurrentScreenshot);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Screenshot failed: {ex.Message}");
        }
        finally
        {
            _viewModel.IsCapturing = false;
        }
    }

    private void OnOverlaySelectionReady(object? sender, SelectionReadyEventArgs e)
    {
        var overlay = (ScreenshotOverlay)sender!;
        // Toolbar is now embedded in the overlay canvas — subscribe to its action event
        overlay.ToolbarActionRequested += OnToolbarAction;
    }

    private void OnToolbarAction(object? sender, ToolbarAction action)
    {
        if (action == ToolbarAction.Cancel)
        {
            _currentOverlay?.Close();
            return;
        }

        // Crop now, but defer execution until after the overlay's modal loop exits
        if (_currentOverlay == null) return;

        var cropped = _currentOverlay.CropCurrentSelection();
        _viewModel.CurrentScreenshot = cropped;
        _pendingAction = action;
        _currentOverlay.ConfirmAndClose();
    }

    private async Task ExecuteActionAsync(ToolbarAction action, BitmapSource image)
    {
        switch (action)
        {
            case ToolbarAction.Save:
                SaveScreenshot(image);
                break;
            case ToolbarAction.Copy:
                CopyScreenshot(image);
                break;
            case ToolbarAction.OCR:
                await PerformOcrAsync(image);
                break;
            case ToolbarAction.Annotate:
                AnnotateScreenshot(image);
                break;
        }
    }

    /// <summary>Called AFTER the overlay confirms (crops + fires event).</summary>
    private void OnScreenshotCaptured(object? sender, ScreenshotCapturedEventArgs e)
    {
        _viewModel.CurrentScreenshot = e.Image;
        _viewModel.StatusMessage = $"Captured {e.Width}×{e.Height}";
    }

    private void OnScreenshotRequested(object? sender, EventArgs e) => OnHotkeyPressed();

    // ============================================================
    // Actions
    // ============================================================

    private void SaveScreenshot(BitmapSource image)
    {
        var settings = ((App)Application.Current).Services
            .GetRequiredService<IOptionsMonitor<AppSettings>>().CurrentValue;
        var defaultPath = settings.Storage.DefaultSavePath;

        string? initialDir = null;
        if (!string.IsNullOrWhiteSpace(defaultPath))
        {
            initialDir = System.IO.Path.IsPathRooted(defaultPath)
                ? defaultPath
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, defaultPath));
            System.IO.Directory.CreateDirectory(initialDir);
        }

        var dialog = new SaveFileDialog
        {
            Filter = "PNG Image|*.png",
            DefaultExt = "png",
            FileName = $"screenshot_{System.DateTime.Now:yyyyMMdd_HHmmss}.png"
        };
        if (initialDir != null) dialog.InitialDirectory = initialDir;

        if (dialog.ShowDialog() == true)
        {
            using var stream = new FileStream(dialog.FileName, FileMode.Create);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(stream);

            // Also copy to clipboard
            CopyScreenshot(image);
            _viewModel.StatusMessage = $"Saved to {dialog.FileName} (also copied to clipboard)";
        }
    }

    private void CopyScreenshot(BitmapSource image)
    {
        var clipboard = ((App)Application.Current).Services.GetRequiredService<IClipboardService>();
        clipboard.CopyImage(image);
    }

    private void AnnotateScreenshot(BitmapSource image)
    {
        var annotWindow = new AnnotationWindow { Owner = this };
        annotWindow.SetScreenshot(image);

        if (annotWindow.ShowDialog() == true && annotWindow.ResultImage != null)
        {
            using var ms = new MemoryStream(annotWindow.ResultImage);
            var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
            _viewModel.CurrentScreenshot = decoder.Frames[0];
            _viewModel.StatusMessage = "Annotation applied";
        }
    }

    private async Task PerformOcrAsync(BitmapSource image)
    {
        var app = (App)Application.Current;
        var ocrService = app.Services.GetRequiredService<IOcrService>();
        var resultPanel = app.Services.GetRequiredService<ResultPanel>();
        resultPanel.Owner = this;
        resultPanel.SetLoading("Loading OCR model...");
        resultPanel.Show();

        try
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            resultPanel.SetLoading("Recognizing text...");
            var result = await ocrService.RecognizeAsync(image, cts.Token);
            resultPanel.SetResult(result);
            _viewModel.StatusMessage = $"OCR complete in {result.ElapsedMs / 1000.0:F1}s ({result.ModelUsed})";
        }
        catch (OcrException ex)
        {
            resultPanel.SetError(ex.Message);
            _viewModel.StatusMessage = "OCR failed";
        }
        catch (Exception ex)
        {
            resultPanel.SetError($"Unexpected error: {ex.Message}");
            _viewModel.StatusMessage = "OCR failed";
        }
    }

    // ============================================================
    // Window / tray
    // ============================================================

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        ShowInTaskbar = false;
    }

    private void OpenSettings()
    {
        var settingsWindow = ((App)Application.Current).Services.GetRequiredService<SettingsWindow>();
        settingsWindow.Owner = this;
        settingsWindow.ShowDialog();
    }

    private void ShutdownApp()
    {
        _hotkeyHelper?.Dispose();
        TrayIcon.Dispose();
        Application.Current.Shutdown();
    }
}
