using System.Xml.Linq;
using static PrinterDesk.Core.Escl.EsclNamespaces;

namespace PrinterDesk.Core.Escl;

/// <summary>Capabilities of one input source (flatbed or feeder). Sizes are in 1/300 inch.</summary>
public sealed record InputCaps(
    int MaxWidth,
    int MaxHeight,
    IReadOnlyList<int> Resolutions,
    IReadOnlyList<string> ColorModes,
    IReadOnlyList<string> DocumentFormats)
{
    /// <summary>Highest supported resolution not above <paramref name="dpi"/>, or the lowest one.</summary>
    public int ClosestResolution(int dpi) =>
        Resolutions.Where(r => r <= dpi).DefaultIfEmpty(Resolutions.Min()).Max();
}

public sealed record ScannerCapabilities(string MakeAndModel, InputCaps? Platen, InputCaps? Adf, int? FeederCapacity)
{
    private static readonly int[] FallbackResolutions = [75, 150, 300];

    public static ScannerCapabilities Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root ?? throw new FormatException("Empty ScannerCapabilities document.");

        return new ScannerCapabilities(
            MakeAndModel: root.Element(Pwg + "MakeAndModel")?.Value.Trim() ?? "eSCL scanner",
            Platen: ParseInput(root.Element(Scan + "Platen")?.Element(Scan + "PlatenInputCaps")),
            Adf: ParseInput(root.Element(Scan + "Adf")?.Element(Scan + "AdfSimplexInputCaps")),
            FeederCapacity: (int?)root.Element(Scan + "Adf")?.Element(Scan + "FeederCapacity"));
    }

    private static InputCaps? ParseInput(XElement? caps)
    {
        if (caps is null)
            return null;

        var resolutions = caps.Descendants(Scan + "DiscreteResolution")
            .Select(r => (int)r.Element(Scan + "XResolution")!)
            .Distinct()
            .Order()
            .ToList();

        return new InputCaps(
            MaxWidth: (int?)caps.Element(Scan + "MaxWidth") ?? 2550,
            MaxHeight: (int?)caps.Element(Scan + "MaxHeight") ?? 3508,
            Resolutions: resolutions.Count > 0 ? resolutions : FallbackResolutions,
            ColorModes: caps.Descendants(Scan + "ColorMode").Select(e => e.Value).Distinct().ToList(),
            DocumentFormats: caps.Descendants(Pwg + "DocumentFormat").Select(e => e.Value).Distinct().ToList());
    }
}
