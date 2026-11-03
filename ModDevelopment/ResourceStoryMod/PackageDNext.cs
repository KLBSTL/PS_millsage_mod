using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ResourceStoryMod.Story
{
    public sealed class MusicNextState
    {
        public int Version{get;set;}=1;
        public Dictionary<int,MusicUseCase> Uses{get;set;}=new();
        public Dictionary<string,MusicUseVisit> Visits{get;set;}=new();
        public Dictionary<int,MusicBusinessDay> Days{get;set;}=new();
        public Dictionary<string,MusicObservedOffer> Offers{get;set;}=new();
        public Dictionary<string,MusicTechnicalCase> Cases{get;set;}=new();
        public Dictionary<string,MusicRealizedCost> Realized{get;set;}=new();
        public Dictionary<string,MusicObservedBuyer> BuyerObservations{get;set;}=new();
        public int LastNaturalDay{get;set;}=-100;
        public int BusinessOrdinal{get;set;}
        public int LastOpenedDay{get;set;}=-1;
    }
    public sealed class MusicUseCase
    {
        public int Entity{get;set;} public string Customer{get;set;} public string Origin{get;set;} public string Profile{get;set;} public string Target{get;set;} public string Zone{get;set;} public string Sku{get;set;} public string Sprite{get;set;}
        public string Snapshot{get;set;} public Dictionary<int,string> Components{get;set;}=new();
        public int SoldDay{get;set;} public int SoldOrdinal{get;set;} public string Status{get;set;}="outside";
        public string Service{get;set;} public int DueDay{get;set;}=-1; public int Budget{get;set;} public int Cash{get;set;} public bool BudgetInitialized{get;set;}
        public int NoteEntity{get;set;}
        public int? AcquisitionPaid{get;set;}
        public int? AcquisitionDay{get;set;}
        public bool SavedOnTemporaryCounter{get;set;}
        public string Receipt{get;set;} public int Paid{get;set;} public int AuthorizedQuote{get;set;}
    }
    public sealed class MusicUseVisit {public string Id{get;set;} public string Ticket{get;set;} public int Entity{get;set;} public int Day{get;set;} public string Kind{get;set;} public string Status{get;set;}="queued";}
    public sealed class MusicBusinessDay
    {
        public int Day{get;set;} public int OpeningCash{get;set;} public int ClosingCash{get;set;} public bool Opened{get;set;} public bool Closed{get;set;} public bool Controlled{get;set;}
        public long EffectiveMs{get;set;} public string Context{get;set;} public int Inventory{get;set;} public int Pending{get;set;} public int OrdinarySlots{get;set;} public int Attractiveness{get;set;} public string[] UnlockedPerks{get;set;}=Array.Empty<string>();
    }
    public sealed class MusicObservedOffer {public string Id{get;set;} public int Day{get;set;} public int Entity{get;set;} public string Sku{get;set;} public string Visit{get;set;} public string Zone{get;set;} public int Quote{get;set;} public bool Purchased{get;set;} public string Decision{get;set;}="未记录决定";}
    public sealed class MusicTechnicalCase {public string Id{get;set;} public int Entity{get;set;} public int Day{get;set;} public string Kind{get;set;} public string Source{get;set;} public string Configuration{get;set;} public string Summary{get;set;} public int? Cost{get;set;} public int ActualReceived{get;set;} public Dictionary<string,string> Before{get;set;}=new(); public Dictionary<string,string> After{get;set;}=new();}
    public sealed class MusicRealizedCost {public string Receipt{get;set;} public int? Cost{get;set;}}
    public sealed class MusicObservedBuyer {public string Id{get;set;} public int Day{get;set;} public string Zone{get;set;} public int RemainingBudget{get;set;} public int MatchCount{get;set;} public int BudgetBlocked{get;set;} public string Reason{get;set;}}
    public sealed class MusicPlan
    {
        public string Name,Target,Block; public bool Meets,Executable; public int Seconds,Energy,Parts; public int? AddedCost;
        public Dictionary<string,int> Inputs=new(),Tools=new(); public List<string> Steps=new(); public Dictionary<string,int> Stats=new();
    }
    public static class PackageDNext
    {
        public static string Name(string sku)=>sku switch{"screwdriver" or "ref_screwdriver"=>"螺丝刀","wire_cutter" or "ref_wirecutter"=>"剪线钳","welder" or "ref_welder"=>"焊接工具","magnifier" or "ref_magnifier"=>"放大镜","metal_ingot" or "ref_metal_ingot"=>"金属锭","printer_plastic"=>"打印塑料","wire" or "ref_wire"=>"线材","common_electronic"=>"普通电子件","rare_electronic"=>"稀有电子件","common_chemical"=>"普通化学品","energy_credit"=>"能源额度","station:electrobench"=>"空闲整备台","station:calibrator"=>"空闲标定台","energy:E"=>"可用电量E",_=>WorkshopCatalog.Product(sku)?.Name??MusicCatalog.Find(sku)?.Name??sku};
        public static void Validate(MusicNextState n)
        {
            if(n==null||n.Version!=1||n.Uses==null||n.Visits==null||n.Days==null||n.Offers==null||n.Cases==null||n.Realized==null||n.BuyerObservations==null||n.BusinessOrdinal<0)throw new InvalidDataException("续用与经营观察记录无效");
            foreach(var p in n.Uses){var u=p.Value;if(u==null||p.Key!=u.Entity||u.Entity<=0||!PackageDCatalog.Hosts.Contains(u.Sku)||string.IsNullOrWhiteSpace(u.Snapshot)||string.IsNullOrWhiteSpace(u.Customer)||u.Components==null||u.Budget<0||u.Cash<0||u.Paid<0||!new[]{"outside","offered","servicing","pickup","cancel_pickup","done","declined"}.Contains(u.Status))throw new InvalidDataException("普通外部物主记录无效");}
            if(n.Uses.Values.Count(x=>x.Status is "servicing" or "pickup" or "cancel_pickup")>1)throw new InvalidDataException("普通外部保管位已占用");
            foreach(var p in n.Visits){var v=p.Value;if(v==null||v.Id!=p.Key||!n.Uses.ContainsKey(v.Entity)||string.IsNullOrEmpty(v.Ticket)||!new[]{"queued","active","closed"}.Contains(v.Status))throw new InvalidDataException("续用来访记录无效");}
            if(n.Visits.Values.Where(x=>x.Status!="closed").GroupBy(x=>x.Entity).Any(g=>g.Count()>1))throw new InvalidDataException("同一物主存在重复来访");
            foreach(var p in n.Cases)if(p.Value==null||p.Key!=p.Value.Id||!new[]{"sale","service","salvage"}.Contains(p.Value.Kind)||string.IsNullOrWhiteSpace(p.Value.Source))throw new InvalidDataException("技术案例缺少真实来源");
        }
        public static void ValidateState(StoryState s)
        {
            if(s.MusicTrade==null)return;Validate(s.MusicTrade.Next);
            foreach(var u in s.MusicTrade.Next.Uses.Values){
                if(!s.MusicTrade.Receipts.TryGetValue(u.Origin,out var origin)||origin.Kind!="sale"||!origin.Allocations.ContainsKey(u.Entity)||u.Customer!="customer_"+origin.Visit||!s.Economy.Business.Instruments.TryGetValue(u.Entity,out var g))throw new InvalidDataException("续用原件缺少实际出售来源");
                if(u.Status is "servicing" or "pickup" or "cancel_pickup"){
                    if(g.Owner!="outside"||u.Service==null||!s.Workshop.Advanced.Services.TryGetValue(u.Service,out var service)||service.Host!=u.Entity||service.Actor!="outside")throw new InvalidDataException("外部工单与原件授权不一致");
                }
            }
            foreach(var entry in s.MusicTrade.Next.Cases.Values){
                bool proof=entry.Kind=="sale"?s.MusicTrade.Receipts.TryGetValue(entry.Source,out var receipt)&&receipt.Kind=="sale"&&receipt.Allocations.ContainsKey(entry.Entity):entry.Kind=="service"?s.Workshop.Advanced.Services.Values.Any(x=>x.Host==entry.Entity&&x.Delivery==entry.Source):s.MusicTrade.Salvage.TryGetValue(entry.Source,out var salvage)&&salvage.Complete&&salvage.Host==entry.Entity;
                if(!proof)throw new InvalidDataException("技术实例缺少对应实际回执");
            }
        }
        public static string Configuration(StoryState s,int entity,Dictionary<string,int> slots=null)
        {
            var g=s.Economy.Business.Instruments[entity];return PackageB.MeasurementHash(s.Workshop.Advanced,s.Economy.Business,entity,"basic",slots??g.Slots);
        }
        public static void Case(StoryState s,string id,int entity,int day,string kind,string source,string summary)
        {
            if(s.MusicTrade.Next.Cases.ContainsKey(id)||s.CampaignItems?.Originals.Values.Any(x=>x.Entity==entity)==true)return;
            if(!s.Economy.Business.Instruments.ContainsKey(entity))return;
            var entry=new MusicTechnicalCase{Id=id,Entity=entity,Day=day,Kind=kind,Source=source,Configuration=Configuration(s,entity),Summary=summary,Cost=PackageDFull.TotalCost(s,entity)};
            entry.After=s.Economy.Business.Instruments[entity].Slots.ToDictionary(x=>x.Key,x=>s.Workshop.Advanced.Facts[x.Value].Sku+" / "+PackageBCatalog.GradeName(s.Workshop.Advanced.Facts[x.Value].Grade));
            var service=s.Workshop.Advanced.Services.Values.FirstOrDefault(x=>x.Delivery==source);
            if(service!=null){entry.Before=service.InitialSlots.ToDictionary(x=>x.Key,x=>s.Workshop.Advanced.Facts[x.Value].Sku+" / "+PackageBCatalog.GradeName(s.Workshop.Advanced.Facts[x.Value].Grade));entry.Cost=ServiceCost(s,service);entry.ActualReceived=service.Paid;}
            else if(s.MusicTrade.Receipts.TryGetValue(source,out var receipt))entry.ActualReceived=receipt.Total;
            s.MusicTrade.Next.Cases[id]=entry;
        }
        public static int? ServiceCost(StoryState s,EquipmentService service)
        {
            var parts=service.Steps.Where(x=>x.Status=="complete"&&x.ComponentOwner=="player"&&x.Component>0&&service.EffectiveSlots.Values.Contains(x.Component)).Select(x=>x.Component).Distinct().ToArray();
            var expenses=s.MusicTrade.Investments.Values.Where(x=>service.Steps.Any(step=>step.Id==x.Job)).ToArray();
            if(expenses.Any(x=>!x.Known)||parts.Any(id=>!s.MusicTrade.Costs.TryGetValue(id,out var c)||!c.Known))return null;
            return checked(parts.Sum(id=>s.MusicTrade.Costs[id].Amount)+expenses.Sum(x=>x.Amount));
        }
        public static string[] CaseThemes={"够用与过度整备：按用途和实际预算决定，不默认清洁、满配。","保留旧件解决新用途：先看当前缺口，不重复采购相同部件。","拆件不等于回本：残料和模块仍是存货，参考值不等于现金。"};
        public static MusicPlan Current(StoryState s,int entity,string target,string zone)
        {
            var p=new MusicPlan{Name="按现状处理",Target=target};if(!s.Workshop.Advanced.Facts.ContainsKey(entity)||!s.Economy.Business.Instruments.ContainsKey(entity)){p.Block="缺少已授权原件记录";return p;}
            p.Block=PackageD.Eligible(s,PackageDCatalog.Buyer(target),entity,zone,true,false,0,0,false);p.Meets=p.Block==null;p.Executable=p.Meets;p.AddedCost=0;p.Stats=PackageB.Stats(s.Workshop.Advanced,s.Economy.Business,entity);return p;
        }
        public static MusicPlan Plan(StoryState source,int entity,string target,string zone,IReadOnlyDictionary<string,int> stock,HashSet<int> availableParts)
        {
            var best=Current(source,entity,target,zone);best.Name="最低合格整备";if(best.Meets)return best;
            var f=source.Workshop.Advanced.Facts.GetValueOrDefault(entity);if(f==null||!source.Economy.Business.Instruments.ContainsKey(entity))return best;
            var inspection=source.Workshop.Advanced.Inspections.GetValueOrDefault(entity+":detailed");
            if(inspection==null||inspection.Hash!=PackageB.InspectionHash(source.Workshop.Advanced,source.Economy.Business,entity,"detailed",source.Economy.Business.Instruments[entity].Slots)){best.Block="先完成已授权深度检查，再比较整备；不估计未知内部故障";return best;}
            var profile=PackageDCatalog.Buyer(target);if(profile==null||!profile.Hosts.Contains(f.Sku)||!profile.Zones.Contains(zone)){best.Block="本用途不接受这个型号或地区";return best;}
            var variants=new List<Dictionary<string,int>>{new(source.Economy.Business.Instruments[entity].Slots)};
            var options=PackageBCatalog.Rows("module_effects").Where(r=>r["host_id"]==f.Sku&&r["effect_status"]=="B_enabled").Select(r=>(Sku:r["id"],Slot:r["slot"])).Concat(PackageBCatalog.Rows("internal_repair_slots").Where(r=>r["host_id"]==f.Sku&&f.Faults.ContainsKey(r["slot"].Replace("service.",""))).Select(r=>(Sku:r["accepted_sku"],Slot:r["slot"])));
            foreach(var group in options.GroupBy(r=>r.Slot)){
                var candidates=source.Workshop.Advanced.Facts.Values.Where(x=>availableParts.Contains(x.Entity)&&x.Owner=="player"&&x.Faults.Count==0&&group.Any(r=>r.Sku==x.Sku)).GroupBy(x=>x.Sku+"/"+x.Grade).Select(g=>g.OrderBy(x=>source.MusicTrade.Costs.TryGetValue(x.Entity,out var cost)&&cost.Known?cost.Amount:int.MaxValue).ThenBy(x=>x.Entity).First()).OrderBy(x=>PackageB.LeafValue(source.Workshop.Advanced,x.Entity)).Take(3).ToArray();
                var prior=variants.ToArray();foreach(var slots in prior)foreach(var part in candidates){if(slots.ContainsValue(part.Entity))continue;if(slots.TryGetValue(group.Key,out int old)&&source.Workshop.Advanced.Facts.TryGetValue(old,out var previous)&&previous.Sku==part.Sku&&PackageBCatalog.Rank(previous.Grade)>=PackageBCatalog.Rank(part.Grade)&&previous.Faults.Count==0)continue;var next=new Dictionary<string,int>(slots){[group.Key]=part.Entity};if(variants.Count<256)variants.Add(next);}
            }
            var simulation=JsonSerializer.Deserialize<StoryState>(JsonSerializer.Serialize(source,StoryContent.Json),StoryContent.Json);
            foreach(var slots in variants){
                var s=simulation;var a=s.Workshop.Advanced;var g=s.Economy.Business.Instruments[entity];var fact=a.Facts[entity];fact.Faults=new(f.Faults);fact.ElectricalRevision=f.ElectricalRevision;g.Slots=new(source.Economy.Business.Instruments[entity].Slots);if(source.Workshop.Advanced.Calibrations.TryGetValue(entity+":basic",out var originalReport))a.Calibrations[entity+":basic"]=originalReport;else a.Calibrations.Remove(entity+":basic");var p=new MusicPlan{Name="最低合格整备",Target=target,AddedCost=0};
                void Step(string kind){var row=PackageBCatalog.Rows("services_package_b").First(x=>x["id"]==kind);p.Steps.Add(row["name"]);p.Seconds+=PackageBCatalog.Number(row["seconds_draft"]);p.Energy+=PackageBCatalog.Number(row["energy_draft"]);string station=PackageBCatalog.Station(row["station"]);if(station!="counter")p.Tools["station:"+station]=1;}
                void Input(string sku,int units=1){p.Inputs[sku]=p.Inputs.GetValueOrDefault(sku)+units;p.AddedCost=null;}
                bool impossible=false;
                foreach(var fault in fact.Faults.ToArray()){
                    string bypass=fault.Key=="board"?"service.board":fault.Key=="driver"?"service.driver":fault.Key=="power"?"power":null;if(bypass!=null&&slots.ContainsKey(bypass))continue;
                    if(fault.Value=="dirty_oxidized"&&profile.Dirty)continue;
                    if(fault.Value=="dirty_oxidized"){if(!p.Steps.Contains(PackageBCatalog.Rows("services_package_b").First(x=>x["id"]=="b15_s03_clean")["name"])){Step("b15_s03_clean");Input("ms_supply_contact_cleaner");}fact.Faults.Remove(fault.Key);}
                    else if(fault.Value=="contact_fault"&&new[]{"wire","capacitor"}.Contains(fault.Key)){Step("b15_s04_contact_repair");Input("ms_supply_solder_pack");Input(fault.Key=="wire"?"ms_part_wire_bundle":"ms_part_capacitor");p.Tools["ref_welder"]=1;fact.Faults.Remove(fault.Key);fact.ElectricalRevision++;}
                    else{impossible=true;break;}
                }
                if(impossible)continue;
                foreach(var slot in slots.Where(x=>!g.Slots.TryGetValue(x.Key,out int old)||old!=x.Value)){Step(slot.Key=="interface"?"b15_s06_install_simple":"b15_s07_install_precision");p.Tools["ref_screwdriver"]=1;p.Parts++;if(p.AddedCost.HasValue&&source.MusicTrade.Costs.TryGetValue(slot.Value,out var cost)&&cost.Known)p.AddedCost+=cost.Amount;else p.AddedCost=null;}
                g.Slots=new(slots);
                if(!PackageB.CalibrationValid(a,s.Economy.Business,entity)){Step("b15_s08_calibrate");p.Tools["ref_magnifier"]=1;a.Calibrations[entity+":basic"]=new(){Entity=entity,Scope="basic",Passed=true,Hash=PackageB.MeasurementHash(a,s.Economy.Business,entity,"basic")};}
                p.Block=PackageD.Eligible(s,profile,entity,zone,true,false,0,0,false);if(p.Block!=null)continue;
                if(p.Energy>0){p.Tools["energy:E"]=p.Energy;p.AddedCost=null;}
                p.Meets=true;p.Stats=PackageB.Stats(a,s.Economy.Business,entity);var missing=p.Inputs.Concat(p.Tools).Where(x=>stock.GetValueOrDefault(x.Key)<x.Value).Select(x=>x.Key+"×"+Math.Max(0,x.Value-stock.GetValueOrDefault(x.Key))).ToArray();
                p.Executable=missing.Length==0;p.Block=missing.Length==0?null:"缺少实际输入/工具："+string.Join("、",missing);
                if(!best.Meets||p.Parts<best.Parts||p.Parts==best.Parts&&p.Seconds<best.Seconds)best=p;
            }
            if(!best.Meets)best.Block="现有已知条件未找到合格方案；可在维护页核对其他部件，不建议盲目满配";return best;
        }
    }
}
