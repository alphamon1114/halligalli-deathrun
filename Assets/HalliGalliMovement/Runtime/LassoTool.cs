using System;
using UnityEngine;

namespace HalliGalli.Movement
{
    public enum LassoPhase : byte { Idle, Flying, Latched }

    [DisallowMultipleComponent, DefaultExecutionOrder(160)]
    public sealed class LassoTool : MonoBehaviour
    {
        public const float CooldownSeconds=5, Range=12, FlightSpeed=28, BreakDistance=16;
        public const float PullSpeed=32, AutoPullDelay=.12f;
        public const float PlayerHitRadius=.32f, WorldHitRadius=.06f;
        public LassoPhase Phase {get;private set;}
        public CourseParticipant Target {get;private set;}
        public float CooldownRemaining {get;private set;}
        public int Casts {get;private set;}
        public int Hits {get;private set;}
        public int Pulls {get;private set;}
        public Vector3 Tip {get;private set;}
        public Vector3 CastOrigin {get;private set;}
        public Vector3 CastDirection {get;private set;}
        public Vector3 LastImpulse {get;private set;}
        public float CastAt {get;private set;}=-100;
        public float PullAt {get;private set;}=-100;
        public bool ConnectionActive=>Phase!=LassoPhase.Idle;
        public bool MotionActive=>Time.time-CastAt<.4f||Time.time-PullAt<.45f;
        public bool HitFeedbackActive=>Phase==LassoPhase.Latched||Time.time-PullAt<.3f;
        public string Motion=>Time.time-PullAt<.45f?"LassoPull":Time.time-CastAt<.4f?"LassoThrow":ConnectionActive?"LassoHold":"";
        public float MotionTime=>Motion=="LassoPull"?Time.time-PullAt:Motion=="LassoThrow"?Time.time-CastAt:0;
        public bool CanAct=>isActiveAndEnabled&&owner&&owner.Racing&&owner.Combat.actionsEnabled&&!owner.Motor.Stunned&&!owner.Motor.Staggered&&(!CourseNetwork.Active||CourseNetwork.Instance.Playing);
        public bool CanThrow=>CanAct&&!ConnectionActive&&CooldownRemaining<=0&&!owner.Combat.IsAttacking;
        public bool SimulateAutomatically=true;
        CourseParticipant owner;LassoAudio audio;float travelled,pullDelay;
        static int Mask=>~((1<<CharacterMotionDriver.BodyLayer)|(1<<CharacterMotionDriver.HandsLayer));
        void Awake(){owner=GetComponent<CourseParticipant>();audio=gameObject.AddComponent<LassoAudio>();gameObject.AddComponent<LassoVisual>();}
        void FixedUpdate(){if(SimulateAutomatically)Simulate(Time.fixedDeltaTime);}
        public void BeginRace(){EndRace();}
        public void EndRace(){CancelConnection();CooldownRemaining=0;CastAt=PullAt=-100;if(audio)audio.Stop();}
        public void CancelConnection(){Phase=LassoPhase.Idle;Target=null;pullDelay=0;}
        public void ReleaseIfTarget(CourseParticipant participant){if(Target==participant)CancelConnection();}
        public bool TryThrow(Vector3 direction)
        {
            if(!CanThrow||!Valid(direction))return false;
            if(CourseNetwork.Active&&!CourseNetwork.Authority){CourseNetwork.Instance.LocalAction(NetworkAction.LassoThrow,true,direction);return true;}
            owner.Combat.InterruptAttack();
            CooldownRemaining=CooldownSeconds;Casts++;CastAt=Time.time;
            CastOrigin=Tip=owner.Motor.CurrentEyePosition;CastDirection=direction.normalized;
            travelled=0;Target=null;Phase=LassoPhase.Flying;Sound(LassoSound.Throw,Casts,CastOrigin);return true;
        }
        void Pull()
        {
            Vector3 pull=owner.transform.position-Target.transform.position;pull.y=0;pull.Normalize();
            // Overcome momentum away from the caster, retaining sideways and vertical speed.
            LastImpulse=pull*Mathf.Max(PullSpeed,PullSpeed-Vector3.Dot(Target.Motor.Velocity,pull));
            Target.Motor.AddImpulse(LastImpulse);
            Target.Combat.InterruptAttack();
            Tip=Anchor(Target);Pulls++;PullAt=Time.time;Sound(LassoSound.Pull,Pulls,owner.Motor.CurrentEyePosition,Target);CancelConnection();
        }
        public void Simulate(float dt)
        {
            if(dt<=0)return;
            CooldownRemaining=Mathf.Max(0,CooldownRemaining-dt);
            if(CourseNetwork.Active&&!CourseNetwork.Authority)return;
            if(!CanAct){CancelConnection();return;}
            if(Phase==LassoPhase.Latched){
                if(!ConnectionValid()){CancelConnection();return;}
                Tip=Anchor(Target);pullDelay-=dt;
                if(pullDelay<=.00001f)Pull();
                return;
            }
            if(Phase!=LassoPhase.Flying)return;
            float step=Mathf.Min(FlightSpeed*dt,Range-travelled);
            // Wider on players, narrow on scenery: forgiving edge hits without snagging a
            // nearby ramp. Initial overlaps count too, so touching opponents can be caught.
            float obstacle=step+.001f;
            foreach(var c in Physics.OverlapSphere(Tip,WorldHitRadius,Mask,QueryTriggerInteraction.Ignore))
                if(!c.GetComponentInParent<CourseParticipant>())obstacle=0;
            foreach(var hit in Physics.SphereCastAll(Tip,WorldHitRadius,CastDirection,step,Mask,QueryTriggerInteraction.Ignore))
                if(!hit.collider.GetComponentInParent<CourseParticipant>())obstacle=Mathf.Min(obstacle,hit.distance);
            CourseParticipant nearest=null;float nearestDistance=obstacle;
            void Candidate(CourseParticipant candidate,float distance)
            {
                if(!Eligible(candidate)||distance>=nearestDistance)return;
                // The generous player radius never reaches through a solid wall.
                var point=Tip+CastDirection*distance;float radius=candidate.Motor.profile.radius;
                var axis=candidate.transform.position+Vector3.up*Mathf.Clamp(point.y-candidate.transform.position.y,radius,candidate.Motor.BodyHeight-radius);
                var contact=axis+Vector3.ClampMagnitude(point-axis,radius);
                var sight=contact-Tip;
                foreach(var h in Physics.RaycastAll(Tip,sight.normalized,sight.magnitude,Mask,QueryTriggerInteraction.Ignore))
                    if(!h.collider.GetComponentInParent<CourseParticipant>())return;
                nearest=candidate;nearestDistance=distance;
            }
            // CharacterController query shapes can trail Move inside a physics tick. Use
            // the live motor capsule for targets, and PhysX only for world obstruction.
            foreach(var p in owner.session.participants)if(Eligible(p))Candidate(p,CapsuleDistance(p,step));
            if(nearest){
                Target=nearest;Tip=Anchor(nearest);
                if(ConnectionValid()){Hits++;Phase=LassoPhase.Latched;pullDelay=AutoPullDelay;Sound(LassoSound.Latch,Hits,Tip,Target);return;}
                CancelConnection();return;
            }
            if(obstacle<=step){Tip+=CastDirection*obstacle;CancelConnection();return;}
            Tip+=CastDirection*step;travelled+=step;
            if(travelled>=Range-.001f)CancelConnection();
        }
        bool Eligible(CourseParticipant target)=>target&&target!=owner&&target.isActiveAndEnabled&&target.session==owner.session&&target.Racing&&target.Receiver.canReceiveHits;
        float CapsuleDistance(CourseParticipant p,float step)
        {
            float bodyRadius=p.Motor.profile.radius,radius=bodyRadius+PlayerHitRadius;
            Vector3 bottom=p.transform.position+Vector3.up*bodyRadius,top=p.transform.position+Vector3.up*(p.Motor.BodyHeight-bodyRadius);
            var axis=new Vector3(bottom.x,Mathf.Clamp(Tip.y,bottom.y,top.y),bottom.z);
            if((Tip-axis).sqrMagnitude<=radius*radius&&Vector3.Dot(Anchor(p)-Tip,CastDirection)>-.1f)return 0;
            float best=float.PositiveInfinity;
            void Root(float t){if(t>=0&&t<=step)best=Mathf.Min(best,t);}
            void Sphere(Vector3 center){
                var offset=Tip-center;float b=Vector3.Dot(offset,CastDirection),c=offset.sqrMagnitude-radius*radius,d=b*b-c;
                if(d>=0)Root(-b-Mathf.Sqrt(d));
            }
            Sphere(bottom);Sphere(top);
            var flat=Vector3.ProjectOnPlane(CastDirection,Vector3.up);var delta=Vector3.ProjectOnPlane(Tip-bottom,Vector3.up);
            float a=flat.sqrMagnitude,b2=Vector3.Dot(delta,flat),c2=delta.sqrMagnitude-radius*radius,disc=b2*b2-a*c2;
            if(a>.00001f&&disc>=0){
                float t=(-b2-Mathf.Sqrt(disc))/a,y=Tip.y+CastDirection.y*t;
                if(y>=bottom.y&&y<=top.y)Root(t);
            }
            return best;
        }
        bool ConnectionValid()
        {
            if(!Eligible(Target))return false;
            Vector3 origin=owner.Motor.CurrentEyePosition,delta=Anchor(Target)-origin;
            if(delta.magnitude>BreakDistance)return false;
            // A corner, closed door, or platform edge breaks the tether instead of pulling
            // somebody through it. Other players do not sever a rope already attached.
            foreach(var hit in Physics.RaycastAll(origin,delta.normalized,delta.magnitude,Mask,QueryTriggerInteraction.Ignore))
                if(!hit.collider.GetComponentInParent<CourseParticipant>())return false;
            return true;
        }
        public static Vector3 Anchor(CourseParticipant p)=>p.transform.position+Vector3.up*(p.Motor.BodyHeight*.65f);
        void Sound(LassoSound sound,int sequence,Vector3 position,CourseParticipant target=null)
        {
            audio.Play(sound,sequence,position,target&&target.localPlayer);
            if(CourseNetwork.Active&&CourseNetwork.Authority)CourseNetwork.Instance.SendLassoSound(owner,target,sound,sequence,position);
        }
        static bool Valid(Vector3 v)=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z)&&!float.IsInfinity(v.x)&&!float.IsInfinity(v.y)&&!float.IsInfinity(v.z)&&v.sqrMagnitude>.01f&&v.sqrMagnitude<10000;
        public struct NetworkState : Unity.Netcode.INetworkSerializeByMemcpy
        {
            public int casts,hits,pulls,target;
            public float cooldown,castAge,pullAge;
            public LassoPhase phase;
            public Vector3 tip,origin,direction;
        }
        public NetworkState CaptureNetworkState()=>new NetworkState{casts=Casts,hits=Hits,pulls=Pulls,target=Target?Array.IndexOf(owner.session.participants,Target):-1,
            cooldown=CooldownRemaining,castAge=Time.time-CastAt,pullAge=Time.time-PullAt,phase=Phase,tip=Tip,origin=CastOrigin,direction=CastDirection};
        public void ApplyNetwork(NetworkState state)
        {
            if(state.casts!=Casts)CastAt=Time.time-state.castAge;
            if(state.pulls!=Pulls)PullAt=Time.time-state.pullAge;
            Casts=state.casts;Hits=state.hits;Pulls=state.pulls;CooldownRemaining=state.cooldown;
            Phase=state.phase;Tip=state.tip;CastOrigin=state.origin;CastDirection=state.direction;
            Target=state.target>=0&&state.target<owner.session.participants.Length?owner.session.participants[state.target]:null;
            if(owner.IsSeated||owner.IsRespawning)CancelConnection();
        }
    }
}
