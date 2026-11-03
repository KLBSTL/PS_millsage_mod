using System;
using System.Linq;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;

namespace ResourceStoryMod
{
    internal static class ReleaseSaveRoot
    {
        private static string isolated;
        internal static string Path { get=>isolated??Application.persistentDataPath; set=>isolated=value; }
    }
    internal static class NativeLibrary
    {
        [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32",CharSet=CharSet.Ansi,ExactSpelling=true,SetLastError=true)] private static extern IntPtr GetProcAddress(IntPtr module,string name);
        internal static IntPtr Load(string path) { var h=LoadLibraryW(path);if(h==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();return h; }
        internal static IntPtr GetExport(IntPtr h,string name) { var p=GetProcAddress(h,name);if(p==IntPtr.Zero)throw new EntryPointNotFoundException(name);return p; }
    }
    internal static class ReleaseMap
    {
        private static AfterhourLocation venue;
        internal static void Register(Func<bool> available,Action arrive)
        {
            if(venue!=null)return;
            venue=ModHelper.AddAfterhourLocation(MillsageMod.Id,"livehouse",()=>"演出场地",arrive,()=>{},null,null,null,null,available,available);
        }
        internal static bool IsOwn(AfterhourLocation value)=>value!=null&&ReferenceEquals(value,venue);
        internal static bool Travel(){if(venue==null||!venue.IsAvailable())return false;MapUIManager.Instance.Travel(venue);return true;}
        internal static void SetOutside(MapUIManager map,bool outside)
        {
            if(map==null)return;
            if(!outside){map.Current=null;return;}
            if(venue==null)throw new InvalidOperationException("演出场地尚未注册。");
            map.Current=venue;
        }
    }
    internal static class ReleaseUi
    {
        private static TextMeshProUGUI Find(NegociationUIManager ui,string value)
        {
            return ui?.uiObject?.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(x=>!string.IsNullOrEmpty(value)&&x.text.Contains(value));
        }
        internal static TextMeshProUGUI ItemName(NegociationUIManager ui) => Find(ui,ui?.currentNegociatedItem?.name);
        internal static TextMeshProUGUI Cost(NegociationUIManager ui) => Find(ui,ui?.currentNegociatedItem?.GetNegociatedValue().ToString());
    }
}
