<#
.SYNOPSIS
    Installs the Sensor Panel widget into WigiDash Manager, from source or from a release zip.

.DESCRIPTION
    WigiDash Manager loads widgets from %APPDATA%\G.SKILL\WigiDashManager\Widgets\<GUID>\<GUID>.dll
    and locks them while running, so the Manager is closed during the copy and restarted afterwards.
    The shipped layouts\default.json is refreshed on every install; keep your own edits in a separate
    file (or point the widget at this repo's layout via its settings page).

    From a clone of the repo, the widget is built first (needs the .NET SDK). From an extracted
    release zip, the prebuilt files next to this script are installed as they are.

.PARAMETER Prebuilt
    Install the prebuilt files next to this script instead of building. Implied when the widget
    DLL sits next to the script, as it does in a release zip.

.PARAMETER NoRestart
    Leave WigiDash Manager closed after installing.
#>
[CmdletBinding()]
param(
    [switch]$Prebuilt,
    [switch]$NoRestart
)

$ErrorActionPreference = 'Stop'
$guid = '56507051-FDDD-447B-B59B-81D78C7CD961'
$target = Join-Path $env:APPDATA "G.SKILL\WigiDashManager\Widgets\$guid"
$managerExe = 'C:\Program Files (x86)\G.SKILL\WigiDash Manager\WigiDashManager.exe'

if (-not $Prebuilt -and (Test-Path (Join-Path $PSScriptRoot "$guid.dll"))) { $Prebuilt = $true }
if ($Prebuilt) {
    $src = $PSScriptRoot
    if (-not (Test-Path (Join-Path $src "$guid.dll"))) {
        throw "-Prebuilt installs the files from a release zip, but $guid.dll isn't next to this script. Run install.ps1 from the extracted zip."
    }
} else {
    $src = Join-Path ([IO.Path]::GetTempPath()) "wigipanel-$guid"
    & (Join-Path $PSScriptRoot 'package.ps1') -OutDir $src
}

$running = Get-Process WigiDashManager -ErrorAction SilentlyContinue
if ($running) {
    Write-Host '==> Closing WigiDash Manager' -ForegroundColor Cyan
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 60 -ErrorAction SilentlyContinue
}

Write-Host "==> Installing to $target" -ForegroundColor Cyan
New-Item -ItemType Directory -Force (Join-Path $target 'layouts') | Out-Null
# The DLL can stay locked for a few seconds after the Manager process exits.
for ($i = 1; ; $i++) {
    try { Copy-Item (Join-Path $src "$guid.dll") $target -Force -ErrorAction Stop; break }
    catch { if ($i -ge 30) { throw }; Start-Sleep -Seconds 1 }
}
Copy-Item (Join-Path $src "$guid.pdb") $target -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $src '*.png') $target -Force
Copy-Item (Join-Path $src 'layouts\*') (Join-Path $target 'layouts') -Recurse -Force
# Drop .ttf/.otf files here to make them available to every layout and AIDA64 skin.
New-Item -ItemType Directory -Force (Join-Path $target 'fonts') | Out-Null
if (Test-Path (Join-Path $src 'fonts')) { Copy-Item (Join-Path $src 'fonts\*') (Join-Path $target 'fonts') -Recurse -Force }
# Files extracted from a downloaded zip carry the "from the internet" mark, and .NET Framework
# refuses to load a marked DLL. Clear it on everything we installed.
Get-ChildItem $target -Recurse -File | Unblock-File

if (-not $NoRestart -and (Test-Path $managerExe)) {
    Write-Host '==> Starting WigiDash Manager' -ForegroundColor Cyan
    Start-Process $managerExe -WorkingDirectory (Split-Path $managerExe)
}

Write-Host "Done. Add 'Sensor Panel' to a page at size 5x4." -ForegroundColor Green
