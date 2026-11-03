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
        public Dictionary<string,CampaignNative012> NativeActTwo {get;set;}
    }
    public static class ActTwoPresentation
    {
        public static bool Scene(string id)=>id is "ms_scene_011" or "ms_scene_013" or "ms_scene_014" or "ms_scene_015" or "ms_scene_016";
        public static bool Night(string id)=>id=="ms_scene_016";
        public static string Title(string id)=>id=="ms_scene_016"?"中层区星环回廊 · 正式选拔赛":"";
        public static string Emotion(string scene,string actor)=>scene is "ms_scene_014"?"sad":scene is "ms_scene_015" or "ms_scene_016"?"smile":"neutral";
    }
    public static partial class PackageC
    {
        public static IEnumerable<CampaignWork> ActTwoJobs(StoryState s,string role=null)=>s.CampaignItems.Jobs.Values.Where(j=>(j.Id is BassStrings or Hammer or Mic)&&(role==null||j.HostRole==role));
        public static CampaignNative012 PrepareActTwo(StoryState s,string actor)
        {
            Need(MemberBusiness.ValidActor(actor),"无效的委托成员");s.CampaignItems.NativeActTwo??=new();
            if(!s.CampaignItems.NativeActTwo.TryGetValue(actor,out var n))s.CampaignItems.NativeActTwo.Add(actor,n=new());return n;
        }
        public static bool ScheduleActTwoPickup(StoryState s,string id,int day)
        {
            if(id is not (Hammer or Mic))return false;var c=s.CampaignItems;var j=c.Jobs[id];string receipt="act2.story_pickup."+id;
            if(j.Delivered||c.Receipts.Contains(receipt))return false;
            MemberBusiness.Book(s.Economy.Business,j.SupplyActor,day,true,true,s);c.Receipts.Add(receipt);PrepareActTwo(s,j.SupplyActor).NotifiedDay=day;return true;
        }
        private static CampaignNative012 ActTwoRecord(StoryState s,string role)=>PrepareActTwo(s,ActTwoJobs(s,role).First().SupplyActor);
        public static int ActTwoEnergy(StoryState s,string role)=>(int)Math.Ceiling(ActTwoJobs(s,role).Where(j=>!j.Delivered).Sum(j=>j.Steps.Skip(j.Step).Sum(x=>(decimal)x.EnergyE*(x.DurationMs-x.ElapsedMs)/x.DurationMs)));
        public static bool WorkActTwo(StoryState s,string role,int day,int source,int energy)
        {
            var c=s.CampaignItems;var n=ActTwoRecord(s,role);var original=c.Originals[role];
            Need(original.Custodian=="player"&&original.Location=="native","请保留当前受托原件");
            if(n.WorkedDay.GetValueOrDefault(role,-1)>=day)return false;
            Need(ActTwoJobs(s,role).All(j=>day>=j.EarliestDay),"按游戏日完成加工，最早次日验收");
            Need(energy==ActTwoEnergy(s,role)&&(energy==0||source>0),"实际电量不完整");
            foreach(var job in ActTwoJobs(s,role).Where(j=>!j.Delivered&&!j.Ready).ToArray())while(!job.Ready)FinishNativeStep(s,job,day,source);
            n.WorkedDay[role]=day;n.Detected.Remove(role);return true;
        }
        public static bool DetectActTwo(StoryState s,string role,int day)
        {
            var jobs=ActTwoJobs(s,role).ToArray();Need(jobs.Length>0&&jobs.All(j=>j.Ready),"先在整备台过夜完成加工");
            Need(jobs.All(j=>day>=j.EarliestDay),"最早次日验收");var r=s.CampaignItems.Originals[role];var n=ActTwoRecord(s,role);string signature=ConfigSignature(r);
            if(n.Detected.GetValueOrDefault(role)==signature)return false;r.CalibratedRevision=r.ConfigRevision;n.Detected[role]=signature;return true;
        }
        public static string ActTwoBlock(StoryState s,string role,int day)
        {
            var jobs=ActTwoJobs(s,role).Where(j=>!j.Delivered).ToArray();if(jobs.Length==0)return null;
            if(jobs.Any(j=>!j.Ready))return "请把原件、所附配件及工具放入整备台，睡觉过一天加工。";
            if(jobs.Any(j=>day<j.EarliestDay))return "这份委托最早次日交回。";
            var c=s.CampaignItems;var actor=jobs[0].SupplyActor;
            return c.NativeActTwo?.GetValueOrDefault(actor)?.Detected.GetValueOrDefault(role)==ConfigSignature(c.Originals[role])?null:"请用检测仪确认同一原件的当前配置。";
        }
        public static void DeliverActTwo(StoryState s,int entity,string actor,int day,int paid,string snapshot)
        {
            var c=s.CampaignItems;var original=c.Originals.Values.Single(r=>r.Entity==entity);var jobs=ActTwoJobs(s,original.Role).Where(j=>!j.Delivered).ToArray();var n=PrepareActTwo(s,actor);
            Need(jobs.Length>0&&jobs.All(j=>j.SupplyActor==actor)&&original.Custodian=="player"&&original.Location=="native"&&!n.Settled.ContainsKey(entity),"原件已经交回或当前顾客不是物主");
            Need(ActTwoBlock(s,original.Role,day)==null,ActTwoBlock(s,original.Role,day));Need(paid>=0&&s.Economy.Wallets[actor].Balance>=paid&&!string.IsNullOrEmpty(snapshot),"实际付款或快照无效");
            s.Economy.Wallets[actor].Balance-=paid;int remaining=paid,total=jobs.Sum(j=>j.LaborReference);var charged=jobs.Where(j=>j.LaborReference>0).ToArray();
            foreach(var job in jobs){int share=job.LaborReference==0?0:ReferenceEquals(job,charged.Last())?remaining:(int)((long)paid*job.LaborReference/Math.Max(1,total));remaining-=share;job.Quote=share;job.Delivered=true;job.FeeState=share==0?"waived":"paid";foreach(var r in job.Resources.Where(r=>r.Tool||r.State is "available" or "unused_customer_output"))r.State="returned_to_customer";c.Receipts.Add(job.Id+".delivered");}
            original.Snapshot=snapshot;original.Custodian=actor;original.Location="member";n.Settled.Add(entity,paid);History(s,original,"transfer","native_act2.counter",day,"custody_changed");
        }
        private static string ActTwoAdmission(StoryState s,string scene,int day)
        {
            var c=s.CampaignItems;if(c.NativeActTwo==null||c.LegacyScenes.Contains(scene)||c.LegacyActiveScene==scene)return null;
            string previous=scene switch{"ms_scene_011"=>"ms_scene_010","ms_scene_012"=>"ms_scene_011","ms_scene_013"=>"ms_scene_012","ms_scene_014"=>"ms_scene_013","ms_scene_015"=>"ms_scene_014",_=>null};
            if(previous!=null&&s.CompletedDays.TryGetValue(previous,out int at)&&day<=at)return "按约定，次日再见。";
            if(scene=="ms_scene_012"&&ActTwoJobs(s).Any(j=>j.Id==BassStrings&&!j.Delivered))return "先交回茉幌的贝斯套弦委托，再接小凪的原琴。";
            if(scene=="ms_scene_013"&&Native012Jobs(s,c.EarlyGuitar).Any(j=>!j.Delivered))return "先完成凪的舞台用琴交回。";
            if(scene=="ms_scene_014"&&ActTwoJobs(s).Any(j=>(j.Id is BassStrings or Hammer)&&!j.Delivered))return "先把套弦和重锤改装的原件交回，成员才会去预选演出。";
            if(scene=="ms_scene_015"&&ActTwoJobs(s).Any(j=>j.Id==Mic&&!j.Delivered))return "先校准并交回舞台话筒，再进行主唱交接。";
            return null;
        }
        private static void ValidateActTwo(CampaignItemsState c)
        {
            if(c.NativeActTwo==null)return;
            foreach(var p in c.NativeActTwo){var n=p.Value;
                if(!MemberBusiness.ValidActor(p.Key)||n==null||n.Version!=1||n.Note<0||n.Issued==null||n.Plans==null||n.WorkedDay==null||n.TestPower==null||n.Detected==null||n.Settled==null||n.Issued.Any(id=>id<=0)||n.Settled.Any(x=>x.Key<=0||x.Value<0)||n.Detected.Keys.Any(role=>!c.Originals.ContainsKey(role)))throw new InvalidDataException("第二幕原生交接记录无效，保留旧档");
                if(n.Plans.Any(x=>!c.Originals.ContainsKey(x.Key)||x.Value==null||x.Value.Machine<=0||x.Value.Day<0)||n.WorkedDay.Any(x=>!c.Originals.ContainsKey(x.Key)||x.Value<0)||n.Settled.Keys.Any(id=>!c.Originals.Values.Any(o=>o.Entity==id&&(o.Custodian==p.Key&&o.Location=="member"||ReturnTransferred(c,id))))||n.Note>0&&(c.Originals.Values.Any(o=>o.Entity==n.Note)||c.Jobs.Values.SelectMany(j=>j.Resources).Any(r=>r.Entity==n.Note)))throw new InvalidDataException("第二幕日结或交付身份冲突，保留旧档");
            }
        }
    }
}
