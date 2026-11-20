<#
.SYNOPSIS
    Generates the native skins into layouts/: Monsters, Nord Frost, Synthwave, Quartet, Amber Grid,
    Jade, Molten and Sage Ledger.
.DESCRIPTION
    All skins share sensor bindings, defined once below. Edit this script and re-run it
    rather than hand-editing the generated JSON, then preview with tools/Preview.
#>
param([string]$OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'layouts'))
$ErrorActionPreference = 'Stop'

# ------------------------------------------------------------------ element helpers
function Opt($h, $o) { if ($o) { foreach ($k in ($o.Keys | Sort-Object)) { $h[$k] = $o[$k] } }; $h }  # sorted: stable output
function Rect($x, $y, $w, $h, $color, $radius = 0) { [ordered]@{ type = 'rect'; x = $x; y = $y; w = $w; h = $h; color = $color; radius = $radius } }
function Txt($x, $y, $text, $font, $size, $color, $o = $null) { Opt ([ordered]@{ type = 'text'; x = $x; y = $y; text = $text; font = $font; size = $size; color = $color }) $o }
function Bar($x, $y, $w, $h, $sensor, $min, $max, $color, $bg, $o = $null) { Opt ([ordered]@{ type = 'bar'; x = $x; y = $y; w = $w; h = $h; sensor = $sensor; min = $min; max = $max; color = $color; bg = $bg; radius = [Math]::Min($h, $w) / 2 }) $o }
function Ring($x, $y, $d, $t, $sensor, $min, $max, $color, $bg, $o = $null) { Opt ([ordered]@{ type = 'gauge'; x = $x; y = $y; w = $d; thickness = $t; start = 135; sweep = 270; roundCaps = $true; sensor = $sensor; min = $min; max = $max; color = $color; bg = $bg }) $o }
function Graph($x, $y, $w, $h, $sensor, $max, $color, $o = $null) { Opt ([ordered]@{ type = 'graph'; x = $x; y = $y; w = $w; h = $h; sensor = $sensor; min = 0; max = $max; autoscale = $true; points = 90; color = $color }) $o }
function Hot($warn, $crit, $cw, $cc) { , @([ordered]@{ at = $warn; color = $cw }, [ordered]@{ at = $crit; color = $cc }) }
# Adds elements (or arrays of them) to the current skin's $e list.
function A { foreach ($a in $args) { foreach ($i in @($a)) { [void]$e.Add($i) } } }
# Segmented 270° ring: n short gauges, each lit once the value passes the middle of its segment.
function SegRing($x, $y, $d, $t, $sensor, $min, $max, $n, $color, $bg, $th = $null) {
  $step = 270.0 / $n; $span = ($max - $min) / $n
  for ($i = 0; $i -lt $n; $i++) {
    $lo = $min + $span * ($i + 0.5)
    $o = @{ start = [Math]::Round(135 + $i * $step, 2); sweep = [Math]::Round($step * 0.72, 2); roundCaps = $false }
    if ($th) { $o.thresholds = $th }
    Ring $x $y $d $t $sensor ([Math]::Round($lo, 4)) ([Math]::Round($lo + $span * 0.001, 4)) $color $bg $o
  }
}

# ------------------------------------------------------------------ shared sensors
$GB = 1 / 1024.0
$sensors = [ordered]@{
  cpuTemp   = [ordered]@{ name = '^CPU$'; source = '^CPU \[#0\]'; unit = 'C'; demo = 52 }
  cpuClock  = [ordered]@{ name = '^Core \d+ Clock$'; source = '^CPU \[#0\]'; aggregate = 'avg'; demo = 5225 }
  cpuPower  = [ordered]@{ name = '^CPU Package Power$'; demo = 63.4 }
  cpuLoad   = [ordered]@{ name = '^Total CPU Usage$'; demo = 24 }
  memUsed   = [ordered]@{ name = '^Physical Memory Used$'; scale = $GB; demo = 17.4 }
  memTotal  = [ordered]@{ name = '^Physical Memory (Used|Available)$'; aggregate = 'sum'; scale = $GB; demo = 61.7 }
  memLoad   = [ordered]@{ name = '^Physical Memory Load$'; demo = 28 }
  gpuTemp   = [ordered]@{ name = '^GPU Temperature$'; source = '^[di]?GPU \[#0\]'; demo = 61 }
  gpuClock  = [ordered]@{ name = '^GPU Clock$'; source = '^[di]?GPU \[#0\]'; demo = 2655 }
  gpuPower  = [ordered]@{ name = '^GPU Power$'; source = '^[di]?GPU \[#0\]'; unit = '^W$'; demo = 412 }
  gpuLoad   = [ordered]@{ name = '^GPU Core Load$'; source = '^[di]?GPU \[#0\]'; demo = 87 }
  vramUsed  = [ordered]@{ name = '^GPU Memory Allocated$'; source = '^[di]?GPU \[#0\]'; scale = $GB; demo = 11.6 }
  vramTotal = [ordered]@{ name = '^GPU Memory (Allocated|Available)$'; source = '^[di]?GPU \[#0\]'; aggregate = 'sum'; scale = $GB; demo = 31.8 }
  vramLoad  = [ordered]@{ name = '^GPU Memory Usage$'; source = '^[di]?GPU \[#0\]'; unit = '%'; demo = 36 }
  fps       = [ordered]@{ name = '^Framerate Presented$'; source = '^PresentMon'; staleMs = 3000; demo = 144 }
  pump      = [ordered]@{ name = '^Fan 4$'; source = 'Aqua Computer Octo'; unit = 'RPM'; demo = 2750 }
  water     = [ordered]@{ name = '^Temperature 4$'; source = 'Aqua Computer Octo'; demo = 31.2 }
  fan       = [ordered]@{ name = '^Fan 1$'; source = 'Aqua Computer Octo'; unit = 'RPM'; demo = 860 }
  flow      = [ordered]@{ name = '^Flow$'; source = 'Aqua Computer Octo'; demo = 94 }
  # HWiNFO reports KB/s (1024 bytes); x 8 x 1024 / 1e6 = Mbps
  netDown   = [ordered]@{ name = '^Current DL rate$'; aggregate = 'sum'; scale = 0.008192; demo = 182.4 }
  netUp     = [ordered]@{ name = '^Current UP rate$'; aggregate = 'sum'; scale = 0.008192; demo = 21.7 }
}
foreach ($i in 1..6) { $sensors["pin$i"] = [ordered]@{ name = "^Pin $i Current$"; source = 'WireView'; demo = @(5.4, 5.9, 5.2, 6.6, 5.7, 5.8)[$i - 1] } }

function Save($file, $name, $bg, $elements) {
  $layout = [ordered]@{ name = $name; canvas = [ordered]@{ width = 1024; height = 600; background = $bg }; refreshMs = 1000; sensors = $sensors; elements = $elements }
  $layout | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutDir $file) -Encoding utf8
}

