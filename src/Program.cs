using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MabiRemote {
public static class Program {
    public static string ReadEmbedded(string name){using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)){if(s==null)throw new Exception("내장 자료 없음: "+name);using(var r=new StreamReader(s,Encoding.UTF8))return r.ReadToEnd();}}
    [STAThread] public static int Main(string[] args){
        if(args.Contains("--self-test")){string result;int code=SelfTests.Run(out result);File.WriteAllText(args.Length>1?args[1]:"test-results.txt",result,Encoding.UTF8);return code;}
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException+=(s,e)=>MessageBox.Show("프로그램 오류: "+e.Exception.Message,"에린 리모컨");
        bool demo=args.Contains("--automation-check")||args.Contains("--demo")||args.Contains("--render-preview");bool preview=args.Contains("--automation-check")||args.Contains("--render-preview")||args.Contains("--live-check");
        using(var mutex=new System.Threading.Mutex(false,"Local\\MabiRemote.SingleInstance")){
            bool held=preview||demo||mutex.WaitOne(0);if(!held){MessageBox.Show("에린 리모컨이 이미 실행 중입니다. 작업 표시줄에서 기존 창을 여세요.");return 1;}
            var f=new MainForm(demo,preview);
            if(args.Contains("--render-preview"))f.Shown+=async(s,e)=>{try{await f.RenderPreview(args.Length>1?args[1]:"preview");}catch(Exception ex){File.WriteAllText("render-error.txt",ex.ToString());}finally{f.Close();}};
            if(args.Contains("--live-check"))f.Shown+=async(s,e)=>{try{var r=await f.LiveCheck();f.SaveLivePreview(args[1]+".png");File.WriteAllText(args.Length>1?args[1]:"live-check.json",J.Json(r),Encoding.UTF8);}finally{f.Close();}};
            if(args.Contains("--automation-check"))f.Shown+=async(s,e)=>{try{var r=await f.CheckActionFlow();File.WriteAllText(args[1],J.Json(r),Encoding.UTF8);}finally{f.Close();}};
            Application.Run(f);if(!preview&&!demo)mutex.ReleaseMutex();
        }
        return 0;
    }
}
public static class SelfTests {
    static List<string> results;static int failed;
    static void Check(string name,Action test){try{test();results.Add("PASS "+name);}catch(Exception ex){failed++;results.Add("FAIL "+name+" / "+ex.Message);}}
    static void Assert(bool v,string message){if(!v)throw new Exception(message);}
    static Snapshot Sample(){var s=new Snapshot{At=DateTime.UtcNow,Activity=J.Parse("{\"IsInCombat\":false,\"IsDead\":false,\"IsAutoPlaying\":false,\"Dungeon\":{\"State\":\"NotInDungeon\"},\"Mode\":{\"MainButtonState\":\"Compass\"}}"),Inventory=J.Obj("CurrentInventoryWeight",10,"MaxInventoryWeight",100)};return s;}
    static Plan Next(Snapshot s,Settings c,bool fish=false,bool owns=false,int spent=0){return Planner.Next(s,c,fish,owns,spent,new Dictionary<string,DateTime>(),DateTime.UtcNow);}
    static object Recipe(string name,bool can,double yield,params object[] missing){return J.Obj("DisplayName",name,"Alterable",can,"ProducedPerWork",yield,"Reason",can?"":"not_enough_ingredient","MissingIngredients",missing);}
    static object Work(string name,bool done){return J.Obj("DisplayName",name,"FacilityName",name.Contains("목재")?"목재 가공 시설":"약품 가공 시설","IsCompleted",done,"RemainingSeconds",done?0:100);}
    static object Item(string n,int count,string place="inventory"){return J.Obj("DisplayName",n,"Count",count,"Location",place);}
    static object UpdateFixture(string tag){return J.Obj("tag_name",tag,"draft",false,"prerelease",false);}

