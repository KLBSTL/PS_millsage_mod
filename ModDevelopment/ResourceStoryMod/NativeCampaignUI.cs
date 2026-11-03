using System;
using System.Collections.Generic;
using System.Linq;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    internal sealed partial class NativeCampaignItems
    {
        private Vector2 panelScroll;
        private float panelContentHeight=900;
        private bool drawingBody,scrollToEnd;
        private Rect panelArea;
        private readonly Dictionary<string,string[]> wrappedLines=new();
        private readonly Dictionary<string,int> sourceChoices=new();
        private readonly Dictionary<string,int> stringChoices=new();
        internal string PanelJobId {get;private set;}
        internal string PanelSection=>tab;
        internal bool PanelHasOverflow {get;private set;}
        internal void NavigateJob(int index){tab="jobs";page=RuntimeCompat.Clamp(index,0,Math.Max(0,C.Jobs.Count-1));panelScroll=Vector2.zero;}
        internal void ShowOriginalDetail(string role){tab="originals";selectedRole=role;panelScroll=Vector2.zero;}
        internal void ShowSourceChoices(){Section("selections");}
        internal void ScrollPanelToEnd(){scrollToEnd=true;}
        internal int[] SourceCandidates(string role)
        {
            string sku=role=="c_mic"?"ms_gear_stage_mic":"ms_gear_commercial_guitar",actor=role=="c_mic"?"ms_hotaru":"ms_nagi";
            return E.AllPlayerItems().Where(x=>x.identifier==sku && E.PackageB.PlayerOwned(x)).Select(x=>x.uniqueId)
                .Concat(host.Engine.Snapshot().Economy.OwnedItems.Values.Where(x=>x.ActorId==actor && x.ItemId==sku).Select(x=>x.EntityId)).Distinct().OrderBy(x=>x).ToArray();
        }
        private void Section(string section){tab=section;page=0;selectedRole=null;panelScroll=Vector2.zero;}
        private void Label(ref float y,float w,string text,float minimum=36)
        {
            float lineHeight=body.CalcHeight(new GUIContent("国"),w);
            string key=w.ToString("F1")+"|"+text;
            if(!wrappedLines.TryGetValue(key,out var lines)){
                var result=new List<string>();string line="";
                foreach(char ch in text){
                    if(ch=='\n'){result.Add(line);line="";continue;}
                    if(line.Length>0 && body.CalcHeight(new GUIContent(line+ch),w)>lineHeight+1){result.Add(line);line="";}
                    line+=ch;
                }
                result.Add(line);lines=result.ToArray();if(wrappedLines.Count>500)wrappedLines.Clear();wrappedLines[key]=lines;
            }
            foreach(string line in lines){var rect=new Rect(0,y,w,lineHeight);if(VisibleRect(rect,out var placed))GUI.Label(placed,line,body);y+=lineHeight;}
            y+=8;
        }
        private bool VisibleRect(Rect rect,out Rect placed)
        {
            placed=drawingBody?new Rect(rect.x+panelArea.x,rect.y+panelArea.y-panelScroll.y,rect.width,rect.height):rect;
            return !drawingBody || placed.y>=panelArea.y-0.1f && placed.yMax<=panelArea.yMax+0.1f;
        }
        private bool PanelButton(Rect rect,string text,GUIStyle style)=>VisibleRect(rect,out var placed) && NativePanelGUI.Button(placed,text,style);
        private void DrawCampaignPanel()
        {
            drawingBody=false;PanelRenderedRows=0;PanelJobId=null;float w=Math.Min(1080,Screen.width-40),h=Math.Min(800,Screen.height-40);
            float col=(w-55)/3;
            if(PanelButton(new Rect(15,34,col,38),"剧情委托",button))Section("jobs");
            if(PanelButton(new Rect(25+col,34,col,38),"原件与经历",button))Section("originals");
            if(C.Stage>=2 && PanelButton(new Rect(35+col*2,34,col,38),"后续需求 / 器材",button))Section("requests");
            GUI.Label(new Rect(15,80,w-30,66),message.Length>0?message:C.LastBlock??"选择办理方式；关闭面板后，加工才会继续。",body);
            var jobs=CampaignPanel.Jobs(C);var originals=C.Originals.Values.Where(x=>x.Visible && x.Location!="sealed_payload").ToArray();
            int total=tab=="jobs"?jobs.Length:tab=="originals"?originals.Length:0;
            page=RuntimeCompat.Clamp(page,0,Math.Max(0,total-1));
            var area=new Rect(15,151,w-30,h-250);float cw=area.width-24,y=0;
            panelArea=area;panelScroll.y=RuntimeCompat.Clamp(panelScroll.y,0,Math.Max(0,panelContentHeight-area.height));drawingBody=true;
            if(tab=="jobs"){
                Label(ref y,cw,"未完工 "+jobs.Count(j=>!j.Ready)+" / 已完工 "+jobs.Count(j=>j.Ready)+"；新近未完工委托排在前面。");
                if(jobs.Length>0){var j=jobs[page];PanelJobId=j.Id;DrawCampaignJob(j,cw,ref y);PanelRenderedRows++;}
                else Label(ref y,cw,"剧情提出委托后，这里会显示原件、工序和办理方式。");
            }else if(tab=="originals"){
                var original=selectedRole!=null?C.Originals.GetValueOrDefault(selectedRole):originals.ElementAtOrDefault(page);
                if(original!=null){
                    Label(ref y,cw,PackageC.Definitions.First(x=>x.Role==original.Role).Name);Label(ref y,cw,Ownership(original));
                    Label(ref y,cw,PackageC.Description(host.Engine.Snapshot(),original.Role,selectedRole!=null));
                    if(PanelButton(new Rect(0,y,cw,38),selectedRole==null?"查看完整履历":"返回简要经历",button)){selectedRole=selectedRole==null?original.Role:null;panelScroll=Vector2.zero;}y+=46;
                    if(original.Custodian=="player"){
                        if(PanelButton(new Rect(0,y,cw,38),original.Location=="native"?"存入委托保管":"取出到合法库存",button))Act(()=>{if(original.Location=="native")StoreOriginal(original.Role);else if(!ClaimOriginal(original.Role))throw new InvalidOperationException("库存没有合法空位，原件继续保管。");});y+=46;
                    }
                    PanelRenderedRows++;
                }else Label(ref y,cw,"尚无已公开的原件记录。");
            }else if(tab=="selections")DrawSourceChoices(cw,ref y);
            else DrawRequestList(cw,ref y);
            drawingBody=false;panelContentHeight=y+8;PanelHasOverflow=panelContentHeight>area.height;
            if(scrollToEnd){panelScroll.y=Math.Max(0,panelContentHeight-area.height);scrollToEnd=false;}
            if(PanelHasOverflow){
                if(PanelButton(new Rect(w-335,h-87,150,32),"向上查看",button))panelScroll.y=Math.Max(0,panelScroll.y-area.height/2);
                if(PanelButton(new Rect(w-175,h-87,150,32),"向下查看",button))panelScroll.y=Math.Min(Math.Max(0,panelContentHeight-area.height),panelScroll.y+area.height/2);
            }
            float footer=h-50,bw=(w-60)/4;
            if(total>0){
                if(page>0 && PanelButton(new Rect(15,footer,bw,36),"上一件",button)){page--;selectedRole=null;panelScroll=Vector2.zero;}
                if(page+1<total && PanelButton(new Rect(25+bw,footer,bw,36),"下一件",button)){page++;selectedRole=null;panelScroll=Vector2.zero;}
                GUI.Label(new Rect(15, h-87,220,32),(page+1)+" / "+total+(PanelHasOverflow?"　可分段查看":""),body);
            }
            if(host.Engine.Frame()!=null && PanelButton(new Rect(35+bw*2,footer,bw,36),"返回剧情",button)){Close();host.OpenView();return;}
            if(PanelButton(new Rect(45+bw*3,footer,bw,36),"保存并关闭",button))Close();
        }
        private void DrawCampaignJob(CampaignWork j,float w,ref float y)
        {
            Label(ref y,w,j.Name+" · "+CampaignPanel.Status(j));
            string Who(string actor)=>E.Business.ActorName(actor);
            Label(ref y,w,"委托人："+Who(j.SupplyActor)+"；原件："+PackageC.Definitions.First(x=>x.Role==j.HostRole).Name+" #"+C.Originals[j.HostRole].Entity);
            if(!j.Ready){
                var step=j.Steps[j.Step];Label(ref y,w,step.Name+" · 剩余 "+Math.Max(0,(step.DurationMs-step.ElapsedMs+999)/1000)+" 秒；后房供电余 "+j.SupportRemainingE.ToString("0.##")+"E");
                string Items(System.Collections.Generic.Dictionary<string,int> list)=>list.Count==0?"无":string.Join("、",list.Select(x=>E.Workshop.Label(x.Key)+" ×"+x.Value));
                Label(ref y,w,"本步材料："+Items(step.Materials)+"；工具："+Items(step.Tools));
                bool counter=step.Station=="counter",supportOnly=step.Station=="support_only";
                string sku=step.Station=="calibration"?"ms_machine_calibrator":"ms_machine_electrobench";
                var stations=E.AllPlayerItems().Where(x=>x.identifier==sku && E.PackageB.PlayerOwned(x) && E.PackageB.Working(x.uniqueId,sku) && !MachineBusy(x.uniqueId,j.Id) && !Workshop.Busy(host.Engine.Snapshot().Workshop,x.uniqueId)).OrderBy(x=>x.uniqueId).ToArray();
                if(!stations.Any(x=>x.uniqueId==machine))machine=stations.FirstOrDefault()?.uniqueId??0;
                if(counter)Label(ref y,w,"自有工位：柜台；此步骤不需要机器。");
                else if(supportOnly)Label(ref y,w,"此步骤需要后房专用工位。");
                else{
                    if(PanelButton(new Rect(0,y,w,38),"工位："+(stations.FirstOrDefault(x=>x.uniqueId==machine) is GameItem current?current.name+" #"+current.uniqueId:"暂无可用的"+(step.Station=="calibration"?"标定台":"整备台")),button) && stations.Length>0){int ix=Array.FindIndex(stations,x=>x.uniqueId==machine);machine=stations[(ix+1)%stations.Length].uniqueId;}y+=46;
                }
                bool wasEnabled=GUI.enabled;GUI.enabled=wasEnabled && !supportOnly && (counter || stations.Length>0);float half=(w-10)/2;
                if(PanelButton(new Rect(0,y,half,38),"自有工位 · 委托供料",button))Act(()=>StartOwn(j.Id,machine,true));
                if(PanelButton(new Rect(half+10,y,half,38),"自有材料优先 · 供料补缺",button))Act(()=>StartOwn(j.Id,machine,false,true));GUI.enabled=wasEnabled;y+=46;
                if(PanelButton(new Rect(0,y,half,38),"后房 · 营业期办理",button))Act(()=>SelectSupport(j.Id));
                if(j.OvernightAllowed && PanelButton(new Rect(half+10,y,half,38),"后房 · 安排今夜办理",button))Act(()=>SelectSupport(j.Id,true));y+=46;
                if(PanelButton(new Rect(0,y,w,38),"暂停办理",button))Act(()=>Pause(j.Id));y+=46;
                Label(ref y,w,"自有工位使用玩家电源；委托方供料不收费。工具用后返还。最早取件：第 "+j.EarliestDay+" 天。");
            }
            if(C.Stage>=2)y=DrawFullJob(j,w,y);
            foreach(var r in j.Resources.Where(x=>x.Owner=="player" && x.State=="pending_return")){
                if(PanelButton(new Rect(0,y,w,38),"取回工具："+E.Workshop.Label(r.Reference)+" #"+r.Entity,button))Act(()=>{if(!ClaimTool(j.Id,r.Entity))throw new InvalidOperationException("库存没有合法空位，工具继续保管。");});y+=46;
            }
        }
        private float DrawFullJob(CampaignWork j,float w,float y)
        {
            if(j.Id==PackageC.FinalTune && j.Ready && !C.Receipts.Contains("029.joint_claim_once") && j.TargetSignatures.Any(x=>PackageC.ConfigSignature(C.Originals[x.Key])!=x.Value)){
                if(PanelButton(new Rect(0,y,w,38),"配置已变化：仅复核受影响器材",button))Act(()=>E.MutateNative(()=>host.Engine.EditCampaignItems(s=>PackageC.PrepareFinalRecheck(s,host.Day,this)),()=>{}));y+=46;
            }
            if((j.Id==PackageC.BassStrings || j.Id==PackageC.GuitarStrings) && j.Step==0 && j.Steps[0].ElapsedMs==0 && j.Steps[0].Bound.Count==0){
                string sku=j.Id==PackageC.BassStrings?"ms_strings_bass5_nickel":"ms_strings_guitar6_tension";
                var strings=E.AllPlayerItems().Where(x=>x.identifier==sku && E.PackageB.PlayerOwned(x) && x.unitCount==1).OrderBy(x=>x.uniqueId).ToArray();
                if(strings.Length>0){int ix=RuntimeCompat.Clamp(stringChoices.GetValueOrDefault(j.Id),0,strings.Length-1);var item=strings[ix];float half=(w-10)/2;
                    if(PanelButton(new Rect(0,y,half,38),"成品套弦 #"+item.uniqueId+"（"+(ix+1)+"/"+strings.Length+"）",button))stringChoices[j.Id]=(ix+1)%strings.Length;
                    if(PanelButton(new Rect(half+10,y,half,38),"投入此套弦 · 仅验收安装",button))Act(()=>UseFinishedStrings(j.Id,item.uniqueId));y+=46;
                }
            }
            if(j.LaborReference>0){
                string state=j.FeeState switch{"waived"=>"已免收","paid"=>"已实付","quoted"=>"已报价 · 货到付款",_=>"待确认"};
                Label(ref y,w,"劳动费："+state+"；参考 "+j.LaborReference+"；当前报价 "+j.Quote);
                if(j.FeeState!="paid" && !j.Delivered){
                    if(!feeInputs.ContainsKey(j.Id))feeInputs[j.Id]=j.Quote.ToString();float third=(w-20)/3;
                    int draft=int.TryParse(feeInputs[j.Id],out int parsed)?parsed:j.Quote;
                    Label(ref y,w,"拟报价："+draft+"（确认后生效）");
                    int[] changes={-100,-10,-1,1,10,100};float segment=(w-50)/6;
                    for(int index=0;index<changes.Length;index++){int delta=changes[index];if(PanelButton(new Rect(index*(segment+10),y,segment,38),(delta>0?"+":"")+delta,button))feeInputs[j.Id]=RuntimeCompat.Clamp((long)draft+delta,0,int.MaxValue).ToString();}y+=46;
                    if(PanelButton(new Rect(0,y,third,38),"本次免收",button))Act(()=>{SetFee(j.Id,0);feeInputs[j.Id]="0";});
                    if(PanelButton(new Rect(third+10,y,third,38),"按参考价",button))feeInputs[j.Id]=j.LaborReference.ToString();
                    if(PanelButton(new Rect(third*2+20,y,third,38),"确认报价",button))Act(()=>SetFee(j.Id,draft));y+=46;
                    if(j.Ready && j.FeeState=="quoted"){
                        bool present=CanCollectFee(j.Id),funded=host.Engine.Snapshot().Economy.Wallets[j.SupplyActor].Balance>=j.Quote,enabled=GUI.enabled;
                        GUI.enabled=enabled && present && funded;
                        if(PanelButton(new Rect(0,y,w,38),"当面收取劳动费 "+j.Quote,button))Act(()=>CollectFee(j.Id));GUI.enabled=enabled;y+=46;
                        if(!present)Label(ref y,w,"委托人尚未到场，可电话约次日来店。");
                        else if(!funded)Label(ref y,w,"委托人本次资金不足，可降低报价、免收，或下次到场再结。原件继续保管。");
                    }
                }
            }else Label(ref y,w,"本单按剧情约定免费，不另收劳动费。");
            if(j.Ready && !j.Delivered && C.Originals[j.HostRole].Custodian=="player"){
                if(PanelButton(new Rect(0,y,w,38),"电话约委托方次日来店",button))Act(()=>E.Business.Book(j.SupplyActor));y+=46;
                if(j.Id.StartsWith("c_optional_")){
                    if(PanelButton(new Rect(0,y,w,38),"按约交回原件",button))Act(()=>ReturnOptional(j.Id));y+=46;
                }
                else Label(ref y,w,"取件与剧情节点一同办理；结清或免收后，返回剧情继续。");
            }
            if(j.Resources.Any(r=>r.Logical))Label(ref y,w,"本单专用供料按委托批次消耗，不占玩家库存。");
            return y;
        }
        private void DrawRequestList(float w,ref float y)
        {
            if(PanelButton(new Rect(0,y,w,38),"选择现有话筒 / 备用琴",button))Section("selections");y+=46;
            foreach(var request in C.UseRequests.Values.OrderByDescending(x=>x.Day)){
                string kind=request.Kind=="stage_stability"?"舞台稳定性适配":request.Kind=="known_fault"?"已知故障处理":"当前配置复核";
                Label(ref y,w,E.Business.ActorName(request.Actor)+" · "+kind+" · "+(request.Status switch{"offered"=>"可受理或谢绝","accepted"=>"已受理","completed"=>"已归还",_=>"已谢绝"}));
                if(request.Status=="offered"){
                    if(PanelButton(new Rect(0,y,w,38),"成员到场后确认受理",button))Act(()=>AcceptUseRequest(request.Id));y+=46;
                    if(PanelButton(new Rect(0,y,w,38),"谢绝本次提案",button))Act(()=>{host.Engine.EditCampaignItems(s=>PackageC.DeclineUseRequest(s,request.Id));host.Persist();});y+=46;
                }PanelRenderedRows++;
            }
            if(C.UseRequests.Count==0)Label(ref y,w,"成员来访时，会按实际器材缺口提出需求。谢绝不影响主线。");
        }
        private void DrawSourceChoices(float w,ref float y)
        {
            Label(ref y,w,"正式受理前可选择现有器材。选择不锁货；受理时会核对物品仍在原处。来源失效时使用获授权的舞台借件。");
            foreach(string role in new[]{"c_mic","c_early_backup"}){
                string sku=role=="c_mic"?"ms_gear_stage_mic":"ms_gear_commercial_guitar",actor=role=="c_mic"?"ms_hotaru":"ms_nagi";
                Label(ref y,w,(role=="c_mic"?"话筒":"早期备用琴")+(C.Originals.ContainsKey(role)?"：已受理，保留原件":"：尚未受理"));
                if(C.Originals.ContainsKey(role))continue;
                var mine=E.AllPlayerItems().Where(x=>x.identifier==sku && E.PackageB.PlayerOwned(x)).Select(x=>x.uniqueId).ToArray();
                var members=host.Engine.Snapshot().Economy.OwnedItems.Values.Where(x=>x.ActorId==actor && x.ItemId==sku).Select(x=>x.EntityId).ToArray();
                var candidates=SourceCandidates(role);
                if(candidates.Length>0){
                    int ix=RuntimeCompat.Clamp(sourceChoices.GetValueOrDefault(role),0,candidates.Length-1),entity=candidates[ix];bool member=members.Contains(entity);
                    if(PanelButton(new Rect(0,y,w,38),(member?"成员自有":"玩家自有")+" #"+entity+"（"+(ix+1)+"/"+candidates.Length+"，点击换一件）",button))sourceChoices[role]=(ix+1)%candidates.Length;y+=46;
                    if(member){if(PanelButton(new Rect(0,y,w,38),"选用成员的此件器材",button))Act(()=>SelectExisting(role,entity,"member"));y+=46;}
                    else{
                        float half=(w-10)/2;
                        if(PanelButton(new Rect(0,y,half,38),"借出此件 · 比赛后归还",button))Act(()=>SelectExisting(role,entity,"loan"));
                        if(PanelButton(new Rect(half+10,y,half,38),"赠予此件 · 所有权交乐队",button))Act(()=>SelectExisting(role,entity,"donate"));y+=46;
                    }
                }else Label(ref y,w,"暂无符合条件的现有器材，将使用获授权的舞台借件。");
                if(C.Selections.TryGetValue(role,out var selected)){
                    Label(ref y,w,"当前：#"+selected.Entity+" · "+(selected.Mode switch{"loan"=>"玩家借出","donate"=>"赠予乐队",_=>"成员自有"})+(candidates.Contains(selected.Entity)?"":"（来源已失效，将在受理时改用舞台借件）"));
                    if(PanelButton(new Rect(0,y,w,38),"取消此项选择 · 使用默认来源",button))Act(()=>ClearSelection(role));y+=46;
                }
            }
            if(PanelButton(new Rect(0,y,w,38),"返回后续需求",button))Section("requests");y+=46;
        }
    }
}
