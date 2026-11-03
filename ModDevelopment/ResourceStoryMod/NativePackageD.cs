using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageD
    {
        private readonly Core core;
        private readonly NativeStoryHost host;
        private readonly NativeMemberEconomy economy;
        private bool attached,restoring;
        private int normalDepth;
        private int displayDepth;
        private readonly Dictionary<string,System.Func<StoreClient>> factories=new();
        private readonly Dictionary<long,string> ordinaryClients=new();
        private readonly Dictionary<long,StoreClient> ordinaryRoots=new();
        private MusicTradeState D=>host.Engine.Snapshot().MusicTrade;
        internal bool Enabled=>attached && economy.Enabled && host.Engine?.Snapshot().MusicTrade?.Enabled==true;
        private static NativePackageD Self=>Core.Instance?.Story?.Economy?.Trade;
        internal NativePackageD(Core core,NativeStoryHost host,NativeMemberEconomy economy)
        {
            this.core=core;this.host=host;this.economy=economy;
            Patch(typeof(StoreClientManager),"HandleNormalClient",nameof(BeginNormal),null,nameof(EndNormal));
            Patch(typeof(StoreClientManager),"HandleShowcaseClient",nameof(BeginDisplay),null,nameof(EndDisplay));
            // HandleNormalClient calls PickClient -> PickRandomClient. Shopper
            // selection belongs to another native path and misses ordinary slots.
            Patch(typeof(StoreClientManager),"AddClient",nameof(BeforeNormalAdd),null);
            Patch(typeof(StoreClient),"IsClientBuyingThisItem",nameof(Candidate),null);
            Patch(typeof(PlayerStore),"CanSellThisItem",nameof(CanSell),null);
            Patch(typeof(PlayerStore),"SellItem",nameof(BeforeSale),nameof(AfterSale));
            Patch(typeof(NegociationUIManager),"OnWholesaleClick",nameof(BeforeWholesale),null);
            Patch(typeof(PlayerStore),"DismissCurrentClient",nameof(Dismissed),null);
            Patch(typeof(NotebookUIManager),"UpdateUI",nameof(NotebookDuringTransfer),null);
            Patch(typeof(PlayerStore),"OnWeightedAreaChanged",nameof(WeightedDuringTransfer),null);
            Patch(typeof(StorePhoneClient),"GetCallDialog",nameof(NativeUseCall),null);
            Patch(typeof(StorePhoneClient),"UseService",nameof(NativeUsePhoneService),null);
            Patch(typeof(StorePhoneClient),"ShownInPhoneBook",nameof(NativeUsePhoneShown),null);
            Patch(typeof(PhoneUIManager),"WillAnswerCall",nameof(NativeUsePhoneAnswer),null);
            Patch(typeof(StorePhoneClient),"GetCallDialog",nameof(NetworkPhoneCall),null);
            Patch(typeof(StorePhoneClient),"UseService",nameof(NetworkPhoneService),null);
            Patch(typeof(StorePhoneClient),"ShownInPhoneBook",nameof(NetworkPhoneShown),null);
            Patch(typeof(StorePhoneClient),"GetLocalizedDisplayName",nameof(NetworkPhoneName),null);
            Patch(typeof(PhoneUIManager),"WillAnswerCall",nameof(NetworkPhoneAnswer),null);
        }
        private void Patch(Type type,string name,string prefix,string postfix,string finalizer=null)=>core.HarmonyInstance.Patch(ReleaseApi.Method(type,name),prefix==null?null:new HarmonyMethod(typeof(NativePackageD),prefix),postfix==null?null:new HarmonyMethod(typeof(NativePackageD),postfix),finalizer:finalizer==null?null:new HarmonyMethod(typeof(NativePackageD),finalizer));
        // Removing owned gear from a weighted counter can refresh a notebook
        // with no current customer. There is no negotiated view to redraw here.
        private static bool NotebookDuringTransfer()=>Self?.Enabled!=true||!Self.economy.IsTransferring||PlayerStore.instance?.currentClientInstance!=null;
        // The native callback dereferences a selected item even when a mod
        // transfer has removed it. With no negotiated item there is no offer to refresh.
        private static bool WeightedDuringTransfer()=>Self?.Enabled!=true||!Self.economy.IsTransferring||PlayerStore.instance?.currentNegociatedItem!=null;
        internal bool Attach()
        {
            attached=core.Test==null || core.Test.Request.NormalPlay || core.Test.Request.PackageD || core.Test.Request.Mode=="packaged";
            if(!attached)return false;
            bool changed=host.Engine.EnableMusicTrade();
            foreach(var v in D.Buyers.Values)RegisterFactory(v.Id);
            foreach(string id in D.Returns.Keys)RegisterReturn(id);
            foreach(var visit in D.Next.Visits.Values)RegisterUse(visit.Id);
            RestoreQueued();foreach(var item in economy.AllPlayerItems()){if(D.Destroyed.Contains(item.uniqueId))throw new InvalidDataException("已报废源在库存中重新出现");economy.PackageB.Present(item);}return changed;
        }
        internal void Detach(){ClosePanel();attached=false;restoring=false;normalDepth=0;displayDepth=0;var directory=StoreClientListDict.storeClientDict;if(directory!=null)foreach(string id in factories.Keys)directory.Remove(id);factories.Clear();ordinaryClients.Clear();ordinaryRoots.Clear();}
        private static void BeginNormal(){if(Self?.Enabled==true)Self.normalDepth++;}
        private static void BeginDisplay(){if(Self?.Enabled==true)Self.displayDepth++;}
        private static Exception EndDisplay(Exception __exception){if(Self!=null)Self.displayDepth=Math.Max(0,Self.displayDepth-1);return __exception;}
        private static Exception EndNormal(Exception __exception){var p=Self;if(p!=null){p.normalDepth=Math.Max(0,p.normalDepth-1);if(p.Enabled&&p.normalDepth==0&&__exception==null){p.ResolveMissedReferrals();p.host.Visits?.GenerationComplete("native_normal_complete");}}return __exception;}
        private static bool BeforeNormalAdd(StoreClientManager __instance,ref StoreClient __0)
        {
            var p=Self;if(p?.Enabled!=true||(p.normalDepth<=0&&p.displayDepth<=0)||__0==null)return true;
            if(p.economy.IsTransferring){__0=p.AdaptShopper(__0);return __0!=null;}
            var original=__0;p.economy.MutateNative(()=>__instance.AddClient(original),()=>{});return false;
        }
        internal StoreClient AdaptShopper(StoreClient original)
        {
            if(!Enabled || !BasicOrdinary(original))return original;
            string zone=Zone(original);if(zone==null)return original;
            var state=host.Engine.Snapshot();string ordinalKey=host.Day.ToString();int ordinal=state.MusicTrade.NativePickOrdinals.GetValueOrDefault(ordinalKey)+1;
            string ticket="d_slot_"+host.Day+"_"+ordinal;
            string displayCategory=DisplayCategory(original);
            bool display=displayDepth>0;
            var samples=display&&displayCategory!=null?DisplaySamples(zone):new List<MusicDisplaySample>();
            int share=displayCategory==null?0:PackageDFull.DisplayShare(samples,displayCategory);
            host.Engine.EditMusicTrade(s=>{s.MusicTrade.NativePickOrdinals[ordinalKey]=ordinal;s.MusicTrade.Slots.Add(ticket,new(){Id=ticket,Day=host.Day,OriginalClient=original.identifier,Zone=zone,Source=display?"display":"natural",Category=displayCategory,MusicShare=share,MusicRoots=samples.Where(x=>x.Music&&x.Category==displayCategory).Select(x=>x.Entity).ToArray()});});
            ordinaryClients[original.NativeIdentity().ToInt64()]=ticket;
            ordinaryRoots[original.NativeIdentity().ToInt64()]=original;
            foreach(long old in ordinaryClients.Keys.Where(k=>k!=original.NativeIdentity().ToInt64()&&D.Slots.TryGetValue(ordinaryClients[k],out var oldSlot)&&oldSlot.Day<host.Day).ToArray()){ordinaryClients.Remove(old);ordinaryRoots.Remove(old);}
            var referred=display||displayCategory!=null?null:TryReferral(original,ticket,zone);if(referred!=null)return referred;
            var pickup=display?null:TryUse(original,ticket,zone,true);if(pickup!=null)return pickup;
            if(!display&&host.Visits?.CaptureNaturalSlot(ticket)==true)return null;
            var inspection=display?null:host.Inspection?.Adapt(original,ticket);if(inspection!=null)return inspection;
            if(original.clientIntent!=StoreClient.ClientIntent.BUY||!PackageDFull.NaturalSlotAvailable(host.Engine.Snapshot(),host.Day))return original;
            int chance=display?share:2500;
            if(PackageBSupply.Roll(state.RunId+"/"+ticket+"/music",10000)>=chance)return original;
            var use=display?null:TryUse(original,ticket,zone,false);if(use!=null)return use;
            var displayIds=display?samples.Where(x=>x.Music&&x.Category==displayCategory).Select(x=>x.Entity).ToHashSet():null;
            var inventory=economy.AllPlayerItems().Where(x=>(displayIds==null||displayIds.Contains(x.uniqueId))&&economy.PackageB.PlayerOwned(x)&&!economy.Workshop.BlocksSale(x)).ToArray();
            var candidates=PackageDCatalog.Buyers.Where(profile=>profile.Zones.Contains(zone)&&inventory.Any(item=>PackageD.Eligible(host.Engine.Snapshot(),profile,item.uniqueId,zone,true,item.IsTag(MusicCatalog.ProtectedTag),0,0,false)==null)).ToArray();
            if(candidates.Length==0)return original;
            int roll=PackageBSupply.Roll(state.RunId+"/"+ticket+"/profile",candidates.Sum(x=>PackageDFull.DemandWeight(state,x.Id)));var selected=candidates.Last();foreach(var p in candidates){roll-=PackageDFull.DemandWeight(state,p.Id);if(roll<0){selected=p;break;}}
            return CreateBuyer(ticket,selected,zone,original);
        }
        internal static string Zone(StoreClient c)=>c?.clientFaction==StoreClient.FACTION_UPPER?"upper":c?.clientFaction==StoreClient.FACTION_MIDDLE?"middle":c?.clientFaction==StoreClient.FACTION_LOWER?"lower":null;
        internal bool IsNaturalQueued(StoreClient c)=>TryNaturalTicket(c,out _);
        private bool TryNaturalTicket(StoreClient c,out string ticket)
        {
            ticket=null;if(!Enabled||!BasicOrdinary(c)||!ordinaryClients.TryGetValue(c.NativeIdentity().ToInt64(),out var key))return false;
            if(!D.Slots.TryGetValue(key,out var slot)||slot.OriginalClient!=c.identifier||slot.Day!=host.Day||slot.Source!="natural"||slot.Buyer!=null||host.ReadState.NormalVisits?.SlotClaims.ContainsKey(key)==true)return false;
            ticket=key;return true;
        }
        internal StoreClient InspectQueued(StoreClient c)=>TryNaturalTicket(c,out var ticket)?host.Inspection.Adapt(c,ticket):null;
        internal bool ReservePickupSlot(string visitKey)
        {
            var queue=PlayerStore.instance.storeClientManager.clientStack;
            for(int index=0;index<queue.Count;index++){
                var c=queue[index];if(!TryNaturalTicket(c,out var ticket))continue;int at=index;bool removed=false;
                economy.MutateNative(()=>{queue.RemoveAt(at);removed=true;host.Engine.EditNormalVisits(s=>NormalVisits.Claim(s.NormalVisits,s.NormalVisits.Visits[visitKey],ticket));},()=>{if(removed)queue.Insert(at,c);});return true;
            }
            return false;
        }
        internal StoreClient CreateBuyer(string ticket,MusicBuyerProfile profile,string zone,StoreClient prototype)
        {
            if(!D.Slots.ContainsKey(ticket))throw new InvalidOperationException("没有原生普通位置凭据");
            if(D.Slots[ticket].Buyer!=null)return BuildBuyer(D.Slots[ticket].Buyer,false);
            string id=PackageDCatalog.BuyerPrefix+ticket;
            host.Engine.EditMusicTrade(s=>{
                var v=new MusicBuyerVisit{Id=id,Profile=profile.Id,Ticket=ticket,Zone=zone,Day=host.Day,BaseBudget=profile.Budget,Sprite=prototype.spriteName,BuyModifier=prototype.buyPriceModifier,SellModifier=prototype.sellPriceModifier,AcceptMarkup=prototype.acceptRetailMarkup};
                s.MusicTrade.Buyers.Add(id,v);s.MusicTrade.Slots[ticket].Buyer=id;
            });
            var buyer=BuildBuyer(id,true);RegisterFactory(id);return buyer;
        }
        private void RegisterFactory(string id)
        {
            if(factories.ContainsKey(id))return;
            var callback=(System.Func<StoreClient>)(Func<StoreClient>)(()=>BuildBuyer(id,false));factories[id]=callback;
            StoreClientListDict.storeClientDict[id]=callback;
        }
        private StoreClient BuildBuyer(string id,bool initialize)
        {
            var v=D.Buyers[id];var profile=PackageDCatalog.Buyer(v.Profile);
            core.EnsureChineseFallback();
            var c=new StoreClient{identifier=id,displayName=profile.Name,spriteName=v.Sprite,clientIntent=StoreClient.ClientIntent.BUY,clientFaction=v.Zone=="upper"?StoreClient.FACTION_UPPER:v.Zone=="middle"?StoreClient.FACTION_MIDDLE:StoreClient.FACTION_LOWER,dismissable=true,nonShootable=true,clientCash=v.NativeCash,buyPriceModifier=v.BuyModifier,sellPriceModifier=v.SellModifier,acceptRetailMarkup=v.AcceptMarkup,mainDialogue=new Dialogue().SetText(profile.Name,profile.Faulty?"想收些已经查明故障的器材。":"想看看符合这次用途的器材。"),allDoneDialogue=new Dialogue().SetText(profile.Name,"谢谢，我再看看。")};
            c.clientBuyingTagList.Add(profile.Id=="B07"?"MATERIAL":"HOUSEHOLD_GOOD");if(profile.Id=="B07")c.clientBuyingTagList.Add("TOOL");
            if(profile.Id!="B07"&&profile.Id!="B06")c.clientBuyingTagList.Add("MODULE");
            c.CompleteClientCreation(false);
            if(initialize){
                c.SetBudget(profile.Budget);int effective=c.GetBudget();
                host.Engine.EditMusicTrade(s=>{var saved=s.MusicTrade.Buyers[id];saved.InitialBudget=effective;saved.RemainingBudget=effective;saved.NativeCash=c.clientCash;});
            }else{c.useClientBudget=true;c.clientBudget=v.RemainingBudget;c.clientCash=v.NativeCash;c.isBusinessComplete=v.BusinessComplete;c.isIntroduced=v.Introduced;c.isMainDialogueStarted=v.DialogueStarted;c.acceptDealDialoguePlayed=v.AcceptDeal;c.varietyTrackingList.Clear();foreach(string item in v.Variety)c.varietyTrackingList.Add(item);}
            return c;
        }
        internal void Capture()
        {
            if(!Enabled || restoring)return;var c=PlayerStore.instance.currentClientInstance?.storeClient;
            if(c==null || !D.Buyers.TryGetValue(c.identifier,out var v))return;
            if(!c.useClientBudget)throw new InvalidOperationException("普通音乐买家预算被意外关闭");
            var variety=c.varietyTrackingList.ToArray();
            if(v.Status=="active" && D.CurrentBuyer==v.Id && v.RemainingBudget==c.GetBudget()&&v.NativeCash==c.clientCash&&v.BusinessComplete==c.isBusinessComplete&&v.Introduced==c.isIntroduced&&v.DialogueStarted==c.isMainDialogueStarted&&v.AcceptDeal==c.acceptDealDialoguePlayed&&v.Variety.SequenceEqual(variety))return;
            host.Engine.EditMusicTrade(s=>{var b=s.MusicTrade.Buyers[c.identifier];b.Status="active";b.RemainingBudget=c.GetBudget();b.NativeCash=c.clientCash;b.BusinessComplete=c.isBusinessComplete;b.Introduced=c.isIntroduced;b.DialogueStarted=c.isMainDialogueStarted;b.AcceptDeal=c.acceptDealDialoguePlayed;b.Variety=variety;s.MusicTrade.CurrentBuyer=b.Id;});
        }
        private void RestoreQueued()
        {
            if(!Enabled)return;var store=PlayerStore.instance;restoring=true;
            try{
                foreach(var v in D.Buyers.Values.Where(x=>x.Status!="closed").ToArray()){
                    if(v.Day!=host.Day){host.Engine.EditMusicTrade(s=>{s.MusicTrade.Buyers[v.Id].Status="closed";if(s.MusicTrade.CurrentBuyer==v.Id)s.MusicTrade.CurrentBuyer=null;});continue;}
                    bool exists=store.currentClientInstance?.storeClient?.identifier==v.Id;
                    if(store.storeClientManager.clientStack!=null)foreach(var queued in store.storeClientManager.clientStack)exists|=queued.identifier==v.Id;
                    if(!exists)store.storeClientManager.AddNextClient(BuildBuyer(v.Id,false));
                }
                foreach(string id in D.Returns.Keys.ToArray()){
                    var r=D.Referrals.Values.FirstOrDefault(x=>x.Client==id);if(r==null||r.Day!=host.Day){host.Engine.EditMusicTrade(s=>s.MusicTrade.Returns.Remove(id));continue;}
                    bool exists=store.currentClientInstance?.storeClient?.identifier==id;foreach(var c in store.storeClientManager.clientStack)exists|=c.identifier==id;if(!exists)store.storeClientManager.AddNextClient(BuildReturn(id));
                }
                RestoreUses();
            }finally{restoring=false;}
        }
        private static void Dismissed()
        {
            var p=Self;if(p?.Enabled!=true)return;var c=PlayerStore.instance.currentClientInstance?.storeClient;if(c==null)return;
            if(p.IsUseClient(c.identifier)){p.CloseUseVisit(c.identifier);return;}
            if(p.IsReturnedSeller(c.identifier)){
                string origin=p.D.Returns[c.identifier];
                p.economy.PackageB.CaptureSupplier();
                p.host.Engine.EditMusicTrade(s=>{
                    s.MusicTrade.Returns.Remove(c.identifier);
                    var workshop=s.Workshop.Advanced;
                    if(workshop.CurrentVisit==origin){workshop.Visits[origin].Status="closed";workshop.CurrentVisit=null;}
                });
                p.host.Flush();return;
            }
            if(!p.D.Buyers.ContainsKey(c.identifier))return;
            p.host.Engine.EditMusicTrade(s=>{var v=s.MusicTrade.Buyers[c.identifier];v.Status="closed";v.RemainingBudget=c.GetBudget();v.NativeCash=c.clientCash;if(s.MusicTrade.CurrentBuyer==v.Id)s.MusicTrade.CurrentBuyer=null;});p.host.Flush();
        }
        private string Block(StoreClient c,GameItem item,bool ignoreBudget)
        {
            if(!Enabled || c==null || item==null)return null;
            if(IsUseClient(c.identifier))return UsePickup(c,item)==null?"本次来访仅办理原件适配和领取":null;
            if(!Enabled || c==null || item==null || !D.Buyers.TryGetValue(c.identifier,out var v))return null;
            if(v.Status=="closed")return "本次买家已经离开";
            int quote=checked((int)item.GetNegociatedValue());
            return PackageD.Eligible(host.Engine.Snapshot(),PackageDCatalog.Buyer(v.Profile),item.uniqueId,Zone(c),economy.PackageB.PlayerOwned(item),item.IsTag(MusicCatalog.ProtectedTag)||economy.Workshop.BlocksSale(item),quote,c.GetBudget(),!ignoreBudget);
        }
        private static bool Candidate(StoreClient __instance,GameItem __0,ref bool __result){var p=Self;if(p?.NativeUseEnabled==true&&p.IsUseClient(__instance.identifier)){__result=p.UsePickup(__instance,__0)!=null;return false;}if(p?.Block(__instance,__0,true)==null)return true;__result=false;return false;}
        private static bool CanSell(GameItem __0,bool __1,ref bool __result){if(Self?.Block(PlayerStore.instance.currentClientInstance?.storeClient,__0,__1)==null)return true;__result=false;return false;}
        private sealed class SaleState {internal string Buyer,Receipt,Ticket,Snapshot;internal Dictionary<int,string> Components=new();internal int Entity,Cash,Quote;}
        private static bool BeforeSale(GameItem __0,out SaleState __state)
        {
            __state=null;var p=Self;if(p?.Enabled!=true)return true;var store=PlayerStore.instance;var c=store.currentClientInstance?.storeClient;
            if(c==null)return true;
            // Entrusted inspection has its own atomic sale. A stale native
            // pointer must never turn its pickup into an ordinary sale wrapper.
            if(p.host.Inspection?.Managed(__0)==true)return true;
            if(p.HoldsNativeUse(__0))return p.SellNativeUse(c,__0);
            bool isMusic=p.D.Buyers.ContainsKey(c.identifier);string ordinaryTicket=null;
            if(!isMusic&&!p.ordinaryClients.TryGetValue(c.NativeIdentity().ToInt64(),out ordinaryTicket))return true;
            if(!isMusic&&(!p.D.Slots.TryGetValue(ordinaryTicket,out var slot)||slot.OriginalClient!=c.identifier))return true;
            if(!p.economy.IsTransferring){p.economy.MutateNative(()=>store.SellItem(__0),()=>{});return false;}
            string block=p.Block(c,__0,false);if(block!=null)throw new InvalidOperationException(block);
            __state=new(){Buyer=isMusic?c.identifier:null,Ticket=ordinaryTicket,Entity=__0.uniqueId,Cash=store.GetCash(),Quote=checked((int)__0.GetNegociatedValue()),Snapshot=NativeMemberEconomy.Encode(__0),Receipt="sale_"+(isMusic?c.identifier:ordinaryTicket)+"_"+__0.uniqueId};
            if(p.host.Engine.Snapshot().Economy.Business.Instruments.TryGetValue(__0.uniqueId,out var soldGear))foreach(int component in soldGear.Slots.Values)__state.Components[component]=p.host.Engine.Snapshot().Economy.Business.Components[component].Snapshot;
            return true;
        }
        private static void AfterSale(SaleState __state)
        {
            var p=Self;if(__state==null || p?.Enabled!=true)return;var store=PlayerStore.instance;var c=store.currentClientInstance.storeClient;
            int paid=store.GetCash()-__state.Cash;if(paid!=__state.Quote || p.economy.FindPlayerItem(__state.Entity)!=null&&GeneralHelper.IsItemOwned(p.economy.FindPlayerItem(__state.Entity)))throw new InvalidOperationException("原生销售现金或物权未完成");
            if(__state.Buyer!=null)p.host.Engine.EditMusicTrade(s=>PackageD.Sale(s,__state.Receipt,__state.Buyer,__state.Entity,paid,c.GetBudget(),c.clientCash));
            else p.host.Engine.EditMusicTrade(s=>{if(!s.MusicTrade.Receipts.ContainsKey(__state.Receipt))s.MusicTrade.Receipts.Add(__state.Receipt,new(){Id=__state.Receipt,Kind="ordinary_sale",Visit=__state.Ticket,Day=p.host.Day,Total=paid,Allocations=new(){{__state.Entity,paid}}});if(paid>0)s.MusicTrade.OrdinaryFulfillments.Add(__state.Ticket);});
            p.economy.Business.RecordNativeOwnership(__state.Entity,false);
            if(__state.Buyer!=null&&PackageDCatalog.Hosts.Contains(p.host.Engine.Snapshot().Workshop.Advanced.Facts.GetValueOrDefault(__state.Entity)?.Sku)){
                var outbound=NativeMemberEconomy.Decode(__state.Snapshot);GeneralHelper.SetItemOwned(outbound,false);string snapshot=NativeMemberEconomy.Encode(outbound);
                p.host.Engine.EditMusicTrade(s=>{var components=s.Economy.Business.Instruments[__state.Entity].Slots.Values.ToDictionary(id=>id,id=>s.Economy.Business.Components[id].Snapshot);PackageDUse.Sold(s,__state.Entity,__state.Receipt,__state.Buyer,snapshot,components);PackageDNext.Case(s,"case_"+__state.Receipt,__state.Entity,p.host.Day,"sale",__state.Receipt,"按当前用途完成普通器材交付；成交事实可查，参考值不算收入。");});
            }
        }
        internal void Update(){if(Enabled){Capture();UpdateNext();foreach(var r in D.Referrals.Values.Where(x=>x.Status=="confirmed"&&x.Day<host.Day).ToArray())ResolveMissedReferrals();}}
    }
}
