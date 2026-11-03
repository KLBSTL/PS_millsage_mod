using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageB
    {
        private bool repairCounterExecuting;
        private void InstallRepairCounter()
        {
            foreach(string name in new[]{"MayActivateSlotItem","CanActivateSlotItem","MayToggleSlotItem","CanToggleSlotItem"})core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(GameItem),name),prefix:new HarmonyMethod(typeof(NativePackageB),nameof(RepairPaperAllowed)));
            foreach(string name in new[]{"ActivateSlotItem","ToggleSlotItem"})core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(GameItem),name),prefix:new HarmonyMethod(typeof(NativePackageB),nameof(RepairPaperUse)));
            InstallScannerReport();InstallBenchSaveHooks();
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(PlayerStore),"EndDay"),prefix:new HarmonyMethod(typeof(NativePackageB),nameof(RepairEndDay)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(PlayerStore),"SellItem"),prefix:new HarmonyMethod(typeof(NativePackageB),nameof(RepairNativeSell)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(PlayerStore),"CanSellThisItem"),prefix:new HarmonyMethod(typeof(NativePackageB),nameof(RepairLargeCanSell)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(PlayerStore),"SellItemFromTable"),prefix:new HarmonyMethod(typeof(NativePackageB),nameof(RepairLargeSell)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(NegociationUIManager),"OnOfferAcceptClick"),prefix:new HarmonyMethod(typeof(NativePackageB),nameof(RepairLargeAccept)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(GameItem),"GetCurrentChildValue"),prefix:new HarmonyMethod(typeof(NativePackageB),nameof(RepairChildValue)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(GameItem),"GetNegociatedValue"),postfix:new HarmonyMethod(typeof(NativePackageB),nameof(RepairNegociatedValue)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(NegociationUIManager),"GetSellableBackInvItems"),postfix:new HarmonyMethod(typeof(NativePackageB),nameof(RepairSellableOriginals)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(NegociationUIManager),"OnBackCounter"),postfix:new HarmonyMethod(typeof(NativePackageB),nameof(RepairOnBackCounter)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(NegociationUIManager),"GetOwnedBackInvItems"),postfix:new HarmonyMethod(typeof(NativePackageB),nameof(RepairVisibleOriginals)));
            // The native tablet validates selection separately from its row list.
            // Authorized large originals use the existing large counter grid.
        }
        private static void RepairOnBackCounter(GameItem __0,ref bool __result)
        {if(!__result&&RepairLargeItem(__0))__result=true;}
        private static void RepairVisibleOriginals(List<GameItem> __result)
        {
            var item=PlayerStore.instance?.currentNegociatedItem;
            if(__result!=null&&RepairLargeItem(item)&&!__result.Any(x=>x.uniqueId==item.uniqueId))__result.Add(item);
        }
        private static bool RepairLargeItem(GameItem item)
        {
            var p=RepairSelf;
            return p?.Enabled==true&&item!=null&&Core.Instance.Story.Inspection?.Managed(item)!=true&&GeneralHelper.IsItemOwned(item)&&item.parentInventory?.NativeIdentity()==EmporiumEntry.Instance.backInvinvElementCounter.NativeIdentity()&&p.RepairCounterOutgoing(item);
        }
        private static bool RepairLargeCanSell(GameItem __0,bool __1,ref bool __result)
        {
            if(!RepairLargeItem(__0))return true;var c=PlayerStore.instance.currentClientInstance?.storeClient;
            // Native CanSellThisItem rejects an empty small back grid before it
            // even checks the supplied item. Preserve its remaining conditions.
            bool affordable=c!=null&&(__1||!c.useClientBudget||c.HasBudgetLeftToBuy(__0));
            if(affordable&&!__1&&RepairSelf.economy.Trade.CounterUse(__0))affordable=__0.GetNegociatedValue()<=c.clientCash;
            __result=affordable&&c.IsClientBuyingThisItem(__0)&&!c.CanClientExposeAnyFeatureDeep(__0)&&(!c.isRequireVariety||!c.IsItemInVarietyList(__0));return false;
        }
        private static bool RepairLargeAccept(NegociationUIManager __instance)
        {
            if(!RepairLargeItem(PlayerStore.instance.currentNegociatedItem))return true;
            if(!NegociationUIManager.IsTradeLocked())__instance.TrySellItem();return false;
        }
        private static bool RepairLargeSell()
        {
            var store=PlayerStore.instance;var item=store.currentNegociatedItem;if(!RepairLargeItem(item))return true;
            if(NegociationUIManager.IsTradeLocked())return false;var c=store.currentClientInstance.storeClient;var ui=NegociationUIManager.Instance;
            if(c.CanClientExposeAnyFeatureDeep(item)){c.InspectItemDeep(item);ui.UpdateUI(item);return false;}
            if(!store.CanSellThisItem(item,false)){if(store.CanSellThisItem(item,true))ui.OpenConfirmPanel();return false;}
            store.SellItem(item);c.OnDealAccepted(null);return false;
        }
        private static bool RepairQuoteDispatch(NegociationUIManager __instance)
        {
            var p=RepairSelf;var store=PlayerStore.instance;var item=store?.currentNegociatedItem;
            if(p?.Enabled!=true||item==null||!__instance.uiObject.activeInHierarchy||NegociationUIManager.IsTradeLocked()||!p.RepairCounterOutgoing(item)||item.parentInventory?.NativeIdentity()!=EmporiumEntry.Instance.backInvinvElementCounter.NativeIdentity())return true;
            // Vanilla drops a selection outside the main quote inventory. Large
            // repair originals use its counter inventory; retain the same native
            // sell view across refreshes, including while its modal is displayed.
            __instance.currentNegociatedItem=item;
            if(!__instance.frame.confirmRoot.activeInHierarchy)__instance.UpdateUI(item);
            return false;
        }
        private static void RepairSellableOriginals(StoreClient __0,System.Collections.Generic.List<GameItem> __result)
        {
            var p=RepairSelf;if(p?.Enabled!=true||__result==null||__0?.identifier!=PlayerStore.instance.currentClientInstance?.storeClient?.identifier)return;
            foreach(var item in EmporiumEntry.Instance.GetBackInvItems())if(p.RepairCounterOutgoing(item)&&!ItemList(__result).Any(x=>x.uniqueId==item.uniqueId))__result.Add(item);
            var selected=PlayerStore.instance.currentNegociatedItem;
            if(selected!=null&&p.RepairCounterOutgoing(selected)&&!ItemList(__result).Any(x=>x.uniqueId==selected.uniqueId)&&p.economy.PlayerInventories().Any(inv=>inv.NativeIdentity()==selected.parentInventory?.NativeIdentity()))__result.Add(selected);
        }
        private static bool RepairChildValue(GameItem __instance,ref int __result){var p=RepairSelf;if(p?.Enabled==true&&p.IsRepairGear(__instance)&&p.TryRepairCounterValue(__instance,out _)){__result=0;return false;}return true;}
        private static void RepairNegociatedValue(GameItem __instance,ref long __result)
        {
            var p=RepairSelf;if(p?.Enabled!=true||!p.TryRepairCounterValue(__instance,out int reference))return;
            // A repair is quoted as a service, not as another sale of its host
            // and installed parts. Keep vanilla final-stage bargaining factors.
            long innate=0,market=0;double final=1;
            __instance.AccumulateFeatureStages(ref innate,ref market,ref final,false);
            __result=GameItem.ComposeStagedValue(reference,0,0,final,0,0);
            if(p.economy.Trade.CounterUse(__instance))__result=Math.Min(__result,p.economy.Trade.UseReturnCeiling(__instance));
        }
        private GameItem RepairActualItem(int id)
        {
            var item=economy.FindPlayerItem(id);if(item!=null)return item;
            foreach(var grid in new[]{EmporiumEntry.Instance?.frontInvinvElement,PlayerStore.instance?.currentClientInstance?.gridInv})if(grid!=null)foreach(var root in ItemList(grid.childItems)){if(root.uniqueId==id)return root;foreach(var child in root.FindAllChildItems())if(child.uniqueId==id)return child;}return null;
        }
        internal bool RepairOfferVisible(int id)=>RepairActualItem(id)?.parentInventory?.NativeIdentity()==EmporiumEntry.Instance.frontInvinvElement.NativeIdentity()&&EmporiumEntry.Instance.frontInvWindow.IsVisible();
        internal object RepairOfferStatus()=>new{selected=PlayerStore.instance.currentNegociatedItem?.uniqueId,selectedSku=PlayerStore.instance.currentNegociatedItem?.identifier,frontVisible=EmporiumEntry.Instance.frontInvWindow.IsVisible(),front=ItemList(EmporiumEntry.Instance.frontInvinvElement.childItems).Select(x=>new{id=x.uniqueId,sku=x.identifier}).ToArray(),stock=ItemList(PlayerStore.instance.currentClientInstance.gridInv.childItems).Select(x=>new{id=x.uniqueId,sku=x.identifier,incoming=RepairCounterIncoming(x),fits=EmporiumEntry.Instance.frontInvinvElement.TryFindOneValidInventorySlot(x)!=null}).ToArray()};
        private void PresentRepairOffer()
        {
            var store=PlayerStore.instance;var entry=EmporiumEntry.Instance;var front=entry.frontInvinvElement;
            // Client stock is a hidden source inventory. Offer one original on
            // the vanilla customer counter; leave any current negotiation alone.
            if(front.childItems.Count!=0||store.currentNegociatedItem?.parentInventory?.NativeIdentity()==entry.backInvinvElementCounter.NativeIdentity())return;
            var source=store.currentClientInstance.gridInv;
            var item=ItemList(source.childItems).FirstOrDefault(RepairCounterIncoming);
            if(item==null||front.TryFindOneValidInventorySlot(item)==null)return;
            repairMove=true;
            try{
                if(!source.Expel(item))return;
                if(!Place(front,item)){source.UncheckedAccept(item);return;}
                entry.ShowFrontArea();store.currentNegociatedItem=item;
                store.PlacedItemForBuying(item);NegociationUIManager.Instance.UpdateUI(item);
            }finally{repairMove=false;}
        }
        private RepairNote RepairRequestFor(int id)=>A.RepairNotes.Values.Where(x=>x.Host==id).OrderBy(x=>x.Day).ThenBy(x=>x.Id).LastOrDefault();
        internal bool RepairCounterIncoming(GameItem item)
        {
            if(economy.Trade?.UseIncoming(item)==true)return true;
            if(item==null||!Enabled||!IsRepairGear(item)||GeneralHelper.IsItemOwned(item))return false;
            var note=RepairRequestFor(item.uniqueId);return note!=null&&note.Actor==economy.Business.CurrentActor&&note.Service==null&&host.Engine.Snapshot().Economy.OwnedItems.GetValueOrDefault(item.uniqueId)?.ActorId==note.Actor;
        }
        internal bool RepairCounterOutgoing(GameItem item)
        {
            if(economy.Trade?.CounterUse(item)==true)return true;
            if(host.Inspection?.Counter(item)==true)return true;
            if(host.Return?.Counter(item)==true||host.Native012?.Counter(item)==true||host.ActTwo?.Counter(item)==true)return true;
            if(item==null||!Enabled||!IsRepairGear(item)||!GeneralHelper.IsItemOwned(item))return false;
            var svc=ServiceFor(item.uniqueId);return svc!=null&&svc.Actor!="player"&&svc.Actor==economy.Business.CurrentActor&&RepairCallBlock(svc.Actor)==null;
        }
        internal bool RepairCounterSupported(GameItem item)=>RepairCounterIncoming(item)||RepairCounterOutgoing(item);
        private bool TryRepairCounterValue(GameItem item,out int value)
        {
            if(economy.Trade?.UseIncoming(item)==true){value=economy.Trade.UseAcquisitionPrice(item);return true;}
            if(economy.Trade?.CounterUse(item)==true){value=economy.Trade.UseReturnPrice(item);return true;}
            if(host.Inspection?.Value(item,out value)==true)return true;
            if(host.Return?.Value(item,out value)==true)return true;
            if(host.Native012?.Value(item,out value)==true)return true;
            if(host.ActTwo?.Value(item,out value)==true)return true;
            value=0;if(!Enabled||item==null)return false;
            if(RepairCounterIncoming(item)){value=PackageB.RepairAcquisitionPrice(PackageB.LeafValue(A,item.uniqueId,item.identifier));return true;}
            if(RepairCounterOutgoing(item)){var svc=ServiceFor(item.uniqueId);value=checked((RepairRequestFor(item.uniqueId)?.PurchasePrice??0)+PackageB.Suggested(svc));return true;}return false;
        }
        internal bool TryBuyRepairFromCounter()
        {
            var store=PlayerStore.instance;var item=store.currentNegociatedItem;if(!RepairCounterIncoming(item))return false;
            if(economy.IsTransferring||repairCounterExecuting)return false;
            if(economy.Trade.UseIncoming(item)){
                economy.Trade.BuyUseFromCounter(item,()=>{
                    repairCounterExecuting=true;
                    try{store.BuyItemFromTable();}finally{repairCounterExecuting=false;}
                });
                return true;
            }
            var note=RepairRequestFor(item.uniqueId);int before=store.GetCash();string service="repair_"+Guid.NewGuid().ToString("N"),receipt="purchase_"+Guid.NewGuid().ToString("N");
            economy.MutateNative(()=>{
                repairCounterExecuting=true;try{store.BuyItemFromTable();}finally{repairCounterExecuting=false;}
                int paid=before-store.GetCash();var actual=economy.FindPlayerItem(item.uniqueId);
                if(paid<0||actual==null||!GeneralHelper.IsItemOwned(actual))throw new InvalidOperationException("原版收购没有完成，保留原件");
                string snapshot=NativeMemberEconomy.Encode(actual);
                host.Engine.EditPackageB(s=>{
                    PackageB.Authorize(s,service,note.Actor,item.uniqueId,snapshot);s.Economy.OwnedItems.Remove(item.uniqueId);
                    s.Economy.Wallets[note.Actor].Balance=checked(s.Economy.Wallets[note.Actor].Balance+paid);
                    if(paid>0)s.Economy.Trades.Add(receipt,new MemberTradeReceipt{ActorId=note.Actor,EntityId=item.uniqueId,MemberBuys=false,Price=paid});
                    var paper=s.Workshop.Advanced.RepairNotes[note.Id];paper.Service=service;paper.PurchasePrice=paid;paper.PurchaseReceipt=receipt;
                });
            },()=>{});
            RefreshRepairViews();return true;
        }
        internal bool RepairCounterExecuting=>repairCounterExecuting;
        private static bool RepairNativeSell(GameItem __0)
        {
            if(RepairSelf?.economy.Trade?.HoldsNativeUse(__0)==true)return true;
            if(Core.Instance?.Story?.Inspection?.Managed(__0)==true||Core.Instance?.Story?.Return?.Counter(__0)==true||Core.Instance?.Story?.Native012?.Counter(__0)==true||Core.Instance?.Story?.ActTwo?.Counter(__0)==true)return true;
            var p=RepairSelf;if(p==null||p.repairCounterExecuting||!p.RepairCounterOutgoing(__0))return true;
            var store=PlayerStore.instance;var svc=p.ServiceFor(__0.uniqueId);int before=store.GetCash();long wallet=p.host.Engine.Snapshot().Economy.Wallets[svc.Actor].Balance;
            string receipt="counter_"+svc.Id;
            Action apply=()=>{
                p.repairCounterExecuting=true;try{store.SellItem(__0);}finally{p.repairCounterExecuting=false;}
                int paid=store.GetCash()-before;if(paid<0||paid>wallet||p.economy.FindPlayerItem(__0.uniqueId)!=null&&GeneralHelper.IsItemOwned(p.economy.FindPlayerItem(__0.uniqueId)))throw new InvalidOperationException("原版出售没有完成，保留原件与资金");
                string snapshot=NativeMemberEconomy.Encode(__0);
                p.host.Engine.EditPackageB(s=>{s.Workshop.Advanced.Services[svc.Id].HostSnapshot=snapshot;PackageB.Deliver(s,svc.Id,receipt,paid,p.host.Day);if(paid>0)s.Economy.Trades[receipt]=new MemberTradeReceipt{ActorId=svc.Actor,EntityId=svc.Host,MemberBuys=true,Price=paid};});
            };
            try{if(p.economy.IsTransferring)apply();else p.economy.MutateNative(apply,()=>{});}catch{p.RefreshRepairViews();throw;}
            return false;
        }
        private void PrepareRepairCounter()
        {
            foreach(var service in A.Services.Values.Where(x=>!x.Terminal&&x.Delivery==null)){
                var legacy=economy.FindPlayerItem(service.Host);if(legacy!=null&&!GeneralHelper.IsItemOwned(legacy))GeneralHelper.SetItemOwned(legacy,true);
            }
            string actor=economy.Business.CurrentActor;var store=PlayerStore.instance;
            if(!store.isClientArrived||store.currentClientInstance==null)return;
            if(actor==null){
                if(economy.Trade?.IsUseClient(store.currentClientInstance.storeClient?.identifier)==true)PresentRepairOffer();
                return;
            }
            var client=store.currentClientInstance.storeClient;long wallet=host.Engine.Snapshot().Economy.Wallets[actor].Balance;
            client.clientIntent=StoreClient.ClientIntent.SELLNBUY;client.useClientBudget=true;client.clientCash=(int)Math.Min(int.MaxValue,wallet);client.clientBudget=client.clientCash;
            client.buyPriceModifier=0;client.sellPriceModifier=0;
            client.isRegularSellDisable=false;client.isRequireVariety=false;
            if(client.clientBuyingIdList==null)client.clientBuyingIdList=new System.Collections.Generic.List<string>();
            foreach(var row in PackageBCatalog.Rows("equipment_package_b").Where(x=>x["actor"]==actor))if(!client.clientBuyingIdList.Contains(row["id"]))client.clientBuyingIdList.Add(row["id"]);
            foreach(var owned in host.Engine.Snapshot().Economy.OwnedItems.Values.Where(x=>x.ActorId==actor&&IsRepairSku(x.ItemId)).ToArray()){
                var prior=RepairRequestFor(owned.EntityId);bool closed=prior?.Service!=null&&(A.Services[prior.Service].Delivery!=null||A.Services[prior.Service].Terminal||A.Services[prior.Service].Status=="return_ready");
                if(closed){var done=A.Services[prior.Service];int day=A.Ledger.GetValueOrDefault(done.Delivery??(done.Id+"_cancel"))?.Day??prior.Day;if(day>=host.Day)continue;prior=null;}
                string key=prior?.Id??actor+"_"+owned.EntityId+"_"+host.Day;
                if(prior==null||economy.FindPlayerItem(prior.Entity)==null){
                    var paper=DirectoryMaster.Item(RepairNoteSku,true);bool placed=false;
                    economy.MutateNative(()=>{EmporiumEntry.Instance.TryAddToPlayerInv(paper);placed=economy.FindPlayerItem(paper.uniqueId)!=null;if(placed)host.Engine.EditPackageB(s=>{if(prior==null)s.Workshop.Advanced.RepairNotes[key]=new RepairNote{Id=key,Actor=actor,Host=owned.EntityId,Entity=paper.uniqueId,Day=host.Day,Demand=PackageB.RepairDemand(s,actor,owned.EntityId)?.GetValueOrDefault("id")};else s.Workshop.Advanced.RepairNotes[key].Entity=paper.uniqueId;});},()=>{if(placed)paper.parentInventory?.Expel(paper);});
                }
                if(RepairActualItem(owned.EntityId)==null){var item=NativeMemberEconomy.Decode(owned.NativeSnapshot);GeneralHelper.SetItemOwned(item,false);item.SetValue(PackageB.RepairAcquisitionPrice(PackageB.LeafValue(A,item.uniqueId,item.identifier)));repairMove=true;try{if(!store.currentClientInstance.gridInv.UncheckedAccept(item))throw new InvalidOperationException("物主原件未能放入原生货架");}finally{repairMove=false;}}
            }
            PresentRepairOffer();
        }
        private static bool IsRepairSku(string sku)=>PackageBCatalog.Rows("equipment_package_b").Any(x=>x["id"]==sku);
    }
}
