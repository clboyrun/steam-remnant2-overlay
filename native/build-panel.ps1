<#
.SYNOPSIS
  Builds the in-game panel (native\Remnant2Overlay.InGame) from any PowerShell window.
.DESCRIPTION
  Finds the newest Visual Studio with the C++ tools (2022, 2026, ...), loads its 64-bit build environment, and
  builds with the CMake and Ninja that ship with Visual Studio, so no separate CMake or special prompt is needed.
.EXAMPLE
  .\native\build-panel.ps1
  .\native\build-panel.ps1 -Clean
#>
param(
    [switch]$Clean
)
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'VsBuildEnv.ps1')
$tools = Enter-VsBuildEnv

$source = Join-Path $PSScriptRoot 'Remnant2Overlay.InGame'
$build = Join-Path $source 'build'
Reset-BuildFolderIfNotNinja $build -Force:$Clean

& $tools.CMake -S $source -B $build -G Ninja -DCMAKE_BUILD_TYPE=Release "-DCMAKE_MAKE_PROGRAM=$($tools.Ninja)"
if ($LASTEXITCODE -ne 0) { throw 'CMake configure failed (see above).' }
& $tools.CMake --build $build
if ($LASTEXITCODE -ne 0) { throw 'Build failed (see above).' }

Write-Host "Built $(Join-Path $build 'Remnant2Overlay.InGame.dll')"
