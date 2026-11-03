using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ResourceStoryMod.Story;
using UnityEngine;
using UnityEngine.UI;

namespace ResourceStoryMod
{
    // P0 is opt-in through the isolated test command. No new gameplay panel is drawn.
    internal sealed class NativeStoryPresentation
    {
        private readonly Core core;
        private readonly NativePresentationPortrait portrait;
        private readonly NativeStoryVoice voice;
        private StoryPresentationCursor cursor;
        private StoreClient client;
        private readonly List<System.Delegate> callbacks=new();
        private readonly HashSet<long> dialogues=new();
        private StoryPresentationPage shown,pending;
        private string pendingChoice;
        private Dialogue current;
        private bool closing,reportPausedVoice,quitting,redisplayRejected;
        private int epoch;
        private sealed class BubbleLayout
        {
            internal DialogBubbleWidth Width;internal float Max;
            internal TMPro.TextMeshProUGUI Text;internal bool Wrap;
        }
        private readonly Dictionary<long,BubbleLayout> bubbleLayouts=new();
        private sealed class TextLayout
        {
            internal TMPro.TextMeshProUGUI Text;internal LayoutElement Layout;
            internal bool Wrap,Enabled;internal int Priority;internal float MinWidth,PreferredWidth,FlexibleWidth,MinHeight,PreferredHeight,FlexibleHeight,LastHeight;internal Vector2 Size,AnchorMin,AnchorMax,Pivot,Position;internal string Rendered;
        }
        private readonly Dictionary<long,TextLayout> textLayouts=new();
        private void FitAuthoredBubbles()
        {
            if(!Active||Night||core.Story?.Package.CampaignProfile!=PackageC.ReturnProfile)return;
            var ui=DialogUIManager.Instance;var roots=new List<Transform>{ui.clientBubble.transform,ui.playerBubble.transform};foreach(var choice in ui.dialogChoiceBubbles)roots.Add(choice.transform);
            foreach(var root in roots)foreach(var width in root.GetComponentsInChildren<DialogBubbleWidth>(true)){
                if(width.tmp==null)continue;long key=width.NativeIdentity().ToInt64();if(!bubbleLayouts.ContainsKey(key))bubbleLayouts[key]=new(){Width=width,Max=width.maxWidth,Text=width.tmp,Wrap=width.tmp.enableWordWrapping};
                if(width.maxWidth!=200||!width.tmp.enableWordWrapping){width.maxWidth=200;width.tmp.enableWordWrapping=true;width.LateUpdate();if(width.layoutElement!=null)LayoutRebuilder.ForceRebuildLayoutImmediate(width.layoutElement.transform.Cast<RectTransform>());}
            }
            var texts=new List<TMPro.TextMeshProUGUI>{ui.clientBubble.textfield,ui.playerBubble.textfield};foreach(var choice in ui.dialogChoiceBubbles)texts.Add(choice.TMP);
            foreach(var text in texts){
                long key=text.NativeIdentity().ToInt64();if(!textLayouts.TryGetValue(key,out var saved)){
                    var layout=text.GetComponent<LayoutElement>();bool existed=layout!=null;layout??=text.gameObject.AddComponent<LayoutElement>();
                    bool wrap=bubbleLayouts.Values.FirstOrDefault(b=>b.Text?.NativeIdentity()==text.NativeIdentity())?.Wrap??text.enableWordWrapping;
                    saved=new(){Text=text,Layout=layout,Wrap=wrap,Enabled=existed&&layout.enabled,Priority=layout.layoutPriority,MinWidth=layout.minWidth,PreferredWidth=layout.preferredWidth,FlexibleWidth=layout.flexibleWidth,MinHeight=layout.minHeight,PreferredHeight=layout.preferredHeight,FlexibleHeight=layout.flexibleHeight,Size=text.rectTransform.sizeDelta,AnchorMin=text.rectTransform.anchorMin,AnchorMax=text.rectTransform.anchorMax,Pivot=text.rectTransform.pivot,Position=text.rectTransform.anchoredPosition};textLayouts[key]=saved;
                }
                if(saved.Rendered==text.text&&text.enableWordWrapping&&saved.Layout.enabled&&Math.Abs(text.rectTransform.rect.width-200)<.05f&&Math.Abs(text.rectTransform.rect.height-saved.LastHeight)<.05f)continue;
                text.enableWordWrapping=true;var le=saved.Layout;le.enabled=true;le.layoutPriority=100;le.minWidth=le.preferredWidth=200;le.flexibleWidth=0;
                float height=Math.Max(12,text.GetPreferredValues(text.text,200,float.PositiveInfinity).y);le.minHeight=le.preferredHeight=height;le.flexibleHeight=0;
                text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,200);text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,height);
                var parent=text.transform.parent.TryCast<RectTransform>();if(parent!=null)LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
                saved.Rendered=text.text;saved.LastHeight=height;
            }
        }
        private void RestoreBubbleLayout()
        {
            foreach(var saved in bubbleLayouts.Values){if(saved.Width!=null)saved.Width.maxWidth=saved.Max;if(saved.Text!=null)saved.Text.enableWordWrapping=saved.Wrap;}bubbleLayouts.Clear();
            foreach(var s in textLayouts.Values){if(s.Text!=null){s.Text.enableWordWrapping=s.Wrap;var r=s.Text.rectTransform;r.anchorMin=s.AnchorMin;r.anchorMax=s.AnchorMax;r.pivot=s.Pivot;r.sizeDelta=s.Size;r.anchoredPosition=s.Position;}if(s.Layout!=null){s.Layout.minWidth=s.MinWidth;s.Layout.preferredWidth=s.PreferredWidth;s.Layout.flexibleWidth=s.FlexibleWidth;s.Layout.minHeight=s.MinHeight;s.Layout.preferredHeight=s.PreferredHeight;s.Layout.flexibleHeight=s.FlexibleHeight;s.Layout.layoutPriority=s.Priority;s.Layout.enabled=s.Enabled;}}textLayouts.Clear();
            foreach(var s in choiceRoots.Values)if(s.Rect!=null){s.Rect.anchorMin=s.Min;s.Rect.anchorMax=s.Max;s.Rect.pivot=s.Pivot;s.Rect.sizeDelta=s.Size;s.Rect.anchoredPosition=s.Position;}choiceRoots.Clear();
        }
        private sealed class ChoiceRoot {internal RectTransform Rect;internal Vector2 Min,Max,Pivot,Size,Position;}
        private readonly Dictionary<long,ChoiceRoot> choiceRoots=new();
        private void PositionChoices()
        {
            if(!Active||core.Story?.Package.CampaignProfile!=PackageC.ReturnProfile||shown?.Frame.Node.Kind!="choice")return;
            var ui=DialogUIManager.Instance;var list=new List<DialogChoiceBubble>();foreach(var b in ui.dialogChoiceBubbles)if(b.gameObject.activeInHierarchy)list.Add(b);var visible=list.ToArray();if(visible.Length==0)return;
            float bottom=90;
            void Position(Transform node,TMPro.TextMeshProUGUI text){
                var rect=node.TryCast<RectTransform>();var parent=node.parent.TryCast<RectTransform>();if(rect==null||parent==null)return;
                long key=rect.NativeIdentity().ToInt64();if(!choiceRoots.ContainsKey(key))choiceRoots[key]=new(){Rect=rect,Min=rect.anchorMin,Max=rect.anchorMax,Pivot=rect.pivot,Size=rect.sizeDelta,Position=rect.anchoredPosition};
                float h=Math.Max(12,text.GetPreferredValues(text.text,200,float.PositiveInfinity).y)+14;rect.pivot=new Vector2(.5f,.5f);rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,232);rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,h);
                var tr=text.rectTransform;tr.anchorMin=tr.anchorMax=tr.pivot=new Vector2(.5f,.5f);tr.anchoredPosition=Vector2.zero;
                var canvas=rect.GetComponentInParent<Canvas>();var camera=canvas?.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas?.worldCamera;
                Vector2 center=RectTransformUtility.WorldToScreenPoint(camera,rect.position),unit=RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(new Vector3(1,1,0)));
                float px=Math.Abs(unit.x-center.x),py=Math.Abs(unit.y-center.y);if(px<.01f||py<.01f)return;
                Vector2 point=new(Screen.width-50-232*px/2,bottom+h*py/2);if(RectTransformUtility.ScreenPointToWorldPointInRectangle(parent,point,camera,out Vector3 world))rect.position=world;
                bottom+=h*py+10;
            }
            for(int i=visible.Length-1;i>=0;i--)Position(visible[i].transform,visible[i].TMP);
            if(ui.playerBubble.gameObject.activeInHierarchy)Position(ui.playerBubble.transform,ui.playerBubble.textfield);
            else if(ui.clientBubble.gameObject.activeInHierarchy)Position(ui.clientBubble.transform,ui.clientBubble.textfield);
        }
        internal bool Active=>cursor!=null;
        internal bool Paused=>cursor?.Paused==true;
        internal StoryPresentationCursor Cursor=>cursor;
        internal Dialogue CurrentDialogue=>current;
        internal int NativeStarts {get;private set;}
        internal int AcceptedInputs {get;private set;}
        internal int RejectedInputs {get;private set;}
        internal int Interruptions {get;private set;}
        internal bool AwaitingCommit=>pending!=null;
        internal bool OwnsCurrent=>DialogUIManager.Instance?.currentDialog is Dialogue d && dialogues.Contains(d.NativeIdentity().ToInt64());
        internal bool HasDialogue(Dialogue d)=>d!=null&&dialogues.Contains(d.NativeIdentity().ToInt64());
        private bool Night=>core.Story?.ActOne?.NightContext==true&&cursor!=null&&ReferenceEquals(cursor.Engine,core.Story.Engine);
        internal NativeStoryPresentation(Core core)
        {
            this.core=core;
            string performance=System.IO.Path.Combine(core.Root,"ModWorkbench/res/prepared/performance/performance.json");
            if(System.IO.File.Exists(performance))try{StoryPerformance.Configure(System.IO.File.ReadAllText(performance));}catch(Exception e){core.LoggerInstance.Warning("P11_PERFORMANCE_UNAVAILABLE "+e.Message);}
            portrait=new NativePresentationPortrait(core);
            voice=new NativeStoryVoice(core);
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(DialogUIManager),"EndConversation"),prefix:new HarmonyMethod(typeof(NativeStoryPresentation),nameof(BeforeEnd)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(DialogUIManager),"OnAdvancePressed"),prefix:new HarmonyMethod(typeof(NativeStoryPresentation),nameof(BeforeAdvance)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(StoreClientMono),"OnClick"),prefix:new HarmonyMethod(typeof(NativeStoryPresentation),nameof(BeforeClientClick)));
            foreach(string name in new[]{"StartLeavingStore","StartLeavingStoreRight"})core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(StoreClientMono),name),prefix:new HarmonyMethod(typeof(NativeStoryPresentation),nameof(BeforeLeaving)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(StoreClientMono),"OnLeft"),postfix:new HarmonyMethod(typeof(NativeStoryPresentation),nameof(AfterLeft)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(StoreClientMono),"StartApprochingStore"),prefix:new HarmonyMethod(typeof(NativeStoryPresentation),nameof(BeforeArrival)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(AudioManager),"PauseAllSource"),postfix:new HarmonyMethod(typeof(NativeStoryPresentation),nameof(PauseVoice)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(AudioManager),"ResumeAllSource"),postfix:new HarmonyMethod(typeof(NativeStoryPresentation),nameof(ResumeVoice)));
        }
        private static void BeforeLeaving()=>Core.Instance?.Presentation?.portrait.Leaving();
        private static void AfterLeft()=>Core.Instance?.Presentation?.portrait.Left();
        private static void BeforeArrival()=>Core.Instance?.Presentation?.portrait.BeforeArrival();
        internal void LateUpdate(){portrait.LateUpdate();if(Night)core.Story.ActOne.Layout();else PositionChoices();}
        internal object PortraitStatus()=>portrait.Status();
        internal object VoiceStatus()=>voice.Status();
        internal int VoiceStarts=>voice.Starts;
        internal bool VoicePlaying=>voice.Playing;
        internal string VoiceActor=>voice.Actor;
        internal float VoiceMouth=>voice.MouthLevel;
        internal void MissingNextVoiceForTest(){if(core.Test!=null)voice.MissingNextForTest=true;}
        internal int MouthChanges=>portrait.MouthChanges;
        private static void PauseVoice()=>Core.Instance?.Presentation?.voice.Pause();
        private static void ResumeVoice()=>Core.Instance?.Presentation?.voice.Resume();
        internal bool DepartureVerified=>portrait.Departures>0&&portrait.DepartureFrames>0&&portrait.VisiblePlaceholderFrames==0;
        private static bool BeforeClientClick()
        {
            var self=Core.Instance?.Presentation;
            if(self?.Paused==true&&self.SameClient()&&self.Resume())return false;
            var host=Core.Instance?.Story;
            if(self?.Active!=true&&host?.NativeCounterScene==true&&host.Integration.IsOwnCurrent){host.OpenView();return false;}
            return true;
        }
        private static void BeforeEnd()
        {
            var self=Core.Instance?.Presentation;
            if(self?.Active==true&&!self.closing&&self.OwnsCurrent&&self.pending==null){
                self.cursor.Pause();self.epoch++;self.shown=null;self.Interruptions++;
                self.voice.Stop(true);
                coreLog("PRESENTATION_NATIVE_INTERRUPTED");
            }
        }
        private static void coreLog(string text)=>Core.Instance?.LoggerInstance.Msg(text);
        private static bool BeforeAdvance(DialogUIManager __instance)
        {
            var self=Core.Instance?.Presentation;
            if(self?.Formal==true&&self.Active&&Core.Instance.Story.ReportBlocked)return false;
            if(self?.Night!=true||!self.Active||self.Paused||!self.OwnsCurrent||self.shown==null||!__instance.isWaitingForManualAdvance)return true;
            var page=self.shown;
            if(page.Frame.Node.Kind=="choice"&&page.Segment==page.Segments-1)return true;
            // Vanilla continuation removes the dialogue when there is no shop
            // customer. A venue has none, so use the same queued commit fence
            // from the native Space/continue action after typing has finished.
            return !self.Queue(page,null,self.epoch);
        }
        internal void Begin(StoryEngine engine,int day)
        {
            if(core.Test==null&&core.Story?.NativeScene!=true)throw new InvalidOperationException("Native presentation is not enabled for this scene.");
            Stop();
            var store=PlayerStore.instance;var ui=DialogUIManager.Instance;
            bool night=core.Story?.ActOne?.NightContext==true&&ReferenceEquals(engine,core.Story.Engine);
            if((!night&&(store?.currentClientInstance?.storeClient==null||!store.isClientArrived))||ui.currentDialog!=null||ui.isConversationActiveFlag!=0)
                throw new InvalidOperationException("A quiet, physically arrived native customer is required.");
            if(core.Story?.NativeCounterScene==true)StoreUIManager.Instance.CloseAllUI();
            if(!night){client=store.currentClientInstance.storeClient;portrait.Begin();}
            cursor=new StoryPresentationCursor(engine,day);NativeStarts=AcceptedInputs=RejectedInputs=Interruptions=0;epoch++;
            Pump();
        }
        internal bool Resume()
        {
            if(!Active||!Paused||!SameClient()||DialogUIManager.Instance.currentDialog!=null||DialogUIManager.Instance.isConversationActiveFlag!=0)return false;
            cursor.Resume();shown=null;epoch++;Pump();return true;
        }
        private bool SameClient()=>Night||client!=null&&PlayerStore.instance?.currentClientInstance?.storeClient?.NativeIdentity()==client.NativeIdentity()&&PlayerStore.instance.isClientArrived;
        internal bool Queue(StoryPresentationPage page,string choice,int issuedEpoch)
        {
            if(!Active||Paused||Formal&&core.Story.ReportBlocked||pending!=null||issuedEpoch!=epoch||!ReferenceEquals(cursor.Page(),page)||!SameClient()){
                if(Active&&!Paused&&pending==null&&issuedEpoch==epoch&&SameClient()&&!ReferenceEquals(cursor.Page(),page))redisplayRejected=true;
                RejectedInputs++;return false;
            }
            voice.Stop(true);pending=page;pendingChoice=choice;return true;
        }
        internal void Update()
        {
            if(Active&&Formal&&core.Story.ReportBlocked){
                if(voice.Playing){voice.Pause();reportPausedVoice=true;}
                portrait.Mouth(null,0);return;
            }
            if(reportPausedVoice){reportPausedVoice=false;if(Time.timeScale>0)voice.Resume();}
            voice.Update(Active&&!Paused&&OwnsCurrent);
            portrait.Mouth(voice.Actor,voice.MouthLevel);
            if(!Active){if(core.Story?.Native012?.Enabled==true&&core.Story.ActOne?.NightContext!=true)portrait.SyncVisitor();return;}
            if(!SameClient()){Stop(keepPortrait:core.Story?.Native012?.Enabled==true);portrait.SyncVisitor();return;}
            if(redisplayRejected){CloseOwned();var ui=DialogUIManager.Instance;if(OwnsCurrent&&ui.isConversationActiveFlag==0)ui.currentDialog=null;shown=null;epoch++;redisplayRejected=false;}
            if(pending!=null){
                var page=pending;string choice=pendingChoice;pending=null;pendingChoice=null;
                CloseOwned();shown=null;epoch++;
                if(Commit(page,choice))AcceptedInputs++;else {RejectedInputs++;var ui=DialogUIManager.Instance;if(OwnsCurrent&&ui.isConversationActiveFlag==0)ui.currentDialog=null;}
                if(cursor.Engine.Frame()==null){Complete();return;}
            }
            // A delayed native counter-departure callback can hide the shared
            // bubbles after a venue page was shown. Repaint the same ticket;
            // this never advances the engine or reapplies scene effects.
            if(Night&&!Paused&&shown!=null&&OwnsCurrent&&Time.timeScale>0&&!MapUIManager.Instance.inTransit){
                var bubble=shown.IsPlayerBubble?DialogUIManager.Instance.playerBubble:DialogUIManager.Instance.clientBubble;
                if(!bubble.gameObject.activeInHierarchy){CloseOwned();shown=null;epoch++;}
            }
            Pump();
            ApplySpeakerName();
            FitAuthoredBubbles();
        }
        private void Pump()
        {
            if(!Active||Paused||shown!=null||Formal&&(core.Story.ReportBlocked||NativeP19Presentation.InteractionBlocked))return;
            if(Formal&&cursor.Waiting){core.Story.ActOne.SuspendVisit();return;}
            var ui=DialogUIManager.Instance;
            if(ui.currentDialog!=null||ui.isConversationActiveFlag!=0)return;
            var page=cursor.Page();if(page==null){Stop();return;}
            if(page.Frame.Node.Kind=="end"){
                bool moved=Commit(page,null);if(moved)Complete();else {cursor.Pause();shown=null;}return;
            }
            core.EnsureChineseFallback();dialogues.Clear();callbacks.Clear();int token=epoch;
            core.Story?.ActOne?.Frame(page.Frame);
            if(!Night){portrait.SetSpeaker(page.SpeakerId);portrait.SetForNode(page.Frame.Scene.Id,page.Frame.Node.Id,page.DialoguePart);}
            string speaker=page.CanVoice&&page.SpeakerId==StoryContent.PlayerSpeaker?DialogUIManager.PLAYER:page.SpeakerName;
            var root=new Dialogue().SetText(speaker,page.Text);Track(root);
            bool choices=page.Frame.Node.Kind=="choice"&&page.Segment==page.Segments-1;
            if(choices){
                int index=0;
                foreach(var option in page.Frame.Choices){
                    string selected=option.Id;
                    var action=(System.Action)(Action)(()=>Queue(page,selected,token));callbacks.Add(action);
                    // The vanilla choice renderer reads Item2.plainText, not the
                    // item's name. Keep the authored response on that native node.
                    var destination=new Dialogue().SetText(DialogUIManager.PLAYER,option.Text);Track(destination);
                    var icon=ItemDirectory.CreateEmptyItem("ms_p0_choice_"+(index++));
                    root.AddChoice(icon,option.Text,option.Text,destination,action,null);
                }
                foreach(var choice in root.choices)Track(choice.Item2);
            }else{
                // EndAction can run at typewriter completion. The next node starts only
                // after the native ContinueConversation path, so it is our commit fence.
                var action=(System.Action)(Action)(()=>Queue(page,null,token));callbacks.Add(action);
                var fence=new Dialogue().SetText(speaker,"").SetStartAction(action);Track(fence);root.SetNextDialogue(fence);
            }
            current=root;shown=page;
            if(Night)core.Story.ActOne.Layout();
            if(!ui.StartDialogue(root,false)){shown=null;current=null;return;}
            if(Night){
                core.Story.ActOne.Layout();
                // The native typewriter caches source glyph vertices. A counter
                // bubble's old font/width cannot be reused after venue reflow.
                ui.clientBubble.typewriter.TextAnimator.ScheduleMeshRefresh();
                ui.playerBubble.typewriter.TextAnimator.ScheduleMeshRefresh();
            }
            ApplySpeakerName();
            FitAuthoredBubbles();
            NativeStarts++;
            voice.Play(page);
            coreLog("PRESENTATION_NATIVE_LINE "+page.Frame.Scene.Id+"/"+page.Frame.Node.Id+" part="+(page.Segment+1)+" speaker="+page.SpeakerId);
        }
        private void ApplySpeakerName()
        {
            if(!OwnsCurrent||shown==null)return;
            var ui=DialogUIManager.Instance;
            if(ui.clientBubble.gameObject.activeInHierarchy){
                string name=shown.SpeakerName;
                if(ui.clientNameTMP.text!=name)ui.clientNameTMP.text=name;
            }
        }
        private void Track(Dialogue dialogue){if(dialogue!=null)dialogues.Add(dialogue.NativeIdentity().ToInt64());}
        private bool Formal=>cursor!=null&&ReferenceEquals(cursor.Engine,core.Story?.Engine)&&core.Story.Native012.Enabled;
        private bool Commit(StoryPresentationPage page,string choice)
        {
            if(!Formal)return cursor.Commit(page,choice);
            if(page.Segment<page.Segments-1){bool advanced=cursor.Commit(page,choice);if(advanced)core.Story.Persist();return advanced;}
            bool moved=core.Story.Campaign.StoryMutation(()=>cursor.Commit(page,choice));
            if(!ReferenceEquals(cursor.Engine,core.Story.Engine)){cursor=new StoryPresentationCursor(core.Story.Engine,core.Story.Day);cursor.Pause();return false;}
            core.Story.Native012.AfterStoryMutation();core.Story.Opening.AfterStoryMutation();core.Story.Persist();return moved;
        }
        private void Complete(){bool formal=Formal;Stop(formal);if(formal)core.Story.Integration.OnSceneComplete();}
        private void CloseOwned()
        {
            if(quitting||!OwnsCurrent)return;
            closing=true;try{DialogUIManager.Instance.EndConversation();}finally{closing=false;}
        }
        internal void Stop(bool keepPortrait=false)
        {
            CloseOwned();RestoreBubbleLayout();voice.Stop();if(!keepPortrait&&!quitting)portrait.Dispose();cursor=null;shown=pending=null;pendingChoice=null;client=null;current=null;
            dialogues.Clear();callbacks.Clear();reportPausedVoice=false;redisplayRejected=false;epoch++;
        }
        internal void PrepareQuit(){quitting=true;Stop(true);}
        internal object Status()=>new{active=Active,paused=Paused,scene=cursor?.Engine.Frame()?.Scene.Id,node=cursor?.Engine.Frame()?.Node.Id,
            waitingNative=DialogUIManager.Instance.isWaitingForManualAdvance,clientBubbleActive=DialogUIManager.Instance.clientBubble.gameObject.activeInHierarchy,playerBubbleActive=DialogUIManager.Instance.playerBubble.gameObject.activeInHierarchy,
            clientText=DialogUIManager.Instance.clientBubble.textfield.text,playerText=DialogUIManager.Instance.playerBubble.textfield.text,
            segment=cursor?.Page()?.Segment,nativeStarts=NativeStarts,acceptedInputs=AcceptedInputs,rejectedInputs=RejectedInputs,
            interruptions=Interruptions,ownsCurrent=OwnsCurrent,epoch,currentPlainText=DialogUIManager.Instance.currentDialog?.plainText,shownText=shown?.Text,pageText=cursor?.Page()?.Text,anyTyping=DialogUIManager.Instance.IsAnyBubbleTyping(),portrait=portrait.Status(),voice=voice.Status(),venue=core.Story?.ActOne?.StageStatus()};
        internal bool StaticPortraitActive=>portrait.Active;
        internal bool StaticPortraitRestored=>portrait.Restored;
        internal int Epoch=>epoch;
    }
}
