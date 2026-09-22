using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MabiRemote {
public partial class MainForm : Form {
    readonly bool demo, preview; Settings cfg; IBridge bridge; Snapshot snap=new Snapshot();
    bool polling, pendingCliChange;
    bool connected, busy, auto, fishEnabled, ownsFishing, actionOwned, stopRequested, closing, hasCatalog;
    string connectionIssue="연결 확인 중";
    DateTime lastUiAt=DateTime.MinValue;
    int generation; string actionText="대기"; DateTime nextPoll=DateTime.MinValue, catalogAt=DateTime.MinValue;
    Dictionary<string,object> commands=new Dictionary<string,object>();
    Dictionary<string,DateTime> cooldown=new Dictionary<string,DateTime>();
    List<Goal> manualQueue=new List<Goal>();
    Label connectionLabel, placeLabel, activityLabel, weightLabel, wingsLabel, refreshLabel, planLabel, cliLabel;
    Button startButton; CheckBox fishCheck, storageCheck;
    NumericUpDown fullNumber; ComboBox fishCombo;
    TabControl tabs=new TabControl(); DataGridView facilityGrid,goalGrid,recipeGrid,manualGrid,itemGrid,stockGrid;
    Label activityWatchLabel, lastNotificationLabel;
    TextBox logBox, itemSearch; ComboBox itemLocation; NotifyIcon tray; Timer timer=new Timer();
    static readonly Color Ink=Color.FromArgb(28,45,50), Accent=Color.FromArgb(15,109,101), Canvas=Color.FromArgb(243,247,247);
    public MainForm(bool demoMode,bool previewMode){
        demo=demoMode;preview=previewMode;cfg=demo?new Settings():Storage.Load();
        if(demo){cfg.Playlist.Add(new PlaylistEntry{Title="악보: 첫 곡",Instrument="하프"});cfg.Playlist.Add(new PlaylistEntry{Title="악보: 두 번째 곡",Instrument="피아노"});cfg.Goals.Add(new Goal{Name="목재",Product="목재",Target=100});cfg.Goals.Add(new Goal{Name="옷감",Product="옷감",Mode="unlimited"});cfg.Stocks.Add(new StockGoal{Name="통나무",Target=300});cfg.FishName="연어";}
        bridge=demo?(IBridge)new DemoBridge():(IBridge)new GameBridge(cfg.CliPath);
        if(demo){var db=(DemoBridge)bridge;db.Works.Add(J.Obj("DisplayName","목재","FacilityName","목재 가공 시설","State","Completed","IsCompleted",true,"RemainingSeconds",0));db.Works.Add(J.Obj("DisplayName","목재","FacilityName","목재 가공 시설","State","InProgress","IsCompleted",false,"RemainingSeconds",750));db.Works.Add(J.Obj("DisplayName","옷감","FacilityName","옷감 가공 시설","State","InProgress","IsCompleted",false,"RemainingSeconds",1800));}
        Text="에린 리모컨 "+Updates.CurrentVersion+(demo?" · 데모":""); Width=1220;Height=840;MinimumSize=new Size(1020,720);StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("맑은 고딕",9F);BackColor=Canvas;ForeColor=Ink;AutoScaleMode=AutoScaleMode.Dpi;
        BuildUI(); LoadEmbeddedCatalog();
        tray=new NotifyIcon{Icon=SystemIcons.Information,Text="에린 리모컨",Visible=!preview};
        tray.DoubleClick+=(s,e)=>{Show();WindowState=FormWindowState.Normal;Activate();};
        timer.Interval=250;timer.Tick+=async(s,e)=>{if((DateTime.UtcNow-lastUiAt).TotalSeconds>=1){UpdateLiveLabels();lastUiAt=DateTime.UtcNow;}if(!busy&&!closing&&DateTime.UtcNow>=nextPoll)await Poll();};
        Shown+=async(s,e)=>{if(!preview){if(!demo&&!File.Exists(cfg.CliPath)){MessageBox.Show(this,"게임 연결에 필요한 MabinogiMobile_CLI.exe 파일을 찾지 못했습니다.\n\n확인을 누른 후 게임 설치 폴더의 MabinogiMobile_CLI.exe를 선택하세요.\n취소한 경우 우상단 설정 → 게임 CLI 선택에서 다시 지정할 수 있습니다.","게임 CLI 파일 선택",MessageBoxButtons.OK,MessageBoxIcon.Information);ChooseCli();}await Poll();timer.Start();}if(Storage.LastLoadWarning!="")Log(Storage.LastLoadWarning);};
        Load+=(s,e)=>ApplyDisplayScale(cfg.UiScale);FormClosed+=(s,e)=>{if(displayScale!=null)displayScale.Dispose();};
        FormClosing+=OnClosing;
    }
    Label Label(string text,int size){return new Label{Text=text,AutoSize=true,Font=new Font("맑은 고딕",size,size>=13?FontStyle.Bold:FontStyle.Regular),ForeColor=Ink,Margin=new Padding(6,6,6,4)};}
    Button Button(string text,Action click,bool accent=false){var b=new Button{Text=text,AutoSize=true,Height=34,MinimumSize=new Size(90,32),FlatStyle=FlatStyle.Flat,BackColor=accent?Accent:Color.White,ForeColor=accent?Color.White:Ink,Margin=new Padding(4),Padding=new Padding(8,2,8,2)};b.FlatAppearance.BorderColor=Color.FromArgb(200,215,215);b.Click+=(s,e)=>click();return b;}
    FlowLayoutPanel Bar(){return new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Padding=new Padding(4)};}
    ComboBox Combo(int width){var box=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=width,IntegralHeight=false,DropDownHeight=360,Margin=new Padding(4,6,4,4),FormattingEnabled=true};box.Format+=(s,e)=>{if(e.ListItem is string)e.Value=FriendlyText.DisplayName((string)e.ListItem);};return box;}
    NumericUpDown Number(decimal val,int max){return new NumericUpDown{Minimum=0,Maximum=max,Value=Math.Max(0,Math.Min(max,val)),Width=90,ThousandsSeparator=true,Margin=new Padding(4,6,4,4)};}
    DataGridView Grid(params string[] cols){var g=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,EnableHeadersVisualStyles=false,AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.None};g.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(228,237,236);g.ColumnHeadersDefaultCellStyle.ForeColor=Ink;g.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;g.ColumnHeadersHeight=32;g.ColumnHeadersDefaultCellStyle.WrapMode=DataGridViewTriState.False;g.DefaultCellStyle.WrapMode=DataGridViewTriState.False;g.DefaultCellStyle.Padding=new Padding(4,2,4,2);g.RowTemplate.Height=30;g.DefaultCellStyle.SelectionBackColor=Color.FromArgb(215,237,231);g.DefaultCellStyle.SelectionForeColor=Ink;g.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(248,250,250);g.CellFormatting+=(s,e)=>{if(e.Value is string){e.Value=FriendlyText.DisplayName((string)e.Value);e.FormattingApplied=true;}};foreach(var c in cols){int index=g.Columns.Add(c,c);g.Columns[index].MinimumWidth=40;}return g;}
    TabPage Page(string title,string hint,Control content,FlowLayoutPanel bar){var p=new TabPage(title){BackColor=Canvas,Padding=new Padding(10)};p.Controls.Add(content);if(bar!=null)p.Controls.Add(bar);var l=new Label{Text=hint,Dock=DockStyle.Top,AutoSize=true,MaximumSize=new Size(1100,0),Padding=new Padding(4,4,4,6),ForeColor=Color.FromArgb(76,99,100)};p.Controls.Add(l);p.Resize+=(s,e)=>l.MaximumSize=new Size(Math.Max(100,p.ClientSize.Width-20),0);tabs.TabPages.Add(p);return p;}
    void BuildUI(){
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(14)};
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Absolute,82));root.RowStyles.Add(new RowStyle(SizeType.Absolute,50));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(root);
        root.Controls.Add(BuildHeader(),0,0);
        var cards=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1};for(int i=0;i<4;i++)cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25F));
        placeLabel=Card(cards,0,"현재 위치","—");activityLabel=Card(cards,1,"현재 행동","—");weightLabel=Card(cards,2,"가방 무게","—");wingsLabel=Card(cards,3,"정령의 날개","—");root.Controls.Add(cards,0,1);
        var controls=Bar();controls.Dock=DockStyle.Fill;
        startButton=Button("자동화 시작",async()=>{if(auto)await StopOwned();else if(startRequested)Pause("시작 요청 취소");else StartAutomation();},true);
        controls.Controls.Add(startButton);root.Controls.Add(controls,0,2);
        tabs.Multiline=true;tabs.Dock=DockStyle.Fill;tabs.Font=new Font("맑은 고딕",9.5F);contentHost=new Panel{Dock=DockStyle.Fill};contentHost.Controls.Add(tabs);root.Controls.Add(contentHost,0,3);
        var footer=Bar();footer.Dock=DockStyle.Top;planLabel=Label("자동화 OFF",9);refreshLabel=Label("",9);footer.Controls.Add(planLabel);footer.Controls.Add(refreshLabel);root.Controls.Add(footer,0,4);
        BuildFacilities();BuildGoals();BuildManual();BuildInventory();BuildFishing();BuildSettings();BuildUpdates();BuildRoutes();BuildJukebox();SeparateUtilityPages();BuildUpdateBanner();
    }
    Label Card(TableLayoutPanel host,int col,string title,string value){var p=new Panel{Dock=DockStyle.Fill,BackColor=Color.White,Margin=new Padding(3)};var top=new Label{Text=title,AutoSize=true,Location=new Point(10,6),ForeColor=Color.FromArgb(83,111,112)};var val=new Label{Text=value,Location=new Point(10,30),Size=new Size(340,30),Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right,Font=new Font("맑은 고딕",11.5F,FontStyle.Bold),TextAlign=ContentAlignment.TopLeft};p.Controls.Add(top);p.Controls.Add(val);host.Controls.Add(p,col,0);return val;}
    void BuildFacilities(){
        facilityGrid=Grid("시설","전체","빈 슬롯","완료","진행·대기","다음 완료","최종 완료","가공 품목");
        float[] widths={140,60,60,55,75,95,95,420};for(int i=0;i<widths.Length;i++){facilityGrid.Columns[i].FillWeight=widths[i];facilityGrid.Columns[i].SortMode=DataGridViewColumnSortMode.NotSortable;}
        var bar=Bar();bar.Controls.Add(Button("자동 가공 목표",()=>{tabs.SelectedIndex=1;}));bar.Controls.Add(Button("선택 시설 수동 가공",()=>{var n=Selected<string>(facilityGrid);if(n!=null){manualFacilityCombo.SelectedItem=n;tabs.SelectedIndex=2;}}));bar.Controls.Add(Button("일괄 수령",async()=>await ManualCollect()));
        Page("가공 시설","우상단 [설정]에서 시설별 슬롯 수를 변경할 수 있습니다 (기본 7칸). 완료 후 미수령 작업도 슬롯을 차지합니다.",facilityGrid,bar);RenderFacilities();
    }
    void BuildGoals(){
        goalGrid=Grid("우선순위","활성화","시설","가공법 / 완제품","방식","목표","진행 / 보유","예약","재료 보충","상태");var bar=Bar();float[] goalWidths={45,52,115,160,75,55,75,60,65,165};for(int i=0;i<goalGrid.Columns.Count;i++){goalGrid.Columns[i].SortMode=DataGridViewColumnSortMode.NotSortable;goalGrid.Columns[i].FillWeight=goalWidths[i];}
        goalGrid.Columns[0].MinimumWidth=45;goalGrid.Columns[0].HeaderCell.Style.WrapMode=DataGridViewTriState.False;
        goalGrid.Columns[1].HeaderCell.ToolTipText="클릭하여 목표 활성화 / 비활성화";
        goalGrid.CellClick+=(sender,e)=>{if(e.RowIndex<0||e.ColumnIndex!=1||!CanEdit())return;var goal=goalGrid.Rows[e.RowIndex].Tag as Goal;if(goal==null)return;goal.Enabled=!goal.Enabled;Save();RenderGoals();};
        bar.Controls.Add(Button("목표 추가",()=>EditGoal(null)));bar.Controls.Add(Button("선택 수정",()=>EditGoal(Selected<Goal>(goalGrid))));bar.Controls.Add(Button("선택 삭제",()=>{if(CanEdit()){var g=Selected<Goal>(goalGrid);if(g!=null){cfg.Goals.Remove(g);Save();RenderGoals();}}}));bar.Controls.Add(Button("우선순위 ↑",()=>MoveGoal(-1)));bar.Controls.Add(Button("우선순위 ↓",()=>MoveGoal(1)));
        Page("자동 가공","목표 목록 위쪽부터 우선 처리되며, 완료품 보유량 또는 등록 횟수를 기준으로 가공합니다.",goalGrid,bar);
    }
    void BuildManual(){
        recipeGrid=Grid("가공법","상태","생산량","부족 재료 / 사유");manualGrid=Grid("시설","가공법","수량");
        float[] rWidths={150,60,60,200};for(int i=0;i<recipeGrid.Columns.Count;i++)recipeGrid.Columns[i].FillWeight=rWidths[i];
        float[] mWidths={130,170,70};for(int i=0;i<manualGrid.Columns.Count;i++)manualGrid.Columns[i].FillWeight=mWidths[i];
        var split=new SplitContainer{Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,SplitterDistance=230};split.Panel1.Controls.Add(recipeGrid);split.Panel2.Controls.Add(manualGrid);var bar=Bar();
        manualFacilityCombo=Combo(160);Fill(manualFacilityCombo,Facilities.Names,null);manualInfoLabel=Label("",9);manualFacilityCombo.SelectedIndexChanged+=(s,e)=>{RenderRecipes();RenderManualInfo();};manualShowAll=new CheckBox{Text="전체 가공법",AutoSize=true,Margin=new Padding(6,8,6,6)};manualShowAll.CheckedChanged+=(s,e)=>RenderRecipes();bar.Controls.Add(manualFacilityCombo);bar.Controls.Add(manualShowAll);bar.Controls.Add(manualInfoLabel);
        bar.Controls.Add(Button("선택 품목 예약",()=>AddManual()));bar.Controls.Add(Button("예약 제거",()=>{if(CanEdit()){var g=Selected<Goal>(manualGrid);if(g!=null)manualQueue.Remove(g);RenderManual();}}));bar.Controls.Add(Button("수령 후 일괄 등록",async()=>await ManualRegister(),true));bar.Controls.Add(Button("일괄 수령",async()=>await ManualCollect()));
        Page("수동 가공","완료된 품목을 수령한 뒤 빈 슬롯에 즉시 일괄 등록합니다.",split,bar);
    }
    void BuildInventory(){itemGrid=Grid("아이템","가방","계정 창고","캐릭터 창고","분류","목표","자동 채집");var iconColumn=new DataGridViewImageColumn{Name="아이콘",HeaderText="",ImageLayout=DataGridViewImageCellLayout.Zoom,FillWeight=40};iconColumn.DefaultCellStyle.NullValue=null;itemGrid.Columns.Add(iconColumn);
        float[] itemWidths={160,60,75,80,85,60,65,40};for(int i=0;i<itemGrid.Columns.Count;i++)itemGrid.Columns[i].FillWeight=itemWidths[i];
        stockGrid=Grid("아이템","목표","자동 채집");float[] sWidths={180,75,75};for(int i=0;i<stockGrid.Columns.Count;i++)stockGrid.Columns[i].FillWeight=sWidths[i];
        itemGrid.SelectionChanged+=(s,e)=>{};var split=new SplitContainer{Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,SplitterDistance=240};split.Panel1.Controls.Add(itemGrid);split.Panel2.Controls.Add(stockGrid);var bar=Bar();itemSearch=new TextBox{AccessibleName="아이템 이름 검색",Width=120,Margin=new Padding(4,6,4,4)};itemSearch.TextChanged+=(s,e)=>RenderItems();itemLocation=Combo(100);itemLocation.Items.AddRange(new object[]{"가방 보유","전체 보유"});itemLocation.SelectedIndex=0;itemLocation.SelectedIndexChanged+=(s,e)=>RenderItems();bar.Controls.Add(itemLocation);bar.Controls.Add(itemSearch);bar.Controls.Add(Button("선택 재고 목표",()=>EditStock(Selected<string>(itemGrid))));bar.Controls.Add(Button("재고 목표 추가",()=>EditStock(null)));bar.Controls.Add(Button("목표 수정",()=>{var g=Selected<StockGoal>(stockGrid);if(g!=null)EditStock(g.Name);}));bar.Controls.Add(Button("목표 삭제",()=>{if(CanEdit()){var g=Selected<StockGoal>(stockGrid);if(g!=null)cfg.Stocks.Remove(g);Save();RenderItems();}}));bar.Controls.Add(Button("아이콘 지정",()=>AssignIcon()));inventoryGatherStatus=Label(gatherStatus,9);inventoryGatherStatus.MaximumSize=new Size(900,0);bar.Controls.Add(inventoryGatherStatus);Page("재고 · 채집","목표 수량에 도달할 때까지 부족한 재료를 자동으로 채집합니다.",split,bar);}
    void BuildFishing(){var p=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(16)};p.Controls.Add(Label("자동 낚시",15));
        var row=Bar();row.Dock=DockStyle.None;row.Controls.Add(Label("어종 선택",10));fishCombo=Combo(260);row.Controls.Add(fishCombo);p.Controls.Add(row);
        fishCheck=new CheckBox{Text="빈 시간 자동 낚시 활성화",AutoSize=true,Margin=new Padding(6,8,6,6)};fishCheck.CheckedChanged+=async(s,e)=>{if(fishCheck.Checked&&fishCombo.SelectedItem==null){fishCheck.Checked=false;return;}fishEnabled=fishCheck.Checked;cfg.FishName=Convert.ToString(fishCombo.SelectedItem)??"";Save();if(!fishEnabled&&ownsFishing){await StopFishOnly();nextPoll=DateTime.MinValue;}};p.Controls.Add(fishCheck);fishCombo.SelectedIndexChanged+=(s,e)=>{if(auto||busy){return;}cfg.FishName=Convert.ToString(fishCombo.SelectedItem)??"";Save();};
        p.Controls.Add(Label("가공 및 채집 작업이 없을 때 선택한 어종을 자동으로 낚시합니다.",9.5F>8?9:8));p.Controls.Add(Label("가방 용량 한도에 도달하거나 새 가공·채집 작업이 생기면 낚시를 중지합니다.",9));
        Page("낚시","가공 및 채집 작업이 없을 때 지정한 어종을 자동으로 낚시합니다.",p,null);}
    Label activitySettingsHeading;
    void AddActivitySettings(FlowLayoutPanel opts){
        var sep=new Panel{Height=1,Width=850,BackColor=Color.FromArgb(220,230,230),Margin=new Padding(6,14,6,12)};
        opts.Controls.Add(sep);
        activitySettingsHeading=Label("알림 설정",12);opts.Controls.Add(activitySettingsHeading);
        var notifRow=Bar();notifRow.Dock=DockStyle.None;
        var dungeonCheck=new CheckBox{Text="던전 완료 시 알림",Checked=cfg.DungeonNotifications,AutoSize=true,Margin=new Padding(6,6,12,6)};dungeonCheck.CheckedChanged+=(s,e)=>{cfg.DungeonNotifications=dungeonCheck.Checked;Save();};notifRow.Controls.Add(dungeonCheck);
        var huntingCheck=new CheckBox{Text="사냥터 완료 시 알림",Checked=cfg.HuntingNotifications,AutoSize=true,Margin=new Padding(6,6,12,6)};huntingCheck.CheckedChanged+=(s,e)=>{cfg.HuntingNotifications=huntingCheck.Checked;Save();nextPoll=DateTime.MinValue;};notifRow.Controls.Add(huntingCheck);
        var bag=new CheckBox{Text="가방 100% 초과 시 알림",AutoSize=true,Checked=cfg.BagOverweightNotifications,Margin=new Padding(6,6,12,6)};bag.CheckedChanged+=(s,e)=>{cfg.BagOverweightNotifications=bag.Checked;if(!bag.Checked)bagWasOverweight=false;Save();};notifRow.Controls.Add(bag);
        opts.Controls.Add(notifRow);
        AddBlackLumpSettings(opts);
        var testBar=Bar();testBar.Dock=DockStyle.None;
        var testNotice=Button("알림 테스트",()=>Notify("알림 테스트","테스트 알림이 정상적으로 전송되었습니다."));testBar.Controls.Add(testNotice);
        activityDiagnosticButton=Button("활동 기록 시작",ToggleActivityDiagnostic);testBar.Controls.Add(activityDiagnosticButton);
        testBar.Controls.Add(Button("기록 폴더 열기",()=>{Directory.CreateDirectory(Storage.Root);ProcessFolder(Storage.Root);}));opts.Controls.Add(testBar);
        activityWatchLabel=Label("활동 감시: 대기 중",9);activityWatchLabel.MaximumSize=new Size(850,0);opts.Controls.Add(activityWatchLabel);
        lastNotificationLabel=Label("최근 알림: 없음",9);lastNotificationLabel.MaximumSize=new Size(850,0);opts.Controls.Add(lastNotificationLabel);
    }
    void BuildSettings(){
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,75));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,25));
        var opts=new FlowLayoutPanel{
            Dock=DockStyle.Fill,
            AutoScroll=true,
            FlowDirection=FlowDirection.TopDown,
            WrapContents=false,
            Padding=new Padding(16,12,16,16),
            BackColor=Color.White,
            BorderStyle=BorderStyle.FixedSingle,
            Margin=new Padding(2,2,2,6)
        };
        var scrollHint=new Label{
            Text="설정 목록 (마우스 휠이나 우측 스크롤바로 내려 알림 설정 확인 ↕)",
            AutoSize=true,
            ForeColor=Accent,
            Font=new Font("맑은 고딕",9F,FontStyle.Bold),
            Margin=new Padding(6,4,6,6)
        };
        opts.Controls.Add(scrollHint);
        opts.Controls.Add(Label("일반 설정",12));
        var row=Bar();row.Dock=DockStyle.None;row.Controls.Add(Label("가방 용량 한도 (%)",10));fullNumber=Number(cfg.FullPercent,100);fullNumber.Minimum=50;row.Controls.Add(fullNumber);opts.Controls.Add(row);
        AddDisplayScale(opts);
        storageCheck=new CheckBox{Text="목표 수량에 창고 재고 포함",Checked=cfg.CountStorage,AutoSize=true,Margin=new Padding(6,6,6,6)};opts.Controls.Add(storageCheck);
        var buttons=Bar();buttons.Dock=DockStyle.None;
        buttons.Controls.Add(Button("설정 적용",()=>{if(!CanEdit())return;cfg.FullPercent=(int)fullNumber.Value;cfg.CountStorage=storageCheck.Checked;Save();RenderGoals();Log("설정 저장 완료");}));
        buttons.Controls.Add(Button("시설별 슬롯 설정",()=>EditFacilitySlots(null)));buttons.Controls.Add(Button("게임 CLI 선택",ChooseCli));buttons.Controls.Add(Button("설정 폴더 열기",()=>{Directory.CreateDirectory(Storage.Root);ProcessFolder(Storage.Root);}));opts.Controls.Add(buttons);
        cliLabel=new Label{Text="CLI 경로: "+cfg.CliPath,AutoSize=true,Margin=new Padding(6,4,6,8)};opts.Controls.Add(cliLabel);
        AddActivitySettings(opts);
        layout.Controls.Add(opts,0,0);
        logBox=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Color.FromArgb(28,43,46),ForeColor=Color.FromArgb(219,235,230),Font=new Font("맑은 고딕",9)};layout.Controls.Add(logBox,0,1);
        Page("설정 · 기록","가방 용량 한도 및 알림 조건을 설정하고 프로그램 동작 기록을 확인합니다.",layout,null);
    }
    void ProcessFolder(string path){System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true});}
    void ApplyCliChange(){if(!pendingCliChange)return;pendingCliChange=false;var previous=bridge as IDisposable;if(previous!=null)previous.Dispose();bridge=new GameBridge(cfg.CliPath);hasCatalog=false;connected=false;connectionIssue="연결 확인 중";nextPoll=DateTime.MinValue;}
    void ChooseCli(){if(!CanEdit())return;using(var d=new OpenFileDialog{Filter="게임 CLI|MabinogiMobile_CLI.exe|실행 파일|*.exe",FileName="MabinogiMobile_CLI.exe"}){if(d.ShowDialog(this)!=DialogResult.OK)return;cfg.CliPath=d.FileName;pendingCliChange=!demo;if(!busy)ApplyCliChange();hasCatalog=false;connected=false;connectionIssue="연결 확인 중";cliLabel.Text="CLI 경로: "+cfg.CliPath;Save();nextPoll=DateTime.MinValue;}}
    T Selected<T>(DataGridView grid) where T:class {return grid.SelectedRows.Count==0?null:grid.SelectedRows[0].Tag as T;}
    bool CanEdit(){if(auto||actionOwned||stopping||(busy&&!polling)||MusicActive){Notice("진행 중인 작업을 중지한 후 변경하세요.","안내");return false;}return true;}
    string UserError(Exception ex){Log("오류 상세: "+ex.Message);return FriendlyText.Error(ex);}
    void Notice(string message,string title="안내"){if(closing||IsDisposed)return;planLabel.Text=title+" · "+message;planLabel.ForeColor=Color.Firebrick;Log(title+" · "+message);}
    void Save(){if(demo||preview)return;try{Storage.Save(cfg);}catch(Exception ex){Log("설정 저장 실패: "+ex.Message);}}
    void Log(string text){if(closing||IsDisposed)return;string line=DateTime.Now.ToString("HH:mm:ss")+"  "+text; if(logBox!=null){logBox.AppendText(line+Environment.NewLine);if(logBox.TextLength>80000)logBox.Text=logBox.Text.Substring(logBox.TextLength-50000);}if(!demo)try{Directory.CreateDirectory(Storage.Root);File.AppendAllText(Path.Combine(Storage.Root,"events-"+DateTime.Now.ToString("yyyyMMdd")+".log"),line+Environment.NewLine);}catch{}}
    void Notify(string title,string text){if(closing||IsDisposed)return;Log(title+" · "+text);if(lastNotificationLabel!=null)lastNotificationLabel.Text="최근 알림 ("+DateTime.Now.ToString("HH:mm:ss")+"): "+title+" · "+text;if(!preview){try{System.Media.SystemSounds.Asterisk.Play();tray.ShowBalloonTip(7000,title,text,ToolTipIcon.Info);}catch(Exception ex){Log("Windows 알림 표시 실패: "+ex.Message);}}}
}
}
