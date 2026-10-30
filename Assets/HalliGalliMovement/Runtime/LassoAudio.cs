using UnityEngine;

namespace HalliGalli.Movement
{
    public enum LassoSound : byte { Throw, Latch, Pull }

    [DisallowMultipleComponent]
    public sealed class LassoAudio:MonoBehaviour
    {
        static readonly string[] Names={"Throw","Latch","Pull"};
        static readonly float[] Volumes={.65f,.82f,.78f};
        readonly AudioClip[] clips=new AudioClip[3];
        readonly AudioSource[] sources=new AudioSource[3];
        readonly int[] sequences=new int[3];
        readonly int[] played=new int[3];
        readonly Vector3[] positions=new Vector3[3];
        CourseParticipant owner;
        public bool ClipsReady=>clips[0]&&clips[1]&&clips[2];
        public int Played(LassoSound sound)=>played[(int)sound];
        public AudioSource Source(LassoSound sound)=>sources[(int)sound];
        void Awake()
        {
            owner=GetComponent<CourseParticipant>();
            for(int i=0;i<3;i++){
                clips[i]=Resources.Load<AudioClip>("LassoAudio/"+Names[i]);
                var go=new GameObject("Lasso sound / "+Names[i]);go.transform.SetParent(transform,false);
                var source=go.AddComponent<AudioSource>();sources[i]=source;
                source.playOnAwake=false;source.loop=false;source.clip=clips[i];source.volume=Volumes[i];
                source.spatialBlend=1;source.rolloffMode=AudioRolloffMode.Linear;source.minDistance=2;source.maxDistance=22;
                source.dopplerLevel=0;source.spread=30;source.priority=i==1?80:100;
            }
        }
        public void Play(LassoSound sound,int sequence,Vector3 position,bool localVictim=false)
        {
            int index=(int)sound;if(index<0||index>=3||sequence<=sequences[index])return;
            // Network events, rather than sampled poses, preserve a hit immediately followed
            // by a pull and never replay an old sound during reconciliation or reconnects.
            sequences[index]=sequence;
            if(!clips[index]||!owner||owner.IsSeated||owner.IsRespawning)return;
            if(CourseNetwork.Active&&!CourseNetwork.Instance.Playing)return;
            var source=sources[index];positions[index]=position;source.transform.position=position;
            source.spatialBlend=localVictim||owner.localPlayer&&sound!=LassoSound.Latch?0:1;
            source.PlayOneShot(clips[index],1);played[index]++;
        }
        void LateUpdate()
        {
            if(owner&&(owner.IsSeated||owner.IsRespawning||CourseNetwork.Active&&!CourseNetwork.Instance.Playing))Stop();
            for(int i=0;i<3;i++)if(sources[i]&&sources[i].isPlaying)sources[i].transform.position=positions[i];
        }
        public void Stop(){foreach(var source in sources)if(source)source.Stop();}
        void OnDisable()=>Stop();
    }
}
