using PrinterDesk.Core.Documents;
using PrinterDesk.Core.Ipp;

namespace PrinterDesk.Core.Printing;

/// <summary>Draws page images into sheet pixel buffers. Implemented with WPF imaging in the app.</summary>
public interface IPageRasterizer
{
    (int Width, int Height) GetPixelSize(DocumentPage page);

    /// <summary>
    /// Draws <paramref name="page"/> at <paramref name="placement"/> into <paramref name="sheet"/>, which is
    /// white, <paramref name="sheetWidth"/> pixels wide, 3 bytes (RGB) or 1 byte (gray) per pixel.
    /// </summary>
    void Render(DocumentPage page, PagePlacement placement, Span<byte> sheet, int sheetWidth, bool color);
}

/// <summary>Turns the document pages and print options into a PWG raster job for one printer.</summary>
public sealed class PrintJobBuilder
{
    /// <summary>Scans are 75–300 dpi, so higher print resolutions would only make the job larger.</summary>
    private const int PreferredDpi = 300;

    private readonly IReadOnlyList<DocumentPage> _pages;
    private readonly IReadOnlyList<int?> _sides;
    private readonly PrintOptions _options;
    private readonly PrinterCapabilities _printer;
    private readonly bool _color;

    /// <param name="pageIndexes">0-based pages in print order (see <see cref="PageRange"/>).</param>
    public PrintJobBuilder(IReadOnlyList<DocumentPage> pages, IReadOnlyList<int> pageIndexes, PrintOptions options, PrinterCapabilities printer)
    {
        if (!printer.SupportsPwgRaster)
            throw new IppException("This printer does not accept PWG raster jobs, which PrinterDesk needs.");

        _pages = pages;
        _options = options;
        _printer = printer;
        _sides = PrintLayout.Sides(pageIndexes, Math.Clamp(options.Copies, 1, Math.Min(PrintOptions.MaxCopies, Math.Max(1, printer.MaxCopies))), options.IsTwoSided);
        _color = options.Color == PrintColor.Color && printer.RasterTypes.Contains("srgb_8");

        Media = PrintPreview.ResolveMedia(options, printer);
        var dpi = printer.RasterResolutions.Where(r => r <= PreferredDpi).DefaultIfEmpty(printer.RasterResolutions[0]).Max();
        Sheet = SheetLayout.For(Media, dpi, printer.Margins);
    }

    public MediaSize Media { get; }

    public SheetLayout Sheet { get; }

    /// <summary>Printed sides, including copies and blank backs.</summary>
    public int SideCount => _sides.Count;

    public int SheetCount => _options.IsTwoSided ? (SideCount + 1) / 2 : SideCount;

    public PrintTicket Ticket => new()
    {
        JobName = $"PrinterDesk – {SideCount} page{(SideCount == 1 ? "" : "s")}",
        Sides = _options.SidesKeyword,
        ColorMode = _color ? "color" : "monochrome",
        Media = Media.Keyword,
        Quality = _printer.Qualities.Contains(_options.QualityValue) ? _options.QualityValue : null,
    };

    /// <summary>Writes the whole job. Renders one sheet side at a time to keep memory flat.</summary>
    /// <param name="progress">Number of sides written so far.</param>
    public void Write(Stream output, IPageRasterizer rasterizer, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var writer = new PwgRasterWriter(output);
        var bpp = _color ? 3 : 1;
        var sheet = new byte[Sheet.Width * Sheet.Height * bpp];
        for (var side = 0; side < _sides.Count; side++)
        {
            ct.ThrowIfCancellationRequested();
            sheet.AsSpan().Fill(0xFF);
            if (_sides[side] is { } index)
            {
                var page = _pages[index];
                var (width, height) = rasterizer.GetPixelSize(page);
                var placement = PrintLayout.Place(width, height, page.Dpi, page.Rotation, _options.Orientation, Sheet,
                    PrintLayout.IsBackSideRotated(side, _options.Sides, _printer.SheetBack));
                rasterizer.Render(page, placement, sheet, Sheet.Width, _color);
            }

            writer.WritePage(new PwgPage
            {
                Width = Sheet.Width,
                Height = Sheet.Height,
                Dpi = Sheet.Dpi,
                Color = _color,
                Duplex = _options.IsTwoSided,
                Tumble = _options.Sides == PrintSides.TwoSidedShortEdge,
                TotalPageCount = _sides.Count,
                Quality = _options.QualityValue,
                MediaKeyword = Media.Keyword,
            }, sheet);
            progress?.Report(side + 1);
        }
    }
}
