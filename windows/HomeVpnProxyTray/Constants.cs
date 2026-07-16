namespace HomeVpnProxyTray;

internal static class Constants
{
    public const string ProxyHost = "192.168.2.250";
    public const int ProxyPort = 2080;

    public const string HttpProxyUrl = "http://192.168.2.250:2080";
    public const string SocksProxyUrl = "socks5://192.168.2.250:2080";
    public const string PacUrl = "http://192.168.2.1/homevpn-proxy.pac";

    public const string InternetSettingsRegPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    public const string RunRegPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string AutoStartValueName = "HomeVpnProxyTray";

    public const string CheckPointAdapterHint = "Check Point";

    public const int HealthCheckIntervalMs = 45_000;
    public const int TcpProbeTimeoutMs = 2_000;
}
