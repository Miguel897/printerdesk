using System.Xml.Linq;
using static PrinterDesk.Core.Escl.EsclNamespaces;

namespace PrinterDesk.Core.Escl;

public enum EsclSource { Platen, Feeder }

/// <summary>Scan area in 1/300 inch, starting at the top-left corner.</summary>
public sealed record ScanRegion(int Width, int Height);

/// <summary>A fully resolved eSCL scan job (values the device supports).</summary>
public sealed record ScanRequest(
    EsclSource Source,
    int Dpi,
    string ColorMode,
    string DocumentFormat = "image/jpeg",
    ScanRegion? Region = null)
{
    public string ToXml()
    {
        var settings = new XElement(Scan + "ScanSettings",
            new XAttribute(XNamespace.Xmlns + "scan", Scan),
            new XAttribute(XNamespace.Xmlns + "pwg", Pwg),
            new XElement(Pwg + "Version", "2.63"));

        if (Region is not null)
        {
            settings.Add(new XElement(Pwg + "ScanRegions",
                new XElement(Pwg + "ScanRegion",
                    new XElement(Pwg + "Height", Region.Height),
                    new XElement(Pwg + "ContentRegionUnits", "escl:ThreeHundredthsOfInches"),
                    new XElement(Pwg + "Width", Region.Width),
                    new XElement(Pwg + "XOffset", 0),
                    new XElement(Pwg + "YOffset", 0))));
        }

        settings.Add(
            new XElement(Pwg + "DocumentFormat", DocumentFormat),
            new XElement(Scan + "DocumentFormatExt", DocumentFormat),
            new XElement(Pwg + "InputSource", Source == EsclSource.Feeder ? "Feeder" : "Platen"),
            new XElement(Scan + "XResolution", Dpi),
            new XElement(Scan + "YResolution", Dpi),
            new XElement(Scan + "ColorMode", ColorMode));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), settings).ToString(SaveOptions.DisableFormatting);
    }
}
