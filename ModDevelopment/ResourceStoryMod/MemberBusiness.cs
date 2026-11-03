using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ResourceStoryMod.Story
{
    public sealed class MusicProduct
    {
        public string Id, Name, Actor, Profile, Slot, NativeType;
        public int Price, Willingness, Width, Height, UnlockScene;
        public string[] Zones;
        public string[] RetailZones => PackageBCatalog.Item(Id) is { } row?PackageBCatalog.Split(row["ordinary_retail_zones"]):Id=="ms_gear_drum_practice_pad" ? new[]{"lower","middle"} : Id=="ms_gear_headphone_amp" ? new[]{"middle"} : new[]{"middle","upper"};
        public bool IsGear => Slot == null;
        public string MemberTag => "ms.member." + Actor.Substring(3);
        public string InstrumentTag => "ms.instrument." + Profile;
    }
    public static class MusicCatalog
    {
        public const string ProtectedTag = "ms.unique.protected";
        public static readonly MusicProduct[] Products = new MusicProduct[] {
            new() { Id="ms_module_quiet_power",Name="茉幌·监听静音供电件",Actor="ms_mahoro",Profile="monitor_speaker",Slot="power",NativeType="MODULE",Price=520,Willingness=650,Width=2,Height=2,UnlockScene=10,Zones=new[]{"upper"} },
            new() { Id="ms_part_stabilizer_pickup",Name="凪·蓝鲸稳压拾音件",Actor="ms_nagi",Profile="whale_guitar",Slot="signal",NativeType="MODULE",Price=780,Willingness=900,Width=2,Height=2,UnlockScene=12,Zones=new[]{"upper"} },
            new() { Id="ms_module_tone_filter",Name="枣·过载音色滤波件",Actor="ms_natsume",Profile="custom_overdrive",Slot="signal",NativeType="MODULE",Price=680,Willingness=800,Width=2,Height=2,UnlockScene=9,Zones=new[]{"upper"} },
            new() { Id="ms_gear_drum_practice_pad",Name="朋花·低噪练习鼓垫",Actor="ms_houka",Profile="practice_drum_pad",NativeType="HOUSEHOLD_GOOD",Price=900,Willingness=1050,Width=4,Height=4,UnlockScene=10,Zones=new[]{"middle","upper"} },
            new() { Id="ms_gear_headphone_amp",Name="萤·便携耳放",Actor="ms_hotaru",Profile="headphone_amp",NativeType="HOUSEHOLD_GOOD",Price=1100,Willingness=1250,Width=2,Height=3,UnlockScene=15,Zones=new[]{"middle","upper"} },
            new() { Id="ms_module_quick_patch",Name="凪·商业琴快接换场件",Actor="ms_nagi",Profile="commercial_guitar",Slot="interface",NativeType="MODULE",Price=420,Willingness=550,Width=2,Height=2,UnlockScene=12,Zones=new[]{"upper"} },
            new() { Id="ms_part_stabilizer_pickup_commercial",Name="凪·商业琴稳压拾音件",Actor="ms_nagi",Profile="commercial_guitar",Slot="signal",NativeType="MODULE",Price=780,Willingness=900,Width=2,Height=2,UnlockScene=12,Zones=new[]{"upper"} },
            new() { Id="ms_gear_commercial_guitar",Name="凪·商业电吉他",Actor="ms_nagi",Profile="commercial_guitar",NativeType="HOUSEHOLD_GOOD",Price=3200,Willingness=4000,Width=3,Height=7,UnlockScene=12,Zones=new[]{"upper"} }
        }.Concat(PackageBCatalog.ExtraMusic()).Concat(PackageC.RetailProducts()).ToArray();
        public static MusicProduct Find(string id) => Products.FirstOrDefault(p => p.Id == id);
        public static bool Met(StoryPackage package, StoryState state, string actor) => package.Scenes.Any(s => state.CompletedDays.ContainsKey(s.Id) && s.Participants.Contains(actor));
        public static bool Unlocked(StoryState state, MusicProduct product) => state.CompletedDays.ContainsKey("ms_scene_" + product.UnlockScene.ToString("000"));
    }
    public sealed class MemberAppointment
    {
        public int Day { get; set; }
        public string Status { get; set; } = "pending";
    }
    public sealed class MemberNeed
    {
        public string Actor { get; set; }
        public string ItemId { get; set; }
        public int FulfilledEntity { get; set; }
    }
    public sealed class MusicComponent
    {
        public int EntityId { get; set; }
        public string ItemId { get; set; }
        public string Snapshot { get; set; }
        public string Owner { get; set; }
        public string Location { get; set; } = "loose";
        public int Instrument { get; set; }
        public string Slot { get; set; }
    }
    public sealed class MusicInstrument
    {
        public string InstanceId { get; set; }
        public int EntityId { get; set; }
        public string ItemId { get; set; }
        public string Owner { get; set; } = "player";
        public string Custody { get; set; } = "player";
        public string MemberTag { get; set; }
        public string InstrumentTag { get; set; }
        public string Condition { get; set; } = "native_snapshot";
        public int Revision { get; set; }
        public Dictionary<string,int> Slots { get; set; } = new();
        public Dictionary<string,int> Stats { get; set; } = new();
        public bool DefaultsRegistered { get; set; }
    }
    public sealed class GearJob
    {
        public int Instrument { get; set; }
        public int Component { get; set; }
        public string Slot { get; set; }
    }
    public sealed class GearReturn
    {
        public int Entity {get;set;}
        public string Actor {get;set;}
        public int Fee {get;set;}
    }
    public sealed class MemberBusinessState
    {
        public int Version { get; set; } = 1;
        public Dictionary<string,MemberNeed> Needs { get; set; } = new();
        public Dictionary<string,MemberAppointment> Appointments { get; set; } = new();
        public Dictionary<int,string> NaturalRolls { get; set; } = new();
        public HashSet<string> Arrivals { get; set; } = new();
        public HashSet<string> Supplied { get; set; } = new();
        public Dictionary<int,MusicInstrument> Instruments { get; set; } = new();
        public Dictionary<int,MusicComponent> Components { get; set; } = new();
        public Dictionary<string,GearJob> Jobs { get; set; } = new();
        public Dictionary<string,GearReturn> Returns {get;set;}=new();
        public int NaturalChancePercent { get; set; } = 35;
    }
    public static class MemberBusiness
    {
        public static string ArrivalKey(int day,string actor) => day + ":" + actor;
        public static bool Refresh(MemberBusinessState b, StoryState state)
        {
            bool changed=false;
            foreach(var p in MusicCatalog.Products.Take(5))
                if(MusicCatalog.Unlocked(state,p) && !b.Needs.ContainsKey(p.Id)) { b.Needs.Add(p.Id,new MemberNeed { Actor=p.Actor,ItemId=p.Id }); changed=true; }
            return changed;
        }
        public static bool Book(MemberBusinessState b,string actor,int day,bool known,bool rebook=false,StoryState context=null)
        {
            if(!known || !ValidActor(actor) || day<0) throw new InvalidOperationException("尚未获得该成员的联系方式");
            if(context!=null && !PackageC.CanContact(context,actor,true))throw new InvalidOperationException("成员目前无法预约，已有委托与余额继续保留");
            if(b.Appointments.TryGetValue(actor,out var a) && a.Status=="pending" && !rebook) return false;
            b.Appointments[actor]=new MemberAppointment { Day=checked(day+1) };if(context!=null)NormalVisits.Book(context,"member",actor,day+1);return true;
        }
        public static bool Cancel(MemberBusinessState b,string actor) => b.Appointments.Remove(actor);
        public static bool Expire(MemberBusinessState b,int day)
        {
            bool changed=false;
            foreach(var a in b.Appointments.Values) if(a.Status=="pending" && a.Day<day) {a.Status="missed";changed=true;}
            return changed;
        }
        // SHA256 is independent of Unity's/global Random state and stable across processes.
        public static string Roll(MemberBusinessState b,string run,int day,IEnumerable<string> candidates)
        {
            if(b.Appointments.Values.Any(a=>a.Day==day&&a.Status is "pending" or "visited")){b.NaturalRolls[day]="";return "";}
            if(b.NaturalRolls.TryGetValue(day,out var saved)) return saved;
            var eligible=candidates.Where(ValidActor).Distinct().OrderBy(x=>x,StringComparer.Ordinal)
                .Where(a=>!b.Arrivals.Contains(ArrivalKey(day,a)) && !(b.Appointments.TryGetValue(a,out var ap) && ap.Status=="pending" && ap.Day==day)).ToArray();
            byte[] bytes=RuntimeCompat.Sha256(Encoding.UTF8.GetBytes(run+"/millsage/natural/"+day));
            uint chance=Read32(bytes,0), selection=Read32(bytes,4);
            string actor=eligible.Length>0 && chance%100<b.NaturalChancePercent ? eligible[selection%(uint)eligible.Length] : "";
            b.NaturalRolls.Add(day,actor); return actor;
        }
        private static uint Read32(byte[] x,int i) => (uint)x[i]|((uint)x[i+1]<<8)|((uint)x[i+2]<<16)|((uint)x[i+3]<<24);
        public static bool Arrive(MemberBusinessState b,string actor,int day)
        {
            if(!ValidActor(actor)) return false;
            bool fresh=b.Arrivals.Add(ArrivalKey(day,actor));
            if(b.Appointments.TryGetValue(actor,out var a) && a.Status=="pending" && a.Day==day) a.Status="visited";
            return fresh;
        }
        public static IEnumerable<string> Due(MemberBusinessState b,int day,StoryState context=null)
        {
            foreach(var p in b.Appointments.OrderBy(p=>context?.NormalVisits?.Visits.GetValueOrDefault(NormalVisits.Key("member",p.Key,p.Value.Day))?.Sequence??long.MaxValue).ThenBy(p=>p.Key,StringComparer.Ordinal))
                if(p.Value.Status=="pending" && p.Value.Day==day && !b.Arrivals.Contains(ArrivalKey(day,p.Key)) && (context==null || PackageC.CanContact(context,p.Key,true))) yield return p.Key;
            if(!b.Appointments.Values.Any(a=>a.Day==day&&a.Status is "pending" or "visited")&&b.NaturalRolls.TryGetValue(day,out var natural) && natural!="" && !b.Arrivals.Contains(ArrivalKey(day,natural)) && (context==null || PackageC.CanContact(context,natural,false))) yield return natural;
        }
        public static bool ValidActor(string a) => new[]{"ms_mahoro","ms_nagi","ms_natsume","ms_houka","ms_hotaru"}.Contains(a);
        public static string ResidenceZone(string actor)=>actor is "ms_houka" or "ms_hotaru"?"upper":ValidActor(actor)?"middle":"lower";
        public static MusicInstrument Register(MemberBusinessState b,int entity,string itemId,string owner)
        {
            if(b.Instruments.TryGetValue(entity,out var saved)) {
                if(saved.ItemId!=itemId) throw new InvalidDataException("原件身份与型号不符"); return saved;
            }
            var p=MusicCatalog.Find(itemId);
            if(p==null || !p.IsGear) throw new InvalidOperationException("该物品不是已开放的乐器宿主");
            var gear=new MusicInstrument { EntityId=entity,InstanceId="gear_"+entity,ItemId=itemId,Owner=owner,Custody=owner,MemberTag=p.MemberTag,InstrumentTag=p.InstrumentTag,DefaultsRegistered=true };
            // Headphone-amp baseline includes its sealed factory power stage. It is not a free removable part.
            Recompute(gear); b.Instruments.Add(entity,gear); return gear;
        }
        public static bool Install(MemberBusinessState b,string jobId,int gearId,MusicComponent component)
        {
            if(!StoryContent.ValidId(jobId) || component==null) throw new InvalidDataException("改装事件无效");
            if(b.Jobs.TryGetValue(jobId,out var job)) {
                if(job.Instrument!=gearId || job.Component!=component.EntityId || job.Slot!=component.Slot) throw new InvalidDataException("工单号已用于其他改装");
                return false;
            }
            if(!b.Instruments.TryGetValue(gearId,out var g) || g.Custody!="player") throw new InvalidOperationException("乐器尚未交给玩家");
            var host=MusicCatalog.Find(g.ItemId); var part=MusicCatalog.Find(component.ItemId);
            // Package A opens this commercial host only; story-original work permissions remain closed.
            if(host?.Id!="ms_gear_commercial_guitar" || part?.Slot==null || part.Slot!=component.Slot || host.MemberTag!=part.MemberTag || host.InstrumentTag!=part.InstrumentTag)
                throw new InvalidOperationException("成员、具体乐器或插槽不匹配");
            if(g.Slots.TryGetValue(part.Slot,out int old)) {
                if(old==component.EntityId || b.Components[old].ItemId==component.ItemId) return false;
                b.Components[old].Location="loose"; b.Components[old].Instrument=0; b.Components[old].Slot=null;
            }
            if(b.Components.TryGetValue(component.EntityId,out var registered) && registered.Location=="installed") throw new InvalidOperationException("这个配件已装在乐器上");
            if(string.IsNullOrWhiteSpace(component.Snapshot) || component.Owner!=g.Owner) throw new InvalidOperationException("配件必须先归乐器物主所有");
            b.Components[component.EntityId]=component; component.Location="installed";component.Instrument=gearId;
            g.Slots[part.Slot]=component.EntityId; g.Revision=checked(g.Revision+1); Recompute(g);
            b.Jobs.Add(jobId,new GearJob {Instrument=gearId,Component=component.EntityId,Slot=part.Slot}); return true;
        }
        public static void Recompute(MusicInstrument g)
        {
            g.Stats = g.ItemId=="ms_gear_commercial_guitar" ? new() {{"noiseControl",60},{"stability",62},{"response",60},{"portability",65}} : new();
            if(g.Stats.Count==0) return;
            if(g.Slots.ContainsKey("signal")) { g.Stats["noiseControl"]+=10;g.Stats["stability"]+=8;g.Stats["portability"]-=2; }
            if(g.Slots.ContainsKey("interface")) g.Stats["portability"]+=8;
        }
        public static void Validate(MemberBusinessState b)
        {
            if(b==null || b.Version!=1 || b.NaturalChancePercent<0 || b.NaturalChancePercent>100 || b.Needs==null || b.Appointments==null || b.NaturalRolls==null || b.Arrivals==null || b.Supplied==null || b.Instruments==null || b.Components==null || b.Jobs==null || b.Returns==null) throw new InvalidDataException("成员事务存档无效");
            foreach(var p in b.Appointments) if(!ValidActor(p.Key) || p.Value==null || p.Value.Day<0 || !new[]{"pending","missed","visited","deferred_unavailable"}.Contains(p.Value.Status)) throw new InvalidDataException("预约记录无效");
            foreach(var p in b.NaturalRolls) if(p.Key<0 || (p.Value!="" && !ValidActor(p.Value))) throw new InvalidDataException("自然来访记录无效");
            foreach(var p in b.Instruments) {
                var g=p.Value;
                if(g==null || g.EntityId!=p.Key || g.Slots==null || g.Stats==null || g.Revision<0 || (g.Owner!="player" && g.Owner!="outside" && !ValidActor(g.Owner)) || (g.Custody!="player" && g.Custody!=g.Owner && g.Custody?.StartsWith("service:")!=true)) throw new InvalidDataException("乐器档案无效");
                foreach(var s in g.Slots) if(!b.Components.TryGetValue(s.Value,out var c) || c.Location!="installed" || c.Instrument!=g.EntityId || c.Slot!=s.Key || c.Owner!=g.Owner) throw new InvalidDataException("乐器配件关系不完整");
            }
            foreach(var p in b.Components) {
                var c=p.Value;
                if(c==null || c.EntityId!=p.Key || string.IsNullOrWhiteSpace(c.Snapshot) || !new[]{"loose","installed"}.Contains(c.Location)) throw new InvalidDataException("配件档案无效");
                if(c.Location=="installed" && (!b.Instruments.TryGetValue(c.Instrument,out var g) || !g.Slots.TryGetValue(c.Slot,out int e) || e!=p.Key)) throw new InvalidDataException("配件被重复占用");
            }
        }
    }
}
