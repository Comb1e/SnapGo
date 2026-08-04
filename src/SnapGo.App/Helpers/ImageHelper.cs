using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace SnapGo.App.Helpers;

public static class ImageHelper
{
    /// <summary>Convert a System.Drawing.Bitmap to a WPF BitmapSource.</summary>
    public static BitmapSource ToBitmapSource(this Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, bitmap.PixelFormat);

        try
        {
            var source = BitmapSource.Create(
                bitmapData.Width,
                bitmapData.Height,
                bitmap.HorizontalResolution,
                bitmap.VerticalResolution,
                System.Windows.Media.PixelFormats.Bgr32,
                null,
                bitmapData.Scan0,
                bitmapData.Stride * bitmapData.Height,
                bitmapData.Stride);

            // Freeze to enable cross-thread access
            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    /// <summary>Encode a BitmapSource to PNG bytes.</summary>
    public static byte[] ToPngBytes(this BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Convert PNG bytes to a Base64 data URI.</summary>
    public static string ToBase64DataUri(byte[] pngBytes)
    {
        return $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}";
    }

    /// <summary>Create a thumbnail from a byte array.</summary>
    public static byte[] CreateThumbnail(byte[] imageBytes, int maxWidth, int maxHeight)
    {
        using var ms = new MemoryStream(imageBytes);
        var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
        var source = decoder.Frames[0];

        var scale = Math.Min((double)maxWidth / source.PixelWidth, (double)maxHeight / source.PixelHeight);
        if (scale >= 1.0) return imageBytes; // Already smaller than thumbnail size

        var thumb = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(thumb));
        using var outMs = new MemoryStream();
        encoder.Save(outMs);
        return outMs.ToArray();
    }
}
