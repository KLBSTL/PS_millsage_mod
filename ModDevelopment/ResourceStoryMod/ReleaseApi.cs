using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ResourceStoryMod
{
    internal static class ReleaseApi
    {
        internal static MethodInfo Method(Type type,string name,Type[] parameters=null,Type[] generics=null)
        {
            if(parameters!=null||generics!=null)return AccessTools.Method(type,name,parameters,generics);
            // The newer one-argument wrapper delegates to the contextual overload.
            if(type==typeof(StoreClient)&&name==nameof(StoreClient.IsClientBuyingThisItem))
                return AccessTools.Method(type,name,new[]{typeof(GameItem),typeof(bool)})
                    ??AccessTools.Method(type,name,new[]{typeof(GameItem)});
            var candidates=type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance).Where(m=>m.Name==name&&!m.IsGenericMethodDefinition).ToArray();
            if(candidates.Length==1)return candidates[0];
            if(ReleaseApiSignatures.Expected.TryGetValue(type.FullName+"|"+name,out var signature)) {
                var matching=candidates.Where(m=>m.GetParameters().Select(p=>TypeName(p.ParameterType)).SequenceEqual(signature)).ToArray();
                if(matching.Length==1)return matching[0];
            }
            if(candidates.Length==0)return AccessTools.Method(type,name);
            throw new AmbiguousMatchException("正式版API需要明确签名："+type.FullName+"."+name);
        }
        private static string TypeName(Type type)
        {
            if(type.IsByRef)return TypeName(type.GetElementType())+"&";
            if(type.IsArray)return TypeName(type.GetElementType())+"[]";
            return type.IsGenericType?type.GetGenericTypeDefinition().FullName+"<"+string.Join(",",type.GetGenericArguments().Select(TypeName))+">":type.FullName;
        }
    }
}
