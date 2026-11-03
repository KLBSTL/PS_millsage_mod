using System;
using System.Collections.Generic;
using System.Linq;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageD
    {
        internal string BusinessItemText(GameItem item)
        {
            if(!Enabled)return "";
            var state=host.ReadState;
            int id=item.uniqueId;
            int? cost=PackageDFull.TotalCost(state,id);
            string text="\n可追溯成本 "+(cost?.ToString()??"来源未知或有未计价投入")+"。";
            var service=state.Workshop.Advanced.Services.Values.LastOrDefault(x=>x.Host==id&&!x.Terminal&&x.Delivery==null);
            var advanced=state.Workshop.Advanced;
            var report=advanced.Calibrations.GetValueOrDefault(id+":precision");
            if(report!=null)text+="\n精密鉴定 "+(PackageB.CalibrationValid(advanced,state.Economy.Business,id,"precision",service?.EffectiveSlots)?"当前配置合格":report.Passed?"配置有变，请重新鉴定":"上次鉴定未通过")+"。";
            string history=PackageDFull.Summary(state,id);
            if(history.Length>0)text+="\n"+history;
            var showcase=EmporiumEntry.Instance?.showcaseElement;
            if(showcase!=null&&item.parentInventory?.NativeIdentity()==showcase.NativeIdentity()){
                var samples=DisplaySamples("middle").Concat(DisplaySamples("upper"));
                text+=samples.Any(x=>x.Entity==id&&x.Music)?"\n原版陈列柜已登记合格器材，按类别使用现有来访名额。":"\n当前陈列配置尚未匹配音乐买家需求。";
            }
            return text;
        }
        private Dialogue NetworkLedger()
        {
            var d=host.ReadState.MusicTrade;
            var rows=new List<(int Day,string Id,string Text)>();
            foreach(var receipt in d.Receipts.Values.Where(r=>r.Kind is "purchase" or "sale" or "ordinary_sale")){
                bool bought=receipt.Kind=="purchase";
                string row=(bought?"收购支出 ":"销售收入 ")+receipt.Total;
                if(receipt.Kind=="sale"){
                    int? cost=d.Next.Realized.GetValueOrDefault(receipt.Id)?.Cost;
                    row+="，出货成本 "+(cost?.ToString()??"未知");
                }
                rows.Add((receipt.Day,receipt.Id,row));
            }
            foreach(var use in d.Next.Uses.Values.Where(x=>x.AcquisitionPaid.HasValue)){
                rows.Add((use.AcquisitionDay??-1,use.Origin,"续用原件收件支出 "+use.AcquisitionPaid.Value));
                if(use.Status=="done")rows.Add((d.Histories.GetValueOrDefault("use_"+use.Receipt)?.Day??host.Day,use.Receipt,"续用交回实收 "+use.Paid));
            }
            foreach(var fee in d.Fees){
                int day=d.Referrals.GetValueOrDefault(fee.Key)?.ConfirmedDay??d.Openings.GetValueOrDefault(fee.Key.StartsWith("open_",StringComparison.Ordinal)?fee.Key.Substring(5):fee.Key);
                rows.Add((day,fee.Key,"业务费 "+fee.Value+(d.Refunds.Contains(fee.Key)?"，系统已退回同额":"")));
            }
            var investments=d.Investments.Values.GroupBy(x=>x.Job);
            foreach(var group in investments){
                int day=host.ReadState.Workshop.Advanced.Ledger.GetValueOrDefault(group.Key)?.Day??host.Day;
                string cost=group.All(x=>x.Known)?group.Sum(x=>x.Amount).ToString():"含未知来源";
                rows.Add((day,group.Key,"加工投入 "+cost+"，实际电量 "+group.Where(x=>x.Energy).Sum(x=>x.Units)+"E"));
            }
            if(rows.Count==0)return NetworkLine("还没有经营流水。账册只记实际成交和工序。");
            var ordered=rows.OrderByDescending(x=>x.Day).ThenByDescending(x=>x.Id,StringComparer.Ordinal).ToArray();
            int pageCount=(ordered.Length+3)/4;
            Dialogue next=null;
            for(int i=pageCount-1;i>=0;i--){
                var page=ordered.Skip(i*4).Take(4);
                string body="经营账册 "+(i+1)+"/"+pageCount+"\n"+string.Join("\n",page.Select(r=>(r.Day<0?"日期未记录":"第 "+r.Day+" 天")+" · "+r.Text));
                var current=NetworkLine(body);
                if(next!=null)current.SetNextDialogue(next);
                next=current;
            }
            return next;
        }
    }
}