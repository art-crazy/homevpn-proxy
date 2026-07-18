using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace HomeVpnProxyTray;

public sealed record HealthSnapshot(
    bool ProxyReachable,
    bool CheckPointConnected,
    IReadOnlyList<string> TunneledDomains,
    string? Error);

internal static class HealthChecker
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(4) };

    // Shown in the UI next to the local check result, so it's obvious
    // exactly what got tested instead of just trusting a green/red dot.
    public static string DescribeLocalCheckCommand() =>
        $"curl -x http://{Constants.ProxyHost}:{Constants.ProxyPort} {Constants.HealthCheckTestUrl}";

    public static async Task<HealthSnapshot> RunAsync()
    {
        var proxyTask = ProbeProxyAsync();
        var domainsTask = FetchTunneledDomainsAsync();

        bool checkPointConnected;
        string? error = null;
        try
        {
            checkPointConnected = CheckPointConnected();
        }
        catch (Exception ex)
        {
            checkPointConnected = false;
            error = ex.Message;
        }

        bool proxyReachable;
        try
        {
            proxyReachable = await proxyTask;
        }
        catch
        {
            proxyReachable = false;
        }

        IReadOnlyList<string> domains;
        try
        {
            domains = await domainsTask;
        }
        catch
        {
            domains = Array.Empty<string>();
        }

        return new HealthSnapshot(proxyReachable, checkPointConnected, domains, error);
    }

    // A plain TCP-connect check isn't enough: the router's sing-box process
    // has been observed staying up and accepting connections on 2080 while
    // unable to actually complete a TLS handshake through the tunnel
    // (CONNECT succeeds, ClientHello gets reset). Only a real request
    // through the proxy proves it's actually working end to end.
    public static async Task<bool> ProbeProxyAsync()
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                Proxy = new System.Net.WebProxy($"http://{Constants.ProxyHost}:{Constants.ProxyPort}"),
                UseProxy = true,
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
            using var response = await client.GetAsync(Constants.HealthCheckTestUrl);
            return true; // any real HTTP response - even a non-2xx - proves the tunnel works
        }
        catch
        {
            return false;
        }
    }

    public static bool CheckPointConnected()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Any(nic =>
                nic.Description.Contains(Constants.CheckPointAdapterHint, StringComparison.OrdinalIgnoreCase) &&
                nic.OperationalStatus == OperationalStatus.Up);
    }

    private static async Task<IReadOnlyList<string>> FetchTunneledDomainsAsync()
    {
        var pacContent = await HttpClient.GetStringAsync(Constants.PacUrl);
        // Pulls the string literals out of dnsDomainIs(host, "example.com")
        // calls - good enough for our own hand-written PAC, not a general
        // JS parser.
        var matches = Regex.Matches(pacContent, "dnsDomainIs\\(\\s*host\\s*,\\s*\"([^\"]+)\"\\s*\\)");
        return matches.Select(m => m.Groups[1].Value).Distinct().OrderBy(s => s).ToList();
    }
}
