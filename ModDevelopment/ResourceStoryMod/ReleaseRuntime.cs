using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }

namespace ResourceStoryMod
{
    internal static class NativeTypes
    {
        internal static Type Of<T>() => typeof(T);
        internal static Type From(Type value) => value;
    }
    internal static class ManagedIdentity
    {
        private sealed class Key { internal readonly IntPtr Value = new IntPtr(Interlocked.Increment(ref next)); }
        private static long next;
        private static readonly ConditionalWeakTable<object,Key> keys = new ConditionalWeakTable<object,Key>();
        internal static IntPtr NativeIdentity(this object value) => value == null ? IntPtr.Zero : keys.GetValue(value,_=>new Key()).Value;
        internal static T Cast<T>(this object value) => (T)value;
        internal static T TryCast<T>(this object value) where T:class => value as T;
    }
    internal sealed class NativeArray<T>
    {
        private readonly T[] values;
        public NativeArray(long length) { values=new T[checked((int)length)]; }
        public NativeArray(T[] source) { values=source; }
        public T this[int index] { get=>values[index]; set=>values[index]=value; }
        public int Length => values.Length;
        public static implicit operator T[](NativeArray<T> value) => value?.values;
        public static implicit operator NativeArray<T>(T[] value) => value==null?null:new NativeArray<T>(value);
    }
}
namespace MelonLoader
{
    // Internal lifecycle adapter only. There is no external loader or auto-start hook.
    public abstract class MelonMod
    {
        internal readonly HarmonyLib.Harmony HarmonyInstance = new HarmonyLib.Harmony("local.millsage.story.runtime");
        internal readonly ReleaseLogger LoggerInstance = new ReleaseLogger();
        public virtual void OnInitializeMelon() { }
        public virtual void OnUpdate() { }
        public virtual void OnLateUpdate() { }
        public virtual void OnGUI() { }
        public virtual void OnApplicationQuit() { }
        public virtual void OnDeinitializeMelon() { }
    }
    internal sealed class ReleaseLogger
    {
        internal void Msg(object value) => UnityEngine.Debug.Log("[米尔赛奇] "+value);
        internal void Warning(object value) => UnityEngine.Debug.LogWarning("[米尔赛奇] "+value);
        internal void Error(object value) => UnityEngine.Debug.LogError("[米尔赛奇] "+value);
    }
}
