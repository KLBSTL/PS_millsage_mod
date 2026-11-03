using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    // No workflow window: counter visits use the existing customer; night visits
    // use the same dialogue/choices over a non-interactive scene backdrop.
    internal sealed class NativeActOneCampaign
    {
        private readonly Core core;
        private readonly NativeStoryHost host;
        private readonly NativeActOneStage stage;
        internal bool Enabled=>host.Native012.Enabled&&(core.Test==null||core.Test.Request.ActOneExtension||core.Test.Request.ActTwoExtension);
        private bool Allowed(string scene)=>(!DiaryPresentation.Scene(scene)||host.Diary.Enabled)&&(!ReturnPresentation.Scene(scene)||host.Return?.Enabled==true);
        internal bool NativeScene=>Enabled&&ActOnePresentation.NativeScene(host.Engine?.Frame()?.Scene.Id)&&Allowed(host.Engine?.Frame()?.Scene.Id);
        internal bool NightScene=>NativeScene&&host.Engine.Frame().Scene.Context=="night";
        internal bool NightContext=>NightScene&&host.Integration.InVenue&&PlayerStore.instance?.storeState==PlayerStore.StoreState.CLOSED;
        internal bool OwnCounterVisit=>Enabled&&host.Opening.VisitScene is string scene&&ActOnePresentation.NativeScene(scene)&&!ActOnePresentation.NativeNight(scene)&&Allowed(scene);
        internal bool WaitingForDays=>Enabled&&host.Engine.PresentationBookmark?.NotBeforeDay>host.Day;
        internal bool Later005Visit=>Enabled&&host.Engine.Frame()?.Scene.Id=="ms_scene_005"&&(host.Engine.Frame().Node.Id=="line_009"||(host.Engine.Frame().Node.Id is "line_008a" or "line_008b")&&host.Engine.PresentationBookmark?.DialoguePart>=1);
        internal void SuspendVisit()
        {
            core.Presentation.Stop(true);host.Persist();
            if(host.Integration.IsOwnCurrent&&PlayerStore.instance.isClientArrived)PlayerStore.instance.DismissCurrentClient(false,false,true);
        }
        internal NativeActOneCampaign(Core core,NativeStoryHost host)
        {
            this.core=core;this.host=host;stage=new NativeActOneStage(core);
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(DialogUIManager),"DisplayClientText"),prefix:new HarmonyMethod(typeof(NativeActOneCampaign),nameof(NightText)));
        }
        internal bool Attach()
        {
            if(!Enabled||host.ReadState.CampaignItems.ActOne!=null)return false;
            host.Engine.EditCampaignItems(PackageC.PrepareActOne);return true;
        }
        internal bool CanStart(string scene)
        {
            if(!Enabled||scene!="ms_scene_005"||host.ReadState.CampaignItems.Opening==null)return true;
            var original=host.ReadState.CampaignItems.Originals.GetValueOrDefault("c_synth");
            var actual=original==null?null:host.Economy.FindPlayerItem(original.Entity);
            // A protected story original is intentionally excluded from ordinary
            // retail/workshop ownership. Audition uses its explicit custody.
            return actual!=null&&actual.identifier==original.Sku&&original.Custodian=="player"&&original.Location=="native"&&GeneralHelper.IsItemOwned(actual);
        }
        internal void Frame(StoryFrame frame)
        {
            if(frame?.Scene.Context=="night"&&ActOnePresentation.NativeNight(frame.Scene.Id)){
                var page=core.Presentation?.Cursor?.Page();
                stage.Show(frame,page?.SpeakerId,page?.DialoguePart??0);
            }
        }
        internal void Update()
        {
            if(!Enabled){stage.Dispose();return;}
            if(host.Return?.FinalShop==true&& !host.ReportBlocked&&PlayerStore.instance.lockStorePhysicalFlag==0)host.Integration.RecoverFinaleBackstage();
            if(!host.Integration.InVenue){stage.Dispose();return;}
            // Native leave callbacks can reopen the map after the custom
            // destination was selected. The venue owns this interval; calling
            // CloseUI would dispatch STORE again instead of just hiding it.
            if(MapUIManager.Instance.mapPanel.activeInHierarchy)MapUIManager.Instance.mapPanel.SetActive(false);
            if(MapUIManager.Instance.inTransit||Time.timeScale<=0||PlayerStore.instance.lockStorePhysicalFlag!=0)return;
            var frame=host.Engine.Frame();
            if(frame==null){
                if(host.Integration.CompletedNativeNight){FinishNight();return;}
                var scene=host.Engine.Eligible("night",host.Day).FirstOrDefault();
                if(scene!=null&&Allowed(scene.Id)&&ActOnePresentation.NativeNight(scene.Id)&&host.CanOpen("night")){host.Integration.BindNativeNight(scene.Id);host.Start(scene.Id);}
                return;
            }
            if(!NightContext)return;
            host.Integration.BindNativeNight(frame.Scene.Id);
            Frame(frame);
            if(Time.timeScale<=0||MapUIManager.Instance.mapPanel.activeInHierarchy)return;
            if(core.Presentation.Paused)core.Presentation.Resume();
            else if(!core.Presentation.Active&&host.CanOpen("night"))host.OpenView();
        }
        internal void FinishNight(){stage.Dispose();host.Integration.ReturnFromVenue();}
        internal void Layout()=>stage.Layout();
        internal object StageStatus()=>stage.Status();
        internal bool StageActive=>stage.Active;
        internal bool StageRestored=>stage.Restored;
        internal void Detach()=>stage.Dispose();
        private static bool NightText(DialogUIManager __instance,Dialogue __0)
        {
            var core=Core.Instance;var self=core?.Story?.ActOne;
            if(self?.NightContext!=true||core.Presentation?.HasDialogue(__0)!=true)return true;
            // Vanilla DisplayClientText reads the shop's current customer. At a
            // night venue there is none. Keep its bubble/typewriter/history path
            // without manufacturing a counter customer or changing native flags.
            __instance.HideBubbles();__instance.clientBubble.typewriter.StopShowingText();
            __instance.clientNameBubble.SetActive(true);
            string name=core.Presentation.Cursor?.Page()?.SpeakerName??"旁白（测试）";
            __instance.clientNameTMP.text=name;
            __instance.clientBubble.gameObject.SetActive(true);
            __instance.clientBubble.typewriter.ShowText(__0.plainText);
            __instance.UpdateDialogHistory(name+"："+__0.plainText);
            return false;
        }
    }
}
