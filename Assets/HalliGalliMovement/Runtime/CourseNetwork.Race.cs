using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class CourseNetwork
    {
        int raceSequence,lastRaceSequence;
        float nextRaceSnapshot;
        void ResetRaceNetwork()
        {
            raceSequence=lastRaceSequence=0;nextRaceSnapshot=0;
            if(RaceCourse.Active)RaceCourse.Active.ResetRound();
        }
        void BroadcastRace()
        {
            var race=RaceCourse.Active;
            if(!race||race.session!=game||Time.unscaledTime<nextRaceSnapshot)return;
            nextRaceSnapshot=Time.unscaledTime+.1f;
            // Box timers and projectiles can exceed one MTU. Keep them off the small unreliable pose channel.
            using(var writer=new FastBufferWriter(race.NetworkCapacity,Allocator.Temp)){
                writer.WriteValueSafe(epoch);writer.WriteValueSafe(++raceSequence);writer.WriteValueSafe(game.cardGame.Match.Revision);
                race.WriteNetwork(writer);
                foreach(var member in members)if(!member.bot&&member.id!=manager.LocalClientId)
                    manager.CustomMessagingManager.SendNamedMessage("HG.race",member.id,writer,NetworkDelivery.ReliableFragmentedSequenced);
            }
        }
        void ReceiveRace(ulong sender,FastBufferReader reader)
        {
            var race=RaceCourse.Active;
            if(sender!=NetworkManager.ServerClientId||IsHost||!Playing||!bound||!race||race.session!=game)return;
            try{
                reader.ReadValueSafe(out int receivedEpoch);reader.ReadValueSafe(out int sequence);reader.ReadValueSafe(out int revision);
                if(receivedEpoch!=epoch||sequence<=lastRaceSequence||revision<receivedRevision)return;
                if(race.ReadNetwork(reader))lastRaceSequence=sequence;
            }catch(OverflowException){ }
        }
    }
}
