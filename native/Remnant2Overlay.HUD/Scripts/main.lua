-- Remnant 2 Overlay: the on-screen item list as a real widget in the game's HUD (UMG), on the right of the screen.
--
-- The in-game panel (Home) decides what the list shows and writes it to %LOCALAPPDATA%\Remnant2Overlay\hud.txt:
-- first line "show" or "hide", then one "RRGGBB<tab>text" line per row. This mod reads that file twice a second and
-- builds the widget at runtime from the engine's own UMG classes, so no Unreal Editor or .pak is needed.

local Tag = "[Remnant2OverlayHUD] "
local HudFile = (os.getenv("LOCALAPPDATA") or ".") .. "\\Remnant2Overlay\\hud.txt"
local PollMs = 500
local FontSize = 16

-- UMG enum values.
local Visibility = { Collapsed = 1, HitTestInvisible = 3 }
local HAlignRight = 3
local JustifyRight = 2

local widget, list = nil, nil
local rows = {}
local lastText = nil
local gameFont = nil
local warned = {}
local retryAt = 0 -- os.time() before which a failed widget isn't built again

local function Log(message) print(Tag .. message .. "\n") end

-- Logs a failure once per step instead of every poll.
local function Try(step, fn)
    local ok, err = pcall(fn)
    if not ok and not warned[step] then
        warned[step] = true
        Log(step .. " failed: " .. tostring(err))
    end
    return ok
end

local function Valid(object) return object ~= nil and object:IsValid() end

local function ReadHud()
    local file = io.open(HudFile, "rb")
    if not file then return nil end
    local text = file:read("a")
    file:close()
    return text
end

local function Parse(text)
    local lines = {}
    for line in text:gmatch("[^\n]+") do lines[#lines + 1] = line end
    local visible = lines[1] == "show"
    local parsed = {}
    for i = 2, #lines do
        local hex, label = lines[i]:match("^(%x%x%x%x%x%x)\t(.*)$")
        if hex then parsed[#parsed + 1] = { hex = hex, text = label } end
    end
    return visible, parsed
end

-- UMG colours are linear; the panel sends sRGB hex.
local function LinearColor(hex)
    local function channel(i) return (tonumber(hex:sub(i, i + 1), 16) / 255) ^ 2.2 end
    return { R = channel(1), G = channel(3), B = channel(5), A = 1.0 }
end

-- Borrow the font of one of the game's own text widgets so the list looks like part of the HUD.
local function FindGameFont()
    local found = nil
    Try("Finding the game's font", function()
        for _, text in ipairs(FindAllOf("TextBlock") or {}) do
            if text:IsValid() and not text:GetFullName():find("Remnant2OverlayHUD", 1, true) then
                local font = text.Font
                if font.FontObject:IsValid() then
                    found = { object = font.FontObject, typeface = font.TypefaceFontName }
                    break
                end
            end
        end
    end)
    return found
end

local function NewRow()
    local text = StaticConstructObject(StaticFindObject("/Script/UMG.TextBlock"), widget.WidgetTree)
    -- Set before the row is added, so the first build already uses them.
    Try("Styling a row", function()
        if gameFont then
            text.Font.FontObject = gameFont.object
            text.Font.TypefaceFontName = gameFont.typeface
        end
        text.Font.Size = FontSize
        text.Justification = JustifyRight
        text.ShadowOffset = { X = 1.5, Y = 1.5 }
        text.ShadowColorAndOpacity = { R = 0, G = 0, B = 0, A = 0.85 }
    end)
    local slot = list:AddChildToVerticalBox(text)
    Try("Aligning a row", function() slot:SetHorizontalAlignment(HAlignRight) end)
    rows[#rows + 1] = text
    return text
end

local function CreateWidget()
    local engine = FindFirstOf("Engine")
    if not Valid(engine) or not Valid(engine.GameViewport) then return false end
    local gameInstance = engine.GameViewport.GameInstance
    if not Valid(gameInstance) or not Valid(FindFirstOf("PlayerController")) then return false end

    gameFont = gameFont or FindGameFont()
    rows = {}

    widget = StaticConstructObject(StaticFindObject("/Script/UMG.UserWidget"), gameInstance, FName("Remnant2OverlayHUD"))
    if not Valid(widget) then return false end
    local tree = StaticConstructObject(StaticFindObject("/Script/UMG.WidgetTree"), widget, FName("WidgetTree"))
    widget.WidgetTree = tree

    local canvas = StaticConstructObject(StaticFindObject("/Script/UMG.CanvasPanel"), tree, FName("Canvas"))
    tree.RootWidget = canvas
    list = StaticConstructObject(StaticFindObject("/Script/UMG.VerticalBox"), tree, FName("List"))

    -- Right edge, a fifth of the way down, growing downwards and leftwards with the text.
    local slot = canvas:AddChildToCanvas(list)
    slot:SetAnchors({ Minimum = { X = 1.0, Y = 0.22 }, Maximum = { X = 1.0, Y = 0.22 } })
    slot:SetAlignment({ X = 1.0, Y = 0.0 })
    slot:SetAutoSize(true)
    slot:SetPosition({ X = -28.0, Y = 0.0 })

    widget:SetVisibility(Visibility.Collapsed)
    widget:AddToViewport(50)
    lastText = nil -- fill it on this poll
    Log("Added the list to the HUD" .. (gameFont and " (game font)." or " (default font)."))
    return true
end

local function Update(text)
    local visible, parsed = Parse(text)
    for i, row in ipairs(parsed) do
        local block = rows[i] or NewRow()
        block:SetText(FText(row.text))
        block:SetColorAndOpacity({ SpecifiedColor = LinearColor(row.hex), ColorUseRule = 0 })
        block:SetVisibility(Visibility.HitTestInvisible)
    end
    for i = #parsed + 1, #rows do rows[i]:SetVisibility(Visibility.Collapsed) end
    -- HitTestInvisible: drawn, but every click goes to the game.
    widget:SetVisibility(visible and Visibility.HitTestInvisible or Visibility.Collapsed)
end

local function Poll()
    local text = ReadHud()
    if not text then return end
    -- Loading screens and level changes remove the widget; build it again when there's a player.
    if not Valid(widget) or not widget:IsInViewport() then
        if os.time() < retryAt then return end
        widget = nil
        if not Try("Creating the widget", CreateWidget) or not Valid(widget) or not widget:IsInViewport() then
            -- No player yet (menus, loading) or something failed: don't build a new one every poll.
            widget = nil
            retryAt = os.time() + 5
            return
        end
    end
    if text == lastText then return end
    if Try("Updating the list", function() Update(text) end) then lastText = text end
end

Log("Loaded. Waiting for " .. HudFile)
LoopAsync(PollMs, function()
    ExecuteInGameThread(function() Try("Polling", Poll) end)
    return false
end)