# ================================================================== 1. Monsters (user palette), card grid + rings
& {
  $bg = '#111310'; $card = '#1A1D17'; $track = '#2B3026'; $txt = '#ECF2E2'; $mut = '#7E8A72'
  $green = '#95C92C'; $lime = '#B5E655'; $teal = '#4CB6C2'; $orange = '#FF8F00'; $hot = '#F06000'
  $L = 'Roboto Light'; $M = 'Roboto Medium'; $K = 'Roboto Black'; $R = 'Roboto'
  $e = [System.Collections.ArrayList]::new()

  # header
  [void]$e.Add((Txt 22 6 '{time:HH:mm}' $L 34 $txt))
  [void]$e.Add((Txt 142 14 '{date:dddd}' $M 13 $lime))
  [void]$e.Add((Txt 142 34 '{date:d MMMM yyyy}' $R 12 $mut))
  [void]$e.Add((Txt 994 20 '{HOSTNAME}' $K 16 $txt @{ align = 'right' }))

  function Metric($x, $y, $w, $label, $value, $sensor, $max, $color) {
    [void]$e.Add((Txt $x $y $label $M 9 $mut))
    [void]$e.Add((Txt ($x + $w) ($y - 4) $value $M 14 $txt @{ align = 'right' }))
    [void]$e.Add((Bar $x ($y + 22) $w 6 $sensor 0 $max $color $track))
  }

  # CPU card
  [void]$e.Add((Rect 12 70 440 286 $card 14))
  [void]$e.Add((Txt 32 86 'CPU' $K 11 $green))
  [void]$e.Add((Txt 32 102 '{CPUMODEL}' $M 15 $txt))
  [void]$e.Add((Ring 32 140 150 12 'cpuTemp' 20 100 $green $track @{ thresholds = (Hot 75 88 $orange $hot) }))
  [void]$e.Add((Txt 32 178 '{cpuTemp:0}°' $L 34 $txt @{ w = 150; align = 'center' }))
  [void]$e.Add((Txt 32 226 'PACKAGE' $M 8 $mut @{ w = 150; align = 'center' }))
  Metric 206 146 226 'CLOCK' '{cpuClock:0} MHz' 'cpuClock' 5700 $green
  Metric 206 194 226 'POWER' '{cpuPower:0.0} W' 'cpuPower' 160 $green
  Metric 206 242 226 'LOAD' '{cpuLoad:0} %' 'cpuLoad' 100 $green
  Metric 32 306 400 'MEMORY' '{memUsed:0.0} / {memTotal:0} GB' 'memLoad' 100 $lime

  # GPU card
  [void]$e.Add((Rect 464 70 540 286 $card 14))
  [void]$e.Add((Txt 484 86 'GPU' $K 11 $orange))
  [void]$e.Add((Txt 484 102 '{GPUMODEL}' $M 15 $txt))
  [void]$e.Add((Ring 484 140 150 12 'gpuTemp' 20 100 $orange $track @{ thresholds = (Hot 75 85 $hot '#FF3B1F') }))
  [void]$e.Add((Txt 484 178 '{gpuTemp:0}°' $L 34 $txt @{ w = 150; align = 'center' }))
  [void]$e.Add((Txt 484 226 'CORE' $M 8 $mut @{ w = 150; align = 'center' }))
  Metric 656 146 180 'CLOCK' '{gpuClock:0} MHz' 'gpuClock' 3200 $orange
  Metric 656 194 180 'POWER' '{gpuPower:0} W' 'gpuPower' 600 $orange
  Metric 656 242 180 'LOAD' '{gpuLoad:0} %' 'gpuLoad' 100 $orange
  [void]$e.Add((Txt 858 142 'FPS' $M 9 $mut))
  [void]$e.Add((Txt 984 128 '{fps:0}' $L 34 $lime @{ align = 'right' }))
  [void]$e.Add((Txt 858 198 '12V PINS · A' $M 9 $mut))
  foreach ($i in 0..5) {
    $px = 858 + $i * 22
    [void]$e.Add((Bar $px 216 14 40 "pin$($i+1)" 0 12 $teal $track @{ vertical = $true; radius = 3; thresholds = (Hot 8 9.5 $orange $hot) }))
    [void]$e.Add((Txt ($px - 4) 260 "{pin$($i+1):0.0}" $R 7 $mut @{ w = 22; align = 'center' }))
  }
  Metric 484 306 500 'VRAM' '{vramUsed:0.0} / {vramTotal:0} GB' 'vramLoad' 100 $orange

  # Water loop card
  [void]$e.Add((Rect 12 368 440 212 $card 14))
  [void]$e.Add((Txt 32 384 'WATER LOOP' $K 11 $teal))
  function Tile($x, $y, $label, $value, $unit, $sensor, $min, $max, $th = $null) {
    [void]$e.Add((Txt $x $y $label $M 9 $mut))
    [void]$e.Add((Txt $x ($y + 12) $value $L 28 $txt))
    [void]$e.Add((Txt ($x + 190) ($y + 28) $unit $M 10 $mut @{ align = 'right' }))
    $o = @{}; if ($th) { $o.thresholds = $th }
    [void]$e.Add((Bar $x ($y + 56) 190 5 $sensor $min $max $teal $track $o))
  }
  Tile 32 408 'PUMP' '{pump:0}' 'RPM' 'pump' 0 4800
  Tile 242 408 'WATER' '{water:0.0}' '°C' 'water' 20 45 (Hot 38 42 $orange $hot)
  Tile 32 492 'FAN' '{fan:0}' 'RPM' 'fan' 0 2000
  Tile 242 492 'FLOW' '{flow:0}' 'L/h' 'flow' 0 200

  # Network card
  [void]$e.Add((Rect 464 368 540 212 $card 14))
  [void]$e.Add((Txt 484 384 'NETWORK' $K 11 $lime))
  [void]$e.Add((Txt 484 408 'IPv4' $M 9 $mut))
  [void]$e.Add((Txt 524 404 '{LOCALIP}' $M 13 $txt))
  [void]$e.Add((Txt 484 430 'IPv6' $M 9 $mut @{ showIf = '{LOCALIP6}' }))
  [void]$e.Add((Txt 524 428 '{LOCALIP6}' $R 10 $txt @{ showIf = '{LOCALIP6}' }))
  [void]$e.Add((Txt 484 456 '▼ DOWNLOAD' $M 9 $lime))
  [void]$e.Add((Txt 484 468 '{netDown:0.0}' $L 26 $txt))
  [void]$e.Add((Txt 724 484 'Mbps' $M 10 $mut @{ align = 'right' }))
  [void]$e.Add((Graph 484 512 240 52 'netDown' 100 $lime @{ graphType = 'area'; bg = '#151813' }))
  [void]$e.Add((Txt 744 456 '▲ UPLOAD' $M 9 $orange))
  [void]$e.Add((Txt 744 468 '{netUp:0.0}' $L 26 $txt))
  [void]$e.Add((Txt 984 484 'Mbps' $M 10 $mut @{ align = 'right' }))
  [void]$e.Add((Graph 744 512 240 52 'netUp' 20 $orange @{ graphType = 'area'; bg = '#151813' }))

  Save 'Monsters.json' 'Monsters' $bg $e
}

