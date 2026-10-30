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
            public bool seated,respawning,grounded,crouched,aiming,aiEnabled;
            public int falls,respawns,hits,attacks,checkpoint,gates,ammo,shots,stunHits,blinks;
            public float blink,yaw,viewYaw,checkpointSide,stun,immunity;
            public int lassoCasts,lassoHits,lassoPulls,lassoTarget;
            public string lassoPhase;public float lassoCooldown;
            public int ropeThrowSounds,ropeLatchSounds,ropePullSounds;
        }
        [Serializable] sealed class CheckpointInfo {public Vector3 position;public float yaw;public int gates;}
        [Serializable] sealed class Report
        {
            public string role,status,code,map,cardState,error;
            public string winnerNickname,victoryAction;
            public bool victoryVisible,canContinueAfterVictory;
            public bool victoryReturnPending;
            public float victoryReturnRemaining;
            public int victorySoundsPlayed;
            public bool victoryAudioReady,victorySoundPlaying;
            public bool active,playing,loading,host,canStart;
            public int localSeat,bells,revision,turn,bots,capacity,surfSections,twoSidedSurfSections;
            public bool canAddBot,botCards;
            public CourseNetwork.Member[] members;
            public Actor[] actors;
            public CheckpointInfo[] checkpoints;
            public Vector3 bellLanding;
            public bool hasFerry;public Vector3 ferryPosition;public Vector3[] ferryPositions;
            public double worldTime;public Vector3[] factoryPlatforms,factoryPresses;
        }
        string role,folder,error="";
        int lastCommand,lastRespawn=-1;float nextReport;
        CourseNetwork network;CourseSession game;
        MovementCommand movement;float pitch;bool pendingJump;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if(Arg("--net-probe")!=null){var go=new GameObject("Network test driver");DontDestroyOnLoad(go);go.AddComponent<CourseNetworkProbe>();}
        }
        static string Arg(string key){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
        async void Start()
        {
            role=Arg("--net-probe");folder=Arg("--probe-dir");if(string.IsNullOrEmpty(folder)){enabled=false;return;}
            Application.targetFrameRate=60;
            Directory.CreateDirectory(folder);
            await System.Threading.Tasks.Task.Yield();
            network=CourseNetwork.Instance;
            if(!network){error="Network runtime missing";return;}
            try{
                if(Arg("--relay-probe")=="true"){
                    if(role=="host")await network.Connect(true);
                    // A Relay client waits for its join command, after the real code has been created.
                }else if(role!="manual"){
                    ushort port=ushort.TryParse(Arg("--net-port"),out var parsed)?parsed:(ushort)7777;
                    network.StartLoopback(role=="host",port,role);
                }
            }catch(Exception e){error=e.ToString();}
        }
        void Update()
        {
            if(!network||string.IsNullOrEmpty(folder))return;
            if(network.Game!=game){game=network.Game;if(game){game.Local.GetComponent<SourcePlayerController>().enabled=false;game.cardGame.ReadInput=false;}}
            // The probe supplies pointer state explicitly; live-device focus checks must not cancel it.
            if(game)game.Local.Combat.readInput=false;
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
                active=CourseNetwork.Active,playing=network.Playing,loading=network.IsLoading,host=network.IsHost,canStart=network.CanStart,
                localSeat=network.LocalSeat,bells=game?game.BellCount:0,revision=match?.Revision??-1,turn=match?.Turn??-1,bots=network.BotCount,capacity=network.Capacity,canAddBot=network.CanAddBot,botCards=game&&game.cardGame.botEnabled,
                cardState=match==null?"":JsonUtility.ToJson(match.ExportPublicState(game.cardGame.deck)),members=network.Members.ToArray(),
                actors=game?game.participants.Select(p=>new Actor{position=p.transform.position,velocity=p.Motor.Velocity,seated=p.IsSeated,
                    respawning=p.IsRespawning,grounded=p.Motor.Grounded,crouched=p.Motor.IsCrouched,falls=p.Falls,respawns=p.RespawnSequence,aiEnabled=p.GetComponent<CourseRival>()&&p.GetComponent<CourseRival>().enabled,
                    hits=p.Receiver.ReceivedHits,attacks=p.Combat.AttackSequence,blink=p.Receiver.PunchBlinkAmount,blinks=p.Receiver.BlinksPlayed,
                    lassoCasts=p.Lasso.Casts,lassoHits=p.Lasso.Hits,lassoPulls=p.Lasso.Pulls,lassoTarget=Array.IndexOf(game.participants,p.Lasso.Target),lassoPhase=p.Lasso.Phase.ToString(),lassoCooldown=p.Lasso.CooldownRemaining,
                    ropeThrowSounds=p.GetComponent<LassoAudio>().Played(LassoSound.Throw),ropeLatchSounds=p.GetComponent<LassoAudio>().Played(LassoSound.Latch),ropePullSounds=p.GetComponent<LassoAudio>().Played(LassoSound.Pull),
                    checkpoint=p.CheckpointIndex,checkpointSide=p.CheckpointSide,gates=game.roster.surf?game.roster.surf.GatesPassed(p):0,yaw=p.transform.eulerAngles.y,viewYaw=p.localPlayer?p.GetComponent<SourcePlayerController>().AimRotation.eulerAngles.y:p.transform.eulerAngles.y}).ToArray():new Actor[0],error=error,
                checkpoints=game&&game.checkpoints?game.checkpoints.points.Select(p=>new CheckpointInfo{position=p.spawn.position,yaw=p.spawn.eulerAngles.y,gates=p.surfGates}).ToArray():new CheckpointInfo[0],
                surfSections=game&&game.roster.surf?game.roster.surf.sections.Length:0,
                twoSidedSurfSections=game&&game.roster.surf?game.roster.surf.sections.Count(s=>s.twoSided):0,
                bellLanding=game?game.roster.surf?game.roster.surf.finish.position:game.roster.branches?game.roster.branches.summit.position:game.roster.RouteForSeat(0).Last().position:Vector3.zero};
            var hud=game?FindAnyObjectByType<CourseHUD>():null;
            if(hud){report.winnerNickname=hud.WinnerNickname;report.victoryAction=hud.VictoryAction;report.victoryVisible=hud.VictoryVisible;report.canContinueAfterVictory=hud.CanContinueAfterVictory;}
            report.victoryReturnPending=network.VictoryReturnPending;report.victoryReturnRemaining=network.VictoryReturnRemaining;
            if(hud){report.victorySoundsPlayed=hud.VictorySoundsPlayed;report.victoryAudioReady=hud.VictoryAudioReady;report.victorySoundPlaying=hud.VictoryAudioSource&&hud.VictoryAudioSource.isPlaying;}
            try{File.WriteAllText(Path.Combine(folder,"state-"+role+".json"),JsonUtility.ToJson(report));}catch(IOException){}
        }
        void FixedUpdate(){if(network&&network.Playing){movement.jumpPressed=pendingJump;pendingJump=false;network.LocalMovement(movement,pitch);}}
        void Execute(Command c)
        {
            switch(c.action){
                case "join":_=network.Connect(false,c.value);break;
                case "add-bot":network.AddBot();break;
                case "remove-bot":if(c.seat>=0&&c.seat<network.Members.Count)network.RemoveBot(network.Members[c.seat].id);break;
                case "ready":network.LocalAction(NetworkAction.Ready);break;
                case "start":network.StartMatch();break;
                case "map":network.SelectMap(c.value);break;
                case "room":network.ReturnToRoom();break;
                case "menu":network.ShowMenu();break;
                case "leave":_=network.Leave();break;
                case "action":network.LocalAction((NetworkAction)Enum.Parse(typeof(NetworkAction),c.value));break;
                case "lasso":game.Local.Lasso.TryUse(Quaternion.Euler(c.pitch,c.yaw,0)*Vector3.forward);break;
                case "pull":
                    if(c.value=="mouse"){
                        var control=game.Local.GetComponent<SourcePlayerController>();
                        control.ResetView(c.yaw,c.pitch);control.ApplyLook(new Vector2(c.x,c.z));game.Local.Combat.ProcessLassoInput();
                    }else game.Local.Lasso.TryPull(c.value=="gesture"?Quaternion.Euler(c.pitch,c.yaw,0)*Vector3.forward:Vector3.zero);
                    break;
                case "pointer":
                    movement.yaw=c.yaw;pitch=c.pitch;game.Local.GetComponent<SourcePlayerController>().ResetView(c.yaw,c.pitch);
                    FindAnyObjectByType<CourseHUD>().ProcessActions(false,c.primary,false);
                    game.Local.Combat.ProcessPointerInput(c.primary,c.aim,c.kick);break;
                case "move":pendingJump=c.jump&&!movement.jumpHeld;movement=new MovementCommand{move=new Vector2(c.x,c.z),yaw=c.yaw,crouch=c.crouch,jumpHeld=c.jump};pitch=c.pitch;break;
                case "teleport":
                    if(!network.IsHost)return;
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
                case "capture":
                    if(!Application.isBatchMode)ScreenCapture.CaptureScreenshot(Path.Combine(folder,c.value+".png"));break;
            }
        }
    }
}
