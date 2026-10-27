# Builds the UE4SS mod against a local RE-UE4SS checkout in native/RE-UE4SS (UE4SS 3.0.x builds with CMake).
# Use native\build-mod.ps1 rather than calling this directly.
cmake_minimum_required(VERSION 3.18)
project(Remnant2OverlayMods)

add_subdirectory(RE-UE4SS)
add_subdirectory(Remnant2Overlay.UE4SS)