# ================================================================== 2. Nord Frost, side panel + wide strips with sparklines
& {
  $bg = '#2E3440'; $card = '#3B4252'; $track = '#4C566A'; $txt = '#ECEFF4'; $mut = '#9AA5BA'
  $frost1 = '#8FBCBB'; $frost2 = '#88C0D0'; $frost3 = '#81A1C1'; $frost4 = '#5E81AC'
  $green = '#A3BE8C'; $yellow = '#EBCB8B'; $orange = '#D08770'; $red = '#BF616A'
  $T = 'Roboto Thin'; $L = 'Roboto Light'; $M = 'Roboto Medium'; $K = 'Roboto Black'; $R = 'Roboto'
  $e = [System.Collections.ArrayList]::new()

  # side panel: time, date, host, network
  [void]$e.Add((Rect 12 12 318 568 $card 12))
  [void]$e.Add((Txt 28 16 '{time:HH:mm}' $T 60 $txt))
  [void]$e.Add((Txt 30 104 '{date:dddd}' $M 15 $frost2))
  [void]$e.Add((Txt 30 126 '{date:d MMMM yyyy}' $R 13 $mut))
  [void]$e.Add((Rect 30 162 282 1 $track))
  [void]$e.Add((Txt 30 176 '{HOSTNAME}' $M 17 $txt))
  [void]$e.Add((Txt 30 206 'IPv4' $M 9 $mut))
  [void]$e.Add((Txt 68 203 '{LOCALIP}' $R 12 $txt))
  [void]$e.Add((Txt 30 226 'IPv6' $M 9 $mut @{ showIf = '{LOCALIP6}' }))
  [void]$e.Add((Txt 68 224 '{LOCALIP6}' $R 9 $txt @{ showIf = '{LOCALIP6}' }))
  [void]$e.Add((Rect 30 256 282 1 $track))
  [void]$e.Add((Txt 30 270 'DOWNLOAD' $M 9 $mut))
  [void]$e.Add((Txt 30 282 '{netDown:0.0}' $L 30 $green))
  [void]$e.Add((Txt 312 300 'Mbps' $M 10 $mut @{ align = 'right' }))
  [void]$e.Add((Graph 30 330 282 64 'netDown' 100 $green @{ graphType = 'area' }))
  [void]$e.Add((Txt 30 412 'UPLOAD' $M 9 $mut))
  [void]$e.Add((Txt 30 424 '{netUp:0.0}' $L 30 $yellow))
  [void]$e.Add((Txt 312 442 'Mbps' $M 10 $mut @{ align = 'right' }))
  [void]$e.Add((Graph 30 472 282 64 'netUp' 20 $yellow @{ graphType = 'area' }))

  function Stat($x, $y, $label, $value) {
    [void]$e.Add((Txt $x $y $label $M 9 $mut))
    [void]$e.Add((Txt $x ($y + 13) $value $M 17 $txt))
  }
  function Usage($y, $label, $value, $sensor, $color) {
    [void]$e.Add((Txt 362 $y $label $M 9 $mut))
    [void]$e.Add((Txt 988 ($y - 3) $value $M 12 $txt @{ align = 'right' }))
    [void]$e.Add((Bar 362 ($y + 18) 626 6 $sensor 0 100 $color $track))
  }

  # CPU strip
  [void]$e.Add((Rect 342 12 662 196 $card 12))
  [void]$e.Add((Txt 362 26 'CPU' $K 11 $frost2))
  [void]$e.Add((Txt 400 23 '{CPUMODEL}' $M 14 $txt))
  [void]$e.Add((Txt 358 48 '{cpuTemp:0}°' $T 56 $txt @{ sensor = 'cpuTemp'; thresholds = (Hot 75 88 $orange $red) }))
  Stat 520 52 'CLOCK' '{cpuClock:0} MHz'
  Stat 660 52 'POWER' '{cpuPower:0.0} W'
  Stat 800 52 'LOAD' '{cpuLoad:0} %'
  [void]$e.Add((Graph 520 100 468 52 'cpuLoad' 100 $frost2 @{ autoscale = $false; points = 120 }))
  Usage 166 'MEMORY' '{memUsed:0.0} / {memTotal:0} GB' 'memLoad' $frost1

  # GPU strip
  [void]$e.Add((Rect 342 220 662 228 $card 12))
  [void]$e.Add((Txt 362 234 'GPU' $K 11 $frost3))
  [void]$e.Add((Txt 400 231 '{GPUMODEL}' $M 14 $txt))
  [void]$e.Add((Txt 358 256 '{gpuTemp:0}°' $T 56 $txt @{ sensor = 'gpuTemp'; thresholds = (Hot 75 85 $orange $red) }))
  Stat 520 260 'CLOCK' '{gpuClock:0} MHz'
  Stat 660 260 'POWER' '{gpuPower:0} W'
  Stat 800 260 'LOAD' '{gpuLoad:0} %'
  [void]$e.Add((Txt 912 260 'FPS' $M 9 $mut))
  [void]$e.Add((Txt 988 266 '{fps:0}' $L 30 $yellow @{ align = 'right' }))
  [void]$e.Add((Graph 520 310 300 56 'gpuLoad' 100 $frost3 @{ autoscale = $false; points = 120 }))
  [void]$e.Add((Txt 842 308 '12V PIN CURRENT' $M 9 $mut))
  foreach ($i in 0..5) {
    $px = 842 + $i * 25
    [void]$e.Add((Bar $px 326 17 34 "pin$($i+1)" 0 12 $frost1 $track @{ vertical = $true; radius = 3; thresholds = (Hot 8 9.5 $orange $red) }))
    [void]$e.Add((Txt ($px - 4) 362 "{pin$($i+1):0.0}" $R 7 $mut @{ w = 25; align = 'center' }))
  }
  Usage 404 'VRAM' '{vramUsed:0.0} / {vramTotal:0} GB' 'vramLoad' $frost3

  # Water strip
  [void]$e.Add((Rect 342 460 662 120 $card 12))
  [void]$e.Add((Txt 362 474 'WATER LOOP' $K 11 $frost4))
  $cols = @(@('PUMP', '{pump:0}', 'RPM', 'pump', 0, 4800), @('WATER', '{water:0.0}', '°C', 'water', 20, 45), @('FAN', '{fan:0}', 'RPM', 'fan', 0, 2000), @('FLOW', '{flow:0}', 'L/h', 'flow', 0, 200))
  for ($i = 0; $i -lt 4; $i++) {
    $wc = $cols[$i]; $x = 362 + $i * 160
    [void]$e.Add((Txt $x 498 $wc[0] $M 9 $mut))
    [void]$e.Add((Txt $x 508 $wc[1] $L 26 $txt))
    [void]$e.Add((Txt ($x + 140) 522 $wc[2] $M 10 $mut @{ align = 'right' }))
    $o = @{}; if ($wc[3] -eq 'water') { $o.thresholds = (Hot 38 42 $orange $red) }
    [void]$e.Add((Bar $x 556 140 4 $wc[3] $wc[4] $wc[5] $frost4 $track $o))
  }

  Save 'Nord Frost.json' 'Nord Frost' $bg $e
}

