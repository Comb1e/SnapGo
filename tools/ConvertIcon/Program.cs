using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

var pngPath = args.Length > 0 ? args[0] : "src/SnapGo.App/icon/SnapGo.png";
var icoPath = args.Length > 1 ? args[1] : "src/SnapGo.App/icon/SnapGo.ico";

Console.WriteLine($"Converting {pngPath} → {icoPath}");

using var bitmap = new Bitmap(pngPath);
Console.WriteLine($"Source: {bitmap.Width}×{bitmap.Height}, PixelFormat={bitmap.PixelFormat}");

var sizes = new[] { 256, 64, 48, 32, 16 };
var dibBlocks = new List<byte[]>();

// Resize to each icon size and build proper DIB with AND mask
foreach (var size in sizes)
{
    using var resized = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(resized);
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.DrawImage(bitmap, 0, 0, size, size);

    dibBlocks.Add(BuildDibWithAndMask(resized));
    Console.WriteLine($"  {size}×{size} → {dibBlocks[^1].Length} bytes (DIB + AND mask)");
}

// Write ICO file
using var fs = new FileStream(icoPath, FileMode.Create);
using var writer = new BinaryWriter(fs);

// ICONDIR header
writer.Write((short)0);   // idReserved
writer.Write((short)1);   // idType = ICO
writer.Write((short)sizes.Length); // idCount

// ICONDIRENTRY for each image
int offset = 6 + 16 * sizes.Length;

for (int i = 0; i < sizes.Length; i++)
{
    int s = sizes[i];
    int dataSize = dibBlocks[i].Length;

    // Width/height are 0 for 256px (0 is shorthand for 256)
    writer.Write((byte)(s == 256 ? 0 : s));
    writer.Write((byte)(s == 256 ? 0 : s));
    writer.Write((byte)0);        // bColorCount = 0
    writer.Write((byte)0);        // bReserved
    writer.Write((short)1);       // wPlanes
    writer.Write((short)32);      // wBitCount
    writer.Write(dataSize);       // dwBytesInRes
    writer.Write(offset);         // dwImageOffset

    offset += dataSize;
}

// Write DIB blocks
foreach (var block in dibBlocks)
    writer.Write(block);

Console.WriteLine($"Created {icoPath} ({new FileInfo(icoPath).Length / 1024}KB) with {sizes.Length} sizes (DIB)");

/// Build a DIB with AND mask for ICO format.
/// biHeight = 2 × image_height (top half: XOR BGRA, bottom half: AND 1bpp mask).
static byte[] BuildDibWithAndMask(Bitmap bmp)
{
    int w = bmp.Width;
    int h = bmp.Height;
    int xorStride = w * 4;                              // 32bpp BGRA
    int andStride = ((w + 31) / 32) * 4;               // 1bpp, DWORD-aligned

    int dibSize = 40 + xorStride * h + andStride * h;  // header + XOR + AND
    var bytes = new byte[dibSize];

    // BITMAPINFOHEADER
    WriteInt32LE(bytes, 0, 40);        // biSize
    WriteInt32LE(bytes, 4, w);         // biWidth
    WriteInt32LE(bytes, 8, h * 2);     // biHeight = 2x (XOR + AND)
    WriteInt16LE(bytes, 12, 1);        // biPlanes
    WriteInt16LE(bytes, 14, 32);       // biBitCount
    WriteInt32LE(bytes, 16, 0);        // biCompression = BI_RGB
    WriteInt32LE(bytes, 20, 0);        // biSizeImage (0 is valid for BI_RGB)
    WriteInt32LE(bytes, 24, 0);        // biXPelsPerMeter
    WriteInt32LE(bytes, 28, 0);        // biYPelsPerMeter
    WriteInt32LE(bytes, 32, 0);        // biClrUsed
    WriteInt32LE(bytes, 36, 0);        // biClrImportant

    // XOR mask: copy BGRA pixels (top-down: row 0 first)
    var bmpData = bmp.LockBits(
        new Rectangle(0, 0, w, h),
        ImageLockMode.ReadOnly,
        PixelFormat.Format32bppArgb);
    try
    {
        // Bitmap stores bottom-up; we write top-down by reading rows in reverse
        int srcStride = Math.Abs(bmpData.Stride);
        for (int y = 0; y < h; y++)
        {
            int srcRow = (h - 1 - y); // reverse: bottom-up → top-down
            int dstOff = 40 + y * xorStride;
            Marshal.Copy(
                bmpData.Scan0 + srcRow * srcStride,
                bytes,
                dstOff,
                xorStride);
        }
    }
    finally
    {
        bmp.UnlockBits(bmpData);
    }

    // AND mask: all zeros at offset 40 + xorStride * h
    // Already zero-initialized, no need to write

    return bytes;
}

static void WriteInt32LE(byte[] buf, int off, int val)
{
    buf[off] = (byte)val;
    buf[off + 1] = (byte)(val >> 8);
    buf[off + 2] = (byte)(val >> 16);
    buf[off + 3] = (byte)(val >> 24);
}

static void WriteInt16LE(byte[] buf, int off, int val)
{
    buf[off] = (byte)val;
    buf[off + 1] = (byte)(val >> 8);
}
