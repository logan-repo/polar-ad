<#
.SYNOPSIS
    Emergency fallback: resets every active network adapter's DNS server back to
    "obtained automatically" (DHCP). Use this if PolarAd's own restore (the
    "네트워크 설정 긴급 복구" button, or normal Exit) did not run — for example if
    the process was killed with Task Manager instead of exited normally.

.DESCRIPTION
    This script does NOT read PolarAd's saved backup file (%LocalAppData%\PolarAd\dns_backup.json).
    It unconditionally sets DNS back to automatic/DHCP on every adapter, which is
    correct for the vast majority of home/office setups. If you intentionally use
    static DNS servers (e.g. a custom internal resolver) note them down before
    running PolarAd the first time, because this script will remove them and you
    will need to re-enter them yourself.

.NOTES
    Must be run as Administrator (right-click -> "Run with PowerShell" from an
    elevated prompt, or: powershell -ExecutionPolicy Bypass -File restore-dns.ps1)
#>

$ErrorActionPreference = 'Stop'

$currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script must be run as Administrator. Re-run it from an elevated PowerShell window."
    exit 1
}

Write-Output "Resetting DNS to automatic (DHCP) on all active network adapters..."

$adapters = Get-DnsClientServerAddress -AddressFamily IPv4 | Select-Object -ExpandProperty InterfaceAlias -Unique

foreach ($alias in $adapters) {
    try {
        Write-Output "  - $alias"
        Set-DnsClientServerAddress -InterfaceAlias $alias -ResetServerAddresses
    }
    catch {
        Write-Warning "    Failed to reset '$alias': $($_.Exception.Message)"
    }
}

ipconfig /flushdns | Out-Null

Write-Output ""
Write-Output "Done. DNS has been reset to automatic on all adapters and the DNS cache was flushed."
Write-Output "If you use a static/internal DNS server intentionally, re-configure it now."