# ================================================================== 3. Synthwave HUD, nested rings
& {
  $bg = '#0B0420'; $card = '#160A35'; $track = '#2A1B57'; $txt = '#F4F1FF'; $mut = '#8F86B8'
  $pink = '#FF2A6D'; $cyan = '#05D9E8'; $violet = '#9B7BFF'; $amber = '#FFB800'
  $L = 'Roboto Light'; $M = 'Roboto Medium'; $K = 'Roboto Black'; $R = 'Roboto'; $C = 'Roboto Condensed'
  $e = [System.Collections.ArrayList]::new()

  # header bar
  [void]$e.Add((Rect 0 0 1024 54 '#120731'))
  [void]$e.Add((Rect 0 54 1024 2 $pink))
  [void]$e.Add((Txt 20 8 '{time:HH:mm:ss}' $M 24 $cyan))
  [void]$e.Add((Txt 170 18 '{date:ddd d MMM yyyy}' $R 13 $mut))
  [void]$e.Add((Txt 994 14 '{HOSTNAME}' $K 16 $pink @{ align = 'right' }))

  function Hud($x, $name, $model, $accent, $load, $temp, $warn, $crit) {
    [void]$e.Add((Rect $x 68 488 300 $card 6))
    [void]$e.Add((Rect $x 68 4 300 $accent))
    [void]$e.Add((Txt ($x + 20) 80 $name $K 12 $accent))
    [void]$e.Add((Txt ($x + 20) 98 $model $C 14 $txt))
    # outer ring = load, inner ring = temperature
    [void]$e.Add((Ring ($x + 20) 130 196 10 $load 0 100 $accent $track))
    [void]$e.Add((Ring ($x + 40) 150 156 6 $temp 20 100 $amber $track @{ thresholds = (Hot $warn $crit '#FF6B00' $pink) }))
    [void]$e.Add((Txt ($x + 20) 186 "{$($load):0}%" $L 34 $txt @{ w = 196; align = 'center' }))
    [void]$e.Add((Txt ($x + 20) 234 "{$($temp):0} °C" $M 13 $amber @{ w = 196; align = 'center'; sensor = $temp; thresholds = (Hot $warn $crit '#FF6B00' $pink) }))
  }
  function Readout($x, $y, $label, $value, $color) {
    [void]$e.Add((Txt $x $y $label $M 9 $mut))
    [void]$e.Add((Txt $x ($y + 12) $value $C 20 $color))
  }

  # CPU
  Hud 12 'CPU' '{CPUMODEL}' $cyan 'cpuLoad' 'cpuTemp' 75 88
  Readout 246 136 'CLOCK' '{cpuClock:0} MHz' $txt
  Readout 246 186 'POWER' '{cpuPower:0.0} W' $txt
  [void]$e.Add((Txt 246 236 'MEMORY' $M 9 $mut))
  [void]$e.Add((Txt 246 248 '{memUsed:0.0} / {memTotal:0} GB' $C 16 $txt))
  [void]$e.Add((Bar 246 276 234 6 'memLoad' 0 100 $cyan $track))
  [void]$e.Add((Graph 246 300 234 52 'cpuLoad' 100 $cyan @{ graphType = 'area'; bg = '#120731' }))

  # GPU
  Hud 524 'GPU' '{GPUMODEL}' $pink 'gpuLoad' 'gpuTemp' 75 85
  Readout 758 136 'CLOCK' '{gpuClock:0} MHz' $txt
  Readout 758 186 'POWER' '{gpuPower:0} W' $txt
  [void]$e.Add((Txt 892 136 'FPS' $M 9 $mut))
  [void]$e.Add((Txt 992 140 '{fps:0}' $L 34 $amber @{ align = 'right' }))
  [void]$e.Add((Txt 758 236 'VRAM' $M 9 $mut))
  [void]$e.Add((Txt 758 248 '{vramUsed:0.0} / {vramTotal:0} GB' $C 16 $txt))
  [void]$e.Add((Bar 758 276 234 6 'vramLoad' 0 100 $pink $track))
  [void]$e.Add((Txt 758 296 '12V PINS · A' $M 9 $mut))
  foreach ($i in 0..5) {
    $px = 758 + $i * 40
    [void]$e.Add((Bar $px 312 32 24 "pin$($i+1)" 0 12 $violet $track @{ vertical = $true; radius = 2; thresholds = (Hot 8 9.5 $amber $pink) }))
    [void]$e.Add((Txt $px 340 "{pin$($i+1):0.0}" $C 9 $mut @{ w = 32; align = 'center' }))
  }

  # Water loop: four mini rings
  [void]$e.Add((Rect 12 380 488 200 $card 6))
  [void]$e.Add((Rect 12 380 4 200 $violet))
  [void]$e.Add((Txt 32 392 'WATER LOOP' $K 12 $violet))
  $w = @(@('PUMP', '{pump:0}', 'RPM', 'pump', 0, 4800), @('WATER', '{water:0.0}', '°C', 'water', 20, 45), @('FAN', '{fan:0}', 'RPM', 'fan', 0, 2000), @('FLOW', '{flow:0}', 'L/h', 'flow', 0, 200))
  for ($i = 0; $i -lt 4; $i++) {
    $wc = $w[$i]; $x = 32 + $i * 117
    $o = @{}; if ($wc[3] -eq 'water') { $o.thresholds = (Hot 38 42 $amber $pink) }
    [void]$e.Add((Ring $x 420 100 8 $wc[3] $wc[4] $wc[5] $violet $track $o))
    [void]$e.Add((Txt $x 452 $wc[1] $C 20 $txt @{ w = 100; align = 'center' }))
    [void]$e.Add((Txt $x 480 $wc[2] $M 9 $mut @{ w = 100; align = 'center' }))
    [void]$e.Add((Txt $x 530 $wc[0] $M 10 $violet @{ w = 100; align = 'center' }))
  }

  # Network
  [void]$e.Add((Rect 524 380 488 200 $card 6))
  [void]$e.Add((Rect 524 380 4 200 $amber))
  [void]$e.Add((Txt 544 392 'NETWORK' $K 12 $amber))
  [void]$e.Add((Txt 544 414 'IPv4' $M 9 $mut))
  [void]$e.Add((Txt 584 410 '{LOCALIP}' $C 14 $txt))
  [void]$e.Add((Txt 544 436 'IPv6' $M 9 $mut @{ showIf = '{LOCALIP6}' }))
  [void]$e.Add((Txt 584 434 '{LOCALIP6}' $C 11 $txt @{ showIf = '{LOCALIP6}' }))
  [void]$e.Add((Txt 544 462 '▼ DOWN' $M 9 $cyan))
  [void]$e.Add((Txt 544 474 '{netDown:0.0} Mbps' $C 20 $txt))
  [void]$e.Add((Graph 544 506 220 60 'netDown' 100 $cyan @{ graphType = 'area'; bg = '#120731' }))
  [void]$e.Add((Txt 776 462 '▲ UP' $M 9 $pink))
  [void]$e.Add((Txt 776 474 '{netUp:0.0} Mbps' $C 20 $txt))
  [void]$e.Add((Graph 776 506 216 60 'netUp' 20 $pink @{ graphType = 'area'; bg = '#120731' }))

  Save 'Synthwave.json' 'Synthwave' $bg $e
}
# ================================================================== 4. Quartet, four tall columns ending in a disc gauge
& {
  $bg = '#1F2029'; $card = '#F3F0E8'; $ink = '#2A2C36'; $mut = '#8B8D98'; $track = '#DEDAD0'; $wh = '#FFFFFF'
  $coral = '#EF5B5B'; $teal = '#22A99A'; $saffron = '#EFAA2E'; $indigo = '#5E6CEB'
  $M = 'Roboto Medium'; $K = 'Roboto Black'; $R = 'Roboto'
  $e = [System.Collections.ArrayList]::new()

  function Column($x, $title, $sub, $accent, $fg) {
    A (Rect $x 12 244 568 $card 16) (Rect $x 12 244 62 $accent 16) (Rect $x 50 244 24 $accent)
    A (Txt ($x + 18) 18 $title $K 15 $fg) (Txt ($x + 18) 46 $sub $M 9 $fg)
  }
  function Row($x, $y, $label, $value, $sensor, $max, $accent, $o = $null) {
    A (Txt ($x + 18) $y $label $M 9 $mut) (Txt ($x + 226) ($y - 4) $value $M 14 $ink @{ align = 'right' })
    if ($sensor) { A (Bar ($x + 18) ($y + 20) 208 7 $sensor 0 $max $accent $track $o) }
  }
  function Disc($x, $accent, $fg, $sensor, $min, $max, $value, $unit, $size = 44) {
    A (Rect ($x + 8) 336 228 228 $accent 114) (Rect ($x + 20) 348 204 204 '#1FFFFFFF' 102)
    A (Ring ($x + 32) 360 180 12 $sensor $min $max $fg '#33000000')
    A (Txt ($x + 8) (444 - $size) $value $K $size $fg @{ w = 228; align = 'center' })
    A (Txt ($x + 8) 466 $unit $M 10 $fg @{ w = 228; align = 'center' })
  }

  Column 12 'CPU' '{CPUMODEL}' $coral $wh
  Row 12 92 'LOAD' '{cpuLoad:0} %' 'cpuLoad' 100 $coral
  Row 12 148 'CLOCK' '{cpuClock:0} MHz' 'cpuClock' 5700 $coral
  Row 12 204 'POWER' '{cpuPower:0.0} W' 'cpuPower' 160 $coral
  Row 12 260 'MEMORY' '{memUsed:0.0} / {memTotal:0} GB' 'memLoad' 100 $coral
  Disc 12 $coral $ink 'cpuTemp' 20 100 '{cpuTemp:0}°' 'CPU PACKAGE'

  Column 264 'GPU' '{GPUMODEL}' $teal $wh
  Row 264 92 'LOAD' '{gpuLoad:0} %' 'gpuLoad' 100 $teal
  Row 264 148 'CLOCK' '{gpuClock:0} MHz' 'gpuClock' 3200 $teal
  Row 264 204 'POWER' '{gpuPower:0} W' 'gpuPower' 600 $teal
  Row 264 260 'VRAM' '{vramUsed:0.0} / {vramTotal:0} GB' 'vramLoad' 100 $teal
  Disc 264 $teal $ink 'gpuTemp' 20 100 '{gpuTemp:0}°' 'GPU CORE'

  Column 516 'FPS' '{HOSTNAME}' $saffron $ink
  Row 516 92 'DOWNLOAD' '{netDown:0.0} Mbps' 'netDown' 1000 $saffron
  Row 516 148 'UPLOAD' '{netUp:0.0} Mbps' 'netUp' 100 $saffron
  Row 516 204 'IPv4' '{LOCALIP}' $null 0 $saffron
  A (Txt 534 228 'IPv6' $M 9 $mut @{ showIf = '{LOCALIP6}' }) (Txt 742 228 '{LOCALIP6}' $R 8 $ink @{ align = 'right'; showIf = '{LOCALIP6}' })
  A (Txt 534 260 '12V PINS · A' $M 9 $mut)
  foreach ($i in 0..5) {
    $px = 534 + $i * 36
    A (Bar $px 278 28 24 "pin$($i+1)" 0 12 $saffron $track @{ vertical = $true; radius = 3; thresholds = (Hot 8 9.5 $coral '#B3261E') })
    A (Txt $px 304 "{pin$($i+1):0.0}" $R 8 $mut @{ w = 28; align = 'center' })
  }
  Disc 516 $saffron $ink 'fps' 0 240 '{fps:0}' 'FRAMES PER SECOND'

  Column 768 'LOOP' 'WATER COOLING' $indigo $wh
  Row 768 92 'PUMP' '{pump:0} RPM' 'pump' 4800 $indigo
  Row 768 148 'FAN' '{fan:0} RPM' 'fan' 2000 $indigo
  Row 768 204 'FLOW' '{flow:0} L/h' 'flow' 200 $indigo
  Row 768 260 '{date:ddd d MMM}' '{time:HH:mm}' $null 0 $indigo
  Disc 768 $indigo $wh 'water' 20 45 '{water:0.0}°' 'WATER' 36

  Save 'Quartet.json' 'Quartet' $bg $e
}

