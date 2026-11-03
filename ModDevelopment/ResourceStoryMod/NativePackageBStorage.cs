using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageB
    {
        private static readonly HashSet<string> documentIds=new();
        internal static IReadOnlyCollection<string> DocumentIds=>documentIds;
        internal static bool IsBox(GameItem item)=>item?.identifier is "ms_case_parts_box" or "ms_case_document_box" or "ms_case_guitar_case";
        private void InstallStorageHooks()
        {
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(GameItem),"MayHaveValidInventorySlot"),prefix:new HarmonyMethod(typeof(NativePackageB),nameof(BeforeMachineContainerMay)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(GameItem),"TryFindOneValidInventorySlot"),prefix:new HarmonyMethod(typeof(NativePackageB),nameof(BeforeMachineContainerSlot)));
            foreach(var type in new[]{typeof(GameGridInventory),typeof(GameSlotInventory)}){
                var method=AccessTools.DeclaredMethod(type,"UncheckedAccept",new[]{typeof(GameItem)});if(method!=null)core.HarmonyInstance.Patch(method,prefix:new HarmonyMethod(typeof(NativePackageB),nameof(BeforeAccept)));
                var may=AccessTools.DeclaredMethod(type,"MayHaveValidInventorySlot",new[]{typeof(GameItem)});if(may!=null)core.HarmonyInstance.Patch(may,prefix:new HarmonyMethod(typeof(NativePackageB),nameof(BeforeAccept)));
            }
            core.HarmonyInstance.Patch(AccessTools.DeclaredMethod(typeof(GameGridInventory),"CheckInventorySlotStillValid",new[]{typeof(GameItem),typeof(GridShape),typeof(TagSystem)}),postfix:new HarmonyMethod(typeof(NativePackageB),nameof(AfterBoxValidity)));
            core.HarmonyInstance.Patch(AccessTools.DeclaredMethod(typeof(GameGridInventory),"TryFindOneValidInventorySlot",new[]{typeof(GameItem)}),postfix:new HarmonyMethod(typeof(NativePackageB),nameof(AfterFindBoxSlot)));
            core.HarmonyInstance.Patch(AccessTools.DeclaredMethod(typeof(GameGridInventory),"CheckShapePlacement",new[]{typeof(GameItem),typeof(GridShape),typeof(System.Collections.Generic.List<GameItem>)}),postfix:new HarmonyMethod(typeof(NativePackageB),nameof(AfterBoxShapePlacement)));
            core.HarmonyInstance.Patch(AccessTools.DeclaredMethod(typeof(GameGridInventory),"GetGridPosition",new[]{typeof(UnityEngine.Vector2),typeof(GridShape)}),postfix:new HarmonyMethod(typeof(NativePackageB),nameof(AfterBoxGridPosition)));
            foreach(string name in new[]{"TryAcceptOnce","AcceptUnchecked"})core.HarmonyInstance.Patch(AccessTools.DeclaredMethod(typeof(SlotMarker),name),prefix:new HarmonyMethod(typeof(NativePackageB),nameof(BeforeSlotAccept)));
        }
        private static bool BeforeMachineContainerMay(GameItem __instance,GameItem __0,ref bool __result)
        {if(!IsRepairMachine(__instance)||!IsRepairMachine(__0))return true;__result=false;return false;}
        private static bool BeforeMachineContainerSlot(GameItem __instance,GameItem __0,ref SlotMarker __result)
        {if(!IsRepairMachine(__instance)||!IsRepairMachine(__0))return true;__result=null;return false;}
        private static bool BeforeSlotAccept(SlotMarker __instance,ref int __result)
        {
            if(!RepairSlotAccept(__instance,ref __result))return false;
            if(__instance?.inventory==null || __instance.item==null)return true;
            if(Core.Instance?.Story?.Diary?.Accept(__instance.inventory,__instance.item)==false){__result=0;return false;}
            if(Core.Instance?.Story?.Return?.StorageAccept(__instance.inventory,__instance.item)==false){__result=0;return false;}
            var box=InventoryOwner(__instance.inventory);if(!IsBox(box))return true;
            if(!CanContain(box,__instance.inventory,__instance.item) || (box.identifier=="ms_case_guitar_case" && !GuitarPosition(box,__instance.inventory,__instance.item,__instance.itemGridShape))){__result=0;return false;}
            return true;
        }
        private static void AfterBoxShapePlacement(GameGridInventory __instance,GameItem __0,GridShape __1,ref bool __result)
        {
            if(__result && InventoryOwner(__instance)?.identifier=="ms_case_guitar_case")__result=GuitarPosition(InventoryOwner(__instance),__instance,__0,__1);
        }
        private static void AfterBoxGridPosition(GameGridInventory __instance,ref GridShape __result)
        {
            var drag=ItemMouseDragHandler.current;var item=drag?.currentItem;
            if(__result==null||drag?.isDragging!=true||item?.parentInventory?.NativeIdentity()!=__instance.NativeIdentity()||!IsGuitar(item)||InventoryOwner(__instance)?.identifier!="ms_case_guitar_case"||GuitarRegion(__result)>=0)return;
            int w=__result.globalWidth,h=__result.globalHeight;if(w<1||w>3||h<1||h>7)return;
            float x=__result.minX+w/2f,y=__result.minY+h/2f;if(x<0||x>=6||y<0||y>=14)return;
            // Use the same candidate for native highlighting and EndDrag. A
            // rotation can leave its center one cell off a fixed 3x7 berth;
            // snapping never bypasses occupancy, permissions or native validity.
            __result=new GridShapeBuilder(__result).SetPosition((int)(x/3)*3,(int)(y/7)*7).Build();
        }
        private static void AfterFindBoxSlot(GameGridInventory __instance,GameItem __0,ref SlotMarker __result)
        {
            var box=InventoryOwner(__instance);if(!IsBox(box) || __result==null)return;
            if(!CanContain(box,__instance,__0)){__result=null;return;}
            if(box.identifier!="ms_case_guitar_case" || GuitarPosition(box,__instance,__0,__result.itemGridShape))return;
            for(int orientation=0;orientation<4;orientation++)for(int y=0;y<16;y++)for(int x=0;x<6;x++){
                var shape=new GridShapeBuilder(__0.shape).SetOrientation(orientation).SetPosition(x,y).Build();
                if(GuitarPosition(box,__instance,__0,shape) && __instance.CheckInventorySlotStillValid(__0,shape,__0.state)){__result.itemGridShape=shape;return;}
            }
            __result=null;
        }
        private static void AfterBoxSlot(GameGridInventory __instance,GameItem __0,ref SlotMarker __result)
        {
            if(__result==null)return;var box=InventoryOwner(__instance);if(IsBox(box) && (!CanContain(box,__instance,__0) || (box.identifier=="ms_case_guitar_case" && !GuitarPosition(box,__instance,__0,__result.itemGridShape))))__result=null;
        }
        private static void AfterBoxValidity(GameGridInventory __instance,GameItem __0,GridShape __1,ref bool __result)
        {
            RepairPlacement(__instance,__0,ref __result);
            if(!__result)return;var box=InventoryOwner(__instance);if(IsBox(box) && (!CanContain(box,__instance,__0) || (box.identifier=="ms_case_guitar_case" && !GuitarPosition(box,__instance,__0,__1))))__result=false;
        }
        private static bool BeforeAccept(GameInventory __instance,GameItem __0,ref bool __result)
        {
            var diary=Core.Instance?.Story?.Diary?.Accept(__instance,__0);if(diary==false){__result=false;return false;}
            if(Core.Instance?.Story?.Return?.StorageAccept(__instance,__0)==false){__result=false;return false;}
            if(!RepairBeforeAccept(__instance,__0,ref __result))return false;
            if(__0==null)return true;var owner=InventoryOwner(__instance);
            // Native save/load temporarily wraps root inventories in these
            // synthetic items. They are not physical backpacks or nested boxes.
            bool saveRoot=owner?.identifier is "save_bag" or "back_inv_save_bag" or "showcase_save_bag" or "sold_save_bag" or "hidden_save_bag";
            if((IsBox(__0) && owner!=null && !saveRoot) || (IsBox(owner) && !CanContain(owner,__instance,__0))){__result=false;return false;}return true;
        }
        internal static GameItem InventoryOwner(GameInventory inv)
        {
            var seen=new HashSet<long>();GameItem Search(GraphNodeStorage n){if(n==null || !seen.Add(n.NativeIdentity().ToInt64()))return null;var item=n.TryCast<GameItem>();if(item!=null)return item;if(n.parents!=null)foreach(var p in n.parents){var found=Search(p);if(found!=null)return found;}return null;}
            if(inv.parents!=null)foreach(var p in inv.parents){var item=Search(p);if(item!=null)return item;}return null;
        }
        internal void InitBox(GameItem item)
        {
            bool guitar=item.identifier=="ms_case_guitar_case";var pair=DirectoryUtils.CreateInventoryWindow(guitar?6:12,guitar?16:6,true);var grid=pair.Item2.Cast<GameGridInventory>();
            pair.Item1.SetTitle(guitar?"四琴运输箱":item.name+" · 12×6");item.SetContentWindow(pair.Item1);
            item.longDescription=item.identifier switch {
                "ms_case_parts_box"=>"外格4×3，内格12×6。收纳电声配件、材料、模组、芯片及不超过3×2（可旋转）的小工具。不能嵌套箱具；装有物品时不能出售，机器不能在箱内运行。",
                "ms_case_document_box"=>"外格4×4，内格12×6，最多18份合规纸张或介质，仍须实际放得下。保留原件物权与阅读授权；不删除、不复制档案，不能嵌套箱具或存放任意器材。",
                _=>"外格4×8，内格6×16。上方四个3×7琴位，每格一把合规吉他；同箱拖动会吸附到指向的琴位，仍须保持能放入琴位的朝向。底部6×2放线材、套弦和兼容小配件。不能跨琴位，不能装贝斯、鼓组、机器或其它箱具。装有物品时不能出售。"
            };
            ContainerHelper.InitContainerItemTagOnly(grid,item);
            // Native inventory combiners use Delegate.DynamicInvoke, which does
            // not support Il2Cpp-to-managed delegate targets here. Permission and
            // layout checks therefore wrap the real native slot methods above.
        }
        internal static bool IsGuitar(GameItem item)=>new[]{"guitar","electric_guitar","ms_gear_commercial_guitar","ms_nagi_whale_guitar","ms_natsume_spinel_guitar"}.Contains(item.identifier);
        private static bool Accessory(GameItem item)=>new[]{"ms_item_balanced_cable","ms_module_quick_patch","ms_part_patch_jack","guitar_string","guitar_strings","guitar_cable","instrument_cable","ms_story_diamond_pick","ms_item_standard_strings","ms_strings_bass5_nickel","ms_strings_guitar6_tension"}.Contains(item.identifier);
        internal static bool CanContain(GameItem box,GameInventory inv,GameItem item)
        {
            if(box==null || item==null || box.uniqueId==item.uniqueId || IsBox(item) || Inventories(item).Any()&&!PackageBCatalog.Rows("equipment_package_b").Any(x=>x["id"]==item.identifier))return false;
            var p=WorkshopCatalog.Product(item.identifier);var shape=item.modifiedShape??item.shape;int w=shape.width,h=shape.height;
            if(box.identifier=="ms_case_parts_box"){
                bool tool=new[]{"screwdriver","wire_cutter","welder","magnifier"}.Contains(item.identifier) || item.itemTypes.Contains("TOOL");
                if(tool)return (w<=3 && h<=2) || (h<=3 && w<=2);
                return p?.Kind is "material" or "part" or "module" or "blueprint" || NativeWorkshop.References.Where(x=>!new[]{"ref_screwdriver","ref_wirecutter","ref_welder","ref_magnifier"}.Contains(x.Key)).Any(x=>x.Value==item.identifier) || item.itemTypes.Contains("MATERIAL");
            }
            if(box.identifier=="ms_case_document_box")return (documentIds.Contains(item.identifier) || new[]{"ms_story_handdrawn_flyer","ms_story_crew_pass","ms_story_keybed_sketch","ms_story_hotaru_note","ms_story_departure_receipt","ms_story_prelim_master_tape","ms_story_final_master_tape"}.Contains(item.identifier)) && (ItemList(inv.childItems).Any(x=>x.uniqueId==item.uniqueId) || inv.childItems.Count<18);
            if(box.identifier=="ms_case_guitar_case")return IsGuitar(item)?(((w<=3 && h<=7) || (h<=3 && w<=7)) && (ItemList(inv.childItems).Any(x=>x.uniqueId==item.uniqueId) || ItemList(inv.childItems).Count(IsGuitar)<4)):Accessory(item);
            return false;
        }
        private static int GuitarRegion(GridShape shape)
        {
            int x=shape.minX,y=shape.minY,w=shape.globalWidth,h=shape.globalHeight;
            for(int row=0;row<2;row++)for(int col=0;col<2;col++)if(x>=col*3 && x+w<=col*3+3 && y>=row*7 && y+h<=row*7+7)return row*2+col;
            return -1;
        }
        private static bool GuitarPosition(GameItem box,GameInventory inv,GameItem item,GridShape shape)
        {
            if(shape==null)return false;
            if(!IsGuitar(item))return Accessory(item) && shape.minX>=0 && shape.maxX<6 && shape.minY>=14 && shape.maxY<16;
            int slot=GuitarRegion(shape);if(slot<0)return false;
            return !ItemList(inv.childItems).Any(x=>x.uniqueId!=item.uniqueId && IsGuitar(x) && GuitarRegion(x.modifiedShape??x.shape)==slot);
        }
        internal void BindDocuments()
        {
            var dir=UnityEngine.Resources.FindObjectsOfTypeAll(NativeTypes.Of<MiscItemDirectory>()).FirstOrDefault();if(dir==null)return;
            var directory=dir.Cast<MiscItemDirectory>();
            foreach(var row in PackageBCatalog.Rows("document_permissions")){
                string name=row["factory_reference"].Split('.').Last();var method=typeof(MiscItemDirectory).GetMethod(name,System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static,null,Type.EmptyTypes,null);if(method==null)continue;
                GameItem sample;try{sample=(GameItem)method.Invoke(directory,null);}catch{continue;}if(sample==null || string.IsNullOrWhiteSpace(sample.name))continue;
                var matches=new List<string>();foreach(var p in directory.factoryDictionary){var candidate=DirectoryMaster.Item(p.Key,true);if(candidate!=null && candidate.name==sample.name && candidate.spritePath==sample.spritePath && candidate.spriteAtlasPath==sample.spriteAtlasPath)matches.Add(p.Key);}
                if(matches.Count==1)documentIds.Add(matches[0]);
            }
        }
        internal bool BlocksSale(GameItem item)
        {
            if(!Enabled || item==null)return false;
            if(item.identifier==RepairNoteSku||RepairInstalled(item))return true;
            var all=new[]{item}.Concat(ItemList(item.FindAllChildItems())).ToArray();
            foreach(var e in all){
                if(A.Services.Values.Any(s=>s.Host==e.uniqueId&&!s.Terminal&&s.Delivery==null))return true;
                if(e.IsTag(MusicCatalog.ProtectedTag) || (A.Facts.TryGetValue(e.uniqueId,out var f) && f.Owner!="player"))return true;
                if(IsBox(e) && e.FindAllChildItems().Count>0)return true;
                if(MachineBusy(e.uniqueId) || PrintBusy(e.uniqueId) || ChipInUse(e.uniqueId) || A.References.GetValueOrDefault(e.uniqueId)?.Reference>0)return true;
            }
            return false;
        }
        internal bool Archive(int entity)
        {
            if(economy.FindPlayerItem(entity)==null)throw new InvalidOperationException("原件不在玩家库存或箱内");
            throw new InvalidOperationException("当前没有具备发行ID、版本、可再生和任务完结证明的可回收副本；此原件只收纳，不删除、不发物品或现金");
        }
        internal static IEnumerable<GameItem> ItemList(System.Collections.Generic.List<GameItem> items){if(items!=null)foreach(var item in items)yield return item;}
    }
}
