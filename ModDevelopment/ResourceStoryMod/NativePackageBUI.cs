using System;
using System.Collections.Generic;
using System.Linq;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageB
    {
        internal bool PanelOpen {get;private set;}
        internal int PanelRows {get;private set;}
        private float oldTime;
        private StoreUIManager panelUI;
        private PlayerStore panelStore;
        private GUIStyle body,button;
        private string tab="equipment",selectedService,notice="",search="",quote="0",actorFilter="all";
        private int priceStep;
        private string storageKind="all";
        private int page,bench,calibrator;
        private readonly Dictionary<string,Dictionary<string,int[]>> serviceMaterialChoices=new();
        internal void Open(string section="equipment")
        {
            if(!Enabled || PanelOpen || host.Visible)return;
            economy.Workshop.Close();economy.Business.Close();economy.CloseLedger();var store=PlayerStore.instance;
            if(store.lockStorePhysicalFlag!=0 || DialogUIManager.Instance.isConversationActiveFlag!=0)return;
            oldTime=Time.timeScale;panelStore=store;panelUI=StoreUIManager.Instance;store.lockStorePhysicalFlag++;NativePanelCursor.Acquire(panelUI);Time.timeScale=0;PanelOpen=true;tab=section;page=0;
        }
        internal void Close()
        {
            if(!PanelOpen)return;PanelOpen=false;Time.timeScale=oldTime;if(panelStore!=null)panelStore.lockStorePhysicalFlag=Math.Max(0,panelStore.lockStorePhysicalFlag-1);if(panelUI!=null)NativePanelCursor.Release(panelUI);panelUI=null;panelStore=null;
        }
        internal void Draw(GUIStyle text,GUIStyle buttons,GUIStyle window)
        {
            if(!Enabled || host.Visible)return;body=text;button=buttons;
            if(!PanelOpen){if(!economy.Workshop.PanelOpen && !economy.Business.PanelOpen && NativePanelGUI.Button(new Rect(660,Screen.height-58,220,42),"器材维护 / 箱具档案",button))Open();return;}
            float w=Math.Min(1120,Screen.width-30),h=Math.Min(880,Screen.height-30);
            NativePanelGUI.Window(781349,new Rect((Screen.width-w)/2,(Screen.height-h)/2,w,h),DrawWindow,"电声器材 · 维护、交收与档案",window,!workflowLegacy&&tab=="detail");
        }
        private void Action(Action action){try{action();notice="已执行并保存。";}catch(Exception e){notice=e.Message;core.LoggerInstance.Warning("PACKAGE_B_ACTION "+e.Message);}}
        private static string Label(string sku)=>PackageDNext.Name(sku);
        private static string ServiceStatus(string status)=>status switch{"authorized"=>"已受理","working"=>"加工中","ready"=>"工序完成，待交收","return_ready"=>"取消后待取回","delivered_return"=>"已交收，有玩家垫件待取","delivered"=>"已交收","cancelled"=>"已取消",_=>status};
        private string Name(string actor)=>actor=="all"?"全部":actor=="player"?"麻里奈（自有）":actor=="outside"?"普通器材物主":host.Package.Actors.FirstOrDefault(x=>x.Id==actor)?.DisplayName??actor;
        private int Machine(string station)=>station=="counter"?0:station=="calibrator"?calibrator:bench;
        private void Row(List<Action<float>> rows,string text,Action action=null,string label="选择")=>rows.Add(y=>{GUI.Label(new Rect(20,y,Math.Min(1120,Screen.width-30)-(action==null?60:270),82),text,body);if(action!=null && NativePanelGUI.Button(new Rect(Math.Min(1120,Screen.width-30)-240,y,205,54),label,button))Action(action);});
        private void SelectService(string id){selectedService=id;tab="detail";page=0;quote=ResourceStoryMod.Story.PackageB.Suggested(A.Services[id]).ToString();ResetWorkflow();}
        internal void OpenService(string id,bool lastPage=false){if(!lastPage&&ShowNativeRepairService(id))return;Open();SelectService(id);if(lastPage)page=int.MaxValue;}
        private void DrawWindow(int windowId)
        {
            if(!workflowLegacy&&tab=="detail"&&selectedService!=null&&A.Services.TryGetValue(selectedService,out var workflowService)&&workflowService.Actor=="outside"){DrawServiceWorkflow(workflowService);return;}
            float w=Math.Min(1120,Screen.width-30),h=Math.Min(880,Screen.height-30);var rows=new List<Action<float>>();
            string[] tabs={"equipment","services","machines","storage","ledger"},titles={"器材授权","当前工单","工作机器","箱具定位","检测账册"};
            float stepWidth=(w-165)/5;for(int i=0;i<tabs.Length;i++){string target=tabs[i];if(NativePanelGUI.Button(new Rect(15+i*stepWidth,40,stepWidth-10,38),titles[i],button)){tab=target;page=0;}}
            if(NativePanelGUI.Button(new Rect(w-140,40,120,38),"关闭",button))Close();
            GUI.Label(new Rect(20,85,w-40,62),notice==""?"自有工作不发工钱；成员到店授权、货到付款。所有实际部件保留原件ID。":notice,body);
            if(tab=="equipment"){
                foreach(var item in economy.AllPlayerItems().Where(x=>PackageBCatalog.Rows("equipment_package_b").Any(r=>r["id"]==x.identifier)).ToArray()){
                    var current=item;B.Instruments.TryGetValue(item.uniqueId,out var g);
                    if(g!=null && g.Owner!="player"){
                        Row(rows,current.name+" #"+current.uniqueId+" · 旧A受托器材（不补新费用）",()=>economy.Business.Return(current.uniqueId,g.Owner,0,"legacy_return_"+current.uniqueId),"原样归还本人");continue;
                    }
                    var existing=A.Services.Values.FirstOrDefault(x=>!x.Terminal && x.Host==current.uniqueId);
                    Row(rows,current.name+" #"+current.uniqueId+"\n"+Location(current),()=>{if(existing!=null)SelectService(existing.Id);else SelectService(Authorize("service_"+Guid.NewGuid().ToString("N"),"player",current.uniqueId));},existing==null?"开始自有维护":"继续现有工单");
                }
                string actor=economy.Business.CurrentActor;
                if(actor==null)Row(rows,"成员服务需要本人实际到店；可在成员电话界面预约明天。");
                else foreach(var owned in host.Engine.Snapshot().Economy.OwnedItems.Values.Where(x=>x.ActorId==actor && PackageBCatalog.Rows("equipment_package_b").Any(r=>r["id"]==x.ItemId)).ToArray()){
                    var item=owned;Row(rows,Name(actor)+"持有："+Label(item.ItemId)+" #"+item.EntityId,()=>SelectService(Authorize("service_"+Guid.NewGuid().ToString("N"),actor,item.EntityId)),"本人确认交入维护");
                }
            }else if(tab=="services"){
                foreach(var service in A.Services.Values.Where(x=>!x.Terminal).OrderBy(x=>x.Id)){var s=service;var running=s.Steps.FirstOrDefault(x=>x.Status=="running");string progress="";
                    if(running!=null && host.Engine.Snapshot().Workshop.Jobs.TryGetValue(running.Id,out var job))progress=" · 剩余有效 "+Math.Max(0,(job.DurationMs-job.ElapsedMs+999)/1000)+"秒 · "+(job.PauseReason??job.Status);
                    Row(rows,Name(s.Actor)+" · "+Label(A.Facts[s.Host].Sku)+" #"+s.Host+"\n"+ServiceStatus(s.Status)+progress+" · 建议交收 "+ResourceStoryMod.Story.PackageB.Suggested(s),()=>SelectService(s.Id),"查看 / 继续 / 交收");
                }
            }else if(tab=="detail" && selectedService!=null && A.Services.TryGetValue(selectedService,out var svc))BuildServiceRows(rows,svc);
            else if(tab=="machines"){
                foreach(var item in economy.AllPlayerItems().Where(x=>x.identifier=="ms_machine_electrobench" || x.identifier=="ms_machine_calibrator")){
                    var m=item;Row(rows,m.name+" #"+m.uniqueId+" · "+Location(m),()=>{if(m.identifier=="ms_machine_electrobench")bench=m.uniqueId;else calibrator=m.uniqueId;},"选为本次工作机器");
                    if(m.identifier!="ms_machine_calibrator")continue;var reference=A.References.GetValueOrDefault(m.uniqueId);
                    if(reference?.Reference>0){Row(rows,"参考输入 #"+reference.Reference+" · 自检"+(ResourceStoryMod.Story.PackageB.ReferenceValid(A,m.uniqueId)?"有效":"尚未有效"),()=>ReferenceJob("reference_"+Guid.NewGuid().ToString("N"),m.uniqueId,"selfcheck"),"30秒 / 4E 自检");Row(rows,"取出后原件进入机器待取清单；过去器材的精密证书不追溯作废。",()=>ReferenceJob("reference_"+Guid.NewGuid().ToString("N"),m.uniqueId,"remove"),"30秒 / 4E 拆参考");}
                    else foreach(var part in economy.AllPlayerItems().Where(x=>x.identifier=="ms_module_reference_input" && PlayerOwned(x))){var p=part;Row(rows,"实际参考输入 #"+p.uniqueId+" → 标定台 #"+m.uniqueId,()=>ReferenceJob("reference_"+Guid.NewGuid().ToString("N"),m.uniqueId,"install",p.uniqueId),"30秒 / 4E 装入");}
                }
                Row(rows,"当前整备台 #"+bench+" / 标定台 #"+calibrator+"。工作中的机器不能移动到箱中继续加工。");
                var state=host.Engine.Snapshot().Workshop;Row(rows,"P2："+(state.Professional?"已登记":"未登记")+" · 合法上层记录 "+state.UpperPurchaseReceipts.Count+" · 基础校准记录 "+state.BasicCalibrationReceipts.Count,()=>{if(!Qualify() && !host.Engine.Snapshot().Workshop.Professional)throw new InvalidOperationException("条件不足：需要当前有效整备台、真实上层白名单成交和已通过基础校准");},"登记专业制作 P2");
            }else if(tab=="storage"){
                rows.Add(y=>{string[] kinds={"all","HOUSEHOLD_GOOD","MODULE","MATERIAL","TOOL","MACHINE","box","DOCUMENT"},names={"全部","整机/家居","模块","材料","工具","机器","箱具","文档"};int index=Array.IndexOf(kinds,storageKind);if(NativePanelGUI.Button(new Rect(20,y,350,40),"分类："+names[Math.Max(0,index)],button)){storageKind=kinds[(index+1)%kinds.Length];page=0;}});
                foreach(var item in economy.AllPlayerItems().Where(x=>(storageKind=="all"||storageKind=="box"&&IsBox(x)||x.GetGameItemType().Contains(storageKind))&&(string.IsNullOrWhiteSpace(search) || x.name.Contains(search,StringComparison.OrdinalIgnoreCase) || x.uniqueId.ToString()==search)).ToArray()){
                    var i=item;var box=InventoryOwner(i.parentInventory);Row(rows,i.name+" #"+i.uniqueId+" · "+Location(i),IsBox(i)?()=>{Close();ShowContainer(i);}:IsBox(box)?()=>{Close();ShowContainer(box);}:null,IsBox(i)?"打开原生箱具":"打开所在箱具");
                }
                Row(rows,"文书箱最多18份且必须实格放下；三箱不嵌套，有内容不整箱卖。当前原版文书仅收纳，不自动回收或生成报告。");
            }else if(tab=="ledger"){
                rows.Add(y=>{if(NativePanelGUI.Button(new Rect(20,y,350,40),"筛选："+Name(actorFilter),button)){var values=new[]{"all","player","ms_nagi","ms_mahoro","ms_natsume","ms_houka","ms_hotaru"};actorFilter=values[(Array.IndexOf(values,actorFilter)+1)%values.Length];page=0;}});
                foreach(var demand in PackageBCatalog.Rows("member_demands").Where(x=>x["tier"]=="advanced" && (actorFilter=="all" || x["actor"]==actorFilter)))Row(rows,Name(demand["actor"])+"的器材用途："+(A.FulfilledDemands.Contains(demand["id"])?"已完成":"待交付符合条件的器材")+"\n"+Label(demand["host_id"])+" · 基础需求完成后，检查实际配置和有效校准。");
                foreach(var log in A.Ledger.Values.Where(x=>(actorFilter=="all" || x.Actor==actorFilter) && (string.IsNullOrWhiteSpace(search) || (x.Entity+" "+x.Id+" "+x.Service+" "+x.Kind+" "+x.Day+" "+x.Result).Contains(search,StringComparison.OrdinalIgnoreCase))).OrderByDescending(x=>x.Day).ThenBy(x=>x.Id))Row(rows,"第"+log.Day+"天 · "+Name(log.Actor)+" · 原件 #"+log.Entity+"\n"+log.Kind+" · "+log.Result+" · "+log.Service);
            }
            if(rows.Count==0)Row(rows,"暂无符合条件的记录或实际原件。");
            int count=Math.Max(1,(int)((h-215)/95)),pages=Math.Max(1,(rows.Count+count-1)/count);page=RuntimeCompat.Clamp(page,0,pages-1);PanelRows=Math.Min(count,rows.Count-page*count);
            for(int i=0;i<PanelRows;i++)rows[page*count+i](155+i*95);
            if(NativePanelGUI.Button(new Rect(20,h-55,150,38),"上一页",button))page=Math.Max(0,page-1);GUI.Label(new Rect(190,h-55,w-400,38),(page+1)+" / "+pages+" 页 · 整备台 #"+bench+" / 标定台 #"+calibrator,body);if(NativePanelGUI.Button(new Rect(w-175,h-55,150,38),"下一页",button))page=Math.Min(pages-1,page+1);
        }
        internal string Location(GameItem item)
        {
            var owner=InventoryOwner(item.parentInventory);return owner==null?"原生库存根":owner.name+" #"+owner.uniqueId+" 内";
        }
        internal static void ShowContainer(GameItem item){var window=item.contentWindow??throw new InvalidOperationException("原生箱具窗口缺失");window.EnsureFloatingDock();window.Show().Center();window.ToFront(true);}
        private void BuildServiceRows(List<Action<float>> rows,EquipmentService svc)
        {
            var f=A.Facts[svc.Host];Row(rows,Name(svc.Actor)+" · "+Label(f.Sku)+" #"+svc.Host+" · "+ServiceStatus(svc.Status)+"\n工单 "+svc.Id+" · 建议总额 "+ResourceStoryMod.Story.PackageB.Suggested(svc));
            if(svc.Actor=="outside")Row(rows,"普通续用使用原普通位置；完工后通知次日领取，无位待安排。",()=>economy.Trade.BookUse(svc.Host),"完工通知领取");
            else if(svc.Actor!="player")Row(rows,"货到付款；余额不足可降价或下次来取，无定金、债务或期限罚金。",()=>economy.Business.Book(svc.Actor),"电话约本人明天来");
            if(svc.Delivery==null && svc.Status!="return_ready"){
                foreach(var supplied in host.Engine.Snapshot().Economy.OwnedItems.Values.Where(x=>x.ActorId==svc.Actor && (WorkshopCatalog.Product(x.ItemId)?.Kind is "material" or "part" || NativeWorkshop.References.Values.Contains(x.ItemId)))){
                    var owned=supplied;Row(rows,"本人可供耗材："+Label(owned.ItemId)+" #"+owned.EntityId+"。只对需要该型号的工序生效，不计玩家材料费。",()=>{if(!serviceMaterialChoices.ContainsKey(svc.Id))serviceMaterialChoices[svc.Id]=new();string reference=NativeWorkshop.References.FirstOrDefault(x=>x.Value==owned.ItemId).Key??owned.ItemId;serviceMaterialChoices[svc.Id][reference]=new[]{owned.EntityId};},"指定本人提供此件");
                }
                foreach(var row in PackageBCatalog.Rows("services_package_b").Where(x=>!x["id"].Contains("install_"))){var r=row;string kind=r["id"],scope=f.Sku=="ms_gear_drum_practice_pad"?"passive":"basic";
                    if(kind.EndsWith("contact_repair") || kind.EndsWith("pad_repair")){
                        string[] domains=kind.EndsWith("pad_repair")?new[]{"surface","support"}:new[]{"wire","capacitor"};foreach(string domain in domains){string d=domain;Row(rows,r["name"]+" / "+d+" · "+r["seconds_draft"]+"秒 / "+r["energy_draft"]+"E · 人工参考 "+r["labor_reference"],()=>StartStep(svc.Id,"step_"+Guid.NewGuid().ToString("N"),kind,Machine(ResourceStoryMod.Story.PackageBCatalog.Station(r["station"])),d),"按已知故障开工");}
                    }else Row(rows,r["name"]+" · "+r["seconds_draft"]+"秒 / "+r["energy_draft"]+"E · 人工参考 "+r["labor_reference"]+(kind.EndsWith("calibrate")?"（测量不保证合格）":""),()=>StartStep(svc.Id,"step_"+Guid.NewGuid().ToString("N"),kind,Machine(PackageBCatalog.Station(r["station"])),scope:scope),"确认开工");
                }
                if(f.Sku!="ms_gear_drum_practice_pad")Row(rows,"精密校准：有效参考标定台，60秒 / 12E；当前配置变化会失效。",()=>StartStep(svc.Id,"step_"+Guid.NewGuid().ToString("N"),"b15_s08_calibrate",calibrator,scope:"precision"),"确认精密测量");
                foreach(string currentSlot in svc.EffectiveSlots.Keys){string slot=currentSlot;Row(rows,"拆下实际部件 #"+svc.EffectiveSlots[slot]+"，保留原ID；原封装恢复时保留原故障。",()=>StartStep(svc.Id,"step_"+Guid.NewGuid().ToString("N"),slot=="interface"?"b15_s06_install_simple":"b15_s07_install_precision",bench,slot:slot),"授权拆卸 "+slot);}
                var candidates=economy.AllPlayerItems().Select(x=>(Entity:x.uniqueId,Sku:x.identifier)).Concat(host.Engine.Snapshot().Economy.OwnedItems.Values.Where(x=>x.ActorId==svc.Actor).Select(x=>(Entity:x.EntityId,Sku:x.ItemId))).Concat(svc.Held.Values.Select(x=>(Entity:x.Item.Entity,Sku:x.Item.ItemId))).Distinct().ToArray();
                foreach(var part in candidates){var p=part;foreach(string slot in PackageBCatalog.Rows("module_effects").Where(x=>x["host_id"]==f.Sku && x["id"]==p.Sku && x["effect_status"]=="B_enabled").Select(x=>x["slot"]).Concat(PackageBCatalog.Rows("internal_repair_slots").Where(x=>x["host_id"]==f.Sku && x["accepted_sku"]==p.Sku).Select(x=>x["slot"])).Distinct()){
                    string target=slot;Row(rows,"实际配件 "+Label(p.Sku)+" #"+p.Entity+" → "+slot,()=>StartStep(svc.Id,"step_"+Guid.NewGuid().ToString("N"),target=="interface"?"b15_s06_install_simple":"b15_s07_install_precision",bench,slot:target,component:p.Entity),"授权安装该原件");
                }}
                if(svc.Actor=="ms_hotaru")foreach(var cable in economy.AllPlayerItems().Where(x=>x.identifier=="ms_item_balanced_cable" && PlayerOwned(x))){var c=cable;Row(rows,"玩家平衡线 #"+c.uniqueId+"，交付时只计一次价格。",()=>AddCable(svc.Id,c.uniqueId),"加入本次交付");}
                if(svc.Actor!="player")rows.Add(y=>{int amount=int.TryParse(quote,out int current)?Math.Max(0,current):0;int increment=new[]{1,10,100}[priceStep];GUI.Label(new Rect(20,y,200,40),"交收 "+amount,body);if(NativePanelGUI.Button(new Rect(220,y,80,42),"−",button))quote=Math.Max(0,amount-increment).ToString();if(NativePanelGUI.Button(new Rect(310,y,80,42),"＋",button))quote=Math.Min(int.MaxValue,(long)amount+increment).ToString();if(NativePanelGUI.Button(new Rect(400,y,180,42),"步长："+increment,button))priceStep=(priceStep+1)%3;if(NativePanelGUI.Button(new Rect(590,y,220,42),"使用建议价",button))quote=ResourceStoryMod.Story.PackageB.Suggested(A.Services[svc.Id]).ToString();});
                Row(rows,svc.Actor=="player"?"自有维护不收费；完成至少一项实际工序后取回同一原件。":"按本次选择的交收价货到付款；本人未到或余额不足时可降价、免收或等下次。",()=>{if(!int.TryParse(quote,out int amount) || amount<0)throw new InvalidOperationException("交收金额无效");Deliver(svc.Id,svc.Actor=="player"?0:amount,"delivery_"+svc.Id);},svc.Actor=="player"?"自有完成 / 取回":"本人到店 · 交收");
                Row(rows,"取消后原件归本人；已完成改善保留，已用耗材不退款，玩家未付款可拆件取回。",()=>Cancel(svc.Id),"取消授权并归还");
            }
            foreach(var step in svc.Steps){var st=step;if(host.Engine.Snapshot().Workshop.Jobs.TryGetValue(st.Id,out var job)){Row(rows,st.Kind+" · "+job.Status+" · "+job.ElapsedMs/1000+"/"+job.DurationMs/1000+"秒\n"+job.PauseReason,job.Status=="paused"?()=>economy.Workshop.Resume(job.Id):job.Status=="running"?()=>economy.Workshop.Pause(job.Id):null,job.Status=="paused"?"继续":"暂停");}}
            if(svc.Status=="return_ready" || svc.Status=="delivered_return")foreach(var held in svc.Held.Values.Where(x=>x.Owner=="player" && !svc.EffectiveSlots.Values.Contains(x.Item.Entity))){var p=held;Row(rows,"玩家待取原件："+Label(p.Item.ItemId)+" #"+p.Item.Entity,()=>{if(!Claim(svc.Id,p.Item.Entity))throw new InvalidOperationException("库存仍无空间；原件继续保管");},"取回这一原件");}
        }
    }
}
