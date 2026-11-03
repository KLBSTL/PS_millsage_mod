using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ResourceStoryMod.Story
{
    public sealed class MusicSalvage {public string Id{get;set;} public int Host{get;set;} public int Day{get;set;} public bool Complete{get;set;} public int[] Outputs{get;set;}=Array.Empty<int>();}
    public sealed class MusicReferral {
        public string Id{get;set;} public string Tier{get;set;} public string Kind{get;set;} public string Target{get;set;} public string Zone{get;set;}
        public int Day{get;set;} public int ConfirmedDay{get;set;} public int Fee{get;set;} public string Status{get;set;}="pending_slot";public string Ticket{get;set;} public string Client{get;set;}
    }
    public sealed class MusicHistory {public string Id{get;set;} public int Entity{get;set;} public int Day{get;set;} public string Kind{get;set;} public string Text{get;set;} public bool Public{get;set;}=true;public string Configuration{get;set;}}
    public sealed class MusicInvestment {public string Id{get;set;} public int Entity{get;set;} public string Job{get;set;} public int Amount{get;set;} public bool Known{get;set;} public int Units{get;set;} public int Source{get;set;} public int Held{get;set;} public bool Energy{get;set;} public bool Allocated{get;set;}}
    public sealed class MusicDisplaySample {public int Entity;public string Category;public int Value;public bool Music;}
    public static class PackageDFull
    {
        public static void Validate(MusicTradeState d){
            if(d.Salvage==null||d.Destroyed==null||d.Referrals==null||d.Openings==null||d.Fees==null||d.Refunds==null||d.ReferralDays==null||d.Histories==null||d.Investments==null||d.SplitCosts==null||d.Returns==null)throw new InvalidDataException("经营扩展记录不完整");
            foreach(var p in d.Salvage)if(p.Value==null||p.Key!=p.Value.Id||p.Value.Host<=0||p.Value.Outputs==null||p.Value.Outputs.Contains(p.Value.Host)||p.Value.Outputs.Distinct().Count()!=p.Value.Outputs.Length||(p.Value.Complete&&!d.Destroyed.Contains(p.Value.Host)))throw new InvalidDataException("拆解源和产物身份无效");
            if(d.Destroyed.Any(id=>!d.Salvage.Values.Any(x=>x.Host==id&&x.Complete)))throw new InvalidDataException("报废墓碑缺少完成回执");
            if(d.Salvage.Values.GroupBy(x=>x.Host).Any(x=>x.Count(y=>y.Complete)>1))throw new InvalidDataException("同一源重复拆解");
            foreach(var p in d.Referrals){var r=p.Value;if(r==null||r.Id!=p.Key||r.Day<=r.ConfirmedDay||r.Fee<0||!new[]{"pending_slot","confirmed","arrived","cancelled","refunded"}.Contains(r.Status))throw new InvalidDataException("普通预约无效");}
            foreach(var p in d.Fees)if(p.Value<0)throw new InvalidDataException("经营费用无效");
            foreach(string id in d.Refunds)if(!d.Fees.ContainsKey(id))throw new InvalidDataException("退款缺少原费用");
            foreach(var p in d.Histories)if(p.Value==null||p.Value.Id!=p.Key||p.Value.Entity<=0||string.IsNullOrWhiteSpace(p.Value.Text))throw new InvalidDataException("履历记录无效");
            foreach(var p in d.Investments)if(p.Value==null||p.Value.Id!=p.Key||p.Value.Amount<0||p.Value.Units<=0)throw new InvalidDataException("实际投入无效");
        }
        public static string SalvageBlock(StoryState s,int entity,bool owned,bool protectedItem){
            if(!owned||protectedItem||s.MusicTrade.Destroyed.Contains(entity))return "原件不是可拆解的玩家自有物";
            if(s.CampaignItems?.Originals.Values.Any(x=>x.Entity==entity)==true)return "剧情登记原件不可报废";
            if(!s.Workshop.Advanced.Facts.TryGetValue(entity,out var f)||f.Owner!="player"||!PackageDCatalog.Hosts.Contains(f.Sku))return "缺少合法三宿主原件记录";
            if(!s.Economy.Business.Instruments.TryGetValue(entity,out var g)||g.Owner!="player"||g.Custody!="player")return "原件仍有保管或物权义务";
            if(g.Slots.Count>0)return "先用现有工序卸下所有真实配件";
            if(s.Workshop.Advanced.Services.Values.Any(x=>!x.Terminal&&x.Host==entity))return "请先结清或取消已有维护工单";
            if(s.MusicTrade.Salvage.Values.Any(x=>x.Host==entity&&s.Workshop.Jobs.TryGetValue(x.Id,out var j)&&j.Status!="cancelled"))return "原件已有拆解记录";
            return null;
        }
        public static Dictionary<string,int> SalvageOutputs(string sku)=>sku==PackageDCatalog.Monitor?new(){{"metal_ingot",1},{"printer_plastic",1}}:new(){{"printer_plastic",1}};
        public static WorkshopSpec SalvageSpec(string sku)=>new(){Recipe="d_salvage_"+sku,Station="electrobench",Purpose="d_salvage",DurationMs=90000,Energy=0,Inputs=new(){{sku,1}},Tools=new(){{"ref_screwdriver",1},{"ref_wirecutter",1}},Output="printer_plastic",Quantity=SalvageOutputs(sku).Values.Sum(),OutputBatch=SalvageOutputs(sku)};
        public static void FinishSalvage(StoryState s,string id,int day){
            var d=s.MusicTrade;if(!d.Salvage.TryGetValue(id,out var r)||r.Complete)return;var job=s.Workshop.Jobs[id];PackageB.Need(job.InputsConsumed&&job.Status=="ready","拆解尚未实际完成");
            r.Complete=true;r.Outputs=job.Outputs.Select(x=>x.Entity).ToArray();d.Destroyed.Add(r.Host);s.Workshop.Advanced.Facts[r.Host].Owner="outside";var g=s.Economy.Business.Instruments[r.Host];g.Owner="outside";g.Custody="outside";
            var weights=job.Outputs.ToDictionary(x=>x.Entity,x=>x.ItemId=="metal_ingot"?50:12);
            int? total=TotalCost(s,r.Host);
            if(d.Costs.TryGetValue(r.Host,out var cost)&&total.HasValue){var allocated=PackageD.Allocate(total.Value,weights);cost.Amount=0;foreach(var a in allocated)d.Costs[a.Key]=new(){Entity=a.Key,Known=true,Amount=a.Value,Receipt=cost.Receipt};}
            else foreach(int entity in weights.Keys)d.Costs[entity]=new(){Entity=entity,Known=false};
            foreach(var expense in d.Investments.Values.Where(x=>x.Entity==r.Host))expense.Allocated=true;
            History(s,"salvage_"+id,r.Host,day,"salvage","已机械报废；原件不再作为库存，固定残料一次登记。");
            PackageDNext.Case(s,"case_salvage_"+id,r.Host,day,"salvage",id,"实际拆解的模块/残料仍是存货；未实际售出不计收入。");
            if(d.Next.Cases.TryGetValue("case_salvage_"+id,out var example)){example.Cost=total;example.After=new(){{"产物",string.Join("、",job.Outputs.Select(x=>x.ItemId+"×"+x.Units))}};}
        }
        public static void SplitSourceCost(StoryState s,int host){
            var d=s.MusicTrade;if(d.SplitCosts.Contains(host)||!s.Economy.Business.Instruments.TryGetValue(host,out var g))return;
            var missing=g.Slots.Values.Where(id=>!d.Costs.ContainsKey(id)).ToArray();
            if(d.Costs.TryGetValue(host,out var cost)&&cost.Known){
                var values=missing.ToDictionary(id=>id,id=>Math.Max(0,PackageB.LeafValue(s.Workshop.Advanced,id)));
                values[host]=Math.Max(0,PackageB.Value(s.Workshop.Advanced,s.Economy.Business,host)-g.Slots.Values.Sum(id=>Math.Max(0,PackageB.LeafValue(s.Workshop.Advanced,id))));
                if(values.Values.Sum()>0){foreach(var a in PackageD.Allocate(cost.Amount,values))d.Costs[a.Key]=new(){Entity=a.Key,Amount=a.Value,Known=true,Receipt=cost.Receipt};}
                else foreach(int id in missing)d.Costs[id]=new(){Entity=id,Known=false};
            }else foreach(int id in missing)d.Costs[id]=new(){Entity=id,Known=false};
            d.SplitCosts.Add(host);
        }
        public static int? TotalCost(StoryState s,int host){
            var d=s.MusicTrade;var ids=new HashSet<int>{host};if(d.SplitCosts.Contains(host)&&s.Economy.Business.Instruments.TryGetValue(host,out var g))ids.UnionWith(g.Slots.Values);
            if(ids.Any(id=>!d.Costs.TryGetValue(id,out var c)||!c.Known))return null;
            var inputs=d.Investments.Values.Where(x=>x.Entity==host&&!x.Allocated).ToArray();if(inputs.Any(x=>!x.Known))return null;
            return checked(ids.Sum(id=>d.Costs[id].Amount)+inputs.Sum(x=>x.Amount));
        }
        public static void History(StoryState s,string id,int entity,int day,string kind,string text,bool visible=true){
            if(s.MusicTrade.Histories.ContainsKey(id))return;s.MusicTrade.Histories[id]=new(){Id=id,Entity=entity,Day=day,Kind=kind,Text=text,Public=visible};
        }
        public static string Summary(StoryState s,int entity)=>string.Join("\n",s.MusicTrade.Histories.Values.Where(x=>x.Entity==entity&&x.Public).OrderByDescending(x=>x.Day).ThenByDescending(x=>x.Id).Take(2).Reverse().Select(x=>"第"+x.Day+"天："+x.Text));
        public static string DemandMode(StoryState s){
            var leads=s.CampaignItems?.BusinessLeads;bool independent=leads?.Contains("independent_refurbishment")==true,commercial=leads?.Contains("commercial_brand_consistency")==true;
            if(independent!=commercial)return independent?"independent":"commercial";
            if(!s.CompletedDays.ContainsKey("ms_scene_031"))return "default";
            string ending=s.CampaignItems==null?null:PackageC.EndingOf(s);return ending=="commercial"?"commercial":ending=="rainbow"||ending=="shared"?"independent":"default";
        }
        public static int DemandWeight(StoryState s,string profile){int i=Array.FindIndex(PackageDCatalog.Buyers,x=>x.Id==profile);return (DemandMode(s)=="independent"?new[]{42,18,6,6,2,21,5}:DemandMode(s)=="commercial"?new[]{32,12,14,18,14,5,5}:new[]{40,15,10,10,5,15,5})[i];}
        public static bool NaturalSlotAvailable(StoryState s,int day)=>s.MusicTrade.Buyers.Values.Count(x=>x.Day==day)+s.MusicTrade.Referrals.Values.Count(x=>x.Day==day&&x.Kind=="buyer"&&x.Status=="confirmed")+s.MusicTrade.Next.Visits.Values.Count(x=>x.Day==day)+s.MusicTrade.Next.Uses.Values.Count(x=>x.DueDay==day&&x.Status is "pickup" or "cancel_pickup"&&!s.MusicTrade.Next.Visits.Values.Any(v=>v.Entity==x.Entity&&v.Day==day))<2;
        public static int DisplayShare(IEnumerable<MusicDisplaySample> samples,string category){var rows=samples.Where(x=>x.Category==category&&x.Value>0).GroupBy(x=>x.Entity).Select(x=>x.First()).ToArray();long total=rows.Sum(x=>(long)x.Value);return total==0?0:(int)(10000*rows.Where(x=>x.Music).Sum(x=>(long)x.Value)/total);}
    }
    public static class PackageDNetwork
    {
        public static int Opening(string tier)=>tier=="community"?300:tier=="practice"?900:tier=="stage"?2400:throw new InvalidOperationException("未知音乐业务");
        public static int Fee(string tier)=>tier=="community"?20:tier=="practice"?80:tier=="stage"?240:tier=="archive"?360:throw new InvalidOperationException("未知推荐方向");
        public static string Business(string tier)=>tier=="archive"?"stage":tier;
        public static string Qualification(StoryState s,string tier,bool middleOpen){
            int ordinary=s.MusicTrade.OrdinaryFulfillments.Count;int upper=s.MusicTrade.OrdinaryFulfillments.Count(id=>s.MusicTrade.Slots.TryGetValue(id,out var t)&&(t.Zone=="middle"||t.Zone=="upper"));
            if(tier=="community")return s.CompletedDays.ContainsKey("ms_scene_005")||ordinary>=1?null:"需要005介绍或一次真实普通成交";
            if(!middleOpen)return "需要中层已开放";if(tier=="practice")return ordinary>=3?null:"需要三个不同普通来访成交";
            return upper>=(s.CompletedDays.ContainsKey("ms_scene_016")?1:3)?null:"需要中/上层有效成交（016后一次，否则三次）";
        }
        public static bool Open(StoryState s,string tier,int day){string business=Business(tier);if(s.MusicTrade.Openings.ContainsKey(business))return false;int fee=Opening(business);s.MusicTrade.Openings[business]=day;s.MusicTrade.Fees["open_"+business]=fee;return true;}
        public static MusicReferral Preview(StoryState s,string tier,string kind,string target,string zone,int day){
            PackageB.Need(s.MusicTrade.Openings.ContainsKey(Business(tier)),"尚未开通对应业务");PackageB.Need(tier!="archive","目前没有合格公开普通档案商品，不收费");
            PackageB.Need(kind=="buyer"||kind=="seller","推荐方向无效");PackageB.Need(!s.MusicTrade.ReferralDays.Contains(day),"今天已经安排过一次普通推荐");
            string required=kind=="buyer"?(target=="B07"?"community":new[]{"B01","B02","B06"}.Contains(target)?"practice":"stage"):(zone=="lower"?"community":zone=="middle"?"practice":"stage");
            PackageB.Need(tier==required,"该需求需要对应业务档位");
            string id="d_ref_"+day+"_"+kind+"_"+target;return new(){Id=id,Tier=tier,Kind=kind,Target=target,Zone=zone,Day=day+1,ConfirmedDay=day};
        }
        public static int Confirm(StoryState s,MusicReferral r,int capacity){
            var d=s.MusicTrade;if(d.Referrals.TryGetValue(r.Id,out var prior)){PackageB.Need(prior.Target==r.Target&&prior.Kind==r.Kind,"推荐回执冲突");return 0;}
            PackageB.Need(!d.ReferralDays.Contains(r.ConfirmedDay),"今日推荐次数已用");
            var intent=d.PendingIntent;
            PackageB.Need(intent==null||intent.Tier==r.Tier&&intent.Kind==r.Kind&&intent.Target==r.Target&&intent.Zone==r.Zone,"请先处理已有的未收费意向");
            if(capacity<=0){d.PendingIntent=r;return 0;}
            PackageB.Need(r.Kind!="buyer"||d.Referrals.Values.Count(x=>x.Day==r.Day&&x.Kind=="buyer"&&x.Status=="confirmed")+d.Buyers.Values.Count(x=>x.Day==r.Day)+d.Next.Visits.Values.Count(x=>x.Day==r.Day)+d.Next.Uses.Values.Count(x=>x.DueDay==r.Day&&x.Status is "pickup" or "cancel_pickup"&&!d.Next.Visits.Values.Any(v=>v.Entity==x.Entity&&v.Day==r.Day))<2,"明日音乐买家位置已满");
            if(r.Kind=="seller")PackageB.Need(!d.Referrals.Values.Any(x=>x.Kind=="seller"&&x.Target==r.Target&&x.Status=="confirmed"),"这个卖方已有回访预约");
            int fee=r.Tier=="community"&&!d.CommunityFreeUsed?0:Fee(r.Tier);r.Fee=fee;r.Status="confirmed";d.Referrals[r.Id]=r;d.Fees[r.Id]=fee;d.ReferralDays.Add(r.ConfirmedDay);d.PendingIntent=null;if(r.Tier=="community")d.CommunityFreeUsed=true;return fee;
        }
        public static int Fail(StoryState s,string id){var d=s.MusicTrade;var r=d.Referrals[id];if(r.Status is "refunded" or "cancelled" or "arrived")return 0;r.Status="refunded";if(!d.Refunds.Add(id))return 0;return r.Fee;}
        public static void CancelIntent(StoryState s,string id)
        {
            PackageB.Need(s.MusicTrade.PendingIntent?.Id==id,"这条未收费意向已经变更");
            s.MusicTrade.PendingIntent=null;
        }
        public static void Cancel(StoryState s,string id){var r=s.MusicTrade.Referrals[id];PackageB.Need(r.Status=="confirmed","本预约不可取消");r.Status="cancelled";}
    }
}
