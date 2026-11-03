using System;
using System.Linq;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    // Controlled counterparties, native prices and native settlement. This
    // harness never adds goods to the player or adjusts the player's cash.
    internal static class NativeOperatingMarket
    {
        internal sealed class Result {public string Kind{get;set;}public string Sku{get;set;}public int Entity{get;set;}public int Expected{get;set;}public int Actual{get;set;}public int Before{get;set;}public int After{get;set;}}
        internal static StoreClientInstance Trader(bool selling)
        {
            for(int n=0;n<20;n++){
                var client=selling?StoreClientList.CreateLowerLevelRareMerchant():StoreClientList.CreateMerchant();
                if(client==null || client.IsClientSelling()!=selling || (selling && client.isRegularSellDisable) || (!selling && client.clientIntent!=StoreClient.ClientIntent.BUY))continue;
                client.clientCash=100000;client.clientBudget=100000;client.useClientBudget=true;
                client.clientFaction=StoreClient.FACTION_MIDDLE;
                if(!selling)client.clientBuyingTagList.Add("MATERIAL");
                return StoreClientInstance.CreateClientInstance(client);
            }
            throw new InvalidOperationException("No registered native market counterparty available");
        }
        internal static Result Buy(string sku)
        {
            var store=PlayerStore.instance;var prior=store.currentClientInstance;var oldItem=store.currentNegociatedItem;
            var vendor=Trader(true);foreach(var old in NativePackageB.ItemList(vendor.gridInv.childItems).ToArray())vendor.gridInv.Expel(old);
            var product=DirectoryMaster.Item(sku,false);if(product==null || product.identifier!=sku)throw new InvalidOperationException("Unregistered purchased SKU: "+sku);
            var zones=PackageBCatalog.Item(sku)?["source_zones"]??MusicCatalog.Find(sku)?.Zones?.FirstOrDefault();
            vendor.storeClient.clientFaction=zones?.Contains("upper")==true?StoreClient.FACTION_UPPER:StoreClient.FACTION_MIDDLE;
            Core.Instance.Story.Economy.PackageB.ApplyFacts(product,"ordinary","outside","vendor:controlled_operating_trial");
            if(!NativePackageB.Place(vendor.gridInv,product))throw new InvalidOperationException("Native seller inventory cannot hold "+sku);
            try{
                store.currentClientInstance=vendor;store.currentNegociatedItem=product;
                // The native purchase button requires a vendor item on the
                // back counter, even when its negotiated item is already set.
                EmporiumEntry.Instance.TransferOwnedItemBackToInv();
                vendor.gridInv.Expel(product);
                var counter=EmporiumEntry.Instance.backInvinvElement;var slot=counter.TryFindOneValidInventorySlot(product,false);
                if(slot==null || slot.TryAcceptOnce(product.unitCount)!=product.unitCount)throw new InvalidOperationException("Native buying counter cannot hold "+sku+"; shape="+counter.inventoryShape.width+"x"+counter.inventoryShape.height+" count="+counter.childItems.Count);
                int price=checked((int)product.GetNegociatedValue()),before=store.GetCash();
                if(before<price)throw new InvalidOperationException("Insufficient operating cash for "+sku+": "+before+" < "+price);
                store.BuyItemFromTable();int after=store.GetCash();
                if(Core.Instance.Story.Economy.FindPlayerItem(product.uniqueId)==null)throw new InvalidOperationException("Native purchase did not transfer "+sku+"; before="+before+" after="+after+" parent="+product.parentInventory?.NativeIdentity()+"; roots="+string.Join(",",Core.Instance.Story.Economy.PlayerInventories().Select(x=>x.NativeIdentity()+":"+x.childItems.Count)));
                return new Result{Kind="purchase",Sku=sku,Entity=product.uniqueId,Expected=price,Actual=before-after,Before=before,After=after};
            }finally{store.currentClientInstance=prior;store.currentNegociatedItem=oldItem;}
        }
        internal static Result Sell(GameItem item,bool wholesale=false)
        {
            var store=PlayerStore.instance;var prior=store.currentClientInstance;var oldItem=store.currentNegociatedItem;
            var buyer=Trader(false);buyer.storeClient.isOfferWholesale=wholesale;buyer.storeClient.wholesaleDiscount=30;
            try{
                store.currentClientInstance=buyer;store.currentNegociatedItem=item;
                int expected=checked((int)item.GetNegociatedValue()),before=store.GetCash();
                store.SellItemFromTable();int after=store.GetCash();
                return new Result{Kind=wholesale?"sale_with_wholesale_client":"sale",Sku=item.identifier,Entity=item.uniqueId,Expected=expected,Actual=after-before,Before=before,After=after};
            }finally{store.currentClientInstance=prior;store.currentNegociatedItem=oldItem;}
        }
    }
}
