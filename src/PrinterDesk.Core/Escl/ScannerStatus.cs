using System.Xml.Linq;
using static PrinterDesk.Core.Escl.EsclNamespaces;

namespace PrinterDesk.Core.Escl;

public sealed record ScannerStatus(string State, string? AdfState)
{
    public bool IsIdle => State == "Idle";

    public bool IsAdfLoaded => AdfState == "ScannerAdfLoaded";

    /// <summary>True when the feeder reports a condition the user has to fix (jam, open cover, ...).</summary>
    public bool HasAdfProblem => AdfState is not (null or "ScannerAdfLoaded" or "ScannerAdfEmpty" or "ScannerAdfProcessing");

    public static ScannerStatus Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root ?? throw new FormatException("Empty ScannerStatus document.");
        return new ScannerStatus(
            State: root.Element(Pwg + "State")?.Value.Trim() ?? "Unknown",
            AdfState: root.Element(Scan + "AdfState")?.Value.Trim());
    }

    /// <summary>Short human-readable status, e.g. "Idle · Feeder loaded".</summary>
    public string Describe()
    {
        var state = State switch
        {
            "Idle" => "Idle",
            "Processing" => "Busy",
            "Testing" => "Busy (self test)",
            "Stopped" => "Stopped",
            "Down" => "Not available",
            _ => State,
        };

        var adf = AdfState switch
        {
            null => null,
            "ScannerAdfLoaded" => "Feeder loaded",
            "ScannerAdfEmpty" => "Feeder empty",
            "ScannerAdfProcessing" => "Feeding",
            "ScannerAdfJam" => "Paper jam in feeder",
            "ScannerAdfHatchOpen" => "Feeder cover open",
            "ScannerAdfMispick" => "Feeder failed to pick paper",
            "ScannerAdfMultipickDetected" => "Feeder picked several sheets",
            "ScannerAdfInputTrayOverloaded" => "Feeder overloaded",
            _ => AdfState.Replace("ScannerAdf", "Feeder: "),
        };

        return adf is null ? state : $"{state} · {adf}";
    }
}
