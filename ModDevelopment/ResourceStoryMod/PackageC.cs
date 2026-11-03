using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ResourceStoryMod.Story
{
    // Stage one deliberately covers 004-006 and 007/008 intake -> 009 only.
    public sealed partial class CampaignItemsState
    {
        public int Version {get;set;}=1;
        public int Stage {get;set;}=1;
        public Dictionary<string,CampaignOriginal> Originals {get;set;}=new();
        public Dictionary<string,CampaignWork> Jobs {get;set;}=new();
        public Dictionary<string,CampaignHistory> History {get;set;}=new();
        public HashSet<string> Receipts {get;set;}=new();
        public HashSet<string> LegacyScenes {get;set;}=new();
        public HashSet<string> CraftPractice {get;set;}=new();
        public string LegacyActiveScene {get;set;}
        public string PendingChoice {get;set;}
        public string PendingScene {get;set;}
        public string LastBlock {get;set;}
        public CampaignSleep Sleep {get;set;}
    }
    public sealed partial class CampaignOriginal
    {
        public string Role {get;set;}
        public int Entity {get;set;}
        public string Sku {get;set;}
        public string Snapshot {get;set;}
        public string Owner {get;set;}
        public string Custodian {get;set;}
        public string Location {get;set;}="custody";
        public string Condition {get;set;}="unknown";
        public string Source {get;set;}
        public int FirstObservedDay {get;set;}
        public int? KnownYears {get;set;}
        public string AgeSource {get;set;}
        public int ConfigRevision {get;set;}
    }
    public sealed class CampaignResource
    {
        public int Entity {get;set;}
        public string Sku {get;set;}
        public string Reference {get;set;}
        public string Snapshot {get;set;}
        public string Owner {get;set;}
        public bool Tool {get;set;}
        public string State {get;set;}="available";
        public bool Logical {get;set;}
    }
    public sealed class CampaignWorkStep
    {
        public string Id {get;set;}
        public string Name {get;set;}
        public string Station {get;set;}="electrobench";
        public int DurationMs {get;set;}
        public int EnergyE {get;set;}
        public Dictionary<string,int> Materials {get;set;}=new();
        public Dictionary<string,int> Tools {get;set;}=new();
        public List<int> Bound {get;set;}=new();
        public int ElapsedMs {get;set;}
        public int OwnMs {get;set;}
        public bool SkippedByProcurement {get;set;}
        public decimal SupportEnergyE {get;set;}
        public Dictionary<int,decimal> NativeEnergyE {get;set;}=new();
    }
    public sealed partial class CampaignWork
    {
        public string Id {get;set;}
        public string Name {get;set;}
        public string SpecHash {get;set;}
        public string HostRole {get;set;}
        public string SupplyActor {get;set;}
        public int RequestDay {get;set;}
        public int EarliestDay {get;set;}
        public bool OvernightAllowed {get;set;}
        public string Mode {get;set;}="unselected";
        public string Status {get;set;}="pending";
        public int Step {get;set;}
        public int Machine {get;set;}
        public int? OvernightDay {get;set;}
        public decimal SupportRemainingE {get;set;}
        public decimal SupportCapacityE {get;set;}
        public List<CampaignWorkStep> Steps {get;set;}=new();
        public List<CampaignResource> Resources {get;set;}=new();
        public bool Ready=>Step==Steps.Count;
    }
    public sealed class CampaignHistory
    {
        public string Id {get;set;}
        public int Entity {get;set;}
        public string Kind {get;set;}
        public string Source {get;set;}
        public string Template {get;set;}
        public string Visibility {get;set;}="operator";
        public int Day {get;set;}
        public int ConfigRevision {get;set;}
    }
    public sealed class CampaignSleep
    {
        public string Id {get;set;}
        public int BeforeDay {get;set;}
        public string Phase {get;set;}="prepared";
        public List<string> Jobs {get;set;}=new();
    }
    public sealed class CampaignDefinition
    {
        public string Role,Sku,Name,Actor,Profile,Brief;
        public bool Ordinary;
        public int Width,Height,Value;
    }
    public interface ICampaignItemAdapter
    {
        CampaignResource Create(string sku,string reference,string owner,bool tool);
        void ToCustody(CampaignOriginal original);
    }
    public static partial class PackageC
    {
        public const string SynthJob="c_job_synth_initial",SpinalJob="c_job_spinel_free";
        private static readonly CampaignDefinition[] EarlyDefinitions={
            new(){Role="c_synth",Sku="ms_gear_synth_damaged",Name="钻星61键旧合成器",Actor="ms_hotaru",Profile="diamond_synth",Width=10,Height=4,Value=4800,Brief="一台贴着钻星贴纸、因受潮亟待检修的 61 键旧合成器。"},
            new(){Role="c_pick",Sku="ms_story_diamond_pick",Name="钻星旧拨片",Width=1,Height=2,Value=240,Brief="一枚边缘留有长期弹奏磨损痕迹的钻星旧拨片。"},
            new(){Role="c_flyer",Sku="ms_story_handdrawn_flyer",Name="手绘演出传单",Width=2,Height=2,Value=600,Brief="一张完好保存下来的水濑高中时期手绘演出传单。"},
            new(){Role="c_spinel",Sku="ms_natsume_spinel_guitar",Name="红尖晶石旧吉他",Actor="ms_natsume",Profile="spinel_guitar",Width=3,Height=7,Value=5800,Brief="一把留有鲜明扫拨痕迹的赤尖晶石电吉他，寄托着小枣对音乐的执念。"},
            new(){Role="c_whale",Sku="ms_nagi_whale_guitar",Name="沧溟蓝鲸旧吉他",Actor="ms_nagi",Profile="whale_guitar",Width=3,Height=7,Value=6200,Brief="一把琴颈留有深深握痕的沧溟蓝鲸旧吉他；确切年限未知。"},
            new(){Role="c_crew_pass",Sku="ms_story_crew_pass",Name="麻里奈的工作人员通行证",Width=2,Height=2,Value=0,Brief="用于已获授权的舞台工作人员通行。"},
            new(){Role="",Sku="ms_item_standard_strings",Name="普通吉他套弦",Actor="ms_natsume",Profile="spinel_guitar",Width=1,Height=2,Value=180,Brief="一套规格明确的普通吉他弦。"}
        };
        public static CampaignDefinition[] Definitions=>EarlyDefinitions.Concat(LaterDefinitions()).ToArray();
        private static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        public static CampaignOriginal Ensure(StoryState s,string role,int day,string owner,string custodian,ICampaignItemAdapter adapter,string source)
        {
            var c=s.CampaignItems;if(c.Originals.TryGetValue(role,out var prior))return prior;
            var def=Definitions.Single(x=>x.Role==role);
            var entity=adapter?.Create(def.Sku,def.Sku,owner,false)??throw new InvalidOperationException("剧情原件工厂未接入");
            Need(entity.Entity>0 && !string.IsNullOrWhiteSpace(entity.Snapshot),"原件快照缺失");
            var item=new CampaignOriginal{Role=role,Entity=entity.Entity,Sku=def.Sku,Snapshot=entity.Snapshot,Owner=owner,Custodian=custodian,FirstObservedDay=day,Source=source,Condition=role=="c_synth"?"damp":role=="c_spinel"?"used":"unknown"};
            c.Originals.Add(role,item);History(s,item,"observed",source,day,"observed");return item;
        }
        public static void Initialize(StoryState s,int day,ICampaignItemAdapter adapter)
        {
            s.CampaignItems=new CampaignItemsState{LegacyScenes=new(s.CompletedDays.Keys),LegacyActiveScene=s.Active?.SceneId};
            if(s.CompletedDays.ContainsKey("ms_scene_004") && !s.CompletedDays.ContainsKey("ms_scene_005") && s.Active?.SceneId!="ms_scene_005") {
                Ensure(s,"c_synth",day,"party:band","player",adapter,"legacy_intake_future_repair");Request(s,SynthJob,day,adapter);
            }
        }
        public static string Admission(StoryState s,string scene,int day)
        {
            var c=s.CampaignItems;if(c==null || c.LegacyActiveScene==scene)return null;
            if(scene=="ms_scene_005" && !c.LegacyScenes.Contains(scene)) {
                if(!c.Jobs.TryGetValue(SynthJob,out var job) || !job.Ready)return "合成器初修尚未完成；请在剧情委托中办理。";
                if(day<job.EarliestDay)return "初修已完成，最早次日安排试音。";
                if(!c.Originals.ContainsKey("c_synth"))return "缺少初修原件记录。";
            }
            return ReturnAdmission(s,scene,day)??ActOneAdmission(s,scene,day)??ActTwoAdmission(s,scene,day)??DiaryAdmission(s,scene,day)??(c.Stage>=2?FullAdmission(s,scene,day):null);
        }
        public static bool Enter(StoryState s,string scene,string node,int day,string choice,ICampaignItemAdapter adapter)
        {
            var c=s.CampaignItems;if(c==null || c.LegacyActiveScene==scene)return true;
            if(c.PendingChoice!=null && c.PendingScene==scene && choice!=null && c.PendingChoice!=choice){c.LastBlock="先完成已经受理的现场维修，再继续原选择。";return false;}
            if(scene=="ms_scene_004" && node=="line_001")foreach(string role in new[]{"c_synth","c_pick","c_flyer"})Ensure(s,role,day,"player","player",adapter,"ms_scene_004.intake");
            if(scene=="ms_scene_004" && (node=="line_005a" || node=="line_005b")) {
                foreach(string role in new[]{"c_synth","c_pick","c_flyer"}) {
                    var original=Ensure(s,role,day,"player","player",adapter,"ms_scene_004.intake");adapter.ToCustody(original);original.Owner="party:band";original.Custodian=role=="c_synth"?"player":"ms_mahoro";original.Location=role=="c_synth"?"custody":"member";
                }
                Request(s,SynthJob,day,adapter);c.Receipts.Add("004_relics_narrative_no_cash");
            }
            if(scene=="ms_scene_005" && node=="line_002" && c.Originals.TryGetValue("c_synth",out var synth))History(s,synth,"identity",scene+"."+node,day,"teacher_recognized");
            if(scene=="ms_scene_006" && node=="line_008") {
                if(c.Originals.TryGetValue("c_synth",out var original)){adapter.ToCustody(original);original.Custodian="ms_hotaru";original.Location="member";History(s,original,"custody",scene+"."+node,day,"hotaru_use");}
                Ensure(s,"c_crew_pass",day,"player","player",adapter,"ms_scene_006.crew_authorization");
            }
            if(scene=="ms_scene_007" && node=="line_001")Ensure(s,"c_spinel",day,"ms_natsume","ms_natsume",adapter,"ms_scene_007.member_original").Location="member";
            if(scene=="ms_scene_008" && node=="line_001")Ensure(s,"c_whale",day,"ms_nagi","ms_nagi",adapter,"ms_scene_008.member_original").Location="member";
            if(scene=="ms_scene_009" && node=="line_001" && !c.Jobs.ContainsKey(SpinalJob))Ensure(s,"c_spinel",day,"ms_natsume","ms_natsume",adapter,"ms_scene_009.rain_return").Condition="wet_broken_string";
            if(scene=="ms_scene_009" && (node=="line_006a" || node=="line_006b")) {
                var original=Ensure(s,"c_spinel",day,"ms_natsume","ms_natsume",adapter,"ms_scene_009.intake");
                if(!c.Jobs.ContainsKey(SpinalJob)){adapter.ToCustody(original);original.Custodian="player";original.Location="custody";Request(s,SpinalJob,day,adapter);}
                if(c.ActOne!=null)CompleteActOneCare(s,day,choice);
                if(!c.Jobs[SpinalJob].Ready){c.PendingChoice=choice;c.PendingScene=scene;c.LastBlock="已受理免费修理换弦。完成工序后，再继续这次选择。";return false;}
                adapter.ToCustody(original);original.Custodian="ms_natsume";original.Location="member";c.Receipts.Add("009_free_return_no_pawn_cash");
            }
            if(c.Stage>=2 && !FullEnter(s,scene,node,day,choice,adapter))return false;
            c.PendingChoice=null;c.PendingScene=null;c.LastBlock=null;return true;
        }
        public static void SceneEnded(StoryState s,string scene,int day,ICampaignItemAdapter adapter)
        {
            if(s.CampaignItems==null)return;
            if(s.CampaignItems.Stage>=2)FullSceneEnded(s,scene,day,adapter);
            if(s.CampaignItems.LegacyActiveScene==scene){s.CampaignItems.LegacyActiveScene=null;
                if(scene=="ms_scene_004" && !s.CampaignItems.Jobs.ContainsKey(SynthJob)){Ensure(s,"c_synth",day,"party:band","player",adapter,"legacy_active_future_repair");Request(s,SynthJob,day,adapter);}
            }
        }
        private static CampaignWorkStep Step(string id,string name,int sec,int energy,string materials,string tools,string station="electrobench")=>new(){Id=id,Name=name,DurationMs=sec*1000,EnergyE=energy,Materials=PackageBCatalog.Quantities(materials),Tools=PackageBCatalog.Quantities(tools),Station=station};
        public static CampaignWork Request(StoryState s,string id,int day,ICampaignItemAdapter adapter)
        {
            var c=s.CampaignItems;if(c.Jobs.TryGetValue(id,out var prior))return prior;
            Need(id==SynthJob || id==SpinalJob,"首段尚不支持此剧情工单");
            var job=new CampaignWork{Id=id,HostRole=id==SynthJob?"c_synth":"c_spinel",SupplyActor=id==SynthJob?"ms_mahoro":"ms_natsume",Name=id==SynthJob?"钻星合成器初修":"红尖晶石免费修理换弦",RequestDay=day,EarliestDay=day+(id==SynthJob?1:0),OvernightAllowed=id==SynthJob};
            job.Steps=id==SynthJob?new(){Step("clean","清洁接点",30,4,"ms_supply_contact_cleaner*1","ref_screwdriver*1"),Step("capacitor","更换失效电容",60,10,"ms_supply_solder_pack*1;ms_part_capacitor*1","ref_screwdriver*1;ref_welder*1"),Step("test","基础上电验收",30,6,"","","counter")}:new(){Step("clean","干燥与接点清理",30,4,"ms_supply_contact_cleaner*1","ref_screwdriver*1"),Step("strings","换标准套弦",30,0,"ms_item_standard_strings*1","ref_wirecutter*1","counter")};
            job.SupportCapacityE=job.SupportRemainingE=job.Steps.Sum(x=>x.EnergyE);
            job.SpecHash=Signature(job);
            foreach(var requirement in job.Steps.SelectMany(x=>x.Materials).GroupBy(x=>x.Key))for(int n=0;n<requirement.Sum(x=>x.Value);n++)job.Resources.Add(adapter.Create(NativeReference(requirement.Key),requirement.Key,job.SupplyActor,false));
            foreach(var requirement in job.Steps.SelectMany(x=>x.Tools).GroupBy(x=>x.Key))for(int n=0;n<requirement.Max(x=>x.Value);n++)job.Resources.Add(adapter.Create(NativeReference(requirement.Key),requirement.Key,job.SupplyActor,true));
            c.Jobs.Add(id,job);return job;
        }
        public static string NativeReference(string reference)=>reference switch{"ref_screwdriver"=>"screwdriver","ref_welder"=>"welder","ref_wirecutter"=>"wire_cutter",_=>reference};
        private static string Signature(CampaignWork j)
        {
            var frozen=new{j.Id,j.HostRole,j.SupplyActor,j.RequestDay,j.EarliestDay,j.OvernightAllowed,Steps=j.Steps.Select(x=>new{x.Id,x.Station,x.DurationMs,x.EnergyE,Materials=x.Materials.OrderBy(p=>p.Key,StringComparer.Ordinal).ToArray(),Tools=x.Tools.OrderBy(p=>p.Key,StringComparer.Ordinal).ToArray()}).ToArray()};
            return RuntimeCompat.Hex(RuntimeCompat.Sha256(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(frozen,StoryContent.Json)));
        }
        public static void BindSupport(CampaignWork job)
        {
            if(job.Ready)return;var step=job.Steps[job.Step];if(step.Bound.Count>0)return;
            var picked=new List<CampaignResource>();
            foreach(var r in step.Materials.Select(x=>(x.Key,x.Value,Tool:false)).Concat(step.Tools.Select(x=>(x.Key,x.Value,Tool:true)))) {
                var candidates=job.Resources.Where(x=>x.Reference==r.Key && x.Tool==r.Tool && x.State=="available" && x.Owner==job.SupplyActor && !picked.Contains(x)).Take(r.Value).ToArray();
                Need(candidates.Length==r.Value,"委托方供料不完整，保留原进度");picked.AddRange(candidates);
            }
            foreach(var r in picked){r.State="leased";step.Bound.Add(r.Entity);}
        }
        public static void SelectMode(StoryState s,string id,string mode,int day)
        {
            var j=s.CampaignItems.Jobs[id];Need(!j.Ready,"工单已经完成");Need(new[]{"support","own","overnight","paused"}.Contains(mode),"未知办理方式");
            Need(mode!="overnight" || j.OvernightAllowed,"现场维修不能改成过夜工单");
            j.Mode=mode;j.Status=mode=="paused"?"paused":"running";if(mode!="own")j.Machine=0;
            j.OvernightDay=mode=="overnight"?day:null;
            if(mode=="support" || mode=="overnight")BindSupport(j);
        }
        public static bool Progress(StoryState s,string id,int milliseconds,int day,bool own,int powerEntity=0)
        {
            Need(milliseconds>0 && milliseconds<=60000,"时间片必须为1—60000毫秒");var c=s.CampaignItems;var j=c.Jobs[id];if(j.Ready || j.Status!="running")return false;
            Need(own?j.Mode=="own":j.Mode=="support" || j.Mode=="overnight","办理方式与时间推进不符");
            var step=j.Steps[j.Step];Need(step.Bound.Count>0 || step.Materials.Count+step.Tools.Count==0,"尚未绑定真实材料与工具");
            int delta=Math.Min(milliseconds,step.DurationMs-step.ElapsedMs);decimal energy=(decimal)step.EnergyE*(step.ElapsedMs+delta)/step.DurationMs-(decimal)step.EnergyE*step.ElapsedMs/step.DurationMs;
            decimal supportAfter=(decimal)step.EnergyE*(step.ElapsedMs+delta-step.OwnMs-(own?delta:0))/step.DurationMs;
            decimal totalSupportAfter=j.Steps.Where(x=>!ReferenceEquals(x,step)).Sum(x=>x.SupportEnergyE)+supportAfter;
            if(!own)Need(j.SupportCapacityE>=totalSupportAfter,"委托方电量不足；未虚报完成");
            else Need(step.EnergyE==0 || powerEntity>0,"自有工位需要实际电源");
            foreach(int entity in step.Bound){var r=j.Resources.Single(x=>x.Entity==entity);Need(r.State=="leased" || (!r.Tool && r.State=="consumed"),"工序资源租约失效");if(!r.Tool)r.State="consumed";}
            step.ElapsedMs+=delta;
            if(own){step.OwnMs+=delta;if(energy>0)step.NativeEnergyE[powerEntity]=step.NativeEnergyE.GetValueOrDefault(powerEntity)+energy;}
            step.SupportEnergyE=supportAfter;
            j.SupportRemainingE=j.SupportCapacityE-j.Steps.Sum(x=>x.SupportEnergyE);
            if(step.ElapsedMs==step.DurationMs) {
                foreach(int entity in step.Bound){var r=j.Resources.Single(x=>x.Entity==entity);if(r.Tool)r.State="available";}
                var original=c.Originals[j.HostRole];original.ConfigRevision++;
                if(j.Id==SynthJob)original.Condition=step.Id=="clean"?"cleaned":step.Id=="capacitor"?"circuit_repaired":"serviced";
                else if(j.Id==SpinalJob)original.Condition=step.Id=="clean"?"cleaned_broken_string":"serviced";
                else CompleteFullStep(s,j,step,day);
                if(step.OwnMs==step.DurationMs)c.CraftPractice.Add(id+"."+step.Id);
                j.Step++;j.Machine=0;
                if(j.Ready){j.Status="ready";j.Mode="completed";foreach(var r in j.Resources.Where(x=>x.Tool && x.Owner=="player" && x.State=="available"))r.State="pending_return";History(s,original,j.Id==SynthJob || j.Id==SpinalJob?"repair":"service",id,day,id==SynthJob?"synth_initial_repaired":id==SpinalJob?"spinel_free_repair":FullJobTemplate(j));c.Receipts.Add(id+".completed");}
                else if(own){j.Status="pending";j.Mode="unselected";}
                else BindSupport(j);
            }
            return true;
        }
        public static void PrepareSleep(StoryState s,int day)
        {
            var c=s.CampaignItems;var jobs=c.Jobs.Values.Where(j=>!j.Ready && j.Mode=="overnight" && j.OvernightDay<=day).Select(j=>j.Id).OrderBy(x=>x,StringComparer.Ordinal).ToList();
            if(jobs.Count==0)return;
            c.Sleep=new CampaignSleep{Id="sleep_"+day,BeforeDay=day,Jobs=jobs};
        }
        public static bool OvernightDue(StoryState s,int day)=>s.CampaignItems?.Sleep is {Phase:"prepared"} sleep && day>sleep.BeforeDay;
        public static void SettleOvernight(StoryState s,int day)
        {
            if(!OvernightDue(s,day))return;var c=s.CampaignItems;var sleep=c.Sleep;
            foreach(string id in sleep.Jobs){var j=c.Jobs[id];if(j.Mode!="overnight" || j.Ready)continue;BindSupport(j);
                while(!j.Ready){var step=j.Steps[j.Step];decimal due=jobEnergy(j);if(j.SupportRemainingE<due){j.Status="paused";j.Mode="paused";c.LastBlock="委托电量不足，保留夜间未完成工序。";break;}Progress(s,id,Math.Min(60000,step.DurationMs-step.ElapsedMs),day,false);}
            }
            sleep.Phase="settled";c.Receipts.Add(sleep.Id+".settled");
        }
        private static decimal jobEnergy(CampaignWork j)=>j.Steps.Skip(j.Step).Sum(x=>(decimal)x.EnergyE*(x.DurationMs-x.ElapsedMs)/x.DurationMs);
        public static void History(StoryState s,CampaignOriginal item,string kind,string source,int day,string template)
        {
            string id=s.RunId+"/"+item.Entity+"/"+kind+"/"+source;
            if(!s.CampaignItems.History.ContainsKey(id))s.CampaignItems.History.Add(id,new CampaignHistory{Id=id,Entity=item.Entity,Kind=kind,Source=source,Day=day,Template=template,ConfigRevision=item.ConfigRevision});
        }
        public static string Description(StoryState s,string role,bool details=false,bool authorized=true)
        {
            var item=s.CampaignItems.Originals[role];var def=Definitions.First(x=>x.Role==role || x.Sku==item.Sku);
            if(s.CampaignItems.Stage>=2)return FullDescription(s,item,def,details,authorized);
            var events=s.CampaignItems.History.Values.Where(x=>x.Entity==item.Entity).OrderBy(x=>x.Day).ThenBy(x=>x.Id,StringComparer.Ordinal).ToArray();
            string Render(CampaignHistory h)=>h.Template switch{"teacher_recognized"=>"小萤一眼认出了奏多姐生前常弹的这台旧合成器。","synth_initial_repaired"=>"经过店里的初次整备，它的电源指示灯重新亮了起来。","spinel_free_repair"=>"它在这家乐器店里完成了检修，并换上了一套新琴弦。","hotaru_use"=>"由小萤按乐队约定保管并使用。",_=>""};
            string brief=item.Condition!="damp" && role=="c_synth"?"一台贴着钻星贴纸的 61 键旧合成器，琴键下方留有熟悉的浅痕。":def.Brief;
            var known=events.Select(Render).Where(x=>x.Length>0).Distinct().ToArray();
            string condition=item.Condition switch{"damp"=>"受潮待修","cleaned"=>"已清洁，线路待修","circuit_repaired"=>"线路已修，待上电验收","serviced"=>"已完成整备","used"=>"已有使用痕迹","wet_broken_string"=>"受潮并有断弦","cleaned_broken_string"=>"已清理，待换弦",_=>"尚未详细检查"};
            return brief+(known.Length==0?"":"\n"+string.Join("\n",details?known:known.TakeLast(2)))+(details?"\n确切使用年限："+(item.KnownYears.HasValue?item.KnownYears+"年（"+item.AgeSource+"）":"未知")+"。\n首次登记：第"+item.FirstObservedDay+"天；当前状态："+condition+"。":"");
        }
        public static void Validate(CampaignItemsState c)
        {
            if(c==null)return;
            if(c.Version!=1 || c.Stage<1 || c.Stage>2 || c.Originals==null || c.Jobs==null || c.History==null || c.Receipts==null || c.CraftPractice==null || c.LegacyScenes==null)throw new InvalidDataException("剧情原件版本或结构不受支持，保留原数据");
            var canonical=new HashSet<int>();
            foreach(var pair in c.Originals){var r=pair.Value;if(r.Role!=pair.Key || !Definitions.Any(x=>x.Role==r.Role && x.Sku==r.Sku) || r.Entity<=0 || !canonical.Add(r.Entity) || string.IsNullOrWhiteSpace(r.Snapshot) || !ValidLocation(r.Location) || r.ConfigRevision<0 || r.KnownYears<0 || (r.KnownYears.HasValue && string.IsNullOrWhiteSpace(r.AgeSource)))throw new InvalidDataException("剧情原件身份/位置记录冲突");}
            foreach(var pair in c.Jobs){var j=pair.Value;if(j.Id!=pair.Key || !c.Originals.ContainsKey(j.HostRole) || j.Step<0 || j.Step>j.Steps.Count || j.Steps.Count==0 || j.SupportRemainingE<0 || j.SupportCapacityE<0 || j.SupportRemainingE!=j.SupportCapacityE-j.Steps.Sum(x=>x.SupportEnergyE) || j.Ready!=(j.Status=="ready"))throw new InvalidDataException("剧情工单状态不完整");
                if(j.SpecHash!=Signature(j))throw new InvalidDataException("剧情工序的冻结规格发生变化");
                foreach(var r in j.Resources)if((r.Logical?r.Entity>=0:r.Entity<=0) || !canonical.Add(r.Entity) || string.IsNullOrEmpty(r.Snapshot))throw new InvalidDataException("剧情资源实体重复或快照缺失");
                for(int i=0;i<j.Steps.Count;i++){var step=j.Steps[i];if(step.ElapsedMs<0 || step.ElapsedMs>step.DurationMs || step.DurationMs<=0 || step.EnergyE<0 || step.OwnMs<0 || step.OwnMs>step.ElapsedMs || step.SupportEnergyE<0 || step.Bound.Count!=step.Bound.Distinct().Count() || step.Bound.Any(x=>!j.Resources.Any(r=>r.Entity==x)) || (i<j.Step && step.ElapsedMs!=step.DurationMs && !step.SkippedByProcurement) || (step.SkippedByProcurement && (step.Id!="wind" || step.ElapsedMs!=0 || step.Bound.Count!=0)))throw new InvalidDataException("剧情工序进度或资源引用错误");}
            }
            foreach(var h in c.History.Values)if(!c.Originals.Values.Any(x=>x.Entity==h.Entity))throw new InvalidDataException("履历缺少对应原件");
            if(c.Stage>=2)ValidateFull(c);
            ValidateNative012(c);
            ValidateOpening(c);
            ValidateActOne(c);
            ValidateActTwo(c);
            ValidateDiary(c);ValidateReturn(c);
        }
    }
}
