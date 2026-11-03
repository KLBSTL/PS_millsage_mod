using System;
using System.Collections.Generic;
using System.Linq;

namespace ResourceStoryMod.Story
{
    public static class PackageBSupply
    {
        public static int Roll(string seed,int max)=>max<=0?0:(int)(Convert.ToUInt32(PackageB.Hash(seed).Substring(0,8),16)%(uint)max);
        public static string QuotaKey(int day,string pool)=>day+":"+pool;
        public static int Limit(string pool)=>pool is "native_food" or "native_cloth"?1:PackageBCatalog.Number(PackageBCatalog.Rows("supplier_pools").First(x=>x["id"]==pool)["daily_global_stacks"]);
        public static void MigrateLegacy(PackageBState a,MemberBusinessState b)
        {
            if(a.Migrations.Contains("legacy_supply_mapped"))return;
            foreach(string old in b.Supplied){int colon=old.IndexOf(':');if(colon<1 || !int.TryParse(old.Substring(0,colon),out int day))continue;string sku=old.Substring(colon+1);var row=PackageBCatalog.Rows("supplier_candidates").FirstOrDefault(x=>x["id"]==sku);if(row==null)continue;string key=QuotaKey(day,row["pool"]);a.Quotas[key]=Math.Min(Limit(row["pool"]),a.Quotas.GetValueOrDefault(key)+1);}
            a.Migrations.Add("legacy_supply_mapped");
        }
        public static SupplierVisit Plan(StoryState state,int day,string vendor,string zone,bool nativeSeller,bool accessible)
        {
            PackageB.Need(nativeSeller && accessible && new[]{"lower","middle","upper"}.Contains(zone) && !vendor.StartsWith("ms_"),"不是可进入地区的原生卖方");
            var a=state.Workshop.Advanced;MigrateLegacy(a,state.Economy.Business);long sequence=checked(++a.VisitSequence);
            var v=new SupplierVisit{Id="supply_"+sequence,Sequence=sequence,Day=day,Vendor=vendor,Zone=zone};
            string seed=state.RunId+"/millsage/supply/"+day+"/"+sequence;int probability=zone=="lower"?60:zone=="middle"?70:80;
            if(Roll(seed+"/chance",100)>=probability){v.Status="skipped";a.Visits.Add(v.Id,v);a.CurrentVisit=v.Id;return v;}
            int cap=zone=="lower"?2:zone=="middle"?3:4,desired=1+Roll(seed+"/batches",cap);var chosen=new HashSet<string>();bool large=false;
            for(int n=0;n<desired;n++){
                var candidates=PackageBCatalog.Rows("supplier_candidates").Concat(PackageC.SupplierCandidates(state)).Concat(PackageDSupply.ExtraCandidates(state,zone)).Where(x=>(PackageBCatalog.Split(x["source_zones"]).Contains(zone)||PackageDSupply.CanOfferBase(state,x["id"],zone)) && !chosen.Contains(x["id"]) && (!large || x["pool"]!="large") && a.Quotas.GetValueOrDefault(QuotaKey(day,x["pool"]))+PackageDSupply.Reserved(v,x["pool"])<Limit(x["pool"]) && (MusicCatalog.Find(x["id"]) is not MusicProduct m || MusicCatalog.Unlocked(state,m)||PackageDSupply.CanOfferBase(state,x["id"],zone))).GroupBy(x=>x["id"]).Select(x=>x.First()).ToArray();
                var pools=PackageBCatalog.Rows("supplier_pools").Where(p=>candidates.Any(c=>c["pool"]==p["id"]) && PackageBCatalog.Number(p[zone+"_weight"])>0).ToArray();if(pools.Length==0)break;
                int roll=Roll(seed+"/pool/"+n,pools.Sum(p=>PackageBCatalog.Number(p[zone+"_weight"])));string pool=pools.Last()["id"];
                foreach(var p in pools){roll-=PackageBCatalog.Number(p[zone+"_weight"]);if(roll<0){pool=p["id"];break;}}
                var options=candidates.Where(x=>x["pool"]==pool).OrderBy(x=>x["id"],StringComparer.Ordinal).ToArray();var item=options[Roll(seed+"/sku/"+n,options.Length)];string sku=item["id"];
                item=PackageDSupply.HostChoice(state,item,options,zone,seed+"/"+n);sku=item["id"];
                var dTemplate=PackageDSupply.Template(state,v,sku,seed+"/"+n);
                string grade="ordinary";if(zone=="upper" && PackageBCatalog.Graded(sku)){int g=Roll(seed+"/grade/"+n,100);grade=g<65?"ordinary":g<90?"selected":"precision";}
                bool used=item["used_allowed"]=="True" && Roll(seed+"/used/"+n,100)<(zone=="middle"?30:zone=="upper"?20:0);string fault="normal",domain=null;
                if(used){int f=Roll(seed+"/fault/"+n,100);fault=f<40?"normal":f<70?"dirty_oxidized":sku=="ms_gear_drum_practice_pad"?"structural_fault":sku=="ms_gear_commercial_guitar"?"contact_fault":f<90?"contact_fault":"structural_fault";
                    domain=fault=="dirty_oxidized"?"body":fault=="contact_fault"?(Roll(seed+"/domain/"+n,2)==0?"wire":"capacitor"):sku=="ms_gear_monitor_speaker"?(Roll(seed+"/domain/"+n,2)==0?"board":"driver"):sku=="ms_gear_drum_practice_pad"?(Roll(seed+"/domain/"+n,2)==0?"surface":"support"):"board";
                }
                int quantity=pool=="raw"?2+Roll(seed+"/qty/"+n,4):pool=="parts" && !PackageBCatalog.Graded(sku) && sku!="ms_item_balanced_cable"?1+Roll(seed+"/qty/"+n,3):1;
                if(dTemplate!=null){grade="ordinary";used=true;fault="normal";domain=null;quantity=1;}
                v.Batches.Add(new SupplyBatch{Pool=pool,Sku=sku,Grade=grade,Used=used,Fault=fault,Domain=domain,Quantity=quantity,DTemplate=dTemplate?.Id});chosen.Add(sku);large|=pool=="large";
            }
            a.Visits.Add(v.Id,v);a.CurrentVisit=v.Id;return v;
        }
        public static void CommitBatch(PackageBState a,SupplierVisit visit,SupplyBatch batch,List<WorkshopEntity> actual)
        {
            if(batch.Committed)return;
            PackageB.Need(actual.Count==batch.Quantity && actual.Select(x=>x.Entity).Distinct().Count()==actual.Count && actual.All(x=>x.ItemId==batch.Sku && x.Units==1),"供货批次实际实体不完整");
            string key=QuotaKey(visit.Day,batch.Pool);PackageB.Need(a.Quotas.GetValueOrDefault(key)<Limit(batch.Pool),"当日批次额度已满");
            batch.Remaining=actual;batch.Committed=true;a.Quotas[key]=a.Quotas.GetValueOrDefault(key)+1;
        }
    }
}
