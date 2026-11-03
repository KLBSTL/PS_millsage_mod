using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    // Protect finite entrusted originals across vanilla nightly inventory cleanup.
    // Legitimate campaign transfers and game-over are never rolled back.
    internal static class NativeOriginalNightGuard
    {
        private sealed class Held {internal int Entity;internal string Sku,Snapshot;internal GameItem Item;internal GameInventory Source;}
        private static readonly List<Held> held=new();private static string run;private static bool active;
        internal static int Recovered {get;private set;}
        internal static void Install(Core core)
        {
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(PlayerStore),"EndNight"),prefix:new HarmonyMethod(typeof(NativeOriginalNightGuard),nameof(Begin)),postfix:new HarmonyMethod(typeof(NativeOriginalNightGuard),nameof(End)));
            core.HarmonyInstance.Patch(ReleaseApi.Method(typeof(PlayerStore),"ExecuteGameOver"),prefix:new HarmonyMethod(typeof(NativeOriginalNightGuard),nameof(GameOver)));
        }
        private static void Begin()
        {
            held.Clear();active=false;var h=Core.Instance.Story;if(h?.NormalPlay!=true||h.Engine==null)return;
            run=PlayerStore.instance.runID;
            foreach(var r in h.ReadState.CampaignItems.Originals.Values.Where(x=>x.Location=="native"&&x.Custodian=="player")){
                var item=h.Economy.FindPlayerItem(r.Entity);if(item==null)continue;
                held.Add(new(){Entity=r.Entity,Sku=r.Sku,Item=item,Source=item.parentInventory,Snapshot=NativeMemberEconomy.Encode(item)});
            }
            if(h.Inspection?.State!=null)foreach(var order in h.Inspection.State.Orders.Where(BasicInspection.Held)){
                var item=h.Economy.FindPlayerItem(order.Entity);if(item!=null)held.Add(new(){Entity=order.Entity,Sku=order.Sku,Item=item,Source=item.parentInventory,Snapshot=NativeMemberEconomy.Encode(item)});
            }
            active=true;
        }
        private static void GameOver(){active=false;held.Clear();}
        private static void End(){Recover();active=false;held.Clear();}
        internal static void Recover()
        {
            if(!active||PlayerStore.instance?.runID!=run)return;var h=Core.Instance.Story;
            foreach(var old in held){
                var r=h.ReadState.CampaignItems.Originals.Values.FirstOrDefault(x=>x.Entity==old.Entity);
                if(!(r!=null&&r.Location=="native"&&r.Custodian=="player"||h.Inspection?.HeldEntity(old.Entity)==true)||h.Economy.FindPlayerItem(old.Entity)!=null)continue;
                GameItem item=null;foreach(var native in PlayerStore.instance.FindAllItem(true))if(native.uniqueId==old.Entity){item=native;break;}
                if(item==null)foreach(var native in PlayerStore.instance.FindAllItem(false))if(native.uniqueId==old.Entity){item=native;break;}
                // A vanilla-destroyed renderer cannot be placed back. Decode the
                // exact captured graph only when the entity is absent everywhere.
                if(item==null||item.identifier!=old.Sku)item=NativeMemberEconomy.Decode(old.Snapshot);
                if(item.parentInventory!=null)item.parentInventory.Expel(item);
                GeneralHelper.SetItemOwned(item,true);
                bool placed=old.Source!=null&&NativePackageB.Place(old.Source,item);
                if(!placed)placed=h.Economy.PlayerInventories().Any(inv=>NativePackageB.Place(inv,item));
                if(!placed)throw new InvalidOperationException("请腾出空间保管委托原件；保留过夜前的存档。");
                Recovered++;Core.Instance.LoggerInstance.Msg("NATIVE_ORIGINAL_NIGHT_RECOVER "+old.Sku+" #"+old.Entity);
            }
        }
    }
}
