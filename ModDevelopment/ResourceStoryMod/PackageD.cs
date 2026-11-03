using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ResourceStoryMod.Story
{
    public sealed class MusicTradeState
    {
        public MusicNextState Next {get;set;}=new();
        public int Version {get;set;}=1;
        public bool Enabled {get;set;}=true;
        public int CatalogVersion {get;set;}=1;
        public long Sequence {get;set;}
        public Dictionary<string,MusicSourceLot> SourceLots {get;set;}=new();
        public Dictionary<string,MusicBuyerVisit> Buyers {get;set;}=new();
        public Dictionary<string,MusicSlotTicket> Slots {get;set;}=new();
        public Dictionary<string,MusicTradeReceipt> Receipts {get;set;}=new();
        public Dictionary<int,MusicCost> Costs {get;set;}=new();
        public HashSet<string> OrdinaryFulfillments {get;set;}=new();
        public Dictionary<string,int> NativePickOrdinals {get;set;}=new();
        public string CurrentBuyer {get;set;}
        public Dictionary<string,MusicSalvage> Salvage {get;set;}=new();
        public HashSet<int> Destroyed {get;set;}=new();
        public Dictionary<string,MusicReferral> Referrals {get;set;}=new();
        public Dictionary<string,int> Openings {get;set;}=new();
        public Dictionary<string,int> Fees {get;set;}=new();
        public HashSet<string> Refunds {get;set;}=new();
        public HashSet<int> ReferralDays {get;set;}=new();
        public bool CommunityFreeUsed {get;set;}
        public MusicReferral PendingIntent {get;set;}
        public Dictionary<string,MusicHistory> Histories {get;set;}=new();
        public Dictionary<string,MusicInvestment> Investments {get;set;}=new();
        public HashSet<int> SplitCosts {get;set;}=new();
        public Dictionary<string,string> Returns {get;set;}=new();
    }
    public sealed class MusicSourceLot
    {
        public string Id {get;set;}
        public string Visit {get;set;}
        public string Template {get;set;}
        public int Entity {get;set;}
        public int[] Components {get;set;}=Array.Empty<int>();
        public string PurchaseReceipt {get;set;}
    }
    public sealed class MusicSlotTicket
    {
        public string Id {get;set;}
        public int Day {get;set;}
        public string OriginalClient {get;set;}
        public string Buyer {get;set;}
        public string Zone {get;set;}
        public string Source {get;set;}
        public string Category {get;set;}
        public int MusicShare {get;set;}
        public int[] MusicRoots {get;set;}=Array.Empty<int>();
    }
    public sealed class MusicBuyerVisit
    {
        public string Id {get;set;}
        public string Profile {get;set;}
        public string Ticket {get;set;}
        public string Zone {get;set;}
        public int Day {get;set;}
        public int BaseBudget {get;set;}
        public int InitialBudget {get;set;}
        public int RemainingBudget {get;set;}
        public int NativeCash {get;set;}
        public string Status {get;set;}="queued";
        public string Sprite {get;set;}
        public int BuyModifier {get;set;}
        public int SellModifier {get;set;}
        public bool AcceptMarkup {get;set;}
        public bool BusinessComplete {get;set;}
        public bool Introduced {get;set;}
        public bool DialogueStarted {get;set;}
        public bool AcceptDeal {get;set;}
        public string[] Variety {get;set;}=Array.Empty<string>();
        public int[] SoldEntities {get;set;}=Array.Empty<int>();
    }
    public sealed class MusicTradeReceipt
    {
        public string Id {get;set;}
        public string Kind {get;set;}
        public string Visit {get;set;}
        public int Day {get;set;}
        public int Total {get;set;}
        public Dictionary<int,int> Allocations {get;set;}=new();
    }
    public sealed class MusicCost
    {
        public int Entity {get;set;}
        public int Amount {get;set;}
        public string Receipt {get;set;}
        public bool Known {get;set;}
        public int Units {get;set;}=1;
    }
    public sealed class MusicUsedTemplate
    {
        public string Id,Host,Name;
        public Dictionary<string,string> Faults=new();
        public Dictionary<string,string> Installed=new();
        public int MiddleWeight,UpperWeight;
        public bool Irreparable;
        public int[] Band;
        public int ProQuota=>Installed.Count;
    }
    public sealed class MusicBuyerProfile
    {
        public string Id,Name;
        public int Budget;
        public string[] Hosts,Zones;
        public int[] Minimum;
        public bool Dirty,Faulty,Calibration;
        public int Weight;
    }
    public static class PackageDCatalog
    {
        public const string Guitar="ms_gear_commercial_guitar",Pedal="ms_gear_custom_overdrive",Monitor="ms_gear_monitor_speaker";
        public const string BuyerPrefix="millsage_trade_buyer_";
        public static readonly string[] Hosts={Pedal,Guitar,Monitor};
        public static readonly MusicUsedTemplate[] Used={
            T("U01",Pedal,"氧化效果器","surface","dirty_oxidized",50,50,1100,1300,1500),
            T("U02",Pedal,"线束故障效果器","wire","contact_fault",40,40,900,1100,1300),
            T("U03",Pedal,"高要价板故障效果器","board","structural_fault",10,10,1750,1900,2050),
            T("U04",Guitar,"氧化商业琴","surface","dirty_oxidized",45,35,1950,2150,2350),
            T("U05",Guitar,"线束故障商业琴","wire","contact_fault",55,45,1750,1950,2150),
            T("U06",Guitar,"带快接件的商业琴","surface","dirty_oxidized",0,20,2250,2450,2650,"interface","ms_module_quick_patch"),
            T("U07",Monitor,"氧化监听","surface","dirty_oxidized",45,40,1800,2000,2200),
            T("U08",Monitor,"单元故障监听","driver","structural_fault",55,50,1600,1800,2000),
            new(){Id="U09",Host=Monitor,Name="报废监听供体",MiddleWeight=0,UpperWeight=10,Irreparable=true,Band=new[]{240,280,360},Faults=new(){{"body","structural_fault"},{"board","structural_fault"},{"driver","structural_fault"}},Installed=new(){{"power","ms_module_quiet_power"}}}
        };
        private static MusicUsedTemplate T(string id,string host,string name,string domain,string fault,int middle,int upper,int low,int anchor,int high,string slot=null,string component=null)=>new(){Id=id,Host=host,Name=name,Faults=new(){{domain,fault}},MiddleWeight=middle,UpperWeight=upper,Band=new[]{low,anchor,high},Installed=slot==null?new():new(){{slot,component}}};
        public static readonly MusicBuyerProfile[] Buyers={
            B("B01","排练器材采购",2400,Hosts,new[]{50,55,50,45},40,dirty:true),
            B("B02","换场乐手",3300,new[]{Guitar},new[]{60,60,58,70},15),
            B("B03","低噪信号链采购",3200,new[]{Pedal},new[]{66,65,66,0},10),
            B("B04","固定监听采购",4200,new[]{Monitor},new[]{68,74,60,45},10),
            B("B05","专业规格采购",4800,new[]{Guitar},new[]{70,70,60,70},5,upper:true),
            B("B06","已知故障器材采购",1800,Hosts,new int[4],15,faulty:true),
            B("B07","日常维修料采购",200,Array.Empty<string>(),new int[4],5,materials:true)
        };
        private static MusicBuyerProfile B(string id,string name,int budget,string[] hosts,int[] min,int weight,bool dirty=false,bool faulty=false,bool upper=false,bool materials=false)=>new(){Id=id,Name=name,Budget=budget,Hosts=hosts,Minimum=min,Weight=weight,Dirty=dirty,Faulty=faulty,Calibration=!faulty&&!materials,Zones=materials?new[]{"lower","middle"}:upper?new[]{"upper"}:new[]{"middle","upper"}};
        public static MusicUsedTemplate Template(string id)=>Used.FirstOrDefault(x=>x.Id==id);
        public static MusicBuyerProfile Buyer(string id)=>Buyers.FirstOrDefault(x=>x.Id==id);
    }
    public static class PackageD
    {
        public static void Validate(MusicTradeState d)
        {
            if(d==null)return;
            if(d.Version!=1 || d.CatalogVersion!=1 || d.Sequence<0 || d.SourceLots==null || d.Buyers==null || d.Slots==null || d.Receipts==null || d.Costs==null || d.OrdinaryFulfillments==null || d.NativePickOrdinals==null)throw new InvalidDataException("普通音乐经营存档无效");
            foreach(var p in d.SourceLots){var l=p.Value;if(l==null || l.Id!=p.Key || l.Entity<=0 || PackageDCatalog.Template(l.Template)==null || l.Components==null || l.Components.Distinct().Count()!=l.Components.Length)throw new InvalidDataException("二手器材来源无效");}
            foreach(var p in d.Slots){var t=p.Value;if(t==null || t.Id!=p.Key || t.Day<0 || string.IsNullOrEmpty(t.OriginalClient) || (!string.IsNullOrEmpty(t.Buyer)&&!d.Buyers.ContainsKey(t.Buyer)))throw new InvalidDataException("普通来客位置无效");}
            foreach(var p in d.Buyers){var b=p.Value;if(b==null || b.Id!=p.Key || PackageDCatalog.Buyer(b.Profile)==null || !d.Slots.TryGetValue(b.Ticket,out var t) || t.Buyer!=b.Id || b.Day!=t.Day || b.RemainingBudget<0 || b.InitialBudget<b.RemainingBudget || b.BaseBudget!=PackageDCatalog.Buyer(b.Profile).Budget || b.SoldEntities==null || b.SoldEntities.Distinct().Count()!=b.SoldEntities.Length || !new[]{"queued","active","closed"}.Contains(b.Status))throw new InvalidDataException("音乐买家预算或位置无效");}
            if(d.CurrentBuyer!=null && (!d.Buyers.ContainsKey(d.CurrentBuyer) || d.Buyers[d.CurrentBuyer].Status!="active"))throw new InvalidDataException("当前音乐买家记录无效");
            foreach(var r in d.Receipts){if(r.Value==null || r.Value.Id!=r.Key || r.Value.Total<0 || r.Value.Allocations==null || r.Value.Allocations.Any(x=>x.Key<=0 || x.Value<0) || r.Value.Allocations.Values.Sum(x=>(long)x)!=r.Value.Total)throw new InvalidDataException("音乐交易账单不完整");}
            foreach(var c in d.Costs)if(c.Value==null || c.Value.Entity!=c.Key || c.Value.Amount<0 || (c.Value.Known&&!d.Receipts.ContainsKey(c.Value.Receipt)))throw new InvalidDataException("音乐库存成本无效");
            PackageDFull.Validate(d);
            PackageDNext.Validate(d.Next);
        }
        public static Dictionary<int,int> Allocate(int total,Dictionary<int,int> weights)
        {
            PackageB.Need(total>=0 && weights.Count>0 && weights.All(x=>x.Key>0 && x.Value>=0),"付款分摊参数无效");
            long sum=weights.Values.Sum(x=>(long)x);PackageB.Need(sum>0 || total==0,"无法确认批次成本，保留未知状态");
            var result=new Dictionary<int,int>();int remaining=total;var entries=weights.OrderBy(x=>x.Key).ToArray();
            for(int i=0;i<entries.Length;i++){int n=i==entries.Length-1?remaining:sum==0?0:(int)((long)total*entries[i].Value/sum);result[entries[i].Key]=n;remaining-=n;}
            return result;
        }
        public static bool Purchase(StoryState s,string receipt,string visit,int day,int paid,Dictionary<int,int> weights)
        {
            var d=s.MusicTrade;if(d.Receipts.TryGetValue(receipt,out var prior)){PackageB.Need(prior.Kind=="purchase"&&prior.Total==paid&&prior.Visit==visit&&prior.Allocations.Keys.OrderBy(x=>x).SequenceEqual(weights.Keys.OrderBy(x=>x)),"重复付款回执内容冲突");return false;}
            var allocated=Allocate(paid,weights);d.Receipts.Add(receipt,new(){Id=receipt,Kind="purchase",Visit=visit,Day=day,Total=paid,Allocations=allocated});
            foreach(var p in allocated){d.Costs[p.Key]=new(){Entity=p.Key,Amount=p.Value,Receipt=receipt,Known=true};foreach(var lot in d.SourceLots.Values.Where(x=>x.Entity==p.Key))lot.PurchaseReceipt=receipt;}
            return true;
        }
        public static string Eligible(StoryState s,MusicBuyerProfile profile,int entity,string zone,bool playerOwned,bool protectedItem,int quote,int budget,bool enforceBudget=true)
        {
            if(profile==null || !profile.Zones.Contains(zone))return "买家所在地区不匹配";
            if(!playerOwned || protectedItem)return "原件不可出售或物主不符";
            if(s.MusicTrade.Destroyed.Contains(entity))return "原件已报废";
            if(quote<0 || (enforceBudget&&quote>budget))return "报价超过剩余预算";
            var a=s.Workshop.Advanced;var b=s.Economy.Business;
            if(!a.Facts.TryGetValue(entity,out var f) || f.Owner!="player")return "没有玩家自有原件记录";
            var retail=PackageBCatalog.Item(f.Sku);
            if(retail!=null&&!PackageBCatalog.Split(retail.GetValueOrDefault("ordinary_retail_zones")).Contains(zone))return "商品在本地区不可零售";
            var product=MusicCatalog.Find(f.Sku);if(retail==null&&product!=null&&!product.RetailZones.Contains(zone))return "商品在本地区不可零售";
            if(a.Services.Values.Any(x=>!x.Terminal&&(x.Host==entity||x.Held.ContainsKey(entity))))return "原件仍在工单中";
            if(profile.Id=="B07"){var row=PackageBCatalog.Item(f.Sku);return row!=null && new[]{"MATERIAL","TOOL"}.Contains(row.GetValueOrDefault("native_item_type"))||new[]{"metal_ingot","printer_plastic","wire","common_electronic","rare_electronic","common_chemical","screwdriver","wire_cutter","welder","magnifier"}.Contains(f.Sku)?null:"本次只采购维修材料或工具";}
            if(retail?.GetValueOrDefault("native_item_type")=="MODULE"||product?.NativeType=="MODULE"){
                bool accepted=profile.Id=="B01"&&new[]{"ms_module_quick_patch","ms_module_tone_filter"}.Contains(f.Sku)
                    ||profile.Id=="B02"&&f.Sku=="ms_module_quick_patch"
                    ||profile.Id=="B03"&&f.Sku=="ms_module_tone_filter"&&PackageBCatalog.Rank(f.Grade)>=PackageBCatalog.Rank("selected")
                    ||profile.Id=="B04"&&new[]{"ms_module_quiet_power","ms_module_stable_bus"}.Contains(f.Sku)
                    ||profile.Id=="B05"&&f.Sku=="ms_part_stabilizer_pickup_commercial"&&PackageBCatalog.Rank(f.Grade)>=PackageBCatalog.Rank("selected");
                return accepted&&f.Faults.Count==0&&(!b.Components.TryGetValue(entity,out var component)||component.Location!="installed")?null:"松散模块型号、品质或状态不符合本次用途";
            }
            if(!profile.Hosts.Contains(f.Sku) || !b.Instruments.TryGetValue(entity,out var gear))return "器材类型不匹配";
            if(s.MusicTrade.SourceLots.Values.Any(x=>x.Entity==entity && PackageDCatalog.Template(x.Template).Irreparable) && !profile.Faulty)return "这是明确报废的供体";
            var active=f.Faults.Where(x=>!gear.Slots.ContainsKey(x.Key=="board"?"service.board":x.Key=="driver"?"service.driver":x.Key=="power"?"power":"__none")).ToArray();
            if(profile.Faulty){if(active.Length==0 || active.Any(x=>!PackageB.Known(a,entity,x.Key)))return "本次只收已查明并披露的故障器材";return null;}
            if(active.Any(x=>x.Value!="dirty_oxidized"||!profile.Dirty) || gear.Slots.Values.Any(id=>a.Facts[id].Faults.Count>0))return "状态未达到本次用途要求";
            if(active.Any(x=>!PackageB.Known(a,entity,x.Key)))return "尚未披露当前器材状态";
            if(profile.Calibration && (!a.Calibrations.TryGetValue(entity+":basic",out var report) || !report.Passed || report.Hash!=PackageB.MeasurementHash(a,b,entity,"basic",gear.Slots)))return "当前配置缺少有效基础校准";
            var stats=PackageB.Stats(a,b,entity);string[] keys={"noiseControl","stability","response","portability"};
            for(int i=0;i<4;i++)if(stats[keys[i]]<profile.Minimum[i])return "用途指标不足："+keys[i];
            return null;
        }
        public static bool Sale(StoryState s,string id,string buyerId,int entity,int paid,int remaining,int cash)
        {
            var d=s.MusicTrade;if(d.Receipts.TryGetValue(id,out var old)){PackageB.Need(old.Kind=="sale"&&old.Visit==buyerId&&old.Total==paid&&old.Allocations.ContainsKey(entity),"重复销售回执冲突");return false;}
            var v=d.Buyers[buyerId];PackageB.Need(paid>=0&&v.RemainingBudget>=paid&&remaining==v.RemainingBudget-paid&&!v.SoldEntities.Contains(entity),"原生预算扣减与成交不一致");
            d.Next.Realized[id]=new(){Receipt=id,Cost=PackageDFull.TotalCost(s,entity)};
            v.RemainingBudget=remaining;v.NativeCash=cash;v.SoldEntities=v.SoldEntities.Append(entity).ToArray();d.Receipts[id]=new(){Id=id,Kind="sale",Visit=buyerId,Day=v.Day,Total=paid,Allocations=new(){{entity,paid}}};
            PackageDFull.History(s,"delivered_"+id,entity,v.Day,"sale","已合法交付普通音乐买家；实际收款 "+paid+"。");
            if(paid>0)d.OrdinaryFulfillments.Add(v.Ticket);return true;
        }
    }
}
