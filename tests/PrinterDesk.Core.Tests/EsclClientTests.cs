using System.Net;
using PrinterDesk.Core.Escl;

namespace PrinterDesk.Core.Tests;

public class EsclClientTests
{
    private static readonly Uri Base = new("http://scanner.test/eSCL");
    private static readonly ScanRequest Request = new(EsclSource.Feeder, 150, "RGB24");

    private static HttpResponseMessage Created() => new(HttpStatusCode.Created)
    {
        Headers = { Location = new Uri("/eSCL/ScanJobs/job1", UriKind.Relative) },
    };

    private static HttpResponseMessage Page(byte b) => new(HttpStatusCode.OK) { Content = new ByteArrayContent([b]) };

    private static EsclClient Client(FakeHandler handler) => new(Base, handler) { BusyRetryDelay = TimeSpan.Zero };

    [Fact]
    public async Task Feeder_job_returns_every_page_and_retries_while_busy()
    {
        var replies = new Queue<HttpResponseMessage>(
        [
            Created(),
            new(HttpStatusCode.ServiceUnavailable),
            Page(1),
            new(HttpStatusCode.ServiceUnavailable),
            Page(2),
            Page(3),
            new(HttpStatusCode.NotFound),
        ]);
        var handler = new FakeHandler(_ => replies.Dequeue());
        using var client = Client(handler);

        var pages = new List<byte[]>();
        await foreach (var page in client.ScanAsync(Request))
            pages.Add(page);

        Assert.Equal([1, 2, 3], pages.Select(p => p[0]));
        Assert.Equal((HttpMethod.Post, "/eSCL/ScanJobs"), handler.Requests[0]);
        Assert.All(handler.Requests.Skip(1), r => Assert.Equal("/eSCL/ScanJobs/job1/NextDocument", r.Path));
        Assert.Contains("<pwg:InputSource>Feeder</pwg:InputSource>", handler.LastBody);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task Busy_scanner_is_reported()
    {
        using var client = Client(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var ex = await Assert.ThrowsAsync<EsclException>(async () =>
        {
            await foreach (var _ in client.ScanAsync(Request)) { }
        });
        Assert.Contains("busy", ex.Message);
    }

    [Fact]
    public async Task Cancelling_deletes_the_job_and_keeps_received_pages()
    {
        using var cts = new CancellationTokenSource();
        var handler = new FakeHandler(r => r.Method == HttpMethod.Post ? Created()
            : r.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.OK)
            : Page(1));
        using var client = Client(handler);

        var pages = new List<byte[]>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var page in client.ScanAsync(Request, cts.Token))
            {
                pages.Add(page);
                cts.Cancel();
            }
        });

        Assert.Single(pages);
        Assert.Contains((HttpMethod.Delete, "/eSCL/ScanJobs/job1"), handler.Requests);
    }

    [Fact]
    public async Task Unreachable_scanner_gives_readable_error()
    {
        using var client = Client(new FakeHandler(_ => throw new HttpRequestException("No route to host")));

        var ex = await Assert.ThrowsAsync<EsclException>(() => client.GetStatusAsync());
        Assert.Contains("scanner.test", ex.Message);
    }

    [Fact]
    public async Task Reads_status()
    {
        using var client = Client(new FakeHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixture.Text("ScannerStatus.xml")) }));

        var status = await client.GetStatusAsync();

        Assert.True(status.IsIdle);
    }
}
