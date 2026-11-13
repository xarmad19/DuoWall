<#
.SYNOPSIS
    Builds, tests, publishes, packages, and generates SHA-256 checksums for DuoWall by SRMD.
#>
param(
    [string]$Version = "2.0.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$pubDir = Join-Path $root "publish\DuoWall-Portable"
$zipPath = Join-Path $root "publish\DuoWall-v$Version-win-x64-Portable.zip"
$checksumFile = Join-Path $root "publish\SHA256SUMS.txt"

dotnet test (Join-Path $root "DuoWall.sln") -c Release --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

dotnet publish (Join-Path $root "src\DuoWall.App\DuoWall.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$Version `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -o $pubDir
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$stage = Join-Path $root "publish\_stage"
Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item (Join-Path $pubDir "DuoWall.exe") (Join-Path $stage "DuoWall.exe")
if (Test-Path (Join-Path $pubDir "libvlc")) {
    Copy-Item (Join-Path $pubDir "libvlc") (Join-Path $stage "libvlc") -Recurse -Force
}
Copy-Item (Join-Path $root "README.md") (Join-Path $stage "README.md")
Copy-Item (Join-Path $root "LICENSE") (Join-Path $stage "LICENSE")
Compress-Archive -Path "$stage\*" -DestinationPath $zipPath -Force
Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue

# Build Inno Setup Installer if compiler is available
$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
$setupExe = Join-Path $root "publish\DuoWall-v$Version-Setup.exe"

if ($iscc) {
    Write-Host "Compiling Inno Setup installer via $iscc..." -ForegroundColor Cyan
    & $iscc (Join-Path $root "installer\DuoWall.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }
} else {
    Write-Warning "ISCC.exe not found. Setup installer skipped."
}

$hashTargets = @( (Join-Path $pubDir "DuoWall.exe"), $zipPath )
if (Test-Path $setupExe) {
    $hashTargets += $setupExe
}

Get-FileHash -Path $hashTargets -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash.ToLowerInvariant())  $(Split-Path $_.Path -Leaf)" } |
    Out-File -Encoding ascii $checksumFile

Write-Host "Release artifacts ready:" -ForegroundColor Green
Get-Content $checksumFile

Write-Host "Release artifacts ready:" -ForegroundColor Green
Get-Content $checksumFile

