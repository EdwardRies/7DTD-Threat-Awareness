# v3.3 API inspection

Inspected against a dedicated-server installation of **V 3.3.0 (b18)** and the local game's assemblies on 2026-10-26. Evidence is from assembly metadata and IL via the installation's bundled Mono.Cecil, plus shipped configuration. No game binary or decompiled source is redistributed.

`7DaysToDieServer_Data/Managed/Assembly-CSharp.dll` SHA-256:

```text
D2E0D1431291CBD5808FC4F088D1ED33B6D01758D1F82D33151E70928FDAE479
```

| Area | Verified API / observed behavior |
|---|---|
| Loading | `IModApi.InitMod(Mod)`; root-folder DLL discovery by `Mod.LoadAssemblies`. The dedicated-server branch bypasses client anti-cheat DLL restrictions. |
| Host authority and local player | Both game and dedicated-server assemblies expose `World.IsRemote()` and `EntityPlayerLocal : EntityPlayer`. `World.IsRemote()` returns the inverse of `ConnectionManager.IsServer`, allowing detection only in the authoritative world. Local host players bypass network identities and saved participation preferences. |
| Local lifecycle | The game's `GameManager` start-game coroutine invokes `GameStartDone`; `SaveAndCleanupWorld` invokes `WorldShuttingDown`. State and cached configuration are reset per world, including transitions between a local world and a remote server. |
| Chat | `ModEvents.ChatMessage.RegisterHandler` receives `ref SChatMessageData` and returns `EModEventResult`. The `StopHandlersAndVanilla` result prevents network broadcast in `GameManager.ChatMessageServer`. Older `bool ChatMessage(ClientInfo, ...)` examples do not describe this installed build. |
| Chat caveat | The server still logs handled messages. A local `ChatMessageClient` call occurs before the broadcast stop check, so a local host can see guest commands even when broadcast to other guests is suppressed. Earlier mod handlers may already have observed the message. Local-player participation does not require commands. |
| Chat coexistence | `ModManager.loadModsFromFolder` sorts directory paths using `Array.Sort<string>` before loading. `ModEventAbs.RegisterHandler` appends to the receiver list; `ModEventInterruptible.Invoke` stops on the first non-Continue result. Another mod that consumes all slash messages can prevent later handlers from receiving commands. Package folder `0_ThreatAwareness` registers early; `!threat` is available if an earlier slash guard still exists. |
| Private reply | `ClientInfo.SendPackage(NetPackageManager.GetPackage<NetPackageChat>().Setup(...))` targets one connection. The installed `BbCodeSupportMode` enum uses `NotSupported`, not `None`. |
| Identity | `ClientInfo.InternalId` returns `CrossplatformId ?? PlatformId`. `PlatformUserIdentifierAbs.CombinedString` includes platform and ID. |
| Save directory | `GameIO.GetSaveGameDir()` returns the current save location; preferences are stored beneath it. |
| Lifecycle | `GameStartDone`, `GameUpdate`, `PlayerSpawnedInWorld`, `PlayerDisconnected`, `EntityKilled`, `WorldShuttingDown` use typed `ref` data callbacks. `SEntityKilledData.KilledEntitiy` is spelled that way in the game API. |
| Broad-phase query | `World.GetEntitiesInBounds(Type, Bounds, List<Entity>)` visits intersecting chunks and fills the caller's list. It does not enumerate all world entities per player. Exact squared 3-D distance filters candidates afterward. |
| Eligibility | `EntityZombie`, `EntityAnimal`, `EntityClass.GetEntityClass`, `bIsEnemyEntity`, `entityClassName`; dead/unloaded/despawned/marked-for-unload/spawn-state checks are available. Shipped `entityclasses.xml` marks the hostile animal template with `IsEnemyEntity=true` and passive classes false. |
| Buff operations | `EntityBuffs.AddBuff(string, int=-1, bool netSync=true, bool fromElectrical=false, float duration=-1)` and `RemoveBuff(string, int=-1, bool netSync=true)`. Add/remove network paths build `NetPackageAddRemoveBuff`. |
| Local buffs | The same add/remove APIs accept `_netSync=false`. Local host indicators update local buff state directly; remote players retain synchronized add/remove operations. |
| Buff state | `RemoveBuff` marks `BuffValue.Remove`; `GetBuff` can still return that pending-removal value. The adapter checks `Remove`, so it does not mistake pending removal for an active indicator. |
| XML synchronization | `WorldStaticData` registers `buffs` in its XML load table. `SendXmlsToClient` sends selected configuration through `NetPackageConfigFile`; client processing calls `WorldStaticData.ReceivedConfigFile`. The live server's `ConfigsDump/buffs.xml` contains all four patched definitions. Actual buff synchronization/rendering remains an acceptance check with a real client. |
| Vanilla icons | The four icon names in the mod appear in the shipped v3.3 `Data/Config/buffs.xml`. `icon_color`, `icon_blink`, `display_value_key`, `display_value`, `stack_type`, and `remove_on_death` are used by vanilla buffs. |
| Text fallback | `Localization.Get` returns the supplied key when no entry exists (normally; diagnostic localization-check mode adds a prefix). Literal English XML labels avoid relying on synchronization of a separate localization file. |

Static evidence establishes available APIs and intended transport, not player-visible behavior. The actual DLL is compiled against these game assemblies. The test doubles are used only for deterministic adapter tests and are never packaged with the mod.
