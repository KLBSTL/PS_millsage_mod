using System;
using System.Collections.Generic;
using System.Linq;

namespace ResourceStoryMod.Story
{
    public static class PackageDSupply
    {
        public static bool CanOfferBase(StoryState s,string sku,string zone)=>s.MusicTrade?.Enabled==true && PackageDCatalog.Hosts.Contains(sku) && new[]{"middle","upper"}.Contains(zone) && (MusicCatalog.Unlocked(s,MusicCatalog.Find(sku)) || s.MusicTrade.OrdinaryFulfillments.Count>=3);
        public static IEnumerable<Dictionary<string,string>> ExtraCandidates(StoryState s,string zone)
            =>PackageBCatalog.Rows("supplier_candidates").Where(x=>CanOfferBase(s,x["id"],zone)).Select(x=>new Dictionary<string,string>(x){{"d_source","True"}});
        public static int Reserved(SupplierVisit v,string pool)=>v.Batches.Count(x=>x.Pool==pool)+(pool=="pro"?v.Batches.Sum(x=>PackageDCatalog.Template(x.DTemplate)?.ProQuota??0):0);
        public static Dictionary<string,string> HostChoice(StoryState s,Dictionary<string,string> chosen,Dictionary<string,string>[] options,string zone,string seed)
        {
            if(!CanOfferBase(s,chosen["id"],zone))return chosen;
            var all=options.Where(x=>CanOfferBase(s,x["id"],zone)).ToArray();int Weight(string sku)=>sku==PackageDCatalog.Pedal?40:sku==PackageDCatalog.Guitar?35:25;
            int roll=PackageBSupply.Roll(seed+"/dhost",all.Sum(x=>Weight(x["id"])));
            foreach(var p in all){roll-=Weight(p["id"]);if(roll<0)return p;}return chosen;
        }
        public static MusicUsedTemplate Template(StoryState s,SupplierVisit visit,string sku,string seed)
        {
            if(!CanOfferBase(s,sku,visit.Zone))return null;
            bool newAllowed=PackageBCatalog.Split(PackageBCatalog.Rows("supplier_candidates").First(x=>x["id"]==sku)["source_zones"]).Contains(visit.Zone)&&MusicCatalog.Unlocked(s,MusicCatalog.Find(sku));
            if(newAllowed && PackageBSupply.Roll(seed+"/dused",100)>=60)return null;
            int available=PackageBSupply.Limit("pro")-s.Workshop.Advanced.Quotas.GetValueOrDefault(PackageBSupply.QuotaKey(visit.Day,"pro"))-Reserved(visit,"pro");
            var candidates=PackageDCatalog.Used.Where(t=>t.Host==sku&&(visit.Zone=="middle"?t.MiddleWeight:t.UpperWeight)>0&&t.ProQuota<=available&&t.Installed.Values.All(part=>MusicCatalog.Unlocked(s,MusicCatalog.Find(part)))).ToArray();
            int Weight(MusicUsedTemplate t)=>visit.Zone=="middle"?t.MiddleWeight:t.UpperWeight;
            int roll=PackageBSupply.Roll(seed+"/dtemplate",candidates.Sum(Weight));
            foreach(var t in candidates){roll-=Weight(t);if(roll<0)return t;}
            return null;
        }
        public static void Commit(StoryState s,SupplierVisit visit,SupplyBatch batch,int host,int[] parts)
        {
            var t=PackageDCatalog.Template(batch.DTemplate);if(t==null)return;
            PackageB.Need(parts.Length==t.ProQuota && parts.Distinct().Count()==parts.Length,"实际已装配件不完整");
            string q=PackageBSupply.QuotaKey(visit.Day,"pro");PackageB.Need(s.Workshop.Advanced.Quotas.GetValueOrDefault(q)+t.ProQuota<=PackageBSupply.Limit("pro"),"专业件额度已满");
            s.Workshop.Advanced.Quotas[q]=s.Workshop.Advanced.Quotas.GetValueOrDefault(q)+t.ProQuota;
            string id=visit.Id+"_"+host;s.MusicTrade.SourceLots.Add(id,new(){Id=id,Visit=visit.Id,Entity=host,Template=t.Id,Components=parts});
        }
    }
}
