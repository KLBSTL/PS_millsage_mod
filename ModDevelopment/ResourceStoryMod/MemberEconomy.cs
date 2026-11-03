using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ResourceStoryMod.Story
{
    public sealed class MemberIncomeProfile
    {
        public string ActorId { get; set; }
        public long InitialBalance { get; set; }
        public long DailyNetIncome { get; set; }
    }
    public sealed class MemberEconomyConfig
    {
        public int Version { get; set; } = 1;
        public List<MemberIncomeProfile> Members { get; set; } = new();
        public Dictionary<string, long> LivePayouts { get; set; } = new();
        public void Validate(StoryPackage package)
        {
            string[] ids = { "ms_mahoro", "ms_nagi", "ms_natsume", "ms_houka", "ms_hotaru" };
            if (Version != 1 || Members == null || LivePayouts == null || Members.Count != ids.Length ||
                !Members.Select(m => m?.ActorId).OrderBy(x => x).SequenceEqual(ids.OrderBy(x => x)) ||
                Members.Any(m => m.InitialBalance < 0 || m.DailyNetIncome < 0) || package.Id != "millsage" ||
                ids.Any(id => !package.Actors.Any(a => a.Id == id)))
                throw new InvalidDataException("Invalid millsage member economy configuration");
            var allowed = new[] { "ms_scene_010", "ms_scene_016", "ms_scene_025", "ms_scene_030" };
            if (LivePayouts.Count != 4 || allowed.Any(id => !LivePayouts.ContainsKey(id)) ||
                LivePayouts.Any(p => p.Value < 0 || !package.Scenes.Any(s => s.Id == p.Key && s.Context == "night")))
                throw new InvalidDataException("Invalid millsage Live payout configuration");
        }
    }
    public sealed class MemberWallet
    {
        public long Balance { get; set; }
        public int LastIncomeDay { get; set; }
    }
    public sealed class MemberOwnedItem
    {
        public int EntityId { get; set; }
        public string ItemId { get; set; }
        public string ActorId { get; set; }
        public string NativeSnapshot { get; set; }
    }
    public sealed class MemberTradeReceipt
    {
        public string ActorId { get; set; }
        public int EntityId { get; set; }
        public bool MemberBuys { get; set; }
        public int Price { get; set; }
    }
    public sealed class MemberEconomyState
    {
        public int Version { get; set; } = 1;
        public string RunId { get; set; }
        public int ActivatedDay { get; set; }
        public Dictionary<string, MemberWallet> Wallets { get; set; } = new();
        public HashSet<string> SettledLives { get; set; } = new();
        public Dictionary<int, MemberOwnedItem> OwnedItems { get; set; } = new();
        public Dictionary<string, MemberTradeReceipt> Trades { get; set; } = new();
        public MemberBusinessState Business { get; set; }
    }
    public static class MemberEconomy
    {
        public static MemberEconomyState Create(MemberEconomyConfig config, string run, int day, IEnumerable<string> completed)
        {
            if (day < 0) throw new ArgumentOutOfRangeException(nameof(day));
            var state = new MemberEconomyState { RunId = run, ActivatedDay = day };
            foreach (var p in config.Members) state.Wallets.Add(p.ActorId, new MemberWallet { Balance = p.InitialBalance, LastIncomeDay = day });
            foreach (var id in completed.Where(config.LivePayouts.ContainsKey)) state.SettledLives.Add(id);
            return state;
        }
        public static void Validate(MemberEconomyState state, MemberEconomyConfig config, string run)
        {
            if (state == null || state.Version != 1 || state.RunId != run || state.ActivatedDay < 0 || state.Wallets == null ||
                state.SettledLives == null || state.Trades == null || state.OwnedItems == null ||
                !state.Wallets.Keys.OrderBy(x => x).SequenceEqual(config.Members.Select(m => m.ActorId).OrderBy(x => x)) ||
                state.Wallets.Values.Any(w => w == null || w.Balance < 0 || w.LastIncomeDay < state.ActivatedDay) ||
                state.SettledLives.Any(id => !config.LivePayouts.ContainsKey(id)))
                throw new InvalidDataException("Invalid saved member economy; existing data was retained");
            foreach (var p in state.OwnedItems)
                if (p.Value == null || p.Key != p.Value.EntityId || !state.Wallets.ContainsKey(p.Value.ActorId) ||
                    string.IsNullOrWhiteSpace(p.Value.ItemId) || string.IsNullOrWhiteSpace(p.Value.NativeSnapshot))
                    throw new InvalidDataException("Invalid saved member item");
            foreach (var p in state.Trades)
                if (!StoryContent.ValidId(p.Key) || p.Value == null || !state.Wallets.ContainsKey(p.Value.ActorId) || p.Value.Price <= 0)
                    throw new InvalidDataException("Invalid saved member trade receipt");
        }
        public static bool IncomeDue(MemberEconomyState state, int day) => day >= 0 && state.Wallets.Values.Any(w => day > w.LastIncomeDay);
        public static void SettleIncome(MemberEconomyState state, MemberEconomyConfig config, int day)
        {
            if (day < 0) throw new ArgumentOutOfRangeException(nameof(day));
            // Operates only on StoryEngine's draft snapshot; an overflow rolls back every member.
            foreach (var p in config.Members)
            {
                var w = state.Wallets[p.ActorId];
                if (day <= w.LastIncomeDay) continue;
                w.Balance = checked(w.Balance + checked(((long)day - w.LastIncomeDay) * p.DailyNetIncome));
                w.LastIncomeDay = day;
            }
        }
        public static void SettleLive(MemberEconomyState state, MemberEconomyConfig config, string scene)
        {
            if (!config.LivePayouts.TryGetValue(scene, out long amount) || !state.SettledLives.Add(scene)) return;
            foreach (var w in state.Wallets.Values) w.Balance = checked(w.Balance + amount);
        }
        public static bool ApplyTrade(MemberEconomyState state, string transactionId, MemberOwnedItem item, bool memberBuys, int price)
        {
            if (!StoryContent.ValidId(transactionId) || item == null || !state.Wallets.ContainsKey(item.ActorId) ||
                price <= 0 || string.IsNullOrWhiteSpace(item.ItemId) || string.IsNullOrWhiteSpace(item.NativeSnapshot))
                throw new InvalidDataException("Invalid ordinary member trade");
            if (state.Trades.TryGetValue(transactionId, out var previous))
            {
                if (previous.ActorId != item.ActorId || previous.EntityId != item.EntityId || previous.MemberBuys != memberBuys || previous.Price != price)
                    throw new InvalidDataException("Transaction id reused with different trade terms");
                return false;
            }
            var w = state.Wallets[item.ActorId];
            if (memberBuys)
            {
                if (w.Balance < price) throw new InvalidOperationException("成员余额不足");
                if (state.OwnedItems.ContainsKey(item.EntityId)) throw new InvalidOperationException("物品已经属于成员");
                w.Balance = checked(w.Balance - price);
                state.OwnedItems.Add(item.EntityId, new MemberOwnedItem { ActorId = item.ActorId, EntityId = item.EntityId, ItemId = item.ItemId, NativeSnapshot = item.NativeSnapshot });
            }
            else
            {
                if (!state.OwnedItems.TryGetValue(item.EntityId, out var owned) || owned.ActorId != item.ActorId ||
                    owned.ItemId != item.ItemId || owned.NativeSnapshot != item.NativeSnapshot)
                    throw new InvalidOperationException("成员没有这件可售物品");
                w.Balance = checked(w.Balance + price);
                state.OwnedItems.Remove(item.EntityId);
            }
            state.Trades.Add(transactionId, new MemberTradeReceipt { ActorId = item.ActorId, EntityId = item.EntityId, MemberBuys = memberBuys, Price = price });
            return true;
        }
    }
}
