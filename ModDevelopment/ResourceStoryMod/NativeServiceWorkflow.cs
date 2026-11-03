using System;
using System.Collections.Generic;
using System.Linq;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageB
    {
        private NativeWorkflowUI serviceUI;
        private string workflowView="auto",workflowNotice="";
        private bool workflowLegacy,workflowQuoteEdited;
        private int workflowPage,workflowPart;
        private sealed class WorkflowPart{internal int Entity;internal string Sku,Slot,Step,Block;}
        private void ResetWorkflow(){workflowView="auto";workflowNotice="";workflowLegacy=false;workflowQuoteEdited=false;workflowPage=0;workflowPart=0;}
        internal void WorkflowView(string view){workflowView=view;workflowPage=0;workflowLegacy=false;}
        internal void WorkflowMachines(int work,int measure){bench=work;calibrator=measure;}
        private void WorkflowAction(Action action){try{action();host.Persist();workflowView="auto";workflowNotice="";}catch(Exception e){workflowNotice=e.Message;core.LoggerInstance.Warning("WORKFLOW_ACTION "+e.Message);}}
        internal int WorkflowStage(EquipmentService svc)
        {
            if(svc==null)return 1;var s=host.Engine.Snapshot();var u=PackageDUse.ForService(s,svc.Id);
            if(u?.Status is "pickup" or "cancel_pickup")return economy.Trade.ExternalPresent(svc.Id)&&host.Day>=u.DueDay?5:4;
            if(svc.Delivery!=null||u?.Status=="done")return 5;
            var current=svc.Steps.LastOrDefault(x=>x.Status=="running");
            if(current!=null)return current.Kind.EndsWith("calibrate")?3:2;
            if(u!=null&&PackageDUse.ReadyBlock(s,u)==null)return 4;
            if(!Working(bench,"ms_machine_electrobench")||!Working(calibrator,"ms_machine_calibrator"))return 1;
            return svc.EffectiveSlots.Any(x=>!svc.InitialSlots.TryGetValue(x.Key,out int old)||old!=x.Value)?3:2;
        }
        private string WorkflowStepBlock(EquipmentService svc,string kind,int machine,string slot=null,int component=0)
        {
            try{
                var state=host.Engine.Snapshot();var custody=svc.Held.Values.Where(x=>!svc.EffectiveSlots.Values.Contains(x.Item.Entity)&&!x.DeliverToMember).Select(x=>NativeMemberEconomy.Decode(x.Item.Snapshot)).ToList();
                var choices=new Dictionary<string,int[]>();
                if(component>0){var part=economy.FindPlayerItem(component)??custody.FirstOrDefault(x=>x.uniqueId==component);if(part==null)return "对应的实际配件不在库存或本工单内";
                    if(!state.Workshop.Advanced.Facts.ContainsKey(component))PackageB.Fact(state.Workshop.Advanced,component,part.identifier,"player",Grade(part));choices[part.identifier]=new[]{component};}
                var spec=PackageB.ServiceSpec(state,svc.Id,kind,null,slot,component,"basic");
                return economy.Workshop.PreparedBlock(spec,machine,choices,custody);
            }catch(InvalidOperationException e){return e.Message;}
        }
        private List<WorkflowPart> WorkflowParts(EquipmentService svc)
        {
            var parts=new List<WorkflowPart>();var inputs=economy.AllPlayerItems().Where(PlayerOwned).Select(x=>(Entity:x.uniqueId,Sku:x.identifier)).Concat(svc.Held.Values.Where(x=>x.Owner=="player"&&!svc.EffectiveSlots.Values.Contains(x.Item.Entity)).Select(x=>(Entity:x.Item.Entity,Sku:x.Item.ItemId))).Distinct().OrderBy(x=>x.Entity);
            foreach(var part in inputs){foreach(string slot in PackageBCatalog.Rows("module_effects").Where(x=>x["host_id"]==A.Facts[svc.Host].Sku&&x["id"]==part.Sku&&x["effect_status"]=="B_enabled").Select(x=>x["slot"]).Concat(PackageBCatalog.Rows("internal_repair_slots").Where(x=>x["host_id"]==A.Facts[svc.Host].Sku&&x["accepted_sku"]==part.Sku).Select(x=>x["slot"])).Distinct()){
                string step=slot=="interface"?"b15_s06_install_simple":"b15_s07_install_precision";
                parts.Add(new(){Entity=part.Entity,Sku=part.Sku,Slot=slot,Step=step,Block=svc.EffectiveSlots.ContainsKey(slot)?"已有配件，普通续用保留旧件":WorkflowStepBlock(svc,step,bench,slot,part.Entity)});
            }}return parts.OrderBy(x=>x.Block!=null).ThenBy(x=>x.Entity).ToList();
        }
        private static string InstallTitle(string sku)=>sku switch{"ms_module_quick_patch"=>"安装快接换场件","ms_module_tone_filter"=>"安装音色滤波件","ms_module_stable_bus"=>"安装稳定总线件",_=>"安装选中配件"};
        private void ReturnToUse(){Close();economy.Trade.OpenNextPanel("use");}
        private void DrawServiceWorkflow(EquipmentService svc)
        {
            PanelRows=0;
            serviceUI??=new NativeWorkflowUI(body);var ui=serviceUI;
            float w=Math.Min(1120,Screen.width-30),h=Math.Min(880,Screen.height-30),side=w>=1000?285:245,left=w-side-55,top=188,bottom=h-116;
            var state=host.Engine.Snapshot();var use=PackageDUse.ForService(state,svc.Id);if(use==null){workflowLegacy=true;return;}
            int stage=WorkflowStage(svc);bool done=svc.Delivery!=null||use.Status=="done";
            var step=svc.Steps.LastOrDefault(x=>x.Status=="running");var job=step==null?null:state.Workshop.Jobs.GetValueOrDefault(step.Id);
            string view=workflowView=="auto"?(job!=null?"progress":stage==1?"prepare":stage==2?"install":stage==3?"calibrate":"handover"):workflowView;
            NativeWorkflowUI.Fill(new Rect(0,0,w,h),NativeWorkflowUI.Ink);
            ui.Small(new Rect(22,12,500,23),"普通原件续用  /  当前维护工单");
            if(ui.Button(new Rect(w-224,12,102,33),"返回委托"))ReturnToUse();
            if(ui.Button(new Rect(w-112,12,92,33),"返回营业"))Close();
            string phase=done?"已交回":job!=null?(step.Kind.EndsWith("calibrate")?"校准加工中":"安装加工中"):stage==1?"准备机器":stage==2?"待安装配件":stage==3?"待基础校准":stage==5?"物主到场，待交收":use.DueDay>=0?"已预约，等待领取":"已合格，待预约";
            ui.Title(new Rect(22,46,w-44,37),Label(A.Facts[svc.Host].Sku)+" · "+phase);
            ui.Steps(w,stage,done);
            string[] keys={"auto","prepare","install","calibrate","handover"},labels={"当前步骤","准备机器","安装配件","校准检测","预约交收"};float cell=(w-40)/5;
            for(int i=0;i<keys.Length;i++){string key=keys[i];if(ui.Button(new Rect(20+i*cell,139,cell-6,34),labels[i],workflowView==key)){workflowView=key;workflowPage=0;}}
            ui.Box(new Rect(20,top,left,bottom-top));ui.Box(new Rect(left+35,top,side,h-top-23));
            float sx=left+52;ui.Small(new Rect(sx,top+13,side-32,24),"这件原件的状态");
            ui.Text(new Rect(sx,top+43,side-32,57),"物主：普通器材客户\n用途："+PackageDCatalog.Buyer(use.Target).Name);
            ui.Small(new Rect(sx,top+108,side-32,70),"保留配置\n"+NativeWorkflowUI.SlotList(svc.InitialSlots));
            ui.Small(new Rect(sx,top+183,side-32,72),"当前配置\n"+NativeWorkflowUI.SlotList(svc.EffectiveSlots));
            ui.Small(new Rect(sx,h-110,side-32,78),"整备台："+(bench>0?"#"+bench:"未选择")+"\n标定台："+(calibrator>0?"#"+calibrator:"未选择")+"\n支付能力："+Math.Min(use.Budget,use.Cash));
            string mainText="继续当前步骤",block=null;Action main=()=>workflowView="auto";
            if(view=="prepare"){
                ui.Title(new Rect(37,top+13,left-34,33),"先选择本次使用的两台机器");
                string[] stations={"ms_machine_electrobench","ms_machine_calibrator"},names={"整备台","标定台"};
                for(int i=0;i<2;i++){int index=i;var machines=economy.AllPlayerItems().Where(x=>x.identifier==stations[index]&&Working(x.uniqueId,stations[index])).OrderBy(x=>x.uniqueId).ToArray();int selected=index==0?bench:calibrator;float y=top+58+i*87;
                    ui.Text(new Rect(37,y,left-220,29),names[index]+"："+(selected>0?"已选 #"+selected:"尚未选择"));
                    ui.Small(new Rect(37,y+32,left-220,43),machines.Length==0?"没有可用机器；请将自有机器放在店铺或仓库外层。":"外层可用 "+machines.Length+" 台；箱中的机器不能加工。");
                    if(ui.Button(new Rect(left-167,y,170,39),selected>0?"切换"+names[index]:"选择"+names[index],block:machines.Length==0?"无可用机器":null)){int found=Array.FindIndex(machines,x=>x.uniqueId==selected);int chosen=machines[(found+1)%machines.Length].uniqueId;if(index==0)bench=chosen;else calibrator=chosen;}
                }
                block=!Working(bench,"ms_machine_electrobench")?"请先选择可用整备台":!Working(calibrator,"ms_machine_calibrator")?"请先选择可用标定台":null;
                mainText="准备完成，继续当前步骤";main=()=>workflowView="auto";
            }else if(view=="install"){
                ui.Title(new Rect(37,top+13,left-34,33),"选择要装入空槽的实际配件");var parts=WorkflowParts(svc);
                var chosen=parts.FirstOrDefault(x=>x.Entity==workflowPart)??parts.FirstOrDefault(x=>x.Block==null)??parts.FirstOrDefault();
                int pages=Math.Max(1,(parts.Count+1)/2);workflowPage=RuntimeCompat.Clamp(workflowPage,0,pages-1);
                int row=0;foreach(var part in parts.Skip(workflowPage*2).Take(2)){var p=part;float y=top+58+row++*81;
                    ui.Text(new Rect(37,y,left-225,47),(chosen?.Entity==p.Entity?"● ":"")+Label(p.Sku)+" #"+p.Entity);
                    ui.Small(new Rect(37,y+48,left-225,28),p.Block??"新增到"+NativeWorkflowUI.Slot(p.Slot)+"，保留已有配置");
                    if(ui.Button(new Rect(left-167,y,170,39),chosen?.Entity==p.Entity?"当前选中":"选用这件",block:p.Block))workflowPart=p.Entity;
                }
                if(parts.Count==0)ui.Text(new Rect(37,top+65,left-35,100),"当前没有可用于这件器材的实际配件。请先准备对应型号，再回到此工单。");
                if(pages>1){if(ui.Button(new Rect(37,bottom-36,90,29),"上一组"))workflowPage=Math.Max(0,workflowPage-1);ui.Small(new Rect(140,bottom-32,130,27),(workflowPage+1)+" / "+pages+" 组");if(ui.Button(new Rect(left-104,bottom-36,107,29),"下一组"))workflowPage=Math.Min(pages-1,workflowPage+1);}
                mainText=chosen==null?"安装选中配件":InstallTitle(chosen.Sku);block=chosen?.Block??(chosen==null?"没有可用配件":null);
                main=()=>StartStep(svc.Id,"step_"+Guid.NewGuid().ToString("N"),chosen.Step,bench,slot:chosen.Slot,component:chosen.Entity);
            }else if(view=="calibrate"){
                ui.Title(new Rect(37,top+13,left-34,33),"为当前配置做基础校准");
                ui.Text(new Rect(37,top+64,left-35,82),"用时：60秒有效营业时间\n耗电：12E；需要实际标定台与放大镜。\n更换配置后，旧报告不能代替新测量。");
                string ready=PackageDUse.ReadyBlock(state,use);ui.Small(new Rect(37,top+159,left-35,70),ready==null?"当前用途已满足，可以进入预约交收。":"当前用途检查："+ready);
                block=WorkflowStepBlock(svc,"b15_s08_calibrate",calibrator);mainText=ready==null?"进入预约交收":"开始基础校准";
                if(ready==null){block=null;main=()=>workflowView="handover";}else main=()=>StartStep(svc.Id,"step_"+Guid.NewGuid().ToString("N"),"b15_s08_calibrate",calibrator);
            }else if(view=="progress"&&job!=null){
                ui.Title(new Rect(37,top+13,left-34,33),step.Kind.EndsWith("calibrate")?"基础校准已开工":"配件安装已开工");
                ui.Text(new Rect(37,top+64,left-35,66),"有效进度 "+job.ElapsedMs/1000+" / "+job.DurationMs/1000+" 秒\n本次总能耗 "+job.EnergyE+"E");
                ui.Small(new Rect(37,top+147,left-35,81),"此面板打开时暂停加工。\n返回营业后才会继续；缺电或机器无效时会等待。"+(job.PauseReason==null?"":"\n当前等待："+job.PauseReason));
                mainText="返回营业并继续加工";main=()=>{if(job.Status=="paused")economy.Workshop.Resume(job.Id);Close();};
            }else{
                ui.Title(new Rect(37,top+13,left-34,33),done?"本次交收已完成":use.Status is "pickup" or "cancel_pickup"?"预约与交收":"完工后预约物主领取");
                string ready=done?null:PackageDUse.ReadyBlock(state,use);
                ui.Text(new Rect(37,top+62,left-35,75),done?"实收 "+use.Paid+"；原件与配件已交回物主。":use.DueDay>=0?"预约领取：第 "+use.DueDay+" 天\n"+(economy.Trade.ExternalPresent(svc.Id)?"本人已实际到场。":"本人当前未到场，可返回营业等待。"):(ready??"当前用途验收已满足，可预约明天领取。"));
                if(!done){
                    if(!workflowQuoteEdited)quote=PackageB.Suggested(svc).ToString();
                    int amount=int.TryParse(quote,out int value)?Math.Max(0,value):0;int increment=new[]{1,10,100}[priceStep];float y=top+151;
                    if(ui.Button(new Rect(37,y,45,36),"−")){quote=Math.Max(0,amount-increment).ToString();workflowQuoteEdited=true;}
                    ui.Title(new Rect(94,y+3,160,33),"交收价 "+amount);
                    if(ui.Button(new Rect(265,y,45,36),"＋")){quote=Math.Min(int.MaxValue,(long)amount+increment).ToString();workflowQuoteEdited=true;}
                    if(ui.Button(new Rect(321,y,116,36),"步长 "+increment))priceStep=(priceStep+1)%3;
                    if(ui.Button(new Rect(448,y,Math.Max(110,left-466),36),"使用建议价")){quote=PackageB.Suggested(svc).ToString();workflowQuoteEdited=false;}
                    ui.Small(new Rect(37,y+43,left-35,40),"建议价 "+PackageB.Suggested(svc)+"；授权上限 "+use.AuthorizedQuote+"；可降价或免收。");
                }
                if(done){var pending=svc.Held.Values.FirstOrDefault(x=>x.Owner=="player"&&!svc.EffectiveSlots.Values.Contains(x.Item.Entity));mainText=pending==null?"返回委托摘要":"取回待返还工具";main=pending==null?ReturnToUse:()=>{if(!Claim(svc.Id,pending.Item.Entity))throw new InvalidOperationException("库存空间不足，工具仍在本工单待领。");};}
                else if(use.Status=="servicing"){mainText="预约明日领取";block=ready;main=()=>economy.Trade.BookUse(svc.Host);}
                else if(!economy.Trade.ExternalPresent(svc.Id)||host.Day<use.DueDay){mainText="返回营业，等待物主";main=Close;}
                else if(use.Status=="cancel_pickup"){mainText="免费交回原件";main=()=>economy.Trade.FinishUseWithoutWork(use.Entity);}
                else{mainText="交回器材并收款";try{if(!int.TryParse(quote,out int amount))throw new InvalidOperationException("交收价无效");PackageDUse.CheckDelivery(state,svc,amount,host.Day);}catch(InvalidOperationException e){block=e.Message;}main=()=>Deliver(svc.Id,int.Parse(quote),"delivery_"+svc.Id);}
            }
            if(ui.Button(new Rect(20,h-67,148,40),"更多维护操作")){workflowLegacy=true;tab="detail";page=0;}
            if(ui.Button(new Rect(left-275,h-67,295,40),mainText,true,block))WorkflowAction(main);
            string hint=done?"器材已交回；可逐件取回待返还工具，不会再次收款。":stage>=4?"预约与调价不收费；本人到场后才交回器材并收款。":view=="prepare"?"选好整备台和标定台后，点击右下角继续。":view=="install"?"保留右侧原配置，只安装当前选中的实际配件。":view=="calibrate"?"改装后重新校准；点击开工后返回营业加工。":"面板打开时暂停加工，请返回营业继续。";
            ui.Hint(new Rect(23,h-108,left-4,37),block??(workflowNotice.Length>0?workflowNotice:hint),block!=null||workflowNotice.Length>0);
            PanelRows=6;
        }
    }
}
