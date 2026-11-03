using System;
using System.Collections.Generic;
using System.Linq;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageB
    {
        internal void ReferenceJob(string job,int machine,string action,int reference=0)
        {
            if(host.Engine.Snapshot().Workshop.Jobs.TryGetValue(job,out var existing)){if(existing.Machine!=machine || existing.Spec?.Purpose!="reference_"+action)throw new InvalidOperationException("参考作业号与原操作不符");return;}
            if(!Working(machine,"ms_machine_calibrator"))throw new InvalidOperationException("标定台须在有效库存根");
            var a=A;var prior=a.References.GetValueOrDefault(machine);var spec=new WorkshopSpec{Recipe="reference_"+action,Purpose="reference_"+action,Station="calibrator",DurationMs=30000,Energy=4,Quantity=0};
            Dictionary<string,int[]> choices=null;
            if(action=="install"){
                if(prior?.Reference>0)throw new InvalidOperationException("先移除原参考输入");var item=economy.FindPlayerItem(reference)??throw new InvalidOperationException("参考件须是玩家实际持有物");
                if(item.identifier!="ms_module_reference_input" || !PlayerOwned(item))throw new InvalidOperationException("不是玩家自有的参考输入模组");Ensure(item);
                spec.Tools[item.identifier]=1;choices=new(){{item.identifier,new[]{reference}}};
            }else if(action=="remove"){
                if(prior?.Reference<=0 || prior==null)throw new InvalidOperationException("未安装参考件");spec.Output="ms_module_reference_input";spec.Quantity=1;spec.Grade=a.Facts[prior.Reference].Grade;
            }else if(action=="selfcheck"){
                if(PackageB.ReferenceHash(a,machine)==null)throw new InvalidOperationException("参考件缺失或不健康");if(PackageB.ReferenceValid(a,machine))throw new InvalidOperationException("同配置已有有效自检，无需重复耗电");
            }else throw new InvalidOperationException("未知参考操作");
            economy.Workshop.StartPrepared(job,spec,machine,choices);
        }
        internal WorkshopEntity ReferenceOutput(int machine)
        {
            var r=A.References[machine];return NativeWorkshop.Store(NativeMemberEconomy.Decode(r.Snapshot));
        }
        internal void FinishReferenceJob(string jobId)
        {
            host.Engine.EditPackageB(s=>{
                var j=s.Workshop.Jobs[jobId];var a=s.Workshop.Advanced;var purpose=j.Spec.Purpose;
                if(purpose=="reference_install"){
                    var input=j.Inputs.Single(x=>x.ItemId=="ms_module_reference_input");a.References[j.Machine]=new MachineReference{Machine=j.Machine,Reference=input.Entity,Snapshot=input.Snapshot};Workshop.Take(s.Workshop,jobId,input.Entity);
                }else if(purpose=="reference_remove"){
                    a.References[j.Machine]=new MachineReference{Machine=j.Machine};
                }else if(purpose=="reference_selfcheck"){
                    var hash=PackageB.ReferenceHash(a,j.Machine);if(hash==null)throw new InvalidOperationException("自检完成时参考配置已不健康");a.References[j.Machine].SelfCheckHash=hash;j.Status="claimed";
                }
                a.Ledger[jobId]=new WorkshopLedger{Id=jobId,Kind=purpose,Actor="player",Entity=j.Machine,Day=host.Day,Result="完成"};
            });
        }
        internal bool Qualify()
        {
            bool changed=false;host.Engine.EditWorkshop(w=>changed=Workshop.Qualify(w,economy.AllPlayerItems().Any(x=>Working(x.uniqueId,"ms_machine_electrobench")))) ;if(changed)host.Persist();return changed;
        }
    }
}
