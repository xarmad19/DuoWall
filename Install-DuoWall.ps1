<#
.SYNOPSIS
    Installs DuoWall by SRMD locally to %LOCALAPPDATA%\Programs\DuoWall and creates Start Menu & Desktop shortcuts.
#>
param(
    [switch]$CreateDesktopShortcut = $true,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
$installDir = Join-Path $env:LOCALAPPDATA "Programs\DuoWall"
$startMenuShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\DuoWall by SRMD.lnk"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "DuoWall by SRMD.lnk"

if ($Uninstall) {
    if (Test-Path $startMenuShortcut) { Remove-Item $startMenuShortcut -Force }
    if (Test-Path $desktopShortcut) { Remove-Item $desktopShortcut -Force }
    if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force }
    Write-Host "DuoWall by SRMD has been uninstalled." -ForegroundColor Green
    return
}

$sourceExe = Join-Path $PSScriptRoot "publish\DuoWall-Portable\DuoWall.exe"
if (-not (Test-Path $sourceExe)) {
    throw "Standalone DuoWall.exe not found at: $sourceExe. Please run dotnet publish first."
}

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot "publish\DuoWall-Portable\*") -Destination $installDir -Recurse -Force
$installedExe = Join-Path $installDir "DuoWall.exe"

$wsh = New-Object -ComObject WScript.Shell

$sm = $wsh.CreateShortcut($startMenuShortcut)
$sm.TargetPath = $installedExe
$sm.WorkingDirectory = $installDir
$sm.Description = "DuoWall by SRMD - Smart Multi-Monitor Wallpaper Manager"
$sm.Save()

if ($CreateDesktopShortcut) {
    $dt = $wsh.CreateShortcut($desktopShortcut)
    $dt.TargetPath = $installedExe
    $dt.WorkingDirectory = $installDir
    $dt.Description = "DuoWall by SRMD - Smart Multi-Monitor Wallpaper Manager"
    $dt.Save()
}

Write-Host "Installed DuoWall by SRMD to: $installDir" -ForegroundColor Green
Write-Host "Start Menu shortcut created: $startMenuShortcut" -ForegroundColor Cyan
if ($CreateDesktopShortcut) {
    Write-Host "Desktop shortcut created: $desktopShortcut" -ForegroundColor Cyan
}

