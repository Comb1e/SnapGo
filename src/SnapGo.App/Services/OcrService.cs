using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using RapidOcrNet;
using SkiaSharp;
using SnapGo.App.Helpers;
using SnapGo.App.Models;

namespace SnapGo.App.Services;

/// <summary>In-process PP-OCRv5. DirectML when it loads, otherwise CPU.</summary>
public sealed class OcrService : IOcrService, IDisposable
{
    private enum EngineState { Unloaded, Loading, Ready, Failed }

    private readonly OcrSettings _ocr;
    private readonly ILogger<OcrService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RapidOcr? _engine;
    private EngineState _state = EngineState.Unloaded;
    private string _provider = "CPU";

    public OcrService(IOptions<AppSettings> options, ILogger<OcrService> logger)
    {
        _ocr = options.Value.Ocr;
        _logger = logger;
    }

    public async Task<Models.OcrResult> RecognizeAsync(BitmapSource image, CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var bitmap = ToSkBitmap(image);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var text = await Task.Run(() =>
            {
                EnsureLoaded();
                return Recognize(bitmap, cancellationToken);
            }, cancellationToken);

            sw.Stop();
            return new Models.OcrResult
            {
                Text = text,
                ElapsedMs = sw.ElapsedMilliseconds,
                ModelUsed = $"PP-OCRv5-mobile ({_provider})",
                Timestamp = DateTime.UtcNow
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OcrException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new OcrException("OCR failed.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _engine?.Dispose();
        _engine = null;
        _gate.Dispose();
    }

    private void EnsureLoaded()
    {
        if (_state == EngineState.Ready && _engine != null)
            return;

        if (_state == EngineState.Failed)
        {
            _engine?.Dispose();
            _engine = null;
            _state = EngineState.Unloaded;
        }

        _state = EngineState.Loading;
        try
        {
            var det = Resolve(_ocr.DetModel);
            var cls = Resolve(_ocr.ClsModel);
            var rec = Resolve(_ocr.RecModel);
            var keys = Resolve(_ocr.Dict);
            foreach (var path in new[] { det, cls, rec, keys })
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException($"OCR model file is missing: {path}", path);
            }

            var (engine, provider) = LoadEngine(det, cls, rec, keys);
            _engine = engine;
            _provider = provider;
            _state = EngineState.Ready;
            _logger.LogInformation("OCR ready on {Provider}", provider);
        }
        catch (Exception ex) when (ex is not OcrException)
        {
            _state = EngineState.Failed;
            throw new OcrException("OCR model failed to load.", ex);
        }
        catch
        {
            _state = EngineState.Failed;
            throw;
        }
    }

    private (RapidOcr Engine, string Provider) LoadEngine(string det, string cls, string rec, string keys)
    {
        var wantGpu = !string.Equals(_ocr.Provider, "CPU", StringComparison.OrdinalIgnoreCase);
        if (wantGpu)
        {
            RapidOcr? gpu = null;
            try
            {
                gpu = new RapidOcr();
                using var options = RapidOcr.GetDefaultSessionOptions();
                options.AppendExecutionProvider_DML(0);
                gpu.InitModels(det, cls, rec, keys, options);
                return (gpu, "DirectML");
            }
            catch (Exception ex)
            {
                // A failed InitModels leaves the classifier unset, and Dispose then throws.
                try { gpu?.Dispose(); } catch (Exception disposeEx) { _logger.LogDebug(disposeEx, "Ignoring OCR dispose failure"); }
                if (string.Equals(_ocr.Provider, "DirectML", StringComparison.OrdinalIgnoreCase))
                    throw;
                _logger.LogWarning(ex, "DirectML is unavailable; OCR will use the CPU");
            }
        }

        var cpu = new RapidOcr();
        cpu.InitModels(det, cls, rec, keys);
        return (cpu, "CPU");
    }

    private string Recognize(SKBitmap bitmap, CancellationToken cancellationToken)
    {
        if (_engine == null)
            throw new OcrException("OCR engine is not loaded.");

        cancellationToken.ThrowIfCancellationRequested();
        var options = CopyOptions(RapidOcrOptions.Default, _ocr.DoAngle);
        var result = _engine.Detect(bitmap, options);
        var lines = new List<OcrLine>(result.TextBlocks.Length);
        foreach (var block in result.TextBlocks)
        {
            if (block.BoxPoints.Length == 0)
                continue;
            var left = block.BoxPoints.Min(point => point.X);
            var top = block.BoxPoints.Min(point => point.Y);
            var right = block.BoxPoints.Max(point => point.X);
            var bottom = block.BoxPoints.Max(point => point.Y);
            lines.Add(new OcrLine(block.Text, left, top, right, bottom));
        }

        return OcrReadingOrder.Arrange(lines);
    }

    private static RapidOcrOptions CopyOptions(RapidOcrOptions source, bool doAngle) => new()
    {
        Padding = source.Padding,
        ImgResize = source.ImgResize,
        LimitSideLen = source.LimitSideLen,
        MaxSideLen = source.MaxSideLen,
        MinSideLen = source.MinSideLen,
        WidthHeightRatio = source.WidthHeightRatio,
        MinHeight = source.MinHeight,
        TextScore = source.TextScore,
        ClsThresh = source.ClsThresh,
        ClsPreserveAspectRatio = source.ClsPreserveAspectRatio,
        BoxScoreThresh = source.BoxScoreThresh,
        BoxThresh = source.BoxThresh,
        UnClipRatio = source.UnClipRatio,
        DoAngle = doAngle,
        MostAngle = source.MostAngle,
        ReturnWordBox = source.ReturnWordBox,
        ReturnSingleCharBox = source.ReturnSingleCharBox
    };

    private static string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new OcrException("An OCR model path is empty.");
        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private static SKBitmap ToSkBitmap(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        int width = converted.PixelWidth;
        int height = converted.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        var destination = bitmap.GetPixels();
        if (destination == IntPtr.Zero)
            throw new OcrException("Could not allocate the OCR image.");
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, destination, pixels.Length);
        return bitmap;
    }
}

public class OcrException : Exception
{
    public OcrException(string message, Exception? inner = null) : base(message, inner) { }
}