# ================================================================== 5. Amber Grid, outlined boxes with ring + bar readouts
& {
  $bg = '#0B0A09'; $line = '#C98A2E'; $panel = '#131009'; $txt = '#F4EADB'; $mut = '#8F806C'
  $acc = '#FFB547'; $track = '#2E261C'; $warn = '#FF8A3D'; $hot = '#FF5F4A'; $alt = '#7BD389'
  $L = 'Roboto Light'; $R = 'Roboto'
  $e = [System.Collections.ArrayList]::new()

  function Box($x, $y, $w, $h) { A (Rect $x $y $w $h $line 8) (Rect ($x + 2) ($y + 2) ($w - 4) ($h - 4) $panel 6) }
  function KV($x, $y, $w, $label, $value, $unit) {
    A (Txt $x $y $label $R 15 $mut) (Txt ($x + $w - 40) $y $value $R 15 $txt @{ align = 'right' }) (Txt ($x + $w - 36) $y $unit $R 15 $acc)
  }
  function Proc($x, $w, $model, $load, $power, $rows) {
    Box $x 114 $w 282
    A (Txt ($x + 20) 128 $model $R 20 $txt)
    A (Ring ($x + 22) 182 176 14 $load 0 100 $acc $track @{ roundCaps = $false })
    A (Txt ($x + 22) 232 "{$($load):0}%" $L 34 $txt @{ w = 176; align = 'center' })
    A (Txt ($x + 22) 282 $power $R 13 $acc @{ w = 176; align = 'center' }) (Txt ($x + 22) 302 'Load' $R 12 $mut @{ w = 176; align = 'center' })
    $bx = $x + 232; $bw = $w - 252
    for ($j = 0; $j -lt $rows.Count; $j++) {
      $row = $rows[$j]; $y = 196 + $j * 62
      $o = @{ radius = 0 }; if ($row[5]) { $o.thresholds = $row[5] }
      A (Bar $bx $y $bw 12 $row[2] $row[3] $row[4] $acc $track $o)
      A (Txt $bx ($y + 18) $row[0] $R 14 $mut) (Txt ($bx + $bw) ($y + 18) $row[1] $R 14 $txt @{ align = 'right' })
    }
  }

  Box 12 12 484 92
  KV 32 24 200 'Pump' '{pump:0}' 'RPM'
  KV 272 24 204 'Water' '{water:0.0}' '°C'
  KV 32 60 200 'Fan' '{fan:0}' 'RPM'
  KV 272 60 204 'Flow' '{flow:0}' 'L/h'

  Box 508 12 504 92
  A (Txt 528 18 '{HOSTNAME}' $R 22 $txt) (Txt 528 62 '{date:dddd d MMMM yyyy}' $R 14 $mut)
  A (Txt 992 58 '{time:HH:mm:ss}' $R 18 $acc @{ align = 'right' })

  Proc 12 484 '{CPUMODEL}' 'cpuLoad' '{cpuPower:0.0} W' @(
    @('Temperature', '{cpuTemp:0} °C', 'cpuTemp', 20, 100, (Hot 75 88 $warn $hot)),
    @('CPU Clock', '{cpuClock:0} MHz', 'cpuClock', 0, 5700, $null),
    @('Memory', '{memUsed:0.0} / {memTotal:0} GB', 'memLoad', 0, 100, $null))
  Proc 508 504 '{GPUMODEL}' 'gpuLoad' '{gpuPower:0} W' @(
    @('Temperature', '{gpuTemp:0} °C', 'gpuTemp', 20, 100, (Hot 75 85 $warn $hot)),
    @('GPU Clock', '{gpuClock:0} MHz', 'gpuClock', 0, 3200, $null),
    @('VRAM', '{vramUsed:0.0} / {vramTotal:0} GB', 'vramLoad', 0, 100, $null))

  Box 12 408 316 172
  A (Txt 104 418 '{fps:0}' $R 26 $txt @{ align = 'right' }) (Txt 110 430 'FPS' $R 15 $acc)
  A (Graph 32 470 276 92 'fps' 240 $acc @{ graphType = 'line'; lineWidth = 2; autoscale = $false; gridColor = '#241E16'; gridSize = 23; bg = $panel })

  Box 340 408 328 172
  A (Txt 360 420 '12V PINS' $R 22 $txt) (Txt 648 428 'A' $R 15 $acc @{ align = 'right' })
  foreach ($i in 0..5) {
    $px = 362 + $i * 50
    A (Bar $px 462 30 72 "pin$($i+1)" 0 12 $acc $track @{ vertical = $true; radius = 0; thresholds = (Hot 8 9.5 $warn $hot) })
    A (Txt ($px - 10) 540 "{pin$($i+1):0.0}" $R 12 $txt @{ w = 50; align = 'center' })
  }

  Box 680 408 332 172
  A (Txt 700 420 'NETWORK' $R 22 $txt) (Txt 700 454 '{LOCALIP}' $R 19 $acc) (Txt 700 484 '{LOCALIP6}' $R 10 $mut @{ showIf = '{LOCALIP6}' })
  A (Txt 700 510 'RX' $R 14 $mut) (Bar 736 513 140 14 'netDown' 0 1000 $acc $track @{ radius = 0 }) (Txt 992 510 '{netDown:0.0} Mbps' $R 13 $txt @{ align = 'right' })
  A (Txt 700 542 'TX' $R 14 $mut) (Bar 736 545 140 14 'netUp' 0 100 $alt $track @{ radius = 0 }) (Txt 992 542 '{netUp:0.0} Mbps' $R 13 $txt @{ align = 'right' })

  Save 'Amber Grid.json' 'Amber Grid' $bg $e
}

