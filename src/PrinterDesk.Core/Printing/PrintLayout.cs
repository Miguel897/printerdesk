namespace PrinterDesk.Core.Printing;

/// <summary>
/// Where a page goes on the sheet, in sheet pixels: <see cref="Rotation"/> (clockwise, 0/90/180/270)
/// is applied to the page image first, then it is scaled to <see cref="Width"/> × <see cref="Height"/>
/// and drawn at (<see cref="X"/>, <see cref="Y"/>).
/// </summary>
public readonly record struct PagePlacement(int Rotation, int X, int Y, int Width, int Height);

/// <summary>Sheet size and printable area in pixels at the print resolution.</summary>
public sealed record SheetLayout(int Width, int Height, int Dpi, int MarginLeft, int MarginTop, int MarginRight, int MarginBottom)
{
    public static SheetLayout For(MediaSize media, int dpi, Ipp.Margins margins)
    {
        int Px(double mm) => (int)Math.Round(mm / 25.4 * dpi);
        return new SheetLayout(
            Px(media.WidthMm), Px(media.HeightMm), dpi,
            Px(margins.Left / 100.0), Px(margins.Top / 100.0), Px(margins.Right / 100.0), Px(margins.Bottom / 100.0));
    }

    public int PrintableWidth => Width - MarginLeft - MarginRight;

    public int PrintableHeight => Height - MarginTop - MarginBottom;
}

public static class PrintLayout
{
    /// <summary>
    /// The page indexes to print, one entry per printed side, with copies collated (1,2,3,1,2,3).
    /// For two-sided printing, a copy with an odd number of pages gets a blank (null) back so the
    /// next copy starts on a new sheet.
    /// </summary>
    public static IReadOnlyList<int?> Sides(IReadOnlyList<int> pages, int copies, bool twoSided)
    {
        var sides = new List<int?>();
        for (var copy = 0; copy < copies; copy++)
        {
            sides.AddRange(pages.Select(p => (int?)p));
            if (twoSided && pages.Count % 2 == 1 && copy < copies - 1)
                sides.Add(null);
        }
        return sides;
    }

    /// <summary>
    /// Whether the printer needs this side's image turned 180°. With <c>pwg-raster-document-sheet-back
    /// = rotated</c> the back of a long-edge sheet is fed upside down (verified on the HP ENVY 6432e);
    /// short-edge backs need no change. Other sheet-back modes are treated as normal.
    /// </summary>
    public static bool IsBackSideRotated(int sideIndex, PrintSides sides, string sheetBack) =>
        sideIndex % 2 == 1 && sides == PrintSides.TwoSidedLongEdge && sheetBack == "rotated";

    /// <summary>
    /// Places a page image (<paramref name="pixelWidth"/> × <paramref name="pixelHeight"/> at
    /// <paramref name="pageDpi"/>, turned by the user's <paramref name="pageRotation"/>) on the sheet:
    /// landscape pages are turned to fit a portrait sheet (Auto), pages keep their real size but shrink
    /// to fit the printable area, centered.
    /// </summary>
    public static PagePlacement Place(int pixelWidth, int pixelHeight, int pageDpi, int pageRotation,
        PrintOrientation orientation, SheetLayout sheet, bool rotateBackSide = false)
    {
        var turned = pageRotation % 180 != 0;
        double width = turned ? pixelHeight : pixelWidth;
        double height = turned ? pixelWidth : pixelHeight;

        var sheetIsPortrait = sheet.PrintableWidth <= sheet.PrintableHeight;
        var turnToFit = orientation switch
        {
            PrintOrientation.Landscape => sheetIsPortrait,
            PrintOrientation.Auto => width > height == sheetIsPortrait && width != height,
            _ => false,
        };
        // 90° counterclockwise: the top of a landscape page ends up on the left edge of the sheet.
        var rotation = (pageRotation + (turnToFit ? 270 : 0)) % 360;
        if (turnToFit)
            (width, height) = (height, width);

        var scale = sheet.Dpi / (double)pageDpi;
        scale = Math.Min(scale, Math.Min(sheet.PrintableWidth / width, sheet.PrintableHeight / height));
        var w = Math.Max(1, (int)Math.Round(width * scale));
        var h = Math.Max(1, (int)Math.Round(height * scale));
        var x = sheet.MarginLeft + (sheet.PrintableWidth - w) / 2;
        var y = sheet.MarginTop + (sheet.PrintableHeight - h) / 2;

        if (rotateBackSide)
        {
            rotation = (rotation + 180) % 360;
            x = sheet.Width - x - w;
            y = sheet.Height - y - h;
        }
        return new PagePlacement(rotation, x, y, w, h);
    }
}
