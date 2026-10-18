using PrinterDesk.Core.Printing;
using PrinterDesk.Core.Scanning;

namespace PrinterDesk.Core.Settings;

public sealed class AppSettings
{
    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double WindowWidth { get; set; } = 1240;

    public double WindowHeight { get; set; } = 780;

    public bool WindowMaximized { get; set; }

    public string? ScannerUri { get; set; }

    public string? ScannerName { get; set; }

    public ScanOptions Scan { get; set; } = new();

    /// <summary>Last print settings (copies and page range are not kept).</summary>
    public PrintOptions Print { get; set; } = new();

    public string OutputFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Scans");
}
