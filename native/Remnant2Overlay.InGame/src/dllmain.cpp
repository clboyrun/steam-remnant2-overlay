#include "hooks.h"
#include "log.h"

#include <windows.h>

namespace
{
    DWORD WINAPI Start(LPVOID)
    {
        // A second copy (e.g. a newer build loaded into the same game) would hook everything twice.
        HANDLE once = CreateMutexW(nullptr, TRUE, L"Local\\Remnant2Overlay.InGame");
        if (GetLastError() == ERROR_ALREADY_EXISTS)
        {
            r2o::Log("Already loaded in this game; restart the game to load a new build.");
            if (once) CloseHandle(once);
            return 0;
        }

        r2o::Log("Loaded. Installing hooks...");
        r2o::Log(r2o::hooks::Install() ? "Hooks installed. Press F8 in game." : "Hook install failed; the panel won't show.");
        return 0; // the mutex stays held for the life of the game
    }
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(module);
        // Keep DllMain short (loader lock): do the real work on a thread.
        if (HANDLE thread = CreateThread(nullptr, 0, Start, nullptr, 0, nullptr)) CloseHandle(thread);
    }
    return TRUE;
}
