using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using System.Xml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrinterDesk.App.Services;
using PrinterDesk.Core;
using PrinterDesk.Core.Discovery;
using PrinterDesk.Core.Documents;
using PrinterDesk.Core.Escl;
using PrinterDesk.Core.Scanning;
using PrinterDesk.Core.Settings;

namespace PrinterDesk.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IDialogService _dialogs;
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly WorkingSession _session;
    private readonly PdfSources _pdfs = new();
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _elapsedTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    private EsclClient? _client;
    private ScannerCapabilities? _caps;
    private CancellationTokenSource? _scanCts;
    private DateTime _scanStarted;
    private string? _previewFile;
    private int _preferredDpi;
    private int _connectVersion;
    private bool _isDirty;
    private bool _loadingOptions;
    private bool _refreshingStatus;
    private bool _renamingScanner;

    public MainViewModel(IDialogService dialogs, SettingsStore settingsStore, WorkingSession session)
    {
        _dialogs = dialogs;
        _settingsStore = settingsStore;
        _session = session;
        _settings = settingsStore.Load();

        StatusText = "Not connected";
        ManualAddress = "";
        ProgressText = "";
        ElapsedText = "";
        PageCountText = "No pages";

        _loadingOptions = true;
        _preferredDpi = _settings.Scan.Dpi;
        SelectedSource = _settings.Scan.Source;
        SelectedColor = _settings.Scan.Color;
        SelectedPaper = _settings.Scan.Paper;
        Resolutions.Add(_preferredDpi);
        SelectedDpi = _preferredDpi;
        _loadingOptions = false;

        Print = new PrintViewModel(dialogs, _settings, SaveSettings, () => Pages.Select(p => p.Page).ToList(), _pdfs);

        Pages.CollectionChanged += OnPagesChanged;
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _elapsedTimer.Tick += (_, _) => UpdateElapsed();
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            UpdatePreview();
        };
    }

    public PrintViewModel Print { get; }

    public ObservableCollection<DiscoveredScanner> Scanners { get; } = [];

    public ObservableCollection<PageViewModel> Pages { get; } = [];

    public ObservableCollection<int> Resolutions { get; } = [];

    public IReadOnlyList<ScanSource> Sources { get; } = Enum.GetValues<ScanSource>();

    public IReadOnlyList<ColorMode> ColorModes { get; } = Enum.GetValues<ColorMode>();

    public IReadOnlyList<PaperSize> PaperSizes { get; } = Enum.GetValues<PaperSize>();

    [ObservableProperty]
    public partial DiscoveredScanner? SelectedScanner { get; set; }

    [ObservableProperty]
    public partial string ManualAddress { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(ScanDuplexCommand))]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanDuplexCommand))]
    public partial bool HasFeeder { get; set; }

    [ObservableProperty]
    public partial bool IsDiscovering { get; set; }

    [ObservableProperty]
    public partial ScanSource SelectedSource { get; set; }

    [ObservableProperty]
    public partial int SelectedDpi { get; set; }

    [ObservableProperty]
    public partial ColorMode SelectedColor { get; set; }

    [ObservableProperty]
    public partial PaperSize SelectedPaper { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(ScanDuplexCommand), nameof(CancelScanCommand),
        nameof(MoveUpCommand), nameof(MoveDownCommand), nameof(RotateLeftCommand), nameof(RotateRightCommand),
        nameof(DeletePagesCommand), nameof(NewDocumentCommand), nameof(SavePdfCommand), nameof(SaveJpegCommand), nameof(OpenFilesCommand))]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; }

    [ObservableProperty]
    public partial string ElapsedText { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand), nameof(MoveDownCommand), nameof(RotateLeftCommand),
        nameof(RotateRightCommand), nameof(DeletePagesCommand))]
    public partial PageViewModel? SelectedPage { get; set; }

    [ObservableProperty]
    public partial bool HasPages { get; set; }

    [ObservableProperty]
    public partial string PageCountText { get; set; }

    /// <summary>Preview PDF to show (with <c>#page=N</c>), or null for an empty preview.</summary>
    [ObservableProperty]
    public partial Uri? PreviewUri { get; set; }

    /// <summary>"Saved: file.pdf" after a save, cleared on the next edit.</summary>
    [ObservableProperty]
    public partial string? SavedMessage { get; set; }

    private string? LastSavedFile { get; set; }

    // ---------------------------------------------------------------- startup / connection

    public async Task InitializeAsync()
    {
        RestoreSession();

        if (_settings.ScannerUri is { } saved && Uri.TryCreate(saved, UriKind.Absolute, out var uri))
        {
            var scanner = new DiscoveredScanner(_settings.ScannerName ?? uri.Host, uri);
            Scanners.Add(scanner);
            SelectedScanner = scanner;
        }

        await DiscoverAsync();
    }

    [RelayCommand]
    private async Task DiscoverAsync()
    {
        IsDiscovering = true;
        try
        {
            foreach (var scanner in await ScannerDiscovery.FindAsync(TimeSpan.FromSeconds(3)))
            {
                if (!Scanners.Any(s => SameHost(s, scanner)))
                    Scanners.Add(scanner);
            }

            if (SelectedScanner is null && Scanners.Count > 0)
                SelectedScanner = Scanners[0];
            else if (Scanners.Count == 0)
                StatusText = "No scanner found. Type its IP address and click Connect.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // mDNS can fail on some networks/firewalls; manual address still works.
            if (SelectedScanner is null)
                StatusText = $"Network search failed ({ex.Message}). Type the scanner's IP address.";
        }
        finally
        {
            IsDiscovering = false;
        }
    }

    [RelayCommand]
    private void ConnectManual()
    {
        if (string.IsNullOrWhiteSpace(ManualAddress))
            return;

        DiscoveredScanner scanner;
        try
        {
            scanner = DiscoveredScanner.FromAddress(ManualAddress);
        }
        catch (UriFormatException)
        {
            _dialogs.ShowError($"\"{ManualAddress}\" is not a valid address. Example: 192.168.1.130");
            return;
        }

        var existing = Scanners.FirstOrDefault(s => SameHost(s, scanner));
        if (existing is null)
            Scanners.Add(scanner);

        var target = existing ?? scanner;
        if (Equals(SelectedScanner, target))
            _ = ConnectAsync(target);
        else
            SelectedScanner = target;
    }

    partial void OnSelectedScannerChanged(DiscoveredScanner? value)
    {
        if (value is not null && !_renamingScanner)
            _ = ConnectAsync(value);
    }

    private async Task ConnectAsync(DiscoveredScanner scanner)
    {
        var version = ++_connectVersion;
        _statusTimer.Stop();
        _client?.Dispose();
        _client = null;
        _caps = null;
        IsConnected = false;
        HasFeeder = false;
        StatusText = $"Connecting to {scanner.BaseUri.Host}…";
        Print.Disconnect();

        var client = new EsclClient(scanner.BaseUri);
        try
        {
            var caps = await client.GetCapabilitiesAsync();
            if (version != _connectVersion)
            {
                client.Dispose();
                return;
            }

            scanner = NameAfterModel(scanner, caps);
            _client = client;
            _caps = caps;
            IsConnected = true;
            HasFeeder = caps.Adf is not null;
            _settings.ScannerUri = scanner.BaseUri.AbsoluteUri;
            _settings.ScannerName = scanner.Name;
            SaveSettings();
            UpdateResolutions();
            await RefreshStatusAsync();
            _statusTimer.Start();
            _ = Print.ConnectAsync(scanner.BaseUri.Host);
        }
        catch (Exception ex) when (ex is EsclException or XmlException or FormatException)
        {
            client.Dispose();
            if (version == _connectVersion)
                StatusText = ex.Message;
        }
    }

    /// <summary>A scanner added by IP is shown as its IP until we know its model; then it gets the model name.</summary>
    private DiscoveredScanner NameAfterModel(DiscoveredScanner scanner, ScannerCapabilities caps)
    {
        var index = Scanners.IndexOf(scanner);
        if (scanner.Name != scanner.BaseUri.Host || index < 0)
            return scanner;

        var named = scanner with { Name = caps.MakeAndModel };
        _renamingScanner = true;
        Scanners[index] = named;
        SelectedScanner = named;
        _renamingScanner = false;
        return named;
    }

    private async Task RefreshStatusAsync()
    {
        if (_client is null || IsScanning || _refreshingStatus)
            return;

        _refreshingStatus = true;
        try
        {
            StatusText = (await _client.GetStatusAsync()).Describe();
        }
        catch (Exception ex) when (ex is EsclException or XmlException or FormatException)
        {
            StatusText = ex.Message;
        }
        finally
        {
            _refreshingStatus = false;
        }
    }

    private static bool SameHost(DiscoveredScanner a, DiscoveredScanner b) =>
        string.Equals(a.BaseUri.Host, b.BaseUri.Host, StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- scan options

    private ScanOptions CurrentOptions => new()
    {
        Source = SelectedSource,
        Dpi = SelectedDpi,
        Color = SelectedColor,
        Paper = SelectedPaper,
    };

    partial void OnSelectedSourceChanged(ScanSource value)
    {
        UpdateResolutions();
        SaveOptions();
    }

    partial void OnSelectedDpiChanged(int value)
    {
        if (_loadingOptions || value <= 0)
            return;
        _preferredDpi = value;
        SaveOptions();
    }

    partial void OnSelectedColorChanged(ColorMode value) => SaveOptions();

    partial void OnSelectedPaperChanged(PaperSize value) => SaveOptions();

    /// <summary>Offers the resolutions of the selected source, keeping the user's preferred dpi when possible.</summary>
    private void UpdateResolutions()
    {
        if (_caps is null)
            return;

        _loadingOptions = true;
        Resolutions.Clear();
        foreach (var dpi in ScanPlanner.ResolutionsFor(SelectedSource, _caps))
            Resolutions.Add(dpi);
        SelectedDpi = Resolutions.Count == 0 ? _preferredDpi
            : Resolutions.Where(r => r <= _preferredDpi).DefaultIfEmpty(Resolutions[0]).Max();
        _loadingOptions = false;
        OnPropertyChanged(nameof(SelectedDpi));
    }

    private void SaveOptions()
    {
        if (_loadingOptions)
            return;
        _settings.Scan = CurrentOptions with { Dpi = _preferredDpi };
        SaveSettings();
    }

    /// <summary>Loaded settings; the window reads and updates its placement here.</summary>
    public AppSettings Settings => _settings;

    public void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth interrupting the user; defaults are used next time.
            Log.Error("Settings could not be saved", ex);
        }
    }

    // ---------------------------------------------------------------- scanning

    private bool CanScan() => IsConnected && !IsScanning;

    private bool CanScanDuplex() => CanScan() && HasFeeder;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private Task ScanAsync() => RunScanJobAsync(async ct =>
    {
        var pages = await ScanPagesAsync(CurrentOptions, "Scanning", ct);
        ProgressText = $"Done: {Plural(pages.Count, "page")} added.";
    });

    [RelayCommand(CanExecute = nameof(CanScanDuplex))]
    private Task ScanDuplexAsync() => RunScanJobAsync(async ct =>
    {
        var options = CurrentOptions with { Source = ScanSource.Feeder };
        var fronts = await ScanPagesAsync(options, "Front sides", ct);

        if (!await WaitForFlippedStackAsync(fronts.Count, ct))
        {
            ProgressText = $"Duplex stopped: {Plural(fronts.Count, "front side")} kept.";
            return;
        }

        var backs = await ScanPagesAsync(options, "Back sides", ct);
        var start = Pages.IndexOf(fronts[0]);
        var ordered = ManualDuplex.Interleave(fronts, backs);
        foreach (var page in fronts.Concat(backs))
            Pages.Remove(page);
        for (var i = 0; i < ordered.Count; i++)
            Pages.Insert(start + i, ordered[i]);
        SelectedPage = ordered[0];

        ProgressText = $"Done: {Plural(ordered.Count, "page")} added (two-sided).";
        if (fronts.Count != backs.Count)
        {
            _dialogs.ShowInfo($"{Plural(fronts.Count, "front side")} but {Plural(backs.Count, "back side")} were scanned. " +
                              "The extra pages were added at the end; please check the page order.");
        }
    });

    [RelayCommand(CanExecute = nameof(IsScanning))]
    private void CancelScan()
    {
        _scanCts?.Cancel();
        ProgressText = "Cancelling…";
    }

    private async Task RunScanJobAsync(Func<CancellationToken, Task> job)
    {
        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        _scanStarted = DateTime.Now;
        UpdateElapsed();
        _elapsedTimer.Start();
        try
        {
            await job(_scanCts.Token);
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Scan cancelled. Pages already received were kept.";
        }
        catch (Exception ex) when (ex is EsclException or IOException or XmlException or FormatException)
        {
            ProgressText = "Scan stopped.";
            _dialogs.ShowError(ex.Message);
        }
        finally
        {
            _elapsedTimer.Stop();
            IsScanning = false;
            _scanCts.Dispose();
            _scanCts = null;
            await RefreshStatusAsync();
        }
    }

    /// <summary>Runs one scan job and appends every received page to the document.</summary>
    private async Task<List<PageViewModel>> ScanPagesAsync(ScanOptions options, string label, CancellationToken ct)
    {
        var client = _client ?? throw new EsclException("Not connected to a scanner.");
        var caps = _caps!;

        ProgressText = $"{label}: checking the scanner…";
        var status = await WaitUntilIdleAsync(client, ct);
        if (status.HasAdfProblem)
            throw new EsclException($"Check the feeder: {status.Describe()}.");
        if (options.Source == ScanSource.Feeder && !status.IsAdfLoaded)
            throw new EsclException("The feeder is empty. Load the pages and try again.");

        var request = ScanPlanner.Plan(options, caps, status);
        var fromFeeder = request.Source == EsclSource.Feeder;
        var scanned = new List<PageViewModel>();
        ProgressText = fromFeeder ? $"{label} from the feeder: scanning page 1…" : $"{label} from the flatbed…";

        await foreach (var jpeg in client.ScanAsync(request, ct))
        {
            var page = new PageViewModel(_session.AddPage(jpeg, request.Dpi), _pdfs);
            Pages.Add(page);
            scanned.Add(page);
            SelectedPage = page;
            ProgressText = fromFeeder
                ? $"{label} from the feeder: {Plural(scanned.Count, "page")} received, scanning…"
                : $"{label}: page received.";
        }

        if (scanned.Count == 0)
        {
            var after = await client.GetStatusAsync(ct);
            throw new EsclException($"The scanner returned no pages ({after.Describe()}).");
        }

        return scanned;
    }

    /// <summary>Right after a job the scanner may report Processing for a few seconds.</summary>
    private static async Task<ScannerStatus> WaitUntilIdleAsync(EsclClient client, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var status = await client.GetStatusAsync(ct);
            if (status.IsIdle)
                return status;
            if (attempt == 10)
                throw new EsclException("The scanner is busy (it may be in use by another computer). Try again in a moment.");
            await Task.Delay(TimeSpan.FromSeconds(1.5), ct);
        }
    }

    private async Task<bool> WaitForFlippedStackAsync(int fronts, CancellationToken ct)
    {
        ProgressText = "Waiting for the flipped stack…";
        var message = $"{Plural(fronts, "front side")} scanned.\n\n" +
                      "Take the pages from the output tray, turn the whole stack over without changing " +
                      "the page order, put it back in the feeder and click OK.";
        while (true)
        {
            if (!_dialogs.Confirm(message, "Two-sided scan"))
                return false;
            if ((await _client!.GetStatusAsync(ct)).IsAdfLoaded)
                return true;
            message = "The feeder still looks empty.\n\nLoad the flipped stack and click OK, or Cancel to keep only the front sides.";
        }
    }

    private void UpdateElapsed() => ElapsedText = (DateTime.Now - _scanStarted).ToString(@"m\:ss");

    // ---------------------------------------------------------------- page editing

    private bool CanEditPage() => SelectedPage is not null && !IsScanning;

    [RelayCommand(CanExecute = nameof(CanEditPage))]
    private void MoveUp()
    {
        var index = Pages.IndexOf(SelectedPage!);
        if (index > 0)
            Pages.Move(index, index - 1);
    }

    [RelayCommand(CanExecute = nameof(CanEditPage))]
    private void MoveDown()
    {
        var index = Pages.IndexOf(SelectedPage!);
        if (index >= 0 && index < Pages.Count - 1)
            Pages.Move(index, index + 1);
    }

    [RelayCommand(CanExecute = nameof(CanEditPage))]
    private void RotateLeft() => RotateSelected(-90);

    [RelayCommand(CanExecute = nameof(CanEditPage))]
    private void RotateRight() => RotateSelected(90);

    private void RotateSelected(int degrees)
    {
        SelectedPage!.Rotate(degrees);
        PagesEdited();
    }

    /// <param name="selected">The list's selected items (multi-selection); falls back to <see cref="SelectedPage"/>.</param>
    [RelayCommand(CanExecute = nameof(CanEditPage))]
    private void DeletePages(IList? selected)
    {
        var victims = selected?.OfType<PageViewModel>().ToList() ?? [];
        if (victims.Count == 0 && SelectedPage is not null)
            victims.Add(SelectedPage);
        if (victims.Count == 0)
            return;

        var index = victims.Min(Pages.IndexOf);
        foreach (var page in victims)
        {
            Pages.Remove(page);
            _session.DeletePage(page.Page);
        }
        SelectedPage = Pages.Count == 0 ? null : Pages[Math.Clamp(index, 0, Pages.Count - 1)];
    }

    private bool CanOpen() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenFilesAsync()
    {
        if (_dialogs.AskOpenPaths(DocumentImporter.DialogFilter) is { } paths)
            await ImportFilesAsync(paths);
    }

    /// <summary>Adds the pages of PDF, JPEG and PNG files at the end (Open… and files dropped on the window).</summary>
    public async Task ImportFilesAsync(IReadOnlyList<string> paths)
    {
        if (IsScanning)
            return;

        var errors = new List<string>();
        var added = 0;
        foreach (var path in paths)
        {
            try
            {
                foreach (var page in await DocumentImporter.ImportAsync(path, _session, _pdfs))
                {
                    var item = new PageViewModel(page, _pdfs);
                    Pages.Add(item);
                    SelectedPage = item;
                    added++;
                }
            }
            catch (ImportException ex)
            {
                Log.Error("Import failed", ex);
                errors.Add(ex.Message);
            }
        }

        if (added > 0)
            ProgressText = $"Added {Plural(added, "page")}.";
        if (errors.Count > 0)
            _dialogs.ShowError(string.Join("\n", errors));
    }

    /// <summary>Drag &amp; drop reorder from the view.</summary>
    public void MovePage(PageViewModel page, int newIndex)
    {
        var oldIndex = Pages.IndexOf(page);
        if (IsScanning || oldIndex < 0 || newIndex < 0 || newIndex >= Pages.Count || oldIndex == newIndex)
            return;
        Pages.Move(oldIndex, newIndex);
        SelectedPage = page;
    }

    private bool CanStartNew() => HasPages && !IsScanning;

    [RelayCommand(CanExecute = nameof(CanStartNew))]
    private void NewDocument()
    {
        if (_isDirty && !_dialogs.Confirm($"Discard the {Plural(Pages.Count, "unsaved page")}?", "New document"))
            return;

        Pages.Clear();
        _pdfs.Clear();
        _session.Clear();
        _isDirty = false;
        SavedMessage = null;
        ProgressText = "";
        ElapsedText = "";
    }

    private void OnPagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        for (var i = 0; i < Pages.Count; i++)
            Pages[i].Number = i + 1;
        PagesEdited();
    }

    /// <summary>Called after any change to the pages: persist, refresh preview and commands.</summary>
    private void PagesEdited()
    {
        _isDirty = Pages.Count > 0;
        HasPages = Pages.Count > 0;
        PageCountText = Pages.Count == 0 ? "No pages" : Plural(Pages.Count, "page");
        Print.PagesChanged(Pages.Count);
        SavedMessage = null;
        TrySaveSession();

        SavePdfCommand.NotifyCanExecuteChanged();
        SaveJpegCommand.NotifyCanExecuteChanged();
        NewDocumentCommand.NotifyCanExecuteChanged();

        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void TrySaveSession()
    {
        try
        {
            _session.Save(Pages.Select(p => p.Page), _isDirty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Crash recovery is best effort.
        }
    }

    private void RestoreSession()
    {
        var (pages, dirty) = _session.Load();
        if (pages.Count > 0 && dirty &&
            _dialogs.Confirm($"{Plural(pages.Count, "unsaved page")} from the last session were found.\n\nRestore them?", "Restore pages"))
        {
            foreach (var page in pages)
            {
                try
                {
                    Pages.Add(new PageViewModel(page, _pdfs));
                }
                catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
                {
                    // Skip a damaged page file.
                }
            }
            ProgressText = $"Restored {Plural(Pages.Count, "page")}.";
            return;
        }

        _session.Clear();
    }

    // ---------------------------------------------------------------- preview

    partial void OnSelectedPageChanged(PageViewModel? value) => UpdatePreviewUri();

    private void UpdatePreview()
    {
        if (Pages.Count == 0)
        {
            _previewFile = null;
            PreviewUri = null;
            _session.DeletePreviewsExcept(null);
            return;
        }

        var path = _session.NewPreviewPath();
        try
        {
            PdfExporter.Export(Pages.Select(p => p.Page), path);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or PdfSharp.PdfSharpException)
        {
            return;
        }

        _previewFile = path;
        UpdatePreviewUri();
        _session.DeletePreviewsExcept(path);
    }

    private void UpdatePreviewUri()
    {
        if (_previewFile is null)
        {
            PreviewUri = null;
            return;
        }

        var page = SelectedPage is null ? 1 : Math.Max(Pages.IndexOf(SelectedPage) + 1, 1);
        PreviewUri = new Uri($"{new Uri(_previewFile).AbsoluteUri}#page={page}");
    }

    // ---------------------------------------------------------------- saving

    private bool CanSave() => HasPages && !IsScanning;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SavePdf() => Save("PDF document (*.pdf)|*.pdf", ".pdf", path =>
    {
        PdfExporter.Export(Pages.Select(p => p.Page), path);
        return $"Saved {Path.GetFileName(path)}";
    });

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveJpeg() => Save("JPEG image (*.jpg)|*.jpg", ".jpg", path =>
    {
        var files = JpegExporter.Export(Pages.Select(p => p.Page).ToList(), path, _pdfs);
        return files.Count == 1 ? $"Saved {Path.GetFileName(files[0])}" : $"Saved {files.Count} JPEG files";
    });

    private void Save(string filter, string extension, Func<string, string> export)
    {
        var folder = _settings.OutputFolder;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        var path = _dialogs.AskSavePath(folder, $"Scan_{DateTime.Now:yyyy-MM-dd_HHmmss}{extension}", filter);
        if (path is null)
            return;

        string message;
        try
        {
            message = export(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or PdfSharp.PdfSharpException or Core.Pdfium.PdfOpenException)
        {
            _dialogs.ShowError($"Could not save the file.\n\n{ex.Message}");
            return;
        }

        _settings.OutputFolder = Path.GetDirectoryName(path)!;
        SaveSettings();
        _isDirty = false;
        TrySaveSession();
        LastSavedFile = path;
        SavedMessage = message;
    }

    [RelayCommand]
    private void OpenSavedFile()
    {
        var file = LastSavedFile;
        if (file is not null && File.Exists(file))
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenSavedFolder()
    {
        var file = LastSavedFile;
        if (file is null)
            return;
        if (File.Exists(file))
            Process.Start("explorer.exe", $"/select,\"{file}\"");
        else if (Path.GetDirectoryName(file) is { } dir && Directory.Exists(dir))
            Process.Start("explorer.exe", $"\"{dir}\"");
    }

    [RelayCommand]
    private void ShowAbout() => _dialogs.ShowAbout();

    // ---------------------------------------------------------------- shutdown

    /// <summary>Returns false to keep the window open. Unsaved pages stay in the session and are offered next start.</summary>
    public bool ConfirmClose()
    {
        if (!IsScanning && !Print.IsPrinting)
            return true;
        var job = IsScanning ? "A scan" : "A print job";
        if (!_dialogs.Confirm($"{job} is in progress. Cancel it and close PrinterDesk?", "Close"))
            return false;
        _scanCts?.Cancel();
        Print.CancelIfPrinting();
        return true;
    }

    public void Dispose()
    {
        _statusTimer.Stop();
        _elapsedTimer.Stop();
        _previewTimer.Stop();
        _scanCts?.Cancel();
        _client?.Dispose();
        Print.Dispose();
        _pdfs.Dispose();
    }

    private static string Plural(int count, string word) => count == 1 ? $"1 {word}" : $"{count} {word}s";
}
