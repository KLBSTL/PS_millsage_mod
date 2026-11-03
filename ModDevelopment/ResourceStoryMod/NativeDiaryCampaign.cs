using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ResourceStoryMod.Story;
using UnityEngine;
using DecoderWindow=System.ValueTuple<PixelWindow,GameSlotInventory,GameGridInventory,GameSlotInventory,GameSlotInventory,GameSlotInventory>;

namespace ResourceStoryMod
{
    internal sealed class NativeDiaryCampaign
    {
        internal const string BoxSku="ms_story_lost_property_box",ReadoutSku="ms_story_diary_readout_card";
        private readonly Core core;private readonly NativeStoryHost host;
        private NativeMemberEconomy E=>host.Economy;
        private StoryState S=>host.ReadState;
        private CampaignNativeDiary N=>S?.CampaignItems?.NativeDiary;
        private readonly Dictionary<long,DecoderWindow> windows=new();
        private readonly List<System.Delegate> roots=new();
        private bool updating,selling;private float next;private string notice="";
        internal bool Enabled=>host.Native012.Enabled&&(core.Test==null||core.Test.Request.DiaryExtension)&&S?.CampaignItems?.Stage==2;
        internal bool Pending=>Enabled&&N?.ShopReadGranted==true&&!N.Closed;
        internal bool DuePhone=>Pending&&S.Economy.Business.Appointments.TryGetValue("ms_hotaru",out var a)&&a.Status=="pending"&&a.Day==host.Day;
        private string Visit=>host.Opening.VisitScene;
        internal bool Trading=>Enabled&&N!=null&&Visit is "ms_scene_017" or "ms_scene_018"&&host.Engine.Frame()==null&&PlayerStore.instance.isClientArrived&&E.Business.HeldStoryActor==DiaryPresentation.Lead(Visit);
        internal bool FinishedVisit=>Trading&&PlayerStore.instance.lockAllFlag==0&&DialogUIManager.Instance.isConversationActiveFlag==0;
        private static NativeDiaryCampaign Self=>Core.Instance?.Story?.Diary;
        private static bool StoryItem(GameItem item)=>item?.identifier is "ms_kanata_keychain" or ReadoutSku;
        private bool Restoring=>NativeMemberEconomy.RestoringSnapshot||host.Engine==null;
        internal NativeDiaryCampaign(Core c,NativeStoryHost h)
        {
            core=c;host=h;
            Patch(typeof(MachineDesequencer),"CreateMachineInventoryWindow",null,nameof(CaptureWindow));Patch(typeof(MachineDesequencer),"Desequencer",null,nameof(CaptureMachine));
            foreach(var nested in typeof(MachineDesequencer).GetNestedTypes(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic))
                foreach(var method in nested.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance)) {
                    var args=method.GetParameters();
                    if(!method.Name.StartsWith("<Desequencer>b__",StringComparison.Ordinal))continue;
                    if(method.ReturnType==typeof(bool)&&args.Length==2&&args[0].ParameterType==typeof(GameItem)&&args[1].ParameterType==typeof(GameInventory))
                        core.HarmonyInstance.Patch(method,postfix:new HarmonyMethod(typeof(NativeDiaryCampaign),nameof(NativeFilter)));
                    else if(!method.IsStatic&&AccessTools.Field(nested,"machine")!=null&&args.Length>0&&args[0].ParameterType==typeof(GameItem))
                        core.HarmonyInstance.Patch(method,prefix:new HarmonyMethod(typeof(NativeDiaryCampaign),nameof(NativeCallback)));
                }
            foreach(string method in new[]{"StartProgressTypeMachine","ContinueProgressTypeMachine","FinishProgressTypeMachine"})Patch(typeof(MachineProgressHelper),method,nameof(NativeProgress),null);
            Patch(typeof(MachineDesequencer),"FinishDesequencer",nameof(NativeFinish),null);Patch(typeof(PlayerStore),"EndDay",null,nameof(EndDay));Patch(typeof(PlayerStore),"SellItem",nameof(Sell),null);
            foreach(string method in new[]{"MayActivateSlotItem","CanActivateSlotItem","MayToggleSlotItem","CanToggleSlotItem"})Patch(typeof(GameItem),method,nameof(PaperAllowed),null);
            foreach(string method in new[]{"ActivateSlotItem","ToggleSlotItem"})Patch(typeof(GameItem),method,nameof(PaperUse),null);
        }
        private void Patch(Type t,string method,string before,string after)=>core.HarmonyInstance.Patch(ReleaseApi.Method(t,method),prefix:before==null?null:new HarmonyMethod(typeof(NativeDiaryCampaign),before),postfix:after==null?null:new HarmonyMethod(typeof(NativeDiaryCampaign),after));
        private static void CaptureWindow(DecoderWindow __result){var p=Self;if(p!=null&&__result.Item1!=null)p.windows[__result.Item1.NativeIdentity().ToInt64()]=__result;}
        private static void CaptureMachine(GameItem __result)
        {
            var p=Self;if(p==null||__result?.contentWindow==null||!p.windows.ContainsKey(__result.contentWindow.NativeIdentity().ToInt64()))return;
            var window=__result.contentWindow;var previous=window.openCallback;var opening=(System.Action)(Action)(()=>{previous?.Invoke();window.EnsureFloatingDock();window.ToFront(true);});p.roots.Add(opening);window.openCallback=opening;
        }
        private bool TryWindow(GameItem machine,out DecoderWindow view){view=default;return machine?.contentWindow!=null&&windows.TryGetValue(machine.contentWindow.NativeIdentity().ToInt64(),out view);}
        internal GameInventory[] DecoderSlots(GameItem machine)
        {
            if(!TryWindow(machine,out var v))throw new InvalidOperationException("请选择原版密码解序器。");
            // Native closure fields: Item2 battery, Item3 modules, Item4 source
            // chip, Item5 blank-keycard input, Item6 completed-keycard output.
            return new GameInventory[]{v.Item4,v.Item5,v.Item2,v.Item6,v.Item3};
        }
        private static bool NativeCallback(object __instance,GameItem __0)=>!StoryItem(__0)&&Self?.Frozen((GameItem)AccessTools.Field(__instance.GetType(),"machine").GetValue(__instance))!=true;
        private bool Frozen(GameItem machine)=>TryWindow(machine,out var v)&&new GameInventory[]{v.Item4,v.Item5,v.Item6}.Any(inv=>NativePackageB.ItemList(inv.childItems).Any(StoryItem));
        private static bool NativeProgress(GameItem __0)=>Self?.Frozen(__0)!=true;
        private static bool NativeFinish(GameItem __0)=>Self?.Frozen(__0)!=true;
        private static void NativeFilter(GameItem __0,GameInventory __1,ref bool __result)
        {
            var p=Self;if(p==null||!StoryItem(__0))return;var owner=NativePackageB.InventoryOwner(__1);
            if(!p.TryWindow(owner,out var v)){__result=false;return;}
            __result=__0.identifier==ReadoutSku?__1.NativeIdentity()==v.Item6.NativeIdentity():__1.NativeIdentity()==v.Item4.NativeIdentity()&&(p.Restoring||p.Enabled&&p.N?.ShopReadGranted==true&&p.S.CampaignItems.Originals.GetValueOrDefault("c_keychain")?.Entity==__0.uniqueId);
        }
        internal bool? Accept(GameInventory inventory,GameItem item)
        {
            var owner=NativePackageB.InventoryOwner(inventory);
            if(owner?.identifier==BoxSku)return item?.identifier=="ms_kanata_keychain"&&owner.uniqueId!=item.uniqueId;
            if(item==null||!TryWindow(owner,out var v))return null;
            if(item.identifier=="ms_kanata_keychain")return inventory.NativeIdentity()==v.Item4.NativeIdentity()&&(Restoring||Enabled&&N?.ShopReadGranted==true&&S.CampaignItems.Originals["c_keychain"].Entity==item.uniqueId);
            if(item.identifier==ReadoutSku)return inventory.NativeIdentity()==v.Item6.NativeIdentity()&&(Restoring||E.IsTransferring&&N?.Blank==item.uniqueId);
            if(Frozen(owner)&&inventory.NativeIdentity()==v.Item5.NativeIdentity())return Restoring||Enabled&&N!=null&&item.uniqueId==N.Blank&&item.identifier==N.BlankSku;
            return null;
        }
        internal void Register(ModItemDirectory directory)
        {
            if(directory.Has(BoxSku))return;var factory=(System.Func<GameItem>)(Func<GameItem>)(()=>{
                core.EnsureResources();var item=ItemDirectory.CreateEmptyItem(BoxSku);item.name="失物招领盒";item.longDescription="只存放代管挂件。发现芯片不代表获得读取许可；请先归还物主。";item.SetValue(0);item.SetSpriteAndShape("resource_story","item");item.SetShape(new GridShapeBuilder(3,2).SetDataFill(3,2,1).Build());item.SetGameItemType("HOUSEHOLD_GOOD");item.EnableTag(MusicCatalog.ProtectedTag,false);
                var pair=DirectoryUtils.CreateInventoryWindow(12,6,true);pair.Item1.SetTitle("失物招领盒 · 代管原件");pair.Item1.canMove=true;item.SetContentWindow(pair.Item1);ContainerHelper.InitContainerItemTagOnly(pair.Item2.Cast<GameGridInventory>(),item);return item;
            });roots.Add(factory);directory.Add(BoxSku,factory);
        }
        internal static GameItem NativeItem(GameItem template)
        {
            if(!string.IsNullOrEmpty(template.identifier))return DirectoryMaster.Item(template.identifier,true);
            foreach(string id in DirectoryMaster.GetIdentifierList<GameItem>(null)){
                if(string.IsNullOrEmpty(id)||id.StartsWith("ms_"))continue;var candidate=DirectoryMaster.Item(id,false);
                if(candidate!=null&&candidate.name==template.name&&candidate.spritePath==template.spritePath&&candidate.spriteAtlasPath==template.spriteAtlasPath)return DirectoryMaster.Item(id,true);
            }
            throw new InvalidOperationException("找不到原版目录工厂："+template.name);
        }
        internal static GameItem BlankCard()=>NativeItem(Resources.FindObjectsOfTypeAll(NativeTypes.Of<MiscItemDirectory>())[0].Cast<MiscItemDirectory>().BlankKeycard());
        internal static GameItem Cloth()=>NativeItem(Resources.FindObjectsOfTypeAll(NativeTypes.Of<AmenitiesItemDirectory>())[0].Cast<AmenitiesItemDirectory>().KottonFabric());
        private void Place(GameItem item){GeneralHelper.SetItemOwned(item,true);item.EnableTag(MusicCatalog.ProtectedTag,false);if(!E.PlayerInventories().Any(inv=>NativePackageB.Place(inv,item)))throw new InvalidOperationException("请为挂件与便条腾出库存空间，原件仍保留。");}
        private void Issue()
        {
            var c=S.CampaignItems;if(!c.Originals.TryGetValue("c_keychain",out var keychain))return;
            if(S.CompletedDays.ContainsKey("ms_scene_017")&&N.Box==0)E.MutateNative(()=>{var box=DirectoryMaster.Item(BoxSku,true);Place(box);host.Engine.EditCampaignItems(s=>s.CampaignItems.NativeDiary.Box=box.uniqueId);},()=>{});
            if(keychain.Custodian=="player"&&keychain.Location=="custody"){
                if(!host.Campaign.ClaimOriginal("c_keychain"))throw new InvalidOperationException("请为同一挂件腾出空间。");
            }
            if(!N.ShopReadGranted&&!N.MahoroReturned&&keychain.Custodian=="player"&&E.FindPlayerItem(N.Box) is GameItem box){var item=E.FindPlayerItem(keychain.Entity);var inv=NativePackageB.Inventories(box).Single();if(item!=null&&item.parentInventory?.NativeIdentity()!=inv.NativeIdentity())E.MutateNative(()=>{if(!item.parentInventory.Expel(item)||!NativePackageB.Place(inv,item))throw new InvalidOperationException("挂件未能放入失物招领盒。");},()=>{});}
            if(!N.ShopReadGranted)return;
            if(N.WorkedDay<0&&!N.BlankIssued){var r=S.CampaignItems.Jobs[PackageC.DecodeJob].Resources.Single(x=>x.Entity==N.Blank);E.MutateNative(()=>{if(E.FindPlayerItem(r.Entity)!=null)throw new InvalidOperationException("本次空白卡已经存在，不能重复发放。");Place(NativeMemberEconomy.Decode(r.Snapshot));host.Engine.EditCampaignItems(s=>s.CampaignItems.NativeDiary.BlankIssued=true);},()=>{});}
            if(N.Note==0)E.MutateNative(()=>{var paper=DirectoryMaster.Item(NativePackageB.RepairNoteSku,true);Place(paper);host.Engine.EditCampaignItems(s=>s.CampaignItems.NativeDiary.Note=paper.uniqueId);},()=>{});
        }
        internal bool CompleteVisit(string scene)
        {
            if(!Enabled||scene is not ("ms_scene_017" or "ms_scene_018"))return false;
            E.Business.HoldStoryTrade(DiaryPresentation.Lead(scene));host.Engine.EditCampaignItems(s=>s.CampaignItems.NativeDiary.TradeVisit=scene);UpdateTrade();return true;
        }
        internal void FinishVisit(){if(!FinishedVisit)return;StoreUIManager.Instance.CloseAllUI();PlayerStore.instance.DismissCurrentClient(true,false,true);}
        private bool Commodity(GameItem item)=>item!=null&&!item.identifier.StartsWith("ms_")&&!item.IsTag(MusicCatalog.ProtectedTag)&&(Visit=="ms_scene_017"?item.GetGameItemType().Contains("FOOD")||item.GetGameItemType().Contains("DRINK")||item.IsTag("WATER"):item.identifier.Contains("cloth",StringComparison.OrdinalIgnoreCase)||item.identifier.Contains("fabric",StringComparison.OrdinalIgnoreCase));
        internal bool CanTrade(GameItem item)=>Trading&&GeneralHelper.IsItemOwned(item)&&E.FindPlayerItem(item.uniqueId)!=null&&Commodity(item);
        private void UpdateTrade()
        {
            if(!Trading)return;var c=PlayerStore.instance.currentClientInstance.storeClient;string actor=DiaryPresentation.Lead(Visit);c.clientIntent=StoreClient.ClientIntent.SELLNBUY;c.isMainDialogueStarted=c.isIntroduced=true;c.isRegularSellDisable=false;c.isBargainAvailable=true;c.clientBudget=c.clientCash=(int)Math.Min(int.MaxValue,S.Economy.Wallets[actor].Balance);c.useClientBudget=true;
            c.clientBuyingIdList??=new System.Collections.Generic.List<string>();foreach(var item in E.AllPlayerItems().Where(Commodity))if(!c.clientBuyingIdList.Contains(item.identifier))c.clientBuyingIdList.Add(item.identifier);
        }
        private static bool Sell(GameItem __0)
        {
            var p=Self;if(p?.Trading!=true||p.selling)return true;if(!p.CanTrade(__0)||p.E.IsTransferring)return false;var store=PlayerStore.instance;int cash=store.GetCash();string visit=p.Visit;
            p.E.MutateNative(()=>{p.selling=true;try{store.SellItem(__0);}finally{p.selling=false;}if(p.E.FindPlayerItem(__0.uniqueId)!=null&&GeneralHelper.IsItemOwned(p.E.FindPlayerItem(__0.uniqueId)))throw new InvalidOperationException("日用品尚未实际售出。");p.host.Engine.EditCampaignItems(s=>PackageC.DiaryTrade(s,"diary_sell_"+Guid.NewGuid().ToString("N"),visit,__0.uniqueId,__0.identifier,NativeMemberEconomy.Encode(__0),store.GetCash()-cash));},()=>{});return false;
        }
        private static void EndDay()=>Self?.Stage();
        private void Stage()
        {
            if(!Pending||N.WorkedDay>=0)return;var original=S.CampaignItems.Originals["c_keychain"];
            foreach(var machine in E.AllPlayerItems().Where(i=>TryWindow(i,out _)).ToArray()){var slots=DecoderSlots(machine);if(NativePackageB.ItemList(slots[0].childItems).Any(i=>i.uniqueId==original.Entity)){host.Engine.EditCampaignItems(s=>PackageC.PlanDiary(s,machine.uniqueId,host.Day));return;}}
        }
        private void Work()
        {
            if(N.Machine<=0||N.PlannedDay>=host.Day||N.WorkedDay>=0)return;
            var machine=E.FindPlayerItem(N.Machine);if(machine==null||!E.PackageB.Working(machine.uniqueId,machine.identifier)||!TryWindow(machine,out _))throw new InvalidOperationException("请保留放在店内的原版密码解序器。");
            var slots=DecoderSlots(machine);var keychain=NativePackageB.ItemList(slots[0].childItems).SingleOrDefault(i=>i.uniqueId==S.CampaignItems.Originals["c_keychain"].Entity);var card=NativePackageB.ItemList(slots[1].childItems).SingleOrDefault(i=>i.uniqueId==N.Blank&&i.identifier==N.BlankSku);var power=NativePackageB.ItemList(slots[2].childItems).SingleOrDefault();
            if(keychain==null||card==null)throw new InvalidOperationException("缺少同一挂件或随附空白卡；没有扣电，重新放好后再睡一天。");
            if(slots[3].childItems.Count!=0)throw new InvalidOperationException("请先腾空解序器输出槽；没有扣电或消耗空白卡。");
            if(power==null||!E.PackageB.PlayerOwned(power)||PowerHelper.GetAvailableEnergyFromItem(power,false)<6)throw new InvalidOperationException("请在解序器电源槽放入至少6E的自有电源；没有扣料或电。");
            int energy=PowerHelper.GetAvailableEnergyFromItem(power,false);
            E.MutateNative(()=>{
                if(!PowerHelper.DrawPowerSource(power,6))throw new InvalidOperationException("实际6E电量扣除失败。");card.identifier=ReadoutSku;card.name="授权读取结果卡";card.longDescription="本次读取结果只按柜台确认的方式展示。";card.SetValue(0);card.SetShape(new GridShapeBuilder(2,2).SetDataFill(2,2,1).Build());card.EnableTag(MusicCatalog.ProtectedTag,false);
                if(!card.parentInventory.Expel(card)||!NativePackageB.Place(slots[3],card))throw new InvalidOperationException("实际结果卡未能进入原版输出槽。");
                host.Engine.EditCampaignItems(s=>PackageC.WorkDiary(s,host.Day,machine.uniqueId,keychain.uniqueId,card.uniqueId,power.uniqueId,6,NativeMemberEconomy.Encode(keychain),NativeMemberEconomy.Encode(card)));
            },()=>PowerHelper.SetPowerSourceAt(power,energy));notice="读取已完成，按约来访时查看。";
        }
        internal void Update()
        {
            if(!Enabled||updating||E.IsTransferring||Time.unscaledTime<next)return;next=Time.unscaledTime+.4f;updating=true;
            try{
                if(N==null&&(S.CompletedDays.ContainsKey("ms_scene_016")||DiaryPresentation.Scene(host.Engine.Frame()?.Scene.Id))&&!S.CompletedDays.ContainsKey("ms_scene_020"))host.Engine.EditCampaignItems(PackageC.PrepareDiary);
                if(N==null)return;
                if(!core.Presentation.Active){try{Issue();}catch(InvalidOperationException e){notice=e.Message;}}
                try{Work();}catch(InvalidOperationException e){notice=e.Message;host.Engine.EditCampaignItems(s=>s.CampaignItems.NativeDiary.Machine=0);}
                UpdateTrade();
                if(S.CompletedDays.ContainsKey("ms_scene_020")&&!N.Closed)host.Engine.EditCampaignItems(s=>s.CampaignItems.NativeDiary.Closed=true);
            }finally{updating=false;}
        }
        internal string CallBlock(string actor)=>Pending&&actor=="ms_hotaru"&&N.WorkedDay<0?"先按授权完成挂件读取，再电话另约；已有的次日约定继续保留。":null;
        internal string CallText=>"读取已完成，按之前确认的方式明天来店查看吧。";
        internal bool CanStart(string scene)=>!Enabled||scene!="ms_scene_018"||N==null||E.FindPlayerItem(S.CampaignItems.Originals["c_keychain"].Entity)!=null;
        internal string Paper(int entity)
        {
            if(!Enabled||N==null)return null;
            if(entity==N.Note){decimal energy=E.AllPlayerItems().Where(i=>E.PackageB.PlayerOwned(i)&&i.IsTag("power_source_item")).Select(i=>PowerHelper.GetAvailableEnergyFromItem(i,false)).DefaultIfEmpty(0).Max();return "麻里奈姐姐：\n\n茉幌学姐已经把挂件和查看权限交给我。这次也确认了由您帮忙读取的方式。\n\n请把同一挂件放进原版密码解序器的输入槽，随附空白卡放空白卡槽，自有电源放电源槽，备好6E后睡觉过一天。这个委托按游戏日处理，不用等现实时间。\n\n我们按柜台约定次日过来；如果还没准备好，完成后再电话改约。请保留原件，结果按已确认的方案展示。\n\n萤\n\n"+(notice==""?(N.WorkedDay>=0?"已读取，待按约查看。":"待过夜读取。"):notice)+"\n读取需要至少6E；当前持有的单个自有电源最高"+energy+"E。缺机器或带电电源可联系8670200，按柜台报价采购。";}
            if(S.CampaignItems.Originals.GetValueOrDefault("c_diary_result")?.Entity!=entity)return null;
            if(N.RevealedMode==null)return "私人读取结果卡\n\n格式读取已完成；原件未损坏。结果等待萤按约到店后查看。\n\n此卡不是可出售的商品。";
            return "私人读取结果卡\n\n"+(N.RevealedMode=="filter"?"店长整理版；十月九日末尾扇区已按选择覆盖，此卡不能恢复该段。":N.RevealedMode=="raw"?"完整读取版，包含十月九日。萤要求暂时保密到比赛结束。":"完整读取版，已由五名成员共同面对。")+"\n\n保管人：萤。实际记录随剧情对话呈现。";
        }
        private static bool PaperAllowed(GameItem __instance,ref bool __result){if(__instance?.identifier!=ReadoutSku)return true;__result=true;return false;}
        private static bool PaperUse(GameItem __instance){if(__instance?.identifier!=ReadoutSku)return true;var p=Self;string text=p?.Paper(__instance.uniqueId);if(text!=null){p.core.EnsureChineseFallback();PaperUIManager.Instance.OpenUI("授权读取结果卡",text,"萤",0);}return false;}
    }
}
