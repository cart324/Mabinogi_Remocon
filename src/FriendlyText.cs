using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using System.Text.RegularExpressions;
namespace MabiRemote {
public sealed class GameCommandException:Exception {
    public string UserMessage{get;private set;}
    public GameCommandException(string friendly,string details):base(details){UserMessage=friendly;}
}
public static class FriendlyText {
    public static string DisplayName(string value){return Regex.Replace(value??"",@"</?color(?:=[^>]*)?>", "",RegexOptions.IgnoreCase);}

    static readonly Dictionary<string,string> Errors=new Dictionary<string,string>{
        {"game_off","게임에 연결할 수 없습니다. 게임 실행 및 캐릭터 접속 상태를 확인하세요."},
        {"option_off","AI 커넥터가 꺼져 있습니다. 게임 설정에서 활성화하세요."},
        {"not_enough_currency","정령의 날개가 부족합니다."},
        {"cost_payment_failed","필요한 재화가 부족합니다."},
        {"not_enough_ingredient","가공 재료가 부족합니다."},
        {"ingredient_locked","필요한 재료가 잠겨 있습니다."},
        {"insufficient_transfer_cost","재료 이동 비용이 부족합니다."},
        {"insufficient_facility_level","시설 레벨이 부족합니다."},
        {"insufficient_living_skill_level","생활스킬 레벨이 부족합니다."},
        {"insufficient_decor_score","꾸미기 점수가 부족합니다."},
        {"overweight","가방 용량이 초과되었습니다. 아이템을 정리하세요."},
        {"tool_missing","채집 도구가 없습니다."},
        {"tool_broken","채집 도구의 내구도가 모두 소진되었습니다."},
        {"required_consumable_missing","채집에 필요한 소모품(빈 병 등)이 부족합니다."},
        {"no_route","채집 장소로 이동할 수 없습니다."},
        {"not_in_field","현재 위치에서는 실행할 수 없습니다."},
        {"blocked","게임 내 대화나 팝업창을 확인하세요."},
        {"requires_user_interaction","수동 조작이 필요한 작업입니다."},
        {"not_found","대상을 찾을 수 없습니다."},
        {"not_available","현재 조건에서는 실행할 수 없습니다."},
        {"facility_not_found","해당 가공 시설을 찾을 수 없습니다."},
        {"component_not_found","필요한 게임 정보를 찾을 수 없습니다."},
        {"no_altering","등록된 가공 작업이 없습니다."},
        {"no_completed_work","수령할 완료 가공물이 없습니다."},
        {"no_completed_work_at_facility","해당 시설에 수령할 완료 가공물이 없습니다."},
        {"not_completed_yet","아직 완료되지 않은 작업입니다."},
        {"timeout","응답 시간이 초과되었습니다."},
        {"canceled","작업이 취소되었습니다."},
        {"invalid_state","현재 상태에서는 실행할 수 없습니다."},
        {"invalid_body","작업 정보가 올바르지 않습니다."},
        {"invalid_count","요청 횟수가 허용 범위를 벗어났습니다."},
        {"unknown_command","현재 게임 버전에서 지원하지 않는 기능입니다."},
        {"unsupported_command","현재 게임 버전에서 지원하지 않는 기능입니다."},
        {"crafting_locked","제작 기능이 잠겨 있습니다."},
        {"not_available_on_combat","전투 중에는 실행할 수 없습니다."},
        {"not_available_on_dead","캐릭터가 쓰러진 상태에서는 실행할 수 없습니다."},
        {"not_available_on_riding","탑승 중에는 실행할 수 없습니다."},
        {"no_instrument","장착된 악기가 없습니다."},
        {"is_playing_instrument","연주 중에는 악기를 바꿀 수 없습니다."},
        {"invalid_target","해당 악기를 장착할 수 없습니다."},
        {"failed_unequip","기존 장비를 해제하지 못했습니다."},
        {"system_error","작업을 처리하지 못했습니다."},
        {"level_requirement","필요 레벨이 부족합니다."},
        {"rate_limited","요청이 너무 많습니다. 잠시 후 다시 시도하세요."}
    };
    public static string Code(string code){string text;return Errors.TryGetValue(code??"",out text)?text:"작업을 완료하지 못했습니다. 게임 화면을 확인하세요.";}
    static string ErrorCode(object data){string error=J.S(data,"error");if(error!="")return error;var body=J.Get(data,"body");return body==null?"":ErrorCode(body);}
    public static string Reply(object data,int exit){
        string code=ErrorCode(data);if(code=="")code=J.S(data,"reason");if(code=="")code=J.S(J.Get(data,"body"),"reason");
        if(code!="")return Code(code);
        if(exit==5)return "게임 연결이 끊겼습니다. 게임 실행 및 AI 커넥터 설정을 확인하세요.";
        if(exit==3)return "작업이 취소되었습니다.";
        if(exit!=0)return "게임 명령 실행에 실패했습니다.";
        return Code(J.S(data,"status"));
    }
    public static string Error(Exception ex){
        var game=ex as GameCommandException;if(game!=null)return game.UserMessage;
        if(ex is WebException)return "네트워크 연결에 실패했습니다. 잠시 후 다시 시도하세요.";
        if(ex is UnauthorizedAccessException)return "접근 권한이 없습니다.";
        if(ex is FileNotFoundException||ex is DirectoryNotFoundException)return "지정한 파일 또는 폴더를 찾을 수 없습니다.";
        if(ex is FormatException)return "데이터 형식이 올바르지 않습니다.";
        if(Regex.IsMatch(ex.Message??"",@"[가-힣]"))return ex.Message;
        if(ex is IOException)return "파일을 읽거나 저장할 수 없습니다.";
        return "작업을 처리하지 못했습니다. 상태를 확인하세요.";
    }
    public static string Dungeon(string state){switch(state){case "Entering":return "입장 중";case "InProgress":return "진행 중";case "Cleared":return "완료";case "NotInDungeon":return "던전 밖";default:return "확인 중";}}
    public static string Weather(string weather){switch(weather){case "Sunny":case "Clear":return "맑음";case "Cloudy":return "흐림";case "Rainy":case "Rain":return "비";case "Snowy":case "Snow":return "눈";case "Thunderstorm":return "뇌우";case "Foggy":return "안개";default:return "";}}
}
}
