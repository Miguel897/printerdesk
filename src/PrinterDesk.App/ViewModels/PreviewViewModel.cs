using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PrinterDesk.App.Services;
using PrinterDesk.Core;
using PrinterDesk.Core.Documents;
using PrinterDesk.Core.Ipp;
using PrinterDesk.Core.Pdfium;
using PrinterDesk.Core.Printing;

namespace PrinterDesk.App.ViewModels;

/// <summary>
/// The preview pane: the selected page on its own (Scan tab), or as it will print (Print tab): on the
/// chosen paper, with the printer's margins, Auto orientation and gray for black &amp; white. Pages are
/// rendered with the same code as print jobs, at screen resolution, off the UI thread.
/// </summary>
public sealed partial class PreviewViewModel : ObservableObject, IDisposable
{
    private const int PreviewDpi = 150;
    private const int MaxPixels = 2400;

    private readonly PdfSources _pdfs;
    private readonly PrintViewModel _print;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private CancellationTokenSource? _renderCts;
    private PageViewModel? _page;
    private int _number;
    private int _count;

    public PreviewViewModel(PdfSources pdfs, PrintViewModel print)
    {
        _pdfs = pdfs;
        _print = print;
        Caption = "";
        _print.OptionsChanged += (_, _) =>
        {
            if (IsPrintPreview)
                Schedule();
        };
        _timer.Tick += async (_, _) =>
        {
            _timer.Stop();
            await RenderAsync();
        };
    }

    /// <summary>Null when there is nothing to show.</summary>
    [ObservableProperty]
    public partial BitmapSource? Image { get; private set; }

    [ObservableProperty]
    public partial string Caption { get; private set; }

    /// <summary>True while the Print tab is open.</summary>
    [ObservableProperty]
    public partial bool IsPrintPreview { get; set; }

    /// <summary>Fit the page width and scroll (to check details), instead of showing the whole page.</summary>
    [ObservableProperty]
    public partial bool FitWidth { get; set; }

    partial void OnIsPrintPreviewChanged(bool value) => Schedule();

    /// <summary>Shows a page (or nothing). Repeated calls within a short time render only once.</summary>
    public void Show(PageViewModel? page, int number, int count)
    {
        (_page, _number, _count) = (page, number, count);
        Schedule();
    }

    private void Schedule()
    {
        _timer.Stop();
        _timer.Start();
    }

    private async Task RenderAsync()
    {
        _renderCts?.Cancel();
        var cts = _renderCts = new CancellationTokenSource();
        if (_page is null)
        {
            Image = null;
            Caption = "";
            return;
        }

        var page = _page.Page;
        var printMode = IsPrintPreview;
        var options = _print.PreviewOptions;
        var printer = _print.Capabilities;
        try
        {
            var image = await Task.Run(() => Render(page, printMode, options, printer, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
                return;
            Image = image;
            Caption = printMode ? $"Page {_number} of {_count} · as printed: {Describe(options, printer)}" : $"Page {_number} of {_count}";
        }
        catch (OperationCanceledException)
        {
            // A newer page or option replaced this render.
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FormatException or InvalidOperationException
                                       or PdfOpenException or ObjectDisposedException)
        {
            Log.Error("Preview failed", ex);
            Image = null;
            Caption = "This page cannot be shown.";
        }
    }

    private BitmapSource Render(DocumentPage page, bool printMode, PrintOptions options, PrinterCapabilities? printer, CancellationToken ct)
    {
        var rasterizer = new PageRasterizer(_pdfs);
        var (pixelWidth, pixelHeight) = rasterizer.GetPixelSize(page);

        int width, height;
        PagePlacement placement;
        var color = !printMode || options.Color == PrintColor.Color;
        if (printMode)
        {
            var sheet = PrintPreview.Sheet(options, printer, PreviewDpi);
            placement = PrintPreview.PlaceOnSheet(pixelWidth, pixelHeight, page.Dpi, page.Rotation, options, sheet);
            (width, height) = (sheet.Width, sheet.Height);
        }
        else
        {
            placement = PrintPreview.PageOnly(pixelWidth, pixelHeight, page.Dpi, page.Rotation, PreviewDpi, MaxPixels);
            (width, height) = (placement.Width, placement.Height);
        }
        ct.ThrowIfCancellationRequested();

        var bpp = color ? 3 : 1;
        var pixels = new byte[width * height * bpp];
        pixels.AsSpan().Fill(0xFF);
        rasterizer.Render(page, placement, pixels, width, color);

        var image = BitmapSource.Create(width, height, PreviewDpi, PreviewDpi, color ? PixelFormats.Rgb24 : PixelFormats.Gray8, null, pixels, width * bpp);
        image.Freeze();
        return image;
    }

    private string Describe(PrintOptions options, PrinterCapabilities? printer)
    {
        var text = $"{PrintPreview.ResolveMedia(options, printer).Name}, {(options.Color == PrintColor.Color ? "color" : "black & white")}";
        return printer is null ? text + " (no printer connected)" : text;
    }

    public void Dispose()
    {
        _timer.Stop();
        _renderCts?.Cancel();
    }
}
