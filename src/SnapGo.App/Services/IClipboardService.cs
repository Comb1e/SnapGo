using System.Windows.Media.Imaging;

namespace SnapGo.App.Services;

public interface IClipboardService
{
    /// <summary>Copy a PNG image to the system clipboard.</summary>
    void CopyImage(BitmapSource image);

    /// <summary>Copy text to the system clipboard.</summary>
    void CopyText(string text);
}
