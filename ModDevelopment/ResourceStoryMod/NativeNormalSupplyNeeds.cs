using System;
using System.Collections.Generic;
using System.Linq;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    internal sealed partial class NativeNormalSupplier
    {
        private readonly Dictionary<string,string> nativeSkus=new();
        internal string ResolveNative(string kind)
        {
            if(nativeSkus.TryGetValue(kind,out var cached))return cached;
            GameItem template=kind switch{
                "decoder"=>MachineDesequencer.Desequencer(),
                "cloth"=>Resources.FindObjectsOfTypeAll(NativeTypes.Of<AmenitiesItemDirectory>())[0].Cast<AmenitiesItemDirectory>().KottonFabric(),
                "food"=>Resources.FindObjectsOfTypeAll(NativeTypes.Of<FoodItemDirectory>())[0].Cast<FoodItemDirectory>().CupNoodle(),
                _=>throw new InvalidOperationException("未知原版补给类别")};
            var actual=NativeDiaryCampaign.NativeItem(template);GeneralHelper.SetItemOwned(template,false);GeneralHelper.SetItemOwned(actual,false);
            nativeSkus.Add(kind,actual.identifier);return actual.identifier;
        }
        private static bool Food(GameItem item)=>item.GetGameItemType().Contains("FOOD")||item.GetGameItemType().Contains("DRINK")||item.IsTag("WATER");
        private static bool Cloth(GameItem item)=>item.identifier.Contains("cloth",StringComparison.OrdinalIgnoreCase)||item.identifier.Contains("fabric",StringComparison.OrdinalIgnoreCase);
        internal NormalSupplyNeed[] Needs(StoryState state=null)
        {
            var s=state??host.ReadState;var result=new List<NormalSupplyNeed>();var e=host.Economy;
            var all=e.AllPlayerItems().ToArray();var installed=s.Economy.Business.Instruments.Values.SelectMany(i=>i.Slots.Values).ToHashSet();
            var allocated=s.CampaignItems.Jobs.Values.SelectMany(j=>j.Resources).Where(r=>r.State=="available").Select(r=>r.Entity).ToHashSet();
            foreach(var job in s.Workshop.Jobs.Values)foreach(var item in job.Inputs)allocated.Add(item.Entity);
            foreach(var service in s.Workshop.Advanced.Services.Values.Where(v=>!v.Terminal))foreach(var id in service.Held.Keys)allocated.Add(id);
            bool Free(GameItem item)=>e.PackageB.PlayerOwned(item)&&!installed.Contains(item.uniqueId)&&!allocated.Contains(item.uniqueId)&&!host.Inspection.Managed(item);
            bool Own(string sku)=>all.Any(i=>i.identifier==sku&&e.PackageB.PlayerOwned(i)&&!host.Inspection.Managed(i));
            void Add(string sku,string pool,string name,bool native=false,int qty=1){if(!result.Any(n=>n.Sku==sku))result.Add(new(){Sku=sku,Pool=pool,Name=name,Native=native,Quantity=qty});}
            if(s.CompletedDays.ContainsKey("ms_scene_004")&&!Own(NormalPlayPolicy.BasicInspector))Add(NormalPlayPolicy.BasicInspector,"parts","基础器材检测仪480");
            var jobs=s.CampaignItems.Jobs.Values.Where(j=>!j.Ready&&j.Status is not ("cancelled" or "returned")).OrderBy(j=>j.RequestDay).ThenBy(j=>j.Id,StringComparer.Ordinal).ToArray();
            bool benchNeeded=jobs.Any(j=>j.Id!=PackageC.SynthJob&&j.Steps.Skip(j.Step).Any(step=>step.Station=="electrobench"));
            if(benchNeeded&&!Own("ms_machine_electrobench"))Add("ms_machine_electrobench","large","电声整备台1800");
            if(s.CompletedDays.ContainsKey("ms_scene_016")){
                string decoder=ResolveNative("decoder");if(!Own(decoder))Add(decoder,"large","原版密码解序器（原生报价）",true);
                if(!s.CompletedDays.ContainsKey("ms_scene_018")&&!all.Any(i=>Free(i)&&Cloth(i)))Add(ResolveNative("cloth"),"native_cloth","原版棉布",true);
                if(!s.CompletedDays.ContainsKey("ms_scene_017")&&!all.Any(i=>Free(i)&&Food(i)))Add(ResolveNative("food"),"native_food","一种原版食饮补给",true);
            }
            foreach(var job in jobs){
                var bound=job.Steps.Skip(job.Step).SelectMany(step=>step.Bound).ToHashSet();
                foreach(var requirement in job.Steps.Skip(job.Step).SelectMany(step=>step.Materials).GroupBy(p=>p.Key)){
                    string sku=requirement.Key;int available=all.Where(i=>i.identifier==sku&&!bound.Contains(i.uniqueId)&&(Free(i)||job.Resources.Any(r=>r.Entity==i.uniqueId&&r.State=="available"&&!r.Logical))).Sum(i=>Math.Max(1,i.unitCount));
                    int committed=job.Resources.Where(r=>(r.Sku==sku||r.Reference==sku)&&r.State=="leased"&&bound.Contains(r.Entity)).Select(r=>r.Entity).Distinct().Count();
                    int missing=NormalSupply.MissingUnits(requirement.Sum(p=>p.Value),available,committed);if(missing==0)continue;
                    var candidate=PackageBCatalog.Rows("supplier_candidates").Concat(PackageC.SupplierCandidates(s)).Concat(PackageDSupply.ExtraCandidates(s,"middle")).FirstOrDefault(r=>r["id"]==sku&&PackageBCatalog.Split(r["source_zones"]).Contains("middle"));
                    if(candidate==null||MusicCatalog.Find(sku) is MusicProduct p&&!MusicCatalog.Unlocked(s,p))continue;
                    string pool=candidate["pool"];var poolRow=PackageBCatalog.Rows("supplier_pools").First(r=>r["id"]==pool);
                    int minimum=PackageBCatalog.Number(poolRow["quantity_min"]),maximum=PackageBCatalog.Number(poolRow["quantity_max"]);
                    int qty=PackageBCatalog.Graded(sku)||sku=="ms_item_balanced_cable"?1:RuntimeCompat.Clamp(missing,minimum,maximum);
                    Add(sku,pool,MusicCatalog.Find(sku)?.Name??PackageBCatalog.Item(sku)?.GetValueOrDefault("name")??sku,false,qty);
                }
            }
            return result.ToArray();
        }
        internal string SupplyHint()
        {
            var needs=Needs();string wanted=needs.Length==0?"常规器材与带电电源。":"当前可优先补："+string.Join("、",needs.Take(3).Select(n=>n.Name))+"。";
            string limits="补货共用当日和每客额度，大型设备每次最多一台；标准电源实际"+host.Normal.Config.SupplierEnergyE+"E，按储电量计价。";
            var a=host.ReadState.Workshop.Advanced;
            if(a.CurrentVisit!=null&&a.Visits.TryGetValue(a.CurrentVisit,out var v)){
                if(v.PriorityBlocks.Values.Contains("daily_quota"))limits+="本日部分补货额度已用完，可另约后日。";
                if(v.Batches.Any(b=>b.Priority&&b.SkipReason=="skipped_space"))limits+="本次部分物件未放入卖家格位，可另约补货。";
                if(state?.PowerSpaceBlockedVisits.Contains(v.Id)==true)limits+="本次电源未放入卖家格位，可另约补货。";
            }
            return wanted+limits;
        }
    }
}
