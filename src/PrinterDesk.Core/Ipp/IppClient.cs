using System.Net;
using System.Net.Http.Headers;

namespace PrinterDesk.Core.Ipp;

/// <summary>Job settings sent with Validate-Job and Create-Job, as IPP keywords.</summary>
public sealed record PrintTicket
{
    public string JobName { get; init; } = "PrinterDesk";
    public string DocumentFormat { get; init; } = PrinterCapabilities.PwgRaster;
    public string Sides { get; init; } = "one-sided";
    public string ColorMode { get; init; } = "color";
    public string? Media { get; init; }
    public int? Quality { get; init; }

    internal void AddTo(IppMessage message, bool withFormat)
    {
        message.Add(IppTag.OperationAttributes, "job-name", IppTag.NameWithoutLanguage, JobName);
        if (withFormat)
            message.Add(IppTag.OperationAttributes, "document-format", IppTag.MimeMediaType, DocumentFormat);
        message.Add(IppTag.JobAttributes, "sides", IppTag.Keyword, Sides);
        message.Add(IppTag.JobAttributes, "print-color-mode", IppTag.Keyword, ColorMode);
        if (Media is not null)
            message.Add(IppTag.JobAttributes, "media", IppTag.Keyword, Media);
        if (Quality is { } quality)
            message.Add(IppTag.JobAttributes, "print-quality", IppTag.Enum, quality);
    }
}

public sealed record PrintJobStatus(int JobId, int State, IReadOnlyList<string> Reasons, int ImpressionsCompleted)
{
    public const int Pending = 3;
    public const int PendingHeld = 4;
    public const int Processing = 5;
    public const int ProcessingStopped = 6;
    public const int Canceled = 7;
    public const int Aborted = 8;
    public const int Completed = 9;

    public bool IsFinished => State >= Canceled;
}

