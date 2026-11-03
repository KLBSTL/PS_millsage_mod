using System;
using System.Collections.Generic;
using System.Linq;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageD
    {
        private NativeWorkflowUI useUI;
        private void DrawUseWorkflow()
        {
            PanelRows=0;
            useUI??=new NativeWorkflowUI(panelBody);var ui=useUI;
            float w=Math.Min(1100,Screen.width-30),h=Math.Min(820,Screen.height-30),side=w>=1000?285:245,left=w-side-55;
            NativeWorkflowUI.Fill(new Rect(0,0,w,h),NativeWorkflowUI.Ink);
            ui.Small(new Rect(22,12,500,24),"音乐经营  /  普通原件续用");
            if(ui.Button(new Rect(w-218,12,96,33),"经营主页")){nextView=null;panelPage=0;}
            if(ui.Button(new Rect(w-112,12,92,33),"返回店铺"))ClosePanel();
            var state=host.Engine.Snapshot();var active=N.Visits.GetValueOrDefault(PlayerStore.instance.currentClientInstance?.storeClient?.identifier??"");
            var use=active!=null?N.Uses.GetValueOrDefault(active.Entity):N.Uses.Values.Where(x=>x.Status is "offered" or "servicing" or "pickup" or "cancel_pickup").FirstOrDefault()??N.Uses.Values.OrderByDescending(x=>x.SoldDay).FirstOrDefault();
            PanelPages=1;
            if(use==null){ui.Title(new Rect(22,48,w-44,35),"当前没有待办的普通器材委托");ui.Text(new Rect(25,155,w-50,90),"普通物主携原件到店后，会在这里显示用途、预算和下一步。已有工单可从器材维护入口继续。");PanelRows=1;return;}
            var u=use;bool present=active?.Entity==u.Entity&&PlayerStore.instance.isClientArrived;
            var svc=u.Service==null?null:state.Workshop.Advanced.Services.GetValueOrDefault(u.Service);
            bool done=u.Status is "done" or "declined";int stage=u.Status is "offered" or "outside"?0:u.Status is "pickup" or "cancel_pickup"?(present&&host.Day>=u.DueDay?5:4):u.Status=="servicing"?economy.PackageB.WorkflowStage(svc):5;
            ui.Title(new Rect(22,45,w-44,39),Product(u.Sku)+" · "+(done?"本次事项已结束":UseStatus(u.Status)));
            ui.Steps(w,stage,done);
            float top=146,bottom=h-111;ui.Box(new Rect(20,top,left,bottom-top));ui.Box(new Rect(left+35,top,side,h-top-23));
            string headline=u.Status switch{"offered"=>"下一步：确认接手这件器材","servicing"=>"下一步：继续同一张维护工单","pickup"=>present&&host.Day>=u.DueDay?"物主已到场，可以办理交收":"已预约，等待物主再次到店","cancel_pickup"=>"取消后，等待原物主领回","declined"=>"本次未承接","done"=>"已将原件交回物主",_=>"物主在外使用器材"};
            ui.Title(new Rect(37,top+15,left-35,60),headline);
            ui.Text(new Rect(37,top+84,left-35,54),"本次用途："+PackageDCatalog.Buyer(u.Target).Name);
            ui.Text(new Rect(37,top+142,left-35,80),u.Status=="offered"?"保留原有配件；按新用途选择适配件并完成校准。受理时不收费。":svc!=null?(PackageDUse.ReadyBlock(state,u) is string block?"当前条件："+block:"用途验收条件已满足。可预约次日领取，交收时再收款。"):"原件与已装配件继续保留，可查看本次记录。");
            float sx=left+52;ui.Small(new Rect(sx,top+15,side-32,25),"委托摘要");
            ui.Text(new Rect(sx,top+49,side-32,55),"物主：普通器材客户\n"+(present?"本人已在柜台":"本人当前未到场"));
            ui.Small(new Rect(sx,top+119,side-32,25),"本次剩余支付能力");ui.Title(new Rect(sx,top+147,side-32,32),Math.Min(u.Budget,u.Cash).ToString());
            ui.Small(new Rect(sx,top+191,side-32,68),"保留配置\n"+NativeWorkflowUI.SlotList(svc?.EffectiveSlots??state.Economy.Business.Instruments[u.Entity].Slots));
            ui.Small(new Rect(sx,h-109,side-32,74),u.DueDay>=0?"预约：第 "+u.DueDay+" 天领取\n货到付款，可降价或稍后交收。":"货到付款\n余额不足时可降价或等待。");
            string reason=panelNotice;Action main=null;string mainText="查看器材记录",mainBlock=null;
            if(u.Status=="offered"){
                mainText="受理并进入维护";mainBlock=!present?"需要原物主携原件实际到场":!PackageDUse.CanAuthorize(state,u.Entity)?"已有一件普通器材正在保管，请先交回":null;
                main=()=>{IntakeUse(u.Entity,Math.Min(u.Budget,u.Cash));ClosePanel();economy.PackageB.OpenService("use_service_"+u.Entity);};
                var copy=host.Engine.Snapshot();copy.Economy.Business.Instruments[u.Entity].Owner="player";copy.Economy.Business.Instruments[u.Entity].Custody="player";copy.Workshop.Advanced.Facts[u.Entity].Owner="player";
                string enough=PackageD.Eligible(copy,PackageDCatalog.Buyer(u.Target),u.Entity,u.Zone,true,false,0,0,false);
                if(ui.Button(new Rect(37,top+220,Math.Min(240,left-35),39),"现有配置免费确认",block:!present?"原物主未到场":enough))PanelAction(()=>FinishUseWithoutWork(u.Entity));
                if(enough!=null)ui.Small(new Rect(37,top+265,left-35,42),"现有配置还不能直接交回："+enough);
            }else if(svc!=null&&!done){mainText=stage==5?"进入交收面板":"继续当前维护";main=()=>{ClosePanel();economy.PackageB.OpenService(svc.Id);};}
            else{mainText="返回音乐经营";main=()=>{nextView=null;panelPage=0;};}
            if(u.Status=="offered"&&ui.Button(new Rect(20,h-68,145,40),"暂不承接",destructive:true))PanelAction(()=>host.Engine.EditMusicTrade(s=>s.MusicTrade.Next.Uses[u.Entity].Status="declined"));
            else if(done&&svc!=null){var held=svc.Held.Values.FirstOrDefault(x=>x.Owner=="player"&&!svc.EffectiveSlots.Values.Contains(x.Item.Entity));if(held!=null&&ui.Button(new Rect(20,h-68,180,40),"取回待返还工具"))PanelAction(()=>{if(!economy.PackageB.Claim(svc.Id,held.Item.Entity))throw new InvalidOperationException("库存空间不足，工具仍在原工单待领。");});}
            if(ui.Button(new Rect(Math.Max(175,left-276),h-68,296,40),mainText,true,mainBlock))PanelAction(main);
            ui.Hint(new Rect(23,h-106,left-4,35),mainBlock??(reason.Length>0?reason:"面板打开时暂停。安装与校准需返回营业后加工。"),mainBlock!=null);
            PanelRows=6;
        }
    }
}
