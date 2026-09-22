using System;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace MabiRemote {
public partial class MainForm {
    bool checkingCloseConnection;
    static void ObserveLateStatus(Task<Reply> status){status.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
    async void OnClosing(object sender,FormClosingEventArgs e){
        if(closing)return;
        bool pending=actionOwned||stopping||busy||MusicActive||ownsFishing;
        // A disconnected game cannot acknowledge stop_action. Never wait for it to close the app.
        if(connected&&pending){
            e.Cancel=true;
            if(checkingCloseConnection)return;
            checkingCloseConnection=true;
            bool unavailable=false;
            try{
                var status=bridge.Call("status",null);
                if(await Task.WhenAny(status,Task.Delay(2000))!=status){
                    // Observe any late fault, but do not keep the window trapped by a hung CLI.
                    ObserveLateStatus(status);
                    unavailable=true;
                }else{var reply=await status;unavailable=reply.ExitCode!=0||J.S(reply.Data,"pipe")!="connected";}
            }catch{unavailable=true;}
            finally{checkingCloseConnection=false;}
            if(closing||IsDisposed)return;
            if(unavailable||!connected){connected=false;BeginInvoke(new Action(Close));return;}
            if(MusicActive){RequestMusicStop();Notice("주크박스 연주를 중지한 후 종료하세요.");}
            else Notice("작업이 진행 중입니다. 작업을 중지한 후 종료하세요.");
            return;
        }
        EndActivityDiagnostic("프로그램 종료");closing=true;auto=false;stopRequested=true;generation++;
        timer.Stop();ownsFishing=false;actionOwned=false;busy=false;stopping=false;
        if(MusicActive)jukebox.Detach("프로그램 종료");
        Save();
        // Dispose only the CLI processes launched by this instance. Never terminate the game.
        var disposable=bridge as IDisposable;if(disposable!=null)disposable.Dispose();
        tray.Visible=false;tray.Dispose();foreach(var im in iconCache.Values)im.Dispose();
    }
}
}