/// <summary>
/// Minimal IPP/2.0 client over HTTP for one printer, e.g. <c>ipp://192.168.1.130:631/ipp/print</c>.
/// </summary>
public sealed class IppClient : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>The printer reads the document as it prints, so sending can take as long as printing.</summary>
    private static readonly TimeSpan SendTimeout = TimeSpan.FromMinutes(30);

    private const int MaxResponseBytes = 4 * 1024 * 1024;

    private readonly HttpClient _http;
    private int _requestId;

    public IppClient(Uri printerUri, HttpMessageHandler? handler = null)
    {
        PrinterUri = printerUri;
        HttpUri = new UriBuilder(printerUri)
        {
            Scheme = printerUri.Scheme == "ipps" ? "https" : "http",
            Port = printerUri.IsDefaultPort || printerUri.Port < 0 ? 631 : printerUri.Port,
        }.Uri;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = Timeout.InfiniteTimeSpan; // per-request timeouts below
    }

    /// <summary>The usual IPP Everywhere / AirPrint path on a host.</summary>
    public static Uri UriForHost(string host) => new($"ipp://{host}:631/ipp/print");

    public Uri PrinterUri { get; }

    public Uri HttpUri { get; }

    public async Task<PrinterCapabilities> GetCapabilitiesAsync(CancellationToken ct = default) =>
        PrinterCapabilities.Parse(await GetPrinterAttributesAsync(PrinterCapabilities.RequestedAttributes, ct).ConfigureAwait(false));

    public async Task<PrinterStatus> GetStatusAsync(CancellationToken ct = default) =>
        PrinterStatus.Parse(await GetPrinterAttributesAsync(
            ["printer-state", "printer-state-reasons", "printer-is-accepting-jobs",
             "marker-names", "marker-colors", "marker-levels", "marker-low-levels"], ct).ConfigureAwait(false));

    private Task<IppMessage> GetPrinterAttributesAsync(string[] names, CancellationToken ct)
    {
        var request = NewRequest(IppOperation.GetPrinterAttributes);
        request.Add(IppTag.OperationAttributes, "requested-attributes", IppTag.Keyword, names);
        return SendAsync(request, null, ct);
    }

    /// <summary>Asks the printer whether it accepts these settings, without printing anything.</summary>
    public async Task ValidateJobAsync(PrintTicket ticket, CancellationToken ct = default)
    {
        var request = NewRequest(IppOperation.ValidateJob);
        ticket.AddTo(request, withFormat: true);
        var response = await SendAsync(request, null, ct).ConfigureAwait(false);
        ThrowIfUnsupported(response);
    }

    /// <returns>The new job's id.</returns>
    public async Task<int> CreateJobAsync(PrintTicket ticket, CancellationToken ct = default)
    {
        var request = NewRequest(IppOperation.CreateJob);
        ticket.AddTo(request, withFormat: false);
        var response = await SendAsync(request, null, ct).ConfigureAwait(false);
        ThrowIfUnsupported(response);
        return response.Int("job-id") ?? throw new IppException("The printer did not return a job number.");
    }

    /// <summary>Sends the job's only document. <paramref name="progress"/> receives the bytes sent so far.</summary>
    public async Task SendDocumentAsync(int jobId, Stream document, string format, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        var request = NewRequest(IppOperation.SendDocument);
        request.Add(IppTag.OperationAttributes, "job-id", IppTag.Integer, jobId);
        request.Add(IppTag.OperationAttributes, "document-format", IppTag.MimeMediaType, format);
        request.Add(IppTag.OperationAttributes, "last-document", IppTag.Boolean, true);
        await SendAsync(request, new DocumentContent(request.Encode(), document, progress), ct, SendTimeout).ConfigureAwait(false);
    }

    public async Task<PrintJobStatus> GetJobAsync(int jobId, CancellationToken ct = default)
    {
        var request = NewRequest(IppOperation.GetJobAttributes);
        request.Add(IppTag.OperationAttributes, "job-id", IppTag.Integer, jobId);
        request.Add(IppTag.OperationAttributes, "requested-attributes", IppTag.Keyword,
            "job-state", "job-state-reasons", "job-impressions-completed");
        var response = await SendAsync(request, null, ct).ConfigureAwait(false);
        return new PrintJobStatus(
            jobId,
            response.Int("job-state") ?? PrintJobStatus.Pending,
            PrinterStatus.CleanReasons(response.Strings("job-state-reasons")),
            response.Int("job-impressions-completed") ?? 0);
    }

    /// <summary>Best effort: a job that already finished cannot be cancelled.</summary>
    public async Task CancelJobAsync(int jobId)
    {
        var request = NewRequest(IppOperation.CancelJob);
        request.Add(IppTag.OperationAttributes, "job-id", IppTag.Integer, jobId);
        try
        {
            await SendAsync(request, null, CancellationToken.None).ConfigureAwait(false);
        }
        catch (IppException ex)
        {
            Log.Info($"Cancel-Job {jobId}: {ex.Message}");
        }
    }

    private IppMessage NewRequest(IppOperation operation) =>
        IppMessage.Request(operation, PrinterUri, Interlocked.Increment(ref _requestId));

    /// <summary>Settings the printer ignored or replaced come back in the unsupported-attributes group.</summary>
    private static void ThrowIfUnsupported(IppMessage response)
    {
        var unsupported = response.Attributes.Where(a => a.Group == IppTag.UnsupportedAttributes).Select(a => a.Name).ToList();
        if (unsupported.Count > 0)
            throw new IppException($"The printer does not support these settings: {string.Join(", ", unsupported)}.");
    }

    private async Task<IppMessage> SendAsync(IppMessage request, HttpContent? content, CancellationToken ct, TimeSpan? timeout = null)
    {
        content ??= new ByteArrayContent(request.Encode());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/ipp");
        using var _ = content;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? RequestTimeout);

        byte[] body;
        try
        {
            using var response = await _http.PostAsync(HttpUri, content, cts.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new IppException($"The printer answered with HTTP {(int)response.StatusCode}.");
            body = await ReadLimitedAsync(response.Content, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IppException($"The printer at {HttpUri.Host} did not answer in time.");
        }
        catch (HttpRequestException ex)
        {
            throw new IppException($"Cannot reach the printer at {HttpUri.Host}: {ex.Message}", ex);
        }

        var message = IppMessage.Decode(body);
        if (message.Code >= 0x0400)
            throw new IppException(DescribeStatus(message.Code, message.String("status-message")));
        return message;
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
                throw new IppException("The printer's answer is too large.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    internal static string DescribeStatus(int code, string? statusMessage)
    {
        var text = code switch
        {
            0x0400 => "The printer rejected the request",
            0x0401 => "The printer refused the request (forbidden)",
            0x0402 or 0x0403 => "The printer requires a password, which PrinterDesk does not support",
            0x0406 => "The print job no longer exists on the printer",
            0x040A => "The printer does not accept this document format",
            0x040B => "The printer does not support these settings",
            0x0503 or 0x0507 => "The printer is busy. Try again in a moment",
            0x0506 => "The printer is not accepting jobs right now",
            _ => $"The printer reported an error (IPP status 0x{code:X4})",
        };
        return string.IsNullOrWhiteSpace(statusMessage) ? text + "." : $"{text}: {statusMessage}.";
    }

    public void Dispose() => _http.Dispose();

    /// <summary>The IPP request followed by the document, streamed without loading it into memory.</summary>
    private sealed class DocumentContent(byte[] header, Stream document, IProgress<long>? progress) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            var buffer = new byte[64 * 1024];
            long sent = 0;
            int read;
            while ((read = await document.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                sent += read;
                progress?.Report(sent);
            }
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override bool TryComputeLength(out long length)
        {
            length = header.Length + (document.CanSeek ? document.Length - document.Position : 0);
            return document.CanSeek;
        }
    }
}
