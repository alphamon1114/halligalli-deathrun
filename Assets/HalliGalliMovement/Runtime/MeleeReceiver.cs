using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed class MeleeReceiver : MonoBehaviour
    {
        public ImpactAudioSet audioSet;
        public AudioSource impactSource;
        [HideInInspector] public bool canReceiveHits=true;
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
        public const float PushProtectionDuration=.65f;
        float protectedUntil;
        public float PushProtectionRemaining => Mathf.Max(0,protectedUntil-Time.time);

        public bool Receive(MeleeAttack attack, Vector3 direction, Vector3 point)
        {
            // Only authority decides acceptance. Rejected contacts do not refresh protection.
            if(CourseNetwork.Active&&!CourseNetwork.Authority || !CanReceive(attack) || Time.time<protectedUntil)return false;
            protectedUntil=Time.time+PushProtectionDuration;
            ApplyHit(attack,direction,point,true);
            return true;
        }
        public void ReceiveNetwork(MeleeAttack attack,Vector3 direction,Vector3 point)
        {
            // The host already accepted this event; arrival spacing must not filter it again.
            // Position/velocity arrive in motor snapshots, never in this presentation message.
            if(CanReceive(attack))ApplyHit(attack,direction,point,false);
        }
        bool CanReceive(MeleeAttack attack)
        {
            return canReceiveHits && (attack==MeleeAttack.PushLeft || attack==MeleeAttack.PushRight || attack==MeleeAttack.Kick);
        }
        void ApplyHit(MeleeAttack attack,Vector3 direction,Vector3 point,bool applyImpulse)
        {
            ReceivedHits++;LastAttack=attack;LastHitTime=Time.time;
            direction=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;
            if(direction.sqrMagnitude<.01f)direction=Vector3.ProjectOnPlane(-transform.forward,Vector3.up).normalized;
            if(direction.sqrMagnitude<.01f)direction=Vector3.back;
            bool strong=attack==MeleeAttack.Kick;
            LastImpulse=direction*MeleeCombat.PushSpeed(attack);
            Vector3 local=transform.InverseTransformDirection(direction);
            ReactionName=strong?"Knockback":Mathf.Abs(local.x)>Mathf.Abs(local.z)?(local.x>0?"HitLeft":"HitRight"):(local.z<0?"HitFront":"HitBack");
            if(applyImpulse){
                var motor=GetComponent<SourceMotor>();
                if(motor)motor.AddImpulse(LastImpulse);
                else {var body=GetComponent<Rigidbody>();if(body&&!body.isKinematic)body.AddForceAtPosition(LastImpulse,point,ForceMode.VelocityChange);}
            }
            PlayImpact(strong);
        }
        void PlayImpact(bool strong)
        {
            if(!audioSet || !impactSource)return;
            var clips=strong?audioSet.strongPushes:audioSet.pushes;
            if(clips==null || clips.Length==0)return;
            var clip=clips[(ReceivedHits-1)%clips.Length];if(!clip)return;
            impactSource.pitch=1f+((ReceivedHits%3)-1)*.035f;
            impactSource.PlayOneShot(clip,strong?audioSet.strongPushVolume:audioSet.pushVolume);
            LastSound=clip;SoundsPlayed++;
        }
        public void ResetReaction(){LastAttack=MeleeAttack.None;ReactionName="";LastHitTime=-100;LastImpulse=Vector3.zero;protectedUntil=0;if(impactSource)impactSource.Stop();}
    }
}
