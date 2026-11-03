using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageB
    {
        private readonly Core core;
        private readonly NativeStoryHost host;
        private readonly NativeMemberEconomy economy;
        private readonly List<System.Delegate> roots=new();
        internal bool Enabled=>economy.Enabled && host.ReadState?.Workshop?.Advanced!=null;
        private PackageBState A=>host.ReadState.Workshop.Advanced;
        private MemberBusinessState B=>host.ReadState.Economy.Business;
        internal NativePackageB(Core c,NativeStoryHost h,NativeMemberEconomy e){core=c;host=h;economy=e;InstallStorageHooks();InstallSupplyHooks();InstallRepairCounter();foreach(string name in new[]{"MayTarget","CanTarget"})core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(GameItem),name),prefix:new HarmonyLib.HarmonyMethod(typeof(NativePackageB),nameof(RepairMayTarget)));core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(GameItem),"Target"),prefix:new HarmonyLib.HarmonyMethod(typeof(NativePackageB),nameof(RepairTarget)));core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(GameItem),"GetComplexeItemValue"),postfix:new HarmonyLib.HarmonyMethod(typeof(NativePackageB),nameof(RepairComplexValue)));}
        internal void Attach()
        {
            repairSettledDay=-1;
            BindDocuments();
            documentIds.Add(RepairNoteSku);
            core.EnsureChineseFallback();
            foreach(var item in economy.AllPlayerItems().Where(x=>PackageBCatalog.AllIds.Contains(x.identifier)))Ensure(item,"player");
            foreach(var owned in host.Engine.Snapshot().Economy.OwnedItems.Values.Where(x=>PackageBCatalog.AllIds.Contains(x.ItemId)))Ensure(NativeMemberEconomy.Decode(owned.NativeSnapshot),owned.ActorId);
            AttachSupplier();
            AuditLocations();
        }
        internal static string Grade(GameItem item)=>PackageBCatalog.Grades.Reverse().FirstOrDefault(g=>item.IsTag("ms.grade."+g))??"ordinary";
        internal void TrackInventoryFacts()
        {
            if(!Enabled)return;var snapshot=host.Engine.Snapshot();var known=snapshot.Workshop.Advanced.Facts;
            var missing=economy.AllPlayerItems().Where(x=>PackageBCatalog.AllIds.Contains(x.identifier) && !known.ContainsKey(x.uniqueId)).Select(x=>(Entity:x.uniqueId,Sku:x.identifier,Quality:Grade(x),Owner:snapshot.Economy.Business.Instruments.GetValueOrDefault(x.uniqueId)?.Owner??snapshot.Economy.Business.Components.GetValueOrDefault(x.uniqueId)?.Owner??"player")).ToArray();
            if(missing.Length>0)host.Engine.EditPackageB(s=>{foreach(var item in missing)PackageB.Fact(s.Workshop.Advanced,item.Entity,item.Sku,item.Owner,item.Quality);});
        }
        internal static void TagGrade(GameItem item,string grade){foreach(string g in PackageBCatalog.Grades)if(g!=grade)item.DisableTag("ms.grade."+g,false);item.EnableTag("ms.grade."+grade,false);}
        internal EquipmentFact Ensure(GameItem item,string owner="player",string origin="legacy_unknown")
        {
            if(A.Facts.TryGetValue(item.uniqueId,out var existing)){if(existing.Sku!=item.identifier)throw new InvalidDataException("实体事实与实物型号冲突");return existing;}
            host.Engine.EditPackageB(s=>PackageB.Fact(s.Workshop.Advanced,item.uniqueId,item.identifier,owner,Grade(item),origin));return A.Facts[item.uniqueId];
        }
        internal void ApplyFacts(GameItem item,string grade,string owner,string origin,string fault=null,string domain=null,bool used=false)
        {
            TagGrade(item,grade);
            host.Engine.EditPackageB(s=>{var f=PackageB.Fact(s.Workshop.Advanced,item.uniqueId,item.identifier,owner,grade,origin);f.Grade=grade;f.Owner=owner;f.Origin=origin;f.Used=used;if(fault!=null && fault!="normal")f.Faults[domain??"body"]=fault;});
            Present(item);
        }
        internal void ApplyCatalog(GameItem item)
        {
            var row=PackageBCatalog.Item(item.identifier);
            TagGrade(item,"ordinary");item.EnableTag("ms.music",false);
            if(row==null)return;
            item.EnableTag("ms.kind."+(row["group"]=="raw_supply"?"material":row["group"]),false);
            foreach(string z in PackageBCatalog.Split(row["source_zones"]))item.EnableTag("ms.source."+z,false);
            foreach(string z in PackageBCatalog.Split(row["ordinary_retail_zones"]))item.EnableTag("ms.retail."+z,false);
            foreach(string tag in new[]{row["member_tag"],row["instrument_tag"],row["machine_tag"]})if(tag!="")item.EnableTag(tag,false);
        }
        internal bool PlayerOwned(GameItem item)
        {
            if(item==null || !GeneralHelper.IsItemOwned(item) || item.IsTag(MusicCatalog.ProtectedTag))return false;
            return (!A.Facts.TryGetValue(item.uniqueId,out var f) || f.Owner=="player") && (!B.Instruments.TryGetValue(item.uniqueId,out var g) || g.Owner=="player") && (!B.Components.TryGetValue(item.uniqueId,out var c) || c.Owner=="player");
        }
        internal bool Working(int entity,string sku)
        {
            var item=economy.FindPlayerItem(entity);return item!=null && item.identifier==sku && PlayerOwned(item) && economy.PlayerInventories().Any(i=>i.NativeIdentity()==item.parentInventory?.NativeIdentity());
        }
        internal bool ReferenceAvailable(int machine)=>Working(machine,"ms_machine_calibrator") && PackageB.ReferenceValid(A,machine);
        internal int PrecisionMachine()=>A.References.Keys.OrderBy(x=>x).FirstOrDefault(x=>ReferenceAvailable(x) && !Workshop.Busy(host.Engine.Snapshot().Workshop,x) && !host.Engine.Snapshot().Workshop.Jobs.Values.Any(j=>!j.Terminal && j.Spec?.MeasuringMachine==x));
        internal bool MachineBusy(int machine)=>Workshop.Busy(host.Engine.Snapshot().Workshop,machine) || host.Campaign?.MachineBusy(machine)==true || (machine>0 && host.Engine.Snapshot().Workshop.Jobs.Values.Any(j=>!j.Terminal && j.Spec?.MeasuringMachine==machine));
        internal void Present(GameItem item,Dictionary<string,int> projection=null)
        {
            if(!Enabled || (!PackageBCatalog.AllIds.Contains(item.identifier) && item.identifier!="printer") || !A.Facts.ContainsKey(item.uniqueId))return;
            var a=A;var b=B;var f=a.Facts[item.uniqueId];bool gear=b.Instruments.ContainsKey(item.uniqueId);
            int value=gear?PackageB.Value(a,b,item.uniqueId,projection):PackageB.LeafValue(a,item.uniqueId,item.identifier);
            if(item.GetCurrentValue()!=value)item.SetValue(value);TagGrade(item,f.Grade);
            bool knownGrade=f.Origin.StartsWith("manufacture:") || f.Origin.StartsWith("native_print:") || a.Inspections.Values.Any(x=>x.Entity==item.uniqueId);
            item.customText=(knownGrade?PackageBCatalog.GradeName(f.Grade):"品质待查")+" · "+(f.Used?"二手 · ":"")+"原件 #"+item.uniqueId+" · 参考 "+value;
            if(gear){var st=PackageB.Stats(a,b,item.uniqueId,projection);item.customText+="\n降噪/稳定/响应/便携 "+string.Join("/",new[]{st["noiseControl"],st["stability"],st["response"],st["portability"]});}
            var known=a.Inspections.Values.Where(x=>x.Entity==item.uniqueId).ToArray();
            var detail=known.LastOrDefault(x=>x.Scope=="detailed");
            item.customText+="\n"+(detail!=null?"已查明："+(detail.KnownFaults.Count==0?"上次检测正常":string.Join("、",detail.KnownFaults.Select(x=>x.Key+":"+x.Value)))+"；配置或故障变化后需复查":"内部状况未知；可在工坊检查");
            economy.Trade.PresentSource(item);
            if(TryRepairCounterValue(item,out int counterValue))item.SetValue(counterValue);
            // The native grid paints customText over the item. Slot descriptions
            // belong in the window below, where they cannot cover adjacent cells.
            if(gear&&item.parentInventory!=null&&repairInventories.ContainsKey(item.parentInventory.NativeIdentity().ToInt64()))item.customText="";
            if(gear&&RepairCounterIncoming(item)&&item.parentInventory?.NativeIdentity()==EmporiumEntry.Instance.frontInvinvElement.NativeIdentity()){
                item.customText=item.name+"\n收购参考 "+counterValue;
                foreach(var part in item.FindAllChildItems())part.customText="";
            }
            NativeP19Presentation.Detail(item);
        }
        internal string Authorize(string id,string actor,int entity)
        {
            if(A.Services.TryGetValue(id,out var prior)){if(prior.Actor!=actor || prior.Host!=entity)throw new InvalidOperationException("授权编号与原交接不符");return id;}
            if(actor!="player" && economy.Business.CurrentActor!=actor)throw new InvalidOperationException("需要成员实际来访并确认授权");
            var state=host.Engine.Snapshot();GameItem item;GameInventory source=null;
            if(actor=="player"){item=economy.FindPlayerItem(entity)??throw new InvalidOperationException("玩家没有该原件");if(!PlayerOwned(item))throw new InvalidOperationException("不是玩家自有器材");source=item.parentInventory;}
            else {if(!state.Economy.OwnedItems.TryGetValue(entity,out var owned) || owned.ActorId!=actor)throw new InvalidOperationException("成员没有持有该原件");item=NativeMemberEconomy.Decode(owned.NativeSnapshot);}
            if(item.IsTag(MusicCatalog.ProtectedTag))throw new InvalidOperationException("剧情原件不在包2服务范围");
            Ensure(item,actor);string snapshot=NativeMemberEconomy.Encode(item);bool removed=false;
            economy.MutateNative(()=>{if(source!=null){removed=source.Expel(item);if(!removed)throw new IOException("无法移交原件");}
                host.Engine.EditPackageB(s=>{PackageB.Authorize(s,id,actor,entity,snapshot);s.Economy.OwnedItems.Remove(entity);});
            },()=>{if(removed && !source.UncheckedAccept(item))throw new IOException("原件回退失败");});return id;
        }
        internal void AddCable(string service,int entity)
        {
            var svc=A.Services[service];var item=economy.FindPlayerItem(entity)??throw new InvalidOperationException("请选择玩家持有的实际线缆");
            if(svc.Actor!="ms_hotaru" || item.identifier!="ms_item_balanced_cable" || !PlayerOwned(item) || svc.Delivery!=null)throw new InvalidOperationException("该授权不能交付此线缆");
            Ensure(item);var source=item.parentInventory;bool removed=false;int value=PackageB.LeafValue(A,entity);
            economy.MutateNative(()=>{removed=source.Expel(item);if(!removed)throw new IOException("线缆移交失败");host.Engine.EditPackageB(s=>s.Workshop.Advanced.Services[service].Held.Add(entity,new ServiceCustodyItem{Item=NativeWorkshop.Store(item),Owner="player",SuggestedPrice=value,DeliverToMember=true}));},()=>{if(removed)source.UncheckedAccept(item);});
        }
        internal void StartStep(string service,string job,string kind,int machine=0,string domain=null,string slot=null,int component=0,string scope="basic",Dictionary<string,int[]> materialChoices=null)
        {
            if(PackageB.InstantMeasurement(kind)){MeasureRepair(service,kind,machine,scope);return;}
            var svc=A.Services[service];if(svc.Delivery!=null)throw new InvalidOperationException("已经结清的授权不能增加工作");
            var priorStep=svc.Steps.FirstOrDefault(x=>x.Id==job);if(priorStep!=null){if(priorStep.Kind!=kind || priorStep.Component!=component || priorStep.Domain!=domain || priorStep.Slot!=slot || priorStep.Scope!=scope)throw new InvalidOperationException("子工序号与原操作不符");return;}
            if(materialChoices==null && serviceMaterialChoices.TryGetValue(service,out var chosen))materialChoices=chosen;
            var custody=svc.Held.Values.Where(x=>!svc.EffectiveSlots.Values.Contains(x.Item.Entity) && !x.DeliverToMember).Select(x=>NativeMemberEconomy.Decode(x.Item.Snapshot)).ToList();
            GameItem part=null;
            if(component>0){
                part=economy.FindPlayerItem(component)??custody.FirstOrDefault(x=>x.uniqueId==component);
                if(part==null && host.Engine.Snapshot().Economy.OwnedItems.TryGetValue(component,out var owned) && owned.ActorId==svc.Actor)part=NativeMemberEconomy.Decode(owned.NativeSnapshot);
                if(part==null)throw new InvalidOperationException("所选配件原件不在本次可用位置");
                string owner=host.Engine.Snapshot().Economy.OwnedItems.GetValueOrDefault(component)?.ActorId??svc.Held.GetValueOrDefault(component)?.Owner??"player";Ensure(part,owner);
                if(!custody.Any(x=>x.uniqueId==component) && part.parentInventory==null)custody.Add(part);
            }
            var state=host.Engine.Snapshot();var spec=PackageB.ServiceSpec(state,service,kind,domain,slot,component,scope);
            if(materialChoices!=null)foreach(var request in materialChoices.Where(x=>spec.Inputs.ContainsKey(x.Key) || spec.Tools.ContainsKey(x.Key))){
                foreach(int entity in request.Value){if(economy.FindPlayerItem(entity)!=null || custody.Any(x=>x.uniqueId==entity))continue;
                    if(!state.Economy.OwnedItems.TryGetValue(entity,out var supplied) || supplied.ActorId!=svc.Actor || supplied.ItemId!=economy.Workshop.NativeId(request.Key))throw new InvalidOperationException("指定耗材不属于本人或型号不符");
                    var provided=NativeMemberEconomy.Decode(supplied.NativeSnapshot);Ensure(provided,svc.Actor);custody.Add(provided);
                }
            }
            if(spec.Station=="counter")machine=0;
            if(kind.EndsWith("calibrate") && scope=="precision"){
                if(!ReferenceAvailable(machine))throw new InvalidOperationException("精密测量需要当前有效参考自检");spec.MeasuringMachine=machine;
            }
            var choices=materialChoices==null?new Dictionary<string,int[]>():new Dictionary<string,int[]>(materialChoices);if(part!=null)choices[part.identifier]=new[]{component};
            int labor=PackageBCatalog.Number(PackageBCatalog.Rows("services_package_b").First(x=>x["id"]==kind)["labor_reference"]);
            int partPrice=part==null?0:PackageB.LeafValue(A,part.uniqueId,part.identifier);
            economy.Workshop.StartPrepared(job,spec,machine,choices,custody,(s,stored)=>{
                var v=s.Workshop.Advanced.Services[service];
                foreach(var e in stored){v.Held.Remove(e.Entity);s.Economy.OwnedItems.Remove(e.Entity);var f=PackageB.Fact(s.Workshop.Advanced,e.Entity,e.ItemId,"player",e.Grade);}
                int materials=stored.Where(x=>!x.Tool && s.Workshop.Advanced.Facts.GetValueOrDefault(x.Entity)?.Owner=="player").Sum(x=>{var item=NativeMemberEconomy.Decode(x.Snapshot);return (PackageBCatalog.AllIds.Contains(item.identifier)?PackageB.LeafValue(s.Workshop.Advanced,item.uniqueId,item.identifier):Math.Max(0,checked((int)item.unitValue)))*x.Units;});
                v.Steps.Add(new ServiceStep{Id=job,Kind=kind,Domain=domain,Slot=slot,Component=component,ComponentOwner=part==null?null:s.Workshop.Advanced.Facts[component].Owner,Labor=labor,PartsQuote=partPrice,ConsumableQuote=materials,Scope=scope,FactsAtStart=PackageB.FactsHash(s.Workshop.Advanced.Facts[v.Host])});v.Status="working";
                PackageB.BindRepairDay(s.Workshop.Jobs[job],host.Day,slot?.StartsWith("service.")==true?2:1);
            });
        }
        internal void Deliver(string service,int price,string receipt)
        {
            var svc=A.Services[service];
            if(svc.Actor=="outside"){economy.Trade.DeliverExternal(service,price,receipt);return;}
            if(svc.Delivery!=null){if(svc.Delivery==receipt && svc.Paid==price)return;throw new InvalidOperationException("已经按另一笔交收结清");}
            if(svc.Actor!="player" && economy.Business.CurrentActor!=svc.Actor)throw new InvalidOperationException("成员须到店后货到付款");
            // Reject unaffordable or invalid receipts before touching any native item.
            PackageB.Deliver(host.Engine.Snapshot(),service,receipt,price,host.Day);
            GameItem item=NativeMemberEconomy.Decode(svc.HostSnapshot);Present(item,svc.EffectiveSlots);string snapshot=NativeMemberEconomy.Encode(item);
            int before=PlayerStore.instance.GetCash();
            try{economy.MutateNative(()=>{DematerializeRepair(service);host.Engine.EditPackageB(s=>{s.Workshop.Advanced.Services[service].HostSnapshot=snapshot;PackageB.Deliver(s,service,receipt,price,host.Day);});
                if(svc.Actor!="player")PlayerStore.instance.ModCash(price,false);
                if(PlayerStore.instance.GetCash()!=before+(svc.Actor=="player"?0:price))throw new IOException("实际现金交收不一致");
            },()=>{});}catch{MaterializeRepair(service);RefreshRepairViews();throw;}
            if(svc.Actor=="player")ReturnPlayerHost(service);
        }
        private void ReturnPlayerHost(string service)
        {
            var svc=A.Services[service];if(svc.Actor!="player" || svc.Host==0)return;
            var item=NativeMemberEconomy.Decode(svc.HostSnapshot);if(economy.FindPlayerItem(item.uniqueId)!=null)return;
            bool placed=false;economy.MutateNative(()=>{EmporiumEntry.Instance.TryAddToPlayerInv(item);placed=economy.FindPlayerItem(item.uniqueId)!=null;
                host.Engine.EditPackageB(s=>{var v=s.Workshop.Advanced.Services[service];if(!placed){v.Held[item.uniqueId]=new ServiceCustodyItem{Owner="player",Item=NativeWorkshop.Store(item)};v.Status=v.Delivery==null?"return_ready":"delivered_return";}});
            },()=>{if(placed)item.parentInventory.Expel(item);});
        }
        internal void Cancel(string service)
        {
            foreach(var step in A.Services[service].Steps.Where(x=>x.Status=="running").ToArray()){
                economy.Workshop.Cancel(step.Id);
            }
            if(A.Services[service].Actor=="outside"){economy.Trade.CancelExternal(service);return;}
            var svc=A.Services[service];var item=NativeMemberEconomy.Decode(svc.HostSnapshot);Present(item,svc.InitialSlots);string snapshot=NativeMemberEconomy.Encode(item);
            economy.MutateNative(()=>{DematerializeRepair(service);host.Engine.EditPackageB(s=>{s.Workshop.Advanced.Services[service].HostSnapshot=snapshot;PackageB.CancelService(s,service,host.Day);});},()=>{});
            if(svc.Actor=="player")ReturnPlayerHost(service);
        }
        internal bool Claim(string service,int entity)
        {
            var svc=A.Services[service];if(!svc.Held.TryGetValue(entity,out var h) || h.Owner!="player" || svc.EffectiveSlots.Values.Contains(entity))return false;
            if(!svc.Terminal && svc.Status!="return_ready" && svc.Status!="delivered_return")throw new InvalidOperationException("先结清或取消授权再取回垫件");
            if(economy.FindPlayerItem(entity)!=null)throw new InvalidDataException("库存已有相同ID");var item=NativeMemberEconomy.Decode(h.Item.Snapshot);bool placed=false;
            economy.MutateNative(()=>{EmporiumEntry.Instance.TryAddToPlayerInv(item);placed=economy.FindPlayerItem(entity)!=null;if(placed)host.Engine.EditPackageB(s=>{var v=s.Workshop.Advanced.Services[service];v.Held.Remove(entity);if(v.Held.Count==0)v.Status=v.Delivery==null?"cancelled":"delivered";});},()=>{if(placed)item.parentInventory.Expel(item);});return placed;
        }
        internal void FinishCancelledStep(string service,string step)
        {
            var j=host.Engine.Snapshot().Workshop.Jobs[step];var pending=Workshop.Pending(j).ToArray();
            economy.MutateNative(()=>host.Engine.EditPackageB(s=>{var svc=s.Workshop.Advanced.Services[service];foreach(var p in pending){svc.Held[p.Entity]=new ServiceCustodyItem{Owner=s.Workshop.Advanced.Facts.GetValueOrDefault(p.Entity)?.Owner??"player",Item=p};Workshop.Take(s.Workshop,step,p.Entity);}svc.Steps.First(x=>x.Id==step).Status="cancelled";}),()=>{});
        }
        internal void OnJobCompleted(string jobId)
        {
            var j=host.Engine.Snapshot().Workshop.Jobs[jobId];if(j.Status!="ready")return;
            if(j.Spec?.Purpose?.StartsWith("reference_")==true){FinishReferenceJob(jobId);return;}
            if(j.Spec?.Purpose!="service")return;
            host.Engine.EditPackageB(s=>{var svc=s.Workshop.Advanced.Services[j.Spec.Service];var step=svc.Steps.First(x=>x.Id==jobId);
                foreach(var e in Workshop.Pending(s.Workshop.Jobs[jobId]).ToArray()){
                    var partStep=s.Workshop.Advanced.RepairPlans.GetValueOrDefault(jobId)?.Steps.FirstOrDefault(x=>x.Component==e.Entity)??step;
                    if(e.Entity==partStep.Component)svc.Held[e.Entity]=new ServiceCustodyItem{Owner=s.Workshop.Advanced.Facts[e.Entity].Owner,Item=e,SuggestedPrice=partStep.PartsQuote};
                    else svc.Held[e.Entity]=new ServiceCustodyItem{Owner=s.Workshop.Advanced.Facts.GetValueOrDefault(e.Entity)?.Owner??"player",Item=e};Workshop.Take(s.Workshop,jobId,e.Entity);
                }
                if(!Workshop.Pending(s.Workshop.Jobs[jobId]).Any())s.Workshop.Jobs[jobId].Status="claimed";
                PackageB.CompleteStep(s,svc,step,j.Machine,host.Day);
                if(s.Workshop.Advanced.RepairPlans.TryGetValue(jobId,out var plan))foreach(var operation in plan.Steps){svc.Steps.Add(operation);PackageB.CompleteStep(s,svc,operation,j.Machine,host.Day);}
            });
        }
        internal void AuditLocations()
        {
            if(!Enabled)return;var places=new Dictionary<int,string>();var a=A;var b=B;
            void Place(int id,string location){if(places.TryGetValue(id,out string other))throw new InvalidDataException("同一实体有两个活位置 #"+id+" "+other+" / "+location);places[id]=location;}
            foreach(var item in economy.AllPlayerItems())Place(item.uniqueId,"inventory");
            foreach(var item in host.Engine.Snapshot().Economy.OwnedItems.Values)if(!places.ContainsKey(item.EntityId)||GeneralHelper.IsItemOwned(economy.FindPlayerItem(item.EntityId)))Place(item.EntityId,"member:"+item.ActorId);
            foreach(var j in host.Engine.Snapshot().Workshop.Jobs.Values)foreach(var e in j.Inputs.Where(x=>!j.Taken.Contains(x.Entity) && (!j.InputsConsumed || x.Tool)).Concat(j.Outputs.Where(x=>!j.Taken.Contains(x.Entity))))Place(e.Entity,"work:"+j.Id);
            foreach(var s in a.Services.Values.Where(x=>!x.Terminal)){
                if(s.Delivery==null && !s.Held.ContainsKey(s.Host) && !host.Engine.Snapshot().Economy.OwnedItems.ContainsKey(s.Host) && economy.FindPlayerItem(s.Host)==null)Place(s.Host,"service:"+s.Id);
                foreach(var e in s.Held.Keys)if(economy.FindPlayerItem(e)==null)Place(e,"service:"+s.Id);
            }
            foreach(var g in b.Instruments.Values)foreach(int id in g.Slots.Values)if(economy.FindPlayerItem(id)==null&&!a.Services.Values.Any(s=>!s.Terminal && s.Host==g.EntityId && s.Held.ContainsKey(id)))Place(id,"installed:"+g.EntityId);
            foreach(var r in a.References.Values)if(r.Reference>0)Place(r.Reference,"reference:"+r.Machine);
            foreach(var p in a.Prints.Values){foreach(var e in p.Pending)Place(e.Entity,"print:"+p.Id);if(p.Cycles==0 && p.Status is "running" or "paused")foreach(var e in p.Materials)Place(e.Entity,"print_input:"+p.Id);}
        }
        internal void Update(){if(!Enabled)return;UpdateRepair();UpdatePrinting();UpdateSupply();}
        partial void UpdatePrinting();
        partial void UpdateSupply();
        internal static IEnumerable<GameInventory> Inventories(GameItem item)
        {
            var result=new List<GameInventory>();var seen=new HashSet<long>();
            void Visit(GraphNodeStorage node){if(node==null || !seen.Add(node.NativeIdentity().ToInt64()))return;var inv=node.TryCast<GameInventory>();if(inv!=null)result.Add(inv);if(node.children!=null)foreach(var child in node.children)Visit(child);}
            Visit(item.Cast<GraphNodeStorage>());return result;
        }
        internal static bool Place(GameInventory inventory,GameItem item)
        {
            int units=item.unitCount;var slot=inventory?.TryFindOneValidInventorySlot(item);
            return slot!=null && slot.TryAcceptOnce(units)==units;
        }
    }
}
