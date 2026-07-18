using Renci.SshNet;

namespace HomeVpnProxyTray;

public enum RouterProxyStatus
{
    Unknown,
    Healthy,
    Unhealthy,
}

/// <summary>
/// One SSH connection reused across a whole check/diagnose/fix sequence
/// (opening a fresh connection per command would be slow and pointless -
/// same session for all of it). Exposes the raw remote commands as public
/// constants so the UI can log "$ command" / "→ result" for each step as
/// it runs, instead of hiding everything behind one opaque result.
/// </summary>
internal sealed class RouterSshSession : IDisposable
{
    public const string ProxyUrl = "http://192.168.2.250:2080";
    public static readonly string CheckCommand =
        $"curl -s -o /dev/null -w '%{{http_code}}' --max-time 6 -x {ProxyUrl} {Constants.HealthCheckTestUrl}";

    public const string ProcdStatusCommand = "/etc/init.d/homevpn-proxy status";
    public const string ProcessCheckCommand = "pgrep -f 'homevpn-proxy/config.json' >/dev/null 2>&1 && echo да || echo нет";
    public const string NetnsCheckCommand = "ip netns list 2>/dev/null | awk '{print $1}' | grep -qx homevpn && echo да || echo нет";
    public const string VethCheckCommand = "ip link show veth-hv0 2>/dev/null | grep -q 'master br-lan' && echo да || echo нет";
    public const string ArpCheckCommand = "ip neigh show 2>/dev/null | grep 192.168.2.250 | awk '{print $NF}'";
    public const string RestartCommand = "/etc/init.d/homevpn-proxy restart";

    private readonly SshClient _client;

    private RouterSshSession(SshClient client) => _client = client;

    public static Task<(RouterSshSession? Session, string? Error)> ConnectAsync(RouterConnectionSettings settings) =>
        Task.Run(() =>
        {
            var client = new SshClient(settings.Host, settings.Username, settings.Password);
            try
            {
                client.Connect();
                return (new RouterSshSession(client), (string?)null);
            }
            catch (Exception ex)
            {
                client.Dispose();
                return ((RouterSshSession?)null, $"не удалось подключиться к роутеру ({settings.Host}): {ex.Message}");
            }
        });

    public Task<string> RunAsync(string command) =>
        Task.Run(() => _client.RunCommand(command).Result.Trim());

    public static bool IsHealthyHttpCode(string httpCode) => httpCode is not ("000" or "");

    // As it would actually be typed at a terminal (sshpass, since that's
    // the standard non-interactive way to hand ssh a password) - the
    // password itself is masked with a fixed-length placeholder, shown
    // in place rather than omitted or described separately.
    public static string Describe(RouterConnectionSettings settings, string remoteCommand) =>
        $"sshpass -p '••••••••••' ssh {settings.Username}@{settings.Host} \"{remoteCommand}\"";

    public void Dispose()
    {
        _client.Disconnect();
        _client.Dispose();
    }
}
