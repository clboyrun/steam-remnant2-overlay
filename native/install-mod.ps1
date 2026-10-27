<#
.SYNOPSIS
  Installs the Remnant 2 Overlay UE4SS mod and its in-game panel into the game's UE4SS Mods folder.
.DESCRIPTION
  Copies the mod (built with xmake) as Mods\Remnant2Overlay\dlls\main.dll, the panel (built with CMake) next to it,
  and enables the mod. UE4SS must already be installed in Remnant2\Binaries\Win64.
.EXAMPLE
  .\native\install-mod.ps1
  .\native\install-mod.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Remnant2"
#>
param(
    [string]$GameDir
)
$ErrorActionPreference = 'Stop'

function Find-GameDir {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { return $null }
    $libraries = @($steam)
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        $libraries += Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' -AllMatches |
            ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value -replace '\\\\', '\' }
    }
    foreach ($library in $libraries) {
        $candidate = Join-Path $library 'steamapps\common\Remnant2'
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

function Find-Newest([string]$folder, [string]$name) {
    if (-not (Test-Path $folder)) { return $null }
    Get-ChildItem -Path $folder -Recurse -Filter $name -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\RE-UE4SS\\' } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

if (-not $GameDir) { $GameDir = Find-GameDir }
if (-not $GameDir) { throw 'Remnant 2 not found through Steam. Pass -GameDir "<...>\steamapps\common\Remnant2".' }
$win64 = Join-Path $GameDir 'Remnant2\Binaries\Win64'

# UE4SS 3.x keeps its files in Win64\ue4ss; older layouts put Mods straight in Win64.
$mods = @((Join-Path $win64 'ue4ss\Mods'), (Join-Path $win64 'Mods')) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $mods) { throw "No UE4SS Mods folder under $win64. Install UE4SS first (see native\README.md)." }

$mod = Find-Newest $PSScriptRoot 'Remnant2Overlay.dll'
if (-not $mod) { throw 'Build the mod first: cd native; xmake f -m "Game__Shipping__Win64" -y; xmake' }
$panel = Find-Newest (Join-Path $PSScriptRoot 'Remnant2Overlay.InGame') 'Remnant2Overlay.InGame.dll'
if (-not $panel) { $panel = Get-Item (Join-Path $env:LOCALAPPDATA 'Remnant2Overlay\Remnant2Overlay.InGame.dll') -ErrorAction SilentlyContinue }
if (-not $panel) { throw 'Build the panel first: cd native\Remnant2Overlay.InGame; cmake -B build -A x64; cmake --build build --config Release' }

$target = Join-Path $mods 'Remnant2Overlay'
New-Item -ItemType Directory -Force -Path (Join-Path $target 'dlls') | Out-Null
Copy-Item $mod.FullName (Join-Path $target 'dlls\main.dll') -Force
Copy-Item $panel.FullName (Join-Path $target 'dlls\Remnant2Overlay.InGame.dll') -Force
Set-Content -Path (Join-Path $target 'enabled.txt') -Value '' -NoNewline

Write-Host "Installed to $target"
Write-Host "  mod:   $($mod.FullName)"
Write-Host "  panel: $($panel.FullName)"
Write-Host 'Start Remnant 2 (keep the overlay app running so the panel has data) and press F8.'
