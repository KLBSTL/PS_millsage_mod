using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageB
    {
        internal const string RepairNoteSku="ms_repair_request_note";
        private sealed class RepairView
        {
            internal GameItem Item;internal PixelWindow Window;internal RichTextElement Text;
            internal GameSlotInventory Host;internal GameGridInventory Parts,Tools;
            internal TooltipContainer Manual;
            internal GameSlotInventory Dock;
            internal GameGridInventory LegacyParts,LegacyTools;
            internal PixelWindow Recovery;
            internal Dictionary<string,GameSlotInventory> Slots=new();
            internal string Kind,Message="",RenderedText;internal int TextWidth;
        }
        private readonly Dictionary<long,RepairView> repairViews=new();
        private readonly Dictionary<long,(RepairView View,string Role)> repairInventories=new();
        private bool repairMove,repairRefreshing,repairAutomatic;
        private float repairRefreshAt;
        internal void InitializeRepairItem(GameItem item)=>InitRepairItem(item);
        internal GameInventory[] RepairWorkInventories(int machine){var v=InitRepairItem(economy.FindPlayerItem(machine));return new GameInventory[]{v.Host,v.Parts,(GameInventory)v.Dock??v.Tools};}
        internal void RefreshRepairNative(){repairRefreshAt=0;UpdateRepairNative();}
        internal object RepairGraphDiagnostic()=>repairViews.Values.Where(v=>v.Kind is "bench" or "detector").Select(v=>new{v.Item.uniqueId,v.Kind,ownedRoot=economy.FindPlayerItem(v.Item.uniqueId)!=null,sameWindow=ReferenceEquals(v.Item.contentWindow,v.Window),children=ItemList(v.Item.FindAllChildItems()).Select(x=>x.uniqueId).ToArray(),host=v.Host==null?null:ItemList(v.Host.childItems).Select(x=>x.uniqueId).ToArray()}).ToArray();
        internal void CloseRepairWindowsNative(){foreach(var view in repairViews.Values.Where(x=>x.Window!=null))view.Window.Hide();}
        internal void ShowRepairNative(int entity)
        {
            if(core.Test?.Request.RepairNative==true)StoreUIManager.Instance.controlHintOverlay.SetActive(false);
            var item=RepairActualItem(entity)??throw new InvalidOperationException("实物不在店内");
            if(item.identifier==RepairNoteSku){ShowRepairPaper(item);return;}
            var view=InitRepairItem(item);RefreshRepairViews();view.Window.EnsureFloatingDock();view.Window.Show().Center();view.Window.ToFront(true);
        }
        internal object RepairLayout(int entity)
        {
            var item=RepairActualItem(entity);var view=InitRepairItem(item);
            return view.Kind=="note"?new{kind="note",nativePaper=true,buttons=0}:(object)new{kind=view.Kind,width=view.Window.widthPixels,height=view.Window.heightPixels,movable=view.Window.canMove,close=view.Window.topCloseButton!=null,buttons=0,slots=view.Slots.ToDictionary(x=>x.Key,x=>ItemList(x.Value.childItems).Select(t=>t.uniqueId).ToArray())};
        }
        internal void RegisterRepair(ModItemDirectory directory)
        {
            if(!directory.Has(RepairNoteSku)){
                var factory=(System.Func<GameItem>)(Func<GameItem>)(()=>{
                    var dir=Resources.FindObjectsOfTypeAll(NativeTypes.Of<MiscItemDirectory>())[0].Cast<MiscItemDirectory>();
                    var item=dir.MentorContract("器材整备便条","请检查器材，保留原有配件，完工后电话联系。","");
                    item.identifier=RepairNoteSku;item.name="器材整备便条";item.SetValue(0);item.SetShape(new GridShapeBuilder(2,2).SetDataFill(2,2,1).Build());item.EnableTag("ms.repair.note",false);InitRepairItem(item);return item;
                });roots.Add(factory);directory.Add(RepairNoteSku,factory);
            }
            documentIds.Add(RepairNoteSku);
        }
        private static NativePackageB RepairSelf=>Core.Instance?.Story?.Economy?.PackageB;
        private static bool RepairPaperAllowed(GameItem __instance,ref bool __result){if(__instance?.identifier!=RepairNoteSku&&RepairSelf?.NativeBusinessActivate(__instance)!=true&&RepairSelf?.HasLegacyScanItem(__instance)!=true)return true;__result=true;return false;}
        private static bool RepairPaperUse(GameItem __instance){
            var p=RepairSelf;
            if(__instance?.identifier==RepairNoteSku){p?.ShowRepairPaper(__instance);return false;}
            if(p?.HasLegacyScanItem(__instance)==true&&!p.NativeBusinessActivate(__instance)){p.ShowRepairNative(__instance.uniqueId);return false;}
            if(p?.NativeBusinessActivate(__instance)!=true)return true;
            p.UseNativeBusinessMachine(__instance);return false;
        }
        private void ShowRepairPaper(GameItem item)
        {
            string usePaper=economy.Trade?.UsePaper(item.uniqueId);
            if(usePaper!=null){core.EnsureChineseFallback();PaperUIManager.Instance.OpenUI("普通整备需求便条",usePaper,"器材续用客户",0);return;}
            string inspection=host.Inspection?.Paper(item.uniqueId);if(inspection!=null){core.EnsureChineseFallback();PaperUIManager.Instance.OpenUI("基础检测便条",inspection,"器材送检客户",0);return;}
            string returnPaper=host.Return?.Paper(item.uniqueId);if(returnPaper!=null){core.EnsureChineseFallback();PaperUIManager.Instance.OpenUI("比赛筹备便条",returnPaper,"茉幌",0);return;}
            string diaryPaper=host.Diary?.Paper(item.uniqueId);if(diaryPaper!=null){core.EnsureChineseFallback();PaperUIManager.Instance.OpenUI("挂件读取便条",diaryPaper,"萤",0);return;}
            string campaignPaper=host.Native012?.Paper(item.uniqueId)??host.ActTwo?.Paper(item.uniqueId);
            if(campaignPaper!=null){core.EnsureChineseFallback();PaperUIManager.Instance.OpenUI("小凪留下的整备便条",campaignPaper,"凪",0);return;}
            string openingTitle=null;string openingPaper=host.Opening?.Paper(item.uniqueId,out openingTitle);
            if(openingPaper!=null){core.EnsureChineseFallback();PaperUIManager.Instance.OpenUI(openingTitle,openingPaper,"",0);return;}
            if(core.Test?.Request.RepairNative==true)StoreUIManager.Instance.controlHintOverlay.SetActive(false);
            var note=NoteFor(item);string body="请检查器材，保留原有配件，完工后电话联系。";
            if(note!=null){
                var svc=note.Service==null?null:A.Services.GetValueOrDefault(note.Service);
                body=Label(A.Facts.GetValueOrDefault(note.Host)?.Sku??B.Instruments.GetValueOrDefault(note.Host)?.ItemId)+"\n\n"+RepairDemandText(note)+"\n\n请保留琴上原有的配件，检查后按实际情况帮我整备。\n准备好之后，请打电话通知我来取吧。";
                body+="\n\n"+(svc==null?"原件按低价放在柜台出售，照常收购即可。":svc.Delivery!=null?"器材已经取回，这张便条留作记录。":note.NotifiedDay>=0?"已约好次日到店，照常在柜台交易。":"原件已交入；完工检测后电话联系。");
            }
            core.EnsureChineseFallback();PaperUIManager.Instance.OpenUI("器材整备便条",body,note==null?"":Name(note.Actor),0);
        }
        private static void RepairComplexValue(GameItem __instance,ref long __result)
        {
            var p=RepairSelf;if(p==null||!p.IsRepairGear(__instance)||!p.Enabled)return;
            if(p.TryRepairCounterValue(__instance,out int value)){__result=value;return;}
            if(p.A.Facts.ContainsKey(__instance.uniqueId)&&p.B.Instruments.ContainsKey(__instance.uniqueId))__result=PackageB.Value(p.A,p.B,__instance.uniqueId,p.ServiceFor(__instance.uniqueId)?.EffectiveSlots);
        }
        internal bool RepairInstalled(GameItem item)=>item?.parentInventory!=null&&repairInventories.TryGetValue(item.parentInventory.NativeIdentity().ToInt64(),out var role)&&role.Role.StartsWith("slot:");
        // SaveManager creates the SKU before restoring its saved entity ID.
        // Fixed inventories must already exist when child nodes are decoded.
        internal bool IsRepairGear(GameItem item)=>item!=null&&(PackageC.Definitions.Any(d=>(d.Role is "c_bass" or "c_mic")&&d.Sku==item.identifier)||host.Return?.IsGear(item)==true||host.Native012?.IsGear(item)==true||host.ActTwo?.IsGear(item)==true||host.Opening?.IsGear(item)==true||PackageBCatalog.Rows("equipment_package_b").Any(x=>x["id"]==item.identifier));
        private static bool RepairBeforeAccept(GameInventory __instance,GameItem __0,ref bool __result)
        {var p=RepairSelf;if(p==null||!p.repairInventories.ContainsKey(__instance.NativeIdentity().ToInt64())||p.repairMove||p.economy.IsTransferring||!p.Enabled)return true;if(p.RepairAccepts(__instance,__0))return true;__result=false;return false;}
        private static void RepairPlacement(GameGridInventory __instance,GameItem __0,ref bool __result){var p=RepairSelf;if(__result&&p!=null&&p.repairInventories.ContainsKey(__instance.NativeIdentity().ToInt64())&&!p.repairMove&&p.Enabled)__result=p.RepairAccepts(__instance,__0);}
        private static bool RepairSlotAccept(SlotMarker __instance,ref int __result)
        {var p=RepairSelf;if(p==null||__instance?.inventory==null||!p.repairInventories.ContainsKey(__instance.inventory.NativeIdentity().ToInt64())||p.repairMove||p.economy.IsTransferring||!p.Enabled||p.RepairAccepts(__instance.inventory,__instance.item))return true;__result=0;return false;}
        private bool RepairAccepts(GameInventory inventory,GameItem item)
        {
            if(NativeMemberEconomy.RestoringSnapshot)return true;
            if(!repairInventories.TryGetValue(inventory.NativeIdentity().ToInt64(),out var role))return true;
            // Reject furniture before traversing target inventories or campaign
            // records. A repair machine is never an instrument, supply or tool.
            if(IsRepairMachine(item))return false;
            if(role.Role=="recovery")return false;
            if(role.Role=="dock")return item!=null&&inventory.childItems.Count==0&&item.identifier=="toolbox"&&item.IsTag("TOOLBOX_TAG")&&PlayerOwned(item)&&PlayerOwned(role.View.Item);
            bool? inspection=host.Inspection?.Accept(role.View.Item,role.Role,inventory,item);if(inspection.HasValue)return inspection.Value;
            if(role.View.Item.identifier==NormalPlayPolicy.BasicInspector&&(role.Role!="host"||!BasicCampaignOriginal(item)&&!economy.Trade.HoldsNativeUse(item)))return false;
            bool? campaign=host.Return?.Accept(role.View.Item,role.Role,inventory,item);if(campaign.HasValue)return campaign.Value;
            campaign=host.Native012?.Accept(role.View.Item,role.Role,inventory,item);if(campaign.HasValue)return campaign.Value;
            campaign=host.ActTwo?.Accept(role.View.Item,role.Role,inventory,item);if(campaign.HasValue)return campaign.Value;
            bool? opening=host.Opening?.Accept(role.View.Item,role.Role,inventory,item);if(opening.HasValue)return opening.Value;
            if(item==null||item.uniqueId==role.View.Item.uniqueId||item.IsTag(MusicCatalog.ProtectedTag))return false;
            if(!PlayerOwned(role.View.Item)&&!(role.View.Kind=="gear"&&ServiceFor(role.View.Item.uniqueId)!=null))return false;
            if(role.Role=="host")return IsRepairGear(item)&&inventory.childItems.Count==0&&(PlayerOwned(item)||ServiceFor(item.uniqueId)!=null);
            if(role.Role=="tools")return PlayerOwned(item)&&(new[]{"screwdriver","welder","wire_cutter","magnifier"}.Contains(item.identifier)||item.GetGameItemType().Contains("TOOL"));
            if(role.Role=="parts")return RepairSupplyAllowed(item,ServiceFor(ItemList(role.View.Host.childItems).SingleOrDefault()?.uniqueId??0))&&!IsRepairGear(item)&&!Inventories(item).Any()&&item.identifier!=RepairNoteSku&&!item.GetGameItemType().Contains("MACHINE");
            if(role.Role.StartsWith("slot:")){
                string slot=role.Role.Substring(5);var svc=ServiceFor(role.View.Item.uniqueId);var slots=svc?.EffectiveSlots??B.Instruments.GetValueOrDefault(role.View.Item.uniqueId)?.Slots;
                if(slots?.GetValueOrDefault(slot)==item.uniqueId)return true;
                return slot=="interface"&&inventory.childItems.Count==0&&PlayerOwned(item)&&PackageB.Compatible(role.View.Item.identifier,item.identifier,slot)&&!RepairHostWorking(role.View.Item.uniqueId);
            }
            return false;
        }
        private static bool RepairBeforeExpel(GameInventory __instance,GameItem __0,ref bool __result)
        {
            var p=RepairSelf;if(__0==null||p==null||p.repairMove||p.economy.IsTransferring||!p.Enabled)return true;
            if(!p.repairInventories.TryGetValue(__instance.NativeIdentity().ToInt64(),out var r))return true;
            if(r.Role.StartsWith("slot:")&&r.Role!="slot:interface"){__result=false;return false;}
            if(r.Role=="host"&&p.RepairHostWorking(__0.uniqueId)){
                try{foreach(var step in p.ServiceFor(__0.uniqueId).Steps.Where(x=>x.Status=="running").ToArray())p.economy.Workshop.Cancel(step.Id);p.RefreshRepairViews();}
                catch(Exception e){r.View.Message=e.Message;__result=false;return false;}
            }
            return true;
        }
        private static void RepairAfterExpel(GameInventory __instance,GameItem __0,bool __result)
        {
            var p=RepairSelf;if(!__result||p==null||p.repairMove||p.economy.IsTransferring||!p.Enabled||!p.repairInventories.TryGetValue(__instance.NativeIdentity().ToInt64(),out var r)||r.Role!="slot:interface")return;
            var svc=p.ServiceFor(r.View.Item.uniqueId);if(svc==null||svc.EffectiveSlots.GetValueOrDefault("interface")!=__0.uniqueId)return;
            p.host.Engine.EditPackageB(s=>{var service=s.Workshop.Advanced.Services[svc.Id];if(!service.Held.ContainsKey(__0.uniqueId))service.Held[__0.uniqueId]=new ServiceCustodyItem{Owner=s.Workshop.Advanced.Facts[__0.uniqueId].Owner,Item=NativeWorkshop.Store(__0)};var step=new ServiceStep{Id="unplug_"+Guid.NewGuid().ToString("N"),Kind="b15_s06_install_simple",Slot="interface"};service.Steps.Add(step);PackageB.CompleteStep(s,service,step,0,p.host.Day);});
        }
        private bool BasicCampaignOriginal(GameItem item)=>item!=null&&(host.Inspection?.HeldEntity(item.uniqueId)==true||host.ReadState.CampaignItems?.Originals.Values.Any(x=>x.Entity==item.uniqueId&&x.Custodian=="player"&&x.Location=="native")==true);
        private static bool IsRepairMachine(GameItem item)=>item?.identifier=="ms_machine_electrobench"||NormalPlayPolicy.IsInspector(item?.identifier);
        private static bool RepairMayTarget(GameItem __instance,GameItem __0,ref bool __result)
        {if(IsBenchUpgrade(__instance,__0)){__result=RepairSelf?.CanUpgradeBench(__instance,__0)==true;return false;}if(RepairSelf?.NativeBusinessTarget(__instance,__0)==true){__result=true;return false;}if(IsRepairMachine(__instance)&&IsRepairMachine(__0)){__result=false;return false;}if(!NormalPlayPolicy.IsInspector(__instance?.identifier)&&__instance?.identifier!="magnifier")return true;var p=RepairSelf;if(p?.Enabled!=true)return true;if(NormalPlayPolicy.IsInspector(__instance.identifier)){__result=p.CanScan(__instance,__0);return false;}if(!p.IsRepairGear(__0))return true;__result=p.PlayerOwned(__instance)&&(p.PlayerOwned(__0)||p.ServiceFor(__0.uniqueId)!=null);return false;}
        private static bool RepairTarget(GameItem __instance,GameItem __0)
        {if(IsBenchUpgrade(__instance,__0)){RepairSelf?.UpgradeBench(__instance,__0);return false;}if(RepairSelf?.NativeBusinessTarget(__instance,__0)==true){RepairSelf.UseNativeBusinessTarget(__instance,__0);return false;}if(IsRepairMachine(__instance)&&IsRepairMachine(__0))return false;if(!NormalPlayPolicy.IsInspector(__instance?.identifier)&&__instance?.identifier!="magnifier")return true;var p=RepairSelf;if(p?.Enabled!=true||!p.IsRepairGear(__0))return true;if(NormalPlayPolicy.IsInspector(__instance.identifier)){if(p.CanScan(__instance,__0))p.ScanGear(__instance,__0);return false;}p.RunRepairAction(p.InitRepairItem(__0),()=>p.MeasureBusinessGear(__0,"b15_s02_inspect_detailed"));p.ShowRepairNative(__0.uniqueId);return false;}
        private EquipmentService ServiceFor(int hostId)=>A.Services.Values.LastOrDefault(x=>x.Host==hostId&&!x.Terminal&&x.Delivery==null);
        private bool RepairHostWorking(int hostId)=>ServiceFor(hostId)?.Steps.Any(x=>x.Status=="running")==true;
        private RepairView InitRepairItem(GameItem item)
        {
            if(item==null||item.identifier?.StartsWith("ms_",StringComparison.Ordinal)!=true)return null;if(repairViews.TryGetValue(item.NativeIdentity().ToInt64(),out var old))return old;
            string kind=item.identifier==RepairNoteSku?"note":item.identifier=="ms_machine_electrobench"?"bench":NormalPlayPolicy.IsInspector(item.identifier)?"detector":IsRepairGear(item)?"gear":null;if(kind==null)return null;
            var view=new RepairView{Item=item,Kind=kind,TextWidth=kind=="bench"||item.identifier=="ms_gear_synth_damaged"?390:kind=="detector"?204:286};repairViews[item.NativeIdentity().ToInt64()]=view;if(kind=="note")return view;
            if(kind=="bench"){BuildBench(view);return view;}
            view.Window=new PixelWindow(true,item.identifier=="ms_machine_calibrator"?"专业检测仪":item.name);view.Window.canMove=true;
            var root=new GridPixelElement(1,3,false).SetSpacing(2,5);view.Window.Attach(root.Cast<PixelElement>());item.SetContentWindow(view.Window);
            if(kind!="bench")view.Text=new RichTextElement(false,view.TextWidth,32,false);
            var opening=(System.Action)(Action)(()=>{view.Window.EnsureFloatingDock();view.Window.ToFront(true);});roots.Add(opening);view.Window.openCallback=opening;
            if(kind=="gear"){
                var slots=PackageBCatalog.Rows("module_effects").Where(x=>x["host_id"]==item.identifier).Select(x=>x["slot"]).Concat(PackageBCatalog.Rows("internal_repair_slots").Where(x=>x["host_id"]==item.identifier).Select(x=>x["slot"])).Concat(item.identifier is "ms_nagi_whale_guitar" or "ms_gear_commercial_guitar"?new[]{"strings","signal"}:Array.Empty<string>()).Concat(item.identifier==PackageC.Definitions.First(x=>x.Role=="c_synth").Sku?new[]{"hammer"}:item.identifier==PackageC.Definitions.First(x=>x.Role=="c_bass").Sku?new[]{"strings"}:Array.Empty<string>()).Distinct().ToArray();
                var area=new GridPixelElement(Math.Max(1,slots.Length),2,false).SetSpacing(6,3);root.Attach(area.Cast<PixelElement>());
                for(int i=0;i<slots.Length;i++){string key=slots[i];var label=new RichTextElement(false,72,16,false).SetText(NativeWorkflowUI.Slot(key),72,RenderHandler.ColorPalette.White);area.AttachPos(label.Cast<PixelElement>(),i,0);var slot=new GameSlotInventory(3,3,false,false);slot.SetTooltipName(key=="interface"?"外接快插件，可直接插拔":"固定部件，在整备台投放兼容配件后睡觉安装");area.AttachPos(slot.Cast<PixelElement>(),i,1);view.Slots[key]=slot;repairInventories[slot.NativeIdentity().ToInt64()]=(view,"slot:"+key);}
                if(host.NormalPlay&&item.identifier=="ms_gear_synth_damaged"){
                    var work=new GridPixelElement(2,2,false).SetSpacing(6,3);root.Attach(work.Cast<PixelElement>());
                    work.AttachPos(new RichTextElement(false,192,16,false).SetText("首份委托耗材",192,RenderHandler.ColorPalette.White).Cast<PixelElement>(),0,0);
                    work.AttachPos(new RichTextElement(false,128,16,false).SetText("工具（不消耗）",128,RenderHandler.ColorPalette.White).Cast<PixelElement>(),1,0);
                    view.Parts=new GameGridInventory(12,6);view.Tools=new GameGridInventory(8,6);work.AttachPos(view.Parts.Cast<PixelElement>(),0,1);work.AttachPos(view.Tools.Cast<PixelElement>(),1,1);
                    repairInventories[view.Parts.NativeIdentity().ToInt64()]=(view,"parts");repairInventories[view.Tools.NativeIdentity().ToInt64()]=(view,"tools");
                }
            }else{
                var area=new GridPixelElement(1,2,false).SetSpacing(6,3);root.Attach(area.Cast<PixelElement>());
                area.AttachPos(new RichTextElement(false,72,16,false).SetText("检测位",72,RenderHandler.ColorPalette.White).Cast<PixelElement>(),0,0);
                view.Host=new GameSlotInventory(4,6,false,false);area.AttachPos(view.Host.Cast<PixelElement>(),0,1);
                repairInventories[view.Host.NativeIdentity().ToInt64()]=(view,"host");
            }
            if(kind!="bench")root.Attach(view.Text.Cast<PixelElement>());
            if(kind=="detector")ConfigureScanner(item);
            return view;
        }
        private GameItem WorkHost(RepairView view)=>ItemList(view.Host.childItems).SingleOrDefault()??throw new InvalidOperationException("先放入实际器材");
        private void RunRepairAction(RepairView view,Action action){try{action();view.Message="";}catch(Exception e){view.Message=e.Message;}RefreshRepairViews();}
        private EquipmentService EnsureRepairService(GameItem item)
        {
            var prior=ServiceFor(item.uniqueId);if(prior!=null)return prior;
            if(!PlayerOwned(item))throw new InvalidOperationException("先通过原版柜台收购原件");
            var source=item.parentInventory;repairMove=true;try{string id=Authorize("repair_"+Guid.NewGuid().ToString("N"),"player",item.uniqueId);if(source!=null&&!Place(source,item))throw new InvalidOperationException("原工作位无法恢复");return A.Services[id];}finally{repairMove=false;}
        }
        private void DetectRepair(GameItem detector,GameItem item,string scope="basic")
        {
            if(!PlayerOwned(detector))throw new InvalidOperationException("请使用自己的检测仪");
            if(host.Inspection?.Pump(detector,item,true)==true)return;
            if(economy.Trade.HoldsNativeUse(item)){MeasureRepair(EnsureRepairService(item).Id,"b15_s08_calibrate",detector.uniqueId);return;}
            if(detector.identifier==NormalPlayPolicy.BasicInspector&&!BasicCampaignOriginal(item))throw new InvalidOperationException("基础检测仪支持主线原件及基础检测委托；精密业务请使用专业检测仪。");
            if(host.Return?.Pump(detector,item,true)==true||host.Native012?.Pump(detector,item,true)==true||host.ActTwo?.Pump(detector,item,true)==true||host.Opening?.Pump(detector,item,true)==true)return;
            if(detector.identifier==NormalPlayPolicy.BasicInspector)throw new InvalidOperationException("此原件当前没有待检测的主线工序。");
            if(item.identifier=="ms_gear_drum_practice_pad")scope="passive";
            MeasureBusinessGear(item,"b15_s08_calibrate",detector.uniqueId,scope);
        }
        private bool RepairSupplyAllowed(GameItem item,EquipmentService svc)=>PlayerOwned(item)||svc!=null&&svc.Held.TryGetValue(item.uniqueId,out var held)&&held.Owner==svc.Actor&&!svc.EffectiveSlots.Values.Contains(item.uniqueId);
        private List<RepairOperation> RepairOperations(RepairView view,EquipmentService svc)
        {
            var gear=WorkHost(view);var operations=new List<RepairOperation>();var fact=A.Facts[svc.Host];
            var use=PackageDUse.ForService(host.ReadState,svc.Id);
            bool nativeUse=use?.AcquisitionPaid.HasValue==true;
            if(nativeUse&&use.Status!="servicing")return operations;
            if(fact.Faults.Any(x=>x.Value=="dirty_oxidized"&&PackageB.Known(A,svc.Host,x.Key)))operations.Add(new(){Kind="b15_s03_clean"});
            if(!nativeUse){
                foreach(var fault in fact.Faults.Where(x=>PackageB.Known(A,svc.Host,x.Key)&&x.Value=="contact_fault"&&(x.Key=="wire"||x.Key=="capacitor")))operations.Add(new(){Kind="b15_s04_contact_repair",Domain=fault.Key});
                if(fact.Faults.TryGetValue("support",out string support)&&support=="structural_fault"&&PackageB.Known(A,svc.Host,"support"))operations.Add(new(){Kind="b15_s05_pad_repair",Domain="support"});
            }
            foreach(var part in ItemList(view.Parts.childItems).Where(x=>RepairSupplyAllowed(x,svc))){
                string slot=PackageBCatalog.Rows("module_effects").Where(x=>x["host_id"]==gear.identifier&&x["id"]==part.identifier&&x["effect_status"]=="B_enabled").Select(x=>x["slot"]).Concat(PackageBCatalog.Rows("internal_repair_slots").Where(x=>x["host_id"]==gear.identifier&&x["accepted_sku"]==part.identifier).Select(x=>x["slot"])).FirstOrDefault();
                if(slot==null||nativeUse&&(svc.InitialSlots.ContainsKey(slot)||svc.EffectiveSlots.ContainsKey(slot)))continue;
                if(operations.Any(x=>x.Slot==slot))throw new InvalidOperationException("同一个槽位只放一件待装配件");
                operations.Add(new(){Kind=slot=="interface"?"b15_s06_install_simple":"b15_s07_install_precision",Slot=slot,Component=part.uniqueId});
            }
            return operations;
        }
        private Dictionary<string,int[]> RepairChoices(RepairView view,EquipmentService svc,WorkshopSpec spec)
        {
            var available=ItemList(view.Parts.childItems).Concat(ToolsAt(view)).ToArray();var choices=new Dictionary<string,int[]>();
            foreach(var request in spec.Inputs.Concat(spec.Tools)){
                string sku=economy.Workshop.NativeId(request.Key);var found=available.Where(x=>x.identifier==sku&&RepairSupplyAllowed(x,svc)).OrderBy(x=>x.uniqueId).ToArray();
                if(found.Sum(x=>x.unitCount)<request.Value)throw new InvalidOperationException("工位缺少："+economy.Workshop.Label(request.Key)+" ×"+request.Value);
                choices[request.Key]=found.Select(x=>x.uniqueId).ToArray();
            }
            return choices;
        }
        private string RepairPreview(RepairView view,EquipmentService svc)
        {
            try{
                var operations=RepairOperations(view,svc);if(operations.Count==0)return "无待加工项：先检查故障或放入兼容配件。";
                if(operations.Count==1&&operations[0].Slot=="interface")return "仅外接快插件：即时装配，不耗电。";
                var spec=PackageB.RepairBundleSpec(host.Engine.Snapshot(),svc.Id,"repair_preview",operations,out var plan);
                string result="预计 "+plan.Days+" 日 / "+spec.Energy+"E / "+operations.Count+" 项";
                try{var choices=RepairChoices(view,svc,spec);var custody=svc.Held.Keys.Select(economy.FindPlayerItem).Where(x=>x!=null);string block=economy.Workshop.PreparedBlock(spec,view.Item.uniqueId,choices,custody);result+="\n"+(block??"投料齐全，可以开工。");}catch(InvalidOperationException e){result+="\n"+e.Message;}
                return result;
            }catch(InvalidOperationException e){return e.Message;}
        }
        private void StartNativeRepair(RepairView view)
        {
            var svc=EnsureRepairService(WorkHost(view));foreach(var part in ItemList(view.Parts.childItems))Ensure(part);
            var operations=RepairOperations(view,svc);
            var cable=svc.Actor=="ms_hotaru"?ItemList(view.Parts.childItems).FirstOrDefault(x=>x.identifier=="ms_item_balanced_cable"&&PlayerOwned(x)):null;
            if(operations.Count==0){if(cable!=null){AddCable(svc.Id,cable.uniqueId);return;}throw new InvalidOperationException("没有待加工项；先检查故障或放入配件");}
            if(operations.Count==1&&operations[0].Slot=="interface"){QuickRepair(svc,operations[0].Component);if(cable!=null)AddCable(svc.Id,cable.uniqueId);return;}
            string job="daily_"+Guid.NewGuid().ToString("N");var spec=PackageB.RepairBundleSpec(host.Engine.Snapshot(),svc.Id,job,operations,out var plan);
            var choices=RepairChoices(view,svc,spec);StartRepairPlan(svc.Id,job,view.Item.uniqueId,operations,choices);
            if(cable!=null)AddCable(svc.Id,cable.uniqueId);
        }
        private void QuickRepair(EquipmentService svc,int component)
        {
            var item=economy.FindPlayerItem(component)??throw new InvalidOperationException("配件不在实际库存");Ensure(item);
            PackageB.ServiceSpec(host.Engine.Snapshot(),svc.Id,"b15_s06_install_simple",null,"interface",component,"basic");
            var source=item.parentInventory;repairMove=true;
            try{economy.MutateNative(()=>{if(!source.Expel(item))throw new InvalidOperationException("配件无法移交");host.Engine.EditPackageB(s=>{var v=s.Workshop.Advanced.Services[svc.Id];v.Held[component]=new ServiceCustodyItem{Owner=s.Workshop.Advanced.Facts[component].Owner,Item=NativeWorkshop.Store(item),SuggestedPrice=PackageB.LeafValue(s.Workshop.Advanced,component)};var step=new ServiceStep{Id="plug_"+Guid.NewGuid().ToString("N"),Kind="b15_s06_install_simple",Slot="interface",Component=component};v.Steps.Add(step);PackageB.CompleteStep(s,v,step,0,host.Day);});},()=>{if(item.parentInventory==null)Place(source,item);});}finally{repairMove=false;}
        }
        internal void MaterializeRepair(string service)
        {
            var svc=A.Services[service];if(svc.Delivery!=null||svc.Terminal||economy.FindPlayerItem(svc.Host)!=null)return;
            var item=NativeMemberEconomy.Decode(svc.HostSnapshot);repairMove=true;try{
                var benchView=repairViews.Values.FirstOrDefault(v=>v.Kind=="bench"&&economy.FindPlayerItem(v.Item.uniqueId)?.NativeIdentity()==v.Item.NativeIdentity()&&v.Host.childItems.Count==0);
                if(benchView!=null){if(!Place(benchView.Host,item))return;}else EmporiumEntry.Instance.TryAddToPlayerInv(item);
            }finally{repairMove=false;}
        }
        internal void DematerializeRepair(string service)
        {
            var svc=A.Services[service];repairMove=true;try{
                var gear=economy.FindPlayerItem(svc.Host);
                if(gear!=null){var view=InitRepairItem(gear);view.Window.Hide();foreach(var inv in view.Slots.Values)foreach(var module in ItemList(inv.childItems).ToArray())inv.Expel(module);
                    string snapshot=NativeMemberEconomy.Encode(gear);gear.parentInventory?.Expel(gear);host.Engine.EditPackageB(s=>s.Workshop.Advanced.Services[service].HostSnapshot=snapshot);}
                foreach(int id in svc.Held.Keys){var loose=economy.FindPlayerItem(id);if(loose!=null)loose.parentInventory.Expel(loose);}
            }finally{repairMove=false;}
        }

        private RepairNote NoteFor(GameItem item)=>A.RepairNotes.Values.FirstOrDefault(x=>x.Entity==item.uniqueId);
        internal bool ShowNativeRepairService(string id)
        {var svc=A.Services.GetValueOrDefault(id);if(svc==null||svc.Terminal||svc.Delivery!=null)return false;Close();economy.Trade.ClosePanel();economy.Workshop.Close();MaterializeRepair(id);var item=economy.FindPlayerItem(svc.Host);if(item==null)return false;var parent=InventoryOwner(item.parentInventory);ShowRepairNative(parent?.identifier=="ms_machine_electrobench"?parent.uniqueId:item.uniqueId);return true;}
        private string RepairDemandText(RepairNote note)
        {
            var row=PackageBCatalog.Rows("member_demands").FirstOrDefault(x=>x["id"]==note.Demand);
            if(row==null)return "检查现状，排除发现的故障，完成基础检测。";
            string text=string.Join("、",PackageBCatalog.Split(row["required_components"]).Select(Label));if(text.Length>0)text+="（"+PackageBCatalog.GradeName(row["minimum_component_grade"])+"及以上）";
            var goals=new[]{("noise_min","降噪"),("stability_min","稳定"),("portability_min","便携")}.Where(x=>PackageBCatalog.Number(row[x.Item1])>0).Select(x=>x.Item2+"≥"+row[x.Item1]);
            return text+"；"+string.Join("，",goals)+"。";
        }
        internal string RepairCallBlock(string actor)
        {
            string lastAct=host.Return?.CallBlock(actor);if(lastAct!=null)return lastAct;
            string diary=host.Diary?.CallBlock(actor);if(diary!=null)return diary;
            string campaign=host.Native012?.CallBlock(actor);if(campaign!=null)return campaign;
            campaign=host.ActTwo?.CallBlock(actor);if(campaign!=null)return campaign;
            string opening=host.Opening?.CallBlock(actor);if(opening!=null)return opening;
            var svc=A.Services.Values.FirstOrDefault(x=>x.Actor==actor&&!x.Terminal&&x.Delivery==null);if(svc==null)return null;
            if(svc.Steps.Any(x=>x.Status=="running")||!PackageB.Healthy(A,B,svc.Host,svc.EffectiveSlots))return "先完成维修并排除故障，再联系取件。";
            string block=PackageB.RepairDemandBlock(host.Engine.Snapshot(),svc);if(block!=null)return block;
            string scope=A.Facts[svc.Host].Sku=="ms_gear_drum_practice_pad"?"passive":"basic";
            return PackageB.CalibrationValid(A,B,svc.Host,scope,svc.EffectiveSlots)?null:"请先用检测仪确认当前配置。";
        }
        internal string RepairCallText(string actor)
        {
            string block=RepairCallBlock(actor);if(block!=null)return block;
            if(host.Return?.PendingActor(actor)==true)return host.Return.CallText(actor);
            if(actor=="ms_hotaru"&&host.Diary?.Pending==true)return host.Diary.CallText;
            if(actor=="ms_nagi"&&host.Native012?.Pending==true)return host.Native012.CallText();
            if(host.ActTwo?.PendingActor(actor)==true)return host.ActTwo.CallText(actor);
            if(actor=="ms_mahoro"&&host.Opening?.ReadyCall==true)return "合成器已经修好并通过基础检测，明天过来试音吧。";
            var svc=A.Services.Values.FirstOrDefault(x=>x.Actor==actor&&!x.Terminal&&x.Delivery==null);
            return svc==null?"我明天来店里看看器材，到店后在柜台当面商量价格吧。":"器材已经整备完工，我明天来店里取，照常在柜台结算就好。";
        }
        internal void NotifyRepair(string actor)
        {
            string block=RepairCallBlock(actor);if(block!=null)throw new InvalidOperationException(block);
            economy.Business.Book(actor);if(actor=="ms_nagi")host.Native012?.Notified();host.ActTwo?.Notified(actor);host.Return?.Notified(actor);host.Engine.EditPackageB(s=>{foreach(var n in s.Workshop.Advanced.RepairNotes.Values.Where(x=>x.Actor==actor&&x.Service!=null&&s.Workshop.Advanced.Services[x.Service].Delivery==null))n.NotifiedDay=host.Day;});host.Persist();
        }

        private static void RepairEndDay(){var p=RepairSelf;if(p?.Enabled==true)p.StageRepairSlots();}
        private void StageRepairSlots()
        {
            if(repairAutomatic||economy.IsTransferring)return;repairAutomatic=true;
            foreach(var portable in repairViews.Values.Where(x=>x.Kind=="gear"&&x.Parts!=null&&host.Opening.Portable(x.Item)).ToArray())host.Opening.Stage(portable.Item,portable.Item);
            try{foreach(var view in repairViews.Values.Where(x=>x.Kind=="bench"&&economy.FindPlayerItem(x.Item.uniqueId)?.NativeIdentity()==x.Item.NativeIdentity()&&x.Host.childItems.Count>0).ToArray()){
                var gear=WorkHost(view);if(RepairHostWorking(gear.uniqueId)||NativeBusiness&&NativeSalvage(view.Item.uniqueId)!=null)continue;
                if(host.Return?.Stage(view.Item,gear)==true)continue;
                if(host.Native012?.Stage(view.Item,gear)==true)continue;
                if(host.ActTwo?.Stage(view.Item,gear)==true)continue;
                if(host.Opening?.Stage(view.Item,gear)==true)continue;
                if(NativeBusiness&&ServiceFor(gear.uniqueId)==null&&view.Parts.childItems.Count==0)continue;
                RunRepairAction(view,()=>{var svc=EnsureRepairService(gear);if(RepairOperations(view,svc).Count>0)StartNativeRepair(view);});
            }}finally{repairAutomatic=false;}
        }
        private void PumpRepairSlots()
        {
            if(repairAutomatic||economy.IsTransferring)return;repairAutomatic=true;
            try{foreach(var view in repairViews.Values.Where(x=>x.Host!=null&&economy.FindPlayerItem(x.Item.uniqueId)?.NativeIdentity()==x.Item.NativeIdentity()&&x.Host.childItems.Count>0).ToArray()){
                var gear=WorkHost(view);if(RepairHostWorking(gear.uniqueId))continue;
                if(host.Inspection?.Pump(view.Item,gear,view.Kind=="detector")==true)continue;
                if(host.Return?.Pump(view.Item,gear,view.Kind=="detector")==true)continue;
                if(host.Native012?.Pump(view.Item,gear,view.Kind=="detector")==true)continue;
                if(host.ActTwo?.Pump(view.Item,gear,view.Kind=="detector")==true)continue;
                if(host.Opening?.Pump(view.Item,gear,view.Kind=="detector")==true)continue;
                if(view.Kind=="detector")RunRepairAction(view,()=>DetectRepair(view.Item,gear));
                else if(view.Kind=="bench"&&ToolsAt(view).Any(x=>x.identifier=="magnifier"&&PlayerOwned(x)))RunRepairAction(view,()=>MeasureBusinessGear(gear,"b15_s02_inspect_detailed"));
            }}finally{repairAutomatic=false;}
        }
        partial void UpdateRepairNative()
        {
            if(string.IsNullOrWhiteSpace(PlayerStore.instance?.runID)||core.Test?.Request.RepairNative==true&&core.Test.Tools==null||economy.IsTransferring)return;
            if(Time.unscaledTime<repairRefreshAt)return;repairRefreshAt=Time.unscaledTime+.25f;
            foreach(var item in economy.AllPlayerItems().ToArray())InitRepairItem(item);
            PrepareRepairCounter();PumpRepairSlots();if(NativeBusiness)ReturnNativeWorkOutputs();RefreshRepairViews();
        }
        partial void RefreshRepairViews()
        {
            if(!Enabled||repairRefreshing)return;repairRefreshing=true;repairMove=true;
            try{foreach(var view in repairViews.Values.Where(v=>v.Window!=null&&RepairActualItem(v.Item.uniqueId)?.NativeIdentity()==v.Item.NativeIdentity()).ToArray()){
                var item=view.Item;var svc=ServiceFor(item.uniqueId);string text="";
                if(host.Return?.MachineText(view.Kind=="gear"?view.Item:view.Host==null?null:ItemList(view.Host.childItems).SingleOrDefault()) is string returnText){SyncCampaign012Attachments();text=returnText;}
                else if(TryActTwoView(view,out string campaignText)||TryCampaign012View(view,out campaignText))text=campaignText;
                else if(host.Opening?.MachineText(view.Kind=="gear"?view.Item:view.Host==null?null:ItemList(view.Host.childItems).SingleOrDefault()) is string openingText)text=openingText;
                else if(host.Inspection?.Managed(item)==true){text=host.Inspection.Text(item);}
                else if(view.Kind=="gear"){
                    var slots=svc?.EffectiveSlots??B.Instruments.GetValueOrDefault(item.uniqueId)?.Slots;
                    foreach(var entry in view.Slots){int wanted=slots?.GetValueOrDefault(entry.Key)??0;var current=ItemList(entry.Value.childItems).SingleOrDefault();
                        if(current!=null&&current.uniqueId!=wanted){if(entry.Key=="interface"&&PlayerOwned(current)&&wanted==0){repairMove=false;try{QuickRepair(EnsureRepairService(item),current.uniqueId);wanted=ServiceFor(item.uniqueId).EffectiveSlots.GetValueOrDefault(entry.Key);}finally{repairMove=true;}}else entry.Value.Expel(current);}
                        if(wanted>0&&entry.Value.childItems.Count==0){var existing=RepairActualItem(wanted);string snap=svc?.Held.GetValueOrDefault(wanted)?.Item.Snapshot??B.Components.GetValueOrDefault(wanted)?.Snapshot;var part=existing??(snap==null?null:NativeMemberEconomy.Decode(snap));if(part!=null){part.parentInventory?.Expel(part);if(!entry.Value.UncheckedAccept(part))throw new InvalidOperationException("配件无法还原 #"+wanted);}}
                    }
                    if(A.Facts.ContainsKey(item.uniqueId))Present(item,slots);
                    text="外接件可插拔；内部件在整备台更换。";
                    if(NativeBusiness&&PlayerOwned(item)&&!item.IsTag(MusicCatalog.ProtectedTag))text+=economy.Trade.BusinessItemText(item);
                    if(A.Facts.ContainsKey(item.uniqueId)&&B.Instruments.ContainsKey(item.uniqueId)){var stats=PackageB.Stats(A,B,item.uniqueId,slots);text+="\n降噪 / 稳定 / 响应 / 便携："+string.Join(" / ",new[]{"noiseControl","stability","response","portability"}.Select(x=>stats[x]));string scope=item.identifier=="ms_gear_drum_practice_pad"?"passive":"basic";text+="\n"+(PackageB.CalibrationValid(A,B,item.uniqueId,scope,slots)?"当前配置检测合格。":"当前配置尚未检测合格。");}
                }else{
                    var gear=ItemList(view.Host.childItems).SingleOrDefault();var service=gear==null?null:ServiceFor(gear.uniqueId);var job=service?.Steps.Select(x=>host.ReadState.Workshop.Jobs.GetValueOrDefault(x.Id)).FirstOrDefault(x=>x?.RepairDays>0&&x.Status is "running" or "paused");
                    text=view.Kind=="bench"?"":"这里保留了旧档中的器材。\n取出后，可用检测仪直接检测。";
                    if(view.Kind=="detector"&&host.Inspection?.Managed(gear)==true)text=host.Inspection.Text(gear);
                    if(view.Kind=="bench"&&service!=null&&job==null)text+="\n"+RepairPreview(view,service);
                    if(host.NormalPlay&&view.Kind=="bench"&&service!=null&&job==null)text+="\n常见耗材可联系8670200补货；专属模组沿原地区渠道采购。";
                    if(view.Kind=="detector"&&gear!=null&&service!=null){string scope=gear.identifier=="ms_gear_drum_practice_pad"?"passive":"basic";text+="\n"+(PackageB.CalibrationValid(A,B,gear.uniqueId,scope,service.EffectiveSlots)?"检测合格，完工后可电话通知物主。":"检测未合格；先检查真实故障及需求。");}
                    if(job!=null)text+="\n进度 "+job.RepairDaysWorked+"/"+job.RepairDays+" 日 · 总电量 "+job.EnergyE+"E · "+(job.PauseReason??"下次睡觉继续");
                    if(service!=null&&!service.Steps.Any(x=>x.Status=="running"))foreach(var held in service.Held.Values.Where(x=>x.Owner=="player"&&(x.Item.Tool||host.ReadState.Workshop.Jobs.Values.Any(j=>j.Inputs.Any(i=>i.Entity==x.Item.Entity)))&&!service.EffectiveSlots.Values.Contains(x.Item.Entity)).ToArray()){
                        if(economy.FindPlayerItem(held.Item.Entity)!=null)continue;var tool=NativeMemberEconomy.Decode(held.Item.Snapshot);bool placed=held.Item.Tool?ReturnTool(view,tool,destination=>Place(destination,tool)):view.Parts!=null&&Place(view.Parts,tool);if(placed)host.Engine.EditPackageB(s=>s.Workshop.Advanced.Services[service.Id].Held.Remove(held.Item.Entity));
                    }
                }
                if(view.Manual!=null){
                    const string instructions="整备：放入器材、耗材和工具箱\n睡觉时加工，开工前可换料\n取出器材可停止未完成工序\n检查：放大镜装入工具箱";
                    RecoverBenchParts(view);
                    string recovery=HasRecovery(view)?"\n旧档物品：检查整备台可打开取回区。":"";
                    view.Manual.content=instructions+recovery+(NativeBusiness?"\n报废：对整备台使用剪线钳\n日结前可取消":"")+(string.IsNullOrWhiteSpace(text)?"":"\n\n"+text.Trim())+(view.Message.Length==0?"":"\n"+view.Message);
                    continue;
                }
                string wrapped=WrapRepairText(text+(view.Message.Length==0?"":"\n"+view.Message),(view.TextWidth-10)/7);
                if(wrapped!=view.RenderedText){view.Text.SetText(wrapped,view.TextWidth,RenderHandler.ColorPalette.White);view.Text.SetFixedSize(view.TextWidth,(wrapped.Count(x=>x=='\n')+1)*16+8);view.Window.Validate();view.RenderedText=wrapped;}
            }}finally{repairMove=false;repairRefreshing=false;}
        }
        private static string WrapRepairText(string value,int max)
        {var result=new System.Text.StringBuilder();int width=0;foreach(char c in value){if(c=='\n'){result.Append(c);width=0;continue;}int size=c>255?2:1;if(width+size>max){result.Append('\n');width=0;}result.Append(c);width+=size;}return result.ToString();}
    }
}
