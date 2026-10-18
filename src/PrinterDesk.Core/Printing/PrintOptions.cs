namespace PrinterDesk.Core.Printing;

public enum PrintColor { Color, BlackAndWhite }

public enum PrintSides { OneSided, TwoSidedLongEdge, TwoSidedShortEdge }

public enum PrintOrientation { Auto, Portrait, Landscape }

public enum PrintQuality { Draft, Normal, Best }

/// <summary>User-selectable print settings. Defaults follow docs/PRD-print.md (§5.3, Q6).</summary>
public sealed record PrintOptions
{
    public const int MaxCopies = 99;

    public int Copies { get; init; } = 1;

    /// <summary>Pages to print, e.g. "1-3,5"; empty prints all pages.</summary>
    public string PageRange { get; init; } = "";

    public PrintColor Color { get; init; } = PrintColor.BlackAndWhite;

    public PrintSides Sides { get; init; } = PrintSides.OneSided;

    public PrintOrientation Orientation { get; init; } = PrintOrientation.Auto;

    /// <summary>PWG media keyword, e.g. iso_a4_210x297mm; null uses the paper loaded in the printer.</summary>
    public string? Media { get; init; }

    public PrintQuality Quality { get; init; } = PrintQuality.Normal;

    public bool IsTwoSided => Sides != PrintSides.OneSided;

    public string SidesKeyword => Sides switch
    {
        PrintSides.TwoSidedLongEdge => "two-sided-long-edge",
        PrintSides.TwoSidedShortEdge => "two-sided-short-edge",
        _ => "one-sided",
    };

    public string ColorKeyword => Color == PrintColor.Color ? "color" : "monochrome";

    /// <summary>IPP print-quality: 3 draft, 4 normal, 5 high.</summary>
    public int QualityValue => Quality switch
    {
        PrintQuality.Draft => 3,
        PrintQuality.Best => 5,
        _ => 4,
    };
}
