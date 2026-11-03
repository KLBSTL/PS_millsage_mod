using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TMPro;
using ResourceStoryMod.Story;
using UnityEngine;
using UnityEngine.UI;

namespace ResourceStoryMod
{
    // Layout only: native dialogue/typewriter/choices/history and the existing cursor.
    internal sealed class NativeActOneStage : IDisposable
    {
        private readonly Core core;
        private GameObject root;
        private sealed class RectState
        {
            internal Transform Node,Parent;internal int Sibling;internal Vector3 Position,Scale;
            internal Quaternion Rotation;internal RectTransform Rect;
            internal Vector2 Min,Max,Pivot,Size,Anchored;internal bool Active;
        }
        private readonly List<RectState> savedRects=new();
        private readonly Dictionary<long,(Transform Parent,Transform[] Children)> siblingOrders=new();
        private readonly List<(Behaviour Node,bool Enabled)> behaviours=new();
        private readonly List<(Graphic Node,Color Color,bool Raycast)> graphics=new();
        private readonly List<(TextMeshProUGUI Node,float Size,bool Wrap,bool AutoSize,TextAlignmentOptions Alignment)> texts=new();
        private readonly List<Image> addedImages=new();
        private readonly Dictionary<string,Sprite> sprites=new();
        private readonly List<Texture2D> textures=new();
        private readonly Dictionary<string,Image> people=new();
        private readonly Dictionary<string,RawImage> livePeople=new();
        private NativeLive2DStage live;
        private Image backdrop,dialoguePanel;
        private TextMeshProUGUI title;
        private string lastFrame;
        internal VenueShot Shot {get;private set;}
        internal bool Active=>root!=null;
        internal bool Restored {get;private set;}=true;
        internal NativeActOneStage(Core core){this.core=core;}
        private RectTransform Box(string name,Vector2 size,Vector2 position,Vector2? pivot=null)
        {
            var go=new GameObject(name);var rect=go.AddComponent<RectTransform>();rect.SetParent(root.transform,false);
            rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=pivot??new Vector2(.5f,.5f);rect.sizeDelta=size;rect.anchoredPosition=position;return rect;
        }
        private Image Image(string name,Vector2 size,Vector2 position,Color color,Vector2? pivot=null)
        {
            var rect=Box(name,size,position,pivot);var image=rect.gameObject.AddComponent<Image>();image.color=color;image.raycastTarget=false;return image;
        }
        private void SaveRect(Transform node)
        {
            if(savedRects.Any(s=>s.Node.NativeIdentity()==node.NativeIdentity()))return;
            if(node.parent!=null&&!siblingOrders.ContainsKey(node.parent.NativeIdentity().ToInt64())){
                var parent=node.parent;var children=new Transform[parent.childCount];for(int i=0;i<children.Length;i++)children[i]=parent.GetChild(i);
                siblingOrders.Add(parent.NativeIdentity().ToInt64(),(parent,children));
            }
            var r=node.TryCast<RectTransform>();
            var s=new RectState{Node=node,Parent=node.parent,Sibling=node.GetSiblingIndex(),Position=node.localPosition,Rotation=node.localRotation,Scale=node.localScale,Active=node.gameObject.activeSelf,Rect=r};
            if(r!=null){s.Min=r.anchorMin;s.Max=r.anchorMax;s.Pivot=r.pivot;s.Size=r.sizeDelta;s.Anchored=r.anchoredPosition;}
            savedRects.Add(s);
        }
        private void Disable(Behaviour node){if(behaviours.Any(b=>b.Node.NativeIdentity()==node.NativeIdentity()))return;behaviours.Add((node,node.enabled));node.enabled=false;}
        private void SaveBubble(Transform bubble)
        {
            SaveRect(bubble);foreach(var r in bubble.GetComponentsInChildren<RectTransform>(true))SaveRect(r);
            foreach(var g in bubble.GetComponentsInChildren<Graphic>(true)){
                graphics.Add((g,g.color,g.raycastTarget));
                if(g.TryCast<TextMeshProUGUI>() is TextMeshProUGUI text){
                    texts.Add((text,text.fontSize,text.enableWordWrapping,text.enableAutoSizing,text.alignment));
                    var ui=DialogUIManager.Instance;bool field=text.NativeIdentity()==ui.clientBubble.textfield.NativeIdentity()||text.NativeIdentity()==ui.playerBubble.textfield.NativeIdentity()||text.NativeIdentity()==ui.clientNameTMP.NativeIdentity();
                    foreach(var b in ui.dialogChoiceBubbles)if(text.NativeIdentity()==b.TMP.NativeIdentity())field=true;
                    if(!field)Disable(text);
                }
                else Disable(g);
            }
            // Counter sizing scripts must not fight the venue's bottom text bar.
            foreach(var c in bubble.GetComponentsInChildren<ContentSizeFitter>(true))Disable(c);
            foreach(var c in bubble.GetComponentsInChildren<LayoutGroup>(true))Disable(c);
            foreach(var c in bubble.GetComponentsInChildren<LayoutElement>(true))Disable(c);
            foreach(var c in bubble.GetComponentsInChildren<DialogBubbleWidth>(true))Disable(c);
        }
        private void Create()
        {
            root=new GameObject("MillsageNightScene");root.AddComponent<RectTransform>();
            // Vanilla shop is order1, system menus are order3-5. Venue art must
            // cover the shop without covering Escape or the native options.
            var canvas=root.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=2;
            var scaler=root.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.matchWidthOrHeight=.5f;
            backdrop=Image("Backdrop",new Vector2(1280,720),Vector2.zero,Color.white);
            foreach(string actor in new[]{"ms_nagi","ms_natsume","ms_mahoro","ms_hotaru","ms_houka"}){
                var image=Image("Actor_"+actor,new Vector2(390,1120),Vector2.zero,Color.white,new Vector2(.5f,1));image.preserveAspect=true;image.gameObject.SetActive(false);people.Add(actor,image);
                var rect=Box("Live2D_"+actor,new Vector2(390,1120),Vector2.zero,new Vector2(.5f,1));var raw=rect.gameObject.AddComponent<RawImage>();raw.raycastTarget=false;raw.gameObject.SetActive(false);livePeople.Add(actor,raw);
            }
            live=new NativeLive2DStage(core);
            dialoguePanel=Image("DialoguePanel",new Vector2(1232,180),new Vector2(0,-270),new Color(.075f,.055f,.07f,.97f));
            var heading=Box("VenueName",new Vector2(1150,42),new Vector2(0,330));
            title=heading.gameObject.AddComponent<TextMeshProUGUI>();title.font=FontLoader.Instance.simplifiedChineseFont;title.fontSize=24;title.color=new Color(1,.91f,.76f,1);title.alignment=TextAlignmentOptions.Center;title.raycastTarget=false;
            LoadAssets();
            var ui=DialogUIManager.Instance;
            var nodes=new List<Transform>{ui.transform,ui.clientBubble.transform,ui.playerBubble.transform,ui.clientNameBubble.transform};foreach(var b in ui.dialogChoiceBubbles)nodes.Add(b.transform);
            nodes=nodes.GroupBy(n=>n.NativeIdentity()).Select(g=>g.First()).ToList();
            foreach(var node in nodes)SaveRect(node);
            SaveBubble(ui.clientBubble.transform);SaveBubble(ui.playerBubble.transform);SaveBubble(ui.clientNameBubble.transform);foreach(var b in ui.dialogChoiceBubbles)SaveBubble(b.transform);
            foreach(var node in nodes){node.SetParent(root.transform,false);node.localScale=Vector3.one;}
            // Keep the fields in their native bubble so native visibility and
            // typewriter callbacks continue to control them.
            ui.clientBubble.textfield.transform.SetParent(ui.clientBubble.transform,false);ui.playerBubble.textfield.transform.SetParent(ui.playerBubble.transform,false);
            ui.clientNameTMP.transform.SetParent(ui.clientNameBubble.transform,false);
            foreach(var b in ui.dialogChoiceBubbles){
                b.TMP.transform.SetParent(b.transform,false);
                var image=b.GetComponent<Image>();if(image==null){image=b.gameObject.AddComponent<Image>();addedImages.Add(image);}
                image.enabled=true;image.color=new Color(.20f,.17f,.21f,.98f);image.raycastTarget=true;
            }
            ui.transform.gameObject.SetActive(true);root.AddComponent<GraphicRaycaster>();
            Restored=false;
        }
        private Sprite Load(string folder,JsonElement entry)
        {
            bool pixelArt=entry.TryGetProperty("pixelArtFile",out var replacement);
            string file=(pixelArt?replacement:entry.GetProperty("file")).GetString();
            if(file.Contains("..")||Path.IsPathRooted(file))throw new InvalidDataException("Unsafe venue asset path");
            string path=Path.Combine(folder,file);if(TestSession.Hash(path)!=entry.GetProperty(pixelArt?"pixelArtSha256":"sha256").GetString())throw new InvalidDataException("Venue asset changed");
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);textures.Add(texture);
            if(!ImageConversion.LoadImage(texture,new NativeArray<byte>(File.ReadAllBytes(path)),false))throw new InvalidDataException("Venue PNG rejected");
            texture.filterMode=pixelArt?FilterMode.Point:FilterMode.Bilinear;return Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100);
        }
        private void LoadAssets()
        {
            string folder=Path.Combine(core.Root,"ModWorkbench/res/prepared/venue");using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"assets.json")));
            foreach(var entry in document.RootElement.GetProperty("entries").EnumerateArray())sprites.Add(entry.GetProperty("actor").GetString()+"/"+entry.GetProperty("emotion").GetString(),Load(folder,entry));
            foreach(var entry in document.RootElement.GetProperty("backgrounds").EnumerateArray())sprites.Add("background/"+entry.GetProperty("id").GetString(),Load(folder,entry));
        }
        internal void Show(StoryFrame frame,string speaking=null,int part=0)
        {
            if(!VenuePresentation.IsVenue(frame.Scene.Id))throw new InvalidOperationException("Shop scenes cannot create venue portraits");
            if(root==null){try{Create();}catch{Dispose();throw;}}
            string stamp=frame.Scene.Id+"/"+frame.Node.Id+"/"+speaking+"/"+part;if(stamp==lastFrame)return;lastFrame=stamp;
            title.text=VenuePresentation.Title(frame.Scene.Id,frame.Node.Id);
            Shot=VenuePresentation.Shot(frame.Scene,frame.Node,ActOnePresentation.VisibleActors(frame.Scene,frame.Node.Id,part));
            backdrop.sprite=sprites["background/"+Shot.Background];
            for(int i=0;i<Shot.Actors.Length;i++){
                string actor=Shot.Actors[i];var image=people[actor];image.gameObject.SetActive(true);
                image.sprite=sprites.GetValueOrDefault(actor+"/"+ActOnePresentation.Emotion(frame.Scene.Id,frame.Node.Id,actor,part))??sprites.GetValueOrDefault(actor+"/neutral");
                image.color=image.sprite==null?Color.clear:actor==speaking||!MemberBusiness.ValidActor(speaking)?Color.white:new Color(.94f,.94f,.94f,1);
                var rect=image.rectTransform;rect.sizeDelta=Shot.Wide?new Vector2(215,450):new Vector2(390,1120);rect.anchoredPosition=new Vector2(Shot.X(i),Shot.Wide?286:284);
                var raw=livePeople[actor];raw.gameObject.SetActive(true);image.enabled=!live.Bind(actor,raw,rect.sizeDelta,rect.anchoredPosition,image.color);raw.enabled=!image.enabled;
            }
            foreach(var p in people.Where(p=>!Shot.Actors.Contains(p.Key))){p.Value.gameObject.SetActive(false);livePeople[p.Key].gameObject.SetActive(false);}
        }
        private static void Place(RectTransform rect,Vector2 size,Vector2 position)
        {
            rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=new Vector2(.5f,.5f);rect.localScale=Vector3.one;rect.localRotation=Quaternion.identity;rect.sizeDelta=size;rect.anchoredPosition=position;
        }
        private static void Field(TextMeshProUGUI text,float width,float height,float font,Vector2 position)
        {
            Place(text.rectTransform,new Vector2(width,height),position);text.enableAutoSizing=false;text.fontSize=font;text.enableWordWrapping=true;text.alignment=TextAlignmentOptions.TopLeft;text.color=new Color(1,.96f,.91f,1);text.raycastTarget=false;
        }
        private static int NativeCanvasOrder(Transform node)
        {
            var c=node.GetComponentInParent<Canvas>();return c==null?3:c.overrideSorting?c.sortingOrder:c.rootCanvas.sortingOrder;
        }
        internal void Layout()
        {
            if(root==null)return;var ui=DialogUIManager.Instance;
            var page=core.Presentation.Cursor?.Page();bool paused=core.Presentation.Paused||core.Story.ReportBlocked||Time.timeScale<=0||StoreUIManager.Instance.escapeUIManager.menuPanel.activeInHierarchy||StoreUIManager.Instance.gameOptionUIManager.IsOpen;
            live?.Tick(page,paused,core.Presentation.VoiceActor,core.Presentation.VoiceMouth);
            foreach(var p in people)if(p.Value.gameObject.activeSelf){p.Value.enabled=live?.Loaded(p.Key)!=true;livePeople[p.Key].enabled=!p.Value.enabled;}
            int order=2;var storeUI=StoreUIManager.Instance;var escape=storeUI.escapeUIManager;var options=storeUI.gameOptionUIManager;
            if(escape?.menuPanel?.activeInHierarchy==true)order=Math.Min(order,NativeCanvasOrder(escape.menuPanel.transform)-1);
            if(options?.IsOpen==true&&options.window?.root!=null)order=Math.Min(order,NativeCanvasOrder(options.window.root.transform)-1);
            root.GetComponent<Canvas>().sortingOrder=order;
            var list=new List<DialogChoiceBubble>();foreach(var b in ui.dialogChoiceBubbles)if(b.gameObject.activeInHierarchy)list.Add(b);var choices=list.ToArray();
            float font=24,choiceHeight=0;
            foreach(var b in choices)choiceHeight+=Math.Max(42,b.TMP.GetPreferredValues(b.TMP.text,1120,float.PositiveInfinity).y*font/Math.Max(1,b.TMP.fontSize)+24)+10;
            if(choiceHeight>290)font=20;
            float bottom=-340;
            for(int i=choices.Length-1;i>=0;i--){
                var b=choices[i];b.TMP.fontSize=font;float h=Math.Max(42,b.TMP.GetPreferredValues(b.TMP.text,1120,float.PositiveInfinity).y+24);
                Place(b.transform.Cast<RectTransform>(),new Vector2(1180,h),new Vector2(0,bottom+h/2));Field(b.TMP,1120,h-16,font,new Vector2(0,-2));bottom+=h+10;
            }
            float bodyBottom=choices.Length>0?bottom+10:-334,bodyTop=bodyBottom+100;
            foreach(var bubble in new[]{ui.clientBubble,ui.playerBubble}){
                Place(bubble.transform.Cast<RectTransform>(),new Vector2(1180,100),new Vector2(0,(bodyBottom+bodyTop)/2));Field(bubble.textfield,1160,100,26,Vector2.zero);
            }
            ui.clientNameBubble.SetActive(ui.clientBubble.gameObject.activeInHierarchy||ui.playerBubble.gameObject.activeInHierarchy);
            ui.clientNameTMP.text=core.Presentation.Cursor?.Page()?.SpeakerName??"场景提示";
            Place(ui.clientNameBubble.transform.Cast<RectTransform>(),new Vector2(1160,36),new Vector2(0,bodyTop+26));Field(ui.clientNameTMP,1160,36,25,Vector2.zero);ui.clientNameTMP.color=new Color(1,.81f,.49f,1);
            float top=Math.Max(-180,bodyTop+52);Place(dialoguePanel.rectTransform,new Vector2(1232,top+360),new Vector2(0,(top-360)/2));
        }
        internal object Status()=>new{active=Active,shot=Shot?.Key,actors=Shot?.Actors,background=Shot?.Background,wide=Shot?.Wide,realtimeLive2D=live?.Active==true,live=live?.Status(),liveModels=NativeLive2DStage.LiveCount};
        public void Dispose()
        {
            live?.Dispose();live=null;livePeople.Clear();
            // Unity may already have destroyed the canvas when the loader
            // deinitializes. Dead native transforms must never be traversed.
            if(root==null){savedRects.Clear();siblingOrders.Clear();behaviours.Clear();graphics.Clear();texts.Clear();addedImages.Clear();}
            bool restoring=savedRects.Count>0;
            foreach(var b in behaviours)if(b.Node!=null)b.Node.enabled=b.Enabled;behaviours.Clear();
            foreach(var g in graphics)if(g.Node!=null){g.Node.color=g.Color;g.Node.raycastTarget=g.Raycast;}graphics.Clear();
            foreach(var t in texts)if(t.Node!=null){t.Node.fontSize=t.Size;t.Node.enableWordWrapping=t.Wrap;t.Node.enableAutoSizing=t.AutoSize;t.Node.alignment=t.Alignment;}texts.Clear();
            foreach(var s in savedRects)if(s.Node!=null)s.Node.SetParent(s.Parent,false);
            foreach(var s in savedRects)if(s.Node!=null)s.Node.gameObject.SetActive(s.Active);
            int Depth(Transform n){int d=0;while(n!=null&&n.parent!=null){n=n.parent;d++;}return d;}
            foreach(var s in savedRects.OrderBy(s=>Depth(s.Node)))if(s.Node!=null){
                s.Node.SetSiblingIndex(s.Sibling);
                if(s.Rect!=null){s.Rect.anchorMin=s.Min;s.Rect.anchorMax=s.Max;s.Rect.pivot=s.Pivot;s.Rect.sizeDelta=s.Size;s.Rect.anchoredPosition=s.Anchored;}
                s.Node.localPosition=s.Position;s.Node.localRotation=s.Rotation;s.Node.localScale=s.Scale;
            }
            // Restoring only moved siblings can push an earlier sibling out of
            // position again. Restore each original parent's complete ordering.
            foreach(var group in siblingOrders.Values){int index=0;foreach(var child in group.Children)if(child!=null&&child.parent?.NativeIdentity()==group.Parent?.NativeIdentity())child.SetSiblingIndex(index++);}
            siblingOrders.Clear();
            if(restoring){
                var failures=savedRects.Where(s=>s.Node==null||s.Node.parent?.NativeIdentity()!=s.Parent?.NativeIdentity()||Vector3.Distance(s.Node.localPosition,s.Position)>.01f||Vector3.Distance(s.Node.localScale,s.Scale)>.0001f||s.Node.GetSiblingIndex()!=s.Sibling||s.Node.gameObject.activeSelf!=s.Active).Select(s=>new{name=s.Node?.name,parent=s.Node?.parent?.name,expectedParent=s.Parent?.name,position=s.Node==null?"missing":s.Node.localPosition.ToString(),expectedPosition=s.Position.ToString(),sibling=s.Node?.GetSiblingIndex(),expectedSibling=s.Sibling,active=s.Node?.gameObject.activeSelf,expectedActive=s.Active}).ToArray();
                Restored=failures.Length==0;if(!Restored)core.LoggerInstance.Warning("VENUE_RESTORE_DIFF "+JsonSerializer.Serialize(failures,Core.Json));
            }
            savedRects.Clear();foreach(var image in addedImages)if(image!=null)UnityEngine.Object.Destroy(image);addedImages.Clear();
            if(root!=null)UnityEngine.Object.Destroy(root);root=null;people.Clear();title=null;backdrop=dialoguePanel=null;lastFrame=null;Shot=null;
            foreach(var sprite in sprites.Values)UnityEngine.Object.Destroy(sprite);sprites.Clear();foreach(var texture in textures)UnityEngine.Object.Destroy(texture);textures.Clear();
        }
    }
}
