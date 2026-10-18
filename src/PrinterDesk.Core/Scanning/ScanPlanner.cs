using PrinterDesk.Core.Escl;

namespace PrinterDesk.Core.Scanning;

/// <summary>Turns user options into a scan request the device actually supports.</summary>
public static class ScanPlanner
{
    // Paper sizes in 1/300 inch.
    private static readonly ScanRegion A4 = new(2480, 3508);
    private static readonly ScanRegion Letter = new(2550, 3300);

    public static ScanRequest Plan(ScanOptions options, ScannerCapabilities caps, ScannerStatus status)
    {
        var source = ResolveSource(options.Source, caps, status);
        var input = (source == EsclSource.Feeder ? caps.Adf : caps.Platen)
            ?? throw new EsclException(source == EsclSource.Feeder ? "This scanner has no document feeder." : "This scanner has no flatbed.");

        return new ScanRequest(
            Source: source,
            Dpi: input.ClosestResolution(options.Dpi),
            ColorMode: ResolveColorMode(options.Color, input),
            Region: source == EsclSource.Platen ? PaperRegion(options.Paper, input) : null);
    }

    /// <summary>Auto picks the feeder when it has paper, otherwise the flatbed.</summary>
    public static EsclSource ResolveSource(ScanSource source, ScannerCapabilities caps, ScannerStatus status) => source switch
    {
        ScanSource.Feeder => EsclSource.Feeder,
        ScanSource.Flatbed => EsclSource.Platen,
        _ => caps.Adf is not null && status.IsAdfLoaded ? EsclSource.Feeder : EsclSource.Platen,
    };

    /// <summary>Resolutions offered for a source; Auto offers those both sources support.</summary>
    public static IReadOnlyList<int> ResolutionsFor(ScanSource source, ScannerCapabilities caps)
    {
        var platen = caps.Platen?.Resolutions ?? [];
        var adf = caps.Adf?.Resolutions ?? [];
        return source switch
        {
            ScanSource.Feeder => adf,
            ScanSource.Flatbed => platen,
            _ when adf.Count == 0 => platen,
            _ when platen.Count == 0 => adf,
            _ => platen.Intersect(adf).ToList(),
        };
    }

    private static string ResolveColorMode(ColorMode color, InputCaps input)
    {
        var wanted = color switch
        {
            ColorMode.Grayscale => "Grayscale8",
            ColorMode.BlackAndWhite => "BlackAndWhite1",
            _ => "RGB24",
        };
        return input.ColorModes.Count == 0 || input.ColorModes.Contains(wanted) ? wanted : input.ColorModes[^1];
    }

    private static ScanRegion PaperRegion(PaperSize paper, InputCaps input)
    {
        var region = paper switch
        {
            PaperSize.A4 => A4,
            PaperSize.Letter => Letter,
            _ => new ScanRegion(input.MaxWidth, input.MaxHeight),
        };
        return new ScanRegion(Math.Min(region.Width, input.MaxWidth), Math.Min(region.Height, input.MaxHeight));
    }
}
