using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ResourceStoryMod.Story
{
    public sealed class RepairOperation
    {
        public string Kind {get;set;}
        public string Slot {get;set;}
        public string Domain {get;set;}
        public int Component {get;set;}
    }
    public sealed class RepairPlan
    {
        public string Job {get;set;}
        public string Service {get;set;}
        public int Days {get;set;}
        public List<ServiceStep> Steps {get;set;}=new();
    }
    public sealed class RepairNote
    {
        public string Id {get;set;}
        public string Actor {get;set;}
        public int Host {get;set;}
        public int Entity {get;set;}
        public string Service {get;set;}
        public int Day {get;set;}
        public int NotifiedDay {get;set;}=-1;
        public string Demand {get;set;}
        public int PurchasePrice {get;set;}
        public string PurchaseReceipt {get;set;}
    }
    public static partial class PackageB
    {
        public static int RepairAcquisitionPrice(int value)=>RuntimeCompat.Clamp(value/20,60,300);
        public static Dictionary<string,string> RepairDemand(StoryState state,string actor,int host)
        {
            if(!state.Economy.Business.Instruments.TryGetValue(host,out var gear)||!state.Economy.Business.Needs.Values.Any(x=>x.Actor==actor&&x.FulfilledEntity>0))return null;
            return PackageBCatalog.Rows("member_demands").FirstOrDefault(x=>x["tier"]=="advanced"&&x["actor"]==actor&&x["host_id"]==gear.ItemId&&!state.Workshop.Advanced.FulfilledDemands.Contains(x["id"]));
        }
        public static string RepairDemandBlock(StoryState state,EquipmentService service)
        {
            var note=state.Workshop.Advanced.RepairNotes.Values.FirstOrDefault(x=>x.Service==service.Id);
            var d=note==null?RepairDemand(state,service.Actor,service.Host):PackageBCatalog.Rows("member_demands").FirstOrDefault(x=>x["id"]==note.Demand);if(d==null)return null;
            var a=state.Workshop.Advanced;var stats=Stats(a,state.Economy.Business,service.Host,service.EffectiveSlots);
            if(stats["noiseControl"]<PackageBCatalog.Number(d["noise_min"])||stats["stability"]<PackageBCatalog.Number(d["stability_min"])||stats["portability"]<PackageBCatalog.Number(d["portability_min"]))return "当前配置还没有满足需求单上的用途指标";
            var ids=service.EffectiveSlots.Values.Concat(service.Held.Values.Where(x=>x.DeliverToMember).Select(x=>x.Item.Entity)).Concat(state.Economy.OwnedItems.Values.Where(x=>x.ActorId==service.Actor).Select(x=>x.EntityId)).Distinct().ToArray();
            foreach(string sku in PackageBCatalog.Split(d["required_components"]))if(!ids.Any(id=>a.Facts.TryGetValue(id,out var f)&&f.Sku==sku&&PackageBCatalog.Rank(f.Grade)>=PackageBCatalog.Rank(d["minimum_component_grade"])))return "需求尚缺："+PackageDNext.Name(sku)+"（"+PackageBCatalog.GradeName(d["minimum_component_grade"])+"）";
            return null;
        }
        public static bool InstantMeasurement(string kind)=>kind!=null&&(kind.EndsWith("inspect_basic")||kind.EndsWith("inspect_detailed")||kind.EndsWith("calibrate"));
        public static int RepairDays(IEnumerable<RepairOperation> operations)
        {
            var list=operations.ToArray();return list.Length>=3||list.Any(x=>x.Slot?.StartsWith("service.")==true)?2:1;
        }
        public static void BindRepairDay(WorkshopJob job,int day,int days=1)
        {
            Need(day>=0&&days>=1&&days<=3,"维修日参数无效");
            if(job.RepairDays>0)return;
            job.RepairDays=days;job.RepairDaysWorked=Math.Min(days-1,(int)((long)job.ElapsedMs*days/job.DurationMs));job.LastRepairDay=day;
        }
        public static int RepairDayMilliseconds(WorkshopJob job,int day)
        {
            if(job.RepairDays<=0||day<=job.LastRepairDay||job.Status is not ("running" or "paused"))return 0;
            int target=(int)(((long)job.DurationMs*(job.RepairDaysWorked+1)+job.RepairDays-1)/job.RepairDays);
            return Math.Max(0,Math.Min(job.DurationMs,target)-job.ElapsedMs);
        }
        public static bool MeasureNow(StoryState state,string service,string kind,int machine,int day,string scope="basic")
        {
            Need(InstantMeasurement(kind),"这项工作需要维修日加工");
            var s=state.Workshop.Advanced.Services[service];var a=state.Workshop.Advanced;var b=state.Economy.Business;
            Need(!s.Terminal&&s.Delivery==null&&!s.Steps.Any(x=>x.Status=="running"),"先完成当前加工");
            if(kind.EndsWith("calibrate")){
                Need(new[]{"basic","passive","precision"}.Contains(scope),"检测范围无效");
                Need((a.Facts[s.Host].Sku=="ms_gear_drum_practice_pad")== (scope=="passive"),"鼓垫使用被动检测");
                if(scope=="precision")Need(ReferenceValid(a,machine)&&state.Workshop.Professional,"精密检测需要专业资格与有效参考自检");
                string hash=MeasurementHash(a,b,s.Host,scope,s.EffectiveSlots);
                if(a.Calibrations.TryGetValue(s.Host+":"+scope,out var old)&&old.Hash==hash){ReuseMeasurement(state,s,kind,scope,day);return false;}
            }else{
                scope=kind.EndsWith("inspect_detailed")?"detailed":"basic";
                string hash=InspectionHash(a,b,s.Host,scope,s.EffectiveSlots);
                if(a.Inspections.TryGetValue(s.Host+":"+scope,out var old)&&old.Hash==hash){ReuseMeasurement(state,s,kind,scope,day);return false;}
            }
            string id="measure_"+s.Host+"_"+scope+"_"+a.Ledger.Count;
            var step=new ServiceStep{Id=id,Kind=kind,Scope=scope,FactsAtStart=FactsHash(a.Facts[s.Host]),Labor=0};
            s.Steps.Add(step);CompleteStep(state,s,step,machine,day);return true;
        }
        private static void ReuseMeasurement(StoryState state,EquipmentService service,string kind,string scope,int day)
        {
            string id="reuse_"+service.Id+"_"+kind+"_"+scope;
            if(service.Steps.Any(x=>x.Status=="complete"&&(x.Id==id||x.Kind==kind&&x.Scope==scope)))return;
            var step=new ServiceStep{Id=id,Kind="ms_measurement_reuse",Scope=scope,Labor=0};service.Steps.Add(step);CompleteStep(state,service,step,0,day);
        }
        public static WorkshopSpec RepairBundleSpec(StoryState state,string service,string job,IReadOnlyList<RepairOperation> operations,out RepairPlan plan)
        {
            Need(operations!=null&&operations.Count>0&&operations.Count<=8,"请选择一至八项实际维修工作");
            var trial=JsonSerializer.Deserialize<StoryState>(JsonSerializer.Serialize(state,StoryContent.Json),StoryContent.Json);
            var svc=trial.Workshop.Advanced.Services[service];
            var combined=new WorkshopSpec{Recipe="ms_daily_repair",Purpose="service",Service=service,Station="electrobench",DurationMs=1000,Quantity=0,Energy=0};
            plan=new RepairPlan{Job=job,Service=service,Days=RepairDays(operations)};
            foreach(var operation in operations){
                Need(!InstantMeasurement(operation.Kind),"检测即时完成，不加入隔夜维修方案");
                var spec=ServiceSpec(trial,service,operation.Kind,operation.Domain,operation.Slot,operation.Component,"basic");
                foreach(var input in spec.Inputs)combined.Inputs[input.Key]=checked(combined.Inputs.GetValueOrDefault(input.Key)+input.Value);
                foreach(var tool in spec.Tools)combined.Tools[tool.Key]=Math.Max(combined.Tools.GetValueOrDefault(tool.Key),tool.Value);
                combined.Energy=checked(combined.Energy+spec.Energy);
                var row=PackageBCatalog.Rows("services_package_b").First(x=>x["id"]==operation.Kind);
                var step=new ServiceStep{Id=job+"_"+plan.Steps.Count,Kind=operation.Kind,Domain=operation.Domain,Slot=operation.Slot,Component=operation.Component,
                    ComponentOwner=operation.Component==0?null:trial.Workshop.Advanced.Facts[operation.Component].Owner,
                    Labor=PackageBCatalog.Number(row["labor_reference"]),PartsQuote=operation.Component==0?0:LeafValue(trial.Workshop.Advanced,operation.Component),FactsAtStart=FactsHash(trial.Workshop.Advanced.Facts[svc.Host])};
                // Simulate in an isolated state so subsequent operations see the proposed configuration.
                if(step.Component>0&&!svc.Held.ContainsKey(step.Component))svc.Held[step.Component]=new ServiceCustodyItem{Item=new WorkshopEntity{Entity=step.Component,ItemId=trial.Workshop.Advanced.Facts[step.Component].Sku,Units=1,Snapshot="preview"},Owner=step.ComponentOwner};
                svc.Steps.Add(step);CompleteStep(trial,svc,step,0,0);
                plan.Steps.Add(step);
            }
            foreach(var step in plan.Steps)step.Status="running";
            return combined;
        }
    }
}
