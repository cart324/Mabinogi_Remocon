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
    public static async Task<GatherWaitResult> Wait(IBridge bridge,Task<Reply> pending,HashSet<string> facilities,Func<bool> allowed,Func<Task> stop,int interval=2000,Action<string> warning=null){
        bool interrupted=false;
        try{while(!pending.IsCompleted&&allowed()&&facilities.Count>0){
            if(await Task.WhenAny(pending,Task.Delay(interval))==pending)break;
            if(!allowed())break;
            var reply=await bridge.Call("get_altering_works",null);
            if(pending.IsCompleted||!allowed())break;
            reply.Check();
            if(!Ready(J.Rows(J.Get(J.Unwrap(reply.Data),"works")),facilities))continue;
            await stop();interrupted=true;break;
        }
        }catch(Exception ex){if(warning!=null)warning(ex.Message);}
        return new GatherWaitResult{Reply=await pending,Interrupted=interrupted};
    }
}
public partial class MainForm {
    readonly HashSet<string> autoWorkFacilities=new HashSet<string>();
    HashSet<string> CollectionFacilities(){var set=new HashSet<string>(autoWorkFacilities);foreach(var goal in cfg.Goals.Where(x=>x.Enabled))set.Add(Facilities.ForRecipe(cfg,goal.Name));return set;}
    async Task StopGatherForCollection(){
        stopping=true;
        try{Log("가공 시설의 모든 작업 완료 · 채집/낚시를 중단하고 수령합니다.");var reply=await bridge.Call("stop_action",null);reply.Check();}
        finally{stopping=false;}
    }
}
}
