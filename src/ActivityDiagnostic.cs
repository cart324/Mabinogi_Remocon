using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
namespace MabiRemote {
public partial class MainForm {
    string activityDiagnosticPath="";
    DateTime activityDiagnosticStarted;
    Button activityDiagnosticButton;
    void ToggleActivityDiagnostic(){
        if(activityDiagnosticPath!=""){EndActivityDiagnostic("사용자 종료");return;}
        try{
            Directory.CreateDirectory(Storage.Root);
            activityDiagnosticPath=Path.Combine(Storage.Root,"activity-diagnostic-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".jsonl");
            File.WriteAllText(activityDiagnosticPath,"",new UTF8Encoding(false));activityDiagnosticStarted=DateTime.UtcNow;
            activityDiagnosticButton.Text="활동 기록 종료";nextPoll=DateTime.MinValue;
            Log("활동 기록 파일: "+activityDiagnosticPath);Notice("활동 기록을 시작했습니다. 콘텐츠 완료 후 ‘활동 기록 종료’를 누르세요.","진단 기록");
        }catch(Exception ex){activityDiagnosticPath="";Notice("활동 기록 시작 실패: "+UserError(ex));}
    }
    void EndActivityDiagnostic(string reason){
        if(activityDiagnosticPath=="")return;string file=activityDiagnosticPath;activityDiagnosticPath="";activityDiagnosticButton.Text="활동 기록 시작";
        Log("활동 기록 파일: "+file);Notice(reason+" · ‘기록 폴더 열기’에서 "+Path.GetFileName(file)+"을 확인하세요.","진단 기록 종료");
    }
    void RecordActivityDiagnostic(Snapshot fresh){
        if(activityDiagnosticPath=="")return;
        try{
            var record=J.Obj("timeUtc",DateTime.UtcNow.ToString("o"),"environment",J.Obj("GameSpaceDisplayName",J.S(fresh.Environment,"GameSpaceDisplayName")),"activity",fresh.Activity,"quests",fresh.Quests,"questReadError",lastQuestError);
            File.AppendAllText(activityDiagnosticPath,J.Json(record)+Environment.NewLine,new UTF8Encoding(false));
            if(DateTime.UtcNow-activityDiagnosticStarted>=TimeSpan.FromMinutes(30))EndActivityDiagnostic("30분 기록 완료");
        }catch(Exception ex){string error=UserError(ex);EndActivityDiagnostic("기록 중지: "+error);}
    }
}
}
