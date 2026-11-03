using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace ResourceStoryMod.Story
{
    public sealed partial class CampaignItemsState
    {
        [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
        public CampaignOpening Opening {get;set;}
    }
    public sealed class CampaignOpening
    {
        public int Version {get;set;}=1;
        public Dictionary<string,int> Notes {get;set;}=new();
        public HashSet<int> Issued {get;set;}=new();
        public Dictionary<int,OpeningOffer> Offers {get;set;}=new();
        public HashSet<string> StockPrepared {get;set;}=new();
        public Dictionary<string,MemberTradeReceipt> Trades {get;set;}=new();
        public int Machine {get;set;}
        public int PlannedDay {get;set;}=-1;
        public int WorkedDay {get;set;}=-1;
        public int TestPower {get;set;}
        public bool Detected {get;set;}
    }
    public sealed class OpeningOffer
    {
        public string Actor {get;set;}
        public string Scene {get;set;}
        public string Snapshot {get;set;}
    }
    public static partial class PackageC
    {
        public static bool OpeningScene(string scene)=>scene is "ms_scene_001" or "ms_scene_002" or "ms_scene_003" or "ms_scene_004";
        public static string OpeningActor(string scene)=>scene switch{"ms_scene_001" or "ms_scene_004"=>"ms_mahoro","ms_scene_002"=>"ms_natsume","ms_scene_003"=>"ms_hotaru",_=>null};
        public static bool OpeningCommodity(string scene,string sku,string type,bool protectedItem)
        {
            if(!OpeningScene(scene)||scene=="ms_scene_004"||protectedItem||string.IsNullOrEmpty(sku)||sku.StartsWith("ms_",StringComparison.Ordinal))return false;
            string value=(sku+" "+type).ToLowerInvariant();
            string[] words=scene=="ms_scene_001"?new[]{"food","water","drink","bottle","jug","noodle","ration"}:scene=="ms_scene_002"?new[]{"food","drink","sandwich","coffee","caffeine","energy_drink","medicine","cream","noodle"}:new[]{"food","cookie","biscuit","snack","lamp","light","water"};
            return words.Any(value.Contains);
        }
        public static bool OpeningTrade(StoryState s,string receipt,string scene,int entity,string sku,string snapshot,bool memberBuys,int paid)
        {
            string actor=OpeningActor(scene);var o=s.CampaignItems.Opening;
            Need(o!=null&&scene!="ms_scene_004"&&actor!=null&&entity>0&&paid>=0&&!string.IsNullOrEmpty(snapshot)&&!sku.StartsWith("ms_"),"日用品交易身份无效");
            var record=new MemberTradeReceipt{ActorId=actor,EntityId=entity,MemberBuys=memberBuys,Price=paid};
            if(o.Trades.TryGetValue(receipt,out var prior)){Need(prior.ActorId==actor&&prior.EntityId==entity&&prior.MemberBuys==memberBuys&&prior.Price==paid,"交易回执条款冲突");return false;}
            var wallet=s.Economy.Wallets[actor];
            if(memberBuys){Need(wallet.Balance>=paid&&!s.Economy.OwnedItems.ContainsKey(entity),"成员余额或物品归属不符");wallet.Balance-=paid;s.Economy.OwnedItems[entity]=new(){ActorId=actor,EntityId=entity,ItemId=sku,NativeSnapshot=snapshot};}
            else{Need(s.Economy.OwnedItems.TryGetValue(entity,out var owned)&&owned.ActorId==actor&&owned.ItemId==sku,"不是该成员持有的日用品");wallet.Balance=checked(wallet.Balance+paid);s.Economy.OwnedItems.Remove(entity);o.Offers.Remove(entity);}
            o.Trades.Add(receipt,record);if(paid>0)s.Economy.Trades.Add(receipt,record);return true;
        }
        public static int OpeningSynthEnergy(StoryState s)
        {
            var o=s.CampaignItems.Opening;var j=s.CampaignItems.Jobs[SynthJob];
            if(j.Ready||o.TestPower>0)return 0;
            return (int)Math.Ceiling(j.Steps.Skip(j.Step).Sum(x=>(decimal)x.EnergyE*(x.DurationMs-x.ElapsedMs)/x.DurationMs));
        }
        public static bool WorkOpeningSynth(StoryState s,int day,int source,int paid)
        {
            var c=s.CampaignItems;var o=c.Opening;var j=c.Jobs[SynthJob];var r=c.Originals["c_synth"];
            Need(o!=null&&r.Location=="native"&&r.Custodian=="player","合成器原件不在店内");
            if(o.WorkedDay>=day||j.Ready||o.TestPower>0)return false;
            Need(day>o.PlannedDay&&o.Machine>0&&paid==OpeningSynthEnergy(s)&&source>0,"需要完整的日结工位、电源与电量");
            while(!j.Ready&&j.Steps[j.Step].Id!="test")FinishNativeStep(s,j,day,source);
            o.TestPower=source;o.WorkedDay=day;o.Detected=false;o.Machine=0;return true;
        }
        public static bool DetectOpeningSynth(StoryState s,int day)
        {
            var c=s.CampaignItems;var o=c.Opening;var j=c.Jobs[SynthJob];
            Need(o!=null&&o.TestPower>0&&c.Originals["c_synth"].Location=="native","先在整备台过夜完成加工");
            if(o.Detected)return false;
            Need(j.Ready||j.Steps[j.Step].Id=="test","原件仍未完成加工");
            if(!j.Ready)FinishNativeStep(s,j,day,o.TestPower);
            c.Originals["c_synth"].CalibratedRevision=c.Originals["c_synth"].ConfigRevision;o.Detected=true;return true;
        }
        private static void ValidateOpening(CampaignItemsState c)
        {
            var o=c.Opening;if(o==null)return;
            if(o.Version!=1||o.Notes==null||o.Issued==null||o.Offers==null||o.Trades==null||o.StockPrepared==null||o.Machine<0||o.PlannedDay< -1||o.WorkedDay< -1||o.TestPower<0)throw new System.IO.InvalidDataException("开篇记录结构或日结值无效，保留旧档");
            foreach(var note in o.Notes)if(!OpeningScene(note.Key)||note.Value<=0)throw new System.IO.InvalidDataException("开篇便条身份无效："+note.Key+"/"+note.Value);
            if(o.Notes.Values.Distinct().Count()!=o.Notes.Count||o.Issued.Any(x=>x<=0))throw new System.IO.InvalidDataException("开篇物料或便条身份重复");
            foreach(var offer in o.Offers)if(offer.Key<=0||offer.Value==null||OpeningActor(offer.Value.Scene)!=offer.Value.Actor||string.IsNullOrEmpty(offer.Value.Snapshot))throw new System.IO.InvalidDataException("开篇商品身份无效："+offer.Key+"/"+offer.Value?.Scene+"/"+offer.Value?.Actor+"/snapshot="+offer.Value?.Snapshot?.Length);
            if(o.Trades.Any(x=>!StoryContent.ValidId(x.Key)||x.Value==null||x.Value.Price<0)||o.StockPrepared.Any(x=>!OpeningScene(x)||x=="ms_scene_004"))throw new System.IO.InvalidDataException("开篇回执或供货场次无效");
        }
    }
}
