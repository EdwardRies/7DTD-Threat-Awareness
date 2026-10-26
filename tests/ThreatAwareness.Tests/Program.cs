using ThreatAwareness;
using System.Reflection;
using System.Xml.Linq;

int checks=0;
void Check(bool condition,string label) { if(!condition) throw new Exception(label); checks++; Console.WriteLine("PASS "+label); }
var temp=Path.Combine(Path.GetFullPath("artifacts"),"ThreatAwareness-tests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var s=new Settings { HysteresisMeters=0 };
    Check(!s.DefaultEnabled,"built in preference defaults off");
    Check(!Settings.Load(Path.Combine(temp,"missing.xml")).DefaultEnabled,"missing config defaults off");
    foreach(var pair in new[]{(100d,ThreatLevel.Caution),(50d,ThreatLevel.Warning),(25d,ThreatLevel.HighWarning),(10d,ThreatLevel.Danger),(100.001,ThreatLevel.None),(0d,ThreatLevel.Danger)})
        Check(ThreatEvaluator.Evaluate(pair.Item1,ThreatLevel.None,s)==pair.Item2,"boundary "+pair.Item1);
    Check(ThreatEvaluator.Evaluate(double.PositiveInfinity,ThreatLevel.Danger,s)==ThreatLevel.None,"no hostiles clears");
    Check(ThreatEvaluator.Evaluate(double.NaN,ThreatLevel.None,s)==ThreatLevel.None,"invalid position distance");
    s.HysteresisMeters=1;
    Check(ThreatEvaluator.Evaluate(10.5,ThreatLevel.Danger,s)==ThreatLevel.Danger,"modest de-escalation hysteresis");
    Check(ThreatEvaluator.Evaluate(11.01,ThreatLevel.Danger,s)==ThreatLevel.HighWarning,"leave hysteresis band");
    Check(ThreatEvaluator.Evaluate(8,ThreatLevel.Caution,s)==ThreatLevel.Danger,"immediate escalation");
    Check(ThreatEvaluator.Evaluate(100.1,ThreatLevel.Caution,s)==ThreatLevel.None,"no hysteresis beyond detection range");
    s.Levels[4]=false;
    Check(ThreatEvaluator.Evaluate(8,ThreatLevel.Danger,s)==ThreatLevel.HighWarning,"disabled danger falls back");
    s.Enabled=false;
    Check(ThreatEvaluator.Evaluate(8,ThreatLevel.Danger,s)==ThreatLevel.None,"global disable wins");
    var config=Path.Combine(temp,"ThreatAwareness.xml");
    File.WriteAllText(config,"<ThreatAwareness/>");Check(!Settings.Load(config).DefaultEnabled,"omitted preference default is off");
    File.WriteAllText(config,"<ThreatAwareness><Levels><Danger meters='50'/></Levels></ThreatAwareness>");
    try { Settings.Load(config); Check(false,"overlap rejected"); } catch(FormatException) { Check(true,"overlap rejected"); }
    foreach(string invalid in new[]{"NaN","Infinity","0","251"})
    {
        File.WriteAllText(config,$"<ThreatAwareness><Levels><Caution meters='{invalid}'/></Levels></ThreatAwareness>");
        try { Settings.Load(config); Check(false,"invalid distance "+invalid); } catch(FormatException) { Check(true,"invalid distance "+invalid); }
    }
    foreach(string attributes in new[]{"intervalSeconds='0'","intervalSeconds='61'","hysteresisMeters='3'","hysteresisMeters='NaN'"})
    {
        File.WriteAllText(config,$"<ThreatAwareness {attributes}/>");
        try { Settings.Load(config); Check(false,"invalid option "+attributes); } catch(FormatException) { Check(true,"invalid option "+attributes); }
    }
    Check(!PlayerCommand.TryParse("/threatening off",out _),"unrelated command passes through");
    Check(PlayerCommand.TryParse(" /THREAT Off ",out var action)&&action=="off","case insensitive exact token");
    Check(PlayerCommand.TryParse("!THREAT help",out var help)&&help=="help","fallback help command");
    Check(!PlayerCommand.TryParse("!threatening on",out _),"unrelated fallback command passes through");
    ModEvents.EModEventResult SlashGuard(ref ModEvents.SChatMessageData data) => data.Message.StartsWith("/") ? ModEvents.EModEventResult.StopHandlersAndVanilla : ModEvents.EModEventResult.Continue;
    bool reached=false;
    var blockedChat=new ModEvents.ChatEvent();blockedChat.RegisterHandler(SlashGuard);
    blockedChat.RegisterHandler((ref ModEvents.SChatMessageData data)=> { reached=PlayerCommand.TryParse(data.Message,out _);return reached?ModEvents.EModEventResult.StopHandlersAndVanilla:ModEvents.EModEventResult.Continue; });
    blockedChat.Fire(new(){Message="/threat help"});Check(!reached,"earlier blanket slash guard reproduces original conflict");
    blockedChat.Fire(new(){Message="!threat help"});Check(reached,"fallback bypasses earlier blanket slash guard");
    var prefFile=Path.Combine(temp,"preferences.xml");
    var prefs=new Preferences(prefFile,Console.WriteLine);
    Check(prefs.Get("EOS_A",true),"missing preference uses default");
    Check(prefs.Set("EOS_A",false),"atomic first save");
    Check(prefs.Set("EOS_B",true),"atomic replacement save");
    prefs=new Preferences(prefFile,_=>{});
    Check(!prefs.Get("EOS_A",true)&&prefs.Get("EOS_B",false),"preferences survive new instance independently");
    File.WriteAllText(prefFile,"broken XML");
    prefs=new Preferences(prefFile,_=>{});
    Check(!prefs.Get("EOS_A",true),"corrupt main recovers backup");
    Check(Directory.GetFiles(temp,"preferences.xml.corrupt-*").Length==1,"damaged file preserved");
    Check(prefs.Set("EOS_C",false),"save after corruption recovery");
    File.WriteAllText(prefFile,"broken"); File.WriteAllText(prefFile+".bak","broken");
    prefs=new Preferences(prefFile,_=>{});
    Check(prefs.Get("EOS_A",true),"both corrupted use defaults");
    var blocked=Path.Combine(temp,"not-a-directory");File.WriteAllText(blocked,"file");
    prefs=new Preferences(Path.Combine(blocked,"prefs.xml"),_=>{});
    Check(!prefs.Set("EOS_A",false)&&!prefs.Get("EOS_A",true),"failed write retains session preference and reports failure");

    // Adapter behavior with a controlled world; this does not verify real networking or rendering.
    File.Copy(Path.GetFullPath("Mods/ThreatAwareness/ThreatAwareness.xml"),config,true);
    GameIO.SavePath=Path.Combine(temp,"save");
    var api=new ModApi();api.InitMod(new Mod { Path=temp });ModEvents.GameStartDone.Fire(new());
    ModEvents.ChatMessage.RegisterHandler(SlashGuard); // Model early registration before a blanket slash guard.
    var world=GameManager.Instance.World;
    EntityClass.All[1]=new EntityClass {bIsEnemyEntity=true,entityClassName="zombie"};
    EntityClass.All[2]=new EntityClass {bIsEnemyEntity=false,entityClassName="passive"};
    var p=new EntityPlayer {entityId=1};var p2=new EntityPlayer {entityId=2};
    world.Players.list.AddRange(new[]{p,p2});world.Entities.AddRange(new Entity[]{p,p2});
    ClientInfo Client(int id,string key) { var c=new ClientInfo {entityId=id,InternalId=new PlatformUserIdentifierAbs {CombinedString=key}};ConnectionManager.Instance.Clients.All[id]=c;return c; }
    var c1=Client(1,"EOS_One");var c2=Client(2,"EOS_Two");
    var near=new EntityZombie {entityId=3,entityClass=1,position=new(8,0,0)};
    world.Entities.AddRange(new Entity[]{near,new EntityZombie {entityClass=1,position=new(40,0,0)},new EntityZombie {entityClass=1,position=new(80,0,0)}});
    void Cycle() { typeof(ModApi).GetField("nextDetection",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(api,0d);ModEvents.GameUpdate.Fire(new()); }
    int Active(EntityPlayer ep)=>ep.Buffs.All.Count(k=>!k.Value.Remove);
    bool Has(EntityPlayer ep,string name)=>ep.Buffs.GetBuff(name) is {Remove:false};
    void Command(ClientInfo c,string text) { Check(ModEvents.ChatMessage.Fire(new(){ClientInfo=c,Message=text})==ModEvents.EModEventResult.StopHandlersAndVanilla,"command consumed: "+text);ModEvents.GameUpdate.Fire(new()); }
    Cycle();Check(Active(p)==0&&Active(p2)==0&&world.Queries==0,"new players start off without spatial queries");
    const string helpText="Threat Awareness shows nearby hostiles based on proximity\n/threat on\n/threat off\n/threat status";
    Command(c1,"/threat");Check(c1.Replies.Last()==helpText&&c2.Replies.Count==0,"bare command returns only the requested introduction and three commands privately");
    Command(c1,"/threat help");Check(c1.Replies.Last()==helpText&&c2.Replies.Count==0,"help alias returns the same minimal command list");
    Command(c1,"!threat help");Check(c1.Replies.Last()==helpText,"fallback help replies");
    Command(c1,"!threat on");Cycle();Check(Has(p,"taDanger")&&Active(p2)==0,"explicit fallback opt in only enables requesting player");
    Check(c1.Replies.Last()=="Threat Awareness is on.","on reply has no extra text");
    Command(c1,"/threat status");Check(c1.Replies.Last()=="Threat Awareness is on.","status reports on concisely");
    // Enable the administrator default for the existing two-player detection scenarios.
    File.WriteAllText(config,File.ReadAllText(config).Replace("defaultEnabled=\"false\"","defaultEnabled=\"true\""));
    typeof(ModApi).GetField("nextConfig",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(api,0d);
    Cycle();Check(Has(p,"taDanger")&&Active(p)==1,"nearest of simultaneous hostiles wins");
    int adds=p.Buffs.Adds;Cycle();Check(p.Buffs.Adds==adds,"unchanged buff not reapplied");
    int queryBefore=world.Queries;Command(c1,"/threat off");Check(Active(p)==0&&Has(p2,"taDanger"),"off clears immediately and isolates other player");
    Check(c1.Replies.Last()=="Threat Awareness is off.","off reply has no extra text");
    Command(c1,"/threat status");Check(c1.Replies.Last()=="Threat Awareness is off.","status reports off concisely");
    Cycle();Check(world.Queries==queryBefore+1,"opted out player skips spatial query");
    Command(c1,"/threat on");Cycle();Check(Has(p,"taDanger"),"enable next detection cycle");
    near.Dead=true;Cycle();Check(Has(p,"taWarning"),"dead hostile excluded");
    world.Entities.RemoveAll(e=>e is EntityZombie);Cycle();Check(Active(p)==0,"disappeared threats clear buff");
    world.Entities.Add(new EntityAnimal {entityClass=2,position=new(1,0,0)});Cycle();Check(Active(p)==0,"passive animal excluded");
    near.Dead=false;near.position=new(0,101,0);world.Entities.Add(near);Cycle();Check(Active(p)==0,"three dimensional height excluded");
    near.position=new(8,0,0);Cycle();p.Dead=true;ModEvents.EntityKilled.Fire(new(){KilledEntitiy=p});ModEvents.GameUpdate.Fire(new());Check(Active(p)==0,"death clears without detection timer");
    p.Dead=false;ModEvents.PlayerSpawnedInWorld.Fire(new(){EntityId=1});Cycle();Check(Has(p,"taDanger"),"respawn resumes saved participation");
    Command(c1,"/threat off");c1.disconnecting=true;ModEvents.PlayerDisconnected.Fire(new(){ClientInfo=c1});ModEvents.GameUpdate.Fire(new());
    c1=Client(1,"EOS_One");ModEvents.PlayerSpawnedInWorld.Fire(new(){EntityId=1});Cycle();Check(Active(p)==0,"reconnect retains opt out");
    Command(c1,"/threat on");Cycle();
    var originalConfig=File.ReadAllText(config);
    void Reload(string text) { File.WriteAllText(config,text);typeof(ModApi).GetField("nextConfig",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(api,0d);Cycle(); }
    Reload(originalConfig.Replace("defaultEnabled=\"true\"","defaultEnabled=\"false\""));
    Check(Has(p,"taDanger")&&Active(p2)==0,"individual preference overrides changed default");
    Reload(originalConfig.Replace("enabled=\"true\" defaultEnabled","enabled=\"false\" defaultEnabled"));
    Check(Active(p)==0&&Active(p2)==0,"live global disable clears both players");
    Command(c1,"/threat on");Cycle();Check(Active(p)==0,"player on cannot override administrator global disable");
    Check(c1.Replies.Last()=="Threat Awareness is off.","global disable reports effective off without additional text");
    Reload(originalConfig.Replace("meters=\"10\"","meters=\"50\""));Check(Active(p)==0,"invalid reload keeps global disable");
    Reload(originalConfig);Check(Has(p,"taDanger"),"valid reload restores opted in player");
    near.markedForUnload=true;Cycle();Check(Active(p)==0,"unloading hostile excluded");near.markedForUnload=false;
    near.IsDespawned=true;Cycle();Check(Active(p)==0,"despawned hostile excluded");near.IsDespawned=false;
    near.Spawned=false;Cycle();Check(Active(p)==0,"not spawned hostile excluded");near.Spawned=true;
    EntityClass.All[3]=new EntityClass {bIsEnemyEntity=true,entityClassName="hostileAnimal"};
    near.Dead=true;var animal=new EntityAnimal {entityClass=3,position=new(8,0,0)};world.Entities.Add(animal);Cycle();Check(Has(p,"taDanger"),"hostile animal included without attacking");
    Reload(originalConfig.Replace("hostileAnimals=\"true\"","hostileAnimals=\"false\""));Check(Active(p)==0,"hostile animal category configurable");
    Reload(originalConfig.Replace("<!-- <Exclude class=\"animalSnake\" /> -->","<Exclude class=\"hostileAnimal\" />"));Check(Active(p)==0,"exact hostile class exclusion");
    Reload(originalConfig);ModEvents.WorldShuttingDown.Fire(new());Check(Active(p)==0&&Active(p2)==0,"shutdown clears all indicators");

    var xml=XDocument.Load("Mods/ThreatAwareness/Config/buffs.xml");var buffs=xml.Descendants("buff").ToArray();
    Check(buffs.Length==4&&buffs.Select(b=>(string)b.Attribute("icon")).Distinct().Count()==4,"four distinct icons");
    Check(!xml.Descendants("effect_group").Any()&&!xml.Descendants("passive_effect").Any()&&!xml.Descendants("triggered_effect").Any(),"buff definitions have no gameplay effects");
    Check(buffs.All(b=>(string)b.Attribute("icon_blink")=="false"),"static indicators");
    Console.WriteLine($"All {checks} checks passed.");
}
finally { Directory.Delete(temp,true); }
