using System;
using System.Collections.Generic;
using System.Linq;

namespace ResourceStoryMod.Story
{
    public sealed class NormalSupplyNeed
    {
        public string Sku {get;set;}
        public string Pool {get;set;}
        public int Quantity {get;set;}=1;
        public bool Native {get;set;}
        public string Name {get;set;}
    }
    public static class NormalSupply
    {
        public static int MissingUnits(int required,int free,int committed)=>Math.Max(0,required-Math.Max(0,free)-Math.Max(0,committed));
        public static int VisitCap(string zone)=>zone=="lower"?2:zone=="middle"?3:4;
        public static void Prioritize(StoryState state,SupplierVisit visit,IEnumerable<NormalSupplyNeed> needs)
        {
            if(visit.PriorityPlanned)return;
            visit.PriorityPlanned=true;var random=visit.Batches.Where(b=>!b.Committed).ToArray();visit.Batches.RemoveAll(b=>!b.Committed);
            foreach(var need in needs){
                if(string.IsNullOrEmpty(need.Sku)||need.Quantity<1||visit.Batches.Any(b=>b.Sku==need.Sku))continue;
                string reason=null;
                if(visit.Batches.Count>=VisitCap(visit.Zone))reason="visit_capacity";
                else if(need.Pool=="large"&&visit.Batches.Any(b=>b.Pool=="large"))reason="one_large_per_visit";
                else if(state.Workshop.Advanced.Quotas.GetValueOrDefault(PackageBSupply.QuotaKey(visit.Day,need.Pool))+visit.Batches.Count(b=>b.Pool==need.Pool)>=PackageBSupply.Limit(need.Pool))reason="daily_quota";
                if(reason!=null){visit.PriorityBlocks[need.Sku]=reason;continue;}
                visit.Batches.Add(new SupplyBatch{Sku=need.Sku,Pool=need.Pool,Quantity=need.Quantity,Native=need.Native,Priority=true});
            }
            foreach(var batch in random){
                if(visit.Batches.Count>=VisitCap(visit.Zone)||visit.Batches.Any(b=>b.Sku==batch.Sku)||batch.Pool=="large"&&visit.Batches.Any(b=>b.Pool=="large"))continue;
                if(state.Workshop.Advanced.Quotas.GetValueOrDefault(PackageBSupply.QuotaKey(visit.Day,batch.Pool))+visit.Batches.Count(b=>b.Pool==batch.Pool)>=PackageBSupply.Limit(batch.Pool))continue;
                visit.Batches.Add(batch);
            }
            if(visit.Batches.Count>0)visit.Status="pending";
        }
    }
}
