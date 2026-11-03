using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ResourceStoryMod.Story
{
    public sealed class VoicePack
    {
        public int Version {get;set;}
        public bool Enabled {get;set;}
        public List<VoiceAsset> Entries {get;set;}=new();
        public VoiceAsset Find(string scene,string node,string actor,int segment,string text)=>Enabled?
            Entries.SingleOrDefault(x=>x.Scene==scene&&x.Node==node&&x.Actor==actor&&x.Segment==segment&&x.TextSha256==VoiceAsset.HashText(text)):null;
    }
    public sealed class VoiceAsset
    {
        public string Scene {get;set;}
        public string Node {get;set;}
        public string Actor {get;set;}
        public int Segment {get;set;}
        public string File {get;set;}
        public string Sha256 {get;set;}
        public string TextSha256 {get;set;}
        public bool Reviewed {get;set;}
        public static string HashText(string text)=>RuntimeCompat.Hex(RuntimeCompat.Sha256(Encoding.UTF8.GetBytes(text??"")));
        public static string Resolve(string root,string relative)
        {
            if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||relative.Contains(':')||relative.Split('/','\\').Any(x=>x==".."||x=="."))throw new InvalidDataException("Unsafe voice path");
            string prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            string result=Path.GetFullPath(Path.Combine(root,relative));
            if(!result.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!result.EndsWith(".wav",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Voice must be a local WAV");
            return result;
        }
    }
    public sealed class PcmVoice
    {
        public int Rate {get;private set;}
        public int Channels {get;private set;}
        public float[] Samples {get;private set;}
        public static PcmVoice Read(byte[] bytes)
        {
            if(bytes==null||bytes.Length<44||bytes.Length>16*1024*1024)throw new InvalidDataException("Voice WAV size invalid");
            using var stream=new MemoryStream(bytes);using var r=new BinaryReader(stream);
            string Four()=>Encoding.ASCII.GetString(r.ReadBytes(4));
            if(Four()!="RIFF")throw new InvalidDataException("Expected RIFF");
            uint riff=r.ReadUInt32();if(Four()!="WAVE"||riff>bytes.Length-8)throw new InvalidDataException("Truncated WAV");
            ushort format=0,channels=0,bits=0,align=0;int rate=0;byte[] pcm=null;
            while(stream.Position+8<=riff+8){
                string id=Four();uint length=r.ReadUInt32();long end=stream.Position+length;
                if(end>riff+8||length>int.MaxValue)throw new InvalidDataException("Truncated WAV chunk");
                if(id=="fmt "){
                    if(length<16)throw new InvalidDataException("Short WAV format");
                    format=r.ReadUInt16();channels=r.ReadUInt16();rate=r.ReadInt32();r.ReadInt32();align=r.ReadUInt16();bits=r.ReadUInt16();
                }else if(id=="data"){if(pcm!=null)throw new InvalidDataException("Duplicate WAV data");pcm=r.ReadBytes((int)length);}
                stream.Position=end+(length&1);
            }
            if(format!=1||bits!=16||channels is not (1 or 2)||rate<8000||rate>96000||align!=channels*2||pcm==null||pcm.Length==0||pcm.Length%align!=0||pcm.Length/(double)(rate*align)>120)throw new InvalidDataException("Expected bounded PCM16 mono/stereo WAV");
            var samples=new float[pcm.Length/2];for(int i=0;i<samples.Length;i++)samples[i]=BitConverter.ToInt16(pcm,2*i)/32768f;
            return new PcmVoice{Rate=rate,Channels=channels,Samples=samples};
        }
    }
}
