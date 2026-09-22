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
    public const string CurrentVersion="2.1.1";
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
        var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(16)};
        panel.Controls.Add(Label("현재 버전 "+Updates.CurrentVersion,15));
        var check=new CheckBox{Text="프로그램 시작 시 새 버전 자동 확인",AutoSize=true,Checked=cfg.AutoCheckUpdates,Margin=new Padding(6,8,6,8)};
        check.CheckedChanged+=(s,e)=>{cfg.AutoCheckUpdates=check.Checked;Save();};panel.Controls.Add(check);
        var row=Bar();row.Dock=DockStyle.None;
        checkUpdateButton=Button("지금 확인",async()=>await CheckUpdate(false),true);
        row.Controls.Add(checkUpdateButton);
        row.Controls.Add(Button("릴리스 페이지 열기",()=>ProcessFolder(Updates.Repository+"/releases")));panel.Controls.Add(row);
        updateStatus=Label("업데이트를 확인하지 않았습니다.",10);updateStatus.MaximumSize=new Size(850,0);panel.Controls.Add(updateStatus);
        var help=Label("업데이트 방법:\n1. [릴리스 페이지 열기]를 눌러 최신 ErinRemote.exe 파일을 다운로드합니다.\n2. 실행 중인 리모컨을 종료하고 새 실행 파일로 교체합니다.\n※ 기존 가공 목표, 슬롯 및 알림 설정은 자동으로 유지됩니다.",9.5F>8?9:8);help.MaximumSize=new Size(850,0);panel.Controls.Add(help);
        updatePage=Page("업데이트","GitHub 최신 버전을 확인하고 릴리스 페이지로 이동합니다.",panel,null);
        Shown+=async(s,e)=>{if(!demo&&!preview&&cfg.AutoCheckUpdates)await CheckUpdate(true);};
    }
    async Task CheckUpdate(bool background){
        if(updating||closing)return;
        if(demo||preview){updateStatus.Text="데모에서는 GitHub에 접속하지 않습니다.";return;}
        updating=true;checkUpdateButton.Enabled=false;updateStatus.Text="새 버전 확인 중…";
        try {
            var release=await Updates.Check();if(IsDisposed||closing)return;
            PresentUpdate(release);
        }catch(Exception ex){if(!IsDisposed&&!closing){updateStatus.Text="업데이트 확인 실패: "+UserError(ex);Log(updateStatus.Text);}}
        finally{updating=false;if(!IsDisposed&&!closing){checkUpdateButton.Enabled=true;}}
    }
}
}
