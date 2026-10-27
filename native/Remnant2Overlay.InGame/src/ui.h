#pragma once

namespace r2o::ui
{
    /// Hotkey that shows and hides the panel.
    constexpr int ToggleKey = 0x24; // VK_HOME

    bool IsVisible();
    void Toggle();

    /// Fonts and style; call once after ImGui::CreateContext.
    void Setup();

    /// Draws the panel (or the "loaded" hint) for this frame. Call between ImGui::NewFrame and ImGui::Render.
    void Draw();

    /// True when there is nothing to draw this frame, so the frame can skip rendering entirely.
    bool IsIdle();
}
