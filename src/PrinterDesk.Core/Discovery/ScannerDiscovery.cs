using Zeroconf;

namespace PrinterDesk.Core.Discovery;

public sealed record DiscoveredScanner(string Name, Uri BaseUri)
{
    public override string ToString() => $"{Name} ({BaseUri.Host})";

    /// <summary>Builds the eSCL root from what the user typed: an IP, a host name or a full URL.</summary>
    public static DiscoveredScanner FromAddress(string address)
    {
        address = address.Trim();
        var uri = address.Contains("://", StringComparison.Ordinal)
            ? new Uri(address)
            : new Uri($"http://{address}/eSCL/");
        return new DiscoveredScanner(uri.Host, uri);
    }
}

/// <summary>Finds eSCL scanners on the local network via mDNS (<c>_uscan._tcp</c>).</summary>
public static class ScannerDiscovery
{
    private const string Service = "_uscan._tcp.local.";

    public static async Task<IReadOnlyList<DiscoveredScanner>> FindAsync(TimeSpan scanTime, CancellationToken ct = default)
    {
        var hosts = await ZeroconfResolver.ResolveAsync(Service, scanTime, cancellationToken: ct).ConfigureAwait(false);
        var result = new List<DiscoveredScanner>();

        foreach (var host in hosts)
        {
            if (!host.Services.TryGetValue(Service, out var service))
                continue;

            var txt = service.Properties.FirstOrDefault() ?? new Dictionary<string, string>();
            var root = txt.TryGetValue("rs", out var rs) && !string.IsNullOrWhiteSpace(rs) ? rs.Trim('/') : "eSCL";
            var name = txt.TryGetValue("ty", out var ty) && !string.IsNullOrWhiteSpace(ty) ? ty : host.DisplayName;

            result.Add(new DiscoveredScanner(name, new Uri($"http://{host.IPAddress}:{service.Port}/{root}/")));
        }

        return result;
    }
}
