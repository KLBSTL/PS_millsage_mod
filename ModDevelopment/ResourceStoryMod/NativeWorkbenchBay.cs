using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageB
    {
        private const string BenchSku="ms_machine_electrobench";
        private const string BenchLayoutTag="ms.workbench.bay";
        private const string ExpansionSku="module_bay_expansion_kit";
        private const string BayStageTag="MODULE_UPGRADE_STAGE_INT";
        private static readonly string[] BayMasks={
            "10000010001000001110000010001000001",
            "10000010000000000000000000001000001",
            "00000000000000000000000000000000000"
        };
        private int[] legacyBenchIndices;
        private int decodingBench;
        private (int Width,int Height) toolboxSize;

        private void BuildBench(RepairView view)
        {
            if(toolboxSize.Width==0){
                var original=DirectoryMaster.Item("toolbox",false);
                toolboxSize=(original.shape.width,original.shape.height);
                original.Destroy();
            }
            view.Item.EnableTag(BenchLayoutTag,false);
            view.Host=new GameSlotInventory(4,6,false,false);
            view.Parts=new GameGridInventory(BayMasks[decodingBench>0?2:0],7);
            view.Dock=new GameSlotInventory(toolboxSize.Width,toolboxSize.Height,false,false);
            view.Dock.SetTooltipName("放入一个自有工具箱，箱内已解锁的工具生效");
            var note=new SpritePixelElement("Items/items_tool","postit");
            note.ResizePixels(24,24);
            note.background.image.raycastTarget=true;
            view.Manual=note.background.gameObject.AddComponent<TooltipContainer>();
            view.Window=MachineHelper.CreateMachineryWindow(true,new[]{
                MachineHelper.Panel("器材",view.Host.Cast<PixelElement>(),"host",true),
                MachineHelper.Panel("待装配件／耗材",view.Parts.Cast<PixelElement>(),"module",true),
                MachineHelper.Panel("工具箱",view.Dock.Cast<PixelElement>(),"toolbox",true),
                MachineHelper.IconPanel(MachineryStyle.IconNote,note.Cast<PixelElement>(),"note")
            });
            view.Window.SetTitle(view.Item.name);
            view.Window.canMove=true;
            view.Item.SetContentWindow(view.Window);
            var opening=(System.Action)(()=>{view.Window.EnsureFloatingDock();view.Window.ToFront(true);});
            roots.Add(opening);view.Window.openCallback=opening;

            view.LegacyParts=new GameGridInventory(12,6);
            view.LegacyTools=new GameGridInventory(8,6);
            view.Recovery=new PixelWindow(true,"旧工位取回");
            var row=new GridPixelElement(2,1,false).SetSpacing(6,0);
            row.AttachPos(view.LegacyParts.Cast<PixelElement>(),0,0);
            row.AttachPos(view.LegacyTools.Cast<PixelElement>(),1,0);
            view.Recovery.Attach(row.Cast<PixelElement>());
            var recovery=(System.Action)(()=>{if(!HasRecovery(view))view.Recovery.Hide();});
            roots.Add(recovery);view.Recovery.openCallback=recovery;
            view.Item.SetExamineWindow(view.Recovery);
            RegisterBenchSlot(view,view.Host,"host");
            RegisterBenchSlot(view,view.Parts,"parts");
            RegisterBenchSlot(view,view.Dock,"dock");
            RegisterBenchSlot(view,view.LegacyParts,"recovery");
            RegisterBenchSlot(view,view.LegacyTools,"recovery");
            if(legacyBenchIndices==null){
                var nodes=BenchStorageNodes(view.Item).ToList();
                legacyBenchIndices=new[]{nodes.IndexOf(view.Host),nodes.IndexOf(view.LegacyParts),nodes.IndexOf(view.LegacyTools)};
                if(legacyBenchIndices.Any(x=>x<0))throw new InvalidOperationException("Cannot locate workbench save slots");
            }
        }

        private void RegisterBenchSlot(RepairView view,GameInventory inventory,string role)
            =>repairInventories[inventory.NativeIdentity().ToInt64()]=(view,role);

        private static IEnumerable<GraphNodeStorage> BenchStorageNodes(GameItem item)
        {
            var seen=new HashSet<GraphNodeStorage>();
            var queue=new Queue<GraphNodeStorage>(item.children);
            while(queue.Count>0){
                var node=queue.Dequeue();
                if(node is GameItem||!seen.Add(node))continue;
                yield return node;
                foreach(var child in node.children)queue.Enqueue(child);
            }
        }

        private void RecoverBenchParts(RepairView view)
        {
            foreach(var item in ItemList(view.LegacyParts.childItems).ToArray()){
                var slot=ExactOutputSlot(view.Parts,item);
                if(slot==null||!view.LegacyParts.Expel(item))continue;
                if(slot.TryAcceptOnce(item.unitCount)!=item.unitCount&&!view.LegacyParts.UncheckedAccept(item))
                    throw new InvalidOperationException("旧工位配件未能恢复");
            }
        }
        private static bool HasRecovery(RepairView view)=>view.LegacyParts.childItems.Count>0||view.LegacyTools.childItems.Count>0;
        private static bool IsBenchUpgrade(GameItem kit,GameItem bench)=>kit?.identifier==ExpansionSku&&bench?.identifier==BenchSku;
        private bool CanUpgradeBench(GameItem kit,GameItem bench)=>Enabled&&PlayerOwned(kit)&&PlayerOwned(bench)&&MachineHelper.CanExpand(bench);
        private void UpgradeBench(GameItem kit,GameItem bench)
        {
            if(!CanUpgradeBench(kit,bench))return;
            economy.MutateNative(()=>{
                MachineHelper.ExpandModule(InitRepairItem(bench).Parts,bench);
                bench.SetValue(bench.unitValue+kit.unitValue);
                StartingPerkContent.ConsumeUpgradeMaterial(kit);
            },()=>{});
        }

        private GameItem DockedToolbox(RepairView view)
            =>view.Dock==null?null:ItemList(view.Dock.childItems).SingleOrDefault();
        private IEnumerable<GameSlotInventory> ToolSlots(RepairView view)
        {
            var box=DockedToolbox(view);
            if(box!=null&&PlayerOwned(box))
                return ToolboxHelper.GetToolboxSlots(box).Take(ToolboxHelper.GetUnlockedSlots(box));
            return Array.Empty<GameSlotInventory>();
        }
        private IEnumerable<GameItem> ToolsAt(RepairView view)=>view.Dock==null?ItemList(view.Tools?.childItems):ToolSlots(view).SelectMany(s=>ItemList(s.childItems));
        internal IEnumerable<GameItem> ToolsAt(int machine)=>ToolsAt(InitRepairItem(economy.FindPlayerItem(machine)));
        internal GameInventory[] BenchRecovery(int machine){var view=InitRepairItem(economy.FindPlayerItem(machine));return new GameInventory[]{view.LegacyParts,view.LegacyTools};}
        internal bool BenchHasTool(int machine,int entity)=>economy.FindPlayerItem(machine)?.identifier==BenchSku&&ToolsAt(machine).Any(x=>x.uniqueId==entity);
        private GameInventory ToolReturnSlot(RepairView view,GameItem item)
            =>view.Dock==null?view.Tools:(GameInventory)ToolSlots(view).FirstOrDefault(slot=>ExactOutputSlot(slot,item)!=null)??view.LegacyTools;

        private bool ReturnTool(RepairView view,GameItem tool,Func<GameInventory,bool> put)
        {
            var target=ToolReturnSlot(view,tool);
            if(target==null)return false;
            if(target!=view.LegacyTools)return put(target);
            bool moving=repairMove;
            repairMove=true;
            try{return put(target);}
            finally{repairMove=moving;}
        }

        private void InstallBenchSaveHooks()
        {
            var method=AccessTools.Method(typeof(SaveManager),"DecodeNodesInternal",new[]{typeof(List<SaveItemNode>)});
            core.HarmonyInstance.Patch(method,prefix:new HarmonyMethod(typeof(NativePackageB),nameof(BenchBeforeDecode)),
                postfix:new HarmonyMethod(typeof(NativePackageB),nameof(BenchAfterDecode)),
                finalizer:new HarmonyMethod(typeof(NativePackageB),nameof(BenchDecodeFinished)));
        }
        private static void BenchBeforeDecode(List<SaveItemNode> __0)
        {
            var p=RepairSelf;if(p==null)return;
            p.decodingBench++;
            foreach(var node in __0){
                if(node.identifier==NormalPlayPolicy.BasicInspector)ResizeSavedScanner(node,4,3,3,2);
                else if(node.identifier=="ms_machine_calibrator")ResizeSavedScanner(node,6,4,4,3);
            }
            foreach(var node in __0.Where(n=>n.identifier==BenchSku&&n.itemState?.IsTag(BenchLayoutTag)!=true)){
                if(node.childItemInventoryNode.Count==0)continue;
                if(p.legacyBenchIndices==null){
                    var sample=DirectoryMaster.Item(BenchSku,false);
                    p.InitRepairItem(sample);
                    sample.Destroy();
                }
                // The previous native graph numbered the host, parts and loose tools 3–5.
                for(int i=0;i<node.childItemInventoryNode.Count;i++){
                    int old=node.childItemInventoryNode[i]-3;
                    if(old>=0&&old<3)node.childItemInventoryNode[i]=p.legacyBenchIndices[old];
                }
            }
        }
        private static void ResizeSavedScanner(SaveItemNode node,int oldWidth,int oldHeight,int width,int height)
        {
            if(node.itemShape?.width!=oldWidth||node.itemShape.height!=oldHeight)return;
            node.itemShape.SetDataFill(width,height,1);
            if(node.itemModifiedShape?.width==oldWidth&&node.itemModifiedShape.height==oldHeight)
                node.itemModifiedShape.SetDataFill(width,height,1);
        }
        internal static GameInventory ProbeBenchToolSlot(GameInventory requested,GameItem tool)
        {
            var p=RepairSelf;
            if(p?.core.Test==null||requested==null||tool==null||!p.repairInventories.TryGetValue(requested.NativeIdentity().ToInt64(),out var role)||role.Role!="dock"||tool.identifier=="toolbox")return requested;
            var box=p.DockedToolbox(role.View);
            if(box==null){
                box=DirectoryMaster.Item("toolbox",true);
                GeneralHelper.SetItemOwned(box,true);
                if(!Place(requested,box))throw new InvalidOperationException("Cannot dock test toolbox");
            }
            if(p.BenchHasTool(role.View.Item.uniqueId,tool.uniqueId))return tool.parentInventory;
            return p.ToolSlots(role.View).FirstOrDefault(s=>ExactOutputSlot(s,tool)!=null)
                ??throw new InvalidOperationException("No unlocked test tool slot for "+tool.identifier);
        }
        private static void BenchDecodeFinished(){if(RepairSelf is NativePackageB p)p.decodingBench--;}
        private static void BenchAfterDecode(List<SaveItemNode> __0)
        {
            var p=RepairSelf;if(p==null)return;
            foreach(var node in __0.Where(n=>n.identifier==BenchSku&&n.tempLink!=null)){
                var item=node.tempLink;
                var view=p.InitRepairItem(item);
                item.EnableTag(BenchLayoutTag,false);
                int stage=Math.Max(0,Math.Min(2,item.GetTagReadonly(BayStageTag).GetInt()));
                view.Parts.SetShape(BayMasks[stage],7);
            }
        }
    }
}
