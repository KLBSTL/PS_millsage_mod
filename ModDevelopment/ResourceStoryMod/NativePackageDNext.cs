using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageD
    {
        private string nextView,planTarget="B02",planCacheKey;
        private static string UseDemand(MusicUseCase use)=>use.Target switch
        {
            "B02"=>"上台换场时要能快速接线，接口接牢，原有配件请帮我留着。",
            "B03"=>"这个过载接上以后底噪有点重。麻烦检查信号链，把噪声压下来。",
            "B04"=>"监听接好后得稳定工作。麻烦检查供电和接口，别让它在使用中断掉。",
            _=>"想按新的用途调整之前买的器材。"
        };

        internal string UsePaper(int noteEntity)
        {
            if(!Enabled||noteEntity<=0)return null;
            var use=N.Uses.Values.FirstOrDefault(order=>order.NoteEntity==noteEntity);
            if(use==null)return null;
            string progress=use.Status switch
            {
                "done"=>"器材已交回，这张便条留作记录。",
                "pickup" or "cancel_pickup"=>"约定第 "+use.DueDay+" 天到柜台领取。",
                _=>"当前状态："+(PackageDUse.ReadyBlock(host.ReadState,use)??"配置已符合用途，待安排领取。")
            };
            string contact=use.AcquisitionPaid.HasValue&&use.Status=="servicing"?"\n检测通过后请拨 "+UsePhoneNumber+" 约柜台取件。":"";
            return Product(use.Sku)+"\n\n"+UseDemand(use)+"\n\n"+progress+contact;
        }

        private void IssueUsePaper()
        {
            var use=N.Uses.Values.FirstOrDefault(order=>order.NoteEntity==0&&order.Service!=null&&order.Status is "servicing" or "pickup" or "cancel_pickup");
            if(use==null)return;
            var paper=DirectoryMaster.Item(NativePackageB.RepairNoteSku,true);
            if(paper==null)return;
            var inventory=economy.PlayerInventories().FirstOrDefault(inv=>inv.TryFindOneValidInventorySlot(paper)!=null);
            if(inventory==null)return;
            bool placed=false;
            economy.MutateNative(()=>{
                placed=NativePackageB.Place(inventory,paper);
                if(placed)host.Engine.EditMusicTrade(s=>s.MusicTrade.Next.Uses[use.Entity].NoteEntity=paper.uniqueId);
            },()=>{if(placed)paper.parentInventory?.Expel(paper);});
        }
        private MusicPlan[] planCache;
        private long nextObservation,lastObservation;
        private MusicNextState N=>D.Next;
        private static string UseStatus(string status)=>status switch{"offered"=>"待确认用途","servicing"=>"正在办理","pickup"=>"已通知领取","cancel_pickup"=>"取消后待归还","done"=>"已交回","declined"=>"未承接",_=>"物主在外使用"};
        internal bool IsUseClient(string id)=>id!=null&&N.Visits.ContainsKey(id);
        private bool NativeUseEnabled=>host.NormalPlay&&Enabled;
        private const long UsePhoneNumber=8670202;
        private StorePhoneClient usePhone;
        private Action usePhoneBooking;
        private string nativeUseRecoveredRun;
        private bool sellingNativeUse;
        private static bool NativeUseHeld(MusicUseCase order)=>order.AcquisitionPaid.HasValue&&(order.Status is "servicing" or "pickup" or "cancel_pickup");
        private MusicUseCase ActiveNativeUse=>NativeUseEnabled?N.Uses.Values.FirstOrDefault(NativeUseHeld):null;
        internal void CaptureNativeUse()
        {
            var use=ActiveNativeUse;
            var item=use==null?null:economy.FindPlayerItem(use.Entity);
            if(item==null)return;
            bool temporary=item.parentInventory?.NativeIdentity()==EmporiumEntry.Instance.backInvinvElementCounter.NativeIdentity();
            if(!temporary&&!use.SavedOnTemporaryCounter)return;
            string snapshot=temporary?NativeMemberEconomy.Encode(item):null;
            host.Engine.EditMusicTrade(s=>{
                s.MusicTrade.Next.Uses[use.Entity].SavedOnTemporaryCounter=temporary;
                if(temporary)s.Workshop.Advanced.Services[use.Service].HostSnapshot=snapshot;
            });
        }
        private void RecoverNativeUse()
        {
            var use=ActiveNativeUse;
            var store=PlayerStore.instance;
            if(use?.SavedOnTemporaryCounter!=true||economy.IsTransferring||nativeUseRecoveredRun==store.runID)return;
            if(economy.FindPlayerItem(use.Entity)!=null){nativeUseRecoveredRun=store.runID;return;}
            if(store.currentNegociatedItem?.uniqueId==use.Entity)return;
            var item=NativeMemberEconomy.Decode(host.ReadState.Workshop.Advanced.Services[use.Service].HostSnapshot);
            if(item.uniqueId!=use.Entity||item.identifier!=use.Sku)throw new InvalidDataException("普通整备原件快照身份不符");
            GeneralHelper.SetItemOwned(item,true);
            foreach(var inv in economy.PlayerInventories().Where(i=>i.NativeIdentity()!=EmporiumEntry.Instance.backInvinvElementCounter.NativeIdentity()))
                if(NativePackageB.Place(inv,item)){nativeUseRecoveredRun=store.runID;break;}
        }
        private bool IsUsePhone(StorePhoneClient phone)=>phone!=null&&usePhone!=null&&phone.NativeIdentity()==usePhone.NativeIdentity();
        private void BindUsePhone(PlayerStore store)
        {
            if(ActiveNativeUse==null||store.PhoneClientDict==null||store.PhoneClientDict.ContainsKey(UsePhoneNumber))return;
            usePhone=new StorePhoneClient{displayName="普通整备取回",locID="",dialogFuncId="",phoneState=StorePhoneClient.PhoneState.Regular,dialedBefore=true};
            store.PhoneClientDict[UsePhoneNumber]=usePhone;
        }
        private static bool NativeUsePhoneShown(StorePhoneClient __instance,ref bool __result)
        {var p=Self;if(p?.IsUsePhone(__instance)!=true)return true;__result=p.ActiveNativeUse!=null;return false;}
        private static bool NativeUsePhoneAnswer(long __0,ref bool __result)
        {var p=Self;if(__0!=UsePhoneNumber||p==null||!p.IsUsePhone(PlayerStore.instance?.PhoneClientDict?.GetValueOrDefault(UsePhoneNumber)))return true;__result=p.ActiveNativeUse!=null;return false;}
        private static bool NativeUsePhoneService(StorePhoneClient __instance)
        {var p=Self;if(p?.IsUsePhone(__instance)!=true)return true;p.BookNativeUse();return false;}
        private static bool NativeUseCall(StorePhoneClient __instance,ref Dialogue __result)
        {
            var p=Self;if(p?.IsUsePhone(__instance)!=true)return true;
            var use=p.ActiveNativeUse;
            string text=use==null?"目前没有待取的整备委托。":p.NativeUseReadyBlock(use)??
                (use.DueDay>=p.host.Day?"已经约好第 "+use.DueDay+" 天到店。":"明天起我会找普通来访的空位，到柜台取回器材。");
            p.usePhoneBooking??=(System.Action)(Action)p.BookNativeUse;
            __result=new Dialogue().SetText("器材续用客户",text).SetEndAction(p.usePhoneBooking);return false;
        }
        private string NativeUseReadyBlock(MusicUseCase use)
        {
            var state=host.ReadState;
            if(!state.Workshop.Advanced.Services.TryGetValue(use.Service??"",out var service)||economy.FindPlayerItem(use.Entity)==null)return "请保留同一件原件，并完成整备和检测。";
            if(!PackageB.CalibrationValid(state.Workshop.Advanced,state.Economy.Business,use.Entity,"basic",service.EffectiveSlots))return "请用检测仪检查当前配置，通过后再联系。";
            return PackageDUse.ReadyBlock(state,use);
        }
        private void BookNativeUse()
        {
            var use=ActiveNativeUse;
            if(use==null||use.DueDay>=host.Day||NativeUseReadyBlock(use)!=null)return;
            host.Engine.EditMusicTrade(s=>{
                s.MusicTrade.Next.Uses[use.Entity].DueDay=-1;
                PackageDUse.Book(s,use.Entity,host.Day);
            });
            host.Persist();
        }
        internal MusicUseCase UsePickup(StoreClient client,GameItem item)
        {
            if(item==null||!GeneralHelper.IsItemOwned(item)||!HoldsNativeUse(item)||client?.identifier==null)return null;
            var use=N.Uses[item.uniqueId];
            return use.Status=="pickup"&&use.DueDay>=0&&use.DueDay<=host.Day
                &&N.Visits.TryGetValue(client.identifier,out var visit)&&visit.Entity==use.Entity&&visit.Kind=="pickup"&&visit.Status!="closed"&&visit.Day==host.Day
                &&NativeUseReadyBlock(use)==null?use:null;
        }
        internal bool CounterUse(GameItem item)
        {var store=PlayerStore.instance;return store?.isClientArrived==true&&UsePickup(store.currentClientInstance?.storeClient,item)!=null;}
        internal int UseReturnCeiling(GameItem item)=>N.Uses[item.uniqueId].AuthorizedQuote;
        internal int UseReturnPrice(GameItem item)
        {
            var use=N.Uses[item.uniqueId];
            return Math.Min(use.AuthorizedQuote,checked(use.AcquisitionPaid.Value+PackageB.Suggested(host.ReadState.Workshop.Advanced.Services[use.Service])));
        }
        private bool SellNativeUse(StoreClient client,GameItem item)
        {
            if(sellingNativeUse)return true;
            var use=UsePickup(client,item);
            if(use==null||PlayerStore.instance?.isClientArrived!=true)return false;
            var entry=EmporiumEntry.Instance;
            var inventory=item.parentInventory?.NativeIdentity();
            if(inventory!=entry.backInvinvElement.NativeIdentity()&&inventory!=entry.backInvinvElementCounter.NativeIdentity())return false;
            var store=PlayerStore.instance;
            int before=store.GetCash();string receipt="native_use_return_"+use.Entity;
            economy.MutateNative(()=>{
                sellingNativeUse=true;
                try{store.SellItem(item);}finally{sellingNativeUse=false;}
                var held=economy.FindPlayerItem(use.Entity);
                if(held!=null&&GeneralHelper.IsItemOwned(held))throw new InvalidOperationException("原生柜台尚未交回同一件器材。");
                int paid=store.GetCash()-before;
                CommitUseDelivery(use.Service,receipt,paid,NativeMemberEconomy.Encode(item));
            },()=>{});
            return false;
        }
        internal bool HoldsNativeUse(GameItem item)=>item!=null&&NativeUseEnabled&&N.Uses.TryGetValue(item.uniqueId,out var order)&&NativeUseHeld(order);
        internal bool UseIncoming(GameItem item)
        {
            if(item==null||!NativeUseEnabled||GeneralHelper.IsItemOwned(item))return false;
            var store=PlayerStore.instance;
            string id=store?.currentClientInstance?.storeClient?.identifier;
            return id!=null&&store.isClientArrived&&N.Visits.TryGetValue(id,out var visit)&&visit.Kind=="offer"
                &&visit.Entity==item.uniqueId&&N.Uses.TryGetValue(visit.Entity,out var use)&&use.Status=="offered"&&use.Sku==item.identifier;
        }
        internal int UseAcquisitionPrice(GameItem item)=>PackageB.RepairAcquisitionPrice(PackageB.LeafValue(host.ReadState.Workshop.Advanced,item.uniqueId,item.identifier));
        internal void BuyUseFromCounter(GameItem item,Action buy)
        {
            if(!UseIncoming(item)||item.parentInventory?.NativeIdentity()!=EmporiumEntry.Instance.frontInvinvElement.NativeIdentity())return;
            var state=host.Engine.Snapshot();var order=state.MusicTrade.Next.Uses[item.uniqueId];
            int ceiling=Math.Min(order.Budget,order.Cash);
            string service="use_service_"+order.Entity;
            PackageDUse.Authorize(state,order.Entity,service,ceiling,NativeMemberEconomy.Encode(item));
            var store=PlayerStore.instance;
            int cash=store.GetCash();
            economy.MutateNative(()=>{
                buy();
                var actual=economy.FindPlayerItem(order.Entity);
                int paid=cash-store.GetCash();
                if(actual==null||!GeneralHelper.IsItemOwned(actual)||paid<0)throw new InvalidOperationException("原生收购未完成，未受理整备委托。");
                host.Engine.EditMusicTrade(s=>{
                    PackageDUse.Authorize(s,order.Entity,service,ceiling,NativeMemberEconomy.Encode(actual));
                    var accepted=s.MusicTrade.Next.Uses[order.Entity];
                    accepted.AcquisitionPaid=paid;accepted.AcquisitionDay=host.Day;
                });
            },()=>{});
        }
        internal bool ExternalPresent(string service)=>N.Visits.TryGetValue(PlayerStore.instance.currentClientInstance?.storeClient?.identifier??"",out var v)&&v.Kind=="pickup"&&N.Uses[v.Entity].Service==service&&PlayerStore.instance.isClientArrived;
        private StoreClient TryUse(StoreClient original,string ticket,string zone,bool pickup)
        {
            if(original.clientIntent!=StoreClient.ClientIntent.BUY)return null;
            int count=D.Buyers.Values.Count(x=>x.Day==host.Day)+N.Visits.Values.Count(x=>x.Day==host.Day)+D.Referrals.Values.Count(x=>x.Day==host.Day&&x.Kind=="buyer"&&x.Status=="confirmed");if(count>=2)return null;
            var state=host.Engine.Snapshot();MusicUseCase u;
            if(pickup)u=N.Uses.Values.Where(x=>x.Status is "pickup" or "cancel_pickup"&&x.DueDay<=host.Day&&x.Zone==zone&&!N.Visits.Values.Any(v=>v.Entity==x.Entity&&v.Status!="closed")).OrderBy(x=>x.DueDay).FirstOrDefault();
            else{
                if(!PackageDFull.NaturalSlotAvailable(state,host.Day)||PackageBSupply.Roll(state.RunId+"/"+ticket+"/use",100)>=25)return null;
                u=N.Uses.Values.Where(x=>PackageDUse.Eligible(state,x,host.Day,zone)).OrderBy(x=>x.SoldDay).ThenBy(x=>x.Entity).FirstOrDefault();
            }
            return u==null?null:CreateUseVisitor(u.Entity,ticket,original,pickup);
        }
        internal StoreClient CreateUseVisitor(int entity,string ticket,StoreClient prototype,bool pickup)
        {
            if(!D.Slots.ContainsKey(ticket))throw new InvalidOperationException("没有原生普通位置");
            if(D.Buyers.Values.Count(x=>x.Day==host.Day)+N.Visits.Values.Count(x=>x.Day==host.Day)+D.Referrals.Values.Count(x=>x.Day==host.Day&&x.Kind=="buyer"&&x.Status=="confirmed")>=2)throw new InvalidOperationException("今日音乐普通适配位已满");
            string id="millsage_use_"+ticket;var u=N.Uses[entity];int budget=u.Budget,cash=u.Cash;
            if(!pickup){var c=new StoreClient{identifier="millsage_use_budget",clientIntent=StoreClient.ClientIntent.BUY,clientFaction=prototype.clientFaction};c.CompleteClientCreation(false);c.SetBudget(PackageDCatalog.Buyer(u.Profile).Budget);budget=c.GetBudget();cash=budget;}
            host.Engine.EditMusicTrade(s=>{
                if(!pickup)PackageDUse.Offer(s,entity,id,ticket,host.Day,budget,cash);
                else{s.MusicTrade.Next.Visits.Add(id,new(){Id=id,Entity=entity,Ticket=ticket,Day=host.Day,Kind="pickup"});}
                s.MusicTrade.Slots[ticket].Source=pickup?"use_pickup":"use_offer";
            });RegisterUse(id);return BuildUse(id);
        }
        private void RegisterUse(string id)
        {
            if(factories.ContainsKey(id))return;var f=(System.Func<StoreClient>)(Func<StoreClient>)(()=>BuildUse(id));factories[id]=f;StoreClientListDict.storeClientDict[id]=f;
        }
        private StoreClient BuildUse(string id)
        {
            var v=N.Visits[id];var u=N.Uses[v.Entity];string name="器材续用客户";
            var c=new StoreClient{identifier=id,displayName=name,spriteName=u.Sprite,clientFaction=u.Zone=="upper"?StoreClient.FACTION_UPPER:StoreClient.FACTION_MIDDLE,clientIntent=StoreClient.ClientIntent.BUY,dismissable=true,nonShootable=true,mainDialogue=new Dialogue().SetText(name,v.Kind=="offer"?UseDemand(u):"来办理这件器材的交收。"),allDoneDialogue=new Dialogue().SetText(name,"下次再来。")};
            if(NativeUseEnabled&&v.Kind=="offer")c.clientIntent=StoreClient.ClientIntent.SELL;
            if(NativeUseEnabled&&v.Kind=="pickup")c.clientBuyingIdList.Add(u.Sku);
            c.CompleteClientCreation(false);c.useClientBudget=true;c.clientBudget=u.Budget;c.clientCash=u.Cash;return c;
        }
        private void RestoreUses()
        {
            var store=PlayerStore.instance;
            foreach(var v in N.Visits.Values.Where(x=>x.Status!="closed").ToArray()){
                if(v.Day!=host.Day){CloseUseVisit(v.Id);continue;}
                bool exists=store.currentClientInstance?.storeClient?.identifier==v.Id;foreach(var c in store.storeClientManager.clientStack)exists|=c.identifier==v.Id;
                if(!exists)store.storeClientManager.AddNextClient(BuildUse(v.Id));
            }
        }
        private void CloseUseVisit(string id)
        {
            var v=N.Visits[id];var u=N.Uses[v.Entity];
            host.Engine.EditMusicTrade(s=>{s.MusicTrade.Next.Visits[id].Status="closed";var r=s.MusicTrade.Next.Uses[v.Entity];if(r.Status=="offered")r.Status="declined";else if(v.Kind=="pickup"&&(r.Status is "pickup" or "cancel_pickup")&&r.DueDay<=host.Day)r.DueDay=host.Day+1;});host.Flush();
        }
        internal void IntakeUse(int entity,int quote)
        {
            var store=PlayerStore.instance;var instance=store.currentClientInstance;var v=N.Visits.GetValueOrDefault(instance?.storeClient?.identifier??"");
            if(v==null||v.Entity!=entity||v.Kind!="offer"||!store.isClientArrived)throw new InvalidOperationException("需要该原件物主实际到店");
            var item=NativePackageB.ItemList(instance.gridInv.childItems).SingleOrDefault(x=>x.uniqueId==entity)??throw new InvalidOperationException("原件没有实际到店");
            string service="use_service_"+entity;string snapshot=NativeMemberEconomy.Encode(item);bool removed=false;
            economy.MutateNative(()=>{removed=instance.gridInv.Expel(item);if(!removed)throw new IOException("原件交入失败");host.Engine.EditMusicTrade(s=>PackageDUse.Authorize(s,entity,service,quote,snapshot));},()=>{if(removed)instance.gridInv.UncheckedAccept(item);});
        }
        internal void BookUse(int entity)
        {
            host.Engine.EditMusicTrade(s=>PackageDUse.Book(s,entity,host.Day));
        }
        internal void DeliverExternal(string service,int price,string receipt)
        {
            var state=host.Engine.Snapshot();var svc=state.Workshop.Advanced.Services[service];var u=PackageDUse.ForService(state,service);
            if(u?.Receipt==receipt){if(u.Paid!=price)throw new InvalidOperationException("交收价格与原回执不同");return;}
            if(!ExternalPresent(service))throw new InvalidOperationException("该普通物主须实际到店后交收");
            var c=PlayerStore.instance.currentClientInstance.storeClient;var item=NativeMemberEconomy.Decode(svc.HostSnapshot);economy.PackageB.Present(item,svc.EffectiveSlots);GeneralHelper.SetItemOwned(item,false);string snapshot=NativeMemberEconomy.Encode(item);int before=PlayerStore.instance.GetCash();
            economy.MutateNative(()=>{
                economy.PackageB.DematerializeRepair(service);
                CommitUseDelivery(service,receipt,price,snapshot);
                c.clientBudget=N.Uses[u.Entity].Budget;c.clientCash=N.Uses[u.Entity].Cash;PlayerStore.instance.ModCash(price,false);
                if(PlayerStore.instance.GetCash()!=before+price)throw new IOException("普通服务现金不一致");
            },()=>{});
        }
        private void CommitUseDelivery(string service,string receipt,int price,string snapshot)
        {
            host.Engine.EditMusicTrade(s=>{
                s.Workshop.Advanced.Services[service].HostSnapshot=snapshot;
                PackageB.Deliver(s,service,receipt,price,host.Day);
                var use=PackageDUse.ForService(s,service);
                var slots=s.Economy.Business.Instruments[use.Entity].Slots.Values;
                foreach(int id in slots){
                    var part=s.Economy.Business.Components[id];var original=NativeMemberEconomy.Decode(part.Snapshot);
                    GeneralHelper.SetItemOwned(original,false);part.Snapshot=NativeMemberEconomy.Encode(original);
                }
                use.Components=slots.ToDictionary(id=>id,id=>s.Economy.Business.Components[id].Snapshot);
            });
        }
        internal void FinishUseWithoutWork(int entity)
        {
            var u=N.Uses[entity];var v=N.Visits.GetValueOrDefault(PlayerStore.instance.currentClientInstance?.storeClient?.identifier??"");
            if(v==null||v.Entity!=entity||!PlayerStore.instance.isClientArrived)throw new InvalidOperationException("原物主未到场");
            if(u.Status=="offered"){
                var copy=host.Engine.Snapshot();copy.Economy.Business.Instruments[entity].Owner="player";copy.Economy.Business.Instruments[entity].Custody="player";copy.Workshop.Advanced.Facts[entity].Owner="player";
                string block=PackageD.Eligible(copy,PackageDCatalog.Buyer(u.Target),entity,u.Zone,true,false,0,0,false);if(block!=null)throw new InvalidOperationException("还不满足新用途："+block);
                var item=NativePackageB.ItemList(PlayerStore.instance.currentClientInstance.gridInv.childItems).SingleOrDefault(x=>x.uniqueId==entity);if(item==null)throw new InvalidOperationException("原件未到店");var grid=item.parentInventory;bool removed=false;
                economy.MutateNative(()=>{removed=grid.Expel(item);if(!removed)throw new IOException("原件交回失败");host.Engine.EditMusicTrade(s=>{var r=s.MusicTrade.Next.Uses[entity];r.Status="done";r.Paid=0;r.Receipt="use_enough_"+entity;PackageDFull.History(s,r.Receipt,entity,host.Day,"use","原物主确认现有配置已满足新用途；未重复收费。");});},()=>{if(removed)grid.UncheckedAccept(item);});
            }else if(u.Status=="cancel_pickup")host.Engine.EditMusicTrade(s=>{var r=s.MusicTrade.Next.Uses[entity];r.Status="done";r.Receipt="use_cancel_return_"+entity;r.DueDay=-1;});
            else throw new InvalidOperationException("请按实际工单交收");
        }
        internal void CancelExternal(string service)
        {
            host.Engine.EditMusicTrade(s=>{PackageDUse.CheckCancel(s,service);PackageB.CancelService(s,service,host.Day);var u=PackageDUse.ForService(s,service);u.Snapshot=s.Workshop.Advanced.Services[service].HostSnapshot;u.Status="cancel_pickup";u.DueDay=host.Day+1;});
        }
        private void UpdateNext()
        {
            var store=PlayerStore.instance;var id=store.currentClientInstance?.storeClient?.identifier;
            if(NativeUseEnabled){RecoverNativeUse();BindUsePhone(store);BindNetworkPhone(store);}
            if(IsUseClient(id)&&store.isClientArrived){
                var v=N.Visits[id];var u=N.Uses[v.Entity];if(v.Status!="active")host.Engine.EditMusicTrade(s=>s.MusicTrade.Next.Visits[id].Status="active");
                if(v.Kind=="offer"&&u.Status=="offered"&&!NativePackageB.ItemList(store.currentClientInstance.gridInv.childItems).Any(x=>x.uniqueId==u.Entity)
                    &&!(NativeUseEnabled&&NativePackageB.ItemList(EmporiumEntry.Instance.frontInvinvElement.childItems).Any(x=>x.uniqueId==u.Entity))){
                    if(economy.FindPlayerItem(u.Entity)!=null)throw new InvalidDataException("外部原件与玩家库存冲突");
                    var item=NativeMemberEconomy.Decode(u.Snapshot);if(item.uniqueId!=u.Entity||item.identifier!=u.Sku)throw new InvalidDataException("续用快照身份损坏");GeneralHelper.SetItemOwned(item,false);
                    if(!NativePackageB.Place(store.currentClientInstance.gridInv,item))throw new IOException("客户原件无合法携带格位");
                }
            }
            long now=RuntimeCompat.TickCount64;if(now<nextObservation)return;nextObservation=now+1500;int elapsed=lastObservation==0?0:(int)Math.Min(1500,now-lastObservation);lastObservation=now;
            // Observation changes the shared state revision. Do not invalidate
            // a native dialogue's page/choice ticket while the player reads it.
            if(PanelOpen||economy.PackageB.PanelOpen||host.Campaign?.PanelOpen==true||host.Visible||core.Presentation?.Active==true)return;
            if(host.NormalPlay)IssueUsePaper();
            bool opened=store.storeState==PlayerStore.StoreState.OPEN;int cash=store.GetCash();bool controlled=core.Test?.ToolsMode==true||core.Test?.Request.AutoRun==true&&core.Test?.NaturalSamplingActive!=true||core.Test?.NaturalSampleViolated==true||host.Engine.Snapshot().Flags.GetValueOrDefault("ms_test_fixture_unlocked");
            var offers=new List<MusicObservedOffer>();var client=store.currentClientInstance;
            MusicObservedBuyer buyerObservation=null;
            if(client!=null&&store.isClientArrived&&client.storeClient.clientIntent==StoreClient.ClientIntent.BUY&&!IsUseClient(id)){
                var c=client.storeClient;var items=economy.AllPlayerItems().Where(x=>economy.PackageB.PlayerOwned(x)).ToArray();int matches=0,budgetBlocked=0;
                foreach(var item in items){if(!c.IsClientBuyingThisItem(item))continue;matches++;if(c.useClientBudget&&item.GetNegociatedValue()>c.GetBudget())budgetBlocked++;}
                buyerObservation=new(){Id=host.Day+"/"+c.identifier,Day=host.Day,Zone=Zone(c),RemainingBudget=c.useClientBudget?c.GetBudget():c.clientCash,MatchCount=matches,BudgetBlocked=budgetBlocked,Reason=matches==0?"当前无满足地区/类型/状态的可售现货":matches==budgetBlocked?"匹配现货的当前报价均超过剩余预算":"有当前可匹配现货；是否成交以实际回执为准"};
            }
            var perkNames=new List<string>();foreach(var reputation in store.storeReputations)foreach(var perk in reputation.perks)if(perk.isUnlocked)perkNames.Add(perk.perkId);var perks=perkNames.ToArray();
            if(client!=null&&store.isClientArrived&&client.storeClient.IsClientSelling())foreach(var item in NativePackageB.ItemList(client.gridInv.childItems).Where(x=>PackageDCatalog.Hosts.Contains(x.identifier))){string key=host.Day+"/"+client.storeClient.identifier+"/"+item.uniqueId;offers.Add(new(){Id=key,Day=host.Day,Entity=item.uniqueId,Sku=item.identifier,Visit=client.storeClient.identifier,Zone=Zone(client.storeClient),Quote=checked((int)item.GetNegociatedValue())});}
            host.Engine.EditMusicTrade(s=>{
                var n=s.MusicTrade.Next;if(!n.Days.TryGetValue(host.Day,out var day)){day=new(){Day=host.Day,OpeningCash=cash,Attractiveness=store.GetCurrentStoreAttractiveness(),UnlockedPerks=perks,Context="地区见实际供货/买方记录，预算见原生访问快照；"+(controlled?"受控会话":"正常游玩观察")};n.Days.Add(host.Day,day);}
                if(opened&&n.LastOpenedDay!=host.Day){n.LastOpenedDay=host.Day;n.BusinessOrdinal++;day.Opened=true;}
                day.Controlled|=controlled;day.ClosingCash=cash;if(economy.Workshop.CanClock())day.EffectiveMs+=elapsed;day.Inventory=economy.AllPlayerItems().Count();day.Pending=s.Workshop.Jobs.Values.Count(x=>!x.Terminal);day.OrdinarySlots=s.MusicTrade.Slots.Values.Count(x=>x.Day==host.Day);
                foreach(var old in n.Days.Values.Where(x=>x.Day<host.Day&&x.Opened))old.Closed=true;
                foreach(var offer in offers){if(n.Offers.TryGetValue(offer.Id,out var previous))offer.Decision=previous.Decision;offer.Purchased=s.MusicTrade.Receipts.Values.Any(x=>x.Kind=="purchase"&&x.Allocations.ContainsKey(offer.Entity));n.Offers[offer.Id]=offer;}
                foreach(var offer in n.Offers.Values)offer.Purchased|=s.MusicTrade.Receipts.Values.Any(x=>x.Kind=="purchase"&&x.Allocations.ContainsKey(offer.Entity));
                if(buyerObservation!=null)n.BuyerObservations[buyerObservation.Id]=buyerObservation;
            });
        }
        private void ExportObservation()
        {
            string folder=Path.Combine(core.Root,"ModWorkbench","reports","operating-observations");System.IO.Directory.CreateDirectory(folder);var state=host.Engine.Snapshot();
            string path=Path.Combine(folder,state.RunId+".json");File.WriteAllText(path,JsonSerializer.Serialize(new{Run=state.RunId,Build=TestSession.Hash(core.GetType().Assembly.Location),NaturalCompleteDays=N.Days.Values.Count(x=>x.Opened&&x.Closed&&!x.Controlled),Days=N.Days.Values,Offers=N.Offers.Values,BuyerObservations=N.BuyerObservations.Values,BuyerVisits=D.Buyers.Values,Receipts=D.Receipts.Values,FrozenSaleCosts=N.Realized.Values,Fees=D.Fees,Refunds=D.Refunds,Investments=D.Investments.Values,UnpricedCosts=D.Costs.Values.Where(x=>!x.Known),Uses=N.Uses.Values.Select(x=>new{x.Entity,x.Status,x.Paid}),Scope="实际观察；现金净变动不是利润，未售库存不算收入；受控会话不计自然十日"},Core.Json));panelNotice="已导出经营观察至 reports/operating-observations。";
        }
        private bool BuildNextCards(List<Card> rows,StoryState state)
        {
            if(nextView==null)return false;
            Add(rows,"返回音乐经营主页面。",()=>{nextView=null;panelPage=0;},"返回");
            if(nextView=="plans"){
                var items=economy.AllPlayerItems().Where(x=>economy.PackageB.PlayerOwned(x)).ToArray();var item=items.FirstOrDefault(x=>x.uniqueId==selectedHost&&PackageDCatalog.Hosts.Contains(x.identifier))??items.FirstOrDefault(x=>PackageDCatalog.Hosts.Contains(x.identifier));
                if(item==null){Add(rows,"当前没有玩家自有整机；购前不开放深度方案比较。");return true;}selectedHost=item.uniqueId;
                Add(rows,item.name+"；目标 "+PackageDCatalog.Buyer(planTarget).Name+"。只对已授权原件比较，不代替实际检查。",()=>{var targets=new[]{"B01","B02","B03","B04","B05"};planTarget=targets[(Array.IndexOf(targets,planTarget)+1)%targets.Length];planCacheKey=null;},"切换目标用途");
                string key=state.Revision+"/"+selectedHost+"/"+planTarget;if(planCacheKey!=key){var stock=items.GroupBy(x=>x.identifier).ToDictionary(g=>g.Key,g=>g.Sum(x=>x.unitCount));foreach(var r in NativeWorkshop.References)stock[r.Key]=stock.GetValueOrDefault(r.Value);foreach(string station in new[]{"electrobench","calibrator"}){string sku=station=="electrobench"?"ms_machine_electrobench":"ms_machine_calibrator";stock["station:"+station]=items.Count(x=>x.identifier==sku&&economy.PackageB.Working(x.uniqueId,sku)&&!economy.PackageB.MachineBusy(x.uniqueId));}stock["energy:E"]=items.Where(x=>x.unitCount==1&&x.IsTag("power_source_item")).Sum(x=>Math.Max(0,PowerHelper.GetAvailableEnergyFromItem(x,false)));var available=items.Select(x=>x.uniqueId).ToHashSet();string zone=planTarget=="B05"?"upper":"middle";planCache=new[]{PackageDNext.Current(state,selectedHost,"B01","middle"),PackageDNext.Plan(state,selectedHost,"B01","middle",stock,available),PackageDNext.Plan(state,selectedHost,planTarget,zone,stock,available)};planCache[2].Name="指定用途改装";planCacheKey=key;}
                foreach(var p in planCache){string reason=p.Block??"保留满足要求的原有状态和配件";foreach(var keyName in p.Inputs.Keys.Concat(p.Tools.Keys))reason=reason.Replace(keyName,Product(keyName));if(reason.Length>42)reason=reason.Substring(0,40)+"…";Add(rows,p.Name+" / "+PackageDCatalog.Buyer(p.Target).Name+"："+(p.Meets?(p.Executable?"已知条件具备":"规格可达，输入待补"):"暂不可判断或达成")+"\n"+reason+"；新增 "+p.Seconds+"秒 / "+p.Energy+"E / "+p.Parts+"件。");}
                foreach(var p in planCache.Where(x=>x.Steps.Count>0)){Add(rows,p.Name+"工序："+string.Join(" → ",p.Steps)+"。\n完成后降噪/稳定/响应/便携 "+string.Join("/",new[]{"noiseControl","stability","response","portability"}.Select(x=>p.Stats.GetValueOrDefault(x)))+"；只作方案比较，当前报告和配置不改变。");if(p.Inputs.Count>0||p.Tools.Count>0)Add(rows,"实际所需："+string.Join("、",p.Inputs.Concat(p.Tools).Select(x=>Product(x.Key)+"×"+x.Value))+"。工具保留，耗材、电量和机器条件在实际开工时核对。");}
                var cost=PackageDFull.TotalCost(state,selectedHost);var current=PlayerStore.instance.currentClientInstance?.storeClient;string quote=current!=null&&current.IsClientBuyingThisItem(item)?checked((int)item.GetNegociatedValue()).ToString():"暂无实际买方报价";
                Add(rows,"已知总成本 "+(cost?.ToString()??"有未知投入")+"；当前参考 "+item.GetCurrentValue()+"；当前报价 "+quote+"。计划耗材/能源采购成本未核实时不计算预计利润。");
                foreach(var r in D.Receipts.Values.Where(x=>x.Kind=="sale"&&x.Allocations.Keys.Any(id=>state.Workshop.Advanced.Facts.GetValueOrDefault(id)?.Sku==item.identifier)).OrderByDescending(x=>x.Day).Take(2))Add(rows,"历史同型号实际成交：第"+r.Day+"天 "+r.Total+"；当时配置/顾客不同，不是未来保证售价。");
                Add(rows,"准备实际工具、输入、工位和空间后，在现有维护中逐步办理。",()=>Maintenance(selectedHost),"进入维护");
            }else if(nextView=="observation"){
                Add(rows,"自然完整营业日 "+N.Days.Values.Count(x=>x.Opened&&x.Closed&&!x.Controlled)+" / 10；受控会话单独记录，不用于证明自然盈利。",ExportObservation,"导出观察记录");
                foreach(var day in N.Days.Values.OrderByDescending(x=>x.Day)){var d=day;Add(rows,"第"+d.Day+"天 "+(d.Controlled?"受控":"自然观察")+"；现金 "+d.OpeningCash+" → "+d.ClosingCash+"；可加工营业时长 "+d.EffectiveMs/1000+"秒；库存 "+d.Inventory+"；工位/工单待领 "+d.Pending+"。现金净变动不等于利润。");}
                foreach(var offer in N.Offers.Values.OrderByDescending(x=>x.Day)){var o=offer;Add(rows,"第"+o.Day+"天实际卖方报价 "+o.Quote+"；"+Product(o.Sku)+"；"+(o.Purchased?"已实际收购":o.Decision),()=>host.Engine.EditMusicTrade(s=>{var entry=s.MusicTrade.Next.Offers[o.Id];var choices=new[]{"报价过高，放弃","缺少用途或销路","资金或空间不足","未记录决定"};entry.Decision=choices[(Array.IndexOf(choices,entry.Decision)+1)%choices.Length];}),"记录放弃原因");}
                foreach(var buyer in N.BuyerObservations.Values.OrderByDescending(x=>x.Day))Add(rows,"第"+buyer.Day+"天买方："+buyer.Reason+"；预算余 "+buyer.RemainingBudget+"。这是机会观察，不推断玩家拒绝意图。");
            }else if(nextView=="cases"){
                for(int i=0;i<PackageDNext.CaseThemes.Length;i++){string context=i==0&&state.CompletedDays.ContainsKey("ms_scene_010")?"排练准备主题 · ":i==1&&state.CompletedDays.ContainsKey("ms_scene_016")?"舞台接口主题 · ":i==2&&PackageDFull.DemandMode(state)=="independent"?"翻新与兼容主题 · ":i==2&&PackageDFull.DemandMode(state)=="commercial"?"规格一致主题 · ":"";Add(rows,context+"教学示例 · "+PackageDNext.CaseThemes[i]);}
                foreach(var entry in N.Cases.Values.OrderByDescending(x=>x.Day)){var e=entry;Add(rows,"本店实例 · 第"+e.Day+"天："+e.Summary+"\n"+(e.Kind=="service"?"本次新增投入":"成交时原件成本")+" "+(e.Cost?.ToString()??"未知")+"；实收 "+e.ActualReceived+"。",()=>{selectedHost=economy.AllPlayerItems().FirstOrDefault(x=>x.identifier==state.Workshop.Advanced.Facts[e.Entity].Sku&&economy.PackageB.PlayerOwned(x))?.uniqueId??0;nextView="plans";planCacheKey=null;panelPage=0;},"对照当前同型号");Add(rows,"当时配置："+(e.Before.Count>0?string.Join("、",e.Before.Select(x=>x.Key+"="+Product(x.Value.Split(" / ")[0])))+" → ":"")+ (e.After.Count>0?string.Join("、",e.After.Select(x=>x.Key+"="+Product(x.Value.Split(" / ")[0]))):"保留封装标配"));}
                Add(rows,"只取匿名技术事实；案例不复制原件经历，不发校准报告，不增加售价倍率。公开档案360仍禁用。");
            }else if(nextView=="use"){
                var active=N.Visits.GetValueOrDefault(PlayerStore.instance.currentClientInstance?.storeClient?.identifier??"");
                if(active!=null){var u=N.Uses[active.Entity];Add(rows,UseStatus(u.Status)+" · "+Product(u.Sku)+"；新用途 "+PackageDCatalog.Buyer(u.Target).Name+"；剩余支付能力 "+Math.Min(u.Budget,u.Cash)+"。无需重新购买整机。");
                    if(u.Status=="offered"){Add(rows,"先比较原件；已经满足且报告有效时免费确认，不重复装配。",()=>FinishUseWithoutWork(u.Entity),"已足够 · 免费确认");Add(rows,"受理范围：检查、当前用途的空槽适配与校准；保留旧件；取消须实际卸回新增垫件。最终收费可降低，最多为本次支付能力。",()=>IntakeUse(u.Entity,Math.Min(u.Budget,u.Cash)),"确认交入维护");Add(rows,"拒接不会收费、损坏器材或重抽新用途。",()=>host.Engine.EditMusicTrade(s=>s.MusicTrade.Next.Uses[u.Entity].Status="declined"),"暂不承接此事项");}
                    if(u.Status=="cancel_pickup")Add(rows,"取消后的同一原件，按已完成工序状态免费交回。",()=>FinishUseWithoutWork(u.Entity),"原件交回本人");
                }
                foreach(var u in N.Uses.Values.Where(x=>x.Status is "servicing" or "pickup" or "cancel_pickup")){var r=u;var svc=state.Workshop.Advanced.Services[r.Service];Add(rows,Product(r.Sku)+"；"+UseStatus(r.Status)+"；"+(PackageDUse.ReadyBlock(state,r)??"本次用途已满足")+"。",()=>{ClosePanel();economy.PackageB.OpenService(r.Service);},"继续同一工单");if(r.Status=="servicing")Add(rows,"完成后通知次日领取；占普通位置，没有可安排位置时等待。",()=>BookUse(r.Entity),"完工 · 通知领取");}
                foreach(var service in state.Workshop.Advanced.Services.Values.Where(x=>x.Actor=="outside"&&(x.Delivery!=null||x.Status=="return_ready")))foreach(var part in service.Held.Values.Where(x=>x.Owner=="player"&&!service.EffectiveSlots.Values.Contains(x.Item.Entity))){string sid=service.Id;int entity=part.Item.Entity;Add(rows,"玩家工具/垫件待取："+Product(part.Item.ItemId)+"；不占已交回原件的普通保管位。",()=>{if(!economy.PackageB.Claim(sid,entity))throw new InvalidOperationException("没有合法空间，仍可稍后取回");},"取回这一实物");}
                Add(rows,"普通客器材一次保管1件，成员工单仍在原入口。接手的器材属于客人，原有配件继续保留；实际营业后才可能出现新的用途需求。");
            }
            return true;
        }
        private void AddNextEntrances(List<Card> rows)
        {
            if(panelTab is "work" or "market")Add(rows,"比较现状、最低合格与指定用途；不替未知状态编利润。",()=>{nextView="plans";panelPage=0;planCacheKey=null;},"比较整备方案");
            if(panelTab=="overview"){Add(rows,"查看普通客户原件续用、待交回与新的用途。",()=>{nextView="use";panelPage=0;},"普通原件续用");Add(rows,"记录真实营业报价、现金、库存和未成交原因。",()=>{nextView="observation";panelPage=0;},"自然经营观察");}
            if(panelTab=="ledger")Add(rows,"教学示例与实际工序/交付案例；可对照当前器材。",()=>{nextView="cases";panelPage=0;},"技术案例");
        }
        internal void OpenNextPanel(string view,int entity=0){OpenPanel(view=="cases"?"ledger":"overview");nextView=view;selectedHost=entity;panelPage=0;planCacheKey=null;}
        internal void ObserveNow(){nextObservation=0;UpdateNext();}
        internal void ExportNaturalObservation()=>ExportObservation();
    }
}
