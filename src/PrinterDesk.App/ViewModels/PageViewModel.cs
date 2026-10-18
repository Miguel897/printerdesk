using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PrinterDesk.Core.Documents;

namespace PrinterDesk.App.ViewModels;

public sealed partial class PageViewModel : ObservableObject
{
    public PageViewModel(ScanPage page)
    {
        Page = page;
        Thumbnail = LoadThumbnail(page.FilePath);
    }

    public ScanPage Page { get; }

    public BitmapSource Thumbnail { get; }

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
        image.DecodePixelWidth = 150;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
