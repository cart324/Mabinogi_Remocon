using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
namespace MabiRemote {
public static class ActivityCompletionTests {
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    static Snapshot Sample(string dungeon="NotInDungeon",bool battle=true,string title="얼음 협곡 사냥 IV",bool completed=false){return new Snapshot{Environment=J.Obj("GameSpaceDisplayName","얼음 협곡"),Activity=J.Obj("Dungeon",J.Obj("State",dungeon),"Battlefield",J.Obj("IsInBattleField",battle),"IsAutoPlaying",true),Quests=new List<object>{J.Obj("Source","goddess_mission","QuestTitle",title,"Objectives",new[]{J.Obj("IsCompleted",completed,"Count",0,"Goal",35)})}};}
    public static List<Snapshot> Fixture(string name){return J.Rows(J.Parse(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"fixtures",name)))).Select(x=>new Snapshot{Environment=J.Get(x,"Environment"),Activity=J.Get(x,"Activity"),Quests=J.Rows(J.Get(x,"Quests"))}).ToList();}
    public static void Run(Action<string,Action> check){
        check("던전 클리어를 첫 조회에서도 감지하고 중복 방지",()=>{var w=new ActivityCompletionWatch();var s=Sample("Cleared",false);Assert(w.Observe(s).Single().Kind=="dungeon","first clear");Assert(w.Observe(s).Count==0,"dedupe");w.Observe(Sample("Entering",false));w.Observe(Sample("InProgress",false));Assert(w.Observe(s).Count==1,"next run");});
        check("입장 중 일시적 던전 밖·일반 퇴장은 완료로 판단하지 않음",()=>{var w=new ActivityCompletionWatch();foreach(var state in new[]{"NotInDungeon","Entering","NotInDungeon","InProgress","NotInDungeon"})Assert(w.Observe(Sample(state,false)).Count==0,"no inferred clear");});
        check("사냥 목표 완료 단계와 보상 수령 단계 구분",()=>{var w=new ActivityCompletionWatch();Assert(w.Observe(Sample()).Count==0,"in progress");Assert(w.Observe(Sample(completed:true)).Count==0,"objectives alone can be intermediate");var done=Sample(title:"얼음 협곡 사냥 IV 완료됨");Assert(ActivityCompletionWatch.RewardReady(done)&&w.Observe(done).Single().Kind=="hunting","reward stage");Assert(w.Observe(done).Count==0,"dedupe");});
        check("실제 얼음 협곡 응답 재생 시 보상 대기에 한 번 알림",()=>{var w=new ActivityCompletionWatch();var frames=Fixture("ice-valley-completion.json");Assert(frames.Count==3,"fixture length");Assert(w.Observe(frames[0]).Count==0&&w.Observe(frames[1]).Count==0,"35/35 is not final phase");var notices=w.Observe(frames[2]);Assert(notices.Count==1&&notices[0].Title=="사냥터 완료","one completion");Assert(w.Observe(frames[2]).Count==0,"repeated polling");});
        check("사냥터 중단·퇴장·퀘스트 목록 소실을 완료로 오인하지 않음",()=>{var w=new ActivityCompletionWatch();var s=Sample();w.Observe(s);(s.Activity as Dictionary<string,object>)["IsAutoPlaying"]=false;Assert(w.Observe(s).Count==0,"manual stop");s.Quests.Clear();Assert(w.Observe(s).Count==0,"hidden quest");s=Sample(battle:false,title:"얼음 협곡 사냥 IV 완료됨");Assert(w.Observe(s).Count==0,"exit");});
        check("다른 지역·다른 출처 임무의 완료 알림 제외",()=>{var w=new ActivityCompletionWatch();Assert(w.Observe(Sample(title:"다른 사냥터 완료됨")).Count==0,"unrelated area");var s=Sample(title:"얼음 협곡 사냥 IV 완료됨");(s.Quests[0] as Dictionary<string,object>)["Source"]="main";Assert(w.Observe(s).Count==0,"unrelated source");});
        check("동일 사냥터 다음 회차와 목록 재표시 구분",()=>{var w=new ActivityCompletionWatch();var done=Sample(title:"얼음 협곡 사냥 IV 완료됨");Assert(w.Observe(done).Count==1,"first");var hidden=Sample();hidden.Quests.Clear();w.Observe(hidden);Assert(w.Observe(done).Count==0,"same reward shown again");w.Observe(Sample());Assert(w.Observe(done).Count==1,"new unfinished mission re-arms");});
        check("사냥터 종류를 하드코딩하지 않고 지역 임무로 판정",()=>{var s=Sample(title:"새로운 숲 사냥 II 완료됨");s.Environment=J.Obj("GameSpaceDisplayName","새로운 숲");Assert(new ActivityCompletionWatch().Observe(s).Count==1,"new field name");});
        check("실제 룬다 응답에서 보스 전투 플래그와 무관하게 클리어 감지",()=>{var w=new ActivityCompletionWatch();var notices=new List<ActivityNotice>();foreach(var s in Fixture("runda-completion.json"))notices.AddRange(w.Observe(s));Assert(notices.Count==1&&notices[0].Kind=="dungeon","one clear notification");});
        check("사냥터 알림 설정의 이전 설정 호환·저장",()=>{var old=J.Serializer().Deserialize<Settings>("{}");Assert(old.HuntingNotifications,"enabled by default");old.HuntingNotifications=false;Assert(!J.Serializer().Deserialize<Settings>(J.Json(old)).HuntingNotifications,"persist off");});
    }
}
}
