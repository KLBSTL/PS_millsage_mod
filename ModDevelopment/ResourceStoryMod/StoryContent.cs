using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ResourceStoryMod.Story
{
    public abstract class StoryData
    {
        [JsonExtensionData] public Dictionary<string, JsonElement> Unknown { get; set; }
    }
    public sealed class StoryPackage : StoryData
    {
        public int FormatVersion { get; set; } = 1;
        public string Id { get; set; } = "millsage";
        public bool Enabled { get; set; }
        public string CampaignProfile {get;set;}="";
        public List<StoryActor> Actors { get; set; } = new();
        public List<StoryChapter> Chapters { get; set; } = new();
        public List<StoryScene> Scenes { get; set; } = new();
    }
    public sealed class StoryActor : StoryData
    {
        public string Id { get; set; }
        public string DisplayName { get; set; } = "";
        public string Portrait { get; set; } = "";
        public string ResidenceZone { get; set; } = "";
    }
    public sealed class StoryChapter : StoryData
    {
        public string Id { get; set; }
        public string Title { get; set; } = "";
    }
    public sealed class StoryScene : StoryData
    {
        public string Id { get; set; }
        public string ChapterId { get; set; }
        public string Status { get; set; } = "draft";
        public string Context { get; set; } = "counter";
        public int Priority { get; set; }
        public bool Once { get; set; } = true;
        public int CooldownDays { get; set; }
        public List<string> Participants { get; set; } = new();
        public List<StoryCondition> Requires { get; set; } = new();
        public string Entry { get; set; } = "";
        public List<StoryNode> Nodes { get; set; } = new();
    }
    public sealed class StoryNode : StoryData
    {
        public string Id { get; set; }
        public string Kind { get; set; } = "line";
        public string Speaker { get; set; } = "";
        public string Text { get; set; } = "";
        public string Portrait { get; set; } = "";
        public string Voice { get; set; } = "";
        public string Next { get; set; } = "";
        public List<StoryChoice> Choices { get; set; } = new();
        public List<StoryEffect> Effects { get; set; } = new();
        [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
        public List<StoryDialoguePart> DialogueParts {get;set;}
    }
    public sealed class StoryDialoguePart : StoryData
    {
        public string Speaker {get;set;}="";
        public string Text {get;set;}="";
        [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]
        public int WaitDaysBefore {get;set;}
    }
    public sealed class StoryChoice : StoryData
    {
        public string Id { get; set; }
        public string Text { get; set; } = "";
        public string Next { get; set; }
        public List<StoryCondition> Requires { get; set; } = new();
        public List<StoryEffect> Effects { get; set; } = new();
    }
    public sealed class StoryCondition : StoryData
    {
        public string Kind { get; set; } = "flag";
        public string Key { get; set; }
        public bool Flag { get; set; } = true;
        public int Number { get; set; }
    }
    public sealed class StoryEffect : StoryData
    {
        public string Kind { get; set; } = "set_flag";
        public string Key { get; set; }
        public bool Flag { get; set; } = true;
        public int Number { get; set; }
    }
    public sealed class StoryValidation
    {
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
        public bool Valid => Errors.Count == 0;
    }
    public static class StoryContent
    {
        public const string PlayerSpeaker="player";
        public static StoryActor ResolveSpeaker(StoryPackage package,string id)=>id==PlayerSpeaker?new StoryActor{Id=PlayerSpeaker,DisplayName="麻里奈"}:package.Actors.Find(a=>a.Id==id);
        public static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = true,
            MaxDepth = 32,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        private static readonly Regex Identifier = new("^[a-z][a-z0-9_.-]{0,79}$", RegexOptions.CultureInvariant);
        public static bool ValidId(string value) => value != null && Identifier.IsMatch(value);
        public static StoryPackage Load(string path)
        {
            var file = new FileInfo(path);
            if (file.Length > 1024 * 1024) throw new InvalidDataException("Story package exceeds 1 MiB");
            var package = JsonSerializer.Deserialize<StoryPackage>(File.ReadAllText(path), Json);
            var check = Validate(package);
            if (!check.Valid) throw new InvalidDataException(string.Join("\n", check.Errors));
            return package;
        }
        public static bool SafeMedia(string path, string extension)
        {
            if (string.IsNullOrEmpty(path)) return true;
            if (Path.IsPathRooted(path) || path.Contains(':') || path.Contains('\\')) return false;
            var parts = path.Split('/');
            return parts.All(p => p.Length > 0 && p != "." && p != "..") && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        }
        public static StoryValidation Validate(StoryPackage package)
        {
            var result = new StoryValidation();
            void Error(string text) => result.Errors.Add(text);
            void Strict(StoryData data, string where)
            {
                if (data?.Unknown?.Count > 0) Error(where + ": unknown fields: " + string.Join(",", data.Unknown.Keys));
            }
            HashSet<string> Ids<T>(List<T> values, Func<T, string> id, string where) where T : StoryData
            {
                var set = new HashSet<string>(StringComparer.Ordinal);
                if (values == null) { Error(where + ": array cannot be null"); return set; }
                foreach (var value in values)
                {
                    if (value == null) { Error(where + ": null element"); continue; }
                    var key = id(value);
                    if (!ValidId(key) || !set.Add(key)) Error(where + ": invalid or duplicate id " + key);
                    Strict(value, where + "/" + key);
                }
                return set;
            }
            if (package == null) { Error("Package is null"); return result; }
            Strict(package, "package");
            if (package.FormatVersion != 1) Error("Unsupported formatVersion");
            if(package.CampaignProfile is not ("" or "solo_return_v1"))Error("Unsupported campaign profile");
            if (!ValidId(package.Id)) Error("Invalid package id");
            var actors = Ids(package.Actors, a => a.Id, "actors");
            var chapters = Ids(package.Chapters, c => c.Id, "chapters");
            var scenes = Ids(package.Scenes, s => s.Id, "scenes");
            if (actors.Count > 32 || chapters.Count > 64 || scenes.Count > 256) Error("Package exceeds actor/chapter/scene limits");
            if (!result.Valid) return result;
            foreach (var actor in package.Actors)
            {
                if (!SafeMedia(actor.Portrait, ".png")) Error(actor.Id + ": portrait must be a relative PNG path");
                if (actor.ResidenceZone is not ("" or "lower" or "middle" or "upper")) Error(actor.Id + ": invalid residence zone");
            }
            void Conditions(List<StoryCondition> conditions, string where)
            {
                if (conditions == null) { Error(where + ": requires cannot be null"); return; }
                foreach (var c in conditions)
                {
                    if (c == null) { Error(where + ": null condition"); continue; }
                    Strict(c, where);
                    if (!ValidId(c.Key)) Error(where + ": invalid condition key");
                    if (c.Kind != "flag" && c.Kind != "counter_at_least" && c.Kind != "completed") Error(where + ": unknown condition " + c.Kind);
                    if (c.Kind == "completed" && !scenes.Contains(c.Key)) Error(where + ": missing completed scene " + c.Key);
                }
            }
            void Effects(List<StoryEffect> effects, string where)
            {
                if (effects == null) { Error(where + ": effects cannot be null"); return; }
                foreach (var e in effects)
                {
                    if (e == null) { Error(where + ": null effect"); continue; }
                    Strict(e, where);
                    if (!ValidId(e.Key)) Error(where + ": invalid effect key");
                    if (e.Kind != "set_flag" && e.Kind != "set_counter" && e.Kind != "add_counter") Error(where + ": unknown effect " + e.Kind);
                }
            }
            foreach (var scene in package.Scenes)
            {
                if (!chapters.Contains(scene.ChapterId)) Error(scene.Id + ": missing chapter");
                if (scene.Status != "draft" && scene.Status != "ready") Error(scene.Id + ": status must be draft or ready");
                if (scene.Context != "counter" && scene.Context != "night") Error(scene.Id + ": invalid context");
                if (scene.CooldownDays < 0) Error(scene.Id + ": negative cooldown");
                if (scene.Participants == null || scene.Participants.Count != scene.Participants.Distinct().Count() || scene.Participants.Any(p => !actors.Contains(p)))
                    Error(scene.Id + ": invalid participants");
                Conditions(scene.Requires, scene.Id);
                var nodes = Ids(scene.Nodes, n => n.Id, scene.Id + "/nodes");
                if (nodes.Count > 512) Error(scene.Id + ": at most 512 nodes per scene");
                if (scene.Status == "draft") { result.Warnings.Add(scene.Id + ": draft, never scheduled"); continue; }
                if (!nodes.Contains(scene.Entry)) Error(scene.Id + ": missing entry node");
                if (scene.Participants == null || scene.Nodes.Any(n => n == null)) continue;
                foreach (var node in scene.Nodes)
                {
                    string where = scene.Id + "/" + node.Id;
                    if (node.Kind != "line" && node.Kind != "choice" && node.Kind != "end") Error(where + ": invalid node kind");
                    if (node.Kind != "end" && (string.IsNullOrWhiteSpace(node.Text) || node.Text.Length > 4000)) Error(where + ": text must contain 1..4000 characters");
                    if (!string.IsNullOrEmpty(node.Speaker) && node.Speaker!=PlayerSpeaker && !scene.Participants.Contains(node.Speaker)) Error(where + ": speaker is not a participant");
                    if(node.DialogueParts!=null){
                        if(node.Kind=="end"||node.DialogueParts.Count<1||node.DialogueParts.Count>64)Error(where+": invalid dialogue part count");
                        for(int part=0;part<node.DialogueParts.Count;part++){
                            var p=node.DialogueParts[part];if(p==null){Error(where+": null dialogue part");continue;}Strict(p,where+"/part/"+part);
                            if(string.IsNullOrWhiteSpace(p.Text)||p.Text.Length>4000||p.WaitDaysBefore<0||p.WaitDaysBefore>30||part==0&&p.WaitDaysBefore!=0)Error(where+": invalid dialogue part text/day boundary");
                            if(!string.IsNullOrEmpty(p.Speaker)&&p.Speaker!=PlayerSpeaker&&!scene.Participants.Contains(p.Speaker))Error(where+": dialogue speaker is not a participant");
                        }
                    }
                    if (!SafeMedia(node.Portrait, ".png") || !SafeMedia(node.Voice, ".wav")) Error(where + ": unsafe media path");
                    Effects(node.Effects, where);
                    var choices = Ids(node.Choices, c => c.Id, where + "/choices");
                    if (node.Kind == "line" && !nodes.Contains(node.Next)) Error(where + ": missing next node");
                    if (node.Kind == "end" && (!string.IsNullOrEmpty(node.Next) || choices.Count != 0)) Error(where + ": end cannot have outgoing edges");
                    if (node.Kind != "choice" && choices.Count != 0) Error(where + ": choices on non-choice node");
                    if (node.Kind == "choice" && (choices.Count < 1 || choices.Count > 3 || !string.IsNullOrEmpty(node.Next))) Error(where + ": needs 1..3 choices and no next");
                    foreach (var choice in node.Choices ?? new())
                    {
                        if (choice == null) continue;
                        if (string.IsNullOrWhiteSpace(choice.Text) || choice.Text.Length > 200 || !nodes.Contains(choice.Next)) Error(where + ": invalid choice text or target");
                        Conditions(choice.Requires, where + "/" + choice.Id);
                        Effects(choice.Effects, where + "/" + choice.Id);
                    }
                }
                if (!result.Valid) continue;
                var byId = scene.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
                var visited = new HashSet<string>();
                void Visit(string id)
                {
                    if (!visited.Add(id)) return;
                    var node = byId[id];
                    if (node.Kind == "line") Visit(node.Next);
                    if (node.Kind == "choice") foreach (var c in node.Choices) Visit(c.Next);
                }
                Visit(scene.Entry);
                if (visited.Count != nodes.Count) Error(scene.Id + ": unreachable nodes");
                // Every node must be able to reach an end, even in a choice loop.
                var canEnd = new HashSet<string>(scene.Nodes.Where(n => n.Kind == "end").Select(n => n.Id));
                bool changed;
                do
                {
                    changed = false;
                    foreach (var n in scene.Nodes)
                        if ((n.Kind == "line" && canEnd.Contains(n.Next)) || (n.Kind == "choice" && n.Choices.Any(c => canEnd.Contains(c.Next)))) changed |= canEnd.Add(n.Id);
                } while (changed);
                if (canEnd.Count != nodes.Count) Error(scene.Id + ": node has no path to an end");
            }
            return result;
        }
    }
}
