using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrinterDesk.Core.Documents;
using PrinterDesk.Core.Printing;

namespace PrinterDesk.App.Services;

/// <summary>
/// Draws document pages into print sheets: images with WPF imaging (decoded at the printed size),
/// PDF pages with PDFium in print mode. Transparent areas print as white.
/// </summary>
public sealed class PageRasterizer(PdfSources pdfs) : IPageRasterizer
{
    public (int Width, int Height) GetPixelSize(DocumentPage page)
    {
        if (page.PdfPageIndex is { } index)
        {
            var (width, height) = pdfs.Get(page.FilePath).PageSizes[index];
            return ((int)Math.Round(width), (int)Math.Round(height));
        }

        using var stream = File.OpenRead(page.FilePath);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    public void Render(DocumentPage page, PagePlacement placement, Span<byte> sheet, int sheetWidth, bool color)
    {
        var source = page.PdfPageIndex is { } index ? RenderPdf(page, index, placement) : DecodeImage(page, placement);
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);

        var bpp = color ? 3 : 1;
        var sheetHeight = sheet.Length / (sheetWidth * bpp);
        var width = Math.Min(bgra.PixelWidth, sheetWidth - placement.X);
        var height = Math.Min(bgra.PixelHeight, sheetHeight - placement.Y);
        for (var y = 0; y < height; y++)
        {
            var row = pixels.AsSpan(y * stride, width * 4);
            var offset = ((placement.Y + y) * sheetWidth + placement.X) * bpp;
            var target = sheet[offset..];
            for (var x = 0; x < width; x++)
            {
                // Composite over white paper: c' = c·a + 255·(1 − a).
                int a = row[x * 4 + 3], white = 255 * (255 - a);
                var b = (row[x * 4] * a + white) / 255;
                var g = (row[x * 4 + 1] * a + white) / 255;
                var r = (row[x * 4 + 2] * a + white) / 255;
                if (color)
                {
                    target[x * 3] = (byte)r;
                    target[x * 3 + 1] = (byte)g;
                    target[x * 3 + 2] = (byte)b;
                }
                else
                {
                    target[x] = (byte)((r * 299 + g * 587 + b * 114) / 1000);
                }
            }
        }
    }

    private BitmapSource RenderPdf(DocumentPage page, int index, PagePlacement placement)
    {
        var bitmap = pdfs.Get(page.FilePath)
            .RenderAsync(index, placement.Rotation / 90, placement.Width, placement.Height, forPrinting: true)
            .GetAwaiter().GetResult();
        return BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32, null, bitmap.Pixels, bitmap.Stride);
    }

    private static BitmapSource DecodeImage(DocumentPage page, PagePlacement placement)
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
        return placement.Rotation == 0 ? image : new TransformedBitmap(image, new RotateTransform(placement.Rotation));
    }
}
