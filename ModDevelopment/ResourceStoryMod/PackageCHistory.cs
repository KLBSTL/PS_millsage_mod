using System;
using System.Collections.Generic;
using System.Linq;

namespace ResourceStoryMod.Story
{
    public static partial class PackageC
    {
        private static string HistoryText(string template)=>template switch{
            "teacher_recognized"=>"小萤一眼认出了奏多姐生前常弹的这台旧合成器。",
            "synth_initial_repaired"=>"经过店里的初次整备，它的电源指示灯重新亮了起来。",
            "spinel_free_repair"=>"它在这家乐器店里完成了检修，并换上了一套新琴弦。",
            "synth_hammer_installed"=>"它的键床已完成木质重锤配重改装与触键校准。",
            "whale_restored"=>"经过内部线路整备，它已重新通过通电测试与底噪验收。",
            "mic_stage_calibrated"=>"这支主唱麦克风已完成频响与舞台基础校准。",
            "duel_first_night"=>"它见证了水道街 Live House 首夜的器乐合奏交锋。",
            "duel_last_night"=>"它在次日夜雨的《everscape》合奏比试中再度鸣响。",
            "reunion_first_live"=>"它陪伴五人集结的 millsage 完成了水道街首次路演。",
            "middle_selection_used"=>"它曾随乐队登上中层区星环回廊的选拔赛舞台。",
            "prelim_runner_up_used"=>"它随乐队在「彗星 Rising」首轮晋级赛中奏响《起死開戦》，斩获亚军。",
            "comet_champion_used"=>"它随乐队在「彗星 Rising」年度总决赛中奏响终曲《鳴らす》，见证了总冠军加冕。",
            "synth_custody_natsume"=>"夺冠之夜后，小枣按乐队约定带着它踏上了寻回同伴的旅途。",
            "hotaru_use"=>"由小萤按乐队约定保管并使用。",
            _=>""
        };
        private static string FullDescription(StoryState s,CampaignOriginal item,CampaignDefinition def,bool details,bool authorized)
        {
            var c=s.CampaignItems;
            if(!item.Visible || item.Location=="sealed_payload")return "依照约定妥善保管在店里的封存物品。";
            string brief=item.Role=="c_synth" && item.Condition!="damp"?"一台贴着钻星贴纸的 61 键旧合成器，琴键下方留有熟悉的浅痕。":def.Brief;
            bool instrument=def.Actor!=null;
            var events=c.History.Values.Where(x=>x.Entity==item.Entity && (instrument || x.Visibility=="public" || authorized && item.ReadAuthorized)).OrderBy(x=>x.Day).ThenBy(x=>x.Id,StringComparer.Ordinal).ToArray();
            var selected=new List<string>();
            if(details)selected.AddRange(events.Select(x=>HistoryText(x.Template)).Where(x=>x.Length>0).Distinct());
            else {
                // Preserve the strongest stage milestone and one distinctive early event.
                foreach(string key in new[]{"comet_champion_used","reunion_first_live","teacher_recognized","prelim_runner_up_used","middle_selection_used","synth_hammer_installed","synth_initial_repaired","whale_restored","spinel_free_repair","mic_stage_calibrated"}){
                    if(selected.Count==2)break;if(events.Any(x=>x.Template==key))selected.Add(HistoryText(key));
                }
            }
            string text=brief+(selected.Count==0?"":"\n"+string.Join("\n",selected));
            if(!details)return text.Length<=120?text:text.Substring(0,116)+"……";
            var uses=c.Manifests.Values.Where(x=>x.Completed && x.Entries.Values.Any(y=>y.Entity==item.Entity)).ToArray();
            text+="\n确切使用年限："+(item.KnownYears.HasValue?item.KnownYears+"年（"+item.AgeSource+"）":"未知")+"。";
            text+="\n首次登记：第"+item.FirstObservedDay+"天；正式Live "+uses.Count(x=>x.Kind=="live")+"场，其他舞台活动 "+uses.Count(x=>x.Kind=="activity")+"次；已确认使用 "+uses.Select(x=>x.CompletedDay).Distinct().Count()+"天。";
            text+="\n当前状态："+(item.Condition=="serviced"?"已完成整备":item.Condition=="unrecorded"?"工程制作中":item.Location=="sealed"?"按约封存":"按当前委托记录")+"；已安装可拆配件 "+item.Attachments.Count+"件。";
            if(item.Role=="c_keychain" && authorized && c.DiaryRevealed && item.ReadAuthorized)text+="\n当前授权："+(c.DiaryMode=="shared"?"共同阅读":c.DiaryMode=="filter"?"店方过滤后的许可范围":"物主独读；店方不展示原文")+"。";
            return text;
        }
        public static IEnumerable<MusicProduct> RetailProducts()=>new[]{
            new MusicProduct{Id="ms_item_standard_strings",Name="普通吉他套弦",Actor="ms_natsume",Profile="spinel_guitar",Slot="consumable",NativeType="MATERIAL",Price=180,Willingness=220,Width=1,Height=2,UnlockScene=9,Zones=new[]{"middle","upper"}},
            new MusicProduct{Id="ms_strings_bass5_nickel",Name="五弦贝斯低音套弦",Actor="ms_mahoro",Profile="stage_bass",Slot="strings",NativeType="MATERIAL",Price=420,Willingness=520,Width=1,Height=2,UnlockScene=11,Zones=new[]{"middle","upper"}},
            new MusicProduct{Id="ms_strings_guitar6_tension",Name="六弦高张力套弦",Actor="ms_nagi",Profile="whale_guitar",Slot="strings",NativeType="MATERIAL",Price=460,Willingness=560,Width=1,Height=2,UnlockScene=11,Zones=new[]{"middle","upper"}},
            new MusicProduct{Id="ms_part_hammer_weights",Name="合成器重锤组件",Actor="ms_hotaru",Profile="diamond_synth",Slot="hammer",NativeType="MATERIAL",Price=960,Willingness=1120,Width=2,Height=2,UnlockScene=13,Zones=new[]{"upper"}},
            new MusicProduct{Id="ms_gear_stage_mic",Name="舞台电容话筒",Actor="ms_hotaru",Profile="stage_mic",NativeType="HOUSEHOLD_GOOD",Price=1800,Willingness=2100,Width=2,Height=3,UnlockScene=14,Zones=new[]{"middle","upper"}},
            new MusicProduct{Id="ms_part_blank_tape",Name="空白录音带",Actor="ms_mahoro",Profile="recording",Slot="medium",NativeType="MATERIAL",Price=240,Willingness=300,Width=2,Height=2,UnlockScene=23,Zones=new[]{"middle","upper"}}
        };
        public static IEnumerable<Dictionary<string,string>> SupplierCandidates(StoryState s)
        {
            if(!FullEnabled(s))yield break;
            foreach(var p in RetailProducts())yield return new(){{"id",p.Id},{"pool",p.Id=="ms_gear_stage_mic"?"large":p.Id=="ms_part_hammer_weights"?"pro":"parts"},{"source_zones",string.Join(";",p.Zones)},{"used_allowed","False"}};
        }
        public static CampaignUseRequest OfferUseRequest(StoryState s,string actor,int day)
        {
            if(!FullEnabled(s) || !CanContact(s,actor,false) && !CanContact(s,actor,true))return null;var c=s.CampaignItems;
            if(day-c.LastUseRequestDay<3 || c.UseRequests.Values.Any(x=>x.Day==day) || !c.BusinessLeads.Contains("middle_stage_interfaces"))return null;
            string role=actor=="ms_nagi"?"c_whale":actor=="ms_hotaru"?"c_synth":null;
            if(role==null || !c.Originals.TryGetValue(role,out var r) || r.Location!="member" || r.Custodian!=actor)return null;
            string kind=r.Condition=="wet_broken_string" || r.Condition=="circuit_repaired"?"known_fault":r.CalibratedRevision!=r.ConfigRevision?"configuration_check":actor=="ms_nagi" && !r.Attachments.ContainsKey("signal")?"stage_stability":null;
            if(kind==null)return null;string source=kind=="stage_stability"?"middle_stage_interfaces":r.Condition+":"+r.ConfigRevision;
            string id="use_"+actor+"_"+r.Entity+"_"+kind+"_"+source;
            if(c.UseRequests.ContainsKey(id))return null;
            var request=new CampaignUseRequest{Id=id,Actor=actor,Role=role,Kind=kind,Source=source,Day=day};c.UseRequests.Add(id,request);c.LastUseRequestDay=day;return request;
        }
        public static CampaignWork AcceptUseRequest(StoryState s,string requestId,int day,ICampaignItemAdapter a)
        {
            var c=s.CampaignItems;var r=c.UseRequests[requestId];Need(CanContact(s,r.Actor,true) || CanContact(s,r.Actor,false),"成员暂不可联系，保留提案");
            if(r.Job!=null)return c.Jobs[r.Job];Need(r.Status=="offered","提案已结束");
            var host=c.Originals[r.Role];Need(host.Location=="member" && host.Custodian==r.Actor,"成员尚未带来同一原件");
            string id="c_optional_"+host.Entity+"_"+c.UseRequests.Count;
            List<CampaignWorkStep> steps;
            if(r.Kind=="stage_stability")steps=new(){Step("signal","安装稳定拾音件",60,10,"ms_part_stabilizer_pickup*1","ref_screwdriver*1"),Step("calibrate","配置基础校准",60,12,"","","calibration")};
            else if(r.Kind=="known_fault")steps=new(){Step("circuit","修复已知线路故障",60,10,"ms_supply_solder_pack*1;ms_part_wire_bundle*1","ref_welder*1"),Step("test","基础验收",30,6,"","","counter")};
            else steps=new(){Step("calibrate","现有配置复核",60,12,"","","calibration")};
            Hand(s,r.Role,"player","custody",day,a,"optional_authorized:"+id);
            var j=CreateFullJob(s,id,r.Role,"可选器材用途委托",r.Actor,day,0,false,180,steps,a);r.Job=id;r.Status="accepted";return j;
        }
        public static void DeclineUseRequest(StoryState s,string id){var r=s.CampaignItems.UseRequests[id];Need(r.Status=="offered","已经受理的委托请按工序办理");r.Status="declined";}
        public static void DeliverOptional(StoryState s,string id,int day,ICampaignItemAdapter a)
        {
            var r=s.CampaignItems.UseRequests.Values.Single(x=>x.Job==id);Deliver(s,id,day,a,r.Actor);r.Status="completed";
        }
    }
}
