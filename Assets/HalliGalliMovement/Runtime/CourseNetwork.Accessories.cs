using System;
using Unity.Netcode;

namespace HalliGalli.Movement
{
    public sealed partial class CourseNetwork
    {
        [Serializable] sealed class AccessoryRequest {public int index,epoch,mapRevision;}

        public int SelectedAccessory {
            get {int seat=LocalSeat;return online&&seat>=0?members[seat].accessory:PlayerAccessories.SavedIndex;}
        }
        public bool CanSelectAccessory(int index)=>PlayerAccessories.IsValid(index)&&CanEditAppearance;
        public bool SelectAccessory(int index)
        {
            if(!CanSelectAccessory(index))return false;
            if(!online){PlayerAccessories.Save(index);if(game&&game.roster)game.roster.ApplyOfflineAppearance();return true;}
            if(IsHost){
                if(!AssignAccessory(manager.LocalClientId,index,epoch,mapRevision))return false;
                PlayerAccessories.Save(index);BroadcastRoom();return true;
            }
            // Unlike shirt colors nothing can be taken by someone else, so the preference is saved at once;
            // the worn item still changes only when the host's room state arrives.
            PlayerAccessories.Save(index);
            Send("HG.accessory",new AccessoryRequest{index=index,epoch=epoch,mapRevision=mapRevision},NetworkManager.ServerClientId);
            return true;
        }
        bool AssignAccessory(ulong sender,int index,int requestEpoch,int revision)
        {
            if(!online||!IsHost||Busy||leaving||Playing||loading||requestEpoch!=epoch||revision!=mapRevision||!PlayerAccessories.IsValid(index))return false;
            var member=members.Find(m=>!m.bot&&m.id==sender);
            if(member==null)return false;
            member.accessory=index;
            return true;
        }
        void RegisterAccessoryMessages()
        {
            manager.CustomMessagingManager.RegisterNamedMessageHandler("HG.accessory",(id,r)=>ReadJson<AccessoryRequest>(id,r,ReceiveAccessoryRequest));
        }
        void ReceiveAccessoryRequest(ulong sender,AccessoryRequest request)
        {
            if(AssignAccessory(sender,request.index,request.epoch,request.mapRevision))BroadcastRoom();
        }
        static bool ValidAccessoryMembers(Member[] roomMembers)
        {
            foreach(var member in roomMembers)if(!PlayerAccessories.IsValid(member.accessory))return false;
            return true;
        }
    }
}
