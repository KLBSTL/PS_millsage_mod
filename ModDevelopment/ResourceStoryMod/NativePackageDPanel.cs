using System;
using System.Collections.Generic;
using System.Linq;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageD
    {
        internal bool PanelOpen{get;private set;}
        internal int PanelRows{get;private set;}
        internal int PanelPages{get;private set;}
        private string panelTab="overview",panelNotice="";
        private int panelPage,selectedHost,selectedBench;
        private int ordinaryPreviewDay=-1,ordinaryPreviewAttract=-1,ordinaryPreview;
        private bool salvageConfirm;
        private MusicReferral referralPreview;
        private GUIStyle panelBody,panelButton;
        private float panelOldTime;
        private PlayerStore panelStore;
        private StoreUIManager panelUI;
        private sealed class Card{internal string Text,Label;internal Action Action;}
        internal void OpenPanel(string tab="overview")
        {
            if(!Enabled||PanelOpen||host.Visible||host.Campaign?.PanelOpen==true)return;
            economy.PackageB.Close();economy.Workshop.Close();economy.Business.Close();economy.CloseLedger();var store=PlayerStore.instance;
            if(store.lockStorePhysicalFlag!=0||DialogUIManager.Instance.isConversationActiveFlag!=0)return;
            panelStore=store;panelUI=StoreUIManager.Instance;panelOldTime=Time.timeScale;store.lockStorePhysicalFlag++;NativePanelCursor.Acquire(panelUI);Time.timeScale=0;PanelOpen=true;panelTab=tab;panelPage=0;
        }
        internal void ClosePanel(){if(!PanelOpen)return;PanelOpen=false;Time.timeScale=panelOldTime;if(panelStore!=null)panelStore.lockStorePhysicalFlag=Math.Max(0,panelStore.lockStorePhysicalFlag-1);if(panelUI!=null)NativePanelCursor.Release(panelUI);panelStore=null;panelUI=null;}
        internal void Draw(GUIStyle body,GUIStyle button,GUIStyle window)
        {
            if(!Enabled||host.Visible||host.Campaign?.PanelOpen==true)return;panelBody??=new GUIStyle(body){fontSize=20,wordWrap=true};panelButton=button;
            if(!PanelOpen){if(!economy.PackageB.PanelOpen&&!economy.Workshop.PanelOpen&&!economy.Business.PanelOpen&&NativePanelGUI.Button(new Rect(20,Screen.height-106,220,40),"音乐器材经营",button)){if(N.Uses.Values.Any(x=>x.Status is "offered" or "servicing" or "pickup" or "cancel_pickup"))OpenNextPanel("use");else OpenPanel();}return;}
            NativePanelGUI.Window(781354,new Rect(15,15,Math.Min(1100,Screen.width-30),Math.Min(820,Screen.height-30)),DrawPanel,"麻里奈的音乐器材经营",window,nextView=="use");
        }
        private void PanelAction(Action action){try{action();host.Persist();panelNotice="操作已保存。";}catch(Exception e){panelNotice=e.Message;core.LoggerInstance.Warning("PACKAGE_D_ACTION "+e.Message);}}
        private void Add(List<Card> rows,string text,Action action=null,string label="查看")=>rows.Add(new(){Text=text,Action=action,Label=label});
        private string Product(string sku)=>PackageDNext.Name(sku);
        private void Maintenance(int entity){var svc=host.Engine.Snapshot().Workshop.Advanced.Services.Values.FirstOrDefault(x=>!x.Terminal&&x.Host==entity);string id=svc?.Id??economy.PackageB.Authorize("d_service_"+Guid.NewGuid().ToString("N"),"player",entity);ClosePanel();economy.PackageB.OpenService(id);}
        private void DrawPanel(int id)
        {
            if(nextView=="use"){DrawUseWorkflow();return;}
            float width=Math.Min(1100,Screen.width-30),height=Math.Min(820,Screen.height-30);string[] tabs={"overview","work","market","business","ledger"},names={"概览","整备","销路","业务","账册"};
            float step=(width-120)/5;for(int n=0;n<5;n++){string target=tabs[n];if(NativePanelGUI.Button(new Rect(15+n*step,36,step-6,36),names[n],panelButton)){panelTab=target;nextView=null;panelPage=0;referralPreview=null;salvageConfirm=false;}}
            if(NativePanelGUI.Button(new Rect(width-105,36,90,36),"关闭",panelButton))ClosePanel();
            string notice=panelNotice.Length>0?panelNotice:"所有交易货到付款；推荐占普通名额，成员仍用原电话；成本未知时不计算虚假利润。";
            GUI.Label(new Rect(20,80,width-40,54),notice,panelBody);
            var rows=BuildCards();PanelRows=rows.Count;PanelPages=Math.Max(1,(rows.Count+4)/5);panelPage=RuntimeCompat.Clamp(panelPage,0,PanelPages-1);
            float rowHeight=(height-202)/5;
            for(int n=0;n<5;n++){int index=panelPage*5+n;if(index>=rows.Count)break;var row=rows[index];float y=139+n*rowHeight;
                GUI.Label(new Rect(20,y,width-(row.Action==null?45:225),rowHeight-7),row.Text,panelBody);
                if(row.Action!=null&&NativePanelGUI.Button(new Rect(width-200,y+2,180,Math.Min(48,rowHeight-10)),row.Label,panelButton))PanelAction(row.Action);
            }
            if(NativePanelGUI.Button(new Rect(20,height-50,120,34),"上一页",panelButton))panelPage=Math.Max(0,panelPage-1);
            GUI.Label(new Rect(155,height-48,240,34),$"第 {panelPage+1}/{PanelPages} 页 · 共 {PanelRows} 条",panelBody);
            if(NativePanelGUI.Button(new Rect(width-140,height-50,120,34),"下一页",panelButton))panelPage=Math.Min(PanelPages-1,panelPage+1);
        }
        private List<Card> BuildCards()
        {
            var rows=new List<Card>();var state=host.Engine.Snapshot();var d=state.MusicTrade;var gear=economy.AllPlayerItems().Where(x=>PackageDCatalog.Hosts.Contains(x.identifier)&&economy.PackageB.PlayerOwned(x)).ToArray();
            if(BuildNextCards(rows,state))return rows;
            AddNextEntrances(rows);
            if(panelTab=="overview"){
                Add(rows,"现金 "+PlayerStore.instance.GetCash()+"；可操作音乐整机 "+gear.Length+"；今日已分配音乐普通来访 "+(d.Buyers.Values.Count(x=>x.Day==host.Day)+d.Next.Visits.Values.Count(x=>x.Day==host.Day))+" / 2。");
                int attractiveness=PlayerStore.instance.GetCurrentStoreAttractiveness();
                // The native estimate may roll review/referral perks. Cache it
                // rather than consume native random rolls on every GUI event.
                if(ordinaryPreviewDay!=host.Day||ordinaryPreviewAttract!=attractiveness){ordinaryPreview=PlayerStore.instance.storeClientManager.GetNormalCustomerCount();ordinaryPreviewDay=host.Day;ordinaryPreviewAttract=attractiveness;}
                int ordinary=ordinaryPreview;
                Add(rows,"原版普通名额参考 "+ordinary+"；当前店铺吸引力 "+attractiveness+"。音乐买家和续用沿这些普通位置安排。"+(ordinary<=0?"当前没有普通名额，可先改善店铺吸引力与陈列条件。":"地区、库存用途与概率仍会影响实际来访，不保证每日出现音乐客人。"));
                foreach(var job in d.Salvage.Values.Select(x=>state.Workshop.Jobs.GetValueOrDefault(x.Id)).Where(x=>x!=null&&!x.Terminal)){var j=job;Add(rows,"拆解工位："+(j.Status=="ready"?"固定残料待领":j.Status=="return_ready"?"取消后的原件待领":"加工中；剩余有效 "+Math.Max(0,(j.DurationMs-j.ElapsedMs+999)/1000)+"秒"),()=>{panelTab="work";panelPage=0;},"查看工位");}
                foreach(var r in d.Referrals.Values.Where(x=>x.Status=="confirmed")){var booking=r;Add(rows,"第"+r.Day+"天："+(r.Kind=="buyer"?PackageDCatalog.Buyer(r.Target)?.Name:"已知卖方回访")+"；费用 "+r.Fee+"，玩家取消不退款。",()=>CancelReferral(booking.Id),"取消预约");}
                if(d.PendingIntent!=null)Add(rows,"有一条未收费意向；普通名额出现后需要重新确认。",()=>{panelTab="business";panelPage=0;},"查看意向");
                Add(rows,"下一件事：收购实际旧货 → 检查 → 按用途整备/卸件/报废 → 陈列、等待或推荐 → 原柜成交。",()=>{panelTab="work";panelPage=0;},"选择整备");
            }else if(panelTab=="work"){
                foreach(var machine in economy.AllPlayerItems().Where(x=>x.identifier=="ms_machine_electrobench"&&economy.PackageB.PlayerOwned(x))){int entity=machine.uniqueId;Add(rows,"整备台："+economy.PackageB.Location(machine)+(selectedBench==entity?"（已选）":""),()=>{selectedBench=entity;salvageConfirm=false;},"选择此工位");}
                foreach(var item in gear){int entity=item.uniqueId;var cost=PackageDFull.TotalCost(state,entity);Add(rows,item.name+"；当前可追溯成本 "+(cost.HasValue?cost.Value.ToString():"未知/有未计价投入"),()=>{selectedHost=entity;salvageConfirm=false;},selectedHost==entity?"已选 / 查看":"选择原件");}
                if(selectedHost>0&&economy.FindPlayerItem(selectedHost) is GameItem selected){
                    Add(rows,"检查、清洁、维修、卸实际配件、安装和校准：沿现有工坊按实际工具/材料和有效时间办理。",()=>Maintenance(selectedHost),"进入已有维护");
                    string block=PackageDFull.SalvageBlock(state,selectedHost,economy.PackageB.PlayerOwned(selected),selected.IsTag(MusicCatalog.ProtectedTag)||economy.Workshop.BlocksSale(selected));
                    if(block!=null)Add(rows,"报废暂不可用："+block);
                    else Add(rows,"不可逆报废：90秒 / 0E；保留螺丝刀与剪线钳。产物 "+string.Join("、",PackageDFull.SalvageOutputs(selected.identifier).Select(x=>Product(x.Key)+"×"+x.Value))+(salvageConfirm?"。再次确认后开始。":"。先查看确认。"),()=>{if(!salvageConfirm){salvageConfirm=true;return;}StartSalvage("d_salvage_"+Guid.NewGuid().ToString("N"),selectedHost,selectedBench);salvageConfirm=false;},salvageConfirm?"确认不可逆报废":"预览报废");
                }
                foreach(var job in d.Salvage.Values.Select(x=>state.Workshop.Jobs[x.Id]).Where(x=>!x.Terminal)){
                    var control=job;if(job.Status=="paused")Add(rows,"工位暂停："+job.PauseReason+"；恢复外层机器条件后继续，保留有效进度。",()=>economy.Workshop.Resume(control.Id),"恢复拆解");
                    else if(job.Status=="running")Add(rows,"当前机械工序只按有效营业时间推进；也可主动暂停。",()=>economy.Workshop.Pause(control.Id),"暂停拆解");
                    var j=job;if(j.Status is "running" or "paused")Add(rows,"拆解剩余有效 "+Math.Max(0,(j.DurationMs-j.ElapsedMs+999)/1000)+"秒；完成前可取回原件，空间不足会待领。",()=>economy.Workshop.Cancel(j.Id),"取消并保留源");
                    foreach(var pending in Workshop.Pending(j)){int entity=pending.Entity;Add(rows,(j.Status=="return_ready"?"取消退回：":"待领：")+Product(pending.ItemId)+"×"+pending.Units,()=>{if(!economy.Workshop.Claim(j.Id,entity))throw new InvalidOperationException("库存没有合法格位，原件仍在同一工位待领");},"领取同一实物");}
                }
            }else if(panelTab=="market"){
                var saleItems=economy.AllPlayerItems().Where(x=>economy.PackageB.PlayerOwned(x)&&(PackageDCatalog.Hosts.Contains(x.identifier)||MusicCatalog.Find(x.identifier)?.Slot!=null||PackageBCatalog.Item(x.identifier)?.GetValueOrDefault("group")=="raw_supply")).ToArray();
                var selected=saleItems.FirstOrDefault(x=>x.uniqueId==selectedHost)??saleItems.FirstOrDefault();
                if(selected!=null){var item=selected;int entity=item.uniqueId;Add(rows,"当前比较："+item.name+"。逐项列出用途要求，缺少的状态可以回整备页处理。",()=>{int index=Array.FindIndex(saleItems,x=>x.uniqueId==entity);selectedHost=saleItems[(index+1)%saleItems.Length].uniqueId;panelPage=0;},"切换比较原件");
                    foreach(var profile in PackageDCatalog.Buyers){string zone=profile.Zones.Contains("middle")?"middle":"upper";string reason=PackageD.Eligible(state,profile,entity,zone,true,item.IsTag(MusicCatalog.ProtectedTag)||economy.Workshop.BlocksSale(item),0,0,false);if(reason==null){string tier=profile.Id=="B07"?"community":new[]{"B01","B02","B06"}.Contains(profile.Id)?"practice":"stage";string target=profile.Id;Add(rows,profile.Name+"：用途条件满足；基础预算 "+profile.Budget+"。实际预算按原版声望和吸引力算一次。",()=>{referralPreview=ReferralPreview(tier,"buyer",target,zone);panelTab="business";panelPage=0;},"预览定向推荐");}else Add(rows,profile.Name+"："+reason);}
                }
                string[] categories={"HOUSEHOLD_GOOD","MODULE","MATERIAL","TOOL"},titles={"家居/整机","模块","材料","工具"};var samples=DisplaySamples("middle");for(int i=0;i<4;i++)Add(rows,"原展示柜 · "+titles[i]+"：合格音乐份额 "+(PackageDFull.DisplayShare(samples,categories[i])/100.0).ToString("0.#")+"%。只影响该类别已有名额，不增加总客流。");
                Add(rows,"买卖、议价与摆放继续在原版柜台；箱内物和已装模块不穿透计陈列。",ClosePanel,"返回原柜交易");
            }else if(panelTab=="business"){
                if(referralPreview!=null){var r=referralPreview;int fee=r.Tier=="community"&&!d.CommunityFreeUsed?0:PackageDNetwork.Fee(r.Tier);Add(rows,"第"+r.Day+"天普通推荐；费用 "+fee+"；保守位置预测 "+FutureCapacity()+"。没有位置只存意向不扣费；系统未兑现退款一次。",()=>{ConfirmReferral(r);referralPreview=null;},"确认推荐 / 意向");}
                foreach(string tier in new[]{"community","practice","stage"}){string t=tier;string name=t=="community"?"社区维修料":t=="practice"?"配件与练习":"舞台专业";if(d.Openings.ContainsKey(t))Add(rows,name+"已开通；推荐 "+PackageDNetwork.Fee(t)+"。普通推荐全档每天合计一次。");else Add(rows,name+"一次开通 "+PackageDNetwork.Opening(t)+"；"+(PackageDNetwork.Qualification(state,t,MiddleOpen())??"资格已满足"),()=>OpenBusiness(t),"开通此业务");}
                foreach(var visit in AvailableReturns()){var v=visit;string tier=v.Zone=="lower"?"community":v.Zone=="middle"?"practice":"stage";Add(rows,"已知卖方剩余 "+v.NativeStock.Count+"件；回访保留原库存与报价状态，不新抽货、不重扣额度。",()=>{referralPreview=ReferralPreview(tier,"seller",v.Id,v.Zone);panelPage=0;},"预览卖方回访");}
                if(d.PendingIntent!=null){var pending=d.PendingIntent;Add(rows,"未收费意向：需根据今天条件重新预览确认。",()=>{referralPreview=ReferralPreview(pending.Tier,pending.Kind,pending.Target,pending.Zone);panelPage=0;},"重新预览");Add(rows,"取消意向不收费，不占本日成功推荐次数。",()=>host.Engine.EditMusicTrade(s=>s.MusicTrade.PendingIntent=null),"取消意向");}
                Add(rows,"公开档案推荐费360，与舞台共用开通；当前没有可售公开普通档案，方向禁用、不收费。");
                Add(rows,"五名成员仍使用原有免费电话到货通知；普通推荐不能代领成员物品或绕过结局联系限制。");
            }else if(panelTab=="ledger"){
                foreach(var r in d.Receipts.Values.Where(x=>x.Kind is "purchase" or "sale" or "ordinary_sale").OrderByDescending(x=>x.Day)){
                    string text="第"+r.Day+"天 "+(r.Kind=="purchase"?"实际收购支出 ":"实际销售收入 ")+r.Total;
                    if(r.Kind=="sale"&&r.Allocations.Count==1){var cost=d.Next.Realized.GetValueOrDefault(r.Id)?.Cost;text+="；成交时出货成本 "+(cost.HasValue?cost.Value.ToString():"未知或旧记录未冻结");if(cost.HasValue)text+="；未分摊渠道/开通费的出货差额 "+(r.Total-cost.Value);}
                    Add(rows,text);
                }
                foreach(var fee in d.Fees)Add(rows,"业务实际费用 "+fee.Value+(d.Refunds.Contains(fee.Key)?"；已系统回冲同额一次":"；未退款"));
                foreach(var expense in d.Investments.Values)Add(rows,"实际投入："+(expense.Energy?"能源":"材料")+"×"+expense.Units+"；成本 "+(expense.Known?expense.Amount.ToString():"来源未知")+"。");
                foreach(var h in d.Histories.Values.Where(x=>x.Public).OrderByDescending(x=>x.Day))Add(rows,"第"+h.Day+"天："+h.Text);
                Add(rows,"普通器材使用年限未知；没有实际演出使用记录不追加Live或夺冠经历。剧情原件沿包C既有真实履历和公开权限。");
            }
            if(rows.Count==0)Add(rows,"当前没有可显示的记录；可以先查看真实库存、已有工单和业务资格。");return rows;
        }
    }
}
