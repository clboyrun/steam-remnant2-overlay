# In-game UI for Remnant 2: a DLL loaded into the game that draws an ImGui panel over its DirectX 12 frames.
# Build (x64, from a Developer PowerShell or Visual Studio's "Open Folder"):
#   cmake -B build -A x64
#   cmake --build build --config Release
# The DLL is copied to %LOCALAPPDATA%\Remnant2Overlay; install-mod.ps1 puts it next to the UE4SS mod.
cmake_minimum_required(VERSION 3.24)
project(Remnant2Overlay.InGame LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

include(FetchContent)
# Pinned: imgui 1.91.6 changed the DX12 backend's Init signature.
FetchContent_Declare(imgui GIT_REPOSITORY https://github.com/ocornut/imgui.git GIT_TAG v1.91.5 GIT_SHALLOW TRUE)
FetchContent_Declare(minhook GIT_REPOSITORY https://github.com/TsudaKageyu/minhook.git GIT_TAG v1.3.3 GIT_SHALLOW TRUE)
FetchContent_Declare(json URL https://github.com/nlohmann/json/releases/download/v3.11.3/json.tar.xz)
FetchContent_MakeAvailable(json)
# imgui and minhook (at this tag) have no CMake project of their own: fetch the sources and compile them here.
FetchContent_GetProperties(imgui)
if(NOT imgui_POPULATED)
  FetchContent_Populate(imgui)
endif()
FetchContent_GetProperties(minhook)
if(NOT minhook_POPULATED)
  FetchContent_Populate(minhook)
endif()

add_library(Remnant2Overlay.InGame SHARED
  src/dllmain.cpp
  src/hooks.cpp
  src/log.cpp
  src/state.cpp
  src/ui.cpp
  ${imgui_SOURCE_DIR}/imgui.cpp
  ${imgui_SOURCE_DIR}/imgui_draw.cpp
  ${imgui_SOURCE_DIR}/imgui_tables.cpp
  ${imgui_SOURCE_DIR}/imgui_widgets.cpp
  ${imgui_SOURCE_DIR}/backends/imgui_impl_win32.cpp
  ${imgui_SOURCE_DIR}/backends/imgui_impl_dx12.cpp
  ${minhook_SOURCE_DIR}/src/buffer.c
  ${minhook_SOURCE_DIR}/src/hook.c
  ${minhook_SOURCE_DIR}/src/trampoline.c
  ${minhook_SOURCE_DIR}/src/hde/hde64.c
)

target_include_directories(Remnant2Overlay.InGame PRIVATE
  ${imgui_SOURCE_DIR}
  ${imgui_SOURCE_DIR}/backends
  ${minhook_SOURCE_DIR}/include
)
target_compile_definitions(Remnant2Overlay.InGame PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX UNICODE _UNICODE)
target_link_libraries(Remnant2Overlay.InGame PRIVATE nlohmann_json::nlohmann_json d3d12 dxgi d3dcompiler dwmapi shell32 ole32)
set_target_properties(Remnant2Overlay.InGame PROPERTIES PREFIX "")
if(MSVC)
  # Static runtime: nothing extra to install, and no clash with the game's own CRT.
  set_property(TARGET Remnant2Overlay.InGame PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  target_compile_options(Remnant2Overlay.InGame PRIVATE /utf-8 /W3)
else()
  target_link_options(Remnant2Overlay.InGame PRIVATE -static)
endif()

if(DEFINED ENV{LOCALAPPDATA})
  add_custom_command(TARGET Remnant2Overlay.InGame POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E make_directory "$ENV{LOCALAPPDATA}/Remnant2Overlay"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different $<TARGET_FILE:Remnant2Overlay.InGame> "$ENV{LOCALAPPDATA}/Remnant2Overlay/"
    COMMENT "Copying the in-game UI DLL to %LOCALAPPDATA%\\Remnant2Overlay")
endif()
