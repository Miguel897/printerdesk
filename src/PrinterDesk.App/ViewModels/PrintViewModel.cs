using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrinterDesk.App.Services;
using PrinterDesk.Core;
using PrinterDesk.Core.Documents;
using PrinterDesk.Core.Ipp;
using PrinterDesk.Core.Printing;
using PrinterDesk.Core.Settings;

namespace PrinterDesk.App.ViewModels;

/// <summary>The Print tab: printer status and ink, print options, and the print job itself.</summary>
public sealed partial class PrintViewModel : ObservableObject, IDisposable
{
    private readonly IDialogService _dialogs;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private readonly Func<IReadOnlyList<DocumentPage>> _pages;
    private readonly PdfSources _pdfs;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    private IppClient? _client;
    private PrinterCapabilities? _caps;
    private CancellationTokenSource? _printCts;
    private int _connectVersion;
    private int _pageCount;
    private bool _loadingOptions;

    public PrintViewModel(IDialogService dialogs, AppSettings settings, Action saveSettings, Func<IReadOnlyList<DocumentPage>> pages, PdfSources pdfs)
    {
        _dialogs = dialogs;
        _settings = settings;
        _saveSettings = saveSettings;
        _pages = pages;
        _pdfs = pdfs;

        PrinterStatusText = "Connect to a scanner to find its printer.";
        InkText = "";
        ProgressText = "";
        SummaryText = "";
        PageRange = "";
        Copies = 1;

        _loadingOptions = true;
        SelectedColor = settings.Print.Color;
        SelectedSides = settings.Print.Sides;
        SelectedOrientation = settings.Print.Orientation;
        SelectedQuality = settings.Print.Quality;
        _loadingOptions = false;

        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
    }

    public ObservableCollection<PrintColor> ColorModes { get; } = [PrintColor.BlackAndWhite, PrintColor.Color];

    public ObservableCollection<PrintSides> SidesOptions { get; } = [PrintSides.OneSided];

    public ObservableCollection<MediaSize> MediaSizes { get; } = [];

    public IReadOnlyList<PrintOrientation> Orientations { get; } = Enum.GetValues<PrintOrientation>();

    public IReadOnlyList<PrintQuality> Qualities { get; } = Enum.GetValues<PrintQuality>();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    public partial bool IsAvailable { get; set; }

    [ObservableProperty]
    public partial string PrinterStatusText { get; set; }

    [ObservableProperty]
    public partial string InkText { get; set; }

    [ObservableProperty]
    public partial bool IsInkLow { get; set; }

    [ObservableProperty]
    public partial int Copies { get; set; }

    [ObservableProperty]
    public partial string PageRange { get; set; }

    [ObservableProperty]
    public partial PrintColor SelectedColor { get; set; }

    [ObservableProperty]
    public partial PrintSides SelectedSides { get; set; }

    [ObservableProperty]
    public partial PrintOrientation SelectedOrientation { get; set; }

    [ObservableProperty]
    public partial MediaSize? SelectedMedia { get; set; }

