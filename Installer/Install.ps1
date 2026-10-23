<#
.SYNOPSIS
    Installs RPL Manager for the current Windows user (no admin rights needed).

.DESCRIPTION
    Run from the published folder (dist\RPLManager, or the unzipped RPLManager.zip). It
      - copies the app to %LOCALAPPDATA%\Programs\RPL Manager (replacing an older version),
      - adds "RPL Manager" to the Start menu and the desktop, and
      - registers it in Settings > Apps, where it can be uninstalled.
    League data (%LOCALAPPDATA%\RPL) is separate and is never touched by installing or uninstalling.
    -Uninstall removes the program, the shortcuts and the Settings > Apps entry.
#>
param([switch]$Uninstall, [switch]$Quiet)
$ErrorActionPreference = 'Stop'

$name    = 'RPL Manager'
$target  = Join-Path $env:LOCALAPPDATA "Programs\$name"
$exe     = Join-Path $target 'RPLManager.exe'
$desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) "$name.lnk"
$start   = Join-Path ([Environment]::GetFolderPath('Programs')) "$name.lnk"
$regKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\RPLManager'

function Stop-App {
    Get-Process RPLManager -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$target\*" } | ForEach-Object {
        $_.CloseMainWindow() | Out-Null
        if (-not $_.WaitForExit(5000)) { $_.Kill() }
    }
}

if ($Uninstall) {
    Stop-App
    Remove-Item $desktop, $start -ErrorAction SilentlyContinue
    Remove-Item $regKey -Recurse -ErrorAction SilentlyContinue
    # The uninstaller runs from a copy in %TEMP%, so the program folder can be deleted.
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    if (-not $Quiet) { Write-Host "$name has been uninstalled. Your league data in $env:LOCALAPPDATA\RPL was kept." }
    return
}

$source = $PSScriptRoot
if (-not (Test-Path (Join-Path $source 'RPLManager.exe'))) { throw "RPLManager.exe was not found next to Install.ps1 ($source)." }
if ((Resolve-Path $source).Path.TrimEnd('\') -ieq $target.TrimEnd('\')) { throw "Run Install from the downloaded / published folder, not from the installed one." }

Write-Host "Installing $name to $target ..."
Stop-App
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $source '*') $target -Recurse -Force

$shell = New-Object -ComObject WScript.Shell
foreach ($lnk in $desktop, $start) {
    $sc = $shell.CreateShortcut($lnk)
    $sc.TargetPath = $exe; $sc.WorkingDirectory = $target
    $sc.IconLocation = Join-Path $target 'Assets\RPL.ico'; $sc.Description = 'Ratnadeep Premier League'
    $sc.Save()
}

$version = (Get-Item $exe).VersionInfo.ProductVersion
if (-not $version) { $version = '1.0.0' }
$sizeKb = [int]((Get-ChildItem $target -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
# The uninstaller runs a copy in %TEMP%, so the program folder can be deleted.
$uninstallCmd = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Copy-Item ''{0}\Install.ps1'' $env:TEMP\RPL-Uninstall.ps1 -Force; & $env:TEMP\RPL-Uninstall.ps1 -Uninstall{1}"'
$quietCmd = $uninstallCmd -f $target, ' -Quiet'
$uninstallCmd = $uninstallCmd -f $target, ''

$props = @{
    DisplayName = $name; DisplayVersion = ($version -split '\+')[0]; Publisher = 'Ratnadeep Premier League'
    DisplayIcon = Join-Path $target 'Assets\RPL.ico'; InstallLocation = $target
    UninstallString = $uninstallCmd; QuietUninstallString = $quietCmd
    InstallDate = (Get-Date -Format 'yyyyMMdd')
}
$reg = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall\RPLManager')
foreach ($k in $props.Keys) { $reg.SetValue($k, [string]$props[$k], [Microsoft.Win32.RegistryValueKind]::String) }
foreach ($k in 'NoModify', 'NoRepair') { $reg.SetValue($k, 1, [Microsoft.Win32.RegistryValueKind]::DWord) }
$reg.SetValue('EstimatedSize', $sizeKb, [Microsoft.Win32.RegistryValueKind]::DWord)
$reg.Close()

Write-Host "Installed. Start it from the Start menu or the desktop shortcut '$name'."
Write-Host "Uninstall: Settings > Apps > Installed apps > $name."
