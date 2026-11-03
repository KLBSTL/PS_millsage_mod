using System;
using System.Collections.Generic;
using System.Linq;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    internal sealed partial class NativeOpeningCampaign
    {
        private readonly Core core;private readonly NativeStoryHost host;
        private NativeMemberEconomy E=>host.Economy;
        private StoryState S=>host.ReadState;
        private CampaignOpening O=>S?.CampaignItems?.Opening;
        private string notice="";private float next;
        internal string Notice=>notice;
        internal bool Enabled=>host.Native012.Enabled;
        internal bool NativeScene=>Enabled&&PackageC.OpeningScene(host.Engine?.Frame()?.Scene.Id);
        internal string VisitScene {get{string id=PlayerStore.instance?.currentClientInstance?.storeClient?.identifier;return id?.StartsWith(host.Integration.Prefix)==true?id.Substring(host.Integration.Prefix.Length):null;}}
        internal bool OwnVisit=>Enabled&&PackageC.OpeningScene(VisitScene);
        internal bool Trading=>OwnVisit&&VisitScene!="ms_scene_004"&&host.Engine.Frame()==null&&E.Business.CurrentActor==PackageC.OpeningActor(VisitScene)&&PlayerStore.instance.isClientArrived;
        internal NativeOpeningCampaign(Core core,NativeStoryHost host){this.core=core;this.host=host;InstallTrade();}
        internal bool IsGear(GameItem item)=>Enabled&&item?.identifier=="ms_gear_synth_damaged";
        internal bool Managed(GameItem item)=>Enabled&&O!=null&&item!=null&&S.CampaignItems.Originals.TryGetValue("c_synth",out var r)&&r.Entity==item.uniqueId&&r.Custodian=="player"&&r.Location=="native";
        internal bool Portable(GameItem item)=>host.NormalPlay&&Managed(item)&&!S.CompletedDays.ContainsKey("ms_scene_006");
        internal void AfterStoryMutation()
        {
            if(!Enabled)return;
            if(O==null&&NativeScene)E.MutateNative(()=>host.Engine.EditCampaignItems(s=>s.CampaignItems.Opening=new()),()=>{});
            if(O==null)return;
            IssueSynth();
        }
        private void Place(GameItem item)
        {
            GeneralHelper.SetItemOwned(item,true);item.EnableTag(MusicCatalog.ProtectedTag,false);
            if(!E.PlayerInventories().Any(inv=>NativePackageB.Place(inv,item)))throw new InvalidOperationException("请腾出库存空间，原件与便条仍保留。");
        }
        private void IssueSynth()
        {
            if(O==null||E.IsTransferring)return;
            var c=S.CampaignItems;
            if(host.Engine.Frame()?.Scene.Id=="ms_scene_004"||c.Jobs.ContainsKey(PackageC.SynthJob)&&!S.CompletedDays.ContainsKey("ms_scene_006"))
                foreach(var r in c.Originals.Values.Where(x=>new[]{"c_synth","c_pick","c_flyer"}.Contains(x.Role)&&x.Custodian=="player"&&x.Location=="custody").ToArray())
                    if(!host.Campaign.ClaimOriginal(r.Role))throw new InvalidOperationException("请为同一件剧情原件腾出空间。");
            if(!c.Jobs.TryGetValue(PackageC.SynthJob,out var job))return;
            var resources=job.Resources.Where(r=>!r.Logical&&!O.Issued.Contains(r.Entity)&&r.State is "available" or "unfinished").ToArray();
            if(resources.Length>0)E.MutateNative(()=>{foreach(var r in resources){if(E.FindPlayerItem(r.Entity)!=null)throw new InvalidOperationException("供料已经在库存中");Place(NativeMemberEconomy.Decode(r.Snapshot));}host.Engine.EditCampaignItems(s=>{foreach(var r in resources)s.CampaignItems.Opening.Issued.Add(r.Entity);});},()=>{});
            IssueNote("ms_scene_004");
        }
        private void IssueNote(string scene)
        {
            if(O.Notes.ContainsKey(scene))return;
            E.MutateNative(()=>{var item=DirectoryMaster.Item(NativePackageB.RepairNoteSku,true);Place(item);host.Engine.EditCampaignItems(s=>s.CampaignItems.Opening.Notes.Add(scene,item.uniqueId));},()=>{});
        }
        internal string Paper(int entity,out string title)
        {
            title=null;if(O==null)return null;string scene=O.Notes.FirstOrDefault(p=>p.Value==entity).Key;if(scene==null)return null;
            title=scene=="ms_scene_004"?"茉幌留下的合成器便条":"购物便条";
            if(scene=="ms_scene_004")return "麻里奈姐：\n\n合成器留在店里。"+(host.NormalPlay?"打开合成器自身的检修窗即可处理首份委托，也可以放进电声整备台器材位。":"把它放进电声整备台器材位。")+"清洁剂、电容和焊料放配件区，螺丝刀、焊接工具装入工具箱（使用合成器检修位时放工具区）；备好20E电源，睡觉过一天。\n\n修好后用器材检测仪确认，不再扣电。请打电话告诉我，次日来试音。拨片和传单由我们收好了。\n\n茉幌\n\n"+Progress+(host.NormalPlay?"\n\n器材获取：电话簿的“中层器材供货”（8670200）可预约次日带货；基础检测仪480，电源照柜台报价。首份委托不必先买1800整备台，通用整备以后再购。无需预付款，委托没有交件截止日。":"");
            return scene switch{"ms_scene_001"=>"麻里奈姐：\n\n我还想看看饮用水和方便带走的食物。柜台上有什么合适的，就照平常商量价钱。\n\n茉幌","ms_scene_002"=>"店长：\n\n我想看看方便带走的食物和饮料。这几份是兼职店里发的员工餐，你要的话就照柜台报价收吧。\n\n枣",_=>"麻里奈姐姐：\n\n我想看看小馒头饼干和保温灯。合适的东西请让我在柜台看看吧。\n\n萤"};
        }
        internal string Progress=>O==null?"":S.CompletedDays.ContainsKey("ms_scene_006")?"同一台合成器已按乐队授权交由萤使用。":S.CompletedDays.ContainsKey("ms_scene_005")?"试音已完成，原件留店等待集结。":host.ActOne.WaitingForDays?"试音已完成。保留合成器，数日后朋花和萤会再来店里。":host.Engine.Frame()?.Scene.Id=="ms_scene_005"?"正在店内试音，保留同一台合成器。":O.Detected?"基础检测合格，保留原件等待次日试音。":O.TestPower>0?"清理与电容更换已完成，请用检测仪确认。":"请将原件、所附耗材和工具放入整备台，备电后睡觉加工。";
        internal bool? Accept(GameItem machine,string role,GameInventory inv,GameItem item)
        {
            if(!Enabled||O==null)return null;
            if(Portable(machine)&&role is "parts" or "tools"){
                var resource=S.CampaignItems.Jobs.GetValueOrDefault(PackageC.SynthJob)?.Resources.FirstOrDefault(x=>x.Entity==item?.uniqueId);
                return O.TestPower==0&&resource!=null&&O.Issued.Contains(resource.Entity)&&resource.State is "available" or "leased" or "unfinished"&&(role=="tools")==resource.Tool;
            }
            if(role.StartsWith("slot:")&&Managed(machine))return false;
            if(role=="host"&&Managed(item))return E.PackageB.PlayerOwned(machine)&&inv.childItems.Count==0;
            var r=S.CampaignItems.Jobs.GetValueOrDefault(PackageC.SynthJob)?.Resources.FirstOrDefault(x=>x.Entity==item?.uniqueId);
            if(r==null||!O.Issued.Contains(r.Entity))return null;
            return E.PackageB.PlayerOwned(machine)&&r.State is not ("consumed" or "returned_to_customer")&&(role=="tools"&&r.Tool||role=="parts"&&!r.Tool);
        }
        internal bool Stage(GameItem machine,GameItem gear)
        {
            if(!Managed(gear))return false;if(O.TestPower>0||S.CampaignItems.Jobs[PackageC.SynthJob].Ready)return true;
            host.Engine.EditCampaignItems(s=>{s.CampaignItems.Opening.Machine=machine.uniqueId;s.CampaignItems.Opening.PlannedDay=host.Day;});return true;
        }
        internal bool Pump(GameItem machine,GameItem gear,bool detector)
        {
            if(!Managed(gear))return false;if(!detector||O.TestPower==0||O.Detected)return true;
            E.MutateNative(()=>{
                if(Portable(gear)){
                    var tools=E.PackageB.RepairWorkInventories(gear.uniqueId)[2];
                    foreach(var r in S.CampaignItems.Jobs[PackageC.SynthJob].Resources.Where(x=>x.Tool)){
                        var tool=NativePackageB.ItemList(tools.childItems).FirstOrDefault(x=>x.uniqueId==r.Entity);if(tool==null)continue;
                        if(!tools.Expel(tool)||!E.PlayerInventories().Any(inv=>NativePackageB.Place(inv,tool)))throw new InvalidOperationException("请腾出空间取回检修工具，再完成检测。");
                    }
                }
                host.Engine.EditCampaignItems(s=>PackageC.DetectOpeningSynth(s,host.Day));
            },()=>{});return true;
        }
        internal string CallBlock(string actor)=>Enabled&&O!=null&&actor=="ms_mahoro"&&S.CampaignItems.Jobs.ContainsKey(PackageC.SynthJob)&&!S.CompletedDays.ContainsKey("ms_scene_005")&&!O.Detected?(host.NormalPlay?"先用合成器自身检修槽或整备台过夜加工，再用检测仪确认。":"先用整备台完成过夜加工，再用检测仪确认合成器。"):null;
        internal bool ReadyCall=>Enabled&&O?.Detected==true&&!S.CompletedDays.ContainsKey("ms_scene_005")&&host.Engine.Frame()?.Scene.Id!="ms_scene_005";
        internal void Update()
        {
            if(!Enabled||O==null||E.IsTransferring||Time.unscaledTime<next)return;next=Time.unscaledTime+.5f;
            try{
                if(OwnVisit&&host.Engine.Frame()==null&&S.CompletedDays.ContainsKey(VisitScene)&&E.Business.HeldStoryActor==null)E.Business.AfterStory(VisitScene);
                IssueSynth();if(Trading){IssueNote(VisitScene);UpdateTrade();}
                if(O.Machine<=0||O.PlannedDay>=host.Day||O.TestPower>0)return;
                int machine=O.Machine;var actual=E.FindPlayerItem(machine);bool portable=Portable(actual);
                if(portable){if(!E.PlayerInventories().Any(inv=>inv.NativeIdentity()==actual.parentInventory?.NativeIdentity()))throw new InvalidOperationException("请将合成器放在店内外层，不能在箱中加工。");}
                else if(!E.PackageB.Working(machine,"ms_machine_electrobench"))throw new InvalidOperationException("请保留放在店内的整备台或原合成器检修位。");
                var slots=E.PackageB.RepairWorkInventories(machine);var original=S.CampaignItems.Originals["c_synth"];
                if(!portable&&!NativePackageB.ItemList(slots[0].childItems).Any(x=>x.uniqueId==original.Entity))throw new InvalidOperationException("原件移出，保留材料和进度。");
                var resources=S.CampaignItems.Jobs[PackageC.SynthJob].Resources.Where(r=>!r.Logical&&r.State is "available" or "leased").ToArray();
                foreach(var r in resources)if(!(r.Tool?E.PackageB.ToolsAt(machine):NativePackageB.ItemList(slots[1].childItems)).Any(x=>x.uniqueId==r.Entity))throw new InvalidOperationException("请放入所附全部耗材与工具，没有扣料或扣电。");
                int cost=PackageC.OpeningSynthEnergy(S);var power=E.AllPlayerItems().Where(x=>x.IsTag("power_source_item")&&E.PackageB.PlayerOwned(x)).FirstOrDefault(x=>PowerHelper.GetAvailableEnergyFromItem(x,false)>=cost);
                if(power==null)throw new InvalidOperationException("原版电源不足20E，保留材料和进度。");int energy=PowerHelper.GetAvailableEnergyFromItem(power,false);
                E.MutateNative(()=>{if(!PowerHelper.DrawPowerSource(power,cost))throw new InvalidOperationException("扣电失败");host.Engine.EditCampaignItems(s=>PackageC.WorkOpeningSynth(s,host.Day,power.uniqueId,cost));foreach(var r in S.CampaignItems.Jobs[PackageC.SynthJob].Resources.Where(x=>!x.Tool&&x.State=="consumed")){var item=E.FindPlayerItem(r.Entity);item?.parentInventory?.Expel(item);}},()=>PowerHelper.SetPowerSourceAt(power,energy));notice="";
            }catch(InvalidOperationException error){notice=error.Message;if(O.Machine>0&&O.PlannedDay<host.Day){host.Engine.EditCampaignItems(s=>s.CampaignItems.Opening.Machine=0);host.Persist();}}
        }
        internal string MachineText(GameItem gear)=>Managed(gear)?"茉幌的合成器委托：保留同一原件，耗材放配件区，工具装入工具箱（合成器检修位仍使用工具区）；原版电源20E，睡觉过一天。\n"+Progress+(notice==""?"":"\n"+notice):null;
    }
}
