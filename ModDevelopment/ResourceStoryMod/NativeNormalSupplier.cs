using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    // A real mod supplier, not a vanilla lower merchant relabelled as middle.
    // Uses the existing phone book, tomorrow's client queue, stock and counter.
    internal sealed partial class NativeNormalSupplier
    {
        internal const long Number=8670200;
        internal const string VendorId="millsage_middle_equipment_supplier",SaveKey="millsage.supply.phone.v1";
        internal const string Name="中层器材供货";
        private readonly Core core;private readonly NativeStoryHost host;
        private readonly List<System.Action> roots=new();
        private string run;private float next;private NormalSupplierState state;
        internal NormalSupplierState State=>state;
        private PlayerStore Store=>PlayerStore.instance;
        internal bool Unlocked=>host.NormalPlay&&host.ReadState?.CompletedDays.ContainsKey("ms_scene_004")==true;
        private static NativeNormalSupplier Self=>Core.Instance?.Story?.Supplier;
        internal NativeNormalSupplier(Core c,NativeStoryHost h)
        {
            core=c;host=h;
            NativeOriginalNightGuard.Install(core);
            Patch(typeof(StorePhoneClient),"GetCallDialog",nameof(Call));Patch(typeof(StorePhoneClient),"UseService",nameof(Use));
            Patch(typeof(StorePhoneClient),"GetLocalizedDisplayName",nameof(Display));Patch(typeof(StorePhoneClient),"ShownInPhoneBook",nameof(Shown));
            Patch(typeof(PhoneUIManager),"WillAnswerCall",nameof(Answer));Patch(typeof(StoreClientListDict),"CreateStoreClient",nameof(Factory));
        }
        private void Patch(Type type,string method,string name)=>core.HarmonyInstance.Patch(ReleaseApi.Method(type,method),prefix:new HarmonyMethod(typeof(NativeNormalSupplier),name));
        private bool IsPhone(StorePhoneClient p)=>Store?.PhoneClientDict?.TryGetValue(Number,out var phone)==true&&phone.NativeIdentity()==p?.NativeIdentity();
        private static bool Display(StorePhoneClient __instance,ref string __result){if(Self?.IsPhone(__instance)!=true)return true;__result=Name;return false;}
        private static bool Shown(StorePhoneClient __instance,ref bool __result){if(Self?.IsPhone(__instance)!=true)return true;__result=Self.Unlocked;return false;}
        private static bool Answer(long __0,ref bool __result){if(__0!=Number||Self?.host.NormalPlay!=true)return true;__result=Self.Unlocked;return false;}
        private static bool Use(StorePhoneClient __instance){if(Self?.IsPhone(__instance)!=true)return true;Self.Book();return false;}
        private static bool Call(StorePhoneClient __instance,ref Dialogue __result)
        {
            var s=Self;if(s?.IsPhone(__instance)!=true)return true;s.Bind();s.core.EnsureChineseFallback();
            if(!s.Unlocked){__result=new Dialogue().SetText(Name,"请先留下乐队器材委托。现有原件继续保管。");return false;}
            bool pending=s.state.DueDay>=s.host.Day;string text=(pending?"已经约好第 "+s.state.DueDay+" 天到店。":"明天带器材补货到店。")+s.SupplyHint()+"按柜台现货报价成交，不用预付款；买不齐可以以后再约，委托没有截止日。";
            var action=(System.Action)(Action)(()=>s.Book());s.roots.Add(action);__result=new Dialogue().SetText(Name,text).SetEndAction(action);return false;
        }
        private static bool Factory(string __0,ref StoreClient __result){if(__0!=VendorId||Self==null)return true;__result=Self.CreateVendor();return false;}
        private StoreClient CreateVendor()
        {
            var reference=StoreClientList.CreateLowerLevelRareMerchant();
            var c=new StoreClient{identifier=VendorId,displayName=Name,spriteName=reference.spriteName,clientFaction=StoreClient.FACTION_MIDDLE,clientIntent=StoreClient.ClientIntent.SELL,clientCash=0,clientBudget=0,useClientBudget=false,dismissable=true,nonShootable=true,clientImpatience=999,mainDialogue=new Dialogue().SetText(Name,"器材按现货报价。资金不够的话，保留委托，之后再联系。"),allDoneDialogue=new Dialogue().SetText(Name,"需要补货时再打电话。")};
            c.CompleteClientCreation(false);return c;
        }
        private void Bind()
        {
            if(run==Store.runID&&state!=null)return;run=Store.runID;roots.Clear();
            state=Store.modData.TryGetValue(SaveKey,out var raw)?JsonSerializer.Deserialize<NormalSupplierState>(raw,StoryContent.Json):new NormalSupplierState{RunId=run};state.Validate(run);
        }
        internal void EnsureState()=>Bind();
        internal void QueueDue()
        {
            Bind();if(!state.Due(host.Day)||!host.Visits.CanQueue("supplier",VendorId))return;
            host.Visits.QueueClient("supplier",VendorId,CreateVendor());core.LoggerInstance.Msg("NORMAL_SUPPLIER_QUEUED day="+host.Day);
        }
        private void Persist(){state.Validate(run);Store.modData[SaveKey]=JsonSerializer.Serialize(state,StoryContent.Json);host.Persist();}
        internal void Book(){if(!Unlocked)return;Bind();if(state.Book(host.Day)){host.Engine.EditNormalVisits(s=>NormalVisits.Book(s,"supplier",VendorId,state.DueDay));Persist();}}
        internal void Update()
        {
            if(!host.NormalPlay||host.Engine==null||UnityEngine.Time.unscaledTime<next)return;next=UnityEngine.Time.unscaledTime+.35f;Bind();
            if(Store.PhoneClientDict!=null&&!Store.PhoneClientDict.ContainsKey(Number))Store.PhoneClientDict[Number]=new StorePhoneClient{displayName=Name,locID="",dialogFuncId="",phoneState=StorePhoneClient.PhoneState.Regular,cooldownDuration=0,currentCooldown=0,dialedBefore=true};
            var current=Store.currentClientInstance;
            if(current?.storeClient?.identifier==VendorId&&Store.isClientArrived){
                foreach(var stock in NativePackageB.ItemList(current.gridInv?.childItems).Where(x=>x.identifier=="energy_credit"))if(GeneralHelper.IsItemOwned(stock))GeneralHelper.SetItemOwned(stock,false);
                if(state.DueDay!=-1){state.ArrivedDay=host.Day;state.DueDay=-1;Persist();}
                string visit=host.ReadState.Workshop.Advanced.CurrentVisit;
                if(visit!=null&&host.ReadState.Workshop.Advanced.Visits[visit].Vendor==VendorId&&!state.PowerStockVisits.Contains(visit)&&!state.PowerStockDays.Contains(host.Day)&&!state.PowerSpaceBlockedVisits.Contains(visit)){
                    var power=DirectoryMaster.Item("energy_credit",false);
                    GeneralHelper.SetItemOwned(power,false);
                    PowerHelper.SetPowerSourceAt(power,host.Normal.Config.SupplierEnergyE);power.shortDescription+=" · 实际储电"+host.Normal.Config.SupplierEnergyE+"E，按原版储电量计价";
                    if(NativePackageB.Place(current.gridInv,power)){state.PowerStockVisits.Add(visit);state.PowerStockDays.Add(host.Day);Persist();host.Economy.PackageB.CaptureSupplier();}
                    else{state.PowerSpaceBlockedVisits.Add(visit);Persist();}
                }
                return;
            }
            if(!Unlocked||!state.Due(host.Day)||Store.storeState!=PlayerStore.StoreState.OPEN||host.ReportBlocked||PhoneUIManager.Instance?.isCalling==true||current!=null)return;
            foreach(var c in Store.storeClientManager.clientStack)if(c.identifier==VendorId)return;
            QueueDue();
        }
    }
}
