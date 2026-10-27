"""Builds src/Remnant2Overlay.SaveReader/Data/items.json from lib.remnant2.analyzer's db.json.

Usage: python tools/build_item_db.py path/to/lib.remnant2.analyzer/db.json
Source: https://github.com/AndrewSav/lib.remnant2.analyzer (MIT, see THIRD_PARTY_NOTICES.md)
"""
import json
import os
import sys

TYPES = {"weapon", "armor", "ring", "amulet", "mutator", "mod", "relic", "trait"}
FIELDS = ["Id", "Name", "Type", "Subtype", "World", "DropType", "DropReference", "Note",
          "Prerequisite", "Difficulty", "Hardcore", "Coop", "Material", "WeaponOrMod", "ProfileId"]

src = json.load(open(sys.argv[1], encoding="utf-8"))
items = [{k: x[k] for k in FIELDS if k in x}
         for x in src
         if x.get("Type") in TYPES and "ProfileId" in x and not x.get("Disabled")]
items.sort(key=lambda x: (x["Type"], x["Id"]))

out = os.path.join(os.path.dirname(__file__), "..", "src", "Remnant2Overlay.SaveReader", "Data", "items.json")
with open(out, "w", encoding="utf-8", newline="\n") as f:
    json.dump(items, f, indent=1, ensure_ascii=False)
    f.write("\n")
print(f"Wrote {len(items)} items to {os.path.normpath(out)}")
