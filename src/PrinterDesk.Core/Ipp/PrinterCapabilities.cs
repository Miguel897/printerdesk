namespace PrinterDesk.Core.Ipp;

/// <summary>One ink or toner cartridge, from the printer's <c>marker-*</c> attributes.</summary>
public sealed record Marker(string Name, string Color, int Level, int LowLevel)
{
    /// <summary>-1 (unknown), -2 (unavailable) and -3 (some ink left) are IPP's special levels.</summary>
    public bool IsKnown => Level >= 0;

    public bool IsLow => IsKnown && Level <= LowLevel;
}

/// <summary>What an IPP printer supports (Get-Printer-Attributes), plus its status at that moment.</summary>
public sealed record PrinterCapabilities
{
    public const string PwgRaster = "image/pwg-raster";

    /// <summary>Attributes requested from the printer; the rest of its ~200 attributes are not needed.</summary>
    public static readonly string[] RequestedAttributes =
    [
        "printer-make-and-model", "printer-state", "printer-state-reasons", "printer-is-accepting-jobs",
        "document-format-supported", "sides-supported", "sides-default", "print-color-mode-supported",
        "print-color-mode-default", "media-supported", "media-ready", "media-default", "print-quality-supported",
        "print-quality-default", "copies-supported", "pwg-raster-document-type-supported",
        "pwg-raster-document-resolution-supported", "pwg-raster-document-sheet-back",
        "media-left-margin-supported", "media-right-margin-supported", "media-top-margin-supported",
        "media-bottom-margin-supported", "marker-names", "marker-colors", "marker-levels", "marker-low-levels",
        "operations-supported",
    ];

    public string MakeAndModel { get; init; } = "";

    public IReadOnlyList<string> DocumentFormats { get; init; } = [];

    public IReadOnlyList<string> Sides { get; init; } = [];

    public IReadOnlyList<string> ColorModes { get; init; } = [];

    public IReadOnlyList<string> Media { get; init; } = [];

    /// <summary>Paper loaded in the tray, if the printer knows (else its default).</summary>
    public string? MediaReady { get; init; }

    public IReadOnlyList<int> Qualities { get; init; } = [];

    public int MaxCopies { get; init; } = 1;

    public IReadOnlyList<string> RasterTypes { get; init; } = [];

    /// <summary>PWG raster resolutions in dpi (square only).</summary>
    public IReadOnlyList<int> RasterResolutions { get; init; } = [];

    /// <summary>How the printer expects back sides of two-sided PWG raster jobs: normal, rotated, flipped or manual-tumble.</summary>
    public string SheetBack { get; init; } = "normal";

    /// <summary>Margins for normal (not borderless) printing, in hundredths of a millimeter.</summary>
    public Margins Margins { get; init; }

    public IReadOnlyList<int> Operations { get; init; } = [];

    public PrinterStatus Status { get; init; } = new();

    public bool SupportsPwgRaster => DocumentFormats.Contains(PwgRaster) && RasterResolutions.Count > 0;

    public static PrinterCapabilities Parse(IppMessage response)
    {
        var copies = response.Find("copies-supported")?.Values.OfType<IppRange>().FirstOrDefault();
        return new PrinterCapabilities
        {
            MakeAndModel = response.String("printer-make-and-model") ?? "",
            DocumentFormats = response.Strings("document-format-supported"),
            Sides = response.Strings("sides-supported"),
            ColorModes = response.Strings("print-color-mode-supported"),
            Media = response.Strings("media-supported"),
            MediaReady = response.String("media-ready") ?? response.String("media-default"),
            Qualities = response.Ints("print-quality-supported"),
            MaxCopies = copies?.Upper ?? 1,
            RasterTypes = response.Strings("pwg-raster-document-type-supported"),
            RasterResolutions = response.Find("pwg-raster-document-resolution-supported")?.Values
                .OfType<IppResolution>().Where(r => r.PerInch && r.X == r.Y).Select(r => r.X).Order().ToList() ?? [],
            SheetBack = response.String("pwg-raster-document-sheet-back") ?? "normal",
            Margins = new Margins(
                MaxMargin(response, "media-left-margin-supported"), MaxMargin(response, "media-top-margin-supported"),
                MaxMargin(response, "media-right-margin-supported"), MaxMargin(response, "media-bottom-margin-supported")),
            Operations = response.Ints("operations-supported"),
            Status = PrinterStatus.Parse(response),
        };
    }

