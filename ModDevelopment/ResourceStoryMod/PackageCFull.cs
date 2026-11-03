using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ResourceStoryMod.Story
{
    public sealed partial class CampaignItemsState
    {
        public int NextLogical {get;set;}=-1;
        public Dictionary<string,CampaignManifest> Manifests {get;set;}=new();
        public Dictionary<string,CampaignContact> Contacts {get;set;}=new();
        public Dictionary<string,CampaignFinanceContext> FinanceContexts {get;set;}=new();
        public Dictionary<string,CampaignUseRequest> UseRequests {get;set;}=new();
        public HashSet<string> BusinessLeads {get;set;}=new();
        public string DiaryMode {get;set;}
        public bool DiaryGranted {get;set;}
        public bool DiaryRevealed {get;set;}
        public bool Oct9Covered {get;set;}
        public string FundsState {get;set;}="unannounced";
        public string Ending {get;set;}
        public string EarlyGuitar {get;set;}="c_whale";
        public string StageGuitar {get;set;}="c_whale";
        public bool FinalPreparation {get;set;}
        public bool HiddenRevealed {get;set;}
        public int LastUseRequestDay {get;set;}=-100;
        public Dictionary<string,CampaignSelection> Selections {get;set;}=new();
        public Dictionary<string,CampaignProject> Projects {get;set;}=new();
        public string LegacyIntakeScene {get;set;}
    }
    public sealed partial class CampaignOriginal
    {
        public int CalibratedRevision {get;set;}=-1;
        public bool Visible {get;set;}=true;
        public bool ReadAuthorized {get;set;}
        public bool Borrowed {get;set;}
        public string Lender {get;set;}
        public string ReturnAgreement {get;set;}
        public int[] ExistingComponents {get;set;}=Array.Empty<int>();
        public string ExistingConfiguration {get;set;}
        public bool Released {get;set;}
        public Dictionary<string,CampaignAttachment> Attachments {get;set;}=new();
    }
    public sealed class CampaignAttachment
    {
        public int Entity {get;set;}
        public string Sku {get;set;}
        public string Snapshot {get;set;}
        public string SourceJob {get;set;}
        public string Slot {get;set;}
    }
    public sealed partial class CampaignWork
    {
        public int LaborReference {get;set;}
        public int Quote {get;set;}
        public string FeeState {get;set;}="free";
        public bool Delivered {get;set;}
        public string Version {get;set;}
        public Dictionary<string,string> TargetSignatures {get;set;}=new();
        public string TransferFrom {get;set;}
    }
    public sealed class CampaignManifest
    {
        public string Id {get;set;}
        public string Kind {get;set;}
        public int Day {get;set;}
        public bool Completed {get;set;}
        public int CompletedDay {get;set;}
        public Dictionary<string,CampaignUseEntry> Entries {get;set;}=new();
    }
    public sealed class CampaignUseEntry
    {
        public int Entity {get;set;}
        public string Actor {get;set;}
        public string Signature {get;set;}
        public int Revision {get;set;}
        public int[] Components {get;set;}=Array.Empty<int>();
    }
    public sealed class CampaignContact
    {
        public bool Natural {get;set;}=true;
        public bool Phone {get;set;}=true;
        public string Reason {get;set;}="正常联系";
    }
    public sealed class CampaignFinanceContext
    {
        public string Source {get;set;}
        public int Day {get;set;}
        public bool AnyMemberCanFund {get;set;}
        public bool GroupCanFund {get;set;}
        public string TextVariant {get;set;}
        public string ContentStatus {get;set;}="context_only_text_resource_separate";
    }
    public sealed class CampaignUseRequest
    {
        public string Id {get;set;}
        public string Actor {get;set;}
        public string Role {get;set;}
        public string Kind {get;set;}
        public string Source {get;set;}
        public string Status {get;set;}="offered";
        public int Day {get;set;}
        public string Job {get;set;}
    }
    public sealed class CampaignSelection
    {
        public int Entity {get;set;}
        public string Mode {get;set;}
    }
    public sealed class CampaignProject
    {
        public string Id {get;set;}
        public string Version {get;set;}
        public string Source {get;set;}
        public string Engineer {get;set;}="ms_mahoro";
        public int Medium {get;set;}
        public bool Submitted {get;set;}
        public bool HasAudio {get;set;}
        public Dictionary<string,int> Voices {get;set;}=new();
    }
    public interface ICampaignSourceAdapter
    {
        CampaignOriginal ResolveOriginal(StoryState state,string role,string actor,int day);
    }
    public static partial class PackageC
    {
        public const string BassStrings="c_job_strings_bass",GuitarStrings="c_job_strings_guitar",WhaleRepair="c_job_whale_repair",Hammer="c_job_synth_hammer",Mic="c_job_mic_calibrate",DecodeJob="c_job_decode",Prelim="c_job_prelim_master",Care="c_job_whale_care",FinalTune="c_job_final_tune",FinalMaster="c_job_final_master",Braid="c_job_keychain_braid";
        private static readonly string[] Five={"ms_mahoro","ms_nagi","ms_natsume","ms_houka","ms_hotaru"};
        private static CampaignDefinition Def(string role,string sku,string name,int w,int h,int price,string brief,string actor=null,string profile=null,bool ordinary=false)=>new(){Role=role,Sku=sku,Name=name,Width=w,Height=h,Value=price,Brief=brief,Actor=actor,Profile=profile,Ordinary=ordinary};
        private static IEnumerable<CampaignDefinition> LaterDefinitions()=>new[]{
            Def("c_bass","ms_mahoro_stage_bass","茉幌的五弦贝斯",4,8,4800,"一把随茉幌登台演出的五弦电贝斯，承载着稳稳托住全队的低音律动。","ms_mahoro","stage_bass"),
            Def("c_drums","ms_houka_stage_drum","朋花的舞台鼓组",8,4,6200,"一套按运输规格妥善收拢的舞台架子鼓组。","ms_houka","stage_drum"),
            Def("c_keybed_sketch","ms_story_keybed_sketch","键床尺寸草图",2,2,0,"一份朋花为旧合成器重锤改装留下的键床尺寸草图。"),
            Def("c_keychain","ms_kanata_keychain","小潘挂件",2,2,1800,"带有黄铜底座的‘微笑小潘’旧挂件；内部存储信息依物主授权范围处理。"),
            Def("c_diary_result","ms_story_diary_readout_card","授权读取结果卡",2,2,0,"来自本次实际空白卡的私人读取结果；只按已确认的方案展示。"),
            Def("c_prelim_master","ms_story_prelim_master_tape","初赛两声部母带",2,2,1800,"按当前编曲工程版本录制的《起死開戦》初赛提交母带。"),
            Def("c_final_master","ms_story_final_master_tape","决赛五声部母带",2,2,3600,"按当前五声部工程版本录制并封签的《鳴らす》决赛提交母带。"),
            Def("c_pawn_pedal","ms_story_overdrive_pedal","红尖晶石一代旧单块",2,3,4800,"小枣珍藏的初代过载单块效果器；仅作店内周转暂存，不转移所有权。","ms_natsume","custom_overdrive"),
            Def("c_tin","ms_hotaru_farewell_tin","封存铁盒",3,2,0,"小萤在决战前夜寄存店里的铁皮饼干盒，内放保温灯备用灯泡与《everscape》手抄谱。"),
            Def("c_score","ms_story_everscape_score","《everscape》手抄谱",2,2,0,"铁盒里的手抄谱，页边保留着小萤的改写与标记。"),
            Def("c_stage_case","ms_story_stage_transport_case","舞台器材运输箱",4,4,0,"随六件舞台原件送来的周转箱。内部16×12格，只收纳本次乐队原件，联合取件时随器材归还。"),
            Def("c_apples","ms_story_chilled_apples","冷藏苹果袋",2,2,0,"小萤为夺冠庆功咖喱提前切好、用淡盐水泡过并寄存在店里冷藏的一袋苹果块。"),
            Def("c_departure_receipt","ms_story_departure_receipt","离站回执",2,2,0,"一份按发现时的授权范围保管的私人文件。"),
            Def("c_trophy","ms_story_champion_trophy","彗星赛事奖杯",3,4,0,"属于本届「彗星 Rising」年度总冠军的闪耀奖杯。"),
            Def("c_gold_record","ms_story_commemorative_vinyl","金色纪念唱片",3,3,4200,"比赛之后送到店里的纪念唱片。"),
            Def("c_note","ms_story_hotaru_note","纸巾留言",2,2,0,"留给麻里奈的一张纸巾留言。"),
            Def("c_mic","ms_gear_stage_mic","舞台电容话筒",2,3,1800,"一支需要按当前接口校准的舞台话筒。","ms_hotaru","stage_mic",true),
            Def("c_early_backup","ms_gear_commercial_guitar","早期商业备用琴",3,7,3200,"按当前授权记录使用的商业电吉他。","ms_nagi","commercial_guitar",true),
            Def("c_agency_guitar","ms_gear_commercial_guitar","公司舞台琴",3,7,3200,"公司借用安排中的舞台琴。","ms_nagi","commercial_guitar",true),
            Def("","ms_strings_bass5_nickel","五弦贝斯低音套弦",1,2,420,"适用于五弦贝斯的低音套弦。","ms_mahoro","stage_bass",true),
            Def("","ms_strings_guitar6_tension","六弦高张力套弦",1,2,460,"适用于沧溟旧琴或凪的商业琴；安装时绑定具体宿主。","ms_nagi","whale_guitar",true),
            Def("","ms_part_hammer_weights","合成器重锤组件",2,2,960,"适用于钻星合成器的触键改装组件。","ms_hotaru","diamond_synth",true),
            Def("","ms_part_blank_tape","空白录音带",2,2,240,"用于一个明确工程版本的空白介质。",ordinary:true)
        };
        private static bool ValidLocation(string location)=>new[]{"custody","native","member","sealed","sealed_payload","outside","submitted","pledged"}.Contains(location);
        public static void Upgrade(StoryState s,int stage)
        {
            Need(stage>=1 && stage<=2,"不支持的包3实施阶段");var c=s.CampaignItems;if(c.Stage>=stage)return;
            c.Stage=stage;foreach(string scene in s.CompletedDays.Keys)c.LegacyScenes.Add(scene);
            if(s.Active!=null)c.LegacyActiveScene=s.Active.SceneId;
            c.EarlyGuitar=s.Flags.GetValueOrDefault("ms_route_commercial_chosen")?"c_early_backup":"c_whale";c.StageGuitar=Commercial(s)?"c_agency_guitar":c.EarlyGuitar;
            if(s.CompletedDays.ContainsKey("ms_scene_031")){c.Ending=EndingOf(s);SetContacts(s,true);}
            else if(s.CompletedDays.ContainsKey("ms_scene_030"))SetContacts(s,false);
        }
        public static bool FullEnabled(StoryState s)=>s.CampaignItems?.Stage>=2;
        private static bool Commercial(StoryState s)=>s.Flags.GetValueOrDefault("ms_commercial_contract_signed");
        private static string Diary(StoryState s)=>s.CampaignItems.DiaryMode??(s.Flags.GetValueOrDefault("ms_diary_route_erased_oct9")?"filter":s.Flags.GetValueOrDefault("ms_diary_route_shared_burden")?"shared":"raw");
        public static string EndingOf(StoryState s)=>s.CampaignItems?.Return!=null?"solo_departure":Commercial(s)?"commercial":Diary(s)=="raw"?"apparatus":Diary(s)=="filter"?"rainbow":"shared";
        private static CampaignOriginal MemberOriginal(StoryState s,string role,string actor,int day,ICampaignItemAdapter a)
        {var r=Ensure(s,role,day,actor,actor,a,"member_original:"+role);if(r.Source.StartsWith("member_original:") && r.Location=="custody" && r.Custodian==actor)r.Location="member";return r;}
        private static void Hand(StoryState s,string role,string custodian,string location,int day,ICampaignItemAdapter a,string source)
        {
            var r=s.CampaignItems.Originals[role];if(r.Custodian==custodian && r.Location==location)return;
            a.ToCustody(r);r.Custodian=custodian;r.Location=location;History(s,r,"transfer",source,day,"custody_changed");
        }
        private static CampaignOriginal Borrow(StoryState s,string role,string owner,string actor,int day,ICampaignItemAdapter a,string reason)
        {
            if(!s.CampaignItems.Originals.ContainsKey(role) && a is ICampaignSourceAdapter resolver){var actual=resolver.ResolveOriginal(s,role,actor,day);if(actual!=null){s.CampaignItems.Originals.Add(role,actual);History(s,actual,"observed",actual.Source,day,"observed");return actual;}}
            var r=Ensure(s,role,day,owner,actor,a,reason);r.Borrowed=true;r.Lender=owner;r.ReturnAgreement=role=="c_early_backup"?"最终领取前归还舞台借方":"比赛结束按原借用安排归还";if(r.Location=="custody")r.Location="member";r.Condition="serviced";r.CalibratedRevision=r.ConfigRevision;return r;
        }
        private static CampaignResource Supply(StoryState s,ICampaignItemAdapter a,string reference,string actor,bool tool=false)
        {
            if(reference.StartsWith("record_"))return new(){Entity=s.CampaignItems.NextLogical--,Sku=reference,Reference=reference,Owner=actor,Logical=true,Snapshot="logical_task_batch"};
            return a.Create(NativeReference(reference),reference,actor,tool);
        }
        private static CampaignWork CreateFullJob(StoryState s,string id,string host,string name,string actor,int day,int earliest,bool night,int fee,List<CampaignWorkStep> steps,ICampaignItemAdapter a)
        {
            var c=s.CampaignItems;if(c.Jobs.TryGetValue(id,out var old))return old;
            var j=new CampaignWork{Id=id,Name=name,HostRole=host,SupplyActor=actor,RequestDay=day,EarliestDay=day+earliest,OvernightAllowed=night,LaborReference=fee,Quote=fee,FeeState=fee==0?"free":"unquoted",Steps=steps};
            j.SupportCapacityE=j.SupportRemainingE=steps.Sum(x=>x.EnergyE);j.SpecHash=Signature(j);
            foreach(var req in steps.SelectMany(x=>x.Materials).GroupBy(x=>x.Key))for(int n=0;n<req.Sum(x=>x.Value);n++)j.Resources.Add(Supply(s,a,req.Key,actor));
            foreach(var req in steps.SelectMany(x=>x.Tools).GroupBy(x=>x.Key))for(int n=0;n<req.Max(x=>x.Value);n++)j.Resources.Add(Supply(s,a,req.Key,actor,true));
            c.Jobs.Add(id,j);return j;
        }
        public static CampaignWork RequestFull(StoryState s,string id,int day,ICampaignItemAdapter a)
        {
            var c=s.CampaignItems;if(c.Jobs.TryGetValue(id,out var old))return old;
            string host,actor,name;int fee=0,earliest=0;bool night=false;List<CampaignWorkStep> steps;
            switch(id){
                case BassStrings:host="c_bass";actor="ms_mahoro";name="五弦低音套弦制作安装";fee=120;earliest=1;night=true;steps=new(){Step("wind","绕制五弦低音套弦",60,6,"record_bass_wire_lot*1",""),Step("install_strings","验收安装",30,0,"","","counter")};break;
                case GuitarStrings:host=c.EarlyGuitar;actor="ms_nagi";name="六弦高张力套弦制作安装";fee=160;earliest=1;night=true;steps=new(){Step("wind","绕制高张力套弦",60,6,"record_guitar_wire_lot*1",""),Step("install_strings","验收安装",30,0,"","","counter")};break;
                case WhaleRepair:host="c_whale";actor="ms_nagi";name="沧溟旧琴最低整备";fee=180;steps=new(){Step("clean","清洁接点",30,4,"ms_supply_contact_cleaner*1","ref_screwdriver*1"),Step("circuit","线路维修",60,10,"ms_supply_solder_pack*1;ms_part_wire_bundle*1","ref_screwdriver*1;ref_welder*1"),Step("test","基础验收",30,6,"","","counter")};break;
                case Hammer:host="c_synth";actor="ms_hotaru";name="钻星合成器重锤改装";fee=240;earliest=1;night=true;steps=new(){Step("hammer","安装重锤组件",60,10,"ms_part_hammer_weights*1","ref_screwdriver*1"),Step("calibrate","触键校准",60,12,"","","calibration")};break;
                case Mic:host="c_mic";actor="ms_hotaru";name="舞台话筒校准";fee=160;earliest=1;night=true;steps=new(){Step("calibrate","舞台基础校准",60,12,"","","calibration")};break;
                case DecodeJob:host="c_keychain";actor="ms_hotaru";name="获授权的私人数据处理";earliest=1;night=true;steps=new(){Step("decode","按已确认权限处理",60,6,"","","support_only")};break;
                case Prelim:host="c_prelim_master";actor="ms_mahoro";name="初赛两声部母带";fee=180;earliest=1;night=true;steps=new(){Step("record","整理初赛提交版本",90,12,"ms_part_blank_tape*1","","recording")};break;
                case Care:host="c_whale";actor="ms_nagi";name="旧琴指板保养";steps=new(){Step("care","受托指板保养",30,0,"record_care_supplies*1","","counter")};break;
                case FinalTune:host="c_synth";actor="ms_mahoro";name="五人器材基础联调";fee=160;steps=new(){Step("tune","全套舞台设备连通性联调",60,12,"","","calibration")};break;
                case FinalMaster:host="c_final_master";actor="ms_mahoro";name="决赛五声部母带";fee=360;night=true;steps=new(){Step("record","整理五声部工程",120,18,"ms_part_blank_tape*1","","recording"),Step("seal","同一介质封签",30,0,"","","counter")};break;
                case Braid:host="c_keychain";actor="ms_hotaru";name="挂件编绳加固";steps=new(){Step("braid","原件编绳加固",30,0,"record_braid_supplies*1","","counter")};break;
                default:throw new InvalidOperationException("未知后续剧情工单");
            }
            if(id==Prelim || id==FinalMaster){var tape=Ensure(s,host,day,"party:band","player",a,"project_output:"+id);tape.Condition="unrecorded";}
            Need(c.Originals.ContainsKey(host),"缺少已授权的工单原件");
            var job=CreateFullJob(s,id,host,name,actor,day,earliest,night,fee,steps,a);
            if(id==BassStrings || id==GuitarStrings){string sku=id==BassStrings?"ms_strings_bass5_nickel":"ms_strings_guitar6_tension";var output=a.Create(sku,"prepared_strings",actor,false);output.State="unfinished";job.Resources.Add(output);}
            if(id==Prelim || id==FinalMaster){
                job.Version=Commercial(s)?"company_approved":"original";
                var project=new CampaignProject{Id=id,Version=job.Version,Source=id==Prelim?"ms_scene_023":"ms_scene_027",Medium=c.Originals[host].Entity};
                if(id==Prelim){project.Voices["ms_hotaru"]=c.Originals["c_synth"].Entity;project.Voices["ms_natsume"]=c.Originals["c_spinel"].Entity;}
                else foreach(string role in PerformanceRoles(s,true).Where(x=>x!="c_mic"))project.Voices[Definitions.First(x=>x.Role==role).Actor]=c.Originals[role].Entity;
                c.Projects.Add(id,project);
            }
            Hand(s,host,"player","custody",day,a,"intake:"+id);return job;
        }
        public static void QuoteFee(StoryState s,string id,int amount)
        {var j=s.CampaignItems.Jobs[id];Need(!j.Delivered && j.FeeState!="paid" && amount>=0 && (j.LaborReference>0 || amount==0),"工钱已结清、报价无效或属于固定免费服务");j.Quote=amount;j.FeeState=amount==0?"waived":"quoted";}
        public static void UseFinishedStrings(StoryState s,string id,CampaignResource selected)
        {
            var j=s.CampaignItems.Jobs[id];Need((id==BassStrings || id==GuitarStrings) && j.Step==0 && j.Steps[0].ElapsedMs==0 && j.Steps[0].Bound.Count==0,"绕制开始后不能改用成品退款");
            string sku=id==BassStrings?"ms_strings_bass5_nickel":"ms_strings_guitar6_tension";Need(selected.Sku==sku && selected.Entity>0 && !selected.Tool && !string.IsNullOrEmpty(selected.Snapshot),"套弦规格或实际来源不符");
            var old=j.Resources.Single(x=>x.Reference=="prepared_strings");old.Reference="unused_customer_output";old.State="returned_to_customer";
            selected.Reference="prepared_strings";selected.State="available_output";j.Resources.Add(selected);j.Steps[0].SkippedByProcurement=true;j.Step=1;j.Mode="unselected";j.Status="pending";
            s.CampaignItems.Receipts.Add(id+".actual_finished_procurement");
        }
        public static bool PayFee(StoryState s,string id)
        {
            var c=s.CampaignItems;var j=c.Jobs[id];if(j.FeeState=="paid")return false;
            Need(j.Ready && j.FeeState=="quoted" && j.Quote>0 && s.Economy!=null,"尚未确认实际工钱");
            var wallet=s.Economy.Wallets[j.SupplyActor];Need(wallet.Balance>=j.Quote,"成员余额不足，可降价或等待下次来访");wallet.Balance-=j.Quote;j.FeeState="paid";c.Receipts.Add(id+".fee_paid");return true;
        }
        private static string ReadyReason(StoryState s,string id,int day,bool fee=true)
        {if(!s.CampaignItems.Jobs.TryGetValue(id,out var j))return "尚未受理所需委托。";if(!j.Ready)return j.Name+"尚未完成。";if(day<j.EarliestDay)return j.Name+"最早次日交付。";if(fee && !new[]{"free","waived","paid"}.Contains(j.FeeState))return j.Name+"请确认免收工钱，或由到场成员结清。";return null;}
        private static bool Gate(StoryState s,string scene,string choice,string reason)
        {if(reason==null)return true;var c=s.CampaignItems;c.PendingScene=scene;c.PendingChoice=choice;c.LastBlock=reason;return false;}
        private static void Deliver(StoryState s,string id,int day,ICampaignItemAdapter a,string actor=null)
        {
            var j=s.CampaignItems.Jobs[id];Need(ReadyReason(s,id,day)==null,ReadyReason(s,id,day));if(j.Delivered)return;
            if(j.HostRole!="c_prelim_master" && j.HostRole!="c_final_master")Hand(s,j.HostRole,actor??j.SupplyActor,"member",day,a,"delivery:"+id);
            j.Delivered=true;s.CampaignItems.Receipts.Add(id+".delivered");
        }
        private static void Install(CampaignOriginal host,CampaignWork j,CampaignResource r,string slot)
        {
            Need(!host.Attachments.ContainsKey(slot),"该插槽已有实际配件，须先处理旧件");
            host.Attachments[slot]=new(){Entity=r.Entity,Sku=r.Sku,Snapshot=r.Snapshot,SourceJob=j.Id,Slot=slot};r.State="installed";
        }
        private static void CompleteFullStep(StoryState s,CampaignWork j,CampaignWorkStep step,int day)
        {
            var c=s.CampaignItems;var host=c.Originals[j.HostRole];
            if(step.Id=="calibrate" || step.Id=="test" || step.Id=="tune" || step.Id.StartsWith("review_"))host.ConfigRevision--;
            if(step.Id=="wind")j.Resources.Single(r=>r.Reference=="prepared_strings").State="available_output";
            if(step.Id=="install_strings"){
                if(j.TransferFrom!=null){var from=c.Originals[j.TransferFrom];Need(from.Attachments.TryGetValue("strings",out var attached),"没有可转移的原套弦");Need(!host.Attachments.ContainsKey("strings"),"目标琴已有套弦");host.Attachments["strings"]=attached;from.Attachments.Remove("strings");from.ConfigRevision++;from.CalibratedRevision=-1;}
                else Install(host,j,j.Resources.Single(r=>r.Reference=="prepared_strings"),"strings");
                host.Condition="serviced";host.CalibratedRevision=host.ConfigRevision;
            }
            if(step.Id=="hammer")Install(host,j,j.Resources.Single(r=>r.Sku=="ms_part_hammer_weights"),"hammer");
            if(step.Id=="signal")Install(host,j,j.Resources.Single(r=>r.Sku=="ms_part_stabilizer_pickup"),"signal");
            if(step.Id=="calibrate" || step.Id=="test"){host.Condition="serviced";host.CalibratedRevision=host.ConfigRevision;}
            if(step.Id=="clean")host.Condition="cleaned";
            if(step.Id=="circuit")host.Condition="circuit_repaired";
            if(step.Id=="decode"){Need(c.DiaryGranted && c.DiaryMode!=null,"缺少私人处理许可");if(c.DiaryMode=="filter")c.Oct9Covered=true;host.Condition="authorized_processed";}
            if(step.Id=="record" || step.Id=="seal")host.Condition=step.Id=="seal" || j.Id==Prelim?"sealed_master":"recorded_master";
            if(step.Id=="tune")foreach(var role in PerformanceRoles(s,true)){var r=c.Originals[role];r.CalibratedRevision=r.ConfigRevision;j.TargetSignatures[role]=ConfigSignature(r);}
            if(step.Id=="reseal"){
                foreach(var role in j.TargetSignatures.Keys.ToArray()){var r=c.Originals[role];r.CalibratedRevision=r.ConfigRevision;c.Jobs[FinalTune].TargetSignatures[role]=ConfigSignature(r);if(c.Jobs.TryGetValue(FinalMaster,out var master))master.TargetSignatures[role]=ConfigSignature(r);}
                host.ConfigRevision--;
            }
            if(step.Id=="care" || step.Id=="braid"){host.ConfigRevision--;c.Receipts.Add(j.Id+".care_done");}
        }
        private static string FullJobTemplate(CampaignWork j)=>j.Id switch{WhaleRepair=>"whale_restored",Hammer=>"synth_hammer_installed",Mic=>"mic_stage_calibrated",Prelim=>"prelim_master_ready",FinalMaster=>"final_master_ready",_=>"service_completed"};
        public static string ConfigSignature(CampaignOriginal r)
        {
            string text=r.Entity+"|"+r.Sku+"|"+r.ConfigRevision+"|"+r.ExistingConfiguration+"|"+string.Join(",",r.ExistingComponents)+"|"+string.Join(";",r.Attachments.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>x.Key+":"+x.Value.Entity+":"+x.Value.Sku));
            return RuntimeCompat.Hex(RuntimeCompat.Sha256(Encoding.UTF8.GetBytes(text)));
        }
        private static void ValidateFull(CampaignItemsState c)
        {
            if(c.NextLogical>=0 || c.Manifests==null || c.Contacts==null || c.UseRequests==null || c.FinanceContexts==null || c.BusinessLeads==null)throw new InvalidDataException("包3后续结构不完整");
            var attached=new HashSet<int>();
            foreach(var host in c.Originals.Values)foreach(var part in host.Attachments.Values){var resource=c.Jobs.Values.SelectMany(x=>x.Resources).SingleOrDefault(x=>x.Entity==part.Entity);if(part.Entity<=0 || !attached.Add(part.Entity) || resource==null || resource.State!="installed" || resource.Sku!=part.Sku || part.Snapshot!=resource.Snapshot)throw new InvalidDataException("安装配件实体或单活位置冲突");}
            foreach(var j in c.Jobs.Values)if(j.Quote<0 || !new[]{"free","unquoted","quoted","waived","paid"}.Contains(j.FeeState) || (j.Delivered && !j.Ready))throw new InvalidDataException("剧情工钱或交付记录错误");
            foreach(var m in c.Manifests.Values)foreach(var entry in m.Entries.Values)if(!c.Originals.Values.Any(x=>x.Entity==entry.Entity) || string.IsNullOrEmpty(entry.Signature) || entry.Components.Distinct().Count()!=entry.Components.Length)throw new InvalidDataException("演出清单缺少实际原件");
            foreach(var p in c.Projects.Values)if(p.HasAudio || !c.Originals.Values.Any(x=>x.Entity==p.Medium) || p.Voices.Values.Distinct().Count()!=p.Voices.Count || p.Voices.Values.Any(id=>!c.Originals.Values.Any(x=>x.Entity==id)))throw new InvalidDataException("录制工程元数据缺少来源");
        }
    }
}
