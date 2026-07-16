using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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

    private static async Task<bool> ProbeProxyAsync()
    {
        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(Constants.ProxyHost, Constants.ProxyPort);
        var completed = await Task.WhenAny(connectTask, Task.Delay(Constants.TcpProbeTimeoutMs));
        return completed == connectTask && client.Connected;
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
