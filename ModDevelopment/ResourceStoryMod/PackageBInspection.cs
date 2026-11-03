using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ResourceStoryMod.Story
{
    public sealed class BasicInspectionOrder
    {
        public int Id {get;set;}public int Day {get;set;}public int Entity {get;set;}public int Note {get;set;}
        public string Sku {get;set;}public string Snapshot {get;set;}public string Sprite {get;set;}public string Zone {get;set;}="middle";
        public string Status {get;set;}="offered";public string DetectedSignature {get;set;}
        public int IntakePaid {get;set;}public int ReturnedPaid {get;set;}public int Budget {get;set;}=200;public int Cash {get;set;}=200;
        public int PickupDay {get;set;}=-1;public int VisitDay {get;set;}=-1;public string Ticket {get;set;}
        public bool BudgetInitialized {get;set;}
    }
    public sealed class BasicInspectionState
    {
        public int Version {get;set;}=1;public List<BasicInspectionOrder> Orders {get;set;}=new();
        public int MissedEligibleOffers {get;set;}
        public Dictionary<string,InspectionOfferDecision> OfferDecisions {get;set;}=new();
    }
    public sealed class InspectionOfferDecision
    {
        public int Day {get;set;}
        public bool Offered {get;set;}
        public bool Guaranteed {get;set;}
    }
    public static class BasicInspection
    {
        public const int IntakeReference=15,Fee=120;
        public static bool DecideOffer(BasicInspectionState s,int day,string ticket,int roll,int chance=35,int guarantee=3)
        {
            if(string.IsNullOrWhiteSpace(ticket)||day<0||roll<0||roll>=100||chance<0||chance>100||guarantee<1)throw new InvalidOperationException("检测名额判定参数无效");
            if(s.OfferDecisions.TryGetValue(ticket,out var old))return old.Offered;
            if(!CanOffer(s,day))return false;
            bool forced=s.Orders.Count==0||s.MissedEligibleOffers>=guarantee-1;bool offered=forced||roll<chance;
            s.OfferDecisions.Add(ticket,new(){Day=day,Offered=offered,Guaranteed=forced});s.MissedEligibleOffers=offered?0:checked(s.MissedEligibleOffers+1);
            foreach(string key in s.OfferDecisions.Where(p=>p.Value.Day<day-8).Select(p=>p.Key).ToArray())s.OfferDecisions.Remove(key);
            return offered;
        }
        public static bool Held(BasicInspectionOrder o)=>o!=null&&o.Status is "held" or "ready";
        public static bool CanOffer(BasicInspectionState s,int day)=>day>=0&&!s.Orders.Any(o=>o.Status is "offered" or "held" or "ready")&&!s.Orders.Any(o=>o.Day==day)&&s.Orders.Count(o=>o.Day>=day-6&&o.Day<=day)<3;
        public static void Buy(BasicInspectionOrder o,int paid){if(o.Status!="offered"||paid<0)throw new InvalidOperationException("原件已经接收或收购金额无效");o.IntakePaid=paid;o.Cash=checked(o.Cash+paid);o.Status="held";}
        public static bool Detect(BasicInspectionOrder o,string signature){if(!Held(o)||string.IsNullOrEmpty(signature))throw new InvalidOperationException("先在柜台收购同一原件");if(o.Status=="ready"&&o.DetectedSignature==signature)return false;o.Status="ready";o.DetectedSignature=signature;return true;}
        public static bool Ready(BasicInspectionOrder o,string signature)=>o?.Status=="ready"&&o.DetectedSignature==signature;
        public static bool Book(BasicInspectionOrder o,int day,string signature){if(!Ready(o,signature))throw new InvalidOperationException("请先检测同一原件的当前配置");if(o.PickupDay>=day)return false;o.PickupDay=checked(day+1);o.VisitDay=-1;return true;}
        public static bool ExpireAppointment(BasicInspectionOrder o,int day){if(o?.Status!="ready"||o.PickupDay<0||o.PickupDay>=day)return false;o.PickupDay=o.VisitDay=-1;return true;}
        public static void Return(BasicInspectionOrder o,int day,string signature,int paid){if(!Ready(o,signature)||o.PickupDay<0||day<o.PickupDay||paid<0||paid>o.Cash||paid>o.Budget)throw new InvalidOperationException("物主未到、配置已改变或付款无效");o.ReturnedPaid=paid;o.Budget-=paid;o.Cash-=paid;o.Status="done";}
        public static void Validate(BasicInspectionState s)
        {
            if(s==null)return;
            if(s.Version!=1||s.Orders==null||s.OfferDecisions==null||s.MissedEligibleOffers<0||s.OfferDecisions.Any(p=>string.IsNullOrWhiteSpace(p.Key)||p.Value==null||p.Value.Day<0)||s.Orders.Select(o=>o.Id).Distinct().Count()!=s.Orders.Count||s.Orders.Select(o=>o.Entity).Distinct().Count()!=s.Orders.Count||s.Orders.Count(o=>o.Status is "offered" or "held" or "ready")>1)throw new InvalidDataException("基础检测委托状态损坏，保留存档");
            foreach(var o in s.Orders)if(o.Id<1||o.Day<0||o.Entity<1||string.IsNullOrWhiteSpace(o.Sku)||string.IsNullOrWhiteSpace(o.Snapshot)||o.IntakePaid<0||o.ReturnedPaid<0||o.Cash<0||o.Budget<0||o.PickupDay< -1||o.Status is not ("offered" or "held" or "ready" or "done" or "declined")||o.Status=="ready"&&string.IsNullOrEmpty(o.DetectedSignature))throw new InvalidDataException("基础检测原件/结算记录无效");
            var days=s.Orders.Select(o=>o.Day).OrderBy(d=>d).ToArray();for(int i=0;i<days.Length;i++)if(i>0&&days[i]==days[i-1]||i>=3&&days[i]-days[i-3]<7)throw new InvalidDataException("基础检测每日或滚动七日额度无效");
            var notes=s.Orders.Where(o=>o.Note>0).Select(o=>o.Note).ToArray();if(notes.Distinct().Count()!=notes.Length)throw new InvalidDataException("检测便条不能指向多个原件");
        }
    }
}
