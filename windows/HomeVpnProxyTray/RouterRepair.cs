using Renci.SshNet;

namespace HomeVpnProxyTray;

public sealed record RepairResult(bool Success, string Message);

/// <summary>
/// SSHes into the router to run the same real functional check the cron
/// healthcheck does (an actual HTTP request through the proxy, not just
/// a TCP-connect - the failure mode this exists for leaves the port open
/// but unable to complete a TLS handshake through it), and restarts the
/// service if needed.
/// </summary>
internal static class RouterRepair
{
    private const string ProxyUrl = "http://192.168.2.250:2080";
    private const string TestUrl = "http://example.com/";
    private const string CheckCommand =
        $"curl -s -o /dev/null -w '%{{http_code}}' --max-time 6 -x {ProxyUrl} {TestUrl}";

    public static Task<RepairResult> CheckAndFixAsync(RouterConnectionSettings settings) =>
        Task.Run(() => CheckAndFix(settings));

    private static RepairResult CheckAndFix(RouterConnectionSettings settings)
    {
        using var client = new SshClient(settings.Host, settings.Username, settings.Password);

        try
        {
            client.Connect();
        }
        catch (Exception ex)
        {
            return new RepairResult(false, $"Не удалось подключиться к роутеру ({settings.Host}): {ex.Message}");
        }

        try
        {
            var before = RunCheck(client);
            if (IsHealthy(before))
            {
                return new RepairResult(true, $"Прокси уже работает (HTTP {before}), перезапуск не потребовался.");
            }

            client.RunCommand("/etc/init.d/homevpn-proxy restart");
            Thread.Sleep(3000);

            var after = RunCheck(client);
            return IsHealthy(after)
                ? new RepairResult(true, "Прокси не отвечал - сервис перезапущен, сейчас работает.")
                : new RepairResult(false, "Перезапустил сервис, но прокси всё ещё не отвечает - нужна ручная диагностика на роутере.");
        }
        catch (Exception ex)
        {
            return new RepairResult(false, $"Ошибка при выполнении команд на роутере: {ex.Message}");
        }
        finally
        {
            client.Disconnect();
        }
    }

    private static string RunCheck(SshClient client) => client.RunCommand(CheckCommand).Result.Trim();

    private static bool IsHealthy(string httpCode) => httpCode is not ("000" or "");
}
