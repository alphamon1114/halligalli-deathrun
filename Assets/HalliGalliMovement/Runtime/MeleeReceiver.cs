using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed class MeleeReceiver : MonoBehaviour
    {
        public ImpactAudioSet audioSet;
        public AudioSource impactSource;
        [HideInInspector] public bool canReceiveHits=true;
        [Header("Kick knockback (metres / second)")]
        [Min(0)] public float kickHorizontalSpeed=6f;
        [Min(0)] public float kickLiftSpeed=4f;
        public int ReceivedHits { get; private set; }
        public Vector3 LastImpulse { get; private set; }
        public float LastHitTime { get; private set; } = -100;
        public MeleeAttack LastAttack { get; private set; }
        public string ReactionName { get; private set; } = "";
        public float ReactionElapsed => Mathf.Max(0,Time.time-LastHitTime);
        public float ReactionDuration => LastAttack==MeleeAttack.Kick?.42f:.48f;
        public bool IsReacting => LastAttack!=MeleeAttack.None && ReactionElapsed<ReactionDuration;
        public int SoundsPlayed { get; private set; }
        public AudioClip LastSound { get; private set; }
        public const float PunchBlinkDuration=.3f, PunchBlinkRecovery=.45f;
        public float LastBlinkTime { get; private set; } = -100;
        public int BlinksPlayed { get; private set; }
        Texture2D eyelidTexture;
        public float PunchBlinkAmount
        {
            get {
                float elapsed=Time.time-LastBlinkTime;
                if(elapsed<0 || elapsed>=PunchBlinkDuration)return 0;
                if(elapsed<.06f)return Mathf.SmoothStep(0,1,elapsed/.06f);
                if(elapsed<.09f)return 1;
                return 1-Mathf.SmoothStep(0,1,(elapsed-.09f)/(PunchBlinkDuration-.09f));
            }
        }

        void StartPunchBlink()
        {
            // Hits still react and sound, but cannot extend the blink or its recovery window.
            if(Time.time<LastBlinkTime+PunchBlinkDuration+PunchBlinkRecovery)return;
            LastBlinkTime=Time.time;BlinksPlayed++;
        }
        public void ClearPunchBlink(){LastBlinkTime=-100;}

        void OnGUI()
        {
            if(Event.current.type!=EventType.Repaint || PunchBlinkAmount<=0)return;
            var controller=GetComponent<SourcePlayerController>();
            var participant=GetComponent<CourseParticipant>();
            if(!controller || !controller.enabled || !controller.playerCamera || !controller.playerCamera.enabled)return;
            if(participant && (!participant.localPlayer || participant.IsSeated || participant.IsRespawning))return;
            if(CourseNetwork.CapturesPointer)return;
            if(!eyelidTexture)CreateEyelidTexture();
            Rect view=controller.playerCamera.pixelRect;
            view.y=Screen.height-view.yMax;
            float height=view.height*.5f*PunchBlinkAmount;
            var color=GUI.color;var matrix=GUI.matrix;int depth=GUI.depth;
            GUI.matrix=Matrix4x4.identity;GUI.color=Color.white;GUI.depth=-100;
            // The curved edges leave a narrow central opening even at the peak of the blink.
            GUI.DrawTextureWithTexCoords(new Rect(view.x,view.y,view.width,height),eyelidTexture,new Rect(0,0,1,1));
            GUI.DrawTextureWithTexCoords(new Rect(view.x,view.yMax-height,view.width,height),eyelidTexture,new Rect(0,1,1,-1));
            GUI.color=color;GUI.matrix=matrix;GUI.depth=depth;
        }
        void CreateEyelidTexture()
        {
            const int width=256,height=64;
            eyelidTexture=new Texture2D(width,height,TextureFormat.RGBA32,false){name="Punch eyelid",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear,hideFlags=HideFlags.HideAndDontSave};
            var pixels=new Color[width*height];
            for(int y=0;y<height;y++)for(int x=0;x<width;x++){
                float side=2f*x/(width-1)-1;
                float edge=.86f+.14f*side*side;
                float fromTop=1f-y/(float)(height-1);
                float alpha=Mathf.SmoothStep(0,1,(edge-fromTop)/.035f);
                pixels[y*width+x]=new Color(.018f,.009f,.012f,alpha);
            }
            eyelidTexture.SetPixels(pixels);eyelidTexture.Apply(false,true);
        }
        void OnDestroy(){if(eyelidTexture)Destroy(eyelidTexture);}

        public void Receive(MeleeAttack attack, Vector3 direction, Vector3 point)
        { ApplyHit(attack,direction,point,true); }
        public void ReceiveNetwork(MeleeAttack attack,Vector3 direction,Vector3 point)
        { ApplyHit(attack,direction,point,false); }
        void ApplyHit(MeleeAttack attack,Vector3 direction,Vector3 point,bool applyImpulse)
        {
            if(attack==MeleeAttack.None || !canReceiveHits)return;
            ReceivedHits++;LastAttack=attack;LastHitTime=Time.time;
            direction=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;
            if(direction.sqrMagnitude<.01f)direction=-transform.forward;
            bool kick=attack==MeleeAttack.Kick;
            LastImpulse=kick?direction*kickHorizontalSpeed+Vector3.up*kickLiftSpeed:Vector3.zero;
            Vector3 local=transform.InverseTransformDirection(direction);
            ReactionName=kick?"Knockback":Mathf.Abs(local.x)>Mathf.Abs(local.z)?(local.x>0?"HitLeft":"HitRight"):(local.z<0?"HitFront":"HitBack");
            GetComponent<MeleeCombat>()?.InterruptAttack();
            var motor = GetComponent<SourceMotor>();
            if(applyImpulse&&motor && kick){motor.ApplyStagger(.12f);motor.AddImpulse(LastImpulse);}
            else if(applyImpulse&&kick){var body=GetComponent<Rigidbody>();if(body && !body.isKinematic)body.AddForceAtPosition(LastImpulse,point,ForceMode.VelocityChange);}
            if(!kick)StartPunchBlink();
            PlayImpact(kick);
        }
        void PlayImpact(bool kick)
        {
            if(!audioSet || !impactSource)return;
            var clips=kick?audioSet.kicks:audioSet.punches;
            if(clips==null || clips.Length==0)return;
            var clip=clips[(ReceivedHits-1)%clips.Length];if(!clip)return;
            impactSource.pitch=1f+((ReceivedHits%3)-1)*.035f;
            impactSource.PlayOneShot(clip,kick?audioSet.kickVolume:audioSet.punchVolume);
            LastSound=clip;SoundsPlayed++;
        }
        public void ResetReaction(){LastAttack=MeleeAttack.None;ReactionName="";LastHitTime=-100;LastImpulse=Vector3.zero;ClearPunchBlink();if(impactSource)impactSource.Stop();}
    }
}
