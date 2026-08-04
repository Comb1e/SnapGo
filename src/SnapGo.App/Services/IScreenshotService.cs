using System.Windows.Media.Imaging;

namespace SnapGo.App.Services;

public interface IScreenshotService
{
    /// <summary>Capture the foreground window as a BitmapSource.</summary>
    Task<BitmapSource> CaptureForegroundWindowAsync();

    /// <summary>Capture the entire primary screen.</summary>
    Task<BitmapSource> CaptureFullScreenAsync();

    /// <summary>Crop a region from a screenshot.</summary>
    BitmapSource CropRegion(BitmapSource source, int x, int y, int width, int height);

    /// <summary>Triggered when the screenshot overlay is confirmed with a region.</summary>
    event EventHandler<ScreenshotCapturedEventArgs>? ScreenshotCaptured;

    /// <summary>Notify listeners that a screenshot was captured.</summary>
    void NotifyScreenshotCaptured(BitmapSource image, int x, int y, int width, int height);
}

public class ScreenshotCapturedEventArgs : EventArgs
{
    public BitmapSource Image { get; init; } = null!;
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}
