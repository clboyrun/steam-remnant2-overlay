// UE4SS C++ mod for Remnant 2 Overlay. UE4SS loads it when the game starts; it loads the in-game panel
// (Remnant2Overlay.InGame.dll, built from native/Remnant2Overlay.InGame), which draws over the game's DirectX 12
// frames and shows what the overlay app wrote to %LOCALAPPDATA%\Remnant2Overlay\ingame-state.json.
//
// The panel is a separate DLL so it can bring its own ImGui and DirectX 12 backend without clashing with the
// copy of ImGui inside UE4SS. This mod is where live game data (current zone, pickups) will be read through
// UE4SS's reflection and handed to the panel.
#include <DynamicOutput/DynamicOutput.hpp>
#include <Mod/CppUserModBase.hpp>

#include <windows.h>

#include <filesystem>

using namespace RC;

namespace
{
    std::filesystem::path ThisDllFolder()
    {
        HMODULE module = nullptr;
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           reinterpret_cast<LPCWSTR>(&ThisDllFolder), &module);
        wchar_t path[MAX_PATH]{};
        GetModuleFileNameW(module, path, MAX_PATH);
        return std::filesystem::path(path).parent_path();
    }

    std::filesystem::path LocalAppDataFolder()
    {
        wchar_t path[MAX_PATH]{};
        DWORD length = GetEnvironmentVariableW(L"LOCALAPPDATA", path, MAX_PATH);
        return length > 0 && length < MAX_PATH ? std::filesystem::path(path) / L"Remnant2Overlay" : std::filesystem::path();
    }
}

class Remnant2OverlayMod : public CppUserModBase
{
public:
    Remnant2OverlayMod()
    {
        ModName = STR("Remnant2Overlay");
        ModVersion = STR("0.1.0");
        ModDescription = STR("In-game panel for missing items, requirements and the current run. F8 shows it.");
        ModAuthors = STR("Remnant 2 Overlay");
    }

    ~Remnant2OverlayMod() override = default;

    // The renderer is created by now, so the panel can hook the swap chain straight away.
    auto on_unreal_init() -> void override { LoadPanel(); }

private:
    static void LoadPanel()
    {
        // Next to main.dll (installed), else the CMake build's copy in %LOCALAPPDATA% (handy while developing).
        for (const auto& folder : {ThisDllFolder(), LocalAppDataFolder()})
        {
            if (folder.empty()) continue;
            const auto dll = folder / L"Remnant2Overlay.InGame.dll";
            if (!std::filesystem::exists(dll)) continue;

            if (LoadLibraryW(dll.c_str()))
                Output::send<LogLevel::Normal>(STR("[Remnant2Overlay] Loaded {}. Press F8 in game.\n"), dll.wstring());
            else
                Output::send<LogLevel::Error>(STR("[Remnant2Overlay] Couldn't load {} (error {}).\n"), dll.wstring(), GetLastError());
            return;
        }
        Output::send<LogLevel::Error>(
            STR("[Remnant2Overlay] Remnant2Overlay.InGame.dll not found next to main.dll. Run native/install-mod.ps1.\n"));
    }
};

#define REMNANT2_OVERLAY_API __declspec(dllexport)
extern "C"
{
    REMNANT2_OVERLAY_API CppUserModBase* start_mod() { return new Remnant2OverlayMod(); }
    REMNANT2_OVERLAY_API void uninstall_mod(CppUserModBase* mod) { delete mod; }
}
