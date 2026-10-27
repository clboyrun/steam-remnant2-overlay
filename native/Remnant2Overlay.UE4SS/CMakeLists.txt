cmake_minimum_required(VERSION 3.18)

# UE4SS loads Mods/Remnant2Overlay/dlls/main.dll; install-mod.ps1 copies this build there under that name.
set(TARGET Remnant2Overlay)
project(${TARGET})

add_library(${TARGET} SHARED "dllmain.cpp")
target_include_directories(${TARGET} PRIVATE .)
target_link_libraries(${TARGET} PUBLIC UE4SS)
