using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
namespace MabiRemote {
public class DelayedActivityNotices {
    readonly List<KeyValuePair<DateTime,ActivityNotice>> pending=new List<KeyValuePair<DateTime,ActivityNotice>>();
    public void Add(ActivityNotice notice,DateTime now){pending.Add(new KeyValuePair<DateTime,ActivityNotice>(now.AddSeconds(15),notice));}
    public List<ActivityNotice> Due(DateTime now){var ready=pending.Where(x=>x.Key<=now).ToList();foreach(var item in ready)pending.Remove(item);return ready.Select(x=>x.Value).ToList();}
}
public class BlackLumpWatch {
    bool above;int lastLimit=-1;
    public bool Observe(int count,int limit,bool enabled){if(lastLimit!=limit){above=false;lastLimit=limit;}if(!enabled){above=false;return false;}bool current=count>limit;bool notify=current&&!above;above=current;return notify;}
    public static int Count(IEnumerable<object> items){return (int)items.Where(x=>J.S(x,"Location")=="inventory"&&FriendlyText.DisplayName(J.S(x,"DisplayName")).Replace(" ","")=="검은덩어리").Sum(x=>J.N(x,"Count"));}
}
public partial class MainForm {
    readonly DelayedActivityNotices delayedNotices=new DelayedActivityNotices();
    bool bagWasOverweight;
    void CheckBagWeight(Snapshot current){bool over=cfg.BagOverweightNotifications&&current.Capacity>0&&current.Weight>current.Capacity;if(over&&!bagWasOverweight)Notify("가방 무게 초과",current.Weight.ToString("0.#")+" / "+current.Capacity.ToString("0.#")+" · 최대 무게 100%를 초과했습니다.");bagWasOverweight=over;}
    readonly BlackLumpWatch blackLumpWatch=new BlackLumpWatch();
    void QueueCompletionNotice(ActivityNotice notice){delayedNotices.Add(notice,DateTime.UtcNow);Log(notice.Title+" 감지 · 연출 대기 후 15초 뒤 알림");}
    void DeliverPendingNotices(){foreach(var notice in delayedNotices.Due(DateTime.UtcNow))if((notice.Kind=="dungeon"&&cfg.DungeonNotifications)||(notice.Kind=="hunting"&&cfg.HuntingNotifications))Notify(notice.Title,notice.Text);}
    void CheckBlackLumps(IEnumerable<object> items){int count=BlackLumpWatch.Count(items);if(blackLumpWatch.Observe(count,cfg.BlackLumpLimit,cfg.BlackLumpNotifications))Notify("검은덩어리 수량 알림","가방에 "+count+"개 있습니다. 설정 기준 "+cfg.BlackLumpLimit+"개를 초과했습니다.");}
    void AddBlackLumpSettings(FlowLayoutPanel opts){var bag=new CheckBox{Text="가방 무게 100% 초과 시 알림",AutoSize=true,Checked=cfg.BagOverweightNotifications,Margin=new Padding(8)};bag.CheckedChanged+=(s,e)=>{cfg.BagOverweightNotifications=bag.Checked;if(!bag.Checked)bagWasOverweight=false;Save();};opts.Controls.Add(bag);var row=Bar();row.Dock=DockStyle.None;var enabled=new CheckBox{Text="검은덩어리 초과 알림",Checked=cfg.BlackLumpNotifications,AutoSize=true,Margin=new Padding(8)};var limit=Number(cfg.BlackLumpLimit,1000000);row.Controls.Add(enabled);row.Controls.Add(limit);row.Controls.Add(Label("개 초과 시 알림 (가방 기준 · 즉시 저장)",9));enabled.CheckedChanged+=(s,e)=>{cfg.BlackLumpNotifications=enabled.Checked;Save();};limit.ValueChanged+=(s,e)=>{cfg.BlackLumpLimit=(int)limit.Value;Save();};opts.Controls.Add(row);}
}
}
