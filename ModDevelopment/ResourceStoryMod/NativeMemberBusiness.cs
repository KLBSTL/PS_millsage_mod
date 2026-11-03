using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    internal sealed partial class NativeMemberBusiness
    {
        private readonly Core core;
        private readonly NativeStoryHost host;
        private readonly NativeMemberEconomy economy;
        private readonly List<System.Delegate> roots=new();
        private readonly Dictionary<long,string> phones=new();
        internal bool OwnsPhone(long number)=>phones.ContainsKey(number);
        private readonly List<System.Func<GameItem>> factories=new();
        private long refreshed=-1;
        private int lastDay=-1;
        private bool panel;
        private float timeBefore;
        private StoreUIManager lockedUI;
        private GUIStyle body,button;
        private Vector2 scroll;
        private string message="", tab="contacts", price="350";
        private int selectedGear;
        internal string HeldStoryActor {get;private set;}
        internal string ResidenceFaction(string actor){string zone=host.Package.Actors.FirstOrDefault(a=>a.Id==actor)?.ResidenceZone;if(string.IsNullOrEmpty(zone))zone=MemberBusiness.ResidenceZone(actor);return zone=="upper"?StoreClient.FACTION_UPPER:zone=="middle"?StoreClient.FACTION_MIDDLE:StoreClient.FACTION_LOWER;}
        internal string ActorNameForNote(string actor)=>ActorName(actor);
        internal const string Prefix="ms_member_visit_";
        internal bool Scheduling => economy.Enabled && (core.Test==null || core.Test.PackageA || core.Test.ToolsMode || core.Test.OperatingTrialActive);
        internal bool PanelOpen=>panel;
        internal int PanelRenderedRows {get;private set;}
        internal string CurrentActor {
            get {
                var id=PlayerStore.instance?.currentClientInstance?.storeClient?.identifier;
                return id?.StartsWith(Prefix,StringComparison.Ordinal)==true ? id.Substring(Prefix.Length) : HeldStoryActor;
            }
        }
        internal NativeMemberBusiness(Core core,NativeStoryHost host,NativeMemberEconomy economy)
        {
            this.core=core;this.host=host;this.economy=economy;
            Patch(typeof(StoreClient),"StartMainDialogue",nameof(BeforeDialogue),null);
            Patch(typeof(StoreClient),"IsClientBuyingThisItem",nameof(ProtectedCandidate),null);
            Patch(typeof(PlayerStore),"CanSellThisItem",nameof(ProtectedQuote),null);
            Patch(typeof(PlayerStore),"SellItemFromTable",nameof(ProtectedExecution),nameof(NativeSale));
            Patch(typeof(PlayerStore),"BuyItemFromTable",nameof(NativeBuy),nameof(NativePurchase));
            Patch(typeof(PlayerStore),"CanSellItemFromTable",nameof(OwnTrade),null);
            Patch(typeof(PlayerStore),"CanBuyItemFromTable",nameof(OwnTrade),null);
            Patch(typeof(StorePhoneClient),"GetCallDialog",nameof(PhoneDialog),null);
            Patch(typeof(StorePhoneClient),"UseService",nameof(PhoneUse),null);
            Patch(typeof(StorePhoneClient),"GetLocalizedDisplayName",nameof(PhoneName),null);
            Patch(typeof(StorePhoneClient),"ShownInPhoneBook",nameof(PhoneShown),null);
            Patch(typeof(PhoneUIManager),"WillAnswerCall",nameof(PhoneWillAnswer),null);
            Patch(typeof(StoreClientInstance),"CreateClientInstance",null,nameof(Supply));
            Patch(typeof(DirectoryMaster),"Item",null,nameof(ProtectOriginal));
        }
        private void Patch(Type t,string method,string pre,string post) => core.HarmonyInstance.Patch(ReleaseApi.Method(t,method),pre==null?null:new HarmonyMethod(typeof(NativeMemberBusiness),pre),post==null?null:new HarmonyMethod(typeof(NativeMemberBusiness),post));
        private static NativeMemberBusiness Self=>Core.Instance.Story?.Economy?.Business;
        private static bool BeforeDialogue(StoreClient __instance)=>__instance.identifier?.StartsWith(Prefix,StringComparison.Ordinal)!=true && Self?.DeferCounterDialogue(__instance)!=true;
        private static string Zone(StoreClient c)=>c?.clientFaction==StoreClient.FACTION_UPPER?"upper":c?.clientFaction==StoreClient.FACTION_MIDDLE?"middle":c?.clientFaction==StoreClient.FACTION_LOWER?"lower":null;
        private static bool Excluded(GameItem item,StoreClient client) {
            if(item==null)return false;
            if(Self?.economy.Trade?.HoldsNativeUse(item)==true)return Self.economy.Trade.UsePickup(client,item)==null;
            var workshop=Self?.economy.Workshop;
            if(selfRepairAllowed(item))return false;
            if(workshop?.BlocksSale(item)==true)return true;
            var manufactured=WorkshopCatalog.Product(item.identifier);
            if(manufactured!=null && client!=null && client.identifier?.StartsWith("ms_")!=true && !item.IsTag("ms.retail."+Zone(client)))return true;
            if(item.IsTag(MusicCatalog.ProtectedTag))return true;
            var self=Self;
            var b=self?.host.Engine?.Snapshot().Economy?.Business;
            if(b!=null && b.Instruments.TryGetValue(item.uniqueId,out var g) && g.Owner!="player" && g.Custody=="player")return true;
            var p=MusicCatalog.Find(item.identifier);
            return p!=null && client!=null && client.identifier?.StartsWith("ms_")!=true && !p.RetailZones.Contains(Zone(client));
        }
        private static bool selfRepairAllowed(GameItem item)=>Self?.economy.PackageB?.RepairCounterOutgoing(item)==true;
        private static bool ProtectedCandidate(StoreClient __instance,GameItem __0,ref bool __result) {if(selfRepairAllowed(__0)){__result=true;return false;}if(!Excluded(__0,__instance))return true;__result=false;return false;}
        private static bool ProtectedQuote(GameItem __0,ref bool __result) {if(!Excluded(__0,PlayerStore.instance?.currentClientInstance?.storeClient))return true;__result=false;return false;}
        private static bool ProtectedExecution(out int __state) {
            __state=PlayerStore.instance?.currentNegociatedItem?.uniqueId??0;
            if(Core.Instance.Story?.Opening?.Trading==true){__state=0;return Core.Instance.Story.Opening.Commodity(PlayerStore.instance.currentNegociatedItem);}
            if(Core.Instance.Story?.Diary?.Trading==true){__state=0;return Core.Instance.Story.Diary.CanTrade(PlayerStore.instance.currentNegociatedItem);}
            if(Self?.economy.PackageB.RepairCounterOutgoing(PlayerStore.instance?.currentNegociatedItem)==true){__state=0;return true;}
            // Recheck immediately before native execution, including an item that was tagged after quoting.
            var entry=EmporiumEntry.Instance;
            if(entry==null)return true;
            if(Excluded(PlayerStore.instance?.currentNegociatedItem,PlayerStore.instance?.currentClientInstance?.storeClient)){__state=0;return false;}
            bool allowed=Self?.CurrentActor==null && Core.Instance.Story?.Integration?.IsOwnCurrent!=true;if(!allowed)__state=0;
            if(allowed && Self?.economy.Enabled==true && !Self.economy.IsTransferring && __state!=0){Self.economy.MutateNative(()=>PlayerStore.instance.SellItemFromTable(),()=>{});__state=0;return false;}return allowed;
        }
        private sealed class NativePurchaseState {internal int Entity,Cash;}
        private static bool NativeBuy(out NativePurchaseState __state) {
            __state=null;
            var inspection=Core.Instance.Story?.Inspection;
            if(inspection?.Managed(PlayerStore.instance?.currentNegociatedItem)==true){if(inspection.Trading)return true;inspection.BuyFromTable();return false;}
            if(Core.Instance.Story?.Opening?.Executing==true)return true;
            if(Core.Instance.Story?.Opening?.Trading==true){Core.Instance.Story.Opening.BuyFromTable();return false;}
            if(Self?.economy.PackageB.RepairCounterExecuting==true)return true;
            if(Self?.economy.PackageB.RepairCounterIncoming(PlayerStore.instance?.currentNegociatedItem)==true){Self.economy.PackageB.TryBuyRepairFromCounter();return false;}
            bool allowed=Self?.CurrentActor==null && Core.Instance.Story?.Integration?.IsOwnCurrent!=true;
            var store=PlayerStore.instance;var item=store?.currentNegociatedItem;
            if(!allowed || item==null)return allowed;
            // The buying counter belongs to the inventory tree, but its
            // unpurchased goods still belong to the seller.
            if(GeneralHelper.IsItemOwned(item) && Self?.economy.FindPlayerItem(item.uniqueId)!=null)return false;
            __state=new NativePurchaseState{Entity=item.uniqueId,Cash=store.GetCash()};
            if(Self?.economy.Enabled==true && !Self.economy.IsTransferring){Self.economy.MutateNative(()=>store.BuyItemFromTable(),()=>{});__state=null;return false;}return true;
        }
        private static void NativeSale(int __state)=>Self?.RecordNativeOwnership(__state,false);
        private static void NativePurchase(NativePurchaseState __state){
            if(__state==null || PlayerStore.instance.GetCash()>=__state.Cash)return;
            var item=Self?.economy.FindPlayerItem(__state.Entity);if(item==null || !GeneralHelper.IsItemOwned(item))return;
            Self.RecordNativeOwnership(__state.Entity,true);Self.economy.Workshop.RecordPurchase(__state.Entity);
        }
        internal void RecordNativeOwnership(int entity,bool playerOwns) {
            if(!economy.Enabled || entity==0 || (economy.FindPlayerItem(entity)!=null)!=playerOwns)return;
            economy.PackageB.RecordNativeOwnership(entity,playerOwns);
            var b=host.Engine.Snapshot().Economy.Business;
            if(!b.Instruments.ContainsKey(entity) && !b.Components.ContainsKey(entity)){host.Persist();return;}
            host.Engine.EditMemberBusiness(s=>{
                string owner=playerOwns?"player":"outside";var records=s.Economy.Business;
                void Assign(MusicComponent part){part.Owner=owner;if(!string.IsNullOrEmpty(part.Snapshot)){var actual=NativeMemberEconomy.Decode(part.Snapshot);GeneralHelper.SetItemOwned(actual,playerOwns);part.Snapshot=NativeMemberEconomy.Encode(actual);}}
                if(records.Instruments.TryGetValue(entity,out var g)){g.Owner=owner;g.Custody=owner;foreach(int e in g.Slots.Values)Assign(records.Components[e]);}
                if(records.Components.TryGetValue(entity,out var c))Assign(c);
            });host.Persist();
        }
        private static bool OwnTrade(ref bool __result) {if(Core.Instance.Story?.Opening?.Trading==true||Core.Instance.Story?.Diary?.Trading==true||Core.Instance.Story?.Return?.Trading==true||Self?.CurrentActor==null||Self.economy.PackageB.RepairCounterSupported(PlayerStore.instance?.currentNegociatedItem))return true;__result=false;return false;}
        private static void ProtectOriginal(GameItem __result) {
            if(__result==null)return;
            Core.Instance.Story?.Economy?.PackageB.InitializeRepairItem(__result);
            if(new[]{"ms_nagi_whale_guitar","ms_natsume_spinel_guitar","ms_gear_synth_damaged","ms_gear_synth_serviced","ms_gear_synth_hammer"}.Contains(__result.identifier)) __result.EnableTag(MusicCatalog.ProtectedTag,false);
        }
        private bool OurPhone(StorePhoneClient phone,out string actor) {
            actor=null;var dict=PlayerStore.instance?.PhoneClientDict;if(dict==null)return false;
            foreach(var p in phones) if(dict.TryGetValue(p.Key,out var candidate) && candidate.NativeIdentity()==phone.NativeIdentity()){actor=p.Value;return true;}
            return false;
        }
        private static bool PhoneName(StorePhoneClient __instance,ref string __result) {
            var s=Self;if(s==null || !s.OurPhone(__instance,out string a))return true;
            __result=s.ActorName(a);return false;
        }
        private static bool PhoneShown(StorePhoneClient __instance,ref bool __result) {
            var s=Self;if(s==null || !s.OurPhone(__instance,out string a))return true;
            __result=MusicCatalog.Met(s.host.Package,s.host.Engine.Snapshot(),a);return false;
        }
        private static bool PhoneWillAnswer(long __0,ref bool __result) {var s=Self;if(s==null || !s.phones.TryGetValue(__0,out string actor))return true;__result=s.host.Engine!=null && MusicCatalog.Met(s.host.Package,s.host.Engine.Snapshot(),actor) && PackageC.CanContact(s.host.Engine.Snapshot(),actor,true);return false;}
        private static bool PhoneUse(StorePhoneClient __instance) {
            var s=Self;if(s==null || !s.OurPhone(__instance,out string a))return true;
            try{s.economy.PackageB.NotifyRepair(a);}catch(InvalidOperationException e){s.core.LoggerInstance.Warning("REPAIR_PHONE "+e.Message);}return false;
        }
        private static bool PhoneDialog(StorePhoneClient __instance,ref Dialogue __result) {
            var s=Self;if(s==null || !s.OurPhone(__instance,out string a))return true;
            var state=s.host.Engine.Snapshot();
            if(!MusicCatalog.Met(s.host.Package,state,a)){__result=new Dialogue().SetText("电话","尚未获得联系方式。");return false;}
            if(!PackageC.CanContact(state,a,true)){__result=new Dialogue().SetText("电话","暂时无法取得联系。已有的器材和委托仍继续保管。");return false;}
            s.core.EnsureChineseFallback();string block=s.economy.PackageB.RepairCallBlock(a);if(block!=null){__result=new Dialogue().SetText(s.ActorName(a),block);return false;}
            var action=(System.Action)(Action)(()=>{try{s.economy.PackageB.NotifyRepair(a);}catch(InvalidOperationException e){s.core.LoggerInstance.Warning("REPAIR_PHONE "+e.Message);}}); s.roots.Add(action);
            string text=state.Economy.Business.Appointments.TryGetValue(a,out var ap) && ap.Status=="pending" ? "已约在第 "+ap.Day+" 天来店。" : s.economy.PackageB.RepairCallText(a);
            __result=new Dialogue().SetText(s.ActorName(a),text).SetEndAction(action);return false;
        }
        internal string ActorName(string a)=>host.Package.Actors.First(x=>x.Id==a).DisplayName;
        internal void Register(ModItemDirectory directory) {
            foreach(var product in MusicCatalog.Products) {
                if(directory.Has(product.Id))continue;
                var p=product; var factory=(System.Func<GameItem>)(Func<GameItem>)(()=>Create(p));factories.Add(factory);
                if(!directory.Add(p.Id,factory))throw new InvalidOperationException("音乐商品注册失败 "+p.Id);
            }
        }
        internal GameItem Create(MusicProduct p) {
            core.EnsureResources(); var item=ItemDirectory.CreateEmptyItem(p.Id);
            item.name=p.Name;item.shortDescription="适配："+ActorName(p.Actor)+" / "+(p.Profile switch {"monitor_speaker"=>"监听扬声器","whale_guitar"=>"沧溟蓝鲸","custom_overdrive"=>"过载效果器","practice_drum_pad"=>"练习鼓垫","headphone_amp"=>"便携耳放",_=>"商业电吉他"});
            item.longDescription="音乐器材 · "+p.Width+"×"+p.Height+"格。"+(p.Slot==null?"器材配置随实体保存。":"由玩家安装；购买不会自动改装。")+" 来源："+string.Join("、",p.Zones.Select(z=>z=="upper"?"上层区":"中层区"));
            item.unitCount=1;item.unitValue=p.Price;item.unitBaseValue=p.Price;
            item.SetSpriteAndShape("resource_story","item");
            item.SetShape(new GridShapeBuilder(p.Width,p.Height).SetDataFill(p.Width,p.Height,1).Build());
            item.SetGameItemType(p.NativeType);item.EnableTag(p.MemberTag,false);item.EnableTag(p.InstrumentTag,false);item.EnableTag("ms.music",false);
            foreach(var zone in p.Zones)item.EnableTag("ms.source."+zone,false);
            foreach(var zone in p.RetailZones)item.EnableTag("ms.retail."+zone,false);
            economy.PackageB.ApplyCatalog(item);
            return item;
        }
        internal bool Attach() {
            refreshed=-1;lastDay=-1;HeldStoryActor=null;
            string defaults=Path.Combine(core.Root,"ModDevelopment/ResourceStoryMod/Content/millsage.business.json");
            string path=core.Test==null?Path.Combine(core.DataRoot,"millsage.business.json"):defaults;
            if(!File.Exists(path))File.Copy(defaults,path);
            var config=JsonSerializer.Deserialize<VisitConfig>(File.ReadAllText(path),Core.Json);
            if(config==null || config.Version!=1 || config.NaturalChancePercent<0 || config.NaturalChancePercent>100)throw new InvalidDataException("成员来访概率配置无效");
            bool changed=(core.Test?.ToolsMode!=true||core.Test.Request.NormalPlay) && host.Engine.Snapshot().Economy.Business.NaturalChancePercent!=config.NaturalChancePercent;
            if(changed)host.Engine.EditMemberBusiness(s=>s.Economy.Business.NaturalChancePercent=config.NaturalChancePercent);
            EnsurePhones();
            return changed;
        }
        private sealed class VisitConfig {public int Version {get;set;}=1;public int NaturalChancePercent {get;set;}=35;}
        internal void Detach() {Close();pendingCounterDialogue=null;phones.Clear();roots.Clear();HeldStoryActor=null;refreshed=-1;lastDay=-1;}
        private void EnsurePhones() {
            var store=PlayerStore.instance;if(store?.PhoneClientDict==null || host.Engine==null)return;
            int i=0;
            foreach(var a in new[]{"ms_mahoro","ms_nagi","ms_natsume","ms_houka","ms_hotaru"}) {
                long n=8670100+i++;phones[n]=a;
                if(!store.PhoneClientDict.ContainsKey(n)) store.PhoneClientDict[n]=new StorePhoneClient {displayName=ActorName(a),locID="",dialogFuncId="",phoneState=StorePhoneClient.PhoneState.Regular,cooldownDuration=0,currentCooldown=0,dialedBefore=true};
            }
        }
        internal bool Book(string actor,bool rebook=false) {
            var state=host.Engine.Snapshot();bool changed=false;
            host.Engine.EditMemberBusiness(s=>changed=MemberBusiness.Book(s.Economy.Business,actor,host.Day,MusicCatalog.Met(host.Package,s,actor),rebook,s));
            host.Persist(); message=changed?"已约好第 "+(host.Day+1)+" 天来店。":"已有预约，日期保持不变。";return changed;
        }
        internal void Cancel(string actor) {host.Engine.EditMemberBusiness(s=>MemberBusiness.Cancel(s.Economy.Business,actor));host.Persist();}
        internal void Update() {
            if(!economy.Enabled)return;
            var state=host.ReadState;var store=PlayerStore.instance;
            if(state.Revision!=refreshed || lastDay!=host.Day) {
                bool needs=MusicCatalog.Products.Take(5).Any(p=>MusicCatalog.Unlocked(state,p) && !state.Economy.Business.Needs.ContainsKey(p.Id));
                bool missed=state.Economy.Business.Appointments.Values.Any(a=>a.Status=="pending" && a.Day<host.Day);
                if(needs || missed){host.Engine.EditMemberBusiness(s=>{MemberBusiness.Refresh(s.Economy.Business,s);MemberBusiness.Expire(s.Economy.Business,host.Day);});host.Persist();}
                EnsurePhones();refreshed=host.Engine.Revision;lastDay=host.Day;
            }
            PumpCounterDialogue();
            if(!Scheduling || economy.PackageB.RestoringSupplier)return;
            PruneQueue();
            if(host.Integration.IsOwnCurrent && store.isClientArrived) {
                string sceneId=store.currentClientInstance.storeClient.identifier.Substring(host.Integration.Prefix.Length);
                var scene=host.Package.Scenes.Find(s=>s.Id==sceneId);
                var actor=scene==null?null:host.Integration.LeadActor(scene);
                if(actor!=null) RecordArrival(actor);
            }
            if(HeldStoryActor!=null && !host.Integration.IsOwnCurrent){HeldStoryActor=null;Close();}
            if(CurrentActor!=null && store.isClientArrived){RecordArrival(CurrentActor);return;}
            if(store.storeState!=PlayerStore.StoreState.OPEN || host.Visible || host.Integration.InVenue)return;
            // Story actor goes first, and ordinary queued customers remain untouched.
            if(host.Engine.Frame()?.Scene.Context=="counter" || host.Native012?.DuePhone!=true&&host.ActTwo?.DuePhone!=true&&(host.CounterCandidates().Count>0 || host.Integration.QueuedCount>0))return;
            state=host.ReadState;
            if(!state.Economy.Business.NaturalRolls.ContainsKey(host.Day)) {
                host.Engine.EditMemberBusiness(s=>MemberBusiness.Roll(s.Economy.Business,s.RunId,host.Day,host.Package.Actors.Where(a=>MemberBusiness.ValidActor(a.Id) && MusicCatalog.Met(host.Package,s,a.Id) && PackageC.CanContact(s,a.Id,false)).Select(a=>a.Id)));host.Persist();
            }
            if(store.currentClientInstance!=null || HasQueued())return;
            var latest=host.ReadState;var due=MemberBusiness.Due(latest.Economy.Business,host.Day,latest).FirstOrDefault();
            if(due!=null) Queue(due);
        }
        internal bool HasQueued() {var stack=PlayerStore.instance?.storeClientManager?.clientStack;if(stack==null)return false;foreach(var c in stack)if(c.identifier?.StartsWith(Prefix)==true)return true;return false;}
        private void PruneQueue() {
            var store=PlayerStore.instance;var stack=store.storeClientManager.clientStack;
            var state=host.ReadState;var due=MemberBusiness.Due(state.Economy.Business,host.Day,state).ToHashSet();
            var storyActors=new HashSet<string>();
            string PhysicalActor(StoreClient client) {
                if(!host.Integration.IsOwn(client))return null;
                var scene=host.Package.Scenes.Find(s=>s.Id==client.identifier.Substring(host.Integration.Prefix.Length));
                return host.Integration.LeadActor(scene);
            }
            var current=PhysicalActor(store.currentClientInstance?.storeClient);if(current!=null)storyActors.Add(current);
            foreach(var c in stack){var a=PhysicalActor(c);if(a!=null)storyActors.Add(a);}
            for(int i=stack.Count-1;i>=0;i--){var c=stack[i];if(c.identifier?.StartsWith(Prefix)!=true)continue;string actor=c.identifier.Substring(Prefix.Length);if(core.Test?.ForcedVisitActor==actor)continue;if(!due.Contains(actor)||storyActors.Contains(actor))stack.RemoveAt(i);}
        }
        internal void RecordArrival(string actor) {
            if(PlayerStore.instance?.currentClientInstance!=null&&MemberBusiness.ValidActor(actor))PlayerStore.instance.currentClientInstance.storeClient.clientFaction=ResidenceFaction(actor);
            var b=host.ReadState.Economy.Business;
            if(b.Arrivals.Contains(MemberBusiness.ArrivalKey(host.Day,actor)))return;
            host.Engine.EditMemberBusiness(s=>MemberBusiness.Arrive(s.Economy.Business,actor,host.Day));host.Persist();
            host.Campaign.OfferUseRequest(actor);
        }
        internal void Queue(string actor) {
            var state=host.Engine.Snapshot();if(core.Test?.ForcedVisitActor!=actor && !PackageC.CanContact(state,actor,true) && !PackageC.CanContact(state,actor,false))return;
            if(host.NormalPlay&&core.Test?.ForcedVisitActor!=actor&&!host.Visits.CanQueue("member",actor))return;
            core.EnsureResources();core.EnsureChineseFallback();
            var portrait=host.Package.Actors.Find(a=>a.Id==actor)?.Portrait;
            var c=new StoreClient {identifier=Prefix+actor,displayName=ActorName(actor),spriteName=Prefix+actor,clientIntent=StoreClient.ClientIntent.SELLNBUY,clientFaction=ResidenceFaction(actor),clientCash=(int)Math.Min(int.MaxValue,state.Economy.Wallets[actor].Balance),clientBudget=(int)Math.Min(int.MaxValue,state.Economy.Wallets[actor].Balance),dismissable=true,nonShootable=true,clientImpatience=999,mainDialogue=new Dialogue().SetText(ActorName(actor),""),allDoneDialogue=new Dialogue().SetText(ActorName(actor),"")};
            SpriteDict.Instance.spriteDictionary[c.spriteName]=host.CustomerPortrait(portrait)??core.Portrait;
            c.CompleteClientCreation(false);
            if(host.NormalPlay&&state.Economy.Business.Appointments.TryGetValue(actor,out var appointment)&&appointment.Day==host.Day&&appointment.Status=="pending")host.Visits.QueueClient("member",actor,c);
            else PlayerStore.instance.storeClientManager.AddNextClient(c);
        }
        internal bool AfterStory(string sceneId) {
            if(!Scheduling)return false;
            var scene=host.Package.Scenes.Find(s=>s.Id==sceneId);
            string actor=host.Package.Actors.FirstOrDefault(a=>scene.Participants.Contains(a.Id) && MemberBusiness.ValidActor(a.Id))?.Id;
            if(actor==null)return false;
            HeldStoryActor=actor;RecordArrival(actor);message="剧情交谈结束，可以继续商量器材。";return true;
        }
        internal bool CanTrade(string actor,GameItem item,bool memberBuys,int quote) {
            if(memberBuys&&host.Diary.CanTrade(item))return CurrentActor==actor;
            var p=MusicCatalog.Find(item.identifier);bool cable=item.identifier=="ms_item_balanced_cable" && actor=="ms_hotaru";
            if((p==null && !cable) || (p!=null && (p.Actor!=actor || !item.IsTag(p.MemberTag) || !item.IsTag(p.InstrumentTag))) || quote<=0 || item.IsTag(MusicCatalog.ProtectedTag))return false;
            if(cable)return CurrentActor==actor && (!memberBuys || quote<=400);
            if(item.GetModifiedItemSize()!=p.Width*p.Height)return false;
            var state=host.Engine.Snapshot();
            if(state.Economy.Business.Components.TryGetValue(item.uniqueId,out var part) && part.Location=="installed")return false;
            if(memberBuys) {
                if(state.Economy.Business.Instruments.TryGetValue(item.uniqueId,out var g) && g.Owner!="player")return false;
                // Native modifier is applied exactly once, independently of the wallet balance.
                var client=PlayerStore.instance.currentClientInstance?.storeClient;
                if(client==null || CurrentActor!=actor || !MusicCatalog.Unlocked(state,p))return false;
                int oldBudget=client.clientBudget,oldCash=client.clientCash;bool oldUse=client.useClientBudget;
                var copy=host.Engine.Snapshot();PackageB.Fact(copy.Workshop.Advanced,item.uniqueId,item.identifier,"player",NativePackageB.Grade(item));
                int value=p.IsGear && copy.Economy.Business.Instruments.ContainsKey(item.uniqueId)?PackageB.Value(copy.Workshop.Advanced,copy.Economy.Business,item.uniqueId):PackageB.LeafValue(copy.Workshop.Advanced,item.uniqueId,item.identifier);
                try {client.useClientBudget=true;client.clientBudget=Math.Max(0,checked(p.Willingness+value-p.Price));client.ApplyBudgetModifier();return quote<=client.clientBudget;}
                finally {client.clientBudget=oldBudget;client.clientCash=oldCash;client.useClientBudget=oldUse;}
            }
            return CurrentActor==actor;
        }
        internal void HoldStoryTrade(string actor){HeldStoryActor=actor;RecordArrival(actor);}
        internal void OnTrade(string actor,GameItem item,bool memberBuys) {
            var p=MusicCatalog.Find(item.identifier);
            host.Engine.EditMemberBusiness(s=>{
                var b=s.Economy.Business;
                if(p?.IsGear==true) {var g=MemberBusiness.Register(b,item.uniqueId,item.identifier,memberBuys?"player":actor);g.Owner=memberBuys?actor:"player";g.Custody=g.Owner;
                    foreach(int e in g.Slots.Values)b.Components[e].Owner=g.Owner;}
                if(b.Needs.TryGetValue(item.identifier,out var need))need.FulfilledEntity=memberBuys?item.uniqueId:0;
                if(b.Components.TryGetValue(item.uniqueId,out var component))component.Owner=memberBuys?actor:"player";
            });
            host.Engine.EditPackageB(s=>{
                var a=s.Workshop.Advanced;var f=PackageB.Fact(a,item.uniqueId,item.identifier,memberBuys?actor:"player",NativePackageB.Grade(item));f.Owner=memberBuys?actor:"player";
                if(s.Economy.Business.Instruments.TryGetValue(item.uniqueId,out var g))foreach(int id in g.Slots.Values)if(a.Facts.TryGetValue(id,out var part))part.Owner=g.Owner;
                if(memberBuys && p?.IsGear==true)PackageB.Fulfill(s,actor,item.uniqueId);
            });
        }
        private static void Supply(StoreClientInstance __result) {var s=Self;if(s?.economy.Enabled==true && __result!=null)s.economy.PackageB.QueueSupply(__result);}
        internal int AddSupply(StoreClientInstance instance) {
            return economy.PackageB.QueueSupply(instance);
        }
        internal void Borrow(int entity,string actor) {
            var state=host.Engine.Snapshot();
            if(economy.FindPlayerItem(entity)!=null)throw new InvalidOperationException("原件已在玩家处");
            if(!state.Economy.OwnedItems.TryGetValue(entity,out var owned) || owned.ActorId!=actor || MusicCatalog.Find(owned.ItemId)?.IsGear!=true)throw new InvalidOperationException("成员未持有这件乐器");
            var item=NativeMemberEconomy.Decode(owned.NativeSnapshot);bool placed=false;
            economy.MutateNative(()=>{EmporiumEntry.Instance.TryAddToPlayerInv(item);placed=economy.FindPlayerItem(entity)!=null;if(!placed)throw new InvalidOperationException("玩家没有放置空间");host.Engine.EditMemberBusiness(s=>{var g=MemberBusiness.Register(s.Economy.Business,entity,item.identifier,actor);g.Custody="player";s.Economy.OwnedItems.Remove(entity);});},()=>{if(placed)item.parentInventory.Expel(item);});
        }
        internal bool Install(string jobId,int entity,int componentEntity) {
            var item=economy.FindPlayerItem(entity)??throw new InvalidOperationException("乐器不在玩家处");
            var component=economy.FindPlayerItem(componentEntity);var state=host.Engine.Snapshot();
            var b=state.Economy.Business;
            if(b.Jobs.TryGetValue(jobId,out var job)) {
                if(job.Instrument!=entity || job.Component!=componentEntity)throw new InvalidOperationException("工单号冲突");return false;
            }
            string owner=b.Instruments.TryGetValue(entity,out var registered)?registered.Owner:"player";
            MemberOwnedItem supplied=null;
            if(component==null && state.Economy.OwnedItems.TryGetValue(componentEntity,out supplied) && supplied.ActorId==owner)component=NativeMemberEconomy.Decode(supplied.NativeSnapshot);
            if(component==null)throw new InvalidOperationException("没有这件实际配件");
            if(economy.PackageB.Enabled)economy.PackageB.Ensure(component,supplied==null?"player":owner);
            var p=MusicCatalog.Find(component.identifier)??throw new InvalidOperationException("不是音乐配件");
            if(!component.IsTag(p.MemberTag) || !component.IsTag(p.InstrumentTag))throw new InvalidOperationException("实体配件缺少配对标签");
            var record=new MusicComponent {EntityId=componentEntity,ItemId=component.identifier,Snapshot=NativeMemberEconomy.Encode(component),Owner=supplied==null?"player":owner,Slot=p.Slot};
            MemberBusiness.Register(b,entity,item.identifier,owner).Custody="player";
            if(!MemberBusiness.Install(b,jobId,entity,record))return false;
            var source=component.parentInventory;bool removed=false;var restore=PresentationCheckpoint(item);
            economy.MutateNative(()=>{
                if(source!=null){removed=source.Expel(component);if(!removed)throw new InvalidOperationException("配件无法移出库存");}
                host.Engine.EditMemberBusiness(s=>{s.Economy.Business=b;s.Economy.OwnedItems.Remove(componentEntity);});
                UpdatePresentation(item);
            },()=>{if(removed)source.UncheckedAccept(component);restore();});
            return true;
        }
        internal void Return(int entity,string actor,int fee,string receipt) {
            if(fee<0 || !StoryContent.ValidId(receipt))throw new InvalidOperationException("工钱或交接号无效");
            var state=host.Engine.Snapshot();
            if(state.Economy.Business.Returns.TryGetValue(receipt,out var prior)) {if(prior.Entity!=entity || prior.Actor!=actor || prior.Fee!=fee)throw new InvalidOperationException("交接号已用于其他结算");return;}
            var item=economy.FindPlayerItem(entity)??throw new InvalidOperationException("乐器原件不在玩家处");
            if(!state.Economy.Business.Instruments.TryGetValue(entity,out var gear) || gear.Owner!=actor || gear.Custody!="player" || fee<0)throw new InvalidOperationException("不是本人的受托乐器");
            if(state.Economy.Wallets[actor].Balance<fee)throw new InvalidOperationException("成员余额不足，可降低工钱或下次取件");
            int expected=checked(PlayerStore.instance.GetCash()+fee);string snapshot=NativeMemberEconomy.Encode(item);var source=item.parentInventory;bool removed=false;
            economy.MutateNative(()=>{
                removed=source.Expel(item);if(!removed)throw new InvalidOperationException("原件无法交接");
                PlayerStore.instance.ModCash(fee,false);if(PlayerStore.instance.GetCash()!=expected)throw new InvalidOperationException("工钱结算失败");
                host.Engine.EditMemberBusiness(s=>{s.Economy.Wallets[actor].Balance-=fee;s.Economy.OwnedItems[entity]=new MemberOwnedItem {EntityId=entity,ItemId=item.identifier,ActorId=actor,NativeSnapshot=snapshot};s.Economy.Business.Instruments[entity].Custody=actor;
                    s.Economy.Business.Returns.Add(receipt,new GearReturn {Entity=entity,Actor=actor,Fee=fee});
                    if(fee>0)s.Economy.Trades.Add(receipt,new MemberTradeReceipt {ActorId=actor,EntityId=entity,MemberBuys=true,Price=fee});});
            },()=>{if(removed)source.UncheckedAccept(item);});
        }
        internal void RemovePart(int entity,string slot) {
            var item=economy.FindPlayerItem(entity)??throw new InvalidOperationException("原件不在玩家处");
            var b=host.Engine.Snapshot().Economy.Business;
            if(!b.Instruments.TryGetValue(entity,out var g) || !g.Slots.TryGetValue(slot,out int component))throw new InvalidOperationException("该槽没有可拆配件");
            var part=b.Components[component];GameItem loose=null;bool placed=false;var restore=PresentationCheckpoint(item);
            economy.MutateNative(()=>{
                if(g.Owner=="player") {loose=NativeMemberEconomy.Decode(part.Snapshot);EmporiumEntry.Instance.TryAddToPlayerInv(loose);placed=economy.FindPlayerItem(component)!=null;if(!placed)throw new InvalidOperationException("没有放回配件的位置");}
                host.Engine.EditMemberBusiness(s=>{var gear=s.Economy.Business.Instruments[entity];gear.Slots.Remove(slot);gear.Revision++;MemberBusiness.Recompute(gear);var c=s.Economy.Business.Components[component];c.Location="loose";c.Instrument=0;c.Slot=null;
                    if(gear.Owner!="player")s.Economy.OwnedItems[component]=new MemberOwnedItem {ActorId=gear.Owner,EntityId=component,ItemId=c.ItemId,NativeSnapshot=c.Snapshot};});
                UpdatePresentation(item);
            },()=>{if(placed)loose.parentInventory.Expel(loose);restore();});
        }
        private static string SlotName(string slot)=>slot=="signal"?"拾音槽":slot=="interface"?"接口槽":slot;
        private void UpdatePresentation(GameItem item) {
            if(economy.PackageB.Enabled){economy.PackageB.Ensure(item);economy.PackageB.Present(item);return;}
            var b=host.Engine.Snapshot().Economy.Business;var gear=b.Instruments[item.uniqueId];var product=MusicCatalog.Find(gear.ItemId);
            long value=product.Price;foreach(int entity in gear.Slots.Values)value=checked(value+MusicCatalog.Find(b.Components[entity].ItemId).Price);
            item.SetValue(value);
            item.customText="第 "+gear.Revision+" 次配置 · "+string.Join(" / ",gear.Slots.Keys.Select(SlotName))+" · 器材参考总值 "+value;
        }
        private static Action PresentationCheckpoint(GameItem item) {
            string text=item.customText;long unit=item.unitValue,baseValue=item.unitBaseValue,late=item.lateUnitValue,backup=item.backupUnitValue;
            return ()=>{item.customText=text;item.unitValue=unit;item.unitBaseValue=baseValue;item.lateUnitValue=late;item.backupUnitValue=backup;};
        }
        internal void Open(string target="contacts") {
            var store=PlayerStore.instance;if(panel || host.Visible || store==null || store.lockStorePhysicalFlag!=0 || DialogUIManager.Instance.isConversationActiveFlag!=0)return;
            economy.CloseLedger();tab=target;scroll=Vector2.zero;lockedUI=StoreUIManager.Instance;timeBefore=Time.timeScale;store.lockStorePhysicalFlag++;NativePanelCursor.Acquire(lockedUI);Time.timeScale=0;panel=true;
        }
        internal void Close() {if(!panel)return;panel=false;Time.timeScale=timeBefore;var store=PlayerStore.instance;if(store!=null)store.lockStorePhysicalFlag=Math.Max(0,store.lockStorePhysicalFlag-1);if(lockedUI!=null)NativePanelCursor.Release(lockedUI);lockedUI=null;}
        internal void Draw(GUIStyle bodyStyle,GUIStyle buttonStyle,GUIStyle windowStyle) {
            if(!economy.Enabled || host.Visible)return;
            body=bodyStyle;button=buttonStyle;
            if(!panel){if(NativePanelGUI.Button(new Rect(210,Screen.height-58,220,42),CurrentActor==null?"成员事务 / 改装":"接待 "+ActorName(CurrentActor),button))Open(CurrentActor==null?"contacts":"trade");return;}
            float w=Math.Min(1080,Screen.width-30),h=Math.Min(800,Screen.height-30);
            NativePanelGUI.Window(781343,new Rect((Screen.width-w)/2,(Screen.height-h)/2,w,h),DrawPanel,"成员事务",windowStyle);
        }
        private void Try(Action action){try{action();message="已完成并保存。";}catch(Exception e){message=e.Message;core.LoggerInstance.Warning("MEMBER_ACTION "+e.Message);}}
        private void DrawPanel(int id) {
            var state=host.Engine.Snapshot();float width=Math.Min(1080,Screen.width-30),height=Math.Min(800,Screen.height-30);
            if(NativePanelGUI.Button(new Rect(20,42,180,38),"联系 / 需求",button))tab="contacts";
            if(NativePanelGUI.Button(new Rect(210,42,180,38),"现场交易",button))tab="trade";
            if(NativePanelGUI.Button(new Rect(400,42,180,38),"玩家改装",button))tab="workshop";
            if(NativePanelGUI.Button(new Rect(width-160,42,135,38),"关闭",button))Close();
            GUI.Label(new Rect(20,85,width-40,52),message,body);
            PanelRenderedRows=0;
            bool InPage(ref Rect r) {r.y+=142-scroll.y;return r.y>=142 && r.yMax<=height-110;}
            void Label(Rect r,string text) {if(InPage(ref r)){GUI.Label(r,text,body);PanelRenderedRows++;}}
            bool Btn(Rect r,string text) {if(!InPage(ref r))return false;PanelRenderedRows++;return NativePanelGUI.Button(r,text,button);}
            int y=0;
            if(tab=="contacts")foreach(var profile in economy.Config.Members) {
                string actor=profile.ActorId;if(!MusicCatalog.Met(host.Package,state,actor))continue;
                string status=state.Economy.Business.Appointments.TryGetValue(actor,out var ap)?" · 第"+ap.Day+"天 "+(ap.Status=="pending"?"待到店":ap.Status=="visited"?"已来访":"未接待"):"";
                Label(new Rect(20,y,width-100,36),ActorName(actor)+" · 余额 "+state.Economy.Wallets[actor].Balance+" · 电话 "+phones.First(p=>p.Value==actor).Key+status);y+=40;
                if(Btn(new Rect(20,y,170,36),"电话约明天"))Try(()=>Book(actor));
                if(Btn(new Rect(205,y,170,36),"明确改约明天"))Try(()=>Book(actor,true));
                if(Btn(new Rect(390,y,135,36),"取消预约"))Try(()=>Cancel(actor));y+=43;
                foreach(var n in state.Economy.Business.Needs.Values.Where(n=>n.Actor==actor)){var p=MusicCatalog.Find(n.ItemId);Label(new Rect(20,y,width-100,34),p.Name+" · 参考 "+p.Price+(n.FulfilledEntity!=0?" · 已购":" · 可商议售价"));y+=38;}
                y+=16;
            }
            else if(tab=="trade") {
                string actor=CurrentActor;
                if(actor==null || !PlayerStore.instance.isClientArrived)Label(new Rect(20,y,width-90,55),"成员到柜台后可报价。电话不锁定物品或余额。");
                else {
                    Label(new Rect(20,y,width-90,36),ActorName(actor)+" · 可用余额 "+state.Economy.Wallets[actor].Balance);y+=42;
                    Label(new Rect(20,y,180,36),"最终成交价 / 工钱");var priceRect=new Rect(210,y,150,36);if(InPage(ref priceRect))price=GUI.TextField(priceRect,price,12);y+=44;
                    foreach(var item in economy.AllPlayerItems().Where(i=>MusicCatalog.Find(i.identifier)?.Actor==actor).ToArray()) {
                        var p=MusicCatalog.Find(item.identifier);int entity=item.uniqueId;
                        Label(new Rect(20,y,width-320,38),p.Name+" #"+entity+" · 参考"+p.Price);
                        bool entrusted=state.Economy.Business.Instruments.TryGetValue(entity,out var g) && g.Owner==actor;
                        if(Btn(new Rect(width-310,y,220,36),entrusted?"按工钱归还原件":"按报价卖给成员"))Try(()=>{
                            int q=int.Parse(price);if(entrusted)Return(entity,actor,q,"return_"+Guid.NewGuid().ToString("N"));else economy.TradeNative("sale_"+Guid.NewGuid().ToString("N"),actor,entity,true,q);});y+=44;
                    }
                    foreach(var owned in state.Economy.OwnedItems.Values.Where(o=>o.ActorId==actor).ToArray()) {
                        int entity=owned.EntityId;var p=MusicCatalog.Find(owned.ItemId);if(p==null)continue;
                        Label(new Rect(20,y,width-420,36),"成员持有："+p.Name+" #"+entity);
                        if(p.IsGear && Btn(new Rect(width-410,y,150,36),"交给玩家改装"))Try(()=>Borrow(entity,actor));
                        if(Btn(new Rect(width-250,y,160,36),"玩家按报价收购"))Try(()=>economy.TradeNative("buy_"+Guid.NewGuid().ToString("N"),actor,entity,false,int.Parse(price)));y+=44;
                    }
                    if(Btn(new Rect(20,y+15,240,40),"本次先这样 / 下次再买")){Close();HeldStoryActor=null;PlayerStore.instance.DismissCurrentClient(false,false,true);}
                }
            }
            else {
                foreach(var item in economy.AllPlayerItems().Where(i=>i.identifier=="ms_gear_commercial_guitar").ToArray()) {
                    if(economy.PackageB.Enabled){GUI.Label(new Rect(20,y,width-60,80),"安装、拆换与服务交收已移至器材维护。旧配置继续保留。",body);y+=90;if(NativePanelGUI.Button(new Rect(20,y,width-80,48),"前往器材维护 / 服务保管",button)){Close();economy.PackageB.Open();}break;}
                    int e=item.uniqueId;state.Economy.Business.Instruments.TryGetValue(e,out var g);
                    if(Btn(new Rect(20,y,width-95,40),"选择 "+item.name+" #"+e+" · "+(g==null?"尚未登记":string.Join(" / ",g.Slots.Keys.Select(SlotName))+" · 配置"+g.Revision)))selectedGear=e;y+=48;
                }
                if(!economy.PackageB.Enabled && selectedGear!=0 && economy.FindPlayerItem(selectedGear)!=null) {
                    var parts=economy.AllPlayerItems().Where(i=>MusicCatalog.Find(i.identifier)?.Slot!=null).Select(i=>(i.uniqueId,i.identifier)).ToList();
                    if(state.Economy.Business.Instruments.TryGetValue(selectedGear,out var g) && g.Owner!="player")parts.AddRange(state.Economy.OwnedItems.Values.Where(o=>o.ActorId==g.Owner && MusicCatalog.Find(o.ItemId)?.Slot!=null).Select(o=>(o.EntityId,o.ItemId)));
                    foreach(var part in parts){int e=part.Item1;var p=MusicCatalog.Find(part.Item2);if(Btn(new Rect(20,y,width-95,40),"安装 "+p.Name+" #"+e+" → "+SlotName(p.Slot)))Try(()=>Install("install_"+Guid.NewGuid().ToString("N"),selectedGear,e));y+=48;}
                    if(state.Economy.Business.Instruments.TryGetValue(selectedGear,out var saved))foreach(var slot in saved.Slots.Keys.ToArray()){string s=slot;if(Btn(new Rect(20,y,width-95,40),"拆下 "+SlotName(slot)+"，原件归原物主"))Try(()=>RemovePart(selectedGear,s));y+=48;}
                }
            }
            if(NativePanelGUI.Button(new Rect(20,height-100,110,32),"上一页",button))scroll.y=Math.Max(0,scroll.y-(height-260));
            if(NativePanelGUI.Button(new Rect(140,height-100,110,32),"下一页",button))scroll.y=Math.Min(Math.Max(0,y-(height-260)),scroll.y+(height-260));
            GUI.Label(new Rect(20,height-60,width-40,45),"只有玩家执行改装。普通物品可自由移动；联系、交易和配置跟随当前存档。",body);
        }
    }
}
