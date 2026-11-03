using System;
using System.Collections.Generic;
using System.Linq;

namespace ResourceStoryMod.Story
{
    public static class PackageDUse
    {
        public static MusicUseCase ForService(StoryState s,string service)=>s.MusicTrade?.Next.Uses.Values.FirstOrDefault(x=>x.Service==service);
        public static bool CanAuthorize(StoryState s,int entity)=>s.MusicTrade?.Next.Uses.TryGetValue(entity,out var u)==true&&u.Status=="offered"&&!s.MusicTrade.Next.Uses.Values.Any(x=>x.Status is "servicing" or "pickup" or "cancel_pickup");
        public static void Sold(StoryState s,int entity,string receipt,string buyer,string snapshot,Dictionary<int,string> components)
        {
            var n=s.MusicTrade.Next;if(n.Uses.ContainsKey(entity)||s.CampaignItems?.Originals.Values.Any(x=>x.Entity==entity)==true||!s.MusicTrade.Buyers.TryGetValue(buyer,out var v)||!new[]{"B01","B02","B03","B04","B05"}.Contains(v.Profile)||!s.Economy.Business.Instruments.TryGetValue(entity,out var g)||!PackageDCatalog.Hosts.Contains(g.ItemId))return;
            PackageB.Need(s.MusicTrade.Receipts.TryGetValue(receipt,out var r)&&r.Allocations.ContainsKey(entity)&&!string.IsNullOrWhiteSpace(snapshot),"续用需要真实出站凭据");
            string target=g.ItemId==PackageDCatalog.Guitar?"B02":g.ItemId==PackageDCatalog.Pedal?"B03":"B04";
            n.Uses.Add(entity,new(){Entity=entity,Customer="customer_"+buyer,Origin=receipt,Profile=v.Profile,Target=target,Zone=v.Zone,Sku=g.ItemId,Sprite=v.Sprite,Snapshot=snapshot,Components=new(components),SoldDay=v.Day,SoldOrdinal=n.BusinessOrdinal});
        }
        public static bool Eligible(StoryState s,MusicUseCase u,int day,string zone)
        {
            var n=s.MusicTrade.Next;return u.Status=="outside"&&u.Zone==zone&&s.Economy.Business.Instruments.TryGetValue(u.Entity,out var gear)&&gear.Owner=="outside"&&gear.Custody=="outside"&&s.Workshop.Advanced.Facts[u.Entity].Owner=="outside"&&day>u.SoldDay&&n.BusinessOrdinal-u.SoldOrdinal>=3&&n.BusinessOrdinal-n.LastNaturalDay>=3&&!n.Uses.Values.Any(x=>x.Status is "offered" or "servicing" or "pickup" or "cancel_pickup")&&!n.Visits.Values.Any(v=>v.Entity==u.Entity&&v.Status!="closed");
        }
        public static void Offer(StoryState s,int entity,string id,string ticket,int day,int budget,int cash)
        {
            var n=s.MusicTrade.Next;var u=n.Uses[entity];PackageB.Need(u.Status=="outside"&&budget>=0&&cash>=0&&!n.Visits.ContainsKey(id),"续用来访重复或资金无效");
            u.Budget=budget;u.Cash=cash;u.BudgetInitialized=true;u.Status="offered";n.LastNaturalDay=n.BusinessOrdinal;
            n.Visits.Add(id,new(){Id=id,Ticket=ticket,Entity=entity,Day=day,Kind="offer"});
        }
        public static void Authorize(StoryState s,int entity,string service,int quote,string snapshot)
        {
            var u=s.MusicTrade.Next.Uses[entity];PackageB.Need(CanAuthorize(s,entity)&&quote>=0&&quote<=Math.Min(u.Budget,u.Cash),"普通客户未授权、保管已满或报价超过当前支付能力");
            var f=s.Workshop.Advanced.Facts[entity];var g=s.Economy.Business.Instruments[entity];PackageB.Need(f.Owner=="outside"&&g.Owner=="outside","外部原件物主不一致");
            PackageB.Authorize(s,service,"outside",entity,snapshot);u.Service=service;u.AuthorizedQuote=quote;u.Status="servicing";
        }
        public static void CheckStep(StoryState s,EquipmentService svc,string kind,string slot,int component)
        {
            var u=ForService(s,svc.Id);PackageB.Need(u!=null&&u.Status=="servicing","普通续用授权不在办理状态");
            PackageB.Need(kind.EndsWith("inspect_basic")||kind.EndsWith("inspect_detailed")||kind.EndsWith("calibrate")||kind.EndsWith("install_simple")||kind.EndsWith("install_precision")||u.AcquisitionPaid.HasValue&&kind=="b15_s03_clean","普通续用只允许检查、清洁、校准和空槽适配");
            if(kind.Contains("install_")){
                PackageB.Need(!svc.InitialSlots.ContainsKey(slot),"原有部件保留；首批仅补空槽，不替换原装件");
                if(component>0)PackageB.Need(!svc.EffectiveSlots.ContainsKey(slot),"先按实际工序拆回当前垫件，不能重复替换收费");
            }
        }
        public static string ReadyBlock(StoryState s,MusicUseCase u)
        {
            if(u.Service==null)return "尚未受理";var svc=s.Workshop.Advanced.Services[u.Service];
            if(svc.Steps.Any(x=>x.Status=="running"))return "工序尚未完成";
            var copy=System.Text.Json.JsonSerializer.Deserialize<StoryState>(System.Text.Json.JsonSerializer.Serialize(s,StoryContent.Json),StoryContent.Json);var g=copy.Economy.Business.Instruments[u.Entity];g.Owner="player";g.Custody="player";g.Slots=new(svc.EffectiveSlots);copy.Workshop.Advanced.Facts[u.Entity].Owner="player";
            copy.Workshop.Advanced.Services[svc.Id].Status="delivered";
            foreach(int id in g.Slots.Values)copy.Workshop.Advanced.Facts[id].Owner="player";
            return PackageD.Eligible(copy,PackageDCatalog.Buyer(u.Target),u.Entity,u.Zone,true,false,0,0,false);
        }
        public static void Book(StoryState s,int entity,int day,bool cancel=false)
        {
            var u=s.MusicTrade.Next.Uses[entity];PackageB.Need(u.Status=="servicing"||u.Status=="pickup"||u.Status=="cancel_pickup","没有待归还的普通原件");
            if(!cancel)PackageB.Need(ReadyBlock(s,u)==null,"当前配置尚未满足本次用途");
            if(u.DueDay>=0)return;u.Status=cancel?"cancel_pickup":"pickup";u.DueDay=day+1;
        }
        public static void CheckCancel(StoryState s,string service)
        {
            var svc=s.Workshop.Advanced.Services[service];PackageB.Need(svc.InitialSlots.OrderBy(x=>x.Key).SequenceEqual(svc.EffectiveSlots.OrderBy(x=>x.Key)),"先用实际卸件工序取回新增垫件；取消不能免费撤销安装");
        }
        public static void Delivery(StoryState s,EquipmentService svc,string receipt,int price,int day)
        {
            CheckDelivery(s,svc,price,day);var u=ForService(s,svc.Id);
            u.Budget-=price;u.Cash-=price;u.Paid=price;u.Receipt=receipt;u.Status="done";u.DueDay=-1;u.Snapshot=svc.HostSnapshot;u.Components=s.Economy.Business.Instruments[u.Entity].Slots.Values.ToDictionary(id=>id,id=>s.Economy.Business.Components[id].Snapshot);
            PackageDFull.History(s,"use_"+receipt,u.Entity,day,"service","已按新用途完成普通物主原件适配；原件与旧配件保留。");
            PackageDNext.Case(s,"case_"+receipt,u.Entity,day,"service",receipt,"保留旧件解决新用途；只计实际完成工序与交付。");
        }
        public static void CheckDelivery(StoryState s,EquipmentService svc,int price,int day)
        {
            var u=ForService(s,svc.Id);PackageB.Need(u!=null&&u.Status=="pickup"&&u.DueDay>=0&&day>=u.DueDay&&price>=0&&price<=u.AuthorizedQuote&&price<=Math.Min(u.Budget,u.Cash)&&ReadyBlock(s,u)==null,"本次日期、价格、支付能力或用途验收不符；可以降价或等待");
        }
    }
}
