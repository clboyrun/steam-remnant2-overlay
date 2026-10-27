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

    /// Reloads the item list when it changes and updates hud.txt for the game's HUD. Call every frame; it's throttled.
    void Tick();

    /// True when there is nothing to draw this frame, so the frame can skip rendering entirely.
    bool IsIdle();
}