# ================================================================== 6. Jade, three bevelled columns over a row of small panels
& {
  $bg = '#0A100E'; $edge = '#1E2F29'; $panel = '#111A17'; $plaque = '#0C1411'; $txt = '#E6F2EC'; $mut = '#7C958C'
  $jade = '#2ED3A0'; $track = '#1F2E29'; $warn = '#E5C76B'; $hot = '#FF6B57'
  $L = 'Roboto Light'; $M = 'Roboto Medium'; $R = 'Roboto'
  $e = [System.Collections.ArrayList]::new()

  function Shell($x, $y, $w, $h) {
    $sh = [Math]::Min(60, $h - 40)
    A (Rect $x $y $w $h $edge 16) (Rect ($x + 4) ($y + 4) ($w - 8) ($h - 8) $panel 13)
    A (Rect $x ($y + ($h - $sh) / 2) 4 $sh $jade 2) (Rect ($x + $w - 4) ($y + ($h - $sh) / 2) 4 $sh $jade 2)
  }
  function Plaque($x, $y, $w, $h) { A (Rect $x $y $w $h $edge 10) (Rect ($x + 2) ($y + 2) ($w - 4) ($h - 4) $plaque 8) }
  function Slider($x, $y, $label, $value, $sensor, $min, $max, $o = $null) {
    A (Txt ($x + 28) $y $label $M 9 $mut) (Txt ($x + 292) ($y - 4) $value $M 15 $txt @{ align = 'right' })
    if ($sensor) { A (Bar ($x + 28) ($y + 20) 264 3 $sensor $min $max $jade $track $o) }
  }
  function Column($x, $title, $sub, $sensor, $min, $max, $value, $unit, $th) {
    Shell $x 12 320 454
    Plaque ($x + 24) 28 272 54
    A (Txt ($x + 24) 32 $title $M 15 $txt @{ w = 272; align = 'center' }) (Txt ($x + 24) 56 $sub $R 11 $mut @{ w = 272; align = 'center' })
    $o = @{}; if ($th) { $o.thresholds = $th }
    A (Ring ($x + 85) 98 150 9 $sensor $min $max $jade $track $o) (Rect ($x + 101) 114 118 118 $plaque 59)
    A (Txt ($x + 85) 130 $value $L 38 $txt @{ w = 150; align = 'center' }) (Txt ($x + 85) 184 $unit $M 9 $mut @{ w = 150; align = 'center' })
    A (Rect ($x + 24) 264 272 1 $edge)
  }
  function Readout($x, $big, $small) {
    Plaque ($x + 90) 408 140 48
    A (Txt ($x + 90) 410 $big $M 18 $txt @{ w = 140; align = 'center' }) (Txt ($x + 90) 436 $small $R 8 $mut @{ w = 140; align = 'center' })
  }

  Column 12 'CPU' '{CPUMODEL}' 'cpuTemp' 20 100 '{cpuTemp:0}' '°C' (Hot 75 88 $warn $hot)
  Slider 12 278 'LOAD' '{cpuLoad:0} %' 'cpuLoad' 0 100
  Slider 12 322 'POWER' '{cpuPower:0.0} W' 'cpuPower' 0 160
  Slider 12 366 'MEMORY' '{memUsed:0.0} / {memTotal:0} GB' 'memLoad' 0 100
  Readout 12 '{cpuClock:0}' 'MHz'

  Column 352 'GPU' '{GPUMODEL}' 'gpuTemp' 20 100 '{gpuTemp:0}' '°C' (Hot 75 85 $warn $hot)
  Slider 352 278 'LOAD' '{gpuLoad:0} %' 'gpuLoad' 0 100
  Slider 352 322 'POWER' '{gpuPower:0} W' 'gpuPower' 0 600
  Slider 352 366 'VRAM' '{vramUsed:0.0} / {vramTotal:0} GB' 'vramLoad' 0 100
  Readout 352 '{gpuClock:0}' 'MHz'

  Column 692 'FPS' '{HOSTNAME}' 'fps' 0 240 '{fps:0}' 'FPS' $null
  Slider 692 278 'DOWNLOAD' '{netDown:0.0} Mbps' 'netDown' 0 1000
  Slider 692 322 'UPLOAD' '{netUp:0.0} Mbps' 'netUp' 0 100
  Slider 692 366 'IPv4' '{LOCALIP}' $null 0 0
  A (Txt 984 388 '{LOCALIP6}' $R 8 $mut @{ align = 'right'; showIf = '{LOCALIP6}' })
  Readout 692 '{time:HH:mm:ss}' '{date:ddd d MMM yyyy}'

  Shell 12 478 320 102
  Slider 12 494 'PUMP' '{pump:0} RPM' 'pump' 0 4800
  Slider 12 538 'FAN' '{fan:0} RPM' 'fan' 0 2000

  Shell 352 478 320 102
  A (Txt 380 490 '12V PINS · A' $M 9 $mut)
  foreach ($i in 0..5) {
    $px = 380 + $i * 45
    A (Bar $px 508 32 44 "pin$($i+1)" 0 12 $jade $track @{ vertical = $true; radius = 2; thresholds = (Hot 8 9.5 $warn $hot) })
    A (Txt $px 554 "{pin$($i+1):0.0}" $R 9 $txt @{ w = 32; align = 'center' })
  }

  Shell 692 478 320 102
  Slider 692 494 'WATER' '{water:0.0} °C' 'water' 20 45 @{ thresholds = (Hot 38 42 $warn $hot) }
  Slider 692 538 'FLOW' '{flow:0} L/h' 'flow' 0 200

  Save 'Jade.json' 'Jade' $bg $e
}