    [ObservableProperty]
    public partial PrintQuality SelectedQuality { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand), nameof(CancelPrintCommand))]
    public partial bool IsPrinting { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; }

    /// <summary>"3 pages · 2 sheets", or why the page range is wrong.</summary>
    [ObservableProperty]
    public partial string SummaryText { get; set; }

    /// <summary>Raised when an option or the printer changes what a page looks like when printed.</summary>
    public event EventHandler? OptionsChanged;

    /// <summary>The current options, for the print preview.</summary>
    public PrintOptions PreviewOptions => CurrentOptions;

    /// <summary>The connected printer, or null.</summary>
    public PrinterCapabilities? Capabilities => _caps;

    private PrintOptions CurrentOptions => new()
    {
        Copies = Copies,
        PageRange = PageRange,
        Color = SelectedColor,
        Sides = SelectedSides,
        Orientation = SelectedOrientation,
        Media = SelectedMedia?.Keyword,
        Quality = SelectedQuality,
    };

    // ---------------------------------------------------------------- connection

    /// <summary>Looks for an IPP printer on the scanner's host (an all-in-one is both).</summary>
    public async Task ConnectAsync(string host)
    {
        if (IsPrinting)
            return; // keep the running job's connection
        var version = ++_connectVersion;
        Disconnect();
        PrinterStatusText = $"Looking for the printer at {host}…";

        var client = new IppClient(IppClient.UriForHost(host));
        try
        {
            var caps = await client.GetCapabilitiesAsync();
            if (version != _connectVersion)
            {
                client.Dispose();
                return;
            }
            if (!caps.SupportsPwgRaster)
            {
                client.Dispose();
                PrinterStatusText = $"{caps.MakeAndModel} does not accept the print format PrinterDesk uses (PWG raster).";
                return;
            }

            _client = client;
            _caps = caps;
            ApplyCapabilities(caps);
            ShowStatus(caps.Status);
            IsAvailable = true;
            _statusTimer.Start();
            OptionsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (IppException ex)
        {
            client.Dispose();
            Log.Info($"No IPP printer at {host}: {ex.Message}");
            if (version == _connectVersion)
                PrinterStatusText = $"No printer found at {host}.";
        }
    }

    public void Disconnect()
    {
        if (IsPrinting)
            return;
        _statusTimer.Stop();
        _client?.Dispose();
        _client = null;
        _caps = null;
        IsAvailable = false;
        InkText = "";
        IsInkLow = false;
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Offers only what the printer supports, keeping the saved choices when possible.</summary>
    private void ApplyCapabilities(PrinterCapabilities caps)
    {
        _loadingOptions = true;
        var (color, sides, media) = (_settings.Print.Color, _settings.Print.Sides, _settings.Print.Media);

        // Lists are updated in place: clearing them would make the combo boxes push null into the enum properties.
        Sync(ColorModes, [PrintColor.BlackAndWhite,
            .. caps.ColorModes.Contains("color") && caps.RasterTypes.Contains("srgb_8") ? [PrintColor.Color] : Array.Empty<PrintColor>()]);
        SelectedColor = ColorModes.Contains(color) ? color : PrintColor.BlackAndWhite;
        OnPropertyChanged(nameof(SelectedColor));

        Sync(SidesOptions, [PrintSides.OneSided,
            .. caps.Sides.Contains("two-sided-long-edge") ? [PrintSides.TwoSidedLongEdge] : Array.Empty<PrintSides>(),
            .. caps.Sides.Contains("two-sided-short-edge") ? [PrintSides.TwoSidedShortEdge] : Array.Empty<PrintSides>()]);
        SelectedSides = SidesOptions.Contains(sides) ? sides : PrintSides.OneSided;
        OnPropertyChanged(nameof(SelectedSides));

        MediaSizes.Clear();
        foreach (var size in caps.Media.Select(MediaSize.TryParse).OfType<MediaSize>())
            MediaSizes.Add(size);
        SelectedMedia = MediaSizes.FirstOrDefault(m => m.Keyword == media)
                        ?? MediaSizes.FirstOrDefault(m => m.Keyword == caps.MediaReady)
                        ?? MediaSizes.FirstOrDefault();
        _loadingOptions = false;
        UpdateSummary();
    }

    private static void Sync<T>(ObservableCollection<T> list, IReadOnlyList<T> wanted)
    {
        foreach (var item in list.Except(wanted).ToList())
            list.Remove(item);
        for (var i = 0; i < wanted.Count; i++)
        {
            if (!list.Contains(wanted[i]))
                list.Insert(i, wanted[i]);
        }
    }

    private async Task RefreshStatusAsync()
    {
        if (_client is null || IsPrinting)
            return;
        try
        {
            ShowStatus(await _client.GetStatusAsync());
        }
        catch (IppException ex)
        {
            PrinterStatusText = ex.Message;
        }
    }

    private void ShowStatus(PrinterStatus status)
    {
        PrinterStatusText = $"{_caps?.MakeAndModel ?? "Printer"}: {status.Describe()}";
        var known = status.Markers.Where(m => m.IsKnown).ToList();
        InkText = known.Count == 0 ? "" :
            "Ink: " + string.Join(" · ", known.Select(m => $"{m.Name.Replace(" cartridge", "")} {m.Level} %"));
        IsInkLow = known.Any(m => m.IsLow);
    }

    // ---------------------------------------------------------------- options

    partial void OnCopiesChanged(int value)
    {
        var clamped = Math.Clamp(value, 1, Math.Min(PrintOptions.MaxCopies, _caps?.MaxCopies ?? PrintOptions.MaxCopies));
        if (clamped != value)
            Copies = clamped;
        else
            UpdateSummary();
    }

    partial void OnPageRangeChanged(string value) => UpdateSummary();

    partial void OnSelectedColorChanged(PrintColor value) => SaveOptions();

    partial void OnSelectedSidesChanged(PrintSides value) => SaveOptions();

    partial void OnSelectedOrientationChanged(PrintOrientation value) => SaveOptions();

    partial void OnSelectedMediaChanged(MediaSize? value) => SaveOptions();

    partial void OnSelectedQualityChanged(PrintQuality value) => SaveOptions();

    private void SaveOptions()
    {
        UpdateSummary();
        OptionsChanged?.Invoke(this, EventArgs.Empty);
        if (_loadingOptions)
            return;
        _settings.Print = CurrentOptions with { Copies = 1, PageRange = "" };
        _saveSettings();
    }

    /// <summary>Called by the main view model whenever the document's pages change.</summary>
    public void PagesChanged(int pageCount)
    {
        _pageCount = pageCount;
        UpdateSummary();
        PrintCommand.NotifyCanExecuteChanged();
    }

    private void UpdateSummary()
    {
        if (_pageCount == 0)
        {
            SummaryText = "";
            return;
        }
        if (!Core.Printing.PageRange.TryParse(PageRange, _pageCount, out var pages, out var error))
        {
            SummaryText = error;
            return;
        }
        var sides = PrintLayout.Sides(pages, Copies, SelectedSides != PrintSides.OneSided).Count;
        var sheets = SelectedSides == PrintSides.OneSided ? sides : (sides + 1) / 2;
        SummaryText = $"{Plural(pages.Count * Copies, "page")} · {Plural(sheets, "sheet")}";
    }

    // ---------------------------------------------------------------- printing

    private bool CanPrint() => IsAvailable && !IsPrinting && _pageCount > 0;

    [RelayCommand(CanExecute = nameof(CanPrint))]
    private async Task PrintAsync()
    {
        var pages = _pages();
        if (!Core.Printing.PageRange.TryParse(PageRange, pages.Count, out var indexes, out var error))
        {
            _dialogs.ShowError(error);
            return;
        }

        var client = _client!;
        var caps = _caps!;
        _statusTimer.Stop();
        _printCts = new CancellationTokenSource();
        var ct = _printCts.Token;
        IsPrinting = true;
        var temp = Path.Combine(Path.GetTempPath(), "PrinterDesk", $"print_{Guid.NewGuid():N}.pwg");
        int? jobId = null;
        try
        {
            var builder = new PrintJobBuilder(pages, indexes, CurrentOptions, caps);
            ProgressText = "Checking the settings with the printer…";
            await client.ValidateJobAsync(builder.Ticket, ct);

            var prepared = new Progress<int>(n => ProgressText = $"Preparing page {n} of {builder.SideCount}…");
            Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
            await Task.Run(() =>
            {
                using var file = File.Create(temp);
                builder.Write(file, new PageRasterizer(_pdfs), prepared, ct);
            }, ct);

            jobId = await client.CreateJobAsync(builder.Ticket, ct);
            Log.Info($"Print job {jobId}: {builder.SideCount} sides, {builder.Ticket.Sides}, {builder.Ticket.ColorMode}, {builder.Media.Keyword}");
            await using (var file = File.OpenRead(temp))
            {
                var total = Math.Max(1, file.Length);
                var sent = new Progress<long>(bytes => ProgressText = $"Sending to the printer… {bytes * 100 / total} %");
                await client.SendDocumentAsync(jobId.Value, file, PrinterCapabilities.PwgRaster, sent, ct);
            }
            await MonitorJobAsync(client, jobId.Value, builder.SideCount, ct);
        }
        catch (OperationCanceledException)
        {
            if (jobId is { } id)
                await client.CancelJobAsync(id);
            ProgressText = "Printing cancelled.";
        }
        catch (Exception ex) when (ex is IppException or IOException or UnauthorizedAccessException or NotSupportedException
                                       or FormatException or InvalidOperationException or Core.Pdfium.PdfOpenException)
        {
            if (jobId is { } id)
                await client.CancelJobAsync(id);
            Log.Error("Printing failed", ex);
            ProgressText = "Printing stopped.";
            _dialogs.ShowError(ex.Message);
        }
        finally
        {
            TryDelete(temp);
            _printCts.Dispose();
            _printCts = null;
            IsPrinting = false;
            if (_client is not null)
            {
                await RefreshStatusAsync();
                _statusTimer.Start();
            }
        }
    }

    /// <summary>Follows the job until the printer reports it finished; shows paper/jam problems meanwhile.</summary>
    private async Task MonitorJobAsync(IppClient client, int jobId, int sides, CancellationToken ct)
    {
        while (true)
        {
            var job = await client.GetJobAsync(jobId, ct);
            if (job.IsFinished)
            {
                ProgressText = job.State switch
                {
                    PrintJobStatus.Completed => $"Done: {Plural(sides, "page")} printed.",
                    PrintJobStatus.Canceled => "The print job was cancelled on the printer.",
                    _ => "The printer stopped the job" +
                         (job.Reasons.Count > 0 ? $" ({string.Join(", ", job.Reasons)})." : "."),
                };
                return;
            }

            var status = await client.GetStatusAsync(ct);
            ShowStatus(status);
            ProgressText = status.Problem is { } problem ? $"Waiting: {problem}. Fix it on the printer to continue."
                : job.ImpressionsCompleted > 0 ? $"Printing: {Math.Min(job.ImpressionsCompleted, sides)} of {sides} pages…"
                : "Printing…";
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    [RelayCommand(CanExecute = nameof(IsPrinting))]
    private void CancelPrint()
    {
        _printCts?.Cancel();
        ProgressText = "Cancelling…";
    }

    /// <summary>Cancels a running job, e.g. when the window closes.</summary>
    public void CancelIfPrinting() => _printCts?.Cancel();

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // %TEMP% is cleaned by Windows eventually.
        }
    }

    public void Dispose()
    {
        _statusTimer.Stop();
        _printCts?.Cancel();
        _client?.Dispose();
    }

    private static string Plural(int count, string word) => count == 1 ? $"1 {word}" : $"{count} {word}s";
}
