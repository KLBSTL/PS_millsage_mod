using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    // Explicit P0 fallback: Core-rendered still expressions, not a realtime model.
    // Only changes the existing customer's sprite; no canvas, collider, or input.
    internal sealed class NativePresentationPortrait : IDisposable
    {
        private readonly Core core;
        private readonly Dictionary<string,Sprite> sprites=new();
        private readonly List<Texture2D> textures=new();
        private readonly Dictionary<string,Sprite> mouthSprites=new();
        private string speaker="ms_nagi";
        private bool mouthOpen;
        private float mouthAt;
        internal int MouthChanges {get;private set;}
        private SpriteRenderer renderer;
        private SpriteRenderer companion;
        private string companionActor;
        private Sprite original;
        private string emotion="";
        private long visitor;
        private bool departing;
        private string lastBegin;
        private readonly Queue<object> lifecycle=new();
        private void Trace(string action){if(string.IsNullOrEmpty(core.Test?.Request.Native012))return;if(lifecycle.Count==24)lifecycle.Dequeue();lifecycle.Enqueue(new{action,frame=Time.frameCount,active=Active,departing,imageActive=renderer?.gameObject.activeInHierarchy,monoActive=StoreClientMono.Instance?.gameObject.activeInHierarchy,client=PlayerStore.instance?.currentClientInstance?.NativeIdentity().ToInt64()});}
        internal int DepartureFrames {get;private set;}
        internal int Departures {get;private set;}
        internal int VisiblePlaceholderFrames {get;private set;}
        internal bool Active=>renderer!=null&&sprites.Count==3;
        internal string Emotion=>emotion;
        internal bool Restored {get;private set;}=true;
        internal NativePresentationPortrait(Core core){this.core=core;}
        internal bool Begin()
        {
            Dispose();
            var client=PlayerStore.instance?.currentClientInstance;
            var mono=StoreClientMono.Instance;
            string id=client?.storeClient?.identifier;
            if(!Supported(id)||mono?.image?.sprite==null){lastBegin="client="+id+"; supported="+Supported(id)+"; mono="+(mono!=null)+"; image="+(mono?.image!=null)+"; sprite="+(mono?.image?.sprite!=null);return false;}
            string root=Path.Combine(core.Root,"ModWorkbench","res","tools","live2d");
            string probePath=Path.Combine(root,"core-probe-report.json");
            if(!File.Exists(probePath)){lastBegin="optional portraits unavailable";return false;}
            renderer=mono.image;original=renderer.sprite;
            visitor=client.NativeIdentity().ToInt64();
            try{
                using var report=JsonDocument.Parse(File.ReadAllText(probePath));
                if(!report.RootElement.GetProperty("passed").GetBoolean())throw new InvalidDataException("Core probe has not passed.");
                string basePath=Path.GetFullPath(Path.Combine(root,"models","018_adv-nagi_018_casual_spring_01","static-fallback"))+Path.DirectorySeparatorChar;
                foreach(string state in new[]{"neutral","sad","smile"}){
                    string path=Path.Combine(basePath,"nagi-"+state+".png");
                    var entry=report.RootElement.GetProperty("static_renders").EnumerateArray().Single(x=>Path.GetFileName(x.GetProperty("path").GetString())==Path.GetFileName(path));
                    if(TestSession.Hash(path)!=entry.GetProperty("sha256").GetString())throw new InvalidDataException("Static expression hash changed.");
                    var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);textures.Add(texture);
                    if(!ImageConversion.LoadImage(texture,new NativeArray<byte>(File.ReadAllBytes(path)),false))throw new InvalidDataException("Static expression PNG rejected.");
                    texture.filterMode=FilterMode.Bilinear;
                    // Frame the head and torso in the native shop opening. Keep
                    // identical cropping/pivot across expressions to avoid jumping.
                    float height=texture.height*.40f;
                    float ppu=height/Math.Max(.01f,original.bounds.size.y);
                    sprites[state]=Sprite.Create(texture,new Rect(0,texture.height-height,texture.width,height),new Vector2(.5f,.62f),ppu);
                }
                LoadMouthFrames();LoadOpeningFrames(id);Restored=false;Set("neutral");lastBegin="ready";Trace("begin");return true;
            }catch(Exception error){lastBegin=error.ToString();core.LoggerInstance.Warning("PRESENTATION_STATIC_FALLBACK_UNAVAILABLE "+error.Message);Dispose();return false;}
        }
        internal void SetForNode(string scene,string node,int part=0)
        {
            if(ActOnePresentation.NativeScene(scene)){
                var data=core.Story.Package.Scenes.Single(x=>x.Id==scene);
                var visible=ActOnePresentation.VisibleActors(data,node,part,core.Story.ReadState);
                if(!visible.Contains(speaker))SetSpeaker(visible.FirstOrDefault());
                companionActor=visible.FirstOrDefault(x=>x!=speaker);
                var cue=StoryPerformance.Find(scene,node,part,speaker);string mapped=cue?.Expression;
                Set(mapped==null?ActOnePresentation.Emotion(scene,node,speaker,part):mapped.Contains("smile")?"smile":mapped.Contains("idle")?"neutral":"sad");SetCompanion();return;
            }
            if(scene=="ms_scene_012"){Set(node=="line_002"?"sad":node=="line_004a"?"smile":"neutral");return;}
            Set(scene=="ms_scene_001"&&node is "line_004" or "line_005"?"sad":node is "line_004a" or "line_005a" or "line_006a"?"smile":"neutral");
        }
        internal void SetSpeaker(string actor){if(new[]{"ms_mahoro","ms_houka","ms_hotaru","ms_natsume","ms_nagi"}.Contains(actor))speaker=actor;mouthOpen=false;}
        private static bool Supported(string id)=>id?.StartsWith("ms_member_visit_")==true&&MemberBusiness.ValidActor(id.Substring("ms_member_visit_".Length))||id=="ms_visit_millsage_ms_scene_012"||id?.StartsWith("ms_visit_millsage_")==true&&(PackageC.OpeningScene(id.Substring("ms_visit_millsage_".Length))||ActOnePresentation.NativeScene(id.Substring("ms_visit_millsage_".Length)));
        private void LoadOpeningFrames(string id)
        {
            if(id==null)return;string[] actors;
            if(id.StartsWith("ms_member_visit_")){speaker=id.Substring("ms_member_visit_".Length);actors=new[]{speaker};}
            else {if(!id.StartsWith("ms_visit_millsage_"))return;string scene=id.Substring("ms_visit_millsage_".Length);if(!PackageC.OpeningScene(scene)&&!ActOnePresentation.NativeScene(scene))return;actors=core.Story.Package.Scenes.Single(x=>x.Id==scene).Participants.ToArray();speaker=PackageC.OpeningActor(scene)??actors.FirstOrDefault();}
            string root=Path.Combine(core.Root,"ModWorkbench/res/prepared/p4");using var data=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"portraits.json")));
            foreach(var entry in data.RootElement.GetProperty("entries").EnumerateArray()){
                if(!actors.Contains(entry.GetProperty("actor").GetString()))continue;
                string file=entry.GetProperty("file").GetString();if(file.Contains("..")||Path.IsPathRooted(file)||!file.StartsWith("portraits/"))throw new InvalidDataException("Unsafe opening frame path");
                file=Path.Combine(root,file);if(TestSession.Hash(file)!=entry.GetProperty("sha256").GetString())throw new InvalidDataException("Opening expression hash changed");
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);textures.Add(texture);if(!ImageConversion.LoadImage(texture,new NativeArray<byte>(File.ReadAllBytes(file)),false))throw new InvalidDataException("Opening expression rejected");
                texture.filterMode=FilterMode.Bilinear;float height=texture.height*.4f;
                mouthSprites.Add(entry.GetProperty("actor").GetString()+"/"+entry.GetProperty("emotion").GetString()+"/False",Sprite.Create(texture,new Rect(0,texture.height-height,texture.width,height),new Vector2(.5f,.62f),height/Math.Max(.01f,original.bounds.size.y)));
            }
        }
        internal void Mouth(string actor,float level)
        {
            if(!Active||Time.unscaledTime<mouthAt)return;mouthAt=Time.unscaledTime+.08f;
            bool open=actor==speaker&&level>.018f;
            if(open!=mouthOpen){mouthOpen=open;MouthChanges++;}Set(emotion);
        }
        private void LoadMouthFrames()
        {
            string root=Path.Combine(core.Root,"ModWorkbench","res","prepared","p2"),path=Path.Combine(root,"portraits.json");if(!File.Exists(path))return;
            try{
                using var document=JsonDocument.Parse(File.ReadAllText(path));if(document.RootElement.GetProperty("version").GetInt32()!=1)return;
                foreach(var entry in document.RootElement.GetProperty("entries").EnumerateArray()){
                    string file=entry.GetProperty("file").GetString();
                    if(file.Contains("..")||Path.IsPathRooted(file)||!file.StartsWith("portraits/",StringComparison.Ordinal))throw new InvalidDataException("Unsafe portrait frame path");
                    file=Path.Combine(root,file);if(TestSession.Hash(file)!=entry.GetProperty("sha256").GetString())throw new InvalidDataException("Mouth frame hash changed");
                    var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);textures.Add(texture);
                    if(!ImageConversion.LoadImage(texture,new NativeArray<byte>(File.ReadAllBytes(file)),false))throw new InvalidDataException("Mouth frame rejected");
                    texture.filterMode=FilterMode.Bilinear;float height=texture.height*.4f;
                    var frame=Sprite.Create(texture,new Rect(0,texture.height-height,texture.width,height),new Vector2(.5f,.62f),height/Math.Max(.01f,original.bounds.size.y));
                    mouthSprites.Add(entry.GetProperty("actor").GetString()+"/"+entry.GetProperty("emotion").GetString()+"/"+entry.GetProperty("open").GetBoolean(),frame);
                }
            }catch(Exception e){core.LoggerInstance.Warning("P2_MOUTH_FALLBACK "+e.Message);foreach(var frame in mouthSprites.Values)UnityEngine.Object.Destroy(frame);mouthSprites.Clear();}
        }
        private void Set(string state)
        {
            if(!Active)return;
            Sprite next=mouthSprites.GetValueOrDefault(speaker+"/"+state+"/"+mouthOpen)??mouthSprites.GetValueOrDefault(speaker+"/"+state+"/False")??sprites[state];
            if(renderer.sprite?.NativeIdentity()!=next.NativeIdentity())renderer.sprite=next;emotion=state;
        }
        private void SetCompanion()
        {
            if(!Active)return;
            if(companionActor==null){if(companion!=null)companion.enabled=false;return;}
            if(companion==null){
                var go=new GameObject("MillsageCompanion");go.transform.SetParent(renderer.transform,false);
                go.transform.localPosition=new Vector3(-original.bounds.size.x*.65f,original.bounds.size.y*.05f,.01f);go.transform.localScale=new Vector3(.62f,.62f,1);
                companion=go.AddComponent<SpriteRenderer>();companion.sortingLayerID=renderer.sortingLayerID;companion.sortingOrder=renderer.sortingOrder-1;companion.maskInteraction=renderer.maskInteraction;companion.color=new Color(.78f,.82f,.88f,.95f);
            }
            companion.sprite=mouthSprites.GetValueOrDefault(companionActor+"/neutral/False");companion.enabled=companion.sprite!=null;
        }
        internal void Leaving(){if(Active&&!departing){Trace("leaving");departing=true;Departures++;}}
        internal void Left(){Trace("left");if(Active&&StoreClientMono.Instance?.gameObject.activeInHierarchy==false)Dispose();}
        internal void BeforeArrival(){Trace("beforeArrival");Dispose();}
        internal void LateUpdate()
        {
            if(!Active||!departing||!renderer.gameObject.activeInHierarchy)return;
            // Native dismissal clears currentClientInstance before the walking
            // animation ends. Retain the same portrait through that interval.
            Set(emotion);DepartureFrames++;
            if(renderer.sprite?.NativeIdentity()==original?.NativeIdentity())VisiblePlaceholderFrames++;
        }
        internal void SyncVisitor()
        {
            var client=PlayerStore.instance?.currentClientInstance;string id=client?.storeClient?.identifier;
            if(!Supported(id)){
                if(Active){if(client==null&&renderer.gameObject.activeInHierarchy){Leaving();Set(emotion);}else Dispose();}return;
            }
            if(!Active||visitor!=client.NativeIdentity().ToInt64())Begin();
            if(core.Story?.Opening?.Trading==true){SetSpeaker(PackageC.OpeningActor(core.Story.Opening.VisitScene));Set("neutral");}
            else if(core.Story?.Diary?.Trading==true){SetSpeaker(DiaryPresentation.Lead(core.Story.Opening.VisitScene));companionActor=null;Set("neutral");SetCompanion();}
            else Set(emotion);
        }
        public void Dispose()
        {
            if(companion!=null)UnityEngine.Object.Destroy(companion.gameObject);companion=null;companionActor=null;
            if(Active)Trace("dispose");
            if(renderer!=null&&original!=null){
                if(sprites.Values.Concat(mouthSprites.Values).Any(x=>x.NativeIdentity()==renderer.sprite?.NativeIdentity()))renderer.sprite=original;
                Restored=!sprites.Values.Concat(mouthSprites.Values).Any(x=>x.NativeIdentity()==renderer.sprite?.NativeIdentity());
            }
            foreach(var sprite in sprites.Values)UnityEngine.Object.Destroy(sprite);
            foreach(var sprite in mouthSprites.Values)UnityEngine.Object.Destroy(sprite);
            foreach(var texture in textures)UnityEngine.Object.Destroy(texture);
            sprites.Clear();mouthSprites.Clear();textures.Clear();renderer=null;original=null;emotion="";visitor=0;departing=false;speaker="ms_nagi";mouthOpen=false;
        }
        internal object Status()=>new{active=Active,lastBegin,emotion,loaded=sprites.Count,mouthFrames=mouthSprites.Count,mouthChanges=MouthChanges,speaker,mouthOpen,restored=Restored,departing,departures=Departures,departureFrames=DepartureFrames,visiblePlaceholderFrames=VisiblePlaceholderFrames,lifecycle=lifecycle.ToArray(),kind="static_core_mesh_expressions_with_optional_audio_mouth_flipbook",realtimeAnimation=false};
    }
}
