using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using UnityEngine;

namespace ResourceStoryMod
{
    internal static class NativeP19ItemArt
    {
        private sealed class Entry
        {
            public string Sku {get;set;} public string File {get;set;} public string Kind {get;set;}
            public string Sha256 {get;set;} public bool Reviewed {get;set;}
            public int Width {get;set;} public int Height {get;set;} public int Rotate {get;set;}
            public int[] SourceRect {get;set;}
        }
        private sealed class Manifest {public int Version {get;set;} public Entry[] Entries {get;set;}}
        private static readonly Dictionary<string,Entry> entries=new();
        private static readonly Dictionary<string,Sprite> cache=new();
        private static readonly HashSet<string> failures=new();
        private static bool loaded;
        private static string folder;
        internal static int CachedSprites=>cache.Count;
        internal static bool Resolve(GameItemElement item,ref Sprite result)
        {
            if(item?.shape==null||item.spriteAtlasPath!="resource_story"||item.spritePath!="item")return true;
            folder??=Path.Combine(Core.Instance.Root,"ModWorkbench/res/prepared/p19");
            if(!loaded){
                loaded=true;
                foreach(string manifestName in new[]{"workbench-items.json","visual-items.json","items.json"}){
                    string file=Path.Combine(folder,manifestName);
                    if(!File.Exists(file))continue;
                    try{
                        var manifest=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(file),Core.Json);
                        if(manifest?.Version!=1)throw new InvalidDataException("Unsupported P19 manifest");
                        var seen=new HashSet<string>();
                        foreach(var e in manifest.Entries??Array.Empty<Entry>()){
                            var product=ResourceStoryMod.Story.WorkshopCatalog.Product(e.Sku);
                            var music=product==null?ResourceStoryMod.Story.MusicCatalog.Find(e.Sku):null;
                            var original=product==null&&music==null?ResourceStoryMod.Story.PackageC.Definitions.FirstOrDefault(x=>x.Sku==e.Sku):null;
                            if(e.Width<1||e.Height<1||e.Width!=(product?.Width??music?.Width??original?.Width)||e.Height!=(product?.Height??music?.Height??original?.Height)||!e.Reviewed||e.Rotate is not (0 or 90 or 180 or 270))continue;
                            if(!seen.Add(e.Sku))failures.Add(e.Sku);
                            entries.TryAdd(e.Sku,e);
                        }
                    }catch(Exception e){Core.Instance.LoggerInstance.Warning("P19_ART_MANIFEST "+manifestName+" "+e.Message);}
                }
            }
            if(!entries.TryGetValue(item.identifier,out var entry)||failures.Contains(item.identifier))return true;
            int cell=UISettings.current.gridSize;if(cell<1)return true;
            int w=entry.Width*cell,h=entry.Height*cell;string key=entry.Sku+":"+cell;
            if(cache.TryGetValue(key,out result))return false;
            try{
                Texture2D texture;
                if(!string.IsNullOrEmpty(entry.File)){
                    string path=Path.GetFullPath(Path.Combine(folder,entry.File));
                    if(!path.StartsWith(Path.GetFullPath(folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("P19 path escaped folder");
                    byte[] bytes=File.ReadAllBytes(path);
                    if(RuntimeCompat.Hex(RuntimeCompat.Sha256(bytes))!=entry.Sha256?.ToUpperInvariant())throw new InvalidDataException("P19 PNG checksum mismatch");
                    var source=new Texture2D(2,2,TextureFormat.RGBA32,false);
                    try{if(!ImageConversion.LoadImage(source,new NativeArray<byte>(bytes),false))throw new InvalidDataException("P19 PNG rejected");texture=Fit(source,w,h,entry.Rotate,entry.SourceRect);}
                    finally{UnityEngine.Object.Destroy(source);}
                }else if(entry.Kind=="archival-paper"){
                    var pixels=new Color32[w*h];
                    for(int layer=2;layer>=0;layer--)for(int y=h/8+layer;y<7*h/8+layer&&y<h;y++)for(int x=w/8-layer;x<7*w/8-layer;x++)
                        pixels[y*w+x]=x==w/8-layer||y==h/8+layer||x==7*w/8-layer-1||y==7*h/8+layer-1?new Color32(105,100,92,255):new Color32(232,222,192,255);
                    texture=new Texture2D(w,h,TextureFormat.RGBA32,false);texture.SetPixels32(new NativeArray<Color32>(pixels));texture.Apply(false,false);
                }
                else if(entry.Kind=="commercial-pickup"){
                    texture=NativeP2ItemArt.Draw("ms_part_stabilizer_pickup",w,h);
                    for(int y=h/12;y<h/5;y++)for(int x=w/6;x<5*w/6;x++)texture.SetPixel(x,y,new Color32(95,110,102,255));
                    texture.Apply(false,false);
                }else return true;
                texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
                result=Sprite.Create(texture,new Rect(0,0,w,h),new Vector2(.5f,.5f),100);
                UnityEngine.Object.DontDestroyOnLoad(texture);UnityEngine.Object.DontDestroyOnLoad(result);cache.Add(key,result);return false;
            }catch(Exception e){failures.Add(item.identifier);Core.Instance.LoggerInstance.Warning("P19_ART_FALLBACK "+item.identifier+" "+e.Message);return true;}
        }
        internal static Texture2D Fit(Texture2D source,int w,int h,int rotation,int[] rect)
        {
            if(rect==null)return NativeP13ItemArt.Fit(source,w,h,rotation);
            if(rect.Length!=4||rect[0]<0||rect[1]<0||rect[2]<1||rect[3]<1||rect[0]>source.width-rect[2]||rect[1]>source.height-rect[3])throw new InvalidDataException("P19 source rectangle outside PNG");
            bool turn=rotation is 90 or 270;int rw=rect[2],rh=rect[3],sw=turn?rh:rw,sh=turn?rw:rh;
            float scale=.88f*Math.Min(w/(float)sw,h/(float)sh),left=(w-sw*scale)/2,lower=(h-sh*scale)/2;
            var pixels=new Color32[w*h];int bottom=source.height-rect[1]-rh;
            for(int y=0;y<h;y++)for(int x=0;x<w;x++){
                if(x+.5f<left||x+.5f>=left+sw*scale||y+.5f<lower||y+.5f>=lower+sh*scale)continue;
                int u=RuntimeCompat.Clamp((int)((x+.5f-left)/scale),0,sw-1),v=RuntimeCompat.Clamp((int)((y+.5f-lower)/scale),0,sh-1);
                int sx=u,sy=v;
                if(rotation==90){sx=v;sy=rh-1-u;}
                else if(rotation==180){sx=rw-1-u;sy=rh-1-v;}
                else if(rotation==270){sx=rw-1-v;sy=u;}
                pixels[y*w+x]=source.GetPixel(rect[0]+sx,bottom+sy);
            }
            var texture=new Texture2D(w,h,TextureFormat.RGBA32,false);texture.SetPixels32(new NativeArray<Color32>(pixels));texture.Apply(false,false);return texture;
        }
    }
}
