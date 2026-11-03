using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ResourceStoryMod.Story
{
    // A presentation page never owns story effects. Only the existing engine commits nodes.
    public sealed class StoryPresentationPage
    {
        public StoryFrame Frame {get;internal set;}
        public string Text {get;internal set;}
        public int Segment {get;internal set;}
        public int Segments {get;internal set;}
        public long Generation {get;internal set;}
        public string SpeakerId {get;internal set;}
        public string SpeakerName {get;internal set;}
        public int DialoguePart {get;internal set;}
        public string TextKind {get;internal set;}="spoken";
        public bool CanVoice=>TextKind=="spoken";
        public bool IsPlayerBubble=>CanVoice&&SpeakerId==StoryContent.PlayerSpeaker;
    }
    public sealed class StoryPresentationCursor
    {
        private readonly StoryEngine engine;
        private readonly int day,maxCharacters;
        private long revision=-1,generation;
        private int segment;
        private sealed class Piece {public string Text,Speaker,Kind,Label;public int Part,WaitDays,Start,End;}
        private Piece[] pieces=Array.Empty<Piece>();
        private int notBeforeDay;
        private string contentHash;
        private StoryPresentationPage page;
        public bool Paused {get;private set;}
        public StoryEngine Engine=>engine;
        public bool Waiting=>Sync()!=null&&notBeforeDay>day;
        public int DueDay {get{Sync();return notBeforeDay;}}
        public StoryPresentationCursor(StoryEngine engine,int day,int maxCharacters=72)
        {
            this.engine=engine??throw new ArgumentNullException(nameof(engine));this.day=day;
            if(maxCharacters<16||maxCharacters>512)throw new ArgumentOutOfRangeException(nameof(maxCharacters));
            this.maxCharacters=maxCharacters;
        }
        private StoryFrame Sync()
        {
            var frame=engine.Frame();if(frame==null)return null;
            if(revision!=frame.Revision){
                revision=frame.Revision;segment=0;notBeforeDay=0;page=null;generation++;
                var all=new List<Piece>();var legacy=new List<Piece>();
                var parts=(DiaryPresentation.Scene(frame.Scene.Id)?PackageC.DiaryParts(engine.Snapshot(),frame.Scene.Id,frame.Node,frame.Text,day):null)??engine.DialogueParts(frame)??new List<StoryDialoguePart>{new(){Speaker=frame.Node.Speaker,Text=frame.Text??""}};
                for(int p=0;p<parts.Count;p++){
                    int offset=0;var texts=Split(parts[p].Text,maxCharacters);
                    for(int i=0;i<texts.Length;i++){legacy.Add(new Piece{Text=texts[i],Speaker=parts[p].Speaker,Part=p,Start=offset,End=offset+texts[i].Length,WaitDays=i==0?parts[p].WaitDaysBefore:0});offset+=texts[i].Length;}
                    bool first=true;foreach(var beat in StoryPerformance.Project(frame.Scene.Id,frame.Node.Id,p,parts[p].Speaker,parts[p].Text)){
                        offset=beat.Start;foreach(string text in Split(beat.Text,maxCharacters)){all.Add(new Piece{Text=text,Speaker=parts[p].Speaker,Part=p,Kind=beat.Kind,Label=beat.Label,Start=offset,End=offset+text.Length,WaitDays=first?parts[p].WaitDaysBefore:0});offset+=text.Length;first=false;}
                    }
                }
                pieces=all.ToArray();contentHash=RuntimeCompat.Hex(RuntimeCompat.Sha256(Encoding.UTF8.GetBytes(string.Join("\n",pieces.Select(p=>p.Speaker+"|"+p.Part+"|"+p.WaitDays+"|"+p.Text)))));
                var saved=engine.PresentationBookmark;
                string legacyHash=RuntimeCompat.Hex(RuntimeCompat.Sha256(Encoding.UTF8.GetBytes(string.Join("\n",legacy.Select(p=>p.Speaker+"|"+p.Part+"|"+p.WaitDays+"|"+p.Text)))));
                if(saved?.Scene==frame.Scene.Id&&saved.Node==frame.Node.Id&&saved.Segment>=0){
                    if(saved.ContentSHA256==contentHash&&saved.Segment<pieces.Length){segment=saved.Segment;notBeforeDay=saved.NotBeforeDay;}
                    else if(saved.Segment<legacy.Count&&saved.ContentSHA256==legacyHash){
                        // Migrate the P10 source-page offset, preserving story effects/day wait.
                        var old=legacy[saved.Segment];int found=Array.FindIndex(pieces,p=>p.Part==old.Part&&p.End>old.Start);if(found<0)found=Array.FindLastIndex(pieces,p=>p.Part==old.Part);if(found>=0)segment=found;notBeforeDay=saved.NotBeforeDay;
                    }
                    else{
                        // Authored words/projection changed. Restart the same part
                        // without replaying node effects or losing its game-day wait.
                        int found=Array.FindIndex(pieces,p=>p.Part==saved.DialoguePart);segment=found>=0?found:0;notBeforeDay=saved.NotBeforeDay;
                    }
                }
            }
            return frame;
        }
        public StoryPresentationPage Page()
        {
            var frame=Sync();if(frame==null||notBeforeDay>day)return null;var piece=pieces[segment];
            return page??=new StoryPresentationPage{Frame=frame,Text=piece.Text,Segment=segment,Segments=pieces.Length,Generation=generation,SpeakerId=piece.Speaker,SpeakerName=piece.Kind is "narration" or "thought"?"场景提示":piece.Kind=="prop"?piece.Label??"道具文字":StoryContent.ResolveSpeaker(engine.Package,piece.Speaker)?.DisplayName??"场景提示",DialoguePart=piece.Part,TextKind=piece.Kind??"spoken"};
        }
        public bool Commit(StoryPresentationPage expected,string choice=null)
        {
            if(Paused||expected==null||!ReferenceEquals(Page(),expected)||engine.Frame()?.Revision!=expected.Frame.Revision)return false;
            if(choice==null&&segment<pieces.Length-1){
                segment++;generation++;page=null;notBeforeDay=pieces[segment].WaitDays>0?checked(day+pieces[segment].WaitDays):0;
                engine.StorePresentation(new(){Scene=expected.Frame.Scene.Id,Node=expected.Frame.Node.Id,ContentSHA256=contentHash,Segment=segment,DialoguePart=pieces[segment].Part,NotBeforeDay=notBeforeDay});return true;
            }
            if(expected.Frame.Node.Kind=="choice"){
                if(choice==null||segment!=pieces.Length-1||!expected.Frame.Choices.Any(x=>x.Id==choice))return false;
                if(!engine.Choose(choice,expected.Frame.Revision,day))return false;
            }else{
                if(choice!=null)return false;
                if(!engine.Advance(expected.Frame.Revision,day))return false;
            }
            page=null;generation++;return true;
        }
        public void Pause(){Paused=true;page=null;generation++;}
        public void Resume(){Paused=false;page=null;generation++;}
        public static string[] Split(string text,int maximum)
        {
            if(maximum<2)throw new ArgumentOutOfRangeException(nameof(maximum));
            if(string.IsNullOrEmpty(text))return new[]{""};
            var result=new List<string>();int offset=0;
            while(offset<text.Length){
                int length=Math.Min(maximum,text.Length-offset);
                if(offset+length<text.Length){
                    for(int i=length-1;i>=length/2;i--)if("。！？；，\n".Contains(text[offset+i])){length=i+1;break;}
                    if(char.IsHighSurrogate(text[offset+length-1]))length--;
                }
                result.Add(text.Substring(offset,length));offset+=length;
            }
            return result.ToArray();
        }
    }

    public sealed class StoryPerformanceCue
    {
        public string Scene {get;set;}public string Node {get;set;}public int Part {get;set;}public string Actor {get;set;}
        public string SourceSHA256 {get;set;}public string SourceText {get;set;}public string Expression {get;set;}public string Motion {get;set;}public string DefaultKind {get;set;}public string Label {get;set;}
        public List<StoryPerformanceSpan> Spans {get;set;}=new();
    }
    public sealed class StoryPerformanceSpan
    {
        public int Start {get;set;}public int Length {get;set;}public string Kind {get;set;}public string Label {get;set;}
    }
    public sealed class StoryPerformanceBeat
    {
        public string Text,Kind,Label;public int Start;
    }
    public static class StoryPerformance
    {
        private sealed class Document {public int Version {get;set;}public List<StoryPerformanceCue> Entries {get;set;}=new();}
        private static Dictionary<string,StoryPerformanceCue[]> cues=new();
        public static void Configure(string json)
        {
            var data=JsonSerializer.Deserialize<Document>(json,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});if(data?.Version!=1)throw new FormatException("Bad performance sidecar");
            foreach(var e in data.Entries){
                if(e.SourceSHA256!=Hash(e.SourceText))throw new FormatException("Performance source hash differs");int end=0;
                foreach(var s in e.Spans){if(s.Start<end||s.Length<1||s.Start+s.Length>e.SourceText.Length||s.Kind is not ("action" or "narration" or "thought" or "prop" or "literal"))throw new FormatException("Invalid performance span");end=s.Start+s.Length;}
            }
            cues=data.Entries.GroupBy(e=>e.Scene+"/"+e.Node+"/"+e.Part).ToDictionary(g=>g.Key,g=>g.ToArray());
        }
        private static string Hash(string text)=>RuntimeCompat.Hex(RuntimeCompat.Sha256(Encoding.UTF8.GetBytes(text??"")));
        public static StoryPerformanceCue Find(string scene,string node,int part,string actor)=>cues.GetValueOrDefault(scene+"/"+node+"/"+part)?.FirstOrDefault(c=>c.Actor==actor);
        public static StoryPerformanceBeat[] Project(string scene,string node,int part,string actor,string text)
        {
            text??="";var entry=cues.GetValueOrDefault(scene+"/"+node+"/"+part)?.FirstOrDefault(e=>e.Actor==actor&&e.SourceSHA256==Hash(text));
            if(entry==null||entry.Spans.Count==0)return new[]{new StoryPerformanceBeat{Text=text,Kind=entry?.DefaultKind??(text.Contains('（')||text.Contains('(')?"narration":"spoken"),Label=entry?.Label,Start=0}};
            var result=new List<StoryPerformanceBeat>();int offset=0;
            void Add(int start,int length,string kind,string label=null){if(length>0)result.Add(new StoryPerformanceBeat{Text=text.Substring(start,length),Kind=kind,Label=label,Start=start});}
            foreach(var s in entry.Spans){
                if(s.Kind=="literal")continue;Add(offset,s.Start-offset,entry.DefaultKind??"spoken",entry.DefaultKind=="prop"?entry.Label??"写字板":null);
                if(s.Kind!="action")Add(s.Start,s.Length,s.Kind,s.Label);offset=s.Start+s.Length;
            }
            Add(offset,text.Length-offset,entry.DefaultKind??"spoken",entry.DefaultKind=="prop"?entry.Label??"写字板":null);
            return result.Count>0?result.ToArray():new[]{new StoryPerformanceBeat{Text="……",Kind="silent",Start=0}};
        }
    }
}
