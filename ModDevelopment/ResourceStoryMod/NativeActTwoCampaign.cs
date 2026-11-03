using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    // The second-act jobs use the existing paper, furniture, phone and counter.
    internal sealed class NativeActTwoCampaign
    {
        private readonly Core core;
        private readonly NativeStoryHost host;
        private NativeMemberEconomy E=>host.Economy;
        private StoryState S=>host.ReadState;
        private bool updating,selling;
        private float next;
        private string notice="",noticeRole;
        internal bool Enabled=>host.Native012.Enabled&&(core.Test==null||core.Test.Request.ActTwoExtension)&&S?.CampaignItems?.Stage==2;
        private IEnumerable<CampaignWork> Jobs=>PackageC.ActTwoJobs(S);
        internal IEnumerable<string> Roles=>Enabled?Jobs.Where(j=>!j.Delivered).Select(j=>j.HostRole).Distinct():Array.Empty<string>();
        internal bool Pending=>Enabled&&Roles.Any();
        internal bool PendingActor(string actor)=>Enabled&&Jobs.Any(j=>j.SupplyActor==actor&&!j.Delivered);
        internal bool DuePhone=>Enabled&&S.Economy.Business.Appointments.Any(p=>PendingActor(p.Key)&&p.Value.Status=="pending"&&p.Value.Day==host.Day);
        internal CampaignOriginal Original(GameItem item)=>Enabled&&item!=null?S.CampaignItems.Originals.Values.FirstOrDefault(r=>r.Entity==item.uniqueId&&PackageC.ActTwoJobs(S,r.Role).Any()):null;
        internal bool IsGear(GameItem item)=>Original(item)!=null;
        internal bool Managed(GameItem item)=>Original(item) is {Custodian:"player",Location:"native"}&&PackageC.ActTwoJobs(S,Original(item).Role).Any(j=>!j.Delivered);
        internal NativeActTwoCampaign(Core core,NativeStoryHost host)
        {
            this.core=core;this.host=host;core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(PlayerStore),"SellItem"),prefix:new HarmonyMethod(typeof(NativeActTwoCampaign),nameof(Sell)));
        }
        internal bool? Accept(GameItem machine,string role,GameInventory inventory,GameItem item)
        {
            if(!Enabled)return null;
            if(role.StartsWith("slot:")&&Original(machine)!=null)return false;
            if(role=="host"&&Original(item)!=null)return Managed(item)&&E.PackageB.PlayerOwned(machine)&&inventory.childItems.Count==0;
            var resource=Jobs.Where(j=>!j.Delivered).SelectMany(j=>j.Resources).FirstOrDefault(r=>!r.Logical&&r.Entity==item.uniqueId);if(resource==null)return null;
            return E.PackageB.PlayerOwned(machine)&&machine.identifier=="ms_machine_electrobench"&&(role=="tools"&&resource.Tool||role=="parts"&&!resource.Tool);
        }
        private CampaignNative012 Record(string actor)=>S.CampaignItems.NativeActTwo?.GetValueOrDefault(actor);
        private void Place(GameItem item)
        {
            GeneralHelper.SetItemOwned(item,true);item.EnableTag(MusicCatalog.ProtectedTag,false);
            if(!E.PlayerInventories().Any(inv=>NativePackageB.Place(inv,item)))throw new InvalidOperationException("请腾出库存，委托原件和材料仍完整保留。");
        }
        private void Issue()
        {
            foreach(string actor in Jobs.Where(j=>!j.Delivered).Select(j=>j.SupplyActor).Distinct().ToArray()){
                if(Record(actor)==null)host.Engine.EditCampaignItems(s=>PackageC.PrepareActTwo(s,actor));
                foreach(var r in S.CampaignItems.Originals.Values.Where(r=>r.Custodian=="player"&&r.Location=="custody"&&PackageC.ActTwoJobs(S,r.Role).Any(j=>j.SupplyActor==actor&&!j.Delivered)).ToArray())if(!host.Campaign.ClaimOriginal(r.Role))throw new InvalidOperationException("请为受托原件腾出库存。");
                var supplies=Jobs.Where(j=>j.SupplyActor==actor&&!j.Delivered).SelectMany(j=>j.Resources).Where(r=>!r.Logical&&!Record(actor).Issued.Contains(r.Entity)&&r.State is "available" or "unfinished" or "available_output").ToArray();
                if(supplies.Length>0)E.MutateNative(()=>{foreach(var r in supplies){if(E.FindPlayerItem(r.Entity)!=null)throw new InvalidOperationException("委托供料已存在，不能重复发放");Place(NativeMemberEconomy.Decode(r.Snapshot));}host.Engine.EditCampaignItems(s=>{foreach(var r in supplies)PackageC.PrepareActTwo(s,actor).Issued.Add(r.Entity);});},()=>{});
                if(Record(actor).Note==0)E.MutateNative(()=>{var paper=DirectoryMaster.Item(NativePackageB.RepairNoteSku,true);Place(paper);host.Engine.EditCampaignItems(s=>PackageC.PrepareActTwo(s,actor).Note=paper.uniqueId);},()=>{});
                foreach(var job in Jobs.Where(j=>j.SupplyActor==actor&&!j.Delivered&&(j.Id is PackageC.Hammer or PackageC.Mic)&&!S.CampaignItems.Receipts.Contains("act2.story_pickup."+j.Id)).ToArray())host.Engine.EditMemberBusiness(s=>PackageC.ScheduleActTwoPickup(s,job.Id,host.Day));
            }
        }
        internal string Progress(string actor)=>string.Join("\n",Jobs.Where(j=>j.SupplyActor==actor).Select(j=>j.Name+"："+(j.Delivered?"已交回":j.Ready?"已完成，待检测/交回":j.Steps[j.Step].Name+"待完成")))+(notice!=""&&Jobs.Any(j=>j.SupplyActor==actor&&j.HostRole==noticeRole&&!j.Delivered)?"\n"+notice:"");
        internal string Paper(int entity)
        {
            if(!Enabled||S.CampaignItems.NativeActTwo==null)return null;string actor=S.CampaignItems.NativeActTwo.FirstOrDefault(p=>p.Value.Note==entity).Key;if(actor==null)return null;
            string energy=string.Join("\n",Jobs.Where(j=>j.SupplyActor==actor).Select(j=>j.HostRole).Distinct().Select(role=>Jobs.First(j=>j.HostRole==role).Name+"："+PackageC.ActTwoEnergy(S,role)+"E"));
            return "麻里奈：\n\n请保留这次留下的同一件乐器和原有配件。将原件放入电声整备台器材位，随附套弦或配件放中间，工具装入右侧的工具箱。店内备好以下电量，睡觉过一天完成加工；随后用器材检测仪确认当前配置，不再扣电。\n\n"+energy+"\n\n还没有通用整备台时，可拨8670200预约中层器材供货，参考1800；已有480基础检测仪可继续使用，无须购买4200专业机。先正常营业、留足租金和电费，委托没有完成截止日。\n\n重锤改装和话筒按当面对话约好次日到店；若还没准备好，完工后可再电话约次日来取。套弦准备好后请电话联系。工钱到柜台照常商量，话筒沿用舞台借件的归还约定。\n\n"+E.Business.ActorNameForNote(actor)+"\n\n"+Progress(actor);
        }
        internal bool Stage(GameItem machine,GameItem gear)
        {
            if(!Managed(gear))return false;string role=Original(gear).Role;
            if(PackageC.ActTwoJobs(S,role).All(j=>j.Ready))return true;
            host.Engine.EditCampaignItems(s=>PackageC.PrepareActTwo(s,PackageC.ActTwoJobs(s,role).First().SupplyActor).Plans[role]=new(){Machine=machine.uniqueId,Day=host.Day});return true;
        }
        private void Work(string actor,string role,CampaignNative012Plan plan)
        {
            noticeRole=role;
            var original=S.CampaignItems.Originals[role];if(original.Custodian!="player"||original.Location!="native"||!E.PackageB.Working(plan.Machine,"ms_machine_electrobench"))return;
            var slots=E.PackageB.RepairWorkInventories(plan.Machine);if(!NativePackageB.ItemList(slots[0].childItems).Any(x=>x.uniqueId==original.Entity))throw new InvalidOperationException("原件已移出整备台，本次没有扣料或电。");
            var resources=PackageC.ActTwoJobs(S,role).Where(j=>!j.Delivered&&!j.Ready).SelectMany(j=>j.Resources).Where(r=>!r.Logical&&r.State is "available" or "leased" or "unfinished" or "available_output").ToArray();
            foreach(var r in resources)if(!(r.Tool?E.PackageB.ToolsAt(plan.Machine):NativePackageB.ItemList(slots[1].childItems)).Any(x=>x.uniqueId==r.Entity))throw new InvalidOperationException("请放入本单所附的全部配件、套弦和工具；没有扣料或电。");
            int cost=PackageC.ActTwoEnergy(S,role);var power=E.AllPlayerItems().Where(x=>x.unitCount==1&&x.IsTag("power_source_item")&&E.PackageB.PlayerOwned(x)).OrderBy(x=>x.uniqueId).FirstOrDefault(x=>PowerHelper.GetAvailableEnergyFromItem(x,false)>=cost);
            if(cost>0&&power==null)throw new InvalidOperationException("原版电源不足，需要 "+cost+"E；没有扣料。");int before=power==null?0:PowerHelper.GetAvailableEnergyFromItem(power,false);
            E.MutateNative(()=>{
                if(cost>0&&!PowerHelper.DrawPowerSource(power,cost))throw new InvalidOperationException("实际电量扣除失败");
                host.Engine.EditCampaignItems(s=>{foreach(var r in PackageC.ActTwoJobs(s,role).SelectMany(j=>j.Resources).Where(r=>!r.Logical)){var item=E.FindPlayerItem(r.Entity);if(item!=null)r.Snapshot=NativeMemberEconomy.Encode(item);}PackageC.WorkActTwo(s,role,host.Day,power?.uniqueId??0,cost);PackageC.PrepareActTwo(s,actor).Plans.Remove(role);});
                foreach(var r in PackageC.ActTwoJobs(S,role).SelectMany(j=>j.Resources).Where(r=>!r.Logical&&r.State=="consumed")){var item=E.FindPlayerItem(r.Entity);item?.parentInventory?.Expel(item);}E.PackageB.SyncCampaign012Attachments();
            },()=>{if(power!=null)PowerHelper.SetPowerSourceAt(power,before);});notice="加工完成，请用检测仪确认。";
        }
        internal bool Pump(GameItem machine,GameItem gear,bool detector)
        {
            if(!Managed(gear))return false;if(!detector)return true;string role=Original(gear).Role;noticeRole=role;
            try{host.Engine.EditCampaignItems(s=>PackageC.DetectActTwo(s,role,host.Day));notice="当前配置检测合格，可电话联系物主。";}catch(InvalidOperationException e){notice=e.Message;}return true;
        }
        internal void Update()
        {
            if(!Enabled||updating||E.IsTransferring||Time.unscaledTime<next)return;next=Time.unscaledTime+.5f;updating=true;
            try{try{if(core.Presentation.Active!=true)Issue();}catch(InvalidOperationException e){notice=e.Message;return;}
                if(S.CampaignItems.NativeActTwo==null)return;
                foreach(var pair in S.CampaignItems.NativeActTwo.ToArray())foreach(var plan in pair.Value.Plans.Where(p=>p.Value.Day<host.Day).ToArray())try{Work(pair.Key,plan.Key,plan.Value);}catch(InvalidOperationException e){notice=e.Message;host.Engine.EditCampaignItems(s=>s.CampaignItems.NativeActTwo[pair.Key].Plans.Remove(plan.Key));host.Persist();}
                var store=PlayerStore.instance;if(store?.isClientArrived==true&&E.Business.CurrentActor is string actor&&PendingActor(actor)){var client=store.currentClientInstance.storeClient;client.clientBuyingIdList??=new();foreach(var r in S.CampaignItems.Originals.Values.Where(r=>PackageC.ActTwoJobs(S,r.Role).Any(j=>j.SupplyActor==actor&&!j.Delivered)))if(!client.clientBuyingIdList.Contains(r.Sku))client.clientBuyingIdList.Add(r.Sku);}
            }finally{updating=false;}
        }
        internal string CallBlock(string actor)
        {
            if(!PendingActor(actor))return null;var blocks=Jobs.Where(j=>j.SupplyActor==actor&&!j.Delivered).Select(j=>PackageC.ActTwoBlock(S,j.HostRole,host.Day)).ToArray();return blocks.Any(b=>b==null)?null:blocks.FirstOrDefault();
        }
        internal string CallText(string actor)=>"器材已经完成并检测好，明天来柜台取；工钱到店再商量。";
        internal void Notified(string actor){if(PendingActor(actor))host.Engine.EditCampaignItems(s=>PackageC.PrepareActTwo(s,actor).NotifiedDay=host.Day);}
        internal bool Counter(GameItem item)=>Managed(item)&&PackageC.ActTwoJobs(S,Original(item).Role).Any(j=>j.SupplyActor==E.Business.CurrentActor)&&PlayerStore.instance.isClientArrived&&PackageC.ActTwoBlock(S,Original(item).Role,host.Day)==null;
        internal bool Value(GameItem item,out int value){value=0;if(!Counter(item))return false;value=PackageC.ActTwoJobs(S,Original(item).Role).Where(j=>!j.Delivered).Sum(j=>j.LaborReference);return true;}
        private static bool Sell(GameItem __0)
        {
            var self=Core.Instance?.Story?.ActTwo;if(self==null||self.selling||!self.Counter(__0))return true;var store=PlayerStore.instance;string actor=self.E.Business.CurrentActor,role=self.Original(__0).Role;int before=store.GetCash();
            self.E.MutateNative(()=>{self.selling=true;try{store.SellItem(__0);}finally{self.selling=false;}int paid=store.GetCash()-before;
                if(self.E.FindPlayerItem(__0.uniqueId)!=null&&GeneralHelper.IsItemOwned(self.E.FindPlayerItem(__0.uniqueId)))throw new InvalidOperationException("原版尚未交回这件原件");
                foreach(var r in PackageC.ActTwoJobs(self.S,role).SelectMany(j=>j.Resources).Where(r=>!r.Logical&&r.State is not ("installed" or "consumed"))){var item=self.E.FindPlayerItem(r.Entity);if(item==null)continue;item.parentInventory?.Expel(item);GeneralHelper.SetItemOwned(item,false);if(!store.currentClientInstance.gridInv.UncheckedAccept(item))throw new InvalidOperationException("物主工具未能一并交回");}
                self.host.Engine.EditCampaignItems(s=>PackageC.DeliverActTwo(s,__0.uniqueId,actor,self.host.Day,paid,NativeMemberEconomy.Encode(__0)));
            },()=>{});return false;
        }
    }
}
