using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
namespace MabiRemote {
public partial class MainForm {
    ComboBox autoFacilityCombo,manualFacilityCombo;CheckBox manualShowAll;Label manualInfoLabel;
    readonly List<string> facilityNames=new List<string>(Facilities.Names);
    string CurrentManualFacility(){return manualFacilityCombo==null?Facilities.Names[0]:Convert.ToString(manualFacilityCombo.SelectedItem);}
    string CurrentAutoFacility(){return autoFacilityCombo==null?Facilities.Names[0]:Convert.ToString(autoFacilityCombo.SelectedItem);}
    void EditFacilitySlots(string selected){
        if(!CanEdit())return;using(var d=new EditDialog("시설별 전체 슬롯 설정")){d.Height=600;var controls=new Dictionary<string,NumericUpDown>();foreach(var name in Facilities.Names){var n=Number(Math.Min(50,Facilities.Total(cfg,name)),50);controls[name]=n;d.Add(name,n);}d.Hint("게임에서 전체 슬롯 수를 제공하지 않습니다. 각 시설 레벨에 맞는 수를 설정하세요. 0칸은 해당 시설의 등록을 막습니다.");if(d.ShowDialog(this)!=DialogResult.OK)return;foreach(var pair in controls)cfg.FacilitySlots[pair.Key]=(int)pair.Value.Value;Save();RenderAll();}
    }
    void EditGoal(Goal old){
        if(!CanEdit())return;if(snap.Recipes.Count==0){Notice("게임 연결 후 가공법을 불러오세요.");return;}
        string facility=old==null?CurrentAutoFacility():Facilities.ForRecipe(cfg,old.Name);
        using(var d=new EditDialog(facility+" · 자동 가공")){
            d.Height=650;var recipe=Combo(480);var all=new CheckBox{Text="전체 가공법 보기 (기본 분류가 다를 때)",AutoSize=true};
            Action fill=()=>Fill(recipe,snap.Recipes.Select(x=>J.S(x,"DisplayName")).Where(x=>all.Checked||Facilities.ForRecipe(cfg,x)==facility).Distinct(),old==null?null:old.Name);
            fill();all.CheckedChanged+=(s,e)=>fill();
            var product=Combo(480);Fill(product,ItemNames(),old==null?Planner.Product(Convert.ToString(recipe.SelectedItem)):Planner.Product(old));
            recipe.SelectedIndexChanged+=(s,e)=>{string name=Planner.Product(Convert.ToString(recipe.SelectedItem));if(!product.Items.Contains(name))product.Items.Add(name);product.SelectedItem=name;};
            var mode=Combo(300);mode.Items.AddRange(new object[]{"목표 보유 수량","등록 횟수","무제한 가공"});mode.SelectedIndex=old==null?0:old.Mode=="runs"?1:old.Mode=="unlimited"?2:0;
            var count=Number(old==null?100:Math.Min(1000000,old.Target),1000000);mode.SelectedIndexChanged+=(s,e)=>count.Enabled=mode.SelectedIndex!=2;count.Enabled=mode.SelectedIndex!=2;
            var enabled=new CheckBox{Text="목표 사용",AutoSize=true,Checked=old==null||old.Enabled};
            d.Content.Controls.Add(all);d.Add("가공법",recipe);d.Add("완제품 · 보유 수량 기준",product);d.Add("반복 방식",mode);d.Add("목표 수량 또는 등록 횟수",count);d.Content.Controls.Add(enabled);
            d.Hint("시설 빈 슬롯에만 등록합니다. 등록 횟수는 성공한 작업 등록 횟수이며 앱 재시작 후에도 이어집니다. 시설/가공법 분류는 선택한 시설로 저장됩니다.");
            if(d.ShowDialog(this)!=DialogResult.OK||recipe.SelectedItem==null)return;string selected=Convert.ToString(recipe.SelectedItem);
            if(cfg.Goals.Any(g=>g!=old&&g.Name==selected)){Notice("이미 등록된 가공법입니다.");return;}
            var goal=old??new Goal();string newMode=mode.SelectedIndex==0?"stock":mode.SelectedIndex==1?"runs":"unlimited";
            if(goal.Name!=selected||goal.Mode!=newMode)goal.RunsDone=0;
            goal.Name=selected;goal.Product=Convert.ToString(product.SelectedItem);goal.Facility=facility;goal.Mode=newMode;goal.Target=(int)count.Value;goal.Enabled=enabled.Checked;cfg.RecipeFacilities[selected]=facility;if(old==null)cfg.Goals.Add(goal);Save();RenderAll();
        }
    }
    void AddManual(){
        if(!CanEdit())return;string recipe=Selected<string>(recipeGrid),facility=CurrentManualFacility();if(recipe==null){Notice("시설과 가공법을 선택하세요.");return;}
        int reserved=manualQueue.Where(x=>(x.Facility??Facilities.ForRecipe(cfg,x.Name))==facility).Sum(x=>x.Target);
        int free=Facilities.ManualAvailable(snap,cfg,facility,reserved);if(free==0){Notice("완료품 수령 후에도 예약 가능한 슬롯이 없거나 모두 예약되어 있습니다. 진행 작업이나 예약 목록을 확인하세요.");return;}
        using(var d=new EditDialog(facility+" · 1회성 가공 등록")){d.Height=340;var count=Number(free,free);count.Minimum=1;d.Add(recipe+" · 수령 후 예약 가능한 슬롯 "+free+"칸",count);d.Hint("완료된 작업은 슬롯 계산에서 제외합니다. 실행 시 완료품을 먼저 수령한 뒤 실제 빈 슬롯에 한 번만 등록합니다. 자동 재등록과 하위 재료 보충은 하지 않습니다.");if(d.ShowDialog(this)!=DialogResult.OK)return;cfg.RecipeFacilities[recipe]=facility;manualQueue.Add(new Goal{Name=recipe,Facility=facility,Target=(int)count.Value});Save();RenderManual();RenderManualInfo();}
    }
    void RenderManualInfo(){if(manualInfoLabel==null)return;string f=CurrentManualFacility();int reserved=manualQueue.Where(x=>(x.Facility??Facilities.ForRecipe(cfg,x.Name))==f).Sum(x=>x.Target);manualInfoLabel.Text="전체 "+Facilities.Total(cfg,f)+" · 진행·대기 "+snap.Works.Count(x=>J.S(x,"FacilityName")==f&&!J.B(x,"IsCompleted"))+" · 수령 후 예약 가능 "+Facilities.ManualAvailable(snap,cfg,f,reserved);}
    void RenderFacilities(){
        foreach(var w in snap.Works){string name=J.S(w,"FacilityName");if(name!=""&&!facilityNames.Contains(name))facilityNames.Add(name);}
        while(facilityGrid.Rows.Count<facilityNames.Count){int i=facilityGrid.Rows.Count;facilityGrid.Rows.Add(facilityNames[i],Facilities.Total(cfg,facilityNames[i]),"—","—","—","—","—","조회 대기");facilityGrid.Rows[i].Tag=facilityNames[i];}
        double elapsed=Math.Max(0,(DateTime.UtcNow-snap.At).TotalSeconds);
        for(int i=0;i<facilityNames.Count;i++){
            string name=facilityNames[i];var group=snap.Works.Where(x=>J.S(x,"FacilityName")==name).ToList();var active=group.Where(x=>!J.B(x,"IsCompleted")).ToList();
            var timing=FacilityTiming.Calculate(group,elapsed);
            string next=active.Count==0?"—":timing.NextSeconds.HasValue?Duration(timing.NextSeconds.Value):"시간 미제공";
            string last=active.Count==0?"—":timing.FinalSeconds.HasValue?Duration(timing.FinalSeconds.Value):"시간 미제공";
            object[] values={name,Facilities.Total(cfg,name),snap.At==DateTime.MinValue?"—":Facilities.Free(snap,cfg,name).ToString(),snap.At==DateTime.MinValue?"—":group.Count(x=>J.B(x,"IsCompleted")).ToString(),snap.At==DateTime.MinValue?"—":active.Count.ToString(),next,last,snap.At==DateTime.MinValue?"조회 대기":group.Count==0?"작업 없음":String.Join(", ",group.GroupBy(x=>J.S(x,"DisplayName")).Select(x=>x.Key+" ×"+x.Count()))};
            for(int c=0;c<values.Length;c++)if(!Object.Equals(facilityGrid.Rows[i].Cells[c].Value,values[c]))facilityGrid.Rows[i].Cells[c].Value=values[c];
            facilityGrid.Rows[i].DefaultCellStyle.ForeColor=group.Count>Facilities.Total(cfg,name)?Color.Firebrick:Ink;
        }
    }
    void RenderGoals(){if(goalGrid==null)return;var selected=Selected<Goal>(goalGrid);goalGrid.Rows.Clear();foreach(var g in cfg.Goals.Where(x=>Facilities.ForRecipe(cfg,x.Name)==CurrentAutoFacility())){
        string product=Planner.Product(g);var recipe=snap.Recipes.FirstOrDefault(x=>J.S(x,"DisplayName")==g.Name);int pending=snap.Works.Count(x=>J.S(x,"DisplayName")==product||J.S(x,"DisplayName")==g.Name);
        bool met=g.Mode=="runs"?g.RunsDone>=g.Target:g.Mode=="stock"&&snap.Count(product,cfg.CountStorage)>=g.Target;
        string state=!g.Enabled?"OFF":met?"목표 충족":Facilities.Free(snap,cfg,Facilities.ForRecipe(cfg,g.Name))==0?"빈 슬롯 대기":RecipeBook.Find(cfg,g.Name)==null?"배합표 필요":Planner.BatchRuns(snap,cfg,g)>0?Planner.BatchRuns(snap,cfg,g)+"회분 재료 준비 / 등록":Reason(recipe);
        int i=goalGrid.Rows.Add(g.Enabled?"ON":"OFF",g.Name+(g.Name!=product?" → "+product:""),g.Mode=="unlimited"?"무제한":g.Mode=="runs"?"등록 횟수":"목표 보유",g.Mode=="unlimited"?"∞":g.Target.ToString(),g.Mode=="runs"?g.RunsDone:snap.Count(product,cfg.CountStorage),pending,state);goalGrid.Rows[i].Tag=g;if(g==selected)goalGrid.Rows[i].Selected=true;
    }}
    void RenderRecipes(){if(recipeGrid==null)return;string selected=Selected<string>(recipeGrid);recipeGrid.Rows.Clear();foreach(var r in snap.Recipes.GroupBy(x=>J.S(x,"DisplayName")).Select(g=>g.OrderByDescending(x=>J.B(x,"Alterable")).First())){string n=J.S(r,"DisplayName");if((manualShowAll==null||!manualShowAll.Checked)&&Facilities.ForRecipe(cfg,n)!=CurrentManualFacility())continue;int i=recipeGrid.Rows.Add(n,J.B(r,"Alterable")?"가능":"대기",J.N(r,"ProducedPerWork"),Reason(r));recipeGrid.Rows[i].Tag=n;if(n==selected)recipeGrid.Rows[i].Selected=true;}}
    void RenderManual(){if(manualGrid==null)return;manualGrid.Rows.Clear();foreach(var r in manualQueue){int i=manualGrid.Rows.Add(r.Facility??Facilities.ForRecipe(cfg,r.Name),r.Name,r.Target);manualGrid.Rows[i].Tag=r;}RenderManualInfo();}
}
}
