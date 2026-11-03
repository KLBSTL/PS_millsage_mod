using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ResourceStoryMod.Story
{
    public sealed class NormalVisitReceipt
    {
        public string Key {get;set;}
        public string Kind {get;set;}
        public string Owner {get;set;}
        public int Day {get;set;}
        public long Sequence {get;set;}
        public string Status {get;set;}="booked";
        public string Client {get;set;}
        public string OrdinaryTicket {get;set;}
        public bool ExtraPickup {get;set;}
        public bool LegacyAllocated {get;set;}
        public string DelayReason {get;set;}
    }
    public sealed class NormalVisitsState
    {
        public int Version {get;set;}=1;
        public long Sequence {get;set;}
        public Dictionary<string,NormalVisitReceipt> Visits {get;set;}=new();
        public Dictionary<int,string> Generation {get;set;}=new();
        public Dictionary<string,string> SlotClaims {get;set;}=new();
    }
    public static class NormalVisits
    {
        public static string Key(string kind,string owner,int day)=>kind+"/"+owner+"/"+day;
        public static NormalVisitReceipt Book(StoryState state,string kind,string owner,int day)
        {
            if(kind is not ("inspection" or "supplier" or "member")||string.IsNullOrWhiteSpace(owner)||day<0)throw new InvalidOperationException("预约身份或日期无效");
            var n=state.NormalVisits??=new();string key=Key(kind,owner,day);
            if(n.Visits.TryGetValue(key,out var same))return same;
            foreach(var prior in n.Visits.Values.Where(v=>v.Kind==kind&&v.Owner==owner&&v.Status is "booked" or "queued"))prior.Status="missed";
            var receipt=new NormalVisitReceipt{Key=key,Kind=kind,Owner=owner,Day=day,Sequence=checked(++n.Sequence)};n.Visits.Add(key,receipt);return receipt;
        }
        public static IEnumerable<NormalVisitReceipt> Due(NormalVisitsState n,int day)=>n?.Visits.Values.Where(v=>v.Day==day&&v.Status is "booked" or "queued").OrderBy(v=>v.Sequence)??Enumerable.Empty<NormalVisitReceipt>();
        public static void GenerationComplete(NormalVisitsState n,int day,string reason)
        {
            if(day<0||reason is not ("native_normal_complete" or "native_day_begin" or "native_door_open" or "restored_open_queue"))throw new InvalidOperationException("缺少原生名额生成完成凭据");
            n.Generation.TryAdd(day,reason);
            foreach(int old in n.Generation.Keys.Where(d=>d<day-8).ToArray())n.Generation.Remove(old);
        }
        public static void Claim(NormalVisitsState n,NormalVisitReceipt r,string ticket)
        {
            if(r.Kind!="inspection"||string.IsNullOrEmpty(ticket)||r.OrdinaryTicket!=null&&r.OrdinaryTicket!=ticket||n.SlotClaims.TryGetValue(ticket,out var owner)&&owner!=r.Key)throw new InvalidOperationException("普通顾客名额不能重复抵扣");
            n.SlotClaims[ticket]=r.Key;r.OrdinaryTicket=ticket;r.ExtraPickup=false;
        }
        public static void Queue(NormalVisitsState n,NormalVisitReceipt r,string client)
        {
            if(r.Status is not ("booked" or "queued")||string.IsNullOrEmpty(client))throw new InvalidOperationException("预约已结束或来访身份缺失");
            if(r.Kind=="inspection"&&r.OrdinaryTicket==null&&!r.LegacyAllocated){
                if(!n.Generation.ContainsKey(r.Day))throw new InvalidOperationException("原生普通名额尚未生成完成");
                if(n.Visits.Values.Any(v=>v.Key!=r.Key&&v.Kind=="inspection"&&v.Day==r.Day&&v.ExtraPickup))throw new InvalidOperationException("当日取回兜底已使用");
                r.ExtraPickup=true;
            }
            r.Client=client;r.Status="queued";
        }
        public static void Expire(NormalVisitsState n,int day)
        {
            foreach(var r in n.Visits.Values)if(r.Day<day&&r.Status is "booked" or "queued")r.Status="missed";
        }
        public static void Defer(NormalVisitReceipt r,int nextDay,string reason)
        {
            if(r.Status is not ("booked" or "queued")||nextDay<=r.Day||reason is not ("native_forced_closure" or "native_forced_venue"))throw new InvalidOperationException("不能把未知原因当作系统延期");
            r.Day=nextDay;r.Status="booked";r.DelayReason=reason;r.ExtraPickup=false;
        }
        public static void Validate(NormalVisitsState n)
        {
            if(n==null)return;
            if(n.Version!=1||n.Sequence<0||n.Visits==null||n.Generation==null||n.SlotClaims==null)throw new InvalidDataException("来访凭据结构无效，保留存档");
            foreach(var p in n.Visits){var r=p.Value;if(r==null||p.Key!=r.Key||r.Kind is not ("inspection" or "supplier" or "member")||string.IsNullOrEmpty(r.Owner)||r.Day<0||r.Sequence<1||r.Sequence>n.Sequence||r.Status is not ("booked" or "queued" or "arrived" or "done" or "missed")||r.ExtraPickup&&r.Kind!="inspection"||r.ExtraPickup&&r.OrdinaryTicket!=null)throw new InvalidDataException("来访身份/抵扣状态无效");}
            if(n.Visits.Values.Select(v=>v.Sequence).Distinct().Count()!=n.Visits.Count||n.Visits.Values.Where(v=>v.ExtraPickup).GroupBy(v=>v.Day).Any(g=>g.Count()>1))throw new InvalidDataException("来访序号或当日兜底重复");
            foreach(var p in n.SlotClaims)if(string.IsNullOrEmpty(p.Key)||!n.Visits.TryGetValue(p.Value,out var v)||v.OrdinaryTicket!=p.Key)throw new InvalidDataException("普通名额抵扣回执缺失");
        }
    }
}
