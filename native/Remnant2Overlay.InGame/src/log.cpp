#include "log.h"

#include <windows.h>
#include <shlobj.h>

#include <chrono>
#include <format>
#include <fstream>
#include <mutex>

namespace r2o
{
    std::filesystem::path DataDir()
    {
        static const std::filesystem::path dir = []
        {
            PWSTR local = nullptr;
            std::filesystem::path result;
            if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &local)))
                result = std::filesystem::path(local) / L"Remnant2Overlay";
            CoTaskMemFree(local);
            std::error_code ignored;
            std::filesystem::create_directories(result, ignored);
            return result;
        }();
        return dir;
    }

    void Log(const std::string& message)
    {
        static std::mutex lock;
        std::lock_guard guard(lock);
        std::ofstream out(DataDir() / L"ingame.log", std::ios::app);
        SYSTEMTIME t;
        GetLocalTime(&t);
        out << std::format("{:02}:{:02}:{:02} {}\n", t.wHour, t.wMinute, t.wSecond, message);
    }
}
