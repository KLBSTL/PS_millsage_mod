using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace ResourceStoryMod.Story
{
    public sealed partial class CampaignItemsState
    {
        [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
        public CampaignNativeDiary NativeDiary {get;set;}
    }
    public sealed class CampaignNativeDiary
    {
        public int Version {get;set;}=1;
        public int Box {get;set;}
        public int Note {get;set;}
        public int Blank {get;set;}
        public bool BlankIssued {get;set;}
        public string BlankSku {get;set;}
        public int Machine {get;set;}
        public int PlannedDay {get;set;}=-1;
        public int WorkedDay {get;set;}=-1;
        public int Power {get;set;}
        public int DueDay {get;set;}=-1;
        public string TradeVisit {get;set;}
        public bool Closed {get;set;}
        public bool MahoroReturned {get;set;}
        public bool Gifted {get;set;}
        public bool ViewingGranted {get;set;}
        public bool ShopReadGranted {get;set;}
        public bool CoverRequested {get;set;}
        public bool ResultReturned {get;set;}
        public string RevealedMode {get;set;}
        public HashSet<string> Witnesses {get;set;}=new();
        public HashSet<string> Oct9Knowers {get;set;}=new();
        public bool KeepSecretUntilContest {get;set;}
        public Dictionary<string,MemberTradeReceipt> Trades {get;set;}=new();
    }
    public static class DiaryPresentation
    {
        public static bool Scene(string id)=>id is "ms_scene_017" or "ms_scene_018" or "ms_scene_019" or "ms_scene_020";
        public static string Lead(string id)=>id is "ms_scene_018" or "ms_scene_019" or "ms_scene_020"?"ms_hotaru":id=="ms_scene_017"?"ms_mahoro":null;
        public static string[] Actors(StoryState s,StoryScene scene,string node,int part=0)
        {
            if(scene.Id=="ms_scene_018"&&node=="line_001"&&part<2)return new[]{"ms_hotaru"};
            if(scene.Id=="ms_scene_019"&&node is "line_001" or "line_002")return new[]{"ms_hotaru"};
            if(scene.Id=="ms_scene_020")return s?.CampaignItems?.DiaryMode switch{"raw"=>new[]{"ms_hotaru"},"filter"=>new[]{"ms_hotaru","ms_houka"},_=>new[]{"ms_hotaru","ms_mahoro","ms_natsume","ms_nagi","ms_houka"}};
            return scene.Participants.Where(MemberBusiness.ValidActor).Distinct().ToArray();
        }
        public static string Emotion(string scene,string node,string actor)=>scene=="ms_scene_020"?(node.EndsWith("b")?"smile":"sad"):scene=="ms_scene_017"?"smile":"neutral";
    }
    public static partial class PackageC
    {
        public const string DiaryBlankReference="ref_native_blank_keycard";
        public static void PrepareDiary(StoryState s)=>s.CampaignItems.NativeDiary??=new();
        private static string DiaryAdmission(StoryState s,string scene,int day)
        {
            var c=s.CampaignItems;var n=c.NativeDiary;if(n==null||c.LegacyScenes.Contains(scene)||c.LegacyActiveScene==scene)return null;
            if(scene=="ms_scene_017"&&s.CompletedDays.TryGetValue("ms_scene_016",out int live)&&day<=live)return "庆功采购在选拔赛次日。";
            if(scene=="ms_scene_020"){
                if(!c.Jobs.TryGetValue(DecodeJob,out var j)||!j.Ready||n.WorkedDay<0)return "请把授权挂件、随附空白卡和至少6E电源放入密码解序器，睡觉过一天读取。";
                if(day<j.EarliestDay)return "按昨天的约定，最早次日查看。";
                if(n.ResultReturned||!c.Originals.ContainsKey("c_diary_result"))return "授权结果卡或原件交回记录不完整。";
                if(!s.Economy.Business.Appointments.TryGetValue("ms_hotaru",out var appointment)||appointment.Day!=day||appointment.Status is not ("pending" or "visited"))return "读取已完成，请通过原电话另约次日查看；按时完成则沿用原来的次日约定。";
            }
            return null;
        }
        private static void DiaryBeforeEnter(StoryState s,string scene,string node,int day,string choice,ICampaignItemAdapter adapter)
        {
            var c=s.CampaignItems;var n=c.NativeDiary;if(n==null)return;
            if(scene=="ms_scene_018"&&node=="line_003")n.MahoroReturned=true;
            if(scene=="ms_scene_018"&&node is "line_006a" or "line_006b"){
                Need(n.MahoroReturned,"先把同一挂件归还茉幌，再赠予萤。");n.Gifted=true;n.ViewingGranted=true;
            }
            if(scene=="ms_scene_019"&&choice!=null){
                Need(n.Gifted&&n.ViewingGranted&&c.Originals["c_keychain"].Owner=="ms_hotaru","保管或发现芯片不等于读取授权。");
                n.ShopReadGranted=true;n.CoverRequested=choice=="opt_decode_shop_filter";
            }
            if(scene=="ms_scene_020"&&node=="line_001"){
                Need(n.WorkedDay>=0&&n.ShopReadGranted,"原生读取尚未完成。");
                Hand(s,"c_diary_result","ms_hotaru","member",day,adapter,"020.authorized_result_return");n.ResultReturned=true;
            }
            if(scene=="ms_scene_020"&&choice!=null){
                string mode=choice=="opt_present_solo_truth"?"raw":choice=="opt_present_erased_oct9"?"filter":"shared";
                Need(mode==c.DiaryMode&&n.RevealedMode==null,"展示方式必须沿用昨天已确认的授权方案。");n.RevealedMode=mode;
                foreach(string actor in DiaryPresentation.Actors(s,safeScene(s,scene),node))n.Witnesses.Add(actor);
                if(mode=="shared")foreach(string actor in Five)n.Oct9Knowers.Add(actor);
                else if(mode=="raw")n.Oct9Knowers.Add("ms_hotaru");
                else n.Oct9Knowers.Add("ms_houka");
                n.Oct9Knowers.Add("player");n.KeepSecretUntilContest=mode=="raw";
            }
            // Only the authored participant set is needed by the pure ledger.
            StoryScene safeScene(StoryState state,string id)=>new(){Id=id,Participants=Five.ToList()};
        }
        private static void DiaryAfterEnter(StoryState s,string scene,int day,string choice,ICampaignItemAdapter adapter)
        {
            var c=s.CampaignItems;var n=c.NativeDiary;if(n==null||scene!="ms_scene_019"||choice==null)return;
            if(n.Blank==0){var r=adapter.Create(DiaryBlankReference,"native_blank_keycard","ms_hotaru",false);Need(r.Entity>0&&!string.IsNullOrEmpty(r.Snapshot),"原版空白卡供料缺失。");c.Jobs[DecodeJob].Resources.Add(r);n.Blank=r.Entity;n.BlankSku=r.Sku;}
            if(n.DueDay<0){n.DueDay=day+1;MemberBusiness.Book(s.Economy.Business,"ms_hotaru",day,true,true,s);c.Receipts.Add("diary.next_day_appointment");}
        }
        public static void PlanDiary(StoryState s,int machine,int day)
        {
            var c=s.CampaignItems;var n=c.NativeDiary;Need(n?.ShopReadGranted==true&&c.DiaryMode!=null&&c.Jobs.ContainsKey(DecodeJob),"先在柜台确认读取方式。");Need(machine>0&&day>=c.Jobs[DecodeJob].RequestDay,"实际机器或游戏日无效。");if(c.Jobs[DecodeJob].Ready)return;n.Machine=machine;n.PlannedDay=day;
        }
        public static bool WorkDiary(StoryState s,int day,int machine,int original,int blank,int power,int energy,string originalSnapshot,string resultSnapshot)
        {
            var c=s.CampaignItems;var n=c.NativeDiary;Need(n!=null&&n.ShopReadGranted&&n.ViewingGranted&&n.Gifted,"读取授权不完整。");
            var j=c.Jobs[DecodeJob];if(j.Ready)return false;var r=c.Originals["c_keychain"];
            Need(n.Machine==machine&&machine>0&&day>n.PlannedDay&&day>=j.EarliestDay,"只在放入机器后睡觉过一天读取。");
            Need(original==r.Entity&&r.Owner=="ms_hotaru"&&r.Custodian=="player"&&r.Location=="native"&&blank==n.Blank&&blank>0,"需要同一原件和本次实际空白卡。");
            Need(power>0&&energy==6&&!string.IsNullOrEmpty(originalSnapshot)&&!string.IsNullOrEmpty(resultSnapshot),"实际6E电量或输出快照缺失。");
            while(!j.Ready)FinishNativeStep(s,j,day,power);
            var resource=j.Resources.Single(x=>x.Entity==blank);j.Resources.Remove(resource);c.Receipts.Add("diary.blank_converted."+blank);
            c.Originals.Add("c_diary_result",new(){Role="c_diary_result",Entity=blank,Sku="ms_story_diary_readout_card",Snapshot=resultSnapshot,Owner="ms_hotaru",Custodian="player",Location="native",Source="019.actual_blank_converted",FirstObservedDay=day,ReadAuthorized=true,Condition=c.DiaryMode});
            r.Snapshot=originalSnapshot;n.WorkedDay=day;n.Power=power;return true;
        }
        public static bool DiaryTrade(StoryState s,string receipt,string scene,int entity,string sku,string snapshot,int paid)
        {
            var n=s.CampaignItems.NativeDiary;string actor=DiaryPresentation.Lead(scene);
            Need(n!=null&&scene is "ms_scene_017" or "ms_scene_018"&&entity>0&&paid>=0&&!string.IsNullOrEmpty(snapshot)&&!sku.StartsWith("ms_"),"庆功日用品交易身份无效。");
            if(n.Trades.TryGetValue(receipt,out var prior)){Need(prior.ActorId==actor&&prior.EntityId==entity&&prior.Price==paid,"交易回执条款冲突。");return false;}
            Need(s.Economy.Wallets[actor].Balance>=paid&&!s.Economy.OwnedItems.ContainsKey(entity),"成员资金或实物归属不符。");
            s.Economy.Wallets[actor].Balance-=paid;s.Economy.OwnedItems[entity]=new(){ActorId=actor,EntityId=entity,ItemId=sku,NativeSnapshot=snapshot};var r=new MemberTradeReceipt{ActorId=actor,EntityId=entity,MemberBuys=true,Price=paid};n.Trades.Add(receipt,r);if(paid>0)s.Economy.Trades.Add(receipt,r);return true;
        }
        public static List<StoryDialoguePart> DiaryParts(StoryState s,string scene,StoryNode node,string text,int day)
        {
            if(s.CampaignItems?.NativeDiary==null||!DiaryPresentation.Scene(scene))return node.DialogueParts;
            var parts=node.DialogueParts??new(){new(){Speaker=node.Speaker,Text=text??""}};
            string Adapt(string value){
                if(scene=="ms_scene_017"&&node.Id=="line_002"&&new[]{BassStrings,GuitarStrings}.All(id=>s.CampaignItems.Jobs.TryGetValue(id,out var j)&&j.Delivered))value=value.Replace("这是之前定制琴弦的尾款，","之前定制的琴弦用起来很顺手，");
                if(scene=="ms_scene_017"&&node.Id=="line_001"&&s.CompletedDays.TryGetValue("ms_scene_016",out int live)&&day>live+1)value=value.Replace("昨晚的选拔赛","之前的选拔赛");
                if(scene=="ms_scene_020"&&node.Id=="line_001"&&s.CompletedDays.TryGetValue("ms_scene_019",out int request)&&day>request+1)value=value.Replace("第二天傍晚","几天后的傍晚");
                if(scene=="ms_scene_020"&&node.Id=="line_002a"&&s.CompletedDays.TryGetValue("ms_scene_015",out int handover)&&day!=handover+2)value=value.Replace("我前天居然","我那天居然");
                return value;
            }
            return parts.Select(p=>new StoryDialoguePart{Speaker=p.Speaker,Text=Adapt(p.Text),WaitDaysBefore=p.WaitDaysBefore}).ToList();
        }
        private static void ValidateDiary(CampaignItemsState c)
        {
            var n=c.NativeDiary;if(n==null)return;
            if(n.Version!=1||n.Box<0||n.Note<0||n.Blank<0||n.Machine<0||n.PlannedDay< -1||n.WorkedDay< -1||n.DueDay< -1||n.Witnesses==null||n.Oct9Knowers==null||n.Trades==null||n.Gifted&&!n.MahoroReturned||n.ViewingGranted&&!n.Gifted||n.ShopReadGranted&&!n.ViewingGranted||n.CoverRequested&&c.DiaryMode is not (null or "filter")||n.ResultReturned&&n.WorkedDay<0)throw new InvalidDataException("挂件授权或读取记录无效，保留旧档。");
            if(n.WorkedDay>=0&&(!c.Jobs.TryGetValue(DecodeJob,out var j)||!j.Ready||n.Power<=0||!c.Originals.TryGetValue("c_diary_result",out var r)||r.Entity!=n.Blank))throw new InvalidDataException("原生读取缺少实际卡片或供电记录。");
            if(n.RevealedMode!=null&&(n.RevealedMode!=c.DiaryMode||!n.ResultReturned)||n.Witnesses.Any(a=>!MemberBusiness.ValidActor(a))||n.Oct9Knowers.Any(a=>a!="player"&&!MemberBusiness.ValidActor(a)))throw new InvalidDataException("剧情知情分支或原件交付冲突。");
        }
    }
}
