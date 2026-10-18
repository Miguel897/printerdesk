using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrinterDesk.Core.Documents;

namespace PrinterDesk.App.Services;

/// <summary>Saves pages as JPEG files: <c>name.jpg</c> for one page, <c>name_01.jpg</c>, <c>name_02.jpg</c>… for more.</summary>
public static class JpegExporter
{
    /// <summary>Resolution for PDF pages, which have no pixels of their own.</summary>
    private const int PdfDpi = 200;

    public static IReadOnlyList<string> Export(IReadOnlyList<DocumentPage> pages, string path, PdfSources pdfs)
    {
        var folder = Path.GetDirectoryName(path)!;
        var baseName = Path.GetFileNameWithoutExtension(path);
        var files = new List<string>();

        for (var i = 0; i < pages.Count; i++)
        {
            var target = pages.Count == 1 ? path : Path.Combine(folder, $"{baseName}_{i + 1:D2}.jpg");
            Write(pages[i], target, pdfs);
            files.Add(target);
        }

        return files;
    }

    private static void Write(DocumentPage page, string target, PdfSources pdfs)
    {
        BitmapSource image;
        if (page.PdfPageIndex is { } index)
        {
            var bitmap = pdfs.Get(page.FilePath).RenderAsync(index, page.Rotation / 90, PdfDpi / 72.0).GetAwaiter().GetResult();
            image = BitmapSource.Create(bitmap.Width, bitmap.Height, PdfDpi, PdfDpi, PixelFormats.Bgra32, null, bitmap.Pixels, bitmap.Stride);
        }
        else
        {
            var isJpeg = Path.GetExtension(page.FilePath).ToLowerInvariant() is ".jpg" or ".jpeg";
            if (page.Rotation == 0 && isJpeg)
            {
                // Unrotated JPEGs are copied byte for byte: no quality loss.
                File.Copy(page.FilePath, target, overwrite: true);
                return;
            }

            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.UriSource = new Uri(page.FilePath);
            source.EndInit();
            image = OnWhite(page.Rotation == 0 ? source : new TransformedBitmap(source, new RotateTransform(page.Rotation)));
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(target);
        encoder.Save(stream);
    }

    /// <summary>JPEG has no transparency: transparent PNG areas would turn black without a white background.</summary>
    private static BitmapSource OnWhite(BitmapSource image)
    {
        if (image.Format != PixelFormats.Bgra32 && image.Format != PixelFormats.Pbgra32)
            return image;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, image.PixelWidth, image.PixelHeight);
            dc.DrawRectangle(Brushes.White, null, rect);
            dc.DrawImage(image, rect);
        }
        var flat = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        flat.Render(visual);
        return flat;
    }
}
