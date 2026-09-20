using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
namespace MabiRemote {
public class RecipeIngredient {public string Material{get;set;} public int Count{get;set;} }
public class RecipeDefinition {public string Name{get;set;} public string Product{get;set;} public int ProducedPerWork{get;set;} public List<RecipeIngredient> Ingredients{get;set;} public List<List<RecipeIngredient>> AlternateIngredients{get;set;} public RecipeDefinition(){Ingredients=new List<RecipeIngredient>();} }
public class RecipeFile {public int Version{get;set;} public List<RecipeDefinition> Recipes{get;set;} public RecipeFile(){Version=1;} }
public static class RecipeBook {
    public static void AddMissingDefaults(Settings cfg){foreach(var definition in Defaults())if(!cfg.RecipeDefinitions.Any(x=>x.Name==definition.Name))cfg.RecipeDefinitions.Add(definition);}
    public static List<RecipeDefinition> Defaults(){return J.Serializer().Deserialize<RecipeFile>(Program.ReadEmbedded("recipes.json")).Recipes;}
    public static List<RecipeDefinition> Import(string json){
        if(json==null||json.Length>1024*1024)throw new InvalidDataException("배합표 파일이 너무 큽니다.");
        var data=J.Serializer().Deserialize<RecipeFile>(json.TrimStart('\uFEFF'));
        if(data==null||data.Version!=1||data.Recipes==null||data.Recipes.Count>1000)throw new InvalidDataException("배합표 형식 또는 버전을 확인하세요.");
        var names=new HashSet<string>();foreach(var r in data.Recipes){if(r==null||String.IsNullOrWhiteSpace(r.Name)||r.Name.Length>100||String.IsNullOrWhiteSpace(r.Product)||r.Product.Length>100||!names.Add(r.Name)||r.Ingredients==null||r.Ingredients.Count==0||r.Ingredients.Count>30||r.ProducedPerWork<1||r.ProducedPerWork>10000)throw new InvalidDataException("가공법 이름·완제품·재료 목록을 확인하세요.");
            var materials=new HashSet<string>();foreach(var i in r.Ingredients)if(i==null||String.IsNullOrWhiteSpace(i.Material)||i.Material.Length>100||i.Count<1||i.Count>100000||!materials.Add(i.Material))throw new InvalidDataException("재료 이름·1회 소모량·중복을 확인하세요.");}
        return data.Recipes;
    }
    public static RecipeDefinition Find(Settings cfg,string recipe){return cfg.RecipeDefinitions.FirstOrDefault(x=>x.Name==recipe);}
    static HashSet<string> Descendants(Settings cfg,string name,HashSet<string> visited){
        var result=new HashSet<string>();if(!visited.Add(name)||visited.Count>16)return result;var d=Find(cfg,name);if(d==null)return result;
        foreach(var ingredient in d.Ingredients)foreach(var child in cfg.RecipeDefinitions.Where(x=>x.Product==ingredient.Material)){result.Add(child.Name);result.Add(child.Product);result.UnionWith(Descendants(cfg,child.Name,new HashSet<string>(visited)));}return result;
    }
    public static int BatchSlots(Snapshot s,Settings cfg,string name){
        string facility=Facilities.ForRecipe(cfg,name);var children=Descendants(cfg,name,new HashSet<string>());children.Remove(name);var d=Find(cfg,name);if(d!=null)children.Remove(d.Product);
        // Intermediate products may use the same facility. Reserve the parent's batch
        // across those jobs; otherwise the batch shrinks each time a child is queued.
        int occupied=s.Works.Count(x=>J.S(x,"FacilityName")==facility&&!children.Contains(J.S(x,"DisplayName")));
        return Math.Max(0,Facilities.Total(cfg,facility)-occupied);
    }
    public static double Pending(Snapshot s,Settings cfg,string product){return s.Works.Where(w=>J.S(w,"DisplayName")==product||cfg.RecipeDefinitions.Any(d=>d.Name==J.S(w,"DisplayName")&&d.Product==product)).Sum(w=>{
        string name=J.S(w,"DisplayName");var recipe=s.Recipes.FirstOrDefault(x=>J.S(x,"DisplayName")==name)??s.Recipes.FirstOrDefault(x=>cfg.RecipeDefinitions.Any(d=>d.Name==J.S(x,"DisplayName")&&d.Product==product));return recipe==null?0:Math.Max(0,J.N(recipe,"ProducedPerWork"));
    });}
}
}
