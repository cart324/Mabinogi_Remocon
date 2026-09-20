using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MabiRemote;
class MusicLiveCheck {
    static string logFile;
    static void Log(string text){Console.WriteLine(text);File.AppendAllText(logFile,text+Environment.NewLine,Encoding.UTF8);}
    static object Read(IBridge b,string command,object body=null){var r=b.Call(command,body).GetAwaiter().GetResult();r.Check();return J.Unwrap(r.Data);}
    static int Main(string[] args){
        if(args.Length<2)return 2;logFile=args[1];File.WriteAllText(logFile,"",Encoding.UTF8);
        var bridge=new GameBridge(args[0]);JukeboxPlayer player=null;string original="",other="";
        try{
            var activity=Read(bridge,"get_activity");if(new Snapshot{Activity=activity}.Busy(false)){Log("SKIP: 캐릭터가 다른 행동 중입니다.");return 3;}
            original=J.S(J.Get(activity,"Performance"),"InstrumentName");var instruments=J.Rows(Read(bridge,"get_instruments"));other=instruments.Select(x=>J.S(x,"Name")).FirstOrDefault(x=>x=="피아노"&&x!=original)??instruments.Select(x=>J.S(x,"Name")).FirstOrDefault(x=>x!=original);
            string title="악보: 세탁기 알림음";if(original==""||other==null||!J.Rows(Read(bridge,"get_music_scores")).Any(x=>J.S(x,"DisplayTitle")==title)){Log("SKIP: 테스트 악보나 악기를 찾지 못했습니다.");return 3;}
            player=new JukeboxPlayer(bridge,Log);player.Start(new[]{new PlaylistEntry{Title=title,Instrument=original},new PlaylistEntry{Title=title,Instrument=other}},0,false);
            var clock=System.Diagnostics.Stopwatch.StartNew();string last="";
            while(player.Active&&clock.Elapsed.TotalSeconds<45){
                activity=Read(bridge,"get_activity");player.Step(activity,DateTime.UtcNow).GetAwaiter().GetResult();var perf=J.Get(player.LastActivity,"Performance");string sample=J.Json(perf);if(sample!=last){Log("SAMPLE "+sample);last=sample;}Thread.Sleep(player.NearEnd?250:750);
            }
            if(player.Active){player.RequestStop();player.Step(Read(bridge,"get_activity"),DateTime.UtcNow).GetAwaiter().GetResult();Log("FAIL: 시간 제한으로 테스트 종료");return 1;}
            bool pass=player.Status=="재생 목록 완료";Log((pass?"PASS: ":"FAIL: ")+player.Status);return pass?0:1;
        }catch(Exception ex){Log("ERROR: "+ex);return 1;}
        finally{
            try{if(player!=null&&player.Active){player.RequestStop();player.Step(Read(bridge,"get_activity"),DateTime.UtcNow).GetAwaiter().GetResult();}
                var activity=Read(bridge,"get_activity");if(original!=""&&J.S(J.Get(activity,"Performance"),"InstrumentName")==other&&!new Snapshot{Activity=activity}.Busy(false)){Read(bridge,"change_instrument",J.Obj("name",original));Log("RESTORED: "+original);}}
            catch(Exception ex){Log("RESTORE CHECK: "+ex.Message);}
        }
    }
}
