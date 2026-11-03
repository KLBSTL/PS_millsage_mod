using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace ResourceStoryMod.Story
{
    public static partial class PackageC
    {
        public static string FullAdmission(StoryState s,string scene,int day)
        {
            var c=s.CampaignItems;if(c.LegacyScenes.Contains(scene) || c.LegacyActiveScene==scene)return null;
            if(NeedsLegacyIntake(s,scene))return null;
            string Ready(params string[] jobs)=>jobs.Select(id=>ReadyReason(s,id,day,false)).FirstOrDefault(x=>x!=null);
            return scene switch{
                "ms_scene_015"=>Ready(Mic),
                "ms_scene_016"=>Ready(BassStrings,GuitarStrings,Hammer,Mic)??StageReason(s,false),
                "ms_scene_020"=>Ready(DecodeJob),
                "ms_scene_025"=>Ready(Prelim)??StageReason(s,false),
                "ms_scene_029"=>Ready(FinalMaster,FinalTune)??FinalSignatureReason(s),
                _=>null
            };
        }
        private static string StageReason(StoryState s,bool final)
        {
            var c=s.CampaignItems;string role=final?(Commercial(s)?"c_agency_guitar":"c_whale"):c.StageGuitar;
            foreach(string r in new[]{"c_synth","c_spinel","c_bass","c_drums",role,"c_mic"})if(!c.Originals.ContainsKey(r))return "演出清单缺少已登记原件。";
            if(!c.Originals["c_synth"].Attachments.ContainsKey("hammer") || c.Originals["c_synth"].CalibratedRevision!=c.Originals["c_synth"].ConfigRevision)return "合成器重锤或当前配置校准尚未完成。";
            if(!c.Originals["c_bass"].Attachments.ContainsKey("strings"))return "五弦贝斯尚未安装本次套弦。";
            if(!c.Originals[role].Attachments.ContainsKey("strings") && role!="c_agency_guitar")return "演出琴尚未安装高张力套弦。";
            if(role=="c_whale" && ReadyReason(s,WhaleRepair,int.MaxValue,false)!=null)return "沧溟旧琴尚未完成最低整备。";
            if(c.Originals["c_mic"].CalibratedRevision!=c.Originals["c_mic"].ConfigRevision)return "话筒当前配置需重新校准。";
            return null;
        }
        private static IEnumerable<string> PerformanceRoles(StoryState s,bool final=false)
        {return new[]{"c_bass",final?(Commercial(s)?"c_agency_guitar":"c_whale"):s.CampaignItems.StageGuitar,"c_spinel","c_synth","c_drums","c_mic"}.Where(r=>s.CampaignItems.Originals.ContainsKey(r));}
        private static CampaignWork RetargetStrings(StoryState s,string target,int day,ICampaignItemAdapter a)
        {
            var c=s.CampaignItems;var original=c.Jobs[GuitarStrings];if(!original.Ready && original.HostRole==target)return original;
            Need(original.Ready,"先完成已经受理的套弦工单，再选择转移");
            string source=c.Originals.Values.FirstOrDefault(x=>x.Attachments.ContainsKey("strings") && x.Attachments["strings"].Sku=="ms_strings_guitar6_tension")?.Role;
            Need(source!=null,"没有可转移的原套弦");
            if(source==target)return original;
            string id="c_job_strings_retarget_"+target.Substring(2);if(c.Jobs.TryGetValue(id,out var saved)){if(!saved.Ready)return saved;id+="_r"+c.Originals[source].ConfigRevision;}
            Hand(s,source,"player","custody",day,a,"strings_retarget_source:"+id);Hand(s,target,"player","custody",day,a,"strings_retarget_target:"+id);
            var j=CreateFullJob(s,id,target,"原套弦转移验收","ms_nagi",day,0,false,0,new(){Step("install_strings","原套弦兼容安装",30,0,"","","counter")},a);j.TransferFrom=source;return j;
        }
        private static bool FullEnter(StoryState s,string scene,string node,int day,string choice,ICampaignItemAdapter a)
        {
            var c=s.CampaignItems;if(c.LegacyScenes.Contains(scene) || c.LegacyActiveScene==scene)return true;
            DiaryBeforeEnter(s,scene,node,day,choice,a);
            if(c.Return!=null&&ReturnPresentation.Scene(scene))return ReturnEnter(s,scene,node,day,choice,a);
            if(node=="line_001" && NeedsLegacyIntake(s,scene) && !LegacyFutureIntake(s,scene,day,a))return false;
            if(scene=="ms_scene_010" && node=="line_001"){
                MemberOriginal(s,"c_bass","ms_mahoro",day,a);MemberOriginal(s,"c_drums","ms_houka",day,a);
            }
            if(scene=="ms_scene_011" && (node=="line_004a" || node=="line_004b")){
                RequestFull(s,BassStrings,day,a);RequestFull(s,GuitarStrings,day,a);
            }
            if(scene=="ms_scene_013" && node=="line_005"){
                Ensure(s,"c_keybed_sketch",day,"party:band","player",a,"013.authorized_sketch");RequestFull(s,Hammer,day,a);
            }
            if(scene=="ms_scene_014" && node=="line_006"){
                Borrow(s,"c_mic","outside:stage_lender","ms_hotaru",day,a,"014.authorized_stage_loan");RequestFull(s,Mic,day,a);
            }
            if(scene=="ms_scene_015" && choice!=null){
                if(!Gate(s,scene,choice,ReadyReason(s,Mic,day)))return false;Deliver(s,Mic,day,a,"ms_hotaru");
            }
            if(scene=="ms_scene_016" && node=="line_001"){
                if(!Gate(s,scene,choice,new[]{BassStrings,GuitarStrings,Hammer,Mic}.Select(id=>ReadyReason(s,id,day)).FirstOrDefault(x=>x!=null)??(c.EarlyGuitar=="c_whale"?ReadyReason(s,WhaleRepair,day):null)))return false;
                foreach(string id in new[]{BassStrings,GuitarStrings,Hammer,Mic})Deliver(s,id,day,a);
                if(c.Jobs.ContainsKey("c_job_strings_retarget_early_backup"))Deliver(s,"c_job_strings_retarget_early_backup",day,a);
                if(c.Jobs[WhaleRepair].Ready && c.EarlyGuitar=="c_whale")Deliver(s,WhaleRepair,day,a);
                if(c.Originals.ContainsKey("c_keybed_sketch"))Hand(s,"c_keybed_sketch","ms_hotaru","member",day,a,"hammer_sketch_return");
            }
            if(scene=="ms_scene_017" && node=="line_004")Ensure(s,"c_keychain",day,"ms_mahoro","player",a,"017.lost_property");
            if(scene=="ms_scene_018" && node=="line_003")Hand(s,"c_keychain","ms_mahoro","member",day,a,"018.return_before_gift");
            if(scene=="ms_scene_018" && (node=="line_006a" || node=="line_006b")){
                var r=c.Originals["c_keychain"];r.Owner="ms_hotaru";r.ReadAuthorized=true;c.DiaryGranted=true;Hand(s,"c_keychain","ms_hotaru","member",day,a,"018.gift_and_grant");c.FundsState="deposit_announced";
            }
            if(scene=="ms_scene_019" && choice!=null){
                string mode=choice=="opt_decode_solo_raw"?"raw":choice=="opt_decode_shop_filter"?"filter":"shared";
                Need(c.DiaryGranted && (c.DiaryMode==null || c.DiaryMode==mode),"授权处理方案已经确定");c.DiaryMode=mode;RequestFull(s,DecodeJob,day,a);
                DiaryAfterEnter(s,scene,day,choice,a);
            }
            if(scene=="ms_scene_020" && node=="line_001"){
                c.DiaryRevealed=true;Deliver(s,DecodeJob,day,a,"ms_hotaru");
            }
            if(scene=="ms_scene_022" && node=="line_007a"){
                Ensure(s,"c_pawn_pedal",day,"ms_natsume","player",a,"022.narrative_pledge").Location="pledged";c.FundsState="autonomy_pledge";
            }
            if(scene=="ms_scene_022" && node=="line_007b"){
                if(!c.Originals.ContainsKey("c_whale")){var old=Ensure(s,"c_whale",day,"ms_nagi","ms_nagi",a,"legacy_future_contract_storage");old.Location="member";old.Condition="legacy_unverified";}
                Hand(s,"c_whale","player","sealed",day,a,"022.commercial_storage");Borrow(s,"c_agency_guitar","party:agency","ms_nagi",day,a,"022.company_stage_loan");c.StageGuitar="c_agency_guitar";c.FundsState="agency_contract";
            }
            if(scene=="ms_scene_024" && node=="line_006"){
                if(!c.Originals.ContainsKey("c_whale")){var old=Ensure(s,"c_whale",day,"ms_nagi","ms_nagi",a,"legacy_future_actual_care");old.Location="member";old.Condition="legacy_unverified";}
                RequestFull(s,Care,day,a);if(!Gate(s,scene,choice,ReadyReason(s,Care,day)))return false;
            }
            if(scene=="ms_scene_024" && node=="line_008"){
                c.Jobs[Care].Delivered=true;Hand(s,"c_whale",Commercial(s)?"player":"ms_nagi",Commercial(s)?"sealed":"member",day,a,"024.authorized_care_return");
            }
            if(scene=="ms_scene_025" && node=="line_001"){
                if(!Gate(s,scene,choice,ReadyReason(s,Prelim,day)))return false;
                Deliver(s,Prelim,day,a);Hand(s,"c_prelim_master","outside:contest_staff","submitted",day,a,"025.prelim_submission");
                c.Projects[Prelim].Submitted=true;
            }
            if(scene=="ms_scene_026" && node=="choice_002_auto"){
                var retarget=RetargetStrings(s,"c_whale",day,a);
                if(!Gate(s,scene,choice,ReadyReason(s,WhaleRepair,day)??ReadyReason(s,retarget.Id,day)))return false;
                Deliver(s,WhaleRepair,day,a,"ms_nagi");if(retarget.Id!=GuitarStrings)Deliver(s,retarget.Id,day,a,"ms_nagi");c.StageGuitar="c_whale";
            }
            if(scene=="ms_scene_026" && node=="choice_002_comm"){
                Need(c.Originals.ContainsKey("c_agency_guitar") && c.Originals["c_whale"].Location=="sealed","公司借琴或旧琴封存凭据缺失");
            }
            if(scene=="ms_scene_027" && node=="line_001"){
                Need(c.FinalPreparation || c.LegacyScenes.Contains("ms_scene_026"),"尚未登记联调准备请求");
                foreach(string role in PerformanceRoles(s,true))Hand(s,role,"player","custody",day,a,"027.joint_arrival_authorization");
                RequestFull(s,FinalTune,day,a);
                if(!Gate(s,scene,choice,StageReason(s,true)??ReadyReason(s,FinalTune,day)))return false;
                if(FinalSignatureReason(s)!=null)return Gate(s,scene,choice,"器材配置变化，请复核联调清单。");
            }
            if(scene=="ms_scene_028" && (node=="line_004a_warned" || node=="line_004a")){
                Ensure(s,"c_tin",day,"ms_hotaru","player",a,"028.sealed_deposit").Location="sealed";
                Ensure(s,"c_apples",day,"ms_hotaru","player",a,"028.chilled_apples");
                Hand(s,"c_keychain","player","sealed_payload",day,a,"028.same_keychain_sealed");c.Originals["c_keychain"].Visible=false;
                var hidden=Ensure(s,"c_departure_receipt",day,"ms_hotaru","player",a,"028.sealed_payload");hidden.Visible=false;hidden.Location="sealed_payload";
                c.Receipts.Add("028.collection_tomorrow_22_no_auction");
            }
            if(scene=="ms_scene_028" && node=="line_003c"){
                RequestFull(s,Braid,day,a);if(!Gate(s,scene,choice,ReadyReason(s,Braid,day)))return false;Deliver(s,Braid,day,a,"ms_hotaru");
            }
            if(scene=="ms_scene_029" && node=="line_001" && !Gate(s,scene,choice,ReadyReason(s,FinalMaster,day)??ReadyReason(s,FinalTune,day)))return false;
            if(scene=="ms_scene_029" && node=="line_002"){
                foreach(string role in PerformanceRoles(s,true)){var actor=Definitions.First(x=>x.Role==role).Actor;Hand(s,role,actor,"member",day,a,"029.joint_claim");}
                c.Jobs[FinalTune].Delivered=true;Deliver(s,FinalMaster,day,a);Hand(s,"c_final_master","outside:contest_staff","submitted",day,a,"029.final_submission");
                c.Projects[FinalMaster].Submitted=true;
                if(c.Originals.TryGetValue("c_early_backup",out var backup))Hand(s,"c_early_backup",backup.Borrowed?backup.Lender:"ms_nagi",backup.Borrowed?(backup.Lender=="player"?"custody":"outside"):"member",day,a,"029.early_loan_return");
                c.Receipts.Add("029.joint_claim_once");
            }
            if(scene=="ms_scene_029" && node=="line_004_app_discovery"){
                if(!c.Originals.ContainsKey("c_tin"))Ensure(s,"c_tin",day,"ms_hotaru","player",a,"legacy_future_actual_discovery").Location="sealed";
                foreach(string role in new[]{"c_keychain","c_departure_receipt"})if(!c.Originals.ContainsKey(role))Ensure(s,role,day,"ms_hotaru","player",a,"legacy_future_actual_discovery");
                c.HiddenRevealed=true;foreach(string role in new[]{"c_keychain","c_departure_receipt"}){c.Originals[role].Visible=true;Hand(s,role,"player","custody",day,a,"029.actual_discovery");}
            }
            if(scene=="ms_scene_030" && node!="line_001" && s.Active?.NodeId=="line_003"){
                string owner=Commercial(s)?"party:agency":"party:band";var trophy=Ensure(s,"c_trophy",day,owner,Commercial(s)?"outside:agency_showroom":"ms_hotaru",a,"030.champion_announced");trophy.Location=Commercial(s)?"outside":"member";
            }
            if(scene=="ms_scene_031")EnterEnding(s,node,day,a);
            if(CampaignFinanceText.Anchors.Contains(scene+"/"+node))FinanceContext(s,scene+"/"+node,day);
            if(node=="line_001" && new[]{"ms_scene_007","ms_scene_008","ms_scene_010","ms_scene_016","ms_scene_025","ms_scene_030"}.Contains(scene))FreezePerformance(s,scene,day);
            return true;
        }
        public static bool BeforeEnd(StoryState s,string scene,int day,ICampaignItemAdapter a)
        {
            if(!FullEnabled(s) || s.CampaignItems.LegacyScenes.Contains(scene) || s.CampaignItems.LegacyActiveScene==scene)return true;
            if(s.CampaignItems.Return!=null&&ReturnPresentation.Scene(scene))return ReturnBeforeEnd(s,scene,day,a);
            var c=s.CampaignItems;
            if(scene=="ms_scene_012"){
                RequestFull(s,WhaleRepair,day,a);
                if(s.Flags.GetValueOrDefault("ms_route_commercial_chosen")){
                    Borrow(s,"c_early_backup","outside:stage_lender","ms_nagi",day,a,"012.authorized_stage_backup");c.EarlyGuitar=c.StageGuitar="c_early_backup";
                    var j=c.Jobs[GuitarStrings];
                    if(!j.Ready && j.Steps.All(x=>x.ElapsedMs==0)){j.HostRole="c_early_backup";j.SpecHash=Signature(j);Hand(s,"c_early_backup","player","custody",day,a,"012.selected_strings_host");}
                    else {var retarget=RetargetStrings(s,"c_early_backup",day,a);if(!retarget.Ready)return Gate(s,scene,null,"原套弦转移后需完成实际兼容验收。");}
                }
            }
            if(scene=="ms_scene_023"){
                foreach(var pair in new[]{("c_synth","ms_hotaru"),("c_spinel","ms_natsume")})if(!c.Originals.ContainsKey(pair.Item1)){var r=Ensure(s,pair.Item1,day,pair.Item1=="c_synth"?"party:band":pair.Item2,pair.Item2,a,"legacy_future_actual_recording");r.Location="member";r.Condition="legacy_unverified";}
                RequestFull(s,Prelim,day,a);
            }
            if(scene=="ms_scene_026")c.FinalPreparation=true;
            if(scene=="ms_scene_027"){
                Need(FinalSignatureReason(s)==null,"联调期间配置发生变化");var j=RequestFull(s,FinalMaster,day,a);foreach(string role in PerformanceRoles(s,true))j.TargetSignatures[role]=ConfigSignature(c.Originals[role]);
            }
            return true;
        }
        private static string FinalSignatureReason(StoryState s)
        {
            var c=s.CampaignItems;if(!c.Jobs.TryGetValue(FinalTune,out var tune) || !tune.Ready)return "全套联调尚未完成。";
            foreach(string role in PerformanceRoles(s,true))if(!tune.TargetSignatures.TryGetValue(role,out var signature) || signature!=ConfigSignature(c.Originals[role]))return "联调后器材配置改变，需要复核受影响项。";
            if(c.Jobs.TryGetValue(FinalMaster,out var master) && master.TargetSignatures.Count>0 && master.TargetSignatures.Any(x=>ConfigSignature(c.Originals[x.Key])!=x.Value))return "签封后器材配置改变，需要复核工程版本。";
            return null;
        }
        public static CampaignWork PrepareFinalRecheck(StoryState s,int day,ICampaignItemAdapter a)
        {
            var c=s.CampaignItems;Need(c.Jobs.TryGetValue(FinalTune,out var tune) && tune.Ready && !c.Receipts.Contains("029.joint_claim_once"),"只对联合取件前的既有联调配置复核");
            var changed=PerformanceRoles(s,true).Where(role=>tune.TargetSignatures.GetValueOrDefault(role)!=ConfigSignature(c.Originals[role])).ToArray();Need(changed.Length>0,"当前配置没有变化，无需重复消耗");
            Need(changed.All(role=>c.Originals[role].Custodian=="player"),"需要受影响原件的现有保管授权");
            string id="c_recheck_"+string.Join("_",changed.Select(role=>c.Originals[role].Entity+"r"+c.Originals[role].ConfigRevision));
            var steps=changed.Select((role,i)=>Step("review_"+i,"受影响器材基础校准",60,12,"","","calibration")).ToList();steps.Add(Step("reseal","复核清单与封签",30,0,"","","counter"));
            var job=CreateFullJob(s,id,"c_synth","配置变更后复核封签","ms_mahoro",day,0,false,0,steps,a);foreach(string role in changed)job.TargetSignatures[role]=ConfigSignature(c.Originals[role]);return job;
        }
        private static void EnterEnding(StoryState s,string node,int day,ICampaignItemAdapter a)
        {
            var c=s.CampaignItems;string ending=EndingOf(s);
            if(node=="line_001" && ending=="apparatus"){
                if(!c.Originals.ContainsKey("c_synth")){var synth=Ensure(s,"c_synth",day,"party:band","ms_mahoro",a,"legacy_future_031_band_arrival");synth.Condition="legacy_unverified";synth.Location="member";}
                Hand(s,"c_synth","ms_mahoro","member",day,a,"031.band_retrieved_from_stage");
            }
            if(node=="line_002_comm_solo" || node=="line_002_comm_warm")Ensure(s,"c_gold_record",day,"player","player",a,"031.explicit_gold_record_gift");
            if(node=="line_002_comm_warm")Ensure(s,"c_note",day,"player","player",a,"031.explicit_private_note").ReadAuthorized=true;
            if(new[]{"line_002_app","line_002_rain","line_002_shar"}.Contains(node) && !c.Originals.ContainsKey("c_trophy"))Ensure(s,"c_trophy",day,"party:band",node=="line_002_app"?"ms_mahoro":"ms_hotaru",a,"legacy_future_031_actual_trophy").Location="member";
            if(node=="line_002_app")Hand(s,"c_trophy","player","custody",day,a,"031.mahoro_trophy_deposit");
            if(node=="line_003_app"){if(!c.Originals.ContainsKey("c_apples"))Ensure(s,"c_apples",day,"ms_hotaru","player",a,"legacy_future_031_cold_storage_access");Hand(s,"c_apples","player","custody",day,a,"031.same_chilled_apples");}
            if(node=="line_005_app"){Hand(s,"c_synth","ms_natsume","member",day,a,"031.band_authorized_search_use");History(s,c.Originals["c_synth"],"custody","031.search",day,"synth_custody_natsume");}
            if(node=="line_002_shar")c.FundsState="narrative_refund_only";
            if(node=="line_002_shar" && !c.Originals.ContainsKey("c_keychain"))Ensure(s,"c_keychain",day,"ms_hotaru","ms_hotaru",a,"legacy_future_actual_trophy_attachment").Location="member";
            if(node=="line_005_shar"){if(!c.Originals.ContainsKey("c_pawn_pedal"))Ensure(s,"c_pawn_pedal",day,"ms_natsume","player",a,"legacy_future_031_actual_redemption");Hand(s,"c_pawn_pedal","ms_natsume","member",day,a,"031.actual_pledge_redemption");}
        }
        private static void FreezePerformance(StoryState s,string scene,int day)
        {
            var c=s.CampaignItems;if(c.Manifests.ContainsKey(scene))return;
            IEnumerable<string> roles=scene=="ms_scene_007" || scene=="ms_scene_008"?new[]{"c_synth","c_spinel"}:scene=="ms_scene_010"?new[]{"c_bass","c_whale","c_spinel","c_synth","c_drums"}:PerformanceRoles(s,scene=="ms_scene_030");
            var manifest=new CampaignManifest{Id=scene,Day=day,Kind=scene=="ms_scene_007" || scene=="ms_scene_008"?"activity":"live"};
            foreach(string role in roles){var r=c.Originals[role];Need(r.Visible && r.Location=="member" && r.Custodian!="player","实际舞台原件没有完成交接");manifest.Entries.Add(role,new(){Entity=r.Entity,Actor=Definitions.First(x=>x.Role==role).Actor,Signature=ConfigSignature(r),Revision=r.ConfigRevision,Components=r.Attachments.Values.Select(x=>x.Entity).Concat(r.ExistingComponents).Distinct().OrderBy(x=>x).ToArray()});}
            c.Manifests.Add(scene,manifest);
        }
        private static void FullSceneEnded(StoryState s,string scene,int day,ICampaignItemAdapter a)
        {
            var c=s.CampaignItems;
            if(c.Return!=null&&ReturnPresentation.Scene(scene)){ReturnEnded(s,scene,day);return;}
            if(c.LegacyScenes.Contains(scene) || c.LegacyActiveScene==scene){if(scene=="ms_scene_030" || scene=="ms_scene_031"){if(scene=="ms_scene_031")c.Ending=EndingOf(s);SetContacts(s,scene=="ms_scene_031");}return;}
            if(c.Manifests.TryGetValue(scene,out var m) && !m.Completed){
                Need(m.Entries.All(x=>c.Originals[x.Key].Entity==x.Value.Entity && ConfigSignature(c.Originals[x.Key])==x.Value.Signature),"舞台实际器材与冻结清单不一致");
                string template=scene switch{"ms_scene_007"=>"duel_first_night","ms_scene_008"=>"duel_last_night","ms_scene_010"=>"reunion_first_live","ms_scene_016"=>"middle_selection_used","ms_scene_025"=>"prelim_runner_up_used",_=>"comet_champion_used"};
                foreach(string role in m.Entries.Keys){History(s,c.Originals[role],"performance",scene,day,template);var h=c.History[s.RunId+"/"+c.Originals[role].Entity+"/performance/"+scene];h.Visibility="public";}
                m.Completed=true;m.CompletedDay=day;
            }
            if(scene=="ms_scene_010")c.BusinessLeads.Add("practice_small_repairs");
            if(scene=="ms_scene_016")c.BusinessLeads.Add("middle_stage_interfaces");
            if(scene=="ms_scene_022")c.BusinessLeads.Add(Commercial(s)?"commercial_brand_consistency":"independent_refurbishment");
            if(scene=="ms_scene_030" && (EndingOf(s)=="apparatus" || Commercial(s)))SetContacts(s,false);
            if(scene=="ms_scene_031"){
                c.Ending=EndingOf(s);SetContacts(s,true);c.BusinessLeads.Add("ending:"+c.Ending);
                if(c.Ending=="commercial"){
                    if(c.Originals.ContainsKey("c_agency_guitar"))Hand(s,"c_agency_guitar","party:agency","outside",day,a,"031.company_loan_return");
                }else if(c.Ending=="rainbow" || c.Ending=="shared")Hand(s,"c_trophy","ms_hotaru","member",day,a,"031.display_not_gift");
                if(c.Originals.TryGetValue("c_mic",out var mic) && mic.Borrowed)Hand(s,"c_mic",mic.Lender,mic.Lender=="player"?"custody":"outside",day,a,"031.stage_mic_return");
                c.Receipts.Add("031.ending_disposition:"+c.Ending);
            }
        }
        private static bool NeedsLegacyIntake(StoryState s,string scene)
        {
            var c=s.CampaignItems;if(c.LegacyIntakeScene==scene)return true;if(!c.LegacyScenes.Any(x=>string.CompareOrdinal(x,"ms_scene_011")>=0))return false;
            if(scene=="ms_scene_019")return !c.Originals.ContainsKey("c_keychain");
            if(scene=="ms_scene_020")return !c.Jobs.ContainsKey(DecodeJob);
            if(scene=="ms_scene_028")return !Commercial(s) && !c.Originals.ContainsKey("c_keychain");
            if(!new[]{"ms_scene_015","ms_scene_016","ms_scene_025","ms_scene_027","ms_scene_029","ms_scene_030"}.Contains(scene))return false;
            return !new[]{BassStrings,GuitarStrings,Hammer,Mic,WhaleRepair}.All(c.Jobs.ContainsKey) || scene=="ms_scene_025" && !c.Jobs.ContainsKey(Prelim) || new[]{"ms_scene_029","ms_scene_030"}.Contains(scene) && !c.Jobs.ContainsKey(FinalMaster);
        }
        private static bool LegacyFutureIntake(StoryState s,string scene,int day,ICampaignItemAdapter a)
        {
            var c=s.CampaignItems;c.LegacyIntakeScene=scene;
            if(scene=="ms_scene_019" || scene=="ms_scene_020" || scene=="ms_scene_028"){
                Ensure(s,"c_keychain",day,"ms_hotaru","ms_hotaru",a,"legacy_future_intake:"+scene).Location="member";c.DiaryGranted=true;if(scene!="ms_scene_019")c.DiaryMode=Diary(s);c.Originals["c_keychain"].ReadAuthorized=true;
                if(scene=="ms_scene_020"){RequestFull(s,DecodeJob,day,a);if(!Gate(s,scene,null,ReadyReason(s,DecodeJob,day)))return false;c.LegacyIntakeScene=null;return true;}
                c.DiaryRevealed=c.LegacyScenes.Contains("ms_scene_020");c.LegacyIntakeScene=null;return true;
            }
            foreach(var pair in new[]{("c_synth","ms_hotaru"),("c_spinel","ms_natsume"),("c_whale","ms_nagi"),("c_bass","ms_mahoro"),("c_drums","ms_houka")}){
                if(c.Originals.ContainsKey(pair.Item1))continue;var original=Ensure(s,pair.Item1,day,pair.Item1=="c_synth"?"party:band":pair.Item2,pair.Item2,a,"legacy_future_intake:"+scene);original.Location="member";original.Condition="legacy_unverified";
            }
            if(!c.Jobs.ContainsKey(SynthJob)){Hand(s,"c_synth","player","custody",day,a,"legacy_future_current_check");Request(s,SynthJob,day,a);}
            if(!c.Jobs.ContainsKey(SpinalJob)){Hand(s,"c_spinel","player","custody",day,a,"legacy_future_current_check");Request(s,SpinalJob,day,a);}
            if(c.EarlyGuitar=="c_early_backup" && !c.Originals.ContainsKey("c_early_backup"))Borrow(s,"c_early_backup","outside:stage_lender","ms_nagi",day,a,"legacy_future_authorized_backup");
            foreach(string id in new[]{BassStrings,GuitarStrings,WhaleRepair,Hammer})if(!c.Jobs.ContainsKey(id))RequestFull(s,id,day,a);
            if(!c.Originals.ContainsKey("c_mic"))Borrow(s,"c_mic","outside:stage_lender","ms_hotaru",day,a,"legacy_future_authorized_mic");
            if(!c.Jobs.ContainsKey(Mic))RequestFull(s,Mic,day,a);
            string reason=new[]{SynthJob,SpinalJob,BassStrings,GuitarStrings,WhaleRepair,Hammer,Mic}.Select(id=>ReadyReason(s,id,day)).FirstOrDefault(x=>x!=null);
            if(!Gate(s,scene,null,reason))return false;
            if(Commercial(s)){
                if(!c.Originals.ContainsKey("c_agency_guitar"))Borrow(s,"c_agency_guitar","party:agency","ms_nagi",day,a,"legacy_future_company_authorization");Hand(s,"c_whale","player","sealed",day,a,"legacy_current_company_storage");c.StageGuitar="c_agency_guitar";
            }
            if(scene=="ms_scene_025"){
                RequestFull(s,Prelim,day,a);if(!Gate(s,scene,null,ReadyReason(s,Prelim,day)))return false;
            }
            if(scene=="ms_scene_027" || scene=="ms_scene_029" || scene=="ms_scene_030"){
                c.FinalPreparation=true;
                if(!Commercial(s)){var retarget=RetargetStrings(s,"c_whale",day,a);if(!Gate(s,scene,null,ReadyReason(s,retarget.Id,day)))return false;c.StageGuitar="c_whale";}
                foreach(string role in PerformanceRoles(s,true))Hand(s,role,"player","custody",day,a,"legacy_future_joint_authorization");
                RequestFull(s,FinalTune,day,a);if(!Gate(s,scene,null,ReadyReason(s,FinalTune,day)))return false;
                if(scene=="ms_scene_029" || scene=="ms_scene_030"){
                    var master=RequestFull(s,FinalMaster,day,a);if(master.TargetSignatures.Count==0)foreach(string role in PerformanceRoles(s,true))master.TargetSignatures[role]=ConfigSignature(c.Originals[role]);
                    if(!Gate(s,scene,null,ReadyReason(s,FinalMaster,day)))return false;
                }
            }
            foreach(string id in new[]{SynthJob,SpinalJob,BassStrings,GuitarStrings,Hammer,Mic})Deliver(s,id,day,a,id==SynthJob?"ms_hotaru":null);
            if(!Commercial(s))Deliver(s,WhaleRepair,day,a);
            if(scene=="ms_scene_030"){
                Deliver(s,FinalMaster,day,a);Hand(s,"c_final_master","outside:contest_staff","submitted",day,a,"legacy_future_actual_submission");c.Projects[FinalMaster].Submitted=true;
            }
            c.LegacyIntakeScene=null;return true;
        }
        private static void SetContacts(StoryState s,bool afterFinal)
        {
            var c=s.CampaignItems;string ending=EndingOf(s);
            foreach(string actor in Five){bool limited=(ending=="commercial" || ending=="apparatus") && actor=="ms_hotaru";bool away=afterFinal && ending=="apparatus";bool company=afterFinal && ending=="commercial";
                c.Contacts[actor]=new(){Natural=!limited && !away && !company,Phone=!limited && !away,Reason=away?"成员已外出，未约定返程":limited?(ending=="commercial"?"公司安排限制联系":"目前无法取得联系"):company?"可电话约定，不随机来店":"正常联系"};
            }
            if(s.Economy?.Business!=null)foreach(var ap in s.Economy.Business.Appointments)if(ap.Value.Status=="pending" && !CanContact(s,ap.Key,true))ap.Value.Status="deferred_unavailable";
        }
        public static bool CanContact(StoryState s,string actor,bool phone)=>!FullEnabled(s) || !s.CampaignItems.Contacts.TryGetValue(actor,out var c) || (phone?c.Phone:c.Natural);
        private static void FinanceContext(StoryState s,string source,int day)
        {
            var c=s.CampaignItems;if(c.FinanceContexts.ContainsKey(source))return;var wallets=s.Economy?.Wallets.Values;
            bool single=wallets?.Any(x=>x.Balance>=30000)==true,group=wallets?.Sum(x=>x.Balance)>=30000;
            c.FinanceContexts[source]=new(){Source=source,Day=day,AnyMemberCanFund=single,GroupCanFund=group,TextVariant=single?"funds_available_contract_context":group?"independent_wallet_coordination":"ordinary_pressure"};
        }
    }
}
