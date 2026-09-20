using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MabiRemote {
public sealed class PlaylistEntry {
    public string Title{get;set;}
    public string Instrument{get;set;}
    public PlaylistEntry(){Title="";Instrument="";}
}
public static class MusicLibrary {
    public static int LocationOrder(object row){switch(J.S(row,"Location")){case "inventory":return 0;case "account_storage":return 1;case "character_storage":return 2;default:return 3;}}
    public static string Location(object row){switch(J.S(row,"Location")){case "inventory":return "가방";case "account_storage":return "계정 상자";case "character_storage":return "캐릭터 상자";default:return "위치 정보 없음";}}
    public static List<object> Sort(IEnumerable<object> rows,string name){return rows.OrderBy(LocationOrder).ThenBy(x=>J.S(x,name),StringComparer.CurrentCulture).ToList();}
    public static string TitleKey(string title){return (title??"").StartsWith("악보: ",StringComparison.Ordinal)?title.Substring(4):title??"";}
    public static bool TitleMatches(string expected,string actual){return TitleKey(expected)==TitleKey(actual);}
    public static string Equipped(IEnumerable<object> instruments){return J.S(instruments.FirstOrDefault(x=>J.B(x,"IsEquipped")),"Name");}
}
public enum PerformanceResult { Waiting, Playing, Finished, Interrupted }
public sealed class PerformanceWatch {
    readonly string title,instrument;
    readonly DateTime requested;
    DateTime lastAt,expectedEnd=DateTime.MaxValue;
    string startAt="";
    double remaining=Double.PositiveInfinity,elapsedBefore=-1;
    public bool NearEnd{get{return Confirmed&&remaining<=5;}}
    public bool Confirmed{get;private set;}
    public string Problem{get;private set;}
    public PerformanceWatch(string score,string equipped,DateTime now){title=score;instrument=equipped;requested=now;}
    static bool Number(object p,string key,out double n){n=J.N(p,key);return J.Get(p,key)!=null&&!Double.IsNaN(n)&&!Double.IsInfinity(n)&&n>=0;}
    bool SameSong(object p){return J.B(p,"IsPlaying")&&MusicLibrary.TitleMatches(title,J.S(p,"MusicTitle"))&&J.S(p,"InstrumentName")==instrument;}
    public bool Matches(object p){return SameSong(p)&&(startAt==""||J.S(p,"StartAt")==startAt);}
    bool AtExpectedEnd(DateTime now){return remaining<=5&&(now-lastAt).TotalSeconds<=6&&now>=expectedEnd.AddMilliseconds(-500)&&now<=expectedEnd.AddSeconds(5);}
    PerformanceResult Interrupted(string reason){Problem=reason;return PerformanceResult.Interrupted;}
    public PerformanceResult Observe(object p,DateTime now){
        if(p==null||J.Get(p,"IsPlaying")==null)return Interrupted("연주 상태를 확인할 수 없습니다. 게임에서 현재 연주를 확인하세요.");
        if(J.B(p,"IsPlaying")){
            if(!SameSong(p))return Interrupted("다른 연주 또는 악기 변경이 감지되어 다음 곡을 예약하지 않습니다.");
            double duration,left,elapsed;
            bool hasElapsed=Number(p,"ElapsedSeconds",out elapsed);
            // The game can immediately restart the same score without becoming idle.
            // Accept a cycle rollover only next to the previously observed end.
            bool rolled=Confirmed&&AtExpectedEnd(now)&&hasElapsed&&elapsedBefore>=0&&elapsed<elapsedBefore&&elapsed<=(now-lastAt).TotalSeconds+1;
            if(rolled){startAt=J.S(p,"StartAt");return PerformanceResult.Finished;}
            if(!Matches(p))return Interrupted("연주 시작 시각이 바뀌어 재생 목록을 멈췄습니다.");
            // Live API: elapsed is cumulative across loops while remaining wraps.
            // One full duration on the same performance is sufficient to end this entry.
            if(Confirmed&&hasElapsed&&Number(p,"TotalDurationSeconds",out duration)&&duration>0&&elapsed>=duration)return PerformanceResult.Finished;
            Confirmed=true;startAt=J.S(p,"StartAt");lastAt=now;elapsedBefore=hasElapsed?elapsed:-1;
            bool timed=Number(p,"TotalDurationSeconds",out duration)&&duration>0&&Number(p,"RemainingSeconds",out left);
            left=J.N(p,"RemainingSeconds");
            if(timed&&left<=duration+1){remaining=left;expectedEnd=now.AddSeconds(Math.Min(left,86400*30));}
            else if(Number(p,"TotalDurationSeconds",out duration)&&duration>0&&Number(p,"ElapsedSeconds",out elapsed)&&elapsed<=duration){remaining=duration-elapsed;expectedEnd=now.AddSeconds(Math.Min(remaining,86400*30));}
            else{remaining=Double.PositiveInfinity;expectedEnd=DateTime.MaxValue;}
            if(remaining<=0&&hasElapsed&&elapsed>=J.N(p,"TotalDurationSeconds")&&J.N(p,"TotalDurationSeconds")>0)return PerformanceResult.Finished;
            return PerformanceResult.Playing;
        }
        if(!Confirmed)return now-requested<TimeSpan.FromSeconds(10)?PerformanceResult.Waiting:Interrupted("연주 시작을 확인하지 못했습니다. 게임 상태를 확인한 뒤 다시 재생하세요.");
        // The API has no completion reason; an early stop or a long sample gap is ambiguous.
        if(AtExpectedEnd(now))return PerformanceResult.Finished;
        return Interrupted("연주가 도중에 중단되었거나 종료 시간을 확인하지 못해 재생 목록을 멈췄습니다.");
    }
}
public sealed class JukeboxPlayer {
    readonly IBridge bridge;
    readonly Action<string> log;
    List<PlaylistEntry> queue=new List<PlaylistEntry>();
    PerformanceWatch watch;
    bool repeat,stopRequested;
    public object LastActivity{get;private set;}
    public bool NearEnd{get{return watch!=null&&watch.NearEnd;}}
    public bool Active{get;private set;}
    public int Index{get;private set;}
    public string Status{get;private set;}
    public bool IsWaitingForStart{get{return watch!=null&&!watch.Confirmed;}}
    public JukeboxPlayer(IBridge game,Action<string> logger){bridge=game;log=logger;Index=-1;Status="재생 대기";}
    public void Start(IEnumerable<PlaylistEntry> entries,int index,bool loop){
        if(Active)throw new InvalidOperationException("주크박스를 먼저 중지하세요.");
        queue=entries.Select(x=>new PlaylistEntry{Title=x.Title,Instrument=x.Instrument}).ToList();
        if(index<0||index>=queue.Count||queue.Any(x=>String.IsNullOrWhiteSpace(x.Title)||String.IsNullOrWhiteSpace(x.Instrument)))throw new InvalidOperationException("악보와 악기를 지정한 재생 목록이 필요합니다.");
        repeat=loop;Index=index;watch=null;stopRequested=false;Active=true;SetStatus("재생 준비 중");
    }
    void SetStatus(string text){Status=text;log("주크박스 · "+text);}
    public void Detach(string reason){Active=false;watch=null;stopRequested=false;SetStatus(reason);}
    public void RequestStop(){if(Active){stopRequested=true;SetStatus("중지 요청 · 현재 응답 확인 중");}}
    async Task<object> Call(string command,object body){var reply=await bridge.Call(command,body);reply.Check();var data=J.Unwrap(reply.Data);if(command=="get_activity")LastActivity=data;return data;}
    static bool Ready(object activity){var s=new Snapshot{Activity=activity};return !s.Busy(false)&&J.S(J.Get(activity,"Mode"),"MountPartState")!="Mounted";}
    async Task Stop(){
        if(watch!=null){var activity=await Call("get_activity",null);var perf=J.Get(activity,"Performance");if(watch.Matches(perf)){await Call("stop_action",null);activity=await Call("get_activity",null);if(watch.Matches(J.Get(activity,"Performance"))){Detach("중지 요청 후에도 연주 중입니다. 게임에서 중지를 확인하세요.");return;}}}
        Detach("주크박스 중지됨");
    }
    public async Task Step(object activity,DateTime observedAt){
        if(!Active)return;LastActivity=activity;
        try{
            if(stopRequested){await Stop();return;}
            if(watch!=null){
                var result=watch.Observe(J.Get(activity,"Performance"),observedAt);
                if(result==PerformanceResult.Interrupted){Detach(watch.Problem);return;}
                if(result==PerformanceResult.Waiting)return;
                if(result==PerformanceResult.Playing){Status="재생 중 · "+(Index+1)+" / "+queue.Count+" · "+queue[Index].Title;return;}
                if(J.B(J.Get(activity,"Performance"),"IsPlaying")){
                    // End reached or automatic restart observed: stop the current cycle
                    // before equipping another instrument or ending the playlist.
                    var fresh=await Call("get_activity",null);
                    if(!watch.Matches(J.Get(fresh,"Performance"))){Detach("연주가 바뀌어 다음 곡으로 전환하지 않습니다.");return;}
                    await Call("stop_action",null);
                    activity=await Call("get_activity",null);
                    if(J.B(J.Get(activity,"Performance"),"IsPlaying")){Detach("연주 중지를 확인하지 못했습니다. 게임에서 중지하세요.");return;}
                }
                watch=null;
                if(stopRequested){Detach("주크박스 중지됨");return;}
                Index++;
                if(Index>=queue.Count){if(!repeat){Detach("재생 목록 완료");return;}Index=0;}
            }
            if(!Ready(activity)){Detach("다른 게임 행동이 감지되어 재생 목록을 멈췄습니다.");return;}
            var entry=queue[Index];
            var scores=J.Rows(await Call("get_music_scores",null));
            var instruments=J.Rows(await Call("get_instruments",null));
            if(stopRequested){await Stop();return;}
            if(!scores.Any(x=>J.S(x,"DisplayTitle")==entry.Title)){Detach("악보를 찾을 수 없습니다: "+entry.Title);return;}
            if(!instruments.Any(x=>J.S(x,"Name")==entry.Instrument)){Detach("악기를 찾을 수 없습니다: "+entry.Instrument);return;}
            // Re-read just before mutating: another action may have started during catalog reads.
            activity=await Call("get_activity",null);
            if(stopRequested){await Stop();return;}
            if(!Ready(activity)){Detach("다른 게임 행동이 감지되어 재생 목록을 멈췄습니다.");return;}
            if(J.S(J.Get(activity,"Performance"),"InstrumentName")!=entry.Instrument){
                await Call("change_instrument",J.Obj("name",entry.Instrument));
                if(stopRequested){await Stop();return;}
                activity=await Call("get_activity",null);
                if(stopRequested){await Stop();return;}
                if(!Ready(activity)||J.S(J.Get(activity,"Performance"),"InstrumentName")!=entry.Instrument){Detach("악기 교체를 확인하지 못했습니다. 게임 상태를 확인하세요.");return;}
            }
            watch=new PerformanceWatch(entry.Title,entry.Instrument,DateTime.UtcNow);
            await Call("play_music_score",J.Obj("title",entry.Title));
            if(stopRequested){await Stop();return;}
            SetStatus("연주 시작 확인 중 · "+entry.Title);
            activity=await Call("get_activity",null);
            if(stopRequested){await Stop();return;}
            var started=watch.Observe(J.Get(activity,"Performance"),DateTime.UtcNow);
            if(started==PerformanceResult.Interrupted)Detach(watch.Problem);
            else if(started==PerformanceResult.Playing)Status="재생 중 · "+(Index+1)+" / "+queue.Count+" · "+entry.Title;
        }catch(Exception ex){log("주크박스 오류 상세 · "+ex.Message);Detach("재생 중지 · "+FriendlyText.Error(ex)+" 게임의 현재 연주도 확인하세요.");}
    }
}
}
