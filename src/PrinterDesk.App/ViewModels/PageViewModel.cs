using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PrinterDesk.Core;
using PrinterDesk.Core.Documents;
using PrinterDesk.Core.Pdfium;

namespace PrinterDesk.App.ViewModels;

public sealed partial class PageViewModel : ObservableObject
{
    private const int ThumbnailWidth = 150;

    public PageViewModel(DocumentPage page, PdfSources pdfs)
    {
        Page = page;
        if (page.PdfPageIndex is { } index)
            _ = LoadPdfThumbnailAsync(pdfs, index);
        else
            Thumbnail = LoadThumbnail(page.FilePath);
    }

    public DocumentPage Page { get; }

    /// <summary>Null while a PDF page is still being rendered.</summary>
    [ObservableProperty]
    public partial BitmapSource? Thumbnail { get; private set; }

    public int Rotation => Page.Rotation;

    [ObservableProperty]
    public partial int Number { get; set; }

    /// <summary>Accessible name of the list item (screen readers, UI Automation).</summary>
    public override string ToString() => Rotation == 0 ? $"Page {Number}" : $"Page {Number}, rotated {Rotation}°";

    public void Rotate(int degrees)
    {
        Page.Rotate(degrees);
        OnPropertyChanged(nameof(Rotation));
    }

    private static BitmapSource LoadThumbnail(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad; // do not keep the file locked
        image.DecodePixelWidth = ThumbnailWidth;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private async Task LoadPdfThumbnailAsync(PdfSources pdfs, int index)
    {
        try
        {
            var pdf = await pdfs.GetAsync(Page.FilePath);
            var bitmap = await pdf.RenderAsync(index, 0, ThumbnailWidth / pdf.PageSizes[index].Width);
            var image = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32, null, bitmap.Pixels, bitmap.Stride);
            image.Freeze();
            Thumbnail = image;
        }
        catch (Exception ex) when (ex is PdfOpenException or InvalidOperationException or ObjectDisposedException or System.IO.IOException)
        {
            Log.Error($"Thumbnail of {Page.FilePath} page {index + 1}", ex);
        }
    }
}
