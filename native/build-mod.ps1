<#
.SYNOPSIS
  Builds the UE4SS mod (native\Remnant2Overlay.UE4SS) from any PowerShell window.
.DESCRIPTION
  Fetches RE-UE4SS's submodules over HTTPS (they're listed with SSH links), then builds the mod with Visual Studio's
  own CMake and Ninja. UE4SS 3.0.x builds with CMake; a newer RE-UE4SS checkout with xmake.lua builds with xmake.
  Needs: RE-UE4SS cloned into native\RE-UE4SS at your UE4SS version, GitHub linked to Epic Games (for the private
  UEPseudo submodule), and Rust (rustup.rs).
.EXAMPLE
  .\native\build-mod.ps1
  .\native\build-mod.ps1 -Clean
#>
param(
    [switch]$Clean
)
$ErrorActionPreference = 'Stop'

$ue4ss = Join-Path $PSScriptRoot 'RE-UE4SS'
if (-not (Test-Path (Join-Path $ue4ss '.git'))) {
    throw 'RE-UE4SS not found. Clone it first: git clone -b v3.0.1 https://github.com/UE4SS-RE/RE-UE4SS.git native/RE-UE4SS'
}

# RE-UE4SS fetches its submodules and many CMake dependencies from git@github.com:... (SSH). Rewrite those to HTTPS
# for every git this script starts (CMake's included), without touching your Git settings, so Git signs in through
# the browser like any other GitHub clone. UEPseudo needs your GitHub linked to Epic Games.
$env:GIT_CONFIG_COUNT = '1'
$env:GIT_CONFIG_KEY_0 = 'url.https://github.com/.insteadOf'
$env:GIT_CONFIG_VALUE_0 = 'git@github.com:'

if (-not (Get-ChildItem (Join-Path $ue4ss 'deps\first\Unreal') -ErrorAction SilentlyContinue)) {
    Write-Host 'Fetching RE-UE4SS submodules (sign in to GitHub if asked)...'
    git -C $ue4ss submodule update --init --recursive
    if ($LASTEXITCODE -ne 0) {
        throw 'Couldn''t fetch the submodules. "Repository not found" for UEPseudo means your GitHub isn''t linked to Epic Games yet: https://www.unrealengine.com/en-US/ue-on-github'
    }
}

if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
    throw 'Rust is needed to build UE4SS. Install it from https://rustup.rs, open a new PowerShell window, and run this again.'
}

. (Join-Path $PSScriptRoot 'VsBuildEnv.ps1')
$tools = Enter-VsBuildEnv

if (Test-Path (Join-Path $ue4ss 'xmake.lua')) {
    # Newer RE-UE4SS (experimental releases) builds with xmake.
    if (-not (Get-Command xmake -ErrorAction SilentlyContinue)) { throw 'This RE-UE4SS version builds with xmake: install it (winget install xmake) and run this again.' }
    Push-Location $PSScriptRoot
    try {
        xmake f -m 'Game__Shipping__Win64' -y
        if ($LASTEXITCODE -eq 0) { xmake build Remnant2Overlay }
        if ($LASTEXITCODE -ne 0) { throw 'Build failed (see above).' }
    }
    finally { Pop-Location }
}
else {
    $build = Join-Path $PSScriptRoot 'build-mod'
    Reset-BuildFolderIfNotNinja $build -Force:$Clean
    & $tools.CMake -S $PSScriptRoot -B $build -G Ninja -DCMAKE_BUILD_TYPE=Game__Shipping__Win64 "-DCMAKE_MAKE_PROGRAM=$($tools.Ninja)"
    if ($LASTEXITCODE -ne 0) { throw 'CMake configure failed (see above).' }
    & $tools.CMake --build $build --target Remnant2Overlay
    if ($LASTEXITCODE -ne 0) { throw 'Build failed (see above).' }
}

$dll = Get-ChildItem -Path $PSScriptRoot -Recurse -Filter 'Remnant2Overlay.dll' -File |
    Where-Object { $_.FullName -notmatch '\\RE-UE4SS\\' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "Built $($dll.FullName)"
Write-Host 'Next: .\native\install-mod.ps1'
