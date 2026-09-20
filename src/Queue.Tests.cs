using System;
using System.Linq;
using System.Collections.Generic;
namespace MabiRemote {
public static class QueueTests {
    static void Assert(bool okay,string message){if(!okay)throw new Exception(message);}
    static void Count(Snapshot s,string name,int count){s.Items.RemoveAll(x=>J.S(x,"DisplayName")==name);s.Items.Add(J.Obj("DisplayName",name,"Count",count,"Location","inventory"));}
    static Plan Next(Snapshot s,Settings c){foreach(var recipe in s.Recipes){var d=RecipeBook.Find(c,J.S(recipe,"DisplayName"));((Dictionary<string,object>)recipe)["Alterable"]=d.Ingredients.All(i=>s.Count(i.Material,false)>=i.Count);}return Planner.Next(s,c,false,false,new Dictionary<string,DateTime>(),DateTime.UtcNow);}
    static Snapshot Sample(){return new Snapshot{At=DateTime.UtcNow,Activity=J.Obj("IsInCombat",false,"Dungeon",J.Obj("State","NotInDungeon"),"Mode",J.Obj("MainButtonState","Compass"))};}
    static void Recipe(Snapshot s,Settings c,string name){var d=RecipeBook.Find(c,name);s.Recipes.Add(J.Obj("DisplayName",name,"ProducedPerWork",d.ProducedPerWork,"Reason","not_enough_ingredient"));}
    static void Apply(Snapshot s,Settings c,Plan p){var d=RecipeBook.Find(c,p.Name);foreach(var i in d.Ingredients)Count(s,i.Material,s.Count(i.Material,false)-i.Count);s.Works.Add(J.Obj("DisplayName",p.Name,"FacilityName",Facilities.ForRecipe(c,p.Name),"IsCompleted",false));if(p.Goal!=null)p.Goal.RunsDone++;}
    public static void Replenishment(){
        var legacy=J.Serializer().Deserialize<Goal>("{\"Name\":\"목재+\"}");Assert(legacy.AutoReplenish,"legacy goal default");
        var c=new Settings();var s=Sample();var goal=new Goal{Name="목재+",Mode="unlimited",AutoReplenish=false};c.Goals.Add(goal);Recipe(s,c,"목재+");Recipe(s,c,"목재");Count(s,"목재",3);Count(s,"통나무",100);Count(s,"나무 진액",100);s.Gatherables.Add(J.Obj("DisplayName","통나무","ToolOk",true));
        var direct=Next(s,c);Assert(direct!=null&&direct.Name=="목재+","disabled replenishment must still register owned stock");Apply(s,c,direct);
        Assert(Next(s,c)==null,"disabled replenishment made intermediate wood");Count(s,"통나무",0);Assert(Next(s,c)==null,"disabled replenishment gathered logs");
        goal.AutoReplenish=true;Assert(Next(s,c).Name=="통나무","enabled replenishment did not gather");goal.AutoReplenish=false;
        c.Goals.Add(new Goal{Name="목재",Mode="runs",Target=1});Count(s,"통나무",10);Assert(Next(s,c).Name=="목재","independent goal incorrectly disabled");
        var saved=J.Serializer().Deserialize<Settings>(J.Json(c));Assert(!saved.Goals[0].AutoReplenish&&saved.Goals[1].AutoReplenish,"per-goal persistence");
    }
    public static void Run(){
        var c=new Settings();var s=Sample();c.Goals.Add(new Goal{Name="목재+",Mode="runs",Target=14});Recipe(s,c,"목재+");Recipe(s,c,"목재");Count(s,"목재",12);Count(s,"통나무",50);Count(s,"나무 진액",1000);s.Gatherables.Add(J.Obj("DisplayName","통나무","ToolOk",true));
        for(int i=0;i<4;i++){var p=Next(s,c);Assert(p.Command=="execute_altering"&&p.Name=="목재+","available parent stock must register first");Apply(s,c,p);}
        for(int i=0;i<3;i++){var p=Next(s,c);Assert(p.Command=="execute_altering"&&p.Name=="목재","remaining shared slots must register child");Apply(s,c,p);}
        var gather=Next(s,c);Assert(s.Works.Count==7&&s.Count("통나무",false)==20,"shared facility capacity and consumed logs");Assert(gather.Name=="통나무"&&gather.GatherTarget==40&&gather.GatherTarget-s.Count("통나무",false)==20,"next batch must subtract nine pending wood and gather only twenty missing logs");
        c.Goals.Add(new Goal{Name="실크",Mode="runs",Target=1});Recipe(s,c,"실크");Count(s,"거미줄",10);var ready=Next(s,c);Assert(ready.Name=="실크"&&ready.Command=="execute_altering","lower ready goal before higher future material");
        Count(s,"거미줄",0);s.Gatherables.Add(J.Obj("DisplayName","거미줄","ToolOk",true));var current=Next(s,c);Assert(current.Name=="거미줄","current empty slot materials before higher priority future materials");
        c.Goals[1].Enabled=false;Count(s,"통나무",40);Assert(Next(s,c)==null,"prepared next batch must not overgather or overfill");
        c.Goals[0].RunsDone=14;Count(s,"통나무",0);Assert(Next(s,c)==null,"finite completed goal must not prepare another batch");
    }
}
}
