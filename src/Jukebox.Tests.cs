using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace MabiRemote {
// Offline transport fixtures. No real game commands are sent by these checks.
public sealed class MusicTestBridge:IBridge {
    public List<string> Calls=new List<string>();
    public string Equipped="하프",Title="",StartAt="";
    public bool Playing,Loop,RejectChange,RejectPlay,MissingPerformance,InCombat;
    public double Remaining=60,Total=60;
    public Action<string> OnCall;
    int serial;
    public object Performance(){return J.Obj("IsPlaying",Playing,"MusicTitle",Title,"InstrumentName",Equipped,"StartAt",StartAt,"IsLoop",Loop,"TotalDurationSeconds",Playing?Total:0,"ElapsedSeconds",Playing?Total-Remaining:0,"RemainingSeconds",Playing?Remaining:0);}
    public object Activity(){return J.Obj("IsInCombat",InCombat,"IsDead",false,"Dungeon",J.Obj("State","NotInDungeon"),"Mode",J.Obj("MainButtonState",Playing?"Stop":"Compass","MountPartState","None"),"Performance",MissingPerformance?null:Performance());}
    public Task<Reply> Call(string cmd,object body){
        Calls.Add(cmd+":"+J.S(body,cmd=="change_instrument"?"name":"title"));object result=null;
        if(cmd=="get_activity")result=Activity();
        else if(cmd=="get_instruments")result=new[]{J.Obj("Name","피아노","Durability",100,"IsEquipped",Equipped=="피아노"),J.Obj("Name","하프","Durability",100,"IsEquipped",Equipped=="하프")};
        else if(cmd=="get_music_scores")result=new[]{J.Obj("DisplayTitle","악보: 첫 곡","Location","inventory","IsLocked",false),J.Obj("DisplayTitle","악보: 두 번째 곡","Location","account_storage","IsLocked",false)};
        else if(cmd=="change_instrument"){if(RejectChange)result=J.Obj("status","rejected","body",J.Obj("error","failed_unequip"));else{Equipped=J.S(body,"name");result=J.Obj("status","accepted");}}
        else if(cmd=="play_music_score"){if(RejectPlay)result=J.Obj("status","rejected","body",J.Obj("error","no_instrument"));else{Title=MusicLibrary.TitleKey(J.S(body,"title"));Playing=true;StartAt="performance-"+(++serial);Remaining=Total;result=J.Obj("status","accepted");}}
        else if(cmd=="stop_action"){Playing=false;Title="";result=J.Obj("status","accepted");}
        else result=J.Obj("status","rejected","error","unsupported_command");
        if(OnCall!=null)OnCall(cmd);
        return Task.FromResult(new Reply{Data=result});
    }
}
public static class JukeboxTests {
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    static object P(bool playing,double remaining=10,double total=60,string title="첫 곡",string start="1"){return J.Obj("IsPlaying",playing,"MusicTitle",title,"InstrumentName","하프","StartAt",start,"TotalDurationSeconds",total,"RemainingSeconds",remaining,"ElapsedSeconds",total-remaining);}
    static PlaylistEntry[] List(){return new[]{new PlaylistEntry{Title="악보: 첫 곡",Instrument="하프"},new PlaylistEntry{Title="악보: 두 번째 곡",Instrument="피아노"}};}
    static void Step(JukeboxPlayer player,MusicTestBridge bridge){player.Step(bridge.Activity(),DateTime.UtcNow).GetAwaiter().GetResult();}
    static void Finish(JukeboxPlayer player,MusicTestBridge bridge){bridge.Remaining=0;Step(player,bridge);}
    public static void Run(Action<string,Action> check){
        check("주크박스 설정 저장·이전 설정 호환",()=>{var c=new Settings{Playlist=List().ToList(),PlaylistRepeat=true};var read=J.Serializer().Deserialize<Settings>(J.Json(c));Assert(read.Playlist.Count==2&&read.Playlist[1].Instrument=="피아노"&&read.PlaylistRepeat,"roundtrip");Assert(J.Serializer().Deserialize<Settings>("{}").Playlist.Count==0,"legacy");});
        check("악보 보관 위치 정렬·악기 미제공 위치 구분",()=>{var list=new[]{J.Obj("Name","상자","Location","account_storage"),J.Obj("Name","모름"),J.Obj("Name","가방","Location","inventory"),J.Obj("Name","캐릭터","Location","character_storage")};var rows=MusicLibrary.Sort(list,"Name");Assert(J.S(rows[0],"Name")=="가방"&&J.S(rows[3],"Name")=="모름","sort");Assert(MusicLibrary.Location(list[1])=="위치 정보 없음","unknown");});
        check("악보 제목 접두어를 제외한 실제 연주 제목 일치",()=>{Assert(MusicLibrary.TitleMatches("악보: 첫 곡","첫 곡")&&!MusicLibrary.TitleMatches("첫 곡","다른 곡"),"display title");});
        check("연주 종료 상태와 예상 종료 시간을 함께 확인",()=>{var now=DateTime.UtcNow;var w=new PerformanceWatch("악보: 첫 곡","하프",now);Assert(w.Observe(P(true,1),now)==PerformanceResult.Playing,"started");Assert(w.Observe(P(false),now.AddSeconds(1))==PerformanceResult.Finished,"finished");});
        check("연주 중단·샘플 공백·누락된 길이에서 다음 곡 차단",()=>{var now=DateTime.UtcNow;var w=new PerformanceWatch("첫 곡","하프",now);w.Observe(P(true,20),now);Assert(w.Observe(P(false),now.AddSeconds(1))==PerformanceResult.Interrupted,"early stop");w=new PerformanceWatch("첫 곡","하프",now);w.Observe(P(true,1),now);Assert(w.Observe(P(false),now.AddSeconds(30))==PerformanceResult.Interrupted,"long gap");w=new PerformanceWatch("첫 곡","하프",now);w.Observe(P(true,0,0),now);Assert(w.Observe(P(false),now.AddSeconds(1))==PerformanceResult.Interrupted,"unknown duration");});
        check("연주 시작 미확인·반복·다른 연주 감지",()=>{var now=DateTime.UtcNow;var w=new PerformanceWatch("첫 곡","하프",now);Assert(w.Observe(P(false),now.AddSeconds(2))==PerformanceResult.Waiting,"await start");Assert(w.Observe(P(false),now.AddSeconds(11))==PerformanceResult.Interrupted,"start timeout");w=new PerformanceWatch("첫 곡","하프",now);w.Observe(P(true),now);Assert(w.Observe(P(true,9,60,"첫 곡","2"),now.AddSeconds(1))==PerformanceResult.Interrupted,"same title different performance");w=new PerformanceWatch("첫 곡","하프",now);var p=P(true) as Dictionary<string,object>;p["IsLoop"]=true;Assert(w.Observe(p,now)==PerformanceResult.Playing,"loop can be advanced");});
        check("연주 완료 후에만 악기 교체·다음 곡 재생",()=>{var b=new MusicTestBridge();var p=new JukeboxPlayer(b,x=>{});p.Start(List(),0,false);Step(p,b);Assert(b.Playing&&p.Active&&b.Calls.Count(x=>x.StartsWith("play_music_score:"))==1,"first song");Assert(!b.Calls.Any(x=>x.StartsWith("change_instrument:")),"keep equipped");Step(p,b);Assert(b.Calls.Count(x=>x.StartsWith("play_music_score:"))==1,"no duplicate");Finish(p,b);Assert(b.Equipped=="피아노"&&b.Title=="두 번째 곡"&&p.Index==1,"next song");Assert(b.Calls.IndexOf("change_instrument:피아노")<b.Calls.IndexOf("play_music_score:악보: 두 번째 곡"),"order");Finish(p,b);Assert(!p.Active&&p.Status=="재생 목록 완료","done");});
        check("악기 교체 실패·악보 재생 실패에서 예약 중단",()=>{var b=new MusicTestBridge{RejectChange=true};var p=new JukeboxPlayer(b,x=>{});p.Start(List(),1,false);Step(p,b);Assert(!p.Active&&!b.Calls.Any(x=>x.StartsWith("play_music_score:")),"equip rejected");b=new MusicTestBridge{RejectPlay=true};p=new JukeboxPlayer(b,x=>{});p.Start(List(),0,false);Step(p,b);Assert(!p.Active&&b.Calls.Count(x=>x.StartsWith("play_music_score:"))==1,"no retry");});
        check("수동 연주에 개입하지 않고 목록만 중단",()=>{var b=new MusicTestBridge{Playing=true,Title="다른 곡"};var p=new JukeboxPlayer(b,x=>{});p.Start(List(),0,false);Step(p,b);Assert(!p.Active&&!b.Calls.Any(x=>x.StartsWith("stop_action:")||x.StartsWith("play_music_score:")),"leave manual music");b=new MusicTestBridge();p=new JukeboxPlayer(b,x=>{});p.Start(List(),0,false);Step(p,b);b.StartAt="manual";p.RequestStop();Step(p,b);Assert(b.Playing&&!b.Calls.Any(x=>x.StartsWith("stop_action:")),"do not stop replaced performance");});
        check("주크박스 중지 및 명령 응답 중 취소",()=>{var b=new MusicTestBridge();var p=new JukeboxPlayer(b,x=>{});p.Start(List(),0,false);Step(p,b);p.RequestStop();Step(p,b);Assert(!p.Active&&!b.Playing&&b.Calls.Count(x=>x.StartsWith("stop_action:"))==1,"stop owned");b=new MusicTestBridge();p=new JukeboxPlayer(b,x=>{});b.OnCall=cmd=>{if(cmd=="play_music_score")p.RequestStop();};p.Start(List(),0,false);Step(p,b);Assert(!p.Active&&!b.Playing&&b.Calls.Count(x=>x.StartsWith("play_music_score:"))==1,"stop after in-flight play");});
        check("악기 교체 중 취소와 전투 감지 시 재생 차단",()=>{var b=new MusicTestBridge();var p=new JukeboxPlayer(b,x=>{});b.OnCall=cmd=>{if(cmd=="change_instrument")p.RequestStop();};p.Start(List(),1,false);Step(p,b);Assert(!p.Active&&!b.Calls.Any(x=>x.StartsWith("play_music_score:")),"cancel before play");b=new MusicTestBridge();p=new JukeboxPlayer(b,x=>{});b.OnCall=cmd=>{if(cmd=="get_music_scores")b.InCombat=true;};p.Start(List(),0,false);Step(p,b);Assert(!p.Active&&!b.Calls.Any(x=>x.StartsWith("play_music_score:")),"combat during reads");});
        check("자동 반복 시작 감지 후 중지·악기 교체·다음 곡",()=>{var now=DateTime.UtcNow;var w=new PerformanceWatch("첫 곡","하프",now);w.Observe(P(true,1),now);Assert(w.Observe(P(true,59,60,"첫 곡","2"),now.AddSeconds(2))==PerformanceResult.Finished,"rollover");var b=new MusicTestBridge{Loop=true};var p=new JukeboxPlayer(b,x=>{});p.Start(List(),0,false);Step(p,b);Finish(p,b);int stop=b.Calls.IndexOf("stop_action:"),equip=b.Calls.IndexOf("change_instrument:피아노"),play=b.Calls.IndexOf("play_music_score:악보: 두 번째 곡");Assert(stop>=0&&stop<equip&&equip<play&&p.Active,"stop before changing instrument");Finish(p,b);Assert(!p.Active&&!b.Playing,"final song stops too");});
        check("실제 API의 반복 시 누적 경과 시간·남은 시간 초기화",()=>{var now=DateTime.UtcNow;var w=new PerformanceWatch("악보: 세탁기 알림음","하프",now);var before=P(true,0.172125816,10.7833319,"세탁기 알림음") as Dictionary<string,object>;before["ElapsedSeconds"]=10.6112061;before["IsLoop"]=true;Assert(w.Observe(before,now)==PerformanceResult.Playing,"near end");var after=P(true,10.6350231,10.7833319,"세탁기 알림음") as Dictionary<string,object>;after["ElapsedSeconds"]=10.9316406;after["IsLoop"]=true;Assert(w.Observe(after,now.AddSeconds(0.32))==PerformanceResult.Finished,"cumulative elapsed passes first duration");});
        check("목록 반복과 연결 해제 후 자동 재시작 방지",()=>{var b=new MusicTestBridge();var p=new JukeboxPlayer(b,x=>{});p.Start(List(),1,true);Step(p,b);Finish(p,b);Assert(p.Active&&p.Index==0&&b.Title=="첫 곡","repeat");p.Detach("연결 해제");int count=b.Calls.Count;Step(p,b);Assert(b.Calls.Count==count&&!p.Active,"no resume");});
    }
}
}
