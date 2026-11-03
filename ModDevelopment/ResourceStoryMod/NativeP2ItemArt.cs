using System;
using System.Collections.Generic;
using System.IO;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    // Pixel art extends the existing code-drawn item art. No grid/hit rules change.
    internal static class NativeP2ItemArt
    {
        private static readonly Dictionary<string,Sprite> cache=new();
        private static readonly HashSet<string> skus=new(){"ms_nagi_whale_guitar","ms_gear_commercial_guitar","ms_gear_synth_damaged","ms_machine_electrobench","ms_machine_calibrator","ms_part_stabilizer_pickup","ms_strings_guitar6_tension","ms_item_standard_strings","ms_supply_contact_cleaner","ms_supply_solder_pack","ms_part_wire_bundle"};
        private static readonly HashSet<string> failures=new();
        private static string folder;
        private static bool? enabled;
        internal static bool Resolve(GameItemElement item,ref Sprite result)
        {
            if(!NativeP19ItemArt.Resolve(item,ref result)||!NativeP13ItemArt.Resolve(item,ref result))return false;
            string root=folder??=Path.Combine(Core.Instance.Root,"ModWorkbench","res","prepared","p2");
            if(item?.shape==null||failures.Contains(item.identifier)||!skus.Contains(item.identifier)&&item.identifier!=NormalPlayPolicy.BasicInspector)return true;
            if(!(enabled??=File.Exists(Path.Combine(root,"items.enabled"))))return true;
            // SetSpriteAndShape resolves art before the final footprint is assigned.
            // Machine art must use its catalog size on that first resolution too.
            var machine=WorkshopCatalog.Product(item.identifier);var product=MusicCatalog.Find(item.identifier);
            int cell=UISettings.current.gridSize,w=(machine?.Width??product?.Width??item.shape.width)*cell,h=(machine?.Height??product?.Height??item.shape.height)*cell;
            string key=item.identifier+":"+w+":"+h;
            if(cache.TryGetValue(key,out result))return false;
            try{
                Texture2D texture;
                string reference=Path.Combine(root,"items","nagi-whale-reference.png");
                if(item.identifier=="ms_nagi_whale_guitar"&&File.Exists(reference)){
                    var source=new Texture2D(2,2,TextureFormat.RGBA32,false);
                    try{
                        if(!ImageConversion.LoadImage(source,new NativeArray<byte>(File.ReadAllBytes(reference)),false))throw new InvalidDataException("Guitar PNG rejected");
                        texture=Fit(source,w,h);
                    }finally{UnityEngine.Object.Destroy(source);}
                }else texture=Draw(item.identifier=="ms_nagi_whale_guitar"?"ms_gear_commercial_guitar":item.identifier,w,h);
                texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
                result=Sprite.Create(texture,new Rect(0,0,w,h),new Vector2(.5f,.5f),100);
                UnityEngine.Object.DontDestroyOnLoad(texture);UnityEngine.Object.DontDestroyOnLoad(result);cache.Add(key,result);return false;
            }catch(Exception e){failures.Add(item.identifier);Core.Instance.LoggerInstance.Warning("P2_ITEM_ART_FALLBACK "+item.identifier+" "+e.Message);return true;}
        }
        private static Texture2D Fit(Texture2D source,int w,int h)
        {
            // Graphics.DrawTexture is stripped in this IL2CPP player. Read only
            // the required pixels from our readable PNG instead of touching GL.
            float scale=Math.Min((w-2)/(float)source.width,(h-2)/(float)source.height),dw=source.width*scale,dh=source.height*scale;
            float left=(w-dw)/2,bottom=(h-dh)/2;var pixels=new Color32[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++){
                if(x+.5f<left||x+.5f>=left+dw||y+.5f<bottom||y+.5f>=bottom+dh)continue;
                int sx=RuntimeCompat.Clamp((int)((x+.5f-left)/scale),0,source.width-1),sy=RuntimeCompat.Clamp((int)((y+.5f-bottom)/scale),0,source.height-1);
                pixels[y*w+x]=source.GetPixel(sx,sy);
            }
            var texture=new Texture2D(w,h,TextureFormat.RGBA32,false);texture.SetPixels32(new NativeArray<Color32>(pixels));texture.Apply(false,false);return texture;
        }
        internal static Texture2D Draw(string id,int w,int h)
        {
            var pixels=new Color32[w*h];
            var dark=new Color32(43,46,53,255);var metal=new Color32(155,158,150,255);var light=new Color32(211,207,183,255);var blue=new Color32(70,120,139,255);var green=new Color32(115,169,112,255);var copper=new Color32(165,107,74,255);
            void Box(int x,int y,int width,int height,Color32 color){int x0=x*w/100,y0=y*h/100,x1=(x+width)*w/100,y1=(y+height)*h/100;for(int yy=y0;yy<Math.Min(h,y1);yy++)for(int xx=x0;xx<Math.Min(w,x1);xx++)pixels[(h-1-yy)*w+xx]=color;}
            void Oval(int x,int y,int width,int height,Color32 color){for(int yy=0;yy<h;yy++)for(int xx=0;xx<w;xx++){float dx=(xx+.5f)*100/w-x-width/2f,dy=(yy+.5f)*100/h-y-height/2f;if(dx*dx/(width*width/4f)+dy*dy/(height*height/4f)<=1)pixels[(h-1-yy)*w+xx]=color;}}
            if(id=="ms_gear_commercial_guitar"){
                // A generic instrument, independent of the unique family guitar.
                // The game does not supply an atlas entry named "guitar".
                Oval(8,65,84,31,dark);Oval(12,66,76,27,blue);Oval(17,55,29,25,blue);Oval(56,55,27,24,blue);
                Box(44,15,13,61,copper);Box(46,18,9,56,dark);Box(41,4,19,15,metal);Box(38,7,5,3,light);Box(59,12,5,3,light);
                for(int y=21;y<58;y+=5)Box(45,y,12,1,metal);
                Box(34,71,33,3,light);Box(34,79,33,3,light);Box(37,88,29,3,metal);Box(47,10,6,79,light);Box(72,82,5,3,copper);Box(71,89,5,3,copper);
            }else if(id=="ms_gear_synth_damaged"){
                Box(2,12,96,76,dark);Box(5,16,90,68,metal);Box(9,20,82,26,dark);Box(39,23,22,16,blue);
                for(int x=11;x<88;x+=7)Box(x,49,5,30,light);
                for(int x=15;x<86;x+=14)Box(x,49,3,18,dark);
                Box(15,27,5,5,copper);Box(24,27,5,5,copper);Box(70,27,16,5,green);
            }else if(id=="ms_machine_electrobench"){
                Box(4,18,92,57,dark);Box(6,20,88,49,metal);Box(8,22,84,6,light);Box(10,34,28,27,blue);Box(44,34,20,27,dark);Box(70,34,19,27,dark);Box(5,76,10,12,dark);Box(85,76,10,12,dark);Box(47,38,14,3,copper);Box(47,46,14,3,copper);Box(76,38,3,17,light);Box(83,38,3,17,light);
            }else if(id=="ms_machine_calibrator"||id==NormalPlayPolicy.BasicInspector){
                Box(10,8,80,80,dark);Box(14,12,72,70,metal);Box(20,19,48,43,dark);Box(23,22,42,37,new Color32(30,57,44,255));Box(27,42,10,3,green);Box(35,31,4,14,green);Box(39,31,8,3,green);Box(45,32,4,17,green);Box(49,46,11,3,green);Box(73,25,6,6,blue);Box(73,40,6,6,copper);Box(25,70,8,7,dark);Box(55,70,8,7,dark);
                if(id==NormalPlayPolicy.BasicInspector){Box(14,12,72,5,blue);Box(72,38,9,13,metal);Box(38,69,9,9,blue);}
            }else if(id=="ms_part_stabilizer_pickup"){
                Box(9,20,82,58,dark);Box(12,24,76,50,new Color32(59,95,74,255));Box(25,32,50,27,metal);Box(29,35,42,21,dark);for(int i=0;i<6;i++){Box(31+i*6,39,3,4,light);Box(31+i*6,49,3,4,light);}Box(18,65,65,3,copper);
            }else if(id=="ms_supply_contact_cleaner"){
                Box(28,8,44,10,dark);Box(34,18,32,12,metal);Box(22,31,56,60,dark);Box(26,35,48,52,green);Box(29,48,42,24,light);Box(42,55,16,5,blue);
            }else if(id is "ms_supply_solder_pack" or "ms_part_wire_bundle"){
                Oval(10,9,80,78,dark);Oval(16,15,68,66,metal);Oval(27,25,46,46,id=="ms_part_wire_bundle"?copper:light);Oval(38,36,24,24,dark);Box(48,78,29,5,metal);Box(74,78,5,15,metal);
            }else{
                Box(15,9,70,82,dark);Box(19,13,62,74,new Color32(180,160,114,255));Box(22,17,56,13,blue);for(int i=0;i<6;i++){Box(27+i*8,36,3,42,metal);Box(26+i*8,35,5,3,light);}Box(25,80,52,3,copper);
            }
            var texture=new Texture2D(w,h,TextureFormat.RGBA32,false);texture.SetPixels32(new NativeArray<Color32>(pixels));texture.Apply(false,false);return texture;
        }
        internal static int CachedSprites=>cache.Count+NativeP13ItemArt.CachedSprites;
    }
}
