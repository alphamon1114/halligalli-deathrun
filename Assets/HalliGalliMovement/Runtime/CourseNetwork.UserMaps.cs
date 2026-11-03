using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class CourseNetwork
    {
        const int UserMapChunkBytes=1024,UserMapWindowChunks=8;
        const float UserMapSendInterval=.05f,UserMapReceiveTimeout=30f;
        static readonly UTF8Encoding MapUtf8=new UTF8Encoding(false,true);
        [Serializable] sealed class UserMapOffer {public int revision,bytes;public string id,name,hash;}
        [Serializable] sealed class UserMapAck {public int revision,received;public string id,hash;public bool ready,failed;}
        sealed class UserMapSend {public int revision,offset,acknowledged;public bool ready;public float progressAt;public string error;}
        struct LocalUserMap {public UserMapData data;public string hash,label;public bool cleared,downloaded;}
        readonly Dictionary<ulong,UserMapSend> userMapSends=new Dictionary<ulong,UserMapSend>();
        UserMapOffer userMapOffer;
        UserMapData selectedUserMap;
        byte[] selectedMapBytes,receivingMapBytes;
        int mapRevision,receivedMapBytes;
        float nextMapSend,mapReceiveDeadline;
        string mapTransferError="";
        bool userMapsTab;
        LocalUserMap[] localUserMaps=Array.Empty<LocalUserMap>();
        int userMapsPage,officialMapsPage;

        public bool IsUserMapSelected=>userMapOffer!=null;
        public string SelectedUserMapHash=>userMapOffer?.hash??"";
        public int SelectedUserMapRevision=>mapRevision;
        public bool UserMapReady=>!IsUserMapSelected||selectedUserMap!=null&&selectedUserMap.id==userMapOffer.id;
        internal bool UserMapCacheHit {get;private set;}
        public string SelectedMapTitle
        {
            get{
                if(IsUserMapSelected)return userMapOffer.name;
                if(game&&game.maps&&game.maps.catalog)foreach(var map in game.maps.catalog.maps)if(map&&map.id==mapId)return map.displayName;
                return mapId;
            }
        }
        bool CanEditMaps=>online&&IsHost&&!Busy&&!leaving&&!Playing&&!loading&&game&&game.maps&&game.maps.catalog;
        bool AllUserMapsReady
        {
            get{
                if(!UserMapReady)return false;
                if(!IsUserMapSelected)return true;
                foreach(var member in members)if(!member.bot&&!IsMapPeerReady(member.id))return false;
                return true;
            }
        }
        public string UserMapTransferStatus
        {
            get{
                if(!IsUserMapSelected)return "공식 맵 · 다운로드 필요 없음";
                if(!string.IsNullOrEmpty(mapTransferError))return "맵 공유 실패 · 아래 안내를 확인하세요";
                if(!IsHost)return UserMapReady?(UserMapCacheHit?"보관된 같은 맵 사용 · 다운로드 생략":"받은 맵 저장 완료 · 준비할 수 있어요"):"맵 다운로드 중 · "+(receivedMapBytes*100/userMapOffer.bytes)+"%";
                int ready=0,humans=0;
                foreach(var member in members){
                    if(member.bot)continue;
                    humans++;
                    if(IsMapPeerReady(member.id))ready++;
                    else if(userMapSends.TryGetValue(member.id,out var transfer)&&!string.IsNullOrEmpty(transfer.error))return "맵 공유 실패 · 맵을 다시 선택해 재전송하세요";
                }
                return ready==humans?"맵 검증 완료 · 모두 같은 맵을 받았어요":"맵 공유 중 · "+ready+" / "+humans+"명 검증 완료";
            }
        }

        public bool SelectUserMap(UserMapData data)
        {
            if(!CanEditMaps)return false;
            try{
                if(!Application.CanStreamedLevelBeLoaded(UserMapRuntime.ScenePath))throw new InvalidOperationException("사용자 맵 템플릿이 빌드에 없어. 최신 빌드를 사용해줘.");
                string json=UserMapStorage.Serialize(data);
                if(!UserMapDownloads.Contains(data))UserMapClearance.RequireCleared(data,json);
                var snapshot=UserMapStorage.Parse(json);
                var bytes=MapUtf8.GetBytes(json);
                if(bytes.Length==0||bytes.Length>UserMapStorage.MaxBytes)throw new InvalidOperationException("맵 파일 크기가 너무 커.");
                string hash=UserMapStorage.Hash(json);
                ResetUserMapState();mapRevision++;
                selectedUserMap=snapshot;selectedMapBytes=bytes;mapId="user:"+snapshot.id;userMapsTab=true;
                userMapOffer=new UserMapOffer{revision=mapRevision,id=snapshot.id,name=snapshot.name,hash=hash,bytes=bytes.Length};
                ResetMapReadiness();Status="";
                foreach(var member in members)if(!member.bot)QueueUserMap(member.id);
                BroadcastRoom();return true;
            }catch(Exception e){Status="맵을 선택하지 못했어: "+e.Message;return false;}
        }
        void ResetMapReadiness(){foreach(var member in members)member.ready=member.bot||member.id==NetworkManager.ServerClientId;}
        void ResetUserMapState()
        {
            userMapSends.Clear();userMapOffer=null;selectedUserMap=null;selectedMapBytes=null;receivingMapBytes=null;
            receivedMapBytes=0;nextMapSend=mapReceiveDeadline=0;mapTransferError="";userMapsTab=false;UserMapRuntime.Pending=null;
            UserMapCacheHit=false;
        }
        void RegisterUserMapMessages()
        {
            manager.CustomMessagingManager.RegisterNamedMessageHandler("HG.mapChunk",ReceiveUserMapChunk);
            manager.CustomMessagingManager.RegisterNamedMessageHandler("HG.mapAck",(id,reader)=>ReadJson<UserMapAck>(id,reader,ReceiveUserMapAck));
        }
        static bool IsHex(string text,int length)
        {
            if(text==null||text.Length!=length)return false;
            foreach(char c in text)if(!(c>='0'&&c<='9'||c>='a'&&c<='f'||c>='A'&&c<='F'))return false;
            return true;
        }
        static bool SameOffer(UserMapOffer a,UserMapOffer b)=>a==null?b==null:b!=null&&a.revision==b.revision&&a.id==b.id&&a.hash==b.hash&&a.bytes==b.bytes&&a.name==b.name;
        bool ReceiveMapSelection(Room room)
        {
            if(room.mapRevision<mapRevision)return false;
            bool isUser=room.map!=null&&room.map.StartsWith("user:",StringComparison.Ordinal);
            // Unity's inline serializer can materialize an empty nested object for a null field.
            var offer=isUser?room.userMap:null;
            if(room.mapRevision<0||isUser!=(offer!=null)||offer!=null&&(offer.revision!=room.mapRevision||offer.revision<=0||
                !IsHex(offer.id,32)||!IsHex(offer.hash,64)||offer.bytes<=0||offer.bytes>UserMapStorage.MaxBytes||
                string.IsNullOrWhiteSpace(offer.name)||offer.name.Length>128||room.map!="user:"+offer.id)){
                _=Leave("방장이 보낸 맵 정보가 올바르지 않아.");return false;
            }
            if(room.mapRevision==mapRevision){
                if(room.map!=mapId||!SameOffer(userMapOffer,offer)){_=Leave("같은 선택 번호의 맵 정보가 변경됐어. 다시 참가해줘.");return false;}
                return true;
            }
            ResetUserMapState();mapRevision=room.mapRevision;mapId=room.map;userMapOffer=offer;userMapsTab=isUser;Status="";
            if(offer!=null){
                if(UserMapDownloads.TryLoad(offer.hash,out var cached)&&cached.id==offer.id&&cached.name==offer.name&&
                    MapUtf8.GetByteCount(UserMapStorage.Serialize(cached))==offer.bytes){
                    selectedUserMap=cached;receivedMapBytes=offer.bytes;UserMapCacheHit=true;SendMapAck(true);
                }else{
                    receivingMapBytes=new byte[offer.bytes];mapReceiveDeadline=Time.realtimeSinceStartup+UserMapReceiveTimeout;
                }
            }
            return true;
        }
        void QueueUserMap(ulong peer)
        {
            if(!IsHost||!IsUserMapSelected||selectedMapBytes==null||peer==NetworkManager.ServerClientId)return;
            if(userMapSends.TryGetValue(peer,out var state)&&state.revision==mapRevision)return;
            userMapSends[peer]=new UserMapSend{revision=mapRevision,progressAt=Time.realtimeSinceStartup};
        }
        bool IsMapPeerReady(ulong peer)
        {
            if(!IsUserMapSelected)return true;
            if(peer==NetworkManager.ServerClientId)return UserMapReady;
            return userMapSends.TryGetValue(peer,out var state)&&state.revision==mapRevision&&state.ready;
        }
        void UpdateUserMapTransfer()
        {
            if(!IsUserMapSelected||Playing||loading)return;
            float now=Time.realtimeSinceStartup;
            if(!IsHost){
                if(receivingMapBytes!=null&&now>mapReceiveDeadline)FailUserMapReceive("맵 다운로드 시간이 초과됐어. 방장에게 맵을 다시 선택해 달라고 해줘.");
                return;
            }
            if(now<nextMapSend||selectedMapBytes==null)return;
            nextMapSend=now+UserMapSendInterval;
            // Same reliable pipeline as the manifest; cap unacknowledged data as well as the send rate.
            foreach(var pair in userMapSends){
                var state=pair.Value;
                if(state.ready||state.revision!=mapRevision||!string.IsNullOrEmpty(state.error))continue;
                if(state.offset>state.acknowledged&&now-state.progressAt>UserMapReceiveTimeout){state.error="맵 전송 응답 시간 초과";continue;}
                if(state.offset==selectedMapBytes.Length||state.offset-state.acknowledged>=UserMapChunkBytes*UserMapWindowChunks)continue;
                int count=Math.Min(UserMapChunkBytes,selectedMapBytes.Length-state.offset);
                using(var writer=new FastBufferWriter(count+12,Allocator.Temp)){
                    writer.WriteValueSafe(mapRevision);writer.WriteValueSafe(state.offset);writer.WriteValueSafe(count);
                    writer.WriteBytesSafe(selectedMapBytes,count,state.offset);
                    state.offset+=count;
                    manager.CustomMessagingManager.SendNamedMessage("HG.mapChunk",pair.Key,writer,NetworkDelivery.ReliableFragmentedSequenced);
                }
            }
        }
        void ReceiveUserMapChunk(ulong sender,FastBufferReader reader)
        {
            if(!online||IsHost||sender!=NetworkManager.ServerClientId||!IsUserMapSelected||receivingMapBytes==null||Playing||loading)return;
            try{
                if(!reader.TryBeginRead(12))throw new InvalidOperationException("맵 조각의 헤더가 손상됐어.");
                reader.ReadValueSafe(out int revision);reader.ReadValueSafe(out int offset);reader.ReadValueSafe(out int count);
                if(revision!=mapRevision)return;
                int expected=Math.Min(UserMapChunkBytes,receivingMapBytes.Length-receivedMapBytes);
                if(offset!=receivedMapBytes||count!=expected||count<=0||!reader.TryBeginRead(count)||reader.Length-reader.Position!=count)
                    throw new InvalidOperationException("맵 조각의 순서나 크기가 올바르지 않아.");
                reader.ReadBytesSafe(ref receivingMapBytes,count,receivedMapBytes);receivedMapBytes+=count;
                mapReceiveDeadline=Time.realtimeSinceStartup+UserMapReceiveTimeout;
                if(receivedMapBytes!=receivingMapBytes.Length){
                    if(receivedMapBytes%(UserMapChunkBytes*(UserMapWindowChunks/2))==0)SendMapAck(false);
                    return;
                }
                string json=MapUtf8.GetString(receivingMapBytes);
                if(UserMapStorage.Hash(json)!=userMapOffer.hash)throw new InvalidOperationException("맵 파일의 해시가 일치하지 않아.");
                var data=UserMapStorage.Parse(json);
                if(data.id!=userMapOffer.id||data.name!=userMapOffer.name)throw new InvalidOperationException("맵 파일과 선택 정보가 일치하지 않아.");
                if(UserMapStorage.Serialize(data)!=json)throw new InvalidOperationException("맵 파일이 표준 저장 형식과 일치하지 않아.");
                UserMapDownloads.Store(data);
                selectedUserMap=data;receivingMapBytes=null;mapReceiveDeadline=0;
                SendMapAck(true);
            }catch(Exception e){FailUserMapReceive("맵을 받지 못했어: "+e.Message+" 방장에게 다시 선택해 달라고 해줘.");}
        }
        void FailUserMapReceive(string reason)
        {
            receivingMapBytes=null;selectedUserMap=null;mapReceiveDeadline=0;mapTransferError=reason;Status=reason;SendMapAck(false,true);
        }
        void SendMapAck(bool ready,bool failed=false)
        {
            if(IsUserMapSelected)Send("HG.mapAck",new UserMapAck{revision=mapRevision,received=receivedMapBytes,id=userMapOffer.id,hash=userMapOffer.hash,ready=ready,failed=failed},NetworkManager.ServerClientId);
        }
        void ReceiveUserMapAck(ulong sender,UserMapAck ack)
        {
            if(!online||!IsHost||!IsUserMapSelected||sender==NetworkManager.ServerClientId||
                ack.revision!=mapRevision||ack.id!=userMapOffer.id||ack.hash!=userMapOffer.hash||
                ack.received<0||ack.received>userMapOffer.bytes||ack.ready&&ack.failed)return;
            bool knownPeer=false;
            foreach(var member in members)if(!member.bot&&member.id==sender){knownPeer=true;break;}
            if(!knownPeer)return;
            if(!userMapSends.TryGetValue(sender,out var state)||state.revision!=mapRevision){
                // A cached peer can acknowledge the manifest before Connected queues its payload.
                if(!ack.ready||ack.received!=userMapOffer.bytes)return;
                state=new UserMapSend{revision=mapRevision};userMapSends[sender]=state;
            }
            if(state.ready||!string.IsNullOrEmpty(state.error))return;
            if(ack.failed){
                if(ack.received<=state.offset)state.error="참가자의 맵 검증 실패";
                return;
            }
            if(ack.ready){
                if(ack.received!=userMapOffer.bytes)return;
                state.offset=state.acknowledged=ack.received;state.ready=true;state.progressAt=Time.realtimeSinceStartup;
                BroadcastRoom();return;
            }
            if(ack.received<=state.acknowledged||ack.received>state.offset||
                ack.received==userMapOffer.bytes||ack.received%UserMapChunkBytes!=0)return;
            state.acknowledged=ack.received;state.progressAt=Time.realtimeSinceStartup;
        }
        string MapPeerLabel(Member member)
        {
            if(member.id==NetworkManager.ServerClientId)return "방장";
            if(IsUserMapSelected&&IsHost&&!IsMapPeerReady(member.id)){
                if(userMapSends.TryGetValue(member.id,out var state))return !string.IsNullOrEmpty(state.error)?"공유 실패":state.offset==userMapOffer.bytes?"맵 검증 중":"맵 "+(state.acknowledged*100/userMapOffer.bytes)+"%";
                return "맵 대기";
            }
            return member.ready?"준비 완료":"대기";
        }
        string OfficialHomeScene()
        {
            if(game&&game.maps&&game.maps.catalog)foreach(var map in game.maps.catalog.maps)
                if(map&&map.playable&&Application.CanStreamedLevelBeLoaded(map.scenePath))return map.scenePath;
            return null;
        }

        public void SetStudioPlay(bool testing)
        {
            if(online||!UserMapStudio.IsOpen||!game)return;
            menu=false;Playing=false;Status="";FreezeWorld();game.cardGame.enabled=false;
            if(!testing)return;
            var controller=game.Local.GetComponent<SourcePlayerController>();
            controller.controlsEnabled=true;controller.suppressCursorCapture=false;
            game.Local.Combat.simulateAutomatically=true;game.Local.Combat.readInput=true;
            if(!Application.isBatchMode)controller.SetCursorCaptured(true);
        }

        void RefreshUserMaps()
        {
            try{
                var maps=new List<LocalUserMap>();
                var indices=new Dictionary<string,int>(StringComparer.Ordinal);
                foreach(var data in UserMapStorage.List()){
                    string hash=UserMapStorage.Hash(UserMapStorage.Serialize(data));
                    if(indices.ContainsKey(hash))continue;
                    indices.Add(hash,maps.Count);
                    maps.Add(new LocalUserMap{data=data,hash=hash,cleared=UserMapClearance.IsCleared(data)});
                }
                foreach(var data in UserMapDownloads.List()){
                    string hash=UserMapStorage.Hash(UserMapStorage.Serialize(data));
                    if(indices.TryGetValue(hash,out int index)){
                        var entry=maps[index];entry.downloaded=true;maps[index]=entry;
                    }else{
                        indices.Add(hash,maps.Count);maps.Add(new LocalUserMap{data=data,hash=hash,downloaded=true});
                    }
                }
                localUserMaps=maps.ToArray();userMapsPage=0;
                int unverified=0;
                for(int i=0;i<localUserMaps.Length;i++){
                    var entry=localUserMaps[i];
                    if(!entry.cleared&&!entry.downloaded)unverified++;
                    string label=entry.downloaded?"받은 맵 · 방 선택 가능":entry.cleared?"내 맵 · 완주 인증":"내 맵 · 혼자 완주 필요";
                    localUserMaps[i].label=label+" · "+entry.hash.Substring(0,8);
                }
                Status=unverified==0?"":"미인증 맵 "+unverified+"개는 선택할 수 없어요. 새로 만들거나 수정한 맵은 맵 만들기에서 혼자 종까지 완주하고 저장하세요.";
            }
            catch(Exception e){localUserMaps=Array.Empty<LocalUserMap>();Status="저장한 맵을 읽지 못했어: "+e.Message;}
        }
        void DrawMapSelection(float right,float y)
        {
            if(BellUI.Button(new Rect(right,y+124,211,34),"공식 맵",selected:!userMapsTab))userMapsTab=false;
            if(BellUI.Button(new Rect(right+222,y+124,218,34),"사용자 맵",selected:userMapsTab)){
                userMapsTab=true;if(IsHost)RefreshUserMaps();
            }
            BellUI.Text(new Rect(right,y+164,440,35),"선택: "+SelectedMapTitle,15,BellUI.Paper,true);
            if(!userMapsTab){
                int count=0;foreach(var map in game.maps.catalog.maps)if(map&&map.playable)count++;
                int pages=Mathf.Max(1,(count+3)/4);officialMapsPage=Mathf.Clamp(officialMapsPage,0,pages-1);
                int index=0,first=officialMapsPage*4;
                foreach(var map in game.maps.catalog.maps){
                    if(!map||!map.playable)continue;
                    int slot=index++;if(slot<first||slot>=first+4)continue;
                    float row=y+200+(slot-first)*44;
                    GUI.enabled=CanEditMaps;
                    if(BellUI.Button(new Rect(right,row,440,39),"",selected:map.id==mapId))SelectMap(map.id);
                    BellUI.MapPicture(new Rect(right+8,row+4,72,31),map,10);
                    BellUI.Text(new Rect(right+94,row,338,22),map.displayName,16,BellUI.Paper,true);
                    BellUI.Text(new Rect(right+94,row+22,338,17),map.StyleLabel+" · "+map.DifficultyLabel,11,BellUI.Muted);
                }
                GUI.enabled=officialMapsPage>0;
                if(BellUI.Button(new Rect(right,y+379,70,28),"이전",size:12))officialMapsPage--;
                GUI.enabled=officialMapsPage+1<pages;
                if(BellUI.Button(new Rect(right+370,y+379,70,28),"다음",size:12))officialMapsPage++;
                GUI.enabled=true;
                BellUI.Text(new Rect(right+80,y+379,280,28),(officialMapsPage+1)+" / "+pages,12,BellUI.Muted,false,TextAnchor.MiddleCenter);
            }else if(IsHost){
                GUI.enabled=CanEditMaps;
                if(BellUI.Button(new Rect(right+326,y+201,114,28),"새로고침",size:12))RefreshUserMaps();
                BellUI.Text(new Rect(right,y+201,310,28),"내 완주 맵 / 받은 원본 맵 선택 가능",13,BellUI.Muted);
                if(localUserMaps.Length==0)BellUI.Text(new Rect(right,y+245,440,80),"저장한 맵이 없어요.\n다른 방에서 받은 맵은 자동으로 보관돼요.\n직접 만들거나 수정하면 혼자 완주해야 해요.",15,BellUI.Muted);
                int first=userMapsPage*3;
                for(int i=first;i<Math.Min(first+3,localUserMaps.Length);i++){
                    var entry=localUserMaps[i];bool selectable=entry.cleared||entry.downloaded;float row=y+239+(i-first)*45;
                    GUI.enabled=CanEditMaps&&selectable;
                    if(BellUI.Button(new Rect(right,row,440,39),"",selected:IsUserMapSelected&&SelectedUserMapHash==entry.hash))SelectUserMap(entry.data);
                    BellUI.Text(new Rect(right+12,row,416,22),entry.data.name,15,BellUI.Paper,true);
                    BellUI.Text(new Rect(right+12,row+21,416,18),entry.label,11,selectable?BellUI.Cool:BellUI.Gold);
                }
                GUI.enabled=CanEditMaps&&userMapsPage>0;
                if(BellUI.Button(new Rect(right,y+379,70,28),"이전",size:12))userMapsPage--;
                GUI.enabled=CanEditMaps&&(userMapsPage+1)*3<localUserMaps.Length;
                if(BellUI.Button(new Rect(right+370,y+379,70,28),"다음",size:12))userMapsPage++;
                GUI.enabled=true;
                BellUI.Text(new Rect(right+80,y+379,280,28),(userMapsPage+1)+" / "+Math.Max(1,(localUserMaps.Length+2)/3),12,BellUI.Muted,false,TextAnchor.MiddleCenter);
            }else{
                GUI.enabled=true;
                BellUI.Text(new Rect(right,y+214,440,90),IsUserMapSelected?"같은 맵이 없으면 자동으로 다운로드해요.\n받은 맵은 보관되어 나도 방을 열 수 있어요.\n원본은 그대로 보관 · 수정하면 혼자 완주 필요":"방장이 만든 맵이나 받은 맵을 선택할 수 있어요.",15,BellUI.Muted);
                if(selectedUserMap!=null)BellUI.Text(new Rect(right,y+312,440,86),selectedUserMap.description,14,BellUI.Paper);
            }
            GUI.enabled=true;
            BellUI.Text(new Rect(right,y+412,440,27),UserMapTransferStatus,12,UserMapReady?BellUI.Cool:BellUI.Gold);
        }
    }
}
