using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    internal sealed partial class NativeCampaignItems : ICampaignSourceAdapter
    {
        private readonly Dictionary<string,string> feeInputs=new();
        private void SyncReleasedOriginals()
        {
            foreach(var original in C.Originals.Values.Where(x=>x.Released).ToArray()){
                var actual=E.FindPlayerItem(original.Entity);bool mine=actual!=null && GeneralHelper.IsItemOwned(actual);
                if(mine && original.Location!="native")host.Engine.EditCampaignItems(s=>{var r=s.CampaignItems.Originals[original.Role];r.Location="native";r.Owner=r.Custodian="player";});
                else if(!mine && original.Location=="native")host.Engine.EditCampaignItems(s=>{var r=s.CampaignItems.Originals[original.Role];r.Location="outside";r.Custodian="outside:released_owner_transfer";r.Owner="outside";});
            }
        }
        public CampaignOriginal ResolveOriginal(StoryState s,string role,string actor,int day)
        {
            var c=s.CampaignItems;string sku=role=="c_mic"?"ms_gear_stage_mic":role=="c_early_backup"?"ms_gear_commercial_guitar":null;if(sku==null)return null;
            c.Selections.TryGetValue(role,out var selection);
            var owned=s.Economy.OwnedItems.Values.Where(x=>x.ActorId==actor && x.ItemId==sku).ToArray();
            if(selection==null && owned.Length==1)selection=new(){Entity=owned[0].EntityId,Mode="member"};
            if(selection==null)return null;
            GameItem item;string owner;bool loan;
            if(selection.Mode=="member"){
                var actual=owned.SingleOrDefault(x=>x.EntityId==selection.Entity);if(actual==null)return null;
                item=NativeMemberEconomy.Decode(actual.NativeSnapshot);owner=actor;loan=false;s.Economy.OwnedItems.Remove(item.uniqueId);
            }else{
                item=E.FindPlayerItem(selection.Entity);if(item==null || item.identifier!=sku || !E.PackageB.PlayerOwned(item))return null;
                if(item.parentInventory==null || !item.parentInventory.Expel(item))throw new IOException("无法受理已选器材");
                owner=selection.Mode=="donate"?"party:band":"player";loan=selection.Mode=="loan";
            }
            if(item.identifier!=sku)throw new InvalidDataException("选择的实际器材型号不符");
            var gear=s.Economy.Business.Instruments.GetValueOrDefault(item.uniqueId);
            var fact=s.Workshop?.Advanced.Facts.GetValueOrDefault(item.uniqueId);if(fact!=null && fact.Faults.Count>0)throw new InvalidOperationException("所选器材还有已知故障，请先整备或取消选择改用舞台借件");
            if(gear!=null)gear.Custody="service:campaign";
            return new(){Role=role,Entity=item.uniqueId,Sku=sku,Snapshot=NativeMemberEconomy.Encode(item),Owner=owner,Custodian=actor,Location="member",Condition="serviced",CalibratedRevision=0,FirstObservedDay=day,Source="actual_selection:"+selection.Mode,Borrowed=loan,Lender=loan?"player":null,ReturnAgreement=loan?"比赛后交还玩家":"成员自有或明确赠予",ExistingComponents=gear?.Slots.Values.ToArray()??Array.Empty<int>(),ExistingConfiguration=gear==null?null:JsonSerializer.Serialize(gear,StoryContent.Json)};
        }
        internal void SelectExisting(string role,int entity,string mode)
        {
            if(C.Originals.ContainsKey(role))throw new InvalidOperationException("此角色原件已正式受理，不能用同名物覆盖");
            if(!new[]{"loan","donate","member"}.Contains(mode))throw new InvalidOperationException("请选择借用、赠予或成员自有来源");
            E.MutateNative(()=>host.Engine.EditCampaignItems(s=>s.CampaignItems.Selections[role]=new(){Entity=entity,Mode=mode}),()=>{});
        }
        internal void ClearSelection(string role)
        {
            if(C.Originals.ContainsKey(role))throw new InvalidOperationException("原件已受理，不能更换来源");
            E.MutateNative(()=>host.Engine.EditCampaignItems(s=>s.CampaignItems.Selections.Remove(role)),()=>{});
        }
        internal void UseFinishedStrings(string jobId,int entity)
        {
            var item=E.FindPlayerItem(entity)??throw new InvalidOperationException("请选择玩家持有的合格成品套弦");
            if(item.unitCount!=1 || !E.PackageB.PlayerOwned(item))throw new InvalidOperationException("尚未付款或不属于玩家的套弦不可投入");
            E.MutateNative(()=>{if(item.parentInventory==null || !item.parentInventory.Expel(item))throw new IOException("成品套弦无法移交");host.Engine.EditCampaignItems(s=>PackageC.UseFinishedStrings(s,jobId,new(){Entity=item.uniqueId,Sku=item.identifier,Reference="prepared_strings",Owner="player",Snapshot=NativeMemberEconomy.Encode(item)}));},()=>{});
        }
        private bool ActorPresent(string actor)
        {
            if(E.Business.CurrentActor==actor || E.Business.HeldStoryActor==actor)return true;
            var store=PlayerStore.instance;
            if(host.Integration.IsOwnCurrent && store.isClientArrived){string id=store.currentClientInstance.storeClient.identifier.Substring(host.Integration.Prefix.Length);return host.Package.Scenes.FirstOrDefault(x=>x.Id==id)?.Participants.Contains(actor)==true;}
            if(host.Integration.InVenue && C.PendingScene!=null && host.Package.Scenes.FirstOrDefault(x=>x.Id==C.PendingScene)?.Context=="night")return host.Package.Scenes.First(x=>x.Id==C.PendingScene).Participants.Contains(actor);
            return host.Integration.InVenue && host.Engine.Frame()?.Scene.Participants.Contains(actor)==true;
        }
        internal void SetFee(string id,int amount)=>E.MutateNative(()=>host.Engine.EditCampaignItems(s=>PackageC.QuoteFee(s,id,amount)),()=>{});
        internal bool CanCollectFee(string id)=>ActorPresent(C.Jobs[id].SupplyActor);
        internal bool CollectFee(string id)
        {
            var j=C.Jobs[id];if(j.FeeState=="paid")return false;
            if(!ActorPresent(j.SupplyActor))throw new InvalidOperationException("付款成员须实际到场，或选择免收工钱");
            int before=PlayerStore.instance.GetCash();bool paid=false;
            E.MutateNative(()=>{
                host.Engine.EditCampaignItems(s=>paid=PackageC.PayFee(s,id));
                if(paid){PlayerStore.instance.ModCash(j.Quote,false);if(PlayerStore.instance.GetCash()!=checked(before+j.Quote))throw new InvalidOperationException("原版现金与工钱结算不符");}
            },()=>{});return paid;
        }
        internal void OfferUseRequest(string actor)
        {
            if(!Enabled || C.Stage<2)return;
            var state=host.Engine.Snapshot();
            if(!PackageC.CanContact(state,actor,true) && !PackageC.CanContact(state,actor,false))return;
            host.Engine.EditCampaignItems(s=>PackageC.OfferUseRequest(s,actor,host.Day));host.Persist();
        }
        internal void AcceptUseRequest(string id)
        {
            if(!ActorPresent(C.UseRequests[id].Actor))throw new InvalidOperationException("请在成员实际来访时确认本次委托");
            E.MutateNative(()=>host.Engine.EditCampaignItems(s=>PackageC.AcceptUseRequest(s,id,host.Day,this)),()=>{});
        }
        internal void ReturnOptional(string id)
        {
            if(!ActorPresent(C.Jobs[id].SupplyActor))throw new InvalidOperationException("需要成员实际来访取件");
            E.MutateNative(()=>host.Engine.EditCampaignItems(s=>PackageC.DeliverOptional(s,id,host.Day,this)),()=>{});
        }
    }
}
