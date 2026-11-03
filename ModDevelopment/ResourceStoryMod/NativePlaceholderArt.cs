using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ResourceStoryMod
{
    // Rendering-only adaptation for the legacy shared placeholder. Saved paths,
    // occupied cells, orientation and native drag/rotation handling stay native.
    internal static class NativePlaceholderArt
    {
        private static readonly Dictionary<string,Sprite> sprites=new();
        internal static bool Resolve(GameItemElement item,ref Sprite result)
        {
            if(item.spriteAtlasPath!="resource_story" || item.spritePath!="item" || item.shape==null)return true;
            var shape=item.shape;
            int cell=UISettings.current.gridSize,w=shape.width,h=shape.height;
            if(cell<1 || w<1 || h<1 || w>64 || h>64)throw new InvalidOperationException("Invalid placeholder footprint");
            var key=new StringBuilder().Append(cell).Append(':').Append(w).Append('x').Append(h).Append(':');
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)key.Append(shape.GetLocal(x,y)==0?'0':'1');
            string id=key.ToString();
            if(!sprites.TryGetValue(id,out result)){
                int width=checked(w*cell),height=checked(h*cell);
                var pixels=new Color32[checked(width*height)];
                var fill=new Color32(66,151,173,255);var grid=new Color32(51,126,148,255);var edge=new Color32(107,196,211,255);
                for(int y=0;y<height;y++)for(int x=0;x<width;x++){
                    // GridShape local Y grows downwards; texture Y grows upwards.
                    if(shape.GetLocal(x/cell,(height-1-y)/cell)==0)continue;
                    pixels[y*width+x]=x==0 || y==0 || x==width-1 || y==height-1?edge:x%cell==0 || y%cell==0?grid:fill;
                }
                var texture=new Texture2D(width,height,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                texture.SetPixels32(new NativeArray<Color32>(pixels));texture.Apply(false,false);
                UnityEngine.Object.DontDestroyOnLoad(texture);
                result=Sprite.Create(texture,new Rect(0,0,width,height),new Vector2(.5f,.5f),100);
                UnityEngine.Object.DontDestroyOnLoad(result);sprites.Add(id,result);
            }
            return false;
        }
    }
}
