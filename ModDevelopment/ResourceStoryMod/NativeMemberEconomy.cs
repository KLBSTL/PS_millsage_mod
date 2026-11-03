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
    internal sealed class NativeMemberEconomy
    {
        private readonly Core core;
        private readonly NativeStoryHost host;
        private MemberEconomyConfig config;
        private PlayerStore store;
        private bool transferInProgress, ledgerOpen;
        private float previousTimeScale;
        private StoreUIManager lockedUI;
        private GUIStyle body, button;
        internal bool Enabled => config != null && host.ReadState?.Economy != null;
        internal bool IsTransferring => transferInProgress;
        internal bool LedgerOpen => ledgerOpen;
        internal Action TestBeforeCommit;
        internal Action<string> TestTransactionStage;
        internal void TestStage(string stage){if(core.Test?.ToolsMode==true)TestTransactionStage?.Invoke(stage);}
        internal NativeTransactionJournal PrepareJournal()
        {
            host.Persist();
            return new NativeTransactionJournal(new ES3Settings("save_"+store.saveSlotId+".es3",Array.Empty<System.Enum>()).FullPath);
        }
        private void VerifyCommit()
        {
            string path=new ES3Settings("save_"+store.saveSlotId+".es3",Array.Empty<System.Enum>()).FullPath;
            var disk=NativeSaveDocument.Parse(File.ReadAllText(path));
            if((int)disk["playerStore"]["value"]["playerCash"]!=store.GetCash() || disk["playerStore"]["value"]["modData"][NativeStoryHost.SaveKey]?.ToString()!=host.Engine.Save())throw new IOException("Native save did not persist the full settlement");
        }
        internal MemberEconomyConfig Config => config;
        internal NativeMemberBusiness Business { get; private set; }
        internal NativeWorkshop Workshop { get; private set; }
        internal NativePackageB PackageB {get;private set;}
        internal NativePackageD Trade {get;private set;}
        internal NativeMemberEconomy(Core core, NativeStoryHost host)
        {
            this.core = core; this.host = host;
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(PlayerStore), "SaveGame"),
                prefix: new HarmonyMethod(typeof(NativeMemberEconomy), nameof(BeforeNativeSave)));
            Business = new NativeMemberBusiness(core,host,this);
            Workshop = new NativeWorkshop(core,host,this);
            PackageB=new NativePackageB(core,host,this);
            Trade=new NativePackageD(core,host,this);
        }
        private static bool BeforeNativeSave(PlayerStore __instance)
        {
            var economy = Core.Instance.Story?.Economy;
            if (economy?.store?.NativeIdentity() != __instance.NativeIdentity()) return true;
            if (economy.transferInProgress) return false;
            NativeOriginalNightGuard.Recover();
            economy.SynchronizeDay();
            economy.host.Campaign?.BeforeSave();
            economy.host.Inspection?.BeforeSave();
            economy.Trade.CaptureNativeUse();
            economy.PackageB.TrackInventoryFacts();
            economy.PackageB.CaptureSupplier();
            economy.Trade.Capture();
            economy.host.Flush();
            return true;
        }
        internal bool Attach(PlayerStore attached)
        {
            store = attached;
            if (host.Package.Id != "millsage" || host.IsTestPackage) return false;
            string path = Path.Combine(core.DataRoot, "millsage.economy.json");
            string defaults = Path.Combine(core.Root, "ModDevelopment", "ResourceStoryMod", "Content", "millsage.economy.json");
            if (core.Test != null) path = defaults;
            else if (!File.Exists(path)) File.Copy(defaults, path);
            var selected = JsonSerializer.Deserialize<MemberEconomyConfig>(File.ReadAllText(path), StoryContent.Json);
            if (selected == null) throw new InvalidDataException("Empty member economy configuration");
            // Publish config only after validation/old-save migration succeeds.
            bool changed = host.Engine.EnableMemberEconomy(selected, host.Day);
            config = selected;
            changed |= host.Engine.EnableMemberBusiness();
            changed |= Business.Attach();
            changed |= Workshop.Attach();
            PackageB.Attach();
            changed|=Trade.Attach();
            core.LoggerInstance.Msg("MEMBER_ECONOMY_ATTACHED " + attached.runID + "; day=" + host.Day);
            return changed;
        }
        internal bool SynchronizeDay() => config != null && host.Engine != null && host.Engine.SettleMemberIncome(host.Day);
        internal void Update()
        {
            if (SynchronizeDay()) host.Persist();
            Business.Update();
            PackageB.Update();
            Workshop.Update();
            Trade.Update();
        }
        internal void Detach()
        {
            Trade.Detach();PackageB.Close();
            CloseLedger(); Workshop.Detach(); Business.Detach(); store = null; config = null; transferInProgress = false;
        }
        internal IEnumerable<GameInventory> PlayerInventories()
        {
            var entry = EmporiumEntry.Instance;
            return new[] { entry.invElement, entry.backInvinvElement, entry.backInvinvElementCounter, entry.showcaseElement }
                .Where(i => i != null).GroupBy(i => i.NativeIdentity()).Select(g => g.First());
        }
        internal GameItem FindPlayerItem(int entity)
        {
            foreach (var inv in OwnedInventories())
                foreach (var item in inv.childItems) {
                    if (item.uniqueId == entity) return item;
                    foreach(var nested in item.FindAllChildItems()) if(nested.uniqueId==entity) return nested;
                }
            return null;
        }
        internal IEnumerable<GameItem> AllPlayerItems()
        {
            var seen=new HashSet<int>();
            foreach(var inv in OwnedInventories()) foreach(var item in inv.childItems) {
                if(seen.Add(item.uniqueId)) yield return item;
                foreach(var nested in item.FindAllChildItems()) if(seen.Add(nested.uniqueId)) yield return nested;
            }
        }
        // Vanilla purchases made outside the store first enter this temporary
        // backpack. It is owned storage, but not a valid workshop station root.
        internal IEnumerable<GameInventory> OwnedInventories()=>PlayerInventories().Concat(new GameInventory[]{EmporiumEntry.Instance.afterhourInventory}).Where(x=>x!=null).GroupBy(x=>x.NativeIdentity()).Select(x=>x.First());
        internal static string Encode(GameItem item)
        {
            var nodes = SaveManager.EncodeNodes(item.Cast<GraphNodeStorage>());
            var encoded = new List<string>();
            foreach (var node in nodes) encoded.Add(JsonUtility.ToJson(node));
            return JsonSerializer.Serialize(encoded, StoryContent.Json);
        }
        internal static GameItem Decode(string snapshot)
        {
            var nodes = new System.Collections.Generic.List<SaveItemNode>();
            foreach (var json in JsonSerializer.Deserialize<List<string>>(snapshot, StoryContent.Json)) nodes.Add(JsonUtility.FromJson<SaveItemNode>(json));
            // Rebuilding saved fixed slots is distinct from a player's insertion.
            bool previous = RestoringSnapshot;
            RestoringSnapshot = true;
            try { return SaveManager.DecodeNodes(nodes); }
            finally { RestoringSnapshot = previous; }
        }
        internal static bool RestoringSnapshot { get; private set; }

        // First real-item proof is intentionally isolated and allowlisted. Story visits keep their trade lock.
        internal bool TradeOrdinaryWire(string transactionId, string actor, int entityId, bool memberBuys, int agreedPrice)
            => TradeNative(transactionId,actor,entityId,memberBuys,agreedPrice,true);
        internal bool TradeNative(string transactionId, string actor, int entityId, bool memberBuys, int agreedPrice, bool wireProof=false)
        {
            if (!Enabled || transferInProgress || (wireProof && core.Test?.Wallet!=true)) throw new InvalidOperationException("交易当前不可用");
            SynchronizeDay();
            var state = host.Engine.Snapshot();
            if (state.Economy.Trades.TryGetValue(transactionId, out var receipt))
            {
                if (receipt.ActorId != actor || receipt.EntityId != entityId || receipt.MemberBuys != memberBuys || receipt.Price != agreedPrice)
                    throw new InvalidDataException("Transaction id reused with different trade terms");
                return false;
            }
            GameItem item;
            MemberOwnedItem record;
            if (memberBuys)
            {
                item = FindPlayerItem(entityId) ?? throw new InvalidOperationException("Player does not own this item");
                if (item.unitCount != 1 || (wireProof ? item.identifier!="wire" : !Business.CanTrade(actor,item,true,agreedPrice))) throw new InvalidOperationException("当前物品或报价不符合成员购买条件");
                record = new MemberOwnedItem { ActorId = actor, EntityId = entityId, ItemId = item.identifier, NativeSnapshot = Encode(item) };
            }
            else
            {
                if (FindPlayerItem(entityId) != null || !state.Economy.OwnedItems.TryGetValue(entityId, out record) || record.ActorId != actor || (wireProof ? record.ItemId!="wire" : MusicCatalog.Find(record.ItemId)==null && record.ItemId!="ms_item_balanced_cable"))
                    throw new InvalidOperationException("Member does not own this ordinary wire");
                item = Decode(record.NativeSnapshot);
                if (item == null || item.uniqueId != entityId || item.identifier != record.ItemId || item.unitCount != 1)
                    throw new InvalidDataException("Native item snapshot did not preserve the original entity");
                if(!wireProof && !Business.CanTrade(actor,item,false,agreedPrice)) throw new InvalidOperationException("这件器材当前不能出售");
            }
            // Validate a copy before changing native money/inventory.
            if (!MemberEconomy.ApplyTrade(state.Economy, transactionId, record, memberBuys, agreedPrice)) return false;
            int cashBefore = store.GetCash();
            int cashAfter = checked(cashBefore + (memberBuys ? agreedPrice : -agreedPrice));
            if (cashAfter < 0) throw new InvalidOperationException("玩家现金不足");
            var source = item.parentInventory;
            string storyBefore = host.Engine.Save();
            var journal=PrepareJournal();
            storyBefore=host.Engine.Save();
            transferInProgress = true;
            try
            {
                if (memberBuys)
                {
                    if (source == null || !source.Expel(item) || FindPlayerItem(entityId) != null)
                        throw new InvalidOperationException("Native inventory refused the item transfer");
                }
                else
                {
                    EmporiumEntry.Instance.TryAddToPlayerInv(item);
                    if (FindPlayerItem(entityId) == null) throw new InvalidOperationException("玩家库存没有可用位置");
                }
                store.ModCash(memberBuys ? agreedPrice : -agreedPrice, false);
                if (store.GetCash() != cashAfter) throw new InvalidOperationException("Native cash settlement differed from the confirmed quote");
                if (!host.Engine.CommitMemberTrade(transactionId, record, memberBuys, agreedPrice)) throw new InvalidOperationException("Trade was unexpectedly replayed");
                if(!wireProof) Business.OnTrade(actor,item,memberBuys);
                transferInProgress=false;
                host.Persist();
                VerifyCommit();
                if(core.Test?.PackageA==true || core.Test?.ToolsMode==true) TestBeforeCommit?.Invoke();
                journal.Commit();
            }
            catch
            {
                store.playerCash = cashBefore;
                store.ModCash(0, false);
                if (memberBuys && FindPlayerItem(entityId) == null && source != null)
                {
                    if (!source.UncheckedAccept(item)) throw new InvalidOperationException("Native trade rollback could not restore the item");
                }
                else if (!memberBuys && FindPlayerItem(entityId) != null) item.parentInventory.Expel(item);
                host.RestoreEconomyCheckpoint(storyBefore);
                journal.Rollback();
                throw;
            }
            finally { transferInProgress = false; }
            // Native cash, inventory, receipt and all five wallets share this native save snapshot.
            return true;
        }
        // All native changes are undone together with the engine snapshot if a mutation or save throws.
        internal void MutateNative(Action apply,Action undo)
        {
            if(!Enabled || transferInProgress) throw new InvalidOperationException("另一个交接正在进行");
            SynchronizeDay();
            string before=host.Engine.Save(); int cashBefore=store.GetCash();
            var transactionInventories=OwnedInventories().Concat(new GameInventory[]{store.currentClientInstance?.gridInv,EmporiumEntry.Instance.frontInvinvElement}).Where(x=>x!=null).GroupBy(x=>x.NativeIdentity()).Select(x=>x.First()).ToArray();
            var rootItems=transactionInventories.SelectMany(inv=>NativePackageB.ItemList(inv.childItems).Select(item=>(Inventory:inv,Item:item))).ToArray();
            var nested=rootItems.SelectMany(x=>NativePackageB.ItemList(x.Item.FindAllChildItems())).Select(x=>x.uniqueId).ToHashSet();
            var originals=rootItems.Where(x=>!nested.Contains(x.Item.uniqueId)).Select(x=>(x.Inventory,Snapshot:Encode(x.Item))).ToArray();
            var journal=PrepareJournal();before=host.Engine.Save();transferInProgress=true;
            NativePackageD.Transaction musicTransaction=null;
            try { musicTransaction=Trade.BeforeMutation();TestStage("journal_prepared");apply();Trade.AfterMutation(musicTransaction);TestStage("mutation_applied");transferInProgress=false;host.Persist();TestStage("native_saved");VerifyCommit();TestStage("commit_verified");if(core.Test?.PackageA==true || core.Test?.Workshop==true || core.Test?.ToolsMode==true || core.Test?.Request.CampaignItems==true)TestBeforeCommit?.Invoke();TestStage("before_marker_commit");journal.Commit(); }
            catch { transferInProgress=true; try{
                undo();
                // Restore native inventories as well as the mod snapshot. This
                // covers failures after plastic consumption, nested moves and
                // output creation, for which a partial caller undo is unsafe.
                var changed=transactionInventories.Where(inv=>{
                    var expected=originals.Where(x=>x.Inventory.NativeIdentity()==inv.NativeIdentity()).Select(x=>x.Snapshot).OrderBy(x=>x,StringComparer.Ordinal);
                    // Some exposed player inventories are beneath another
                    // captured container. Compare only roots, consistently with
                    // the snapshot, so an unchanged child is never cleared.
                    var current=NativePackageB.ItemList(inv.childItems).Where(x=>!nested.Contains(x.uniqueId)).Select(Encode).OrderBy(x=>x,StringComparer.Ordinal);
                    return !current.SequenceEqual(expected);
                }).ToArray();
                // Clear changed roots before reconstructing any parent/child
                // graph; clearing after reconstruction could erase restored items.
                foreach(var inv in changed)foreach(var item in NativePackageB.ItemList(inv.childItems).ToArray())if(!inv.Expel(item))throw new IOException("事务回退无法清理原生库存；完整旧档保留");
                foreach(var original in originals.Where(x=>changed.Any(inv=>inv.NativeIdentity()==x.Inventory.NativeIdentity()))){var item=Decode(original.Snapshot);if(!original.Inventory.UncheckedAccept(item))throw new IOException("事务回退无法还原原件；完整旧档保留");}
            }finally{Trade.RollbackMutation(musicTransaction);store.playerCash=cashBefore;store.ModCash(0,false);host.RestoreEconomyCheckpoint(before);journal.Rollback();}throw; }
            finally {transferInProgress=false;}
        }
        internal void Draw(GUIStyle bodyStyle, GUIStyle buttonStyle, GUIStyle windowStyle)
        {
            if (config == null || host.Visible || transferInProgress) return;
            body = bodyStyle; button = buttonStyle;
            if(host.DeveloperPanels||Trade.PanelOpen)Trade.Draw(bodyStyle,buttonStyle,windowStyle);
            if(Trade.PanelOpen)return;
            if(host.DeveloperPanels||PackageB.PanelOpen)PackageB.Draw(bodyStyle,buttonStyle,windowStyle);
            if(PackageB.PanelOpen)return;
            if(host.DeveloperPanels||Workshop.PanelOpen)Workshop.Draw(bodyStyle,buttonStyle,windowStyle);
            if(Workshop.PanelOpen)return;
            if(host.DeveloperPanels||Business.PanelOpen)Business.Draw(bodyStyle,buttonStyle,windowStyle);
            if (!ledgerOpen)
            {
                if(!host.DeveloperPanels)return;
                if (NativePanelGUI.Button(new Rect(20, Screen.height - 58, 180, 42), "成员账本", button)) OpenLedger();
                return;
            }
            float width = Math.Min(860, Screen.width - 40), height = Math.Min(550, Screen.height - 40);
            NativePanelGUI.Window(781342, new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height), DrawLedger, "成员账本", windowStyle);
        }
        internal void OpenLedger()
        {
            if (store == null || store.lockStorePhysicalFlag != 0 || DialogUIManager.Instance?.isConversationActiveFlag != 0) return;
            lockedUI = StoreUIManager.Instance; previousTimeScale = Time.timeScale;
            store.lockStorePhysicalFlag++; NativePanelCursor.Acquire(lockedUI); Time.timeScale = 0; ledgerOpen = true;
        }
        internal void CloseLedger()
        {
            if (!ledgerOpen) return;
            ledgerOpen = false; Time.timeScale = previousTimeScale;
            if (store != null) store.lockStorePhysicalFlag = Math.Max(0, store.lockStorePhysicalFlag - 1);
            if (lockedUI != null) NativePanelCursor.Release(lockedUI);
            lockedUI = null;
        }
        private void DrawLedger(int id)
        {
            var state = host.Engine.Snapshot();
            GUI.Label(new Rect(25, 48, 790, 38), "游戏日 " + host.Day + " · 个人余额随当前存档保存", body);
            int y = 100;
            foreach (var p in config.Members)
            {
                bool met = host.Package.Scenes.Any(s => state.CompletedDays.ContainsKey(s.Id) && s.Participants.Contains(p.ActorId));
                var actor = host.Package.Actors.Single(a => a.Id == p.ActorId);
                GUI.Label(new Rect(25, y, 790, 44), actor.DisplayName + (met ? "    余额 " + state.Economy.Wallets[p.ActorId].Balance + "    每日净收入 +" + p.DailyNetIncome : "    联系后可查看"), body);
                y += 55;
            }
            GUI.Label(new Rect(25, y + 3, 790, 65), "卖出个人物品会增加余额，购物会扣款。\n实际演出结束后结算个人报酬。", body);
            if (NativePanelGUI.Button(new Rect(25, y + 80, 180, 42), "关闭", button)) CloseLedger();
        }
    }
}
