#pragma once

namespace r2o::hooks
{
    /// Hooks the game's DirectX 12 swap chain so the panel is drawn into each frame. Runs on a worker thread.
    bool Install();
}
