using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    // The approved last act uses existing furniture, counter and telephone paths.
    internal sealed class NativeReturnCampaign
    {
        private readonly Core core;private readonly NativeStoryHost host;
        private NativeMemberEconomy E=>host.Economy;
        private StoryState S=>host.ReadState;
        private CampaignReturn N=>S?.CampaignItems?.Return;
        private bool updating,selling;private float next;private string notice="";
        internal bool Enabled=>host.Native012.Enabled&&host.Package.CampaignProfile==PackageC.ReturnProfile&&N!=null&&(core.Test==null||core.Test.Request.ReturnExtension);
        private IEnumerable<CampaignWork> Jobs=>Enabled?PackageC.ReturnJobs(S):Enumerable.Empty<CampaignWork>();
        internal bool Pending=>Jobs.Any(j=>!j.Delivered);
        internal bool FinalShop=>Enabled&&host.Package.Scenes.Any(s=>s.Id=="ms_scene_031"&&s.Context=="night")&&S.CompletedDays.ContainsKey("ms_scene_030")&&!S.CompletedDays.ContainsKey("ms_scene_031")&&(host.Engine.Frame()==null||host.Engine.Frame().Scene.Id=="ms_scene_031")&&PlayerStore.instance?.storeState==PlayerStore.StoreState.CLOSED&&!host.Integration.InVenue&&!MapUIManager.Instance.inTransit&&!MapUIManager.Instance.isOutside&&MapUIManager.Instance.Current==null;
        internal bool PendingActor(string actor)=>Jobs.Any(j=>j.SupplyActor==actor&&!j.Delivered&&j.Id!=PackageC.FinalTune);
        internal bool Trading=>Enabled&&host.Opening.VisitScene=="ms_scene_024"&&host.Engine.Frame()==null&&E.Business.HeldStoryActor=="ms_nagi";
        internal CampaignOriginal Original(GameItem item)=>Enabled&&item!=null?S.CampaignItems.Originals.Values.FirstOrDefault(r=>r.Entity==item.uniqueId):null;
        internal bool IsGear(GameItem item)=>Original(item)?.Role is "c_bass" or "c_whale" or "c_spinel" or "c_synth" or "c_drums" or "c_mic";
        internal bool Managed(GameItem item)=>IsGear(item)&&Original(item) is {Custodian:"player",Location:"native"}&&PackageC.ReturnManagesWork(S,Original(item).Role)&&!PackageC.Native012Jobs(S,Original(item).Role).Any(j=>!j.Ready);
        private CampaignWork ActiveRecording=>Jobs.FirstOrDefault(j=>!j.Ready&&!j.Delivered&&j.Id!=PackageC.Care);
        private static bool StageGear(string role)=>role is "c_bass" or "c_whale" or "c_spinel" or "c_synth" or "c_drums" or "c_mic";
        internal IEnumerable<GameInventory> StorageInventories(string role=null)
        {
            if(StageGear(role)&&S.CampaignItems.Originals.TryGetValue("c_stage_case",out var crate)&&crate.Location=="native")foreach(var inv in NativePackageB.Inventories(E.FindPlayerItem(crate.Entity)))yield return inv;
            foreach(var inv in E.PlayerInventories().Where(inv=>inv.NativeIdentity()!=EmporiumEntry.Instance.backInvinvElementCounter.NativeIdentity()))yield return inv;
        }
        internal bool? StorageAccept(GameInventory inventory,GameItem item)
        {
            if(NativePackageB.InventoryOwner(inventory)?.identifier!="ms_story_stage_transport_case")return null;
            if(NativeMemberEconomy.RestoringSnapshot||host.Engine==null)return PackageC.Definitions.Any(d=>StageGear(d.Role)&&d.Sku==item?.identifier);
            return Enabled&&Original(item) is {Custodian:"player"} r&&(r.Location=="native"||r.Location=="custody"&&E.IsTransferring)&&StageGear(r.Role);
        }
        internal NativeReturnCampaign(Core c,NativeStoryHost h)
        {
            core=c;host=h;c.HarmonyInstance.Patch(ReleaseApi.Method(typeof(PlayerStore),"SellItem"),prefix:new HarmonyMethod(typeof(NativeReturnCampaign),nameof(Sell)));c.HarmonyInstance.Patch(ReleaseApi.Method(typeof(PlayerStore),"EndDay"),prefix:new HarmonyMethod(typeof(NativeReturnCampaign),nameof(StowCounter)));
            foreach(string method in new[]{"MayActivateSlotItem","CanActivateSlotItem","MayToggleSlotItem","CanToggleSlotItem"})c.HarmonyInstance.Patch(ReleaseApi.Method(typeof(GameItem),method),prefix:new HarmonyMethod(typeof(NativeReturnCampaign),nameof(TinAllowed)));
        }
        private static bool TinAllowed(GameItem __instance,ref bool __result)
        {var self=Core.Instance?.Story?.Return;if(__instance?.identifier!="ms_hotaru_farewell_tin"||self?.Enabled!=true||self.N.TinRevealed)return true;__result=false;return false;}
        internal bool? Accept(GameItem machine,string role,GameInventory inventory,GameItem item)
        {
            if(!Enabled||item==null)return null;
            if(role.StartsWith("slot:")&&Managed(machine))return false;
            if(role=="host"&&IsGear(item)&&PackageC.ReturnManagesWork(S,Original(item).Role)&&!PackageC.Native012Jobs(S,Original(item).Role).Any(j=>!j.Ready))return Managed(item)&&E.PackageB.PlayerOwned(machine)&&inventory.childItems.Count==0;
            var resource=Jobs.Where(j=>!j.Delivered).SelectMany(j=>j.Resources).FirstOrDefault(r=>!r.Logical&&r.Entity==item.uniqueId);
            return resource==null?null:E.PackageB.PlayerOwned(machine)&&machine.identifier=="ms_machine_electrobench"&&(role=="tools"&&resource.Tool||role=="parts"&&!resource.Tool);
        }
        private void Place(GameItem item)
        {
            GeneralHelper.SetItemOwned(item,true);item.EnableTag(MusicCatalog.ProtectedTag,false);
            if(!StorageInventories().Any(inv=>NativePackageB.Place(inv,item)))throw new InvalidOperationException("请为受托原件、录音带和便条腾出可存档的店内库存。");
        }
        private void Issue()
        {
            foreach(var r in S.CampaignItems.Originals.Values.Where(r=>r.Custodian=="player"&&r.Location=="custody").OrderBy(r=>r.Role=="c_stage_case"?0:1).ToArray()){
                if(r.Role is "c_prelim_master" or "c_final_master"&&Jobs.Any(j=>j.HostRole==r.Role&&!j.Ready))continue;
                if(!host.Campaign.ClaimOriginal(r.Role))throw new InvalidOperationException("没有足够空间取出同一受托原件。");
            }
            var supplies=Jobs.Where(j=>!j.Delivered).SelectMany(j=>j.Resources).Where(r=>!r.Logical&&!N.Issued.Contains(r.Entity)&&r.State is "available" or "unfinished" or "available_output").ToArray();
            if(supplies.Length>0)E.MutateNative(()=>{foreach(var r in supplies){if(E.FindPlayerItem(r.Entity)!=null)throw new InvalidOperationException("供料已存在，不能重复发放。");Place(NativeMemberEconomy.Decode(r.Snapshot));}host.Engine.EditCampaignItems(s=>{foreach(var r in supplies)s.CampaignItems.Return.Issued.Add(r.Entity);});},()=>{});
            if(Pending&&N.Note==0)E.MutateNative(()=>{var paper=DirectoryMaster.Item(NativePackageB.RepairNoteSku,true);Place(paper);host.Engine.EditCampaignItems(s=>s.CampaignItems.Return.Note=paper.uniqueId);},()=>{});
            if(S.CampaignItems.Originals.TryGetValue("c_tin",out var tin)&&tin.Location=="native"&&!N.TinRevealed){
                var actual=E.FindPlayerItem(tin.Entity);var grid=NativePackageB.Inventories(actual).FirstOrDefault();
                foreach(string role in new[]{"c_keychain","c_score"}){
                    var r=S.CampaignItems.Originals[role];var item=E.FindPlayerItem(r.Entity);if(item==null||grid==null||item.parentInventory?.NativeIdentity()==grid.NativeIdentity())continue;
                    E.MutateNative(()=>{item.parentInventory?.Expel(item);if(!NativePackageB.Place(grid,item))throw new InvalidOperationException("封存铁盒无法容纳原来的挂件和谱页。");},()=>{});
                }
            }
        }
        internal string Progress=>(PackageC.Native012Jobs(S).Any(j=>!j.Delivered&&j.TransferFrom!=null&&j.HostRole=="c_whale")?"原套弦回装：两把琴都留店，旧琴放整备台即可把同一套弦转回旧琴；0E、无需过夜。再用检测仪确认，然后进行联调。\n":"")+string.Join("\n",Jobs.Select(j=>j.Name+"："+(j.Delivered?"已交回":j.Ready?"已完成，待柜台交回":"待放槽并过夜")))+(notice==""?"":"\n"+notice);
        internal void RestoreCounter()
        {
            if(!Enabled)return;
            foreach(var pair in N.CounterSnapshots.ToArray()){
                var r=S.CampaignItems.Originals[pair.Key];if(r.Location!="native"||r.Custodian!="player"||E.FindPlayerItem(r.Entity)!=null)continue;
                var actual=NativeMemberEconomy.Decode(pair.Value);if(actual.uniqueId!=r.Entity||actual.identifier!=r.Sku)throw new InvalidOperationException("临时柜台原件快照身份不符。");
                GeneralHelper.SetItemOwned(actual,true);
                if(!StorageInventories(r.Role).Concat(new[]{(GameInventory)EmporiumEntry.Instance.backInvinvElementCounter}).Any(inv=>NativePackageB.Place(inv,actual)))throw new InvalidOperationException("临时柜台原件仍保留在快照中，请先腾出库存。");
            }
        }
        internal void CaptureCounter()
        {
            if(!Enabled)return;var counter=NativePackageB.ItemList(EmporiumEntry.Instance.backInvinvElementCounter.childItems).Where(i=>GeneralHelper.IsItemOwned(i)&&Original(i) is {Custodian:"player",Location:"native"}).ToDictionary(i=>Original(i).Role,NativeMemberEconomy.Encode);
            if(counter.Count==N.CounterSnapshots.Count&&counter.All(p=>N.CounterSnapshots.GetValueOrDefault(p.Key)==p.Value))return;
            host.Engine.EditCampaignItems(s=>s.CampaignItems.Return.CounterSnapshots=counter);
        }
        private static bool StowCounter()
        {
            var self=Core.Instance?.Story?.Return;if(self?.Enabled!=true)return true;
            foreach(var item in NativePackageB.ItemList(EmporiumEntry.Instance.backInvinvElementCounter.childItems).Where(i=>GeneralHelper.IsItemOwned(i)&&self.Original(i)!=null).ToArray()){
                var source=item.parentInventory;if(!source.Expel(item))return false;
                if(self.StorageInventories(self.Original(item).Role).Any(inv=>NativePackageB.Place(inv,item)))continue;
                source.UncheckedAccept(item);self.notice="临时柜台上的原件尚未入库；请先腾出可保存库存，再睡觉。";return false;
            }return true;
        }
        internal string Paper(int entity)
        {
            if(!Enabled||N.Note!=entity)return null;
            string body;
            if(Jobs.Any(j=>j.Id==PackageC.FinalMaster&&!j.Delivered))body="决赛母带：同一合成器放电声整备台左侧，新空白带放中间。店内备18E电，睡一晚录制。\n\n母带与联调工钱合计参考520，在原柜台照常商量。已约第"+N.Appointments.GetValueOrDefault("final_pickup")+"天早上取件；若延期，完工后电话重约次日。";
            else if(Jobs.Any(j=>j.Id==PackageC.FinalTune&&!j.Delivered))body="决赛联调：用器材检测仪逐一确认六件舞台原件的当前配置，再把合成器放电声整备台左侧，其余原件留在店内。\n\n店内备12E电，睡一晚完成联调；我们约好次日下午再来确认录音安排。原有配件和借件都请保留。";
            else if(Jobs.Any(j=>j.Id==PackageC.Prelim&&!j.Delivered))body="初赛母带：同一合成器放电声整备台左侧，随附空白带放中间。店内备12E电，睡一晚录制；已约次日交给茉幌，工钱参考180，合成器一并归还。\n\n若延期，完工后通过电话另约次日来取。";
            else if(Jobs.Any(j=>j.Id==PackageC.Care&&!j.Delivered))body="小凪的临赛护理免费：用器材检测仪对同一把旧琴即时确认，不耗电、不用睡觉，再到原柜台交回。";
            else body="本次工单已交回。寄存物与原有配件照当面约定保管；后续委托到时再确认。";
            if(S.CampaignItems.Originals.ContainsKey("c_tin")&&!N.TinRevealed)body+="\n\n已寄存的铁盒与苹果请照原约保管，赛后再揭示铁盒。";
            return "麻里奈：\n\n"+body+"\n\n茉幌\n\n"+Progress;
        }
        internal string MachineText(GameItem item)=>Managed(item)?"比赛筹备委托：保留原件与已装部件。\n"+Progress+"\n检测即时完成；整备台按游戏日加工，不使用现实时间。":null;
        internal bool Stage(GameItem machine,GameItem gear)
        {
            if(!Managed(gear))return false;
            var job=ActiveRecording;if(gear.uniqueId!=S.CampaignItems.Originals["c_synth"].Entity||job==null)return true;
            host.Engine.EditCampaignItems(s=>PackageC.ReturnPlan(s,job.Id,machine.uniqueId,host.Day));return true;
        }
        internal bool Pump(GameItem machine,GameItem gear,bool detector)
        {
            if(!Managed(gear))return false;if(!detector)return true;var r=Original(gear);string sig=PackageC.ConfigSignature(r);
            if(N.Inspected.GetValueOrDefault(r.Role)!=sig||r.Role=="c_whale"&&Jobs.Any(j=>j.Id==PackageC.Care&&!j.Ready))try{host.Engine.EditCampaignItems(s=>PackageC.ReturnInspect(s,r.Role,r.Entity,sig,host.Day,machine.uniqueId));notice="已确认同一原件的当前配置。";}catch(InvalidOperationException e){notice=e.Message;}
            return true;
        }
        private void Work(string id,CampaignNative012Plan plan)
        {
            var job=S.CampaignItems.Jobs[id];if(job.Ready)return;
            var synth=S.CampaignItems.Originals["c_synth"];if(synth.Location!="native"||!E.PackageB.Working(plan.Machine,"ms_machine_electrobench"))throw new InvalidOperationException("整备台或合成器已移走；没有扣料、电。");
            var slots=E.PackageB.RepairWorkInventories(plan.Machine);if(!NativePackageB.ItemList(slots[0].childItems).Any(x=>x.uniqueId==synth.Entity))throw new InvalidOperationException("同一合成器未在器材槽；没有扣料、电。");
            var resources=job.Resources.Where(r=>!r.Logical&&r.State is "available" or "leased" or "unfinished" or "available_output").ToArray();
            foreach(var r in resources)if(!(r.Tool?E.PackageB.ToolsAt(plan.Machine):NativePackageB.ItemList(slots[1].childItems)).Any(x=>x.uniqueId==r.Entity))throw new InvalidOperationException("本次工单供料没有放进相应槽位；没有扣料、电。");
            int cost=job.Steps.Skip(job.Step).Sum(x=>x.EnergyE);var power=E.AllPlayerItems().Where(x=>x.unitCount==1&&x.IsTag("power_source_item")&&E.PackageB.PlayerOwned(x)).OrderBy(x=>x.uniqueId).FirstOrDefault(x=>PowerHelper.GetAvailableEnergyFromItem(x,false)>=cost);
            if(power==null)throw new InvalidOperationException("请备妥至少 "+cost+"E的原版电源；本次未扣料。");int before=PowerHelper.GetAvailableEnergyFromItem(power,false);
            E.MutateNative(()=>{
                if(!PowerHelper.DrawPowerSource(power,cost))throw new InvalidOperationException("原版电源扣电失败。");
                host.Engine.EditCampaignItems(s=>{foreach(var r in s.CampaignItems.Jobs[id].Resources.Where(r=>!r.Logical)){var actual=E.FindPlayerItem(r.Entity);if(actual!=null)r.Snapshot=NativeMemberEconomy.Encode(actual);}PackageC.ReturnWork(s,id,plan.Machine,power.uniqueId,host.Day,cost);s.CampaignItems.Return.Plans.Remove(id);});
                foreach(var r in S.CampaignItems.Jobs[id].Resources.Where(r=>!r.Logical&&r.State=="consumed")){var actual=E.FindPlayerItem(r.Entity);actual?.parentInventory?.Expel(actual);}E.PackageB.SyncCampaign012Attachments();
            },()=>PowerHelper.SetPowerSourceAt(power,before));notice="过夜加工完成。";
        }
        internal void Update()
        {
            if(!Enabled||updating||E.IsTransferring||Time.unscaledTime<next)return;next=Time.unscaledTime+.5f;updating=true;
            try{
                if(core.Presentation.Active)return;
                try{Issue();}catch(InvalidOperationException e){notice=e.Message;return;}
                foreach(var plan in N.Plans.Where(p=>p.Value.Day<host.Day).ToArray())try{Work(plan.Key,plan.Value);}catch(InvalidOperationException e){notice=e.Message;host.Engine.EditCampaignItems(s=>s.CampaignItems.Return.Plans.Remove(plan.Key));host.Persist();}
                var store=PlayerStore.instance;var client=store?.currentClientInstance?.storeClient;if(client!=null&&store.isClientArrived&&E.Business.CurrentActor is string actor){client.clientBuyingIdList??=new();foreach(var j in Jobs.Where(j=>j.SupplyActor==actor&&!j.Delivered&&j.Id!=PackageC.FinalTune)){string sku=S.CampaignItems.Originals[j.HostRole].Sku;if(!client.clientBuyingIdList.Contains(sku))client.clientBuyingIdList.Add(sku);}}
                if(Trading&&Jobs.Any(j=>j.Id==PackageC.Care&&j.Delivered)){E.Business.Close();PlayerStore.instance.DismissCurrentClient(false,false,true);}
            }finally{updating=false;}
        }
        internal bool CompleteVisit(string scene)
        {
            if(!Enabled||scene!="ms_scene_024"||!Jobs.Any(j=>j.Id==PackageC.Care&&!j.Delivered))return false;
            E.Business.HoldStoryTrade("ms_nagi");return true;
        }
        internal string CallBlock(string actor)=>PendingActor(actor)&&!Jobs.Any(j=>j.SupplyActor==actor&&j.Id!=PackageC.FinalTune&&!j.Delivered&&j.Ready)?"请先完成本次母带或免费护理，再电话约次日取件。":null;
        internal string CallText(string actor)=>"母带已准备好，明天到店按原柜台报价取件；乐器仍保留原有配件。";
        internal void Notified(string actor){string key=Enabled?PackageC.ReturnNotificationKey(S,actor):null;if(key!=null)host.Engine.EditCampaignItems(s=>s.CampaignItems.Return.Appointments[key]=host.Day+1);}
        private CampaignWork Sale(GameItem item)=>Enabled&&item!=null?Jobs.FirstOrDefault(j=>j.Id!=PackageC.FinalTune&&!j.Delivered&&j.Ready&&j.SupplyActor==E.Business.CurrentActor&&S.CampaignItems.Originals[j.HostRole].Entity==item.uniqueId&&S.CampaignItems.Originals[j.HostRole] is {Custodian:"player",Location:"native"}):null;
        internal bool Counter(GameItem item)=>PlayerStore.instance?.isClientArrived==true&&Sale(item)!=null;
        internal bool Value(GameItem item,out int value){value=0;var j=Counter(item)?Sale(item):null;if(j==null)return false;value=j.LaborReference+(j.Id==PackageC.FinalMaster?S.CampaignItems.Jobs[PackageC.FinalTune].LaborReference:0);return true;}
        private static bool Sell(GameItem __0)
        {
            var self=Core.Instance?.Story?.Return;if(self==null||self.selling||!self.Counter(__0))return true;var store=PlayerStore.instance;string actor=self.E.Business.CurrentActor,id=self.Sale(__0).Id;int before=store.GetCash();
            self.E.MutateNative(()=>{self.selling=true;try{store.SellItem(__0);}finally{self.selling=false;}int paid=store.GetCash()-before;
                var remaining=self.E.FindPlayerItem(__0.uniqueId);if(remaining!=null&&GeneralHelper.IsItemOwned(remaining))throw new InvalidOperationException("原版尚未交回这件实物。");
                self.host.Engine.EditCampaignItems(s=>PackageC.ReturnDeliver(s,id,__0.uniqueId,actor,paid,NativeMemberEconomy.Encode(__0),self.host.Day,self.host.Campaign));
            },()=>{});return false;
        }
    }
}
