# Sets HTTP_PROXY/HTTPS_PROXY/ALL_PROXY for the current user, pointing at
# the homevpn-proxy running on the router (192.168.2.250:2080).
#
# NOTE: these are USER-level env vars - every new process that reads them
# (not just Claude Code / Codex CLI, but also git, npm, curl, etc.) will
# start using this proxy. If you only want specific tools to use it,
# set $env:HTTP_PROXY etc. in a single terminal session instead of running
# this script (session-scoped, gone when you close the terminal).

$ProxyAddr = "http://192.168.2.250:2080"
$SocksAddr = "socks5://192.168.2.250:2080"

[Environment]::SetEnvironmentVariable("HTTP_PROXY", $ProxyAddr, "User")
[Environment]::SetEnvironmentVariable("HTTPS_PROXY", $ProxyAddr, "User")
[Environment]::SetEnvironmentVariable("ALL_PROXY", $SocksAddr, "User")

Write-Host "Proxy env vars set:"
Write-Host "  HTTP_PROXY  = $ProxyAddr"
Write-Host "  HTTPS_PROXY = $ProxyAddr"
Write-Host "  ALL_PROXY   = $SocksAddr"
Write-Host ""
Write-Host "Restart terminals / IDEs / Claude Code / Codex CLI to pick these up."
