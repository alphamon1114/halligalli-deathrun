using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace HalliGalli.Movement
{
    public enum MeleeAttack { None=0, PushLeft=1, PushRight=2, Kick=3 }

    [RequireComponent(typeof(SourceMotor))]
    public sealed class MeleeCombat : MonoBehaviour
    {
        public const float KickCooldown=2f;
        public Camera aimCamera;
        public bool readInput = true;
        [HideInInspector] public bool actionsEnabled=true;
        [HideInInspector] public bool simulateAutomatically = true;
        public LayerMask hitMask = ~((1 << CharacterMotionDriver.BodyLayer) | (1 << CharacterMotionDriver.HandsLayer));
        public MeleeAttack CurrentAttack { get; private set; }
        public float Elapsed { get; private set; }
        public int AttackSequence { get; private set; }
        public int HitCount { get; private set; }
        public float HitMarkerRemaining { get; private set; }
        public float KickCooldownRemaining { get; private set; }
        public bool NextPushIsLeft { get; private set; } = true;
        public bool HasBufferedAttack => buffered != MeleeAttack.None;
        public bool IsAttacking => CurrentAttack != MeleeAttack.None;
        public static float Duration(MeleeAttack attack) => attack == MeleeAttack.Kick ? 1.1f : .55f;
        public static float ContactStart(MeleeAttack attack) => attack == MeleeAttack.Kick ? .32f : .12f;
        public static float ContactEnd(MeleeAttack attack) => attack == MeleeAttack.Kick ? .42f : .20f;
        public static float Reach(MeleeAttack attack) => attack == MeleeAttack.Kick ? 1.65f : 1.25f;
        public static float PushSpeed(MeleeAttack attack) => attack == MeleeAttack.Kick ? 22.5f : 4.5f;
        readonly Collider[] overlaps = new Collider[32];
        readonly RaycastHit[] castHits = new RaycastHit[64];
        SourceMotor motor;
        SourcePlayerController controller;
        CourseParticipant participant;
        MeleeAttack buffered;
        bool resolved, focused = true;
        [System.NonSerialized] public Vector3? networkAim;
        public void ApplyNetworkAttack(MeleeAttack attack,float elapsed,int sequence,int hits,float kickCooldown)
        {
            CurrentAttack=attack;Elapsed=elapsed;AttackSequence=sequence;
            KickCooldownRemaining=kickCooldown;
            if(hits>HitCount)HitMarkerRemaining=.16f;
            else HitMarkerRemaining=Mathf.Max(0,HitMarkerRemaining-Time.deltaTime);
            HitCount=hits;
        }

        void Awake() { motor = GetComponent<SourceMotor>(); controller = GetComponent<SourcePlayerController>(); }
        void Update()
        {
            if (!readInput) return;
            if(!participant)participant=GetComponent<CourseParticipant>();
            if (!focused || !controller || !controller.enabled || !controller.PointerActionsAllowed || CourseNetwork.CapturesPointer ||
                Time.timeScale <= 0) { buffered = MeleeAttack.None; return; }
            bool primary=ControlBindings.Pressed(GameAction.Primary), aim=false;
            bool kick=ControlBindings.Pressed(GameAction.Kick),lasso=ControlBindings.Pressed(GameAction.Lasso);
            if(lasso){ProcessLassoInput();return;}
            ProcessPointerInput(primary,aim,kick);
        }
        public bool ProcessLassoInput()
        {
            if(!participant)participant=GetComponent<CourseParticipant>();
            if(!participant)return false;
            return participant.Lasso.Phase==LassoPhase.Latched
                ?participant.Lasso.TryPull(controller?controller.ConsumeRopeGesture():Vector3.zero)
                :participant.Lasso.TryThrow(controller?controller.AimRotation*Vector3.forward:networkAim??transform.forward);
        }
        public void ProcessPointerInput(bool primaryPressed,bool aimHeld,bool kickPressed)
        {
            if(!participant)participant=GetComponent<CourseParticipant>();
            if(participant&&(participant.IsSeated||participant.IsRespawning))return;
            if(kickPressed){TryKick();return;}
            if(primaryPressed)TryPush();
        }
        void FixedUpdate()
        {
            if(simulateAutomatically)SimulateAttack(Time.fixedDeltaTime);
            else if(CourseNetwork.Active&&!CourseNetwork.Authority){
                KickCooldownRemaining=Mathf.Max(0,KickCooldownRemaining-Time.fixedDeltaTime);
                HitMarkerRemaining=Mathf.Max(0,HitMarkerRemaining-Time.fixedDeltaTime);
                if(IsAttacking)Elapsed=Mathf.Min(Duration(CurrentAttack),Elapsed+Time.fixedDeltaTime);
            }
        }
        public bool TryPush()
        {
            if(!CanAct())return false;
            if(CourseNetwork.RouteCombat(this,false))return true;
            var next = NextPushIsLeft ? MeleeAttack.PushLeft : MeleeAttack.PushRight;
            if (!Request(next)) return false;
            NextPushIsLeft = !NextPushIsLeft;
            return true;
        }
        public bool TryKick()
        {
            if(!CanAct()||KickCooldownRemaining>0)return false;
            if(CourseNetwork.RouteCombat(this,true))return true;
            return Request(MeleeAttack.Kick);
        }
        bool CanAct()
        {
            if(!participant)participant=GetComponent<CourseParticipant>();
            return isActiveAndEnabled && actionsEnabled && !motor.Staggered && !motor.Stunned &&
                (!controller || controller.actionsEnabled) && (!participant || !participant.IsSeated && !participant.IsRespawning);
        }
        bool Request(MeleeAttack attack)
        {
            if(attack==MeleeAttack.Kick&&KickCooldownRemaining>0)return false;
            if (!IsAttacking) return Begin(attack);
            // One deliberate press in the last 160 ms is carried into the next attack.
            if (buffered != MeleeAttack.None || Duration(CurrentAttack) - Elapsed > .16f) return false;
            buffered = attack; return true;
        }
        bool Begin(MeleeAttack attack)
        {
            if(attack==MeleeAttack.Kick&&KickCooldownRemaining>0)return false;
            var p=GetComponent<CourseParticipant>();if(p&&p.Lasso)p.Lasso.CancelConnection();
            CurrentAttack = attack; Elapsed = 0; resolved = false; AttackSequence++;
            if(attack==MeleeAttack.Kick)KickCooldownRemaining=KickCooldown;
            return true;
        }
        public void SimulateAttack(float dt)
        {
            if(dt<=0)return;
            KickCooldownRemaining=Mathf.Max(0,KickCooldownRemaining-dt);
            HitMarkerRemaining = Mathf.Max(0, HitMarkerRemaining - dt);
            if(motor.Stunned){InterruptAttack();return;}
            if (!IsAttacking) return;
            float previous = Elapsed; Elapsed += dt;
            // A long physics tick must not skip the contact window.
            if (!resolved && Elapsed >= ContactStart(CurrentAttack) && previous <= ContactEnd(CurrentAttack)) ResolveContact();
            if (Elapsed < Duration(CurrentAttack)) return;
            CurrentAttack = MeleeAttack.None; Elapsed = 0;
            if (buffered != MeleeAttack.None) { var next = buffered; buffered = MeleeAttack.None; if(CanAct())Begin(next); }
        }
        void ResolveContact()
        {
            Vector3 forward = networkAim??(aimCamera ? aimCamera.transform.forward : transform.forward);
            Vector3 origin = motor.CurrentEyePosition - Vector3.up * (CurrentAttack == MeleeAttack.Kick ? .38f : .16f);
            float radius = CurrentAttack == MeleeAttack.Kick ? .22f : .18f;
            Collider nearest = null; Vector3 point = origin; float distance = float.PositiveInfinity;
            // Saturated non-alloc queries fail closed: an omitted wall must never permit a hit.
            int count = Physics.OverlapSphereNonAlloc(origin, radius, overlaps, hitMask, QueryTriggerInteraction.Ignore);
            if(count==overlaps.Length){resolved=true;return;}
            // Overlap handles a target already touching the capsule, which a sphere cast can omit.
            for(int i=0;i<count;i++) {
                var c=overlaps[i];
                if (c.transform.IsChildOf(transform)) continue;
                if (Vector3.Dot(c.bounds.center - origin, forward) < 0) continue;
                // A solid wall sharing the starting volume takes priority over a target.
                if(nearest && c.GetComponentInParent<MeleeReceiver>())continue;
                nearest = c; point = c.ClosestPoint(origin); distance = 0;
            }
            count = Physics.SphereCastNonAlloc(origin, radius, forward, castHits, Reach(CurrentAttack), hitMask, QueryTriggerInteraction.Ignore);
            if(count==castHits.Length){resolved=true;return;}
            for(int i=0;i<count;i++) {
                var hit=castHits[i];
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= distance) continue;
                nearest = hit.collider; point = hit.point; distance = hit.distance;
            }
            if (!nearest) return;
            // The closest solid surface consumes the strike, even when it is a wall.
            resolved = true;
            var receiver = nearest.GetComponentInParent<MeleeReceiver>();
            if (!receiver || !receiver.canReceiveHits || receiver.transform.IsChildOf(transform)) return;
            Vector3 sight = point - origin;
            count = Physics.RaycastNonAlloc(origin, sight.normalized, castHits, sight.magnitude, hitMask, QueryTriggerInteraction.Ignore);
            if(count==castHits.Length)return;
            for(int i=0;i<count;i++) {
                var obstruction=castHits[i];
                if (!obstruction.collider.transform.IsChildOf(transform) && obstruction.collider != nearest &&
                    obstruction.collider.GetComponentInParent<MeleeReceiver>() != receiver && obstruction.distance < sight.magnitude - .025f) return;
            }
            Vector3 push = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (push.sqrMagnitude < .01f) push = transform.forward;
            if(!receiver.Receive(CurrentAttack, push, point))return;
            if(CourseNetwork.Active&&CourseNetwork.Authority)CourseNetwork.Instance.SendHit(this,receiver,CurrentAttack,push,point);
            HitCount++; HitMarkerRemaining = .16f;
        }
        public void ResetCombat(bool resetCooldown=false)
        {
            CurrentAttack = buffered = MeleeAttack.None; Elapsed = 0; HitMarkerRemaining = 0;
            NextPushIsLeft = true; resolved = false;
            // Falls, seating and cancellation clear attacks, not their spent cooldown.
            if(resetCooldown)KickCooldownRemaining=0;
        }
        public void InterruptAttack(){CurrentAttack=buffered=MeleeAttack.None;Elapsed=0;resolved=false;}
        void OnApplicationFocus(bool value) { focused = value; if (!value) buffered = MeleeAttack.None; }
        void OnDisable() => ResetCombat();
    }
}
