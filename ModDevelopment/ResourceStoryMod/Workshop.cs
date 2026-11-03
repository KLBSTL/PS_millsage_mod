using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Security.Cryptography;

namespace ResourceStoryMod.Story
{
    public sealed class WorkshopProduct
    {
        public string Id,Name,Kind;
        public int Price,Width,Height;
    }
    public sealed class WorkshopRecipe
    {
        public string Id,Name,Station,Output;
        public int Quantity,Seconds,Energy;
        public Dictionary<string,int> Inputs=new(),Tools=new();
        public string Qualification="P0";
        public string[] Critical=Array.Empty<string>();
    }
    public static class WorkshopCatalog
    {
        public static readonly WorkshopProduct[] Products=PackageBCatalog.WorkshopProducts();
        public static readonly WorkshopRecipe[] Recipes=PackageBCatalog.Recipes();
        public static readonly string[] UpperQualificationItems={"ms_part_refurb_control_board","ms_part_isolation_transformer","ms_module_reference_input","ms_module_quiet_power","ms_module_tone_filter","ms_module_stable_bus","ms_module_balanced_out"};
        public static WorkshopProduct Product(string id){var p=Products.FirstOrDefault(x=>x.Id==id);if(p!=null)return p;var m=MusicCatalog.Find(id);return m==null?null:new WorkshopProduct{Id=m.Id,Name=m.Name,Kind=m.IsGear?"finished":"module",Price=m.Price,Width=m.Width,Height=m.Height};}
        public static WorkshopRecipe Recipe(string id)=>Recipes.FirstOrDefault(x=>x.Id==id);
    }
    public sealed class WorkshopEntity
    {
        public int Entity {get;set;}
        public string ItemId {get;set;}
        public string Reference {get;set;}
        public int Units {get;set;}
        public string Snapshot {get;set;}
        public bool Tool {get;set;}
        public string Grade {get;set;}="ordinary";
    }
    public sealed class WorkshopJob
    {
        public string Id {get;set;}
        public string Recipe {get;set;}
        public int RuleVersion {get;set;}=1;
        public WorkshopSpec Spec {get;set;}
        public string SpecHash {get;set;}
        public int Machine {get;set;}
        public int DurationMs {get;set;}
        public int EnergyE {get;set;}
        public int NativeCapacity {get;set;}
        public int ElapsedMs {get;set;}
        public int RepairDays {get;set;}
        public int RepairDaysWorked {get;set;}
        public int LastRepairDay {get;set;}=-1;
        public string Status {get;set;}="running";
        public string PauseReason {get;set;}
        public bool InputsConsumed {get;set;}
        public List<WorkshopEntity> Inputs {get;set;}=new();
        public List<WorkshopEntity> Outputs {get;set;}=new();
        public HashSet<int> Taken {get;set;}=new();
        public HashSet<int> BenchTools {get;set;}
        public Dictionary<int,decimal> EnergyBySource {get;set;}=new();
        public bool Terminal=>Status=="claimed" || Status=="cancelled";
    }
    public sealed class WorkshopState
    {
        public int Version {get;set;}=1;
        public Dictionary<string,WorkshopJob> Jobs {get;set;}=new();
        public Dictionary<int,decimal> NativeEnergyCredit {get;set;}=new();
        public HashSet<string> UpperPurchaseReceipts {get;set;}=new();
        public HashSet<int> BasicCalibrationReceipts {get;set;}=new();
        public bool Professional {get;set;}
        public PackageBState Advanced {get;set;}
    }
    public sealed class WorkshopPowerPlan
    {
        public int ElapsedMs,PhysicalDebit;
        public decimal Equivalent,NextCredit;
        public bool Powered;
    }
    public static class Workshop
    {
        public static bool Busy(WorkshopState state,int machine)=>state.Jobs.Values.Any(j=>j.Machine==machine && !j.Terminal);
        private static void Need(bool valid,string message){if(!valid)throw new InvalidOperationException(message);}
        public static bool Start(WorkshopState state,string id,string recipeId,int machine,int capacity,List<WorkshopEntity> inputs)
            =>StartFrozen(state,id,machine,capacity,inputs,PackageBCatalog.Spec(recipeId,"ordinary",inputs));
        public static string HashSpec(WorkshopSpec spec)=>RuntimeCompat.Hex(RuntimeCompat.Sha256(JsonSerializer.SerializeToUtf8Bytes(spec,StoryContent.Json)));
        public static bool StartFrozen(WorkshopState state,string id,int machine,int capacity,List<WorkshopEntity> inputs,WorkshopSpec recipe)
        {
            Need(StoryContent.ValidId(id),"无效工单号");
            string recipeId=recipe.Recipe;
            if(state.Jobs.TryGetValue(id,out var previous)) {
                Need(previous.Recipe==recipeId && previous.Machine==machine,"工单号已用于其他工作");return false;
            }
            Need(machine>=0 && (recipe.Station=="counter"?machine==0:machine>0),"工位不匹配");
            Need(!Busy(state,machine),"请先领取或取回这个工位的物品");
            Need(capacity>0 && capacity<=1000000,"能源容量尚未绑定");
            Need(inputs!=null && inputs.Select(x=>x.Entity).Distinct().Count()==inputs.Count,"输入实体重复或为空");
            foreach(var part in inputs)Need(part.Entity>0 && part.Units>0 && !string.IsNullOrWhiteSpace(part.Snapshot) && !string.IsNullOrEmpty(part.ItemId),"实际输入不完整");
            var expected=recipe.Inputs.Select(p=>(Key:p.Key,Tool:false,Count:p.Value)).Concat(recipe.Tools.Select(p=>(Key:p.Key,Tool:true,Count:p.Value))).ToArray();
            foreach(var p in expected)Need(inputs.Where(x=>x.Reference==p.Key && x.Tool==p.Tool).Sum(x=>x.Units)==p.Count,"投料/工具数量不符："+p.Key);
            Need(inputs.All(x=>expected.Any(e=>e.Key==x.Reference && e.Tool==x.Tool)),"包含无关物料");
            Need(recipe.DurationMs>0 && recipe.Energy>=0 && PackageBCatalog.Rank(recipe.Grade)>=0,"冻结工艺无效");
            state.Jobs.Add(id,new WorkshopJob{Id=id,Recipe=recipeId,RuleVersion=2,Spec=recipe,SpecHash=HashSpec(recipe),Machine=machine,DurationMs=recipe.DurationMs,EnergyE=recipe.Energy,NativeCapacity=capacity,Inputs=inputs});
            Validate(state);return true;
        }
        public static WorkshopPowerPlan Plan(WorkshopState state,WorkshopJob job,int milliseconds,int source,int available)
        {
            Need(milliseconds>=0 && milliseconds<=60000,"加工时间片不合法");
            var p=new WorkshopPowerPlan{ElapsedMs=Math.Min(job.DurationMs,checked(job.ElapsedMs+milliseconds))};
            decimal oldDue=(decimal)job.EnergyE*job.ElapsedMs/job.DurationMs*job.NativeCapacity/100;
            decimal newDue=(decimal)job.EnergyE*p.ElapsedMs/job.DurationMs*job.NativeCapacity/100;
            p.Equivalent=newDue-oldDue;
            decimal credit=state.NativeEnergyCredit.GetValueOrDefault(source);
            p.PhysicalDebit=checked((int)Math.Ceiling(Math.Max(0,p.Equivalent-credit)));
            p.Powered=job.EnergyE==0 || (source>0 && available>=p.PhysicalDebit);
            p.NextCredit=credit+p.PhysicalDebit-p.Equivalent;
            return p;
        }
        public static bool Advance(WorkshopState state,string id,int milliseconds,int source,int available,List<WorkshopEntity> completedOutput)
        {
            var job=state.Jobs[id];if(job.Status!="running" || milliseconds==0)return false;
            var p=Plan(state,job,milliseconds,source,available);
            if(!p.Powered){job.Status="paused";job.PauseReason="能源不足";return true;}
            if(p.ElapsedMs==job.ElapsedMs)return false;
            if(p.ElapsedMs==job.DurationMs) {
                var r=Frozen(job);
                bool batch=r.OutputBatch!=null&&r.Purpose=="d_salvage";
                Need(completedOutput!=null && completedOutput.Count==r.Quantity && completedOutput.All(x=>(batch?r.OutputBatch.ContainsKey(x.ItemId):x.ItemId==r.Output) && x.Units==1 && !x.Tool && (job.RuleVersion==1 || x.Grade==r.Grade)),"实际产物批次缺失或品质不符");
                if(batch)Need(r.OutputBatch.Values.Sum()==r.Quantity&&r.OutputBatch.All(p=>completedOutput.Count(x=>x.ItemId==p.Key)==p.Value),"拆解实际材料不匹配");
                Need(completedOutput.Select(x=>x.Entity).Distinct().Count()==r.Quantity,"产物身份重复");
                job.Outputs=completedOutput;job.Status="ready";
            }
            if(job.EnergyE>0) {
                state.NativeEnergyCredit[source]=p.NextCredit;
                job.EnergyBySource[source]=job.EnergyBySource.GetValueOrDefault(source)+p.Equivalent;
            }
            job.InputsConsumed=Frozen(job).Purpose=="d_salvage"?p.ElapsedMs==job.DurationMs:true;job.ElapsedMs=p.ElapsedMs;job.PauseReason=null;
            Validate(state);return true;
        }
        public static bool Pause(WorkshopState state,string id,string reason)
        {
            var job=state.Jobs[id];if(job.Status!="running")return false;
            job.Status="paused";job.PauseReason=reason;return true;
        }
        public static bool Resume(WorkshopState state,string id)
        {
            var job=state.Jobs[id];if(job.Status!="paused")return false;
            job.Status="running";job.PauseReason=null;return true;
        }
        public static bool Cancel(WorkshopState state,string id)
        {
            var job=state.Jobs[id];if(job.Status=="return_ready" || job.Status=="cancelled")return false;
            Need(job.Status=="running" || job.Status=="paused","成品已完成，请领取，不再退回原料");
            job.Status="return_ready";job.PauseReason=null;
            if(!Pending(job).Any())job.Status="cancelled";
            return true;
        }
        public static IEnumerable<WorkshopEntity> Pending(WorkshopJob job)
        {
            if(job.Status!="ready" && job.Status!="return_ready")yield break;
            foreach(var input in job.Inputs.Where(x=>x.Tool || (job.Status=="return_ready" && !job.InputsConsumed)))
                if(!job.Taken.Contains(input.Entity))yield return input;
            if(job.Status=="ready")foreach(var output in job.Outputs)if(!job.Taken.Contains(output.Entity))yield return output;
        }
        public static bool Take(WorkshopState state,string id,int entity)
        {
            var job=state.Jobs[id];if(job.Taken.Contains(entity))return false;
            Need(Pending(job).Any(x=>x.Entity==entity),"此实体不是待取物");job.Taken.Add(entity);
            if(!Pending(job).Any())job.Status=job.Status=="ready"?"claimed":"cancelled";
            Validate(state);return true;
        }
        public static bool RecordUpperPurchase(WorkshopState state,string receipt,string sku,bool actuallyPaid,bool upperVendor)
        {
            Need(StoryContent.ValidId(receipt),"采购回执无效");
            if(!actuallyPaid || !upperVendor || !WorkshopCatalog.UpperQualificationItems.Contains(sku))return false;
            return state.UpperPurchaseReceipts.Add(receipt);
        }
        public static bool Qualify(WorkshopState state,bool workingOwnedBench)
        {
            if(state.Professional || !workingOwnedBench || state.UpperPurchaseReceipts.Count==0 || state.BasicCalibrationReceipts.Count==0)return false;
            state.Professional=true;return true;
        }
        public static void Validate(WorkshopState state)
        {
            if(state==null || state.Version!=1 || state.Jobs==null || state.NativeEnergyCredit==null || state.UpperPurchaseReceipts==null || state.BasicCalibrationReceipts==null)throw new InvalidDataException("工坊存档版本或字段无效");
            var liveEntities=new HashSet<int>();var machines=new HashSet<int>();
            foreach(var pair in state.Jobs) {
                var j=pair.Value;var r=j==null?null:Frozen(j);
                if(j==null || r==null || j.Id!=pair.Key || !StoryContent.ValidId(j.Id) || j.RuleVersion<1 || j.RuleVersion>2 || (j.RuleVersion==2 && (j.Spec==null || j.SpecHash!=HashSpec(j.Spec))) || j.DurationMs!=r.DurationMs || j.EnergyE!=r.Energy || j.ElapsedMs<0 || j.ElapsedMs>j.DurationMs || j.NativeCapacity<=0 || j.Inputs==null || j.Outputs==null || j.Taken==null || j.EnergyBySource==null)throw new InvalidDataException("工坊作业损坏");
                if(j.RepairDays<0||j.RepairDays>3||j.RepairDaysWorked<0||j.RepairDaysWorked>j.RepairDays||j.RepairDays>0&&(j.LastRepairDay<0||r.Purpose is not ("service" or "d_salvage")))throw new InvalidDataException("维修日进度损坏；保留原档");
                if(!new[]{"running","paused","ready","claimed","return_ready","cancelled"}.Contains(j.Status) || j.InputsConsumed!=(r.Purpose=="d_salvage"?j.ElapsedMs==j.DurationMs:j.ElapsedMs>0))throw new InvalidDataException("工坊作业阶段不一致");
                if((j.Status=="ready" || j.Status=="claimed") && (j.ElapsedMs!=j.DurationMs || j.Outputs.Count!=r.Quantity))throw new InvalidDataException("产物与完成状态不一致");
                if(j.Outputs.Count>0 && j.ElapsedMs!=j.DurationMs)throw new InvalidDataException("未完工已有产物");
                if(!j.Terminal && !machines.Add(j.Machine))throw new InvalidDataException("同一工位存在两个作业");
                var all=j.Inputs.Concat(j.Outputs).ToArray();
                if(all.Select(x=>x.Entity).Distinct().Count()!=all.Length || all.Any(x=>x.Entity<=0 || x.Units<=0 || string.IsNullOrEmpty(x.ItemId) || string.IsNullOrWhiteSpace(x.Snapshot)) || j.Taken.Any(e=>!all.Any(x=>x.Entity==e)))throw new InvalidDataException("作业内物身份无效");
                foreach(var x in all.Where(x=>!j.Taken.Contains(x.Entity) && (!j.InputsConsumed || x.Tool || j.Outputs.Contains(x))))if(!liveEntities.Add(x.Entity))throw new InvalidDataException("同一实体在多份作业中重复保管");
                decimal due=(decimal)j.EnergyE*j.ElapsedMs/j.DurationMs*j.NativeCapacity/100;
                if(j.EnergyBySource.Any(p=>p.Key<=0 || p.Value<0) || Math.Abs(j.EnergyBySource.Values.Sum()-due)>0.00000000001m)throw new InvalidDataException("作业核销能源不守恒");
            }
            if(state.NativeEnergyCredit.Any(p=>p.Key<=0 || p.Value<0 || p.Value>=1))throw new InvalidDataException("能源信用不合法");
            if(state.Advanced!=null)PackageB.Validate(state.Advanced);
        }
        public static WorkshopSpec Frozen(WorkshopJob job)
        {
            if(job.RuleVersion==2)return job.Spec;
            var r=WorkshopCatalog.Recipe(job.Recipe);if(r==null || !new[]{"b15_01_sort_capacitor","b15_02_wire_bundle","b15_03_solder","b15_04_cleaner","b15_05_shielding","b15_21_build_bench"}.Contains(job.Recipe))throw new InvalidDataException("未知旧工艺");
            return new WorkshopSpec{Recipe=r.Id,Station=r.Station,DurationMs=r.Seconds*1000,Energy=r.Energy,Output=r.Output,Quantity=r.Quantity,Inputs=r.Inputs,Tools=r.Tools};
        }
    }
}
