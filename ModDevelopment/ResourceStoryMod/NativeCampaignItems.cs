using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using ResourceStoryMod.Story;
using UnityEngine;

namespace ResourceStoryMod
{
    internal sealed partial class NativeCampaignItems : ICampaignItemAdapter
    {
        private readonly Core core;
        private readonly NativeStoryHost host;
        private readonly List<System.Func<GameItem>> factories=new();
        private NativeMemberEconomy E=>host.Economy;
        private CampaignItemsState C=>host.ReadState?.CampaignItems;
        private NativeTransactionJournal sleepJournal;
        private bool optedIn,settlingDay;
        private readonly Dictionary<string,float> elapsed=new();
        private PlayerStore panelStore;
        private StoreUIManager panelUI;
        private float oldScale;
        private GUIStyle body,button;
        private string tab="jobs",message="",selectedRole;
        private int machine,page;
        private long describedRevision=-1;
        internal bool Enabled=>optedIn && C!=null && E.Enabled;
        internal bool PanelOpen {get;private set;}
        internal int PanelRenderedRows {get;private set;}
        internal bool SleepTransition=>sleepJournal!=null;
        private sealed class Config {public int Version {get;set;}=1;public bool Enabled {get;set;}public int Stage {get;set;}=2;}
        internal NativeCampaignItems(Core core,NativeStoryHost host)
        {
            this.core=core;this.host=host;
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(MapUIManager),"Sleep"),prefix:new HarmonyMethod(typeof(NativeCampaignItems),nameof(BeforeSleep)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(PlayerStore),"IsEligibleForRepo"),prefix:new HarmonyMethod(typeof(NativeCampaignItems),nameof(ProtectCustodyFromRepo)));
        }
        private static bool ProtectCustodyFromRepo(GameItem __0,ref bool __result)
        {
            if(Core.Instance?.Story?.Economy?.Trade?.HoldsNativeUse(__0)==true){__result=false;return false;}
            var campaign=Core.Instance?.Story?.Campaign;
            if(campaign?.Enabled!=true||__0==null)return true;
            if(!__0.IsTag(MusicCatalog.ProtectedTag)&&!campaign.C.Originals.Values.Any(x=>x.Entity==__0.uniqueId&&x.Location=="native"))return true;
            __result=false;return false;
        }
        internal void Register(ModItemDirectory directory)
        {
            foreach(var definition in PackageC.Definitions){var def=definition;if(directory.Has(def.Sku))continue;
                var factory=(System.Func<GameItem>)(Func<GameItem>)(()=>CreateItem(def));factories.Add(factory);
                if(!directory.Add(def.Sku,factory))throw new InvalidOperationException("剧情原件注册失败："+def.Sku);
            }
        }
        private GameItem CreateItem(CampaignDefinition def)
        {
            core.EnsureResources();var item=ItemDirectory.CreateEmptyItem(def.Sku);
            item.name=def.Name;item.shortDescription=def.Brief;item.longDescription=def.Brief;item.unitCount=1;item.unitValue=def.Value;item.unitBaseValue=def.Value;
            item.SetSpriteAndShape("resource_story","item");item.SetShape(new GridShapeBuilder(def.Width,def.Height).SetDataFill(def.Width,def.Height,1).Build());
            item.SetGameItemType(def.Sku=="ms_item_standard_strings"?"MATERIAL":"HOUSEHOLD_GOOD");
            if(!string.IsNullOrEmpty(def.Role) && !def.Ordinary)item.EnableTag(MusicCatalog.ProtectedTag,false);
            if(def.Actor!=null)item.EnableTag("ms.member."+def.Actor.Substring(3),false);
            if(def.Profile!=null)item.EnableTag("ms.instrument."+def.Profile,false);
            if(def.Role is "c_tin" or "c_stage_case"){
                var pair=DirectoryUtils.CreateInventoryWindow(def.Role=="c_tin"?12:16,def.Role=="c_tin"?6:12,true);pair.Item1.SetTitle(def.Role=="c_tin"?"封存铁盒 · 原件与谱页":"舞台器材运输箱 · 16×12");pair.Item1.canMove=true;item.SetContentWindow(pair.Item1);ContainerHelper.InitContainerItemTagOnly(pair.Item2.Cast<GameGridInventory>(),item);
            }
            E.PackageB.InitializeRepairItem(item);
            return item;
        }
        public CampaignResource Create(string sku,string reference,string owner,bool tool)
        {
            var item=(sku==PackageC.DiaryBlankReference?NativeDiaryCampaign.BlankCard():DirectoryMaster.Item(sku,true))??throw new InvalidOperationException("剧情供料工厂不可用："+sku);
            item.unitCount=1;
            return new CampaignResource{Entity=item.uniqueId,Sku=item.identifier,Reference=reference,Owner=owner,Tool=tool,Snapshot=NativeMemberEconomy.Encode(item)};
        }
        public void ToCustody(CampaignOriginal original)
        {
            if(original.Location=="native"){
                var item=E.FindPlayerItem(original.Entity)??throw new InvalidOperationException("找不到已登记原件，请检查实际库存，不能补发同名物。");
                if(item.identifier!=original.Sku || item.parentInventory==null || !item.parentInventory.Expel(item))throw new IOException("剧情原件无法移交保管");
                original.Snapshot=NativeMemberEconomy.Encode(item);
            }
            original.Location="custody";
        }
        internal bool Attach()
        {
            optedIn=false;
            if(host.Package.Id!="millsage" || host.IsTestPackage || !E.Enabled)return false;
            string path=Path.Combine(core.DataRoot,"millsage.campaign-items.json");
            var config=File.Exists(path)?JsonSerializer.Deserialize<Config>(File.ReadAllText(path),Core.Json):new Config();
            if(config==null || config.Version!=1)throw new InvalidDataException("剧情原件配置版本不受支持");
            optedIn=host.NormalPlay||host.Engine.Snapshot().CampaignItems!=null || (core.Test!=null?core.Test.Request.CampaignItems:config.Enabled);
            if(!optedIn)return false;
            return host.Engine.EnableCampaignItems(host.Day,this,host.NormalPlay?2:core.Test!=null?(core.Test.Request.CampaignComplete?2:1):config.Stage);
        }
        internal void Rebind(){if(host.Engine?.Snapshot().CampaignItems!=null){optedIn=true;host.Engine.EnableCampaignItems(host.Day,this);}}
        internal bool StoryMutation(Func<bool> action)
        {
            if(!Enabled)return action();
            bool moved=false;
            try{E.MutateNative(()=>moved=action(),()=>{});}catch(Exception error){message=error.Message;core.LoggerInstance.Warning("CAMPAIGN_TRANSITION "+error.Message);if(!host.NativeScene){host.CloseView();Open();}return false;}
            if(!moved && C?.PendingScene!=null){message=C.LastBlock;if(!host.NativeScene){host.CloseView();Open();}}
            return moved;
        }
        internal bool MachineBusy(int entity,string except=null)=>Enabled && C.Jobs.Values.Any(j=>j.Id!=except && j.Mode=="own" && j.Status=="running" && j.Machine==entity);
        internal bool BlocksSale(GameItem item)=>Enabled && item!=null && (MachineBusy(item.uniqueId) || C.Originals.Values.Any(x=>x.Entity==item.uniqueId && !x.Released) || item.FindAllChildItems().ToArray().Any(x=>MachineBusy(x.uniqueId) || C.Originals.Values.Any(r=>r.Entity==x.uniqueId && !r.Released)));
        private void MoveHostToWork(StoryState s,CampaignWork job)
        {
            var original=s.CampaignItems.Originals[job.HostRole];
            if(original.Custodian!="player")throw new InvalidOperationException("没有此原件的当前委托授权");
            ToCustody(original);
        }
        internal void SelectSupport(string id,bool overnight=false)
        {
            if(SleepTransition)throw new InvalidOperationException("等待当前日结完成");
            E.MutateNative(()=>host.Engine.EditCampaignItems(s=>{var j=s.CampaignItems.Jobs[id];MoveHostToWork(s,j);PackageC.SelectMode(s,id,overnight?"overnight":"support",host.Day);}),()=>{});
        }
        internal void Pause(string id)
        {
            if(SleepTransition)throw new InvalidOperationException("等待当前日结完成");
            E.MutateNative(()=>host.Engine.EditCampaignItems(s=>PackageC.SelectMode(s,id,"paused",host.Day)),()=>{});
        }
        private void ValidateStation(CampaignWork job,CampaignWorkStep step,int entity)
        {
            var state=host.Engine.Snapshot();
            if(Workshop.Busy(state.Workshop,entity) || (entity>0 && state.Workshop.Jobs.Values.Any(j=>!j.Terminal && j.Spec?.MeasuringMachine==entity)) || MachineBusy(entity,job.Id))throw new InvalidOperationException("工位已被其他作业占用");
            if(step.Station=="counter"){if(entity!=0)throw new InvalidOperationException("此步骤使用柜台工位");return;}
            if(step.Station=="support_only")throw new InvalidOperationException("此工序使用已授权的后房专用能力");
            string sku=step.Station=="calibration"?"ms_machine_calibrator":"ms_machine_electrobench";
            if(!E.PackageB.Working(entity,sku))throw new InvalidOperationException("请选择自有且位于店内有效外层的"+(step.Station=="calibration"?"标定台":"整备台"));
        }
        internal void StartOwn(string id,int selectedMachine,bool customerMaterials=true,bool fallbackToCustomer=false)
        {
            if(SleepTransition)throw new InvalidOperationException("等待当前日结完成");
            var old=C.Jobs[id];if(old.Ready)throw new InvalidOperationException("工单已完成");
            var step=old.Steps[old.Step];int station=step.Station=="counter"?0:selectedMachine;ValidateStation(old,step,station);
            E.MutateNative(()=>host.Engine.EditCampaignItems(s=>{
                var j=s.CampaignItems.Jobs[id];MoveHostToWork(s,j);var current=j.Steps[j.Step];
                if(current.Bound.Count==0){
                    if(customerMaterials)PackageC.BindSupport(j);
                    else {
                        var selected=new List<CampaignResource>();
                        foreach(var req in current.Materials.Select(x=>(x.Key,x.Value,Tool:false)).Concat(current.Tools.Select(x=>(x.Key,x.Value,Tool:true)))) {
                            var reusable=j.Resources.Where(x=>x.Owner=="player" && x.State=="available" && x.Reference==req.Key && x.Tool==req.Tool && !selected.Contains(x)).Take(req.Value).ToList();selected.AddRange(reusable);
                            int need=req.Value-reusable.Count;
                            var candidates=E.AllPlayerItems().Where(x=>x.identifier==PackageC.NativeReference(req.Key) && x.unitCount==1 && E.PackageB.PlayerOwned(x) && x.FindAllChildItems().Count==0 && !selected.Any(r=>r.Entity==x.uniqueId)).OrderBy(x=>x.uniqueId).Take(need).ToArray();
                            if(candidates.Length!=need){
                                if(!fallbackToCustomer)throw new InvalidOperationException("缺少自有材料/工具："+E.Workshop.Label(req.Key));
                                var supplied=j.Resources.Where(x=>x.Owner==j.SupplyActor && x.State=="available" && x.Reference==req.Key && x.Tool==req.Tool && !selected.Contains(x)).Take(need-candidates.Length).ToArray();
                                if(supplied.Length!=need-candidates.Length)throw new InvalidOperationException("自有及委托供料仍不足："+E.Workshop.Label(req.Key));selected.AddRange(supplied);
                            }
                            foreach(var item in candidates){if(item.parentInventory==null || !item.parentInventory.Expel(item))throw new IOException("无法投入自有材料/工具");var r=new CampaignResource{Entity=item.uniqueId,Sku=item.identifier,Reference=req.Key,Tool=req.Tool,Owner="player",Snapshot=NativeMemberEconomy.Encode(item)};j.Resources.Add(r);selected.Add(r);}
                        }
                        foreach(var r in selected){r.State="leased";current.Bound.Add(r.Entity);}
                    }
                }
                PackageC.SelectMode(s,id,"own",host.Day);j.Machine=station;
            }),()=>{});
        }
        internal bool Tick(string id,int milliseconds,int source=0)
        {
            if(!Enabled || SleepTransition || !E.Workshop.CanClock())return false;
            var state=host.Engine.Snapshot();var job=state.CampaignItems.Jobs[id];if(job.Status!="running" || (job.Mode!="own" && job.Mode!="support"))return false;
            bool own=job.Mode=="own";var step=job.Steps[job.Step];
            if(own){try{ValidateStation(job,step,job.Machine);}catch{Pause(id);throw;}}
            var power=source>0?E.FindPlayerItem(source):null;
            if(power!=null && (!E.PackageB.PlayerOwned(power) || power.unitCount!=1 || !power.IsTag("power_source_item")))throw new InvalidOperationException("必须使用玩家自有的单件原版电源");
            int available=power==null?0:PowerHelper.GetAvailableEnergyFromItem(power,false);
            WorkshopPowerPlan plan=null;
            if(own){plan=Workshop.Plan(state.Workshop,new WorkshopJob{ElapsedMs=step.ElapsedMs,DurationMs=step.DurationMs,EnergyE=step.EnergyE,NativeCapacity=100},milliseconds,power?.uniqueId??0,available);if(!plan.Powered){message="自有电源不足，未消耗本次材料；可补电或改后房办理。";return false;}}
            E.MutateNative(()=>{
                if(own && plan.PhysicalDebit>0 && (power==null || !PowerHelper.DrawPowerSource(power,plan.PhysicalDebit)))throw new IOException("原版电源扣电失败");
                if(own && power!=null && PowerHelper.GetAvailableEnergyFromItem(power,false)!=available-plan.PhysicalDebit)throw new IOException("实际电量与冻结工序不符");
                host.Engine.EditCampaignItems(s=>{if(own && step.EnergyE>0)s.Workshop.NativeEnergyCredit[power.uniqueId]=plan.NextCredit;PackageC.Progress(s,id,milliseconds,host.Day,own,power?.uniqueId??0);});
            },()=>{if(own && power!=null)PowerHelper.SetPowerSourceAt(power,available);});return true;
        }
        private int SourceFor(CampaignWork job,int ms)
        {
            var step=job.Steps[job.Step];if(step.EnergyE==0)return 0;var state=host.Engine.Snapshot();
            foreach(var item in E.AllPlayerItems().Where(x=>x.unitCount==1 && x.IsTag("power_source_item") && E.PackageB.PlayerOwned(x)).OrderBy(x=>x.uniqueId))if(Workshop.Plan(state.Workshop,new WorkshopJob{ElapsedMs=step.ElapsedMs,DurationMs=step.DurationMs,EnergyE=step.EnergyE,NativeCapacity=100},ms,item.uniqueId,PowerHelper.GetAvailableEnergyFromItem(item,false)).Powered)return item.uniqueId;
            return 0;
        }
        private static bool BeforeSleep()
        {
            var self=Core.Instance.Story?.Campaign;if(self?.Enabled!=true)return true;
            if(self.SleepTransition)return false;
            if(PlayerStore.instance.storeState!=PlayerStore.StoreState.CLOSED || MapUIManager.Instance.inTransit || MapUIManager.Instance.isOutside)return true;
            if(!self.C.Jobs.Values.Any(j=>!j.Ready && j.Mode=="overnight"))return true;
            try{self.E.MutateNative(()=>self.host.Engine.EditCampaignItems(s=>PackageC.PrepareSleep(s,self.host.Day)),()=>{});self.sleepJournal=self.E.PrepareJournal();return true;}
            catch(Exception error){self.message=error.Message;self.core.LoggerInstance.Warning("CAMPAIGN_SLEEP "+error.Message);return false;}
        }
        internal void BeforeSave()
        {
            if(!Enabled || settlingDay)return;
            settlingDay=true;
            try{
                host.Return?.RestoreCounter();
                if(PackageC.OvernightDue(host.Engine.Snapshot(),host.Day))host.Engine.EditCampaignItems(s=>PackageC.SettleOvernight(s,host.Day));
                SyncReleasedOriginals();
                var updates=C.Originals.Values.Where(x=>x.Location=="native").Select(x=>(Original:x,Item:E.FindPlayerItem(x.Entity))).ToArray();
                foreach(var pair in updates)if(pair.Item==null || pair.Item.identifier!=pair.Original.Sku)throw new InvalidDataException("已登记剧情原件离开了可定位库存，保留旧档："+pair.Original.Role+" #"+pair.Original.Entity+" actual="+(pair.Item?.identifier??"missing")+" repairGraph="+JsonSerializer.Serialize(E.PackageB.RepairGraphDiagnostic(),Core.Json));
                var changes=updates.Select(x=>(Role:x.Original.Role,Snapshot:NativeMemberEconomy.Encode(x.Item))).Where(x=>C.Originals[x.Role].Snapshot!=x.Snapshot).ToArray();
                host.Return?.CaptureCounter();
                if(changes.Length>0)host.Engine.EditCampaignItems(s=>{foreach(var x in changes)s.CampaignItems.Originals[x.Role].Snapshot=x.Snapshot;});
            }finally{settlingDay=false;}
        }
        internal void Update()
        {
            if(!Enabled)return;
            if(core.Test?.Request.CampaignComplete==true && core.Test.Stage!="new" && !core.Test.CampaignDriverReady)return;
            if(sleepJournal!=null){
                if(C.Sleep?.Phase=="settled" && host.Day>C.Sleep.BeforeDay && PlayerStore.instance.storeState==PlayerStore.StoreState.MORNING){host.Persist();sleepJournal.Commit();sleepJournal=null;}
                return;
            }
            var snapshot=host.ReadState;
            if(describedRevision!=snapshot.Revision){foreach(var original in snapshot.CampaignItems.Originals.Values.Where(x=>x.Location=="native")){var item=E.FindPlayerItem(original.Entity);if(item!=null)RefreshDescription(item,original.Role,snapshot);}describedRevision=snapshot.Revision;}
            if(!E.Workshop.CanClock())return;
            foreach(var job in C.Jobs.Values.Where(j=>j.Status=="running" && (j.Mode=="support" || j.Mode=="own"))) {
                if(host.Native012.Enabled&&C.Native012!=null&&PackageC.IsNative012Job(job.Id))continue;
                float time=elapsed.GetValueOrDefault(job.Id)+Math.Min(Time.deltaTime,1f)*1000;if(time<1000){elapsed[job.Id]=time;continue;}
                int ms=Math.Min(60000,(int)time);elapsed[job.Id]=time-ms;
                try{Tick(job.Id,ms,job.Mode=="own"?SourceFor(job,ms):0);}catch(Exception error){message=error.Message;core.LoggerInstance.Warning("CAMPAIGN_WORK "+error.Message);}
            }
        }
        private void RefreshDescription(GameItem item,string role,StoryState state=null)
        {
            // These are per-instance GameItem fields, never ItemDirectory templates.
            state??=host.ReadState;
            item.shortDescription=PackageC.Description(state,role,false,false);
            item.longDescription=PackageC.Description(state,role,true,false);
        }
        internal bool ClaimOriginal(string role)
        {
            var original=C.Originals[role];if(original.Custodian!="player" || original.Location!="custody")throw new InvalidOperationException("原件不在玩家的当前保管权限中");
            if(C.Jobs.Values.Any(j=>j.HostRole==role && j.Status=="running"))throw new InvalidOperationException("先暂停正在进行的工序，再取出原件");
            if(C.Jobs.Values.Any(j=>j.HostRole==role && !j.Ready) && new[]{"c_prelim_master","c_final_master"}.Contains(role))throw new InvalidOperationException("介质仍在制作中，不能取出作为成品");
            if(E.FindPlayerItem(original.Entity)!=null)throw new InvalidDataException("库存已有相同原件ID");
            var item=NativeMemberEconomy.Decode(original.Snapshot);
            if(item.uniqueId!=original.Entity || item.identifier!=original.Sku)throw new InvalidDataException("剧情原件快照与登记身份不符");
            RefreshDescription(item,role);GeneralHelper.SetItemOwned(item,true);bool placed=false;
            bool release=original.Owner=="player" && original.Borrowed && C.Ending!=null && PackageC.Definitions.First(x=>x.Role==role).Ordinary && !C.Jobs.Values.Any(j=>j.HostRole==role && !j.Ready);
            if(release)item.DisableTag(MusicCatalog.ProtectedTag,false);else item.EnableTag(MusicCatalog.ProtectedTag,false);
            E.MutateNative(()=>{foreach(var inv in host.Return?.Enabled==true?host.Return.StorageInventories(role):E.PlayerInventories())if(NativePackageB.Place(inv,item)){placed=true;break;}if(placed)host.Engine.EditCampaignItems(s=>{var r=s.CampaignItems.Originals[role];r.Location="native";r.Released=release;r.Snapshot=NativeMemberEconomy.Encode(item);if(release && s.Economy.Business.Instruments.TryGetValue(r.Entity,out var gear))gear.Custody="player";});},()=>{});
            return placed;
        }
        internal void StoreOriginal(string role)
        {
            E.MutateNative(()=>host.Engine.EditCampaignItems(s=>{var r=s.CampaignItems.Originals[role];if(r.Custodian!="player" || r.Location!="native")throw new InvalidOperationException("不是玩家当前保管的库存原件");ToCustody(r);}),()=>{});
        }
        internal bool ClaimTool(string jobId,int entity)
        {
            var r=C.Jobs[jobId].Resources.Single(x=>x.Entity==entity);if(r.Owner!="player" || r.State!="pending_return")throw new InvalidOperationException("不是玩家可取回的工具");
            if(E.FindPlayerItem(entity)!=null)throw new InvalidDataException("工具ID已经在库存");var item=NativeMemberEconomy.Decode(r.Snapshot);if(item.uniqueId!=entity || item.identifier!=r.Sku)throw new InvalidDataException("工具快照身份不符");bool placed=false;
            E.MutateNative(()=>{foreach(var inv in E.PlayerInventories())if(NativePackageB.Place(inv,item)){placed=true;break;}if(placed)host.Engine.EditCampaignItems(s=>s.CampaignItems.Jobs[jobId].Resources.Single(x=>x.Entity==entity).State="returned");},()=>{});return placed;
        }
        internal void Open(string section="jobs")
        {
            if(!Enabled || PanelOpen || SleepTransition)return;
            tab=section;
            page=0;selectedRole=null;panelScroll=Vector2.zero;
            if(C.PendingChoice==null)message="";
            host.CloseView();E.PackageB.Close();E.Workshop.Close();E.Business.Close();E.CloseLedger();var store=PlayerStore.instance;
            if(store.lockStorePhysicalFlag!=0 || DialogUIManager.Instance.isConversationActiveFlag!=0)return;
            panelStore=store;panelUI=StoreUIManager.Instance;oldScale=Time.timeScale;store.lockStorePhysicalFlag++;NativePanelCursor.Acquire(panelUI);Time.timeScale=0;PanelOpen=true;
        }
        internal void Close()
        {
            if(!PanelOpen)return;PanelOpen=false;Time.timeScale=oldScale;if(panelStore!=null)panelStore.lockStorePhysicalFlag=Math.Max(0,panelStore.lockStorePhysicalFlag-1);if(panelUI!=null)NativePanelCursor.Release(panelUI);panelStore=null;panelUI=null;
        }
        internal void Detach(){Close();if(sleepJournal!=null){sleepJournal.Rollback();sleepJournal=null;}optedIn=false;elapsed.Clear();describedRevision=-1;}
        internal void Draw(GUIStyle text,GUIStyle buttons,GUIStyle window)
        {
            if(!Enabled || host.Visible)return;body=text;button??=new GUIStyle(buttons){fontSize=20,wordWrap=false};
            if(!PanelOpen){if(!E.PackageB.PanelOpen && !E.Workshop.PanelOpen && !E.Business.PanelOpen && NativePanelGUI.Button(new Rect(20,Screen.height-110,200,42),"剧情委托 / 原件经历",button))Open();return;}
            float w=Math.Min(1080,Screen.width-40),h=Math.Min(800,Screen.height-40);
            NativePanelGUI.Window(781350,new Rect((Screen.width-w)/2,(Screen.height-h)/2,w,h),DrawWindow,"乐器委托与原件经历",window);
        }
        private void Act(Action action){try{action();message="已保存。加工只在关闭面板后的有效营业时间推进。";}catch(Exception error){message=error.Message;core.LoggerInstance.Warning("CAMPAIGN_ACTION "+error.Message);}}
        private void DrawWindow(int _) => DrawCampaignPanel();
        private string Ownership(CampaignOriginal item)
        {
            string Who(string id)=>id=="player"?"麻里奈":id=="party:band"?"乐队共有":id=="party:agency"?"公司":host.Package.Actors.FirstOrDefault(x=>x.Id==id)?.DisplayName??"原物主";
            return "所有者："+Who(item.Owner)+"；保管："+Who(item.Custodian)+(item.Location switch{"native"=>"（库存中）","custody"=>"（委托保管）","sealed"=>"（按约封存）","pledged"=>"（质押保管）","submitted"=>"（已送检）","outside"=>"（外部保管）",_=>"（成员携带）"});
        }
    }
}
