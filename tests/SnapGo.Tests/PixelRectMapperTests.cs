using System.Runtime.InteropServices;
using SnapGo.App.Helpers;
using SnapGo.App.Services;
using Xunit;

namespace SnapGo.Tests;

public class PixelRectMapperTests
{
    [Fact]
    public void ScaleOne_CopiesTheSelection()
    {
        var rect = PixelRectMapper.FromDip(10, 20, 30, 40, scaleX: 1, scaleY: 1, pixelWidth: 200, pixelHeight: 200);

        Assert.Equal(new PixelRect(10, 20, 30, 40), rect);
    }

    [Fact]
    public void FullScreenAtOneHundredPercent_CoversEveryPixel()
    {
        var rect = PixelRectMapper.FromDip(0, 0, 2560, 1600, scaleX: 1, scaleY: 1, pixelWidth: 2560, pixelHeight: 1600);

        Assert.Equal(new PixelRect(0, 0, 2560, 1600), rect);
    }

    [Fact]
    public void FullScreenAtOneHundredFiftyPercent_CoversThePhysicalBitmap()
    {
        const double scale = 1.5;
        double dipW = 2560 / scale;
        double dipH = 1600 / scale;

        var rect = PixelRectMapper.FromDip(0, 0, dipW, dipH, scale, scale, pixelWidth: 2560, pixelHeight: 1600);

        Assert.Equal(new PixelRect(0, 0, 2560, 1600), rect);
    }

    [Fact]
    public void OneHundredTwentyFivePercent_LandsOnWholePixels()
    {
        const double scale = 1.25;
        var rect = PixelRectMapper.FromDip(8, 16, 80, 40, scale, scale, pixelWidth: 2560, pixelHeight: 1600);

        Assert.Equal(new PixelRect(10, 20, 100, 50), rect);
    }

    [Fact]
    public void FractionalDip_IncludesThePartialEdgeATruncatingCastWouldDrop()
    {
        // (int)100.9 == 100 drops the last partial pixel. The saved crop must still contain it.
        var rect = PixelRectMapper.FromDip(0, 0, 100.9, 50.2, scaleX: 1, scaleY: 1, pixelWidth: 200, pixelHeight: 200);

        Assert.Equal(new PixelRect(0, 0, 101, 51), rect);
    }

    [Fact]
    public void SelectionPastTheBitmap_IsClamped()
    {
        var rect = PixelRectMapper.FromDip(-10, -4, 30, 20, scaleX: 1, scaleY: 1, pixelWidth: 100, pixelHeight: 80);

        Assert.Equal(new PixelRect(0, 0, 20, 16), rect);
    }

    [Fact]
    public void SelectionHangingOffTheBottomRight_IsClamped()
    {
        var rect = PixelRectMapper.FromDip(90, 70, 40, 40, scaleX: 1, scaleY: 1, pixelWidth: 100, pixelHeight: 80);

        Assert.Equal(new PixelRect(90, 70, 10, 10), rect);
    }
}

public class ScreenshotCaptureTests
{
    [Fact]
    public void FullScreenCapture_MatchesPhysicalVirtualScreen()
    {
        int pixelW = 0, pixelH = 0, screenW = 0, screenH = 0;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                var captured = new ScreenshotService().CaptureFullScreenAsync().GetAwaiter().GetResult();
                pixelW = captured.PixelWidth;
                pixelH = captured.PixelHeight;

                var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
                try
                {
                    screenW = GetSystemMetrics(78);
                    screenH = GetSystemMetrics(79);
                }
                finally
                {
                    if (previous != IntPtr.Zero)
                        SetThreadDpiAwarenessContext(previous);
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Capture timed out.");

        Assert.Null(error?.ToString());
        Assert.Equal(screenW, pixelW);
        Assert.Equal(screenH, pixelH);
        Assert.True(pixelW > 0 && pixelH > 0);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
}
