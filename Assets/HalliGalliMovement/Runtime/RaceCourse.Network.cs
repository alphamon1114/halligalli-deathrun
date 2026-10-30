using System;
using Unity.Netcode;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class RaceCourse
    {
        internal struct Header : INetworkSerializeByMemcpy
        {
            public int round,acquisitions,missileHits,bananaHits,ballHits,laserHits,blockedHits,actorCount,boxCount,entityCount;
            public bool running;
        }
        readonly ActorState[] incomingActors=new ActorState[MaxActors];
        readonly EntityState[] incomingEntities=new EntityState[MaxEntities];
        double[] incomingBoxes;
        static readonly int NetworkFixedCapacity=3*sizeof(int)+UnsafeUtility.SizeOf<Header>()
            +MaxActors*UnsafeUtility.SizeOf<ActorState>()+MaxEntities*(sizeof(int)+UnsafeUtility.SizeOf<EntityState>());
        internal int NetworkCapacity=>NetworkFixedCapacity+itemBoxes.Length*sizeof(double);
        internal void WriteNetwork(FastBufferWriter writer)
        {
            EnsureInitialized();int active=0;for(int i=0;i<entities.Length;i++)if(entities[i].item!=RaceItem.None)active++;
            writer.WriteValueSafe(new Header{round=Round,running=raceRunning,acquisitions=acquisitions,missileHits=missileHits,bananaHits=bananaHits,ballHits=ballHits,laserHits=laserHits,blockedHits=blockedHits,
                actorCount=session.participants.Length,boxCount=boxReady.Length,entityCount=active});
            for(int i=0;i<session.participants.Length;i++)writer.WriteValueSafe(actors[i]);
            for(int i=0;i<boxReady.Length;i++)writer.WriteValueSafe(boxReady[i]);
            for(int i=0;i<entities.Length;i++)if(entities[i].item!=RaceItem.None){writer.WriteValueSafe(i);writer.WriteValueSafe(entities[i]);}
        }
        internal bool ReadNetwork(FastBufferReader reader)
        {
            EnsureInitialized();if(!initialized)return false;
            reader.ReadValueSafe(out Header header);
            if(header.round<Round||header.actorCount!=session.participants.Length||header.actorCount>MaxActors||header.boxCount!=boxReady.Length||header.entityCount<0||header.entityCount>MaxEntities)return false;
            if(incomingBoxes==null||incomingBoxes.Length!=boxReady.Length)incomingBoxes=new double[boxReady.Length];
            for(int i=0;i<header.actorCount;i++)reader.ReadValueSafe(out incomingActors[i]);
            for(int i=0;i<header.boxCount;i++)reader.ReadValueSafe(out incomingBoxes[i]);
            Array.Clear(incomingEntities,0,incomingEntities.Length);
            for(int i=0;i<header.entityCount;i++){
                reader.ReadValueSafe(out int slot);if(slot<0||slot>=MaxEntities)return false;
                reader.ReadValueSafe(out incomingEntities[slot]);
            }
            if(Round!=header.round)RestoreBodies();
            Round=header.round;raceRunning=header.running;acquisitions=header.acquisitions;missileHits=header.missileHits;bananaHits=header.bananaHits;ballHits=header.ballHits;laserHits=header.laserHits;blockedHits=header.blockedHits;
            for(int i=0;i<header.actorCount;i++){
                var p=Participant(i);var state=incomingActors[i];
                // A delayed snapshot cannot put an old life/seat's inventory or effect back.
                if(state.life!=p.RespawnSequence||!Eligible(p)){
                    state.item=RaceItem.None;state.shieldUntil=state.effectUntil=0;state.effect=RaceEffect.None;state.lockTarget=-1;
                }
                actors[i]=state;
            }
            Array.Copy(incomingBoxes,boxReady,boxReady.Length);Array.Copy(incomingEntities,entities,entities.Length);
            return true;
        }
        [Serializable] public sealed class Snapshot
        {
            public int round,leader,acquisitions,missileHits,bananaHits,ballHits,laserHits,blockedHits;
            public bool running;
            public ActorSnapshot[] actors;
            public BoxSnapshot[] boxes;
            public ProjectileSnapshot[] projectiles;
        }
        [Serializable] public struct ActorSnapshot
        {
            public int seat,life,itemSequence,lockTarget,acquisitions,uses,flips,slips,pushes,blocked;
            public RaceItem item;public RaceEffect effect;
            public float shieldRemaining,effectRemaining;
        }
        [Serializable] public struct BoxSnapshot {public int index;public Vector3 position;public float cooldown;}
        [Serializable] public struct ProjectileSnapshot {public int id,owner,target;public RaceItem item;public Vector3 position,direction;public float distance,remaining;}
        // Allocating DTOs are reserved for explicit diagnostics, never the game or packet tick.
        public Snapshot CaptureSnapshot()
        {
            EnsureInitialized();int count=session&&session.participants!=null?session.participants.Length:0,active=0;
            for(int i=0;i<entities.Length;i++)if(entities[i].item!=RaceItem.None)active++;
            var result=new Snapshot{round=Round,leader=Leader,running=raceRunning,acquisitions=acquisitions,missileHits=missileHits,bananaHits=bananaHits,ballHits=ballHits,laserHits=laserHits,blockedHits=blockedHits,
                actors=new ActorSnapshot[count],boxes=new BoxSnapshot[boxReady.Length],projectiles=new ProjectileSnapshot[active]};
            double now=Clock;
            for(int i=0;i<count;i++){
                var s=actors[i];result.actors[i]=new ActorSnapshot{seat=i,life=s.life,item=s.item,itemSequence=s.itemSequence,lockTarget=s.lockTarget,
                    shieldRemaining=Mathf.Max(0,(float)(s.shieldUntil-now)),effectRemaining=Mathf.Max(0,(float)(s.effectUntil-now)),effect=s.effectUntil>now?s.effect:RaceEffect.None,
                    acquisitions=s.acquisitions,uses=s.uses,flips=s.flips,slips=s.slips,pushes=s.pushes,blocked=s.blocked};
            }
            for(int i=0;i<boxReady.Length;i++)result.boxes[i]=new BoxSnapshot{index=i,position=boxRest[i],cooldown=Mathf.Max(0,(float)(boxReady[i]-now))};
            int index=0;for(int i=0;i<entities.Length;i++){
                var e=entities[i];if(e.item==RaceItem.None)continue;
                result.projectiles[index++]=new ProjectileSnapshot{id=e.id,item=e.item,owner=e.owner,target=e.target,position=e.position,direction=e.direction,distance=e.distance,remaining=Mathf.Max(0,(float)(e.expires-now))};
            }
            return result;
        }
    }
}
