# Shared by build-panel.ps1 and build-mod.ps1: finds the newest Visual Studio with the C++ tools, loads its 64-bit
# build environment into this PowerShell session, and returns the CMake and Ninja that ship with it.
function Enter-VsBuildEnv {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw 'Visual Studio not found (no vswhere.exe). Install Visual Studio with "Desktop development with C++".' }
    $vs = & $vswhere -latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vs) { throw 'No Visual Studio with the C++ tools found. In the Visual Studio Installer, add "Desktop development with C++".' }
    Write-Host "Using Visual Studio at $vs"

    & (Join-Path $vs 'Common7\Tools\Launch-VsDevShell.ps1') -Arch amd64 -HostArch amd64 -SkipAutomaticLocation | Out-Null

    $tools = [pscustomobject]@{
        CMake = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
        Ninja = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe'
    }
    if (-not (Test-Path $tools.CMake) -or -not (Test-Path $tools.Ninja)) {
        throw 'Visual Studio''s CMake tools are missing. In the Visual Studio Installer, under Desktop development with C++, tick "C++ CMake tools for Windows".'
    }
    return $tools
}

# Deletes a build folder that was configured with a different generator; CMake can't switch in place.
function Reset-BuildFolderIfNotNinja([string]$build, [switch]$Force) {
    $cache = Join-Path $build 'CMakeCache.txt'
    if ($Force -or ((Test-Path $cache) -and -not (Select-String -Quiet -Path $cache -Pattern 'CMAKE_GENERATOR:INTERNAL=Ninja'))) {
        Remove-Item -Recurse -Force $build -ErrorAction SilentlyContinue
    }
}
