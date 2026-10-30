using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class CourseHUD
    {
        AudioSource victoryAudio;
        bool victorySoundPlayed;
        public int VictorySoundsPlayed{get;private set;}
        public AudioSource VictoryAudioSource=>victoryAudio;
        public bool VictoryAudioReady=>victoryAudio&&victoryAudio.clip;

        void Awake()
        {
            var clip=Resources.Load<AudioClip>("UIAudio/Victory");
            if(!clip)return;
            var sound=new GameObject("Winner reveal sound");sound.transform.SetParent(transform,false);
            victoryAudio=sound.AddComponent<AudioSource>();
            victoryAudio.clip=clip;victoryAudio.playOnAwake=false;victoryAudio.loop=false;
            victoryAudio.spatialBlend=0;victoryAudio.volume=.65f;victoryAudio.priority=24;
            victoryAudio.dopplerLevel=0;victoryAudio.ignoreListenerPause=true;
        }

        void UpdateVictoryAudio()
        {
            if(!MatchFinished){victorySoundPlayed=false;StopVictoryAudio();return;}
            if(CourseNetwork.Active&&!CourseNetwork.Instance.Playing){StopVictoryAudio();return;}
            // This runs once on each client's result reveal, never in OnGUI or on a packet replay.
            if(!VictoryVisible||victorySoundPlayed)return;
            victorySoundPlayed=true;
            if(!VictoryAudioReady)return;
            victoryAudio.Play();VictorySoundsPlayed++;
        }

        void StopVictoryAudio(){if(victoryAudio&&victoryAudio.isPlaying)victoryAudio.Stop();}
        void OnDisable()=>StopVictoryAudio();
    }
}
