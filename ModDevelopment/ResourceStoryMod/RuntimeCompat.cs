using System;
using System.IO;

namespace ResourceStoryMod
{
    // Shared by the Unity Mono build and the offline logic executables.
    internal static class RuntimeCompat
    {
        internal static long TickCount64 => (long)(System.Diagnostics.Stopwatch.GetTimestamp() * (1000.0 / System.Diagnostics.Stopwatch.Frequency));
        internal static byte[] Sha256(byte[] data) { using (var sha=System.Security.Cryptography.SHA256.Create()) return sha.ComputeHash(data); }
        internal static string Hex(byte[] data) => BitConverter.ToString(data).Replace("-", "");
        internal static int Clamp(int value,int min,int max) => Math.Max(min,Math.Min(max,value));
        internal static float Clamp(float value,float min,float max) => Math.Max(min,Math.Min(max,value));
        internal static double Clamp(double value,double min,double max) => Math.Max(min,Math.Min(max,value));
        internal static void MoveReplace(string source,string target) { if(File.Exists(target))File.Delete(target);File.Move(source,target); }
    }
}
