using System;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
namespace MabiRemote {
public partial class MainForm {
    Panel contentHost,settingsView,updatesView;
    Button settingsHeaderButton,updatesHeaderButton;
    TableLayoutPanel updateBanner;
    Label updateBannerText;
    Control BuildHeader(){
        var heading=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=4,RowCount=1,Padding=new Padding(4,2,4,6)};
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title=Label("에린 리모컨",18);title.Anchor=AnchorStyles.Left;
        connectionLabel=Label(demo?"DEMO · 게임 조작 없음":"연결 확인 중",10);connectionLabel.AutoSize=false;connectionLabel.Dock=DockStyle.Fill;connectionLabel.TextAlign=ContentAlignment.MiddleLeft;connectionLabel.MinimumSize=new Size(180,34);
        settingsHeaderButton=Button("설정",()=>ShowUtility(settingsView));settingsHeaderButton.Anchor=AnchorStyles.Right;
        updatesHeaderButton=Button("업데이트",()=>ShowUtility(updatesView));updatesHeaderButton.Anchor=AnchorStyles.Right;
        heading.Controls.Add(title,0,0);heading.Controls.Add(connectionLabel,1,0);heading.Controls.Add(settingsHeaderButton,2,0);heading.Controls.Add(updatesHeaderButton,3,0);return heading;
    }
    void SeparateUtilityPages(){
        settingsView=MoveUtilityPage(tabs.TabPages.Cast<TabPage>().Single(x=>x.Text=="설정 · 기록"));
        updatesView=MoveUtilityPage(updatePage);updatePage=null;
    }
    Panel MoveUtilityPage(TabPage page){
        var panel=new Panel{Dock=DockStyle.Fill,BackColor=Canvas,Padding=page.Padding,Visible=false};
        var controls=page.Controls.Cast<Control>().ToArray();foreach(var control in controls){page.Controls.Remove(control);panel.Controls.Add(control);}
        var back=Bar();back.Controls.Add(Button("← 메인 화면으로",ShowTasks));panel.Controls.Add(back);
        tabs.TabPages.Remove(page);page.Dispose();contentHost.Controls.Add(panel);return panel;
    }
    void ShowUtility(Panel panel){if(panel==null)return;tabs.Visible=false;settingsView.Visible=panel==settingsView;updatesView.Visible=panel==updatesView;panel.BringToFront();}
    void ShowTasks(){settingsView.Visible=false;updatesView.Visible=false;tabs.Visible=true;tabs.BringToFront();}
    void BuildUpdateBanner(){
        updateBanner=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=3,RowCount=1,Padding=new Padding(16,6,16,6),BackColor=Color.FromArgb(215,237,231),Visible=false};
        updateBanner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));updateBanner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));updateBanner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        updateBannerText=Label("새 버전이 있습니다.",10);updateBannerText.Dock=DockStyle.Fill;updateBannerText.TextAlign=ContentAlignment.MiddleLeft;
        var open=Button("업데이트 확인",()=>ShowUtility(updatesView),true);open.Anchor=AnchorStyles.Right;
        var hide=Button("닫기",()=>updateBanner.Visible=false);hide.Anchor=AnchorStyles.Right;
        updateBanner.Controls.Add(updateBannerText,0,0);updateBanner.Controls.Add(open,1,0);updateBanner.Controls.Add(hide,2,0);Controls.Add(updateBanner);
    }
    void PresentUpdate(ReleaseInfo release){
        updatesHeaderButton.Text=release==null?"업데이트":"업데이트 ●";
        updateStatus.Text=release==null?"현재 최신 버전입니다 ("+Updates.CurrentVersion+").":"새 버전 "+release.Version+"이 출시되었습니다. 릴리스 페이지에서 다운로드하세요.";
        updateBannerText.Text=release==null?"":"새 버전 "+release.Version+"이 출시되었습니다. 업데이트 화면에서 확인하세요.";
        updateBanner.Visible=release!=null;
    }
    void CaptureNavigation(string directory){
        Action<string> capture=name=>{Application.DoEvents();using(var bmp=new Bitmap(Width,Height)){DrawToBitmap(bmp,new Rectangle(0,0,Width,Height));bmp.Save(System.IO.Path.Combine(directory,name+".png"));}};
        ShowUtility(settingsView);capture("settings");var activityOptions=activityWatchLabel.Parent as ScrollableControl;if(activityOptions!=null){activityOptions.ScrollControlIntoView(lastNotificationLabel);capture("settings-notifications");activityOptions.AutoScrollPosition=Point.Empty;}ShowUtility(updatesView);capture("updates");ShowTasks();
        var version=new Version(Updates.CurrentVersion);PresentUpdate(new ReleaseInfo{Version=new Version(version.Major,version.Minor,version.Build+1)});capture("update-banner");PresentUpdate(null);
    }
}
}
