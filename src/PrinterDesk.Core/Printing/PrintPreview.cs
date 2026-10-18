using PrinterDesk.Core.Ipp;

namespace PrinterDesk.Core.Printing;

/// <summary>Layout for the on-screen preview: the same placement rules as printing, at screen resolution.</summary>
public static class PrintPreview
{
    public const string DefaultMedia = "iso_a4_210x297mm";

    /// <summary>
    /// The paper a job prints on: the chosen size if the printer offers it, else the paper loaded in the
    /// printer, else A4. Without a printer (preview only) the chosen size is used as is.
    /// </summary>
    public static MediaSize ResolveMedia(PrintOptions options, PrinterCapabilities? printer)
    {
        var keyword = options.Media is { } chosen && (printer is null || printer.Media.Contains(chosen))
            ? chosen
            : printer?.MediaReady ?? DefaultMedia;
        return MediaSize.TryParse(keyword) ?? MediaSize.TryParse(DefaultMedia)!;
    }

    /// <summary>The sheet at <paramref name="dpi"/>, with the printer's margins (none without a printer).</summary>
    public static SheetLayout Sheet(PrintOptions options, PrinterCapabilities? printer, int dpi) =>
        SheetLayout.For(ResolveMedia(options, printer), dpi, printer?.Margins ?? default);

    /// <summary>Where a page goes on the preview sheet (front-side view: no back-side turn).</summary>
    public static PagePlacement PlaceOnSheet(int pixelWidth, int pixelHeight, int pageDpi, int pageRotation, PrintOptions options, SheetLayout sheet) =>
        PrintLayout.Place(pixelWidth, pixelHeight, pageDpi, pageRotation, options.Orientation, sheet);

    /// <summary>
    /// A page shown on its own (the scan preview): turned by the user's rotation and scaled to
    /// <paramref name="previewDpi"/>, with the long side at most <paramref name="maxPixels"/>.
    /// </summary>
    public static PagePlacement PageOnly(int pixelWidth, int pixelHeight, int pageDpi, int rotation, int previewDpi, int maxPixels)
    {
        var turned = rotation % 180 != 0;
        double width = turned ? pixelHeight : pixelWidth;
        double height = turned ? pixelWidth : pixelHeight;
        var scale = Math.Min(previewDpi / (double)pageDpi, maxPixels / Math.Max(width, height));
        return new PagePlacement(rotation, 0, 0, Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }
}
