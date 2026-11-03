using System;
using System.Collections.Generic;
using System.Linq;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageB
    {
        private int repairSettledDay=-1;
        internal void MeasureRepair(string service,string kind,int machine=0,string scope="basic")
        {
            var preview=host.Engine.Snapshot();
            if(kind.EndsWith("calibrate")){
                bool useBasic=scope=="basic"&&PackageDUse.ForService(preview,service)?.AcquisitionPaid.HasValue==true&&Working(machine,NormalPlayPolicy.BasicInspector);
                if(!useBasic&&!Working(machine,"ms_machine_calibrator"))throw new InvalidOperationException("请使用放在店铺外层的自有检测仪");
                if(scope=="precision"&&!ReferenceAvailable(machine))throw new InvalidOperationException("精密检测需要有效参考自检");
            }else if(!economy.AllPlayerItems().Any(x=>x.identifier=="magnifier"&&PlayerOwned(x)))throw new InvalidOperationException("需要实际放大镜进行检查");
            int oldSteps=preview.Workshop.Advanced.Services[service].Steps.Count;bool changed=PackageB.MeasureNow(preview,service,kind,machine,host.Day,scope);if(!changed&&oldSteps==preview.Workshop.Advanced.Services[service].Steps.Count)return;
            economy.MutateNative(()=>host.Engine.EditPackageB(s=>PackageB.MeasureNow(s,service,kind,machine,host.Day,scope)),()=>{});
            RefreshRepairViews();
        }
        internal void StartRepairPlan(string service,string job,int machine,IReadOnlyList<RepairOperation> operations,Dictionary<string,int[]> choices=null)
        {
            var state=host.Engine.Snapshot();
            var spec=PackageB.RepairBundleSpec(state,service,job,operations,out var plan);
            var custody=state.Workshop.Advanced.Services[service].Held.Values.Where(x=>!state.Workshop.Advanced.Services[service].EffectiveSlots.Values.Contains(x.Item.Entity)&&!x.DeliverToMember).Select(x=>NativeMemberEconomy.Decode(x.Item.Snapshot)).ToArray();
            var selected=choices==null?new Dictionary<string,int[]>():new Dictionary<string,int[]>(choices);
            foreach(var group in operations.Where(x=>x.Component>0).GroupBy(x=>state.Workshop.Advanced.Facts[x.Component].Sku))selected[group.Key]=group.Select(x=>x.Component).ToArray();
            economy.Workshop.StartPrepared(job,spec,machine,selected,custody,(s,stored)=>{
                var svc=s.Workshop.Advanced.Services[service];
                foreach(var item in stored){svc.Held.Remove(item.Entity);if(s.Workshop.Jobs[job].BenchTools?.Contains(item.Entity)!=true)s.Economy.OwnedItems.Remove(item.Entity);}
                int cost=stored.Where(x=>!x.Tool&&s.Workshop.Advanced.Facts.GetValueOrDefault(x.Entity)?.Owner=="player").Sum(x=>{var item=NativeMemberEconomy.Decode(x.Snapshot);return (PackageBCatalog.AllIds.Contains(item.identifier)?PackageB.LeafValue(s.Workshop.Advanced,item.uniqueId,item.identifier):Math.Max(0,(int)item.unitValue))*x.Units;});
                svc.Steps.Add(new ServiceStep{Id=job,Kind="ms_daily_repair",ConsumableQuote=cost,FactsAtStart=PackageB.FactsHash(s.Workshop.Advanced.Facts[svc.Host])});
                svc.Status="working";s.Workshop.Advanced.RepairPlans.Add(job,plan);PackageB.BindRepairDay(s.Workshop.Jobs[job],host.Day,plan.Days);
            });
            RefreshRepairViews();
        }
        internal void SettleRepairDay(int day)
        {
            foreach(string id in host.Engine.Snapshot().Workshop.Jobs.Values.Where(x=>x.RepairDays>0&&x.LastRepairDay<day&&x.Status is "running" or "paused").OrderBy(x=>x.Id).Select(x=>x.Id).ToArray()){
                var job=host.Engine.Snapshot().Workshop.Jobs[id];int ms=PackageB.RepairDayMilliseconds(job,day);bool advanced=false;
                if(ms>0&&job.PauseReason!="手动暂停"){
                    try{if(job.Status=="paused")economy.Workshop.Resume(id);int remaining=ms;while(remaining>0){int slice=Math.Min(60000,remaining);if(!economy.Workshop.Tick(id,slice,economy.Workshop.AutomaticSourceFor(id,slice),remaining==slice?day:-1))break;remaining-=slice;}advanced=remaining==0;}
                    catch(InvalidOperationException e){host.Engine.EditWorkshop(w=>{w.Jobs[id].Status="paused";w.Jobs[id].PauseReason=e.Message;});}
                }
                if(!advanced){host.Engine.EditWorkshop(w=>w.Jobs[id].LastRepairDay=day);host.Persist();}
            }
            RefreshRepairViews();
        }
        private void UpdateRepair()
        {
            int day=host.Day;
            if(day!=repairSettledDay){
                var legacy=host.Engine.Snapshot().Workshop.Jobs.Values.Where(x=>x.Spec?.Purpose=="service"&&x.RepairDays==0&&x.Status is "running" or "paused").Select(x=>x.Id).ToArray();
                if(legacy.Length>0){host.Engine.EditWorkshop(w=>{foreach(string id in legacy)PackageB.BindRepairDay(w.Jobs[id],day);});host.Persist();}
                SettleRepairDay(day);repairSettledDay=day;
            }
            UpdateRepairNative();
        }
        partial void RefreshRepairViews();
        partial void UpdateRepairNative();
    }
}
