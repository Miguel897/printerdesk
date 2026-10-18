namespace PrinterDesk.Core.Scanning;

public enum ScanSource { Auto, Feeder, Flatbed }

public enum ColorMode { Color, Grayscale, BlackAndWhite }

public enum PaperSize { A4, Letter, FullBed }

/// <summary>User-selectable scan settings. Defaults match the PRD (§5.2).</summary>
public sealed record ScanOptions
{
    public const int DefaultDpi = 150;

    public ScanSource Source { get; init; } = ScanSource.Auto;
    public int Dpi { get; init; } = DefaultDpi;
    public ColorMode Color { get; init; } = ColorMode.Color;
    public PaperSize Paper { get; init; } = PaperSize.A4;
}
