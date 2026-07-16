# Removes the HTTP_PROXY/HTTPS_PROXY/ALL_PROXY user env vars set by
# set-proxy.ps1. Full rollback of the Windows-side change.

[Environment]::SetEnvironmentVariable("HTTP_PROXY", $null, "User")
[Environment]::SetEnvironmentVariable("HTTPS_PROXY", $null, "User")
[Environment]::SetEnvironmentVariable("ALL_PROXY", $null, "User")

Write-Host "Proxy env vars removed. Restart terminals/apps to pick this up."
