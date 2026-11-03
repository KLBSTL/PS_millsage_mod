using System;
using System.Collections.Generic;
using System.Linq;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    internal sealed partial class NativePackageD
    {
        private const long NetworkNumber=8670203;
        private const string NetworkName="王尔德音乐业务";
        private static readonly string[] NetworkTiers={"community","practice","stage"};
        private readonly List<Action> networkChoices=new();

        private void BindNetworkPhone(PlayerStore store)
        {
            if(!NativeUseEnabled||store.PhoneClientDict==null||store.PhoneClientDict.ContainsKey(NetworkNumber))return;
            if(PackageDNetwork.Qualification(host.ReadState,"community",MiddleOpen())!=null&&D.Openings.Count==0)return;
            store.PhoneClientDict[NetworkNumber]=new StorePhoneClient{displayName=NetworkName,locID="",dialogFuncId="",phoneState=StorePhoneClient.PhoneState.Regular,dialedBefore=true};
        }
        private bool IsNetworkPhone(StorePhoneClient phone)=>NativeUseEnabled&&phone!=null&&PlayerStore.instance?.PhoneClientDict?.TryGetValue(NetworkNumber,out var active)==true&&active.NativeIdentity()==phone.NativeIdentity();
        private static bool NetworkPhoneName(StorePhoneClient __instance,ref string __result)
        {if(Self?.IsNetworkPhone(__instance)!=true)return true;__result=NetworkName;return false;}
        private static bool NetworkPhoneShown(StorePhoneClient __instance,ref bool __result)
        {if(Self?.IsNetworkPhone(__instance)!=true)return true;__result=true;return false;}
        private static bool NetworkPhoneAnswer(long __0,ref bool __result)
        {
            if(__0!=NetworkNumber||Self?.IsNetworkPhone(PlayerStore.instance?.PhoneClientDict?.GetValueOrDefault(NetworkNumber))!=true)return true;
            __result=true;return false;
        }
        private static bool NetworkPhoneService(StorePhoneClient __instance)=>Self?.IsNetworkPhone(__instance)!=true;
        private static bool NetworkPhoneCall(StorePhoneClient __instance,ref Dialogue __result)
        {
            var trade=Self;if(trade?.IsNetworkPhone(__instance)!=true)return true;
            trade.core.EnsureChineseFallback();
            trade.networkChoices.Clear();
            __result=trade.NetworkMenu();return false;
        }
        private static string NetworkTierName(string tier)=>tier switch{"community"=>"社区维修料","practice"=>"配件与练习",_=>"舞台专业"};
        private static Dialogue NetworkLine(string text)=>new Dialogue().SetText(NetworkName,text);
        private void NetworkChoice(Dialogue menu,string label,Dialogue destination,Action run=null)
        {
            Action callback=run??(()=>{});
            networkChoices.Add(callback);
            var icon=ItemDirectory.CreateEmptyItem("ms_network_choice_"+networkChoices.Count);
            // The phone renderer reads the choice node's text, then advances to its next dialogue.
            var choice=new Dialogue().SetText(DialoguePhoneUIManager.PLAYER,label).SetNextDialogue(destination);
            menu.AddChoice(icon,label,label,choice,callback,null);
        }
        private Dialogue NetworkMenu()
        {
            var trade=host.ReadState.MusicTrade;
            var booking=trade.Referrals.Values.Where(x=>x.Status=="confirmed").OrderBy(x=>x.Day).FirstOrDefault();
            if(booking==null&&trade.PendingIntent==null)return NetworkTiersMenu();
            var menu=booking!=null?NetworkLine("已约第 "+booking.Day+" 天到店。"):NetworkLine("有一条未收费意向，名额充足时可以重新确认。");
            if(booking!=null){
                NetworkChoice(menu,"取消预约",NetworkConfirmation("主动取消不会退费，也不恢复当天推荐次数。",()=>{
                    CancelReferral(booking.Id);return "预约已取消。";
                }));
            }else{
                var intent=trade.PendingIntent;
                try{
                    var fresh=ReferralPreview(intent.Tier,intent.Kind,intent.Target,intent.Zone);
                    NetworkChoice(menu,"重新预览意向",NetworkReferralConfirmation(fresh));
                }catch(InvalidOperationException error){menu.SetText(NetworkName,"有一条未收费意向。"+error.Message+"。");}
                NetworkChoice(menu,"撤销意向",NetworkConfirmation("撤销意向无需付款。",()=>{
                    CancelReferralIntent(intent.Id);return "已撤销这条意向。";
                }));
            }
            NetworkChoice(menu,"查看业务",NetworkTiersMenu());
            return menu;
        }
        private Dialogue NetworkTiersMenu()
        {
            var state=host.ReadState;
            var status=new List<string>{"想开通哪项业务，或请我联系哪类普通来访？"};
            var choices=new List<(string Label,Dialogue Destination)>();
            foreach(string tier in NetworkTiers){
                string label=NetworkTierName(tier);
                if(state.MusicTrade.Openings.ContainsKey(tier)){
                    status.Add(label+"已开通。");
                    choices.Add((label,NetworkDirection(tier)));
                    continue;
                }
                int fee=PackageDNetwork.Opening(tier);
                string why=PackageDNetwork.Qualification(state,tier,MiddleOpen());
                if(why==null&&PlayerStore.instance.GetCash()<fee)why="当前现金不足";
                status.Add(label+"开通费 "+fee+(why==null?"。":"，"+why+"。"));
                if(why==null)choices.Add((label,NetworkConfirmation("一次开通费 "+fee+"。确认开通"+label+"吗？",()=>{
                    OpenBusiness(tier);return label+"已开通。";
                })));
            }
            var menu=NetworkLine(string.Join("\n",status));
            foreach(var choice in choices.Take(2))NetworkChoice(menu,choice.Label,choice.Destination);
            if(choices.Count>2){
                var more=NetworkLine("还有一档业务，也可以查阅店铺的实际收支。");
                NetworkChoice(more,choices[2].Label,choices[2].Destination);
                NetworkChoice(more,"查看经营账册",NetworkLedger());
                NetworkChoice(menu,"更多业务与账册",more);
            }else NetworkChoice(menu,"查看经营账册",NetworkLedger());
            return menu;
        }
        private Dialogue NetworkDirection(string tier)
        {
            var menu=NetworkLine("想联系买家，还是请曾到店的卖方带剩余库存回来？");
            NetworkChoice(menu,"找买家",NetworkBuyers(tier));
            NetworkChoice(menu,"请卖方回访",NetworkSellers(tier));
            NetworkChoice(menu,"查看经营账册",NetworkLedger());
            return menu;
        }
        private Dialogue NetworkSellers(string tier)
        {
            var returns=new List<(string Label,MusicReferral Preview)>();
            foreach(var visit in AvailableReturns().Where(x=>NetworkSellerTier(x.Zone)==tier).OrderByDescending(x=>x.Day).ThenBy(x=>x.Id)){
                try{
                    var preview=ReferralPreview(tier,"seller",visit.Id,visit.Zone);
                    returns.Add(("第 "+visit.Day+" 天的卖方 · 剩 "+visit.NativeStock.Count+" 件",preview));
                }catch(InvalidOperationException){continue;}
            }
            if(returns.Count==0)return NetworkLine("没有可以回访的卖方。离店时保留了未售实物和报价记录的卖方才可预约。");
            Dialogue page=null;
            // The native phone shows at most three choices per page.
            for(int end=returns.Count;end>0;){
                int start=end-(page==null?Math.Min(3,end):Math.Min(2,end));
                var menu=NetworkLine("选择要回访的卖方。会带回同一批未售实物，沿用上次的报价条件。");
                for(int i=start;i<end;i++)NetworkChoice(menu,returns[i].Label,NetworkReferralConfirmation(returns[i].Preview));
                if(page!=null)NetworkChoice(menu,"查看更早的卖方",page);
                page=menu;end=start;
            }
            return page;
        }
        private Dialogue NetworkBuyers(string tier)
        {
            var referrals=new List<(MusicBuyerProfile Profile,MusicReferral Preview)>();
            foreach(var profile in PackageDCatalog.Buyers.Where(p=>NetworkBuyerTier(p.Id)==tier)){
                foreach(string zone in profile.Zones.OrderBy(z=>z=="middle"?0:1)){
                    MusicReferral preview;
                    try{preview=ReferralPreview(tier,"buyer",profile.Id,zone);}
                    catch(InvalidOperationException){continue;}
                    referrals.Add((profile,preview));break;
                }
            }
            if(referrals.Count==0)return NetworkLine("当前没有可以确认的买家推荐。请核对可售实物与今日推荐次数。");
            var menu=NetworkLine("想联系哪类买家？确认后按普通来访名额安排。");
            foreach(var referral in referrals)NetworkChoice(menu,referral.Profile.Name,NetworkReferralConfirmation(referral.Preview));
            return menu;
        }
        private Dialogue NetworkReferralConfirmation(MusicReferral preview)
        {
            int fee=preview.Tier=="community"&&!D.CommunityFreeUsed?0:PackageDNetwork.Fee(preview.Tier);
            string text="计划第 "+preview.Day+" 天到店。费用 "+fee+"。若普通来访名额不足，先保留意向，无需付款。";
            return NetworkConfirmation(text,()=>{
                ConfirmReferral(preview);
                return D.Referrals.ContainsKey(preview.Id)?"已约第 "+preview.Day+" 天到店。":"已保留未收费意向。";
            });
        }
        private static string NetworkSellerTier(string zone)=>zone=="lower"?"community":zone=="middle"?"practice":"stage";
        private static string NetworkBuyerTier(string target)=>target=="B07"?"community":target is "B01" or "B02" or "B06"?"practice":"stage";
        private Dialogue NetworkConfirmation(string text,Func<string> commit)
        {
            var menu=NetworkLine(text);
            var result=NetworkLine("正在确认。");
            NetworkChoice(menu,"确认",result,()=>{
                try{result.SetText(NetworkName,commit());}
                catch(InvalidOperationException error){result.SetText(NetworkName,error.Message);}
            });
            NetworkChoice(menu,"先不办",NetworkLine("好，有需要再联系。"));
            return menu;
        }
    }
}