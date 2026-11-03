using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    // Ordinary customers, native trades/notes/slots and tomorrow's phone pickup.
    internal sealed class NativeBasicInspection
    {
        internal const long Number=8670201;
        internal const string Prefix="millsage_inspection_",Tag="ms.inspection.entrusted";
        private readonly Core core;private readonly NativeStoryHost host;private readonly List<System.Action> roots=new();private readonly HashSet<string> queueAttempts=new();private float next;private string restoredRun;
        internal bool Trading {get;private set;}
        internal int RestoredEntity {get;private set;}
        private NativeMemberEconomy E=>host.Economy;private PlayerStore Store=>PlayerStore.instance;
        internal BasicInspectionState State=>host.ReadState?.Inspections;
        internal BasicInspectionOrder Active=>State?.Orders.FirstOrDefault(o=>o.Status is "offered" or "held" or "ready");
        internal bool Enabled=>host.NormalPlay&&host.Engine!=null;
        private bool Idle=>host.Engine.Frame()==null||host.ActOne?.WaitingForDays==true;
        internal bool Unlocked=>Enabled&&host.ReadState.CompletedDays.ContainsKey("ms_scene_004")&&E.AllPlayerItems().Any(x=>x.identifier==NormalPlayPolicy.BasicInspector&&E.PackageB.PlayerOwned(x));
        private static NativeBasicInspection Self=>Core.Instance?.Story?.Inspection;
        internal NativeBasicInspection(Core c,NativeStoryHost h)
        {
            core=c;host=h;
            Patch(typeof(StoreClientListDict),"CreateStoreClient",nameof(Factory));
            Patch(typeof(StoreClient),"IsClientBuyingThisItem",nameof(Buying));Patch(typeof(PlayerStore),"CanSellThisItem",nameof(CanSell));
            Patch(typeof(PlayerStore),"SellItem",nameof(Sell));
            Patch(typeof(PlayerStore),"SellItemFromTable",nameof(SellFromTable));
            Patch(typeof(NegociationUIManager),"OnOfferAcceptClick",nameof(AcceptQuote));
            Patch(typeof(PlayerStore),"DismissCurrentClient",nameof(BeforeDismiss),nameof(Dismissed));
            Patch(typeof(StorePhoneClient),"GetCallDialog",nameof(Call));Patch(typeof(StorePhoneClient),"UseService",nameof(Use));
            Patch(typeof(StorePhoneClient),"GetLocalizedDisplayName",nameof(Display));Patch(typeof(StorePhoneClient),"ShownInPhoneBook",nameof(Shown));Patch(typeof(PhoneUIManager),"WillAnswerCall",nameof(Answer));
        }
        private void Patch(Type t,string m,string prefix,string postfix=null)=>core.HarmonyInstance.Patch(ReleaseApi.Method(t,m),prefix:prefix==null?null:new HarmonyMethod(typeof(NativeBasicInspection),prefix),postfix:postfix==null?null:new HarmonyMethod(typeof(NativeBasicInspection),postfix));
        internal BasicInspectionOrder Order(GameItem item)=>item==null?null:State?.Orders.FirstOrDefault(o=>o.Entity==item.uniqueId);
        internal bool Managed(GameItem item)=>Order(item)!=null;
        internal bool HeldEntity(int entity)=>State?.Orders.Any(o=>o.Entity==entity&&BasicInspection.Held(o))==true;
        private static string Id(BasicInspectionOrder o,bool pickup)=>Prefix+o.Id+(pickup?"_pickup":"_offer");
        internal string PickupClientId=>Active==null?null:Id(Active,true);
        internal void QueuePickup()
        {
            var o=Active;if(o?.Status!="ready"||o.PickupDay!=host.Day)return;
            host.Engine.EditInspections(s=>s.Inspections.Orders.Single(x=>x.Id==o.Id).VisitDay=host.Day);
            host.Visits.QueueClient("inspection",o.Id.ToString(),Create(Id(o,true)));
        }
        private bool Current(BasicInspectionOrder o,bool pickup)=>Store.currentClientInstance?.storeClient?.identifier==Id(o,pickup)&&Store.isClientArrived;
        internal bool Incoming(GameItem item)=>Enabled&&Order(item) is BasicInspectionOrder o&&o.Status=="offered"&&Current(o,false)&&!GeneralHelper.IsItemOwned(item);
        internal string Signature(GameItem item)=>item==null?null:PackageB.Hash(item.uniqueId+"/"+item.identifier+"/"+NativePackageB.Grade(item)+"/"+string.Join("/",NativePackageB.ItemList(item.FindAllChildItems()).OrderBy(x=>x.uniqueId).Select(x=>x.uniqueId+":"+x.identifier+":"+NativePackageB.Grade(x))));
        internal bool Counter(GameItem item)=>Enabled&&Order(item) is BasicInspectionOrder o&&Current(o,true)&&o.PickupDay==host.Day&&BasicInspection.Ready(o,Signature(item))&&GeneralHelper.IsItemOwned(item);
        internal bool Value(GameItem item,out int value){value=0;if(Incoming(item)){value=BasicInspection.IntakeReference;return true;}if(Counter(item)){value=checked(Order(item).IntakePaid+BasicInspection.Fee);return true;}return false;}
        internal bool? Accept(GameItem machine,string role,GameInventory inventory,GameItem item)
        {
            if(!Enabled)return null;if(Managed(machine))return false;if(!Managed(item))return null;
            return BasicInspection.Held(Order(item))&&role=="host"&&NormalPlayPolicy.IsInspector(machine.identifier)&&E.PackageB.PlayerOwned(machine)&&inventory.childItems.Count==0&&GeneralHelper.IsItemOwned(item);
        }
        internal bool Pump(GameItem machine,GameItem item,bool detector)
        {
            if(!Managed(item))return false;if(!detector)return true;
            if(!BasicInspection.Held(Order(item))||!E.PackageB.PlayerOwned(machine)||!NormalPlayPolicy.IsInspector(machine.identifier))return true;
            string sig=Signature(item);if(BasicInspection.Ready(Order(item),sig))return true;
            E.MutateNative(()=>host.Engine.EditInspections(s=>{var o=s.Inspections.Orders.Single(x=>x.Entity==item.uniqueId);BasicInspection.Detect(o,sig);o.Snapshot=NativeMemberEconomy.Encode(item);}),()=>{});return true;
        }
        internal StoreClient Adapt(StoreClient original,string ticket)
        {
            if(!Enabled||!Idle||original.clientIntent!=StoreClient.ClientIntent.BUY)return null;
            var pending=Active;
            if(pending!=null){
                if(pending.Status=="offered"&&pending.VisitDay<0){host.Engine.EditInspections(s=>s.Inspections.Orders.Single(x=>x.Id==pending.Id).VisitDay=host.Day);return Create(Id(pending,false));}
                return null;
            }
            var state=State??new BasicInspectionState();if(!Unlocked||!BasicInspection.CanOffer(state,host.Day))return null;
            if(core.Presentation.Active||host.ReadState.NormalVisits?.SlotClaims.ContainsKey(ticket)==true)return null;
            bool offered=false;var config=host.Normal.Config;
            host.Engine.EditInspections(s=>offered=BasicInspection.DecideOffer(s.Inspections,host.Day,ticket,PackageBSupply.Roll(Store.runID+"/"+ticket+"/basic-inspection",100),config.InspectionOfferChance,config.InspectionGuaranteeOn));
            if(!offered)return null;
            int id=state.Orders.Count+1;var item=DirectoryMaster.Item("ms_gear_custom_overdrive",false);GeneralHelper.SetItemOwned(item,false);item.EnableTag(Tag,false);
            E.PackageB.ApplyFacts(item,"ordinary","outside","basic_inspection:"+id);
            var o=new BasicInspectionOrder{Id=id,Day=host.Day,Entity=item.uniqueId,Sku=item.identifier,Snapshot=NativeMemberEconomy.Encode(item),Sprite=original.spriteName,Zone=NativePackageD.Zone(original)=="upper"?"upper":"middle",Ticket=ticket,VisitDay=host.Day};
            host.Engine.EditInspections(s=>s.Inspections.Orders.Add(o));return Create(Id(o,false));
        }
        private StoreClient Create(string identifier)
        {
            var o=State?.Orders.FirstOrDefault(x=>identifier==Id(x,false)||identifier==Id(x,true));bool pickup=identifier.EndsWith("_pickup",StringComparison.Ordinal);
            var c=new StoreClient{identifier=identifier,displayName="器材送检客户",spriteName=o?.Sprite??StoreClientList.CreateLowerLevelRareMerchant().spriteName,clientFaction=o?.Zone=="upper"?StoreClient.FACTION_UPPER:StoreClient.FACTION_MIDDLE,clientIntent=pickup?StoreClient.ClientIntent.BUY:StoreClient.ClientIntent.SELL,clientCash=o?.Cash??200,dismissable=true,nonShootable=true,clientImpatience=999,mainDialogue=new Dialogue().SetText("器材送检客户",pickup?"来取之前送检的那一件器材，按柜台报价结算。":"只查接点、通断和当前配件兼容，保留原样。按低价收购，检测后电话通知我，次日来取。"),allDoneDialogue=new Dialogue().SetText("器材送检客户","谢谢，回头见。")};
            if(o!=null&&pickup)c.clientBuyingIdList.Add(o.Sku);
            c.CompleteClientCreation(false);c.useClientBudget=true;c.clientBudget=o?.Budget??200;
            if(o!=null&&!o.BudgetInitialized){c.SetBudget(200);int actual=c.GetBudget();c.clientCash=actual;host.Engine.EditInspections(s=>{var n=s.Inspections.Orders.Single(x=>x.Id==o.Id);n.Budget=n.Cash=actual;n.BudgetInitialized=true;});}
            return c;
        }
        private static bool Factory(string __0,ref StoreClient __result){if(__0?.StartsWith(Prefix,StringComparison.Ordinal)!=true||Self==null)return true;__result=Self.Create(__0);return false;}
        private static bool Buying(StoreClient __instance,GameItem __0,ref bool __result)
        {
            var s=Self;if(s?.Enabled!=true)return true;var o=s.Order(__0);
            if(__instance.identifier?.StartsWith(Prefix,StringComparison.Ordinal)==true){
                // Native customer preparation checks interest before arrival and
                // before an item is placed on the counter. Do not mark a valid
                // pickup as already complete merely because it is still queued.
                __result=o!=null&&__instance.identifier==Id(o,true)&&o.PickupDay==s.host.Day&&BasicInspection.Ready(o,s.Signature(__0));return false;
            }
            if(o==null)return true;__result=false;return false;
        }
        private static bool CanSell(GameItem __0,bool __1,ref bool __result){var s=Self;if(s?.Enabled!=true||!s.Managed(__0))return true;var c=s.Store.currentClientInstance?.storeClient;__result=s.Counter(__0)&&(__1||__0.GetNegociatedValue()<=Math.Min(c.GetBudget(),c.clientCash));return false;}
        internal void BuyFromTable()
        {
            var item=Store.currentNegociatedItem;var o=Order(item);if(!Incoming(item))return;int cash=Store.GetCash();
            E.MutateNative(()=>{Trading=true;try{Store.BuyItemFromTable();}finally{Trading=false;}var actual=E.FindPlayerItem(o.Entity);int paid=cash-Store.GetCash();if(paid<0||actual==null||!GeneralHelper.IsItemOwned(actual))throw new InvalidOperationException("原生收购未完成");host.Engine.EditInspections(s=>{var n=s.Inspections.Orders.Single(x=>x.Id==o.Id);BasicInspection.Buy(n,paid);n.Snapshot=NativeMemberEconomy.Encode(actual);});},()=>{});
        }
        private static bool Sell(GameItem __0)
        {
            var s=Self;if(s?.Enabled!=true||!s.Managed(__0)||s.Trading)return true;if(!s.Counter(__0))return false;
            var o=s.Order(__0);string sig=s.Signature(__0);int before=s.Store.GetCash();
            s.E.MutateNative(()=>{s.Trading=true;try{s.Store.SellItem(__0);}finally{s.Trading=false;}int paid=s.Store.GetCash()-before;if(s.E.FindPlayerItem(o.Entity)!=null&&GeneralHelper.IsItemOwned(s.E.FindPlayerItem(o.Entity)))throw new InvalidOperationException("同一原件尚未交回");s.host.Engine.EditInspections(st=>{var n=st.Inspections.Orders.Single(x=>x.Id==o.Id);BasicInspection.Return(n,s.host.Day,sig,paid);n.Snapshot=NativeMemberEconomy.Encode(__0);});},()=>{});return false;
        }
        private static bool AcceptQuote(NegociationUIManager __instance)
        {
            var s=Self;var item=s?.Store.currentNegociatedItem;if(s?.Enabled!=true||!s.Counter(item)||s.Trading)return true;
            if(item.parentInventory?.NativeIdentity()!=EmporiumEntry.Instance.backInvinvElementCounter.NativeIdentity())return true;
            if(NegociationUIManager.IsTradeLocked()||s.Store.currentClientInstance.storeClient.isBusinessComplete)return false;
            // Vanilla's accept handler checks the small back grid before it
            // dispatches BUY. The existing sell routine handles the quote and
            // native budget confirmation; only bridge that skipped dispatch.
            s.Store.SellItemFromTable();__instance.RefreshUI();__instance.RefreshNow();return false;
        }
        private static bool SellFromTable()
        {
            var s=Self;var item=s?.Store.currentNegociatedItem;if(s?.Enabled!=true||!s.Counter(item)||s.Trading||item.parentInventory?.NativeIdentity()!=EmporiumEntry.Instance.backInvinvElementCounter.NativeIdentity())return true;
            if(NegociationUIManager.IsTradeLocked())return false;var c=s.Store.currentClientInstance.storeClient;var ui=NegociationUIManager.Instance;
            // Match native SellItemFromTable after its small-grid presence test.
            if(c.CanClientExposeAnyFeatureDeep(item)){c.InspectItemDeep(item);ui.UpdateUI(item);return false;}
            if(!s.Store.CanSellThisItem(item,false)){if(s.Store.CanSellThisItem(item,true))ui.OpenConfirmPanel();return false;}
            s.Store.SellItem(item);c.OnDealAccepted(null);return false;
        }
        private static void BeforeDismiss(out int __state){__state=0;var s=Self;var o=s?.Active;if(s?.Enabled==true&&!s.Trading&&o!=null){if(s.Current(o,false))__state=o.Id;else if(s.Current(o,true))__state=-o.Id;}}
        private static void Dismissed(int __state){var s=Self;if(__state==0||s?.Enabled!=true||s.Trading)return;var o=s.State.Orders.Single(x=>x.Id==Math.Abs(__state));if(o.Status=="offered")s.host.Engine.EditInspections(st=>st.Inspections.Orders.Single(x=>x.Id==o.Id).Status="declined");else if(__state<0&&o.Status=="ready")s.host.Engine.EditInspections(st=>st.Inspections.Orders.Single(x=>x.Id==o.Id).PickupDay=-1);}
        private bool IsPhone(StorePhoneClient p)=>Store?.PhoneClientDict?.TryGetValue(Number,out var phone)==true&&phone.NativeIdentity()==p?.NativeIdentity();
        private static bool Display(StorePhoneClient __instance,ref string __result){if(Self?.IsPhone(__instance)!=true)return true;__result="器材检测取回";return false;}
        private static bool Shown(StorePhoneClient __instance,ref bool __result){if(Self?.IsPhone(__instance)!=true)return true;__result=Self.Unlocked||Self.Active!=null;return false;}
        private static bool Answer(long __0,ref bool __result){if(__0!=Number||Self?.Enabled!=true)return true;__result=Self.Active!=null;return false;}
        private static bool Use(StorePhoneClient __instance){if(Self?.IsPhone(__instance)!=true)return true;Self.Book();return false;}
        private static bool Call(StorePhoneClient __instance,ref Dialogue __result)
        {
            var s=Self;if(s?.IsPhone(__instance)!=true)return true;s.core.EnsureChineseFallback();var o=s.Active;var item=o==null?null:s.E.FindPlayerItem(o.Entity);
            string text=o==null?"目前没有待取的基础检测委托。":!BasicInspection.Ready(o,s.Signature(item))?"请用基础检测仪检查收购的同一器材，确认当前配置后再联系。":o.PickupDay>=s.host.Day?"已约好第 "+o.PickupDay+" 天来取；错过后可以再约。":"检测记录已收到，明天来取；按柜台报价结算。";
            var a=(System.Action)(Action)(()=>s.Book());s.roots.Add(a);__result=new Dialogue().SetText("器材送检客户",text).SetEndAction(a);return false;
        }
        internal void Book(){var o=Active;if(o==null||!BasicInspection.Ready(o,Signature(E.FindPlayerItem(o.Entity))))return;host.Engine.EditInspections(s=>{var n=s.Inspections.Orders.Single(x=>x.Id==o.Id);if(BasicInspection.Book(n,host.Day,Signature(E.FindPlayerItem(o.Entity))))NormalVisits.Book(s,"inspection",n.Id.ToString(),n.PickupDay);});host.Persist();}
        internal string Paper(int entity){var o=State?.Orders.FirstOrDefault(x=>x.Note==entity);return o==null?null:"麻里奈：\n\n只查这件器材的接点、通断与当前配件兼容，保留原有配件，不维修或提高品质。\n\n收购参考15；检测工钱参考120，按实际收购价加工钱到柜台结算，最终可照常讲价。用480基础检测仪对原件使用即可即时记录，不耗电。完成后拨8670201约我次日来取，未完成可继续保管，没有截止日。\n\n"+StatusText(o,E.FindPlayerItem(o.Entity))+"\n\n器材送检客户";}
        internal string Text(GameItem item)=>StatusText(Order(item),item);
        private string StatusText(BasicInspectionOrder o,GameItem item)
        {
            if(o==null)return null;
            string text=o.Status=="done"?"已交回，实际收购 "+o.IntakePaid+" / 交回 "+o.ReturnedPaid:o.Status=="offered"?"先按原生柜台低价收购。":o.Status=="declined"?"本次未承接。":o.Status=="ready"&&!BasicInspection.Ready(o,Signature(item))?"配件已变化，请重新检测；同一原件继续保管。":o.Status=="ready"?o.PickupDay>=host.Day?"已约第 "+o.PickupDay+" 天来取；按原生柜台报价结算。":"当前配置已检测。拨8670201，约物主次日取回。":"待检测；用自有基础检测仪对原件使用，保持原配件。";
            return "基础检测委托 "+o.Id+"\n"+text;
        }
        internal void BeforeSave()
        {
            if(!Enabled||State==null)return;
            foreach(var o in State.Orders.Where(BasicInspection.Held).ToArray()){
                var item=E.FindPlayerItem(o.Entity);if(item==null)throw new InvalidOperationException("送检原件离开可定位库存，保留旧档 #"+o.Entity);
                string snapshot=NativeMemberEconomy.Encode(item);if(snapshot!=o.Snapshot)host.Engine.EditInspections(s=>s.Inspections.Orders.Single(x=>x.Id==o.Id).Snapshot=snapshot);
            }
        }
        internal void Update()
        {
            if(!Enabled||E.IsTransferring||Time.unscaledTime<next)return;next=Time.unscaledTime+.4f;
            if(!core.Presentation.Active&&Active is BasicInspectionOrder missed&&missed.Status=="ready"&&missed.PickupDay>=0&&missed.PickupDay<host.Day)host.Engine.EditInspections(s=>BasicInspection.ExpireAppointment(s.Inspections.Orders.Single(x=>x.Id==missed.Id),host.Day));
            if(!Store.PhoneClientDict.ContainsKey(Number))Store.PhoneClientDict[Number]=new StorePhoneClient{displayName="器材检测取回",locID="",dialogFuncId="",phoneState=StorePhoneClient.PhoneState.Regular,dialedBefore=true};
            if(restoredRun!=Store.runID&&Idle){var old=Active;
                // The native temporary counter is omitted from its inventory
                // save. Recover only an already held, absent original from its
                // authoritative snapshot; never reconstruct a completed return.
                if(old!=null&&BasicInspection.Held(old)&&E.FindPlayerItem(old.Entity)==null){
                    var item=NativeMemberEconomy.Decode(old.Snapshot);
                    if(item.uniqueId!=old.Entity||item.identifier!=old.Sku||old.Status=="ready"&&Signature(item)!=old.DetectedSignature)throw new InvalidOperationException("检测原件快照身份或配置不符，保留旧档");
                    GeneralHelper.SetItemOwned(item,true);bool placed=false;
                    // Loading recovery must precede journal preparation, which
                    // saves and requires every held original to be locatable.
                    foreach(var inv in E.PlayerInventories().Where(i=>i.NativeIdentity()!=EmporiumEntry.Instance.backInvinvElementCounter.NativeIdentity()))if(NativePackageB.Place(inv,item)){placed=true;break;}
                    if(!placed)return;RestoredEntity=old.Entity;
                }
                restoredRun=Store.runID;if(old!=null&&old.VisitDay==host.Day&&old.Status=="offered"){
                string id=Id(old,old.Status=="ready");bool exists=Store.currentClientInstance?.storeClient?.identifier==id;foreach(var c in Store.storeClientManager.clientStack)exists|=c.identifier==id;
                // Restore the already allocated visit; do not consume/add a
                // second ordinary slot when the Demo omits its current client.
                if(!exists)Store.storeClientManager.AddNextClient(Create(id));
            }}
            if(Idle&&Active is BasicInspectionOrder saved&&(saved.Status=="offered"||saved.Status=="ready"&&saved.PickupDay>=0&&saved.PickupDay<=host.Day)){
                string wanted=Id(saved,saved.Status=="ready");bool present=Store.currentClientInstance?.storeClient?.identifier==wanted;foreach(var c in Store.storeClientManager.clientStack)present|=c.identifier==wanted;
                if(!present&&saved.VisitDay==host.Day)host.Engine.EditInspections(s=>s.Inspections.Orders.Single(x=>x.Id==saved.Id).VisitDay=-1);
            }
            // Substitute an existing ordinary slot even if today's queue was
            // generated before the detector was purchased. Counts stay native.
            if(Idle&&!core.Presentation.Active&&(Unlocked&&BasicInspection.CanOffer(State??new(),host.Day)||Active?.Status=="offered"&&Active.VisitDay<0&&Active.Day==host.Day)){
                var queue=Store.storeClientManager.clientStack;
                for(int i=0;i<queue.Count;i++){var original=queue[i];if(original.clientIntent!=StoreClient.ClientIntent.BUY||!queueAttempts.Add(Store.runID+"/"+host.Day+"/"+original.NativeIdentity()+"/"+(Active?.Status??"offer")))continue;int at=i;bool replaced=false;
                    E.MutateNative(()=>{var c=E.Trade.InspectQueued(original);if(c!=null){queue[at]=c;replaced=true;}},()=>{});if(replaced)break;
                }
            }
            var o=Active;if(o==null||!Idle||core.Presentation.Active)return;
            if(o.Status=="offered"&&o.Day<host.Day&&!Current(o,false)){host.Engine.EditInspections(s=>s.Inspections.Orders.Single(x=>x.Id==o.Id).Status="declined");return;}
            if(o.Status=="offered"&&Current(o,false)){
                var existing=E.FindPlayerItem(o.Entity)??NativePackageB.ItemList(Store.currentClientInstance.gridInv.childItems).FirstOrDefault(x=>x.uniqueId==o.Entity);
                if(existing==null){var item=NativeMemberEconomy.Decode(o.Snapshot);GeneralHelper.SetItemOwned(item,false);if(!NativePackageB.Place(Store.currentClientInstance.gridInv,item))return;existing=item;}
                if(EmporiumEntry.Instance.frontInvinvElement.childItems.Count==0&&existing.parentInventory?.NativeIdentity()==Store.currentClientInstance.gridInv.NativeIdentity()){existing.parentInventory.Expel(existing);NativePackageB.Place(EmporiumEntry.Instance.frontInvinvElement,existing);EmporiumEntry.Instance.ShowFrontArea();}
            }
            if(BasicInspection.Held(o)&&o.Note==0){E.MutateNative(()=>{var note=DirectoryMaster.Item(NativePackageB.RepairNoteSku,true);if(!E.PlayerInventories().Any(inv=>NativePackageB.Place(inv,note)))throw new InvalidOperationException("请腾出2×2空位接收检测便条");host.Engine.EditInspections(s=>s.Inspections.Orders.Single(x=>x.Id==o.Id).Note=note.uniqueId);},()=>{});}
        }
    }
}
