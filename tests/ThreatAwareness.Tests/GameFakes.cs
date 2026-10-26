// Test double for adapter logic only. The real DLL is separately compiled against game assemblies.
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; } }
    public struct Bounds { public Vector3 center,size; public Bounds(Vector3 c,Vector3 s) { center=c;size=s; } }
}
public interface IModApi { void InitMod(Mod mod); }
public class Mod { public string Path; }
public static class Log { public static void Out(string s) { } public static void Warning(string s) { } }
public static class GameIO { public static string SavePath; public static string GetSaveGameDir() => SavePath; }
public class GameManager { public static bool IsDedicatedServer=true; public static GameManager Instance=new GameManager(); public World World=new World(); }
public class World
{
    public bool Remote; public bool IsRemote()=>Remote;
    public class PlayerList { public List<EntityPlayer> list=new List<EntityPlayer>(); }
    public PlayerList Players=new PlayerList(); public List<Entity> Entities=new List<Entity>(); public int Queries;
    public Entity GetEntity(int id) => Entities.Find(e=>e.entityId==id);
    public List<Entity> GetEntitiesInBounds(System.Type type, UnityEngine.Bounds b, List<Entity> list)
    {
        Queries++;
        foreach(var e in Entities)
            if (type.IsInstanceOfType(e) && System.Math.Abs(e.position.x-b.center.x)<=b.size.x/2 && System.Math.Abs(e.position.y-b.center.y)<=b.size.y/2 && System.Math.Abs(e.position.z-b.center.z)<=b.size.z/2) list.Add(e);
        return list;
    }
}
public class Entity
{
    public int entityId,entityClass; public UnityEngine.Vector3 position; public bool IsDespawned,isUnloaded,markedForUnload,Dead,Spawned=true;
    public bool IsDead()=>Dead; public bool IsSpawned()=>Spawned;
}
public class EntityAlive : Entity { public EntityBuffs Buffs=new EntityBuffs(); }
public class EntityPlayer : EntityAlive { public bool IsSpectator; }
public class EntityPlayerLocal : EntityPlayer { }
public class EntityZombie : EntityAlive { }
public class EntityAnimal : EntityAlive { }
public class EntityClass
{
    public bool bIsEnemyEntity; public string entityClassName;
    public static Dictionary<int,EntityClass> All=new Dictionary<int,EntityClass>();
    public static EntityClass GetEntityClass(int id) => All.TryGetValue(id,out var c)?c:null;
}
public class BuffValue { public bool Remove; }
public class EntityBuffs
{
    public enum BuffStatus { Added }
    public Dictionary<string,BuffValue> All=new Dictionary<string,BuffValue>(); public int Adds,Removes,NetworkAdds,NetworkRemoves;
    public BuffValue GetBuff(string name)=>All.TryGetValue(name,out var b)?b:null;
    public BuffStatus AddBuff(string name, int _instigatorId=-1, bool _netSync=true) { Adds++; if(_netSync) NetworkAdds++; All[name]=new BuffValue(); return BuffStatus.Added; }
    public void RemoveBuff(string name, int _instigatorId=-1, bool _netSync=true) { Removes++; if(_netSync) NetworkRemoves++; All[name].Remove=true; }
}
public class PlatformUserIdentifierAbs { public string CombinedString; }
public class ClientInfo
{
    public int entityId; public bool disconnecting; public PlatformUserIdentifierAbs InternalId;
    public List<string> Replies=new List<string>(); public void SendPackage(NetPackageChat p)=>Replies.Add(p.Text);
}
public class ClientInfoCollection { public Dictionary<int,ClientInfo> All=new Dictionary<int,ClientInfo>(); public ClientInfo ForEntityId(int id)=>All.TryGetValue(id,out var c)?c:null; }
public class ConnectionManager { public static ConnectionManager Instance=new ConnectionManager(); public ClientInfoCollection Clients=new ClientInfoCollection(); }
public enum EChatType { Whisper }
public enum EMessageSender { Server }
public static class GeneratedTextManager { public enum BbCodeSupportMode { NotSupported } }
public class NetPackageChat { public string Text; public NetPackageChat Setup(EChatType t,int id,string text,object recipients,EMessageSender sender,GeneratedTextManager.BbCodeSupportMode mode) { Text=text;return this; } }
public static class NetPackageManager { public static T GetPackage<T>() where T:new()=>new T(); }
public static class ModEvents
{
    public delegate void Handler<T>(ref T data); public delegate EModEventResult Interruptible<T>(ref T data);
    public class Event<T> { private Handler<T> handlers; public void RegisterHandler(Handler<T> h)=>handlers+=h; public void Fire(T data)=>handlers?.Invoke(ref data); }
    public class ChatEvent
    {
        private readonly List<Interruptible<SChatMessageData>> handlers=new();
        public void RegisterHandler(Interruptible<SChatMessageData> h)=>handlers.Add(h);
        public EModEventResult Fire(SChatMessageData data)
        {
            foreach(var handler in handlers) { var result=handler(ref data); if(result!=EModEventResult.Continue) return result; }
            return EModEventResult.Continue;
        }
    }
    public enum EModEventResult { Continue,StopHandlersAndVanilla }
    public struct SGameStartDoneData { }
    public struct SGameUpdateData { }
    public struct SWorldShuttingDownData { }
    public struct SChatMessageData { public ClientInfo ClientInfo; public string Message; }
    public struct SPlayerSpawnedInWorldData { public int EntityId; }
    public struct SPlayerDisconnectedData { public ClientInfo ClientInfo; }
    public struct SEntityKilledData { public Entity KilledEntitiy; }
    public static Event<SGameStartDoneData> GameStartDone=new Event<SGameStartDoneData>();
    public static Event<SGameUpdateData> GameUpdate=new Event<SGameUpdateData>();
    public static ChatEvent ChatMessage=new ChatEvent();
    public static Event<SPlayerSpawnedInWorldData> PlayerSpawnedInWorld=new Event<SPlayerSpawnedInWorldData>();
    public static Event<SPlayerDisconnectedData> PlayerDisconnected=new Event<SPlayerDisconnectedData>();
    public static Event<SEntityKilledData> EntityKilled=new Event<SEntityKilledData>();
    public static Event<SWorldShuttingDownData> WorldShuttingDown=new Event<SWorldShuttingDownData>();
}
