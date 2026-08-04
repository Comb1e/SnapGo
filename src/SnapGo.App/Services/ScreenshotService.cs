using System.Drawing;
using System.Windows.Media.Imaging;
using SnapGo.App.Helpers;

namespace SnapGo.App.Services;

public class ScreenshotService : IScreenshotService
{
    public event EventHandler<ScreenshotCapturedEventArgs>? ScreenshotCaptured;

    public Task<BitmapSource> CaptureForegroundWindowAsync()
    {
        var hWnd = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowRect(hWnd, out var rect);

        // Fallback: if window rect is empty, capture entire screen
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return Task.FromResult(CaptureScreen());
        }

        var bitmap = new Bitmap(rect.Width, rect.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(rect.Width, rect.Height));

        var result = ImageHelper.ToBitmapSource(bitmap);
        bitmap.Dispose();
        return Task.FromResult(result);
    }

    public BitmapSource CropRegion(BitmapSource source, int x, int y, int width, int height)
    {
        var cropped = new CroppedBitmap(source, new System.Windows.Int32Rect(x, y, width, height));
        // Force render to a clean bitmap (CroppedBitmap retains reference to source)
        var copy = new RenderTargetBitmap(width, height, source.DpiX, source.DpiY,
            System.Windows.Media.PixelFormats.Pbgra32);
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(cropped, new System.Windows.Rect(0, 0, width, height));
        }
        copy.Render(visual);
        copy.Freeze();
        return copy;
    }

    public Task<BitmapSource> CaptureFullScreenAsync()
    {
        return Task.FromResult(CaptureScreen());
    }

    /// <summary>Trigger the ScreenshotCaptured event (called by overlay on confirmation).</summary>
    public void NotifyScreenshotCaptured(BitmapSource image, int x, int y, int width, int height)
    {
        ScreenshotCaptured?.Invoke(this, new ScreenshotCapturedEventArgs
        {
            Image = image,
            X = x,
            Y = y,
            Width = width,
            Height = height
        });
    }

    private static BitmapSource CaptureScreen()
    {
        var screenRect = new System.Drawing.Rectangle(
            0, 0,
            (int)System.Windows.SystemParameters.PrimaryScreenWidth,
            (int)System.Windows.SystemParameters.PrimaryScreenHeight);

        using var bitmap = new Bitmap(screenRect.Width, screenRect.Height,
            System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(0, 0, 0, 0, bitmap.Size);

        return ImageHelper.ToBitmapSource(bitmap);
    }
}
