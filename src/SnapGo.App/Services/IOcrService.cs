using System.Windows.Media.Imaging;
using SnapGo.App.Models;

namespace SnapGo.App.Services;

public interface IOcrService
{
    /// <summary>Recognize text in a screenshot with the in-process OCR engine.</summary>
    Task<OcrResult> RecognizeAsync(BitmapSource image, CancellationToken cancellationToken = default);
}
