using System;
using Unity.Netcode;

namespace HalliGalli.Movement
{
    public sealed partial class CourseNetwork
    {
        [Serializable] sealed class ShirtRequest {public int index,request,epoch,mapRevision;}
        [Serializable] sealed class ShirtResult {public int index,request;public bool accepted;}
        int shirtRequestSequence,pendingShirtRequest,pendingShirtColor;

        public int SelectedShirtColor {
            get {int seat=LocalSeat;return online&&seat>=0?members[seat].shirtColor:PlayerShirtColors.SavedIndex;}
        }
        bool CanEditShirt=>!Busy&&!leaving&&!Playing&&!loading&&!UserMapStudio.IsOpen&&
            (online?manager&&manager.IsListening&&LocalSeat>=0:menu);
        public bool CanSelectShirtColor(int index)
        {
            if(!PlayerShirtColors.IsValid(index)||!CanEditShirt||pendingShirtRequest!=0)return false;
            return !online||!ShirtOccupied(index,manager.LocalClientId);
        }
        public bool SelectShirtColor(int index)
        {
            if(!CanSelectShirtColor(index))return false;
            if(!online){PlayerShirtColors.Save(index);if(game&&game.roster)game.roster.ApplyOfflineShirtColors();return true;}
            if(IsHost){
                if(!AssignShirtColor(manager.LocalClientId,index,epoch,mapRevision))return false;
                PlayerShirtColors.Save(index);BroadcastRoom();return true;
            }
            pendingShirtRequest=++shirtRequestSequence;pendingShirtColor=index;
            Send("HG.shirt",new ShirtRequest{index=index,request=pendingShirtRequest,epoch=epoch,mapRevision=mapRevision},NetworkManager.ServerClientId);
            return true;
        }
        bool ShirtOccupied(int index,ulong except)
        {
            foreach(var member in members)if(member.id!=except&&member.shirtColor==index)return true;
            // Approved connections reserve their color before Connected adds their member.
            foreach(var entry in approved)if(entry.Key!=except&&entry.Value.shirtColor==index)return true;
            return false;
        }
        int FreeShirtColor(int preferred,ulong except)
        {
            if(PlayerShirtColors.IsValid(preferred)&&!ShirtOccupied(preferred,except))return preferred;
            for(int index=0;index<PlayerShirtColors.Count;index++)if(!ShirtOccupied(index,except))return index;
            return -1;
        }
        bool AssignShirtColor(ulong sender,int index,int requestEpoch,int revision)
        {
            if(!online||!IsHost||Busy||leaving||Playing||loading||requestEpoch!=epoch||revision!=mapRevision||!PlayerShirtColors.IsValid(index))return false;
            var member=members.Find(m=>!m.bot&&m.id==sender);
            if(member==null||ShirtOccupied(index,sender))return false;
            member.shirtColor=index;
            // The approval entry and connected member share one reservation object.
            return true;
        }
        void RegisterShirtMessages()
        {
            manager.CustomMessagingManager.RegisterNamedMessageHandler("HG.shirt",(id,r)=>ReadJson<ShirtRequest>(id,r,ReceiveShirtRequest));
            manager.CustomMessagingManager.RegisterNamedMessageHandler("HG.shirtResult",(id,r)=>ReadJson<ShirtResult>(id,r,ReceiveShirtResult));
        }
        void ReceiveShirtRequest(ulong sender,ShirtRequest request)
        {
            if(!online||!IsHost||!members.Exists(m=>!m.bot&&m.id==sender))return;
            bool accepted=request.request>0&&AssignShirtColor(sender,request.index,request.epoch,request.mapRevision);
            if(accepted)BroadcastRoom();
            Send("HG.shirtResult",new ShirtResult{index=request.index,request=request.request,accepted=accepted},sender);
        }
        void ReceiveShirtResult(ulong sender,ShirtResult result)
        {
            if(!online||IsHost||sender!=NetworkManager.ServerClientId||pendingShirtRequest==0||result.request!=pendingShirtRequest||result.index!=pendingShirtColor)return;
            pendingShirtRequest=0;
            if(result.accepted&&PlayerShirtColors.IsValid(result.index))PlayerShirtColors.Save(result.index);
        }
        static bool ValidShirtMembers(Member[] roomMembers)
        {
            int used=0;
            foreach(var member in roomMembers){
                if(member==null||!PlayerShirtColors.IsValid(member.shirtColor)||(used&(1<<member.shirtColor))!=0)return false;
                used|=1<<member.shirtColor;
            }
            return true;
        }
        void ApplyRoomShirtColors()
        {
            if(!game||!game.roster||LocalSeat<0)return;
            int local=LocalSeat,remote=0;
            // Lobby scenes have not yet undergone BindAfterStart's local-seat swap.
            foreach(var participant in game.roster.allParticipants){
                if(!participant)continue;
                if(participant.localPlayer){PlayerShirtAppearance.Apply(participant,members[local].shirtColor);continue;}
                if(remote==local)remote++;
                if(remote<members.Count)PlayerShirtAppearance.Apply(participant,members[remote++].shirtColor);
            }
        }
    }
}
