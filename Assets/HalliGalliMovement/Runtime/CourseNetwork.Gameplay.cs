using System;
using System.Collections.Generic;
using System.Linq;
using HalliGalli.Cards;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class CourseNetwork
    {
        struct InputPacket : INetworkSerializeByMemcpy
        {
            public int epoch,sequence,respawn;
            public Vector2 move;
            public float yaw,pitch;
            public bool crouch,jumpPressed,jumpHeld;
            public MovementCommand Command=>new MovementCommand{move=move,yaw=yaw,crouch=crouch,jumpPressed=jumpPressed,jumpHeld=jumpHeld};
        }
        struct Pose : INetworkSerializeByMemcpy
        {
            public SourceMotor.NetworkState motor;
            public LassoTool.NetworkState lasso;
            public int ack,falls,respawns,attackSequence,hits,gates,checkpoint;
            public MeleeAttack attack;
            public float attackElapsed,kickCooldown,checkpointSide;
            public bool seated,respawning;
        }
        sealed class InputStream
        {
            public readonly Queue<InputPacket> queue=new Queue<InputPacket>();
            public InputPacket last;
            public int received,ack;
            public float at;
        }
        [Serializable] sealed class CardsMessage
        {
            public int epoch,bells;
            public bool running;
            public double returnToRoomAt;
            public HalliGalliMatch.PublicState cards;
        }
        [Serializable] sealed class HitMessage
        {
            public int epoch,target;
            public MeleeAttack attack;
            public Vector3 direction,point;
        }
        [Serializable] sealed class LassoSoundMessage
        {
            public int epoch,shooter,target,respawn,sequence;
            public LassoSound sound;public Vector3 position;
        }
        readonly Dictionary<ulong,InputStream> inputs=new Dictionary<ulong,InputStream>();
        readonly List<InputPacket> predicted=new List<InputPacket>();
        Pose[] remote;
        int inputSequence,poseSequence,lastPose,sentRevision=-1,receivedRevision=-1;
        float nextPose;
        public const float VictoryDisplaySeconds=5;
        double returnToRoomAt=-1;
        public bool VictoryReturnPending=>Playing&&bound&&game&&game.cardGame.Match.Finished&&returnToRoomAt>=0;
        public float VictoryReturnRemaining=>VictoryReturnPending?Mathf.Max(0,(float)(returnToRoomAt-WorldTime)):0;

        void UpdateVictoryReturn()
        {
            if(!Playing||!bound||!game||!game.cardGame.Match.Finished){returnToRoomAt=-1;return;}
            if(returnToRoomAt<0){
                // One server-clock deadline travels with the final card state. Clients never start their own timer.
                returnToRoomAt=WorldTime+VictoryDisplaySeconds;sentRevision=-1;
            }
            if(WorldTime>=returnToRoomAt)ReturnToRoom();
        }

        void ResetSimulation()
        {
            inputs.Clear();predicted.Clear();remote=null;
            inputSequence=poseSequence=lastPose=0;sentRevision=receivedRevision=-1;nextPose=0;
            returnToRoomAt=-1;
            ResetRaceNetwork();
        }
        public void LocalMovement(MovementCommand command,float pitch)
        {
            if(!Playing||!bound||!game)return;
            var p=game.Local;
            if(menu)command=new MovementCommand{yaw=p.transform.eulerAngles.y};
            if(p.IsSeated||p.IsRespawning)return;
            if(IsHost){p.Combat.networkAim=Quaternion.Euler(pitch,command.yaw,0)*Vector3.forward;p.Motor.Simulate(command,Time.fixedDeltaTime);return;}
            var packet=new InputPacket{epoch=epoch,sequence=++inputSequence,respawn=p.RespawnSequence,
                move=command.move,yaw=command.yaw,pitch=pitch,crouch=command.crouch,
                jumpPressed=command.jumpPressed,jumpHeld=command.jumpHeld};
            predicted.Add(packet);
            if(predicted.Count>128){_=Leave("입력 응답이 너무 늦어졌어. 다시 접속해줘.");return;}
            p.Motor.Simulate(command,Time.fixedDeltaTime);
            // Ordered reliable inputs preserve a single-frame jump press. Simulation is still tick-limited on the host.
            using(var writer=new FastBufferWriter(128,Allocator.Temp)){
                writer.WriteValueSafe(packet);
                manager.CustomMessagingManager.SendNamedMessage("HG.input",0,writer,NetworkDelivery.ReliableSequenced);
            }
        }
        static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
        void ReceiveInput(ulong sender,FastBufferReader reader)
        {
            if(!IsHost||!Playing||!bound||sender==0)return;
            int seat=members.FindIndex(m=>!m.bot&&m.id==sender);if(seat<0)return;
            try{
                reader.ReadValueSafe(out InputPacket packet);
                if(packet.epoch!=epoch||!Finite(packet.move.x)||!Finite(packet.move.y)||!Finite(packet.yaw)||!Finite(packet.pitch))return;
                if(!inputs.TryGetValue(sender,out var stream))inputs[sender]=stream=new InputStream();
                if(packet.sequence<=stream.received||packet.sequence>stream.received+256)return;
                if(stream.queue.Count>=32){manager.DisconnectClient(sender,"입력이 너무 많이 밀렸어. 다시 접속해줘.");return;}
                packet.move=Vector2.ClampMagnitude(packet.move,1);packet.yaw=Mathf.Repeat(packet.yaw,360);packet.pitch=Mathf.Clamp(packet.pitch,-85,85);
                stream.queue.Enqueue(packet);stream.received=packet.sequence;stream.at=Time.realtimeSinceStartup;
            }catch(OverflowException){ }
        }
        void FixedUpdate()
        {
            if(!online||!Playing||!bound||!game)return;
            if(IsHost){
                for(int i=0;i<members.Count;i++){
                    if(members[i].bot||members[i].id==manager.LocalClientId)continue;
                    var p=game.participants[i];
                    if(!inputs.TryGetValue(members[i].id,out var stream)){
                        if(!p.IsSeated&&!p.IsRespawning)p.Motor.Simulate(new MovementCommand{yaw=p.transform.eulerAngles.y},Time.fixedDeltaTime);
                        continue;
                    }
                    bool fresh=stream.queue.Count>0;
                    InputPacket input=fresh?stream.queue.Dequeue():stream.last;
                    if(fresh){stream.last=input;stream.ack=input.sequence;}
                    var command=input.Command;
                    if(!fresh)command.jumpPressed=false;
                    if(Time.realtimeSinceStartup-stream.at>.25f||input.respawn!=p.RespawnSequence)
                        command=new MovementCommand{yaw=p.transform.eulerAngles.y};
                    p.Combat.networkAim=Quaternion.Euler(input.pitch,command.yaw,0)*Vector3.forward;
                    if(!p.IsSeated&&!p.IsRespawning)p.Motor.Simulate(command,Time.fixedDeltaTime);
                }
                if(Time.unscaledTime>=nextPose){nextPose=Time.unscaledTime+.05f;BroadcastPoses();BroadcastRace();}
            }else if(remote!=null){
                for(int i=0;i<remote.Length;i++){
                    var p=game.participants[i];if(p.localPlayer)continue;
                    var target=remote[i].motor;
                    target.position=Vector3.Lerp(p.transform.position,target.position,1-Mathf.Exp(-22*Time.fixedDeltaTime));
                    target.yaw=Mathf.LerpAngle(p.transform.eulerAngles.y,target.yaw,1-Mathf.Exp(-22*Time.fixedDeltaTime));
                    p.Motor.RestoreNetworkState(target);
                }
            }
        }
        void BroadcastPoses()
        {
            using(var writer=new FastBufferWriter(2048,Allocator.Temp)){
                writer.WriteValueSafe(epoch);writer.WriteValueSafe(++poseSequence);writer.WriteValueSafe(game.cardGame.Match.Revision);
                writer.WriteValueSafe(members.Count);
                for(int i=0;i<members.Count;i++){
                    var p=game.participants[i];
                    int ack=inputs.TryGetValue(members[i].id,out var stream)?stream.ack:0;
                    var pose=new Pose{motor=p.Motor.CaptureNetworkState(),lasso=p.Lasso.CaptureNetworkState(),ack=ack,falls=p.Falls,respawns=p.RespawnSequence,
                        attack=p.Combat.CurrentAttack,attackElapsed=p.Combat.Elapsed,attackSequence=p.Combat.AttackSequence,hits=p.Combat.HitCount,kickCooldown=p.Combat.KickCooldownRemaining,
                        seated=p.IsSeated,respawning=p.IsRespawning,gates=game.roster.surf?game.roster.surf.GatesPassed(p):0,checkpoint=p.CheckpointIndex,checkpointSide=p.CheckpointSide};
                    writer.WriteValueSafe(pose);
                }
                foreach(var m in members)if(!m.bot&&m.id!=manager.LocalClientId)
                    manager.CustomMessagingManager.SendNamedMessage("HG.pose",m.id,writer,NetworkDelivery.UnreliableSequenced);
            }
        }
        void ReceivePose(ulong sender,FastBufferReader reader)
        {
            if(sender!=0||IsHost||!Playing||!bound)return;
            try{
                reader.ReadValueSafe(out int receivedEpoch);reader.ReadValueSafe(out int sequence);
                reader.ReadValueSafe(out int revision);reader.ReadValueSafe(out int count);
                if(receivedEpoch!=epoch||sequence<=lastPose||count!=game.participants.Length||revision<receivedRevision)return;
                var poses=new Pose[count];for(int i=0;i<count;i++)reader.ReadValueSafe(out poses[i]);
                lastPose=sequence;remote=poses;
                for(int i=0;i<count;i++){
                    var p=game.participants[i];var pose=poses[i];Vector3 before=p.transform.position;
                    bool teleport=p.ApplyNetworkStatus(pose.seated,pose.respawning,pose.falls,pose.respawns,pose.motor.yaw);
                    p.SetCheckpoint(pose.checkpoint,pose.checkpointSide);
                    p.Lasso.ApplyNetwork(pose.lasso);
                    p.Combat.ApplyNetworkAttack(pose.attack,pose.attackElapsed,pose.attackSequence,pose.hits,pose.kickCooldown);
                    if(game.roster.surf)game.roster.surf.ApplyNetworkProgress(p,pose.gates);
                    if(!p.localPlayer){if(teleport)p.Motor.RestoreNetworkState(pose.motor);continue;}
                    predicted.RemoveAll(c=>c.sequence<=pose.ack||c.respawn!=pose.respawns);
                    if(teleport||pose.seated||pose.respawning)predicted.Clear();
                    p.Motor.RestoreNetworkState(pose.motor);
                    if(!pose.seated&&!pose.respawning)
                        foreach(var command in predicted)p.Motor.Simulate(command.Command,Time.fixedDeltaTime);
                    p.GetComponent<SourcePlayerController>().SmoothNetworkCorrection(before,teleport);
                }
            }catch(OverflowException){ }
        }
        void BroadcastCards()
        {
            if(!IsHost||!bound)return;
            sentRevision=game.cardGame.Match.Revision;
            SendAll("HG.cards",new CardsMessage{epoch=epoch,bells=game.BellCount,running=game.RunActive,returnToRoomAt=returnToRoomAt,cards=game.cardGame.Match.ExportPublicState(game.cardGame.deck)});
        }
        void ReceiveCards(ulong sender,CardsMessage message)
        {
            if(sender!=0||IsHost||!bound||message.epoch!=epoch||message.cards==null||message.cards.revision<receivedRevision)return;
            receivedRevision=message.cards.revision;
            returnToRoomAt=message.cards.winner>=0?message.returnToRoomAt:-1;
            remote=null;
            // Seated transitions precede card camera focus; pose messages cannot undo this newer card state.
            for(int i=0;i<game.participants.Length;i++){
                var p=game.participants[i];bool seated=message.cards.seats[i].seated;
                if(p.IsSeated!=seated){
                    p.ApplyNetworkStatus(seated,false,p.Falls,p.RespawnSequence);
                    var state=p.Motor.CaptureNetworkState();state.position=(seated?p.seatAnchor:p.standAnchor).position;
                    state.yaw=(seated?p.seatAnchor:p.standAnchor).eulerAngles.y;state.velocity=Vector3.zero;state.stun=state.stunImmunity=state.raceSlip=state.raceLaunch=0;
                    p.Motor.RestoreNetworkState(state);if(p.localPlayer){predicted.Clear();p.GetComponent<SourcePlayerController>().SmoothNetworkCorrection(p.transform.position,true);}
                }
            }
            game.cardGame.ApplyNetworkState(message.cards);game.ApplyNetworkState(message.running,message.bells);
        }
        public void LocalAction(NetworkAction action,bool pressed=true,Vector3? aim=null)
        {
            if(!pressed||!online||Busy)return;
            if(action!=NetworkAction.Ready&&(!Playing||!bound||menu))return;
            if(action==NetworkAction.Ready&&(Playing||loading||!UserMapReady))return;
            var race=RaceCourse.Active;
            var message=new ActionMessage{epoch=epoch,mapRevision=mapRevision,action=action,respawn=game?game.Local.RespawnSequence:0,
                raceRound=race?race.Round:0,itemSequence=race&&game?race.ItemSequence(game.Local):0,
                aim=aim??(game?game.Local.GetComponent<SourcePlayerController>().AimRotation*Vector3.forward:Vector3.forward)};
            if(IsHost)ReceiveAction(manager.LocalClientId,message);else Send("HG.action",message,0);
        }
        public static bool RouteCombat(MeleeCombat combat,bool kick)
        {
            if(!Active||Authority)return false;
            var p=combat.GetComponent<CourseParticipant>();
            if(p&&p.localPlayer)Instance.LocalAction(kick?NetworkAction.Kick:NetworkAction.Push);
            return true;
        }
        void ReceiveAction(ulong sender,ActionMessage message)
        {
            if(!IsHost||message.epoch!=epoch)return;
            int seat=members.FindIndex(m=>!m.bot&&m.id==sender);if(seat<0)return;
            if(actionTimes.TryGetValue(sender,out float at)&&Time.realtimeSinceStartup-at<.06f)return;
            actionTimes[sender]=Time.realtimeSinceStartup;
            if(message.action==NetworkAction.Ready){
                if(!Playing&&!loading&&sender!=0&&message.mapRevision==mapRevision&&IsMapPeerReady(sender)){members[seat].ready=!members[seat].ready;BroadcastRoom();}
                return;
            }
            if(!Playing||!bound)return;
            // Keep the result visible for its full duration, including if R was still being pressed.
            if(game.cardGame.Match.Finished)return;
            var p=game.participants[seat];
            switch(message.action){
                case NetworkAction.Reveal:game.cardGame.Reveal(p);break;
                case NetworkAction.Interact:
                    if(p.IsRespawning)break;
                    if(p.IsSeated)p.Stand();else if(game.CanRing(p))game.Ring(p);else if(p.CanSit)p.Sit();break;
                case NetworkAction.Push:
                    if(message.respawn==p.RespawnSequence){p.Combat.networkAim=message.aim;p.Combat.TryPush();}break;
                case NetworkAction.Kick:
                    if(message.respawn==p.RespawnSequence){p.Combat.networkAim=message.aim;p.Combat.TryKick();}break;
                case NetworkAction.Respawn:if(message.respawn==p.RespawnSequence)p.RequestRespawn();break;
                case NetworkAction.LassoThrow:if(message.respawn==p.RespawnSequence)p.Lasso.TryThrow(message.aim);break;
                case NetworkAction.UseItem:
                    var race=RaceCourse.Active;
                    if(race&&race.session==game&&message.mapRevision==mapRevision&&message.respawn==p.RespawnSequence&&message.raceRound==race.Round&&message.itemSequence==race.ItemSequence(p))
                        race.UseItem(p,message.aim);
                    break;
                case NetworkAction.Restart:
                    if(sender!=0)break;
                    ReturnToRoom();break;
            }
        }
        public void ReturnToRoom()
        {
            if(!IsHost||loading)return;
            Playing=false;bound=false;menu=true;epoch++;ResetSimulation();
            foreach(var m in members)m.ready=m.bot||m.id==0;
            FreezeWorld();BroadcastRoom();
        }
        public void SendHit(MeleeCombat attacker,MeleeReceiver target,MeleeAttack attack,Vector3 direction,Vector3 point)
        {
            if(!IsHost||!Playing)return;
            int index=Array.FindIndex(game.participants,p=>p.Receiver==target);if(index<0)return;
            SendAll("HG.hit",new HitMessage{epoch=epoch,target=index,attack=attack,direction=direction,point=point});
        }
        void ReceiveHit(ulong sender,HitMessage hit)
        {
            if(sender!=0||IsHost||!Playing||!bound||hit.epoch!=epoch||hit.target<0||hit.target>=game.participants.Length)return;
            game.participants[hit.target].Receiver.ReceiveNetwork(hit.attack,hit.direction,hit.point);
        }
        public void SendLassoSound(CourseParticipant shooter,CourseParticipant target,LassoSound sound,int sequence,Vector3 position)
        {
            if(!IsHost||!Playing||!bound)return;
            SendAll("HG.lassoSound",new LassoSoundMessage{epoch=epoch,shooter=Array.IndexOf(game.participants,shooter),target=target?Array.IndexOf(game.participants,target):-1,
                respawn=shooter.RespawnSequence,sequence=sequence,sound=sound,position=position});
        }
        void ReceiveLassoSound(ulong sender,LassoSoundMessage message)
        {
            if(sender!=0||IsHost||!Playing||!bound||message.epoch!=epoch||message.shooter<0||message.shooter>=game.participants.Length||(int)message.sound>2)return;
            var p=game.participants[message.shooter];if(message.respawn!=p.RespawnSequence)return;
            bool victim=message.target>=0&&message.target<game.participants.Length&&game.participants[message.target].localPlayer;
            p.GetComponent<LassoAudio>().Play(message.sound,message.sequence,message.position,victim);
        }
    }
}
