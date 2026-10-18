using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

namespace PrinterDesk.Core.Escl;

/// <summary>
/// Minimal eSCL (AirScan) client: capabilities, status and scan jobs.
/// <paramref name="baseUri"/> is the eSCL root, e.g. <c>http://192.168.1.130/eSCL/</c>.
/// </summary>
public sealed class EsclClient : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PageTimeout = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;

    public EsclClient(Uri baseUri, HttpMessageHandler? handler = null)
    {
        BaseUri = baseUri.AbsoluteUri.EndsWith('/') ? baseUri : new Uri(baseUri.AbsoluteUri + "/");
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = Timeout.InfiniteTimeSpan; // per-request timeouts below
    }

    public Uri BaseUri { get; }

    /// <summary>Wait between NextDocument polls while the scanner is still busy (HTTP 503).</summary>
    public TimeSpan BusyRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    public async Task<ScannerCapabilities> GetCapabilitiesAsync(CancellationToken ct = default) =>
        ScannerCapabilities.Parse(await GetStringAsync("ScannerCapabilities", ct).ConfigureAwait(false));

    public async Task<ScannerStatus> GetStatusAsync(CancellationToken ct = default) =>
        ScannerStatus.Parse(await GetStringAsync("ScannerStatus", ct).ConfigureAwait(false));

    /// <summary>
    /// Runs a scan job and yields each page as it arrives. A feeder job yields every page
    /// in the feeder. Cancelling (or abandoning the enumeration) deletes the job on the scanner.
    /// </summary>
    public async IAsyncEnumerable<byte[]> ScanAsync(ScanRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var jobUri = await CreateJobAsync(request, ct).ConfigureAwait(false);
        var finished = false;
        try
        {
            while (true)
            {
                var page = await NextDocumentAsync(jobUri, ct).ConfigureAwait(false);
                if (page is null)
                    break;
                yield return page;
            }
            finished = true;
        }
        finally
        {
            if (!finished)
                await TryDeleteJobAsync(jobUri).ConfigureAwait(false);
        }
    }

    private async Task<Uri> CreateJobAsync(ScanRequest request, CancellationToken ct)
    {
        using var content = new StringContent(request.ToXml(), Encoding.UTF8, "text/xml");
        using var cts = Linked(ct, RequestTimeout);
        using var response = await SendAsync(() => _http.PostAsync(new Uri(BaseUri, "ScanJobs"), content, cts.Token), ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            throw new EsclException("The scanner is busy (it may be in use by another computer). Try again in a moment.");
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new EsclException("The scanner rejected the settings (HTTP 409). Try another resolution or color mode.");
        if (response.StatusCode != HttpStatusCode.Created || response.Headers.Location is null)
            throw new EsclException($"The scanner did not accept the scan job (HTTP {(int)response.StatusCode}).");

        var location = response.Headers.Location;
        return location.IsAbsoluteUri ? location : new Uri(BaseUri, location);
    }

    /// <returns>The next page, or null when the job has no more pages.</returns>
    private async Task<byte[]?> NextDocumentAsync(Uri jobUri, CancellationToken ct)
    {
        var next = new Uri(jobUri.AbsoluteUri.TrimEnd('/') + "/NextDocument");
        while (true)
        {
            using var cts = Linked(ct, PageTimeout);
            using var response = await SendAsync(() => _http.GetAsync(next, cts.Token), ct).ConfigureAwait(false);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
                case HttpStatusCode.NotFound:
                    return null;
                case HttpStatusCode.ServiceUnavailable:
                    await Task.Delay(BusyRetryDelay, ct).ConfigureAwait(false);
                    break;
                default:
                    throw new EsclException($"The scanner returned an error while sending a page (HTTP {(int)response.StatusCode}).");
            }
        }
    }

    private async Task TryDeleteJobAsync(Uri jobUri)
    {
        try
        {
            using var cts = new CancellationTokenSource(RequestTimeout);
            using var _ = await _http.DeleteAsync(jobUri, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Best effort: the scanner drops abandoned jobs on its own.
        }
    }

    private async Task<string> GetStringAsync(string path, CancellationToken ct)
    {
        using var cts = Linked(ct, RequestTimeout);
        using var response = await SendAsync(() => _http.GetAsync(new Uri(BaseUri, path), cts.Token), ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new EsclException($"The scanner answered {path} with HTTP {(int)response.StatusCode}.");
        return await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
    }

    /// <summary>Turns network failures and timeouts into <see cref="EsclException"/>; keeps user cancellation.</summary>
    private async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send, CancellationToken userToken)
    {
        try
        {
            return await send().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!userToken.IsCancellationRequested)
        {
            throw new EsclException($"The scanner at {BaseUri.Host} did not answer in time.");
        }
        catch (HttpRequestException ex)
        {
            throw new EsclException($"Cannot reach the scanner at {BaseUri.Host}: {ex.Message}", ex);
        }
    }

    private static CancellationTokenSource Linked(CancellationToken ct, TimeSpan timeout)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        return cts;
    }

    public void Dispose() => _http.Dispose();
}
