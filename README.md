# Remnant 2 Overlay

A Windows companion window for Remnant 2 (Steam) that reads your save, shows which weapons, armor,
mutators and rings you own or are missing, and later suggests run paths for the missing ones.

## Status

Step 1 of the plan: reading the save.

- `src/Remnant2Overlay.SaveReader`: decompresses `.sav` files and lists owned items per character.
- `src/Remnant2Overlay.Cli`: prints the owned items from a `profile.sav`.
- `tools/`: standalone PowerShell and Python scripts plus a guide for decompressing saves by hand.

## Build and run

Requires the .NET 8 SDK.

```
dotnet test
dotnet run --project src/Remnant2Overlay.Cli -- "C:\path\to\profile.sav"
```

With no argument the CLI looks under `%USERPROFILE%\Saved Games\Remnant2\Steam`.

To run the tests against a real save, set `REMNANT2_PROFILE` to the path of a copy of `profile.sav`.
Save files are personal data, so they're ignored by git.

## How the save is read

`.sav` files are a 12-byte header followed by zlib chunks (see `SaveFile.cs`). In `profile.sav`,
each character is a nested blob that starts with the `Character_Master_Player` class path. Owned
items appear inside it as asset paths such as
`/Game/World_Base/Items/Trinkets/Rings/AkariWarBand/Ring_AkariWarBand.Ring_AkariWarBand_C`.
Mutators use the internal name `MetaGem_*`.
