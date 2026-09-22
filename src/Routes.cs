using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
namespace MabiRemote {
public class RoutePoint { public string Area{get;set;} public double X{get;set;} public double Y{get;set;} }
public class RoutePreset {
    public string Id{get;set;} public string Name{get;set;} public string Material{get;set;}
    public string DeviationMode{get;set;} public double Tolerance{get;set;}
    public string Skill{get;set;} public int MinLevel{get;set;} public int MaxLevel{get;set;}
    public List<RoutePoint> Points{get;set;}
    public RoutePreset(){DeviationMode="corridor";Tolerance=10;Id=Guid.NewGuid().ToString("N");Name="새 루트";Material="";Skill="";MinLevel=1;MaxLevel=100;Points=new List<RoutePoint>();}
}
public class RouteBundle {public int Version{get;set;} public List<RoutePreset> Routes{get;set;} }
public class RouteFile {public int Version{get;set;} public RoutePreset Route{get;set;} public RouteFile(){Version=1;} }
public static class RouteSharing {
    public static List<RoutePreset> Bundled(){var bundle=J.Serializer().Deserialize<RouteBundle>(Program.ReadEmbedded("routes.json"));if(bundle==null||bundle.Version!=1||bundle.Routes==null)throw new InvalidDataException("기본 프리셋 형식 오류");foreach(var r in bundle.Routes)Validate(r);return bundle.Routes;}
    const string Prefix="ERIN-ROUTE-1:";
    public static void Validate(RoutePreset r){
        if(r==null||String.IsNullOrWhiteSpace(r.Name)||r.Name.Length>100||String.IsNullOrWhiteSpace(r.Material)||r.Material.Length>100||r.Skill==null||r.Skill.Length>100||r.MinLevel<1||r.MaxLevel<r.MinLevel||r.MaxLevel>999)throw new InvalidDataException("루트 이름·재료·레벨 구간을 확인하세요.");
        if((r.DeviationMode!="corridor"&&r.DeviationMode!="radius")||Double.IsNaN(r.Tolerance)||Double.IsInfinity(r.Tolerance)||r.Tolerance<1||r.Tolerance>10000)throw new InvalidDataException("경로 이탈 방식·허용 거리를 확인하세요.");
        if(r.Points==null||r.Points.Count>200)throw new InvalidDataException("루트는 최대 200개 지점을 지원합니다.");
        foreach(var p in r.Points)if(p==null||String.IsNullOrWhiteSpace(p.Area)||p.Area.Length>150||Double.IsNaN(p.X)||Double.IsInfinity(p.X)||Double.IsNaN(p.Y)||Double.IsInfinity(p.Y)||Math.Abs(p.X)>10000000||Math.Abs(p.Y)>10000000)throw new InvalidDataException("지점의 지역·좌표가 올바르지 않습니다.");
    }
    public static string Export(RoutePreset route){Validate(route);return J.Json(new RouteFile{Route=route});}
    public static string Code(RoutePreset route){return Prefix+Convert.ToBase64String(Encoding.UTF8.GetBytes(Export(route)));}
    public static RoutePreset Import(string text){
        if(text==null||text.Length>100000)throw new InvalidDataException("루트 데이터가 너무 큽니다.");text=text.Trim().TrimStart('\uFEFF');
        if(text.StartsWith(Prefix))text=new UTF8Encoding(false,true).GetString(Convert.FromBase64String(text.Substring(Prefix.Length)));
        var file=J.Serializer().Deserialize<RouteFile>(text);if(file==null||file.Version!=1)throw new InvalidDataException("지원하지 않는 루트 파일 버전입니다.");
        Validate(file.Route);file.Route.Id=Guid.NewGuid().ToString("N");return file.Route;
    }
    static double Distance(double x,double y,RoutePoint a,RoutePoint b){double dx=b.X-a.X,dy=b.Y-a.Y,length=dx*dx+dy*dy;double t=length==0?0:Math.Max(0,Math.Min(1,((x-a.X)*dx+(y-a.Y)*dy)/length));double px=x-a.X-t*dx,py=y-a.Y-t*dy;return Math.Sqrt(px*px+py*py);}
    public static bool? IsOutside(RoutePreset route,string area,double x,double y){
        if(route==null||route.Points.Count==0||String.IsNullOrEmpty(area)||Double.IsNaN(x)||Double.IsNaN(y)||Double.IsInfinity(x)||Double.IsInfinity(y))return null;
        var start=route.Points[0];if(route.DeviationMode=="radius")return area!=start.Area||Distance(x,y,start,start)>route.Tolerance;
        double distance=Double.PositiveInfinity;
        for(int i=0;i<route.Points.Count;i++){var point=route.Points[i];if(point.Area!=area)continue;distance=Math.Min(distance,Distance(x,y,point,point));if(i+1<route.Points.Count&&route.Points[i+1].Area==area)distance=Math.Min(distance,Distance(x,y,point,route.Points[i+1]));}
        return distance>route.Tolerance;
    }
    public static RoutePreset Resolve(Settings cfg,string material){string id;return cfg.MaterialRoutes.TryGetValue(material,out id)?cfg.Routes.FirstOrDefault(x=>x.Id==id&&x.Material==material):null;}
    public static IEnumerable<RoutePreset> ForLevel(Settings cfg,string skill,int level){return cfg.Routes.Where(x=>x.Skill==skill&&x.MinLevel<=level&&x.MaxLevel>=level);}
}
public static class GatherResult {
    public static string Describe(string item,object body){string result=J.S(body,"result");string count=J.Get(body,"gained")==null?"":" · 획득 "+J.N(body,"gained").ToString("0")+"개";
        switch(result){case "completed":return "채집 완료 · "+item+count;case "stopped":case "stopped_by_user":return "채집 중단 · "+item+count;case "started":return "자동 낚시 진행 중 · "+item;default:return "채집 응답 확인 필요 · "+item;}
    }
}
public partial class MainForm {
    Label routeGatherStatus,inventoryGatherStatus;string gatherStatus="채집 대기";
    void SetGatherStatus(string text){gatherStatus=text;if(routeGatherStatus!=null)routeGatherStatus.Text=text;if(inventoryGatherStatus!=null)inventoryGatherStatus.Text=text;}
    DataGridView routeGrid;ComboBox routeSkillFilter;NumericUpDown routeLevelFilter;
    void BuildRoutes(){
        routeGrid=Grid("이름","채집 재료","생활스킬 / 레벨 구간","지점 수","이탈 기준","선택 여부");
        float[] rWidths={140,100,140,60,100,80};for(int i=0;i<routeGrid.Columns.Count;i++)routeGrid.Columns[i].FillWeight=rWidths[i];
        var bar=Bar();bar.Controls.Add(Button("기본 프리셋 불러오기",()=>{if(!CanEdit())return;var presets=RouteSharing.Bundled();foreach(var preset in presets)if(!cfg.Routes.Any(x=>x.Id==preset.Id))cfg.Routes.Add(preset);Save();RenderRoutes();Notice(presets.Count==0?"제공된 기본 루트가 없습니다.":"기본 프리셋을 불러왔습니다.");}));bar.Controls.Add(Button("새 루트",()=>EditRoute(null)));bar.Controls.Add(Button("선택 수정",()=>EditRoute(Selected<RoutePreset>(routeGrid))));
        bar.Controls.Add(Button("선택 삭제",()=>{if(!CanEdit())return;var r=Selected<RoutePreset>(routeGrid);if(r==null)return;cfg.Routes.Remove(r);foreach(var key in cfg.MaterialRoutes.Where(x=>x.Value==r.Id).Select(x=>x.Key).ToList())cfg.MaterialRoutes.Remove(key);Save();RenderRoutes();}));
        bar.Controls.Add(Button("재료 기본 루트로 선택",()=>{if(!CanEdit())return;var r=Selected<RoutePreset>(routeGrid);if(r==null)return;cfg.MaterialRoutes[r.Material]=r.Id;Save();RenderRoutes();}));
        bar.Controls.Add(Button("재료 선택 해제",()=>{if(!CanEdit())return;var r=Selected<RoutePreset>(routeGrid);if(r==null)return;cfg.MaterialRoutes.Remove(r.Material);Save();RenderRoutes();}));
        bar.Controls.Add(Button("현재 위치로 이탈 확인",()=>{var route=Selected<RoutePreset>(routeGrid);if(route==null)return;if(!connected||snap.At==DateTime.MinValue){Notice("게임 위치 정보가 필요합니다.");return;}var pos=J.Get(snap.Environment,"WorldPosition");var outside=RouteSharing.IsOutside(route,J.S(snap.Environment,"GameSpaceDisplayName"),J.N(pos,"X"),J.N(pos,"Y"));Notice(!outside.HasValue?"시작점과 허용 경로를 먼저 등록하세요.":(outside.Value?"저장 경로 이탈":"저장 경로 범위 안")+" · "+(int)(DateTime.UtcNow-snap.At).TotalSeconds+"초 전 위치 기준");}));bar.Controls.Add(Button("파일 가져오기",ImportRouteFile));bar.Controls.Add(Button("파일 내보내기",ExportRouteFile));
        bar.Controls.Add(Button("공유 코드 가져오기",ImportRouteCode));bar.Controls.Add(Button("공유 코드 복사",()=>{try{var r=Selected<RoutePreset>(routeGrid);if(r!=null){Clipboard.SetText(RouteSharing.Code(r));Notice("루트 공유 코드를 복사했습니다.");}}catch(Exception ex){Notice(UserError(ex));}}));
        routeSkillFilter=Combo(120);routeSkillFilter.Items.AddRange(new object[]{"전체","벌목","채광","채집","낚시"});routeSkillFilter.SelectedIndex=0;routeLevelFilter=Number(1,999);routeLevelFilter.Minimum=1;routeLevelFilter.Width=70;
        routeSkillFilter.SelectedIndexChanged+=(s,e)=>RenderRoutes();routeLevelFilter.ValueChanged+=(s,e)=>RenderRoutes();
        bar.Controls.Add(Label("스킬 / 레벨 필터",9));bar.Controls.Add(routeSkillFilter);bar.Controls.Add(routeLevelFilter);bar.SetFlowBreak(routeLevelFilter,true);routeGatherStatus=Label(gatherStatus,9);routeGatherStatus.MaximumSize=new Size(950,0);bar.Controls.Add(routeGatherStatus);
        Page("채집 루트","채집 경로를 설계하고 프리셋 파일 또는 공유 코드로 내보내고 불러올 수 있습니다.",routeGrid,bar);
        RenderRoutes();
    }
    void RenderRoutes(){if(routeGrid==null)return;var selected=Selected<RoutePreset>(routeGrid);string filter=Convert.ToString(routeSkillFilter.SelectedItem);routeGrid.Rows.Clear();
        foreach(var r in cfg.Routes){if(r.Skill!=""&&!routeSkillFilter.Items.Contains(r.Skill))routeSkillFilter.Items.Add(r.Skill);if(filter!="전체"&&(r.Skill!=filter||r.MinLevel>routeLevelFilter.Value||r.MaxLevel<routeLevelFilter.Value))continue;
            int i=routeGrid.Rows.Add(r.Name,r.Material,r.Skill==""?"재료용":r.Skill+" Lv."+r.MinLevel+"–"+r.MaxLevel,r.Points.Count,(r.DeviationMode=="radius"?"시작점 반경 ":"경로 거리 ")+r.Tolerance,RouteSharing.Resolve(cfg,r.Material)==r?"선택됨":"—");routeGrid.Rows[i].Tag=r;if(selected==r)routeGrid.Rows[i].Selected=true;
        }
    }
    void EditRoute(RoutePreset old){if(!CanEdit())return;
        var r=old==null?new RoutePreset():J.Serializer().Deserialize<RoutePreset>(J.Json(old));
        using(var d=new EditDialog("채집 루트 설정")){d.Height=740;
            var name=new TextBox{Text=r.Name,Width=480};var material=Combo(480);material.DropDownStyle=ComboBoxStyle.DropDown;Fill(material,ItemNames().Concat(snap.Gatherables.Select(x=>J.S(x,"DisplayName"))).Concat(cfg.Routes.Select(x=>x.Material)).Distinct(),r.Material);if(r.Material!="")material.Text=r.Material;
            var skill=Combo(260);skill.DropDownStyle=ComboBoxStyle.DropDown;skill.Items.AddRange(new object[]{"재료용 (레벨 구분 없음)","벌목","채광","채집","낚시"});skill.Text=r.Skill==""?"재료용 (레벨 구분 없음)":r.Skill;
            var levels=Bar();levels.Dock=DockStyle.None;var min=Number(r.MinLevel,999);min.Minimum=1;var max=Number(r.MaxLevel,999);max.Minimum=1;levels.Controls.Add(min);levels.Controls.Add(Label("~",9));levels.Controls.Add(max);
            var deviation=Combo(300);deviation.Items.AddRange(new object[]{"저장 경로에서 벗어남","시작점 반경에서 벗어남"});deviation.SelectedIndex=r.DeviationMode=="radius"?1:0;var tolerance=Number((decimal)r.Tolerance,10000);tolerance.Minimum=1;
            var points=Grid("순서","지역","X","Y");points.Dock=DockStyle.None;points.Size=new Size(490,160);
            Action render=()=>{points.Rows.Clear();for(int i=0;i<r.Points.Count;i++){var p=r.Points[i];points.Rows.Add(i+1,p.Area,p.X,p.Y);}};render();
            var buttons=Bar();buttons.Dock=DockStyle.None;
            buttons.Controls.Add(Button("현재 위치 추가",()=>{if(snap.At==DateTime.MinValue||!connected){Notice("게임 연결 후 위치를 추가하세요.");return;}var pos=J.Get(snap.Environment,"WorldPosition");r.Points.Add(new RoutePoint{Area=J.S(snap.Environment,"GameSpaceDisplayName"),X=J.N(pos,"X"),Y=J.N(pos,"Y")});render();}));
            buttons.Controls.Add(Button("지점 추가",()=>{if(EditRoutePoint(r,-1))render();}));buttons.Controls.Add(Button("지점 수정",()=>{if(points.SelectedRows.Count>0&&EditRoutePoint(r,points.SelectedRows[0].Index))render();}));
            buttons.Controls.Add(Button("지점 삭제",()=>{if(points.SelectedRows.Count>0){r.Points.RemoveAt(points.SelectedRows[0].Index);render();}}));
            buttons.Controls.Add(Button("위로 ↑",()=>{if(points.SelectedRows.Count>0){int i=points.SelectedRows[0].Index;if(i>0){var p=r.Points[i];r.Points.RemoveAt(i);r.Points.Insert(i-1,p);render();points.Rows[i-1].Selected=true;}}}));
            buttons.Controls.Add(Button("아래로 ↓",()=>{if(points.SelectedRows.Count>0){int i=points.SelectedRows[0].Index;if(i<r.Points.Count-1){var p=r.Points[i];r.Points.RemoveAt(i);r.Points.Insert(i+1,p);render();points.Rows[i+1].Selected=true;}}}));
            d.Add("루트 이름",name);d.Add("채집 재료",material);d.Add("생활스킬",skill);d.Add("사용 레벨 구간",levels);d.Add("이탈 판단 방식",deviation);d.Add("이탈 허용 거리",tolerance);d.Add("경로 지점 목록 (첫 지점: 시작·복귀 위치)",points);d.Content.Controls.Add(buttons);d.Hint("루트의 시작 위치와 경유 지점, 이탈 허용 거리를 설정합니다.");
            if(d.ShowDialog(this)!=DialogResult.OK)return;r.Name=name.Text.Trim();r.Material=material.Text.Trim();r.Skill=skill.Text.StartsWith("재료용")?"":skill.Text.Trim();r.MinLevel=(int)min.Value;r.MaxLevel=(int)max.Value;r.DeviationMode=deviation.SelectedIndex==1?"radius":"corridor";r.Tolerance=(double)tolerance.Value;
            try{RouteSharing.Validate(r);}catch(Exception ex){Notice(UserError(ex));return;}
            if(old!=null){int index=cfg.Routes.IndexOf(old);cfg.Routes[index]=r;if(old.Material!=r.Material&&RouteSharing.Resolve(cfg,old.Material)==null)cfg.MaterialRoutes.Remove(old.Material);}else cfg.Routes.Add(r);Save();RenderRoutes();
        }
    }
    bool EditRoutePoint(RoutePreset route,int index){using(var d=new EditDialog("루트 지점")){d.Height=440;var old=index<0?new RoutePoint{Area=""}:route.Points[index];var area=Combo(480);area.DropDownStyle=ComboBoxStyle.DropDown;Fill(area,cfg.Landmarks.Select(entry=>entry.Area).Concat(route.Points.Select(entry=>entry.Area)).Concat(new[]{J.S(snap.Environment,"GameSpaceDisplayName")}).Where(value=>value!="").Distinct(),old.Area);if(old.Area!="")area.Text=old.Area;
        var x=new NumericUpDown{Minimum=-10000000,Maximum=10000000,DecimalPlaces=2,Width=200,Value=(decimal)old.X};var y=new NumericUpDown{Minimum=-10000000,Maximum=10000000,DecimalPlaces=2,Width=200,Value=(decimal)old.Y};
        var bookmark=Combo(480);foreach(var l in cfg.Landmarks)bookmark.Items.Add(l.Name);bookmark.SelectedIndexChanged+=(s,e)=>{var l=cfg.Landmarks.First(v=>v.Name==Convert.ToString(bookmark.SelectedItem));area.Text=l.Area;x.Value=(decimal)l.X;y.Value=(decimal)l.Y;};
        d.Add("저장 장소에서 선택",bookmark);d.Add("지역",area);d.Add("X",x);d.Add("Y",y);if(d.ShowDialog(this)!=DialogResult.OK)return false;if(String.IsNullOrWhiteSpace(area.Text)){Notice("지역 이름을 입력하세요.");return false;}var point=new RoutePoint{Area=area.Text.Trim(),X=(double)x.Value,Y=(double)y.Value};if(index<0)route.Points.Add(point);else route.Points[index]=point;return true;
    }}
    void ImportRoute(string text){if(!CanEdit())return;var route=RouteSharing.Import(text);cfg.Routes.Add(route);Save();RenderRoutes();Notice("루트를 가져왔습니다.");}
    void ImportRouteFile(){if(!CanEdit())return;using(var d=new OpenFileDialog{Filter="루트 JSON|*.json"})if(d.ShowDialog(this)==DialogResult.OK)try{if(new FileInfo(d.FileName).Length>100000)throw new IOException("파일이 너무 큽니다.");ImportRoute(File.ReadAllText(d.FileName,Encoding.UTF8));}catch(Exception ex){Notice(UserError(ex));}}
    void ExportRouteFile(){var route=Selected<RoutePreset>(routeGrid);if(route==null)return;using(var d=new SaveFileDialog{Filter="루트 JSON|*.json",FileName="route.json"})if(d.ShowDialog(this)==DialogResult.OK)try{File.WriteAllText(d.FileName,RouteSharing.Export(route),new UTF8Encoding(false));}catch(Exception ex){Notice(UserError(ex));}}
    void ImportRouteCode(){if(!CanEdit())return;using(var d=new EditDialog("공유 코드 가져오기")){var text=new TextBox{Multiline=true,ScrollBars=ScrollBars.Vertical,Width=480,Height=200,MaxLength=100000};d.Add("공유 코드 또는 JSON",text);d.Content.Controls.Add(Button("클립보드 붙여넣기",()=>{try{text.Text=Clipboard.GetText();}catch(Exception ex){Notice(UserError(ex));}}));if(d.ShowDialog(this)==DialogResult.OK)try{ImportRoute(text.Text);}catch(Exception ex){Notice(UserError(ex));}}}
}
}
