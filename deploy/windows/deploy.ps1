<#
.SYNOPSIS
  Installs a release on the Windows Server (IIS). Invoked over SSH by .github/workflows/deploy.yml.

.DESCRIPTION
  Layout:
    $SitePath                    IIS site physical path (live files)
    $Root\incoming\              uploaded release.zip + this script
    $Root\releases\<version>\    unpacked releases (last $Keep kept, used for rollback)
    $Root\shared\                files copied into every release (appsettings.Production.json)

  Steps: unpack -> add shared files -> app_offline.htm (ASP.NET Core Module stops the app and releases
  file locks) -> mirror the release into $SitePath -> remove app_offline.htm -> recycle the app pool.

.EXAMPLE
  # Rollback to a previous release (no package):
  .\deploy.ps1 -Version 20261124084210-cc77176
#>
param(
    [string] $Package,
    [Parameter(Mandatory)] [string] $Version,
    [string] $Root = 'C:\prenotaconme',
    [string] $SitePath = 'D:\IISSites\prenotaconme.rtres.net',
    [string] $AppPool = 'prenotaconme.rtres.net',
    [int] $Keep = 5
)

$ErrorActionPreference = 'Stop'
Import-Module WebAdministration

$releases = Join-Path $Root 'releases'
$shared = Join-Path $Root 'shared'
$release = Join-Path $releases $Version
$appOffline = Join-Path $SitePath 'app_offline.htm'

if ($Package) {
    Write-Host "Unpacking $Package -> $release"
    if (Test-Path $release) { Remove-Item $release -Recurse -Force }
    New-Item -ItemType Directory -Force $release | Out-Null
    Expand-Archive -Path $Package -DestinationPath $release
}
elseif (-not (Test-Path $release)) {
    throw "Release $Version not found in $releases and no -Package given."
}

if (Test-Path $shared) {
    Copy-Item (Join-Path $shared '*') $release -Recurse -Force
}
if (-not (Test-Path (Join-Path $release 'appsettings.Production.json'))) {
    Write-Warning "appsettings.Production.json missing: create it in $shared"
}

New-Item -ItemType Directory -Force $SitePath | Out-Null

Write-Host 'Taking app offline'
Set-Content -Path $appOffline -Value '<html><body><h1>Aggiornamento in corso / Actualizando…</h1></body></html>' -Encoding UTF8
Start-Sleep -Seconds 3

try {
    Write-Host "Mirroring $release -> $SitePath"
    # /MIR deletes files not in the release; /XF keeps app_offline.htm in place until we are done.
    robocopy $release $SitePath /MIR /XF app_offline.htm /R:5 /W:2 /NFL /NDL /NJH /NP | Out-Host
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
}
finally {
    Remove-Item $appOffline -Force -ErrorAction SilentlyContinue
}

Write-Host "Recycling app pool $AppPool"
Restart-WebAppPool -Name $AppPool

Get-ChildItem $releases -Directory |
    Sort-Object CreationTime -Descending |
    Select-Object -Skip $Keep |
    Remove-Item -Recurse -Force

if ($Package) { Remove-Item $Package -Force }
Write-Host "Deployed $Version"
exit 0
