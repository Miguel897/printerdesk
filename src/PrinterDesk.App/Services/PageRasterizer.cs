using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrinterDesk.Core.Documents;
using PrinterDesk.Core.Printing;

namespace PrinterDesk.App.Services;

/// <summary>Draws scanned JPEGs into print sheets with WPF imaging (decode at target size, rotate, convert).</summary>
public sealed class PageRasterizer : IPageRasterizer
{
    public (int Width, int Height) GetPixelSize(ScanPage page)
    {
        using var stream = File.OpenRead(page.FilePath);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    public void Render(ScanPage page, PagePlacement placement, Span<byte> sheet, int sheetWidth, bool color)
    {
        // Decoding straight to the printed size keeps memory low; rotation comes after, so a turned
        // page is decoded with width and height swapped.
        var turned = placement.Rotation % 180 != 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(page.FilePath);
        image.DecodePixelWidth = turned ? placement.Height : placement.Width;
        image.DecodePixelHeight = turned ? placement.Width : placement.Height;
        image.EndInit();

        BitmapSource source = image;
        if (placement.Rotation != 0)
            source = new TransformedBitmap(source, new RotateTransform(placement.Rotation));
        source = new FormatConvertedBitmap(source, color ? PixelFormats.Rgb24 : PixelFormats.Gray8, null, 0);

        var bpp = color ? 3 : 1;
        var stride = source.PixelWidth * bpp;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);

        var sheetHeight = sheet.Length / (sheetWidth * bpp);
        var width = Math.Min(source.PixelWidth, sheetWidth - placement.X);
        var height = Math.Min(source.PixelHeight, sheetHeight - placement.Y);
        for (var y = 0; y < height; y++)
        {
            var offset = ((placement.Y + y) * sheetWidth + placement.X) * bpp;
            pixels.AsSpan(y * stride, width * bpp).CopyTo(sheet[offset..]);
        }
    }
}
