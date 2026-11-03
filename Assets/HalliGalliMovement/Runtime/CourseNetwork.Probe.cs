using System;
using System.IO;
using System.Linq;
using HalliGalli.Cards;
using UnityEngine;

namespace HalliGalli.Movement
{
    // Invoked only by explicit test command-line arguments. Never accepts commands from a remote peer.
    public sealed class CourseNetworkProbe : MonoBehaviour
    {
        [Serializable] sealed class Command
        {
            public int sequence,seat;
            public string action,value;
            public Vector3 position;
            public float x,z,yaw,pitch;
            public bool jump,crouch,primary,aim,kick;
        }
        [Serializable] sealed class Actor
        {
            public Vector3 position,velocity;
            public bool seated,respawning,grounded,crouched,aiming,aiEnabled,localPlayer;
            public int falls,respawns,hits,attacks,attackHits,checkpoint,gates,ammo,shots,stunHits;
            public float yaw,viewYaw,viewRoll,checkpointSide,stun,immunity,stagger,raceLaunch,pushProtection,attackElapsed,kickCooldown;
            public string attack;
            public int shirtColor,shirtRenderers;
            public Color shirtTint;
            public Vector3 lastPush;
            public int lassoCasts,lassoHits,lassoPulls,lassoTarget;
            public string lassoPhase;public float lassoCooldown;
            public int ropeThrowSounds,ropeLatchSounds,ropePullSounds;
        }
        [Serializable] sealed class BuildPreviewReport
        {
            public bool valid;
            public Vector3 position,rotation,scale;
            public string placedId;
        }
        [Serializable] sealed class CheckpointInfo {public Vector3 position;public float yaw;public int gates;}
        [Serializable] sealed class PartVisualReport
        {
            public string id,kind;
            public int renderers,colliders;
            public string[] materials,shaders;
            public bool supported;
        }
        [Serializable] sealed class VisualReport
        {
            public string map;
            public int checkpointCount;
            public string[] unsupportedMaterials,legacyFloorFrames;
            public PartVisualReport[] parts;
        }
        [Serializable] sealed class ClockworkTrapInfo
        {
            public string kind,stage;
            public Vector3 position,visual,forward;
            public Vector3[] hitPoints;
            public float cycle,period,phase,warningSeconds,attackSeconds;
            public int hits;
        }
        [Serializable] sealed class ClockworkPressInfo
        {
            public Vector3 position,visual,forward;
            public float period,phase,cycle;
        }
        [Serializable] sealed class ClockworkFerryInfo
        {
            public Vector3 position,origin,travel,waiting,boarding,arrival;
            public float period,phase,standingOffset;
        }
        [Serializable] sealed class ClockworkReport
        {
            public ClockworkTrapInfo[] traps;
            public ClockworkPressInfo[] presses;
            public ClockworkFerryInfo[] ferries;
        }
        [Serializable] sealed class Report
        {
            public string role,status,code,map,cardState,error;
            public string winnerNickname,victoryAction;
            public bool victoryVisible,canContinueAfterVictory;
            public bool victoryReturnPending;
            public float victoryReturnRemaining;
            public int victorySoundsPlayed;
            public bool victoryAudioReady,victorySoundPlaying;
            public bool active,playing,loading,host,canStart,busy;
            public int localSeat,bells,revision,turn,bots,capacity,surfSections,twoSidedSurfSections;
            public bool canAddBot,botCards;
            public int protocol,selectedShirtColor,savedShirtColor;
            public int raceAimTarget=-1;
            public bool shirtSelectionAccepted,shirtSelectionLoading;
            public string shirtPreferenceKey;
            public bool[] selectableShirtColors;
            public CourseNetwork.Member[] members;
            public Actor[] actors;
            public CheckpointInfo[] checkpoints;
            public Vector3 bellLanding;
            public bool hasFerry;public Vector3 ferryPosition;public Vector3[] ferryPositions;
            public double worldTime;public Vector3[] factoryPlatforms,factoryPresses;
            public string selectedMap,userMapHash,userMapTransfer,loadedUserMapHash,studioDraftHash,studioDraftId;
            public bool userMapReady,studioOpen,studioTesting;
            public int userMapRevision,studioObjects;
            public string studioSelected,studioStatus;
            public bool studioBusy,studioDirty,studioConfirmation;
            public bool studioCleared;
            public string studioClearStatus;
            public int commandSequence;
            public bool userMapCacheHit;
            public bool studioCameraOrthographic,studioCameraAdvanced;
            public float studioCameraSize;
            public string studioCameraView;
            public Vector3 studioCameraPosition,studioCameraForward,studioCameraFocus;
            public bool studioPartsMenu;
            public float studioViewportWidth;
            public bool studioEasyBuild,studioEraseMode,studioOrbit,studioBuildValid;
            public int studioBuildSize;
            public Vector3 studioBuildPosition,studioBuildRotation,studioBuildScale;
            public bool studioFirstPerson,studioCaptured,studioNoclip,studioAir,studioAirGround,studioAirGuide;
            public int studioFlySpeed;
            public float studioAirDistance,studioAirHeight;
            public Vector3 studioAirBottom,studioAirReference;
            public bool studioPawnActive,studioPawnGrounded;
            public Vector3 studioPawnPosition,studioPawnVelocity;
            public RaceCourse.Snapshot race;
            public ClockworkReport clockwork;
        }
        string role,folder,error="";
        int lastCommand,lastRespawn=-1;float nextReport;
        CourseNetwork network;CourseSession game;
        MovementCommand movement;float pitch;bool pendingJump;
        bool studioUI;
        bool shirtSelectionAccepted,shirtSelectionLoading;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot(){GameReleaseGate.RunWhenAllowed(BootAfterReleaseCheck);}
        static void BootAfterReleaseCheck()
        {
            if(Arg("--net-probe")!=null){var go=new GameObject("Network test driver");DontDestroyOnLoad(go);go.AddComponent<CourseNetworkProbe>();}
        }
        static string Arg(string key){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
        async void Start()
        {
            role=Arg("--net-probe");folder=Arg("--probe-dir");if(string.IsNullOrEmpty(folder)){enabled=false;return;}
            studioUI=Arg("--studio-ui")=="true";
            Application.targetFrameRate=60;
            Directory.CreateDirectory(folder);
            await System.Threading.Tasks.Task.Yield();
            network=CourseNetwork.Instance;
            if(!network){error="Network runtime missing";return;}
            try{
                if(Arg("--relay-probe")=="true"){
                    if(role=="host")await network.Connect(true);
                    // A Relay client waits for its join command, after the real code has been created.
                }else if(role!="manual"&&Arg("--probe-manual")!="true"){
                    ushort port=ushort.TryParse(Arg("--net-port"),out var parsed)?parsed:(ushort)7777;
                    network.StartLoopback(role=="host",port,role);
                }
            }catch(Exception e){error=e.ToString();}
        }
        void Update()
        {
            if(!network||string.IsNullOrEmpty(folder))return;
            if(network.Game!=game){game=network.Game;if(game&&!studioUI){game.Local.GetComponent<SourcePlayerController>().enabled=false;game.cardGame.ReadInput=false;}}
            // The probe supplies pointer state explicitly; live-device focus checks must not cancel it.
            if(game&&!studioUI)game.Local.Combat.readInput=false;
            if(game&&lastRespawn!=game.Local.RespawnSequence){
                lastRespawn=game.Local.RespawnSequence;
                if(!game.Local.IsSeated){movement=new MovementCommand{yaw=game.Local.GetComponent<SourcePlayerController>().AimRotation.eulerAngles.y};pendingJump=false;}
            }
            string path=Path.Combine(folder,"command-"+role+".json");
            try{
                if(File.Exists(path)){
                    var command=JsonUtility.FromJson<Command>(File.ReadAllText(path));
                    if(command!=null&&command.sequence>lastCommand){lastCommand=command.sequence;Execute(command);}
                }
            }catch(IOException){ }
            catch(Exception e){error=e.ToString();}
            if(Time.realtimeSinceStartup<nextReport)return;nextReport=Time.realtimeSinceStartup+.1f;
            var match=game?game.cardGame.Match:null;
            var report=new Report{hasFerry=game&&game.ferry,ferryPosition=game&&game.ferry?game.ferry.carrier.transform.position:Vector3.zero,ferryPositions=game&&game.ferry?new[]{game.ferry.carrier,game.ferry.secondCarrier}.Where(s=>s).Select(s=>s.transform.position).ToArray():Array.Empty<Vector3>(),worldTime=CourseNetwork.WorldTime,factoryPlatforms=game&&game.factory?game.factory.movingPlatforms.Select(s=>s.transform.position).ToArray():Array.Empty<Vector3>(),factoryPresses=game&&game.factory?game.factory.presses.Select(p=>p.head.position).ToArray():Array.Empty<Vector3>(),role=role,status=network.Status,code=network.InviteCode,map=game&&game.maps.current?game.maps.current.id:"",
                active=CourseNetwork.Active,playing=network.Playing,loading=network.IsLoading,host=network.IsHost,canStart=network.CanStart,busy=network.Busy,
                localSeat=network.LocalSeat,bells=game?game.BellCount:0,revision=match?.Revision??-1,turn=match?.Turn??-1,bots=network.BotCount,capacity=network.Capacity,canAddBot=network.CanAddBot,botCards=game&&game.cardGame.botEnabled,
                protocol=CourseNetwork.Protocol,selectedShirtColor=network.SelectedShirtColor,savedShirtColor=PlayerShirtColors.SavedIndex,
                shirtSelectionAccepted=shirtSelectionAccepted,shirtSelectionLoading=shirtSelectionLoading,shirtPreferenceKey=PlayerShirtColors.PreferenceKey,
                selectableShirtColors=Enumerable.Range(0,PlayerShirtColors.Count).Select(network.CanSelectShirtColor).ToArray(),
                selectedMap=network.MapId,userMapHash=network.SelectedUserMapHash,userMapReady=network.UserMapReady,
                userMapTransfer=network.UserMapTransferStatus,userMapRevision=network.SelectedUserMapRevision,
                userMapCacheHit=network.UserMapCacheHit,
                loadedUserMapHash=UserMapRuntime.Current&&UserMapRuntime.Current.Data!=null?UserMapStorage.Hash(JsonUtility.ToJson(UserMapRuntime.Current.Data)):"",
                studioOpen=UserMapStudio.IsOpen,studioTesting=UserMapStudio.IsTesting,
                studioDraftId=UserMapStudio.Current&&UserMapStudio.Current.Draft!=null?UserMapStudio.Current.Draft.id:"",
                studioDraftHash=UserMapStudio.Current&&UserMapStudio.Current.Draft!=null?UserMapStorage.Hash(JsonUtility.ToJson(UserMapStudio.Current.Draft)):"",
                studioObjects=UserMapStudio.Current&&UserMapStudio.Current.Draft!=null?UserMapStudio.Current.Draft.objects.Length:0,
                studioSelected=UserMapStudio.Current?UserMapStudio.Current.SelectedId:"",
                studioStatus=UserMapStudio.Current?UserMapStudio.Current.Status:"",
                studioBusy=UserMapStudio.Current&&UserMapStudio.Current.IsBusy,
                studioDirty=UserMapStudio.Current&&UserMapStudio.Current.HasUnsavedChanges,
                studioCleared=UserMapStudio.Current&&UserMapStudio.Current.IsClearVerified,
                studioClearStatus=UserMapStudio.Current?UserMapStudio.Current.ClearVerificationStatus:"",
                studioConfirmation=UserMapStudio.Current&&UserMapStudio.Current.HasPendingConfirmation,
                commandSequence=lastCommand,
                studioCameraOrthographic=UserMapStudio.Current&&UserMapStudio.Current.EditorCamera&&UserMapStudio.Current.EditorCamera.orthographic,
                studioCameraAdvanced=UserMapStudio.Current&&UserMapStudio.Current.AdvancedCamera,
                studioCameraSize=UserMapStudio.Current&&UserMapStudio.Current.EditorCamera?UserMapStudio.Current.EditorCamera.orthographicSize:0,
                studioCameraView=UserMapStudio.Current?UserMapStudio.Current.CurrentStudioView.ToString():"",
                studioCameraPosition=UserMapStudio.Current&&UserMapStudio.Current.EditorCamera?UserMapStudio.Current.EditorCamera.transform.position:Vector3.zero,
                studioCameraForward=UserMapStudio.Current&&UserMapStudio.Current.EditorCamera?UserMapStudio.Current.EditorCamera.transform.forward:Vector3.zero,
                studioCameraFocus=UserMapStudio.Current?UserMapStudio.Current.CameraFocus:Vector3.zero,
                studioPartsMenu=UserMapStudio.Current&&UserMapStudio.Current.IsPartsMenuOpen,
                studioViewportWidth=UserMapStudio.Current&&UserMapStudio.Current.EditorCamera?UserMapStudio.Current.EditorCamera.pixelWidth:0,
                studioEasyBuild=UserMapStudio.Current&&UserMapStudio.Current.EasyBuild,
                studioEraseMode=UserMapStudio.Current&&UserMapStudio.Current.EraseMode,
                studioOrbit=UserMapStudio.Current&&UserMapStudio.Current.IsOrbitView,
                studioBuildSize=UserMapStudio.Current?UserMapStudio.Current.BuildSizeIndex:0,
                studioBuildValid=UserMapStudio.Current&&UserMapStudio.Current.BuildPreviewValid,
                studioBuildPosition=UserMapStudio.Current?UserMapStudio.Current.BuildPreviewPosition:Vector3.zero,
                studioBuildRotation=UserMapStudio.Current?UserMapStudio.Current.BuildPreviewRotation:Vector3.zero,
                studioBuildScale=UserMapStudio.Current?UserMapStudio.Current.BuildPreviewScale:Vector3.zero,
                studioFirstPerson=UserMapStudio.Current&&UserMapStudio.Current.FirstPersonBuild,
                studioCaptured=UserMapStudio.Current&&UserMapStudio.Current.BuildCursorCaptured,
                studioNoclip=UserMapStudio.Current&&UserMapStudio.Current.BuildNoclip,
                studioAir=UserMapStudio.Current&&UserMapStudio.Current.AirBuild,
                studioFlySpeed=UserMapStudio.Current?UserMapStudio.Current.BuildFlySpeedIndex:0,
                studioAirDistance=UserMapStudio.Current?UserMapStudio.Current.AirBuildDistance:0,
                studioAirGround=UserMapStudio.Current&&UserMapStudio.Current.AirBuildGuideHasGround,
                studioAirGuide=UserMapStudio.Current&&UserMapStudio.Current.AirBuildGuideActive,
                studioAirHeight=UserMapStudio.Current?UserMapStudio.Current.AirBuildGuideHeight:0,
                studioAirBottom=UserMapStudio.Current?UserMapStudio.Current.AirBuildGuideBottom:Vector3.zero,
                studioAirReference=UserMapStudio.Current?UserMapStudio.Current.AirBuildGuideReferencePoint:Vector3.zero,
                studioPawnActive=UserMapStudio.Current&&UserMapStudio.Current.BuildPawnActive,
                studioPawnGrounded=UserMapStudio.Current&&UserMapStudio.Current.BuildPawnGrounded,
                studioPawnPosition=UserMapStudio.Current?UserMapStudio.Current.BuildPawnPosition:Vector3.zero,
                studioPawnVelocity=UserMapStudio.Current?UserMapStudio.Current.BuildPawnVelocity:Vector3.zero,
                cardState=match==null?"":JsonUtility.ToJson(match.ExportPublicState(game.cardGame.deck)),members=network.Members.ToArray(),
                actors=game?game.participants.Select(p=>{var shirt=p.GetComponent<PlayerShirtAppearance>();return new Actor{position=p.transform.position,velocity=p.Motor.Velocity,seated=p.IsSeated,
                    shirtColor=shirt?shirt.ColorIndex:-1,shirtRenderers=shirt?shirt.RendererCount:0,shirtTint=shirt?shirt.AppliedColor:Color.clear,
                    respawning=p.IsRespawning,grounded=p.Motor.Grounded,crouched=p.Motor.IsCrouched,falls=p.Falls,respawns=p.RespawnSequence,aiEnabled=p.GetComponent<CourseRival>()&&p.GetComponent<CourseRival>().enabled,localPlayer=p.localPlayer,
                    hits=p.Receiver.ReceivedHits,attacks=p.Combat.AttackSequence,attackHits=p.Combat.HitCount,pushProtection=p.Receiver.PushProtectionRemaining,
                    attack=p.Combat.CurrentAttack.ToString(),attackElapsed=p.Combat.Elapsed,kickCooldown=p.Combat.KickCooldownRemaining,lastPush=p.Receiver.LastImpulse,
                    stun=p.Motor.StunRemaining,immunity=p.Motor.StunImmuneRemaining,stagger=p.Motor.StaggerRemaining,raceLaunch=p.Motor.RaceLaunchRemaining,
                    lassoCasts=p.Lasso.Casts,lassoHits=p.Lasso.Hits,lassoPulls=p.Lasso.Pulls,lassoTarget=Array.IndexOf(game.participants,p.Lasso.Target),lassoPhase=p.Lasso.Phase.ToString(),lassoCooldown=p.Lasso.CooldownRemaining,
                    ropeThrowSounds=p.GetComponent<LassoAudio>().Played(LassoSound.Throw),ropeLatchSounds=p.GetComponent<LassoAudio>().Played(LassoSound.Latch),ropePullSounds=p.GetComponent<LassoAudio>().Played(LassoSound.Pull),
                    checkpoint=p.CheckpointIndex,checkpointSide=p.CheckpointSide,gates=game.roster.surf?game.roster.surf.GatesPassed(p):0,yaw=p.transform.eulerAngles.y,viewYaw=p.localPlayer?p.GetComponent<SourcePlayerController>().AimRotation.eulerAngles.y:p.transform.eulerAngles.y,viewRoll=p.localPlayer?p.GetComponent<SourcePlayerController>().playerCamera.transform.eulerAngles.z:0};}).ToArray():new Actor[0],error=error,
                checkpoints=game&&game.checkpoints?game.checkpoints.points.Select(p=>new CheckpointInfo{position=p.spawn.position,yaw=p.spawn.eulerAngles.y,gates=p.surfGates}).ToArray():new CheckpointInfo[0],
                surfSections=game&&game.roster.surf?game.roster.surf.sections.Length:0,
                twoSidedSurfSections=game&&game.roster.surf?game.roster.surf.sections.Count(s=>s.twoSided):0,
                bellLanding=game?game.roster.surf?game.roster.surf.finish.position:game.roster.branches?game.roster.branches.summit.position:game.roster.RouteForSeat(0).Last().position:Vector3.zero};
            var hud=game?FindAnyObjectByType<CourseHUD>():null;
            if(hud){report.winnerNickname=hud.WinnerNickname;report.victoryAction=hud.VictoryAction;report.victoryVisible=hud.VictoryVisible;report.canContinueAfterVictory=hud.CanContinueAfterVictory;}
            report.victoryReturnPending=network.VictoryReturnPending;report.victoryReturnRemaining=network.VictoryReturnRemaining;
            if(hud){report.victorySoundsPlayed=hud.VictorySoundsPlayed;report.victoryAudioReady=hud.VictoryAudioReady;report.victorySoundPlaying=hud.VictoryAudioSource&&hud.VictoryAudioSource.isPlaying;}
            if(RaceCourse.Active&&RaceCourse.Active.session==game)report.race=RaceCourse.Active.CaptureSnapshot();
            if(RaceCourse.Active&&RaceCourse.Active.session==game&&RaceCourse.Active.HeldItem(game.Local)==RaceItem.Missile){
                var target=RaceCourse.Active.LockTarget(game.Local,game.Local.GetComponent<SourcePlayerController>().AimRotation*Vector3.forward);
                report.raceAimTarget=target?Array.IndexOf(game.participants,target):-1;
            }
            if(game&&game.clockwork){
                double now=report.worldTime;
                report.clockwork=new ClockworkReport{traps=game.clockwork.traps.Select(trap=>{
                    var sample=trap.Sample(now);
                    return new ClockworkTrapInfo{kind=trap.kind.ToString(),stage=sample.stage.ToString(),cycle=sample.cycle,
                        position=trap.transform.position,visual=trap.movingRoot.position,forward=trap.transform.forward,period=trap.period,phase=trap.phase,
                        warningSeconds=trap.warningSeconds,attackSeconds=trap.attackSeconds,hits=trap.Hits,
                        hitPoints=trap.hitboxes.Select(box=>box.transform.TransformPoint(box.center)).ToArray()};
                }).ToArray(),
                    presses=game.clockwork.presses.Select(press=>new ClockworkPressInfo{
                        position=press.raised,visual=press.head.position,forward=press.transform.forward,
                        period=press.period,phase=press.phase,cycle=press.Cycle(now)}).ToArray(),
                    ferries=game.clockwork.ferries.Select(ferry=>new ClockworkFerryInfo{
                        position=ferry.carrier.transform.position,origin=ferry.carrier.origin,travel=ferry.carrier.travel,
                        waiting=ferry.waitingPoint.position,boarding=ferry.boardingDock.bounds.center,arrival=ferry.arrivalDock.bounds.center,
                        period=ferry.carrier.period,phase=ferry.carrier.phase,standingOffset=ferry.carrier.standingOffset}).ToArray()};
            }
            try{File.WriteAllText(Path.Combine(folder,"state-"+role+".json"),JsonUtility.ToJson(report));}catch(IOException){}
        }
        void FixedUpdate()
        {
            movement.jumpPressed=pendingJump;pendingJump=false;
            if(network&&network.Playing){
                network.LocalMovement(movement,pitch);
                if(!studioUI&&game&&!game.Local.IsSeated)game.Local.GetComponent<SourcePlayerController>().UpdateView();
            }
            else if(UserMapStudio.IsTesting&&!studioUI&&game&&!game.Local.IsSeated&&!game.Local.IsRespawning) {
                game.Local.Motor.Simulate(movement,Time.fixedDeltaTime);
                game.Local.GetComponent<SourcePlayerController>().UpdateView();
            }
        }
        void Execute(Command c)
        {
            switch(c.action){
                case "host-loopback":network.StartLoopback(true,ushort.Parse(c.value),"shared-map-host");break;
                case "join-loopback":network.StartLoopback(false,ushort.Parse(c.value),role);break;
                case "show-home":network.ShowStartScreen();break;
                case "training":network.StartTraining();break;
                case "shirt-color":shirtSelectionLoading=network.IsLoading;shirtSelectionAccepted=network.SelectShirtColor(int.Parse(c.value));break;
                case "start-shirt-color":
                    network.StartMatch();shirtSelectionLoading=network.IsLoading;shirtSelectionAccepted=network.SelectShirtColor(int.Parse(c.value));break;
                case "join":_=network.Connect(false,c.value);break;
                case "add-bot":network.AddBot();break;
                case "remove-bot":if(c.seat>=0&&c.seat<network.Members.Count)network.RemoveBot(network.Members[c.seat].id);break;
                case "ready":network.LocalAction(NetworkAction.Ready);break;
                case "start":network.StartMatch();break;
                case "map":network.SelectMap(c.value);break;
                case "user-map":
                    if(new FileInfo(c.value).Length>UserMapStorage.MaxBytes)throw new InvalidDataException("Map exceeds transfer limit");
                    network.SelectUserMap(UserMapStorage.Parse(File.ReadAllText(c.value)));break;
                case "studio-open":UserMapStudio.Open();break;
                case "studio-parts-menu":UserMapStudio.Current.SetPartsMenuOpen(c.value=="open");break;
                case "studio-choose-part":UserMapStudio.Current.SendMessage("ChoosePalette",(UserMapPart)Enum.Parse(typeof(UserMapPart),c.value));break;
                case "studio-view":UserMapStudio.Current.SetStudioView((UserMapStudio.StudioView)Enum.Parse(typeof(UserMapStudio.StudioView),c.value));break;
                case "studio-orbit":UserMapStudio.Current.OrbitCamera(new Vector2(c.x,c.z));break;
                case "studio-first-person":UserMapStudio.Current.SetFirstPersonBuild(c.value=="true");break;
                case "studio-capture":UserMapStudio.Current.SetBuildCursorCaptured(c.value=="true");break;
                case "studio-noclip":UserMapStudio.Current.SetBuildNoclip(c.value=="true");break;
                case "studio-fly-speed":UserMapStudio.Current.SetBuildFlySpeed(c.seat);break;
                case "studio-air":UserMapStudio.Current.SetAirBuild(c.value=="true");break;
                case "studio-air-distance":UserMapStudio.Current.AdjustAirBuildDistance(c.x);break;
                case "studio-build-navigate":UserMapStudio.Current.NavigateBuild(new Vector2(c.yaw,c.pitch),c.position,c.x,c.primary);break;
                case "studio-easy":UserMapStudio.Current.SetEasyBuild(c.value=="true");break;
                case "studio-build-size":UserMapStudio.Current.SetBuildSize(c.seat);break;
                case "studio-build-rotate":UserMapStudio.Current.RotateBuild90();break;
                case "studio-nudge":UserMapStudio.Current.NudgeSelectedBuild(c.seat,(int)c.x);break;
                case "studio-build-preview":
                    var builder=UserMapStudio.Current;
                    bool valid=builder.PreviewBuild(new Ray(c.position,Quaternion.Euler(c.pitch,c.yaw,0)*Vector3.forward));
                    var preview=new BuildPreviewReport{valid=valid,position=builder.BuildPreviewPosition,rotation=builder.BuildPreviewRotation,scale=builder.BuildPreviewScale};
                    if(c.primary)preview.placedId=builder.PlaceBuildPreview();
                    File.WriteAllText(Path.Combine(folder,"build-preview.json"),JsonUtility.ToJson(preview));break;
                case "studio-build-place":UserMapStudio.Current.PlaceBuildPreview();break;
                case "studio-erase":UserMapStudio.Current.SetEraseMode(c.value=="true");break;
                case "studio-erase-hit":UserMapStudio.Current.EraseAtRay(new Ray(c.position,Quaternion.Euler(c.pitch,c.yaw,0)*Vector3.forward));break;
                case "studio-read":File.WriteAllText(Path.Combine(folder,"studio-draft.json"),UserMapStorage.Serialize(UserMapStudio.Current.Draft));break;
                case "studio-click":
                    var studio=UserMapStudio.Current;
                    studio.SelectAtScreenPoint(studio.EditorCamera.WorldToScreenPoint(c.position),c.x);
                    break;
                case "studio-pan":UserMapStudio.Current.PanCamera(new Vector2(c.x,c.z));break;
                case "studio-zoom":UserMapStudio.Current.ZoomCamera(c.x);break;
                case "studio-advanced":UserMapStudio.Current.SetAdvancedCamera(c.value=="true");break;
                case "studio-focus":
                    if(c.value=="all")UserMapStudio.Current.FocusAll();
                    else if(c.value=="start")UserMapStudio.Current.FocusStart();
                    else if(c.value=="bell")UserMapStudio.Current.FocusBell();
                    else UserMapStudio.Current.FocusSelected();
                    break;
                case "studio-new":UserMapStudio.Current.NewMap();break;
                case "studio-save":UserMapStudio.Current.SaveMap();break;
                case "studio-import":UserMapStudio.Current.ImportMap(c.value);break;
                case "studio-export":UserMapStudio.Current.ExportMap(c.value);break;
                case "studio-test":UserMapStudio.Current.BeginTest();break;
                case "studio-edit":UserMapStudio.Current.EndTest();break;
                case "studio-close":UserMapStudio.Current.Close();break;
                case "studio-place":UserMapStudio.Current.PlaceObject((UserMapPart)Enum.Parse(typeof(UserMapPart),c.value),c.position);break;
                case "studio-select":UserMapStudio.Current.SelectObject(c.value);break;
                case "studio-update":UserMapStudio.Current.UpdateObject(JsonUtility.FromJson<UserMapObject>(c.value));break;
                case "studio-duplicate":UserMapStudio.Current.DuplicateSelected();break;
                case "studio-delete":UserMapStudio.Current.DeleteSelected();break;
                case "studio-undo":UserMapStudio.Current.Undo();break;
                case "studio-redo":UserMapStudio.Current.Redo();break;
                case "studio-confirm":UserMapStudio.Current.ConfirmPending(c.value=="save");break;
                case "studio-interact":FindAnyObjectByType<CourseHUD>().Interact();break;
                case "studio-respawn":game.Local.RequestRespawn();break;
                case "studio-reopen":UserMapStudio.Current.OpenMap(UserMapStorage.Load(c.value));break;
                case "room":network.ReturnToRoom();break;
                case "menu":network.ShowMenu();break;
                case "offline-map-menu":
                    if(CourseNetwork.Active)throw new InvalidOperationException("Offline map menu requires no active room.");
                    FindAnyObjectByType<CourseHUD>().OpenMapMenu();break;
                case "leave":_=network.Leave();break;
                case "action":network.LocalAction((NetworkAction)Enum.Parse(typeof(NetworkAction),c.value));break;
                case "lasso":game.Local.Lasso.TryUse(Quaternion.Euler(c.pitch,c.yaw,0)*Vector3.forward);break;
                case "race-item":FindAnyObjectByType<CourseHUD>().UseRaceItem();break;
                case "controls":ControlSettings.Open();break;
                case "random-seed":if(network.IsHost)UnityEngine.Random.InitState(int.Parse(c.value));break;
                case "pull":
                    if(c.value=="mouse"){
                        var control=game.Local.GetComponent<SourcePlayerController>();
                        control.ResetView(c.yaw,c.pitch);control.ApplyLook(new Vector2(c.x,c.z));game.Local.Combat.ProcessLassoInput();
                    }else game.Local.Lasso.TryPull(c.value=="gesture"?Quaternion.Euler(c.pitch,c.yaw,0)*Vector3.forward:Vector3.zero);
                    break;
                case "pointer":
                    movement.yaw=c.yaw;pitch=c.pitch;game.Local.GetComponent<SourcePlayerController>().ResetView(c.yaw,c.pitch);
                    game.Local.GetComponent<SourcePlayerController>().UpdateView();
                    FindAnyObjectByType<CourseHUD>().ProcessActions(false,c.primary,false);
                    game.Local.Combat.ProcessPointerInput(c.primary,c.aim,c.kick);break;
                case "move":pendingJump=c.jump&&!movement.jumpHeld;movement=new MovementCommand{move=new Vector2(c.x,c.z),yaw=c.yaw,crouch=c.crouch,jumpHeld=c.jump};pitch=c.pitch;break;
                case "teleport":
                    if(!network.IsHost&&!UserMapStudio.IsTesting)return;
                    if(UserMapStudio.IsTesting)UserMapStudio.Current.InvalidateClearAttempt();
                    game.participants[c.seat].Motor.Teleport(c.position);break;
                case "ferry-deck":
                    if(network.IsHost&&game.ferry)game.participants[c.seat].Motor.Teleport((c.value=="1"?game.ferry.secondCarrier:game.ferry.carrier).transform.position+Vector3.up*.225f);break;
                case "factory-shuttle":
                    if(network.IsHost&&game.factory)game.participants[c.seat].Motor.Teleport(game.factory.movingPlatforms[0].transform.position+Vector3.up*.225f);break;
                case "five":
                    if(!network.IsHost)return;
                    // Create a legal exposed-five fixture using actual deck operations on the authoritative host.
                    for(int n=0;n<55&&!game.cardGame.Match.HasExactFive();n++)game.cardGame.Match.Reveal(game.cardGame.Match.Turn);
                    break;
                case "surf-gates":
                    if(network.IsHost&&game.roster.surf&&int.TryParse(c.value,out int gates))game.roster.surf.RestoreCheckpointProgress(game.participants[c.seat],gates);
                    break;
                case "finish-match":
                    if(!network.IsHost||!network.Playing||c.seat<0||c.seat>=game.participants.Length)return;
                    game.cardGame.botEnabled=false;
                    for(int n=0;n<1000&&!game.cardGame.Match.Finished;n++)for(int seat=0;seat<game.participants.Length;seat++)if(seat!=c.seat){game.cardGame.Match.Stand(seat);game.cardGame.Match.Sit(seat);}
                    if(!game.cardGame.Match.Finished||game.cardGame.Match.Winner!=c.seat)throw new InvalidOperationException("Victory fixture failed");
                    break;
                case "visual-audit":WriteVisualAudit(c.value);break;
                case "capture":
                    if(!Application.isBatchMode)ScreenCapture.CaptureScreenshot(Path.Combine(folder,c.value+".png"));break;
            }
        }
        void WriteVisualAudit(string name)
        {
            var renderers=FindObjectsByType<Renderer>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            var materials=renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct().ToArray();
            var runtime=UserMapRuntime.Current;
            var report=new VisualReport {
                map=network.MapId,
                checkpointCount=game&&game.checkpoints?game.checkpoints.points.Length:0,
                unsupportedMaterials=materials.Where(m=>!m.shader||!m.shader.isSupported).Select(m=>m.name).ToArray(),
                legacyFloorFrames=FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                    .Where(t=>t.name=="Edge stripe"||t.name=="Rock lip"||t.name=="Amber edge").Select(t=>t.name).ToArray(),
                parts=runtime&&runtime.Data!=null?runtime.Data.objects.Select(item=>{
                    var root=runtime.RootFor(item.id);
                    var partRenderers=root.GetComponentsInChildren<Renderer>(true);
                    var partMaterials=partRenderers.SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct().ToArray();
                    return new PartVisualReport {id=item.id,kind=item.kind.ToString(),renderers=partRenderers.Length,
                        colliders=root.GetComponentsInChildren<Collider>(true).Length,
                        materials=partMaterials.Select(m=>m.name).ToArray(),shaders=partMaterials.Select(m=>m.shader?m.shader.name:"missing").Distinct().ToArray(),
                        supported=partMaterials.All(m=>m.shader&&m.shader.isSupported)};
                }).ToArray():Array.Empty<PartVisualReport>()
            };
            File.WriteAllText(Path.Combine(folder,name+".json"),JsonUtility.ToJson(report,true));
        }
    }
}
