using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Components;
using UnityEngine;
namespace ResourceStoryMod
{
    internal static class ReleaseChinese
    {
        private static Locale Chinese => LocalizationSettings.AvailableLocales?.GetLocale(new LocaleIdentifier("zh-Hans"))
            ??LocalizationSettings.AvailableLocales?.GetLocale(new LocaleIdentifier("zh-CN"));
        internal static void Install(Harmony harmony)
        {
            for(var type=typeof(LocalizedStringDatabase);type!=null;type=type.BaseType)
                foreach(var method in type.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                    if(!method.IsAbstract&&!method.ContainsGenericParameters&&(method.Name.StartsWith("GetLocalizedString",StringComparison.Ordinal)||method.Name.StartsWith("GetTable",StringComparison.Ordinal))&&Array.Exists(method.GetParameters(),p=>p.ParameterType==typeof(Locale)))
                        harmony.Patch(method,prefix:new HarmonyMethod(typeof(ReleaseChinese),nameof(UseChinese)));
            foreach(var type in new[]{typeof(TMPLocalizer),typeof(TMPInputFieldLocalizer),typeof(DropdownLocalizer)}){
                var method=AccessTools.Method(type,"OnLocaleChanged",new[]{typeof(Locale)});
                if(method!=null)harmony.Patch(method,prefix:new HarmonyMethod(typeof(ReleaseChinese),nameof(FontLocale)));
            }
            harmony.Patch(AccessTools.Method(typeof(FontLoader),"GetFontByRole"),prefix:new HarmonyMethod(typeof(ReleaseChinese),nameof(UseChinese)));
        }
        private static bool refreshed;
        internal static void RefreshWhenReady()
        {
            if(refreshed||Chinese==null||FontLoader.Instance==null)return;
            refreshed=true;
            foreach(var label in Resources.FindObjectsOfTypeAll<LocalizeStringEvent>())label.RefreshString();
            Core.Instance?.EnsureChineseFallback();
        }
        private static void UseChinese(MethodBase __originalMethod,object[] __args)
        {
            var chinese=Chinese;if(chinese==null)return;
            var parameters=__originalMethod.GetParameters();
            for(int i=0;i<parameters.Length;i++)if(parameters[i].ParameterType==typeof(Locale))__args[i]=chinese;
        }
        private static void FontLocale(ref Locale __0){var chinese=Chinese;if(chinese!=null)__0=chinese;}
    }
}
