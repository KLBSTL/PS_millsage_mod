using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace ResourceStoryMod.Story
{
    public sealed partial class CampaignItemsState
    {
        [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
        public CampaignActOne ActOne {get;set;}
    }
    public sealed class CampaignActOne
    {
        public int Version {get;set;}=1;
        public int CareEntity {get;set;}
        public int CareDay {get;set;}=-1;
        public string CareChoice {get;set;}
        public HashSet<int> CounterSupplies {get;set;}=new();
    }
    // Presentation metadata only. Authored text, choices and stable node IDs stay intact.
    public static class ActOnePresentation
    {
        public static bool Scene(string id)=>id is "ms_scene_005" or "ms_scene_006" or "ms_scene_007" or "ms_scene_008" or "ms_scene_009" or "ms_scene_010";
        public static bool Night(string id)=>id is "ms_scene_007" or "ms_scene_008" or "ms_scene_010";
        public static bool NativeScene(string id)=>Scene(id)||ActTwoPresentation.Scene(id)||DiaryPresentation.Scene(id)||ReturnPresentation.Scene(id);
        public static bool NativeNight(string id)=>Night(id)||ActTwoPresentation.Night(id)||ReturnPresentation.Night(id);
        public static string Title(string id)=>id switch{"ms_scene_007"=>"水道街 Live House · 合奏比试","ms_scene_008"=>"水道街 Live House · 最后一场","ms_scene_010"=>"水道街 · 五人初舞台",_=>ReturnPresentation.Scene(id)?ReturnPresentation.Title(id):ActTwoPresentation.Title(id)};
        public static string[] VisibleActors(StoryScene scene,string node,int part=0,StoryState state=null)
        {
            if(DiaryPresentation.Scene(scene.Id))return DiaryPresentation.Actors(state,scene,node,part);
            if(ReturnPresentation.Scene(scene.Id))return scene.Participants.Where(MemberBusiness.ValidActor).Where(a=>a!="ms_hotaru"||scene.Id!="ms_scene_031"&&!(scene.Id=="ms_scene_030"&&node is "line_004_app_chase_unwarned" or "line_005" or "end_030")).Distinct().ToArray();
            if(scene.Id=="ms_scene_005"&&node is "line_001" or "line_002")return new[]{"ms_hotaru"};
            if(scene.Id=="ms_scene_005"&&scene.Nodes.Any(n=>n.DialogueParts!=null)){
                if(node=="line_003"&&part<2)return new[]{"ms_hotaru"};
                if((node is "line_008a" or "line_008b")&&part>=1||node=="line_009")return new[]{"ms_houka","ms_hotaru"};
            }
            if(scene.Id=="ms_scene_009"){
                if(node is "line_001" or "line_002")return new[]{"ms_natsume"};
                if(node is "line_003" or "line_004")return new[]{"ms_natsume","ms_hotaru"};
            }
            return scene.Participants.Where(MemberBusiness.ValidActor).Distinct().ToArray();
        }
        public static string Emotion(string scene,string node,string actor,int part=0)
        {
            if(DiaryPresentation.Scene(scene))return DiaryPresentation.Emotion(scene,node,actor);
            if(ReturnPresentation.Scene(scene))return scene=="ms_scene_031"||scene=="ms_scene_030"&&(node is "line_004_app_chase_unwarned" or "line_005")?"sad":scene=="ms_scene_030"&&node=="line_004_app_unwarned"?"neutral":"smile";
            if(ActTwoPresentation.Scene(scene))return ActTwoPresentation.Emotion(scene,actor);
            if(scene=="ms_scene_005")return actor=="ms_hotaru"?(node is "line_005" or "line_006" or "choice_001" or "line_008a" or "line_008b"?"sad":node=="line_009"&&part>=6?"smile":"neutral"):"neutral";
            if(scene=="ms_scene_008")return actor is "ms_natsume" or "ms_mahoro"?"sad":"neutral";
            if(scene=="ms_scene_009")return node is "line_007" or "line_008" or "line_009"?"smile":"sad";
            return scene=="ms_scene_010"?"smile":"neutral";
        }
    }
    // The existing native night-trip receipt is kept outside StoryEngine so its
    // debit and the vanilla visitLeftTonight are saved together by SaveGame.
    public sealed class StoryNightVisit
    {
        public int Version {get;set;}=1;
        public string RunId {get;set;}
        public int Day {get;set;}
        public bool Active {get;set;}
        public string Scene {get;set;}
        public bool Begin(string run,string scene,int day,ref int remaining)
        {
            if(Active||remaining<=0||day<0||string.IsNullOrEmpty(run)||!StoryContent.ValidId(scene))return false;
            RunId=run;Scene=scene;Day=day;Active=true;remaining--;return true;
        }
        public bool Resume(string run,int day)=>Version==1&&Active&&RunId==run&&Day==day;
        public bool NeedsReturn(StoryState state)=>Active&&state.Active==null&&Scene!=null&&state.CompletedDays.ContainsKey(Scene);
    }
    public static partial class PackageC
    {
        public static void PrepareActOne(StoryState s)=>s.CampaignItems.ActOne??=new();
        private static string ActOneAdmission(StoryState s,string scene,int day)
        {
            var c=s.CampaignItems;if(c.ActOne==null||c.LegacyScenes.Contains(scene)||c.LegacyActiveScene==scene)return null;
            string previous=scene switch{"ms_scene_006"=>"ms_scene_005","ms_scene_008"=>"ms_scene_007","ms_scene_009"=>"ms_scene_008",_=>null};
            if(previous!=null&&s.CompletedDays.TryGetValue(previous,out int completed)&&day<=completed)return "按约定次日再见。";
            if(scene=="ms_scene_005"&&c.Opening!=null){
                if(!c.Opening.Detected)return "先用检测仪确认同一台合成器。";
                if(!c.Originals.TryGetValue("c_synth",out var r)||r.Location!="native"||r.Custodian!="player")return "请保留修好的原合成器在店内试音。";
                if(s.Economy?.Business?.Appointments.TryGetValue("ms_mahoro",out var ap)==true&&ap.Status=="pending"&&ap.Day>day)return "已通知茉幌，按电话约定次日试音。";
            }
            return null;
        }
        // This is the scripted, free counter care already promised in both009
        // choices. It uses the job's finite identified kit, not a realtime clock,
        // a second payment or an arbitrary same-SKU item from the player's bag.
        private static void CompleteActOneCare(StoryState s,int day,string choice)
        {
            var c=s.CampaignItems;var n=c.ActOne;var j=c.Jobs[SpinalJob];var r=c.Originals[j.HostRole];
            if(n.CareEntity!=0){Need(n.CareEntity==r.Entity&&n.CareChoice==choice,"现场护理已按原选择完成");return;}
            Need(choice is "opt_tag_repair" or "opt_gentle_scold","免费护理需要原剧情选择授权");
            foreach(var supply in j.Resources)n.CounterSupplies.Add(supply.Entity);
            if(!j.Ready){SelectMode(s,SpinalJob,"support",day);while(!j.Ready)Progress(s,SpinalJob,Math.Min(60000,j.Steps[j.Step].DurationMs-j.Steps[j.Step].ElapsedMs),day,false);}
            foreach(var supply in j.Resources)supply.Owner="player";
            foreach(var tool in j.Resources.Where(x=>x.Tool))tool.State="available";
            j.Quote=j.LaborReference=0;j.FeeState="free";j.Delivered=true;
            n.CareEntity=r.Entity;n.CareChoice=choice;n.CareDay=day;
            r.CalibratedRevision=r.ConfigRevision;
            c.Receipts.Add("009_counter_care_once");
        }
        private static void ValidateActOne(CampaignItemsState c)
        {
            var n=c.ActOne;if(n==null)return;
            if(n.Version!=1||n.CounterSupplies==null||n.CareEntity<0||n.CareDay< -1)throw new InvalidDataException("第一幕原生记录无效，保留旧档");
            if(n.CareEntity==0){if(n.CareDay!=-1||n.CareChoice!=null||n.CounterSupplies.Count!=0)throw new InvalidDataException("现场护理记录不完整");return;}
            if(!c.Originals.TryGetValue("c_spinel",out var r)||r.Entity!=n.CareEntity||!c.Jobs.TryGetValue(SpinalJob,out var j)||!j.Ready||!j.Delivered||j.FeeState!="free"||j.Quote!=0||n.CareDay<0||n.CareChoice is not ("opt_tag_repair" or "opt_gentle_scold")||!n.CounterSupplies.SetEquals(j.Resources.Select(x=>x.Entity)))throw new InvalidDataException("现场护理原件或耗材回执冲突");
        }
    }
}
