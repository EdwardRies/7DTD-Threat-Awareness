# Validation

## Automated checks

Run from the repository root:

```powershell
dotnet run --project tests/ThreatAwareness.Tests -c Release
```

All **89 checks passed** for version 0.1.2. They cover threshold boundaries, nearest-hostile selection, hysteresis, disabled levels, invalid configuration, command parsing, exact private command replies, preference persistence and corrupt-file recovery, independent player settings, death/respawn/reconnect cleanup, and the absence of gameplay effects in buff XML.

The adapter harness uses controlled game doubles. It reproduces interference from an earlier slash-chat handler and tests the fallback prefix, opt-in defaults, and exclusion of disabled players from proximity calculations. It does not establish real game networking, Unity threading, graphics, or anti-cheat compatibility.

The mod was also compiled against the actual v3.3 dedicated-server assemblies with .NET Framework 4.8: zero warnings and zero errors.

## Game integration status

Validated on a v3.3.0 b18 dedicated test server:

- Mod loading and initialization, with the chat handler registered early.
- Loading all four buff definitions into the game's merged configuration.
- EAC-enabled server startup and successful server registration.
- Valid configuration reloads, global disable, and rejection of overlapping thresholds.
- Clean server shutdown and subsequent startup with version 0.1.2.

A player reported working proximity indicators through Danger as a zombie approached. The initial chat-command conflict was traced to an earlier mod consuming slash commands; early registration and a fallback prefix were added. The current command wording and multiplayer command privacy still require client acceptance testing.

EAC-enabled startup is distinct from validating a real EAC client join. Console/crossplay compatibility is not established. Raw development logs and machine-specific test infrastructure are not distributed with the repository.

## Client acceptance checklist

Use a test dedicated server and an unmodified v3.3 PC client. Keep EAC enabled for this check. No client mod files should be installed.

1. Join without a saved preference and confirm warnings start off. Type `/threat`: confirm it displays exactly the requested proximity description followed by `/threat on`, `/threat off`, and `/threat status`. Type those commands as a non-admin: replies should contain only `Threat Awareness is on.` or `Threat Awareness is off.` Confirm replies go only to you. Also try `!threat` and `!threat status`. With a second player watching global/party chat, confirm commands are not broadcast.
2. Place an eligible living zombie at roughly 80, 40, 20, then 8 meters. Verify yellow Caution, orange Warning, red High Warning, and red DANGER. Confirm distinct icons and readable English labels/tooltips without flashing, sounds, or character-stat changes.
3. Add simultaneous zombies at 80, 40, and 8 meters: only DANGER should appear. Kill/remove the nearest ones and verify de-escalation. Remove all eligible threats: all Threat Awareness indicators should disappear.
4. Test behind a wall and above/below you. Line of sight should not matter; three-dimensional distance should. Cross each threshold slowly: escalation responds next cycle, de-escalation has only 1 meter tolerance by default.
5. While DANGER is visible, `/threat off` should clear it promptly without waiting for a long detection interval. Other players must retain their indicators. `/threat on` should restore the appropriate level at the next cycle.
6. Disable participation, disconnect/reconnect, die/respawn, then restart the server. The individual preference should remain off. Rename a player if convenient: the preference should follow the account, not the name.
7. Leave `defaultEnabled=false` (the supplied default): previously saved choices should remain. A player with no saved preference should inherit off. Set `enabled=false`: all indicators should clear within the one-second configuration reload window even for players whose preference is on. Restore `enabled=true`.
8. Disable a threat level and test fallback to another enabled level. Disable all levels: no indicators. Try overlapping thresholds: the server should log a warning and retain its last valid settings. Restore the supplied configuration.
9. Test hostile animal eligibility and exclusion of passive animals, players, and traders. Sleeping/inactive hostiles should count when loaded; an attacking state is not required.
10. Check the server/client logs for errors and confirm UI readability at the player's usual scale and color settings. With two players, verify that one player's settings never alter the other player's indicators.

Commands processed by earlier third-party mods may still be visible to those mods. Server-side command suppression does not erase administrative chat logs. EAC join, cross-client privacy, labels/icons, real buff replication, crash/restart recovery with saved characters, and console/crossplay support remain unverified until exercised on actual clients.
