using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using TMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;


namespace ResourceStoryMod
{
    public sealed class Core : MelonMod
    {
        internal const string ModVersion="0.37.5";
        internal static Core Instance;
        internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
        internal string Root, DataRoot;
        internal ContentDefinition Content;
        internal Sprite Portrait, ItemSprite;
        internal StoreClient Customer;
        internal Dialogue OfferDialogue, AcceptDialogue, RejectDialogue;
        internal GameItem AcceptChoice, RejectChoice, Sample;
        internal int BoughtCallbacks, SoldCallbacks, DealCallbacks;
        internal string StoryStage = "not_started";
        internal TestSession Test => null;
        internal NativeStoryHost Story;
        internal NativeStoryPresentation Presentation;
        private System.Func<GameItem> factory;
        private readonly List<System.Delegate> callbackRoots = new();
        private bool resourcesReady;

        public override void OnInitializeMelon()
        {
            Instance = this;
            Root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location), "..", ".."));
            DataRoot = Path.Combine(Root, "UserData", "ResourceStoryMod");
            System.IO.Directory.CreateDirectory(DataRoot);
            string path = Path.Combine(DataRoot, "content.json");
            Content = File.Exists(path) ? JsonSerializer.Deserialize<ContentDefinition>(File.ReadAllText(path), Json) : new ContentDefinition();
            if (!File.Exists(path)) File.WriteAllText(path, JsonSerializer.Serialize(Content, Json), Encoding.UTF8);
            if (Content.UnitPrice < 1 || string.IsNullOrWhiteSpace(Content.CustomerId) || string.IsNullOrWhiteSpace(Content.ItemId))
                throw new InvalidDataException("Invalid resource-story content definition");
            Presentation = new NativeStoryPresentation(this);
            int recovered=NativeTransactionJournal.Recover(ReleaseSaveRoot.Path);
            if(recovered>0)LoggerInstance.Msg("MEMBER_TRANSACTION_RECOVERED "+recovered);
            Story = new NativeStoryHost(this);
            Patch(typeof(ModItemDirectory), "InitDirectory", null, nameof(AfterDirectory));
            Patch(typeof(RenderHandler), "LoadFromAtlas", nameof(LoadOwnSprite), null);
            Patch(typeof(GameItemElement), "ResolveSpriteByName", nameof(ResolveOwnItem), null);
            NativeP19Presentation.Install(this);
            Patch(typeof(PlayerStore), "CanSellItemFromTable", nameof(RequireAcceptedOffer), null);
            LoggerInstance.Msg("RESOURCE_STORY_READY v"+ModVersion+"; polished first act; isolated test=" + (Test != null));
        }

        private void Patch(Type type, string method, string prefix, string postfix)
        {
            HarmonyInstance.Patch(ReleaseApi.Method(type, method),
                prefix == null ? null : new HarmonyMethod(typeof(Core), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(Core), postfix));
        }

        private static void AfterDirectory(ModItemDirectory __instance) => Instance.Register(__instance);

        private static bool RequireAcceptedOffer(PlayerStore __instance, ref bool __result)
        {
            if (__instance.currentClientInstance?.storeClient?.identifier != Instance.Content.CustomerId || Instance.StoryStage == "accepted") return true;
            __result = false;
            return false;
        }

        private static bool ResolveOwnItem(GameItemElement __instance,string __0,ref Sprite __result)=>__0!="item" || NativeP2ItemArt.Resolve(__instance,ref __result)&&NativePlaceholderArt.Resolve(__instance,ref __result);

        private static bool LoadOwnSprite(string __0, string __1, ref Sprite __result)
        {
            if (__0 != "resource_story") return true;
            Instance.EnsureResources();
            __result = __1 == "customer" ? Instance.Portrait : Instance.ItemSprite;
            return false;
        }

        internal void EnsureResources()
        {
            if (resourcesReady) return;
            string assets = Path.Combine(Root, "ModDevelopment", "ResourceStoryMod", "Assets");
            string textures = Path.Combine(DataRoot, "Textures");
            System.IO.Directory.CreateDirectory(textures);
            foreach (string name in new[] { "customer.png", "item.png" })
                if (!File.Exists(Path.Combine(textures, name))) File.Copy(Path.Combine(assets, name), Path.Combine(textures, name));
            Portrait = LoadSprite(Path.Combine(textures, "customer.png"));
            ItemSprite = LoadSprite(Path.Combine(textures, "item.png"));
            resourcesReady = true;
        }

        private static Sprite LoadSprite(string path)
        {
            // Native Mono BuildShapeFromSprite samples a lower mip level.
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!ImageConversion.LoadImage(texture, new NativeArray<byte>(File.ReadAllBytes(path)), false))
                throw new InvalidDataException("PNG decoding failed: " + path);
            texture.filterMode = FilterMode.Point;
            Object.DontDestroyOnLoad(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100);
            Object.DontDestroyOnLoad(sprite);
            return sprite;
        }

        internal void Register(ModItemDirectory directory)
        {
            Story?.Economy?.Business.Register(directory);
            Story?.Economy?.Workshop.Register(directory);
            Story?.Campaign?.Register(directory);
            Story?.Diary?.Register(directory);
            Story?.Economy?.PackageB.RegisterPrinting(directory);
            Story?.Economy?.PackageB.RegisterRepair(directory);
            if (directory.Has(Content.ItemId)) return;
            factory ??= (System.Func<GameItem>)(Func<GameItem>)CreateSample;
            if (!directory.Add(Content.ItemId, factory)) throw new InvalidOperationException("Item factory registration failed");
            LoggerInstance.Msg("ITEM_REGISTERED " + Content.ItemId);
        }

        internal GameItem CreateSample()
        {
            EnsureResources();
            var item = ItemDirectory.CreateEmptyItem(Content.ItemId);
            item.name = Content.ItemName;
            item.shortDescription = "供资源交易原型使用的一份封装净水样品。";
            item.longDescription = "占位商品；后续将接入资源品质、供需和剧情阶段。";
            item.unitCount = 1;
            item.unitBaseValue = Content.UnitPrice;
            item.unitValue = Content.UnitPrice;
            item.SetSpriteAndShape("resource_story", "item");
            return item;
        }

        internal void SpawnCustomer()
        {
            if (Customer != null) return;
            var store = PlayerStore.instance;
            var entry = EmporiumEntry.Instance;
            EnsureResources();
            EnsureChineseFallback();
            SpriteDict.Instance.spriteDictionary[Content.CustomerId] = Portrait;
            var directories = Resources.FindObjectsOfTypeAll(NativeTypes.Of<ModItemDirectory>());
            if (directories.Length == 0) throw new InvalidOperationException("ModItemDirectory not ready");
            Register(directories[0].Cast<ModItemDirectory>());
            Sample = DirectoryMaster.Item(Content.ItemId, true);
            if (Sample == null) throw new InvalidOperationException("Registered item factory returned null");
            entry.TryAddToPlayerInv(Sample);
            Customer = new StoreClient();
            Customer.identifier = Content.CustomerId;
            Customer.displayName = Content.CustomerName;
            Customer.spriteName = Content.CustomerId;
            Customer.clientCash = 1000;
            Customer.clientBudget = 1000;
            Customer.useClientBudget = true;
            Customer.clientIntent = StoreClient.ClientIntent.BUY;
            Customer.clientFaction = StoreClient.FACTION_LOWER;
            Customer.clientBuyingIdList.Clear();
            Customer.clientBuyingIdList.Add(Content.ItemId);
            Customer.clientBuyingTagList.Clear();
            Customer.dismissable = true;
            Customer.nonShootable = true;
            Customer.acceptRetailMarkup = false;
            Customer.clientImpatience = 999;
            var intro = new Dialogue().SetText(Content.CustomerName, Content.Intro);
            OfferDialogue = new Dialogue().SetText(Content.CustomerName, Content.Offer);
            AcceptDialogue = new Dialogue().SetText(Content.CustomerName, Content.Accepted);
            RejectDialogue = new Dialogue().SetText(Content.CustomerName, Content.Declined).SetNextDialogue(OfferDialogue);
            AcceptChoice = ItemDirectory.CreateEmptyItem("rs_choice_accept");
            RejectChoice = ItemDirectory.CreateEmptyItem("rs_choice_reject");
            var accept = (System.Action)(Action)(() => SetStage("accepted"));
            var reject = (System.Action)(Action)(() => SetStage("declined"));
            callbackRoots.Add(accept); callbackRoots.Add(reject);
            OfferDialogue.AddChoice(AcceptChoice, "接受试单", "交付一份净水样品。", AcceptDialogue, accept, null);
            OfferDialogue.AddChoice(RejectChoice, "暂时拒绝", "稍后仍可重新考虑。", RejectDialogue, reject, null);
            intro.SetNextDialogue(OfferDialogue);
            Customer.mainDialogue = intro;
            Customer.allDoneDialogue = new Dialogue().SetText(Content.CustomerName, Content.Completed);
            var bought = (System.Action<GameItem, int>)(Action<GameItem, int>)((item, amount) =>
            { if (item.identifier == Content.ItemId) { BoughtCallbacks++; SetStage("delivered"); } });
            var sold = (System.Action<GameItem, int>)(Action<GameItem, int>)((item, amount) =>
            { if (item.identifier == Content.ItemId) { SoldCallbacks++; SetStage("delivered"); } });
            var deal = (System.Action)(Action)(() => { DealCallbacks++; });
            callbackRoots.Add(bought); callbackRoots.Add(sold); callbackRoots.Add(deal);
            Customer.onItemBought = bought; Customer.onItemSold = sold; Customer.onDealAccepted = deal;
            Customer.CompleteClientCreation(false);
            store.storeClientManager.AddNextClient(Customer);
            SetStage("offered");
            LoggerInstance.Msg("CUSTOMER_QUEUED " + Content.CustomerId);
        }

        internal void EnsureChineseFallback()
        {
            var chinese = FontLoader.Instance.simplifiedChineseFont;
            if (chinese == null) throw new InvalidOperationException("Game Chinese font is unavailable");
            var fonts = Resources.FindObjectsOfTypeAll(NativeTypes.Of<TMP_FontAsset>());
            foreach (var obj in fonts)
            {
                var font = obj.Cast<TMP_FontAsset>();
                if (font == chinese) continue;
                font.fallbackFontAssetTable ??= new System.Collections.Generic.List<TMP_FontAsset>();
                if (!font.fallbackFontAssetTable.Contains(chinese)) font.fallbackFontAssetTable.Add(chinese);
            }
            LoggerInstance.Msg("CHINESE_FALLBACK " + chinese.name);
        }

        internal void SetStage(string stage)
        {
            StoryStage = stage;
            var store = PlayerStore.instance;
            if (store != null && store.modData != null)
            {
                store.modData["resource_story.version"] = "1";
                store.modData["resource_story.stage"] = stage;
            }
            LoggerInstance.Msg("STORY_STAGE " + stage);
        }

        internal int StockCount()
        {
            var entry = EmporiumEntry.Instance;
            int count = 0;
            var seen = new HashSet<int>();
            foreach (var inv in new[] { entry.invElement, entry.backInvinvElement, entry.backInvinvElementCounter, entry.showcaseElement })
            {
                if (inv == null) continue;
                foreach (var item in inv.childItems)
                    if (item.identifier == Content.ItemId && seen.Add(item.uniqueId)) count += item.unitCount;
            }
            return count;
        }

        public override void OnUpdate()
        {
            try { Presentation?.Update(); }
            catch (Exception e) { LoggerInstance.Error("Native presentation stopped: "+e); Presentation?.Stop(); }
            try { Story?.Update(); }
            catch (Exception e) { LoggerInstance.Error("Story host stopped: " + e); Test?.Fail(e); Story?.Detach(); Story = null; }
            try { Test?.Update(); }
            catch (Exception e) { Test?.Fail(e); }
        }

        public override void OnLateUpdate(){NativePanelCursor.Refresh();NativeP19Presentation.LateUpdate();Presentation?.LateUpdate();Test?.Tools?.LateOpeningCapture();}

        public override void OnGUI()
        {
            if(Test?.Headless==true)return;
            if(Presentation?.Active==true)return;
            using var frame=NativePanelGUI.Begin();
            try { Story?.Draw(); }
            catch (Exception e) { LoggerInstance.Error("Story view stopped: " + e); Story?.CloseView();Story?.Economy?.Workshop.Close();Story?.Economy?.Business.Close();Story?.Economy?.CloseLedger(); }
            try {Test?.Draw();}catch(Exception e){Test?.Fail(e);}
            NativePanelCursor.Refresh();
        }

        public override void OnApplicationQuit(){Presentation?.PrepareQuit();Story?.ActOne?.Detach();}
        public override void OnDeinitializeMelon() {Test?.RestoreEnvironment();Story?.Detach();}
    }
}
