using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using ResourceStoryMod.Story;
using UnityEngine;
using UnityEngine.UI;

namespace ResourceStoryMod
{
    internal sealed class NativeLive2DStage : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)] private delegate int CreateFn([MarshalAs(UnmanagedType.LPWStr)]string core,byte[] bytes,uint size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CountFn(int id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ParameterFn(int id,int p,[Out]float[] values);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int TargetFn(int id,IntPtr target,float[] bounds);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int TextureFn(int id,int index,IntPtr texture);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void PoseFn(int id,float[] values,int count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void StatsFn(int id,[Out]double[] values);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ErrorFn(int id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr EventFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ReleaseFn(int id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LiveCountFn();
        private static LiveCountFn liveCount;
        private static IntPtr library,renderEvent;private static CreateFn create;private static CountFn count;private static ParameterFn parameter;private static TargetFn target;private static TextureFn texture;private static PoseFn pose;private static StatsFn stats;private static ErrorFn error;private static ReleaseFn release;
        private readonly Core core;private readonly string folder;private JsonElement manifest;
        private readonly Dictionary<string,Actor> actors=new();private readonly HashSet<string> failed=new();
        private int tickFrame=-1;
        private sealed class Expression {internal (int Index,float Value,string Blend)[] Parameters;}
        private sealed class Actor
        {
            internal int Handle;internal string Id,Expression="exp_idle01.exp3",PreviousExpression="exp_idle01.exp3",Motion="mtn_idle01_C",Cue;
            internal float Clock,MotionAt,ExpressionAt,NextFrame;internal RenderTexture Output;internal RawImage Image;internal float[] Defaults,Values,Minimum,Maximum,MotionFrom;
            internal Dictionary<string,int> Ids=new();internal Dictionary<string,Expression> Expressions=new();internal Dictionary<string,Live2DMotion> Motions=new();
            internal List<Texture2D> Textures=new();internal double[] Stats=new double[5];internal int MotionStarts,ExpressionChanges;
        }
        internal NativeLive2DStage(Core core){this.core=core;folder=Path.Combine(core.Root,"ModWorkbench/res/prepared/live2d");}
        private string FilePath(string relative)
        {
            if(string.IsNullOrEmpty(relative)||Path.IsPathRooted(relative)||relative.Contains(".."))throw new InvalidDataException("Unsafe realtime resource path");return Path.Combine(folder,relative);
        }
        private static T Export<T>(string name) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));
        private void Load()
        {
            if(manifest.ValueKind!=JsonValueKind.Undefined)return;
            if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Direct3D11)throw new NotSupportedException("Realtime Live2D requires the game's D3D11 device");
            using var doc=JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(folder,"models.json")));manifest=doc.RootElement.Clone();
            string bridge=FilePath(manifest.GetProperty("bridge").GetString());if(TestSession.Hash(bridge)!=manifest.GetProperty("bridgeSHA256").GetString())throw new InvalidDataException("Realtime bridge hash changed");
            if(library==IntPtr.Zero){library=NativeLibrary.Load(bridge);create=Export<CreateFn>("msCreate");count=Export<CountFn>("msCount");parameter=Export<ParameterFn>("msParameter");target=Export<TargetFn>("msTarget");texture=Export<TextureFn>("msTexture");pose=Export<PoseFn>("msPose");stats=Export<StatsFn>("msStats");error=Export<ErrorFn>("msError");release=Export<ReleaseFn>("msAbandon");liveCount=Export<LiveCountFn>("msLiveCount");renderEvent=Export<EventFn>("msRenderEvent")();}
        }
        private string Error(int handle)=>Marshal.PtrToStringUTF8(error(handle))??"Unknown realtime error";
        private Actor Make(string id,RawImage image)
        {
            Load();var spec=manifest.GetProperty("models").EnumerateArray().Single(x=>x.GetProperty("actor").GetString()==id);
            foreach(var f in spec.GetProperty("files").EnumerateArray())if(TestSession.Hash(FilePath(f.GetProperty("file").GetString()))!=f.GetProperty("sha256").GetString())throw new InvalidDataException("Realtime model dependency hash changed");
            string corePath=Path.GetFullPath(Path.Combine(core.Root,manifest.GetProperty("core").GetString()));if(!corePath.StartsWith(core.Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||TestSession.Hash(corePath)!=manifest.GetProperty("coreSHA256").GetString())throw new InvalidDataException("Official Core hash changed");
            var a=new Actor{Id=id,Image=image};actors.Add(id,a);var moc=System.IO.File.ReadAllBytes(FilePath(spec.GetProperty("moc").GetString()));a.Handle=create(corePath,moc,(uint)moc.Length);if(a.Handle==0)throw new InvalidOperationException(Error(0));
            int n=count(a.Handle);a.Defaults=new float[n];a.Values=new float[n];a.Minimum=new float[n];a.Maximum=new float[n];
            for(int i=0;i<n;i++){var v=new float[3];string name=Marshal.PtrToStringUTF8(parameter(a.Handle,i,v));a.Ids.Add(name,i);a.Values[i]=a.Defaults[i]=v[0];a.Minimum[i]=v[1];a.Maximum[i]=v[2];}
            a.Output=new RenderTexture(spec.GetProperty("width").GetInt32(),spec.GetProperty("height").GetInt32(),0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);a.Output.name="Millsage Live2D "+id;a.Output.useMipMap=false;a.Output.filterMode=FilterMode.Bilinear;if(!a.Output.Create())throw new InvalidOperationException("Realtime render target rejected");
            if(target(a.Handle,a.Output.GetNativeTexturePtr(),spec.GetProperty("bounds").EnumerateArray().Select(x=>x.GetSingle()).ToArray())!=1)throw new InvalidOperationException(Error(a.Handle));
            int ti=0;foreach(var file in spec.GetProperty("textures").EnumerateArray()){
                var tex=new Texture2D(2,2,TextureFormat.RGBA32,false,true);a.Textures.Add(tex);if(!ImageConversion.LoadImage(tex,new NativeArray<byte>(System.IO.File.ReadAllBytes(FilePath(file.GetString()))),false))throw new InvalidDataException("Realtime atlas rejected");
                if(texture(a.Handle,ti++,tex.GetNativeTexturePtr())!=1)throw new InvalidOperationException(Error(a.Handle));
            }
            foreach(var e in spec.GetProperty("expressions").EnumerateObject()){
                using var d=JsonDocument.Parse(System.IO.File.ReadAllText(FilePath(e.Value.GetString())));a.Expressions[e.Name]=new Expression{Parameters=d.RootElement.GetProperty("Parameters").EnumerateArray().Where(p=>a.Ids.ContainsKey(p.GetProperty("Id").GetString())).Select(p=>(a.Ids[p.GetProperty("Id").GetString()],p.GetProperty("Value").GetSingle(),p.TryGetProperty("Blend",out var blend)?blend.GetString():"Add")).ToArray()};
            }
            foreach(var m in spec.GetProperty("motions").EnumerateObject().Where(m=>m.Name.EndsWith("_C")))a.Motions[m.Name]=Live2DMotion.Read(System.IO.File.ReadAllText(FilePath(m.Value.GetString())));
            image.texture=a.Output;image.uvRect=new Rect(0,1,1,-1);image.raycastTarget=false;core.LoggerInstance.Msg("P11_LIVE2D_READY "+id+" parameters="+n);return a;
        }
        internal bool Bind(string id,RawImage image,Vector2 size,Vector2 position,Color color)
        {
            if(failed.Contains(id)||manifest.ValueKind==JsonValueKind.Undefined&&!File.Exists(Path.Combine(folder,"models.json")))return false;
            try{
                var a=actors.GetValueOrDefault(id)??Make(id,image);a.Image=image;image.texture=a.Output;
                float ratio=a.Output.width/(float)a.Output.height,w=Math.Min(size.x,size.y*ratio);image.rectTransform.sizeDelta=new Vector2(w,w/ratio);image.rectTransform.anchoredPosition=position;image.color=color;return Ready(a);
            }catch(Exception e){Fail(id,e.Message);return false;}
        }
        private void Fail(string id,string message){failed.Add(id);if(actors.Remove(id,out var a))Free(a);core.LoggerInstance.Warning("P11_LIVE2D_STATIC_FALLBACK "+id+" "+message);}
        private static void ApplyExpression(Actor a,string name,float[] values)
        {
            if(!a.Expressions.TryGetValue(name,out var e))return;foreach(var p in e.Parameters){if(p.Blend=="Multiply")values[p.Index]*=p.Value;else if(p.Blend=="Overwrite")values[p.Index]=p.Value;else values[p.Index]+=p.Value;}
        }
        internal void Tick(StoryPresentationPage page,bool paused,string voiceActor,float mouth)
        {
            if(tickFrame==Time.frameCount)return;tickFrame=Time.frameCount;
            foreach(var a in actors.Values.ToArray()){
                if(a.Image==null||!a.Image.gameObject.activeInHierarchy)continue;
                try{
                    string fault=Error(a.Handle);if(!string.IsNullOrEmpty(fault))throw new InvalidOperationException(fault);
                    if(paused)continue;a.Clock+=Math.Min(Time.unscaledDeltaTime,.05f);if(Time.unscaledTime<a.NextFrame)continue;a.NextFrame=Time.unscaledTime+1f/30;
                    var cue=page==null?null:StoryPerformance.Find(page.Frame.Scene.Id,page.Frame.Node.Id,page.DialoguePart,a.Id);
                    string mood=page==null?"neutral":ActOnePresentation.Emotion(page.Frame.Scene.Id,page.Frame.Node.Id,a.Id,page.DialoguePart);
                    string next=cue?.Expression??(mood=="sad"?"exp_sad01.exp3":mood=="smile"?"exp_smile01.exp3":"exp_idle01.exp3");if(!a.Expressions.ContainsKey(next))next="exp_idle01.exp3";
                    if(next!=a.Expression){a.PreviousExpression=a.Stats[0]==0?next:a.Expression;a.Expression=next;a.ExpressionAt=a.Clock;a.ExpressionChanges++;}
                    string motion=cue?.Motion??(mood=="sad"?"mtn_sad01_C":"mtn_idle01_C");if(!a.Motions.ContainsKey(motion))motion="mtn_idle01_C";
                    string cueId=page==null?null:page.Frame.Scene.Id+"/"+page.Frame.Node.Id+"/"+page.DialoguePart;
                    if(a.Cue!=cueId){a.Cue=cueId;if(motion!=a.Motion||motion!="mtn_idle01_C"&&a.Clock-a.MotionAt>2){a.MotionFrom=(float[])a.Values.Clone();a.Motion=motion;a.MotionAt=a.Clock;a.MotionStarts++;}}
                    var values=(float[])a.Defaults.Clone();var clip=a.Motions[a.Motion];float elapsed=a.Clock-a.MotionAt;
                    if(a.Motion!="mtn_idle01_C"&&elapsed>clip.Duration){a.MotionFrom=(float[])a.Values.Clone();a.Motion="mtn_idle01_C";a.MotionAt=a.Clock;clip=a.Motions[a.Motion];elapsed=0;}
                    clip.Apply(values,a.Ids,elapsed,a.Motion=="mtn_idle01_C");var before=(float[])values.Clone();ApplyExpression(a,a.PreviousExpression,before);ApplyExpression(a,a.Expression,values);
                    float mix=RuntimeCompat.Clamp((a.Clock-a.ExpressionAt)/.3f,0,1);mix=mix*mix*(3-2*mix);for(int i=0;i<values.Length;i++)values[i]=before[i]+(values[i]-before[i])*mix;
                    if(a.MotionFrom!=null&&elapsed<.25f){float weight=elapsed/.25f;weight=weight*weight*(3-2*weight);for(int i=0;i<values.Length;i++)values[i]=a.MotionFrom[i]+(values[i]-a.MotionFrom[i])*weight;}
                    // Independent deterministic blink phase; no clock/day or save mutations.
                    float phase=(a.Clock+(a.Id.Length*37+a.Id[^1])*0.137f)%4.6f,blink=phase<.13f?1-phase/.13f:phase<.26f?(phase-.13f)/.13f:1;
                    foreach(string eye in new[]{"ParamEyeLOpen","ParamEyeROpen"})if(a.Ids.TryGetValue(eye,out int ei))values[ei]*=blink;
                    if(a.Ids.TryGetValue("ParamBreath",out int breath))values[breath]=.5f+.4f*(float)Math.Sin(a.Clock*1.6f);
                    if(a.Ids.TryGetValue("ParamMouthOpenY",out int mi))values[mi]=a.Id==voiceActor?RuntimeCompat.Clamp(mouth*7,0,.75f):0;
                    for(int i=0;i<values.Length;i++)a.Values[i]=RuntimeCompat.Clamp(values[i],a.Minimum[i],a.Maximum[i]);pose(a.Handle,a.Values,a.Values.Length);GL.IssuePluginEvent(renderEvent,a.Handle);
                }catch(Exception e){Fail(a.Id,e.Message);}
            }
        }
        internal object Status()=>new{supported=SystemInfo.graphicsDeviceType.ToString(),realtime=actors.Count>0,failed=failed.ToArray(),actors=actors.Values.Select(a=>{stats(a.Handle,a.Stats);return new{actor=a.Id,frames=a.Stats[0],geometryChanges=a.Stats[1],renderMs=a.Stats[2],maxRenderMs=a.Stats[3],drawables=a.Stats[4],expression=a.Expression,motion=a.Motion,motionStarts=a.MotionStarts,expressionChanges=a.ExpressionChanges};}).ToArray()};
        private static bool Ready(Actor a){stats(a.Handle,a.Stats);return a.Stats[0]>0;}
        internal bool Loaded(string actor)=>actors.TryGetValue(actor,out var a)&&Ready(a);
        internal bool Active=>actors.Values.Any(Ready);
        internal static int LiveCount=>liveCount?.Invoke()??0;
        private static void Free(Actor a){if(a.Handle!=0){release(a.Handle);a.Handle=0;}if(a.Image!=null){a.Image.texture=null;a.Image.enabled=false;}if(a.Output!=null){a.Output.Release();UnityEngine.Object.Destroy(a.Output);}foreach(var t in a.Textures)if(t!=null)UnityEngine.Object.Destroy(t);}
        public void Dispose(){foreach(var a in actors.Values)Free(a);actors.Clear();failed.Clear();}
    }
}
