using System;
using System.Linq;
using ResourceStoryMod.Story;
using UnityEngine;
using System.Collections.Generic;
using HarmonyLib;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageB
    {
        internal GameItem CreateChip(WorkshopProduct p)
        {
            // Keep the native printer-chip behaviours and slot compatibility.
            // This original chip uses ten progress units; native machine/module
            // speed and power determine the actual time and energy.
            var chip=NativeChip();chip.identifier=p.Id;
            var recipe=WorkshopCatalog.Recipes.First(x=>x.Tools.ContainsKey(p.Id));
            MachineProgressHelper.SetSingleAdditionalRequiredItem(chip,"printer_plastic",recipe.Inputs["ref_print_plastic"]);
            MachineProgressHelper.InitProgressSourceItem(chip,10,recipe.Output,true,recipe.Quantity,null);
            chip.name=p.Name;chip.SetValue(p.Price);chip.SetShape(new GridShapeBuilder(p.Width,p.Height).SetDataFill(p.Width,p.Height,1).Build());
            ApplyCatalog(chip);return chip;
        }
        internal static GameItem NativeChip()=>Resources.FindObjectsOfTypeAll(NativeTypes.Of<ModuleDirectory>())[0].Cast<ModuleDirectory>().PrinterChipSmallBottle();
        private static UnusedDirectory nativePrintSource;
        private readonly Dictionary<int,float> printSeconds=new();
        private bool printingMove;
        private float nextPrintOutput;
        internal void RegisterPrinting(ModItemDirectory directory)
        {
            foreach(var type in new[]{typeof(GameGridInventory),typeof(GameSlotInventory)}){
                var method=AccessTools.DeclaredMethod(type,"Expel",new[]{typeof(GameItem)});
                if(method!=null)core.HarmonyInstance.Patch(method,prefix:new HarmonyMethod(typeof(NativePackageB),nameof(BeforePrintExpel)),postfix:new HarmonyMethod(typeof(NativePackageB),nameof(RepairAfterExpel)));
            }
            foreach(var obj in Resources.FindObjectsOfTypeAll(NativeTypes.Of<ItemDirectory>()))if(obj.Cast<ItemDirectory>().factoryDictionary?.ContainsKey("printer")==true)return;
            var factory=(System.Func<GameItem>)(Func<GameItem>)(()=>{var item=NativeProgressPrinter();item.identifier="printer";item.name="3D打印机（Demo原生进度兼容）";item.SetShape(new GridShapeBuilder(4,4).SetDataFill(4,4,1).Build());
                var intake=Inventories(item).First(x=>x.TryCast<GameSlotInventory>()!=null);ContainerHelper.AllowOnlyTaggedItem(intake,"PRINTER_CHIP_TAG",false,false);
                // The ship processing controller cannot run in the Demo store.
                // Use the surviving native desktop progress controller with the
                // original machine's four progress units per cycle.
                MachineProgressHelper.InitProgressTypeMachine(item,4,true);item.SetValue(3200);
                item.longDescription="Demo兼容打印机：原生芯片槽、模块槽与输出格。装入匹配芯片后从电声工坊开工；使用玩家自有能源，按原生进度每周期结算。";
                return item;});roots.Add(factory);bool added=directory.Add("printer",factory);core.LoggerInstance.Msg("NATIVE_PRINT_FACTORY_REGISTERED "+added);

        }
        internal static GameItem NativeProgressPrinter()
        {
            if(nativePrintSource==null){var obj=new GameObject("Millsage_Native_Print_Source");obj.SetActive(false);UnityEngine.Object.DontDestroyOnLoad(obj);nativePrintSource=obj.AddComponent<UnusedDirectory>();}
            return nativePrintSource.BioPrinterSystem();
        }
        internal string PrintExplain(string recipe,int machine)
        {
            var item=economy.FindPlayerItem(machine);if(item==null || item.identifier!="printer" || !PlayerOwned(item))return "请先选择实际原生打印机";
            if(!economy.PlayerInventories().Any(i=>i.NativeIdentity()==item.parentInventory?.NativeIdentity()))return "打印机不能在箱中运行";
            if(PrintBusy(machine))return "本机已有打印记录，请先处理输出或取消";
            var r=WorkshopCatalog.Recipe(recipe);if(r?.Station!="native_printer")return "不是打印配方";
            if(!ItemList(item.FindAllChildItems()).Concat(economy.AllPlayerItems()).Any(x=>r.Tools.ContainsKey(x.identifier) && PlayerOwned(x) && !ChipInUse(x.uniqueId)))return "需要未被另一台机器占用的真实匹配芯片";
            int plastic=economy.AllPlayerItems().Where(x=>x.identifier=="printer_plastic" && PlayerOwned(x)).Sum(x=>x.unitCount);
            return plastic<r.Inputs["ref_print_plastic"]?"缺少真实打印塑料":"可开工";
        }
        internal bool StartPrint(string id,string recipe,int machine,int exactChip=0)
        {
            if(A.Prints.TryGetValue(id,out var old)){if(old.Machine!=machine || old.Recipe!=recipe)throw new InvalidOperationException("打印记录号冲突");return false;}
            string why=PrintExplain(recipe,machine);if(why!="可开工")throw new InvalidOperationException(why);
            var printer=economy.FindPlayerItem(machine);var r=WorkshopCatalog.Recipe(recipe);var intake=PrintIntake(printer)??throw new InvalidOperationException("找不到原生芯片槽");
            var chip=ItemList(intake.childItems).FirstOrDefault();var matching=economy.AllPlayerItems().FirstOrDefault(x=>(exactChip==0||x.uniqueId==exactChip)&&r.Tools.ContainsKey(x.identifier)&&PlayerOwned(x)&&!ChipInUse(x.uniqueId));
            if(exactChip>0&&chip!=null&&chip.uniqueId!=exactChip)throw new InvalidOperationException("请先取出原生槽内的另一枚芯片");
            if(chip!=null && !r.Tools.ContainsKey(chip.identifier))throw new InvalidOperationException("先从原生槽取出另一枚芯片");chip??=matching;
            if(chip==null || !PlayerOwned(chip) || ChipInUse(chip.uniqueId))throw new InvalidOperationException("匹配芯片不属于玩家或已经被另一台机器占用");
            int speed=printer.GetTagReadonly("CURRENT_PROCESSING_SPEED_TAG").GetInt(),power=PrintPower(printer),target=chip.GetTagReadonly("PROGRESS_ITEM_TARGET_TAG").GetInt();
            if(speed<=0 || power<=0 || target<=0)throw new InvalidOperationException("原生机器有效进度或功耗无效");
            var source=chip.parentInventory;int need=r.Inputs["ref_print_plastic"];var materials=new List<WorkshopEntity>();var costs=new List<(int Source,int Held,int Units)>();
            economy.MutateNative(()=>{
                printingMove=true;try{
                    if(chip.parentInventory?.NativeIdentity()!=intake.NativeIdentity()){source?.Expel(chip);if(!intake.UncheckedAccept(chip))throw new InvalidOperationException("原生芯片槽拒绝此芯片");}
                    foreach(var plastic in economy.AllPlayerItems().Where(x=>x.identifier=="printer_plastic" && PlayerOwned(x)).ToArray()){
                        if(need==0)break;int units=plastic.unitCount,n=Math.Min(need,units);var held=plastic;
                        if(n<plastic.unitCount){int before=plastic.unitCount;held=plastic.GetSplitItem(n);if(plastic.unitCount==before)plastic.unitCount-=n;}
                        if(held.parentInventory!=null && !held.parentInventory.Expel(held))throw new InvalidOperationException("塑料移交失败");materials.Add(NativeWorkshop.Store(held));costs.Add((plastic.uniqueId,held.uniqueId,n));need-=n;
                    }
                    if(need!=0)throw new InvalidOperationException("塑料数量发生变化");MachineProgressHelper.StartProgressTypeMachine(printer,chip);
                    host.Engine.EditPackageB(s=>{
                        s.Workshop.Advanced.Prints.Add(id,new NativePrintRecord{Id=id,Machine=machine,Chip=chip.uniqueId,Recipe=recipe,NativeDuration=(target+speed-1)/speed,NativeEnergy=((target+speed-1)/speed)*power,Materials=materials});
                        if(economy.Trade.Enabled)foreach(var row in costs)NativePackageD.ApplyInputCost(s.MusicTrade,id,0,row.Source,row.Held,row.Units);
                    });
                }finally{printingMove=false;}
            },()=>{});return true;
        }
        internal bool ChipInUse(int entity)=>A.Prints.Values.Any(x=>x.Chip==entity && x.Status is "running" or "paused");
        private static int PrintPower(GameItem printer)
        {
            // ModuleHelper applies the vanilla efficiency/overclock rounding.
            // The base tag alone silently ignores the installed power modules.
            ModuleHelper.ApplyModifiedPower(printer);
            return MachineryHelper.GetMachinePowerUsage(printer);
        }
        internal bool PrintBusy(int machine)=>A.Prints.Values.Any(x=>x.Machine==machine && x.Status is "running" or "paused" or "ready" or "return_ready");
        private static bool BeforePrintExpel(GameInventory __instance,GameItem __0,ref bool __result)
        {
            if(!RepairBeforeExpel(__instance,__0,ref __result))return false;
            var p=Core.Instance.Story?.Economy?.PackageB;if(p?.Enabled!=true || p.printingMove || p.economy.IsTransferring)return true;
            var owner=InventoryOwner(__instance);if((owner?.identifier=="printer" && p.A.Prints.Values.Any(x=>x.Machine==owner.uniqueId && x.Status is "running" or "paused")) || (__0?.identifier=="printer" && p.PrintBusy(__0.uniqueId))){__result=false;return false;}return true;
        }
        internal bool PrintCycle(string id,int source=0)
        {
            var record=A.Prints[id];if(record.Status is not ("running" or "paused"))return false;
            var printer=economy.FindPlayerItem(record.Machine);if(printer==null || !PlayerOwned(printer) || !economy.PlayerInventories().Any(x=>x.NativeIdentity()==printer.parentInventory?.NativeIdentity()))return PausePrint(id,"打印机不在可工作的库存根");
            var chip=ItemList(printer.FindAllChildItems()).FirstOrDefault(x=>x.uniqueId==record.Chip);if(chip==null)return PausePrint(id,"匹配芯片不在原生槽");
            int power=PrintPower(printer);
            if(power<=0)return PausePrint(id,"原生机器功耗无效");
            var energy=source>0?economy.FindPlayerItem(source):economy.AllPlayerItems().Where(x=>PlayerOwned(x) && x.unitCount==1 && x.IsTag("power_source_item")).OrderBy(x=>x.uniqueId).FirstOrDefault(x=>PowerHelper.GetAvailableEnergyFromItem(x,false)>=power);
            if(energy==null || !PlayerOwned(energy) || !energy.IsTag("power_source_item") || PowerHelper.GetAvailableEnergyFromItem(energy,false)<power)return PausePrint(id,"能源不足；已付原生进度保留");
            int before=PowerHelper.GetAvailableEnergyFromItem(energy,false);
            economy.MutateNative(()=>{
                if(!PowerHelper.DrawPowerSource(energy,power))throw new InvalidOperationException("原生能源扣电失败");
                MachineProgressHelper.ContinueProgressTypeMachine(printer);
                host.Engine.EditPackageB(s=>{
                    var p=s.Workshop.Advanced.Prints[id];
                    if(economy.Trade.Enabled)NativePackageD.ApplyEnergyCost(s.MusicTrade,id,0,energy.uniqueId,power,before,"print_energy_"+id+"_"+p.Cycles);
                    p.Status="running";p.PauseReason=null;p.EnergySpent+=power;p.Cycles++;
                });
                if(MachineProgressHelper.IsProgressTypeMachineFinished(printer)){
                    var r=WorkshopCatalog.Recipe(record.Recipe);var output=new List<WorkshopEntity>();for(int n=0;n<r.Quantity;n++){var item=DirectoryMaster.Item(r.Output,true);ApplyFacts(item,"ordinary","player","native_print:"+id);output.Add(NativeWorkshop.Store(item));}
                    MachineProgressHelper.FinishProgressTypeMachine(printer);
                    host.Engine.EditPackageB(s=>{var p=s.Workshop.Advanced.Prints[id];p.Pending=output;p.Outputs=output.Select(x=>x.Entity).ToList();p.Materials.Clear();p.Status="ready";if(economy.Trade.Enabled)NativePackageD.TransferOutputCosts(s.MusicTrade,id,host.Day,output.Select(x=>x.Entity));s.Workshop.Advanced.Ledger[id]=new WorkshopLedger{Id=id,Kind="native_print",Actor="player",Entity=record.Machine,Day=host.Day,Result="原生进度完成；耗电 "+p.EnergySpent+"E"};});
                }
            },()=>PowerHelper.SetPowerSourceAt(energy,before));return true;
        }
        private bool PausePrint(string id,string reason){if(A.Prints[id].Status!="paused" || A.Prints[id].PauseReason!=reason){host.Engine.EditPackageB(s=>{s.Workshop.Advanced.Prints[id].Status="paused";s.Workshop.Advanced.Prints[id].PauseReason=reason;});host.Persist();}return false;}
        internal void CancelPrint(string id)
        {
            var record=A.Prints[id];if(record.Status is not ("running" or "paused"))throw new InvalidOperationException("只有未完成打印可取消");
            var machine=economy.FindPlayerItem(record.Machine);economy.MutateNative(()=>{if(machine!=null)MachineProgressHelper.FinishProgressTypeMachine(machine);host.Engine.EditPackageB(s=>{var p=s.Workshop.Advanced.Prints[id];if(p.Cycles==0){p.Pending=new(p.Materials);if(economy.Trade.Enabled)NativePackageD.RestoreInputCosts(s.MusicTrade,id);}p.Materials.Clear();p.Status=p.Pending.Count==0?"cancelled":"return_ready";});},()=>{});
        }
        internal bool ClaimPrint(string id,int entity,bool outputOnly=false)
        {
            var record=A.Prints[id];var e=record.Pending.FirstOrDefault(x=>x.Entity==entity);if(e==null)return false;if(economy.FindPlayerItem(entity)!=null)throw new InvalidOperationException("打印输出实体已在库存");var item=NativeMemberEconomy.Decode(e.Snapshot);bool placed=false;
            var printer=economy.FindPlayerItem(record.Machine);
            var output=printer==null?null:Inventories(printer).LastOrDefault(x=>x.TryCast<GameGridInventory>()!=null);
            var slot=outputOnly?ExactOutputSlot(output,item):null;
            if(outputOnly&&slot==null)return false;
            economy.MutateNative(()=>{
                printingMove=true;try{
                    if(outputOnly){
                        if(slot.TryAcceptOnce(e.Units)!=e.Units||economy.FindPlayerItem(entity)==null)throw new InvalidOperationException("打印输出位置已改变，原件仍待领。");
                    }else if(output==null||!Place(output,item))EmporiumEntry.Instance.TryAddToPlayerInv(item);
                }finally{printingMove=false;}
                placed=economy.FindPlayerItem(entity)!=null;if(placed)host.Engine.EditPackageB(s=>{var p=s.Workshop.Advanced.Prints[id];p.Pending.RemoveAll(x=>x.Entity==entity);if(p.Pending.Count==0)p.Status=record.Status=="return_ready"?"cancelled":"complete";});},()=>{if(placed)item.parentInventory.Expel(item);});return placed;
        }
        partial void UpdatePrinting()
        {
            if(!economy.Workshop.CanClock())return;
            if(NativeBusiness&&Time.unscaledTime>=nextPrintOutput){
                nextPrintOutput=Time.unscaledTime+.5f;
                foreach(var print in A.Prints.Values.Where(x=>x.Status is "ready" or "return_ready").ToArray())
                    foreach(var item in print.Pending.ToArray())ClaimPrint(print.Id,item.Entity,true);
            }
            foreach(var record in A.Prints.Values.Where(x=>x.Status is "running" or "paused").OrderBy(x=>x.Id).ToArray()){
                float elapsed=printSeconds.GetValueOrDefault(record.Machine)+Math.Min(Time.deltaTime,1f);if(elapsed<1){printSeconds[record.Machine]=elapsed;continue;}printSeconds[record.Machine]=elapsed-1;PrintCycle(record.Id);
            }
        }
    }
}
