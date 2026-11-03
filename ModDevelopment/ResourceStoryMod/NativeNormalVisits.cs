using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    // Coordinates only mod appointments; native events and current dialogue
    // retain ownership. No player-facing window or input simulation is used.
    internal sealed class NativeNormalVisits
    {
        private readonly Core core;private readonly NativeStoryHost host;private string run;
        private PlayerStore Store=>PlayerStore.instance;
        private NormalVisitsState State=>host.ReadState?.NormalVisits;
        internal bool Enabled=>host.NormalPlay&&host.Engine!=null;
        private static NativeNormalVisits Self=>Core.Instance?.Story?.Visits;
        internal NativeNormalVisits(Core c,NativeStoryHost h)
        {
            core=c;host=h;
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(PlayerStore),"BeginDay"),postfix:new HarmonyMethod(typeof(NativeNormalVisits),nameof(BeganDay)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(StoreShutterButton),"OnClick"),prefix:new HarmonyMethod(typeof(NativeNormalVisits),nameof(BeforeDoor)),postfix:new HarmonyMethod(typeof(NativeNormalVisits),nameof(AfterDoor)));
        }
        private static void BeganDay(){if(Self?.Enabled==true)Self.GenerationComplete("native_day_begin");}
        private static void BeforeDoor(out bool __state)=>__state=Self?.Enabled==true&&Self.Store.storeState==PlayerStore.StoreState.MORNING;
        private static void AfterDoor(bool __state){if(__state&&Self?.Store.storeState==PlayerStore.StoreState.OPEN)Self.GenerationComplete("native_door_open");}
        internal void GenerationComplete(string reason)
        {
            if(!Enabled||host.Day<0||State?.Generation.ContainsKey(host.Day)==true)return;
            host.Engine.EditNormalVisits(s=>NormalVisits.GenerationComplete(s.NormalVisits,host.Day,reason));
        }
        private bool ProbeReady=>core.Test==null||!core.Test.Request.InspectionStage.StartsWith("p18",StringComparison.Ordinal)||core.Test.Tools?.P18QueueReady==true;
        private bool Quiet=>Enabled&&ProbeReady&&!host.Economy.IsTransferring&&!core.Presentation.Active&&!host.Visible&&!host.ReportBlocked&&!host.Integration.InVenue&&PhoneUIManager.Instance?.isCalling!=true&&DialogUIManager.Instance.isConversationActiveFlag==0&&DialogUIManager.Instance.currentDialog==null;
        private bool Valid(NormalVisitReceipt r)
        {
            if(r.Day!=host.Day)return false;
            if(r.Kind=="inspection")return host.Inspection.Active is BasicInspectionOrder o&&o.Id.ToString()==r.Owner&&o.Status=="ready"&&o.PickupDay==r.Day;
            if(r.Kind=="supplier")return host.Supplier.State?.DueDay==r.Day;
            return host.ReadState.Economy.Business.Appointments.TryGetValue(r.Owner,out var a)&&a.Day==r.Day&&a.Status=="pending"&&PackageC.CanContact(host.ReadState,r.Owner,true);
        }
        private NormalVisitReceipt Next()=>NormalVisits.Due(State,host.Day).FirstOrDefault(Valid);
        internal bool HasDue=>Enabled&&Next()!=null;
        internal bool HasExplicitMemberDue=>Enabled&&host.ReadState.Economy.Business.Appointments.Any(p=>p.Value.Day==host.Day&&p.Value.Status=="pending");
        internal bool CanStartStory(string actor)
        {
            if(!Enabled)return true;
            if(host.Economy.PackageB.RestoringSupplier)return false;
            if(actor!=null&&host.Economy.Business.CurrentActor==actor)return true;
            return !HasDue&&State?.Visits.Values.Any(v=>v.Day==host.Day&&v.Status=="arrived"&&(Store.currentClientInstance?.storeClient?.identifier==v.Client||ClientQueued(v.Client)))!=true;
        }
        private bool ClientQueued(string id){foreach(var c in Store.storeClientManager.clientStack)if(c.identifier==id)return true;return false;}
        internal bool CanQueue(string kind,string owner)
        {
            if(!Enabled)return true;
            if(!Quiet||Store.currentClientInstance!=null||host.Economy.PackageB.RestoringSupplier)return false;
            if(host.Engine.Frame()!=null&&host.ActOne?.WaitingForDays!=true)return false;
            var next=Next();if(next!=null&&(next.Kind!=kind||next.Owner!=owner))return false;
            foreach(var c in Store.storeClientManager.clientStack){
                if(c.identifier==NativeNormalSupplier.VendorId||c.identifier?.StartsWith(NativeBasicInspection.Prefix)==true||c.identifier?.StartsWith("ms_member_visit_")==true)return false;
                if(host.Integration.IsOwn(c)||!host.Economy.Trade.IsNaturalQueued(c))return false;
            }
            return true;
        }
        internal bool CaptureNaturalSlot(string ticket)
        {
            if(!Enabled)return false;
            var r=State?.Visits.Values.FirstOrDefault(v=>v.Kind=="inspection"&&v.Day==host.Day&&v.ExtraPickup&&v.OrdinaryTicket==null);
            if(r==null)return false;
            host.Engine.EditNormalVisits(s=>NormalVisits.Claim(s.NormalVisits,s.NormalVisits.Visits[r.Key],ticket));return true;
        }
        internal void QueueClient(string kind,string owner,StoreClient client)
        {
            if(!Enabled){Store.storeClientManager.AddNextClient(client);return;}
            var r=State?.Visits.GetValueOrDefault(NormalVisits.Key(kind,owner,host.Day));
            if(r==null)throw new InvalidOperationException("显式预约缺少来访凭据");
            foreach(var c in Store.storeClientManager.clientStack)if(c.identifier==client.identifier)return;
            host.Economy.MutateNative(()=>{
                host.Engine.EditNormalVisits(s=>NormalVisits.Queue(s.NormalVisits,s.NormalVisits.Visits[r.Key],client.identifier));
                Store.storeClientManager.AddNextClient(client);
            },()=>{});
        }
        private void EnsureBookings()
        {
            void Ensure(string kind,string owner,int day){if(day>=host.Day&&State?.Visits.ContainsKey(NormalVisits.Key(kind,owner,day))!=true)host.Engine.EditNormalVisits(s=>{var r=NormalVisits.Book(s,kind,owner,day);if(kind=="inspection"&&host.Inspection.Active.VisitDay==day){r.LegacyAllocated=true;r.Status="queued";r.Client=host.Inspection.PickupClientId;}});}
            var order=host.Inspection.Active;if(order?.Status=="ready"&&order.PickupDay>=host.Day)Ensure("inspection",order.Id.ToString(),order.PickupDay);
            host.Supplier.EnsureState();if(host.Supplier.State.DueDay>=host.Day)Ensure("supplier",NativeNormalSupplier.VendorId,host.Supplier.State.DueDay);
            foreach(var p in host.ReadState.Economy.Business.Appointments.Where(p=>p.Value.Status=="pending").OrderBy(p=>p.Value.Day).ThenBy(p=>p.Key,StringComparer.Ordinal))Ensure("member",p.Key,p.Value.Day);
        }
        private string PhysicalActor()
        {
            if(host.Economy.Business.CurrentActor is string member)return member;
            var client=Store.currentClientInstance?.storeClient;
            if(!host.Integration.IsOwn(client))return null;
            var scene=host.Package.Scenes.FirstOrDefault(s=>host.Integration.Prefix+s.Id==client.identifier);return scene==null?null:host.Integration.LeadActor(scene);
        }
        internal void Update()
        {
            if(!Enabled||host.Economy.IsTransferring)return;
            EnsureBookings();if(State==null)host.Engine.EditNormalVisits(s=>{});
            if(run!=Store.runID){
                run=Store.runID;if(Store.storeState==PlayerStore.StoreState.OPEN)GenerationComplete("restored_open_queue");
                var held=host.Inspection.Active;
                if(held?.Status=="ready"&&held.PickupDay==host.Day){var interrupted=State.Visits.Values.FirstOrDefault(v=>v.Kind=="inspection"&&v.Owner==held.Id.ToString()&&v.Day==host.Day&&v.Status=="arrived");if(interrupted!=null)host.Engine.EditNormalVisits(s=>s.NormalVisits.Visits[interrupted.Key].Status="queued");}
            }
            bool expire=State.Visits.Values.Any(v=>v.Day<host.Day&&v.Status is "booked" or "queued");
            if(expire)host.Engine.EditNormalVisits(s=>NormalVisits.Expire(s.NormalVisits,host.Day));
            foreach(var r in State.Visits.Values.Where(v=>v.Day==host.Day&&v.Status is "queued" or "arrived").ToArray()){
                bool present=Store.currentClientInstance?.storeClient?.identifier==r.Client||r.Kind=="member"&&PhysicalActor()==r.Owner;
                bool paid=r.Kind=="inspection"&&host.Inspection.State?.Orders.Any(o=>o.Id.ToString()==r.Owner&&o.Status=="done")==true;
                string status=paid?"done":present&&Store.isClientArrived?"arrived":r.Status=="arrived"&&!present&&!ClientQueued(r.Client)?"done":r.Status;
                if(status!=r.Status)host.Engine.EditNormalVisits(s=>s.NormalVisits.Visits[r.Key].Status=status);
            }
            if(!Quiet||Store.storeState!=PlayerStore.StoreState.OPEN||Store.currentClientInstance!=null)return;
            var due=Next();if(due==null)return;
            if(due.Kind=="inspection"&&host.Economy.FindPlayerItem(host.Inspection.Active.Entity)==null)return;
            if(due.Kind=="inspection"&&due.OrdinaryTicket==null&&!due.LegacyAllocated)host.Economy.Trade.ReservePickupSlot(due.Key);
            due=State.Visits[due.Key];
            if(!CanQueue(due.Kind,due.Owner))return;
            if(due.Kind=="inspection"){
                if(due.OrdinaryTicket==null&&!due.LegacyAllocated&&!State.Generation.ContainsKey(host.Day))return;
                host.Inspection.QueuePickup();
            }else if(due.Kind=="supplier")host.Supplier.QueueDue();
            else host.Economy.Business.Queue(due.Owner);
        }
    }
}
