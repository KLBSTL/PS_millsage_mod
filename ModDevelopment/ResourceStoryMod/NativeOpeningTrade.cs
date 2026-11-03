using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    internal sealed partial class NativeOpeningCampaign
    {
        private bool executing;
        private static readonly Dictionary<string,string> foodSkus=new();
        internal bool Executing=>executing;
        private static NativeOpeningCampaign Self=>Core.Instance?.Story?.Opening;
        private void InstallTrade()
        {
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(PlayerStore),"SellItem"),prefix:new HarmonyMethod(typeof(NativeOpeningCampaign),nameof(Sell)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(StoreBell),"RingBell"),prefix:new HarmonyMethod(typeof(NativeOpeningCampaign),nameof(BeforeBell)));
        }
        internal bool FinishedVisit=>OwnVisit&&host.Engine.Frame()==null&&S.CompletedDays.ContainsKey(VisitScene)&&PlayerStore.instance.isClientArrived;
        private bool CanLeave=>FinishedVisit&&!E.IsTransferring&&!executing&&DialogUIManager.Instance.isConversationActiveFlag==0&&PlayerStore.instance.lockAllFlag==0&&PlayerStore.instance.lockClientClickFlag==0;
        private static void BeforeBell(){if(Self?.CanLeave==true)StoreUIManager.Instance.CloseAllUI();}
        internal void FinishVisit(){if(!CanLeave)return;StoreUIManager.Instance.CloseAllUI();if(PlayerStore.instance.lockNextBellFlag==0)PlayerStore.instance.DismissCurrentClient(true,false,true);}
        internal bool Commodity(GameItem item)=>item!=null&&PackageC.OpeningCommodity(VisitScene,item.identifier,string.Join(" ",item.GetGameItemType()),item.IsTag(MusicCatalog.ProtectedTag));
        internal static GameItem NativeFood(string factory)
        {
            if(!foodSkus.TryGetValue(factory,out string sku)){
                var method=typeof(FoodItemDirectory).GetMethod(factory,System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static,null,Type.EmptyTypes,null);if(method==null)throw new InvalidOperationException("原版食品工厂缺失："+factory);
                object target=null;if(!method.IsStatic){var dirs=Resources.FindObjectsOfTypeAll(NativeTypes.Of<FoodItemDirectory>());if(dirs.Length==0)throw new InvalidOperationException("原版食品目录未就绪");target=dirs[0].Cast<FoodItemDirectory>();}
                var template=(GameItem)method.Invoke(target,null);
                var ids=DirectoryMaster.GetIdentifierList<GameItem>(null);
                string token=factory.Contains("Noodle")?"noodle":factory.Contains("Beer")?"bottle":"water";
                var candidates=new List<string>();foreach(string id in ids)if(!string.IsNullOrEmpty(id)&&!id.StartsWith("ms_"))candidates.Add(id);
                foreach(string id in candidates.OrderByDescending(x=>x.IndexOf(token,StringComparison.OrdinalIgnoreCase)>=0)){
                    var candidate=DirectoryMaster.Item(id,false);
                    if(candidate!=null&&!string.IsNullOrEmpty(template.name)&&candidate.name==template.name&&!string.IsNullOrEmpty(candidate.identifier)){sku=candidate.identifier;break;}
                }
                if(sku==null)throw new InvalidOperationException("无法在原版目录匹配食品："+factory+" name="+template.name);
                foodSkus.Add(factory,sku);
            }
            var item=DirectoryMaster.Item(sku,true);item.GetUniqueID();return item;
        }
        private void UpdateTrade()
        {
            var store=PlayerStore.instance;var c=store.currentClientInstance.storeClient;
            string scene=VisitScene,actor=PackageC.OpeningActor(scene);
            c.clientIntent=StoreClient.ClientIntent.SELLNBUY;
            c.isMainDialogueStarted=true;c.isIntroduced=true;
            c.isRegularSellDisable=false;c.isBargainAvailable=true;
            c.clientCash=(int)Math.Min(int.MaxValue,S.Economy.Wallets[actor].Balance);c.clientBudget=c.clientCash;c.useClientBudget=true;
            if(c.clientBuyingIdList==null)c.clientBuyingIdList=new System.Collections.Generic.List<string>();
            foreach(var item in E.AllPlayerItems().Where(Commodity))if(!c.clientBuyingIdList.Contains(item.identifier))c.clientBuyingIdList.Add(item.identifier);
            if(!O.StockPrepared.Contains(scene)){
                var goods=scene=="ms_scene_001"?new[]{"EmptyBeerBottle"}:scene=="ms_scene_002"?new[]{"CupNoodle"}:Array.Empty<string>();
                E.MutateNative(()=>{
                    foreach(string factory in goods){
                        var item=NativeFood(factory);item.unitCount=1;GeneralHelper.SetItemOwned(item,false);
                        if(!NativePackageB.Place(store.currentClientInstance.gridInv,item))throw new InvalidOperationException("顾客物品空间不足");
                        string snapshot=NativeMemberEconomy.Encode(item);
                        host.Engine.EditCampaignItems(s=>{s.CampaignItems.Opening.Offers.Add(item.uniqueId,new(){Actor=actor,Scene=scene,Snapshot=snapshot});s.Economy.OwnedItems.Add(item.uniqueId,new(){ActorId=actor,EntityId=item.uniqueId,ItemId=item.identifier,NativeSnapshot=snapshot});});
                    }
                    host.Engine.EditCampaignItems(s=>s.CampaignItems.Opening.StockPrepared.Add(scene));
                },()=>{});
            }
            var front=EmporiumEntry.Instance.frontInvinvElement;
            bool quoting=Incoming(store.currentNegociatedItem)&&new[]{EmporiumEntry.Instance.backInvinvElement,EmporiumEntry.Instance.backInvinvElementCounter}.Any(inv=>inv.NativeIdentity()==store.currentNegociatedItem.parentInventory?.NativeIdentity());
            if(front.childItems.Count==0&&!quoting){
                var item=NativePackageB.ItemList(store.currentClientInstance.gridInv.childItems).FirstOrDefault(x=>O.Offers.ContainsKey(x.uniqueId));
                if(item!=null&&front.TryFindOneValidInventorySlot(item)!=null&&store.currentClientInstance.gridInv.Expel(item)){
                    if(!NativePackageB.Place(front,item)){store.currentClientInstance.gridInv.UncheckedAccept(item);return;}
                    // Front stock is only displayed. Native drag onto the player
                    // table selects a quote; showing stock must not open one.
                    EmporiumEntry.Instance.ShowFrontArea();
                }
            }
        }
        internal bool Incoming(GameItem item)=>Trading&&item!=null&&!GeneralHelper.IsItemOwned(item)&&O.Offers.TryGetValue(item.uniqueId,out var offer)&&offer.Scene==VisitScene&&offer.Actor==PackageC.OpeningActor(VisitScene);
        internal void BuyFromTable()
        {
            var store=PlayerStore.instance;var item=store.currentNegociatedItem;if(!Incoming(item)||executing||E.IsTransferring)return;
            int before=store.GetCash();string scene=VisitScene;
            E.MutateNative(()=>{
                executing=true;try{store.BuyItemFromTable();}finally{executing=false;}
                var owned=E.FindPlayerItem(item.uniqueId);int paid=before-store.GetCash();
                if(owned==null||!GeneralHelper.IsItemOwned(owned)){if(paid!=0)throw new InvalidOperationException("日用品收购现金与归属不符");return;}
                host.Engine.EditCampaignItems(s=>PackageC.OpeningTrade(s,"opening_buy_"+Guid.NewGuid().ToString("N"),scene,item.uniqueId,item.identifier,NativeMemberEconomy.Encode(owned),false,paid));
            },()=>{});
        }
        private static bool Sell(GameItem __0)
        {
            var p=Self;if(p?.Trading!=true||p.executing||!p.Commodity(__0))return true;
            // A customer's unsold offer can never be paid out as a player sale,
            // including stale UI references retained after a native cancellation.
            if(p.O.Offers.ContainsKey(__0.uniqueId)||p.S.Economy.OwnedItems.ContainsKey(__0.uniqueId)||!GeneralHelper.IsItemOwned(__0)||p.E.FindPlayerItem(__0.uniqueId)==null)return false;
            if(p.E.IsTransferring)return false;var store=PlayerStore.instance;int before=store.GetCash();string scene=p.VisitScene;
            p.E.MutateNative(()=>{
                p.executing=true;try{store.SellItem(__0);}finally{p.executing=false;}
                if(p.E.FindPlayerItem(__0.uniqueId)!=null&&GeneralHelper.IsItemOwned(p.E.FindPlayerItem(__0.uniqueId)))throw new InvalidOperationException("原版日用品交易尚未完成");
                int paid=store.GetCash()-before;
                p.host.Engine.EditCampaignItems(s=>PackageC.OpeningTrade(s,"opening_sell_"+Guid.NewGuid().ToString("N"),scene,__0.uniqueId,__0.identifier,NativeMemberEconomy.Encode(__0),true,paid));
            },()=>{});return false;
        }
    }
}
