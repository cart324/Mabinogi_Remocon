using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows.Forms;

namespace MabiRemote {
public partial class MainForm {
    string fishingItem=""; int fishingTarget; bool stopping; string lastWarning="";
    void LoadEmbeddedCatalog(){var cat=J.Parse(Program.ReadEmbedded("capabilities.json"));SetCatalog(cat);}
    void SetCatalog(object cat){commands.Clear();foreach(var c in J.Rows(J.Get(cat,"commands"))){string key=J.S(c,"Command");if(key!="")commands[key]=c;}}
    async Task<object> Read(string command){if(!commands.ContainsKey(command)&&command!="status"&&command!="capabilities")throw new Exception("게임이 명령을 제공하지 않습니다: "+command);var r=await bridge.Call(command,null);if(closing)throw new OperationCanceledException();r.Check();return J.Unwrap(r.Data);}
    async Task RefreshSnapshot(bool catalogs, bool afterAction=false){
        var refreshClock=System.Diagnostics.Stopwatch.StartNew();
        var fresh=new Snapshot();fresh.Environment=await Read("get_current_environment");fresh.Activity=await Read("get_activity");fresh.ActivityAt=DateTime.UtcNow;await ObserveActivity(fresh);fresh.Inventory=await Read("get_inventory");fresh.Items=J.Rows(await Read("get_items"));fresh.Works=J.Rows(J.Get(await Read("get_altering_works"),"works"));
        if(catalogs||snap.Recipes.Count==0||DateTime.UtcNow-catalogAt>TimeSpan.FromSeconds(60)){
            fresh.Recipes=J.Rows(J.Get(await Read("get_alterable_items"),"items"));
            if(!afterAction||snap.Gatherables.Count==0||DateTime.UtcNow-catalogAt>TimeSpan.FromSeconds(60)){fresh.Gatherables=J.Rows(J.Get(await Read("get_gatherable_items"),"items"));catalogAt=DateTime.UtcNow;}else fresh.Gatherables=snap.Gatherables;
        }else{fresh.Recipes=snap.Recipes;fresh.Gatherables=snap.Gatherables;}
        fresh.At=DateTime.UtcNow;snap=fresh;
        await RefreshMusic(catalogs&&!afterAction);
        string selection=cfg.FishName;if(!auto&&!ownsFishing)Fill(fishCombo,snap.Gatherables.Select(x=>J.S(x,"DisplayName")).Where(FishNames.IsFish).Distinct(),selection);
        if(selection==""&&fishCombo.Items.Count>0)fishCombo.SelectedIndex=-1;
        RenderAll();if(afterAction)Log("작업 후 상태 갱신: "+refreshClock.Elapsed.TotalSeconds.ToString("0.00")+"초");
    }
    async Task Poll(bool force=false){
        if(busy||closing||stopping)return;ApplyCliChange();polling=true;busy=true;bool stopFishOnFailure=false;string failureLabel="연결 확인 실패";bool disconnected=false;
        try{
            var status=await bridge.Call("status",null);if(closing)return;if(status.ExitCode!=0||J.S(status.Data,"pipe")!="connected"){failureLabel=ConnectionStatus.Disconnected(status.Data);disconnected=true;throw new Exception(failureLabel+": "+J.S(status.Data,"reason"));}
            connected=true;connectionIssue="";failureLabel="게임 상태 조회 실패";
            if(!hasCatalog){var cat=await Read("capabilities");if(J.B(cat,"loading")){failureLabel="게임 접속 준비 중";throw new Exception("게임 명령 목록이 준비 중입니다. 캐릭터 접속 후 다시 확인합니다.");}SetCatalog(cat);hasCatalog=true;if(!demo){Directory.CreateDirectory(Storage.Root);File.WriteAllText(Path.Combine(Storage.Root,"capabilities.json"),J.Json(cat),Encoding.UTF8);}Log("연결 성공 · 명령 "+commands.Count+"개");}
            int stamp=generation;await RefreshSnapshot(force);
            if(MusicActive){await jukebox.Step(snap.Activity,snap.ActivityAt);snap.Activity=jukebox.LastActivity??snap.Activity;RenderMusicLibrary();RenderPlaylist();UpdateMusicStatus();}
            if(ownsFishing && !snap.Fishing){ownsFishing=false;fishingItem="";fishingTarget=0;}
            if(ownsFishing&&fishingTarget>0&&snap.Count(fishingItem,cfg.CountStorage)>=fishingTarget){Log("낚시 목표 충족: "+fishingItem);await StopFishOnly();await RefreshSnapshot(false);}
            if(snap.Full(cfg.FullPercent)&&(auto||ownsFishing)){Pause("가방 한도 도달 · 직접 판매/분해/창고 정리가 필요합니다.");if(ownsFishing)await StopFishOnly();Notify("가방 정리 필요",snap.Weight.ToString("0.#")+" / "+snap.Capacity.ToString("0.#")+". 정리 후 자동화를 다시 시작하세요.");}
            while(auto&&stamp==generation&&!stopRequested){
                var plan=Planner.Next(snap,cfg,fishEnabled,ownsFishing,cooldown,DateTime.UtcNow);
                if(plan==null){
                    planLabel.Text=ownsFishing?"자동 낚시 중":snap.Busy(ownsFishing)?"다른 게임 행동 종료 대기":"완료 대기 · 목표 충족 또는 재료/시설 조건 대기";
                    break;
                }
                if(ownsFishing){
                    if(plan.Command=="execute_gathering"&&plan.Name==fishingItem){planLabel.Text="자동 낚시 · "+fishingItem+(fishingTarget>0?" 목표 "+fishingTarget:"");break;}
                    await StopFishOnly();if(ownsFishing||!auto)break;await RefreshSnapshot(false);continue;
                }
                if(!await RunPlan(plan,stamp))break;
                await Task.Yield(); // Let Stop/Pause clicks run; no artificial inter-action delay.
            }
            lastWarning="";nextPoll=DateTime.UtcNow.AddSeconds(MusicActive?(jukebox.NearEnd?0.25:1):auto||ActivityCompletionWatch.FastPoll(snap)||activityDiagnosticPath!=""?1:3);
        }catch(Exception ex){if(closing)return;if(disconnected){completionWatch.Reset();ownsFishing=false;fishingItem="";fishingTarget=0;}if(MusicActive)jukebox.Detach("게임 연결 또는 상태 조회 실패 · 게임의 현재 연주를 확인하세요.");connected=false;connectionIssue=ex is FileNotFoundException?"게임 CLI 파일 없음":failureLabel;hasCatalog=false;if(auto)Pause("조회 실패로 자동화를 일시정지했습니다.");stopFishOnFailure=ownsFishing;if(lastWarning!=ex.Message){Log(ex.Message);lastWarning=ex.Message;}nextPoll=DateTime.UtcNow.AddSeconds(disconnected?3:15);}
        finally{busy=false;polling=false;if(!closing)ApplyCliChange();UpdateLiveLabels();}
        if(stopFishOnFailure)await StopFishOnly();
    }
    void StartAutomation(){
        if(!connected||busy)return;
        if(MusicActive){Notice("주크박스를 중지한 뒤 자동화를 시작하세요.");return;}
        if(snap.Full(cfg.FullPercent)){Notice("가방 중지 기준에 도달했습니다. 먼저 가방을 정리하세요.");return;}
        if(cfg.Goals.All(x=>!x.Enabled)&&cfg.Stocks.All(x=>!x.Gather)&&!fishEnabled&&snap.Works.All(x=>!J.B(x,"IsCompleted"))){Notice("자동 가공·재고 목표 또는 낚시를 설정하세요.");return;}
        if(fishEnabled&&(fishCombo.SelectedItem==null||!FishNames.IsFish(Convert.ToString(fishCombo.SelectedItem)))){Notice("낚시 어종을 선택하세요.");return;}
        generation++;stopRequested=false;auto=true;cooldown.Clear();cfg.FishName=Convert.ToString(fishCombo.SelectedItem)??"";Save();nextPoll=DateTime.MinValue;Log("자동화 시작");planLabel.Text="자동화 ON · 우선순위 확인 중";UpdateLiveLabels();
    }
    void Pause(string reason){auto=false;generation++;if(closing)return;planLabel.Text="자동화 OFF · "+reason;Log(reason);UpdateLiveLabels();}
    async Task StopFishOnly(){if(!ownsFishing||stopping)return;stopping=true;try{var r=await bridge.Call("stop_action",null);r.Check();ownsFishing=false;fishingTarget=0;fishingItem="";SetGatherStatus("낚시 중지 완료");Log("리모컨이 시작한 낚시 중지");}catch(Exception ex){Pause("낚시 중지 확인 필요");Notify("낚시 중지 실패",UserError(ex)+" · 게임에서 직접 중지하세요.");}finally{stopping=false;}}
    async Task StopOwned(){
        if(MusicActive){RequestMusicStop();return;}
        if(stopping)return;stopRequested=true;Pause("사용자가 중지를 요청했습니다.");
        if(!actionOwned&&!ownsFishing)return;stopping=true;
        try{var r=await bridge.Call("stop_action",null);r.Check();ownsFishing=false;fishingTarget=0;SetGatherStatus("채집/낚시 중지 요청 전달 · 진행 명령 응답 확인 중");Log("게임에 중지 요청 전달 · 진행 중인 명령의 응답을 기다립니다.");}
        catch(Exception ex){Notify("게임 중지 확인 필요",UserError(ex)+" · 중단 버튼이 없는 단계라면 현재 작업 완료를 기다립니다.");}
        finally{stopping=false;nextPoll=DateTime.MinValue;}
    }
    async Task<bool> RunPlan(Plan p,int stamp){
        if(stopRequested||generation!=stamp)return false;
        if(!commands.ContainsKey(p.Command)){Log("지원하지 않는 명령: "+p.Command);Pause("현재 게임에서 이 작업을 지원하지 않습니다.");return false;}
        if(p.Command=="execute_altering"&&Facilities.Free(snap,cfg,Facilities.ForRecipe(cfg,p.Name))<=0){Notice("시설 빈 슬롯이 없어 등록을 중지했습니다.");return false;}
        actionOwned=true;actionText=p.Reason+" · "+p.Name;planLabel.Text=actionText;Log(actionText);UpdateLiveLabels();
        if(p.Command=="execute_gathering")SetGatherStatus("채집 진행 중 · "+p.Name+" (이동·채집 포함, 완료 응답 대기)");
        bool okay=false;var commandClock=System.Diagnostics.Stopwatch.StartNew();
        try{
            var response=await bridge.Call(p.Command,J.Obj("displayName",p.Name));if(closing)return false;response.Check();Log("게임 명령 응답: "+p.Command+" · "+commandClock.Elapsed.TotalSeconds.ToString("0.00")+"초");var body=J.Unwrap(response.Data);
            string result=J.S(body,"result");if(p.Command=="execute_gathering")SetGatherStatus(GatherResult.Describe(p.Name,body));if(result=="stopped_by_user"){Log("사용자 중지 · 후속 예약 취소");await RefreshSnapshot(true);return false;}
            if(p.Command=="execute_altering"&&result!="started")throw new GameCommandException("가공 등록을 확인하지 못했습니다. 시설의 작업 목록을 확인하세요.",J.Json(body));
            if(p.Goal!=null){p.Goal.RunsDone++;Save();}
            if(p.Completed!=null)p.Completed();
            string cost=J.S(body,"cost");Log("명령 완료: "+p.Name+(cost!=""?" · "+cost:""));okay=!stopRequested&&generation==stamp;
            await RefreshSnapshot(true,true);
            if(p.Command=="execute_gathering"){
                bool idleFish=p.Reason=="빈 시간 자동 낚시";
                if(snap.Fishing){ownsFishing=true;fishingItem=p.Name;var target=cfg.Stocks.FirstOrDefault(x=>x.Name==p.Name&&x.Gather);fishingTarget=idleFish?0:p.GatherTarget>0?p.GatherTarget:target!=null?target.Target:snap.Count(p.Name,cfg.CountStorage)+100;Log("자동 낚시 감시 시작: "+p.Name);if(stopRequested||generation!=stamp||!auto||(idleFish&&!fishEnabled))await StopFishOnly();}
                else if(idleFish){fishEnabled=false;fishCheck.Checked=false;Pause("선택 품목이 자동 낚시를 시작하지 않았습니다. 낚시 어종을 다시 선택하세요.");Notify("낚시 설정 확인","선택 항목: "+p.Name);}
            }
            if(snap.Full(cfg.FullPercent)){Pause("가방 중지 기준 도달");if(ownsFishing)await StopFishOnly();Notify("가방 정리 필요","자동 판매·분해는 게임 API 미지원입니다. 직접 정리 후 재시작하세요.");}
        }catch(Exception ex){
            if(closing)return false;
            if(p.Command=="execute_gathering")SetGatherStatus("채집 오류 · "+p.Name+" · "+FriendlyText.Error(ex));
            cooldown[p.Command+":"+p.Name]=DateTime.UtcNow.AddSeconds(120);Log("실행 실패: "+ex.Message);
            // Unknown/blocked outcomes require a human; never repeatedly spend or blindly retry.
            Pause("작업 실패로 중지 · 설정/게임 상태 확인 필요");Notify("작업 중지",p.Name+" · "+UserError(ex));
        }finally{actionOwned=false;UpdateLiveLabels();}
        return okay;
    }
    async Task ManualCollect(){
        if(auto||busy||ownsFishing||MusicActive){Notice("자동화 및 현재 작업을 중지한 뒤 실행하세요.");return;}
        busy=true;int stamp=++generation;stopRequested=false;
        try{await RefreshSnapshot(false);if(snap.Busy(false)){Notice("전투·이동 등 현재 게임 행동이 끝난 뒤 실행하세요.");return;}if(snap.Full(cfg.FullPercent)){Notice("가방을 먼저 정리하세요.");return;}
            var work=snap.Works.Where(x=>J.B(x,"IsCompleted")).GroupBy(x=>J.S(x,"FacilityName")).Select(x=>J.S(x.First(),"DisplayName")).ToList();if(work.Count==0){Log("수령 가능한 작업이 없습니다.");return;}
            foreach(string name in work){if(generation!=stamp||stopRequested||snap.Full(cfg.FullPercent))break; if(!await RunPlan(new Plan("complete_altering_work",name,"수동 일괄 수령",0),stamp))break;}
        }catch(Exception ex){Log("수령 중지: "+ex.Message);}finally{busy=false;nextPoll=DateTime.UtcNow;UpdateLiveLabels();}
    }
    async Task ManualRegister(){
        if(auto||busy||ownsFishing||MusicActive){Notice("자동화 및 현재 작업을 중지한 뒤 실행하세요.");return;}if(manualQueue.Count==0){Notice("시설과 품목을 선택해 빈 슬롯 등록을 예약하세요.");return;}
        busy=true;int stamp=++generation;stopRequested=false;
        try{
            await RefreshSnapshot(true);if(snap.Busy(false)||snap.Full(cfg.FullPercent)){Notice("게임이 다른 행동 중이거나 가방 한도에 도달했습니다.");return;}
            var requestedFacilities=new HashSet<string>(manualQueue.Where(x=>x.Target>0).Select(x=>x.Facility??Facilities.ForRecipe(cfg,x.Name)));
            var collectFirst=snap.Works.Where(x=>J.B(x,"IsCompleted")&&requestedFacilities.Contains(J.S(x,"FacilityName"))).GroupBy(x=>J.S(x,"FacilityName")).Select(x=>x.First()).ToList();
            foreach(var completed in collectFirst){
                if(generation!=stamp||stopRequested||snap.Full(cfg.FullPercent))return;
                if(!await RunPlan(new Plan("complete_altering_work",J.S(completed,"DisplayName"),"수동 등록 전 완료품 자동 수령",0),stamp))return;
            }
            if(generation!=stamp||stopRequested||snap.Full(cfg.FullPercent))return;
            var available=Facilities.Names.ToDictionary(x=>x,x=>Facilities.Free(snap,cfg,x));var run=new List<Goal>();
            foreach(var original in manualQueue){string facility=original.Facility??Facilities.ForRecipe(cfg,original.Name);int free;available.TryGetValue(facility,out free);int count=Math.Min(original.Target,free);if(count>0){run.Add(new Goal{Name=original.Name,Facility=facility,Target=count});available[facility]=free-count;}}
            int total=run.Sum(x=>x.Target);if(total==0){Notice("완료품 수령 후에도 빈 슬롯이 없습니다. 진행 작업과 시설별 슬롯 설정을 확인하세요.");return;}
            manualQueue=run;RenderManual();
            foreach(var q in run.ToList()){
                while(q.Target>0){
                    if(generation!=stamp||stopRequested||snap.Full(cfg.FullPercent))return;
                    if(Facilities.Free(snap,cfg,q.Facility)<=0){Log(q.Facility+" 빈 슬롯 없음 · 남은 예약을 재등록하지 않습니다.");break;}
                    var r=snap.Recipes.Where(x=>J.S(x,"DisplayName")==q.Name).OrderByDescending(x=>J.B(x,"Alterable")).FirstOrDefault();
                    if(r==null||!J.B(r,"Alterable")){Notice("등록 중지: "+q.Name+" · "+Reason(r));return;}
                    if(!await RunPlan(new Plan("execute_altering",q.Name,"수동 1회성 등록",5){Completed=()=>{q.Target--;RenderManual();}},stamp))return;
                }
                manualQueue.Remove(q);RenderManual();
            }
            Log("수동 등록 완료 · 자동 재등록하지 않습니다.");
        }catch(Exception ex){Notice("등록 중지: "+UserError(ex));}finally{busy=false;nextPoll=DateTime.UtcNow;UpdateLiveLabels();}
    }
    public async Task RenderPreview(string dir){
        Directory.CreateDirectory(dir);int previewScale;if(Int32.TryParse(Environment.GetEnvironmentVariable("MABIREMOTE_PREVIEW_SCALE"),out previewScale)&&UiSizing.Options.Contains(previewScale))displayScaleCombo.SelectedItem=previewScale+"%";string fixture=Environment.GetEnvironmentVariable("MABIREMOTE_PREVIEW_FIXTURE");if(demo&&!String.IsNullOrEmpty(fixture)&&File.Exists(fixture)){var db=(DemoBridge)bridge;db.Works=J.Rows(J.Get(J.Parse(File.ReadAllText(fixture,Encoding.UTF8)),"works"));}await Poll(true);for(int i=0;i<tabs.TabPages.Count;i++){tabs.SelectedIndex=i;Application.DoEvents();using(var bmp=new Bitmap(Width,Height)){DrawToBitmap(bmp,new Rectangle(0,0,Width,Height));bmp.Save(Path.Combine(dir,"screen-"+i+".png"),System.Drawing.Imaging.ImageFormat.Png);}}
        CaptureNavigation(dir);
        int dialogIndex=0;using(var capture=new Timer{Interval=250}){capture.Tick+=(s,e)=>{var dialog=Application.OpenForms.OfType<EditDialog>().FirstOrDefault();if(dialog==null)return;using(var bmp=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bmp,new Rectangle(0,0,dialog.Width,dialog.Height));bmp.Save(Path.Combine(dir,"dialog-"+dialogIndex+".png"),System.Drawing.Imaging.ImageFormat.Png);}dialog.DialogResult=DialogResult.Cancel;};capture.Start();EditGoal(null);dialogIndex++;EditStock(null);dialogIndex++;EditRoute(null);dialogIndex++;EditRoutePoint(new RoutePreset{Material="통나무"},-1);capture.Stop();}

    }
    public async Task<object> CheckActionFlow(){
        if(!demo)throw new Exception("Demo required");
        var db=(DemoBridge)bridge;db.Works.Clear();db.Works.Add(J.Obj("DisplayName","목재","FacilityName","목재 가공 시설","State","Completed","IsCompleted",true,"RemainingSeconds",0));cfg.Goals.Clear();cfg.Stocks.Clear();cfg.Goals.Add(new Goal{Name="목재",Product="목재",Target=25});
        await Poll(true);db.Calls.Clear();StartAutomation();await Poll();
        int registrations=db.Calls.Count(x=>x=="execute_altering"),collections=db.Calls.Count(x=>x=="complete_altering_work");
        bool success=registrations==2&&collections==1&&facilityGrid.Rows.Count>=6;
        Pause("자동 가공 검증 종료");db.Works.Clear();cfg.Goals.Clear();cfg.FacilitySlots["목재 가공 시설"]=4;
        for(int i=0;i<2;i++)db.Works.Add(J.Obj("DisplayName","목재","FacilityName","목재 가공 시설","IsCompleted",false,"RemainingSeconds",100));
        manualQueue.Add(new Goal{Name="목재",Facility="목재 가공 시설",Target=7});db.Calls.Clear();await ManualRegister();await Poll();
        int manualRegistrations=db.Calls.Count(x=>x=="execute_altering");success=success&&manualRegistrations==2&&db.Works.Count==4&&manualQueue.Count==0&&fishCombo.Items.Cast<object>().All(x=>FishNames.IsFish(Convert.ToString(x)));
        int manualUsedSlots=db.Works.Count;db.Works.Clear();db.Counts["통나무"]=200;cfg.Goals.Add(new Goal{Name="목재",Mode="runs",Target=12});cfg.FacilitySlots["목재 가공 시설"]=20;db.Calls.Clear();await Poll(true);StartAutomation();await Poll();
        int unlimitedByBudget=db.Calls.Count(x=>x=="execute_altering");success=success&&unlimitedByBudget==12;Pause("횟수 제한 제거 검증 종료");
        cfg.Goals.Clear();db.Works.Clear();cfg.FacilitySlots["목재 가공 시설"]=7;
        for(int i=0;i<5;i++)db.Works.Add(J.Obj("DisplayName","목재","FacilityName","목재 가공 시설","IsCompleted",true,"RemainingSeconds",0));
        db.Works.Add(J.Obj("DisplayName","목재","FacilityName","목재 가공 시설","IsCompleted",false,"RemainingSeconds",100));
        await Poll(true);int reservable=Facilities.ManualAvailable(snap,cfg,"목재 가공 시설",0);manualQueue.Add(new Goal{Name="목재",Facility="목재 가공 시설",Target=reservable});db.Calls.Clear();await ManualRegister();
        int collectBeforeRegister=db.Calls.IndexOf("complete_altering_work"),firstRegister=db.Calls.IndexOf("execute_altering");int afterCollectRegistrations=db.Calls.Count(x=>x=="execute_altering");
        bool collectedFirst=reservable==6&&collectBeforeRegister>=0&&firstRegister>collectBeforeRegister&&afterCollectRegistrations==6&&db.Works.Count==7;success=success&&collectedFirst;
        db.Works.Clear();db.Works.Add(J.Obj("DisplayName","목재","FacilityName","목재 가공 시설","IsCompleted",true,"RemainingSeconds",0));manualQueue.Add(new Goal{Name="목재",Facility="목재 가공 시설",Target=6});db.RejectCollection=true;db.Calls.Clear();await ManualRegister();bool stoppedOnCollectionFailure=!db.Calls.Contains("execute_altering")&&manualQueue.Count==1;success=success&&stoppedOnCollectionFailure;db.RejectCollection=false;manualQueue.Clear();
        return J.Obj("manualCollectThenRegister",collectedFirst,"stoppedOnCollectionFailure",stoppedOnCollectionFailure,"registrationsBeyondOldLimit",unlimitedByBudget,"passed",success,"collections",collections,"autoRegistrations",registrations,"manualRegistrations",manualRegistrations,"manualUsedSlots",manualUsedSlots,"manualQueueEmpty",manualQueue.Count==0,"visibleFacilities",facilityGrid.Rows.Count,"fishOptions",fishCombo.Items.Count,"dialogs",0);
    }
    public void SaveLivePreview(string path){ShowTasks();tabs.SelectedIndex=0;Application.DoEvents();using(var bmp=new Bitmap(Width,Height)){DrawToBitmap(bmp,new Rectangle(0,0,Width,Height));bmp.Save(path,System.Drawing.Imaging.ImageFormat.Png);}}
    public async Task<object> LiveCheck(){await Poll(true);return J.Obj("connected",connected,"recipes",snap.Recipes.Count,"items",snap.Items.Count,"works",snap.Works.Count,"gatherables",snap.Gatherables.Count,"area",J.S(snap.Environment,"GameSpaceDisplayName"),"activity",ActivityText(),"auto",auto,"lastNotification",lastNotificationLabel.Text,"activityWatch",activityWatchLabel.Text);}
}
}