    /// <summary>The list also holds 0 (borderless); the largest value is the safe one for plain paper.</summary>
    private static int MaxMargin(IppMessage response, string name) => response.Ints(name).DefaultIfEmpty(0).Max();
}

/// <summary>Margins in hundredths of a millimeter (IPP's unit).</summary>
public readonly record struct Margins(int Left, int Top, int Right, int Bottom);

public sealed record PrinterStatus
{
    public const int Idle = 3;
    public const int Processing = 4;
    public const int Stopped = 5;

    public int State { get; init; } = Idle;

    /// <summary>State reasons without the -report/-warning/-error suffix; "none" removed.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];

    public bool IsAcceptingJobs { get; init; } = true;

    public IReadOnlyList<Marker> Markers { get; init; } = [];

    public static PrinterStatus Parse(IppMessage response)
    {
        var names = response.Strings("marker-names");
        var colors = response.Strings("marker-colors");
        var levels = response.Ints("marker-levels");
        var lows = response.Ints("marker-low-levels");
        return new PrinterStatus
        {
            State = response.Int("printer-state") ?? Idle,
            Reasons = CleanReasons(response.Strings("printer-state-reasons")),
            IsAcceptingJobs = response.Find("printer-is-accepting-jobs")?.Values.OfType<bool>().FirstOrDefault() ?? true,
            Markers = names.Select((name, i) => new Marker(
                name,
                i < colors.Count ? colors[i] : "",
                i < levels.Count ? levels[i] : -1,
                i < lows.Count ? lows[i] : 0)).ToList(),
        };
    }

    internal static List<string> CleanReasons(IEnumerable<string> reasons) => reasons
        .Where(r => r != "none")
        .Select(r => r.EndsWith("-report") ? r[..^7] : r.EndsWith("-warning") ? r[..^8] : r.EndsWith("-error") ? r[..^6] : r)
        .Distinct()
        .ToList();

    /// <summary>"Ready", "Printing", or the problem in plain words, e.g. "Out of paper".</summary>
    public string Describe()
    {
        var problems = Reasons.Select(DescribeReason).OfType<string>().ToList();
        if (problems.Count > 0)
            return string.Join(", ", problems);
        return State switch
        {
            Processing => "Printing",
            Stopped => "Stopped",
            _ => IsAcceptingJobs ? "Ready" : "Not accepting jobs",
        };
    }

    /// <summary>The first problem that stops printing (out of paper, jam…); low ink is only a warning.</summary>
    public string? Problem => Reasons
        .Where(r => r is not ("marker-supply-low" or "toner-low"))
        .Select(DescribeReason).OfType<string>().FirstOrDefault();

    /// <summary>Null for reasons that need no attention (e.g. moving-to-paused is not shown).</summary>
    public static string? DescribeReason(string reason) => reason switch
    {
        "media-empty" or "media-needed" => "Out of paper",
        "media-jam" => "Paper jam",
        "door-open" or "cover-open" or "interlock-open" => "Door open",
        "marker-supply-low" or "toner-low" => "Ink low",
        "marker-supply-empty" or "toner-empty" => "Ink empty",
        "marker-supply-missing" => "Cartridge missing",
        "input-tray-missing" => "Paper tray missing",
        "output-area-full" => "Output tray full",
        "offline" or "shutdown" => "Offline",
        "paused" => "Paused",
        _ => null,
    };
}
