using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ResourceStoryMod.Story
{
    public sealed class NormalPlayConfig
    {
        public int Version {get;set;}=1;
        public bool Enabled {get;set;}=true;
        public bool DeveloperPanels {get;set;}
        public int NewCounterScenesPerDay {get;set;}=1;
        public int InspectionOfferChance {get;set;}=35;
        public int InspectionGuaranteeOn {get;set;}=3;
        public int SupplierEnergyE {get;set;}=20;
        public void Validate(){if(Version!=1||NewCounterScenesPerDay<1||NewCounterScenesPerDay>4||InspectionOfferChance<0||InspectionOfferChance>100||InspectionGuaranteeOn<1||InspectionGuaranteeOn>12||SupplierEnergyE<20||SupplierEnergyE>100)throw new InvalidOperationException("普通游玩配置或预约供货参数无效，保留原配置。");}
    }
    public static class NormalPlayPolicy
    {
        public const string BasicInspector="ms_tool_basic_inspector";
        public const int BasicInspectorPrice=480;
        public static bool IsInspector(string sku)=>sku==BasicInspector||sku=="ms_machine_calibrator";
        // Missing/ambiguous legacy markers keep their original multi-route graph.
        public static string ProfileForSave(string saved)
        {
            if(string.IsNullOrEmpty(saved))return "MillsageSoloReturnV12";
            using var d=JsonDocument.Parse(saved);var root=d.RootElement;
            if(root.TryGetProperty("campaignItems",out var c)&&c.ValueKind==JsonValueKind.Object&&c.TryGetProperty("return",out var r)&&r.ValueKind==JsonValueKind.Object)return "MillsageSoloReturnV12";
            return "MillsageCampaignV11";
        }
        public static bool CanQueue(StoryPackage package,StoryState state,StoryScene scene,int day,int limit=1)
        {
            if(scene==null)return false;
            if(scene.Context!="counter"||state.Active?.SceneId==scene.Id)return true;
            int today=package.Scenes.Count(s=>s.Context=="counter"&&state.CompletedDays.GetValueOrDefault(s.Id,-1)==day);
            return today<limit;
        }
        public static IEnumerable<SupplyBatch> IntroductoryStock(StoryState state,string zone)
        {
            // The general bench is still needed after the introductory synth
            // leaves the shop. Completion must not remove its reliable source.
            if(state.CampaignItems?.Opening==null)yield break;
            if(zone is "lower" or "middle")yield return new SupplyBatch{Sku=BasicInspector,Pool="parts",Grade="ordinary",Quantity=1};
            if(zone=="middle")yield return new SupplyBatch{Sku="ms_machine_electrobench",Pool="large",Grade="ordinary",Quantity=1};
        }
        public static void EnsureIntroductoryStock(StoryState state,SupplierVisit visit,ISet<string> owned)
        {
            foreach(var batch in IntroductoryStock(state,visit.Zone)){
                if(owned.Contains(batch.Sku)||visit.Batches.Any(x=>x.Sku==batch.Sku))continue;
                int used=state.Workshop.Advanced.Quotas.GetValueOrDefault(PackageBSupply.QuotaKey(visit.Day,batch.Pool));
                if(used>=PackageBSupply.Limit(batch.Pool))continue;
                // Replace a planned batch when necessary; never expand the daily quota.
                if(batch.Pool=="large")visit.Batches.RemoveAll(x=>x.Pool=="large"&&!x.Committed);
                while(used+visit.Batches.Count(x=>x.Pool==batch.Pool)>=PackageBSupply.Limit(batch.Pool)){
                    var prior=visit.Batches.LastOrDefault(x=>x.Pool==batch.Pool&&!x.Committed);if(prior==null)break;visit.Batches.Remove(prior);
                }
                int cap=visit.Zone=="lower"?2:visit.Zone=="middle"?3:4;
                while(visit.Batches.Count>=cap){
                    var prior=visit.Batches.LastOrDefault(x=>!x.Committed&&x.Sku!=BasicInspector&&x.Sku!="ms_machine_electrobench");if(prior==null)break;visit.Batches.Remove(prior);
                }
                if(visit.Batches.Count>=cap)continue;
                visit.Batches.Insert(0,batch);visit.Status="pending";
            }
        }
    }
    public sealed class NormalSupplierState
    {
        public int Version {get;set;}=1;
        public string RunId {get;set;}
        public int DueDay {get;set;}=-1;
        public int ArrivedDay {get;set;}=-1;
        public HashSet<string> PowerStockVisits {get;set;}=new();
        public HashSet<int> PowerStockDays {get;set;}=new();
        public HashSet<string> PowerSpaceBlockedVisits {get;set;}=new();
        public bool Book(int day){if(day<0)throw new ArgumentOutOfRangeException(nameof(day));if(DueDay>=day)return false;DueDay=checked(day+1);return true;}
        public bool Due(int day)=>DueDay==day;
        public void Validate(string run){if(Version!=1||RunId!=run||DueDay< -1||ArrivedDay< -1||PowerStockVisits==null||PowerStockDays==null||PowerSpaceBlockedVisits==null||PowerStockDays.Any(d=>d<0))throw new InvalidOperationException("器材供货预约记录无效，保留原记录。");}
    }
}
