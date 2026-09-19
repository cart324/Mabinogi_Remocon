using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;

namespace MabiRemote {
public sealed class ReleaseInfo {
    public Version Version;
}
public static class Updates {
    public const string CurrentVersion="1.4.1";
    public const string Repository="https://github.com/cart324/Mabinogi_Remocon";
    public const string LatestApi="https://api.github.com/repos/cart324/Mabinogi_Remocon/releases/latest";
    public static ReleaseInfo ParseRelease(object data) {
        if(J.B(data,"draft")||J.B(data,"prerelease"))return null;
        string tag=J.S(data,"tag_name");Version version;
        if(!Version.TryParse(tag.TrimStart('v','V'),out version)||version.Build<0||version.Revision>0)
            throw new InvalidDataException("릴리스 버전 형식이 올바르지 않습니다.");
        version=new Version(version.Major,version.Minor,version.Build);
        if(version<=new Version(CurrentVersion))return null;
        return new ReleaseInfo{Version=version};
    }

    static HttpWebRequest Request(string url) {
        ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
        var request=(HttpWebRequest)WebRequest.Create(url);
        request.UserAgent="ErinRemote/"+CurrentVersion;
        request.Accept="application/vnd.github+json";
        request.Timeout=15000;request.ReadWriteTimeout=15000;
        return request;
    }
    public static Task<ReleaseInfo> Check() {return Task.Run(()=>{
        try {using(var response=Request(LatestApi).GetResponse())using(var stream=response.GetResponseStream())using(var reader=new StreamReader(stream)) {
            // Metadata is small; cap it so malformed responses cannot exhaust memory.
            char[] buffer=new char[4096];var json=new System.Text.StringBuilder();int count;
            while((count=reader.Read(buffer,0,buffer.Length))>0){json.Append(buffer,0,count);if(json.Length>1024*1024)throw new InvalidDataException("릴리스 정보가 너무 큽니다.");}
            return ParseRelease(J.Parse(json.ToString()));
        }}catch(WebException ex){var http=ex.Response as HttpWebResponse;if(http!=null){int code=(int)http.StatusCode;http.Close();if(code==404)return null;if(code==403||code==429)throw new IOException("GitHub 조회 한도에 도달했습니다. 잠시 후 다시 확인하세요.");}throw;}
    });}
}
public partial class MainForm {
    Label updateStatus;
    Button checkUpdateButton;
    TabPage updatePage;
    bool updating;
    void BuildUpdates(){
        var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};
        panel.Controls.Add(Label("현재 버전 "+Updates.CurrentVersion,16));
        var check=new CheckBox{Text="실행 시 새 버전 자동 확인",AutoSize=true,Checked=cfg.AutoCheckUpdates,Margin=new Padding(8)};
        check.CheckedChanged+=(s,e)=>{cfg.AutoCheckUpdates=check.Checked;Save();};panel.Controls.Add(check);
        var row=Bar();row.Dock=DockStyle.None;
        checkUpdateButton=Button("지금 확인",async()=>await CheckUpdate(false));
        row.Controls.Add(checkUpdateButton);
        row.Controls.Add(Button("릴리스 페이지 열기",()=>ProcessFolder(Updates.Repository+"/releases")));panel.Controls.Add(row);
        updateStatus=Label("업데이트를 확인하지 않았습니다.",10);updateStatus.MaximumSize=new Size(850,0);panel.Controls.Add(updateStatus);
        var help=Label("릴리스 페이지의 Assets에서 ErinRemote.exe를 받으세요.\n리모컨 작업을 중지하고 종료한 뒤 기존 EXE를 교체하면 됩니다.\n가공 목표·시설 슬롯·알림 등의 기존 설정은 유지됩니다.",10);help.MaximumSize=new Size(850,0);panel.Controls.Add(help);
        updatePage=Page("업데이트","GitHub Releases에서 새 버전을 확인합니다. 실행 파일은 릴리스 페이지에서 직접 다운로드하세요.",panel,null);
        Shown+=async(s,e)=>{if(!demo&&!preview&&cfg.AutoCheckUpdates)await CheckUpdate(true);};
    }
    async Task CheckUpdate(bool background){
        if(updating||closing)return;
        if(demo||preview){updateStatus.Text="데모에서는 GitHub에 접속하지 않습니다.";return;}
        updating=true;checkUpdateButton.Enabled=false;updateStatus.Text="GitHub에서 새 버전 확인 중…";
        try {
            var release=await Updates.Check();if(IsDisposed||closing)return;
            updatePage.Text=release==null?"업데이트":"업데이트 ●";
            updateStatus.Text=release==null?"현재 버전 "+Updates.CurrentVersion+" · 게시된 새 정식 버전이 없습니다.":"새 버전 "+release.Version+" · 릴리스 페이지에서 다운로드하세요.";
            if(background&&release!=null)tray.ShowBalloonTip(5000,"리모컨 업데이트",release.Version+" 버전이 있습니다. 업데이트 탭에서 릴리스 페이지를 여세요.",ToolTipIcon.Info);
        }catch(Exception ex){if(!IsDisposed&&!closing){updateStatus.Text="업데이트 확인 실패: "+UserError(ex);Log(updateStatus.Text);}}
        finally{updating=false;if(!IsDisposed&&!closing){checkUpdateButton.Enabled=true;}}
    }
}
}
