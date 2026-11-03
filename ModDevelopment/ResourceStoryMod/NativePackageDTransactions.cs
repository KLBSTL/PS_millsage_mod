using System;
using System.Collections.Generic;
using System.Linq;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageD
    {
        internal sealed class Transaction
        {
            internal int Cash;
            internal HashSet<int> Owned;
            internal Dictionary<int,int> Quotes=new();
            internal StoreClient Client;
            internal int ClientCash,Budget,BuyModifier,SellModifier;
            internal bool UseBudget,BusinessComplete,Introduced,DialogueStarted,AcceptDeal,Wholesale,RequireVariety,MultiBuy;
            internal string[] Variety;
            internal StoreClient[] Queue;
        }
        private static bool BeforeWholesale(NegociationUIManager __instance)
        {
            var trade=Self;
            if(trade?.Enabled!=true||trade.economy.IsTransferring||NegociationUIManager.IsTradeLocked())return true;
            var client=PlayerStore.instance?.currentClientInstance?.storeClient;
            if(client?.isOfferWholesale!=true||!client.IsClientSelling())return true;
            trade.economy.MutateNative(()=>__instance.OnWholesaleClick(),()=>{});
            return false;
        }
        internal Transaction BeforeMutation()
        {
            if(!Enabled)return null;var store=PlayerStore.instance;var c=store.currentClientInstance?.storeClient;
            var queued=new List<StoreClient>();foreach(var client in store.storeClientManager.clientStack)queued.Add(client);
            var t=new Transaction{Cash=store.GetCash(),Owned=economy.AllPlayerItems().Where(GeneralHelper.IsItemOwned).Select(x=>x.uniqueId).ToHashSet(),Client=c,Queue=queued.ToArray()};
            if(c!=null){t.ClientCash=c.clientCash;t.Budget=c.clientBudget;t.UseBudget=c.useClientBudget;t.BuyModifier=c.buyPriceModifier;t.SellModifier=c.sellPriceModifier;t.BusinessComplete=c.isBusinessComplete;t.Introduced=c.isIntroduced;t.DialogueStarted=c.isMainDialogueStarted;t.AcceptDeal=c.acceptDealDialoguePlayed;t.Wholesale=c.isOfferWholesale;t.RequireVariety=c.isRequireVariety;t.MultiBuy=c.isMultiBuyDisabled;t.Variety=c.varietyTrackingList?.ToArray()??Array.Empty<string>();}
            if(c?.IsClientSelling()==true){
                foreach(var item in NativePackageB.ItemList(store.currentClientInstance.gridInv.childItems)
                    .Concat(NativePackageB.ItemList(EmporiumEntry.Instance.frontInvinvElement.childItems))
                    .Concat(economy.AllPlayerItems()).Where(x=>!GeneralHelper.IsItemOwned(x)).GroupBy(x=>x.uniqueId).Select(x=>x.First()))t.Quotes[item.uniqueId]=checked((int)Math.Max(0,item.GetNegociatedValue()));
            }
            return t;
        }
        internal void AfterMutation(Transaction t)
        {
            if(t==null)return;var store=PlayerStore.instance;
            if(t.Client?.IsClientSelling()==true && store.GetCash()<t.Cash){
                var acquired=economy.AllPlayerItems().Where(GeneralHelper.IsItemOwned).Where(x=>!t.Owned.Contains(x.uniqueId)&&!HoldsNativeUse(x)).GroupBy(x=>x.uniqueId).Select(x=>x.First()).ToArray();
                if(acquired.Length>0){
                    var weights=acquired.ToDictionary(x=>x.uniqueId,x=>t.Quotes.GetValueOrDefault(x.uniqueId,checked((int)Math.Max(0,x.GetCurrentValue()))));
                    foreach(var item in acquired)economy.Business.RecordNativeOwnership(item.uniqueId,true);
                    string visit=host.Engine.Snapshot().Workshop.Advanced.CurrentVisit??"native_"+host.Day+"_"+t.Client.identifier;
                    string receipt="buy_"+PackageB.Hash(visit+"/"+string.Join("/",weights.Keys.OrderBy(x=>x))).Substring(0,20);
                    host.Engine.EditMusicTrade(s=>{PackageD.Purchase(s,receipt,visit,host.Day,t.Cash-store.GetCash(),weights);foreach(var item in acquired){s.MusicTrade.Costs[item.uniqueId].Units=item.unitCount;PackageDFull.History(s,"acquired_"+receipt+"_"+item.uniqueId,item.uniqueId,host.Day,"purchase","实际付款收购，来源和原件身份已登记。");}});
                }
            }
            Capture();
        }
        internal void RollbackMutation(Transaction t)
        {
            if(t==null)return;var queue=PlayerStore.instance.storeClientManager.clientStack;queue.Clear();foreach(var saved in t.Queue)queue.Add(saved);
            if(t.Client==null)return;var c=t.Client;c.clientCash=t.ClientCash;c.clientBudget=t.Budget;c.useClientBudget=t.UseBudget;c.buyPriceModifier=t.BuyModifier;c.sellPriceModifier=t.SellModifier;c.isBusinessComplete=t.BusinessComplete;c.isIntroduced=t.Introduced;c.isMainDialogueStarted=t.DialogueStarted;c.acceptDealDialoguePlayed=t.AcceptDeal;c.isOfferWholesale=t.Wholesale;c.isRequireVariety=t.RequireVariety;c.isMultiBuyDisabled=t.MultiBuy;
            c.varietyTrackingList.Clear();foreach(string v in t.Variety)c.varietyTrackingList.Add(v);
        }
    }
}
