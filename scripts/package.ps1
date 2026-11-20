<#
.SYNOPSIS
    Builds the Sensor Panel widget and stages everything WigiDash Manager needs into one folder.

.DESCRIPTION
    The staged folder is what a release zip contains: the widget DLL, rendered preview images,
    layouts\, fonts\ and install.ps1 (which installs from it with -Prebuilt, without the .NET SDK).
    scripts\install.ps1 uses this script when installing from source; the release workflow runs it
    with -Version, -Zip and -Installer to produce dist\wigidash-panel-<version>.zip and
    dist\wigidash-panel-setup-<version>.exe.

.PARAMETER Version
    Version stamped into the widget assembly (e.g. 1.2.0). Defaults to the csproj's <Version>.

.PARAMETER OutDir
    Folder to stage into. It is emptied first. Defaults to dist\wigidash-panel[-<version>].

.PARAMETER Zip
    Also zip the staged folder to dist\wigidash-panel-<version>.zip. Requires -Version.

.PARAMETER Installer
    Also compile installer\wigidash-panel.iss into dist\wigidash-panel-setup-<version>.exe with
    Inno Setup 6. Requires -Version. When ISCC.exe isn't found this is skipped with a warning,
    except in CI, where it fails so a release never ships without the installer.
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutDir,
    [switch]$Zip,
    [switch]$Installer
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$guid = '56507051-FDDD-447B-B59B-81D78C7CD961'
$dist = Join-Path $repo 'dist'
$name = if ($Version) { "wigidash-panel-$Version" } else { 'wigidash-panel' }
if ($Zip -and -not $Version) { throw '-Zip requires -Version.' }
if ($Installer -and -not $Version) { throw '-Installer requires -Version.' }
if (-not $OutDir) { $OutDir = Join-Path $dist $name }

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }

# The preview tool builds the widget as a project reference, so both builds get the same version.
$buildArgs = @('-c', 'Release', '-nologo', '-v', 'q')
if ($Version) { $buildArgs += "-p:Version=$Version" }

Write-Host '==> Building' -ForegroundColor Cyan
& $dotnet build (Join-Path $repo 'src\WigiPanel\WigiPanel.csproj') @buildArgs
if ($LASTEXITCODE) { throw 'Widget build failed.' }
& $dotnet build (Join-Path $repo 'tools\Preview\Preview.csproj') @buildArgs
if ($LASTEXITCODE) { throw 'Preview build failed.' }

$bin = Join-Path $repo 'src\WigiPanel\bin\Release'
$preview = Join-Path $repo 'tools\Preview\bin\Release\wigipanel-preview.exe'

Write-Host "==> Staging $OutDir" -ForegroundColor Cyan
if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Force $OutDir | Out-Null
Copy-Item (Join-Path $bin "$guid.dll") $OutDir
Copy-Item (Join-Path $bin "$guid.pdb") $OutDir -ErrorAction SilentlyContinue
Copy-Item (Join-Path $PSScriptRoot 'install.ps1') $OutDir
Copy-Item (Join-Path $repo 'LICENSE') $OutDir

Write-Host '==> Rendering preview images' -ForegroundColor Cyan
$layout = Join-Path $repo 'layouts\default.json'
& $preview $layout (Join-Path $OutDir 'preview_5x4.png') 1016 592 | Out-Null
if ($LASTEXITCODE) { throw 'Preview render failed.' }
& $preview $layout (Join-Path $OutDir 'thumb.png') 200 145 | Out-Null
if ($LASTEXITCODE) { throw 'Thumbnail render failed.' }

# Layouts, the AIDA64 sensor map and any local (git-ignored) AIDA64 skins with their fonts.
$layoutsSrc = Join-Path $repo 'layouts'
Get-ChildItem $layoutsSrc -Recurse -File | Where-Object { $_.Name -notlike 'live-frame*' -and $_.Name -notlike '*.preview.png' } | ForEach-Object {
    $dest = Join-Path (Join-Path $OutDir 'layouts') $_.FullName.Substring($layoutsSrc.Length + 1)
    New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
    Copy-Item $_.FullName $dest
}
New-Item -ItemType Directory -Force (Join-Path $OutDir 'fonts') | Out-Null
if (Test-Path (Join-Path $repo 'fonts')) { Copy-Item (Join-Path $repo 'fonts\*') (Join-Path $OutDir 'fonts') }

if ($Zip) {
    $zipPath = Join-Path $dist "$name.zip"
    Write-Host "==> Zipping $zipPath" -ForegroundColor Cyan
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $OutDir '*') -DestinationPath $zipPath
}

if ($Installer) {
    $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
    if (-not $iscc) {
        $iscc = @(${env:ProgramFiles(x86)}, $env:ProgramFiles, (Join-Path $env:LOCALAPPDATA 'Programs')) |
            Where-Object { $_ } | ForEach-Object { Join-Path $_ 'Inno Setup 6\ISCC.exe' } |
            Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $iscc) {
        if ($env:CI) { throw 'Inno Setup 6 (ISCC.exe) not found.' }
        Write-Warning 'Inno Setup 6 (ISCC.exe) not found, so the installer was skipped. Get it from https://jrsoftware.org/isinfo.php.'
    } else {
        Write-Host "==> Building installer dist\wigidash-panel-setup-$Version.exe" -ForegroundColor Cyan
        $iss = Join-Path $repo 'installer\wigidash-panel.iss'
        & $iscc /Q "/DVersion=$Version" "/DSourceDir=$((Resolve-Path $OutDir).Path)" "/DOutputDir=$dist" $iss
        if ($LASTEXITCODE) { throw 'Installer build failed.' }
    }
}
