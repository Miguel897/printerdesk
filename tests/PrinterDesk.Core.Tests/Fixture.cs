using System.Runtime.CompilerServices;

namespace PrinterDesk.Core.Tests;

internal static class Fixture
{
    /// <summary>Keeps test runs out of the real <c>%LOCALAPPDATA%\PrinterDesk\logs</c>.</summary>
    [ModuleInitializer]
    internal static void RedirectLog() =>
        Log.Folder = Path.Combine(Path.GetTempPath(), "PrinterDeskTests", "logs");

    public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string Text(string name) => File.ReadAllText(PathOf(name));

    public static byte[] Bytes(string name) => File.ReadAllBytes(PathOf(name));
}

/// <summary>Replies to HTTP requests from a script and records what was sent.</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpMethod Method, string Path)> Requests { get; } = [];

    public string? LastBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
        if (request.Content is not null)
            LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
        return respond(request);
    }
}

/// <summary>Temporary folder deleted at the end of a test.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PrinterDeskTests", Guid.NewGuid().ToString("N"));

    public TempFolder() => Directory.CreateDirectory(Path);

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
