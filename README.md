# Daybook

Daybook tracks daily resource gains and spending in Super Fantasy Kingdom. The strip under the six material icons shows each material’s net change since dawn. Hold **Alt** or the controller’s detail trigger to see opening stock, gains, spending, current stock, and net change. Saved days let you compare how your kingdom used its supplies over a run.

![Daybook’s compact material strip at 1× UI scale](docs/screenshots/0.4.1/compact-1x.png)

## Install

On the [Daybook Thunderstore listing](https://thunderstore.io/c/super-fantasy-kingdom/p/GKJ/Daybook/), choose **Install with App**, select your Super Fantasy Kingdom profile, and choose **Start modded**. To hide the panel, set `ShowOverlay = false` in the profile’s `BepInEx/config/sfk.daybook.cfg`.

## Controller controls

| Action | Xbox | PlayStation |
| --- | --- | --- |
| Open the detailed ledger | Hold RT | Hold R2 |
| Browse older / newer records | While holding RT, press LB / RB | While holding R2, press L1 / R1 |
| Return to the live resource strip | Release RT | Release R2 |

These are the default controls. Daybook follows your in-game detail, pause, and speed bindings and uses the game's button icons when available. The shoulder buttons page only while browsing a ledger with saved records. Keyboard players can hold **Alt** and use **Page Up** or **Page Down**.

## Continuing a run

Hold **Alt** and press **Page Up** to read earlier records from this run. **Page Down** moves toward the live day. Release Alt to return to the live strip. **Earned** and **Spent** count observed increases and decreases in available stock. A reported gain and spend of 8 boards shows 8 in both columns and 0 net. Daybook observes the game's resource notifications and direct setters for coins, shards, stars, super metal, and faith. Mods that silently change stock between samples can hide offsetting transactions.

An asterisk marks partial coverage. Earned and Spent cover observed periods, including resources discovered after dawn. Net compares current stock with known dawn stock and includes changes during an absence. A missing or unreadable journal page is skipped when browsing.

Daybook keeps its journal under `%USERPROFILE%\AppData\LocalLow\Super Fantasy Games\Super Fantasy Kingdom\Daybook`, grouped by game profile, kingdom, and run ID. It writes changed records every two seconds and flushes pending changes on a normal exit. Copy this folder along with your game saves when moving PCs.

On Continue, Daybook can restore live totals when the saved checkpoint, day, and resource balances match. A new or replayed dawn starts a fresh ledger and the earlier attempt stays in history. After a forced close, the last successful write is available. A run first opened with Daybook starts recording at the first observed dawn.

## Resource coverage

The ledger follows your kingdom’s materials and food supplies. For example, the Human kingdom tracks wood and boards, while the Undead kingdom tracks bones, ooze, and blood. Both also track currency and progression. Saved pages retain entries for resources that later become unavailable.

![Saved daily ledger with keyboard paging at 1× UI scale](docs/screenshots/0.4.1/journal-keyboard-1x.png)

![Saved daily ledger with PlayStation paging prompts at 1.5× UI scale](docs/screenshots/0.4.1/journal-controller-1p5x.png)

[Human and Undead gameplay gallery from 0.4.0](docs/screenshots/0.4.0)

## Project layout

| Location | Contents |
| --- | --- |
| [src/Daybook](src/Daybook) | Plugin lifecycle, resource tracking, and in-game HUD |
| [thunderstore](thunderstore) | Package manifest, icon, and public listing text |
| [docs/screenshots](docs/screenshots) | Gameplay images used by GitHub and Thunderstore |
| [CHANGELOG.md](CHANGELOG.md) | Release notes included in the package |

## Build on Windows

Install the .NET SDK, the Windows game, and BepInEx 5 in a mod manager profile. The project targets .NET Standard 2.1 and references the assemblies in your installed game and profile. Set the two paths below for your machine.

```powershell
$gameDir = 'D:\SteamLibrary\steamapps\common\Super Fantasy Kingdom'
$profileDir = Join-Path $env:APPDATA 'Thunderstore Mod Manager\DataFolder\SuperFantasyKingdom\profiles\Daybook Dev'
dotnet build Daybook.csproj -c Release "-p:GameDir=$gameDir" "-p:ProfileDir=$profileDir"
```

The compiled plugin is in `bin/Release/netstandard2.1/Daybook.dll`. To install it into your development profile, close the game and add `-p:DeployMod=true` to the build command.

## Package for Thunderstore

After a successful Release build, run `python package.py` with Python 3 from the repository root. The ZIP in `dist/` contains the plugin, manifest, icon, changelog, and the [Thunderstore listing](thunderstore/README.md).

## Gameplay checks

On Windows, run `python tests/NativeStockChecks/run.py` after packaging to exercise the ZIP's stock hooks in the game's embedded Mono runtime. Pass `--cheat-menu <DLL path>` to check compatibility with the 3× coin reward in ILM/CheatMenu 1.0.3. Both checks run offline.

In a kingdom, compare the compact strip with the Alt ledger after gaining or spending a resource. Check that the comparison resets at dawn, the panel follows the game’s UI scale, and the ledger reflects the active kingdom after switching between Human and Undead.

Run `dotnet run --project tests/JournalChecks -c Release` for the standalone journal checks. For the in-game restart check, earn and spend one material, note the five columns, exit normally, and use Continue. Check the live ledger for the resumed day and use Alt + Page Up to find the earlier record. Repeat at both UI scales.

The 0.3.0 HUD was checked in both kingdoms on Windows with game version 0.8.067 and BepInEx 5.4.23.5. The Undead ledger was checked at 1920 × 1080 with UI scales 1 and 1.5.

## Contributing and license

Bug reports and pull requests are welcome. See the [contribution guide](CONTRIBUTING.md) for local testing and reporting instructions.

The original mod code is available under the [MIT License](LICENSE). Super Fantasy Kingdom, its game assets, and third-party dependencies retain their respective licenses and ownership.
