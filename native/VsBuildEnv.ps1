# Shared by build-panel.ps1 and build-mod.ps1: finds the newest Visual Studio with the C++ tools, loads its 64-bit
# build environment into this PowerShell session, and returns the CMake and Ninja that ship with it.
#
# -Vs2022Compiler picks the VS 2022 compiler (MSVC v143, 14.3x/14.4x) from any installed Visual Studio, newest first.
# UE4SS 3.0.x doesn't compile with VS 2026's compiler (14.5x).
function Enter-VsBuildEnv([switch]$Vs2022Compiler) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw 'Visual Studio not found (no vswhere.exe). Install Visual Studio with "Desktop development with C++".' }
    $all = @(& $vswhere -all -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)
    if ($all.Count -eq 0) { throw 'No Visual Studio with the C++ tools found. In the Visual Studio Installer, add "Desktop development with C++".' }
    $vs = & $vswhere -latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath

    $devCmdArgs = ''
    if ($Vs2022Compiler) {
        # Every installed v143 toolset, in any Visual Studio, newest version first.
        $toolset = $all | ForEach-Object {
            $msvc = Join-Path $_ 'VC\Tools\MSVC'
            Get-ChildItem $msvc -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '^14\.[34]\d\.' } |
                ForEach-Object { [pscustomobject]@{ Vs = $msvc | Split-Path | Split-Path | Split-Path; Version = [version]$_.Name } }
        } | Sort-Object Version -Descending | Select-Object -First 1
        if (-not $toolset) {
            throw 'UE4SS 3.0.x needs the VS 2022 C++ compiler. In the Visual Studio Installer: Modify > Individual components > tick "MSVC v143 - VS 2022 C++ x64/x86 build tools (Latest)", then run this again.'
        }
        $vs = $toolset.Vs
        $devCmdArgs = "-vcvars_ver=$($toolset.Version)"
        Write-Host "Using the VS 2022 compiler $($toolset.Version)"
    }
    Write-Host "Using Visual Studio at $vs"

    $shellArgs = @{ Arch = 'amd64'; HostArch = 'amd64'; SkipAutomaticLocation = $true }
    if ($devCmdArgs) { $shellArgs.DevCmdArguments = $devCmdArgs }
    & (Join-Path $vs 'Common7\Tools\Launch-VsDevShell.ps1') @shellArgs | Out-Null

    $tools = [pscustomobject]@{
        CMake = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
        Ninja = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe'
    }
    if (-not (Test-Path $tools.CMake) -or -not (Test-Path $tools.Ninja)) {
        throw 'Visual Studio''s CMake tools are missing. In the Visual Studio Installer, under Desktop development with C++, tick "C++ CMake tools for Windows".'
    }
    return $tools
}

# Deletes a build folder configured with a different generator or compiler; CMake can't switch either in place.
function Reset-BuildFolderIfNotNinja([string]$build, [switch]$Force) {
    $cache = Join-Path $build 'CMakeCache.txt'
    $compiler = (Get-Command cl.exe -ErrorAction SilentlyContinue).Source -replace '\\', '/'
    $stale = (Test-Path $cache) -and (
        -not (Select-String -Quiet -Path $cache -Pattern 'CMAKE_GENERATOR:INTERNAL=Ninja') -or
        ($compiler -and -not (Select-String -Quiet -SimpleMatch -Path $cache -Pattern $compiler)))
    if ($Force -or $stale) {
        Remove-Item -Recurse -Force $build -ErrorAction SilentlyContinue
    }
}
