<#
.SYNOPSIS
  Installs a release package on the Windows Server (IIS). Invoked over SSH by .github/workflows/deploy.yml.

.DESCRIPTION
  Layout under $Root (default C:\prenotaconme):
    incoming\            uploaded release.zip
    releases\<version>\  unpacked releases (last $Keep kept)
    current\             directory junction -> active release (IIS site physical path)
    shared\              files copied into every release (appsettings.Production.json)

  Swapping the junction and recycling the app pool gives a near-zero-downtime deploy and an easy rollback
  (point the junction at a previous release and recycle).
#>
param(
    [Parameter(Mandatory)] [string] $Package,
    [Parameter(Mandatory)] [string] $Version,
    [string] $Root = 'C:\prenotaconme',
    [string] $AppPool = 'prenotaconme',
    [int] $Keep = 5
)

$ErrorActionPreference = 'Stop'
Import-Module WebAdministration

$releases = Join-Path $Root 'releases'
$current = Join-Path $Root 'current'
$shared = Join-Path $Root 'shared'
$target = Join-Path $releases $Version

Write-Host "Unpacking $Package -> $target"
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Force $target | Out-Null
Expand-Archive -Path $Package -DestinationPath $target

if (Test-Path $shared) {
    Copy-Item (Join-Path $shared '*') $target -Recurse -Force
}

Write-Host "Switching $current -> $target"
if (Test-Path $current) {
    # Removes only the junction, not the release it points to.
    (Get-Item $current).Delete()
}
New-Item -ItemType Junction -Path $current -Target $target | Out-Null

Write-Host "Recycling app pool $AppPool"
Restart-WebAppPool -Name $AppPool

Get-ChildItem $releases -Directory |
    Sort-Object CreationTime -Descending |
    Select-Object -Skip $Keep |
    Remove-Item -Recurse -Force

Remove-Item $Package -Force
Write-Host "Deployed $Version"
