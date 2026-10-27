#include "state.h"
#include "log.h"

#include <windows.h>

#include <fstream>
#include <nlohmann/json.hpp>

namespace r2o
{
    namespace
    {
        std::string Str(const nlohmann::json& j, const char* key)
        {
            auto it = j.find(key);
            return it != j.end() && it->is_string() ? it->get<std::string>() : std::string();
        }

        bool Bool(const nlohmann::json& j, const char* key)
        {
            auto it = j.find(key);
            return it != j.end() && it->is_boolean() && it->get<bool>();
        }

        std::vector<std::string> Strings(const nlohmann::json& j, const char* key)
        {
            std::vector<std::string> result;
            if (auto it = j.find(key); it != j.end() && it->is_array())
                for (const auto& s : *it)
                    if (s.is_string()) result.push_back(s.get<std::string>());
            return result;
        }

        State Parse(const nlohmann::json& j)
        {
            State state;
            state.Character = Str(j, "character");
            state.Run = Str(j, "run");
            state.Summary = Str(j, "summary");
            state.Updated = Str(j, "updated");
            state.NothingLeft = Bool(j, "nothingLeft");
            state.Quests = Strings(j, "quests");
            if (auto items = j.find("items"); items != j.end() && items->is_array())
            {
                for (const auto& i : *items)
                {
                    Item item{
                        .Name = Str(i, "name"), .Category = Str(i, "category"), .World = Str(i, "world"),
                        .Status = Str(i, "status"), .Reason = Str(i, "reason"), .Where = Str(i, "where"),
                        .Source = Str(i, "source"), .QuestState = Str(i, "questState"), .Note = Str(i, "note"),
                        .Owned = Bool(i, "owned"), .Manual = Bool(i, "manual"),
                    };
                    if (auto reqs = i.find("requirements"); reqs != i.end() && reqs->is_array())
                    {
                        for (const auto& r : *reqs)
                        {
                            Requirement req{.Label = Str(r, "label")};
                            if (auto met = r.find("met"); met != r.end() && met->is_boolean()) req.Met = met->get<bool>();
                            item.Requirements.push_back(std::move(req));
                        }
                    }
                    item.Extras = Strings(i, "extras");
                    state.Items.push_back(std::move(item));
                }
            }
            return state;
        }
    }

    bool RefreshState(State& state, std::string& error)
    {
        static ULONGLONG nextCheck = 0;
        static std::filesystem::file_time_type loadedTime{};

        ULONGLONG now = GetTickCount64();
        if (now < nextCheck) return false;
        nextCheck = now + 1000;

        const auto path = DataDir() / L"ingame-state.json";
        std::error_code ec;
        auto time = std::filesystem::last_write_time(path, ec);
        if (ec)
        {
            error = "No item list yet. Start the Remnant 2 Overlay app; it writes the list this panel shows.";
            return false;
        }
        if (time == loadedTime) return false;

        try
        {
            std::ifstream in(path);
            state = Parse(nlohmann::json::parse(in));
            loadedTime = time;
            error.clear();
            return true;
        }
        catch (const std::exception& e)
        {
            // Usually a write in progress; retried on the next check.
            error = std::string("Couldn't read the item list: ") + e.what();
            Log(error);
            return false;
        }
    }
}