    public static int Run(out string report){results=new List<string>();failed=0;
        Check("실제 flat 행동 스키마와 이전 nested 스키마",()=>{var s=Sample();Assert(!s.Busy(false),"flat idle");s.Activity=J.Obj("combatState",J.Obj("IsInCombat",false),"Dungeon",J.Obj("State","NotInDungeon"),"Mode",J.Obj("MainButtonState","Compass"));Assert(!s.Busy(false),"nested idle");});
        Check("전투·던전·알 수 없는 상태에서는 자동 실행 금지",()=>{var s=Sample();s.Activity=J.Obj("IsInCombat",true,"Dungeon",J.Obj("State","NotInDungeon"),"Mode",J.Obj("MainButtonState","Compass"));Assert(s.Busy(false),"combat");s.Activity=J.Obj("IsInCombat",false,"Dungeon",J.Obj("State","InProgress"),"Mode",J.Obj("MainButtonState","Compass"));Assert(s.Busy(false),"dungeon");s.Activity=J.Obj("Mode",J.Obj("MainButtonState","Compass"));Assert(s.Busy(false),"unknown");});
        Check("수령이 등록·채집보다 우선",()=>{var s=Sample();s.Works.Add(Work("목재",true));var c=new Settings();Assert(Next(s,c).Command=="complete_altering_work","collect");});
        Check("완제품 목표와 대기열 생산량 예약",()=>{var s=Sample();s.Items.Add(Item("목재",97));s.Recipes.Add(Recipe("목재",true,3));var c=new Settings();c.Goals.Add(new Goal{Name="목재",Target=100});Assert(Next(s,c)!=null,"one more");s.Works.Add(Work("목재",false));Assert(Next(s,c)==null,"pending covers target");});
        Check("무제한 가공도 대기 상한 준수",()=>{var s=Sample();s.Recipes.Add(Recipe("목재",true,3));var c=new Settings();c.FacilitySlots["목재 가공 시설"]=2;c.Goals.Add(new Goal{Name="목재",Mode="unlimited"});s.Works.Add(Work("목재",false));Assert(Next(s,c)!=null,"queue room");s.Works.Add(Work("목재",false));Assert(Next(s,c)==null,"queue full");});
        Check("하위 가공 재료 재귀 등록",()=>{var s=Sample();s.Recipes.Add(Recipe("목재+",false,3,J.Obj("DisplayName","목재","Required",10,"Owned",0)));s.Recipes.Add(Recipe("목재",true,3));var c=new Settings();c.Goals.Add(new Goal{Name="목재+",Target=30});Assert(Next(s,c).Name=="목재","child first");for(int i=0;i<4;i++)s.Works.Add(Work("목재",false));Assert(Next(s,c)==null,"child pending covers shortage");});
        Check("하위 재료 채집 및 순환 가공법 보호",()=>{var s=Sample();s.Recipes.Add(Recipe("목재",false,3,J.Obj("DisplayName","통나무","Required",10,"Owned",0)));s.Gatherables.Add(J.Obj("DisplayName","통나무","ToolOk",true));var c=new Settings();c.Goals.Add(new Goal{Name="목재"});Assert(Next(s,c).Command=="execute_gathering","gather dependency");s.Gatherables.Clear();s.Recipes.Add(Recipe("통나무",false,3,J.Obj("DisplayName","목재","Required",10,"Owned",0)));Assert(Next(s,c)==null,"cycle stops");});
        Check("대체 가공법과 완제품 이름 대응",()=>{Assert(Planner.Product("철괴(광석)")=="철괴","alias");var s=Sample();s.Recipes.Add(Recipe("철괴(광석)",true,3));s.Items.Add(Item("철괴",100));var c=new Settings();c.Goals.Add(new Goal{Name="철괴(광석)",Product="철괴",Target=100});Assert(Next(s,c)==null,"product target");});
        Check("가방 한도·오래된 데이터·예산 차단",()=>{var s=Sample();s.Works.Add(Work("목재",true));s.Inventory=J.Obj("CurrentInventoryWeight",95,"MaxInventoryWeight",100);Assert(Next(s,new Settings())==null,"full");s.Inventory=J.Obj("CurrentInventoryWeight",10,"MaxInventoryWeight",100);s.At=DateTime.UtcNow.AddMinutes(-2);Assert(Next(s,new Settings())==null,"stale");s.At=DateTime.UtcNow;s.Works.Clear();s.Recipes.Add(Recipe("목재",true,3));var c=new Settings{Budget=5};c.Goals.Add(new Goal{Name="목재"});Assert(Next(s,c,false,false,5)==null,"budget");});
        Check("창고 포함 선택·도구 부족·재고 충족",()=>{var s=Sample();s.Items.Add(Item("통나무",300,"account_storage"));s.Gatherables.Add(J.Obj("DisplayName","통나무","ToolOk",true));var c=new Settings();c.Stocks.Add(new StockGoal{Name="통나무",Target=100});Assert(Next(s,c)!=null,"bag only");c.CountStorage=true;Assert(Next(s,c)==null,"storage included");c.CountStorage=false;s.Gatherables[0]=J.Obj("DisplayName","통나무","ToolOk",false);Assert(Next(s,c)==null,"broken tool");});
        Check("사용자 낚시 보호·소유 낚시 구분",()=>{var s=Sample();s.Activity=J.Obj("IsInCombat",false,"Dungeon",J.Obj("State","NotInDungeon"),"Mode",J.Obj("MainButtonState","Fishing"));Assert(s.Busy(false)&&!s.Busy(true),"ownership");});
        Check("낚시는 작업 없는 경우에만 계획",()=>{var s=Sample();s.Gatherables.Add(J.Obj("DisplayName","연어","ToolOk",true));var c=new Settings{FishName="연어"};Assert(Next(s,c,true).Name=="연어","fish");s.Works.Add(Work("목재",true));Assert(Next(s,c,true).Command=="complete_altering_work","collect preempts fish");});
        Check("종료 코드 0의 거절·중첩 오류도 실패",()=>{bool thrown=false;try{new Reply{ExitCode=0,Data=J.Obj("status","accepted","body",J.Obj("error","blocked"))}.Check();}catch{thrown=true;}Assert(thrown,"body error");thrown=false;try{new Reply{ExitCode=0,Data=J.Obj("status","rejected")}.Check();}catch{thrown=true;}Assert(thrown,"rejected");});
        Check("한글 입력 Base64 왕복 및 JSON 탈출",()=>{string s=J.Json(J.Obj("displayName","목재+\"한글"));string b=Convert.ToBase64String(Encoding.UTF8.GetBytes(s));Assert(Encoding.UTF8.GetString(Convert.FromBase64String(b))==s,"encoding");Assert(J.S(J.Parse("{\"name\":\"\\uBAA9\\uC7AC\"}"),"name")=="목재","JSON unicode");});
        Check("실행 경로 공백·한글과 stdout 파싱",()=>{string dir=Path.Combine(Path.GetTempPath(),"에린 리모컨 테스트 "+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);try{string source=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"FakeCLI.exe");string target=Path.Combine(dir,"Fake CLI.exe");File.Copy(source,target);var b=new GameBridge(target);var r=b.Call("echo",J.Obj("displayName","목재+")).GetAwaiter().GetResult();r.Check();Assert(J.S(J.Unwrap(r.Data),"displayName")=="목재+","real process argument");var err=b.Call("reject",null).GetAwaiter().GetResult();bool rejected=false;try{err.Check();}catch{rejected=true;}Assert(rejected,"process rejected");}finally{foreach(var f in Directory.GetFiles(dir))File.Delete(f);Directory.Delete(dir);}});
        Check("설정 파일 저장·읽기",()=>{string previous=Storage.Root;string dir=Path.Combine(Path.GetTempPath(),"MabiRemoteTest-"+Guid.NewGuid().ToString("N"));try{Storage.Root=dir;var c=new Settings();c.Goals.Add(new Goal{Name="목재",Product="목재",Mode="unlimited"});Storage.Save(c);c.Budget=25;Storage.Save(c);var loaded=Storage.Load();Assert(loaded.Budget==25&&loaded.Goals[0].Mode=="unlimited","persist");}finally{Storage.Root=previous;foreach(var f in Directory.GetFiles(dir))File.Delete(f);Directory.Delete(dir);}});
        Check("시설별 서로 다른 슬롯과 완료 미수령 점유",()=>{var s=Sample();s.Works.Add(Work("목재",true));s.Works.Add(Work("목재",false));var c=new Settings();c.FacilitySlots["목재 가공 시설"]=2;c.FacilitySlots["가죽 가공 시설"]=4;Assert(Facilities.Free(s,c,"목재 가공 시설")==0,"completed occupies slot");Assert(Facilities.Free(s,c,"가죽 가공 시설")==4,"independent capacities");});
        Check("등록 횟수 목표에서 추가 등록 중지",()=>{var s=Sample();s.Recipes.Add(Recipe("목재",true,3));var c=new Settings();var g=new Goal{Name="목재",Mode="runs",Target=2,RunsDone=1};c.Goals.Add(g);Assert(Next(s,c)!=null,"one remaining");g.RunsDone=2;Assert(Next(s,c)==null,"run limit reached");});
        Check("낚시 목록에서 물고기 이외 제외",()=>{Assert(FishNames.IsFish("연어")&&FishNames.IsFish("큰 자루퍼"),"fish");Assert(!FishNames.IsFish("통나무")&&!FishNames.IsFish("황금 도구 상자(낫)")&&!FishNames.IsFish("연어 스테이크"),"non fish excluded");});
        Check("업데이트 버전 비교·정식 릴리스만 선택",()=>{Assert(Updates.ParseRelease(UpdateFixture("v1.2.1"))==null,"old");Assert(Updates.ParseRelease(UpdateFixture("v"+Updates.CurrentVersion+".0"))==null,"four part same");Assert(Updates.ParseRelease(UpdateFixture("v"+Updates.CurrentVersion))==null,"current");var r=Updates.ParseRelease(UpdateFixture("v9.0.0"));Assert(r.Version==new Version("9.0.0"),"new");var prerelease=UpdateFixture("v9.0.0") as Dictionary<string,object>;prerelease["prerelease"]=true;Assert(Updates.ParseRelease(prerelease)==null,"prerelease");});
        Check("업데이트·알림 설정 저장과 이전 버전 호환",()=>{var c=new Settings{AutoCheckUpdates=false,ArrivalNotifications=false,DungeonNotifications=true};var loaded=J.Serializer().Deserialize<Settings>(J.Json(c));Assert(!loaded.AutoCheckUpdates&&!loaded.ArrivalNotifications&&loaded.DungeonNotifications,"persist");loaded=J.Serializer().Deserialize<Settings>("{}");Assert(loaded.AutoCheckUpdates&&loaded.ArrivalNotifications&&loaded.DungeonNotifications,"legacy");});
        Check("연결 실패 사유 구분",()=>{Assert(ConnectionStatus.Disconnected(J.Obj("reason","game_off"))=="게임 미실행","game off");Assert(ConnectionStatus.Disconnected(J.Obj("reason","option_off"))=="AI 커넥터 비활성화","connector off");Assert(ConnectionStatus.Disconnected(J.Obj("reason","unknown"))=="게임 연결 실패","unknown");Assert(ConnectionStatus.Disconnected(null)=="게임 연결 실패","missing");});
        report=String.Join(Environment.NewLine,results)+Environment.NewLine+"TOTAL "+results.Count+" / FAILED "+failed;return failed==0?0:1;
    }
}
}
