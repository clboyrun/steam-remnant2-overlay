# In-game panel (UE4SS mod)

Shows the overlay inside Remnant 2: press **F8** in game to open a panel with your missing items, the run summary,
quest status, and each item's requirements and how to get it. While the panel is open, the mouse and keyboard go
to the panel and not the game.

| Path | What it is |
| --- | --- |
| `Remnant2Overlay.UE4SS` | The UE4SS C++ mod (xmake). UE4SS loads it at game start, and it loads the panel. Live game data will be read here next. |
| `Remnant2Overlay.InGame` | The panel (CMake): a DLL that hooks the game's DirectX 12 swap chain and draws with ImGui. |
| `install-mod.ps1` | Copies both into the game's UE4SS `Mods` folder and enables the mod. |

The panel shows what the overlay app last wrote to `%LOCALAPPDATA%\Remnant2Overlay\ingame-state.json`. The app
rewrites that file whenever the save changes, so **keep the overlay app running** (it can be hidden with
Ctrl+Shift+O) and the panel stays current. It picks up changes within about a second.

## Prerequisites

- Visual Studio 2022 with **Desktop development with C++**.
- [CMake](https://cmake.org/) 3.24+ (Visual Studio's copy works from a Developer PowerShell).
- [xmake](https://xmake.io/) and [Rust](https://rustup.rs/) (RE-UE4SS needs both to build).
- UE4SS installed in the game (you already have it, from generating the mappings file).
- GitHub access to UE4SS's engine headers: RE-UE4SS pulls a private `UEPseudo` submodule. Link your GitHub account
  to an Epic Games account ([Epic's guide](https://www.unrealengine.com/en-US/ue-on-github)), accept the invite to
  the EpicGames organization, then the recursive clone below works.

## Build

1. Clone RE-UE4SS into `native/RE-UE4SS`, at the **same version as the UE4SS in your game** (first line of
   `ue4ss\UE4SS.log`, for example 3.0.1). A mod built against a different version may crash on load.

   ```powershell
   git clone --recursive -b v3.0.1 https://github.com/UE4SS-RE/RE-UE4SS.git native/RE-UE4SS
   ```

2. Build the mod:

   ```powershell
   cd native
   xmake f -m "Game__Shipping__Win64" -y
   xmake
   ```

3. Build the panel:

   ```powershell
   cd native/Remnant2Overlay.InGame
   cmake -B build -G "Visual Studio 17 2022" -A x64
   cmake --build build --config Release
   ```

   If CMake says the generator "does not support platform specification", an earlier run picked NMake and cached
   it: delete the `build` folder and run the commands again.

4. Install both into the game (finds the game through Steam; pass `-GameDir` if it doesn't):

   ```powershell
   .\native\install-mod.ps1
   ```

   This creates `Remnant2\Binaries\Win64\ue4ss\Mods\Remnant2Overlay\` with `dlls\main.dll` (the mod),
   `dlls\Remnant2Overlay.InGame.dll` (the panel), and `enabled.txt`.

5. Start the overlay app, then the game. A "Remnant 2 Overlay loaded" note shows in the corner for a few seconds;
   press F8.

## Troubleshooting

- **Nothing shows up:** look in `ue4ss\UE4SS.log` for `[Remnant2Overlay]` lines, then at
  `%LOCALAPPDATA%\Remnant2Overlay\ingame.log` (written by the panel).
- **"No item list yet":** start the overlay app once; it writes the list the panel shows.
- **DirectX 11:** the panel needs DirectX 12 (the game's default). Remove `-dx11` from the launch options.
- **New build of the panel:** restart the game. A panel already loaded in the game stays loaded.
- The game sees F8 as handled by the panel, so don't bind anything to F8 in game.
