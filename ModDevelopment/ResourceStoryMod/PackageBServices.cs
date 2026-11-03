using System;
using System.Collections.Generic;
using System.Linq;

namespace ResourceStoryMod.Story
{
    public static partial class PackageB
    {
        public static EquipmentService Authorize(StoryState state,string id,string actor,int entity,string snapshot)
        {
            var a=state.Workshop.Advanced;var b=state.Economy.Business;
            Need(StoryContent.ValidId(id) && !string.IsNullOrWhiteSpace(snapshot),"授权编号或原件快照无效");
            if(a.Services.TryGetValue(id,out var existing)){Need(existing.Host==entity && existing.Actor==actor,"授权编号已经用于其他原件");return existing;}
            Need(actor=="player" || MemberBusiness.ValidActor(actor) || actor=="outside"&&PackageDUse.CanAuthorize(state,entity),"物主无效");
            Need(!a.Services.Values.Any(x=>!x.Terminal && (x.Host==entity || (actor!="player" && x.Actor==actor && (actor!="outside" || x.Delivery==null&&x.Status!="return_ready")))),"这个成员或原件已有未结授权");
            Need(!MemberBusiness.ValidActor(actor) || a.Services.Values.Count(x=>!x.Terminal && MemberBusiness.ValidActor(x.Actor))<5,"五个成员保管位已满");
            var f=a.Facts[entity];Need(f.Owner==actor,"宿主不属于授权物主");
            Need(PackageBCatalog.Rows("equipment_package_b").Any(x=>x["id"]==f.Sku),"剧情原件不在包2可维护宿主范围");
            var g=MemberBusiness.Register(b,entity,f.Sku,actor);Need(g.Owner==actor,"乐器档案物主不一致");
            var s=new EquipmentService{Id=id,Actor=actor,Host=entity,HostSnapshot=snapshot,InitialSlots=new(g.Slots),EffectiveSlots=new(g.Slots)};
            a.Services.Add(id,s);g.Custody="service:"+id;return s;
        }
        public static bool Compatible(string host,string sku,string slot)
            =>PackageBCatalog.Rows("module_effects").Any(x=>x["host_id"]==host && x["id"]==sku && x["slot"]==slot && x["effect_status"]=="B_enabled") || PackageBCatalog.Rows("internal_repair_slots").Any(x=>x["host_id"]==host && x["accepted_sku"]==sku && x["slot"]==slot);
        public static string InspectionHash(PackageBState a,MemberBusinessState b,int entity,string scope,Dictionary<string,int> projection)
        {
            var f=a.Facts[entity];string facts=scope=="detailed"?FactsHash(f):string.Join(";",f.Faults.Where(x=>x.Key=="surface" || x.Key=="support" || x.Key=="body" || x.Value=="dirty_oxidized").OrderBy(x=>x.Key).Select(x=>x.Key+"="+x.Value));
            return Hash(facts+"|"+string.Join(";",projection.OrderBy(x=>x.Key).Select(x=>x.Key+":"+x.Value+":"+(scope=="detailed"?FactsHash(a.Facts[x.Value]):""))));
        }
        public static bool Known(PackageBState a,int entity,string domain)=>a.Inspections.Values.Any(x=>x.Entity==entity && x.KnownFaults.TryGetValue(domain,out string fault) && a.Facts[entity].Faults.GetValueOrDefault(domain)==fault);
        public static WorkshopSpec ServiceSpec(StoryState state,string serviceId,string kind,string domain,string slot,int component,string scope)
        {
            var a=state.Workshop.Advanced;var s=a.Services[serviceId];var f=a.Facts[s.Host];var b=state.Economy.Business;
            if(s.Actor=="outside")PackageDUse.CheckStep(state,s,kind,slot,component);
            Need(!s.Terminal && s.Delivery==null && s.Status!="return_ready","授权已结束或正在归还");
            Need(!s.Steps.Any(x=>x.Status=="running"),"当前授权已有加工中的子项");
            var row=PackageBCatalog.Rows("services_package_b").FirstOrDefault(x=>x["id"]==kind)??throw new InvalidOperationException("未知服务");
            var spec=new WorkshopSpec{Recipe=kind,Purpose="service",Service=serviceId,Station=PackageBCatalog.Station(row["station"]),DurationMs=PackageBCatalog.Number(row["seconds_draft"])*1000,Energy=PackageBCatalog.Number(row["energy_draft"]),Quantity=0};
            if(kind.EndsWith("inspect_basic") || kind.EndsWith("inspect_detailed")){
                string inspectScope=kind.EndsWith("inspect_basic")?"basic":"detailed";
                string hash=InspectionHash(a,b,s.Host,inspectScope,s.EffectiveSlots);
                Need(!a.Inspections.TryGetValue(s.Host+":"+inspectScope,out var prior) || prior.Hash!=hash,"同范围结果已知，可免费查看账册");
            }else if(kind.EndsWith("_clean")){
                Need(f.Faults.Any(x=>x.Value=="dirty_oxidized" && Known(a,s.Host,x.Key)),"没有已知污染需要清洁");spec.Inputs["ms_supply_contact_cleaner"]=1;
            }else if(kind.EndsWith("contact_repair")){
                Need((domain=="wire" || domain=="capacitor") && Known(a,s.Host,domain) && f.Faults.GetValueOrDefault(domain)=="contact_fault","请选择已查明的线束或电容故障");
                spec.Inputs["ms_supply_solder_pack"]=1;spec.Inputs[domain=="wire"?"ms_part_wire_bundle":"ms_part_capacitor"]=1;spec.Tools["ref_welder"]=1;
            }else if(kind.EndsWith("pad_repair")){
                Need(f.Sku=="ms_gear_drum_practice_pad" && (domain=="surface" || domain=="support") && Known(a,s.Host,domain) && f.Faults.GetValueOrDefault(domain)=="structural_fault","请选择已查明的鼓垫表面或支撑损坏");
                spec.Inputs[domain=="surface"?"ms_material_acoustic_mesh":"ref_metal_ingot"]=1;
            }else if(kind.EndsWith("install_simple") || kind.EndsWith("install_precision")){
                if(component==0){Need(slot!=null && s.EffectiveSlots.ContainsKey(slot),"该槽没有实际可拆部件，工厂封装不产生松散件");spec.Tools["ref_screwdriver"]=1;return spec;}
                Need(component>0 && a.Facts.TryGetValue(component,out var cf),"请先选择实际配件");var part=a.Facts[component];
                Need(Compatible(f.Sku,part.Sku,slot),"成员、具体宿主或槽位不兼容");
                Need(part.Owner==s.Actor || part.Owner=="player","本次授权不能使用其他人的配件");
                if(slot.StartsWith("service."))Need(Known(a,s.Host,slot.Replace("service.","")) && f.Faults.ContainsKey(slot.Replace("service.","")),"内部换件只用于已经查明的匹配故障");
                if(s.EffectiveSlots.TryGetValue(slot,out int old))Need(old!=component && (a.Facts[old].Sku!=part.Sku || PackageBCatalog.Rank(part.Grade)>PackageBCatalog.Rank(a.Facts[old].Grade) || a.Facts[old].Faults.Count>0),"现有配件已满足相同规格，无需重复安装");
                Need(!s.EffectiveSlots.Values.Contains(component),"这个实体已装在当前宿主上");
                Need(kind.EndsWith("install_simple")?(slot=="interface"):(slot!="interface"),"这个槽使用另一种安装工序");
                spec.Tools[part.Sku]=1;spec.Tools["ref_screwdriver"]=1;
            }else if(kind.EndsWith("calibrate")){
                Need(new[]{"basic","passive","precision"}.Contains(scope),"校准范围无效");
                Need((f.Sku=="ms_gear_drum_practice_pad")== (scope=="passive"),"鼓垫使用被动校准，电声器材使用基础或精密校准");
                string hash=MeasurementHash(a,b,s.Host,scope,s.EffectiveSlots);
                Need(!a.Calibrations.TryGetValue(s.Host+":"+scope,out var old) || old.Hash!=hash,"当前配置已有测量结果，可免费查看；修理或改配置后再测");
                Need(!ActiveFaults(f,s.EffectiveSlots).Any(x=>(x.Value=="contact_fault" || x.Value=="structural_fault") && Known(a,s.Host,x.Key)),"已知故障须先修理；未知故障的测量可能不通过");
            }
            return spec;
        }
        public static void CompleteStep(StoryState state,EquipmentService s,ServiceStep step,int machine,int day)
        {
            if(step.Status=="complete")return;Need(step.Status=="running","子工序不可完成");
            var a=state.Workshop.Advanced;var b=state.Economy.Business;var f=a.Facts[s.Host];string k=step.Kind;
            if(k.EndsWith("inspect_basic") || k.EndsWith("inspect_detailed")){
                string scope=k.EndsWith("inspect_basic")?"basic":"detailed";var known=new HashSet<string>(scope=="detailed"?f.Faults.Keys:f.Faults.Where(x=>x.Value=="dirty_oxidized" || x.Key=="surface" || x.Key=="support" || x.Key=="body").Select(x=>x.Key));
                a.Inspections[s.Host+":"+scope]=new EquipmentInspection{Entity=s.Host,Scope=scope,Hash=InspectionHash(a,b,s.Host,scope,s.EffectiveSlots),KnownDomains=known,KnownFaults=f.Faults.Where(x=>known.Contains(x.Key)).ToDictionary(x=>x.Key,x=>x.Value)};
            }else if(k.EndsWith("_clean")){
                foreach(string d in f.Faults.Where(x=>x.Value=="dirty_oxidized").Select(x=>x.Key).ToArray())f.Faults.Remove(d);f.Revision++;s.Improvements.Add(step.Id);
            }else if(k.EndsWith("contact_repair") || k.EndsWith("pad_repair")){
                f.Faults.Remove(step.Domain);f.Revision++;if(k.EndsWith("pad_repair"))f.MechanicalRevision++;else f.ElectricalRevision++;s.Improvements.Add(step.Id);
            }else if(k.EndsWith("install_simple") || k.EndsWith("install_precision")){
                if(s.EffectiveSlots.TryGetValue(step.Slot,out int old) && !s.Held.ContainsKey(old)){
                    var c=b.Components[old];s.Held[old]=new ServiceCustodyItem{Owner=c.Owner,Item=new WorkshopEntity{Entity=old,ItemId=c.ItemId,Reference=c.ItemId,Units=1,Tool=true,Grade=a.Facts[old].Grade,Snapshot=c.Snapshot}};
                }
                if(step.Component==0)s.EffectiveSlots.Remove(step.Slot);else s.EffectiveSlots[step.Slot]=step.Component;
            }else if(k.EndsWith("calibrate")){
                bool passed=!ActiveFaults(f,s.EffectiveSlots).Any(x=>x.Value=="contact_fault" || x.Value=="structural_fault") && s.EffectiveSlots.Values.All(id=>!a.Facts[id].Faults.Values.Any(x=>x=="contact_fault" || x=="structural_fault"));
                string proof=step.Scope=="precision"?a.References.GetValueOrDefault(machine)?.SelfCheckHash:null;
                a.Calibrations[s.Host+":"+step.Scope]=new EquipmentCalibration{Entity=s.Host,Scope=step.Scope,Hash=MeasurementHash(a,b,s.Host,step.Scope,s.EffectiveSlots),Passed=passed,Machine=machine,MeasurementProof=proof};
                a.CalibrationHistory[step.Id]=a.Calibrations[s.Host+":"+step.Scope];
                if(passed && s.Actor=="player" && step.Scope!="precision")state.Workshop.BasicCalibrationReceipts.Add(s.Host);
            }
            s.MaterialQuote=checked(s.MaterialQuote+step.ConsumableQuote);step.Status="complete";s.Status="ready";a.Ledger[step.Id]=new WorkshopLedger{Id=step.Id,Kind=step.Kind,Actor=s.Actor,Entity=s.Host,Service=s.Id,Day=day,Result=k.EndsWith("calibrate")?(a.Calibrations[s.Host+":"+step.Scope].Passed?"测量通过":"测量未通过；不揭示隐蔽故障"):"完成"};
        }
        public static int Suggested(EquipmentService s)
        {
            if(s.Actor=="player")return 0;
            return checked(s.MaterialQuote+s.Steps.Where(x=>x.Status=="complete").Sum(x=>x.Labor)+s.Held.Values.Where(x=>x.Owner=="player" && (s.EffectiveSlots.Values.Contains(x.Item.Entity) || x.DeliverToMember)).Sum(x=>x.SuggestedPrice));
        }
        public static bool Deliver(StoryState state,string serviceId,string receipt,int price,int day)
        {
            var a=state.Workshop.Advanced;var s=a.Services[serviceId];var b=state.Economy.Business;
            Need(price>=0 && StoryContent.ValidId(receipt),"交收价格或回执无效");
            if(s.Delivery!=null){Need(s.Delivery==receipt && s.Paid==price,"交收号或价格与已完成记录不同");return false;}
            Need(!s.Terminal && s.Status!="return_ready" && s.Steps.Any(x=>x.Status=="complete") && s.Steps.All(x=>x.Status!="running"),"请先完成至少一项实际工作");
            if(MemberBusiness.ValidActor(s.Actor))Need(state.Economy.Wallets[s.Actor].Balance>=price,"余额不足，可以降价、等待下次来访或取消");else if(s.Actor=="player")Need(price==0,"自有工作不产生人工收入");
            if(s.Actor=="outside")PackageDUse.CheckDelivery(state,s,price,day);
            var g=b.Instruments[s.Host];
            foreach(var old in g.Slots.ToArray())if(!s.EffectiveSlots.Values.Contains(old.Value)){var c=b.Components[old.Value];c.Location="loose";c.Instrument=0;c.Slot=null;}
            foreach(var p in s.EffectiveSlots){
                if(!b.Components.TryGetValue(p.Value,out var c)){
                    var held=s.Held[p.Value];c=new MusicComponent{EntityId=p.Value,ItemId=held.Item.ItemId,Snapshot=held.Item.Snapshot};b.Components[p.Value]=c;
                }
                c.Owner=s.Actor;c.Location="installed";c.Instrument=s.Host;c.Slot=p.Key;a.Facts[p.Value].Owner=s.Actor;
            }
            g.Slots=new(s.EffectiveSlots);g.Revision++;g.Custody=s.Actor;g.Stats=Stats(a,b,s.Host);
            if(MemberBusiness.ValidActor(s.Actor))state.Economy.Wallets[s.Actor].Balance-=price;
            foreach(var h in s.Held.Values.ToArray()){
                int id=h.Item.Entity;if(s.EffectiveSlots.Values.Contains(id)){s.Held.Remove(id);continue;}
                if(s.Actor!="player" && (h.Owner==s.Actor || h.DeliverToMember)){a.Facts[id].Owner=s.Actor;if(s.Actor!="outside")state.Economy.OwnedItems[id]=Owned(s.Actor,h.Item);s.Held.Remove(id);}
            }
            if(MemberBusiness.ValidActor(s.Actor))state.Economy.OwnedItems[s.Host]=new MemberOwnedItem{ActorId=s.Actor,EntityId=s.Host,ItemId=g.ItemId,NativeSnapshot=s.HostSnapshot};
            s.Delivery=receipt;s.Paid=price;s.Status=s.Held.Count==0?"delivered":"delivered_return";
            a.Ledger[receipt]=new WorkshopLedger{Id=receipt,Actor=s.Actor,Entity=s.Host,Service=s.Id,Kind="delivery",Day=day,Result="交收 "+price};
            if(s.Actor=="outside")PackageDUse.Delivery(state,s,receipt,price,day);else Fulfill(state,s.Actor,s.Host);
            if(s.Actor=="player"&&state.MusicTrade!=null)PackageDNext.Case(state,"case_"+receipt,s.Host,day,"service",receipt,"自有器材按真实工序完成整备；保留当前配置与实际投入。");return true;
        }
        public static void CancelService(StoryState state,string id,int day)
        {
            var a=state.Workshop.Advanced;var s=a.Services[id];if(s.Status=="cancelled" || s.Status=="return_ready")return;
            Need(s.Delivery==null,"已经交收的工作不能取消");Need(s.Steps.All(x=>x.Status!="running"),"先停止并取回加工中的子工序");
            if(s.Actor=="outside")PackageDUse.CheckCancel(state,id);
            var g=state.Economy.Business.Instruments[s.Host];s.EffectiveSlots=new(s.InitialSlots);g.Custody=s.Actor;
            foreach(var h in s.Held.Values.ToArray()){
                if(s.InitialSlots.Values.Contains(h.Item.Entity)){s.Held.Remove(h.Item.Entity);continue;}
                if(h.Owner==s.Actor && MemberBusiness.ValidActor(s.Actor)){state.Economy.OwnedItems[h.Item.Entity]=Owned(s.Actor,h.Item);s.Held.Remove(h.Item.Entity);}
            }
            if(MemberBusiness.ValidActor(s.Actor))state.Economy.OwnedItems[s.Host]=new MemberOwnedItem{ActorId=s.Actor,EntityId=s.Host,ItemId=g.ItemId,NativeSnapshot=s.HostSnapshot};
            foreach(var step in s.Steps.Where(x=>x.Kind.EndsWith("calibrate") && x.Status=="complete")){
                if(a.Calibrations.TryGetValue(s.Host+":"+step.Scope,out var c) && c.Hash!=MeasurementHash(a,state.Economy.Business,s.Host,step.Scope)){
                    string hash=MeasurementHash(a,state.Economy.Business,s.Host,step.Scope);var previous=a.CalibrationHistory.Values.LastOrDefault(x=>x.Entity==s.Host && x.Scope==step.Scope && x.Hash==hash);if(previous==null)a.Calibrations.Remove(s.Host+":"+step.Scope);else a.Calibrations[s.Host+":"+step.Scope]=previous;
                }
            }
            s.Status=s.Held.Count==0?"cancelled":"return_ready";
            a.Ledger[id+"_cancel"]=new WorkshopLedger{Id=id+"_cancel",Kind="cancel",Actor=s.Actor,Entity=s.Host,Service=id,Day=day,Result="原件归还，已完成清洁维修保留；耗材不退款"};
        }
        public static MemberOwnedItem Owned(string actor,WorkshopEntity e)=>new MemberOwnedItem{ActorId=actor,EntityId=e.Entity,ItemId=e.ItemId,NativeSnapshot=e.Snapshot};
        public static bool Fulfill(StoryState state,string actor,int host)
        {
            if(actor=="player")return false;var a=state.Workshop.Advanced;var b=state.Economy.Business;var g=b.Instruments[host];
            if(g.Owner!=actor || !state.Economy.OwnedItems.ContainsKey(host) || !b.Needs.Values.Any(x=>x.Actor==actor && x.FulfilledEntity>0))return false;
            bool changed=false;
            foreach(var d in PackageBCatalog.Rows("member_demands").Where(x=>x["tier"]=="advanced" && x["actor"]==actor && x["host_id"]==g.ItemId)){
                if(a.FulfilledDemands.Contains(d["id"]) || !CalibrationValid(a,b,host,d["calibration"]) || !Healthy(a,b,host))continue;
                var stats=Stats(a,b,host);if(stats["noiseControl"]<PackageBCatalog.Number(d["noise_min"]) || stats["stability"]<PackageBCatalog.Number(d["stability_min"]) || stats["portability"]<PackageBCatalog.Number(d["portability_min"]))continue;
                bool ready=true;foreach(string sku in PackageBCatalog.Split(d["required_components"])){
                    if(sku=="ms_item_balanced_cable"){if(!state.Economy.OwnedItems.Values.Any(x=>x.ActorId==actor && x.ItemId==sku))ready=false;}
                    else if(!g.Slots.Values.Any(id=>a.Facts[id].Sku==sku && PackageBCatalog.Rank(a.Facts[id].Grade)>=PackageBCatalog.Rank(d["minimum_component_grade"])))ready=false;
                }
                if(ready)changed|=a.FulfilledDemands.Add(d["id"]);
            }
            return changed;
        }
    }
}
