using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ResourceStoryMod.Story
{
    public sealed class EquipmentFact
    {
        public int Entity {get;set;}
        public string Sku {get;set;}
        public string Grade {get;set;}="ordinary";
        public string Owner {get;set;}="player";
        public string Origin {get;set;}="legacy_unknown";
        public bool Used {get;set;}
        public long Revision {get;set;}
        public long ElectricalRevision {get;set;}
        public long MechanicalRevision {get;set;}
        public Dictionary<string,string> Faults {get;set;}=new();
    }
    public sealed class EquipmentInspection
    {
        public int Entity {get;set;}
        public string Scope {get;set;}
        public string Hash {get;set;}
        public HashSet<string> KnownDomains {get;set;}=new();
        public Dictionary<string,string> KnownFaults {get;set;}=new();
    }
    public sealed class EquipmentCalibration
    {
        public int Entity {get;set;}
        public string Scope {get;set;}
        public string Hash {get;set;}
        public bool Passed {get;set;}
        public int Machine {get;set;}
        public string MeasurementProof {get;set;}
    }
    public sealed class MachineReference
    {
        public int Machine {get;set;}
        public int Reference {get;set;}
        public string Snapshot {get;set;}
        public string SelfCheckHash {get;set;}
    }
    public sealed class ServiceCustodyItem
    {
        public WorkshopEntity Item {get;set;}
        public string Owner {get;set;}
        public int SuggestedPrice {get;set;}
        public bool Charged {get;set;}
        public bool DeliverToMember {get;set;}
    }
    public sealed class EquipmentService
    {
        public string Id {get;set;}
        public string Actor {get;set;}
        public int Host {get;set;}
        public string HostSnapshot {get;set;}
        public string Status {get;set;}="authorized";
        public Dictionary<string,int> InitialSlots {get;set;}=new();
        public Dictionary<string,int> EffectiveSlots {get;set;}=new();
        public Dictionary<int,ServiceCustodyItem> Held {get;set;}=new();
        public List<ServiceStep> Steps {get;set;}=new();
        public HashSet<string> Improvements {get;set;}=new();
        public int MaterialQuote {get;set;}
        public string Delivery {get;set;}
        public int Paid {get;set;}
        public bool Terminal=>Status=="delivered" || Status=="cancelled";
    }
    public sealed class ServiceStep
    {
        public string Id {get;set;}
        public string Kind {get;set;}
        public string Domain {get;set;}
        public string Slot {get;set;}
        public int Component {get;set;}
        public string ComponentOwner {get;set;}
        public string Status {get;set;}="running";
        public int Labor {get;set;}
        public int PartsQuote {get;set;}
        public string Scope {get;set;}="basic";
        public string FactsAtStart {get;set;}
        public int ConsumableQuote {get;set;}
    }
    public sealed class WorkshopLedger
    {
        public string Id {get;set;}
        public string Kind {get;set;}
        public string Actor {get;set;}
        public int Entity {get;set;}
        public string Service {get;set;}
        public int Day {get;set;}
        public string Result {get;set;}
    }
    public sealed class NativePrintRecord
    {
        public string Id {get;set;}
        public int Machine {get;set;}
        public int Chip {get;set;}
        public string Recipe {get;set;}
        public string Status {get;set;}="running";
        public int NativeDuration {get;set;}
        public int NativeEnergy {get;set;}
        public List<int> Outputs {get;set;}=new();
        public List<WorkshopEntity> Materials {get;set;}=new();
        public List<WorkshopEntity> Pending {get;set;}=new();
        public int EnergySpent {get;set;}
        public int Cycles {get;set;}
        public string PauseReason {get;set;}
    }
    public sealed class SupplyBatch
    {
        public bool Priority {get;set;}
        public bool Native {get;set;}
        public string DTemplate {get;set;}
        public string SkipReason {get;set;}
        public string Pool {get;set;}
        public string Sku {get;set;}
        public string Grade {get;set;}="ordinary";
        public bool Used {get;set;}
        public string Fault {get;set;}="normal";
        public string Domain {get;set;}
        public int Quantity {get;set;}
        public bool Committed {get;set;}
        public List<WorkshopEntity> Remaining {get;set;}=new();
    }
    public sealed class SupplierVisit
    {
        public bool PriorityPlanned {get;set;}
        public Dictionary<string,string> PriorityBlocks {get;set;}=new();
        public string Id {get;set;}
        public long Sequence {get;set;}
        public int Day {get;set;}
        public string Vendor {get;set;}
        public string Zone {get;set;}
        public string Status {get;set;}="pending";
        public List<SupplyBatch> Batches {get;set;}=new();
        public bool StockCaptured {get;set;}
        public List<WorkshopEntity> NativeStock {get;set;}=new();
        public int NativeCash {get;set;}
        public int NativeBudget {get;set;}
        public bool NativeUseBudget {get;set;}
        public bool NativeBlueprintCaptured {get;set;}
        public string NativeSprite {get;set;}
        public int NativeBuyModifier {get;set;}
        public int NativeSellModifier {get;set;}
        public bool NativeMarkup {get;set;}
        public bool NativeWholesale {get;set;}
        public bool NativeRequireVariety {get;set;}
        public string[] NativeVariety {get;set;}=Array.Empty<string>();
    }
    public sealed class PackageBState
    {
        public int Version {get;set;}=1;
        public Dictionary<int,EquipmentFact> Facts {get;set;}=new();
        public Dictionary<string,EquipmentInspection> Inspections {get;set;}=new();
        public Dictionary<string,EquipmentCalibration> Calibrations {get;set;}=new();
        public Dictionary<string,EquipmentCalibration> CalibrationHistory {get;set;}=new();
        public Dictionary<int,MachineReference> References {get;set;}=new();
        public Dictionary<string,EquipmentService> Services {get;set;}=new();
        public Dictionary<string,NativePrintRecord> Prints {get;set;}=new();
        public Dictionary<string,SupplierVisit> Visits {get;set;}=new();
        public Dictionary<string,int> Quotas {get;set;}=new();
        public Dictionary<string,WorkshopLedger> Ledger {get;set;}=new();
        public HashSet<string> Migrations {get;set;}=new();
        public HashSet<string> ArchiveReceipts {get;set;}=new();
        public HashSet<string> FulfilledDemands {get;set;}=new();
        public long VisitSequence {get;set;}
        public string CurrentVisit {get;set;}
        public Dictionary<string,RepairPlan> RepairPlans {get;set;}=new();
        public Dictionary<string,RepairNote> RepairNotes {get;set;}=new();
    }
    public static partial class PackageB
    {
        public static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        public static string Hash(string value)=>RuntimeCompat.Hex(RuntimeCompat.Sha256(Encoding.UTF8.GetBytes(value)));
        public static EquipmentFact Fact(PackageBState s,int entity,string sku,string owner="player",string grade="ordinary",string origin="legacy_unknown")
        {
            Need(entity>0 && !string.IsNullOrEmpty(sku) && PackageBCatalog.Rank(grade)>=0,"物品事实无效");
            if(s.Facts.TryGetValue(entity,out var prior)){Need(prior.Sku==sku,"原件ID对应不同型号");return prior;}
            var f=new EquipmentFact{Entity=entity,Sku=sku,Owner=owner,Grade=grade,Origin=origin};s.Facts.Add(entity,f);return f;
        }
        public static bool Migrate(StoryState state)
        {
            if(state.Workshop==null)return false;
            if(state.Workshop.Advanced!=null){Validate(state.Workshop.Advanced);return false;}
            var s=state.Workshop.Advanced=new PackageBState();s.Migrations.Add("v080_to_b090");
            foreach(var g in state.Economy?.Business?.Instruments.Values??Enumerable.Empty<MusicInstrument>())Fact(s,g.EntityId,g.ItemId,g.Owner);
            foreach(var c in state.Economy?.Business?.Components.Values??Enumerable.Empty<MusicComponent>())Fact(s,c.EntityId,c.ItemId,c.Owner);
            foreach(var j in state.Workshop.Jobs.Values)foreach(var e in j.Inputs.Concat(j.Outputs))Fact(s,e.Entity,e.ItemId,"player",e.Grade);
            return true;
        }
        public static decimal FaultFactor(IEnumerable<string> faults)
        {
            decimal f=1;foreach(string x in faults)f=Math.Min(f,x=="dirty_oxidized"?.85m:x=="contact_fault"?.60m:x=="structural_fault"?.50m:1m);return f;
        }
        public static int LeafValue(PackageBState s,int entity,string sku=null)
        {
            var f=s.Facts.GetValueOrDefault(entity);return (int)Math.Round(PackageBCatalog.Price(f?.Sku??sku)*PackageBCatalog.Multiplier(f?.Grade)*FaultFactor(f?.Faults.Values??Enumerable.Empty<string>()),0,MidpointRounding.AwayFromZero);
        }
        private static IEnumerable<KeyValuePair<string,string>> ActiveFaults(EquipmentFact f,Dictionary<string,int> slots)
            =>f.Faults.Where(x=>!slots.ContainsKey(x.Key=="board"?"service.board":x.Key=="driver"?"service.driver":x.Key=="power"?"power":"__no_slot"));
        public static int Value(PackageBState s,MemberBusinessState b,int entity,Dictionary<string,int> projection=null)
        {
            if(!b.Instruments.TryGetValue(entity,out var g))return LeafValue(s,entity);
            var f=Fact(s,entity,g.ItemId,g.Owner);var slots=projection??g.Slots;
            decimal basePrice=PackageBCatalog.Price(g.ItemId),total=0,mult=PackageBCatalog.Multiplier(f.Grade);
            var sealedParts=PackageBCatalog.Rows("internal_repair_slots").Where(x=>x["host_id"]==g.ItemId).Select(x=>(Slot:x["slot"],Domain:x["slot"].Replace("service.",""),Price:PackageBCatalog.Number(x["factory_reference_portion"]))).ToList();
            if(g.ItemId=="ms_gear_monitor_speaker")sealedParts.Add(("power","power",260));
            decimal core=basePrice-sealedParts.Sum(x=>x.Price);
            total+=core*mult*FaultFactor(f.Faults.Where(x=>!sealedParts.Any(p=>p.Domain==x.Key)).Select(x=>x.Value));
            foreach(var p in sealedParts)if(!slots.ContainsKey(p.Slot))total+=p.Price*mult*FaultFactor(new[]{f.Faults.GetValueOrDefault(p.Domain,"normal")});
            foreach(int part in slots.Values)total+=LeafExactValue(s,part,b.Components.GetValueOrDefault(part)?.ItemId);
            return (int)Math.Round(total,0,MidpointRounding.AwayFromZero);
        }
        private static decimal LeafExactValue(PackageBState s,int id,string sku){var f=s.Facts.GetValueOrDefault(id);return PackageBCatalog.Price(f?.Sku??sku)*PackageBCatalog.Multiplier(f?.Grade)*FaultFactor(f?.Faults.Values??Enumerable.Empty<string>());}
        public static Dictionary<string,int> Stats(PackageBState s,MemberBusinessState b,int entity,Dictionary<string,int> projection=null)
        {
            var g=b.Instruments[entity];var f=Fact(s,entity,g.ItemId,g.Owner);var slots=projection??g.Slots;
            var row=PackageBCatalog.Rows("equipment_package_b").First(x=>x["id"]==g.ItemId);
            string[] keys={"noiseControl","stability","response","portability"},cols={"base_noise_control","base_stability","base_response","base_portability"};
            int[] v=cols.Select(x=>PackageBCatalog.Number(row[x])).ToArray();int rank=PackageBCatalog.Rank(f.Grade);v[0]+=rank*2;v[1]+=rank*2;v[2]+=rank;
            if(g.ItemId=="ms_gear_monitor_speaker" && slots.ContainsKey("power")){v[0]-=4;v[1]-=4;v[3]+=1;}
            foreach(string fault in ActiveFaults(f,slots).Select(x=>x.Value)){
                int[] d=fault=="dirty_oxidized"?new[]{-4,-4,-2,0}:fault=="contact_fault"?new[]{-12,-10,-8,0}:fault=="structural_fault"?new[]{-8,-12,-6,-3}:new int[4];for(int i=0;i<4;i++)v[i]+=d[i];
            }
            foreach(var p in slots){
                var fact=s.Facts.GetValueOrDefault(p.Value);string sku=fact?.Sku??b.Components.GetValueOrDefault(p.Value)?.ItemId;
                var effect=PackageBCatalog.Rows("module_effects").FirstOrDefault(x=>x["id"]==sku && x["host_id"]==g.ItemId);
                if(effect==null)continue;decimal scale=fact?.Grade=="precision"?1.30m:fact?.Grade=="selected"?1.15m:1m;
                decimal health=fact?.Faults.Values.Any(x=>x=="contact_fault" || x=="structural_fault")==true?0:fact?.Faults.Values.Contains("dirty_oxidized")==true?.75m:1m;
                string[] c={"noise","stability","response","portability"};for(int i=0;i<4;i++){int d=PackageBCatalog.Number(effect[c[i]]);v[i]+=d>0?(int)Math.Round(d*scale*health,0,MidpointRounding.AwayFromZero):d;}
            }
            return Enumerable.Range(0,4).ToDictionary(i=>keys[i],i=>RuntimeCompat.Clamp(v[i],0,100));
        }
        public static string FactsHash(EquipmentFact f)=>Hash(f.Sku+"|"+f.Grade+"|"+string.Join(";",f.Faults.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>x.Key+"="+x.Value)));
        public static string MeasurementHash(PackageBState s,MemberBusinessState b,int entity,string scope,Dictionary<string,int> projection=null)
        {
            var g=b.Instruments[entity];var f=s.Facts[entity];var slots=projection??g.Slots;
            var fields=new List<string>{g.ItemId,f.Grade,(scope=="passive"?f.MechanicalRevision:f.ElectricalRevision).ToString()};
            fields.AddRange(ActiveFaults(f,slots).Where(x=>x.Value=="contact_fault" || x.Value=="structural_fault").OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>x.Key+"="+x.Value));
            foreach(var p in slots.OrderBy(x=>x.Key,StringComparer.Ordinal)){var c=s.Facts[p.Value];fields.Add(p.Key+":"+p.Value+":"+c.Sku+":"+c.Grade+":"+c.ElectricalRevision+":"+string.Join(",",c.Faults.Values.Where(x=>x=="contact_fault" || x=="structural_fault").OrderBy(x=>x,StringComparer.Ordinal)));}
            return Hash(string.Join("|",fields));
        }
        public static bool CalibrationValid(PackageBState s,MemberBusinessState b,int entity,string scope="basic",Dictionary<string,int> projection=null)
            =>s.Calibrations.TryGetValue(entity+":"+scope,out var c) && c.Passed && c.Hash==MeasurementHash(s,b,entity,scope,projection);
        public static bool Healthy(PackageBState s,MemberBusinessState b,int entity,Dictionary<string,int> projection=null)
        {
            var g=b.Instruments[entity];var slots=projection??g.Slots;return !ActiveFaults(s.Facts[entity],slots).Any() && slots.Values.All(id=>s.Facts[id].Faults.Count==0);
        }
        public static string ReferenceHash(PackageBState s,int machine)
        {
            if(!s.References.TryGetValue(machine,out var r) || r.Reference<=0 || !s.Facts.TryGetValue(r.Reference,out var f) || f.Faults.Count!=0)return null;
            return Hash(machine+"|"+r.Reference+"|"+FactsHash(f));
        }
        public static bool ReferenceValid(PackageBState s,int machine)=>s.References.TryGetValue(machine,out var r) && ReferenceHash(s,machine) is string hash && r.SelfCheckHash==hash;
        public static void Validate(PackageBState s)
        {
            if(s.Version!=1 || s.Facts==null || s.Services==null || s.References==null || s.Visits==null || s.Quotas==null || s.Inspections==null || s.Calibrations==null || s.Migrations==null || s.Ledger==null || s.Prints==null || s.FulfilledDemands==null || s.ArchiveReceipts==null || s.VisitSequence<0)throw new InvalidDataException("包2存档字段或版本无效；保留原档");
            if(s.RepairPlans==null||s.RepairNotes==null)throw new InvalidDataException("维修记录字段损坏；保留原档");
            foreach(var p in s.RepairPlans)if(p.Value==null||p.Value.Job!=p.Key||!s.Services.ContainsKey(p.Value.Service)||p.Value.Days<1||p.Value.Days>3||p.Value.Steps==null||p.Value.Steps.Count==0)throw new InvalidDataException("按日维修方案损坏；保留原档");
            foreach(var p in s.RepairNotes)if(p.Value==null||p.Value.Id!=p.Key||p.Value.Entity<=0||p.Value.Host<=0||!MemberBusiness.ValidActor(p.Value.Actor)||p.Value.Day<0||p.Value.NotifiedDay< -1||p.Value.PurchasePrice<0||p.Value.Service!=null&&!s.Services.ContainsKey(p.Value.Service))throw new InvalidDataException("器材需求单记录损坏；保留原档");
            foreach(var p in s.Facts)if(p.Value==null || p.Key<=0 || p.Value.Entity!=p.Key || PackageBCatalog.Rank(p.Value.Grade)<0 || p.Value.Faults==null || p.Value.Faults.Any(x=>!new[]{"dirty_oxidized","contact_fault","structural_fault"}.Contains(x.Value)))throw new InvalidDataException("品质/故障事实损坏");
            var members=s.Services.Values.Where(x=>!x.Terminal && MemberBusiness.ValidActor(x.Actor)).ToArray();
            if(s.Services.Values.Count(x=>!x.Terminal&&x.Actor=="outside"&&x.Delivery==null&&x.Status!="return_ready")>1)throw new InvalidDataException("普通外部保管位超过上限");
            if(members.Length>5 || members.GroupBy(x=>x.Actor).Any(x=>x.Count()>1) || s.Services.Values.Where(x=>!x.Terminal).GroupBy(x=>x.Host).Any(x=>x.Count()>1))throw new InvalidDataException("服务保管重复或超过上限");
            if(s.Quotas.Any(x=>x.Value<0))throw new InvalidDataException("供货额度损坏");
            if(s.CurrentVisit!=null && !s.Visits.ContainsKey(s.CurrentVisit))throw new InvalidDataException("当前供应商访问不存在");
            foreach(var v in s.Visits.Values)if(v==null || v.NativeStock==null || (v.StockCaptured && (v.NativeStock.Select(x=>x.Entity).Distinct().Count()!=v.NativeStock.Count || v.NativeStock.Any(x=>x.Entity<=0 || x.Units<=0 || string.IsNullOrWhiteSpace(x.ItemId) || string.IsNullOrWhiteSpace(x.Snapshot)))))throw new InvalidDataException("供应商库存快照损坏；保留原档");
            foreach(var v in s.Visits.Values.Where(v=>v.PriorityPlanned))if(v.Batches==null||v.PriorityBlocks==null||v.Batches.Count>NormalSupply.VisitCap(v.Zone)||v.Batches.Count(b=>b.Pool=="large")>1||v.Batches.Any(b=>b.Native&&(b.Pool is not ("large" or "native_food" or "native_cloth")||b.Quantity!=1)))throw new InvalidDataException("优先补货超出原生位置或补给额度");
        }
    }
}
