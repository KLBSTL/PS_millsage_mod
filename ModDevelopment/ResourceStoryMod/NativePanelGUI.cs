using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

namespace ResourceStoryMod
{
    internal static class NativePanelGUI
    {
        private static readonly Dictionary<string,int> eventCounts=new();
        private static readonly Queue<object> recentEvents=new(),recentButtons=new(),recentHitTests=new();
        private static long nextReport;
        private static bool screenshotTaken;
        private static int panelId;
        private static string panelTitle="";
        private static Rect panelRect;
        private static string pressedButton;
        private static EventType framePointerType;
        private static int framePointerButton;
        private static bool Trace=>Core.Instance?.Test?.ToolsMode==true && Core.Instance.Test.Request.AutoRun==false;

        internal static IDisposable Begin()=>new Frame();

        private sealed class Frame:IDisposable
        {
            private readonly bool enabled=GUI.enabled;
            private readonly Color color=GUI.color,background=GUI.backgroundColor,content=GUI.contentColor;
            private readonly Matrix4x4 matrix=GUI.matrix;
            private readonly int depth=GUI.depth;
            private readonly string type=Event.current?.type.ToString(),raw=Event.current?.rawType.ToString();
            private readonly EventType? rawKind=Event.current?.rawType;
            private readonly Vector2 point=Event.current?.mousePosition??Vector2.zero;
            private readonly int hot=GUIUtility.hotControl;
            internal Frame()
            {
                GUI.enabled=true;GUI.color=Color.white;GUI.backgroundColor=Color.white;GUI.contentColor=Color.white;
                GUI.matrix=Matrix4x4.identity;GUI.depth=-1000;
                framePointerType=Event.current?.rawType??EventType.Ignore;framePointerButton=Event.current?.button??-1;
                if(framePointerType==EventType.MouseDown && framePointerButton==0 || !Application.isFocused)pressedButton=null;
                if(Trace && type!=null)eventCounts[type]=eventCounts.GetValueOrDefault(type)+1;
            }
            public void Dispose()
            {
                try
                {
                    if(Trace)
                    {
                        bool click=rawKind==EventType.MouseDown || rawKind==EventType.MouseUp;
                        if(click)
                        {
                            recentEvents.Enqueue(new{Utc=DateTime.UtcNow,Before=type,Raw=raw,After=Event.current?.type.ToString(),X=point.x,Y=point.y,InheritedEnabled=enabled,HotBefore=hot,HotAfter=GUIUtility.hotControl,Panel=panelId});
                            while(recentEvents.Count>24)recentEvents.Dequeue();
                        }
                        if(click || RuntimeCompat.TickCount64>=nextReport)
                        {
                            nextReport=RuntimeCompat.TickCount64+1000;
                            var input=Input.mousePosition;
                            var report=new{Build=TestSession.Hash(Core.Instance.GetType().Assembly.Location),Utc=DateTime.UtcNow,Width=Screen.width,Height=Screen.height,
                                Panel=panelId,Title=panelTitle,Rect=new{panelRect.x,panelRect.y,panelRect.width,panelRect.height},
                                InheritedEnabled=enabled,InheritedIdentityMatrix=matrix==Matrix4x4.identity,InheritedDepth=depth,
                                Pointer=new{input.x,input.y},CursorLock=Cursor.lockState.ToString(),CursorVisible=Cursor.visible,
                                Events=eventCounts,RecentEvents=recentEvents.ToArray(),RecentButtons=recentButtons.ToArray(),RecentHitTests=recentHitTests.ToArray(),Focused=Application.isFocused,ModPanelOpen=NativePanelCursor.PanelOpen,MouseInputSent=false};
                            NativeTestTools.Atomic(Path.Combine(Core.Instance.Test.SaveRoot,"ui-input-state.json"),JsonSerializer.Serialize(report,Core.Json));
                        }
                        if(!screenshotTaken && panelId!=0 && Event.current?.type==EventType.Repaint)
                        {
                            screenshotTaken=true;
                            ScreenCapture.CaptureScreenshot(Path.Combine(Core.Instance.Test.SaveRoot,"ui-input-current.png"));
                        }
                    }
                }
                finally
                {
                    GUI.enabled=enabled;GUI.color=color;GUI.backgroundColor=background;GUI.contentColor=content;GUI.matrix=matrix;GUI.depth=depth;
                }
            }
        }

        internal static void Window(int id,Rect rect,Action<int> draw,string title,GUIStyle style,bool shade=false)
        {
            // Draw in this OnGUI event, without a second native window focus/ID
            // dispatch through an IL2CPP WindowFunction delegate.
            panelId=id;panelTitle=title;panelRect=rect;
            if(shade)NativeWorkflowUI.Fill(new Rect(0,0,Screen.width,Screen.height),new Color(.025f,.035f,.05f,.90f));
            var evt=Event.current;bool inside=evt!=null&&rect.Contains(evt.mousePosition);
            GUI.BeginGroup(rect);
            try{GUI.Box(new Rect(0,0,rect.width,rect.height),title,style);draw(id);}
            finally{GUI.EndGroup();}
            // Preserve the existing native gameplay guards and prevent empty
            // panel regions from forwarding pointer input to another overlay.
            if(inside && evt!=null && evt.type is EventType.MouseDown or EventType.MouseUp or EventType.MouseDrag or EventType.ScrollWheel)evt.Use();
        }

        internal static bool Button(Rect rect,string label,GUIStyle style)
        {
            var evt=Event.current;bool enabled=GUI.enabled;
            bool down=framePointerType==EventType.MouseDown&&framePointerButton==0;
            bool up=framePointerType==EventType.MouseUp&&framePointerButton==0;
            bool inside=evt!=null&&rect.Contains(evt.mousePosition);
            // Match an actual press/release on this same control. Some IL2CPP
            // GUI.Button calls consume both events and release hotControl but
            // do not return true; no injected or queued input is used here.
            string key=null;
            if(down||up){var origin=GUIUtility.GUIToScreenPoint(new Vector2(rect.x,rect.y));key=panelId+"/"+origin.x+"/"+origin.y+"/"+rect.width+"/"+rect.height+"/"+label;}
            string previousPress=pressedButton;
            if(down&&inside&&enabled)pressedButton=key;
            bool released=up&&inside&&enabled&&pressedButton==key;
            if(up&&pressedButton==key)pressedButton=null;
            bool consumedBefore=evt?.type==EventType.Used;
            if(Trace&&(down||up)&&(inside||label=="下一页"))
            {
                recentHitTests.Enqueue(new{Utc=DateTime.UtcNow,Label=label,Down=down,Up=up,Inside=inside,Enabled=enabled,Type=evt?.type.ToString(),X=evt?.mousePosition.x,Y=evt?.mousePosition.y,Key=key,Previous=previousPress,Released=released});
                while(recentHitTests.Count>32)recentHitTests.Dequeue();
            }
            bool nativeClick=GUI.Button(rect,label,style);
            bool clicked=down||up?released:nativeClick;
            if(released && evt.type!=EventType.Used)evt.Use();
            if(clicked&&Trace)
            {
                recentButtons.Enqueue(new{Utc=DateTime.UtcNow,Panel=panelId,Label=label,NativeClick=nativeClick,MatchedPhysicalRelease=released,InputConsumedBefore=consumedBefore});
                while(recentButtons.Count>16)recentButtons.Dequeue();
            }
            return clicked;
        }
    }
}
