using System;
using System.Collections.Generic;
using System.Linq;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageD
    {
        internal void MaterializeSource(GameItem item,SupplierVisit visit,SupplyBatch batch)
        {
            var template=PackageDCatalog.Template(batch.DTemplate);if(!Enabled || template==null)return;
            var parts=new Dictionary<string,GameItem>();
            foreach(var slot in template.Installed){var part=DirectoryMaster.Item(slot.Value,false);economy.PackageB.ApplyFacts(part,"ordinary","outside","vendor:"+visit.Id);parts.Add(slot.Key,part);}
            var snapshots=parts.ToDictionary(x=>x.Key,x=>NativeMemberEconomy.Encode(x.Value));
            host.Engine.EditMusicTrade(s=>{
                var a=s.Workshop.Advanced;var b=s.Economy.Business;var fact=a.Facts[item.uniqueId];fact.Faults=new(template.Faults);fact.Used=true;
                var gear=MemberBusiness.Register(b,item.uniqueId,item.identifier,"outside");
                foreach(var pair in parts){var part=pair.Value;b.Components.Add(part.uniqueId,new MusicComponent{EntityId=part.uniqueId,ItemId=part.identifier,Owner="outside",Location="installed",Instrument=item.uniqueId,Slot=pair.Key,Snapshot=snapshots[pair.Key]});gear.Slots.Add(pair.Key,part.uniqueId);}
                gear.Stats=PackageB.Stats(a,b,item.uniqueId);
                PackageDSupply.Commit(s,visit,batch,item.uniqueId,parts.Values.Select(x=>x.uniqueId).ToArray());
            });
            economy.PackageB.Present(item);
        }
        internal void PresentSource(GameItem item)
        {
            if(!Enabled||item==null)return;
            var lot=D.SourceLots.Values.FirstOrDefault(x=>x.Entity==item.uniqueId);if(lot==null)return;
            var template=PackageDCatalog.Template(lot.Template);
            item.name=template.Name;item.longDescription="二手来源已登记。"+(template.Irreparable?"箱体明确不可修复，仅作供体。":"按实际检查结果选择整备用途。")+"\n收购参考区间 "+template.Band[0]+"—"+template.Band[2]+"；实际付款以原生报价为准。";
            var receipt=lot.PurchaseReceipt!=null?D.Receipts.GetValueOrDefault(lot.PurchaseReceipt):null;
            if(receipt?.Allocations.TryGetValue(item.uniqueId,out int paid)==true)item.customText+="\n本店整机收购实付 "+paid+"（含当时已装件）";
            string history=PackageDFull.Summary(host.Engine.Snapshot(),item.uniqueId);if(history.Length>0)item.customText+="\n"+history;
            if(template.Irreparable){int recovery=62+template.Installed.Values.Sum(sku=>MusicCatalog.Find(sku)?.Price??0);item.customText+="\n可回用参考合计 "+recovery+"；"+((receipt?.Allocations.GetValueOrDefault(item.uniqueId)??item.GetCurrentValue())>recovery?"当前成本/参考值高于回用价值，不合算，建议拒收或重新议价。":"还需计入检查、卸件与能源投入；不是现金利润。");}
        }
    }
}
