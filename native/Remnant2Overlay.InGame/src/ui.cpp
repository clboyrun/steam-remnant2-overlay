#include "ui.h"
#include "log.h"
#include "state.h"

#include <windows.h>
#include <imgui.h>

#include <algorithm>
#include <filesystem>
#include <fstream>
#include <atomic>
#include <set>

namespace r2o::ui
{
    namespace
    {
        constexpr ULONGLONG HintMilliseconds = 8000;

        std::atomic<bool> g_visible{false}; // the full panel is open and takes the mouse and keyboard
        bool g_pinned = false;              // show the list in the game's HUD (see WriteHud)
        ULONGLONG g_hintUntil = 0;
        constexpr int PinnedRows = 15;

        State g_state;
        std::string g_error;
        std::vector<std::string> g_categories{"All"};

        const char* const ShowOptions[] = {"Missing", "Available this run", "Needs quest", "Owned", "All"};
        int g_show = 0;
        int g_category = 0;
        char g_search[64] = "";
        std::string g_selected; // item name, so the selection survives reloads

        // Same palette as the overlay window.
        constexpr ImVec4 Hex(unsigned rgb, float a = 1.0f)
        {
            return {((rgb >> 16) & 0xFF) / 255.0f, ((rgb >> 8) & 0xFF) / 255.0f, (rgb & 0xFF) / 255.0f, a};
        }
        constexpr ImVec4 Text = Hex(0xE8E2D6), Muted = Hex(0x9A9286), Accent = Hex(0xC9A45C);
        constexpr ImVec4 OwnedColor = Hex(0x5FAF6A), AvailableColor = Hex(0xE0B54A), QuestColor = Hex(0x9C7BD9),
                         MissingColor = Hex(0xD2553F);

        ImVec4 StatusColor(const std::string& status)
        {
            if (status == "Owned") return OwnedColor;
            if (status == "Available") return AvailableColor;
            if (status == "NeedsPrerequisite") return QuestColor;
            if (status == "MissedThisRun") return MissingColor;
            return Muted;
        }

        bool ContainsIgnoreCase(const std::string& text, const char* needle)
        {
            if (!*needle) return true;
            auto it = std::search(text.begin(), text.end(), needle, needle + strlen(needle),
                                  [](char a, char b) { return std::tolower((unsigned char)a) == std::tolower((unsigned char)b); });
            return it != text.end();
        }

        bool Matches(const Item& item)
        {
            bool show = false;
            switch (g_show)
            {
            case 0: show = !item.Owned; break;
            case 1: show = !item.Owned && item.Status == "Available"; break;
            case 2: show = !item.Owned && item.Status == "NeedsPrerequisite"; break;
            case 3: show = item.Owned; break;
            default: show = true; break;
            }
            if (!show) return false;
            if (g_category > 0 && item.Category != g_categories[g_category]) return false;
            return ContainsIgnoreCase(item.Name, g_search) || ContainsIgnoreCase(item.Where, g_search)
                   || ContainsIgnoreCase(item.Source, g_search);
        }

        void OnStateLoaded()
        {
            std::set<std::string> categories;
            for (const auto& item : g_state.Items) categories.insert(item.Category);
            std::string current = g_categories[g_category];
            g_categories = {"All"};
            g_categories.insert(g_categories.end(), categories.begin(), categories.end());
            auto it = std::find(g_categories.begin(), g_categories.end(), current);
            g_category = it == g_categories.end() ? 0 : int(it - g_categories.begin());
        }

        // Remembered across game launches: the file exists while "Keep on screen" is ticked.
        std::filesystem::path PinnedFile() { return DataDir() / L"ingame-pinned"; }

        void SetPinned(bool pinned)
        {
            g_pinned = pinned;
            std::error_code ec;
            if (pinned) std::ofstream(PinnedFile()).put('1');
            else std::filesystem::remove(PinnedFile(), ec);
        }

        void Heading(const char* text)
        {
            ImGui::Spacing();
            ImGui::TextColored(Accent, "%s", text);
        }

        void Wrapped(const ImVec4& color, const std::string& text)
        {
            ImGui::PushStyleColor(ImGuiCol_Text, color);
            ImGui::TextWrapped("%s", text.c_str());
            ImGui::PopStyleColor();
        }

