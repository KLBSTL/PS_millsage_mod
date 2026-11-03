using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using HarmonyLib;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageB
    {
        private bool supplying;
        private string restoringVisit;
        internal bool RestoringSupplier=>restoringVisit!=null;
        internal void CaptureSupplier()
        {
            if(!Enabled || restoringVisit!=null)return;var current=PlayerStore.instance.currentClientInstance;string id=A.CurrentVisit;
            if(current?.gridInv==null || id==null || !A.Visits.TryGetValue(id,out var v) || v.Status is not ("ready" or "pending") || current.storeClient.identifier!=v.Vendor&&!economy.Trade.IsReturnedSeller(current.storeClient.identifier))return;
            var entry=EmporiumEntry.Instance;
            var known=v.NativeStock.Select(x=>x.Entity).ToHashSet();
            var counter=ItemList(entry.frontInvinvElement.childItems).Concat(ItemList(entry.backInvinvElement.childItems))
                .Where(x=>known.Contains(x.uniqueId)&&!GeneralHelper.IsItemOwned(x));
            var stock=ItemList(current.gridInv.childItems).Where(x=>!GeneralHelper.IsItemOwned(x)).Concat(counter).GroupBy(x=>x.uniqueId).Select(x=>NativeWorkshop.Store(x.First())).ToList();
            var c=current.storeClient;
            if(v.StockCaptured && v.NativeBlueprintCaptured && v.NativeCash==c.clientCash && v.NativeBudget==c.clientBudget && v.NativeUseBudget==c.useClientBudget && v.NativeBuyModifier==c.buyPriceModifier&&v.NativeSellModifier==c.sellPriceModifier&&v.NativeMarkup==c.acceptRetailMarkup&&v.NativeWholesale==c.isOfferWholesale&&v.NativeRequireVariety==c.isRequireVariety&&v.NativeVariety.SequenceEqual(c.varietyTrackingList.ToArray())&&v.NativeStock.Select(x=>x.Snapshot).SequenceEqual(stock.Select(x=>x.Snapshot)))return;
            host.Engine.EditPackageB(s=>{var saved=s.Workshop.Advanced.Visits[id];saved.NativeStock=stock;saved.StockCaptured=true;saved.NativeCash=c.clientCash;saved.NativeBudget=c.clientBudget;saved.NativeUseBudget=c.useClientBudget;saved.NativeBlueprintCaptured=true;saved.NativeSprite=c.spriteName;saved.NativeBuyModifier=c.buyPriceModifier;saved.NativeSellModifier=c.sellPriceModifier;saved.NativeMarkup=c.acceptRetailMarkup;saved.NativeWholesale=c.isOfferWholesale;saved.NativeRequireVariety=c.isRequireVariety;saved.NativeVariety=c.varietyTrackingList.ToArray();});
        }
        internal void AttachSupplier()
        {
            string id=A.CurrentVisit;if(id==null || !A.Visits.TryGetValue(id,out var v) || v.Day!=host.Day || v.Status is "closed" or "closing")return;
            // This Demo does not serialize the current client. Retain the visit
            // while its native blueprint is requeued, then restore exact stock.
            if(PlayerStore.instance.currentClientInstance==null && v.StockCaptured){
                var c=StoreClientListDict.CreateStoreClient(v.Vendor);if(c==null)throw new InvalidOperationException("无法恢复原供应商："+v.Vendor);
                c.clientIntent=StoreClient.ClientIntent.SELL;c.clientFaction=v.Zone=="upper"?StoreClient.FACTION_UPPER:v.Zone=="middle"?StoreClient.FACTION_MIDDLE:StoreClient.FACTION_LOWER;
                c.clientCash=v.NativeCash;c.clientBudget=v.NativeBudget;c.useClientBudget=v.NativeUseBudget;restoringVisit=id;
                PlayerStore.instance.storeClientManager.AddNextClient(c);
            }
        }
        private long vendorPointer;
        private long nextStockRestore;
        private readonly HashSet<long> pendingVendors=new();
        private void InstallSupplyHooks()=>core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(PlayerStore),"DismissCurrentClient"),prefix:new HarmonyMethod(typeof(NativePackageB),nameof(LeavingVendor)));
        private static void LeavingVendor()
        {
            var p=Core.Instance.Story?.Economy?.PackageB;if(p?.Enabled!=true || p.economy.IsTransferring)return;
            string id=p.A.CurrentVisit;if(id==null || !p.A.Visits.TryGetValue(id,out var v))return;
            if(PlayerStore.instance.currentClientInstance?.storeClient?.identifier!=v.Vendor)return;
            p.CaptureSupplier();
            p.host.Engine.EditPackageB(s=>s.Workshop.Advanced.Visits[id].Status="closing");p.host.Flush();
        }
        internal int QueueSupply(StoreClientInstance instance)
        {
            if(economy.Trade?.RestoreReturnedSeller(instance)==true)return 0;
            if(instance?.storeClient==null || instance.storeClient.identifier?.StartsWith("ms_")==true || instance.storeClient.identifier?.StartsWith(NativeBasicInspection.Prefix)==true || !instance.storeClient.IsClientSelling() || instance.gridInv==null)return 0;
            pendingVendors.Add(instance.NativeIdentity().ToInt64());return 0;
        }
        partial void UpdateSupply()
        {
            if(supplying || economy.IsTransferring)return;var current=PlayerStore.instance.currentClientInstance;var a=A;
            if(current==null){
                vendorPointer=0;
                if(restoringVisit!=null)return;
                if(a.CurrentVisit!=null){string id=a.CurrentVisit;host.Engine.EditPackageB(s=>{s.Workshop.Advanced.Visits[id].Status="closed";s.Workshop.Advanced.CurrentVisit=null;});host.Persist();}
                return;
            }
            var client=current.storeClient;if(client==null || client.identifier?.StartsWith("ms_")==true || client.identifier?.StartsWith(NativeBasicInspection.Prefix)==true || !client.IsClientSelling() || current.gridInv==null)return;
            if(economy.Trade.IsReturnedSeller(client.identifier))return;
            if(restoringVisit!=null){
                var restore=A.Visits[restoringVisit];if(client.identifier!=restore.Vendor)return;
                supplying=true;
                try{economy.MutateNative(()=>{
                    foreach(var item in ItemList(current.gridInv.childItems).ToArray())if(!current.gridInv.Expel(item))throw new IOException("无法移除恢复时临时生成的供应商库存");
                    foreach(var e in restore.NativeStock){if(economy.FindPlayerItem(e.Entity)!=null)throw new InvalidDataException("供应商快照与玩家库存实体重复");var item=NativeMemberEconomy.Decode(e.Snapshot);if(!current.gridInv.UncheckedAccept(item))throw new IOException("无法恢复供应商原库存位置");}
                    client.clientCash=restore.NativeCash;client.clientBudget=restore.NativeBudget;client.useClientBudget=restore.NativeUseBudget;
                },()=>{});restoringVisit=null;vendorPointer=current.NativeIdentity().ToInt64();}
                finally{supplying=false;}
            }
            string zone=client.clientFaction==StoreClient.FACTION_UPPER?"upper":client.clientFaction==StoreClient.FACTION_MIDDLE?"middle":client.clientFaction==StoreClient.FACTION_LOWER?"lower":null;if(zone==null)return;
            bool same=a.CurrentVisit!=null && a.Visits.TryGetValue(a.CurrentVisit,out var saved) && saved.Vendor==client.identifier && saved.Day==host.Day && saved.Status!="closed";
            if(same && a.Visits[a.CurrentVisit].Status=="closing"){
                if(vendorPointer==0 || vendorPointer==current.NativeIdentity().ToInt64()){vendorPointer=current.NativeIdentity().ToInt64();return;}same=false;
            }
            vendorPointer=current.NativeIdentity().ToInt64();pendingVendors.Remove(vendorPointer);
            if(!same)host.Engine.EditPackageB(s=>{
                var v=PackageBSupply.Plan(s,host.Day,client.identifier,zone,true,true);
                // Demo omits the vanilla printer factory. Its restored alias
                // shares a large-machine batch, never an extra daily quota.
                if(zone is "middle" or "upper" && v.Sequence%3==0){var machine=v.Batches.FirstOrDefault(x=>x.Sku=="ms_machine_electrobench");if(machine!=null)machine.Sku="printer";}
                if(host.NormalPlay)NormalPlayPolicy.EnsureIntroductoryStock(s,v,economy.AllPlayerItems().Select(x=>x.identifier).ToHashSet());
                if(host.NormalPlay&&client.identifier==NativeNormalSupplier.VendorId)NormalSupply.Prioritize(s,v,host.Supplier.Needs(s));
            });
            string visitId=A.CurrentVisit;var visit=A.Visits[visitId];if(visit.Status=="skipped")return;
            supplying=true;
            try{
                if(visit.Status=="pending"){
                    var added=new List<GameItem>();
                    economy.MutateNative(()=>{
                        for(int index=0;index<visit.Batches.Count;index++){
                            var planned=visit.Batches[index];var batch=new List<GameItem>();bool fits=true;
                            for(int n=0;n<planned.Quantity;n++){
                                var item=DirectoryMaster.Item(planned.Sku,false);
                                if(planned.Native){GeneralHelper.SetItemOwned(item,false);item.unitCount=1;}
                                if(!Place(current.gridInv,item)){fits=false;break;}batch.Add(item);economy.TestStage("supplier_entity_placed");
                            }
                            if(!fits){foreach(var item in batch)current.gridInv.Expel(item);int skipped=index;host.Engine.EditPackageB(s=>s.Workshop.Advanced.Visits[visitId].Batches[skipped].SkipReason="skipped_space");continue;}
                            added.AddRange(batch);int slot=index;
                            foreach(var item in batch){if(!planned.Native)ApplyFacts(item,planned.Grade,"outside","vendor:"+visitId,planned.Fault,planned.Domain,planned.Used);
                                if(MusicCatalog.Find(item.identifier)?.IsGear==true)host.Engine.EditMemberBusiness(s=>MemberBusiness.Register(s.Economy.Business,item.uniqueId,item.identifier,"outside"));Present(item);
                                economy.Trade.MaterializeSource(item,visit,planned);
                            }
                            var actual=batch.Select(x=>NativeWorkshop.Store(x)).ToList();host.Engine.EditPackageB(s=>{var v=s.Workshop.Advanced.Visits[visitId];PackageBSupply.CommitBatch(s.Workshop.Advanced,v,v.Batches[slot],actual);});
                        }
                        host.Engine.EditPackageB(s=>s.Workshop.Advanced.Visits[visitId].Status="ready");
                    },()=>{foreach(var item in added)if(item.parentInventory!=null)item.parentInventory.Expel(item);});
                }else{
                    // Some native versions do not save a seller's appended stock.
                    // Restore the exact unsold IDs, never a fresh random batch.
                    if(RuntimeCompat.TickCount64<nextStockRestore)return;nextStockRestore=RuntimeCompat.TickCount64+1000;
                    var existing=ItemList(current.gridInv.childItems).Select(x=>x.uniqueId).ToHashSet();int negotiated=PlayerStore.instance.currentNegociatedItem?.uniqueId??0;var missing=visit.Batches.Where(x=>x.Committed).SelectMany(x=>x.Remaining).Where(x=>x.Entity!=negotiated && !existing.Contains(x.Entity) && economy.FindPlayerItem(x.Entity)==null).ToArray();
                    if(missing.Length>0){var restored=new List<GameItem>();economy.MutateNative(()=>{
                        foreach(var e in missing){var item=NativeMemberEconomy.Decode(e.Snapshot);if(Place(current.gridInv,item))restored.Add(item);}
                    },()=>{foreach(var item in restored)if(item.parentInventory!=null)item.parentInventory.Expel(item);});}
                }
            }finally{supplying=false;}
        }
        internal void RecordNativeOwnership(int entity,bool playerOwns)
        {
            if(!Enabled || entity<=0)return;var item=economy.FindPlayerItem(entity);if(playerOwns && item==null)return;
            host.Engine.EditPackageB(s=>{
                var a=s.Workshop.Advanced;
                if(playerOwns){
                    var f=PackageB.Fact(a,entity,item.identifier,"player",Grade(item),"native_purchase");f.Owner="player";
                    foreach(var v in a.Visits.Values)foreach(var batch in v.Batches)batch.Remaining.RemoveAll(x=>x.Entity==entity);
                }else if(a.Facts.TryGetValue(entity,out var f))f.Owner="outside";
                if(s.Economy.Business.Instruments.TryGetValue(entity,out var gear))foreach(int part in gear.Slots.Values)if(a.Facts.TryGetValue(part,out var installed))installed.Owner=playerOwns?"player":"outside";
            });
            // Existing gear and all its slots change owner together in the
            // caller. Updating only the host here violates the old A contract.
            if(item!=null && MusicCatalog.Find(item.identifier)?.IsGear==true && !B.Instruments.ContainsKey(entity))host.Engine.EditMemberBusiness(s=>MemberBusiness.Register(s.Economy.Business,entity,item.identifier,"player"));
        }
    }
}
