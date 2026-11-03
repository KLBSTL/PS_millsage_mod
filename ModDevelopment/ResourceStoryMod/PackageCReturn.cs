using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace ResourceStoryMod.Story
{
    public sealed partial class CampaignItemsState
    {
        [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public CampaignReturn Return {get;set;}
    }
    public sealed class CampaignReturn
    {
        public int Version {get;set;}=1;
        public int AttachedDay {get;set;}
        public int Note {get;set;}
        public Dictionary<string,CampaignNative012Plan> Plans {get;set;}=new();
        public Dictionary<string,int> WorkedDay {get;set;}=new();
        public Dictionary<string,int> Power {get;set;}=new();
        public HashSet<int> Issued {get;set;}=new();
        public Dictionary<string,string> Inspected {get;set;}=new();
        public Dictionary<string,int> Appointments {get;set;}=new();
        public Dictionary<int,int> Settled {get;set;}=new();
        public Dictionary<string,string> CounterSnapshots {get;set;}=new();
        public bool TinRevealed {get;set;}
        public bool LeftUnnoticed {get;set;}
        public bool TruthSharedAfterFinal {get;set;}
        public bool FinaleComplete {get;set;}
    }
    public static class ReturnPresentation
    {
        public static bool Scene(string id)=>id!=null&&string.CompareOrdinal(id,"ms_scene_021")>=0&&string.CompareOrdinal(id,"ms_scene_031")<=0;
        public static bool Night(string id)=>id is "ms_scene_025" or "ms_scene_030";
        public static string Lead(string id)=>id is "ms_scene_024" or "ms_scene_026"?"ms_nagi":id=="ms_scene_028"?"ms_hotaru":"ms_mahoro";
        public static string Title(string id)=>Night(id)?VenuePresentation.Title(id):"乐器店 · 比赛筹备";
    }
    public static partial class PackageC
    {
        public const string ReturnProfile="solo_return_v1";
        private static bool ReturnTransferred(CampaignItemsState c,int id)=>c.Return!=null&&c.History.Values.Any(h=>h.Entity==id&&h.Kind=="transfer"&&h.Source.StartsWith("return.",StringComparison.Ordinal));
        public static void AttachReturn(StoryState s,string profile,int day)
        {
            if(profile!=ReturnProfile)return;if(s.CampaignItems.Return!=null)return;
            Need(!s.CompletedDays.Keys.Any(id=>string.CompareOrdinal(id,"ms_scene_020")>0)&&(s.Active==null||string.CompareOrdinal(s.Active.SceneId,"ms_scene_019")<0),"新版只接续020及以前的兼容档；已走入其它后续路线的旧档保留原包，不改写既有决定。");
            Need(s.CampaignItems.DiaryMode is null or "raw","新版仅支持萤独读；已有过滤/共读档保留原路线，不转换授权。");
            s.CampaignItems.Return=new(){AttachedDay=day};
        }
        private static string ReturnAdmission(StoryState s,string scene,int day)
        {
            var c=s.CampaignItems;if(c.Return==null||!ReturnPresentation.Scene(scene))return null;
            string previous="ms_scene_"+(int.Parse(scene.Substring(9))-1).ToString("D3");
            if(scene is "ms_scene_021" or "ms_scene_022" or "ms_scene_023" or "ms_scene_026" or "ms_scene_027" or "ms_scene_028")if(s.CompletedDays.TryGetValue(previous,out int at)&&day<=at)return "按本次约定，次日继续。";
            if(scene=="ms_scene_024"&&(!c.Jobs.TryGetValue(Prelim,out var prelim)||!prelim.Delivered))return "先按电话约定交回初赛母带。";
            if(scene=="ms_scene_031"&&s.CompletedDays.TryGetValue("ms_scene_030",out int finalDay)&&day<=finalDay)return "按交付剧情，夺冠次日清晨回店继续。";
            if(scene=="ms_scene_025"&&(!c.Jobs.TryGetValue(Care,out var care)||!care.Delivered))return "先完成小凪的免费护理并交回同一旧琴。";
            if(scene=="ms_scene_027")return ReadyReason(s,FinalTune,day,false)??StageReason(s,true);
            if(scene=="ms_scene_029"){
                if(!c.Return.Appointments.TryGetValue("final_pickup",out int due)||day<due)return "按后天早上的约定取回全套器材。";
                if(!c.Jobs.TryGetValue(FinalMaster,out var master)||!master.Delivered)return "先在柜台交回决赛母带并按实际报价结算。";
            }
            return null;
        }
        private static bool ReturnEnter(StoryState s,string scene,string node,int day,string choice,ICampaignItemAdapter a)
        {
            var c=s.CampaignItems;
            if(scene=="ms_scene_022"&&node=="line_007a") {Ensure(s,"c_pawn_pedal",day,"ms_natsume","player",a,"return.022.actual_storage");c.FundsState="independent_storage_no_auto_cash";}
            if(scene=="ms_scene_024"&&node=="line_001"){Hand(s,"c_whale","player","custody",day,a,"return.024.free_care");RequestFull(s,Care,day,a);}
            if(scene=="ms_scene_025"&&node=="line_001"){
                Need(c.Jobs[Prelim].Delivered,"初赛母带尚未实际交回。");Hand(s,"c_prelim_master","outside:contest_staff","submitted",day,a,"return.025.actual_submission");c.Projects[Prelim].Submitted=true;FreezePerformance(s,scene,day);
            }
            if(scene=="ms_scene_028"&&node is "line_004a" or "line_004a_warned"){
                Ensure(s,"c_tin",day,"ms_hotaru","player",a,"return.028.tin_deposit");Ensure(s,"c_apples",day,"ms_hotaru","player",a,"return.028.chilled_apples");Ensure(s,"c_score",day,"ms_hotaru","player",a,"return.028.annotated_score");
                Hand(s,"c_keychain","player","custody",day,a,"return.028.same_keychain");c.Originals["c_keychain"].Visible=false;c.Originals["c_score"].Visible=false;
                var hidden=Ensure(s,"c_departure_receipt",day,"ms_hotaru","player",a,"return.028.sealed_receipt");hidden.Visible=false;hidden.Location="sealed_payload";
            }
            if(scene=="ms_scene_029"&&node=="line_002"){
                foreach(string role in PerformanceRoles(s,true))Hand(s,role,Definitions.First(d=>d.Role==role).Actor,"member",day,a,"return.029.actual_joint_claim");
                Hand(s,"c_stage_case","ms_mahoro","member",day,a,"return.029.transport_case_return");
                c.Jobs[FinalTune].Delivered=true;Hand(s,"c_final_master","outside:contest_staff","submitted",day,a,"return.029.final_submission");c.Projects[FinalMaster].Submitted=true;c.Receipts.Add("029.joint_claim_once");
                if(c.Jobs.TryGetValue("c_job_strings_retarget_whale",out var retarget)){Need(retarget.Ready,"原套弦尚未转回旧琴。");retarget.Delivered=true;retarget.Quote=0;retarget.FeeState="free";c.Receipts.Add(retarget.Id+".delivered");}
                if(c.Originals.TryGetValue("c_early_backup",out var backup)&&backup.Borrowed)Hand(s,"c_early_backup",backup.Lender,backup.Lender=="player"?"custody":"outside",day,a,"return.029.early_loan_return");
            }
            if(scene=="ms_scene_029"&&node=="line_004_app_discovery"){
                c.Return.TinRevealed=true;c.HiddenRevealed=true;
                foreach(string role in new[]{"c_keychain","c_departure_receipt"}){
                    var original=Ensure(s,role,day,"ms_hotaru","player",a,"return.029.actual_discovery");original.Visible=true;Hand(s,role,"player","custody",day,a,"return.029.revealed_payload");
                }
            }
            if(scene=="ms_scene_030"&&node=="line_001")FreezePerformance(s,scene,day);
            if(scene=="ms_scene_030"&&node=="line_004_app_unwarned"){
                Ensure(s,"c_trophy",day,"party:band","ms_mahoro",a,"return.030.champion").Location="member";
                Hand(s,"c_mic","ms_mahoro","member",day,a,"return.030.mic_left_on_synth");Hand(s,"c_synth","ms_mahoro","member",day,a,"return.030.synth_retrieved_after_exit");c.Return.LeftUnnoticed=true;
            }
            if(scene=="ms_scene_031"){
                if(node=="line_001"){
                    c.Return.TinRevealed=true;c.HiddenRevealed=true;c.Originals["c_keychain"].Visible=c.Originals["c_score"].Visible=true;
                    // They know the departure and her distress, not the private raw diary.
                    c.Return.TruthSharedAfterFinal=true;if(c.NativeDiary!=null)c.NativeDiary.KeepSecretUntilContest=false;
                }
                if(node=="line_002_app")Hand(s,"c_trophy","player","custody",day,a,"return.031.trophy_deposit");
                if(node=="line_005_app")Hand(s,"c_synth","ms_natsume","member",day,a,"return.031.synth_with_band");
                if(node=="line_006_app"){
                    Hand(s,"c_keychain","ms_mahoro","member",day,a,"return.031.keychain_kept_for_hotaru");
                    // The author explicitly entrusts the score to the shop in this revision.
                    foreach(string role in new[]{"c_score","c_tin"})Hand(s,role,"player","custody",day,a,"return.031.shop_keeps_score_and_tin");
                }
            }
            if(CampaignFinanceText.Anchors.Contains(scene+"/"+node)||scene+"/"+node=="ms_scene_031/line_002_app")FinanceContext(s,scene+"/"+node,day);
            return true;
        }
        private static bool ReturnBeforeEnd(StoryState s,string scene,int day,ICampaignItemAdapter a)
        {
            var c=s.CampaignItems;
            if(scene=="ms_scene_023"){RequestFull(s,Prelim,day,a);Hand(s,"c_synth","player","custody",day,a,"return.023.recording_source");ReturnBook(s,"prelim_pickup","ms_mahoro",day+1);}
            if(scene=="ms_scene_026"){
                c.FinalPreparation=true;c.StageGuitar="c_whale";Ensure(s,"c_stage_case",day,"party:band","player",a,"return.026.transport_case");foreach(string role in PerformanceRoles(s,true))Hand(s,role,"player","custody",day,a,"return.026.joint_inspection");if(!c.Originals["c_whale"].Attachments.ContainsKey("strings")){if(c.Originals.TryGetValue("c_early_backup",out var backup)&&backup.Attachments.ContainsKey("strings"))Hand(s,"c_early_backup","player","custody",day,a,"return.026.same_strings_source");RetargetStrings(s,"c_whale",day,a);}RequestFull(s,FinalTune,day,a);
            }
            if(scene=="ms_scene_027"){
                Need(FinalSignatureReason(s)==null,"先完成联调，再讨论最后的合奏。");var j=RequestFull(s,FinalMaster,day,a);foreach(string role in PerformanceRoles(s,true))j.TargetSignatures[role]=ConfigSignature(c.Originals[role]);ReturnBook(s,"final_pickup","ms_mahoro",day+2);
            }
            if(scene=="ms_scene_031"&&c.Originals.TryGetValue("c_mic",out var mic)&&mic.Borrowed)Hand(s,"c_mic",mic.Lender,mic.Lender=="player"?"custody":"outside",day,a,"return.031.stage_loan_return");
            return true;
        }
        private static void ReturnBook(StoryState s,string key,string actor,int due)
        {
            var n=s.CampaignItems.Return;if(n.Appointments.TryGetValue(key,out int booked)&&booked>=due)return;n.Appointments[key]=due;MemberBusiness.Book(s.Economy.Business,actor,due-1,true,true,s);
        }
        private static void ReturnEnded(StoryState s,string scene,int day)
        {
            var c=s.CampaignItems;if(c.Manifests.TryGetValue(scene,out var m)&&!m.Completed){Need(m.Entries.All(p=>c.Originals[p.Key].Entity==p.Value.Entity&&ConfigSignature(c.Originals[p.Key])==p.Value.Signature),"实际舞台配置与冻结清单不符。");m.Completed=true;m.CompletedDay=day;foreach(var row in m.Entries){History(s,c.Originals[row.Key],"performance",scene,day,scene=="ms_scene_025"?"prelim_runner_up_used":"comet_champion_used");c.History[s.RunId+"/"+row.Value.Entity+"/performance/"+scene].Visibility="public";}}
            if(scene=="ms_scene_030"||scene=="ms_scene_031")foreach(string actor in Five)c.Contacts[actor]=new(){Natural=actor!="ms_hotaru",Phone=actor!="ms_hotaru",Reason=actor=="ms_hotaru"?"小萤已经离场；后续故事尚未展开":"正常联系"};
            if(scene=="ms_scene_031"){c.Ending="solo_departure";c.Return.FinaleComplete=true;c.Receipts.Add("return.031.single_ending_complete");}
        }
        public static IEnumerable<CampaignWork> ReturnJobs(StoryState s)=>s.CampaignItems?.Return==null?Enumerable.Empty<CampaignWork>():s.CampaignItems.Jobs.Values.Where(j=>j.Id is Prelim or FinalTune or FinalMaster or Care);
        public static bool ReturnManagesWork(StoryState s,string role)=>role!=null&&ReturnJobs(s).Any(j=>!j.Delivered&&(j.HostRole==role||j.Id==FinalTune&&(role is "c_bass" or "c_whale" or "c_spinel" or "c_synth" or "c_drums" or "c_mic")||role=="c_synth"&&(j.Id is Prelim or FinalMaster)));
        public static string ReturnNotificationKey(StoryState s,string actor)
        {
            var job=ReturnJobs(s).FirstOrDefault(j=>j.SupplyActor==actor&&j.Ready&&!j.Delivered&&(j.Id is Prelim or FinalMaster));return job==null?null:job.Id==Prelim?"prelim_pickup":"final_pickup";
        }
        public static void ReturnInspect(StoryState s,string role,int entity,string signature,int day,int source)
        {
            var c=s.CampaignItems;Need(c.Return!=null&&c.Originals.TryGetValue(role,out var r)&&r.Entity==entity&&r.Custodian=="player"&&r.Location=="native"&&signature==ConfigSignature(r),"需要实际保管的同一原件和当前配置。");c.Return.Inspected[role]=signature;
            if(role=="c_whale"&&c.Jobs.TryGetValue(Care,out var care)&&!care.Ready)while(!care.Ready)FinishNativeStep(s,care,day,source);
            if(Native012Jobs(s,role).Any(j=>j.TransferFrom!=null&&!j.Delivered)&&Native012Jobs(s,role).All(j=>j.Ready))DetectNative012(s,role,day);
        }
        public static void ReturnPlan(StoryState s,string job,int machine,int day)
        {
            var n=s.CampaignItems.Return;Need(n!=null&&machine>0&&ReturnJobs(s).Any(j=>j.Id==job&&!j.Ready&&!j.Delivered),"没有可安排的实际委托。");n.Plans[job]=new(){Machine=machine,Day=day};
        }
        public static bool ReturnWork(StoryState s,string id,int machine,int source,int day,int energy)
        {
            var c=s.CampaignItems;var n=c.Return;var j=c.Jobs[id];if(j.Ready)return false;
            Need(n!=null&&n.Plans.TryGetValue(id,out var plan)&&plan.Machine==machine&&plan.Day<day&&source>0&&day>=j.EarliestDay,"需要实际放入工位并睡觉过一天。");
            Need(energy==j.Steps.Skip(j.Step).Sum(x=>x.EnergyE),"实际供电不符。");
            if(id==FinalTune)Need(PerformanceRoles(s,true).All(role=>c.Originals[role].Location=="native"&&n.Inspected.GetValueOrDefault(role)==ConfigSignature(c.Originals[role])),"请用检测仪逐一确认六件现有舞台原件。");
            while(!j.Ready)FinishNativeStep(s,j,day,source);n.WorkedDay[id]=day;n.Power[id]=source;return true;
        }
        public static void ReturnDeliver(StoryState s,string id,int entity,string actor,int paid,string snapshot,int day,ICampaignItemAdapter a)
        {
            var c=s.CampaignItems;var n=c.Return;var j=c.Jobs[id];var r=c.Originals[j.HostRole];Need(n!=null&&!j.Delivered&&j.Ready&&r.Entity==entity&&r.Location=="native"&&r.Custodian=="player"&&actor==j.SupplyActor&&!n.Settled.ContainsKey(entity)&&paid>=0&&s.Economy.Wallets[actor].Balance>=paid&&!string.IsNullOrEmpty(snapshot),"工单、实物、物主或实际付款不符。");
            r.Snapshot=snapshot;s.Economy.Wallets[actor].Balance-=paid;j.Quote=paid;j.FeeState=paid==0?"waived":"paid";j.Delivered=true;n.Settled[entity]=paid;r.Custodian=actor;r.Location="member";c.Receipts.Add(id+".delivered");
            if(id==Prelim)Hand(s,"c_synth","ms_hotaru","member",day,a,"return.024.recording_source_return");
            if(id==FinalMaster){c.Jobs[FinalTune].FeeState="waived";c.Jobs[FinalTune].Quote=0;c.Receipts.Add("return.final_tune_fee_in_master_trade");}
        }
        private static void ValidateReturn(CampaignItemsState c)
        {
            var n=c.Return;if(n==null)return;
            if(n.Version!=1||n.AttachedDay<0||n.Note<0||n.Plans==null||n.WorkedDay==null||n.Power==null||n.Issued==null||n.Inspected==null||n.Appointments==null||n.Settled==null||n.CounterSnapshots==null||n.CounterSnapshots.Any(p=>!c.Originals.ContainsKey(p.Key)||string.IsNullOrEmpty(p.Value))||c.DiaryMode is not (null or "raw")||n.WorkedDay.Any(x=>!c.Jobs.TryGetValue(x.Key,out var j)||!j.Ready||x.Value<0)||n.FinaleComplete&&(c.Ending!="solo_departure"||!n.TruthSharedAfterFinal||!n.LeftUnnoticed))throw new InvalidDataException("新版独读单线记录无效，保留原存档。");
        }
    }
}
