namespace SnapGo.App.Helpers;

/// <summary>A rectangle in bitmap pixels.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>
/// Maps a selection made in device-independent pixels onto a bitmap captured in physical pixels.
/// </summary>
public static class PixelRectMapper
{
    /// <summary>
    /// Convert a DIP rectangle to pixels. Edges use floor and ceiling so a fractional DIP
    /// still covers every physical pixel it touches, then the result is clamped to the bitmap.
    /// </summary>
    public static PixelRect FromDip(
        double x, double y, double width, double height,
        double scaleX, double scaleY,
        int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), "Bitmap size must be positive.");
        if (scaleX <= 0 || double.IsNaN(scaleX) || double.IsInfinity(scaleX))
            throw new ArgumentOutOfRangeException(nameof(scaleX), "Scale must be positive.");
        if (scaleY <= 0 || double.IsNaN(scaleY) || double.IsInfinity(scaleY))
            throw new ArgumentOutOfRangeException(nameof(scaleY), "Scale must be positive.");
        if (width <= 0 || height <= 0 || double.IsNaN(width) || double.IsNaN(height))
            throw new ArgumentOutOfRangeException(nameof(width), "Selection size must be positive.");

        int left = (int)Math.Floor(x * scaleX);
        int top = (int)Math.Floor(y * scaleY);
        int right = (int)Math.Ceiling((x + width) * scaleX);
        int bottom = (int)Math.Ceiling((y + height) * scaleY);

        left = Math.Clamp(left, 0, pixelWidth - 1);
        top = Math.Clamp(top, 0, pixelHeight - 1);
        right = Math.Clamp(right, left + 1, pixelWidth);
        bottom = Math.Clamp(bottom, top + 1, pixelHeight);
        return new PixelRect(left, top, right - left, bottom - top);
    }
}
