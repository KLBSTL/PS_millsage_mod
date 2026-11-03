using System;
using System.Linq;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageB
    {
        internal void SyncCampaign012Attachments()
        {
            bool previous=repairMove;repairMove=true;
            var state=host.ReadState;
            try{foreach(var r in state.CampaignItems.Originals.Values.Where(x=>x.Location=="native"&&(PackageC.Native012Jobs(state,x.Role).Any()||PackageC.ActTwoJobs(state,x.Role).Any()))){
                var item=economy.FindPlayerItem(r.Entity);if(item==null)continue;
                var view=InitRepairItem(item);if(view==null)continue;
                foreach(var attached in r.Attachments){
                    if(!view.Slots.TryGetValue(attached.Key,out var slot))throw new InvalidOperationException("原件缺少固定配件槽："+attached.Key);
                    var actual=ItemList(slot.childItems).SingleOrDefault();if(actual?.uniqueId==attached.Value.Entity)continue;
                    if(actual!=null)throw new InvalidOperationException("原件固定配件槽冲突，保留现有配件。");
                    var part=economy.FindPlayerItem(attached.Value.Entity)??NativeMemberEconomy.Decode(attached.Value.Snapshot);
                    part.parentInventory?.Expel(part);GeneralHelper.SetItemOwned(part,true);part.EnableTag(MusicCatalog.ProtectedTag,false);
                    if(!slot.UncheckedAccept(part))throw new InvalidOperationException("实际配件未能装入原件。");
                }
            }}finally{repairMove=previous;}
        }
        private bool TryCampaign012View(RepairView view,out string text)
        {
            text=null;var native=host.Native012;if(native?.Enabled!=true)return false;
            var r=native.Original(view.Item);
            if(r!=null){
                SyncCampaign012Attachments();text="物主：琴平凪。当前仅委托店内保管，保留同一把原琴与已装配件。\n"+PackageC.Description(host.ReadState,r.Role,true,false)+"\n"+(PackageC.Native012Block(host.ReadState,r.Role,host.Day)??"当前配置已检测合格，电话联系后即可由小凪在柜台取回。");return true;
            }
            var gear=view.Host==null?null:ItemList(view.Host.childItems).SingleOrDefault();if(!native.Managed(gear))return false;
            text=(view.Kind=="bench"?"小凪的剧情委托：将原琴放入器材位；随附耗材和套弦放入配件区，随附螺丝刀、焊接工具装入工具箱。\n休息一晚即可完成加工（需备妥店内原版电源）。中途移走原琴、缺料或缺电均会保留当前进度。":"小凪的剧情委托：用检测仪对原琴使用即可确认当前配置，无须等待。")+"\n"+native.ProgressText();return true;
        }
        private bool TryActTwoView(RepairView view,out string text)
        {
            text=null;var native=host.ActTwo;if(native?.Enabled!=true)return false;
            var original=native.Original(view.Item);var gear=original!=null?view.Item:view.Host==null?null:ItemList(view.Host.childItems).SingleOrDefault();if(!native.Managed(gear))return false;
            original=native.Original(gear);string actor=PackageC.ActTwoJobs(host.ReadState,original.Role).First().SupplyActor;
            SyncCampaign012Attachments();
            text=(view.Kind=="gear"?"物主："+economy.Business.ActorNameForNote(actor)+"。受托保管，保留同一原件与已装配件。\n":view.Kind=="bench"?"器材放左侧，随附配件/套弦放中间，工具装入右侧工具箱。店内备电，睡觉过一天加工。\n":"对原件使用检测仪即可确认当前配置，不重复扣电。\n")+native.Progress(actor)+"\n"+(PackageC.ActTwoBlock(host.ReadState,original.Role,host.Day)??"当前配置已检测合格，可电话联系物主次日取件。");return true;
        }
    }
}
