using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SnapGo.App.Services;

public class ClipboardService : IClipboardService
{
    public void CopyImage(BitmapSource image)
    {
        // Must run on STA thread for Clipboard access
        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var ms = new MemoryStream();
                encoder.Save(ms);

                // Set PNG format first for best quality
                Clipboard.SetData("PNG", ms);

                // Also set DIB/bitmap for compatibility with older apps
                Clipboard.SetImage(image);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // Clipboard is busy — retry once after a brief delay
                Thread.Sleep(50);
                try
                {
                    Clipboard.SetImage(image);
                }
                catch
                {
                    // Last resort: silently fail
                }
            }
        });
    }

    public void CopyText(string text)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                Clipboard.SetText(text, System.Windows.TextDataFormat.UnicodeText);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Thread.Sleep(50);
                try
                {
                    Clipboard.SetText(text);
                }
                catch { }
            }
        });
    }
}
