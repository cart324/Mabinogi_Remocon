using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MabiRemote {
public static class J {
    public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 100 }; }
    public static object Parse(string s) { return Serializer().DeserializeObject(s); }
    public static string Json(object o) { return Serializer().Serialize(o); }
    public static object Get(object o, string key) { var d = o as IDictionary<string,object>; object v; return d != null && d.TryGetValue(key,out v) ? v : null; }
    public static string S(object o, string k) { return Convert.ToString(Get(o,k),System.Globalization.CultureInfo.InvariantCulture) ?? ""; }
    public static double N(object o,string k) { double n; return Double.TryParse(S(o,k),System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out n) ? n : 0; }
    public static bool B(object o,string k) { return String.Equals(S(o,k),"true",StringComparison.OrdinalIgnoreCase); }
    public static List<object> Rows(object o) { var a = o as IEnumerable; if (a == null || o is string || o is IDictionary) return new List<object>(); return a.Cast<object>().ToList(); }
    public static object Obj(params object[] a) { var d = new Dictionary<string,object>(); for(int i=0;i<a.Length;i+=2) d[(string)a[i]]=a[i+1]; return d; }
    public static object Unwrap(object o) { return Get(o,"body") ?? o; }
    public static string Error(object o) {
        string e=S(o,"error"); if(e!="")return e+": "+S(o,"message");
        var body=Get(o,"body"); if(body!=null){e=Error(body);if(e!="")return e;}
        string s=S(o,"status"); return s=="rejected"||s=="invalid_body"||s=="unknown_command" ? s+": "+S(o,"message") : "";
    }
}
public class Goal {
    public string Name {get;set;}
    public string Product {get;set;}
    public string Facility {get;set;}
    public bool Enabled {get;set;}
    public int Target {get;set;}
    public string Mode {get;set;}
    public int RunsDone {get;set;}
    public int MaxQueued {get;set;}
    public Goal(){Enabled=true;Target=100;Mode="stock";MaxQueued=3;Product="";}
}
public class StockGoal { public string Name{get;set;} public int Target{get;set;} public bool Gather{get;set;} public StockGoal(){Target=100;Gather=true;} }
public class Landmark { public string Name{get;set;} public string Area{get;set;} public double X{get;set;} public double Y{get;set;} public string Kind{get;set;} }
public class Settings {
    public List<PlaylistEntry> Playlist{get;set;}
    public bool PlaylistRepeat{get;set;}
    public int UiScale{get;set;}
    public List<RecipeDefinition> RecipeDefinitions{get;set;}
    public List<RoutePreset> Routes{get;set;}
    public Dictionary<string,string> MaterialRoutes{get;set;}
    public bool AutoCheckUpdates{get;set;}
    public bool ArrivalNotifications{get;set;}
    public bool DungeonNotifications{get;set;}
    public string CliPath{get;set;}
    public List<Goal> Goals{get;set;}
    public List<StockGoal> Stocks{get;set;}
    public List<Landmark> Landmarks{get;set;}
    public Dictionary<string,string> Icons{get;set;}
    public int FullPercent{get;set;}
    public bool CountStorage{get;set;}
    public string FishName{get;set;}
    public Dictionary<string,int> FacilitySlots{get;set;}
    public Dictionary<string,string> RecipeFacilities{get;set;}
    public Settings(){Playlist=new List<PlaylistEntry>();UiScale=100;RecipeDefinitions=RecipeBook.Defaults();Routes=RouteSharing.Bundled();MaterialRoutes=new Dictionary<string,string>();AutoCheckUpdates=true;ArrivalNotifications=true;DungeonNotifications=true;CliPath=@"C:\Nexon\MabinogiMobile\MabinogiMobile_CLI.exe";Goals=new List<Goal>();Stocks=new List<StockGoal>();Landmarks=new List<Landmark>();Icons=new Dictionary<string,string>();FullPercent=95;FishName="";FacilitySlots=Facilities.Names.ToDictionary(x=>x,x=>7);RecipeFacilities=new Dictionary<string,string>();}
}
public static class Facilities {
    public static readonly string[] Names={"금속 가공 시설","목재 가공 시설","옷감 가공 시설","가죽 가공 시설","약품 가공 시설","식재료 가공 시설"};
    public static string ForRecipe(Settings cfg,string recipe){string value;if(cfg.RecipeFacilities.TryGetValue(recipe,out value))return value;
        string p=Planner.Product(recipe);
        if(p.Contains("가죽"))return Names[3];
        if(p.Contains("목재")||p=="타르"||p=="구름결 막대")return Names[1];
        if(p.Contains("옷감")||p.Contains("실크")||p.Contains("밧줄")||p=="식물 섬유")return Names[2];
        if(p.EndsWith("괴"))return Names[0];
        if(new[]{"마요네즈","밀가루","치즈","면","생크림","물에 불린 콩","두부","두유","숙성된 커다란 고기","물에 불린 쌀","밥","말린 찻잎","발효된 찻잎","헤이즐넛 오일","오트밀"}.Contains(p))return Names[5];
        return Names[4];
    }
    public static int Total(Settings cfg,string name){int n;return cfg.FacilitySlots.TryGetValue(name,out n)?Math.Max(0,n):0;}
    public static int Used(Snapshot s,string name){return s.Works.Count(x=>J.S(x,"FacilityName")==name);}
    public static int Free(Snapshot s,Settings cfg,string name){return Math.Max(0,Total(cfg,name)-Used(s,name));}
    public static int AfterCollection(Snapshot s,Settings cfg,string name){return Math.Max(0,Total(cfg,name)-s.Works.Count(x=>J.S(x,"FacilityName")==name&&!J.B(x,"IsCompleted")));}
    public static int ManualAvailable(Snapshot s,Settings cfg,string name,int reserved){return Math.Max(0,AfterCollection(s,cfg,name)-Math.Max(0,reserved));}
}
public sealed class FacilityTiming {
    public double? NextSeconds, FinalSeconds;
    static double? Seconds(object work){double value;return Double.TryParse(J.S(work,"RemainingSeconds"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value)&&!Double.IsNaN(value)&&!Double.IsInfinity(value)&&value>=0?(double?)value:null;}
    public static FacilityTiming Calculate(IEnumerable<object> works,double elapsed){
        var active=works.Where(x=>!J.B(x,"IsCompleted")).ToList();var result=new FacilityTiming();if(active.Count==0)return result;
        var running=active.FirstOrDefault(x=>J.S(x,"State")=="InProgress");
        double age=running==null?0:Math.Max(0,elapsed);
        var next=Seconds(running??active[0]);if(next.HasValue)result.NextSeconds=Math.Max(0,next.Value-age);
        var durations=active.Select(Seconds).ToList();
        // One facility runs a serial queue. NotStarted seconds are full job durations.
        // Only one job advances at a time, so subtract elapsed time once from the sum.
        if(durations.All(x=>x.HasValue))result.FinalSeconds=Math.Max(0,durations.Sum(x=>x.Value)-age);
        return result;
    }
}
public static class FishNames {
    static readonly HashSet<string> Fish=new HashSet<string>{"황금 잉어","은붕어","브리흐네 잉어","참사랑어","작은 자루퍼","잡어","은어","무지개 송어","자루퍼","황금 송어","금린어","황금 연어","고등어","연어","초롱아귀","황금 메기","메기","어둠유령고기","큰 자루퍼","황금 농어","농어"};
    public static bool IsFish(string name){return Fish.Contains(name);}
}
public static class Storage {
    public static string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MabiRemote");
    public static string LastLoadWarning="";
    public static void Save(Settings s){ Directory.CreateDirectory(Root); string file=Path.Combine(Root,"settings.json"), temp=file+".tmp"; File.WriteAllText(temp,J.Json(s),new UTF8Encoding(false)); if(File.Exists(file))File.Replace(temp,file,file+".bak");else File.Move(temp,file); }
    public static Settings Load(){try{string p=Path.Combine(Root,"settings.json");if(File.Exists(p)){var s=J.Serializer().Deserialize<Settings>(File.ReadAllText(p,Encoding.UTF8));if(s==null || s.Goals==null || s.Stocks==null || s.Landmarks==null || s.Icons==null)throw new Exception("설정 형식 오류");if(s.Playlist==null)s.Playlist=new List<PlaylistEntry>();s.Playlist=s.Playlist.Where(x=>x!=null&&!String.IsNullOrWhiteSpace(x.Title)).ToList();if(s.RecipeDefinitions==null)s.RecipeDefinitions=RecipeBook.Defaults();if(s.Routes==null)s.Routes=new List<RoutePreset>();if(s.MaterialRoutes==null)s.MaterialRoutes=new Dictionary<string,string>();if(s.FacilitySlots==null)s.FacilitySlots=Facilities.Names.ToDictionary(x=>x,x=>7);if(s.RecipeFacilities==null)s.RecipeFacilities=new Dictionary<string,string>();if(!UiSizing.Options.Contains(s.UiScale))s.UiScale=100;s.FullPercent=Math.Max(50,Math.Min(100,s.FullPercent));return s;}}catch(Exception ex){LastLoadWarning="설정을 읽지 못해 기본 설정으로 열었습니다: "+ex.Message;} return new Settings();}
}
public static class ConnectionStatus {
    public static string Disconnected(object status){switch(J.S(status,"reason")){
        case "game_off":return "게임 미실행";
        case "option_off":return "AI 커넥터 비활성화";
        default:return "게임 연결 실패";
    }}
}
public class Reply { public int ExitCode; public object Data; public string Stderr; public void Check(){if(ExitCode!=0)throw new GameCommandException(FriendlyText.Reply(Data,ExitCode),"CLI 종료 코드 "+ExitCode+" / "+J.Json(Data));string e=J.Error(Data);if(e!="")throw new GameCommandException(FriendlyText.Reply(Data,ExitCode),e);} }
public interface IBridge { Task<Reply> Call(string command,object body); }
public class GameBridge : IBridge {
    public string CliPath;
    public GameBridge(string path){CliPath=path;}
    public Task<Reply> Call(string command,object body){
        return Task.Run(async delegate {
            if(!File.Exists(CliPath))throw new FileNotFoundException("게임 CLI를 찾지 못했습니다. 설정에서 실행 파일을 선택하세요.",CliPath);
            string args=command;
            if(body!=null){string text=body as string ?? J.Json(body);args+=" base64:"+Convert.ToBase64String(Encoding.UTF8.GetBytes(text));}
            var si=new ProcessStartInfo(CliPath,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=Path.GetDirectoryName(CliPath)};
            using(var p=new Process()){p.StartInfo=si;p.Start();var output=p.StandardOutput.ReadToEndAsync();var err=p.StandardError.ReadToEndAsync();string text=await output;string stderr=await err;p.WaitForExit();object data;
                try{data=J.Parse(text.Trim().TrimStart('\uFEFF'));}catch{throw new GameCommandException("게임 응답을 읽지 못했습니다. 게임 연결 상태를 확인하세요.","CLI 응답이 JSON이 아닙니다: "+text.Substring(0,Math.Min(300,text.Length)));}
                return new Reply{ExitCode=p.ExitCode,Data=data,Stderr=stderr};
            }
        });
    }
}
public class Snapshot {
    public object Environment,Activity,Inventory;
    public List<object> Items=new List<object>(),Works=new List<object>(),Recipes=new List<object>(),Gatherables=new List<object>();
    public DateTime At=DateTime.MinValue,ActivityAt=DateTime.MinValue;
    public int Count(string name,bool storage){return (int)Items.Where(x=>J.S(x,"DisplayName")==name&&(storage||J.S(x,"Location")=="inventory")).Sum(x=>J.N(x,"Count"));}
    public int Queued(string name){return Works.Count(x=>J.S(x,"DisplayName")==name);}
    public object Section(string name){return J.Get(Activity,name)??Activity;}
    public double Weight {get{return J.Get(Inventory,"CurrentInventoryWeight")!=null?J.N(Inventory,"CurrentInventoryWeight"):J.N(Inventory,"CurrentInventoryWeightAsDecimal");}}
    public double Capacity {get{return J.Get(Inventory,"MaxInventoryWeight")!=null?J.N(Inventory,"MaxInventoryWeight"):J.N(Inventory,"MaxInventoryWeightAsDecimal");}}
    public bool Full(int percent){return Capacity>0&&Weight/Capacity*100>=percent;}
    public bool Fishing {get{var m=J.S(J.Get(Activity,"Mode"),"MainButtonState");return m=="Fishing"||m=="FishingPull";}}
    public string Dungeon {get{return J.S(J.Get(Activity,"Dungeon"),"State");}}
    public bool Busy(bool ownsFishing){
        if(Activity==null || (J.Get(Activity,"combatState")==null && J.Get(Activity,"IsInCombat")==null) || J.Get(Activity,"Dungeon")==null || J.Get(Activity,"Mode")==null)return true;
        if(Dungeon!="NotInDungeon")return true;
        foreach(string k in new[]{"IsDead","IsReviving","IsInCombat"})if(J.B(Section("combatState"),k))return true;
        if(J.B(J.Get(Activity,"Battlefield"),"IsInBattleField")||J.B(Activity,"IsAbyssResultSequencePlaying"))return true;
        if(J.B(J.Get(Activity,"Tutorial"),"IsPlaying")||J.B(J.Get(Activity,"Scenario"),"IsInScenario")||J.B(J.Get(Activity,"Scenario"),"IsSequencePlaying"))return true;
        var dialog=Section("dialogue");if(J.B(dialog,"IsDialoguePlaying")||J.B(dialog,"IsWaitingForSelection"))return true;
        var mode=J.Get(Activity,"Mode");if(J.B(mode,"IsPlayingMiniGame")||J.B(mode,"IsHousingEditMode")||J.S(mode,"SitState")=="Sitting")return true;
        if(J.B(J.Get(Activity,"Performance"),"IsPlaying"))return true;
        if(Fishing)return !ownsFishing;
        return J.B(Section("autoPlay"),"IsAutoPlaying")||J.B(Section("autoTravel"),"IsAutoTraveling")||J.S(mode,"MainButtonState")=="Stop";
    }
}
public class Plan {public string Command,Name,Reason; public Goal Goal; public int Cost; public int GatherTarget; public Action Completed; public Plan(string c,string n,string r,int cost){Command=c;Name=n;Reason=r;Cost=cost;} }
public static class Planner {
    public static string Product(string name){return System.Text.RegularExpressions.Regex.Replace(name??"",@"\([^()]*\)$","").Trim();}
    public static string Product(Goal g){return String.IsNullOrEmpty(g.Product)?Product(g.Name):g.Product;}
    public static int BatchRuns(Snapshot s,Settings cfg,Goal goal){
        var r=s.Recipes.FirstOrDefault(x=>J.S(x,"DisplayName")==goal.Name);if(r==null||J.N(r,"ProducedPerWork")<=0)return 0;
        int slots=RecipeBook.BatchSlots(s,cfg,goal.Name);double needed=goal.Mode=="unlimited"?slots:goal.Mode=="runs"?Math.Max(0,goal.Target-goal.RunsDone):Math.Ceiling(Math.Max(0,goal.Target-s.Count(Product(goal),cfg.CountStorage)-RecipeBook.Pending(s,cfg,Product(goal)))/J.N(r,"ProducedPerWork"));
        return (int)Math.Min(slots,needed);
    }
    private static Plan Recipe(Snapshot s, Settings cfg, string name, string product, int requested, Func<string,bool> ready, HashSet<string> visited){
        if(!visited.Add(name)||visited.Count>16)return null;
        int batch=Math.Min(requested,RecipeBook.BatchSlots(s,cfg,name));if(batch<=0)return null;
        var r=s.Recipes.FirstOrDefault(x=>J.S(x,"DisplayName")==name);var definition=RecipeBook.Find(cfg,name);if(r==null||definition==null)return null;
        if(!J.B(r,"Alterable")&&J.S(r,"Reason")!="not_enough_ingredient")return null;
        bool prepared=true;
        foreach(var ingredient in definition.Ingredients){
            string material=ingredient.Material;double required=(double)ingredient.Count*batch;double owned=s.Count(material,cfg.CountStorage);if(owned>=required)continue;prepared=false;
            double lack=required-owned-RecipeBook.Pending(s,cfg,material);if(lack<=0)continue;
            var children=s.Recipes.Where(x=>cfg.RecipeDefinitions.Any(d=>d.Name==J.S(x,"DisplayName")&&d.Product==material)).OrderByDescending(x=>J.B(x,"Alterable")).ToList();
            foreach(var child in children){double yield=J.N(child,"ProducedPerWork");if(yield<=0)continue;var p=Recipe(s,cfg,J.S(child,"DisplayName"),material,(int)Math.Min(Int32.MaxValue,Math.Ceiling(lack/yield)),ready,new HashSet<string>(visited));if(p!=null){p.Reason+=" → "+product+"용 "+material+" "+required.ToString("0")+"개 준비";return p;}}
            var gather=s.Gatherables.FirstOrDefault(x=>J.S(x,"DisplayName")==material&&J.B(x,"ToolOk"));
            if(gather!=null&&ready("execute_gathering:"+material))return new Plan("execute_gathering",material,"묶음 채집: "+material+" "+required.ToString("0")+"개 준비 (보유 "+owned.ToString("0")+", 채집은 최대 100개 단위)",5){GatherTarget=(int)Math.Min(Int32.MaxValue,required)};
        }
        if(prepared&&J.B(r,"Alterable")&&Facilities.Free(s,cfg,Facilities.ForRecipe(cfg,name))>0&&ready("execute_altering:"+name))return new Plan("execute_altering",name,"묶음 가공 등록: "+product+" · 남은 "+batch+"회분 재료 확보",5);
        return null;
    }

