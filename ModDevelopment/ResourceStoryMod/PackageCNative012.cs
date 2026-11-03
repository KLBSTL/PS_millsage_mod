using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ResourceStoryMod.Story
{
    public sealed partial class CampaignItemsState
    {
        public CampaignNative012 Native012 {get;set;}
    }
    // Optional extension: old campaigns keep their frozen work specifications.
    public sealed class CampaignNative012
    {
        public int Version {get;set;}=1;
        public int Note {get;set;}
        public HashSet<int> Issued {get;set;}=new();
        public Dictionary<string,int> WorkedDay {get;set;}=new();
        public Dictionary<string,CampaignNative012Plan> Plans {get;set;}=new();
        public Dictionary<string,int> TestPower {get;set;}=new();
        public Dictionary<string,string> Detected {get;set;}=new();
        public Dictionary<int,int> Settled {get;set;}=new();
        public int NotifiedDay {get;set;}=-1;
    }
    public sealed class CampaignNative012Plan
    {
        public int Machine {get;set;}
        public int Day {get;set;}
    }
    public static partial class PackageC
    {
        public const string Native012Signal="c_job_whale_signal";
        public static bool IsNative012Job(string id)=>new[]{WhaleRepair,GuitarStrings,Native012Signal,"c_job_strings_retarget_early_backup","c_job_strings_retarget_whale"}.Contains(id);
        public static IEnumerable<CampaignWork> Native012Jobs(StoryState s,string role=null)=>
            s.CampaignItems.Jobs.Values.Where(j=>IsNative012Job(j.Id)&&(role==null||j.HostRole==role));
        public static void PrepareNative012(StoryState s,int day,ICampaignItemAdapter adapter)
        {
            Need(FullEnabled(s)&&s.CampaignItems.Jobs.ContainsKey(WhaleRepair),"012尚未授权收件");
            var c=s.CampaignItems;c.Native012??=new();
            if(!s.Flags.GetValueOrDefault("ms_route_commercial_chosen")&&!c.Originals["c_whale"].Attachments.ContainsKey("signal"))
                CreateFullJob(s,Native012Signal,"c_whale","沧溟稳压拾音模组安装","ms_nagi",day,0,false,0,
                    new(){Step("signal","安装稳压拾音模组",30,6,"ms_part_stabilizer_pickup*1","ref_screwdriver*1")},adapter);
        }
        public static int Native012Energy(StoryState s,string role)
        {
            decimal due=Native012Jobs(s,role).Where(j=>!j.Delivered).Sum(j=>j.Steps.Skip(j.Step).Sum(x=>(decimal)x.EnergyE*(x.DurationMs-x.ElapsedMs)/x.DurationMs));
            if(s.CampaignItems.Native012.TestPower.ContainsKey(role))due-=Native012Jobs(s,role).Where(j=>!j.Ready).Sum(j=>j.Steps.Skip(j.Step).Where(x=>x.Id=="test").Sum(x=>(decimal)x.EnergyE*(x.DurationMs-x.ElapsedMs)/x.DurationMs));
            return (int)Math.Ceiling(due);
        }
        private static void FinishNativeStep(StoryState s,CampaignWork job,int day,int source)
        {
            SelectMode(s,job.Id,"own",day);BindSupport(job);
            while(job.Status=="running")Progress(s,job.Id,Math.Min(60000,job.Steps[job.Step].DurationMs-job.Steps[job.Step].ElapsedMs),day,true,source);
        }
        public static bool WorkNative012(StoryState s,string role,int day,int source,int paidEnergy)
        {
            var c=s.CampaignItems;var n=c.Native012;
            Need(n!=null&&c.Originals.TryGetValue(role,out var original)&&original.Custodian=="player"&&original.Location=="native","原件不在当前保管库存");
            if(n.WorkedDay.GetValueOrDefault(role,-1)>=day)return false;
            int cost=Native012Energy(s,role);Need(paidEnergy==cost&&(cost==0||source>0),"实际工位电量不完整");
            foreach(var job in Native012Jobs(s,role).Where(j=>!j.Delivered).ToArray()){
                while(!job.Ready&&job.Steps[job.Step].Id!="test")FinishNativeStep(s,job,day,source);
            }
            if(Native012Jobs(s,role).Any(j=>!j.Ready&&j.Steps[j.Step].Id=="test"))n.TestPower[role]=source;
            n.WorkedDay[role]=day;n.Detected.Remove(role);return true;
        }
        public static bool DetectNative012(StoryState s,string role,int day)
        {
            var c=s.CampaignItems;var n=c.Native012;Need(n!=null,"没有012原生委托");
            foreach(var j in Native012Jobs(s,role).Where(j=>!j.Delivered&&!j.Ready).ToArray()){
                Need(j.Steps[j.Step].Id=="test"&&n.TestPower.TryGetValue(role,out int source),"先在整备台完成日结加工");
                FinishNativeStep(s,j,day,n.TestPower[role]);
            }
            Need(Native012Jobs(s,role).Any()&&Native012Jobs(s,role).All(j=>j.Ready),"仍有未完成工序");
            var r=c.Originals[role];string signature=ConfigSignature(r);
            if(n.Detected.GetValueOrDefault(role)==signature)return false;
            r.CalibratedRevision=r.ConfigRevision;n.Detected[role]=signature;return true;
        }
        public static string Native012Block(StoryState s,string role,int day)
        {
            var c=s.CampaignItems;var n=c?.Native012;if(n==null)return "没有012委托";
            var jobs=Native012Jobs(s,role).Where(j=>!j.Delivered).ToArray();if(jobs.Length==0)return null;
            if(jobs.Any(j=>!j.Ready))return "请先把原琴、随附耗材和工具放入整备台，休息一晚后完成加工。";
            if(jobs.Any(j=>day<j.EarliestDay))return "这份委托最早次日交付。";
            return n.Detected.GetValueOrDefault(role)==ConfigSignature(c.Originals[role])?null:"请用检测仪确认原件的当前配件配置。";
        }
        public static string Native012CallBlock(StoryState s,int day)
        {
            var roles=Native012Jobs(s).Where(j=>!j.Delivered).Select(j=>j.HostRole).Distinct().ToArray();
            if(roles.Length==0)return null;
            var blocks=roles.Select(role=>Native012Block(s,role,day)).ToArray();
            return blocks.Any(x=>x==null)?null:blocks.First();
        }
        public static int Native012Price(StoryState s,string role)=>Native012Jobs(s,role).Where(j=>!j.Delivered).Sum(j=>j.LaborReference);
        // Called only after native SellItem has transferred the actual registered entity.
        public static void DeliverNative012(StoryState s,int entity,string actor,int day,int paid,string snapshot)
        {
            var c=s.CampaignItems;var n=c.Native012;Need(n!=null,"没有012委托");
            if(n.Settled.ContainsKey(entity))throw new InvalidOperationException("该原件已交回，不能重复结算");
            var r=c.Originals.Values.Single(x=>x.Entity==entity);Need(actor=="ms_nagi"&&r.Custodian=="player"&&r.Location=="native", "当前顾客不是本单物主");
            Need(Native012Block(s,r.Role,day)==null,Native012Block(s,r.Role,day));
            Need(paid>=0&&s.Economy.Wallets[actor].Balance>=paid&&!string.IsNullOrEmpty(snapshot),"实际付款或原件快照无效");
            s.Economy.Wallets[actor].Balance-=paid;
            var jobs=Native012Jobs(s,r.Role).Where(x=>!x.Delivered).ToArray();int remaining=paid,total=jobs.Sum(x=>x.LaborReference);
            var charged=jobs.Where(x=>x.LaborReference>0).ToArray();
            foreach(var j in jobs){
                int share=j.LaborReference==0?0:ReferenceEquals(j,charged.Last())?remaining:(int)((long)paid*j.LaborReference/Math.Max(1,total));remaining-=share;
                j.Quote=share;j.Delivered=true;j.FeeState=j.LaborReference==0?"free":share==0?"waived":"paid";
                foreach(var resource in j.Resources.Where(x=>x.Tool||x.State is "available" or "unused_customer_output"))resource.State="returned_to_customer";
                c.Receipts.Add(j.Id+".delivered");
            }
            r.Snapshot=snapshot;r.Custodian=actor;r.Location="member";n.Settled.Add(entity,paid);
            History(s,r,"transfer","native012.counter",day,"custody_changed");
            // The ordinary economy ledger accepts positive cash trades only.
            // Free returns remain recorded in the custody settlement and history.
            if(paid>0)s.Economy.Trades["native012_"+entity]=new MemberTradeReceipt{ActorId=actor,EntityId=entity,MemberBuys=true,Price=paid};
        }
        private static void ValidateNative012(CampaignItemsState c)
        {
            var n=c.Native012;if(n==null)return;
            if(n.Version!=1||n.Note<0||n.Issued==null||n.Plans==null||n.WorkedDay==null||n.TestPower==null||n.Detected==null||n.Settled==null||n.Issued.Any(x=>x<=0)||n.Settled.Any(x=>x.Key<=0||x.Value<0)||n.Detected.Keys.Any(x=>!c.Originals.ContainsKey(x)))throw new InvalidDataException("012原生交接记录无效，保留旧档");
            if(n.Plans.Any(x=>!c.Originals.ContainsKey(x.Key)||x.Value.Machine<=0||x.Value.Day<0)||n.TestPower.Any(x=>x.Value<=0)||n.WorkedDay.Any(x=>x.Value<0)||n.Settled.Keys.Any(id=>!c.Originals.Values.Any(x=>x.Entity==id&&(x.Location=="member"||ReturnTransferred(c,id))))||n.Note>0&&(c.Originals.Values.Any(x=>x.Entity==n.Note)||c.Jobs.Values.SelectMany(x=>x.Resources).Any(x=>x.Entity==n.Note)))throw new InvalidDataException("012日结或交付身份冲突，保留旧档");
        }
    }
}
