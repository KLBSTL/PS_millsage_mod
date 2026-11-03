using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using HarmonyLib;

namespace ResourceStoryMod
{
    internal static class ReleaseAccessBridge
    {
        private static readonly Dictionary<string,FieldInfo> fields=new Dictionary<string,FieldInfo>();
        private static readonly Dictionary<string,MethodBase> methods=new Dictionary<string,MethodBase>();
        private static FieldInfo Find(Type owner,string name)
        {
            string key=owner.AssemblyQualifiedName+"|"+name;
            if(!fields.TryGetValue(key,out var field))fields[key]=field=AccessTools.Field(owner,name)??throw new MissingFieldException(owner.FullName,name);
            return field;
        }
        internal static T Get<T>(Type owner,string name,object instance)=>(T)Find(owner,name).GetValue(instance);
        internal static void Set<T>(Type owner,string name,object instance,T value)=>Find(owner,name).SetValue(instance,value);
        internal static ref F Ref<T,F>(T instance,string name) where T:class
        { return ref AccessTools.FieldRefAccess<T,F>(Find(typeof(T),name))(instance); }
        internal static ref F StaticRef<F>(Type owner,string name)
        { return ref AccessTools.StaticFieldRefAccess<F>(Find(owner,name))(); }
        internal static object Invoke(Type owner,string name,Type[] parameters,object instance,object[] args)
        {
            // Resolve on closed types. Metadata tokens can change when the
            // native loader or an isolated test rewrites an assembly.
            string key=owner.AssemblyQualifiedName+"|"+name;
            foreach(var parameter in parameters)key+="|"+parameter.AssemblyQualifiedName;
            if(!methods.TryGetValue(key,out var method))
            {
                for(var type=owner;type!=null&&method==null;type=type.BaseType)
                    method=type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly,null,parameters,null);
                if(method==null)throw new MissingMethodException(owner.FullName,name);
                methods[key]=method;
            }
            try { return method.Invoke(instance,args); }
            catch(TargetInvocationException error) { ExceptionDispatchInfo.Capture(error.InnerException??error).Throw();throw; }
        }
    }
}
