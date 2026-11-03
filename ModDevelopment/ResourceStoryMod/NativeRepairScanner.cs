using System;
using System.Linq;
using HarmonyLib;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageB
    {
        private (GameItem Machine,GameItem Target) lastScan;
        private (GameItem Item,string[] Rows,string Result) scanReport;

        private void InstallScannerReport()
        {
            var type=typeof(PlantAnalyserUIManager);
            core.HarmonyInstance.Patch(ReleaseApi.Method(type,"Refresh"),prefix:new HarmonyMethod(typeof(NativePackageB),nameof(RenderScannerReport)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(type,"HideUI"),postfix:new HarmonyMethod(typeof(NativePackageB),nameof(ClearScannerReport)));
        }

        internal TooltipContainer BenchManual(GameItem bench)=>InitRepairItem(bench).Manual;

        internal bool IsManualTooltipVisible(TooltipContainer manual)
        {
            var manager=TooltipManager._instance;
            return manager.tooltipGameobject.activeInHierarchy&&manager.tooltipOwner==manual;
        }

        private void ConfigureScanner(GameItem item)
        {
            AudioHelper.InitScanner(item);
            item.shortDescription="选中检测仪并对器材使用，即可查看检测结果。";
            if(item.identifier=="ms_machine_calibrator")item.shortDescription+="可安装参考输入；完成自检后，操作检测仪可进行精密鉴定。";
            var select=(System.Func<GameItem,GameInventory,SlotMarker,bool>)((tool,inventory,slot)=>Enabled&&PlayerOwned(tool));
            roots.Add(select);
            item.maySelectSlotItemFunc=select;
            item.canSelectSlotItemFunc=select;
        }

        private bool HasLegacyScanItem(GameItem item)=>Enabled&&NormalPlayPolicy.IsInspector(item?.identifier)&&PlayerOwned(item)
            &&InitRepairItem(item).Host.childItems.Count>0;

        private bool CanScan(GameItem tool,GameItem item)
        {
            if(!PlayerOwned(tool)||!IsRepairGear(item)||RepairHostWorking(item.uniqueId))return false;
            bool campaign=BasicCampaignOriginal(item)||economy.Trade.HoldsNativeUse(item);
            return tool.identifier==NormalPlayPolicy.BasicInspector?campaign:campaign||PlayerOwned(item)||ServiceFor(item.uniqueId)!=null;
        }

        private GameItem PrecisionScanTarget(GameItem machine)
        {
            var legacy=ItemList(InitRepairItem(machine).Host.childItems).SingleOrDefault();
            if(legacy!=null)return legacy;
            if(lastScan.Machine!=machine||lastScan.Target==null)return null;
            var item=economy.FindPlayerItem(lastScan.Target.uniqueId);
            return item==lastScan.Target&&CanScan(machine,item)?item:null;
        }

        private void ScanGear(GameItem machine,GameItem item)
        {
            string result;
            var view=InitRepairItem(item);
            try{
                DetectRepair(machine,item);
                lastScan=(machine,item);
                var slots=ServiceFor(item.uniqueId)?.EffectiveSlots;
                string scope=item.identifier=="ms_gear_drum_practice_pad"?"passive":"basic";
                result=PackageB.CalibrationValid(A,B,item.uniqueId,scope,slots)?"当前配置检测合格。":"当前配置尚未通过检测。";
                if(host.Inspection?.Managed(item)==true)result=host.Inspection.Text(item);
                else if(TryActTwoView(view,out string progress)||TryCampaign012View(view,out progress))result=progress.Split('\n').LastOrDefault(x=>!string.IsNullOrWhiteSpace(x))??result;
                else if(host.Opening?.MachineText(item)!=null)result=host.Opening.MachineText(item).Split('\n').LastOrDefault(x=>!string.IsNullOrWhiteSpace(x))??result;
            }catch(InvalidOperationException error){result=error.Message;lastScan=default;}
            string[] rows=Array.Empty<string>();
            if(A.Facts.ContainsKey(item.uniqueId)&&B.Instruments.TryGetValue(item.uniqueId,out var instrument)){
                var stats=PackageB.Stats(A,B,item.uniqueId,ServiceFor(item.uniqueId)?.EffectiveSlots??instrument.Slots);
                rows=new[]{"降噪："+stats["noiseControl"],"稳定："+stats["stability"],"响应："+stats["response"],"便携："+stats["portability"]};
            }
            core.EnsureChineseFallback();
            scanReport=(item,rows,result);
            PlantAnalyserUIManager.Instance.OpenUI(item);
            AudioManager.Instance?.Play3("scanner_activate");
        }

        private static bool RenderScannerReport(PlantAnalyserUIManager __instance)
        {
            var p=RepairSelf;
            if(p?.Enabled!=true||p.scanReport.Item==null)return true;
            var report=p.scanReport;
            if(__instance.currentItem!=report.Item){p.scanReport=default;return true;}
            __instance.titleTMP.text=report.Item.name;
            var labels=new[]{__instance.nutrimentRangeTMP,__instance.nutrientUseTMP,__instance.waterUsageTMP,__instance.contaminantSensitivityTMP};
            for(int i=0;i<labels.Length;i++)labels[i].text=i<report.Rows.Length?report.Rows[i]:"";
            __instance.body2TMP.text=report.Result;
            __instance.growthScoreImage.gameObject.SetActive(false);
            __instance.growthScoreTilledImage.gameObject.SetActive(false);
            return false;
        }

        private static void ClearScannerReport()
        {
            if(RepairSelf is NativePackageB p)p.scanReport=default;
        }
    }
}
