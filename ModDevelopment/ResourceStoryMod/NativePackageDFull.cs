using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageD
    {
        internal void StartSalvage(string id,int entity,int machine,int cutter=0)
        {
            if(D.Salvage.ContainsKey(id))return;
            var item=economy.FindPlayerItem(entity);var state=host.Engine.Snapshot();
            string block=PackageDFull.SalvageBlock(state,entity,economy.PackageB.PlayerOwned(item),item?.IsTag(MusicCatalog.ProtectedTag)==true||economy.Workshop.BlocksSale(item));
            if(block!=null)throw new InvalidOperationException(block);
            var spec=PackageDFull.SalvageSpec(item.identifier);
            var choices=new Dictionary<string,int[]>{{item.identifier,new[]{entity}}};
            if(cutter>0){
                var tools=economy.PackageB.ToolsAt(machine).ToArray();
                foreach(var req in spec.Tools)
                    choices[req.Key]=tools.Where(x=>x.identifier==economy.Workshop.NativeId(req.Key)&&(req.Key!="ref_wirecutter"||x.uniqueId==cutter)).Select(x=>x.uniqueId).ToArray();
            }
            economy.Workshop.StartPrepared(id,spec,machine,choices,onStart:(s,stored)=>{
                if(cutter>0)PackageB.BindRepairDay(s.Workshop.Jobs[id],host.Day);
                s.MusicTrade.Salvage.Add(id,new(){Id=id,Host=entity,Day=host.Day});
                PackageDFull.History(s,"accept_"+id,entity,host.Day,"salvage_accepted","已交入整备台，完成前可以取消报废。");
            });
        }
        internal void RecordWorkInputs(string id,(int Source,int Held,int Count,int Before,bool Tool)[] picks)
        {
            if(!Enabled)return;var state=host.Engine.Snapshot();var job=state.Workshop.Jobs[id];if(job.Spec.Purpose=="d_salvage")return;
            int entity=job.Spec.Service!=null&&state.Workshop.Advanced.Services.TryGetValue(job.Spec.Service,out var svc)?svc.Host:0;
            host.Engine.EditMusicTrade(s=>{
                if(entity>0&&(job.Recipe.EndsWith("install_simple")||job.Recipe.EndsWith("install_precision")))PackageDFull.SplitSourceCost(s,entity);
                foreach(var p in picks.Where(x=>!x.Tool)){
                    ApplyInputCost(s.MusicTrade,id,entity,p.Source,p.Held,p.Count);
                }
            });
        }
        internal static void ApplyInputCost(MusicTradeState trade,string id,int entity,int source,int held,int units)
        {
            string key=id+"_"+held;
            if(trade.Investments.ContainsKey(key))return;
            bool known=trade.Costs.TryGetValue(source,out var cost)&&cost.Known&&cost.Units>=units;
            int amount=known?(units==cost.Units?cost.Amount:(int)((long)cost.Amount*units/cost.Units)):0;
            if(known){cost.Amount-=amount;cost.Units-=units;}
            trade.Investments[key]=new(){Id=key,Job=id,Entity=entity,Amount=amount,Known=known,Units=units,Source=source,Held=held};
        }
        internal static void ApplyEnergyCost(MusicTradeState trade,string id,int entity,int source,int used,int available,string key)
        {
            if(trade.Investments.ContainsKey(key))return;
            bool known=trade.Costs.TryGetValue(source,out var cost)&&cost.Known;
            int amount=known?(used==available?cost.Amount:(int)((long)cost.Amount*used/available)):0;
            if(known)cost.Amount-=amount;
            trade.Investments[key]=new(){Id=key,Entity=entity,Job=id,Amount=amount,Known=known,Units=used,Source=source,Energy=true};
        }
        internal void RecordEnergy(string id,int source,int used,int available)
        {
            if(!Enabled||used<=0)return;var job=host.Engine.Snapshot().Workshop.Jobs[id];string key="energy_"+id+"_"+job.ElapsedMs;
            host.Engine.EditMusicTrade(s=>{
                int entity=job.Spec.Service!=null?s.Workshop.Advanced.Services[job.Spec.Service].Host:0;
                ApplyEnergyCost(s.MusicTrade,id,entity,source,used,available,key);
            });
        }
        internal void WorkCancelled(string id)
        {
            if(!Enabled)return;var job=host.Engine.Snapshot().Workshop.Jobs[id];if(job.InputsConsumed)return;
            host.Engine.EditMusicTrade(s=>RestoreInputCosts(s.MusicTrade,id));
        }
        internal static void RestoreInputCosts(MusicTradeState trade,string id)
        {
            foreach(var row in trade.Investments.Values.Where(x=>x.Job==id&&!x.Energy).ToArray()){
                var old=trade.Costs.GetValueOrDefault(row.Source);
                var target=trade.Costs.GetValueOrDefault(row.Held)??new MusicCost{Entity=row.Held,Known=row.Known,Receipt=old?.Receipt,Units=0};
                target.Amount+=row.Amount;target.Units+=row.Units;trade.Costs[row.Held]=target;trade.Investments.Remove(row.Id);
            }
        }
        internal static void TransferOutputCosts(MusicTradeState trade,string id,int day,IEnumerable<int> outputs)
        {
            var costs=trade.Investments.Values.Where(x=>x.Job==id).ToArray();
            bool known=costs.Length>0&&costs.All(x=>x.Known);
            int total=known?costs.Sum(x=>x.Amount):0;
            string receipt="work_cost_"+id;
            if(!trade.Receipts.ContainsKey(receipt))trade.Receipts.Add(receipt,new(){Id=receipt,Kind="manufacture_cost_transfer",Visit=id,Day=day,Total=total,Allocations=PackageD.Allocate(total,outputs.ToDictionary(x=>x,x=>1))});
            foreach(var entry in trade.Receipts[receipt].Allocations)trade.Costs[entry.Key]=new(){Entity=entry.Key,Known=known,Amount=entry.Value,Receipt=receipt};
        }
        internal void WorkCompleted(string id)
        {
            if(!Enabled)return;var state=host.Engine.Snapshot();var job=state.Workshop.Jobs[id];if(job.Status is not ("ready" or "claimed"))return;
            host.Engine.EditMusicTrade(s=>{
                if(s.MusicTrade.Salvage.ContainsKey(id)){PackageDFull.FinishSalvage(s,id,host.Day);return;}
                if(job.Spec.Service!=null&&s.Workshop.Advanced.Services.TryGetValue(job.Spec.Service,out var svc)){
                    var step=svc.Steps.First(x=>x.Id==id);
                    string text=step.Kind.Contains("inspect")?"完成实际检查，状态与报告已登记。":step.Kind.Contains("calibrate")?"完成当前配置校准，判定以实际报告为准。":step.Kind.Contains("install")?(step.Component==0?"卸下实际配件，保留原件身份。":"安装实际配件，配置已经变更。") :"完成实际清洁或维修工序。";
                    PackageDFull.History(s,"work_"+id,svc.Host,host.Day,step.Kind,text);
                }else if(job.Outputs.Count>0){
                    TransferOutputCosts(s.MusicTrade,id,host.Day,job.Outputs.Select(x=>x.Entity));
                }
            });
        }
        internal void OpenBusiness(string tier)
        {
            string business=PackageDNetwork.Business(tier);if(D.Openings.ContainsKey(business))return;
            string block=PackageDNetwork.Qualification(host.Engine.Snapshot(),business,MiddleOpen());if(block!=null)throw new InvalidOperationException(block);
            int fee=PackageDNetwork.Opening(business);if(PlayerStore.instance.GetCash()<fee)throw new InvalidOperationException("资金不足以开通业务");
            economy.MutateNative(()=>{host.Engine.EditMusicTrade(s=>PackageDNetwork.Open(s,business,host.Day));PlayerStore.instance.ModCash(-fee,false);},()=>{});
        }
        private bool MiddleOpen()=>host.Engine.Snapshot().CompletedDays.ContainsKey("ms_scene_005")||StoreStation.instance.dayCounter>=14;
        internal int FutureCapacity()=>host.Day+1<14?0:Math.Max(0,PlayerStore.instance.storeClientManager.GetNormalCustomerCount()-(StoreReputation.IsPerkUnlocked("LLREFERAL")?1:0));
        internal MusicReferral ReferralPreview(string tier,string kind,string target,string zone)
        {
            var s=host.Engine.Snapshot();
            if(kind=="buyer"){
                var profile=PackageDCatalog.Buyer(target);if(profile==null||!profile.Zones.Contains(zone))throw new InvalidOperationException("该地区没有对应买家");
                if(!economy.AllPlayerItems().Any(item=>PackageD.Eligible(s,profile,item.uniqueId,zone,economy.PackageB.PlayerOwned(item),item.IsTag(MusicCatalog.ProtectedTag)||economy.Workshop.BlocksSale(item),0,0,false)==null))throw new InvalidOperationException("当前没有可售且满足该用途的实物");
            }else if(!AvailableReturns().Any(x=>x.Id==target&&x.Zone==zone))throw new InvalidOperationException("没有保留完整未售库存的已知卖方");
            return PackageDNetwork.Preview(s,tier,kind,target,zone,host.Day);
        }
        internal void ConfirmReferral(MusicReferral preview)
        {
            var fresh=ReferralPreview(preview.Tier,preview.Kind,preview.Target,preview.Zone);if(fresh.Id!=preview.Id)throw new InvalidOperationException("日期已变，请重新预览");
            int capacity=FutureCapacity();int fee=preview.Tier=="community"&&!D.CommunityFreeUsed?0:PackageDNetwork.Fee(preview.Tier);if(capacity>0&&PlayerStore.instance.GetCash()<fee)throw new InvalidOperationException("资金不足以确认推荐");
            economy.MutateNative(()=>{int actual=0;host.Engine.EditMusicTrade(s=>actual=PackageDNetwork.Confirm(s,preview,capacity));if(actual>0)PlayerStore.instance.ModCash(-actual,false);},()=>{});
        }
        internal void CancelReferral(string id){economy.MutateNative(()=>host.Engine.EditMusicTrade(s=>PackageDNetwork.Cancel(s,id)),()=>{});}
        private void CancelReferralIntent(string id)=>economy.MutateNative(()=>host.Engine.EditMusicTrade(s=>PackageDNetwork.CancelIntent(s,id)),()=>{});
        internal void ResolveMissedReferrals()
        {
            if(!Enabled)return;
            foreach(var r in D.Referrals.Values.Where(x=>x.Status=="confirmed"&&x.Day<=host.Day).ToArray())economy.MutateNative(()=>{
                int refund=0;host.Engine.EditMusicTrade(s=>refund=PackageDNetwork.Fail(s,r.Id));if(refund>0)PlayerStore.instance.ModCash(refund,false);
            },()=>{});
        }
        internal IEnumerable<SupplierVisit> AvailableReturns()=>host.Engine.Snapshot().Workshop.Advanced.Visits.Values.Where(x=>x.Status is "closed" or "closing"&&x.StockCaptured&&x.NativeBlueprintCaptured&&x.NativeStock.Count>0&&x.NativeStock.All(e=>economy.FindPlayerItem(e.Entity)==null));
        private bool BasicOrdinary(StoreClient c)=>c!=null&&!c.isWanted&&!c.isSecurity&&!c.isAug&&c.hirelingData==null&&c.procurementData==null&&c.barterOffer==null&&(string.IsNullOrEmpty(c.eventSourceId)||displayDepth>0)&&c.identifier?.StartsWith("ms_")!=true&&c.identifier?.StartsWith(PackageDCatalog.BuyerPrefix)!=true&&!IsUseClient(c.identifier);
        private StoreClient TryReferral(StoreClient original,string ticket,string zone)
        {
            var r=D.Referrals.Values.FirstOrDefault(x=>x.Day==host.Day&&x.Status=="confirmed");
            if(r==null)return null;
            if(r.Kind=="buyer"&&D.Buyers.Values.Count(x=>x.Day==host.Day)+N.Visits.Values.Count(x=>x.Day==host.Day)>=2)return null;
            if(r.Kind=="seller"&&!AvailableReturns().Any(x=>x.Id==r.Target&&x.Zone==r.Zone))return null;
            host.Engine.EditMusicTrade(s=>{s.MusicTrade.Slots[ticket].Zone=r.Zone;s.MusicTrade.Slots[ticket].Source="referral";});
            StoreClient client;
            if(r.Kind=="buyer"){
                client=CreateBuyer(ticket,PackageDCatalog.Buyer(r.Target),r.Zone,original);
            }else{
                string id="millsage_trade_return_"+r.Id;host.Engine.EditMusicTrade(s=>s.MusicTrade.Returns[id]=r.Target);RegisterReturn(id);client=BuildReturn(id);
            }
            host.Engine.EditMusicTrade(s=>{var booking=s.MusicTrade.Referrals[r.Id];booking.Status="arrived";booking.Ticket=ticket;booking.Client=client.identifier;});return client;
        }
        private void RegisterReturn(string id)
        {
            if(factories.ContainsKey(id))return;var callback=(System.Func<StoreClient>)(Func<StoreClient>)(()=>BuildReturn(id));factories[id]=callback;StoreClientListDict.storeClientDict[id]=callback;
        }
        private StoreClient BuildReturn(string id)
        {
            var visit=host.Engine.Snapshot().Workshop.Advanced.Visits[D.Returns[id]];var c=StoreClientListDict.CreateStoreClient(visit.Vendor)??throw new InvalidOperationException("已知卖方工厂不可用");
            c.identifier=id;c.clientIntent=StoreClient.ClientIntent.SELL;c.OnArrival=null;c.OnDismiss=null;c.clientFaction=visit.Zone=="upper"?StoreClient.FACTION_UPPER:visit.Zone=="middle"?StoreClient.FACTION_MIDDLE:StoreClient.FACTION_LOWER;c.clientCash=visit.NativeCash;c.clientBudget=visit.NativeBudget;c.useClientBudget=visit.NativeUseBudget;
            c.spriteName=visit.NativeSprite;c.buyPriceModifier=visit.NativeBuyModifier;c.sellPriceModifier=visit.NativeSellModifier;c.acceptRetailMarkup=visit.NativeMarkup;c.isOfferWholesale=visit.NativeWholesale;c.isRequireVariety=visit.NativeRequireVariety;c.varietyTrackingList.Clear();foreach(string item in visit.NativeVariety)c.varietyTrackingList.Add(item);return c;
        }
        internal bool IsReturnedSeller(string id)=>Enabled&&id!=null&&D.Returns.ContainsKey(id);
        internal bool RestoreReturnedSeller(StoreClientInstance instance)
        {
            if(!IsReturnedSeller(instance?.storeClient?.identifier))return false;string id=instance.storeClient.identifier;string origin=D.Returns[id];var visit=host.Engine.Snapshot().Workshop.Advanced.Visits[origin];
            economy.MutateNative(()=>{
                foreach(var item in NativePackageB.ItemList(instance.gridInv.childItems).ToArray())if(!instance.gridInv.Expel(item))throw new IOException("回访临时库存无法清理");
                foreach(var e in visit.NativeStock){
                    var entry=EmporiumEntry.Instance;
                    var item=economy.FindPlayerItem(e.Entity)??NativePackageB.ItemList(entry.frontInvinvElement.childItems).FirstOrDefault(x=>x.uniqueId==e.Entity);
                    if(item!=null){
                        var source=item.parentInventory;
                        if(GeneralHelper.IsItemOwned(item)||item.identifier!=e.ItemId||item.unitCount!=e.Units
                            ||source?.NativeIdentity()!=entry.backInvinvElement.NativeIdentity()&&source?.NativeIdentity()!=entry.frontInvinvElement.NativeIdentity())
                            throw new InvalidDataException("回访库存与玩家实体冲突");
                        if(!source.Expel(item))throw new IOException("无法恢复柜台上仍属卖方的原件");
                    }else item=NativeMemberEconomy.Decode(e.Snapshot);
                    if(!instance.gridInv.UncheckedAccept(item))throw new IOException("回访库存无法还原");
                }
                host.Engine.EditPackageB(s=>{
                    var workshop=s.Workshop.Advanced;
                    if(workshop.CurrentVisit is string previous&&previous!=origin)workshop.Visits[previous].Status="closed";
                    workshop.CurrentVisit=origin;
                    workshop.Visits[origin].Status="ready";
                    workshop.Visits[origin].NativeStock=visit.NativeStock;
                });
            },()=>{});return true;
        }
        internal List<MusicDisplaySample> DisplaySamples(string zone)
        {
            var result=new List<MusicDisplaySample>();var grid=EmporiumEntry.Instance.showcaseElement;var s=host.Engine.Snapshot();
            foreach(var item in NativePackageB.ItemList(grid.childItems)){
                var categories=new[]{"HOUSEHOLD_GOOD","MODULE","MATERIAL","TOOL"}.Where(x=>item.GetGameItemType().Contains(x)).ToArray();if(categories.Length==0)continue;
                bool music=item.IsTag("ms.music")&&economy.PackageB.PlayerOwned(item)&&!economy.Workshop.BlocksSale(item)&&!NativePackageB.IsBox(item)&&PackageDCatalog.Buyers.Any(profile=>PackageD.Eligible(s,profile,item.uniqueId,zone,true,item.IsTag(MusicCatalog.ProtectedTag),0,0,false)==null);
                foreach(string category in categories)result.Add(new(){Entity=item.uniqueId,Category=category,Value=checked((int)Math.Max(0,item.GetNegociatedValue())),Music=music});
            }
            return result;
        }
        private string DisplayCategory(StoreClient c)
        {
            string[] categories={"HOUSEHOLD_GOOD","MODULE","MATERIAL","TOOL"};if(categories.Contains(c.attractedType))return c.attractedType;
            if(categories.Contains(c.attractedItemId))return c.attractedItemId;
            var item=NativePackageB.ItemList(EmporiumEntry.Instance.showcaseElement.childItems).FirstOrDefault(x=>x.identifier==c.attractedItemId);
            return item==null?null:categories.FirstOrDefault(x=>item.GetGameItemType().Contains(x));
        }
    }
}
