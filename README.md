# Remnant 2 Overlay

A Windows companion window for Remnant 2 (Steam) that reads your save, shows which weapons, armor,
mutators, rings, amulets, relics and traits you own or are missing, and later suggests run paths for the missing ones.

Steam's own overlay can't host third-party content, so this is a borderless, always-on-top window.
Run the game in **borderless windowed** mode so the overlay can sit on top of it.

## Projects

| Path | What it is |
| --- | --- |
| `src/Remnant2Overlay.App` | WPF overlay window |
| `src/Remnant2Overlay.SaveReader` | Save decompression, owned-item reader, bundled item list (`Data/items.json`) |
| `src/Remnant2Overlay.Icons` | Finds the game install and extracts icons into the local cache (CUE4Parse) |
| `src/Remnant2Overlay.Cli` | Prints owned/total per category and the owned items from a `profile.sav` |
| `tools/Remnant2Overlay.IconExtractor` | Command-line icon extraction, for re-running or debugging it |
| `native/` | In-game panel: UE4SS C++ mod plus a DirectX 12 ImGui panel (Home in game). See [native/README.md](native/README.md) |
| `tools/` scripts | Standalone save decompression (PowerShell/Python), item list builder, how-to guide |

## Getting started (Windows)

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Run the overlay:
   ```
   dotnet run --project src/Remnant2Overlay.App
   ```

## Using the overlay

- On first start it finds Remnant 2 through your Steam libraries and extracts item icons in the background
  into `%LOCALAPPDATA%\Remnant2Overlay\icons`; tiles show initials until their icon arrives. Later starts only
  extract icons that are still missing, and items with no icon aren't searched again until the game files
  change. Use **Game folder…** if the game isn't found, and **Re-extract icons** to rebuild the cache.
- It finds `profile.sav` under `%USERPROFILE%\Saved Games\Remnant2\Steam` automatically, or use **Open save…**.
- It reloads by itself whenever the game writes the save.
- Owned items have a green border; missing items are dimmed. Hover a tile for where it drops.
- Filter by category, world, owned/missing, or search by name or drop source.
- **Ctrl+Shift+O** hides and shows the overlay. Drag the title bar to move it; the opacity slider is top right.
- **Run planning**: pick your campaign or adventure (it follows the one you're playing). Tiles are colored
  gold when the item is available in that run, purple when it rolled but needs a quest item or unlock first,
  red when it rolled but you already took or skipped it this run, and grey when it isn't in the run.
  Hover for the reason and exact location. **Run map** shows the rolled worlds, locations (with world
  stones), bosses, events, dungeons and vendors that still have something for you. The banner says when a
  run has nothing new left, and the line below it shows story progress and quest items held.
- **Item details**: click a tile (or an item in the Run map) to open a side panel with its requirements and quest
  info: each prerequisite marked ✓ have / ✗ missing, the crafting material, where it drops in this run, whether
  you've finished the quest or event it comes from, and the how-to-get-it note. Right-click opens details in manual mode.
- **Manual mode**: click tiles to mark them owned or missing yourself (marked with ✎), and pick the world
  you rolled in the world filter. Choices are saved in `%LOCALAPPDATA%\Remnant2Overlay\settings.json`.

## Icon extraction notes

### Mappings file (needed for icons)

Remnant 2's assets are cooked with *unversioned properties*, so they can only be read with a mappings file
(`.usmap`) generated from the running game. You only need to do this once (the game hasn't been patched in
a long time, so the file stays valid):

1. Download the latest [UE4SS](https://github.com/UE4SS-RE/RE-UE4SS/releases) release zip.
2. Extract it into `…\steamapps\common\Remnant2\Remnant2\Binaries\Win64` (next to `Remnant2-Win64-Shipping.exe`).
3. In `UE4SS-settings.ini`, set `GuiConsoleEnabled = 1` and `GuiConsoleVisible = 1`.
4. Start Remnant 2 and get to the main menu. In the UE4SS window, open **Dumpers** and click
   **Generate .usmap file** (or press Ctrl+Numpad 6). It writes `Mappings.usmap` into the Win64 folder.
5. Quit the game. Optionally remove UE4SS again (delete `dwmapi.dll` from Win64).
6. In the overlay click **Re-extract icons**. It finds `Mappings.usmap` in the Win64 folder automatically,
   or you can copy it to `%LOCALAPPDATA%\Remnant2Overlay\` or set `UsmapPath` in `settings.json`.

UE4SS is a third-party mod loader that injects into the game; use it offline and at your own risk.

### Matching

Extraction uses [CUE4Parse](https://github.com/FabianFG/CUE4Parse). It looks for textures with "icon" in
the name inside each item's folder, then anywhere whose name mentions the item. If many icons come out
missing, run the command-line tool to debug:

```
dotnet run --project tools/Remnant2Overlay.IconExtractor -- --list
```

Options (the overlay reads the same ones from `UnrealVersion` and `UsmapPath` in `settings.json`):

- `--game GAME_UE5_2` Unreal version to read the paks as.
- `--usmap <file>` mappings file, if the game needs one to read assets.
- `--aes <0x…>` key, if the paks are encrypted.
- `--paks <dir>` game folder, if it isn't found through Steam.
- `--out <dir>` output folder.
- `--force` clear the cache and extract everything again.

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
Mutators use the internal name `MetaGem_*`.

Rolled worlds, locations, loot sources, quest prerequisites and looted state come from
[lib.remnant2.analyzer](https://github.com/AndrewSav/lib.remnant2.analyzer) (MIT), which reads both
`profile.sav` and the `save_N.sav` world saves. If it can't read the saves (for example right after a game
patch), the app falls back to the simple inventory reader above and shows owned/missing only.

The item list also comes from [lib.remnant2.analyzer](https://github.com/AndrewSav/lib.remnant2.analyzer) (MIT, see
`THIRD_PARTY_NOTICES.md`); rebuild it with `python tools/build_item_db.py path/to/db.json`.
