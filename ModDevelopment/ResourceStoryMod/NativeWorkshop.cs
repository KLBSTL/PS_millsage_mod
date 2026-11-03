using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    internal sealed class NativeWorkshop
    {
        internal static readonly Dictionary<string,string> References=new() {
            {"ref_metal_ingot","metal_ingot"},{"ref_electronics","common_electronic"},{"ref_rare_electronics","rare_electronic"},
            {"ref_wire","wire"},{"ref_print_plastic","printer_plastic"},{"ref_chem","common_chemical"},
            {"ref_screwdriver","screwdriver"},{"ref_wirecutter","wire_cutter"},{"ref_welder","welder"},{"ref_magnifier","magnifier"}
        };
        private readonly Core core;
        private readonly NativeStoryHost host;
        private readonly NativeMemberEconomy economy;
        private readonly List<System.Func<GameItem>> factories=new();
        private readonly Dictionary<string,string> names=new();
        private bool enabled,panel;
        private float previousTime;
        private readonly Dictionary<string,float> accumulatedMs=new();
        private bool automaticClock;
        private StoreUIManager lockedUI;
        private PlayerStore lockedStore;
        private GUIStyle body,button;
        private int page;
        private string tab="recipes",message="";
        private string previewRecipe;
        private string previewMode="ordinary";
        private readonly Dictionary<string,int[]> previewChoices=new();
        private int selectedMachine;
        internal bool Enabled=>enabled && host.ReadState?.Workshop!=null;
        internal bool PanelOpen=>panel;
        internal int RenderedRows {get;private set;}
        internal int TotalRows {get;private set;}
        internal NativeWorkshop(Core c,NativeStoryHost h,NativeMemberEconomy e){core=c;host=h;economy=e;}
        internal void Register(ModItemDirectory directory) {
            foreach(var product in WorkshopCatalog.Products) {
                if(directory.Has(product.Id))continue;
                var p=product;var factory=(System.Func<GameItem>)(Func<GameItem>)(()=>Create(p));factories.Add(factory);
                if(!directory.Add(p.Id,factory))throw new InvalidOperationException("工坊商品登记失败："+p.Id);
            }
        }
        private GameItem Create(WorkshopProduct p) {
            if(p.Kind=="blueprint")return economy.PackageB.CreateChip(p);
            core.EnsureResources();var item=NormalPlayPolicy.IsInspector(p.Id)?Resources.FindObjectsOfTypeAll(NativeTypes.Of<ToolDirectory>())[0].Cast<ToolDirectory>().CreateLabeler():ItemDirectory.CreateEmptyItem(p.Id);item.identifier=p.Id;
            item.name=p.Name;item.unitCount=1;item.unitValue=p.Price;item.unitBaseValue=p.Price;
            item.SetSpriteAndShape("resource_story","item");item.SetShape(new GridShapeBuilder(p.Width,p.Height).SetDataFill(p.Width,p.Height,1).Build());
            item.SetGameItemType(PackageBCatalog.Item(p.Id)?["native_item_type"]??(p.Kind=="machine"?"MACHINE":"MATERIAL"));
            if(NormalPlayPolicy.IsInspector(p.Id)){item.name=p.Id==NormalPlayPolicy.BasicInspector?"基础器材检测仪":"器材检测仪";item.RemoveGameItemType("MACHINE");item.SetGameItemType("TOOL");}
            item.shortDescription=p.Name+" · 普通规格 · "+p.Width+"×"+p.Height;
            item.longDescription=p.Kind=="machine"?"放在店铺或仓库外层，在电声工坊使用；不能在箱中运行。一台机器同时进行一项作业。不兼容原版通用系统模块；参考输入仅由标定台的专用装拆工序处理。":"电声制作的真实部件或耗材；加工数量和余料随存档保存。";
            item.EnableTag("ms.music",false);item.EnableTag("ms.kind."+p.Kind,false);item.EnableTag("ms.grade.ordinary",false);
            economy.PackageB.ApplyCatalog(item);
            if(p.Id==NormalPlayPolicy.BasicInspector)item.longDescription="下层或中层原生卖方可供货，参考价480。放入受托主线原件即可检测，不耗电、不消耗次数。不能为普通商业器材提供专业参考自检或精密标定资格。专业器材检测仪仍由上层卖方供应。";
            if(p.Id=="ms_machine_electrobench")item.longDescription+="\n中层/上层原生卖方供货，参考价1800。主线没有交件截止日，可以经营攒钱后再购入；作业按游戏内过夜推进。";
            if(p.Kind=="container")economy.PackageB.InitBox(item);
            return item;
        }
        internal bool Attach() {
            bool changed=host.Engine.EnableWorkshop();
            foreach(var pair in References) {
                var sample=DirectoryMaster.Item(pair.Value,true);
                if(sample==null || sample.identifier!=pair.Value || sample.unitCount!=1)throw new InvalidDataException("原版单位映射失败："+pair.Key);
                names[pair.Key]=sample.name;
            }
            if(DirectoryMaster.Item("energy_credit",true).GetTagReadonly("power_source_item_max_energy").GetInt()!=100)throw new InvalidDataException("标准能源容量发生变化，已停止工坊");
            foreach(var j in host.Engine.Snapshot().Workshop.Jobs.Values)
                foreach(var e in j.Inputs.Where(x=>!j.Taken.Contains(x.Entity) && (!j.InputsConsumed || x.Tool)).Concat(j.Outputs.Where(x=>!j.Taken.Contains(x.Entity))))
                    if(economy.FindPlayerItem(e.Entity)!=null)throw new InvalidDataException("工单与库存重复保管同一实体："+e.Entity);
            enabled=true;accumulatedMs.Clear();return changed;
        }
        internal void Detach(){Close();enabled=false;accumulatedMs.Clear();}
        internal string NativeId(string reference)=>References.GetValueOrDefault(reference,reference);
        internal string Label(string reference)=>names.GetValueOrDefault(reference,WorkshopCatalog.Product(reference)?.Name??reference);
        private bool PlayerOwned(GameItem item) {
            if(economy.PackageB.Enabled)return economy.PackageB.PlayerOwned(item);
            if(item==null || item.IsTag(MusicCatalog.ProtectedTag))return false;
            var b=host.Engine.Snapshot().Economy.Business;
            return (!b.Instruments.TryGetValue(item.uniqueId,out var g) || g.Owner=="player") && (!b.Components.TryGetValue(item.uniqueId,out var c) || c.Owner=="player");
        }
        private bool WorkingMachine(int entity,string station="electrobench") {
            if(economy.PackageB.Enabled)return economy.PackageB.Working(entity,station=="calibrator"?"ms_machine_calibrator":"ms_machine_electrobench");
            var item=economy.FindPlayerItem(entity);
            return item!=null && item.identifier=="ms_machine_electrobench" && PlayerOwned(item) && economy.PlayerInventories().Any(i=>i.NativeIdentity()==item.parentInventory?.NativeIdentity());
        }
        private sealed class Selection {internal GameItem Item,Held;internal GameInventory Source;internal int Before,Count;internal string Reference;internal bool Tool,InBenchToolbox;}
        private List<Selection> Select(WorkshopSpec r,Dictionary<string,int[]> choices=null,IEnumerable<GameItem> custody=null) {
            var selected=new List<Selection>();var seen=new HashSet<int>();
            foreach(var req in r.Inputs.Select(p=>(Key:p.Key,Count:p.Value,Tool:false)).Concat(r.Tools.Select(p=>(Key:p.Key,Count:p.Value,Tool:true)))) {
                int need=req.Count;
                var owned=economy.AllPlayerItems().ToArray();var extra=(custody??Array.Empty<GameItem>()).ToArray();
                var candidates=owned.Concat(extra).Where(x=>x.identifier==NativeId(req.Key) && x.unitCount>0);
                if(choices?.TryGetValue(req.Key,out var exact)==true)candidates=candidates.Where(x=>exact.Contains(x.uniqueId));
                else candidates=candidates.OrderBy(x=>PackageBCatalog.Rank(NativePackageB.Grade(x))>=PackageBCatalog.Rank(r.Mode)?0:1).ThenBy(x=>PackageBCatalog.Rank(NativePackageB.Grade(x))).ThenBy(x=>x.uniqueId);
                foreach(var item in candidates) {
                    if(seen.Contains(item.uniqueId) || economy.PackageB.RepairInstalled(item) || (!extra.Any(x=>x.uniqueId==item.uniqueId) && !PlayerOwned(item)) || item.FindAllChildItems().Count>0 || (req.Tool && item.unitCount!=1))continue;
                    int count=Math.Min(need,item.unitCount);selected.Add(new Selection{Item=item,Source=item.parentInventory,Before=item.unitCount,Count=count,Reference=req.Key,Tool=req.Tool});
                    seen.Add(item.uniqueId);need-=count;if(need==0)break;
                }
                if(need>0)throw new InvalidOperationException("缺少 "+Label(req.Key)+" ×"+need);
            }
            return selected;
        }
        internal string Explain(string recipeId,int machine,string mode="ordinary") {
            if(!Enabled)return "工坊尚未就绪";
            var r=WorkshopCatalog.Recipe(recipeId);if(r==null)return "配方尚未开放";
            if(r.Station=="native_printer")return economy.PackageB.PrintExplain(recipeId,machine);
            if(r.Station!="counter" && !WorkingMachine(machine))return "请先选择放在店铺或仓库外层的自有整备台";
            if(Workshop.Busy(host.Engine.Snapshot().Workshop,r.Station=="counter"?0:machine))return "工位有未完成或待取回物品";
            try{var spec=PackageBCatalog.Spec(recipeId,mode,null);CheckConditions(spec,machine);Select(spec);return "可开工";}catch(InvalidOperationException e){return e.Message;}
        }
        internal static WorkshopEntity Store(GameItem item,string reference=null,bool tool=false)=>new(){Entity=item.uniqueId,ItemId=item.identifier,Reference=reference??item.identifier,Units=item.unitCount,Tool=tool,Grade=NativePackageB.Grade(item),Snapshot=NativeMemberEconomy.Encode(item)};
        internal string PreparedBlock(WorkshopSpec spec,int machine,Dictionary<string,int[]> choices,IEnumerable<GameItem> custody)
        {
            try{CheckConditions(spec,machine);Select(spec,choices,custody);return null;}
            catch(InvalidOperationException e){return e.Message;}
        }
        internal void CheckConditions(WorkshopSpec spec,int machine)
        {
            var recipe=WorkshopCatalog.Recipe(spec.Recipe);
            if(spec.Purpose=="manufacture" && (recipe?.Qualification=="P2" || spec.Mode!="ordinary") && !host.Engine.Snapshot().Workshop.Professional)throw new InvalidOperationException("需要P2：合法上层成交史、基础校准记录及当前自有整备台");
            if(spec.Station=="counter"){if(machine!=0)throw new InvalidOperationException("请使用柜台工位");}
            else if(!WorkingMachine(machine,spec.Station))throw new InvalidOperationException("机器须是自有并放在有效库存根");
            if(economy.PackageB.MachineBusy(machine))throw new InvalidOperationException("工位或测量设备尚被占用");
            if(spec.Mode=="precision" && spec.Purpose=="manufacture"){
                int measure=economy.PackageB.PrecisionMachine();if(measure==0)throw new InvalidOperationException("需要当前有效、空闲且完成参考自检的标定台");spec.MeasuringMachine=measure;
            }
        }
        internal bool Start(string jobId,string recipeId,int machine=0,string mode="ordinary",Dictionary<string,int[]> choices=null,bool confirmDowngrade=false) {
            if(!Enabled)throw new InvalidOperationException("工坊尚未就绪");
            var state=host.Engine.Snapshot().Workshop;
            if(state.Jobs.TryGetValue(jobId,out var prior)) {
                if(prior.Recipe!=recipeId || prior.Machine!=machine)throw new InvalidOperationException("工单号冲突");return false;
            }
            var recipe=WorkshopCatalog.Recipe(recipeId)??throw new InvalidOperationException("配方尚未开放");
            if(recipe.Station=="native_printer")return economy.PackageB.StartPrint(jobId,recipeId,machine);
            if(recipe.Station=="counter")machine=0;
            var baseSpec=PackageBCatalog.Spec(recipeId,mode,null);CheckConditions(baseSpec,machine);
            var selected=Select(baseSpec,choices);var spec=PackageBCatalog.Spec(recipeId,mode,selected.Select(x=>Store(x.Item,x.Reference,x.Tool)));spec.MeasuringMachine=baseSpec.MeasuringMachine;
            if(PackageBCatalog.Rank(spec.Grade)<PackageBCatalog.Rank(mode) && !confirmDowngrade)throw new InvalidOperationException("关键件等级不足，预计出货 "+PackageBCatalog.GradeName(spec.Grade)+"；请在预览中明确确认降级或换件");
            return StartPrepared(jobId,spec,machine,choices);
        }
        internal string PreviewGrade(string recipeId,string mode,Dictionary<string,int[]> choices=null)
        {
            var spec=PackageBCatalog.Spec(recipeId,mode,null);var selected=Select(spec,choices);return PackageBCatalog.Spec(recipeId,mode,selected.Select(x=>Store(x.Item,x.Reference,x.Tool))).Grade;
        }
        internal bool StartPrepared(string jobId,WorkshopSpec spec,int machine,Dictionary<string,int[]> choices=null,IEnumerable<GameItem> custody=null,Action<StoryState,List<WorkshopEntity>> onStart=null)
        {
            if(host.Engine.Snapshot().Workshop.Jobs.TryGetValue(jobId,out var existing)){if(existing.Machine!=machine || existing.Recipe!=spec.Recipe || existing.SpecHash!=Workshop.HashSpec(spec))throw new InvalidOperationException("作业号与原冻结规格不符");return false;}
            CheckConditions(spec,machine);var selections=Select(spec,choices,custody);var stored=new List<WorkshopEntity>();
            foreach(var pick in selections)economy.PackageB.Ensure(pick.Item,host.Engine.Snapshot().Economy.OwnedItems.GetValueOrDefault(pick.Item.uniqueId)?.ActorId??"player");
            economy.MutateNative(()=>{
                foreach(var s in selections) {
                    s.InBenchToolbox=s.Tool&&economy.PackageB.BenchHasTool(machine,s.Item.uniqueId);
                    if(s.InBenchToolbox)s.Held=s.Item;
                    else if(s.Count==s.Before) {if(s.Source!=null && !s.Source.Expel(s.Item))throw new InvalidOperationException("投料移动失败");s.Held=s.Item;}
                    else {
                        var split=s.Item.GetSplitItem(s.Count);s.Held=split;
                        if(split==null || split.uniqueId==s.Item.uniqueId || split.identifier!=s.Item.identifier || split.unitCount!=s.Count)throw new InvalidOperationException("原版分拆未生成独立份数");
                        if(s.Item.unitCount==s.Before)s.Item.unitCount=s.Before-s.Count;
                        if(s.Item.unitCount!=s.Before-s.Count)throw new InvalidOperationException("原版分拆数量不符");
                        if(split.parentInventory!=null && !split.parentInventory.Expel(split))throw new InvalidOperationException("拆分材料无法移入工单");
                    }
                    economy.PackageB.Ensure(s.Held,host.Engine.Snapshot().Workshop.Advanced.Facts.GetValueOrDefault(s.Item.uniqueId)?.Owner??"player");
                    stored.Add(Store(s.Held,s.Reference,s.Tool));
                }
                host.Engine.EditPackageB(state=>{Workshop.StartFrozen(state.Workshop,jobId,machine,100,stored,spec);
                    var retained=selections.Where(s=>s.InBenchToolbox).Select(s=>s.Item.uniqueId).ToHashSet();
                    if(retained.Count>0){var job=state.Workshop.Jobs[jobId];job.BenchTools=retained;job.Taken.UnionWith(retained);}
                    onStart?.Invoke(state,stored);});
                economy.Trade.RecordWorkInputs(jobId,selections.Select((s,i)=>(s.Item.uniqueId,stored[i].Entity,s.Count,s.Before,s.Tool)).ToArray());
            },()=>{foreach(var s in selections){s.Item.unitCount=s.Before;if(s.Source!=null && s.Count==s.Before && s.Item.parentInventory==null && !s.Source.UncheckedAccept(s.Item))throw new IOException("投料回退失败，完整旧档保留");}});
            return true;
        }
        internal bool Tick(string id,int milliseconds,int sourceEntity=0,int repairDay=-1) {
            if(core.Test?.NaturalSamplingActive==true&&!automaticClock){core.Test.NaturalSampleViolated=true;throw new InvalidOperationException("自然经营采样中禁止直接快进工序");}
            if(!Enabled)throw new InvalidOperationException("工坊尚未就绪");
            var w=host.Engine.Snapshot().Workshop;var job=w.Jobs[id];if(job.Status!="running")return false;
            var frozen=Workshop.Frozen(job);
            if(job.Machine>0 && !WorkingMachine(job.Machine,frozen.Station)){host.Engine.EditWorkshop(s=>Workshop.Pause(s,id,"机器未放在有效位置"));host.Persist();return false;}
            if(frozen.MeasuringMachine>0 && !economy.PackageB.ReferenceAvailable(frozen.MeasuringMachine)){host.Engine.EditWorkshop(s=>Workshop.Pause(s,id,"参考测量条件失效，保留已付进度"));host.Persist();return false;}
            if(job.BenchTools?.Any(tool=>!economy.PackageB.BenchHasTool(job.Machine,tool))==true){host.Engine.EditWorkshop(s=>Workshop.Pause(s,id,"请把原工具放回整备台的工具箱"));host.Persist();return false;}
            var power=sourceEntity>0?economy.FindPlayerItem(sourceEntity):null;
            if(power!=null && (!PlayerOwned(power) || power.unitCount!=1 || !power.IsTag("power_source_item")))throw new InvalidOperationException("不是玩家自有的单件原版供电源");
            int available=power==null?0:PowerHelper.GetAvailableEnergyFromItem(power,false);
            var plan=Workshop.Plan(w,job,milliseconds,power?.uniqueId??0,available);
            if(!plan.Powered){host.Engine.EditWorkshop(s=>Workshop.Pause(s,id,"能源不足，请补充后继续"));host.Persist();return false;}
            var output=new List<WorkshopEntity>();int oldPower=available;
            economy.MutateNative(()=>{
                if(plan.PhysicalDebit>0 && (power==null || !PowerHelper.DrawPowerSource(power,plan.PhysicalDebit)))throw new InvalidOperationException("原版供电源扣电失败");
                if(power!=null && PowerHelper.GetAvailableEnergyFromItem(power,false)!=oldPower-plan.PhysicalDebit)throw new InvalidOperationException("实际扣电与工单不一致");
                economy.Trade.RecordEnergy(id,power?.uniqueId??0,plan.PhysicalDebit,oldPower);
                if(plan.ElapsedMs==job.DurationMs&&frozen.OutputBatch!=null){foreach(var p in frozen.OutputBatch)for(int n=0;n<p.Value;n++){
                    var item=DirectoryMaster.Item(p.Key,true);economy.PackageB.ApplyFacts(item,frozen.Grade,"player","salvage:"+id);output.Add(Store(item));
                }}else if(plan.ElapsedMs==job.DurationMs){for(int n=0;n<frozen.Quantity;n++){
                    if(frozen.Purpose=="reference_remove"){output.Add(economy.PackageB.ReferenceOutput(job.Machine));continue;}
                    var item=DirectoryMaster.Item(frozen.Output,true);economy.PackageB.ApplyFacts(item,frozen.Grade,"player","manufacture:"+id);output.Add(Store(item));
                }}
                host.Engine.EditWorkshop(s=>{Workshop.Advance(s,id,milliseconds,power?.uniqueId??0,available,output);if(repairDay>=0){s.Jobs[id].LastRepairDay=repairDay;s.Jobs[id].RepairDaysWorked++;}});
                economy.PackageB.OnJobCompleted(id);
                economy.Trade.WorkCompleted(id);
            },()=>{if(power!=null)PowerHelper.SetPowerSourceAt(power,oldPower);});return true;
        }
        internal void Pause(string id){host.Engine.EditWorkshop(w=>Workshop.Pause(w,id,"手动暂停"));host.Persist();}
        internal void Resume(string id) {
            var j=host.Engine.Snapshot().Workshop.Jobs[id];var spec=Workshop.Frozen(j);if(j.Machine>0 && !WorkingMachine(j.Machine,spec.Station))throw new InvalidOperationException("请先放回机器");
            if(spec.MeasuringMachine>0 && !economy.PackageB.ReferenceAvailable(spec.MeasuringMachine))throw new InvalidOperationException("请恢复有效参考测量设备");
            host.Engine.EditWorkshop(w=>Workshop.Resume(w,id));host.Persist();
        }
        internal void Cancel(string id){Action action=()=>{host.Engine.EditWorkshop(w=>Workshop.Cancel(w,id));economy.Trade.WorkCancelled(id);var j=host.Engine.Snapshot().Workshop.Jobs[id];if(j.Spec?.Purpose=="service")economy.PackageB.FinishCancelledStep(j.Spec.Service,id);};if(economy.IsTransferring)action();else economy.MutateNative(action,()=>{});}
        internal bool Claim(string id,int entity,GameInventory destination=null) {
            var job=host.Engine.Snapshot().Workshop.Jobs[id];if(job.Taken.Contains(entity))return false;
            var r=Workshop.Pending(job).SingleOrDefault(x=>x.Entity==entity)??throw new InvalidOperationException("没有这份待取物");
            if(economy.FindPlayerItem(entity)!=null)throw new InvalidOperationException("库存存在相同实体，拒绝复制");
            var item=NativeMemberEconomy.Decode(r.Snapshot);
            if(item.uniqueId!=entity || item.identifier!=r.ItemId || item.unitCount!=r.Units)throw new InvalidDataException("待取实物身份损坏");
            var slot=destination==null?null:NativePackageB.ExactOutputSlot(destination,item);
            if(destination!=null&&slot==null)return false;
            bool placed=false;
            economy.MutateNative(()=>{
                if(destination==null)EmporiumEntry.Instance.TryAddToPlayerInv(item);
                else if(slot.TryAcceptOnce(r.Units)!=r.Units||economy.FindPlayerItem(entity)==null)throw new InvalidOperationException("工位输出位置已改变，原件仍待领。");
                placed=economy.FindPlayerItem(entity)!=null;
                if(placed)host.Engine.EditWorkshop(s=>Workshop.Take(s,id,entity));
            },()=>{if(placed && item.parentInventory!=null)item.parentInventory.Expel(item);});
            return placed;
        }
        internal bool CanClock() {
            var s=PlayerStore.instance;var ui=StoreUIManager.Instance;
            return Enabled && s!=null && s.storeState==PlayerStore.StoreState.OPEN && !panel && !host.Visible && host.Campaign?.PanelOpen!=true && !economy.Business.PanelOpen && !host.Integration.InVenue
                && MapUIManager.Instance?.isOutside!=true && MapUIManager.Instance?.inTransit!=true
                && Time.timeScale>0 && s.lockAllFlag==0 && s.lockStorePhysicalFlag==0 && ui!=null && ui.mouseLockFlag==0 && !ui.isTyping
                && DialogUIManager.Instance!=null && DialogUIManager.Instance.isConversationActiveFlag==0;
        }
        internal int AutomaticSourceFor(string id,int milliseconds) {
            var state=host.Engine.Snapshot().Workshop;var job=state.Jobs[id];if(job.EnergyE==0)return 0;
            foreach(var item in economy.AllPlayerItems().Where(x=>PlayerOwned(x) && x.unitCount==1 && x.IsTag("power_source_item")).OrderBy(x=>x.uniqueId)) {
                int available=PowerHelper.GetAvailableEnergyFromItem(item,false);
                if(Workshop.Plan(state,job,milliseconds,item.uniqueId,available).Powered)return item.uniqueId;
            }
            return 0;
        }
        internal void Update() {
            if(!CanClock())return;
            var running=host.ReadState.Workshop.Jobs.Values.Where(x=>x.Status=="running"&&x.RepairDays==0).Select(x=>x.Id).ToArray();
            foreach(string stale in accumulatedMs.Keys.Where(k=>!running.Contains(k)).ToArray())accumulatedMs.Remove(stale);
            foreach(string id in running){
                float elapsed=accumulatedMs.GetValueOrDefault(id)+Math.Min(Time.deltaTime,1f)*1000;
                if(elapsed<1000){accumulatedMs[id]=elapsed;continue;}int ms=(int)elapsed;accumulatedMs[id]=elapsed-ms;
                automaticClock=true;try{Tick(id,ms,AutomaticSourceFor(id,ms));}finally{automaticClock=false;}
            }
        }
        internal bool BlocksSale(GameItem item) {
            if(host.Campaign?.BlocksSale(item)==true)return true;
            if(!Enabled || item==null)return false;
            if(economy.PackageB.BlocksSale(item))return true;
            var busy=host.Engine.Snapshot().Workshop.Jobs.Values.Where(x=>!x.Terminal && x.Machine>0).Select(x=>x.Machine).ToHashSet();
            if(busy.Contains(item.uniqueId))return true;
            foreach(var child in item.FindAllChildItems())if(busy.Contains(child.uniqueId))return true;
            return false;
        }
        internal void RecordPurchase(int entity) {
            if(!Enabled || entity<=0)return;var item=economy.FindPlayerItem(entity);var client=PlayerStore.instance?.currentClientInstance?.storeClient;
            if(item==null || client==null || client.clientFaction!=StoreClient.FACTION_UPPER || !WorkshopCatalog.UpperQualificationItems.Contains(item.identifier))return;
            host.Engine.EditWorkshop(w=>Workshop.RecordUpperPurchase(w,"upper_"+host.Day+"_"+entity,item.identifier,true,true));host.Persist();
        }
        internal void Open() {
            if(!Enabled || panel || host.Visible)return;var s=PlayerStore.instance;if(s==null || s.lockStorePhysicalFlag!=0 || DialogUIManager.Instance.isConversationActiveFlag!=0)return;
            economy.CloseLedger();economy.Business.Close();previousTime=Time.timeScale;lockedStore=s;lockedUI=StoreUIManager.Instance;
            lockedStore.lockStorePhysicalFlag++;NativePanelCursor.Acquire(lockedUI);Time.timeScale=0;panel=true;page=0;
        }
        internal void Close() {
            if(!panel)return;panel=false;Time.timeScale=previousTime;if(lockedStore!=null)lockedStore.lockStorePhysicalFlag=Math.Max(0,lockedStore.lockStorePhysicalFlag-1);
            if(lockedUI!=null)NativePanelCursor.Release(lockedUI);lockedUI=null;lockedStore=null;
        }
        internal void Draw(GUIStyle bs,GUIStyle buttons,GUIStyle windows) {
            if(!Enabled || host.Visible)return;body=bs;button=buttons;
            if(!panel){if(NativePanelGUI.Button(new Rect(440,Screen.height-58,200,42),"电声工坊",button))Open();return;}
            float width=Math.Min(1100,Screen.width-30),height=Math.Min(850,Screen.height-30);
            NativePanelGUI.Window(781345,new Rect((Screen.width-width)/2,(Screen.height-height)/2,width,height),DrawPanel,"电声工坊 · 制作与取件",windows);
        }
        private void Try(Action action){try{action();message="已完成并保存。";}catch(Exception e){message=e.Message;core.LoggerInstance.Warning("WORKSHOP_ACTION "+e.Message);}}
        private static string StatusName(string status)=>status switch {"running"=>"加工中","paused"=>"已暂停","ready"=>"待领取","return_ready"=>"待取回",_=>"已结束"};
        private static string JobLabel(WorkshopJob j)=>WorkshopCatalog.Recipe(j.Recipe)?.Name??PackageBCatalog.Rows("services_package_b").FirstOrDefault(x=>x["id"]==j.Recipe)?.GetValueOrDefault("name")??(j.Spec?.Purpose=="reference_install"?"参考输入安装":j.Spec?.Purpose=="reference_remove"?"参考输入拆卸":j.Spec?.Purpose=="reference_selfcheck"?"参考输入自检":"器材维护");
        private void CycleChoice(string reference,int count)
        {
            var items=economy.AllPlayerItems().Where(x=>x.identifier==NativeId(reference) && PlayerOwned(x)).OrderBy(x=>x.uniqueId).ToArray();if(items.Length==0)throw new InvalidOperationException("没有对应实际材料");
            int start=previewChoices.TryGetValue(reference,out var old)?(Array.FindIndex(items,x=>x.uniqueId==old.FirstOrDefault())+1)%items.Length:0,need=count;var picked=new List<int>();
            for(int n=0;n<items.Length && need>0;n++){var item=items[(start+n)%items.Length];picked.Add(item.uniqueId);need-=item.unitCount;}previewChoices[reference]=picked.ToArray();
        }
        private void DrawPanel(int windowId) {
            float width=Math.Min(1100,Screen.width-30),height=Math.Min(850,Screen.height-30);
            if(NativePanelGUI.Button(new Rect(20,42,150,38),"配方",button)){tab="recipes";previewRecipe=null;page=0;}
            if(NativePanelGUI.Button(new Rect(180,42,150,38),"作业 / 取件",button)){tab="jobs";previewRecipe=null;page=0;}
            if(NativePanelGUI.Button(new Rect(width-150,42,130,38),"关闭",button))Close();
            GUI.Label(new Rect(20,86,width-40,60),message==""?"首次加工后原料不返还。关闭面板、正常营业时加工；暂停、睡觉和关闭游戏不加工。":message,body);
            var rows=new List<Action<float>>();
            if(previewRecipe!=null) {
                var r=WorkshopCatalog.Recipe(previewRecipe);int machine=r.Station=="counter"?0:selectedMachine;
                var spec=r.Station=="native_printer"?null:PackageBCatalog.Spec(r.Id,previewMode,null);
                string grade="";if(spec!=null){try{grade=PreviewGrade(r.Id,previewMode,previewChoices);}catch(InvalidOperationException e){grade=e.Message;}}
                rows.Add(y=>GUI.Label(new Rect(15,y,width-70,80),r.Name+" · "+(spec==null?"读取原生机器时间与能耗":spec.DurationMs/1000+"秒 / "+spec.Energy+"E · 预计 "+(PackageBCatalog.Rank(grade)>=0?PackageBCatalog.GradeName(grade):grade))+"\n全部材料由玩家提供；不会向自己支付工钱。",body));
                foreach(var modeRow in PackageBCatalog.Rows("recipe_modes_costs").Where(x=>x["recipe_id"]==r.Id)){string mode=modeRow["mode"];rows.Add(y=>{if(NativePanelGUI.Button(new Rect(15,y,420,45),(previewMode==mode?"已选：":"工艺：")+PackageBCatalog.GradeName(mode)+(mode=="ordinary"?"":" / P2与额外耗材"),button)){previewMode=mode;previewChoices.Clear();}});}
                foreach(var p in spec?.Inputs??r.Inputs){var item=p;rows.Add(y=>{GUI.Label(new Rect(15,y,width-310,75),"投入："+Label(item.Key)+" ×"+item.Value+"\n"+(previewChoices.TryGetValue(item.Key,out var ids)?"指定原件："+string.Join("、",ids):"自动选择最低足够等级"),body);if(NativePanelGUI.Button(new Rect(width-275,y,220,45),"轮换具体原件",button))Try(()=>CycleChoice(item.Key,item.Value));});}
                foreach(var p in r.Tools){var tool=p;rows.Add(y=>GUI.Label(new Rect(15,y,width-70,70),"工具："+Label(tool.Key)+" ×"+tool.Value+"（保留原件，完成或取消后领回）",body));}
                rows.Add(y=>GUI.Label(new Rect(15,y,width-70,80),"普通加工首次进度后取消不返原料；打印遵循原生规则。所选工艺不保证成品同级，请核对预计出货。\n当前检查："+Explain(r.Id,machine,previewMode),body));
                rows.Add(y=>{if(NativePanelGUI.Button(new Rect(15,y,420,48),"确认所列投入与预计规格，开工",button))Try(()=>{Start("work_"+Guid.NewGuid().ToString("N"),r.Id,machine,previewMode,previewChoices,true);previewRecipe=null;tab="jobs";page=0;});});
            } else if(tab=="recipes") {
                rows.Add(y=>GUI.Label(new Rect(15,y,width-85,60),"整备台："+(selectedMachine==0?"尚未选择":selectedMachine.ToString())+"。工具开工后随工单保管，取件时领回。",body));
                foreach(var machine in economy.AllPlayerItems().Where(x=>(x.identifier=="ms_machine_electrobench" && WorkingMachine(x.uniqueId)) || x.identifier=="printer")){var m=machine;rows.Add(y=>{if(NativePanelGUI.Button(new Rect(15,y,600,48),"选择 "+m.name+" #"+m.uniqueId,button))selectedMachine=m.uniqueId;});}
                foreach(var recipe in WorkshopCatalog.Recipes){var r=recipe;rows.Add(y=>{
                    GUI.Label(new Rect(15,y,width-280,70),r.Name+" / "+r.Seconds+"秒 / "+r.Energy+"E\n"+string.Join("、",r.Inputs.Select(p=>Label(p.Key)+"×"+p.Value)),body);
                    if(NativePanelGUI.Button(new Rect(width-255,y,180,58),"查看 / 制作",button)){previewRecipe=r.Id;previewMode="ordinary";previewChoices.Clear();page=0;}
                });}
            } else {
                foreach(var job in host.Engine.Snapshot().Workshop.Jobs.Values.Where(x=>!x.Terminal).OrderBy(x=>x.Id)){var j=job;rows.Add(y=>{
                    GUI.Label(new Rect(15,y,width-320,70),JobLabel(j)+" · "+StatusName(j.Status)+" · "+j.ElapsedMs/1000+"/"+j.DurationMs/1000+"秒"+(j.PauseReason==null?"":"\n"+j.PauseReason),body);
                    if(j.Status=="running" && NativePanelGUI.Button(new Rect(width-290,y,95,45),"暂停",button))Try(()=>Pause(j.Id));
                    if(j.Status=="paused" && NativePanelGUI.Button(new Rect(width-290,y,95,45),"继续",button))Try(()=>Resume(j.Id));
                    if((j.Status=="running" || j.Status=="paused") && NativePanelGUI.Button(new Rect(width-185,y,105,45),"取消",button))Try(()=>Cancel(j.Id));
                });
                foreach(var entity in Workshop.Pending(j)){var e=entity;rows.Add(y=>{GUI.Label(new Rect(30,y,width-300,60),"待取："+Label(e.Reference)+" ×"+e.Units+" #"+e.Entity,body);if(NativePanelGUI.Button(new Rect(width-250,y,170,45),"领取原件",button))Try(()=>{if(!Claim(j.Id,e.Entity))throw new InvalidOperationException("库存空间不足，原件仍保留在工位。");});});}}
                foreach(var record in host.Engine.Snapshot().Workshop.Advanced.Prints.Values.Where(x=>x.Status is "running" or "paused" or "ready" or "return_ready")){
                    var p=record;rows.Add(y=>{GUI.Label(new Rect(15,y,width-300,80),"原生打印 #"+p.Machine+" · "+p.Status+" · 预计 "+p.NativeDuration+"周期 / "+p.NativeEnergy+"E\n已扣 "+p.EnergySpent+"E · "+p.PauseReason,body);if(p.Status is "running" or "paused" && NativePanelGUI.Button(new Rect(width-185,y,105,45),"取消",button))Try(()=>economy.PackageB.CancelPrint(p.Id));});
                    foreach(var output in p.Pending){var e=output;rows.Add(y=>{GUI.Label(new Rect(20,y,width-300,70),"打印待取："+Label(e.ItemId)+" #"+e.Entity,body);if(NativePanelGUI.Button(new Rect(width-250,y,170,45),"领取原件",button))Try(()=>{if(!economy.PackageB.ClaimPrint(p.Id,e.Entity))throw new InvalidOperationException("空间不足，保留同一输出");});});}
                }
                if(rows.Count==0)rows.Add(y=>GUI.Label(new Rect(15,y,width-80,50),"目前没有未结作业。",body));
            }
            int perPage=Math.Max(1,(int)((height-230)/100));int pages=Math.Max(1,(rows.Count+perPage-1)/perPage);page=Math.Min(page,pages-1);
            TotalRows=rows.Count;RenderedRows=Math.Min(perPage,rows.Count-page*perPage);
            for(int i=0;i<RenderedRows;i++)rows[page*perPage+i](155+i*100);
            if(NativePanelGUI.Button(new Rect(20,height-60,150,40),"上一页",button))page=Math.Max(0,page-1);
            GUI.Label(new Rect(190,height-60,360,40),(page+1)+" / "+pages+" 页",body);
            if(NativePanelGUI.Button(new Rect(width-180,height-60,150,40),"下一页",button))page=Math.Min(pages-1,page+1);
        }
    }
}
