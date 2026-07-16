using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HomeVpnProxyTray;

/// <summary>
/// Applies/removes the two pieces of Windows-side config this whole
/// project relies on: the HTTP_PROXY/HTTPS_PROXY/ALL_PROXY user env vars
/// (for CLI tools) and the PAC AutoConfigURL (for browsers). Equivalent to
/// running set-proxy.ps1+set-pac.ps1 / unset-proxy.ps1+unset-pac.ps1.
/// </summary>
internal static class ProxyToggle
{
    private const int InternetOptionSettingsChanged = 39;
    private const int InternetOptionRefresh = 37;

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    public static bool IsEnabled()
    {
        var httpProxy = Environment.GetEnvironmentVariable("HTTP_PROXY", EnvironmentVariableTarget.User);
        var autoConfigUrl = GetAutoConfigUrl();
        return httpProxy == Constants.HttpProxyUrl && autoConfigUrl == Constants.PacUrl;
    }

    public static void Enable()
    {
        Environment.SetEnvironmentVariable("HTTP_PROXY", Constants.HttpProxyUrl, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("HTTPS_PROXY", Constants.HttpProxyUrl, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("ALL_PROXY", Constants.SocksProxyUrl, EnvironmentVariableTarget.User);

        using var key = Registry.CurrentUser.OpenSubKey(Constants.InternetSettingsRegPath, writable: true);
        key?.SetValue("AutoConfigURL", Constants.PacUrl, RegistryValueKind.String);

        RefreshWinInet();
    }

    public static void Disable()
    {
        Environment.SetEnvironmentVariable("HTTP_PROXY", null, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("HTTPS_PROXY", null, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("ALL_PROXY", null, EnvironmentVariableTarget.User);

        using var key = Registry.CurrentUser.OpenSubKey(Constants.InternetSettingsRegPath, writable: true);
        key?.DeleteValue("AutoConfigURL", throwOnMissingValue: false);

        RefreshWinInet();
    }

    private static string? GetAutoConfigUrl()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Constants.InternetSettingsRegPath, writable: false);
        return key?.GetValue("AutoConfigURL") as string;
    }

    private static void RefreshWinInet()
    {
        InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
    }
}
