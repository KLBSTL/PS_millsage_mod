using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ResourceStoryMod.Story;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace ResourceStoryMod
{
    internal sealed class NativeStoryHost
    {
        internal const string SaveKey = "millsage.story.v1";
        private readonly Core core;
        private string contentRoot;
        internal NativeNormalPlay Normal {get;}
        internal bool NormalPlay=>Normal.Active&&normalProfile&&!IsTestPackage;
        internal bool DeveloperPanels=>!NormalPlay||Normal.DeveloperPanels;
        private bool normalProfile;
        internal System.Collections.Generic.IReadOnlyList<StoryScene> CounterCandidates()=>Engine.Eligible("counter",Day).Where(s=>!NormalPlay||NormalPlayPolicy.CanQueue(package,ReadState,s,Day,Normal.Config.NewCounterScenesPerDay)).ToArray();
        private StoryPackage package;
        private PlayerStore attachedStore, lockedStore;
        private StoreUIManager lockedUI;
        private string attachedRun;
        internal StoryEngine Engine { get; private set; }
        private StoryEngine readEngine;
        private StoryState readState;
        internal long ReadSnapshotBuilds { get; private set; }
        // Native predicates only read this detached view. Transactions continue to
        // use Snapshot/Edit: never hand this cached object to a mutating operation.
        internal StoryState ReadState {
            get {
                if(Engine==null){readEngine=null;readState=null;return null;}
                if(!ReferenceEquals(readEngine,Engine)||readState?.Revision!=Engine.Revision){
                    readEngine=Engine;readState=Engine.Snapshot();ReadSnapshotBuilds++;
                }
                return readState;
            }
        }
        internal string LastError { get; private set; }
        internal bool Visible { get; private set; }
        internal bool WindowDrawn { get; private set; }
        internal long DrawnRevision { get; private set; } = -1;
        internal bool IsTestPackage { get; private set; }
        internal int PageCount => pages.Count;
        internal StoryPackage Package => package;
        internal string ContentRoot => contentRoot;
        internal NativeStoryIntegration Integration { get; private set; }
        internal NativeMemberEconomy Economy { get; private set; }
        internal NativeCampaignItems Campaign { get; private set; }
        internal NativeCampaign012 Native012 { get; private set; }
        internal NativeOpeningCampaign Opening {get;private set;}
        internal NativeActOneCampaign ActOne {get;private set;}
        internal NativeActTwoCampaign ActTwo {get;private set;}
        internal NativeDiaryCampaign Diary {get;private set;}
        internal NativeReturnCampaign Return {get;private set;}
        internal NativeNormalSupplier Supplier {get;private set;}
        internal NativeBasicInspection Inspection {get;private set;}
        internal NativeNormalVisits Visits {get;private set;}
        internal bool NativeCounterScene=>Native012.NativeScene||Opening.NativeScene||ActOne?.NativeScene==true&&Engine.Frame()?.Scene.Context=="counter";
        internal bool NativeScene=>NativeCounterScene||ActOne?.NightScene==true;
        private bool previewMenu;
        private int previewPage;
        internal bool CanResume => Engine?.Frame() != null && ((IsTestPackage && core.Test != null) || CanOpen(Engine.Frame().Scene.Context));
        private bool locked;
        private bool storeLockAdded, uiLockAdded;
        private float previousTimeScale;
        private Font font;
        private Texture2D windowBackground, buttonBackground;
        private GUIStyle bodyStyle, headingStyle, buttonStyle, windowStyle;
        private readonly List<string> pages = new();
        private long pageRevision = -1;
        private int page;
        private readonly Dictionary<string, Texture2D> portraits = new();
        private readonly Dictionary<string, Sprite> customerPortraits = new();
        internal int Day => StoreStation.instance == null ? 1 : StoreStation.instance.dayCounter;

        internal NativeStoryHost(Core core)
        {
            this.core = core;
            Normal=new NativeNormalPlay(core);
            string selected = core.Test?.PackagePath;
            contentRoot = string.IsNullOrEmpty(selected) ? Path.Combine(core.DataRoot, "Content") : Path.GetDirectoryName(selected);
            System.IO.Directory.CreateDirectory(contentRoot);
            string path = string.IsNullOrEmpty(selected) ? Path.Combine(contentRoot, "story.package.json") : selected;
            if(!string.IsNullOrEmpty(selected) && !File.Exists(path)) throw new FileNotFoundException("Preview package is missing",path);
            if (!File.Exists(path)) File.Copy(Path.Combine(core.Root, "ModDevelopment", "ResourceStoryMod", "Content", "story.package.json"), path);
            package = StoryContent.Load(path);
            normalProfile=Normal.Active&&(core.Test?.Request.NormalPlay==true||!package.Enabled&&package.Id=="millsage");
            if(normalProfile){string normalPath=Normal.PackagePath(null);contentRoot=Path.GetDirectoryName(normalPath);package=StoryContent.Load(normalPath);package.Enabled=true;}
            if (core.Test?.Campaign == true||core.Test!=null&&(core.Test.Request.Native012!=""||core.Test.Request.OpeningMode!="")) package.Enabled = true;
            if (core.Test?.Preview == true || core.Test?.Delivery == true) { package.Enabled=true; IsTestPackage=true; previewMenu=true; }
            Integration = new NativeStoryIntegration(core,this);
            Economy = new NativeMemberEconomy(core,this);
            Campaign = new NativeCampaignItems(core,this);
            Native012 = new NativeCampaign012(core,this);
            Opening = new NativeOpeningCampaign(core,this);
            ActOne = new NativeActOneCampaign(core,this);
            ActTwo = new NativeActTwoCampaign(core,this);
            Diary = new NativeDiaryCampaign(core,this);
            Return = new NativeReturnCampaign(core,this);
            Supplier=new NativeNormalSupplier(core,this);
            Inspection=new NativeBasicInspection(core,this);
            Visits=new NativeNormalVisits(core,this);
            core.LoggerInstance.Msg($"STORY_CONTENT {package.Id}; enabled={package.Enabled}; ready={package.Scenes.Count(s => s.Status == "ready")}");
        }
        internal void Update()
        {
            if (SceneManager.GetActiveScene().name != "InventoryScene") { Detach(); return; }
            var store = PlayerStore.instance;
            if (store == null || StoreStation.instance == null || string.IsNullOrWhiteSpace(store.runID) || store.modData == null) return;
            if(core.Test?.ReadyPolling==true && !TestSession.LocalizationReady())return;
            if (attachedRun != store.runID || attachedStore?.NativeIdentity() != store.NativeIdentity()) Attach(store);
            if (Engine == null || !package.Enabled) return;
            Campaign.Update();
            if(Campaign.SleepTransition)return;
            Visits.Update();
            Economy.Update();
            Native012.Update();
            ActTwo.Update();
            Diary.Update();
            Return.Update();
            Opening.Update();
            Supplier.Update();
            Inspection.Update();
            if (Engine.Frame() == null && Visible) CloseView();
            Integration.Update();
            ActOne.Update();
        }
        private void Attach(PlayerStore store)
        {
            CloseView(); Economy.Detach();
            attachedStore = store; attachedRun = store.runID; Engine = null;
            if (!package.Enabled) return;
            try
            {
                string saved = store.modData.TryGetValue(SaveKey, out var value) ? value : null;
                if(normalProfile){string normalPath=Normal.PackagePath(saved);contentRoot=Path.GetDirectoryName(normalPath);package=StoryContent.Load(normalPath);package.Enabled=true;}
                Engine = new StoryEngine(package, attachedRun, saved);
                ConfigureCampaignText();
                Engine.Changed += Flush;
                bool changed=Economy.Attach(store);
                changed|=Campaign.Attach();
                changed|=ActOne.Attach();
                // The native save omits the large counter grid. Restore its
                // recorded originals before any gameplay or test reads them.
                Return.RestoreCounter();
                if(changed)Persist();
                LastError = null;
                core.LoggerInstance.Msg("STORY_ATTACHED " + attachedRun + "; resume=" + (Engine.Frame() != null));
            }
            catch (Exception error) { Engine = null; Economy.Detach(); LastError = error.Message; core.LoggerInstance.Error("Story state preserved; feature unavailable: " + error.Message); }
        }
        internal void Flush()
        {
            if (Engine != null && attachedStore != null && attachedStore.runID == attachedRun)
                attachedStore.modData[SaveKey] = Engine.Save();
        }
        // Morning reports open from a delayed vanilla callback, independently
        // of the physical and conversation locks.
        internal bool ReportBlocked=>EndOfDayUIManager.Instance?.UI?.activeInHierarchy==true||AdvCalendarUIManager.Instance?.UIPanel?.activeInHierarchy==true;
        internal bool CanOpen(string context)
        {
            if (attachedStore == null || Visible || ReportBlocked || NativeP19Presentation.InteractionBlocked || attachedStore.lockStorePhysicalFlag != 0 || DialogUIManager.Instance == null || DialogUIManager.Instance.currentDialog != null || DialogUIManager.Instance.isConversationActiveFlag != 0) return false;
            if (context == "counter") return attachedStore.storeState == PlayerStore.StoreState.OPEN && Integration.IsOwnCurrent && attachedStore.isClientArrived;
            return context == "night" && attachedStore.storeState == PlayerStore.StoreState.CLOSED && (Integration.InVenue||Return.FinalShop);
        }
        internal bool Start(string sceneId, bool testOverride = false)
        {
            if (Engine == null) return false;
            var scene = package.Scenes.Find(s => s.Id == sceneId);
            if (scene == null || (!(testOverride && IsTestPackage && core.Test != null) && !CanOpen(scene.Context))) return false;
            if(!ActOne.CanStart(sceneId)||!Diary.CanStart(sceneId))return false;
            if (!Campaign.StoryMutation(()=>Engine.Start(sceneId, scene.Context, Day))) return false;
            Opening.AfterStoryMutation();Persist(); OpenView(); return true;
        }
        internal void OpenView()
        {
            if (Engine?.Frame() == null || Visible) return;
            if (!(IsTestPackage && core.Test != null) && !CanOpen(Engine.Frame().Scene.Context)) return;
            if(NativeScene){ActOne.Frame(Engine.Frame());core.Presentation.Begin(Engine,Day);return;}
            Visible = true; WindowDrawn = false; DrawnRevision = -1; page = 0; pageRevision = -1;
            lockedStore = attachedStore;
            lockedUI = StoreUIManager.Instance;
            previousTimeScale = Time.timeScale;
            locked = true;
            try
            {
                Time.timeScale = 0;
                lockedStore.lockStorePhysicalFlag++; storeLockAdded = true;
                NativePanelCursor.Acquire(lockedUI); uiLockAdded = true;
            }
            catch { CloseView(); throw; }
        }
        internal void CloseView()
        {
            Visible = false;
            if (!locked) return;
            Time.timeScale = previousTimeScale;
            if (storeLockAdded && lockedStore != null) lockedStore.lockStorePhysicalFlag = Math.Max(0, lockedStore.lockStorePhysicalFlag - 1);
            if (uiLockAdded && lockedUI != null) NativePanelCursor.Release(lockedUI);
            locked = false; storeLockAdded = false; uiLockAdded = false; lockedStore = null; lockedUI = null;
        }
        internal void Pause()
        {
            CloseView();
            Persist();
        }
        internal void Persist()
        {
            Flush();
            if (Engine != null && attachedStore != null && attachedStore.runID == attachedRun) attachedStore.SaveGame();
        }
        internal bool Advance(long revision)
        {
            bool moved = Engine != null && Campaign.StoryMutation(()=>Engine.Advance(revision, Day));
            if (moved) { page = 0; if (Engine.Frame() == null) { CloseView(); Integration.OnSceneComplete(); } Persist(); }
            return moved;
        }
        internal bool Choose(string id, long revision)
        {
            bool moved = Engine != null && Campaign.StoryMutation(()=>Engine.Choose(id, revision, Day));
            if (moved) { page = 0; Persist(); }
            return moved;
        }
        internal void UseTestPackage(StoryPackage testPackage)
        {
            if (core.Test == null) throw new InvalidOperationException("An isolated test session is required");
            Detach(); package = testPackage; IsTestPackage = true; Attach(PlayerStore.instance);
        }
        internal void RestoreTestState(string saved)
        {
            if (!IsTestPackage || core.Test == null) throw new InvalidOperationException("Test-only operation");
            CloseView();
            Engine = new StoryEngine(package, attachedRun, saved);
            ConfigureCampaignText();
            Campaign.Rebind();
            Engine.Changed += Flush;
        }
        internal void RestoreEconomyCheckpoint(string saved)
        {
            Engine = new StoryEngine(package, attachedRun, saved);
            ConfigureCampaignText();
            Campaign.Rebind();
            if (Economy.Config != null) Engine.EnableMemberEconomy(Economy.Config, Day);
            Engine.Changed += Flush;
            Flush();
        }
        private void ConfigureCampaignText()
        {
            string finance=Path.Combine(contentRoot,"campaign.finance-text.json");
            if(package.Id!="millsage" || !File.Exists(finance))return;
            try{Engine.ConfigureCampaignText(CampaignFinanceText.Load(finance,package));}
            catch(Exception error){core.LoggerInstance.Warning("FINANCE_TEXT_FALLBACK "+error.Message);}
        }
        internal void Detach()
        {
            core.Presentation?.Stop();ActOne?.Detach();CloseView(); Campaign?.Detach(); Economy?.Detach(); Integration?.Detach(); Engine = null; attachedStore = null; attachedRun = null;
        }
        internal void Draw()
        {
            if (Engine == null || !package.Enabled) return;
            if(NativeScene||ActOne.OwnCounterVisit||Opening.OwnVisit||(Native012.Pending||ActTwo.Pending||Diary.Pending||Return.Pending)&&Engine.Frame()==null)return;
            EnsureStyles();
            if(DeveloperPanels||Campaign.PanelOpen)Campaign.Draw(bodyStyle,buttonStyle,windowStyle);
            if(Campaign.PanelOpen)return;
            Integration.Draw(bodyStyle,buttonStyle,windowStyle);
            Economy.Draw(bodyStyle,buttonStyle,windowStyle);
            if (!Visible)
            {
                if(!DeveloperPanels)return;
                if (previewMenu)
                {
                    GUI.Label(new Rect(20,80,1050,38),"纯剧情预览 · 交易与改装尚未接入",headingStyle);
                    var available=Engine.Eligible("counter",Day).Concat(Engine.Eligible("night",Day)).ToList();
                    if (Engine.Frame()==null && available.Count==0) GUI.Label(new Rect(20,125,750,70),"当前没有可开始的剧情，后续章节待补充。",bodyStyle);
                    previewPage=Math.Min(previewPage,Math.Max(0,(available.Count-1)/8));
                    for(int i=previewPage*8;i<Math.Min(available.Count,previewPage*8+8);i++)
                    {
                        var scene=available[i];
                        string title=package.Chapters.Single(c=>c.Id==scene.ChapterId).Title;
                        int sequence=package.Scenes.Where(s=>s.Status=="ready").ToList().FindIndex(s=>s.Id==scene.Id)+1;
                        string label=title+" · 第 "+sequence+" 段（"+(scene.Context=="night"?"夜间":"柜台")+"）";
                        if(NativePanelGUI.Button(new Rect(20,125+(i%8)*50,850,44),label,buttonStyle)) Start(scene.Id,true);
                    }
                    if(previewPage>0 && NativePanelGUI.Button(new Rect(20,540,180,44),"上一组",buttonStyle)) previewPage--;
                    if((previewPage+1)*8<available.Count && NativePanelGUI.Button(new Rect(220,540,180,44),"下一组",buttonStyle)) previewPage++;
                }
                var active = Engine.Frame();
                if (active != null)
                {
                    if (CanResume && NativePanelGUI.Button(new Rect(Screen.width - 230, 100, 210, 44), "继续剧情", buttonStyle)) OpenView();
                    else if(!previewMenu && Integration.CanRequestResume && NativePanelGUI.Button(new Rect(Screen.width-230,100,210,44),"安排继续来访",buttonStyle)) Integration.RequestResume();
                }
                else
                {
                    string context = attachedStore.storeState == PlayerStore.StoreState.CLOSED ? "night" : "counter";
                    var scene = Engine.Eligible(context, Day).FirstOrDefault();
                    if (!previewMenu && scene != null && CanOpen(context) && (context=="night" || Integration.IsOwnCurrent) && NativePanelGUI.Button(new Rect(Screen.width - 230, 100, 210, 44), context == "night" ? "夜间事件" : "剧情来访", buttonStyle)) Start(scene.Id);
                }
                return;
            }
            float width = Math.Min(1100, Screen.width - 60), height = Math.Min(640, Screen.height - 80);
            NativePanelGUI.Window(781340, new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height), DrawWindow,
                previewMenu ? "纯剧情预览 · 交易与改装尚未接入" : Engine.Frame().Scene.Context == "night" ? "夜间事件" : "剧情来访", windowStyle);
        }
        private void EnsureStyles()
        {
            if (font == null)
            {
                font = FontLoader.Instance.simplifiedChineseFont.sourceFontFile;
                if (font == null) font = new Font("Microsoft YaHei");
                core.LoggerInstance.Msg("STORY_GUI_FONT " + font.name);
            }
            if (bodyStyle != null) return;
            bodyStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = 24, wordWrap = true, richText = false };
            headingStyle = new GUIStyle(bodyStyle) { fontSize = 28 };
            buttonStyle = new GUIStyle(GUI.skin.button) { font = font, fontSize = 22, wordWrap = true, richText = false };
            windowStyle = new GUIStyle(GUI.skin.window) { font = font, fontSize = 22 };
            windowBackground = Solid(new Color(.065f, .075f, .10f, 1));
            buttonBackground = Solid(new Color(.18f, .24f, .30f, 1));
            foreach (var style in new[] { bodyStyle, headingStyle, buttonStyle, windowStyle }) style.normal.textColor = Color.white;
            foreach (var state in new[] { windowStyle.normal, windowStyle.hover, windowStyle.active, windowStyle.focused,
                windowStyle.onNormal, windowStyle.onHover, windowStyle.onActive, windowStyle.onFocused })
            { state.background = windowBackground; state.textColor = Color.white; }
            foreach (var state in new[] { buttonStyle.normal, buttonStyle.hover, buttonStyle.active, buttonStyle.focused })
            { state.background = buttonBackground; state.textColor = Color.white; }
        }
        private static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color); texture.Apply(false, false);
            UnityEngine.Object.DontDestroyOnLoad(texture);
            return texture;
        }
        private void DrawWindow(int id)
        {
            var frame = Engine.Frame();
            if (frame == null) return;
            float width = Math.Min(1100, Screen.width - 60), height = Math.Min(640, Screen.height - 80);
            string roster = string.Join("　", frame.Scene.Participants.Select(p => package.Actors.Single(a => a.Id == p).DisplayName));
            GUI.Label(new Rect(24, 32, width - 240, 38), roster, bodyStyle);
            if (NativePanelGUI.Button(new Rect(width - 200, 32, 175, 42), "存档并暂停", buttonStyle)) { Pause(); return; }
            GUI.Label(new Rect(24, 82, width - 48, 44), frame.Speaker?.DisplayName ?? "", headingStyle);
            string portrait = string.IsNullOrEmpty(frame.Node.Portrait) ? frame.Speaker?.Portrait : frame.Node.Portrait;
            var texture = GetPortrait(portrait);
            float left = 24;
            if (texture != null)
            {
                float scale = Math.Min(160f / texture.width, 200f / texture.height);
                var imageContent = new GUIContent("") { image = texture };
                GUI.Label(new Rect(24, 142, texture.width * scale, texture.height * scale), imageContent, GUI.skin.label);
                left = 208;
            }
            var view = new Rect(left, 140, width - left - 24, height - 380);
            string text = frame.Node.Kind == "end" ? "本段已结束。" : frame.Text;
            if (pageRevision != frame.Revision)
            {
                pages.Clear(); page = 0; pageRevision = frame.Revision;
                int offset = 0;
                while (offset < text.Length)
                {
                    int low = 1, high = text.Length - offset, fit = 1;
                    while (low <= high)
                    {
                        int length = (low + high) / 2;
                        if (bodyStyle.CalcHeight(new GUIContent(text.Substring(offset, length)), view.width) <= view.height)
                        { fit = length; low = length + 1; }
                        else high = length - 1;
                    }
                    if (fit > 1 && char.IsHighSurrogate(text[offset + fit - 1])) fit--;
                    pages.Add(text.Substring(offset, fit)); offset += fit;
                }
                if (pages.Count == 0) pages.Add("");
            }
            GUI.Label(view, pages[page], bodyStyle);
            if (pages.Count > 1)
            {
                GUI.Label(new Rect(24, height - 233, width - 48, 36), $"{page + 1} / {pages.Count}", bodyStyle);
                if (page > 0 && NativePanelGUI.Button(new Rect(width - 340, height - 233, 145, 36), "上一页", buttonStyle)) page--;
                if (page < pages.Count - 1 && NativePanelGUI.Button(new Rect(width - 180, height - 233, 145, 36), "下一页", buttonStyle)) page++;
            }
            if (frame.Node.Kind == "choice" && page == pages.Count - 1)
            {
                if (frame.Choices.Count == 0) GUI.Label(new Rect(24, height - 180, width - 48, 70), "当前选项条件尚未满足，可稍后继续。", bodyStyle);
                for (int i = 0; i < frame.Choices.Count; i++)
                    if (NativePanelGUI.Button(new Rect(24, height - 185 + i * 54, width - 48, 48), frame.Choices[i].Text, buttonStyle))
                    { Choose(frame.Choices[i].Id, frame.Revision); break; }
            }
            else if (frame.Node.Kind != "choice" && NativePanelGUI.Button(new Rect(24, height - 90, width - 48, 54), page < pages.Count - 1 ? "下一页" : frame.Node.Kind == "end" ? "结束" : "继续", buttonStyle))
            { if (page < pages.Count - 1) page++; else Advance(frame.Revision); }
            WindowDrawn = true;
            DrawnRevision = frame.Revision;
        }
        private Texture2D GetPortrait(string relative)
        {
            if (string.IsNullOrEmpty(relative) || !StoryContent.SafeMedia(relative, ".png")) return null;
            if (portraits.TryGetValue(relative, out var existing)) return existing;
            string path = Path.Combine(contentRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Texture2D texture = null;
            if (File.Exists(path) && new FileInfo(path).Length <= 8 * 1024 * 1024)
            {
                try
                {
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(texture, new NativeArray<byte>(File.ReadAllBytes(path)), false))
                    { UnityEngine.Object.Destroy(texture); texture = null; }
                }
                catch (Exception error) { core.LoggerInstance.Warning("Optional portrait unavailable: " + error.Message); texture = null; }
            }
            portraits[relative] = texture;
            return texture;
        }
        internal Sprite CustomerPortrait(string relative)
        {
            if(string.IsNullOrEmpty(relative)) return null;
            if(customerPortraits.TryGetValue(relative,out var sprite)) return sprite;
            var texture=GetPortrait(relative);
            if(texture==null) return null;
            sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100);
            customerPortraits[relative]=sprite;
            return sprite;
        }
    }
}