        void DrawDetails(const Item& item)
        {
            Wrapped(Text, item.Name);
            Wrapped(Muted, item.Category + " · " + item.World);
            ImGui::Spacing();
            Wrapped(StatusColor(item.Status), item.Reason + (item.Manual ? " (marked manually)" : ""));
            if (!item.Where.empty()) Wrapped(Text, "In this run: " + item.Where);

            if (!item.Source.empty())
            {
                Heading("SOURCE");
                Wrapped(Text, item.Source);
                if (!item.QuestState.empty()) Wrapped(Muted, item.QuestState);
            }

            if (!item.Requirements.empty())
            {
                Heading("REQUIREMENTS");
                for (const auto& r : item.Requirements)
                {
                    const bool known = r.Met.has_value();
                    ImGui::TextColored(!known ? Muted : *r.Met ? OwnedColor : MissingColor, "%s",
                                       !known ? "?" : *r.Met ? "✓" : "✗");
                    ImGui::SameLine();
                    Wrapped(Text, r.Label);
                }
            }
            for (const auto& extra : item.Extras) Wrapped(Muted, "• " + extra);

            if (!item.Note.empty())
            {
                Heading("HOW TO GET IT");
                Wrapped(Text, item.Note);
            }
        }

        void DrawPanel()
        {
            ImGui::SetNextWindowSize({820, 560}, ImGuiCond_FirstUseEver);
            ImGui::SetNextWindowPos({60, 60}, ImGuiCond_FirstUseEver);
            bool open = true;
            if (!ImGui::Begin("Remnant 2 Overlay", &open, ImGuiWindowFlags_NoCollapse))
            {
                ImGui::End();
                return;
            }
            if (!open) g_visible = false;

            if (!g_error.empty() && g_state.Items.empty())
            {
                Wrapped(Muted, g_error);
                ImGui::End();
                return;
            }

            ImGui::TextColored(Accent, "%s", g_state.Character.c_str());
            ImGui::SameLine();
            ImGui::TextColored(Muted, "%s", g_state.Run.c_str());
            Wrapped(g_state.NothingLeft ? MissingColor : AvailableColor, g_state.Summary);
            if (!g_state.Quests.empty() && ImGui::CollapsingHeader("Quests and run"))
                for (const auto& q : g_state.Quests) Wrapped(Muted, q);

            ImGui::SetNextItemWidth(170);
            ImGui::Combo("##show", &g_show, ShowOptions, IM_ARRAYSIZE(ShowOptions));
            ImGui::SameLine();
            ImGui::SetNextItemWidth(140);
            if (ImGui::BeginCombo("##category", g_categories[g_category].c_str()))
            {
                for (int i = 0; i < int(g_categories.size()); i++)
                    if (ImGui::Selectable(g_categories[i].c_str(), i == g_category)) g_category = i;
                ImGui::EndCombo();
            }
            ImGui::SameLine();
            ImGui::SetNextItemWidth(-ImGui::CalcTextSize("Keep on screen").x - ImGui::GetFrameHeight() * 2);
            ImGui::InputTextWithHint("##search", "Search name or where it drops", g_search, sizeof g_search);
            ImGui::SameLine();
            if (bool pinned = g_pinned; ImGui::Checkbox("Keep on screen", &pinned)) SetPinned(pinned);
            if (ImGui::IsItemHovered())
                ImGui::SetTooltip("Show this list on the right side of the game's HUD, open or closed.\n"
                                  "It follows the filters and selection set here.\n"
                                  "Needs the Remnant2OverlayHUD mod (install-mod.ps1 installs it).");

            const Item* selected = nullptr;
            const float listWidth = ImGui::GetContentRegionAvail().x * 0.5f;
            if (ImGui::BeginChild("list", {listWidth, 0}, ImGuiChildFlags_Borders))
            {
                if (ImGui::BeginTable("items", 3, ImGuiTableFlags_RowBg | ImGuiTableFlags_SizingStretchProp))
                {
                    ImGui::TableSetupColumn("Item", ImGuiTableColumnFlags_WidthStretch, 3.0f);
                    ImGui::TableSetupColumn("Type", ImGuiTableColumnFlags_WidthStretch, 1.2f);
                    ImGui::TableSetupColumn("World", ImGuiTableColumnFlags_WidthStretch, 1.6f);
                    int shown = 0;
                    for (int i = 0; i < int(g_state.Items.size()); i++)
                    {
                        const auto& item = g_state.Items[i];
                        if (!Matches(item)) continue;
                        shown++;
                        ImGui::PushID(i);
                        ImGui::TableNextRow();
                        ImGui::TableNextColumn();
                        ImGui::TextColored(StatusColor(item.Status), "●");
                        ImGui::SameLine();
                        const bool isSelected = item.Name == g_selected;
                        if (ImGui::Selectable(item.Name.c_str(), isSelected, ImGuiSelectableFlags_SpanAllColumns))
                            g_selected = item.Name;
                        ImGui::TableNextColumn();
                        ImGui::TextColored(Muted, "%s", item.Category.c_str());
                        ImGui::TableNextColumn();
                        ImGui::TextColored(Muted, "%s", item.World.c_str());
                        ImGui::PopID();
                    }
                    ImGui::EndTable();
                    if (shown == 0) ImGui::TextColored(Muted, "Nothing matches these filters.");
                }
            }
            ImGui::EndChild();

            for (const auto& item : g_state.Items)
                if (item.Name == g_selected) selected = &item;

            ImGui::SameLine();
            if (ImGui::BeginChild("details", {0, 0}, ImGuiChildFlags_Borders))
            {
                if (selected) DrawDetails(*selected);
                else Wrapped(Muted, "Select an item to see its requirements, quest and where to find it.");
            }
            ImGui::EndChild();

            ImGui::End();
        }

