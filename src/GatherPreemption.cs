using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace MabiRemote {
public class GatherWaitResult { public Reply Reply; public bool Interrupted; }
public static class GatherPreemption {
    public static bool Ready(IEnumerable<object> works,HashSet<string> facilities){
        return works.GroupBy(x=>J.S(x,"FacilityName")).Any(g=>facilities.Contains(g.Key)&&g.All(x=>J.B(x,"IsCompleted")));
    }
    static async void ObserveLate(Task task){try{await task;}catch{}}
    static async Task<bool> Query(IBridge bridge,HashSet<string> facilities,Func<Task> observe){
        bool ready=false;if(facilities.Count>0){var reply=await bridge.Call("get_altering_works",null);reply.Check();ready=Ready(J.Rows(J.Get(J.Unwrap(reply.Data),"works")),facilities);}
        if(observe!=null)await observe();return ready;
    }
    public static async Task<GatherWaitResult> Wait(IBridge bridge,Task<Reply> pending,HashSet<string> facilities,Func<bool> allowed,Func<Task> stop,int interval=2000,Action<string> warning=null,int maxDurationMs=300000,Func<Task> observe=null,Action<string> reason=null,Func<Task<string>> inventoryDecision=null){
        if(inventoryDecision!=null){
            bool stopped=false;
            while(!pending.IsCompleted&&allowed()){
                if(await Task.WhenAny(pending,Task.Delay(interval))==pending||!allowed())break;
                var query=inventoryDecision();
                if(await Task.WhenAny(pending,query)!=query){ObserveLate(query);break;}
                string decision=null;try{decision=await query;}catch(Exception ex){if(warning!=null)warning(ex.Message);}
                if(pending.IsCompleted||!allowed())break;
                if(String.IsNullOrEmpty(decision))continue;
                if(reason!=null)reason(decision);
                try{await stop();stopped=true;}catch(Exception ex){if(warning!=null)warning(ex.Message);}break;
            }
            return new GatherWaitResult{Reply=await pending,Interrupted=stopped};
        }
        bool interrupted=false;var clock=System.Diagnostics.Stopwatch.StartNew();
        while(!pending.IsCompleted&&allowed()){
            int remaining=Math.Max(1,maxDurationMs-(int)clock.ElapsedMilliseconds);
            if(await Task.WhenAny(pending,Task.Delay(Math.Min(interval,remaining)))==pending)break;
            if(!allowed())break;
            bool timedOut=clock.ElapsedMilliseconds>=maxDurationMs,ready=false;
            if(!timedOut){
                var query=Query(bridge,facilities,observe);
                var winner=await Task.WhenAny(pending,query,Task.Delay(Math.Max(1,maxDurationMs-(int)clock.ElapsedMilliseconds)));
                if(winner!=query){ObserveLate(query);if(winner==pending||!allowed())break;timedOut=true;}
                else try{ready=await query;}catch(Exception ex){if(warning!=null)warning(ex.Message);}
            }
            if(pending.IsCompleted||!allowed())break;
            if(!timedOut&&!ready)continue;
            if(reason!=null)reason(timedOut?"채집 5분 경과 · 중단 후 재고를 다시 확인합니다.":"가공 시설의 모든 작업 완료 · 채집/낚시를 중단하고 수령합니다.");
            try{await stop();interrupted=true;}catch(Exception ex){if(warning!=null)warning(ex.Message);}
            break;
        }
        return new GatherWaitResult{Reply=await pending,Interrupted=interrupted};
    }

}
public partial class MainForm {
    readonly HashSet<string> autoWorkFacilities=new HashSet<string>();
    HashSet<string> CollectionFacilities(){var set=new HashSet<string>(autoWorkFacilities);foreach(var goal in cfg.Goals.Where(x=>x.Enabled))set.Add(Facilities.ForRecipe(cfg,goal.Name));return set;}
    async Task ObserveGatherActivity(int stamp,Func<bool> active){
        var environment=await bridge.Call("get_current_environment",null);environment.Check();
        var activity=await bridge.Call("get_activity",null);activity.Check();
        if(closing||!actionOwned||generation!=stamp||!active())return;
        snap.Environment=J.Unwrap(environment.Data);snap.Activity=J.Unwrap(activity.Data);snap.ActivityAt=DateTime.UtcNow;
        UpdateLiveLabels();
    }
    async Task<string> CheckGatherInventory(Plan plan,int stamp,Func<bool> active){
        var items=await bridge.Call("get_items",null);items.Check();
        if(closing||generation!=stamp||!active())return null;
        var fresh=new Snapshot{At=DateTime.UtcNow,Items=J.Rows(J.Unwrap(items.Data)),Recipes=snap.Recipes,Gatherables=snap.Gatherables};
        if(plan.GatherTarget>0&&fresh.Count(plan.Name,cfg.CountStorage)>=plan.GatherTarget)return plan.Name+" 목표 재고 도달 · 채집을 중단하고 계획을 다시 확인합니다.";
        var works=await bridge.Call("get_altering_works",null);works.Check();fresh.Works=J.Rows(J.Get(J.Unwrap(works.Data),"works"));
        if(closing||generation!=stamp||!active())return null;
        if(Planner.ShouldInterruptForCollection(fresh,cfg))return "가공 완료 및 재등록 재료 확보 · 채집을 중단하고 수령합니다.";
        await ObserveGatherActivity(stamp,active);return null;
    }
    async Task StopGatherForCollection(){
        stopping=true;
        try{var reply=await bridge.Call("stop_action",null);reply.Check();}
        finally{stopping=false;}
    }
}
}
