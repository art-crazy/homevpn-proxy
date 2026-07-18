namespace HomeVpnProxyTray;

internal static class Constants
{
    public const string ProxyHost = "192.168.2.250";
    public const int ProxyPort = 2080;

    public const string HttpProxyUrl = "http://192.168.2.250:2080";
    public const string SocksProxyUrl = "socks5://192.168.2.250:2080";
    public const string PacUrl = "http://192.168.2.1/homevpn-proxy.pac";

    // Has to be a domain that's actually in ZeroBlock's tunneled list and
    // goes through the VPN outbound - testing an unrelated direct-out
    // domain means a hiccup on the *direct* internet path reads as "proxy
    // is broken" even though the VPN path Claude/ChatGPT actually use is
    // fine. A lightweight static asset, not the main page, to go easy on
    // bot-detection/rate-limiting on a repeatedly-hit shared VPN exit IP.
    public const string HealthCheckTestUrl = "https://chatgpt.com/favicon.ico";

    public const string InternetSettingsRegPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    public const string RunRegPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string AutoStartValueName = "HomeVpnProxyTray";

    public const string CheckPointAdapterHint = "Check Point";

    public const int HealthCheckIntervalMs = 45_000;
    public const int TcpProbeTimeoutMs = 2_000;
}