        // The game's HUD (the Remnant2OverlayHUD Lua mod) draws the on-screen list from hud.txt. First line "show" or
        // "hide", then one "RRGGBB<tab>text" line per row. ASCII only, so it goes through Lua and FText unchanged.
        std::string Ascii(const std::string& text)
        {
            std::string out;
            for (unsigned char c : text)
                if (c == '\t' || c == '\n' || c == '\r') out += ' ';
                else if (c < 0x80) out += char(c);
                else if ((c & 0xC0) == 0xC0) out += '?'; // first byte of a multi-byte character
            return out;
        }

        std::string HexColor(const ImVec4& c)
        {
            char buffer[8];
            snprintf(buffer, sizeof buffer, "%02X%02X%02X", int(c.x * 255 + 0.5f), int(c.y * 255 + 0.5f), int(c.z * 255 + 0.5f));
            return buffer;
        }

        std::string BuildHud()
        {
            std::string hud = g_pinned ? "show\n" : "hide\n";
            auto row = [&](const ImVec4& color, const std::string& text) { hud += HexColor(color) + "\t" + Ascii(text) + "\n"; };
            if (g_state.Items.empty())
            {
                row(Muted, "Remnant 2 Overlay: start the overlay app");
                return hud;
            }

            std::string title = ShowOptions[g_show];
            if (g_category > 0) title += " - " + g_categories[g_category];
            row(Accent, title);
            if (!g_state.Summary.empty()) row(g_state.NothingLeft ? MissingColor : Muted, g_state.Summary);

            int matching = 0;
            for (const auto& item : g_state.Items)
            {
                if (!Matches(item)) continue;
                if (++matching <= PinnedRows) row(StatusColor(item.Status), item.Name);
            }
            if (matching > PinnedRows) row(Muted, "+" + std::to_string(matching - PinnedRows) + " more");
            if (matching == 0) row(Muted, "Nothing matches these filters");

            for (const auto& item : g_state.Items)
            {
                if (item.Name != g_selected) continue;
                row(Accent, "> " + item.Name);
                row(StatusColor(item.Status), item.Reason);
                for (const auto& r : item.Requirements)
                    if (r.Met == false) row(MissingColor, "Missing: " + r.Label);
            }
            return hud;
        }

        // Rewrites hud.txt when the list changes (checked a few times a second; the write is atomic).
        void WriteHud()
        {
            static ULONGLONG nextCheck = 0;
            static std::string written;
            const ULONGLONG now = GetTickCount64();
            if (now < nextCheck) return;
            nextCheck = now + 250;

            std::string hud = BuildHud();
            if (hud == written) return;
            const auto path = DataDir() / L"hud.txt";
            auto temp = path;
            temp += L".tmp";
            {
                std::ofstream out(temp, std::ios::binary | std::ios::trunc);
                out << hud;
                if (!out) return;
            }
            if (MoveFileExW(temp.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING)) written = std::move(hud);
        }

