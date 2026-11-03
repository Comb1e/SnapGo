using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Media.Imaging;
using SnapGo.App.Helpers;

namespace SnapGo.App.Services;

public class ScreenshotService : IScreenshotService
{
    public event EventHandler<ScreenshotCapturedEventArgs>? ScreenshotCaptured;

    public Task<BitmapSource> CaptureForegroundWindowAsync()
    {
        return Task.FromResult(NativeMethods.WithPerMonitorDpi(() =>
        {
            var hWnd = NativeMethods.GetForegroundWindow();
            NativeMethods.GetWindowRect(hWnd, out var rect);

            if (rect.Width <= 0 || rect.Height <= 0)
                return CaptureVirtualScreen();

            return CaptureRect(new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height));
        }));
    }

    public BitmapSource CropRegion(BitmapSource source, int x, int y, int width, int height)
    {
        return source.CropPixels(x, y, width, height);
    }

    public Task<BitmapSource> CaptureFullScreenAsync()
    {
        return Task.FromResult(NativeMethods.WithPerMonitorDpi(CaptureVirtualScreen));
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

    /// <summary>
    /// Capture the virtual desktop in physical pixels. Caller must already be in a per-monitor DPI context,
    /// otherwise GetSystemMetrics returns DIP-sized values and the bitmap is a scaled-down screen.
    /// </summary>
    private static BitmapSource CaptureVirtualScreen()
    {
        var bounds = new Rectangle(
            NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));
        return CaptureRect(bounds);
    }

    private static BitmapSource CaptureRect(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("Capture bounds are empty.");

        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        }

        return bitmap.ToBitmapSource();
    }
}
