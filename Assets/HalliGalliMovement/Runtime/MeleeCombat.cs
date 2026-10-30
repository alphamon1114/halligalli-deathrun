using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace HalliGalli.Movement
{
    public enum MeleeAttack { None, PunchLeft, PunchRight, Kick }

    [RequireComponent(typeof(SourceMotor))]
    public sealed class MeleeCombat : MonoBehaviour
    {
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
        public bool NextPunchIsLeft { get; private set; } = true;
        public bool HasBufferedAttack => buffered != MeleeAttack.None;
        public bool IsAttacking => CurrentAttack != MeleeAttack.None;
        public static float Duration(MeleeAttack attack) => attack == MeleeAttack.Kick ? .72f : .42f;
        public static float ContactStart(MeleeAttack attack) => attack == MeleeAttack.Kick ? .27f : .14f;
        public static float ContactEnd(MeleeAttack attack) => attack == MeleeAttack.Kick ? .38f : .22f;
        public static float Reach(MeleeAttack attack) => attack == MeleeAttack.Kick ? 1.65f : 1.25f;
        SourceMotor motor;
        SourcePlayerController controller;
        CourseParticipant participant;
        MeleeAttack buffered;
        bool resolved, focused = true;
        [System.NonSerialized] public Vector3? networkAim;
        public void ApplyNetworkAttack(MeleeAttack attack,float elapsed,int sequence,int hits)
        {
            CurrentAttack=attack;Elapsed=elapsed;AttackSequence=sequence;
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
            if(primaryPressed)TryPunch();
        }
        void FixedUpdate()
        {
            if(simulateAutomatically)SimulateAttack(Time.fixedDeltaTime);
            else if(CourseNetwork.Active&&!CourseNetwork.Authority){
                HitMarkerRemaining=Mathf.Max(0,HitMarkerRemaining-Time.fixedDeltaTime);
                if(IsAttacking)Elapsed=Mathf.Min(Duration(CurrentAttack),Elapsed+Time.fixedDeltaTime);
            }
        }
        public bool TryPunch()
        {
            if(CourseNetwork.RouteCombat(this,false))return true;
            var next = NextPunchIsLeft ? MeleeAttack.PunchLeft : MeleeAttack.PunchRight;
            if (!Request(next)) return false;
            NextPunchIsLeft = !NextPunchIsLeft;
            return true;
        }
        public bool TryKick() {if(CourseNetwork.RouteCombat(this,true))return true;return Request(MeleeAttack.Kick);}
        bool Request(MeleeAttack attack)
        {
            if (!isActiveAndEnabled || !actionsEnabled || motor.Staggered || motor.Stunned || (controller && !controller.actionsEnabled)) return false;
            if (!IsAttacking) { Begin(attack); return true; }
            // One deliberate press in the last 160 ms is carried into the next attack.
            if (buffered != MeleeAttack.None || Duration(CurrentAttack) - Elapsed > .16f) return false;
            buffered = attack; return true;
        }
        void Begin(MeleeAttack attack) { var p=GetComponent<CourseParticipant>();if(p&&p.Lasso)p.Lasso.CancelConnection();CurrentAttack = attack; Elapsed = 0; resolved = false; AttackSequence++; }
        public void SimulateAttack(float dt)
        {
            HitMarkerRemaining = Mathf.Max(0, HitMarkerRemaining - dt);
            if(motor.Stunned){InterruptAttack();return;}
            if (!IsAttacking || dt <= 0) return;
            float previous = Elapsed; Elapsed += dt;
            // A long physics tick must not skip the contact window.
            if (!resolved && Elapsed >= ContactStart(CurrentAttack) && previous <= ContactEnd(CurrentAttack)) ResolveContact();
            if (Elapsed < Duration(CurrentAttack)) return;
            CurrentAttack = MeleeAttack.None; Elapsed = 0;
            if (buffered != MeleeAttack.None) { var next = buffered; buffered = MeleeAttack.None; Begin(next); }
        }
        void ResolveContact()
        {
            Vector3 forward = networkAim??(aimCamera ? aimCamera.transform.forward : transform.forward);
            Vector3 origin = motor.CurrentEyePosition - Vector3.up * (CurrentAttack == MeleeAttack.Kick ? .38f : .16f);
            float radius = CurrentAttack == MeleeAttack.Kick ? .22f : .18f;
            Collider nearest = null; Vector3 point = origin; float distance = float.PositiveInfinity;
            // Overlap handles a target already touching the capsule, which a sphere cast can omit.
            foreach (var c in Physics.OverlapSphere(origin, radius, hitMask, QueryTriggerInteraction.Ignore)) {
                if (c.transform.IsChildOf(transform)) continue;
                Vector3 p = c.ClosestPoint(origin);
                if (Vector3.Dot(c.bounds.center - origin, forward) < 0) continue;
                nearest = c; point = p; distance = 0; break;
            }
            foreach (var hit in Physics.SphereCastAll(origin, radius, forward, Reach(CurrentAttack), hitMask, QueryTriggerInteraction.Ignore)) {
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= distance) continue;
                nearest = hit.collider; point = hit.point; distance = hit.distance;
            }
            if (!nearest) return;
            // The closest solid surface consumes the strike, even when it is a wall.
            resolved = true;
            var receiver = nearest.GetComponentInParent<MeleeReceiver>();
            if (!receiver || !receiver.canReceiveHits || receiver.transform.IsChildOf(transform)) return;
            Vector3 sight = point - origin;
            foreach (var obstruction in Physics.RaycastAll(origin, sight.normalized, sight.magnitude, hitMask, QueryTriggerInteraction.Ignore))
                if (!obstruction.collider.transform.IsChildOf(transform) && obstruction.collider != nearest &&
                    obstruction.collider.GetComponentInParent<MeleeReceiver>() != receiver && obstruction.distance < sight.magnitude - .025f) return;
            Vector3 push = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (push.sqrMagnitude < .01f) push = transform.forward;
            receiver.Receive(CurrentAttack, push, point);
            if(CourseNetwork.Active&&CourseNetwork.Authority)CourseNetwork.Instance.SendHit(this,receiver,CurrentAttack,push,point);
            HitCount++; HitMarkerRemaining = .16f;
        }
        public void ResetCombat()
        {
            CurrentAttack = buffered = MeleeAttack.None; Elapsed = 0; HitMarkerRemaining = 0;
            NextPunchIsLeft = true; resolved = false;
        }
        public void InterruptAttack(){CurrentAttack=buffered=MeleeAttack.None;Elapsed=0;resolved=false;}
        void OnApplicationFocus(bool value) { focused = value; if (!value) buffered = MeleeAttack.None; }
        void OnDisable() => ResetCombat();
    }
}