# ================================================================== 7. Molten, segmented ring gauges over a four-panel footer
& {
  $bg = '#100B09'; $edge = '#33261F'; $panel = '#1B1411'; $panel2 = '#251C17'; $txt = '#F6EAE1'; $mut = '#A08A7D'
  $glow = '#FF7A1A'; $amber = '#FFC145'; $track = '#3A2A21'; $red = '#FF3D2E'
  $M = 'Roboto Medium'; $K = 'Roboto Black'; $R = 'Roboto'
  $e = [System.Collections.ArrayList]::new()

  function Shell($x, $y, $w, $h) { A (Rect $x $y $w $h $edge 10) (Rect ($x + 3) ($y + 3) ($w - 6) ($h - 6) $panel 8) }
  function Glow($x, $y, $h) { A (Rect $x $y 4 $h $glow 2) }
  function Row($x, $y, $w, $label, $value, $sensor, $min, $max, $o = $null) {
    A (Rect $x $y $w 44 $panel2 6) (Txt ($x + 14) ($y + 6) $label $M 9 $txt) (Txt ($x + $w - 14) ($y + 2) $value $M 15 $txt @{ align = 'right' })
    if ($sensor) { A (Bar ($x + 14) ($y + 28) ($w - 28) 7 $sensor $min $max $glow $track $o) }
  }
  function Info($x, $y, $w, $label, $value) {
    A (Rect $x $y $w 44 $panel2 6) (Txt ($x + 14) ($y + 6) $label $M 9 $mut) (Txt ($x + 14) ($y + 21) $value $M 11 $txt)
  }
  function Column($x, $title, $sub, $sensor, $min, $max, $value, $unit, $th) {
    Shell $x 12 328 450
    A (Rect ($x + 24) 20 280 34 $panel2 17) (Rect ($x + 12) 32 10 10 $glow 5) (Rect ($x + 306) 32 10 10 $glow 5)
    A (Txt ($x + 40) 24 $title $K 15 $txt) (Txt ($x + 92) 30 $sub $M 9 $mut)
    A (Rect ($x + 24) 62 280 196 $panel2 8)
    Glow ($x + 10) 100 120; Glow ($x + 314) 100 120
    A (Ring ($x + 94) 88 140 2 $null 0 1 $edge $edge @{ sweep = 360; roundCaps = $false })
    A (SegRing ($x + 72) 66 184 16 $sensor $min $max 36 $glow $track $th)
    A (Txt ($x + 72) 114 $value $K 40 $txt @{ w = 184; align = 'center' }) (Txt ($x + 72) 172 $unit $M 10 $mut @{ w = 184; align = 'center' })
  }
  function Footer($x) { Shell $x 470 244 110; Glow ($x + 8) 490 70; Glow ($x + 232) 490 70 }

  Column 12 'CPU' '{CPUMODEL}' 'cpuTemp' 20 100 '{cpuTemp:0}' '°C' (Hot 75 88 $amber $red)
  Row 24 266 304 'LOAD' '{cpuLoad:0} %' 'cpuLoad' 0 100
  Row 24 314 304 'CLOCK' '{cpuClock:0} MHz' 'cpuClock' 0 5700
  Row 24 362 304 'POWER' '{cpuPower:0.0} W' 'cpuPower' 0 160
  Row 24 410 304 'MEMORY' '{memUsed:0.0} / {memTotal:0} GB' 'memLoad' 0 100

  Column 348 'GPU' '{GPUMODEL}' 'gpuTemp' 20 100 '{gpuTemp:0}' '°C' (Hot 75 85 $amber $red)
  Row 360 266 304 'LOAD' '{gpuLoad:0} %' 'gpuLoad' 0 100
  Row 360 314 304 'CLOCK' '{gpuClock:0} MHz' 'gpuClock' 0 3200
  Row 360 362 304 'POWER' '{gpuPower:0} W' 'gpuPower' 0 600
  Row 360 410 304 'VRAM' '{vramUsed:0.0} / {vramTotal:0} GB' 'vramLoad' 0 100

  Column 684 'FPS' '{HOSTNAME}' 'fps' 0 240 '{fps:0}' 'FPS' $null
  Row 696 266 304 'DOWNLOAD' '{netDown:0.0} Mbps' 'netDown' 0 1000
  Row 696 314 304 'UPLOAD' '{netUp:0.0} Mbps' 'netUp' 0 100
  A (Rect 696 362 304 92 $panel2 6) (Rect 768 368 160 24 $panel 12) (Txt 768 371 'SYSTEM' $K 11 $txt @{ w = 160; align = 'center' })
  A (Txt 696 394 '{time:HH:mm:ss}' $K 22 $amber @{ w = 304; align = 'center' })
  A (Txt 696 428 '{date:dddd d MMMM yyyy}' $M 10 $mut @{ w = 304; align = 'center' })

  Footer 12
  Row 32 480 204 'WATER TEMP' '{water:0.0} °C' 'water' 20 45 @{ thresholds = (Hot 38 42 $amber $red) }
  Row 32 528 204 'FLOW RATE' '{flow:0} L/h' 'flow' 0 200
  Footer 264
  Row 284 480 204 'PUMP' '{pump:0} RPM' 'pump' 0 4800
  Row 284 528 204 'FANS' '{fan:0} RPM' 'fan' 0 2000
  Footer 516
  A (Rect 536 480 204 92 $panel2 6) (Txt 550 486 '12V PINS · A' $M 9 $txt)
  foreach ($i in 0..5) {
    $px = 552 + $i * 30
    A (Bar $px 504 20 48 "pin$($i+1)" 0 12 $glow $track @{ vertical = $true; radius = 3; thresholds = (Hot 8 9.5 $amber $red) })
    A (Txt ($px - 5) 554 "{pin$($i+1):0.0}" $R 7 $mut @{ w = 30; align = 'center' })
  }
  Footer 768
  Info 788 480 204 'IPv4' '{LOCALIP}'
  Info 788 528 204 'IPv6' '{LOCALIP6}'

  Save 'Molten.json' 'Molten' $bg $e
}

