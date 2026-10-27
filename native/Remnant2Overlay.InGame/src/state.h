#pragma once
#include <optional>
#include <string>
#include <vector>

namespace r2o
{
    struct Requirement
    {
        std::string Label;
        std::optional<bool> Met; // empty when the save can't tell
    };

    struct Item
    {
        std::string Name, Category, World, Status, Reason, Where, Source, QuestState, Note;
        bool Owned = false;
        bool Manual = false;
        std::vector<Requirement> Requirements;
        std::vector<std::string> Extras;
    };

    /// What the overlay app wrote to ingame-state.json (see InGameState.cs).
    struct State
    {
        std::string Character, Run, Summary, Updated;
        bool NothingLeft = false;
        std::vector<std::string> Quests;
        std::vector<Item> Items;
    };

    /// Reloads ingame-state.json when it has changed. Call once per frame; it only touches the disk about once a second.
    /// Returns true when the state was (re)loaded.
    bool RefreshState(State& state, std::string& error);
}
