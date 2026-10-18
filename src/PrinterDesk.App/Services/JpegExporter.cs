using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrinterDesk.Core.Documents;

namespace PrinterDesk.App.Services;

/// <summary>Saves pages as JPEG files: <c>name.jpg</c> for one page, <c>name_01.jpg</c>, <c>name_02.jpg</c>… for more.</summary>
public static class JpegExporter
{
    public static IReadOnlyList<string> Export(IReadOnlyList<ScanPage> pages, string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        var baseName = Path.GetFileNameWithoutExtension(path);
        var files = new List<string>();

        for (var i = 0; i < pages.Count; i++)
        {
            var target = pages.Count == 1 ? path : Path.Combine(folder, $"{baseName}_{i + 1:D2}.jpg");
            Write(pages[i], target);
            files.Add(target);
        }

        return files;
    }

    private static void Write(ScanPage page, string target)
    {
        if (page.Rotation == 0)
        {
            // Unrotated pages are copied byte for byte: no quality loss.
            File.Copy(page.FilePath, target, overwrite: true);
            return;
        }

        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.UriSource = new Uri(page.FilePath);
        source.EndInit();

        var rotated = new TransformedBitmap(source, new RotateTransform(page.Rotation));
        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(rotated));
        using var stream = File.Create(target);
        encoder.Save(stream);
    }
}
