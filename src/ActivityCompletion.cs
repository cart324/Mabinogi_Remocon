using System;
using System.Linq;
using System.Collections.Generic;
namespace MabiRemote {
public sealed class ActivityNotice {
    public string Kind,Title,Text;
    public ActivityNotice(string kind,string title,string text){Kind=kind;Title=title;Text=text;}
}
public sealed class ActivityCompletionWatch {
    const string CompletedSuffix=" 완료됨";
    string lastDungeon="";bool wasBattlefield;
    readonly HashSet<string> notifiedMissions=new HashSet<string>();
    public void Reset(){lastDungeon="";wasBattlefield=false;notifiedMissions.Clear();}
    public static bool Battlefield(Snapshot s){return J.B(J.Get(s.Activity,"Battlefield"),"IsInBattleField");}
    public static bool FastPoll(Snapshot s){return Battlefield(s)||s.Dungeon=="Entering"||s.Dungeon=="InProgress";}
    static string MissionKey(object quest){string title=J.S(quest,"QuestTitle");return title.EndsWith(CompletedSuffix,StringComparison.Ordinal)?title.Substring(0,title.Length-CompletedSuffix.Length):title;}
    static bool Relevant(object quest,string area){return area!=""&&J.S(quest,"Source")=="goddess_mission"&&J.S(quest,"QuestTitle").Contains(area);}
    public static bool RewardReady(Snapshot s){return Battlefield(s)&&s.Quests!=null&&s.Quests.Any(q=>Relevant(q,J.S(s.Environment,"GameSpaceDisplayName"))&&J.S(q,"QuestTitle").EndsWith(CompletedSuffix,StringComparison.Ordinal));}
    public List<ActivityNotice> Observe(Snapshot s){
        var notices=new List<ActivityNotice>();string area=J.S(s.Environment,"GameSpaceDisplayName"),state=s.Dungeon;
        // Cleared is a positive signal even if the app attached after the clear.
        if(state=="Cleared"&&lastDungeon!="Cleared")notices.Add(new ActivityNotice("dungeon","던전 완료",(area==""?"던전":area)+" · 게임의 클리어 상태를 확인했습니다."));
        if(state!="")lastDungeon=state;
        bool battlefield=Battlefield(s);
        if(!battlefield){if(J.Get(J.Get(s.Activity,"Battlefield"),"IsInBattleField")!=null){wasBattlefield=false;notifiedMissions.Clear();}return notices;}
        if(!wasBattlefield){notifiedMissions.Clear();wasBattlefield=true;}
        if(s.Quests==null)return notices;
        foreach(var quest in s.Quests.Where(q=>Relevant(q,area))){
            string title=J.S(quest,"QuestTitle"),key=area+"|"+MissionKey(quest);
            if(title.EndsWith(CompletedSuffix,StringComparison.Ordinal)){
                if(notifiedMissions.Add(key))notices.Add(new ActivityNotice("hunting","사냥터 완료",area+" · "+MissionKey(quest)+" 완료. 보상을 수령하세요."));
            }else{
                // Objective completion can be a stage transition, not the end of the hunt.
                // A new unfinished instance of this mission re-arms its completion notice.
                var objectives=J.Rows(J.Get(quest,"Objectives"));
                if(objectives.Any(x=>J.Get(x,"IsCompleted")!=null&&!J.B(x,"IsCompleted")))notifiedMissions.Remove(key);
            }
        }
        return notices;
    }
}
public partial class MainForm {
    readonly ActivityCompletionWatch completionWatch=new ActivityCompletionWatch();
    string lastActivityTrace="",lastQuestError="";
    async System.Threading.Tasks.Task ObserveActivity(Snapshot fresh){
        if((ActivityCompletionWatch.Battlefield(fresh)&&cfg.HuntingNotifications)||activityDiagnosticPath!=""){
            try{fresh.Quests=J.Rows(await Read("get_quests"));lastQuestError="";}
            catch(Exception ex){string error=FriendlyText.Error(ex);if(lastQuestError!=error){Log("사냥 목표 조회 실패: "+ex.Message);lastQuestError=error;}}
        }
        RecordActivityDiagnostic(fresh);
        bool battle=ActivityCompletionWatch.Battlefield(fresh);
        string trace="던전: "+FriendlyText.Dungeon(fresh.Dungeon)+" · 사냥터: "+(battle?(ActivityCompletionWatch.RewardReady(fresh)?"보상 수령 대기":"진행 중"):"밖")+" · 자동 진행: "+(J.B(fresh.Section("autoPlay"),"IsAutoPlaying")?"ON":"OFF")+" · 지역: "+J.S(fresh.Environment,"GameSpaceDisplayName");
        if(activityWatchLabel!=null)activityWatchLabel.Text=trace+(battle&&cfg.HuntingNotifications&&fresh.Quests==null?" · 사냥 목표 조회 실패":"");
        if(trace!=lastActivityTrace){Log("활동 상태: "+trace);lastActivityTrace=trace;}
        foreach(var notice in completionWatch.Observe(fresh)){
            if((notice.Kind=="dungeon"&&cfg.DungeonNotifications)||(notice.Kind=="hunting"&&cfg.HuntingNotifications))QueueCompletionNotice(notice);
        }
    }
}
}
