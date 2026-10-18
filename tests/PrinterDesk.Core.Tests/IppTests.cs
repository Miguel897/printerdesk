using System.Net;
using PrinterDesk.Core.Ipp;

namespace PrinterDesk.Core.Tests;

public class IppMessageTests
{
    [Fact]
    public void Encode_and_decode_round_trip()
    {
        var request = IppMessage.Request(IppOperation.ValidateJob, new Uri("ipp://printer.test:631/ipp/print"), 7)
            .Add(IppTag.OperationAttributes, "requested-attributes", IppTag.Keyword, "sides-supported", "media-ready")
            .Add(IppTag.JobAttributes, "print-quality", IppTag.Enum, 5)
            .Add(IppTag.JobAttributes, "last-document", IppTag.Boolean, true);

        var decoded = IppMessage.Decode(request.Encode());

        Assert.Equal((ushort)IppOperation.ValidateJob, decoded.Code);
        Assert.Equal(7, decoded.RequestId);
        Assert.Equal("ipp://printer.test:631/ipp/print", decoded.String("printer-uri"));
        Assert.Equal(["sides-supported", "media-ready"], decoded.Strings("requested-attributes"));
        Assert.Equal(IppTag.JobAttributes, decoded.Find("print-quality")!.Group);
        Assert.Equal(5, decoded.Int("print-quality"));
        Assert.Equal([true], decoded.Find("last-document")!.Values);
    }

    [Fact]
    public void Real_printer_attributes_are_parsed()
    {
        // Get-Printer-Attributes answer of an HP ENVY 6432e (only the attributes PrinterDesk requests).
        var caps = PrinterCapabilities.Parse(IppMessage.Decode(Fixture.Bytes("printer-attributes.bin")));

        Assert.Equal("HP ENVY 6400 series", caps.MakeAndModel);
        Assert.True(caps.SupportsPwgRaster);
        Assert.Equal(["one-sided", "two-sided-short-edge", "two-sided-long-edge"], caps.Sides);
        Assert.Contains("monochrome", caps.ColorModes);
        Assert.Contains("iso_a4_210x297mm", caps.Media);
        Assert.Equal("iso_a4_210x297mm", caps.MediaReady);
        Assert.Equal([3, 4, 5], caps.Qualities);
        Assert.Equal(99, caps.MaxCopies);
        Assert.Equal([300, 600], caps.RasterResolutions);
        Assert.Contains("srgb_8", caps.RasterTypes);
        Assert.Equal("rotated", caps.SheetBack);
        Assert.Equal(new Margins(296, 296, 296, 296), caps.Margins);
        Assert.Equal(["tri-color cartridge", "black cartridge"], caps.Status.Markers.Select(m => m.Name));
        Assert.All(caps.Status.Markers, m => Assert.InRange(m.Level, 0, 100));
        Assert.Equal("Ready", caps.Status.Describe());
    }

    [Fact]
    public void Collections_are_skipped()
    {
        byte[] data =
        [
            2, 0, 0, 0, 0, 0, 0, 1,
            0x04,
            0x34, 0, 9, .."media-col"u8, 0, 0,          // begin collection
            0x4A, 0, 0, 0, 10, .."media-size"u8,        //   member name
            0x34, 0, 0, 0, 0,                           //   nested collection
            0x37, 0, 0, 0, 0,                           //   end nested
            0x37, 0, 0, 0, 0,                           // end collection
            0x44, 0, 5, .."sides"u8, 0, 9, .."one-sided"u8,
            0x03,
        ];

        var message = IppMessage.Decode(data);

        Assert.Equal([null], message.Find("media-col")!.Values);
        Assert.Equal("sides", message.Attributes[1].Name);
    }

    [Theory]
    [InlineData(new byte[] { 2, 0 })]                                          // header cut short
    [InlineData(new byte[] { 2, 0, 0, 0, 0, 0, 0, 1, 0x04, 0x44, 0, 9, 1 })]   // name longer than the data
    [InlineData(new byte[] { 2, 0, 0, 0, 0, 0, 0, 1, 0x04, 0x21, 0, 1, 0x61, 0, 2, 0, 1, 0x03 })] // 2-byte integer
    [InlineData(new byte[] { 2, 0, 0, 0, 0, 0, 0, 1, 0x04, 0x44, 0, 0, 0, 1, 0x61, 0x03 })]       // value without a name
    public void Malformed_data_is_rejected(byte[] data) =>
        Assert.Throws<IppException>(() => IppMessage.Decode(data));

    [Fact]
    public void Deeply_nested_collections_are_rejected()
    {
        var data = new List<byte> { 2, 0, 0, 0, 0, 0, 0, 1, 0x04, 0x34, 0, 1, (byte)'c', 0, 0 };
        for (var i = 0; i < 20; i++)
            data.AddRange([0x34, 0, 0, 0, 0]);

        Assert.Throws<IppException>(() => IppMessage.Decode(data.ToArray()));
    }
}

