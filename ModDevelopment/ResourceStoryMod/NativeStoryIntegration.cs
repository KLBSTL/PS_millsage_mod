using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using ResourceStoryMod.Story;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ResourceStoryMod
{
    // Native customers carry the visit; StoryEngine remains the sole owner of progress/effects.
    internal sealed class NativeStoryIntegration
    {
        private const string VenueKey="millsage.venue.v1";
        private readonly Core core;
        private readonly NativeStoryHost host;
        private readonly List<System.Delegate> roots=new();
        private string run;
        private GameObject mapButton;
        private UnityAction mapClick;
        private bool nativeMapEntryRegistered;
        private TextMeshProUGUI[] mapLabels=Array.Empty<TextMeshProUGUI>();
        private string deferredScene;
        private int deferredDay=-1;
        private bool completing;
        private bool returning;
        private int venueDay;
        private StoryNightVisit venue=new();
        internal bool InVenue { get; private set; }
        internal bool CompletedNativeNight=>InVenue&&ActOnePresentation.NativeNight(venue.Scene)&&venue.NeedsReturn(host.ReadState);
        internal void BindNativeNight(string scene){if(InVenue&&venue.Scene!=scene&&ActOnePresentation.NativeNight(scene)){venue.Scene=scene;WriteVenue();host.Persist();}}
        internal string Prefix => "ms_visit_"+host.Package.Id+"_";
        internal bool IsOwn(StoreClient client) => client?.identifier?.StartsWith(Prefix,StringComparison.Ordinal)==true;
        internal bool IsOwnCurrent => IsOwn(PlayerStore.instance?.currentClientInstance?.storeClient);
        internal string LeadActor(StoryScene scene)=>host.Return?.Enabled==true&&ReturnPresentation.Scene(scene?.Id)?ReturnPresentation.Lead(scene.Id):DiaryPresentation.Lead(scene?.Id)??(scene?.Id=="ms_scene_005"?(host.ActOne.Later005Visit?"ms_houka":"ms_hotaru"):host.Package.Actors.FirstOrDefault(a=>scene?.Participants.Contains(a.Id)==true&&MemberBusiness.ValidActor(a.Id))?.Id);
        internal bool CanRequestResume => host.Engine?.Frame()?.Scene.Context=="counter" && !IsOwnCurrent && QueuedCount==0 && PlayerStore.instance?.storeState==PlayerStore.StoreState.OPEN;
        internal void RequestResume() { if(CanRequestResume) { deferredScene=null;deferredDay=-1; } }
        internal int QueuedCount
        {
            get { int n=0; var stack=PlayerStore.instance?.storeClientManager?.clientStack; if(stack!=null) foreach(var c in stack) if(IsOwn(c)) n++; return n; }
        }
        internal NativeStoryIntegration(Core core,NativeStoryHost host)
        {
            this.core=core; this.host=host;
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(StoreClient),"StartMainDialogue"),prefix:new HarmonyMethod(typeof(NativeStoryIntegration),nameof(OwnDialogue)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(PlayerStore),"CanBuyItemFromTable"),prefix:new HarmonyMethod(typeof(NativeStoryIntegration),nameof(OwnTrade)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(PlayerStore),"CanSellItemFromTable"),prefix:new HarmonyMethod(typeof(NativeStoryIntegration),nameof(OwnTrade)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(EmporiumEntry),"OnArriveStore"),prefix:new HarmonyMethod(typeof(NativeStoryIntegration),nameof(PrepaidReturn)));
        }
        private static void PrepaidReturn(ref bool __0)
        {
            var integration=Core.Instance.Story?.Integration;
            if(Core.Instance.Test!=null)Core.Instance.LoggerInstance.Msg("VENUE_ARRIVE fromMap="+__0+" venue="+integration?.InVenue+" remaining="+PlayerStore.instance.visitLeftTonight);
            if(__0||integration==null)return;
            if(!integration.InVenue){
                // Loading the native shop calls OnArriveStore before our first
                // Update restores InVenue. Its saved, same-day receipt already
                // paid for this trip; keep the receipt active for cold resume.
                var store=PlayerStore.instance;
                if(store?.storeState==PlayerStore.StoreState.CLOSED&&store.modData.TryGetValue(VenueKey,out var json)){
                    var saved=JsonSerializer.Deserialize<StoryNightVisit>(json,Core.Json);
                    if(saved?.Resume(store.runID,integration.host.Day)==true&&ActOnePresentation.NativeNight(saved.Scene)){
                        __0=true;integration.core.LoggerInstance.Msg("STORY_COLD_VENUE_PREPAID remaining="+store.visitLeftTonight);
                    }
                }
                return;
            }
            // Native OnArriveStore(false) charges a visit on return. This venue paid
            // on entry, so use its existing fromMap=true path to avoid a second charge.
            __0=true; integration.host.CloseView(); integration.InVenue=false; integration.WriteVenue();
        }
        private static bool OwnDialogue(StoreClient __instance)
        {
            var story=Core.Instance.Story;
            if(story?.Integration?.IsOwn(__instance)!=true)return true;
            if(story.Opening.FinishedVisit)story.Opening.FinishVisit();
            else if(story.Diary.FinishedVisit)story.Diary.FinishVisit();
            return false;
        }
        private static bool OwnTrade(PlayerStore __instance,ref bool __result)
        {
            if(Core.Instance.Story?.Integration?.IsOwn(__instance.currentClientInstance?.storeClient)!=true) return true;
            if(Core.Instance.Story?.Opening?.Trading==true||Core.Instance.Story?.Diary?.Trading==true||Core.Instance.Story?.Return?.Trading==true)return true;
            __result=false; return false;
        }
        internal void Update()
        {
            if(core.Test?.SuppressStory==true&&core.Test.Request.Native012==""&&core.Test.Request.OpeningMode=="")return;
            if(host.IsTestPackage) return;
            var store=PlayerStore.instance;
            EnsureMapButton();
            if(run!=store.runID)
            {
                run=store.runID;
                if(store.modData.TryGetValue(VenueKey,out var json))
                {
                    var visit=JsonSerializer.Deserialize<StoryNightVisit>(json,Core.Json);
                    if(visit.Version!=1 || visit.RunId!=run) throw new InvalidDataException("Invalid venue state; original data retained");
                    InVenue=visit.Active && visit.Day==host.Day && store.storeState==PlayerStore.StoreState.CLOSED&&ActOnePresentation.NativeNight(visit.Scene);
                    venueDay=visit.Day;
                    venue=visit;
                    if(InVenue){EmporiumEntry.Instance.OnLeaveStore();ReleaseMap.SetOutside(MapUIManager.Instance,true);MapUIManager.Instance.mapPanel.SetActive(false);}
                }else{venue=new StoryNightVisit();InVenue=false;}
            }
            if(InVenue && (venueDay!=host.Day || store.storeState!=PlayerStore.StoreState.CLOSED))
            {
                host.CloseView(); InVenue=false; WriteVenue();
            }
            EnsureMapButton();
            if(InVenue&&ReleaseMap.IsOwn(MapUIManager.Instance.Current))MapUIManager.Instance.CloseLocationPanel();
            foreach(var label in mapLabels) if(label!=null && label.text!="Live House") label.text="Live House";
            if(mapButton!=null) mapButton.SetActive(store.storeState==PlayerStore.StoreState.CLOSED&&(!host.ActOne.Enabled||InVenue||host.Engine.Frame()?.Scene.Context=="night"||host.Engine.Eligible("night",host.Day).Count>0));
            var frame=host.Engine.Frame();
            if(NativeP19Presentation.InteractionBlocked&&!core.Presentation.Active)return;
            if(host.ActOne.WaitingForDays){if(IsOwnCurrent&&store.isClientArrived&&!core.Presentation.Active)host.ActOne.SuspendVisit();return;}
            if(host.Visible && frame?.Scene.Context=="counter" && (!IsOwnCurrent || !store.isClientArrived || DialogUIManager.Instance.isConversationActiveFlag!=0)) host.CloseView();
            if(frame?.Scene.Context=="night") return;
            if(IsOwnCurrent)
            {
                if(host.Economy.Business.HeldStoryActor!=null) return;
                if(store.isClientArrived&&host.NativeCounterScene&&!core.Presentation.Active&&!host.Visible&&frame!=null&&CanResumeNative(frame))host.OpenView();
                if(store.isClientArrived && !host.Visible && frame==null && !completing)
                {
                    string id=store.currentClientInstance.storeClient.identifier.Substring(Prefix.Length);
                    if(host.ActOne.OwnCounterVisit&&host.ReadState.CompletedDays.ContainsKey(id))OnSceneComplete();else host.Start(id);
                }
                return;
            }
            completing=false;
            if(host.Native012?.DuePhone==true)return;
            if(store.storeState!=PlayerStore.StoreState.OPEN || InVenue || QueuedCount>0) return;
            var scene=frame?.Scene ?? host.CounterCandidates().FirstOrDefault();
            if(core.Test?.Request.Native012!=""&&core.Test!=null&&scene?.Id!="ms_scene_012")return;
            if(core.Test!=null&&core.Test.Request.OpeningMode!=""&&!PackageC.OpeningScene(scene?.Id)&&!(core.Test.Request.ActOneExtension&&ActOnePresentation.NativeScene(scene?.Id)&&!DiaryPresentation.Scene(scene?.Id)&&!ReturnPresentation.Scene(scene?.Id))&&!(core.Test.Request.ActTwoExtension&&scene?.Id=="ms_scene_012")&&!(core.Test.Request.DiaryExtension&&DiaryPresentation.Scene(scene?.Id))&&!(core.Test.Request.ReturnExtension&&ReturnPresentation.Scene(scene?.Id)))return;
            if(scene==null || scene.Context!="counter" || (deferredScene==scene.Id && deferredDay==host.Day)) return;
            var actor=host.Package.Actors.Find(a=>a.Id==LeadActor(scene));
            if(host.NormalPlay&&host.Visits?.CanStartStory(actor?.Id)==false)return;
            // A newly unlocked scene may reuse the member already physically at the counter.
            if(actor!=null && host.Economy.Business.CurrentActor==actor.Id && store.currentClientInstance!=null)
            {
                host.Economy.Business.Close();
                var existing=store.currentClientInstance.storeClient;existing.identifier=Prefix+scene.Id;
                existing.clientFaction=host.Economy.Business.ResidenceFaction(actor.Id);
                var onDismiss=(System.Action)(Action)(()=>{host.CloseView();deferredScene=scene.Id;deferredDay=host.Day;});
                roots.Add(onDismiss);existing.OnDismiss=onDismiss;
                return;
            }
            core.EnsureResources(); core.EnsureChineseFallback();
            var client=new StoreClient {
                identifier=Prefix+scene.Id, displayName=actor?.DisplayName ?? "剧情来访", spriteName=Prefix+scene.Id,
                clientIntent=StoreClient.ClientIntent.DIALOGUE, clientFaction=host.Economy.Business.ResidenceFaction(actor?.Id),
                dismissable=true, nonShootable=true, clientImpatience=999, clientCash=0, clientBudget=0,
                mainDialogue=new Dialogue().SetText(actor?.DisplayName ?? "", ""),
                allDoneDialogue=new Dialogue().SetText(actor?.DisplayName ?? "", "")
            };
            SpriteDict.Instance.spriteDictionary[client.spriteName]=host.CustomerPortrait(actor?.Portrait) ?? core.Portrait;
            var dismissed=(System.Action)(Action)(()=> { host.CloseView(); deferredScene=scene.Id; deferredDay=host.Day; });
            roots.Add(dismissed); client.OnDismiss=dismissed;
            client.CompleteClientCreation(false);
            store.storeClientManager.AddNextClient(client);
            core.LoggerInstance.Msg("STORY_NATIVE_QUEUED "+scene.Id);
        }
        internal void OnSceneComplete()
        {
            if(host.IsTestPackage) return;
            if(InVenue&&host.ActOne.Enabled&&ActOnePresentation.NativeNight(venue.Scene)){
                // The championship epilogue stays in the same venue and same
                // prepaid night visit; there is no intermediate shop return.
                if(venue.Scene=="ms_scene_030"&&host.Return.Enabled&&host.Package.Scenes.Any(s=>s.Id=="ms_scene_031"&&s.Context=="night")&&host.ReadState.CompletedDays.ContainsKey("ms_scene_030")&&!host.ReadState.CompletedDays.ContainsKey("ms_scene_031")){
                    BindNativeNight("ms_scene_031");host.Start("ms_scene_031");return;
                }
                host.ActOne.FinishNight();return;
            }
            if(IsOwnCurrent&&host.Diary.CompleteVisit(host.Opening.VisitScene))return;
            if(IsOwnCurrent&&host.Return.CompleteVisit(host.Opening.VisitScene))return;
            if(host.ActOne.OwnCounterVisit){completing=true;PlayerStore.instance.DismissCurrentClient(false,false,true);return;}
            if(IsOwnCurrent&&PlayerStore.instance.currentClientInstance.storeClient.identifier==Prefix+"ms_scene_012"&&host.Native012.Enabled){completing=true;PlayerStore.instance.DismissCurrentClient(false,false,true);return;}
            if(IsOwnCurrent && host.Economy.Business.AfterStory(PlayerStore.instance.currentClientInstance.storeClient.identifier.Substring(Prefix.Length))) return;
            if(IsOwnCurrent) { completing=true; PlayerStore.instance.DismissCurrentClient(false,false,true); }
        }
        private bool CanResumeNative(StoryFrame frame)=>PlayerStore.instance.currentClientInstance.storeClient.identifier==Prefix+frame.Scene.Id&&DialogUIManager.Instance.isConversationActiveFlag==0;
        internal void EnsureMapButton()
        {
            if(nativeMapEntryRegistered)return;
            ReleaseMap.Register(()=>PlayerStore.HasInstance&&PlayerStore.instance.storeState==PlayerStore.StoreState.CLOSED&&
                (!host.ActOne.Enabled||InVenue||host.Engine?.Frame()?.Scene.Context=="night"||host.Engine?.Eligible("night",host.Day).Count>0),()=>EnterVenue());
            nativeMapEntryRegistered=true;
        }
        internal bool EnterVenue()
        {
            var store=PlayerStore.instance; var map=MapUIManager.Instance;
            if(InVenue || host.Visible || store==null || store.storeState!=PlayerStore.StoreState.CLOSED ||
                store.currentClientInstance!=null || store.visitLeftTonight<=0 || map==null ||
                (map.inTransit&&!ReleaseMap.IsOwn(map.Current)) || (map.Current!=null&&!ReleaseMap.IsOwn(map.Current)) ||
                store.lockStorePhysicalFlag!=0 || DialogUIManager.Instance.isConversationActiveFlag!=0) return false;
            var scene=host.Engine.Frame()?.Scene??host.Engine.Eligible("night",host.Day).FirstOrDefault();
            if(host.ActOne.Enabled&&scene==null)return false;
            // One native night visit pays for entry. Pausing/resuming dialogue never pays again.
            int remaining=store.visitLeftTonight;
            if(!venue.Begin(store.runID,scene?.Id??"millsage_night_visit",host.Day,ref remaining))return false;
            store.visitLeftTonight=remaining;InVenue=true;venueDay=host.Day;WriteVenue();
            returning=false;
            // The generic map dispatcher branches on currentLocation. STORE
            // would dispatch straight back into the shop, so this custom venue
            // uses the same native shop leave/arrive endpoints directly.
            if(!ReleaseMap.IsOwn(map.Current))EmporiumEntry.Instance.OnLeaveStore();ReleaseMap.SetOutside(map,true);
            // CloseUI is also a location dispatcher in the native map. Hide
            // just its menu; the native shop endpoint owns this custom trip.
            map.mapPanel.SetActive(false);
            store.SaveGame(); core.LoggerInstance.Msg("STORY_VENUE_ENTER remaining="+store.visitLeftTonight);
            return true;
        }
        internal bool ClickMapEntry()
        {
            if(InVenue)return false;
            EnsureMapButton();return ReleaseMap.Travel();
        }
        internal bool ReturnFromVenue()
        {
            if(!InVenue||returning) return false;
            returning=true;
            host.CloseView();
            var map=MapUIManager.Instance;map.mapPanel.SetActive(false);
            EmporiumEntry.Instance.OnArriveStore(true);
            ReleaseMap.SetOutside(map,false);map.Current=null;
            InVenue=false;WriteVenue();
            PlayerStore.instance.SaveGame();
            return true;
        }
        internal void RecoverFinaleBackstage()
        {
            if(host.Return?.FinalShop!=true)return;
            // Compatibility with an older save already returned from030.
            // Its contest visit was paid; adopting its pending epilogue cannot
            // debit another trip or roll the game day forward.
            venue=new StoryNightVisit{RunId=PlayerStore.instance.runID,Day=host.Day,Scene="ms_scene_031",Active=true};
            venueDay=host.Day;InVenue=true;returning=false;WriteVenue();
            EmporiumEntry.Instance.OnLeaveStore();ReleaseMap.SetOutside(MapUIManager.Instance,true);MapUIManager.Instance.mapPanel.SetActive(false);host.Persist();
        }
        private void WriteVenue(){venue.RunId=run;venue.Day=host.Day;venue.Active=InVenue;PlayerStore.instance.modData[VenueKey]=JsonSerializer.Serialize(venue,Core.Json);}
        internal void Draw(GUIStyle body,GUIStyle button,GUIStyle window)
        {
            if(host.ActOne.Enabled&&ActOnePresentation.NativeNight(venue.Scene))return;
            if(!InVenue || host.Visible) return;
            GUI.Box(new Rect(20,155,450,180),"演出场地",window);
            GUI.Label(new Rect(35,200,420,55),"夜间活动 · 剩余外出次数："+PlayerStore.instance.visitLeftTonight,body);
            if(NativePanelGUI.Button(new Rect(35,270,420,45),"返回店铺 / 地图",button)) ReturnFromVenue();
        }
        internal void Detach()
        {
            InVenue=false; run=null; deferredScene=null; deferredDay=-1; completing=returning=false; roots.Clear();
            venue=new StoryNightVisit();
            if(mapButton!=null) UnityEngine.Object.Destroy(mapButton);
            mapButton=null; mapClick=null;
            mapLabels=Array.Empty<TextMeshProUGUI>();
        }
    }
}
