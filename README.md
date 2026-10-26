# Threat Awareness

A standalone C# accessibility mod for **7 Days to Die dedicated server v3.3**. It shows nearby hostile proximity using four static vanilla buff icons. Players start with warnings **off** and can opt in with `/threat on`. No client installation, external services, database, Harmony patch, or dependency on another mod is used.

This is a development build. Server startup and automated checks are separate from the client acceptance tests in [docs/VALIDATION.md](docs/VALIDATION.md). A missing indicator does not establish that an area is safe: detection covers eligible, currently loaded entities only.

Download an installable ZIP from the [GitHub releases](https://github.com/EdwardRies/7DTD-Threat-Awareness/releases), or build it from source using the instructions below.

| Nearest eligible hostile | Indicator | Vanilla icon |
|---|---|---|
| <=100 m | Caution, yellow | Animal tracker |
| <=50 m | Warning, orange | Zombie |
| <=25 m | High Warning, red | Berserker |
| <=10 m | DANGER, red | Skull |

Only the highest applicable enabled level appears. Levels have distinct icons and text, so color is supplementary. Buffs have no gameplay effects, sound, flashing, blinking, or screen overlays. Text is literal English in the XML; no client localization file is required.

## Player commands

Type in ordinary in-game chat:

- `/threat` displays exactly:

```text
Threat Awareness shows nearby hostiles based on proximity
/threat on
/threat off
/threat status
```

- `/threat on` enables participation; the indicator appears on the next detection cycle.
- `/threat off` disables participation and removes existing indicators on the next game update, independently of the detection timer.
- `/threat status` reports whether warnings are on or off, including the administrator's global switch.

On/off/status replies contain only `Threat Awareness is on.` or `Threat Awareness is off.` The `help` alias remains supported and returns the same short introduction and three commands. Diagnostics about persistence failures go to the server log.

Commands need no administrator permission. Only the authenticated sender's connection can change its setting. The v3.3 interruptible chat hook suppresses vanilla broadcast of matching commands; responses go directly to that client's connection. Other chat passes through. Server logs still record handled chat, and earlier third-party chat handlers can observe it: this is not a promise of secrecy from administrators or other mods.

The installation folder is **`0_ThreatAwareness`** so the game's alphabetical mod loading registers its handler before most other chat handlers. Keep the `0_` folder prefix when installing. If an earlier mod consumes slash commands, use the equivalent `!threat`, `!threat on`, `!threat off`, or `!threat status` fallback. These are also intercepted and replied to privately when they reach Threat Awareness. An earlier mod that blocks both prefixes can still prevent this handler from running.

## Configuration

Edit `Mods/0_ThreatAwareness/ThreatAwareness.xml`. The mod checks for changes every second and applies valid changes immediately, clearing stale states. Detection uses `intervalSeconds` (default 1 second). Invalid edits retain the last valid configuration; invalid configuration at startup uses defaults and logs a warning. Missing file uses defaults.

- `enabled`: global switch; false overrides every individual preference.
- `defaultEnabled`: used only for players without saved preferences (default false, including missing-file and omitted-setting fallbacks). Saved individual choices remain in effect.
- `intervalSeconds`: 0.1 to 60.
- `hysteresisMeters`: 0 to 2 (default 1); de-escalation tolerance only. Escalation is immediate at the next cycle. Warnings always clear beyond the largest enabled range.
- Each of the four `Levels` has `enabled` and `meters`. All distances must be >0 and <=250 and strictly ordered Caution > Warning > HighWarning > Danger, including disabled levels. Invalid or overlapping values reject the edit as a whole.
- `Eligibility zombies` and `hostileAnimals`: enable the two supported categories. Both also require the entity class's `IsEnemyEntity` flag. Optional `Exclude class="..."` entries exclude exact entity class names.

Players, traders, other NPC categories, and passive animals are excluded. Zombies and hostile animals can qualify without attacking or seeing the player. Modded friendly entities incorrectly marked as enemy should be explicitly excluded. There is no line-of-sight check; distance includes height. Disabled participants receive no proximity query. If all levels are disabled, no proximity queries run.

## Preferences and recovery

Preferences live in `<active world save>/ThreatAwareness/preferences.xml`, keyed by `ClientInfo.InternalId.CombinedString` (cross-platform ID when available; otherwise platform ID). Names and temporary entity IDs are never persistent identifiers. Preferences survive death, reconnect, and server restarts within that save. Back up this folder with the save; switching to another world save has separate preferences.

Changes write a flushed temporary file, then atomically replace the primary and retain a `.bak` snapshot. A damaged primary is preserved as `.corrupt-<id>`; a readable backup is restored in memory. If neither file is readable, defaults apply. If storage is unwritable, the individual choice remains active in memory and the server logs a warning. Repair storage before restart to preserve that choice. Identity-less connections receive no detection and cannot save a setting. Manual preference-file edits require a restart.

## Build and package

Requires a .NET SDK supporting `net10.0` tests, **.NET Framework 4.8 developer/reference assemblies**, PowerShell, and a licensed v3.3 dedicated-server installation. No game DLLs are redistributed. On Windows with prerequisites installed:

```powershell
.\build.ps1 -GameDir 'D:\Games\7DaysToDieServer'
```

The script compiles against that installation, runs the tests, and creates `artifacts/0_ThreatAwareness/` and `artifacts/ThreatAwareness-0.1.2.zip`. The source assets remain under `Mods/ThreatAwareness/`; the build gives the installation folder its load-order prefix. Tests are a dependency-free executable; any failing assertion exits unsuccessfully. The game adapter is compiled against the real game DLL separately from its test harness.

Supply your own dedicated-server installation path; no machine-specific path is configured in the project. To run the tests without a game installation:

```powershell
dotnet run --project tests/ThreatAwareness.Tests -c Release
```

Generated packages, build output, logs, and player/server data are excluded from Git. The repository contains source and XML definitions; build packages are generated locally for installation or release distribution.

## Dedicated-server installation

1. Shut down the dedicated server.
2. Copy the complete `artifacts/0_ThreatAwareness` folder into the server's `Mods` folder. `ModInfo.xml`, `ThreatAwareness.dll`, and `ThreatAwareness.xml` must sit directly in `Mods/0_ThreatAwareness`; buff definitions are in `Config/buffs.xml`. When upgrading 0.1.0, replace the old `Mods/ThreatAwareness` folder with the new folder; do not leave both installed. Preserve any intentional administrator overrides while setting `defaultEnabled=false` for opt-in behavior. Preferences remain in the save and do not need migration.
3. Start the server; check for `[ThreatAwareness] Ready` and any XML errors.
4. Join using an unmodified PC client and complete the acceptance checks in `docs/VALIDATION.md`.

The current build explicitly disables its C# handlers outside a dedicated server. Installing it in a singleplayer or locally hosted game's `Mods` folder therefore does not enable detection. Supporting those modes would require adapting local-player identity and command handling and testing the host lifecycle; this is an implementation restriction, not an established limitation of the game engine.

The inspected loader permits dedicated-server DLLs with EAC enabled; client-side C# installation is neither supplied nor required. Real client/EAC acceptance must still be checked. Console/crossplay compatibility is not established.

To remove the mod, set `enabled="false"`, allow connected players' indicators to clear, then shut down and remove its folder. Players offline during removal may have saved indicators; safest cleanup is to retain the disabled mod through their next login before uninstalling. Shutdown and reconnect cleanup cannot clear offline character files. Do not remove another mod or edit player saves to clear indicators.

## Implementation

`Settings.cs` validates configuration and evaluates levels. `Preferences.cs` stores player choices and parses commands. `ModApi.cs` manages lifecycle, main-thread command handling, chunk-based detection, and synchronized buff transitions. All callbacks requiring world changes are queued onto `GameUpdate`; shutdown cleanup runs in the world shutdown event. Detection does not scan the entire world per player. Actual buff presence is reconciled each cycle, including repair of stale/duplicate indicators; unchanged indicators are not reapplied.

See [docs/API-EVIDENCE.md](docs/API-EVIDENCE.md) for inspected v3.3 API details and [docs/VALIDATION.md](docs/VALIDATION.md) for test scope and outstanding client checks.

## License

Released under the [MIT License](LICENSE), allowing personal and commercial use, modification, and redistribution with the copyright and license notice retained. Game binaries and assets are not included and remain subject to their owners' licenses.
