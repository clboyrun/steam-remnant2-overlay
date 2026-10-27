#pragma once
#include <filesystem>
#include <string>

namespace r2o
{
    /// %LOCALAPPDATA%\Remnant2Overlay, shared with the overlay app.
    std::filesystem::path DataDir();

    /// Appends a line to ingame.log in the data folder.
    void Log(const std::string& message);
}
