# Points Windows' "Automatic proxy setup" at the homevpn-proxy PAC file
# hosted on the router (http://192.168.2.1/homevpn-proxy.pac). Chrome/Edge
# (and anything else using WinINet system proxy settings) will pick this
# up: claude.ai/anthropic.com/claude.com go through the LAN proxy, every
# other site goes direct - so corporate/other browsing is unaffected.

$PacUrl = "http://192.168.2.1/homevpn-proxy.pac"
$RegPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings"

Set-ItemProperty -Path $RegPath -Name AutoConfigURL -Value $PacUrl

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class WinInet {
    [DllImport("wininet.dll", SetLastError = true)]
    public static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
}
"@ -ErrorAction SilentlyContinue

$INTERNET_OPTION_SETTINGS_CHANGED = 39
$INTERNET_OPTION_REFRESH = 37
[WinInet]::InternetSetOption([IntPtr]::Zero, $INTERNET_OPTION_SETTINGS_CHANGED, [IntPtr]::Zero, 0) | Out-Null
[WinInet]::InternetSetOption([IntPtr]::Zero, $INTERNET_OPTION_REFRESH, [IntPtr]::Zero, 0) | Out-Null

Write-Host "PAC script set to: $PacUrl"
Write-Host "Restart the browser to be safe (Chrome/Edge usually pick it up live, but a restart guarantees it)."