    public static Plan Next(Snapshot s,Settings cfg,bool fish,bool ownsFishing,Dictionary<string,DateTime> cooldown,DateTime now){
        if(s.At==DateTime.MinValue || (now-s.At).TotalSeconds>45 || s.Full(cfg.FullPercent)||s.Busy(ownsFishing))return null;
        Func<string,bool> ready=key=>!cooldown.ContainsKey(key)||cooldown[key]<=now;
        var done=s.Works.FirstOrDefault(x=>J.B(x,"IsCompleted")&&ready("complete_altering_work:"+J.S(x,"DisplayName")));
        if(done!=null)return new Plan("complete_altering_work",J.S(done,"DisplayName"),J.S(done,"FacilityName")+" 완료 가공물 수령",0);
        foreach(var g in cfg.Goals.Where(x=>x.Enabled)){
            if(!ready("execute_altering:"+g.Name))continue;
            var r=s.Recipes.FirstOrDefault(x=>J.S(x,"DisplayName")==g.Name);if(r==null)continue;
            string product=Product(g);int batch=BatchRuns(s,cfg,g);
            if(batch>0){var p=Recipe(s,cfg,g.Name,product,batch,ready,new HashSet<string>());if(p!=null){if(p.Name==g.Name&&p.Command=="execute_altering")p.Goal=g;return p;}}
        }
        foreach(var g in cfg.Stocks.Where(x=>x.Gather&&x.Target>0)){
            if(s.Count(g.Name,cfg.CountStorage)>=g.Target||!ready("execute_gathering:"+g.Name))continue;
            var item=s.Gatherables.FirstOrDefault(x=>J.S(x,"DisplayName")==g.Name);
            if(item!=null&&J.B(item,"ToolOk"))return new Plan("execute_gathering",g.Name,"목표 재고 부족 · 최대 100개 단위 채집",5);
        }
        if(fish && !ownsFishing && !String.IsNullOrWhiteSpace(cfg.FishName) && ready("execute_gathering:"+cfg.FishName)){
            var item=s.Gatherables.FirstOrDefault(x=>J.S(x,"DisplayName")==cfg.FishName);
            if(item!=null&&J.B(item,"ToolOk"))return new Plan("execute_gathering",cfg.FishName,"빈 시간 자동 낚시",5);
        }
        return null;
    }
}
public class DemoBridge : IBridge {
    public List<object> Works=new List<object>(); public Dictionary<string,int> Counts=new Dictionary<string,int>{{"정령의 날개",12051},{"통나무",140},{"목재",12},{"거미줄",80},{"옷감",5},{"연어",4}};
    public MusicTestBridge Music=new MusicTestBridge();
    public bool RejectCollection; public bool Fishing; public string Dungeon="NotInDungeon";
    public List<string> Calls=new List<string>();
    public Task<Reply> Call(string cmd,object body){
        Calls.Add(cmd);object d=null;string n=J.S(body,"displayName");
        if(cmd=="status")d=J.Obj("pipe","connected");
        else if(cmd=="capabilities")d=J.Parse(Program.ReadEmbedded("capabilities.json"));
        else if(cmd=="get_current_environment")d=J.Obj("GameSpaceDisplayName","던바튼","ChannelDisplayName","데모 채널","Weather","Sunny","WorldPosition",J.Obj("X",180.6,"Y",-222.7));
        else if(cmd=="get_music_scores"||cmd=="get_instruments"||cmd=="change_instrument"||cmd=="play_music_score")return Music.Call(cmd,body);
        else if(cmd=="get_activity")d=J.Obj("Performance",Music.Performance(),"Dungeon",J.Obj("State",Dungeon),"combatState",J.Obj("IsInCombat",false),"Mode",J.Obj("MainButtonState",Fishing?"Fishing":"Interaction"),"autoPlay",J.Obj("IsAutoPlaying",false),"autoTravel",J.Obj("IsAutoTraveling",false));
        else if(cmd=="get_inventory")d=J.Obj("CurrentInventoryWeightAsDecimal",38.5,"MaxInventoryWeightAsDecimal",100);
        else if(cmd=="get_items")d=Counts.Select(x=>J.Obj("DisplayName",x.Key,"Count",x.Value,"Location","inventory","CategoryDisplayName","재료","IsLocked",false)).ToArray();
        else if(cmd=="get_altering_works")d=J.Obj("works",Works.ToArray(),"completedCount",Works.Count(x=>J.B(x,"IsCompleted")));
        else if(cmd=="get_alterable_items")d=J.Obj("items",new[]{J.Obj("DisplayName","목재","Alterable",true,"ProducedPerWork",5),J.Obj("DisplayName","옷감","Alterable",true,"ProducedPerWork",3)});
        else if(cmd=="get_gatherable_items")d=J.Obj("items",new[]{J.Obj("DisplayName","통나무","ToolOk",true),J.Obj("DisplayName","거미줄","ToolOk",true),J.Obj("DisplayName","연어","ToolOk",true)});
        else if(cmd=="execute_altering"){Works.Add(J.Obj("DisplayName",n,"FacilityName",n=="목재"?"목재 가공 시설":"옷감 가공 시설","State","InProgress","IsCompleted",false,"RemainingSeconds",120));d=J.Obj("status","accepted","body",J.Obj("result","started"));}
        else if(cmd=="complete_altering_work"&&RejectCollection)d=J.Obj("status","rejected","body",J.Obj("error","blocked"));
        else if(cmd=="complete_altering_work"){var w=Works.FirstOrDefault(x=>J.S(x,"DisplayName")==n&&J.B(x,"IsCompleted"));if(w!=null){string f=J.S(w,"FacilityName");foreach(var a in Works.Where(x=>J.S(x,"FacilityName")==f&&J.B(x,"IsCompleted")).ToList()){string name=J.S(a,"DisplayName");if(!Counts.ContainsKey(name))Counts[name]=0;Counts[name]+=5;Works.Remove(a);}}d=J.Obj("status","accepted","body",J.Obj("collected",1));}
        else if(cmd=="execute_gathering"){if(n=="연어")Fishing=true;else {if(!Counts.ContainsKey(n))Counts[n]=0;Counts[n]+=100;}d=J.Obj("status","accepted","body",J.Obj("result",Fishing?"fishing_started":"completed"));}
        else if(cmd=="stop_action"){Fishing=false;Music.Playing=false;d=J.Obj("status","accepted");}
        else d=J.Obj("error","unsupported_command");
        return Task.FromResult(new Reply{Data=d,ExitCode=0});
    }
}
}
