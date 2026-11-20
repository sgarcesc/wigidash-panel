<#
.SYNOPSIS
    Builds the GitHub Pages site into _site/ (or -OutDir).
.DESCRIPTION
    Copies the static files from site/, the hero image from docs/preview.png and every skin
    screenshot from designs/*.png, then fills the gallery in index.html. Skin names and order come
    from the Save calls in designs/make-designs.ps1, so the gallery follows the skins without
    hand edits. A screenshot with no matching skin is still shown, named after its file.
#>
param([string]$OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) '_site'))
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
$designs = Join-Path $repo 'designs'

function Slug($s) { ($s.ToLowerInvariant() -replace '[^a-z0-9]', '') }
function Html($s) { [System.Net.WebUtility]::HtmlEncode($s) }

# Skin names, in the order make-designs.ps1 generates them: Save 'Nord Frost.json' 'Nord Frost' ...
$script = Get-Content (Join-Path $designs 'make-designs.ps1') -Raw
$names = @([regex]::Matches($script, "(?m)^\s*Save\s+'[^']+'\s+'([^']+)'") | ForEach-Object { $_.Groups[1].Value })
if (-not $names) { throw 'No skin names found in designs/make-designs.ps1.' }

# Match each screenshot to a skin: nord.png -> Nord Frost, amber-grid.png -> Amber Grid.
$skins = foreach ($png in Get-ChildItem $designs -Filter *.png -File) {
  $slug = Slug $png.BaseName
  $name = $names | Where-Object { (Slug $_) -eq $slug } | Select-Object -First 1
  if (-not $name) { $name = $names | Where-Object { (Slug $_).StartsWith($slug) } | Select-Object -First 1 }
  $rank = if ($name) { [array]::IndexOf($names, $name) } else { $names.Count }
  if (-not $name) { $name = (Get-Culture).TextInfo.ToTitleCase(($png.BaseName -replace '[-_]', ' ')) }
  [pscustomobject]@{ File = $png; Name = $name; Rank = $rank }
}
$skins = @($skins | Sort-Object Rank, Name)
if (-not $skins) { throw 'No skin screenshots found in designs/.' }

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
$skinDir = New-Item -ItemType Directory -Force (Join-Path $OutDir 'skins')

Get-ChildItem $PSScriptRoot -File | Where-Object { $_.Name -ne 'build.ps1' -and $_.Name -ne 'index.html' } |
  Copy-Item -Destination $OutDir
Copy-Item (Join-Path $repo 'docs/preview.png') $OutDir

$cards = foreach ($s in $skins) {
  Copy-Item $s.File.FullName $skinDir
  $src = 'skins/' + [uri]::EscapeDataString($s.File.Name)
  $n = Html $s.Name
  @"
        <li class="skin">
          <a href="$src" data-lightbox aria-label="$n skin at full size">
            <img src="$src" alt="$n skin" width="1016" height="592" loading="lazy" decoding="async">
          </a>
          <p>$n</p>
        </li>
"@
}

$index = Get-Content (Join-Path $PSScriptRoot 'index.html') -Raw
if ($index -notmatch '<!-- SKINS -->') { throw 'site/index.html is missing the <!-- SKINS --> marker.' }
$index = $index.Replace('<!-- SKINS -->', ($cards -join "`n").Trim()).Replace('{{SKIN_COUNT}}', [string]$skins.Count)
Set-Content (Join-Path $OutDir 'index.html') $index -Encoding utf8NoBOM -NoNewline

Write-Host "Built $OutDir with $($skins.Count) skins: $(($skins | ForEach-Object Name) -join ', ')"
