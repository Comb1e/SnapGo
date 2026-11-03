using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SnapGo.App.Helpers;
using SnapGo.App.Models;
using SnapGo.App.Services;
using Xunit;

namespace SnapGo.Tests;

public class OcrReadingOrderTests
{
    [Fact]
    public void OneRow_SortsLeftToRight()
    {
        var text = OcrReadingOrder.Arrange(new[]
        {
            new OcrLine("right", 100, 0, 160, 20),
            new OcrLine("left", 0, 0, 40, 20),
        });

        Assert.Equal("left right", text);
    }

    [Fact]
    public void TwoRows_SortTopToBottom()
    {
        var text = OcrReadingOrder.Arrange(new[]
        {
            new OcrLine("lower", 0, 40, 40, 60),
            new OcrLine("upper", 0, 0, 40, 20),
        });

        Assert.Equal("upper\nlower", text);
    }

    [Fact]
    public void OverlappingBoxes_StayOneRow()
    {
        var text = OcrReadingOrder.Arrange(new[]
        {
            new OcrLine("right", 180, 4, 240, 24),
            new OcrLine("left", 0, 0, 40, 20),
        });

        Assert.Equal("left right", text);
    }

    [Fact]
    public void RightHandBox_DoesNotJumpTheLowerRow()
    {
        var text = OcrReadingOrder.Arrange(new[]
        {
            new OcrLine("right", 180, 0, 240, 20),
            new OcrLine("left", 0, 8, 40, 28),
            new OcrLine("lower", 0, 40, 50, 60),
        });

        Assert.Equal("left right\nlower", text);
    }

    [Fact]
    public void TallColumn_DoesNotSwallowTheNextRow()
    {
        var text = OcrReadingOrder.Arrange(new[]
        {
            new OcrLine("COL", 0, 0, 40, 80),
            new OcrLine("ROW1", 120, 0, 180, 20),
            new OcrLine("ROW2", 120, 50, 180, 70),
        });

        Assert.Equal("COL\nROW1\nROW2", text);
    }

    [Fact]
    public void Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, OcrReadingOrder.Arrange(Array.Empty<OcrLine>()));
    }
}

public class OcrEngineTests
{
    [Fact]
    public void RecognizesChineseEnglishAndDigits()
    {
        string text = "";
        string model = "";
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                var models = ModelDirectory();
                var settings = Options.Create(new AppSettings
                {
                    Ocr = new OcrSettings
                    {
                        Provider = "Auto",
                        DetModel = Path.Combine(models, "ch_PP-OCRv5_det_mobile.onnx"),
                        ClsModel = Path.Combine(models, "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"),
                        RecModel = Path.Combine(models, "ch_PP-OCRv5_rec_mobile.onnx"),
                        Dict = Path.Combine(models, "ppocrv5_dict.txt"),
                        DoAngle = false
                    }
                });
                using var service = new OcrService(settings, NullLogger<OcrService>.Instance);
                var result = service.RecognizeAsync(RenderSample("SnapGo 42 截图")).GetAwaiter().GetResult();
                text = result.Text;
                model = result.ModelUsed;
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(90)), "OCR timed out.");

        Assert.Null(error?.ToString());
        Assert.Contains("SnapGo", text);
        Assert.Contains("42", text);
        Assert.Contains("截图", text);
        Assert.Contains("PP-OCRv5-mobile", model);
    }

    private static BitmapSource RenderSample(string text)
    {
        if (Fonts.SystemFontFamilies.All(font => font.Source != "Microsoft YaHei"))
            throw new InvalidOperationException("Microsoft YaHei is required to render the OCR sample.");

        const int width = 720;
        const int height = 160;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            var formatted = new FormattedText(
                text,
                CultureInfo.GetCultureInfo("zh-CN"),
                FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei"),
                48,
                Brushes.Black,
                1.25);
            dc.DrawText(formatted, new Point(24, 48));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static string ModelDirectory()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "SnapGo.App", "ocr-models");
            if (File.Exists(Path.Combine(candidate, "ch_PP-OCRv5_rec_mobile.onnx")))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("PP-OCRv5 models were not found next to the app project.");
    }
}
