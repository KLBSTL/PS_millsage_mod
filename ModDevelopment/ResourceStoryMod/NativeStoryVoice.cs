using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    // A single owned source; audio completion never calls the story engine.
    internal sealed class NativeStoryVoice : IDisposable
    {
        private readonly Core core;
        private readonly string root;
        private VoicePack pack;
        private AudioSource source;
        private AudioClip clip;
        private bool loaded,paused;
        private float fadeAt=-1;
        private string owner;
        private string actor;
        private float[] samples;
        private int channels;
        internal string Actor=>actor;
        internal float MouthLevel {get;private set;}
        internal int Starts {get;private set;}
        internal int Misses {get;private set;}
        internal int Releases {get;private set;}
        internal bool Playing=>source!=null&&source.isPlaying;
        internal bool MissingNextForTest {get;set;}
        internal NativeStoryVoice(Core core){this.core=core;root=Path.Combine(core.Root,"ModWorkbench","res","prepared","p2");}
        private void Load()
        {
            if(loaded)return;loaded=true;
            string path=Path.Combine(root,"voices.json");if(!File.Exists(path))return;
            try{pack=JsonSerializer.Deserialize<VoicePack>(File.ReadAllText(path),Core.Json);if(pack?.Version!=1)pack=null;}
            catch(Exception e){core.LoggerInstance.Warning("P2_VOICE_MANIFEST "+e.Message);pack=null;}
        }
        internal bool Play(StoryPresentationPage page)
        {
            Stop();Load();
            if(page?.CanVoice!=true||string.IsNullOrEmpty(page.SpeakerId)||page.SpeakerId==StoryContent.PlayerSpeaker)return false;
            try{
                var asset=pack?.Find(page.Frame.Scene.Id,page.Frame.Node.Id,page.SpeakerId,page.Segment,page.Text);
                if(asset==null){Misses++;return false;}
                string path=VoiceAsset.Resolve(root,asset.File);
                if(core.Test!=null&&MissingNextForTest){MissingNextForTest=false;path+=".missing-probe";}
                var bytes=File.ReadAllBytes(path);
                if(RuntimeCompat.Hex(RuntimeCompat.Sha256(bytes))!=asset.Sha256)throw new InvalidDataException("Voice checksum changed");
                var pcm=PcmVoice.Read(bytes);
                if(source==null){var obj=new GameObject("Millsage dialogue voice");UnityEngine.Object.DontDestroyOnLoad(obj);source=obj.AddComponent<AudioSource>();source.playOnAwake=false;source.loop=false;source.spatialBlend=0;source.ignoreListenerPause=false;source.outputAudioMixerGroup=AudioManager.Instance.audioSource2.outputAudioMixerGroup;}
                clip=AudioClip.Create(asset.Actor+"/"+asset.Node+"/"+asset.Segment,pcm.Samples.Length/pcm.Channels,pcm.Channels,pcm.Rate,false);
                if(!clip.SetData(new NativeArray<float>(pcm.Samples),0))throw new InvalidDataException("Voice PCM rejected");
                source.clip=clip;source.volume=Volume();source.Play();paused=false;
                actor=asset.Actor;samples=pcm.Samples;channels=pcm.Channels;
                owner=asset.Actor+"/"+asset.Scene+"/"+asset.Node+"/"+asset.Segment;Starts++;return true;
            }catch(Exception e){Misses++;Stop();core.LoggerInstance.Warning("P2_VOICE_FALLBACK "+e.Message);return false;}
        }
        private static float Volume()=>Mathf.Clamp01(EmporiumOption.Instance?.dialogVolume??1f);
        internal void Pause(){if(source!=null&&clip!=null&&!paused){source.Pause();paused=true;}}
        internal void Resume(){if(source!=null&&clip!=null&&paused){source.UnPause();paused=false;}}
        internal void Stop(bool fade=false)
        {
            if(fade&&clip!=null&&Playing){fadeAt=Time.unscaledTime;return;}
            fadeAt=-1;paused=false;owner=null;actor=null;samples=null;MouthLevel=0;
            if(source!=null){source.Stop();source.clip=null;}
            if(clip!=null){UnityEngine.Object.Destroy(clip);clip=null;Releases++;}
        }
        internal void Update(bool ownsDialogue)
        {
            if(clip==null)return;
            if(fadeAt>=0){float t=(Time.unscaledTime-fadeAt)/.04f;if(t>=1){Stop();return;}source.volume=Volume()*(1-t);return;}
            if(!ownsDialogue){Stop();return;}
            source.volume=Volume();if(!paused&&!source.isPlaying)Stop();
            if(samples!=null&&!paused&&Playing){int start=source.timeSamples*channels,count=Math.Min(512,samples.Length-start);double sum=0;for(int i=0;i<count;i++)sum+=samples[start+i]*samples[start+i];MouthLevel=count>0?(float)Math.Sqrt(sum/count):0;}else MouthLevel=0;
        }
        public void Dispose(){Stop();if(source!=null){UnityEngine.Object.Destroy(source.gameObject);source=null;}}
        internal object Status()=>new{playing=Playing,paused,owner,loaded=clip!=null,starts=Starts,misses=Misses,releases=Releases,sources=source==null?0:1,volume=source?.volume??0,storyCompletionCallbacks=0};
        internal string CurrentActor=>actor;
    }
}
