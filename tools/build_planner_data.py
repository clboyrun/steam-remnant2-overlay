"""Builds src/Remnant2Overlay.Companion/wwwroot/data/planner.json, the build planner's item stats.

Usage: python tools/build_planner_data.py path/to/remnant2-toolkit
Source: https://github.com/joshpayette/remnant2-toolkit (GPL-3.0, see THIRD_PARTY_NOTICES.md)

The toolkit keeps its items as TypeScript object literals in src/app/(items)/_constants. They're plain data, so this
reads them with a small literal parser instead of running any of the toolkit's code.
"""
import json
import os
import re
import subprocess
import sys

LISTS = {
    "weapon": "weapon-items", "armor": "armor-items", "ring": "ring-items", "amulet": "amulet-items",
    "relic": "relic-items", "fragment": "relic-fragment-items", "mutator": "mutator-items", "mod": "mod-items",
    "archetype": "archetype-items", "skill": "skill-items", "trait": "trait-items", "concoction": "concoction-items",
}
# Identifiers the lists use as values.
CONSTANTS = {"DEFAULT_TRAIT_AMOUNT": 10, "true": True, "false": False, "null": None, "undefined": None}
STATS = ["damage", "rps", "magazine", "accuracy", "ideal", "falloff", "ammo", "crit", "weakspot", "stagger",
         "armor", "armorPercent", "weight", "weightPercent", "weightThreshold", "health", "healthPercent", "healthCap",
         "stamina", "staminaPercent", "bleedResistance", "fireResistance", "shockResistance", "blightResistance",
         "toxinResistance", "armorStep", "armorStepPercent", "healthStep", "healthStepPercent", "staminaStep",
         "staminaStepPercent", "weightStep", "weightStepPercent", "elementalResistanceStep", "cooldown"]

TOKEN = re.compile(r"""
    \s+ | //[^\n]* | /\*.*?\*/                       # skipped
  | (?P<str>'(?:\\.|[^'\\])*'|"(?:\\.|[^"\\])*"|`(?:\\.|[^`\\])*`)
  | (?P<num>-?\d+(?:\.\d+)?(?:e-?\d+)?)
  | (?P<id>[A-Za-z_$][\w$]*)
  | (?P<punct>[{}\[\]:,+;])
""", re.S | re.X)


def tokens(text):
    pos = 0
    while pos < len(text):
        m = TOKEN.match(text, pos)
        if not m:
            raise ValueError(f"Unexpected {text[pos:pos + 40]!r}")
        pos = m.end()
        if m.lastgroup:
            yield m.lastgroup, m.group(m.lastgroup)


def unquote(s):
    body = s[1:-1]
    return re.sub(r"\\(.)", lambda m: {"n": "\n", "t": "\t"}.get(m.group(1), m.group(1)), body)


class Parser:
    """Reads tokens lazily, so whatever code follows the list is never tokenized."""

    def __init__(self, toks):
        self.toks = toks
        self.next = next(toks, (None, None))

    def peek(self):
        return self.next

    def take(self, value=None):
        tok = self.next
        if value is not None and tok[1] != value:
            raise ValueError(f"Expected {value}, got {tok[1]}")
        self.next = next(self.toks, (None, None))
        return tok

    def value(self):
        kind, text = self.take()
        if text == "{":
            result = {}
            while self.peek()[1] != "}":
                _, key = self.take()
                key = unquote(key) if key[0] in "'\"`" else key
                self.take(":")
                result[key] = self.value()
                if self.peek()[1] == ",":
                    self.take()
            self.take("}")
            v = result
        elif text == "[":
            v = []
            while self.peek()[1] != "]":
                v.append(self.value())
                if self.peek()[1] == ",":
                    self.take()
            self.take("]")
        elif kind == "str":
            v = unquote(text)
        elif kind == "num":
            v = float(text) if any(c in text for c in ".e") else int(text)
        elif kind == "id":
            if text not in CONSTANTS:
                raise ValueError(f"Unknown identifier {text}")
            v = CONSTANTS[text]
        else:
            raise ValueError(f"Unexpected {text}")
        # 'a' + 'b' string concatenation
        while self.peek()[1] == "+":
            self.take()
            v = v + self.value()
        return v


def read_list(path):
    text = open(path, encoding="utf-8").read()
    start = re.search(r"export const \w+\s*:\s*[\w<>\[\]\s|]+=\s*", text).end()
    return Parser(tokens(text[start:])).value()


def where(location):
    if not location:
        return None
    parts = [location.get("world")]
    for key in ("dungeon", "biome", "injectable"):
        v = location.get(key)
        parts += v if isinstance(v, list) else [v]
    parts = list(dict.fromkeys(p for p in parts if p))
    return " · ".join(parts) or None


def to_item(kind, i):
    links = i.get("linkedItems") or {}
    item = {
        "Id": (i.get("saveFileSlug") or "").removesuffix("_C") or f"{kind}:{i['name']}",
        "Name": i["name"],
        "Kind": i["category"] if kind == "armor" else kind,
        "Sub": i.get("type") or i.get("color"),
        "Set": i.get("set"),
        "Dlc": i.get("dlc"),
        "Desc": (i.get("description") or "").strip() or None,
        "MaxLevelBonus": i.get("maxLevelBonus"),
        "Tags": i.get("tags") or None,
        "Where": where(i.get("location")),
        "Stats": {k[0].upper() + k[1:]: i[k] for k in STATS
                  if isinstance(i.get(k), (int, float)) and i[k] != 0 and not (k == "cooldown" and i[k] < 0)},
        "WeightThresholds": i.get("weightThresholds"),
        "LinkedMod": (links.get("mod") or {}).get("name"),
        "LinkedArchetype": (links.get("archetype") or {}).get("name"),
        "LinkedSkills": [s["name"] for s in links.get("skills", [])] or None,
        "LinkedTraits": [{"Name": t["name"], "Points": t["amount"]} for t in links.get("traits", [])] or None,
    }
    return {k: v for k, v in item.items() if v not in (None, {}, [])}


toolkit = sys.argv[1]
constants = os.path.join(toolkit, "src", "app", "(items)", "_constants")
items, seen = [], set()
for kind, file in LISTS.items():
    for raw in read_list(os.path.join(constants, file + ".ts")):
        item = to_item(kind, raw)
        if (item["Kind"], item["Id"]) not in seen:
            seen.add((item["Kind"], item["Id"]))
            items.append(item)

commit = subprocess.run(["git", "-C", toolkit, "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
out = os.path.join(os.path.dirname(__file__), "..", "src", "Remnant2Overlay.Companion", "wwwroot", "data", "planner.json")
os.makedirs(os.path.dirname(out), exist_ok=True)
with open(out, "w", encoding="utf-8", newline="\n") as f:
    json.dump({"Source": f"remnant2-toolkit@{commit or 'unknown'}", "Items": items}, f, ensure_ascii=False,
              separators=(",", ":"))
    f.write("\n")
print(f"Wrote {len(items)} items to {os.path.normpath(out)}")
