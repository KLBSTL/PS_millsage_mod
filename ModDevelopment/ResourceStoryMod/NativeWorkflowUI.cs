using System;
using UnityEngine;

namespace ResourceStoryMod
{
    internal sealed class NativeWorkflowUI
    {
        internal static readonly Color Ink=new(.065f,.09f,.12f,1),Card=new(.10f,.14f,.18f,1),Muted=new(.66f,.73f,.78f,1),Gold=new(.94f,.73f,.36f,1),Teal=new(.38f,.81f,.70f,1);
        private readonly GUIStyle text,small,title,primary,secondary,danger,disabled,stepText,warning;
        private static GUIStyle fillStyle;
        internal NativeWorkflowUI(GUIStyle source)
        {
            text=new GUIStyle(source){fontSize=18,wordWrap=true,richText=false};text.normal.textColor=new Color(.92f,.94f,.95f,1);
            small=new GUIStyle(text){fontSize=16};small.normal.textColor=Muted;
            stepText=new GUIStyle(small){wordWrap=false};stepText.normal.textColor=Color.white;
            warning=new GUIStyle(small);warning.normal.textColor=Gold;
            title=new GUIStyle(text){fontSize=23,fontStyle=FontStyle.Bold};
            primary=ButtonStyle(source,Gold,new Color(.08f,.10f,.12f,1));
            secondary=ButtonStyle(source,new Color(.18f,.25f,.31f,1),Color.white);
            danger=ButtonStyle(source,new Color(.33f,.17f,.18f,1),new Color(1,.78f,.77f,1));
            disabled=ButtonStyle(source,new Color(.14f,.18f,.21f,1),Muted);
        }
        private static Texture2D Solid(Color c){var t=new Texture2D(1,1,TextureFormat.RGBA32,false);t.SetPixel(0,0,c);t.Apply(false,false);UnityEngine.Object.DontDestroyOnLoad(t);return t;}
        private static GUIStyle ButtonStyle(GUIStyle source,Color bg,Color fg)
        {
            var s=new GUIStyle(source){fontSize=18,alignment=TextAnchor.MiddleCenter,wordWrap=true,richText=false,padding=new RectOffset(8,8,5,5)};
            var normal=Solid(bg);var hover=Solid(Color.Lerp(bg,Color.white,.12f));
            foreach(var state in new[]{s.normal,s.active,s.focused,s.onNormal,s.onActive,s.onFocused}){state.background=normal;state.textColor=fg;}
            s.hover.background=hover;s.hover.textColor=fg;return s;
        }
        internal static void Fill(Rect rect,Color c)
        {
            fillStyle??=new GUIStyle(GUI.skin.box){border=new RectOffset(0,0,0,0),padding=new RectOffset(0,0,0,0),margin=new RectOffset(0,0,0,0)};
            fillStyle.normal.background=Texture2D.whiteTexture;
            var before=GUI.color;GUI.color=c;try{GUI.Box(rect,"",fillStyle);}finally{GUI.color=before;}
        }
        internal void Box(Rect rect){Fill(rect,Card);Fill(new Rect(rect.x,rect.y,3,rect.height),new Color(.24f,.34f,.40f,1));}
        internal static string Human(string value)=>(value??"").Replace("noiseControl","降噪能力").Replace("stability","稳定性").Replace("response","响应").Replace("portability","便携性");
        internal void Text(Rect rect,string value)=>GUI.Label(rect,Human(value),text);
        internal void Small(Rect rect,string value)=>GUI.Label(rect,Human(value),small);
        internal void Hint(Rect rect,string value,bool blocked)=>GUI.Label(rect,Human(value),blocked?warning:small);
        internal void Title(Rect rect,string value)=>GUI.Label(rect,value??"",title);
        internal bool Button(Rect rect,string label,bool main=false,string block=null,bool destructive=false)
        {
            bool before=GUI.enabled;GUI.enabled=before&&block==null;
            try{return NativePanelGUI.Button(rect,label,block!=null?disabled:destructive?danger:main?primary:secondary);}
            finally{GUI.enabled=before;}
        }
        internal void Steps(float width,int current,bool completed=false)
        {
            string[] names={"受理","准备","安装","校准","预约领取","交收"};float cell=(width-40)/6;
            for(int i=0;i<names.Length;i++){
                var r=new Rect(20+i*cell,93,cell-5,35);Fill(r,i==current&&!completed?new Color(.29f,.24f,.16f,1):i<current||completed?new Color(.13f,.26f,.25f,1):new Color(.12f,.17f,.21f,1));
                var color=GUI.color;GUI.color=i==current&&!completed?Gold:i<current||completed?Teal:Muted;
                GUI.Label(new Rect(r.x+8,r.y+6,r.width-12,27),(i+1)+"  "+names[i],stepText);GUI.color=color;
            }
        }
        internal static string Slot(string key)=>key switch{"signal"=>"信号槽","power"=>"供电槽","interface"=>"接口槽","service.board"=>"主控板","service.driver"=>"扬声器单元",_=>"内部配件"};
        internal static string SlotList(System.Collections.Generic.Dictionary<string,int> slots)=>slots.Count==0?"原厂封装，无松散配件":string.Join(" / ",System.Linq.Enumerable.Select(slots,x=>Slot(x.Key)+" #"+x.Value));
    }
}
