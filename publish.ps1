<#
.SYNOPSIS
    Builds a ready-to-run copy of RPL Manager that needs nothing installed (no .NET, no Windows App SDK).

.DESCRIPTION
    Publishes App\ as a self-contained win-x64 app to dist\RPLManager, then
      - installs it for this Windows user (Start menu, desktop, Settings > Apps; skip with -NoInstall), and
      - zips the folder to dist\RPLManager.zip for other committee members (skip with -NoZip).
    On another PC: unzip anywhere, double-click Install.cmd. Uninstall from Settings > Apps.
    Generating files there still needs Excel (.xlsx), Word (.docx) and Edge (PDFs).
#>
param(
    [string]$Out = (Join-Path $PSScriptRoot 'dist\RPLManager'),
    [switch]$NoInstall,
    [switch]$NoZip
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'App\RPL.App.csproj'

Write-Host "Publishing to $Out ..."
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true -o $Out --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }
$exe = Join-Path $Out 'RPLManager.exe'
if (-not (Test-Path $exe)) { throw "Publish finished but $exe is missing." }

# The installer travels with the app: Install.cmd on another PC installs it for that user.
Copy-Item (Join-Path $PSScriptRoot 'Installer\Install.ps1'), (Join-Path $PSScriptRoot 'Installer\Install.cmd') $Out

if (-not $NoInstall) {
    # Install on this PC too (Start menu, desktop, Settings > Apps); replaces the old desktop shortcut.
    & (Join-Path $Out 'Install.ps1')
}
if (-not $NoZip) {
    $zip = Join-Path (Split-Path $Out) 'RPLManager.zip'
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path (Join-Path $Out '*') -DestinationPath $zip
    Write-Host ("Zip for other PCs: {0} ({1:N0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
}
Write-Host "Done: $exe"