        void DrawHint()
        {
            const auto* viewport = ImGui::GetMainViewport();
            ImGui::SetNextWindowPos({viewport->WorkPos.x + 20, viewport->WorkPos.y + 20});
            ImGui::SetNextWindowBgAlpha(0.8f);
            ImGui::Begin("##hint", nullptr,
                         ImGuiWindowFlags_NoDecoration | ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoInputs
                             | ImGuiWindowFlags_NoSavedSettings | ImGuiWindowFlags_NoFocusOnAppearing);
            ImGui::TextColored(Accent, "Remnant 2 Overlay loaded.");
            ImGui::TextColored(Text, "Press Home to show or hide it.");
            ImGui::End();
        }
    }

    bool IsVisible() { return g_visible; }

    void Toggle()
    {
        g_visible = !g_visible;
        g_hintUntil = 0;
    }

    bool IsIdle() { return !g_visible && GetTickCount64() > g_hintUntil; }

    void Tick()
    {
        if (RefreshState(g_state, g_error)) OnStateLoaded();
        WriteHud();
    }

    void Setup()
    {
        auto& io = ImGui::GetIO();
        static const std::string ini = (DataDir() / "ingame-imgui.ini").string();
        io.IniFilename = ini.c_str();
        io.ConfigFlags |= ImGuiConfigFlags_NoMouseCursorChange;

        // Segoe UI for text, with Segoe UI Symbol merged in for ● ✓ ✗ and arrows.
        wchar_t windows[MAX_PATH];
        GetWindowsDirectoryW(windows, MAX_PATH);
        const auto fonts = std::filesystem::path(windows) / "Fonts";
        static const ImWchar textRanges[] = {0x0020, 0x00FF, 0x0100, 0x017F, 0x2000, 0x206F, 0};
        static const ImWchar symbolRanges[] = {0x2190, 0x21FF, 0x25A0, 0x25FF, 0x2700, 0x27BF, 0};
        if (ImGui::GetIO().Fonts->AddFontFromFileTTF((fonts / "segoeui.ttf").string().c_str(), 18.0f, nullptr, textRanges))
        {
            ImFontConfig merge;
            merge.MergeMode = true;
            io.Fonts->AddFontFromFileTTF((fonts / "seguisym.ttf").string().c_str(), 18.0f, &merge, symbolRanges);
        }
        else
        {
            Log("Segoe UI not found; using ImGui's default font.");
            io.Fonts->AddFontDefault();
        }

        auto& style = ImGui::GetStyle();
        ImGui::StyleColorsDark(&style);
        style.WindowRounding = 8;
        style.ChildRounding = 6;
        style.FrameRounding = 4;
        style.PopupRounding = 4;
        style.WindowPadding = {12, 10};
        auto* c = style.Colors;
        c[ImGuiCol_Text] = Text;
        c[ImGuiCol_TextDisabled] = Muted;
        c[ImGuiCol_WindowBg] = Hex(0x1A1815, 0.94f);
        c[ImGuiCol_ChildBg] = Hex(0x1A1815, 0.0f);
        c[ImGuiCol_PopupBg] = Hex(0x2A2621, 0.98f);
        c[ImGuiCol_Border] = Hex(0x3A342C);
        c[ImGuiCol_FrameBg] = Hex(0x2A2621);
        c[ImGuiCol_FrameBgHovered] = Hex(0x3A342C);
        c[ImGuiCol_FrameBgActive] = Hex(0x3A342C);
        c[ImGuiCol_TitleBg] = Hex(0x1A1815);
        c[ImGuiCol_TitleBgActive] = Hex(0x2A2621);
        c[ImGuiCol_Header] = Hex(0xC9A45C, 0.25f);
        c[ImGuiCol_HeaderHovered] = Hex(0xC9A45C, 0.35f);
        c[ImGuiCol_HeaderActive] = Hex(0xC9A45C, 0.45f);
        c[ImGuiCol_Button] = Hex(0x2A2621);
        c[ImGuiCol_ButtonHovered] = Hex(0x3A342C);
        c[ImGuiCol_CheckMark] = Accent;
        c[ImGuiCol_TableRowBgAlt] = Hex(0xFFFFFF, 0.03f);

        std::error_code ec;
        g_pinned = std::filesystem::exists(PinnedFile(), ec);
        g_hintUntil = GetTickCount64() + HintMilliseconds;
    }

    void Draw()
    {
        // The game hides the OS cursor, so ImGui draws its own while the panel is open.
        ImGui::GetIO().MouseDrawCursor = g_visible;
        if (g_visible) DrawPanel();
        else if (GetTickCount64() < g_hintUntil) DrawHint();
    }
}