public class IppClientTests
{
    private static readonly Uri Printer = new("ipp://printer.test:631/ipp/print");

    private static HttpResponseMessage Reply(IppMessage message) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(message.Encode()) };

    private static IppMessage Ok(Action<IppMessage>? fill = null)
    {
        var message = new IppMessage { Code = 0, RequestId = 1 };
        message.Add(IppTag.OperationAttributes, "attributes-charset", IppTag.Charset, "utf-8");
        fill?.Invoke(message);
        return message;
    }

    [Fact]
    public void Http_uri_uses_port_631()
    {
        using var client = new IppClient(IppClient.UriForHost("192.168.1.130"));

        Assert.Equal("http://192.168.1.130:631/ipp/print", client.HttpUri.AbsoluteUri);
    }

    [Fact]
    public async Task Create_send_and_poll_a_job()
    {
        var operations = new List<IppOperation>();
        byte[]? sent = null;
        using var client = new IppClient(Printer, new FakeHandler(request =>
        {
            var body = request.Content!.ReadAsByteArrayAsync().Result;
            var ipp = IppMessage.Decode(body);
            operations.Add((IppOperation)ipp.Code);
            switch ((IppOperation)ipp.Code)
            {
                case IppOperation.CreateJob:
                    Assert.Equal("two-sided-long-edge", ipp.String("sides"));
                    return Reply(Ok(m => m.Add(IppTag.JobAttributes, "job-id", IppTag.Integer, 42)));
                case IppOperation.SendDocument:
                    sent = body;
                    Assert.Equal(42, ipp.Int("job-id"));
                    return Reply(Ok());
                default:
                    return Reply(Ok(m => m
                        .Add(IppTag.JobAttributes, "job-state", IppTag.Enum, PrintJobStatus.Completed)
                        .Add(IppTag.JobAttributes, "job-state-reasons", IppTag.Keyword, "job-completed-successfully")
                        .Add(IppTag.JobAttributes, "job-impressions-completed", IppTag.Integer, 2)));
            }
        }));

        var ticket = new PrintTicket { Sides = "two-sided-long-edge" };
        var id = await client.CreateJobAsync(ticket);
        await client.SendDocumentAsync(id, new MemoryStream([1, 2, 3]), PrinterCapabilities.PwgRaster);
        var status = await client.GetJobAsync(id);

        Assert.Equal([IppOperation.CreateJob, IppOperation.SendDocument, IppOperation.GetJobAttributes], operations);
        Assert.Equal([1, 2, 3], sent![^3..]); // document follows the IPP header
        Assert.True(status.IsFinished);
        Assert.Equal(2, status.ImpressionsCompleted);
    }

    [Fact]
    public async Task Unsupported_settings_are_reported()
    {
        using var client = new IppClient(Printer, new FakeHandler(_ => Reply(Ok(m =>
        {
            m.Attributes.Add(new IppAttribute(IppTag.UnsupportedAttributes, "orientation-requested", IppTag.Enum, [4]));
        }))));

        var ex = await Assert.ThrowsAsync<IppException>(() => client.ValidateJobAsync(new PrintTicket()));
        Assert.Contains("orientation-requested", ex.Message);
    }

    [Fact]
    public async Task Error_status_becomes_a_readable_message()
    {
        var error = new IppMessage { Code = 0x0507, RequestId = 1 };
        error.Add(IppTag.OperationAttributes, "status-message", IppTag.TextWithoutLanguage, "printer busy");
        using var client = new IppClient(Printer, new FakeHandler(_ => Reply(error)));

        var ex = await Assert.ThrowsAsync<IppException>(() => client.GetCapabilitiesAsync());
        Assert.Equal("The printer is busy. Try again in a moment: printer busy.", ex.Message);
    }

    [Fact]
    public async Task Http_error_is_reported()
    {
        using var client = new IppClient(Printer, new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var ex = await Assert.ThrowsAsync<IppException>(() => client.GetStatusAsync());
        Assert.Contains("HTTP 404", ex.Message);
    }

    [Fact]
    public void Printer_problems_are_described_in_plain_words()
    {
        var status = PrinterStatus.Parse(Ok(m => m
            .Add(IppTag.PrinterAttributes, "printer-state", IppTag.Enum, PrinterStatus.Stopped)
            .Add(IppTag.PrinterAttributes, "printer-state-reasons", IppTag.Keyword, "media-empty-error", "marker-supply-low-warning")));

        Assert.Equal(["media-empty", "marker-supply-low"], status.Reasons);
        Assert.Equal("Out of paper, Ink low", status.Describe());
        Assert.Equal("Out of paper", status.Problem);
    }
}
