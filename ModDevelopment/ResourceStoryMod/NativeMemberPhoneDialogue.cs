using System;
namespace ResourceStoryMod
{
    internal sealed partial class NativeMemberBusiness
    {
        private StoreClient pendingCounterDialogue;
        private bool MemberPhoneBusy
        {
            get
            {
                var phone=PhoneUIManager.Instance;
                // StopCall clears currentNumber but retains currentPhoneClient as call history.
                return phone!=null && phones.ContainsKey(phone.currentNumber) && (phone.isCalling || phone.currentPhoneClient!=null);
            }
        }
        private bool DeferCounterDialogue(StoreClient client)
        {
            // Native StartMainDialogue marks the client started even when StartDialogue
            // refuses it. A member phone call owns the shared dialogue UI until hangup.
            if(!MemberPhoneBusy || PlayerStore.instance?.currentClientInstance?.storeClient?.NativeIdentity()!=client.NativeIdentity())return false;
            pendingCounterDialogue=client;
            core.LoggerInstance.Msg("MEMBER_PHONE_COUNTER_DEFERRED "+client.identifier);
            return true;
        }
        private void PumpCounterDialogue()
        {
            if(pendingCounterDialogue==null)return;
            var store=PlayerStore.instance;
            if(store?.currentClientInstance?.storeClient?.NativeIdentity()!=pendingCounterDialogue.NativeIdentity()){pendingCounterDialogue=null;return;}
            var dialog=DialogUIManager.Instance;
            if(!store.isClientArrived || MemberPhoneBusy || dialog.isConversationActiveFlag!=0 || dialog.currentDialog!=null)return;
            var client=pendingCounterDialogue;pendingCounterDialogue=null;
            client.StartMainDialogue();
            core.LoggerInstance.Msg("MEMBER_PHONE_COUNTER_RESUMED "+client.identifier);
        }
        internal object PhoneCounterStatus()=>new{
            memberPhoneBusy=MemberPhoneBusy,deferredClient=pendingCounterDialogue?.identifier,
            client=PlayerStore.instance?.currentClientInstance?.storeClient?.identifier,
            mainStarted=PlayerStore.instance?.currentClientInstance?.storeClient?.isMainDialogueStarted,
            conversation=DialogUIManager.Instance.isConversationActiveFlag,
            waiting=DialogUIManager.Instance.isWaitingForManualAdvance,
            currentDialogue=DialogUIManager.Instance.currentDialog!=null,
            shutterLock=PlayerStore.instance.lockShutterButtonFlag,
            clientClickLock=PlayerStore.instance.lockClientClickFlag
        };
        internal bool CounterDialoguePending=>pendingCounterDialogue!=null;
        internal bool IsMemberPhoneActive=>MemberPhoneBusy;
        internal long PhoneNumber(string actor)
        {
            foreach(var pair in phones)if(pair.Value==actor)return pair.Key;
            throw new InvalidOperationException("Member phone is not registered");
        }
    }
}
