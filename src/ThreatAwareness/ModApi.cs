using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace ThreatAwareness
{
    public sealed class ModApi : IModApi
    {
        private readonly ConcurrentQueue<Action> pending = new ConcurrentQueue<Action>();
        private readonly List<Entity> nearby = new List<Entity>(128);
        private readonly Dictionary<int, ThreatLevel> states = new Dictionary<int, ThreatLevel>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private Settings settings = new Settings();
        private Preferences preferences;
        private string configPath;
        private string configText;
        private double nextDetection, nextConfig, nextError;
        private bool ready;

        public void InitMod(Mod mod)
        {
            configPath = Path.Combine(mod.Path, "ThreatAwareness.xml");
            ModEvents.GameStartDone.RegisterHandler(OnStart);
            ModEvents.GameUpdate.RegisterHandler(OnUpdate);
            ModEvents.ChatMessage.RegisterHandler(OnChat);
            ModEvents.PlayerSpawnedInWorld.RegisterHandler(OnSpawn);
            ModEvents.PlayerDisconnected.RegisterHandler(OnDisconnect);
            ModEvents.EntityKilled.RegisterHandler(OnKilled);
            ModEvents.WorldShuttingDown.RegisterHandler(OnShutdown);
            Log.Out("[ThreatAwareness] Registered v3.3 host handlers.");
        }

        private void OnStart(ref ModEvents.SGameStartDoneData data)
        {
            // One installed mod instance can visit several local or remote worlds.
            ready = false;
            preferences = null;
            states.Clear();
            while (pending.TryDequeue(out _)) { }
            configText = null;
            settings = new Settings();
            nextDetection = nextConfig = 0;
            if (AuthoritativeWorld == null) return;
            try
            {
                ReloadConfig();
                ready = true;
                Log.Out("[ThreatAwareness] Ready. Local players participate automatically; remote players: /threat.");
            }
            catch (Exception ex) { Warn("Startup failed; detection disabled: " + ex.Message); }
        }

        private ModEvents.EModEventResult OnChat(ref ModEvents.SChatMessageData data)
        {
            if (AuthoritativeWorld == null || data.ClientInfo == null || !PlayerCommand.TryParse(data.Message, out var action)) return ModEvents.EModEventResult.Continue;
            var client = data.ClientInfo;
            // Only the authenticated sending connection can change its own preference.
            pending.Enqueue(() => HandleCommand(client, action));
            return ModEvents.EModEventResult.StopHandlersAndVanilla;
        }

        private void HandleCommand(ClientInfo client, string action)
        {
            if (!Current(client)) return;
            if (!ready) { Reply(client, "Threat Awareness is unavailable."); return; }
            if (action == "help" || (action != "on" && action != "off" && action != "status"))
            { Reply(client, "Threat Awareness shows nearby hostiles based on proximity\n/threat on\n/threat off\n/threat status"); return; }
            if (IsLocalHostPlayer(Player(client)))
            { Reply(client, StateMessage(settings.Enabled)); return; }
            string id = client.InternalId?.CombinedString;
            if (string.IsNullOrWhiteSpace(id))
            {
                // Clear even if an authenticated connection lacks a usable persistent identity.
                if (action == "off") Clear(Player(client));
                Reply(client, action == "off" || action == "status" ? "Threat Awareness is off." : "Threat Awareness is unavailable.");
                return;
            }
            if (action == "status")
            {
                bool enabled = RemotePreferences.Get(id, settings.DefaultEnabled);
                Reply(client, StateMessage(enabled && settings.Enabled));
                return;
            }
            bool on = action == "on";
            // Remove before disk I/O, independently of the detection timer.
            if (!on) Clear(Player(client));
            bool saved = RemotePreferences.Set(id, on);
            if (!saved) Warn("Player preference could not be persisted; the choice applies in memory for this session.");
            Reply(client, StateMessage(on && settings.Enabled));
        }

        private static string StateMessage(bool enabled) => enabled ? "Threat Awareness is on." : "Threat Awareness is off.";

        private void OnUpdate(ref ModEvents.SGameUpdateData data)
        {
            var world = AuthoritativeWorld;
            // Never calculate threats or change replicated buffs in a remote server's world.
            if (!ready || world == null) return;
            // Network/lifecycle callbacks only queue work. All world access is on GameUpdate.
            int count = 0;
            while (count++ < 256 && pending.TryDequeue(out var work))
                try { work(); } catch (Exception ex) { Warn("Command/lifecycle action failed: " + ex.Message); }
            double now = clock.Elapsed.TotalSeconds;
            if (now >= nextConfig) { nextConfig = now + 1; ReloadConfig(); }
            if (now < nextDetection) return;
            nextDetection = now + settings.IntervalSeconds;
            var players = world.Players.list;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null) continue;
                try
                {
                    if (!settings.Enabled || !Participates(player) ||
                        player.IsDead() || player.IsDespawned || player.isUnloaded || player.markedForUnload || !player.IsSpawned() || player.IsSpectator || settings.Range <= 0)
                    { Clear(player); continue; }
                    var previous = states.TryGetValue(player.entityId, out var old) ? old : ThreatLevel.None;
                    SetBuff(player, ThreatEvaluator.Evaluate(Nearest(world, player), previous, settings));
                }
                catch (Exception ex) { Clear(player); Warn("Player detection failed; indicator cleared: " + ex.Message); }
            }
        }

        private static World AuthoritativeWorld
        {
            get
            {
                var world = GameManager.Instance?.World;
                return world != null && !world.IsRemote() ? world : null;
            }
        }

        // A pure singleplayer session never opens the remote-player preference store.
        private Preferences RemotePreferences => preferences ?? (preferences = new Preferences(
            Path.Combine(GameIO.GetSaveGameDir(), "ThreatAwareness", "preferences.xml"), Warn));

        private bool Participates(EntityPlayer player)
        {
            if (IsLocalHostPlayer(player)) return true;
            var client = ConnectionManager.Instance?.Clients.ForEntityId(player.entityId);
            string id = client?.InternalId?.CombinedString;
            return Current(client) && !string.IsNullOrWhiteSpace(id) && RemotePreferences.Get(id, settings.DefaultEnabled);
        }

        private static bool IsLocalHostPlayer(EntityPlayer player) => !GameManager.IsDedicatedServer && player is EntityPlayerLocal;

        private double Nearest(World world, EntityPlayer player)
        {
            nearby.Clear();
            float size = (float)(settings.Range * 2);
            // Caller-owned list avoids using World's shared temporary lists.
            world.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(player.position, new Vector3(size, size, size)), nearby);
            double nearestSquared = double.PositiveInfinity;
            for (int i = 0; i < nearby.Count; i++)
            {
                var entity = nearby[i] as EntityAlive;
                if (entity == null || entity is EntityPlayer || entity.IsDead() || entity.IsDespawned || entity.isUnloaded || entity.markedForUnload || !entity.IsSpawned()) continue;
                var cls = EntityClass.GetEntityClass(entity.entityClass);
                if (cls == null || !cls.bIsEnemyEntity || settings.ExcludedClasses.Contains(cls.entityClassName)) continue;
                if (!(settings.Zombies && entity is EntityZombie) && !(settings.HostileAnimals && entity is EntityAnimal)) continue;
                // Exact three-dimensional position distance after broad-phase chunk query; no line-of-sight test.
                double dx = (double)entity.position.x - player.position.x;
                double dy = (double)entity.position.y - player.position.y;
                double dz = (double)entity.position.z - player.position.z;
                double squared = dx * dx + dy * dy + dz * dz;
                if (squared < nearestSquared) nearestSquared = squared;
            }
            return Math.Sqrt(nearestSquared);
        }

        private static readonly string[] BuffNames = { null, "taCaution", "taWarning", "taHighWarning", "taDanger" };

        private void SetBuff(EntityPlayer player, ThreatLevel desired)
        {
            // The local host's UI reads its own buff state; no network round trip is needed.
            bool netSync = !(player is EntityPlayerLocal);
            for (int i = 1; i < BuffNames.Length; i++)
            {
                var buff = player.Buffs.GetBuff(BuffNames[i]);
                bool active = buff != null && !buff.Remove;
                if (i != (int)desired && active) player.Buffs.RemoveBuff(BuffNames[i], _netSync: netSync);
            }
            if (desired != ThreatLevel.None)
            {
                var buff = player.Buffs.GetBuff(BuffNames[(int)desired]);
                if (buff == null || buff.Remove)
                {
                    var result = player.Buffs.AddBuff(BuffNames[(int)desired], _netSync: netSync);
                    if (result != EntityBuffs.BuffStatus.Added) throw new InvalidOperationException("Cannot apply " + BuffNames[(int)desired] + ": " + result);
                }
            }
            if (desired == ThreatLevel.None) states.Remove(player.entityId);
            else states[player.entityId] = desired;
        }

        private void Clear(EntityPlayer player) { if (player != null) SetBuff(player, ThreatLevel.None); }
        private static EntityPlayer Player(ClientInfo c) => GameManager.Instance?.World?.GetEntity(c.entityId) as EntityPlayer;
        private static bool Current(ClientInfo c) => c != null && !c.disconnecting && ReferenceEquals(ConnectionManager.Instance?.Clients.ForEntityId(c.entityId), c);
        private static void Reply(ClientInfo c, string text) => c.SendPackage(NetPackageManager.GetPackage<NetPackageChat>().Setup(
            EChatType.Whisper, -1, text, null, EMessageSender.Server, GeneratedTextManager.BbCodeSupportMode.NotSupported));

        private void OnSpawn(ref ModEvents.SPlayerSpawnedInWorldData data)
        {
            if (AuthoritativeWorld == null) return;
            int id = data.EntityId;
            pending.Enqueue(() => { states.Remove(id); Clear(GameManager.Instance?.World?.GetEntity(id) as EntityPlayer); });
        }
        private void OnDisconnect(ref ModEvents.SPlayerDisconnectedData data)
        {
            if (AuthoritativeWorld == null) return;
            var client = data.ClientInfo;
            if (client == null) return;
            int id = client.entityId;
            pending.Enqueue(() => { Clear(Player(client)); states.Remove(id); });
        }
        private void OnKilled(ref ModEvents.SEntityKilledData data)
        {
            if (AuthoritativeWorld == null) return;
            var player = data.KilledEntitiy as EntityPlayer;
            if (player != null) pending.Enqueue(() => Clear(player));
        }
        private void OnShutdown(ref ModEvents.SWorldShuttingDownData data)
        {
            ready = false;
            ClearAll();
            preferences = null;
            states.Clear();
            while (pending.TryDequeue(out _)) { }
        }
        private void ClearAll()
        {
            var players = AuthoritativeWorld?.Players.list;
            if (players == null) return;
            for (int i = 0; i < players.Count; i++)
                try { Clear(players[i]); } catch (Exception ex) { Warn("Cleanup failed: " + ex.Message); }
        }
        private void ReloadConfig()
        {
            try
            {
                string text = File.Exists(configPath) ? File.ReadAllText(configPath) : "";
                if (text == configText) return;
                // Remember rejected content to avoid logging the same invalid file every second.
                configText = text;
                var updated = Settings.Load(configPath);
                settings = updated;
                ClearAll();
                states.Clear();
                nextDetection = 0;
                Log.Out("[ThreatAwareness] Configuration loaded; globally " + (settings.Enabled ? "enabled." : "disabled."));
            }
            catch (Exception ex) { Warn("Invalid/unreadable configuration; keeping last valid settings (defaults at startup): " + ex.Message); }
        }
        private void Warn(string message)
        {
            // Shared throttle prevents a failing player or disk from flooding the server log.
            double now = clock.Elapsed.TotalSeconds;
            if (now < nextError) return;
            nextError = now + 10;
            Log.Warning("[ThreatAwareness] " + message);
        }
    }
}
