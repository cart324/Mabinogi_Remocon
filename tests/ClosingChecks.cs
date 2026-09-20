using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MabiRemote;
class CloseBridge:IBridge,IDisposable {
    public string Mode;public bool Disposed;public int Calls;
    public Task<Reply> Call(string command,object body){Calls++;if(Mode=="hang")return new TaskCompletionSource<Reply>().Task;return Task.FromResult(new Reply{Data=J.Obj("pipe",Mode)});}
    public void Dispose(){Disposed=true;}
}
class ClosingChecks {
    static void Set(object f,string key,object val){typeof(MainForm).GetField(key,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(f,val);}
    static void Assert(bool value,string text){if(!value)throw new Exception(text);}
    static void Run(string name,bool connected,string mode,bool protect){
        var b=new CloseBridge{Mode=mode};var form=new MainForm(true,true);var clock=Stopwatch.StartNew();bool closed=false,protectedState=false,timedOut=false;
        using(var guard=new System.Windows.Forms.Timer{Interval=5000})using(var second=new System.Windows.Forms.Timer{Interval=200}){
            guard.Tick+=(s,e)=>{timedOut=true;Set(form,"connected",false);form.Close();};
            second.Tick+=(s,e)=>{second.Stop();protectedState=!closed&&!form.IsDisposed;Set(form,"connected",false);form.Close();};
            form.FormClosed+=(s,e)=>{closed=true;};
            form.Shown+=(s,e)=>{Set(form,"bridge",b);Set(form,"connected",connected);Set(form,"busy",true);Set(form,"actionOwned",true);Set(form,"stopping",true);Set(form,"ownsFishing",true);clock.Restart();guard.Start();if(protect)second.Start();form.Close();};
            Application.Run(form);guard.Stop();second.Stop();
        }
        Assert(closed&&!timedOut&&b.Disposed,name+" did not close");
        if(!connected)Assert(b.Calls==0&&clock.Elapsed.TotalSeconds<1,name+" waited for game");
        if(protect)Assert(protectedState,"live game lost close guard");
        Console.WriteLine("PASS "+name+" ("+clock.ElapsedMilliseconds+" ms)");
    }
    static void Cleanup(){
        string pidFile=Path.Combine(Path.GetTempPath(),"mabi-close-"+Guid.NewGuid().ToString("N")+".txt");var bridge=new GameBridge(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"FakeCLI.exe"));
        try{
            var pending=bridge.Call("hang",pidFile);var wait=Stopwatch.StartNew();while(!File.Exists(pidFile)&&wait.ElapsedMilliseconds<3000)Thread.Sleep(20);
            Assert(File.Exists(pidFile),"CLI did not start");int pid=Int32.Parse(File.ReadAllText(pidFile));bridge.Dispose();
            try{Assert(pending.Wait(3000),"CLI did not exit");}catch(AggregateException){}
            bool exited;try{using(var child=Process.GetProcessById(pid))exited=child.HasExited;}catch(ArgumentException){exited=true;}
            Assert(exited,"owned CLI left running");
            bool blocked=false;try{bridge.Call("echo",null).GetAwaiter().GetResult();}catch(ObjectDisposedException){blocked=true;}
            Assert(blocked,"new CLI started after close");Console.WriteLine("PASS pending CLI cleanup and no launch after disposal");
        }finally{bridge.Dispose();if(File.Exists(pidFile))File.Delete(pidFile);}
    }
    [STAThread]static int Main(){try{Application.EnableVisualStyles();Run("known disconnected with pending flags",false,"hang",false);Run("stale connection now disconnected",true,"disconnected",false);Run("unresponsive status bounded exit",true,"hang",false);Run("connected operation retains guard",true,"connected",true);Cleanup();return 0;}catch(Exception ex){Console.WriteLine("FAIL "+ex);return 1;}}
}
