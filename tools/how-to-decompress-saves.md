# How to decompress Remnant 2 saves yourself

## 1. Copy the saves (never work on the originals)
1. Press Win+R, paste `%USERPROFILE%\Saved Games\Remnant2\Steam` and press Enter.
2. Open the folder with the long number (your Steam ID).
3. Copy `profile.sav` and `save_0.sav` to a work folder, for example `C:\RemnantWork`.

## 2. Run the script
**Option A, PowerShell (nothing to install):** put `Decompress-RemnantSave.ps1` in `C:\RemnantWork`, open PowerShell there and run:
```
powershell -ExecutionPolicy Bypass -File .\Decompress-RemnantSave.ps1 -Path .\profile.sav
```
**Option B, Python:** `python decompress_remnant_save.py profile.sav`

Each run writes two files next to the save:
- `profile.sav.bin`: the decompressed data
- `profile.sav.strings.txt`: every readable text run, with its byte offset

## 3. What's inside the file
- **Outer wrapper:** a 12-byte header (CRC32, size, version), then chunks of up to 128 KB. Each chunk has a 49-byte header and then zlib-compressed data. The script just strips the headers and inflates the chunks.
- **Inside:** Unreal Engine style serialized objects. Items show up as asset paths like
  `/Game/World_Base/Items/Weapons/HandGuns/Standard/SMG/Weapon_SMG.Weapon_SMG_C`.

## 4. Where to look
- **profile.sav** holds your character(s): the inventory of owned items.
  - Weapons: `.../Items/Weapons/...` (`Weapon_*`, with the weapon mods as `Mod_*`)
  - Armor: `.../Items/Armor/...` (`Armor_Head_*`, `Armor_Body_*`, `Armor_Gloves_*`, `Armor_Legs_*`)
  - Rings and amulets: `.../Items/Trinkets/...` (`Ring_*`, `Amulet_*`)
  - Mutators: `.../Items/Gems/...` as `MetaGem_*` (that's the game's internal name for mutators)
  - Relic fragments: `RelicFragment_*`
- **save_0.sav** holds the world state (campaign and adventure), meaning which zones, events and items were rolled. That's what we'll use later for run paths.

## 5. Handy tools
- A hex editor such as [HxD](https://mh-nexus.de/en/hxd/) to browse the `.bin`.
- Open `strings.txt` in any editor and search for a name like `Ring_` to see what you own.
- Try this: get an item in game, quit to menu, decompress again and diff the two `strings.txt` files to see what changed.

## 6. Repacking
An edited file would need its chunks recompressed and the CRC header rebuilt, and a bad save could cost you your character. Treat this as read-only and only work on copies.
