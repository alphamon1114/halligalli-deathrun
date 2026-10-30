using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HalliGalli.Movement
{
    public enum NetworkAction { Reveal, Interact, Punch, Kick, Restart, Ready, Respawn, LassoThrow, LassoPull, UseItem }

    // Lobby/Relay supplies discovery and encrypted transport; this host owns all game decisions.
    [DefaultExecutionOrder(150)]
    public sealed partial class CourseNetwork : MonoBehaviour
    {
        public const int Protocol=28;
        public static double WorldTime=>Active&&Instance.manager&&Instance.manager.IsListening?Instance.manager.ServerTime.Time:Time.fixedTimeAsDouble;
        public const int MaxParticipants=6;
        const int capacity=MaxParticipants;
        public static CourseNetwork Instance {get;private set;}
        public static bool Active=>Instance&&Instance.online;
        public static bool Authority=>!Active||Instance.manager&&Instance.manager.IsServer;
        public bool Playing{get;private set;}
        public bool MenuVisible=>menu||!Playing;
        public string Status{get;private set;}="";
        public string InviteCode{get;private set;}="";
        public bool Busy{get;private set;}
        public int LocalSeat=>members.FindIndex(m=>!m.bot&&manager&&m.id==manager.LocalClientId);
        public CourseSession Game=>game;
        public IReadOnlyList<Member> Members=>members;
        [Serializable] public sealed class Member {public ulong id;public string name;public bool ready,bot;}
        public int BotCount=>members.Count(m=>m.bot);
        public int HumanCount=>members.Count(m=>!m.bot);
        public int Capacity=>capacity;
        int OccupiedSeats=>BotCount+Mathf.Max(HumanCount,approved.Count);
        public bool CanEditBots=>online&&IsHost&&!Busy&&!leaving&&!Playing&&!loading;
        public bool CanAddBot=>CanEditBots&&OccupiedSeats<capacity;
        int botSequence;
        [Serializable] sealed class Room {public Member[] members;public string map,code;public int capacity,epoch,mapRevision;public bool loading,playing;public UserMapOffer userMap;}
        [Serializable] sealed class Hello {public int protocol;public string name;}
        [Serializable] sealed class Load {public int epoch,mapRevision;public string map,mapHash;}
        [Serializable] sealed class ActionMessage {public int epoch,mapRevision,respawn,toolSequence,raceRound,itemSequence;public NetworkAction action;public Vector3 aim;}
        [Serializable] sealed class Signal {public int epoch;}

        NetworkManager manager;
        UnityTransport transport;
        ISession cloud;
        CourseSession game;
        readonly List<Member> members=new List<Member>();
        readonly HashSet<ulong> loaded=new HashSet<ulong>();
        readonly Dictionary<ulong,string> approved=new Dictionary<ulong,string>();
        readonly Dictionary<ulong,float> actionTimes=new Dictionary<ulong,float>();
        bool online,menu,bound,loading,leaving;
        int epoch,operation;
        float loadDeadline,connectDeadline;
        string nickname="Player",joinCode="",mapId="first-steps";
        public string MapId=>mapId;
        public bool IsHost=>manager&&manager.IsServer;
        public bool IsLoading=>loading;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatic(){Instance=null;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if(!Instance&&FindAnyObjectByType<CourseSession>())new GameObject("Online rooms").AddComponent<CourseNetwork>();
        }
        void Awake()
        {
            if(Instance&&Instance!=this){Destroy(gameObject);return;}
            Instance=this;DontDestroyOnLoad(gameObject);
            nickname=PlayerPrefs.GetString("HalliGalli.Nickname","Player");
            SceneManager.sceneLoaded+=SceneLoaded;game=FindAnyObjectByType<CourseSession>();
        }
        void Start()
        {
#if UNITY_EDITOR
            // Finite editor validation jobs opt out; normal editor Play uses the same home as a build.
            if(UnityEditor.SessionState.GetBool("HalliGalli.SkipStartScreen",false)){
                UnityEditor.SessionState.EraseBool("HalliGalli.SkipStartScreen");return;
            }
#endif
            if(Argument("--net-probe")!=null)return;
            ShowStartScreen();
        }
        void OnDestroy(){SceneManager.sceneLoaded-=SceneLoaded;if(Instance==this){ResetUserMapState();Instance=null;}}
        void SceneLoaded(Scene scene,LoadSceneMode mode)
        {
            game=FindAnyObjectByType<CourseSession>();bound=false;
            if(online&&loading)StartCoroutine(BindAfterStart());
            else if(!online&&menu)StartCoroutine(FreezeOfflineAfterStart());
        }
        IEnumerator FreezeOfflineAfterStart(){yield return null;if(!online&&menu&&!UserMapStudio.IsOpen)FreezeWorld();}
        void EnsureManager()
        {
            if(manager)return;
            var root=new GameObject("HalliGalli transport");DontDestroyOnLoad(root);
            transport=root.AddComponent<UnityTransport>();
            manager=root.AddComponent<NetworkManager>();
            manager.NetworkConfig=new NetworkConfig{NetworkTransport=transport,EnableSceneManagement=false,
                ConnectionApproval=true,PlayerPrefab=null,TickRate=30,ClientConnectionBufferTimeout=15};
            manager.ConnectionApprovalCallback=Approve;
            manager.OnClientStarted+=RegisterMessages;
            manager.OnClientConnectedCallback+=Connected;
            manager.OnClientDisconnectCallback+=Disconnected;
            manager.OnTransportFailure+=()=>{if(online&&!leaving)_=Leave("연결이 끊어졌어. 다시 접속해줘.");};
        }
        void Approve(NetworkManager.ConnectionApprovalRequest request,NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject=false;
            string reason=null;Hello hello=null;
            try{if(request.Payload.Length>256)throw new Exception();hello=JsonUtility.FromJson<Hello>(Encoding.UTF8.GetString(request.Payload));}
            catch{reason="잘못된 접속 요청";}
            if(hello==null||hello.protocol!=Protocol)reason="게임 버전이 달라. 같은 빌드를 사용해줘.";
            else if(Playing||loading)reason="이미 게임 중인 방이야. 대기방으로 돌아온 뒤 입장해줘.";
            else if(OccupiedSeats>=capacity&&!approved.ContainsKey(request.ClientNetworkId))reason="방이 가득 찼어. 방장에게 AI를 빼거나 빈자리를 만들어 달라고 해줘.";
            response.Approved=reason==null;response.Reason=reason??"";response.Pending=false;
            if(response.Approved)approved[request.ClientNetworkId]=CleanName(hello.name);
        }
        static string CleanName(string value)
        {
            var text=new string((value??"").Where(c=>!char.IsControl(c)&&c!='<'&&c!='>').Take(16).ToArray()).Trim();
            return text.Length==0?"Player":text;
        }
        public string LocalNickname=>CleanName(nickname);
        public string PlayerName(int seat)=>seat>=0&&seat<members.Count?members[seat].name:"플레이어";
        public string SeatName(int seat)=>PlayerName(seat)+(seat>=0&&seat==LocalSeat?" (나)":"");
        public bool AddBot()
        {
            if(!CanAddBot)return false;
            int number=1;while(members.Any(m=>m.bot&&m.name=="AI "+number))number++;
            members.Add(new Member{id=ulong.MaxValue-(ulong)++botSequence,name="AI "+number,ready=true,bot=true});
            Status="";BroadcastRoom();return true;
        }
        public bool RemoveBot(ulong id)
        {
            if(!CanEditBots)return false;
            int index=members.FindIndex(m=>m.bot&&m.id==id);if(index<0)return false;
            members.RemoveAt(index);Status="";BroadcastRoom();return true;
        }
        async Task Authenticate()
        {
            if(!Guid.TryParse(Application.cloudProjectId,out _))throw new InvalidOperationException("먼저 Project Settings → Services에서 Unity Cloud 프로젝트를 연결해줘.");
            if(UnityServices.State!=ServicesInitializationState.Initialized){
                var options=new InitializationOptions();
                string profile=Argument("--auth-profile")??(Application.isEditor?"editor":"player");options.SetProfile(profile);
                await UnityServices.InitializeAsync(options);
            }
            if(!AuthenticationService.Instance.IsSignedIn)await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        public async Task Connect(bool host,string code=null)
        {
            if(Busy||online||leaving||UserMapStudio.IsOpen)return;
            Busy=true;menu=true;Status=host?"방 만드는 중…":"초대 코드로 접속 중…";
            int token=++operation;
            try{
                await Authenticate();
                if(token!=operation)return;
                PrepareConnection();
                var result=host
                    ?await MultiplayerService.Instance.CreateSessionAsync(new SessionOptions{Name=CleanName(nickname)+"의 방",MaxPlayers=capacity,IsPrivate=true}.WithRelayNetwork())
                    :await MultiplayerService.Instance.JoinSessionByCodeAsync((code??"").Trim().ToUpperInvariant());
                if(token!=operation){await result.LeaveAsync();return;}
                cloud=result;InviteCode=result.Code;
                cloud.Deleted+=CloudEnded;cloud.RemovedFromSession+=CloudEnded;
                cloud.SessionHostChanged+=HostChanged;
                Status="친구에게 초대 코드를 보내줘.";
                if(IsHost)BroadcastRoom();
            }catch(Exception e){
                Debug.LogWarning("[Online] "+e.GetType().Name+": "+e.Message);
                await Leave(FriendlyError(e));
            }finally{if(token==operation)Busy=false;}
        }
        void CloudEnded(){if(online&&!leaving)_=Leave("방이 종료됐어.");}
        void HostChanged(string id){if(online&&!leaving)_=Leave("방장이 나가서 방이 종료됐어.");}
        static string FriendlyError(Exception e)
        {
            if(e is InvalidOperationException)return e.Message;
            string message=e.Message.ToLowerInvariant();
            if(message.Contains("full"))return "방이 가득 찼어.";
            if(message.Contains("not found")||message.Contains("404")||message.Contains("code"))return "초대 코드를 확인해줘. 종료된 방에는 들어갈 수 없어.";
            if(message.Contains("locked"))return "지금 게임 중인 방이야.";
            return "접속하지 못했어. 인터넷 연결과 Unity Dashboard의 Authentication·Lobby·Relay 설정을 확인해줘.";
        }
        void PrepareConnection()
        {
            EnsureManager();members.Clear();approved.Clear();loaded.Clear();actionTimes.Clear();botSequence=0;ResetSimulation();
            ResetUserMapState();mapRevision=epoch=0;mapId="first-steps";
            manager.NetworkConfig.ConnectionData=Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Hello{protocol=Protocol,name=CleanName(nickname)}));
            online=true;Playing=false;loading=false;bound=false;menu=true;connectDeadline=Time.realtimeSinceStartup+35;
            FreezeWorld();
            // Diagnostic clients use temporary names; keep the player's saved nickname intact.
            if(Argument("--net-probe")==null){PlayerPrefs.SetString("HalliGalli.Nickname",CleanName(nickname));PlayerPrefs.Save();}
        }
        // A separate, explicit diagnostic entry point. Production invite codes always use Unity Relay.
        public void StartLoopback(bool host,ushort port=7777,string name="Test")
        {
            if(online||Busy||UserMapStudio.IsOpen)return;
            nickname=name;PrepareConnection();
            transport.SetConnectionData("127.0.0.1",port,"127.0.0.1");
            bool ok=host?manager.StartHost():manager.StartClient();
            if(!ok)_=Leave("로컬 연결 시작 실패");
        }
        void RegisterMessages()
        {
            var messages=manager.CustomMessagingManager;
            messages.RegisterNamedMessageHandler("HG.room",(id,r)=>ReadJson<Room>(id,r,ReceiveRoom));
            messages.RegisterNamedMessageHandler("HG.load",(id,r)=>ReadJson<Load>(id,r,ReceiveLoad));
            messages.RegisterNamedMessageHandler("HG.loaded",(id,r)=>ReadJson<Signal>(id,r,ReceiveLoaded));
            messages.RegisterNamedMessageHandler("HG.begin",(id,r)=>ReadJson<Signal>(id,r,ReceiveBegin));
            messages.RegisterNamedMessageHandler("HG.action",(id,r)=>ReadJson<ActionMessage>(id,r,ReceiveAction));
            messages.RegisterNamedMessageHandler("HG.cards",(id,r)=>ReadJson<CardsMessage>(id,r,ReceiveCards));
            messages.RegisterNamedMessageHandler("HG.hit",(id,r)=>ReadJson<HitMessage>(id,r,ReceiveHit));
            messages.RegisterNamedMessageHandler("HG.lassoSound",(id,r)=>ReadJson<LassoSoundMessage>(id,r,ReceiveLassoSound));
            messages.RegisterNamedMessageHandler("HG.input",ReceiveInput);
            messages.RegisterNamedMessageHandler("HG.pose",ReceivePose);
            messages.RegisterNamedMessageHandler("HG.race",ReceiveRace);
            RegisterUserMapMessages();
        }
        void Connected(ulong id)
        {
            connectDeadline=0;
            if(!IsHost)return;
            if(!members.Any(m=>m.id==id))members.Add(new Member{id=id,name=approved.TryGetValue(id,out var name)?name:"Player",ready=id==NetworkManager.ServerClientId});
            BroadcastRoom();
            QueueUserMap(id);
        }
        void Disconnected(ulong id)
        {
            if(!online||leaving)return;
            if(!IsHost){_=Leave(string.IsNullOrEmpty(manager.DisconnectReason)?"방장과의 연결이 끊어졌어.":manager.DisconnectReason);return;}
            approved.Remove(id);members.RemoveAll(m=>m.id==id);inputs.Remove(id);actionTimes.Remove(id);
            userMapSends.Remove(id);
            if(Playing||loading){Playing=loading=false;bound=false;ResetSimulation();foreach(var m in members)m.ready=m.bot||m.id==0;FreezeWorld();Status="참가자가 나갔어. 대기방에서 다시 시작해줘.";}
            BroadcastRoom();
        }
        void BroadcastRoom()
        {
            if(!IsHost)return;
            SendAll("HG.room",new Room{members=members.ToArray(),capacity=capacity,epoch=epoch,map=mapId,code=InviteCode,loading=loading,playing=Playing,mapRevision=mapRevision,userMap=userMapOffer});
        }
        void ReceiveRoom(ulong sender,Room room)
        {
            if(!online||sender!=NetworkManager.ServerClientId||IsHost||room.members==null||room.members.Length>capacity||room.epoch<epoch)return;
            if(!ReceiveMapSelection(room))return;
            members.Clear();members.AddRange(room.members);InviteCode=room.code;
            if(!room.playing&&!room.loading){epoch=room.epoch;Playing=loading=false;menu=true;bound=false;ResetSimulation();FreezeWorld();}
        }
        public bool CanStart=>online&&IsHost&&!Busy&&!leaving&&!loading&&!Playing&&members.Count>=2&&members.Count<=capacity&&approved.Count==HumanCount&&members.All(m=>m.bot||m.ready)&&AllUserMapsReady;
        public void SelectMap(string id)
        {
            if(!CanEditMaps||!game.maps.catalog.maps.Any(m=>m&&m.id==id&&game.maps.CanSelect(m)))return;
            if(mapId==id&&!IsUserMapSelected)return;
            ResetUserMapState();mapRevision++;mapId=id;ResetMapReadiness();Status="";BroadcastRoom();
        }
        public void StartMatch()
        {
            if(!CanStart)return;
            loading=true;Playing=false;loaded.Clear();epoch++;sentRevision=-1;menu=true;
            loadDeadline=Time.realtimeSinceStartup+45;
            BroadcastRoom();
            var message=new Load{epoch=epoch,map=mapId,mapRevision=mapRevision,mapHash=SelectedUserMapHash};
            SendAll("HG.load",message);LoadMap(message);
        }
        void ReceiveLoad(ulong sender,Load message)
        {
            if(!online||sender!=NetworkManager.ServerClientId||IsHost||message.epoch<=epoch)return;
            if(message.mapRevision!=mapRevision||message.map!=mapId||message.mapHash!=SelectedUserMapHash){_=Leave("선택한 맵 정보가 달라. 방에 다시 참가해줘.");return;}
            epoch=message.epoch;LoadMap(message);
        }
        void LoadMap(Load message)
        {
            string scenePath;
            if(IsUserMapSelected){
                if(!UserMapReady||message.map!=mapId||message.mapRevision!=mapRevision||message.mapHash!=SelectedUserMapHash){_=Leave("사용자 맵 다운로드가 완료되지 않았어. 방에 다시 참가해줘.");return;}
                if(!Application.CanStreamedLevelBeLoaded(UserMapRuntime.ScenePath)){_=Leave("사용자 맵 템플릿이 빌드에 없어. 최신 빌드를 사용해줘.");return;}
                UserMapRuntime.Pending=UserMapStorage.Clone(selectedUserMap);scenePath=UserMapRuntime.ScenePath;
            }else{
                UserMapRuntime.Pending=null;
                var map=game.maps.catalog.maps.FirstOrDefault(m=>m&&m.id==message.map&&m.playable);
                if(!map||!game.maps.CanSelect(map)){_=Leave("필요한 맵이 빌드에 없어. 같은 최신 빌드를 사용해줘.");return;}
                scenePath=map.scenePath;
            }
            mapId=message.map;loading=true;Playing=false;bound=false;ResetSimulation();menu=true;FreezeWorld();
            SceneManager.LoadSceneAsync(scenePath);
        }
        IEnumerator BindAfterStart()
        {
            yield return null;
            if(!online||!loading||!game)yield break;
            int seat=LocalSeat;
            if(seat<0||members.Count<2){_=Leave("참가자 정보를 받지 못했어.");yield break;}
            var all=game.roster.allParticipants;
            int old=Array.FindIndex(all,p=>p.localPlayer);
            if(old!=seat){var other=all[seat];all[seat]=all[old];all[old]=other;}
            game.roster.ApplyLayout(members.Count);game.ResetPractice();
            game.cardGame.botEnabled=false;
            foreach(var p in all){
                var rival=p.GetComponent<CourseRival>();if(rival)rival.enabled=false;
                p.Combat.simulateAutomatically=IsHost;p.Combat.readInput=p.localPlayer;
            }
            for(int i=0;i<members.Count;i++){
                var text=game.roster.cardLabels[i].GetComponent<TextMesh>();if(text)text.text=SeatName(i);
                text=game.roster.startLabels[i].GetComponent<TextMesh>();if(text)text.text=(i+1).ToString();
            }
            bound=true;FreezeWorld();
            if(IsHost)ReceiveLoaded(0,new Signal{epoch=epoch});
            else Send("HG.loaded",new Signal{epoch=epoch},0);
        }
        void ReceiveLoaded(ulong sender,Signal signal)
        {
            if(!IsHost||!loading||signal.epoch!=epoch||!members.Any(m=>!m.bot&&m.id==sender))return;
            loaded.Add(sender);
            if(members.All(m=>m.bot||loaded.Contains(m.id))){
                Playing=true;loading=false;menu=false;loadDeadline=0;SendAll("HG.begin",new Signal{epoch=epoch});
                BroadcastCards();BroadcastRoom();EnablePlay();Status="";
            }
        }
        void ReceiveBegin(ulong sender,Signal signal)
        {
            if(sender!=0||IsHost||signal.epoch!=epoch||!bound)return;
            Playing=true;loading=false;menu=false;EnablePlay();Status="";
        }
        void FreezeWorld()
        {
            if(!game)return;
            game.cardGame.botEnabled=false;
            foreach(var p in game.roster.allParticipants){var ai=p.GetComponent<CourseRival>();if(ai)ai.enabled=false;p.Combat.simulateAutomatically=false;if(p.Lasso)p.Lasso.CancelConnection();}
            var controller=game.Local.GetComponent<SourcePlayerController>();
            controller.controlsEnabled=false;controller.suppressCursorCapture=true;controller.SetCursorCaptured(false);
            var hud=game.GetComponent<CourseHUD>();if(hud)hud.CloseMapMenu();
        }
        void EnablePlay()
        {
            if(!game)return;
            var controller=game.Local.GetComponent<SourcePlayerController>();controller.controlsEnabled=true;controller.suppressCursorCapture=false;
            if(!Application.isBatchMode)controller.SetCursorCaptured(true);
            game.cardGame.botEnabled=IsHost&&BotCount>0;
            for(int i=0;i<game.roster.allParticipants.Length;i++){
                var p=game.roster.allParticipants[i];var rival=p.GetComponent<CourseRival>();
                // Only the authoritative host runs AI. Human seats never acquire a bot controller.
                bool bot=IsHost&&i<members.Count&&members[i].bot;
                if(rival)rival.enabled=bot;
                p.Combat.simulateAutomatically=IsHost;p.Combat.networkAim=null;
            }
        }
        public async Task Leave(string reason="방에서 나왔어.")
        {
            if(leaving)return;
            leaving=true;++operation;online=false;Playing=loading=bound=false;Busy=false;menu=true;entryScreen=EntryScreen.Home;
            string returnScene=game?game.gameObject.scene.path:null;
            if(IsUserMapSelected||returnScene==UserMapRuntime.ScenePath) returnScene=OfficialHomeScene();
            ResetUserMapState();mapRevision=0;mapId="first-steps";
            var old=cloud;cloud=null;
            if(old!=null){old.Deleted-=CloudEnded;old.RemovedFromSession-=CloudEnded;old.SessionHostChanged-=HostChanged;}
            try{if(old!=null){if(old.IsHost)await old.AsHost().DeleteAsync();else await old.LeaveAsync();}}
            catch(Exception e){Debug.LogWarning("[Online] Room cleanup: "+e.GetType().Name);}
            if(manager){
                if(manager.IsListening)manager.Shutdown();
                // Transport jobs finish at the end of an engine frame. Never destroy their owner in a disconnect callback.
                while(manager&&manager.ShutdownInProgress)await Task.Yield();
                await Task.Yield();
                if(manager)Destroy(manager.gameObject);
                manager=null;transport=null;
                await Task.Yield();
            }
            members.Clear();approved.Clear();InviteCode="";Status=reason;ResetSimulation();
            if(!string.IsNullOrEmpty(returnScene))_=SceneManager.LoadSceneAsync(returnScene);
            leaving=false;
        }
        void Update()
        {
            if(!game)game=FindAnyObjectByType<CourseSession>();
            if(!online){
                if(menu&&game){
                    var controller=game.Local.GetComponent<SourcePlayerController>();controller.suppressCursorCapture=true;
                    if(!ControlSettings.BlocksInput&&!Busy&&!leaving&&entryScreen!=EntryScreen.Home&&Input.GetKeyDown(KeyCode.Escape))ShowStartScreen();
                }
                return;
            }
            if(connectDeadline>0&&Time.realtimeSinceStartup>connectDeadline){connectDeadline=0;_=Leave("접속 시간이 초과됐어.");return;}
            if(loading&&IsHost&&loadDeadline>0&&Time.realtimeSinceStartup>loadDeadline){_=Leave("맵을 불러오지 못한 참가자가 있어. 방을 다시 만들어줘.");return;}
            UpdateUserMapTransfer();
            if(!ControlSettings.BlocksInput&&Playing&&Input.GetKeyDown(KeyCode.Escape)){menu=true;game.Local.GetComponent<SourcePlayerController>().SetCursorCaptured(false);}
            if(IsHost)UpdateVictoryReturn();
            if(Playing&&IsHost&&bound&&game.cardGame.Match.Revision!=sentRevision)BroadcastCards();
            if(menu&&game){var c=game.Local.GetComponent<SourcePlayerController>();c.suppressCursorCapture=true;}
        }
        static string Argument(string key){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i>=0&&i+1<a.Length?a[i+1]:null;}
        void Send<T>(string name,T value,ulong to)
        {
            if(!manager||!manager.IsListening)return;
            byte[] bytes=Encoding.UTF8.GetBytes(JsonUtility.ToJson(value));
            using(var writer=new FastBufferWriter(bytes.Length+4,Allocator.Temp)){
                writer.WriteValueSafe(bytes.Length);writer.WriteBytesSafe(bytes);
                manager.CustomMessagingManager.SendNamedMessage(name,to,writer,NetworkDelivery.ReliableFragmentedSequenced);
            }
        }
        void SendAll<T>(string name,T value){foreach(var m in members)if(!m.bot&&m.id!=manager.LocalClientId)Send(name,value,m.id);}
        void ReadJson<T>(ulong sender,FastBufferReader reader,Action<ulong,T> action)
        {
            try{
                reader.ReadValueSafe(out int size);if(size<0||size>16384||!reader.TryBeginRead(size))return;
                byte[] data=new byte[size];reader.ReadBytesSafe(ref data,size);
                var message=JsonUtility.FromJson<T>(Encoding.UTF8.GetString(data));if(message!=null)action(sender,message);
            }catch(Exception e){Debug.LogWarning("[Online] Dropped invalid "+typeof(T).Name+": "+e.GetType().Name);}
        }
    }
}
