using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;

namespace ResourceStoryMod
{
    // Presentation only. Keep item shapes, saved IDs, native rotations and hit rules.
    internal static class NativeP13ItemArt
    {
        private sealed class Entry { public string Sku {get;set;} public string File {get;set;} public string Kind {get;set;} public int Width {get;set;} public int Height {get;set;} public int Rotate {get;set;} public int[] SourceRect {get;set;} }
        private sealed class Manifest { public int Version {get;set;} public Entry[] Entries {get;set;} }
        private static readonly Dictionary<string,Sprite> cache=new();
        private static readonly HashSet<string> failures=new();
        private static readonly List<object> observations=new();
        private static Dictionary<string,Entry> entries;
        private static string folder;
        internal static int CachedSprites=>cache.Count;
        internal static bool Resolve(GameItemElement item,ref Sprite result)
        {
            if(item?.shape==null || item.spriteAtlasPath!="resource_story" || item.spritePath!="item")return true;
            folder??=Path.Combine(Core.Instance.Root,"ModWorkbench/res/prepared/p13");
            if(entries==null){
                entries=new();
                var path=Path.Combine(folder,"items.json");
                if(File.Exists(path))try{
                    var manifest=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path),Core.Json);
                    if(manifest?.Version!=1)throw new InvalidDataException("Unsupported P13 art manifest");
                    foreach(var e in manifest.Entries){if(e.Width<1||e.Height<1||e.Width>64||e.Height>64||!entries.TryAdd(e.Sku,e))throw new InvalidDataException("Invalid P13 art entry");}
                }catch(Exception ex){entries.Clear();Core.Instance.LoggerInstance.Warning("P13_ART_MANIFEST "+ex.Message);}
            }
            if(!entries.TryGetValue(item.identifier,out var entry)||failures.Contains(item.identifier))return true;
            int cell=UISettings.current.gridSize;
            if(cell<1)return true;
            // Use the catalog footprint even when the factory resolves an image
            // before assigning its final shape. Native rotation acts afterwards.
            int w=entry.Width*cell,h=entry.Height*cell;string key=item.identifier+":"+w+":"+h;
            if(cache.TryGetValue(key,out result))return false;
            try{
                Texture2D texture;
                if(!string.IsNullOrEmpty(entry.File)){
                    string path=Path.GetFullPath(Path.Combine(folder,entry.File));
                    if(!path.StartsWith(Path.GetFullPath(folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Art path escaped prepared folder");
                    var source=new Texture2D(2,2,TextureFormat.RGBA32,false);
                    try{
                        if(!ImageConversion.LoadImage(source,new NativeArray<byte>(File.ReadAllBytes(path)),false))throw new InvalidDataException("P13 PNG rejected");
                        texture=NativeP19ItemArt.Fit(source,w,h,entry.Rotate,entry.SourceRect);
                    }finally{UnityEngine.Object.Destroy(source);}
                }else texture=Draw(entry.Kind,w,h);
                texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
                result=Sprite.Create(texture,new Rect(0,0,w,h),new Vector2(.5f,.5f),100);
                UnityEngine.Object.DontDestroyOnLoad(texture);UnityEngine.Object.DontDestroyOnLoad(result);cache.Add(key,result);
                if(Core.Instance.Test!=null)try{
                    observations.Add(new{Sku=item.identifier,Width=w,Height=h,Cell=cell,entry.File,entry.Kind,entry.Rotate,Nonempty=texture.GetPixels32().ToArray().Any(p=>p.a>0)});
                    File.WriteAllText(Path.Combine(Core.Instance.Test.SaveRoot,"p13-native-art.json"),JsonSerializer.Serialize(new{Session=Core.Instance.Test.SessionId,Build=TestSession.Hash(Core.Instance.GetType().Assembly.Location),ManifestSHA256=TestSession.Hash(Path.Combine(folder,"items.json")),Rows=observations},Core.Json));
                }catch(Exception ex){Core.Instance.LoggerInstance.Warning("P13_ART_TRACE "+ex.Message);}
                return false;
            }catch(Exception ex){failures.Add(item.identifier);Core.Instance.LoggerInstance.Warning("P13_ART_FALLBACK "+item.identifier+" "+ex.Message);return true;}
        }
        internal static Texture2D Fit(Texture2D source,int w,int h,int rotation)
        {
            bool turn=rotation==90;int sw=turn?source.height:source.width,sh=turn?source.width:source.height;
            float scale=Math.Min((w-2)/(float)sw,(h-2)/(float)sh),left=(w-sw*scale)/2,lower=(h-sh*scale)/2;
            var pixels=new Color32[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++){
                if(x+.5f<left||x+.5f>=left+sw*scale||y+.5f<lower||y+.5f>=lower+sh*scale)continue;
                int u=RuntimeCompat.Clamp((int)((x+.5f-left)/scale),0,sw-1),v=RuntimeCompat.Clamp((int)((y+.5f-lower)/scale),0,sh-1);
                pixels[y*w+x]=turn?source.GetPixel(v,source.height-1-u):source.GetPixel(u,v);
            }
            return Texture(pixels,w,h);
        }
        private static Texture2D Texture(Color32[] pixels,int w,int h)
        {
            var texture=new Texture2D(w,h,TextureFormat.RGBA32,false);texture.SetPixels32(new NativeArray<Color32>(pixels));texture.Apply(false,false);return texture;
        }
        internal static Texture2D Draw(string kind,int w,int h)
        {
            // Extend the existing code-authored pixel-art family for flat paper,
            // media and simple accessories. Never add interactive UI to a sprite.
            var p=new Color32[w*h];var ink=new Color32(48,43,51,255);var paper=new Color32(220,204,166,255);var pale=new Color32(238,225,190,255);var blue=new Color32(68,111,139,255);var red=new Color32(145,62,66,255);var gold=new Color32(183,143,73,255);var metal=new Color32(154,158,148,255);
            void Box(int x,int y,int dx,int dy,Color32 c){for(int yy=Math.Max(0,y*h/100);yy<Math.Min(h,(y+dy)*h/100);yy++)for(int xx=Math.Max(0,x*w/100);xx<Math.Min(w,(x+dx)*w/100);xx++)p[(h-1-yy)*w+xx]=c;}
            void Oval(int x,int y,int dx,int dy,Color32 c){for(int yy=0;yy<h;yy++)for(int xx=0;xx<w;xx++){float a=(xx+.5f)*100/w-x-dx/2f,b=(yy+.5f)*100/h-y-dy/2f;if(a*a/(dx*dx/4f)+b*b/(dy*dy/4f)<=1)p[(h-1-yy)*w+xx]=c;}}
            if(kind is "tape-blank" or "tape-prelim" or "tape-final"){
                Box(5,20,90,60,ink);Box(9,24,82,52,metal);Box(13,28,74,18,kind=="tape-final"?gold:kind=="tape-prelim"?blue:paper);Box(17,50,66,18,ink);Oval(21,51,15,15,paper);Oval(63,51,15,15,paper);Box(38,55,23,6,metal);Box(25,72,50,4,ink);
            }else if(kind=="pedal"){
                Box(15,5,70,91,ink);Box(19,9,62,83,red);for(int x=27;x<75;x+=20){Oval(x,18,12,9,ink);Box(x+4,19,3,5,pale);}Box(26,48,48,32,ink);Box(31,52,38,24,metal);Oval(44,35,10,7,gold);
            }else if(kind=="mic"){
                Box(43,38,15,54,ink);Oval(26,3,47,42,ink);Oval(30,6,39,34,metal);for(int y=12;y<35;y+=6)Box(31,y,37,2,ink);Box(46,45,8,43,metal);Box(40,86,21,8,ink);
            }else if(kind=="lostbox"){
                Box(4,22,92,63,ink);Box(8,26,84,55,new Color32(120,99,78,255));Box(6,20,88,14,metal);Box(12,37,76,3,gold);Box(39,47,23,17,ink);Box(43,51,15,9,paper);
            }else if(kind=="strings-bass"){
                Box(14,9,72,82,ink);Box(18,13,64,74,paper);Box(22,18,56,15,blue);for(int x=29;x<75;x+=10){Box(x,42,3,34,metal);Box(x-1,39,5,4,gold);}Box(26,79,49,2,ink);
            }else if(kind=="weights"){
                Box(7,21,86,58,ink);for(int x=13;x<90;x+=15){Box(x,26,10,47,metal);Box(x,26,10,8,gold);Box(x+3,37,4,29,ink);}
            }else if(kind=="pick"){
                Oval(14,20,72,45,ink);Oval(20,24,60,39,blue);Box(31,59,38,13,blue);Box(39,72,22,9,blue);Box(45,81,10,5,ink);Box(45,35,12,14,pale);Box(40,40,22,4,pale);
            }else if(kind=="record"){
                Oval(5,5,90,90,ink);Oval(12,12,76,76,gold);Oval(19,19,62,62,ink);Oval(23,23,54,54,gold);Oval(36,36,28,28,paper);Oval(47,47,6,6,ink);
            }else if(kind=="apples"){
                Box(14,12,72,76,ink);Box(18,16,64,68,new Color32(112,142,136,255));Box(16,12,68,9,blue);for(int y=31;y<75;y+=20)for(int x=23;x<75;x+=22){Oval(x,y,18,16,paper);Box(x,y+8,18,5,pale);}Box(22,22,4,51,metal);
            }else{
                Box(10,6,80,89,ink);Box(14,10,72,81,kind=="note"?pale:paper);
                if(kind=="score"){
                    for(int y=25;y<78;y+=25){for(int d=0;d<5;d++)Box(22,y+d*3,57,1,ink);for(int x=28;x<77;x+=17){Oval(x,y+4,5,4,ink);Box(x+4,y-2,1,9,ink);}}
                }else if(kind=="sketch"){
                    Box(22,30,55,35,ink);Box(24,32,51,31,pale);for(int x=27;x<75;x+=7)Box(x,33,2,25,ink);Box(23,73,54,1,blue);Box(23,69,1,9,blue);Box(76,69,1,9,blue);
                }else{
                    Box(22,19,55,9,kind=="receipt"?red:kind=="flyer"?blue:ink);
                    for(int y=38;y<76;y+=10)Box(23,y,y==68?34:52,2,ink);
                    if(kind=="receipt") {Box(63,73,15,11,red);for(int x=22;x<61;x+=5)Box(x,80,2,5,ink);}
                    if(kind=="pass") {Box(30,35,39,27,blue);Box(36,39,26,17,metal);Box(42,11,16,3,ink);}
                }
            }
            return Texture(p,w,h);
        }
    }
}
