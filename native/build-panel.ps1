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

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'Visual Studio not found (no vswhere.exe). Install Visual Studio with "Desktop development with C++".' }
$vs = & $vswhere -latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'No Visual Studio with the C++ tools found. In the Visual Studio Installer, add "Desktop development with C++".' }
Write-Host "Using Visual Studio at $vs"

# 64-bit compiler, plus Visual Studio's own CMake and Ninja, on PATH for this script.
$devShell = Join-Path $vs 'Common7\Tools\Launch-VsDevShell.ps1'
& $devShell -Arch amd64 -HostArch amd64 -SkipAutomaticLocation | Out-Null

$cmake = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
$ninja = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe'
if (-not (Test-Path $cmake) -or -not (Test-Path $ninja)) {
    throw 'Visual Studio''s CMake tools are missing. In the Visual Studio Installer, under Desktop development with C++, tick "C++ CMake tools for Windows".'
}

$source = Join-Path $PSScriptRoot 'Remnant2Overlay.InGame'
$build = Join-Path $source 'build'
# A build folder configured another way (NMake, a VS generator) can't be reused with Ninja.
if ($Clean -or ((Test-Path (Join-Path $build 'CMakeCache.txt')) -and -not (Select-String -Quiet -Path (Join-Path $build 'CMakeCache.txt') -Pattern 'CMAKE_GENERATOR:INTERNAL=Ninja'))) {
    Remove-Item -Recurse -Force $build -ErrorAction SilentlyContinue
}

& $cmake -S $source -B $build -G Ninja -DCMAKE_BUILD_TYPE=Release "-DCMAKE_MAKE_PROGRAM=$ninja"
if ($LASTEXITCODE -ne 0) { throw 'CMake configure failed (see above).' }
& $cmake --build $build
if ($LASTEXITCODE -ne 0) { throw 'Build failed (see above).' }

Write-Host "Built $(Join-Path $build 'Remnant2Overlay.InGame.dll')"
