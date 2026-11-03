using System;
using System.Collections.Generic;
using System.Linq;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageB
    {
        private readonly List<Action> benchDialogueActions=new();
        private bool NativeBusiness=>Enabled&&host.NormalPlay;
        private WorkshopJob NativeSalvage(int bench)=>host.ReadState.Workshop.Jobs.Values.FirstOrDefault(j=>j.Machine==bench&&j.Spec?.Purpose=="d_salvage"&&!j.Terminal);
        private WorkshopJob NativeReference(int detector)=>host.ReadState.Workshop.Jobs.Values.FirstOrDefault(j=>j.Machine==detector&&j.Spec?.Purpose?.StartsWith("reference_")==true&&!j.Terminal);
        private WorkshopRecipe PrintRecipe(GameItem chip)=>chip==null?null:WorkshopCatalog.Recipes.FirstOrDefault(r=>r.Station=="native_printer"&&r.Tools.ContainsKey(chip.identifier));
        private GameInventory PrintIntake(GameItem printer)=>Inventories(printer).FirstOrDefault(x=>x.TryCast<GameSlotInventory>()!=null);
        private GameItem LoadedChip(GameItem printer)=>ItemList(PrintIntake(printer)?.childItems).SingleOrDefault();
        private string BusinessJobId(string name)=>"native_"+name+"_"+Guid.NewGuid().ToString("N");
        private bool NativeBusinessTarget(GameItem tool,GameItem target)
        {
            if(!NativeBusiness||tool==null||target==null||!PlayerOwned(tool)||!PlayerOwned(target))return false;
            return target.identifier=="printer"&&PrintRecipe(tool)!=null
                ||target.identifier=="ms_machine_electrobench"&&tool.identifier=="wire_cutter"&&BenchHasTool(target.uniqueId,tool.uniqueId)
                ||target.identifier=="ms_machine_calibrator"&&tool.identifier=="ms_module_reference_input";
        }
        private void UseNativeBusinessTarget(GameItem tool,GameItem target)
        {
            if(target.identifier=="printer"){ShowPrintAction(target,tool);return;}
            if(target.identifier=="ms_machine_electrobench"){ShowSalvageAction(target,tool);return;}
            ConfirmMachine(target,"专业检测仪","安装这枚参考输入。工序消耗 4E，需营业中的有效加工时间。",()=>{
                if(!NativeBusinessTarget(economy.FindPlayerItem(tool.uniqueId),economy.FindPlayerItem(target.uniqueId)))throw new InvalidOperationException("机器或参考输入已移走");
                ReferenceJob(BusinessJobId("reference"),target.uniqueId,"install",tool.uniqueId);return "参考输入已交入工位。";
            });
        }
        private bool NativeBusinessActivate(GameItem item)
        {
            if(!NativeBusiness||!PlayerOwned(item))return false;
            if(item.identifier=="printer")return PrintRecipe(LoadedChip(item))!=null;
            return item.identifier=="ms_machine_electrobench"&&NativeSalvage(item.uniqueId)!=null
                ||item.identifier=="ms_machine_calibrator";
        }
        private void UseNativeBusinessMachine(GameItem item)
        {
            if(item.identifier=="printer"){ShowPrintAction(item,LoadedChip(item));return;}
            if(item.identifier=="ms_machine_electrobench"){ShowSalvageAction(item,null);return;}
            if(HasLegacyScanItem(item)&&core.Test==null){ShowRepairNative(item.uniqueId);return;}
            ShowPrecisionAction(item);
        }
        private void ConfirmMachine(GameItem machine,string title,string text,Func<string> action=null)
        {
            var ui=DialogUIManager.Instance;
            if(ui==null||ui.isConversationActiveFlag!=0||core.Presentation?.Active==true)return;
            machine.contentWindow?.Hide();
            core.EnsureChineseFallback();benchDialogueActions.Clear();
            var root=new Dialogue().SetText(title,text);
            if(action!=null){
                var result=new Dialogue().SetText(title,"已确认。");
                Action confirm=()=>{
                    try{result.SetText(title,action());}
                    catch(InvalidOperationException error){result.SetText(title,error.Message);}
                };
                benchDialogueActions.Add(confirm);
                root.AddChoice(ItemDirectory.CreateEmptyItem("ms_work_confirm"),"确认","确认",new Dialogue().SetText(DialogUIManager.PLAYER,"确认").SetNextDialogue(result),confirm,null);
                root.AddChoice(ItemDirectory.CreateEmptyItem("ms_work_keep"),"先不办","先不办",new Dialogue().SetText(DialogUIManager.PLAYER,"先不办"),null,null);
            }
            ui.StartDialogue(root,false);
        }
        private void ShowPrintAction(GameItem printer,GameItem chip)
        {
            var pending=A.Prints.Values.FirstOrDefault(x=>x.Machine==printer.uniqueId&&x.Status is "running" or "paused" or "ready" or "return_ready");
            if(pending!=null){
                if(pending.Status is "ready" or "return_ready"){ConfirmMachine(printer,"打印机","请在原机输出格取件。空间不足时，原产物会继续等待。");return;}
                ConfirmMachine(printer,"打印机","当前已加工 "+pending.Cycles+" 个周期。取消会保留芯片，已消耗的电力和塑料不退。未加工时塑料可取回。",()=>{
                    CancelPrint(pending.Id);return "已取消。未消耗的塑料放入打印机输出格。";
                });return;
            }
            var recipe=PrintRecipe(chip);
            if(recipe==null)return;
            int machine=printer.uniqueId,entity=chip.uniqueId;
            ConfirmMachine(printer,"打印机","产物："+Label(recipe.Output)+" ×"+recipe.Quantity+"。\n耗材：打印塑料 ×"+recipe.Inputs["ref_print_plastic"]+"，按加工周期耗电。",()=>{
                StartPrint(BusinessJobId("print"),recipe.Id,machine,entity);return "已开始打印。完成后从原机输出格取件。";
            });
        }
        private void ShowSalvageAction(GameItem machine,GameItem cutter)
        {
            int bench=machine.uniqueId;
            var running=NativeSalvage(bench);
            if(running!=null){
                if(running.Status is "ready" or "return_ready"){
                    ConfirmMachine(machine,"电声整备台","待领实物会进入对应工位。请先腾出器材、配件或工具格。");return;
                }
                ConfirmMachine(machine,"电声整备台","取消这次报废，原件和工具会退回工位。",()=>{
                    var current=NativeSalvage(bench);
                    if(current?.Id!=running.Id||current.Status is not ("running" or "paused"))throw new InvalidOperationException("拆解已结束，请查看工位输出。");
                    economy.Workshop.Cancel(running.Id);return "已取消。原件和工具等待取回。";
                });return;
            }
            var view=InitRepairItem(machine);
            var item=ItemList(view.Host.childItems).SingleOrDefault();
            if(item==null){ConfirmMachine(machine,"电声整备台","把要报废的自有器材放入器材位，再对整备台使用剪线钳。");return;}
            string reason=PackageDFull.SalvageBlock(host.ReadState,item.uniqueId,PlayerOwned(item),item.IsTag(MusicCatalog.ProtectedTag)||economy.Workshop.BlocksSale(item));
            if(reason!=null){ConfirmMachine(machine,"电声整备台",reason);return;}
            var outputs=PackageDFull.SalvageOutputs(item.identifier);
            string list=string.Join("、",outputs.Select(x=>Label(x.Key)+" ×"+x.Value));
            ConfirmMachine(machine,"电声整备台","报废 "+item.name+" 后只能收回 "+list+"。需在工具箱内装入螺丝刀和剪线钳，睡觉过一天处理。确认报废这件原件吗？",()=>{
                if(ItemList(view.Host.childItems).SingleOrDefault()?.uniqueId!=item.uniqueId||!BenchHasTool(bench,cutter?.uniqueId??0))throw new InvalidOperationException("器材或剪线钳已移走");
                economy.Trade.StartSalvage(BusinessJobId("salvage"),item.uniqueId,bench,cutter.uniqueId);
                return "已登记拆解。日结前可操作整备台取消。";
            });
        }
        private void ShowPrecisionAction(GameItem machine)
        {
            int id=machine.uniqueId;
            var prior=A.References.GetValueOrDefault(id);
            var job=NativeReference(id);
            if(job!=null){
                if(job.Status=="paused"){
                    ConfirmMachine(machine,"专业检测仪","参考工序已暂停。恢复供电和机器位置后可以继续。",()=>{
                        economy.Workshop.Resume(job.Id);return "参考工序继续。";
                    });
                }else ConfirmMachine(machine,"专业检测仪","参考工序尚在处理或等待取件。");
                return;
            }
            if(prior?.Reference is not >0){ConfirmMachine(machine,"专业检测仪","请先对检测仪使用自有参考输入。安装后需运行自检。");return;}
            if(!ReferenceAvailable(id)){
                ConfirmMachine(machine,"专业检测仪","对当前参考输入自检，消耗 4E。",()=>{
                    ReferenceJob(BusinessJobId("reference"),id,"selfcheck");return "已开始参考自检。";
                });return;
            }
            var item=PrecisionScanTarget(machine);
            if(item==null){
                ConfirmMachine(machine,"专业检测仪","参考自检有效。先用检测仪对自有器材检测，再操作检测仪进行精密鉴定。现在卸下参考输入需消耗 4E。",()=>{
                    if(ItemList(InitRepairItem(machine).Host.childItems).Any())throw new InvalidOperationException("请先取出检测位的器材。");
                    ReferenceJob(BusinessJobId("reference"),id,"remove");return "已开始卸下参考输入。";
                });return;
            }
            ConfirmMachine(machine,"专业检测仪","对当前配置做精密鉴定。需真实上层采购、已通过基础检测和有效整备台。鉴定本身无需额外电力。",()=>{
                if(PrecisionScanTarget(machine)?.uniqueId!=item.uniqueId||!ReferenceAvailable(id)||!PlayerOwned(item)||item.IsTag(MusicCatalog.ProtectedTag))throw new InvalidOperationException("需要当前自有器材和有效参考自检");
                Qualify();
                if(!host.ReadState.Workshop.Professional)throw new InvalidOperationException("尚未满足专业资格。");
                MeasureBusinessGear(item,"b15_s08_calibrate",id,"precision");
                return PackageB.CalibrationValid(A,B,item.uniqueId,"precision",ServiceFor(item.uniqueId)?.EffectiveSlots)?"当前配置已通过精密鉴定，报告可在原件上查看。":"当前配置未通过精密鉴定，报告已记录。";
            });
        }
        private void MeasureBusinessGear(GameItem item,string kind,int machine=0,string scope="basic")
        {
            if(!NativeBusiness||!PlayerOwned(item)||item.IsTag(MusicCatalog.ProtectedTag)||!PackageDCatalog.Hosts.Contains(item.identifier)||ServiceFor(item.uniqueId)!=null){
                MeasureRepair(EnsureRepairService(item).Id,kind,machine,scope);return;
            }
            var state=host.ReadState;
            string mode=kind.EndsWith("detailed")?"detailed":scope;
            var slots=state.Economy.Business.Instruments.GetValueOrDefault(item.uniqueId)?.Slots??new Dictionary<string,int>();
            var fact=state.Workshop.Advanced.Facts.GetValueOrDefault(item.uniqueId);
            if(fact!=null&&state.Economy.Business.Instruments.ContainsKey(item.uniqueId)){
                if(kind.EndsWith("calibrate")&&A.Calibrations.GetValueOrDefault(item.uniqueId+":"+mode)?.Hash==PackageB.MeasurementHash(A,B,item.uniqueId,mode,slots))return;
                if(kind.EndsWith("detailed")&&A.Inspections.GetValueOrDefault(item.uniqueId+":"+mode)?.Hash==PackageB.InspectionHash(A,B,item.uniqueId,mode,slots))return;
            }
            string id=BusinessJobId("appraisal"),snapshot=NativeMemberEconomy.Encode(item);
            economy.MutateNative(()=>host.Engine.EditPackageB(s=>{
                PackageB.Fact(s.Workshop.Advanced,item.uniqueId,item.identifier,"player",Grade(item));
                PackageB.Authorize(s,id,"player",item.uniqueId,snapshot);
                PackageB.MeasureNow(s,id,kind,machine,host.Day,scope);
                PackageB.Deliver(s,id,id+"_filed",0,host.Day);
                PackageDFull.History(s,id+"_history",item.uniqueId,host.Day,"appraisal",scope=="precision"?"已实测当前配置的精密指标。":"已检查当前器材配置。");
            }),()=>{});
        }
        internal static SlotMarker ExactOutputSlot(GameInventory inventory,GameItem item)
        {
            bool Exact(SlotMarker slot)=>slot!=null&&slot.targetItem==null&&slot.inventory.NativeIdentity()==inventory.NativeIdentity()&&slot.numTransfer>=item.unitCount;
            var best=inventory?.TryFindOneValidInventorySlot(item);
            if(Exact(best))return best;
            var grid=inventory?.TryCast<GameGridInventory>();
            if(grid==null)return null;
            var shape=new GridShapeBuilder(item.modifiedShape);
            for(int rotation=0;rotation<4;rotation++)
                for(int y=0;y<grid.inventoryShape.height;y++)
                    for(int x=0;x<grid.inventoryShape.width;x++){
                        var slot=grid.TryInventorySlot(item,item.unitCount,shape.SetTransform(x,y,false,4-rotation),null);
                        if(Exact(slot))return slot;
                    }
            return null;
        }
        private void ReturnNativeWorkOutputs()
        {
            var state=host.ReadState.Workshop;
            foreach(var job in state.Jobs.Values.Where(j=>j.Status is "ready" or "return_ready"&&(j.Spec?.Purpose=="d_salvage"||j.Spec?.Purpose?.StartsWith("reference_")==true)).ToArray()){
                var machine=economy.FindPlayerItem(job.Machine);
                var view=machine==null?null:InitRepairItem(machine);
                if(view==null)continue;
                foreach(var item in Workshop.Pending(job).ToArray()){
                    if(job.Spec.Purpose!="d_salvage"){
                        var actual=NativeMemberEconomy.Decode(item.Snapshot);
                        var output=economy.PlayerInventories().FirstOrDefault(inv=>ExactOutputSlot(inv,actual)!=null);
                        if(output!=null)economy.Workshop.Claim(job.Id,item.Entity,output);
                        continue;
                    }
                    if(item.Tool)ReturnTool(view,NativeMemberEconomy.Decode(item.Snapshot),destination=>economy.Workshop.Claim(job.Id,item.Entity,destination));
                    else economy.Workshop.Claim(job.Id,item.Entity,item.Entity==host.ReadState.MusicTrade.Salvage[job.Id].Host?view.Host:view.Parts);
                }
            }
        }
    }
}