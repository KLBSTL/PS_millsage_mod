using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ResourceStoryMod.Story
{
    public sealed class StoryCursor
    {
        public string SceneId { get; set; }
        public string NodeId { get; set; }
        public int StartedDay { get; set; }
    }
    public sealed class StoryState
    {
        public int Version { get; set; } = 1;
        public string PackageId { get; set; }
        public string RunId { get; set; }
        public long Revision { get; set; }
        public Dictionary<string, bool> Flags { get; set; } = new();
        public Dictionary<string, int> Counters { get; set; } = new();
        public Dictionary<string, int> CompletedDays { get; set; } = new();
        public HashSet<string> ProcessedEvents { get; set; } = new();
        public StoryCursor Active { get; set; }
        public MemberEconomyState Economy { get; set; }
        public WorkshopState Workshop { get; set; }
        public CampaignItemsState CampaignItems { get; set; }
        public MusicTradeState MusicTrade {get;set;}
        [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
        public BasicInspectionState Inspections {get;set;}
        [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
        public NormalVisitsState NormalVisits {get;set;}
        [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
        public StoryPageBookmark Presentation {get;set;}
    }
    public sealed class StoryPageBookmark
    {
        public string Scene {get;set;}
        public string Node {get;set;}
        public string ContentSHA256 {get;set;}
        public int Segment {get;set;}
        public int DialoguePart {get;set;}
        public int NotBeforeDay {get;set;}
        public StoryPageBookmark Copy()=>(StoryPageBookmark)MemberwiseClone();
    }
    public sealed class StoryFrame
    {
        public long Revision { get; init; }
        public StoryScene Scene { get; init; }
        public StoryNode Node { get; init; }
        public StoryActor Speaker { get; init; }
        public List<StoryChoice> Choices { get; init; }
        public string Text { get; init; }
    }
    public sealed class StoryEngine
    {
        public StoryPackage Package { get; }
        private StoryState state;
        public long Revision => state.Revision;
        public StoryPageBookmark PresentationBookmark=>state.Presentation?.Copy();
        // Reading progress is save metadata, not a gameplay revision or event.
        public void StorePresentation(StoryPageBookmark bookmark)
        {
            ValidateBookmark(bookmark,state.Active);state.Presentation=bookmark?.Copy();Changed?.Invoke();
        }
        private static void ValidateBookmark(StoryPageBookmark b,StoryCursor active)
        {
            if(b==null)return;
            if(active==null||b.Scene!=active.SceneId||b.Node!=active.NodeId||b.ContentSHA256?.Length!=64||!b.ContentSHA256.All(Uri.IsHexDigit)||b.Segment<0||b.Segment>16384||b.DialoguePart<0||b.DialoguePart>63||b.NotBeforeDay<0)throw new InvalidDataException("Invalid presentation bookmark; original save retained");
        }
        private MemberEconomyConfig economyConfig;
        public ICampaignItemAdapter CampaignAdapter { get; private set; }
        private CampaignFinanceText financeText;
        public void ConfigureCampaignText(CampaignFinanceText text){text?.Validate(Package);financeText=text;}
        public event Action Changed;
        public StoryEngine(StoryPackage package, string runId, string saved = null)
        {
            var check = StoryContent.Validate(package);
            if (!check.Valid) throw new InvalidDataException(string.Join("\n", check.Errors));
            if (string.IsNullOrWhiteSpace(runId)) throw new InvalidDataException("Run id is required");
            Package = package;
            state = saved == null ? new StoryState { PackageId = package.Id, RunId = runId } : JsonSerializer.Deserialize<StoryState>(saved, StoryContent.Json);
            if (state == null || state.Version != 1 || state.PackageId != package.Id || state.RunId != runId)
                throw new InvalidDataException("Story state version, package or run does not match");
            if (state.Flags == null || state.Counters == null || state.CompletedDays == null || state.ProcessedEvents == null || state.Revision < 0)
                throw new InvalidDataException("Incomplete story state");
            if (state.Active != null)
            {
                var scene = package.Scenes.Find(s => s.Id == state.Active.SceneId && s.Status == "ready");
                if (scene == null || !scene.Nodes.Any(n => n.Id == state.Active.NodeId)) throw new InvalidDataException("Saved cursor is missing from this content package");
            }
            PackageC.Validate(state.CampaignItems);
            ValidateBookmark(state.Presentation,state.Active);
            PackageD.Validate(state.MusicTrade);
            PackageDNext.ValidateState(state);
            BasicInspection.Validate(state.Inspections);
            NormalVisits.Validate(state.NormalVisits);
        }
        public string Save() => JsonSerializer.Serialize(state, StoryContent.Json);
        public void EditInspections(Action<StoryState> edit)=>Commit(next=>{next.Inspections??=new BasicInspectionState();edit(next);BasicInspection.Validate(next.Inspections);});
        public void EditNormalVisits(Action<StoryState> edit)=>Commit(next=>{next.NormalVisits??=new NormalVisitsState();edit(next);});
        public StoryState Snapshot() => JsonSerializer.Deserialize<StoryState>(Save(), StoryContent.Json);
        public bool EnableCampaignItems(int day, ICampaignItemAdapter adapter,int stage=1)
        {
            if(Package.Id!="millsage" || day<0 || adapter==null)throw new InvalidOperationException("剧情原件需要millsage包、有效日期及实体工厂");
            CampaignAdapter=adapter;
            if(state.CampaignItems!=null){PackageC.Validate(state.CampaignItems);if(stage>state.CampaignItems.Stage||Package.CampaignProfile==PackageC.ReturnProfile&&state.CampaignItems.Return==null){Commit(next=>{PackageC.Upgrade(next,Math.Max(stage,next.CampaignItems.Stage));PackageC.AttachReturn(next,Package.CampaignProfile,day);});return true;}return false;}
            Commit(next=>{PackageC.Initialize(next,day,adapter);PackageC.Upgrade(next,stage);PackageC.AttachReturn(next,Package.CampaignProfile,day);PackageC.Validate(next.CampaignItems);});return true;
        }
        public void EditCampaignItems(Action<StoryState> edit)
        {
            if(state.CampaignItems==null)throw new InvalidOperationException("剧情原件未启用");
            Commit(next=>{edit(next);PackageC.Validate(next.CampaignItems);if(next.Workshop!=null)Workshop.Validate(next.Workshop);});
        }
        public bool EnableMemberEconomy(MemberEconomyConfig config, int day)
        {
            config.Validate(Package);
            if (day < 0) throw new ArgumentOutOfRangeException(nameof(day));
            if (state.Economy != null) MemberEconomy.Validate(state.Economy, config, state.RunId);
            economyConfig = JsonSerializer.Deserialize<MemberEconomyConfig>(JsonSerializer.Serialize(config, StoryContent.Json), StoryContent.Json);
            if (state.Economy != null) return SettleMemberIncome(day);
            Commit(next => next.Economy = MemberEconomy.Create(config, next.RunId, day, next.CompletedDays.Keys));
            return true;
        }
        public bool SettleMemberIncome(int day)
        {
            if (day < 0) throw new ArgumentOutOfRangeException(nameof(day));
            if (economyConfig == null || state.Economy == null || !MemberEconomy.IncomeDue(state.Economy, day)) return false;
            Commit(next => MemberEconomy.SettleIncome(next.Economy, economyConfig, day));
            return true;
        }
        public bool CommitMemberTrade(string id, MemberOwnedItem item, bool memberBuys, int price)
        {
            if (economyConfig == null || state.Economy == null) throw new InvalidOperationException("Member economy is unavailable");
            var draft = Snapshot();
            if (!MemberEconomy.ApplyTrade(draft.Economy, id, item, memberBuys, price)) return false;
            Commit(next => next.Economy = draft.Economy);
            return true;
        }
        public bool EnableMemberBusiness()
        {
            if(state.Economy==null) return false;
            if(state.Economy.Business!=null) { MemberBusiness.Validate(state.Economy.Business); return false; }
            Commit(next=>next.Economy.Business=new MemberBusinessState()); return true;
        }
        public void EditMemberBusiness(Action<StoryState> edit)
        {
            if(state.Economy?.Business==null) throw new InvalidOperationException("成员事务未启用");
            Commit(next=> {edit(next); MemberBusiness.Validate(next.Economy.Business);});
        }
        public bool EnableWorkshop()
        {
            if(state.Workshop!=null){Workshop.Validate(state.Workshop);if(state.Workshop.Advanced!=null)return false;Commit(next=>PackageB.Migrate(next));return true;}
            Commit(next=>{next.Workshop=new WorkshopState();PackageB.Migrate(next);});return true;
        }
        public void EditWorkshop(Action<WorkshopState> edit)
        {
            if(state.Workshop==null)throw new InvalidOperationException("工坊未启用");
            Commit(next=>{edit(next.Workshop);Workshop.Validate(next.Workshop);});
        }
        public void EditPackageB(Action<StoryState> edit)
        {
            if(state.Workshop?.Advanced==null)throw new InvalidOperationException("包2未启用");
            Commit(next=>{edit(next);Workshop.Validate(next.Workshop);MemberBusiness.Validate(next.Economy.Business);});
        }
        public bool EnableMusicTrade()
        {
            if(state.MusicTrade!=null){PackageD.Validate(state.MusicTrade);return false;}
            if(state.Workshop?.Advanced==null || state.Economy?.Business==null)throw new InvalidOperationException("普通音乐经营需要现有工坊和物权数据");
            Commit(next=>next.MusicTrade=new MusicTradeState());return true;
        }
        public void EditMusicTrade(Action<StoryState> edit)
        {
            if(state.MusicTrade==null)throw new InvalidOperationException("普通音乐经营未启用");
            Commit(next=>{edit(next);Workshop.Validate(next.Workshop);MemberBusiness.Validate(next.Economy.Business);});
        }
        private bool Match(List<StoryCondition> conditions) => conditions.All(c => c.Kind switch
        {
            "flag" => state.Flags.GetValueOrDefault(c.Key) == c.Flag,
            "counter_at_least" => state.Counters.GetValueOrDefault(c.Key) >= c.Number,
            "completed" => state.CompletedDays.ContainsKey(c.Key) == c.Flag,
            _ => false
        });
        public List<StoryScene> Eligible(string context, int day)
        {
            if (!Package.Enabled || state.Active != null || day < 0) return new();
            return Package.Scenes.Where(s => s.Status == "ready" && s.Context == context && Match(s.Requires)
                && (!s.Once || !state.CompletedDays.ContainsKey(s.Id)) && PackageC.Admission(state,s.Id,day)==null
                && (!state.CompletedDays.TryGetValue(s.Id, out int last) || (long)day - last >= Math.Max(1, s.CooldownDays)))
                .OrderByDescending(s => s.Priority).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
        }
        public bool Start(string sceneId, string context, int day)
        {
            var scene = Eligible(context, day).Find(s => s.Id == sceneId);
            if (scene == null) return false;
            bool moved=false;
            Commit(next => {if(!PackageC.Enter(next,scene.Id,scene.Entry,day,null,CampaignAdapter))return;next.Active = new StoryCursor { SceneId = scene.Id, NodeId = scene.Entry, StartedDay = day };moved=true;});
            return moved;
        }
        public StoryFrame Frame()
        {
            if (state.Active == null) return null;
            var scene = Package.Scenes.Single(s => s.Id == state.Active.SceneId);
            var node = scene.Nodes.Single(n => n.Id == state.Active.NodeId);
            var parts=financeText?.ResolveParts(state,scene.Id,node)??node.DialogueParts;
            string text=parts==null?financeText?.Resolve(state,scene.Id,node)??node.Text:string.Join("\n",parts.Select(p=>(StoryContent.ResolveSpeaker(Package,p.Speaker)?.DisplayName??"旁白")+"："+p.Text));
            return new StoryFrame { Revision = state.Revision, Scene = scene, Node = node, Text=text,
                Speaker = StoryContent.ResolveSpeaker(Package,node.Speaker), Choices = node.Choices.Where(c => Match(c.Requires)).ToList() };
        }
        public List<StoryDialoguePart> DialogueParts(StoryFrame frame)=>financeText?.ResolveParts(state,frame.Scene.Id,frame.Node)??frame.Node.DialogueParts;
        public bool Advance(long revision, int day)
        {
            var frame = Frame();
            if (frame == null || frame.Revision != revision || frame.Node.Kind == "choice" || day < state.Active.StartedDay) return false;
            bool moved=false;
            Commit(next =>
            {
                if(frame.Node.Kind!="end" && !PackageC.Enter(next,frame.Scene.Id,frame.Node.Next,day,null,CampaignAdapter))return;
                if(frame.Node.Kind=="end" && !PackageC.BeforeEnd(next,frame.Scene.Id,day,CampaignAdapter))return;
                Effects(next, frame.Node.Effects);
                if (frame.Node.Kind == "end")
                {
                    if (economyConfig != null && next.Economy != null)
                    {
                        MemberEconomy.SettleIncome(next.Economy, economyConfig, day);
                        MemberEconomy.SettleLive(next.Economy, economyConfig, frame.Scene.Id);
                    }
                    next.CompletedDays[frame.Scene.Id] = day; next.Active = null;
                    PackageC.SceneEnded(next,frame.Scene.Id,day,CampaignAdapter);
                }
                else next.Active.NodeId = frame.Node.Next;
                moved=true;
            });
            return moved;
        }
        public bool Choose(string choiceId, long revision, int? day=null)
        {
            var frame = Frame();
            if (frame == null || frame.Revision != revision || frame.Node.Kind != "choice") return false;
            var choice = frame.Choices.Find(c => c.Id == choiceId);
            if (choice == null) return false;
            int effectiveDay=day??state.Active.StartedDay;
            if(effectiveDay<state.Active.StartedDay)return false;
            bool moved=false;
            Commit(next => {if(!PackageC.Enter(next,frame.Scene.Id,choice.Next,effectiveDay,choice.Id,CampaignAdapter))return;Effects(next, frame.Node.Effects); Effects(next, choice.Effects); next.Active.NodeId = choice.Next;moved=true;});
            return moved;
        }
        public bool ExternalEvent(string eventId, List<StoryEffect> effects)
        {
            if (!StoryContent.ValidId(eventId)) throw new ArgumentException("Invalid event id");
            if (state.ProcessedEvents.Contains(eventId)) return false;
            Commit(next => { Effects(next, effects); next.ProcessedEvents.Add(eventId); });
            return true;
        }
        private void Commit(Action<StoryState> update)
        {
            var next = Snapshot();
            update(next);
            if(next.Active==null||next.Active.SceneId!=state.Active?.SceneId||next.Active.NodeId!=state.Active?.NodeId)next.Presentation=null;
            PackageC.Validate(next.CampaignItems);
            PackageD.Validate(next.MusicTrade);
            PackageDNext.ValidateState(next);
            BasicInspection.Validate(next.Inspections);
            NormalVisits.Validate(next.NormalVisits);
            next.Revision = checked(next.Revision + 1);
            state = next;
            Changed?.Invoke();
        }
        private static void Effects(StoryState next, List<StoryEffect> effects)
        {
            if (effects == null) throw new InvalidDataException("Effects are required");
            foreach (var e in effects)
            {
                if (e == null || !StoryContent.ValidId(e.Key)) throw new InvalidDataException("Invalid effect key");
                switch (e.Kind)
                {
                    case "set_flag": next.Flags[e.Key] = e.Flag; break;
                    case "set_counter": next.Counters[e.Key] = e.Number; break;
                    case "add_counter": next.Counters[e.Key] = checked(next.Counters.GetValueOrDefault(e.Key) + e.Number); break;
                    default: throw new InvalidDataException("Unknown effect kind " + e.Kind);
                }
            }
        }
    }
}
