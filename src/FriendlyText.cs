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
        {"game_off","게임에 연결할 수 없습니다. 게임 실행과 캐릭터 접속 상태를 확인하세요."},
        {"option_off","AI 커넥터가 꺼져 있습니다. 게임 설정에서 켜주세요."},
        {"not_enough_currency","정령의 날개가 부족합니다. 보유 수량을 확인하세요."},
        {"cost_payment_failed","작업에 필요한 재화를 사용할 수 없습니다. 보유 수량과 게임 상태를 확인하세요."},
        {"not_enough_ingredient","가공 재료가 부족합니다. 필요한 재료를 준비하세요."},
        {"ingredient_locked","필요한 재료가 잠겨 있습니다. 게임에서 잠금을 해제하세요."},
        {"insufficient_transfer_cost","재료를 가져오는 데 필요한 비용이 부족합니다."},
        {"insufficient_facility_level","시설 레벨이 부족합니다."},
        {"insufficient_living_skill_level","생활스킬 레벨이 부족합니다."},
        {"insufficient_decor_score","제작에 필요한 꾸미기 점수가 부족합니다."},
        {"overweight","가방이 무겁습니다. 아이템을 정리한 뒤 다시 시작하세요."},
        {"tool_missing","필요한 채집 도구가 없습니다. 도구를 준비하세요."},
        {"tool_broken","채집 도구의 내구도가 소진되었습니다. 수리하거나 교체하세요."},
        {"required_consumable_missing","빈 병 등 채집에 필요한 소모품이 부족합니다."},
        {"no_route","채집 장소로 가는 길을 찾지 못했습니다. 다른 위치에서 다시 시도하세요."},
        {"not_in_field","현재 장소에서는 실행할 수 없습니다. 일반 필드로 이동하세요."},
        {"blocked","게임에서 직접 확인할 화면이나 상태가 있습니다. 대화·선택창 등 게임 화면을 확인하세요."},
        {"requires_user_interaction","이 작업은 게임에서 직접 조작해야 합니다."},
        {"not_found","요청한 품목이나 대상을 찾지 못했습니다. 목록을 새로고침하세요."},
        {"not_available","현재 조건에서는 이 작업을 실행할 수 없습니다. 게임 상태를 확인하세요."},
        {"facility_not_found","이용할 수 있는 가공·제작 시설을 찾지 못했습니다."},
        {"component_not_found","작업에 필요한 게임 정보를 찾지 못했습니다. 게임 상태를 확인하세요."},
        {"no_altering","등록된 가공 작업이 없습니다."},
        {"no_completed_work","수령할 수 있는 완료 가공물이 없습니다."},
        {"no_completed_work_at_facility","해당 시설에는 수령할 수 있는 완료 가공물이 없습니다."},
        {"not_completed_yet","선택한 가공 작업이 아직 완료되지 않았습니다."},
        {"timeout","게임의 완료 응답을 제시간에 받지 못했습니다. 실제 작업 상태를 확인하세요."},
        {"canceled","작업이 취소되거나 다른 명령으로 바뀌었습니다."},
        {"invalid_state","지금은 이 동작을 실행할 수 없습니다. 중지 가능한 작업이 있는지 확인하세요."},
        {"invalid_body","게임에 전달할 작업 정보가 올바르지 않습니다. 목록을 갱신한 뒤 다시 시도하세요."},
        {"invalid_count","요청한 작업 횟수가 허용 범위를 벗어났습니다."},
        {"unknown_command","현재 게임 버전에서 지원하지 않는 기능입니다."},
        {"unsupported_command","현재 게임 버전에서 지원하지 않는 기능입니다."},
        {"crafting_locked","제작 기능이 아직 열리지 않았습니다."},
        {"not_available_on_combat","전투 중에는 실행할 수 없습니다."},
        {"not_available_on_dead","캐릭터가 쓰러진 상태에서는 실행할 수 없습니다."},
        {"not_available_on_riding","탑승 중에는 실행할 수 없습니다."},
        {"no_instrument","장착한 악기가 없습니다. 사용할 악기를 선택하세요."},
        {"is_playing_instrument","연주 중에는 악기를 바꿀 수 없습니다."},
        {"invalid_target","선택한 악기를 장착할 수 없습니다."},
        {"failed_unequip","기존 장비를 해제하지 못했습니다. 게임의 장비 상태를 확인하세요."},
        {"system_error","게임이 작업을 처리하지 못했습니다. 현재 상태를 확인하세요."},
        {"level_requirement","필요한 레벨에 도달하지 않았습니다."},
        {"rate_limited","요청이 너무 많습니다. 잠시 후 다시 시도하세요."}
    };
    public static string Code(string code){string text;return Errors.TryGetValue(code??"",out text)?text:"게임에서 작업을 완료하지 못했습니다. 게임 화면을 확인한 뒤 다시 시도하세요.";}
    static string ErrorCode(object data){string error=J.S(data,"error");if(error!="")return error;var body=J.Get(data,"body");return body==null?"":ErrorCode(body);}
    public static string Reply(object data,int exit){
        string code=ErrorCode(data);if(code=="")code=J.S(data,"reason");if(code=="")code=J.S(J.Get(data,"body"),"reason");
        if(code!="")return Code(code);
        if(exit==5)return "게임 연결이 끊겼습니다. 게임 실행과 AI 커넥터 설정을 확인하세요.";
        if(exit==3)return "게임 작업이 취소되었습니다.";
        if(exit!=0)return "게임 명령 실행에 실패했습니다. 게임 연결 상태를 확인하세요.";
        return Code(J.S(data,"status"));
    }
    public static string Error(Exception ex){
        var game=ex as GameCommandException;if(game!=null)return game.UserMessage;
        if(ex is WebException)return "서버에 연결할 수 없습니다. 인터넷 연결을 확인하고 잠시 후 다시 시도하세요.";
        if(ex is UnauthorizedAccessException)return "접근 권한이 없습니다. 파일 위치와 사용 권한을 확인하세요.";
        if(ex is FileNotFoundException||ex is DirectoryNotFoundException)return "필요한 파일이나 폴더를 찾지 못했습니다. 지정한 경로를 확인하세요.";
        if(ex is FormatException)return "입력 형식이 올바르지 않습니다. 파일이나 공유 문자열을 확인하세요.";
        if(Regex.IsMatch(ex.Message??"",@"[가-힣]"))return ex.Message;
        if(ex is IOException)return "파일을 읽거나 저장하지 못했습니다. 다른 프로그램에서 사용 중인지 확인하세요.";
        return "작업을 처리하지 못했습니다. 입력 내용과 연결 상태를 확인하세요. 자세한 내용은 기록에 남겼습니다.";
    }
    public static string Dungeon(string state){switch(state){case "Entering":return "입장 중";case "InProgress":return "진행 중";case "Cleared":return "완료";case "NotInDungeon":return "던전 밖";default:return "상태 확인 중";}}
    public static string Weather(string weather){switch(weather){case "Sunny":case "Clear":return "맑음";case "Cloudy":return "흐림";case "Rainy":case "Rain":return "비";case "Snowy":case "Snow":return "눈";case "Thunderstorm":return "뇌우";case "Foggy":return "안개";default:return "";}}
}
}