# ================================================================== 8. Sage Ledger, paper panels with thermometers and dials
& {
  $paper = '#E4E8DA'; $card = '#EDF0E4'; $ink = '#2E3A33'; $mut = '#6B7569'; $track = '#CDD2C1'; $soft = '#B9C2B0'; $stripe = '#3D4B42'
  $teal = '#3C7A89'; $mustard = '#C48F1E'; $plum = '#8A4B6D'; $hot = '#C0392B'
  $L = 'Roboto Light'; $M = 'Roboto Medium'; $K = 'Roboto Black'; $R = 'Roboto'
  $e = [System.Collections.ArrayList]::new()

  function Box($x, $y, $w, $h, $title, $tw, $right = $false) {
    A (Rect $x $y $w $h $ink 10) (Rect ($x + 2) ($y + 2) ($w - 4) ($h - 4) $card 8)
    if ($right) { A (Rect ($x + $w - $tw) $y $tw 26 $ink 8) (Txt ($x + $w - 12) ($y + 5) $title $M 11 $paper @{ align = 'right' }) }
    else { A (Rect $x $y $tw 26 $ink 8) (Txt ($x + 12) ($y + 5) $title $M 11 $paper) }
  }
  function Meter($x, $y, $w, $label, $value, $sensor, $min, $max, $color, $o = $null) {
    A (Txt $x $y $label $M 8 $mut) (Txt ($x + $w) ($y - 3) $value $K 12 $ink @{ align = 'right' })
    A (Bar $x ($y + 16) $w 10 $sensor $min $max $color $track (Opt @{ radius = 0; frameColor = $ink } $o))
  }
  # CPU/GPU side panel: thermometer on the outer edge, two meters and a load dial on the inside.
  function Side($x, $title, $right, $temp, $load, $th, $m1, $m2) {
    Box $x 108 250 352 $title 214 $right
    if ($right) { $tx = $x + 210; $tick = $tx - 7; $mx = $x + 12; $bx = $x + 170 } else { $tx = $x + 18; $tick = $x + 42; $mx = $x + 92; $bx = $x + 10 }
    A (Bar $tx 150 22 222 $temp 0 100 $plum $track @{ vertical = $true; radius = 0; frameColor = $ink; thresholds = $th })
    foreach ($v in 0..10) {
      $ty = [Math]::Round(372 - $v * 22.2)
      A (Rect $tick $ty 5 1 $ink)
      if ($v % 2 -eq 0) {
        if ($right) { A (Txt ($tick - 3) ($ty - 7) "$($v * 10)" $R 7 $ink @{ align = 'right' }) } else { A (Txt ($tick + 8) ($ty - 7) "$($v * 10)" $R 7 $ink) }
      }
    }
    A (Rect $bx 384 70 40 $ink 4) (Txt $bx 388 "{$($temp):0}°" $K 18 $paper @{ w = 70; align = 'center' })
    Meter $mx 146 146 $m1[0] $m1[1] $m1[2] 0 $m1[3] $teal
    Meter $mx 192 146 $m2[0] $m2[1] $m2[2] 0 $m2[3] $mustard
    A (Ring ($mx + 3) 268 140 2 $null 0 1 $ink $ink @{ sweep = 360; roundCaps = $false })
    A (Ring ($mx + 15) 280 116 18 $load 0 100 $mustard $track @{ start = 270; sweep = 360; roundCaps = $false; steps = 20 })
    A (Txt ($mx + 3) 309 "{$($load):0}" $K 26 $plum @{ w = 140; align = 'center' }) (Txt ($mx + 3) 346 '% LOAD' $M 8 $mut @{ w = 140; align = 'center' })
  }

  # banner
  A (Rect 12 12 1000 84 $ink 10)
  foreach ($i in 0..8) { A (Rect (26 + $i * 18) 22 9 64 $stripe 1) (Rect (853 + $i * 18) 22 9 64 $stripe 1) }
  A (Txt 196 20 'HOST' $M 8 $mustard) (Txt 196 32 '{HOSTNAME}' $M 15 $paper) (Txt 196 58 '{LOCALIP}' $R 10 $soft)
  A (Txt 196 74 '{LOCALIP6}' $R 7 $soft @{ showIf = '{LOCALIP6}' })
  A (Txt 470 20 '{date:dddd}' $M 8 $mustard) (Txt 470 32 '{date:yyyy-MM-dd}' $L 26 $paper)
  A (Txt 836 20 'LOCAL TIME' $M 8 $mustard @{ align = 'right' }) (Txt 836 32 '{time:HH:mm:ss}' $L 26 $paper @{ align = 'right' })

  Side 12 '{CPUMODEL}' $false 'cpuTemp' 'cpuLoad' (Hot 75 88 $mustard $hot) @('CLOCK', '{cpuClock:0} MHz', 'cpuClock', 5700) @('POWER', '{cpuPower:0.0} W', 'cpuPower', 160)
  Side 762 '{GPUMODEL}' $true 'gpuTemp' 'gpuLoad' (Hot 75 85 $mustard $hot) @('CLOCK', '{gpuClock:0} MHz', 'gpuClock', 3200) @('POWER', '{gpuPower:0} W', 'gpuPower', 600)

  Box 274 108 476 84 'MEMORY' 110
  Meter 292 146 440 'IN USE' '{memUsed:0.0} / {memTotal:0} GB  ·  {memLoad:0} %' 'memLoad' 0 100 $teal
  Box 274 202 476 84 'VIDEO MEMORY' 140
  Meter 292 240 440 'IN USE' '{vramUsed:0.0} / {vramTotal:0} GB  ·  {vramLoad:0} %' 'vramLoad' 0 100 $plum

  Box 274 298 232 162 'NETWORK' 110
  A (Txt 290 334 '▼ DOWN' $M 8 $teal) (Txt 490 330 '{netDown:0.0} Mbps' $K 12 $ink @{ align = 'right' })
  A (Graph 290 350 200 34 'netDown' 100 $teal @{ graphType = 'area'; bg = $card; frameColor = $ink })
  A (Txt 290 392 '▲ UP' $M 8 $mustard) (Txt 490 388 '{netUp:0.0} Mbps' $K 12 $ink @{ align = 'right' })
  A (Graph 290 408 200 34 'netUp' 20 $mustard @{ graphType = 'area'; bg = $card; frameColor = $ink })

  Box 518 298 232 162 'DISPLAY' 100
  A (Txt 534 334 'FRAMERATE' $M 8 $mut) (Txt 690 326 '{fps:0}' $K 30 $ink @{ align = 'right' }) (Txt 696 344 'FPS' $M 10 $plum)
  A (Graph 534 384 200 58 'fps' 240 $plum @{ graphType = 'line'; lineWidth = 2; autoscale = $false; bg = $card; frameColor = $ink; gridColor = $track; gridSize = 20 })

  Box 12 472 494 108 'WATER LOOP' 130
  $loop = @(@('PUMP · RPM', '{pump:0}', 'pump', 0, 4800, $teal), @('WATER · °C', '{water:0.0}', 'water', 20, 45, $plum), @('FAN · RPM', '{fan:0}', 'fan', 0, 2000, $mustard), @('FLOW · L/h', '{flow:0}', 'flow', 0, 200, $teal))
  for ($i = 0; $i -lt 4; $i++) {
    $c = $loop[$i]; $o = $null; if ($c[2] -eq 'water') { $o = @{ thresholds = (Hot 38 42 $mustard $hot) } }
    Meter (28 + $i * 120) 522 104 $c[0] $c[1] $c[2] $c[3] $c[4] $c[5] $o
  }

  Box 518 472 494 108 '12V-2x6 PINS · A' 170 $true
  foreach ($i in 0..5) { Meter (534 + $i * 78) 522 66 "PIN $($i+1)" "{pin$($i+1):0.0}" "pin$($i+1)" 0 12 $teal @{ thresholds = (Hot 8 9.5 $mustard $hot) } }

  Save 'Sage Ledger.json' 'Sage Ledger' $paper $e
}

'Monsters.json', 'Nord Frost.json', 'Synthwave.json', 'Quartet.json', 'Amber Grid.json', 'Jade.json', 'Molten.json', 'Sage Ledger.json' | ForEach-Object { Join-Path $OutDir $_ }
