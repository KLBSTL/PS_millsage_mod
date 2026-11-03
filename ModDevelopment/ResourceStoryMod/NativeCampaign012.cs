using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    // Scene 012 uses the existing repair furniture, paper reader, phone and counter.
    internal sealed class NativeCampaign012
    {
        private readonly Core core;
        private readonly NativeStoryHost host;
        private NativeMemberEconomy E=>host.Economy;
        private StoryState S=>host.ReadState;
        private CampaignNative012 N=>S?.CampaignItems?.Native012;
        private bool updating,selling;
        private string notice="";
        private float nextUpdate;
        internal bool Opted {get;}
        internal bool Enabled=>Opted&&host.Campaign.Enabled&&S?.CampaignItems?.Stage==2;
        internal bool NativeScene=>Enabled&&host.Engine.Frame()?.Scene.Id=="ms_scene_012";
        internal bool Pending=>Enabled&&N!=null&&PackageC.Native012Jobs(S).Any(j=>!j.Delivered);
        internal bool DuePhone=>Pending&&S.Economy.Business.Appointments.TryGetValue("ms_nagi",out var ap)&&ap.Status=="pending"&&ap.Day==host.Day;
        private sealed class Config {public int Version {get;set;}=1;public bool Enabled {get;set;}}
        internal NativeCampaign012(Core core,NativeStoryHost host)
        {
            this.core=core;this.host=host;
            string path=Path.Combine(core.DataRoot,"millsage.native-presentation.json");
            if(host.NormalPlay)Opted=true;
            else if(core.Test!=null)Opted=core.Test.Request.Native012!=""||core.Test.Request.OpeningMode!="";
            else if(File.Exists(path)){var config=JsonSerializer.Deserialize<Config>(File.ReadAllText(path),Core.Json);Opted=config?.Version==1&&config.Enabled;}
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(PlayerStore),"SellItem"),prefix:new HarmonyMethod(typeof(NativeCampaign012),nameof(Sell)));
        }
        internal bool IsGear(GameItem item)=>item?.identifier=="ms_nagi_whale_guitar";
        internal CampaignOriginal Original(GameItem item)=>Enabled&&item!=null?S.CampaignItems.Originals.Values.FirstOrDefault(r=>r.Entity==item.uniqueId&&PackageC.Native012Jobs(S,r.Role).Any()):null;
        internal bool Managed(GameItem item)=>N!=null&&Original(item) is {Custodian:"player",Location:"native"};
        internal bool Resource(GameItem item)=>Enabled&&N!=null&&item!=null&&PackageC.Native012Jobs(S).SelectMany(j=>j.Resources).Any(r=>r.Entity==item.uniqueId&&!r.Logical);
        internal bool? Accept(GameItem machine,string role,GameInventory inventory,GameItem item)
        {
            if(!Enabled||N==null)return null;
            if(role.StartsWith("slot:")&&Original(machine)!=null)return false;
            if(role=="host"&&Original(item)!=null)return Managed(item)&&E.PackageB.PlayerOwned(machine)&&inventory.childItems.Count==0;
            if(!Resource(item))return null;
            var resource=PackageC.Native012Jobs(S).SelectMany(j=>j.Resources).Single(r=>r.Entity==item.uniqueId);
            return E.PackageB.PlayerOwned(machine)&&resource.State is not ("installed" or "consumed" or "returned_to_customer")&&
                (role=="tools"&&resource.Tool||role=="parts"&&!resource.Tool);
        }
        internal void AfterStoryMutation()
        {
            if(!Enabled||!S.CampaignItems.Jobs.ContainsKey(PackageC.WhaleRepair))return;
            if(N==null)E.MutateNative(()=>host.Engine.EditCampaignItems(s=>PackageC.PrepareNative012(s,host.Day,host.Campaign)),()=>{});
            try{Issue();}catch(InvalidOperationException e){notice=e.Message;core.LoggerInstance.Warning("NATIVE012_INTAKE "+notice);}
        }
        private void Place(GameItem item)
        {
            GeneralHelper.SetItemOwned(item,true);item.EnableTag(MusicCatalog.ProtectedTag,false);
            if(!E.PlayerInventories().Any(inv=>NativePackageB.Place(inv,item)))throw new InvalidOperationException("请腾出店内库存，委托原件和供料仍完整保留。");
        }
        private void Issue()
        {
            if(N==null||E.IsTransferring)return;
            // A later return can reuse the guitar. Materialize it after the
            // current conversation so its new revision cannot stale that page.
            if(host.Return?.Enabled==true&&core.Presentation.Active)return;
            foreach(var r in S.CampaignItems.Originals.Values.Where(r=>PackageC.Native012Jobs(S,r.Role).Any()&&r.Custodian=="player"&&r.Location=="custody").ToArray())
                if(!host.Campaign.ClaimOriginal(r.Role))throw new InvalidOperationException("请为原件腾出库存空间。");
            var supplies=PackageC.Native012Jobs(S).SelectMany(j=>j.Resources).Where(r=>!r.Logical&&!N.Issued.Contains(r.Entity)&&r.State is "available" or "unfinished" or "available_output").ToArray();
            if(supplies.Length>0)E.MutateNative(()=>{
                foreach(var r in supplies){if(E.FindPlayerItem(r.Entity)!=null)throw new InvalidDataException("供料实体已存在");Place(NativeMemberEconomy.Decode(r.Snapshot));}
                host.Engine.EditCampaignItems(s=>{foreach(var r in supplies)s.CampaignItems.Native012.Issued.Add(r.Entity);});
            },()=>{});
            if(N.Note==0)E.MutateNative(()=>{var paper=DirectoryMaster.Item(NativePackageB.RepairNoteSku,true);Place(paper);host.Engine.EditCampaignItems(s=>s.CampaignItems.Native012.Note=paper.uniqueId);},()=>{});
        }
        internal string Paper(int entity)
        {
            if(N?.Note!=entity)return null;
            string branch=S.Flags.GetValueOrDefault("ms_route_commercial_chosen")?"先把原套弦装到舞台借用的备用琴上。爸爸留下的旧琴另行修复；两把琴分别检测、分别交回，借用关系照旧。":"请保留爸爸留下的这把旧琴，帮我装好高张力套弦和随附的稳压拾音模组。";
            bool returning=host.Package.CampaignProfile==PackageC.ReturnProfile&&PackageC.Native012Jobs(S).Any(j=>!j.Delivered&&j.TransferFrom!=null&&j.HostRole=="c_whale");
            if(returning){branch="这次请把备用琴上已经装好的同一套弦转回旧琴，不用另一套新弦。两把琴都留在店内，将旧琴放整备台器材位即可转移，不耗电、无需过夜；再用检测仪确认旧琴。备用琴随后按借件约定归还。";return "麻里奈姐：\n\n"+branch+"\n\n凪\n\n"+ProgressText();}
            string energy=string.Join("\n",PackageC.Native012Jobs(S).Select(j=>j.HostRole).Distinct().Select(role=>PackageC.Native012Jobs(S,role).First().Name+"："+PackageC.Native012Energy(S,role)+"E"));
            return "麻里奈姐：\n\n"+branch+"\n\n我把琴、耗材、配件和工具一起留在店里了。麻烦将吉他放入电声整备台的器材位；随附的耗材和未完成套弦放在配件区，螺丝刀和焊接工具装入整备台的工具箱。店里备好原版电源后，休息一晚即可完成加工。\n\n完工后请用检测仪确认当前配置，再打电话约我次日来店里取琴，工钱我们在柜台当面商量。工具届时也请一并归还，谢谢麻里奈姐。\n\n凪\n\n"+"本次备电：\n"+energy+"\n\n"+ProgressText();
        }
        internal string ProgressText()=>N==null?"":string.Join("\n",PackageC.Native012Jobs(S).Select(j=>j.Name+"："+(j.Delivered?"已交回":j.Ready?"已完成":j.Steps[j.Step].Name+"待完成")))+(notice==""?"":"\n"+notice);
        internal bool Stage(GameItem machine,GameItem gear)
        {
            if(!Managed(gear))return false;
            string role=Original(gear).Role;
            if(PackageC.Native012Jobs(S,role).All(j=>j.Ready||j.Steps[j.Step].Id=="test"))return true;
            host.Engine.EditCampaignItems(s=>s.CampaignItems.Native012.Plans[role]=new(){Machine=machine.uniqueId,Day=host.Day});
            return true;
        }
        private void Work(string role,CampaignNative012Plan plan)
        {
            var state=S;var original=state.CampaignItems.Originals[role];
            if(original.Custodian!="player"||original.Location!="native"||!E.PackageB.Working(plan.Machine,"ms_machine_electrobench"))return;
            var slots=E.PackageB.RepairWorkInventories(plan.Machine);
            if(!NativePackageB.ItemList(slots[0].childItems).Any(x=>x.uniqueId==original.Entity))throw new InvalidOperationException("原件已移出整备台，本次日结未加工。");
            var resources=PackageC.Native012Jobs(state,role).Where(j=>!j.Delivered&&!j.Ready).SelectMany(j=>j.Resources).Where(r=>!r.Logical&&r.State is "available" or "leased" or "unfinished" or "available_output").ToArray();
            foreach(var r in resources)if(!(r.Tool?E.PackageB.ToolsAt(plan.Machine):NativePackageB.ItemList(slots[1].childItems)).Any(x=>x.uniqueId==r.Entity))throw new InvalidOperationException("请放入这份委托所附的全部"+(r.Tool?"工具":"耗材和套弦")+"；没有扣除材料和电量。");
            foreach(var j in PackageC.Native012Jobs(state,role).Where(j=>!j.Ready&&j.TransferFrom!=null))if(E.FindPlayerItem(state.CampaignItems.Originals[j.TransferFrom].Entity)==null)throw new InvalidOperationException("原套弦转移还需要原来的那把琴在店内。");
            int cost=PackageC.Native012Energy(state,role);
            var power=E.AllPlayerItems().Where(x=>x.unitCount==1&&x.IsTag("power_source_item")&&E.PackageB.PlayerOwned(x)).OrderBy(x=>x.uniqueId).FirstOrDefault(x=>PowerHelper.GetAvailableEnergyFromItem(x,false)>=cost);
            if(cost>0&&power==null)throw new InvalidOperationException("原版电源电量不足，需要 "+cost+"E；没有扣料。");
            int before=power==null?0:PowerHelper.GetAvailableEnergyFromItem(power,false);
            E.MutateNative(()=>{
                if(cost>0&&!PowerHelper.DrawPowerSource(power,cost))throw new InvalidOperationException("实际电量扣除失败。");
                host.Engine.EditCampaignItems(s=>{foreach(var j in PackageC.Native012Jobs(s,role))foreach(var r in j.Resources.Where(x=>!x.Logical)) {var item=E.FindPlayerItem(r.Entity);if(item!=null)r.Snapshot=NativeMemberEconomy.Encode(item);}PackageC.WorkNative012(s,role,host.Day,power?.uniqueId??0,cost);s.CampaignItems.Native012.Plans.Remove(role);});
                foreach(var r in PackageC.Native012Jobs(S,role).SelectMany(j=>j.Resources).Where(r=>r.State=="consumed"&&!r.Logical)){var item=E.FindPlayerItem(r.Entity);item?.parentInventory?.Expel(item);}
                E.PackageB.SyncCampaign012Attachments();
            },()=>{if(power!=null)PowerHelper.SetPowerSourceAt(power,before);});
            notice="整备加工已完成。请取出吉他原件，用检测仪确认配置。";
        }
        internal bool Pump(GameItem machine,GameItem gear,bool detector)
        {
            if(!Managed(gear))return false;
            string role=Original(gear).Role;
            if(!detector){
                var jobs=PackageC.Native012Jobs(S,role).Where(j=>!j.Ready&&!j.Delivered).ToArray();
                if(host.Package.CampaignProfile==PackageC.ReturnProfile&&machine.identifier=="ms_machine_electrobench"&&E.PackageB.Working(machine.uniqueId,machine.identifier)&&jobs.Length>0&&jobs.All(j=>j.TransferFrom!=null&&j.Steps.Skip(j.Step).All(step=>step.Station=="counter"&&step.EnergyE==0))&&jobs.All(j=>E.FindPlayerItem(S.CampaignItems.Originals[j.TransferFrom].Entity)!=null)){
                    E.MutateNative(()=>{host.Engine.EditCampaignItems(s=>PackageC.WorkNative012(s,role,host.Day,0,0));E.PackageB.SyncCampaign012Attachments();},()=>{});notice="同一套弦已转回原件，不耗电、无需过夜；请用检测仪确认配置。";
                }
                return true;
            }
            if(PackageC.Native012Jobs(S,role).Any(j=>!j.Ready&&j.Steps[j.Step].Id!="test"))return true;
            if(N.Detected.GetValueOrDefault(role)==PackageC.ConfigSignature(Original(gear)))return true;
            E.MutateNative(()=>host.Engine.EditCampaignItems(s=>PackageC.DetectNative012(s,role,host.Day)),()=>{});
            notice="当前配置已检测合格，可以通过电话通知小凪明日来取琴了。";return true;
        }
        internal void Update()
        {
            if(!Enabled||N==null||updating||E.IsTransferring||UnityEngine.Time.unscaledTime<nextUpdate)return;
            nextUpdate=UnityEngine.Time.unscaledTime+.5f;updating=true;
            try{
                try{Issue();}catch(InvalidOperationException e){notice=e.Message;return;}
                foreach(var pair in N.Plans.Where(x=>x.Value.Day<host.Day).ToArray())try{Work(pair.Key,pair.Value);}catch(InvalidOperationException e){notice=e.Message;host.Engine.EditCampaignItems(s=>s.CampaignItems.Native012.Plans.Remove(pair.Key));host.Persist();}
                if(E.Business.CurrentActor=="ms_nagi"&&PlayerStore.instance.isClientArrived){var c=PlayerStore.instance.currentClientInstance.storeClient;if(c.clientBuyingIdList!=null&&!c.clientBuyingIdList.Contains("ms_nagi_whale_guitar"))c.clientBuyingIdList.Add("ms_nagi_whale_guitar");}
            }finally{updating=false;}
        }
        internal string CallBlock(string actor)
        {
            if(actor!="ms_nagi"||!Pending)return null;
            return PackageC.Native012CallBlock(S,host.Day);
        }
        internal string CallText()
        {
            var roles=PackageC.Native012Jobs(S).Where(j=>!j.Delivered).Select(j=>j.HostRole).Distinct().ToArray();
            var ready=roles.Where(r=>PackageC.Native012Block(S,r,host.Day)==null).ToArray();
            string names=string.Join("、",ready.Select(r=>r=="c_early_backup"?"备用琴":"爸爸的旧琴"));
            return names+"已经检测妥当了，我明天来店里柜台取琴，工钱到店里再当面商量吧。"+(ready.Length<roles.Length?"其余器材继续按约保管整备。":"");
        }
        internal bool Counter(GameItem item)=>Managed(item)&&PackageC.Native012Jobs(S,Original(item).Role).Any(j=>!j.Delivered)&&E.Business.CurrentActor=="ms_nagi"&&PlayerStore.instance.isClientArrived&&PackageC.Native012Block(S,Original(item).Role,host.Day)==null;
        internal bool Value(GameItem item,out int price){price=0;if(!Counter(item))return false;price=PackageC.Native012Price(S,Original(item).Role);return true;}
        private static bool Sell(GameItem __0)
        {
            var self=Core.Instance?.Story?.Native012;if(self==null||self.selling||!self.Counter(__0))return true;
            var store=PlayerStore.instance;int before=store.GetCash();string role=self.Original(__0).Role;
            self.E.MutateNative(()=>{
                self.selling=true;try{store.SellItem(__0);}finally{self.selling=false;}
                int paid=store.GetCash()-before;
                if(self.E.FindPlayerItem(__0.uniqueId)!=null&&GeneralHelper.IsItemOwned(self.E.FindPlayerItem(__0.uniqueId)))throw new InvalidOperationException("原版柜台尚未交回原件。");
                foreach(var r in PackageC.Native012Jobs(self.S,role).SelectMany(j=>j.Resources).Where(r=>!r.Logical&&r.State!="installed"&&r.State!="consumed")){
                    var item=self.E.FindPlayerItem(r.Entity);if(item==null)continue;item.parentInventory?.Expel(item);GeneralHelper.SetItemOwned(item,false);if(!store.currentClientInstance.gridInv.UncheckedAccept(item))throw new InvalidOperationException("物主工具未能一并交回。");
                }
                self.host.Engine.EditCampaignItems(s=>PackageC.DeliverNative012(s,__0.uniqueId,"ms_nagi",self.host.Day,paid,NativeMemberEconomy.Encode(__0)));
            },()=>{});return false;
        }
        internal void Notified(){if(Pending)host.Engine.EditCampaignItems(s=>s.CampaignItems.Native012.NotifiedDay=host.Day);}
        internal object Status()=>new{enabled=Enabled,pending=Pending,nativeScene=NativeScene,state=N,progress=N==null?null:ProgressText()};
    }
}
