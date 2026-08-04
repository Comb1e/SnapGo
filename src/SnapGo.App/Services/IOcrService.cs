using System.Windows.Media.Imaging;
using SnapGo.App.Models;

namespace SnapGo.App.Services;

public interface IOcrService
{
    /// <summary>Send a screenshot to PaddleOCR-VL and return recognized text.</summary>
    Task<OcrResult> RecognizeAsync(BitmapSource image, CancellationToken cancellationToken = default);
}
