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

local widget = nil
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

-- Writes a colour into a struct field in place. UE4SS 3.0.1 can't take nested Lua tables as struct values (it reads
-- the outer table for every inner field, so colours come out transparent and anchors zero), so every struct here is
-- set field by field, before the widget is built, and the whole widget is rebuilt when the list changes.
local function SetColor(target, color)
    target.R, target.G, target.B, target.A = color.R, color.G, color.B, color.A
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

local function AddRow(tree, list, row)
    local text = StaticConstructObject(StaticFindObject("/Script/UMG.TextBlock"), tree)
    text:SetText(FText(row.text))
    Try("Styling a row", function()
        if gameFont then
            text.Font.FontObject = gameFont.object
            text.Font.TypefaceFontName = gameFont.typeface
        end
        text.Font.Size = FontSize
        text.Justification = JustifyRight
        text.ColorAndOpacity.ColorUseRule = 0 -- UseColor_Specified
        SetColor(text.ColorAndOpacity.SpecifiedColor, LinearColor(row.hex))
        text.ShadowOffset.X, text.ShadowOffset.Y = 1.5, 1.5
        SetColor(text.ShadowColorAndOpacity, { R = 0, G = 0, B = 0, A = 0.85 })
    end)
    local slot = list:AddChildToVerticalBox(text)
    Try("Aligning a row", function() slot.HorizontalAlignment = HAlignRight end)
    return text
end

local built = 0

-- Builds a fresh widget with every row already styled, then adds it to the HUD. Returns false when there's no
-- player yet (title screen, loading).
local function Build(rows)
    local engine = FindFirstOf("Engine")
    if not Valid(engine) or not Valid(engine.GameViewport) then return false end
    local gameInstance = engine.GameViewport.GameInstance
    if not Valid(gameInstance) or not Valid(FindFirstOf("PlayerController")) then return false end
    gameFont = gameFont or FindGameFont()

    local root = StaticConstructObject(StaticFindObject("/Script/UMG.UserWidget"), gameInstance)
    if not Valid(root) then return false end
    local tree = StaticConstructObject(StaticFindObject("/Script/UMG.WidgetTree"), root, FName("WidgetTree"))
    root.WidgetTree = tree
    local canvas = StaticConstructObject(StaticFindObject("/Script/UMG.CanvasPanel"), tree)
    tree.RootWidget = canvas
    local list = StaticConstructObject(StaticFindObject("/Script/UMG.VerticalBox"), tree)

    -- Right edge, a fifth of the way down, growing downwards and leftwards with the text.
    local slot = canvas:AddChildToCanvas(list)
    local layout = slot.LayoutData
    layout.Anchors.Minimum.X, layout.Anchors.Minimum.Y = 1.0, 0.22
    layout.Anchors.Maximum.X, layout.Anchors.Maximum.Y = 1.0, 0.22
    layout.Alignment.X, layout.Alignment.Y = 1.0, 0.0
    layout.Offsets.Left, layout.Offsets.Top = -28.0, 0.0
    slot.bAutoSize = true

    local first = nil
    for _, row in ipairs(rows) do first = first or AddRow(tree, list, row) end

    -- HitTestInvisible: drawn, but every click goes to the game.
    root:SetVisibility(Visibility.HitTestInvisible)
    root:AddToViewport(50)
    widget = root

    built = built + 1
    if built == 1 then
        Log("Added the list to the HUD" .. (gameFont and " (game font)." or " (default font)."))
        -- Read back what the game ended up with, for troubleshooting.
        Try("Reading back the layout", function()
            local a = slot.LayoutData.Anchors.Minimum
            local c = first and first.ColorAndOpacity.SpecifiedColor
            Log(string.format("Anchor %.2f,%.2f; %d rows; first row alpha %s; in viewport %s", a.X, a.Y, #rows,
                c and string.format("%.2f", c.A) or "-", tostring(root:IsInViewport())))
        end)
    end
    return true
end

local lastText = nil

local function Poll()
    local text = ReadHud()
    if not text then return end
    local visible, rows = Parse(text)
    local showing = Valid(widget) and widget:IsInViewport()
    -- Nothing changed, unless a level change or loading screen took the widget away.
    if text == lastText and (showing or not visible) then return end
    if os.time() < retryAt then return end

    if Valid(widget) then Try("Removing the old list", function() widget:RemoveFromParent() end) end
    widget = nil
    if not visible then
        lastText = text
        return
    end
    if Try("Building the list", function() Build(rows) end) and Valid(widget) and widget:IsInViewport() then
        lastText = text
    else
        -- No player yet (menus, loading) or something failed: don't build a new one every poll.
        widget = nil
        retryAt = os.time() + 5
    end
end

Log("Loaded. Waiting for " .. HudFile)
LoopAsync(PollMs, function()
    ExecuteInGameThread(function() Try("Polling", Poll) end)
    return false
end)
