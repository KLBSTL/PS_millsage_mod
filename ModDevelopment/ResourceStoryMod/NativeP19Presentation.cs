using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ResourceStoryMod
{
    // Presentation owns neither inventory coordinates nor saved business facts.
    internal static class NativeP19Presentation
    {
        private sealed class DetailState
        {
            internal string Base, Text, Rendered;
        }
        private static readonly Dictionary<long,DetailState> details=new();
        private static readonly Dictionary<long,PixelWindow> windows=new();
        private static readonly Dictionary<long,(long Shape,GridShape Mask)> masks=new();
        private const string DetailSeparator="\n\n物品记录：\n";
        private static string BaseDescription(string text){int at=text.IndexOf(DetailSeparator,StringComparison.Ordinal);return at<0?text:text.Substring(0,at);}
        private static string run;
        private static float refreshAt;
        private static long ownedPhone;
        private static bool finishPhone;
        internal static int DetailCount=>details.Count;
        internal static int MaskCount=>masks.Count;

        internal static bool Managed(GameItem item)=>item!=null&&item.identifier!=null&&
            item.identifier.StartsWith("ms_",StringComparison.Ordinal)&&
            !item.identifier.StartsWith("ms_test_",StringComparison.Ordinal)&&
            !item.identifier.StartsWith("ms_p0_choice_",StringComparison.Ordinal);

        internal static void Install(Core core)
        {
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(Image),"IsRaycastLocationValid"),prefix:new HarmonyMethod(typeof(NativeP19Presentation),nameof(ItemRaycast)));
            foreach(string name in new[]{"ValidateSelectLocked","ValidatePartialSprite"})
                core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(GameItemElement),name),postfix:new HarmonyMethod(typeof(NativeP19Presentation),nameof(ArtworkValidated)));
            foreach(var m in AccessTools.GetDeclaredMethods(typeof(PixelWindow)).Where(m=>m.Name=="Show"))
                core.HarmonyInstance.Patch(m,postfix:new HarmonyMethod(typeof(NativeP19Presentation),nameof(WindowShown)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(PixelWindow),"Hide"),postfix:new HarmonyMethod(typeof(NativeP19Presentation),nameof(WindowHidden)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(PhoneUIManager),"HandleCall"),prefix:new HarmonyMethod(typeof(NativeP19Presentation),nameof(Calling)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(DialogUIManager),"EndConversation"),postfix:new HarmonyMethod(typeof(NativeP19Presentation),nameof(ConversationEnded)));
        }

        private static void ArtworkValidated(GameItemElement __instance)
        {
            if(!Managed(__instance)||__instance.spriteAtlasPath!="resource_story"||__instance.spritePath!="item"||__instance.background?.image==null)return;
            Sprite expected=null;
            if(NativeP2ItemArt.Resolve(__instance,ref expected)&&NativePlaceholderArt.Resolve(__instance,ref expected)||expected==null)return;
            // The factory's first native refresh can retain the shared 32px
            // atlas image. Repair only that sprite at the native refresh point;
            // never write inventory positions, rotations or shapes here.
            foreach(var node in new[]{__instance.background,__instance.fakeBackground}){
                var image=node?.image;if(image==null||image.sprite?.NativeIdentity()==expected.NativeIdentity())continue;
                image.sprite=expected;image.SetNativeSize();
            }
        }

        private static bool ItemRaycast(Image __instance,Vector2 __0,Camera __1,ref bool __result)
        {
            var node=__instance.GetComponent<TreeNodeRender>();
            GameItemElement item=null;
            for(int depth=0;node!=null&&depth<16;depth++,node=node.parent){
                var candidate=node.element?.TryCast<GameItemElement>();
                if(candidate?.background?.image?.NativeIdentity()==__instance.NativeIdentity()){item=candidate;break;}
            }
            if(!Managed(item)||item.background?.image?.NativeIdentity()!=__instance.NativeIdentity())return true;
            __result=Hit(item,__instance,__0,__1);return false;
        }
        internal static bool Hit(GameItemElement item,Image image,Vector2 point,Camera camera)
        {
            var shape=item.modifiedShape??item.shape;
            if(shape==null||!RectTransformUtility.ScreenPointToLocalPointInRectangle(image.rectTransform,point,camera,out Vector2 local))return false;
            var rect=image.rectTransform.rect;
            if(rect.width<=0||rect.height<=0||local.x<rect.xMin||local.x>=rect.xMax||local.y<rect.yMin||local.y>=rect.yMax)return false;
            long key=item.NativeIdentity().ToInt64();
            if(!masks.TryGetValue(key,out var saved)||saved.Shape!=shape.NativeIdentity().ToInt64()){
                // The native image's parent already applies orientation/flip.
                // Its inverse transform therefore maps to the unrotated mask.
                saved=(shape.NativeIdentity().ToInt64(),new GridShapeBuilder(shape).SetOrientation(0).SetFlipped(false).SetPosition(0,0).Build());masks[key]=saved;
            }
            int x=(int)((local.x-rect.xMin)*saved.Mask.width/rect.width);
            int y=(int)((rect.yMax-local.y)*saved.Mask.height/rect.height);
            return x>=0&&x<saved.Mask.width&&y>=0&&y<saved.Mask.height&&saved.Mask.GetLocal(x,y)!=0;
        }

        internal static void Detail(GameItem item)
        {
            if(!Managed(item))return;
            long key=item.NativeIdentity().ToInt64();string text=item.customText??"",description=item.longDescription??"";
            if(!details.TryGetValue(key,out var state)){
                if(text.Length==0)return;
                details[key]=state=new DetailState{Base=BaseDescription(description),Text=text};
            }else{
                if(description==state.Rendered&&(text.Length==0||text==state.Text)){if(text.Length>0)item.customText="";return;}
                // A new authorized description replaces the old disclosure.
                if(description!=state.Rendered){state.Base=BaseDescription(description);state.Text="";}
                if(text.Length>0)state.Text=text;
            }
            string full=state.Base;
            if(!string.IsNullOrWhiteSpace(state.Text)&&!full.Contains(state.Text,StringComparison.Ordinal))full+=DetailSeparator+state.Text;
            state.Rendered=full;
            if(item.longDescription!=full)item.longDescription=full;
            if(text.Length>0)item.customText="";
        }
        private static void WindowShown(PixelWindow __instance)
        {
            if(__instance.parentItems?.Count>0)windows[__instance.NativeIdentity().ToInt64()]=__instance;
        }
        private static void WindowHidden(PixelWindow __instance)=>windows.Remove(__instance.NativeIdentity().ToInt64());
        internal static bool InteractionBlocked=>WildUIManager.Instance?.panel?.activeInHierarchy==true||
            PaperUIManager.Instance?.panel?.activeInHierarchy==true||
            PhoneUIManager.Instance?.phonePanel?.activeInHierarchy==true||
            windows.Values.Any(w=>w.handler?.gameObject?.activeInHierarchy==true);

        private static void Calling(long __0)
        {
            ownedPhone=__0==8670200||__0==8670201||Core.Instance?.Story?.Economy?.Business?.OwnsPhone(__0)==true?__0:0;
            finishPhone=false;
        }
        private static void ConversationEnded(){if(ownedPhone!=0)finishPhone=true;}
        internal static void LateUpdate()
        {
            if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="InventoryScene"){
                run=null;details.Clear();masks.Clear();windows.Clear();ownedPhone=0;finishPhone=false;return;
            }
            var store=PlayerStore.instance;
            string current=store?.runID;
            if(run!=current){run=current;details.Clear();masks.Clear();windows.Clear();ownedPhone=0;finishPhone=false;refreshAt=0;}
            var phone=PhoneUIManager.Instance;
            if(finishPhone&&phone!=null){
                if(phone.currentNumber!=ownedPhone){ownedPhone=0;finishPhone=false;}
                else if(DialogUIManager.Instance?.isConversationActiveFlag==0&&DialogUIManager.Instance.currentDialog==null){
                    ownedPhone=0;finishPhone=false;phone.StopCall();phone.CloseUI();
                }
            }
            var economy=Core.Instance?.Story?.Economy;
            if(economy==null||store==null||Time.unscaledTime<refreshAt)return;
            refreshAt=Time.unscaledTime+.25f;
            var live=new HashSet<long>();
            foreach(var item in economy.AllPlayerItems())if(Managed(item)){live.Add(item.NativeIdentity().ToInt64());Detail(item);}
            foreach(var inventory in new[]{EmporiumEntry.Instance?.frontInvinvElement,EmporiumEntry.Instance?.backInvinvElementCounter,store.currentClientInstance?.gridInv})
                if(inventory?.childItems!=null)foreach(var item in inventory.childItems)if(Managed(item)){live.Add(item.NativeIdentity().ToInt64());Detail(item);}
            foreach(long key in details.Keys.Where(k=>!live.Contains(k)).ToArray())details.Remove(key);
            foreach(long key in masks.Keys.Where(k=>!live.Contains(k)).ToArray())masks.Remove(key);
            foreach(long key in windows.Where(p=>p.Value.handler?.gameObject==null||!p.Value.handler.gameObject.activeInHierarchy).Select(p=>p.Key).ToArray())windows.Remove(key);
        }
    }
}
