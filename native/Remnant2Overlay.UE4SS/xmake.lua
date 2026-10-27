-- UE4SS loads Mods/Remnant2Overlay/dlls/main.dll; install-mod.ps1 copies this build there under that name.
local projectName = "Remnant2Overlay"

target(projectName)
    add_rules("ue4ss.mod")
    add_includedirs(".")
    add_files("dllmain.cpp")
