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
class GatherProbeBridge:IBridge {
    public TaskCompletionSource<Reply> Pending=new TaskCompletionSource<Reply>();public int Reads,Stops;public bool Cancel;public bool Allowed=true;
    public Task<Reply> Call(string command,object body){
        if(command=="get_altering_works"){Reads++;if(Cancel){Allowed=false;Pending.SetResult(new Reply{Data=J.Obj("result","completed")});}return Task.FromResult(new Reply{Data=J.Obj("works",new[]{J.Obj("FacilityName","wood","IsCompleted",Reads>=2)})});}
        Stops++;Pending.SetResult(new Reply{Data=J.Obj("result","stopped_by_user")});return Task.FromResult(new Reply{Data=J.Obj("status","accepted")});
    }
}
class RetryStopBridge:IBridge {
    public int Stops,Reads;public bool Other,Finish;public TaskCompletionSource<Reply> Pending=new TaskCompletionSource<Reply>();
    public Task<Reply> Call(string command,object body){
        if(command=="get_activity"){Reads++;if(Finish)Pending.TrySetResult(new Reply());return Task.FromResult(new Reply{Data=J.Obj("IsAutoPlaying",true,"IsInCombat",Other,"Dungeon",J.Obj("State","NotInDungeon"),"Mode",J.Obj("MainButtonState",Reads==1?"Compass":"Stop"),"Interaction",J.Obj("AvailableInteractionType","Gathering"))});}
        Stops++;return Task.FromResult(new Reply{Data=Stops<3?J.Obj("status","rejected","body",J.Obj("error","invalid_state")):J.Obj("status","accepted")});
    }
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
    static void SettingsDuringPoll(){
        using(var form=new MainForm(true,true)){
            var canEdit=typeof(MainForm).GetMethod("CanEdit",BindingFlags.NonPublic|BindingFlags.Instance);
            Set(form,"busy",true);Set(form,"polling",true);
            Assert((bool)canEdit.Invoke(form,null),"read-only polling blocked settings");
            Set(form,"actionOwned",true);Assert(!(bool)canEdit.Invoke(form,null),"game action allowed settings");Set(form,"actionOwned",false);
            Set(form,"polling",false);Assert(!(bool)canEdit.Invoke(form,null),"manual operation allowed settings");
            Set(form,"busy",false);Assert((bool)canEdit.Invoke(form,null),"idle settings blocked");
        }
        Console.WriteLine("PASS settings remain editable during polling, protected during actions");
    }
    static async Task GatherChecks(){
        var facilities=new System.Collections.Generic.HashSet<string>{"wood"};
        Assert(!GatherPreemption.Ready(new object[0],facilities),"empty queue interrupted");
        Assert(!GatherPreemption.Ready(new[]{J.Obj("FacilityName","wood","IsCompleted",true),J.Obj("FacilityName","wood","IsCompleted",false)},facilities),"partial queue interrupted");
        Assert(!GatherPreemption.Ready(new[]{J.Obj("FacilityName","metal","IsCompleted",true)},facilities),"unrelated facility interrupted");
        var bridge=new GatherProbeBridge();var result=await GatherPreemption.Wait(bridge,bridge.Pending.Task,facilities,()=>bridge.Allowed,async()=>{var r=await bridge.Call("stop_action",null);r.Check();},1);
        Assert(result.Interrupted&&bridge.Reads==2&&bridge.Stops==1&&J.S(result.Reply.Data,"result")=="stopped_by_user","completed queue did not interrupt once");
        bridge=new GatherProbeBridge{Cancel=true};result=await GatherPreemption.Wait(bridge,bridge.Pending.Task,facilities,()=>bridge.Allowed,async()=>{await bridge.Call("stop_action",null);},1);
        Assert(!result.Interrupted&&bridge.Stops==0,"stop issued after cancellation or gathering completion");
        bridge=new GatherProbeBridge();result=await GatherPreemption.Wait(bridge,bridge.Pending.Task,new System.Collections.Generic.HashSet<string>(),()=>true,async()=>{await bridge.Call("stop_action",null);},1,null,10);
        Assert(result.Interrupted&&bridge.Stops==1,"periodic inventory recheck must stop without processing goals");
        bridge=new GatherProbeBridge();var stalled=new TaskCompletionSource<bool>();result=await GatherPreemption.Wait(bridge,bridge.Pending.Task,new System.Collections.Generic.HashSet<string>(),()=>true,async()=>{await bridge.Call("stop_action",null);},1,null,10,()=>stalled.Task);
        Assert(result.Interrupted&&bridge.Stops==1,"stalled status query blocked periodic stop");stalled.SetResult(true);
        bridge=new GatherProbeBridge();int inventoryReads=0;
        result=await GatherPreemption.Wait(bridge,bridge.Pending.Task,new System.Collections.Generic.HashSet<string>(),()=>true,async()=>{await bridge.Call("stop_action",null);},1,null,1,null,null,()=>Task.FromResult(++inventoryReads>=3?"target reached":null));
        Assert(inventoryReads==3&&result.Interrupted&&bridge.Stops==1,"inventory mode used timer instead of actual target");
        Console.WriteLine("PASS collection preemption: full queue only, one stop, unrelated and cancelled actions protected");
    }
    static async Task RetryChecks(){
        var b=new RetryStopBridge();await GatherPreemption.StopWhenAvailable(b,b.Pending.Task,()=>true,null,1);Assert(b.Stops==3&&b.Reads==3,"transient invalid_state did not retry across action gaps");
        b=new RetryStopBridge{Finish=true};await GatherPreemption.StopWhenAvailable(b,b.Pending.Task,()=>true,null,1);Assert(b.Stops==1,"retried after gathering completed");
        b=new RetryStopBridge{Other=true};bool cancelled=false;try{await GatherPreemption.StopWhenAvailable(b,b.Pending.Task,()=>true,null,1);}catch(OperationCanceledException){cancelled=true;}Assert(cancelled&&b.Stops==1,"stopped unrelated combat");
        b=new RetryStopBridge();await GatherPreemption.StopWhenAvailable(b,b.Pending.Task,()=>false,null,1);Assert(b.Stops==0,"stopped after user cancellation");
        Console.WriteLine("PASS transient stop rejection retry, natural completion, other action and user cancellation");
    }
    [STAThread]static int Main(){try{RetryChecks().GetAwaiter().GetResult();GatherChecks().GetAwaiter().GetResult();Application.EnableVisualStyles();SettingsDuringPoll();Run("known disconnected with pending flags",false,"hang",false);Run("stale connection now disconnected",true,"disconnected",false);Run("unresponsive status bounded exit",true,"hang",false);Run("connected operation retains guard",true,"connected",true);Cleanup();return 0;}catch(Exception ex){Console.WriteLine("FAIL "+ex);return 1;}}
}
