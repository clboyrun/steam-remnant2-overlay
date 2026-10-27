# Remnant 2 Overlay

A Windows companion window for Remnant 2 (Steam) that reads your save, shows which weapons, armor,
mutators, rings and amulets you own or are missing, and later suggests run paths for the missing ones.

Steam's own overlay can't host third-party content, so this is a borderless, always-on-top window.
Run the game in **borderless windowed** mode so the overlay can sit on top of it.

## Projects

| Path | What it is |
| --- | --- |
| `src/Remnant2Overlay.App` | WPF overlay window |
| `src/Remnant2Overlay.SaveReader` | Save decompression, owned-item reader, bundled item list (`Data/items.json`) |
| `src/Remnant2Overlay.Cli` | Prints owned/total per category and the owned items from a `profile.sav` |
| `tools/Remnant2Overlay.IconExtractor` | Extracts item icons from your own game install |
| `tools/` scripts | Standalone save decompression (PowerShell/Python), item list builder, how-to guide |

## Getting started (Windows)

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Extract icons from your install (optional, tiles show initials without them):
   ```
   dotnet run --project tools/Remnant2Overlay.IconExtractor -- --paks "C:\Program Files (x86)\Steam\steamapps\common\Remnant2\Remnant2\Content\Paks"
   ```
   Icons go to `%LOCALAPPDATA%\Remnant2Overlay\icons`. If many come out missing, run with `--list` to see
   the icon textures it can find, and see "Icon extraction notes" below.
3. Run the overlay:
   ```
   dotnet run --project src/Remnant2Overlay.App
   ```

## Using the overlay

- It finds `profile.sav` under `%USERPROFILE%\Saved Games\Remnant2\Steam` automatically, or use **Open save…**.
- It reloads by itself whenever the game writes the save.
- Owned items have a green border; missing items are dimmed with a red border. Hover a tile for where it drops.
- Filter by category, world, owned/missing, or search by name or drop source.
- **Ctrl+Shift+O** hides and shows the overlay. Drag the title bar to move it; the opacity slider is top right.
- **Manual mode**: click tiles to mark them owned or missing yourself (marked with ✎), and pick the world
  you rolled in the world filter. Choices are saved in `%LOCALAPPDATA%\Remnant2Overlay\settings.json`.

## Icon extraction notes

The extractor uses [CUE4Parse](https://github.com/FabianFG/CUE4Parse). It looks for textures with "icon" in
the name inside each item's folder, then anywhere whose name mentions the item. Options:

- `--game GAME_UE5_2` Unreal version to read the paks as.
- `--usmap <file>` mappings file, if the game needs one to read assets.
- `--aes <0x…>` key, if the paks are encrypted.
- `--out <dir>` output folder.

Results are listed in `index.json` in the output folder. Icons are game assets and are never committed here.

## Build and test

```
dotnet build
dotnet test
```

The WPF app builds on any OS (`EnableWindowsTargeting`) but only runs on Windows. To run the tests against a
real save, set `REMNANT2_PROFILE` to the path of a copy of `profile.sav`. Save files are personal data and
are ignored by git.

## How the save is read

`.sav` files are a 12-byte header followed by zlib chunks (see `SaveFile.cs`). In `profile.sav`, each
character is a nested blob that starts with the `Character_Master_Player` class path. Owned items appear
inside it as asset paths such as
`/Game/World_Base/Items/Trinkets/Rings/AkariWarBand/Ring_AkariWarBand.Ring_AkariWarBand_C`.
Mutators use the internal name `MetaGem_*`. The item list comes from
[lib.remnant2.analyzer](https://github.com/AndrewSav/lib.remnant2.analyzer) (MIT, see
`THIRD_PARTY_NOTICES.md`); rebuild it with `python tools/build_item_db.py path/to/db.json`.
