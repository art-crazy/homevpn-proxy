using Renci.SshNet;

namespace HomeVpnProxyTray;

public enum RouterProxyStatus
{
    Unknown,
    Healthy,
    Unhealthy,
}

public sealed record RouterCheckResult(RouterProxyStatus Status, string Message);

/// <summary>
/// SSHes into the router to run the same real functional check the cron
/// healthcheck does (an actual HTTP request through the proxy, not just
/// a TCP-connect - the failure mode this exists for leaves the port open
/// but unable to complete a TLS handshake through it). Check and fix are
/// separate operations: CheckAsync only reports status, FixAsync restarts
/// the service and re-verifies - the UI only enables "Починить" once a
/// check has actually shown a problem.
/// </summary>
internal static class RouterRepair
{
    private const string ProxyUrl = "http://192.168.2.250:2080";
    private static readonly string CheckCommand =
        $"curl -s -o /dev/null -w '%{{http_code}}' --max-time 6 -x {ProxyUrl} {Constants.HealthCheckTestUrl}";

    public static Task<RouterCheckResult> CheckAsync(RouterConnectionSettings settings) =>
        Task.Run(() => Check(settings));

    public static Task<RouterCheckResult> FixAsync(RouterConnectionSettings settings) =>
        Task.Run(() => Fix(settings));

    // Shown in the UI so it's obvious exactly what ran on the router -
    // an actual runnable command line (as sshpass would take it), with
    // the password masked with a fixed placeholder in place rather than
    // its real value or its real length.
    public static string DescribeCheckCommand(RouterConnectionSettings settings) =>
        $"sshpass -p '••••••••••' ssh {settings.Username}@{settings.Host} \"{CheckCommand}\"";

    public static string DescribeFixCommand(RouterConnectionSettings settings) =>
        $"sshpass -p '••••••••••' ssh {settings.Username}@{settings.Host} \"/etc/init.d/homevpn-proxy restart && {CheckCommand}\"";

    private static RouterCheckResult Check(RouterConnectionSettings settings)
    {
        using var client = TryConnect(settings, out var connectError);
        if (client is null)
        {
            return new RouterCheckResult(RouterProxyStatus.Unknown, connectError!);
        }

        try
        {
            var code = RunCheck(client);
            if (IsHealthy(code))
            {
                return new RouterCheckResult(RouterProxyStatus.Healthy, $"Прокси: доступен на роутере (HTTP {code}).");
            }

            // Only gathered on failure - these are all the specific ways
            // this service has actually been observed silently breaking
            // while procd still reports it as running (veth knocked off
            // the bridge or its ARP entry going stale after a ZeroBlock
            // reload, the sing-box process itself missing). No point
            // paying for this on every healthy check.
            var diagnosis = DiagnoseService(client);
            return new RouterCheckResult(RouterProxyStatus.Unhealthy, $"Прокси: НЕ отвечает на роутере.\n{diagnosis}");
        }
        catch (Exception ex)
        {
            return new RouterCheckResult(RouterProxyStatus.Unknown, $"Ошибка проверки: {ex.Message}");
        }
        finally
        {
            client.Disconnect();
        }
    }

    private static string DiagnoseService(SshClient client)
    {
        string Run(string cmd) => client.RunCommand(cmd).Result.Trim();

        var procdStatus = Run("/etc/init.d/homevpn-proxy status");
        var processAlive = Run("pgrep -f 'homevpn-proxy/config.json' >/dev/null 2>&1 && echo да || echo нет");
        var netnsExists = Run("ip netns list 2>/dev/null | awk '{print $1}' | grep -qx homevpn && echo да || echo нет");
        var vethInBridge = Run("ip link show veth-hv0 2>/dev/null | grep -q 'master br-lan' && echo да || echo нет");
        var arpState = Run("ip neigh show 2>/dev/null | grep 192.168.2.250 | awk '{print $NF}'");

        return "Диагностика: "
            + $"procd={procdStatus}, "
            + $"процесс={processAlive}, "
            + $"netns={netnsExists}, "
            + $"veth в br-lan={vethInBridge}, "
            + $"ARP 192.168.2.250={(string.IsNullOrEmpty(arpState) ? "нет записи" : arpState)}";
    }

    private static RouterCheckResult Fix(RouterConnectionSettings settings)
    {
        using var client = TryConnect(settings, out var connectError);
        if (client is null)
        {
            return new RouterCheckResult(RouterProxyStatus.Unknown, connectError!);
        }

        try
        {
            client.RunCommand("/etc/init.d/homevpn-proxy restart");
            Thread.Sleep(3000);

            var code = RunCheck(client);
            return IsHealthy(code)
                ? new RouterCheckResult(RouterProxyStatus.Healthy, "Прокси: доступен на роутере (сервис перезапущен).")
                : new RouterCheckResult(RouterProxyStatus.Unhealthy, "Перезапустил сервис, но прокси всё ещё не отвечает на роутере - нужна ручная диагностика.");
        }
        catch (Exception ex)
        {
            return new RouterCheckResult(RouterProxyStatus.Unknown, $"Ошибка при перезапуске: {ex.Message}");
        }
        finally
        {
            client.Disconnect();
        }
    }

    private static SshClient? TryConnect(RouterConnectionSettings settings, out string? error)
    {
        var client = new SshClient(settings.Host, settings.Username, settings.Password);
        try
        {
            client.Connect();
            error = null;
            return client;
        }
        catch (Exception ex)
        {
            client.Dispose();
            error = $"Не удалось подключиться к роутеру ({settings.Host}): {ex.Message}";
            return null;
        }
    }

    private static string RunCheck(SshClient client) => client.RunCommand(CheckCommand).Result.Trim();

    private static bool IsHealthy(string httpCode) => httpCode is not ("000" or "");
}
