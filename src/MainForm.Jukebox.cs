using System;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace MabiRemote {
public partial class MainForm {
    JukeboxPlayer jukebox;
    List<object> musicScores=new List<object>(),musicInstruments=new List<object>();
    DateTime musicCatalogAt=DateTime.MinValue;
    DataGridView scoreGrid,instrumentGrid,playlistGrid;
    Label musicStatus,musicLibraryStatus;
    TextBox scoreSearch;
    CheckBox playlistRepeat;
    Button musicStartButton,musicSelectedButton,musicStopButton;
    bool MusicActive{get{return jukebox!=null&&jukebox.Active;}}
    bool MusicSupported{get{return new[]{"get_activity","get_music_scores","get_instruments","change_instrument","play_music_score","stop_action"}.All(commands.ContainsKey);}}
    Control MusicPanel(string title,Control content){var p=new Panel{Dock=DockStyle.Fill,Margin=new Padding(0,0,6,0)};p.Controls.Add(content);p.Controls.Add(new Label{Text=title,Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(4,4,4,6),Font=new Font("맑은 고딕",9.5F,FontStyle.Bold)});return p;}
    void BuildJukebox(){
        scoreGrid=Grid("악보","보관 위치","잠금");scoreGrid.Columns[0].FillWeight=260;scoreGrid.Columns[1].FillWeight=90;scoreGrid.Columns[2].FillWeight=50;
        instrumentGrid=Grid("악기","보관 위치","장착");instrumentGrid.Columns[0].FillWeight=180;instrumentGrid.Columns[1].FillWeight=100;instrumentGrid.Columns[2].FillWeight=60;
        playlistGrid=Grid("순서","악보","악기","상태");playlistGrid.Columns[0].FillWeight=45;playlistGrid.Columns[1].FillWeight=240;playlistGrid.Columns[2].FillWeight=150;playlistGrid.Columns[3].FillWeight=85;
        foreach(var grid in new[]{scoreGrid,instrumentGrid,playlistGrid})foreach(DataGridViewColumn col in grid.Columns)col.SortMode=DataGridViewColumnSortMode.NotSortable;
        var library=new TabControl{Dock=DockStyle.Fill};var scoresPage=new TabPage("보유 악보");scoresPage.Controls.Add(scoreGrid);library.TabPages.Add(scoresPage);var instrumentsPage=new TabPage("보유 악기");instrumentsPage.Controls.Add(instrumentGrid);library.TabPages.Add(instrumentsPage);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,46));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,54));layout.Controls.Add(library,0,0);layout.Controls.Add(MusicPanel("플레이리스트",playlistGrid),1,0);
        var bar=Bar();bar.Controls.Add(Button("목록 새로고침",async()=>{if(!busy&&!MusicActive)await Poll(true);}));
        bar.Controls.Add(Label("악보 검색",9));scoreSearch=new TextBox{Width=160,Margin=new Padding(4,8,4,4),AccessibleName="악보 제목 검색"};scoreSearch.TextChanged+=(s,e)=>RenderMusicLibrary();bar.Controls.Add(scoreSearch);
        bar.Controls.Add(Button("선택 악보 추가",()=>AddSelectedScore()));bar.Controls.Add(Button("곡·악기 수정",()=>EditPlaylistEntry()));bar.Controls.Add(Button("선택 삭제",()=>{if(!CanEditMusic())return;var item=Selected<PlaylistEntry>(playlistGrid);if(item!=null){cfg.Playlist.Remove(item);Save();RenderPlaylist();}}));
        var up=Button("순서 ↑",()=>MovePlaylist(-1));var down=Button("순서 ↓",()=>MovePlaylist(1));bar.Controls.Add(up);bar.Controls.Add(down);bar.SetFlowBreak(down,true);
        musicStartButton=Button("처음부터 재생",()=>StartMusic(0),true);musicSelectedButton=Button("선택 곡부터 재생",()=>{var selected=Selected<PlaylistEntry>(playlistGrid);if(selected!=null)StartMusic(cfg.Playlist.IndexOf(selected));});musicStopButton=Button("재생 중지",RequestMusicStop);
        bar.Controls.Add(musicStartButton);bar.Controls.Add(musicSelectedButton);bar.Controls.Add(musicStopButton);
        playlistRepeat=new CheckBox{Text="목록 반복",Checked=cfg.PlaylistRepeat,AutoSize=true,Margin=new Padding(8,10,8,6)};playlistRepeat.CheckedChanged+=(s,e)=>{cfg.PlaylistRepeat=playlistRepeat.Checked;Save();};bar.Controls.Add(playlistRepeat);bar.SetFlowBreak(playlistRepeat,true);
        musicStatus=Label("재생 대기",9);musicStatus.MaximumSize=new Size(950,0);musicStatus.Margin=new Padding(4,4,12,4);bar.Controls.Add(musicStatus);
        musicLibraryStatus=Label("게임 연결 후 악보 및 악기 목록을 불러옵니다.",9);musicLibraryStatus.MaximumSize=new Size(950,0);musicLibraryStatus.Margin=new Padding(4);bar.Controls.Add(musicLibraryStatus);
        jukeboxPage=Page("주크박스","목록에 악보를 추가하고 악기를 지정하여 순서대로 자동 연주합니다.",layout,bar);
        RenderPlaylist();
    }
    async Task RefreshMusic(bool force){
        if(!MusicSupported){musicLibraryStatus.Text="현재 게임 버전에서 주크박스 기능을 지원하지 않습니다.";return;}
        if(!force&&DateTime.UtcNow-musicCatalogAt<TimeSpan.FromSeconds(60))return;
        try{
            var scores=J.Rows(await Read("get_music_scores"));var instruments=J.Rows(await Read("get_instruments"));
            musicScores=MusicLibrary.Sort(scores,"DisplayTitle");musicInstruments=MusicLibrary.Sort(instruments,"Name");musicCatalogAt=DateTime.UtcNow;
            musicLibraryStatus.Text="악보 "+scores.Count+"개 · 악기 "+instruments.Count+"개";RenderMusicLibrary();RenderPlaylist();
        }catch(Exception ex){musicLibraryStatus.Text="음악 목록 조회 실패 · "+UserError(ex);}
    }
    string CurrentInstrument(){string name=J.S(J.Get(snap.Activity,"Performance"),"InstrumentName");return name!=""?name:MusicLibrary.Equipped(musicInstruments);}
    void RenderMusicLibrary(){
        if(scoreGrid==null)return;
        var selected=Selected<object>(scoreGrid);string title=J.S(selected,"DisplayTitle"),location=J.S(selected,"Location");int first=scoreGrid.FirstDisplayedScrollingRowIndex;
        scoreGrid.Rows.Clear();foreach(var item in musicScores){if(!String.IsNullOrWhiteSpace(scoreSearch.Text)&&J.S(item,"DisplayTitle").IndexOf(scoreSearch.Text,StringComparison.CurrentCultureIgnoreCase)<0)continue;int i=scoreGrid.Rows.Add(J.S(item,"DisplayTitle"),MusicLibrary.Location(item),J.B(item,"IsLocked")?"잠김":"—");scoreGrid.Rows[i].Tag=item;if(J.S(item,"DisplayTitle")==title&&J.S(item,"Location")==location)scoreGrid.Rows[i].Selected=true;}
        if(first>=0&&first<scoreGrid.Rows.Count)scoreGrid.FirstDisplayedScrollingRowIndex=first;
        instrumentGrid.Rows.Clear();string equipped=CurrentInstrument();foreach(var item in musicInstruments){int i=instrumentGrid.Rows.Add(J.S(item,"Name"),MusicLibrary.Location(item),J.S(item,"Name")==equipped?"장착 중":"—");instrumentGrid.Rows[i].Tag=item;}
    }
    void RenderPlaylist(){
        if(playlistGrid==null)return;var selected=Selected<PlaylistEntry>(playlistGrid);int first=playlistGrid.FirstDisplayedScrollingRowIndex;playlistGrid.Rows.Clear();
        for(int i=0;i<cfg.Playlist.Count;i++){var entry=cfg.Playlist[i];string state=MusicActive&&jukebox.Index==i?"현재 곡":MusicActive&&jukebox.Index>i?"완료":"대기";if(!MusicActive&&musicCatalogAt!=DateTime.MinValue&&(!musicScores.Any(x=>J.S(x,"DisplayTitle")==entry.Title)||!musicInstruments.Any(x=>J.S(x,"Name")==entry.Instrument)))state="확인 필요";int row=playlistGrid.Rows.Add(i+1,entry.Title,entry.Instrument==""?"악기 선택 필요":entry.Instrument,state);playlistGrid.Rows[row].Tag=entry;if(entry==selected)playlistGrid.Rows[row].Selected=true;}
        if(first>=0&&first<playlistGrid.Rows.Count)playlistGrid.FirstDisplayedScrollingRowIndex=first;
    }
    bool CanEditMusic(){if(MusicActive||auto||busy||ownsFishing){Notice("진행 중인 작업을 중지한 후 목록을 수정하세요.");return false;}return true;}
    void AddSelectedScore(){
        if(!CanEditMusic())return;var score=Selected<object>(scoreGrid);if(score==null){Notice("추가할 악보를 선택하세요.");return;}
        var entry=new PlaylistEntry{Title=J.S(score,"DisplayTitle"),Instrument=CurrentInstrument()};cfg.Playlist.Add(entry);Save();RenderPlaylist();SelectPlaylist(entry);
        if(entry.Instrument=="")Notice("장착된 악기가 없습니다. '곡·악기 수정'에서 연주할 악기를 선택하세요.");
    }
    void SelectPlaylist(PlaylistEntry entry){playlistGrid.ClearSelection();foreach(DataGridViewRow row in playlistGrid.Rows)if(row.Tag==entry){row.Selected=true;playlistGrid.FirstDisplayedScrollingRowIndex=row.Index;break;}}
    void EditPlaylistEntry(){
        if(!CanEditMusic())return;var entry=Selected<PlaylistEntry>(playlistGrid);if(entry==null){Notice("수정할 곡을 선택하세요.");return;}
        using(var d=new EditDialog("곡 및 악기 설정")){d.Height=350;var score=Combo(480);Fill(score,musicScores.Select(x=>J.S(x,"DisplayTitle")).Concat(new[]{entry.Title}).Distinct(),entry.Title);var instrument=Combo(480);Fill(instrument,musicInstruments.Select(x=>J.S(x,"Name")).Concat(new[]{entry.Instrument}).Where(x=>x!="").Distinct(),entry.Instrument);if(entry.Instrument=="")instrument.SelectedIndex=-1;d.Add("악보",score);d.Add("연주 악기",instrument);d.Hint("이 곡을 연주할 때 자동으로 장착할 악기를 선택합니다.");if(d.ShowDialog(this)!=DialogResult.OK)return;if(score.SelectedItem==null||instrument.SelectedItem==null){Notice("악보와 악기를 모두 선택하세요.");return;}entry.Title=Convert.ToString(score.SelectedItem);entry.Instrument=Convert.ToString(instrument.SelectedItem);Save();RenderPlaylist();}
    }
    void MovePlaylist(int delta){if(!CanEditMusic())return;var entry=Selected<PlaylistEntry>(playlistGrid);if(entry==null)return;int i=cfg.Playlist.IndexOf(entry),next=i+delta;if(next<0||next>=cfg.Playlist.Count)return;cfg.Playlist.RemoveAt(i);cfg.Playlist.Insert(next,entry);Save();RenderPlaylist();SelectPlaylist(entry);}
    void StartMusic(int index){
        if(!connected){Notice("게임에 연결된 상태에서 실행하세요.");return;}
        if(busy||auto||ownsFishing||actionOwned||MusicActive){Notice("진행 중인 작업을 중지한 후 실행하세요.");return;}
        if(!MusicSupported){Notice("현재 게임 버전에서 주크박스를 지원하지 않습니다.");return;}
        if(snap.Busy(false)){Notice("현재 행동 또는 연주가 끝난 후 실행하세요.");return;}
        try{jukebox=new JukeboxPlayer(bridge,Log);jukebox.Start(cfg.Playlist,index,cfg.PlaylistRepeat);nextPoll=DateTime.MinValue;RenderPlaylist();UpdateLiveLabels();}catch(Exception ex){Notice(UserError(ex));}
    }
    void RequestMusicStop(){if(!MusicActive)return;jukebox.RequestStop();nextPoll=DateTime.MinValue;UpdateMusicStatus();}
    void UpdateMusicStatus(){
        if(musicStatus==null)return;string text=jukebox==null?"재생 대기":jukebox.Status;var perf=J.Get(snap.Activity,"Performance");
        if(connected&&J.B(perf,"IsPlaying")){double seconds=J.N(perf,"RemainingSeconds");text+=" · 남은 시간 "+(J.N(perf,"TotalDurationSeconds")>0?Duration(seconds):"정보 없음");}
        musicStatus.Text=text;bool canStart=connected&&!busy&&!auto&&!ownsFishing&&!actionOwned&&!MusicActive&&cfg.Playlist.Count>0;
        musicStartButton.Enabled=canStart;musicSelectedButton.Enabled=canStart;musicStopButton.Enabled=MusicActive;playlistRepeat.Enabled=!MusicActive;
    }
}
}
