using UnityEngine;

namespace HalliGalli.Movement
{
    public struct MovementCommand
    {
        public Vector2 move;
        public bool crouch, jumpPressed, jumpHeld;
        public float yaw;
    }

    [DisallowMultipleComponent, RequireComponent(typeof(CharacterController))]
    public sealed class SourceMotor : MonoBehaviour
    {
        public SourceMovementProfile profile;
        public Vector3 Velocity { get; private set; }
        public bool Grounded { get; private set; }
        public bool IsCrouched => body != null && body.height < profile.standingHeight - 0.01f;
        // Animation follows actual momentum; there is no sprint input or speed mode.
        public bool IsRunning => !IsCrouched && HorizontalSpeed > profile.moveSpeed * 1.35f;
        public float HorizontalSpeed => new Vector2(Velocity.x, Velocity.z).magnitude;
        public float BodyHeight => body.height;
        public Vector3 PreviousEyePosition { get; private set; }
        public Vector3 CurrentEyePosition { get; private set; }
        public int JumpCount { get; private set; }
        public float StaggerRemaining {get;private set;}
        public bool Staggered => StaggerRemaining>0;
        public float StunRemaining {get;private set;}
        public float StunImmuneRemaining {get;private set;}
        public bool Stunned => StunRemaining>0;
        public float RaceSlipRemaining {get;private set;}
        public float RaceLaunchRemaining {get;private set;}
        public bool Surfing { get; private set; }
        public Vector3 SurfNormal { get; private set; }
        public int SurfContactTicks { get; private set; }

        private CharacterController body;
        private bool ownsProfile;
        private float jumpBuffer = -1;
        public void ClearJumpBuffer(){jumpBuffer=-1;}
        private Vector3 groundNormal = Vector3.up;
        private readonly RaycastHit[] groundHits = new RaycastHit[32];
        private readonly Collider[] clearanceHits = new Collider[32];
        private readonly Vector3[] contactNormals = new Vector3[12];
        private int contactCount;
        private FactorySurface groundSurface;

        private float EyeHeight => Mathf.Lerp(profile.crouchingEyeHeight, profile.standingEyeHeight,
            Mathf.InverseLerp(profile.crouchingHeight, profile.standingHeight, body.height));

        public struct NetworkState : Unity.Netcode.INetworkSerializeByMemcpy
        {
            public Vector3 position,velocity,normal,surfNormal;
            public float yaw,height,stagger,buffer,stun,stunImmunity,raceSlip,raceLaunch;
            public int jumps,surfTicks;
            public bool grounded,surfing;
        }
        public NetworkState CaptureNetworkState()=>new NetworkState{
            position=transform.position,velocity=Velocity,normal=groundNormal,surfNormal=SurfNormal,yaw=transform.eulerAngles.y,
            height=body.height,stagger=StaggerRemaining,buffer=jumpBuffer,stun=StunRemaining,stunImmunity=StunImmuneRemaining,raceSlip=RaceSlipRemaining,raceLaunch=RaceLaunchRemaining,jumps=JumpCount,surfTicks=SurfContactTicks,
            grounded=Grounded,surfing=Surfing
        };
        public void RestoreNetworkState(NetworkState state)
        {
            Initialize();bool mobile=body.enabled;body.enabled=false;
            transform.SetPositionAndRotation(state.position,Quaternion.Euler(0,state.yaw,0));
            body.height=state.height;body.center=Vector3.up*body.height*.5f;body.enabled=mobile;
            Velocity=state.velocity;Grounded=state.grounded;StaggerRemaining=state.stagger;
            StunRemaining=state.stun;StunImmuneRemaining=state.stunImmunity;RaceSlipRemaining=state.raceSlip;RaceLaunchRemaining=state.raceLaunch;
            groundNormal=state.normal;SurfNormal=state.surfNormal;Surfing=state.surfing;SurfContactTicks=state.surfTicks;
            jumpBuffer=state.buffer;JumpCount=state.jumps;CurrentEyePosition=PreviousEyePosition=transform.position+Vector3.up*EyeHeight;
        }

        private void Awake() => Initialize();

        public void Initialize()
        {
            if (body != null) return;
            body = GetComponent<CharacterController>();
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<SourceMovementProfile>();
                profile.hideFlags = HideFlags.DontSave;
                ownsProfile = true;
            }
            body.radius = profile.radius;
            body.height = profile.standingHeight;
            body.center = Vector3.up * body.height * 0.5f;
            body.skinWidth = profile.skinWidth;
            // Our ground probe still classifies only walkable slopes as grounded.
            body.slopeLimit = profile.surfEnabled ? 90 : profile.slopeLimit;
            body.stepOffset = profile.stepHeight;
            body.minMoveDistance = 0;
            CurrentEyePosition = PreviousEyePosition = transform.position + Vector3.up * EyeHeight;
        }

        private void OnDestroy()
        {
            if (ownsProfile && profile != null) Destroy(profile);
        }

        // Input is sampled separately; one command is simulated per physics tick.
        public void Simulate(MovementCommand command, float dt)
        {
            Initialize();
            if (dt <= 0 || !body.enabled) return;
            bool stunned=Stunned,sliding=RaceSlipRemaining>0,launched=RaceLaunchRemaining>0;
            AdvanceStun(dt);
            if(stunned){command.move=Vector2.zero;command.jumpPressed=command.jumpHeld=false;command.crouch=IsCrouched;jumpBuffer=-1;}
            if(Staggered) {
                command.move=Vector2.zero;command.jumpPressed=command.jumpHeld=false;
                jumpBuffer=-1;StaggerRemaining=Mathf.Max(0,StaggerRemaining-dt);
            }
            PreviousEyePosition = CurrentEyePosition;
            transform.rotation = Quaternion.Euler(0, command.yaw, 0);
            bool hadGround = Grounded;
            float gap = float.PositiveInfinity;
            bool probeSupport = ProbeGround(out gap, out groundNormal)
                && gap <= (hadGround ? profile.groundSnapDistance : 0.065f);
            // PhysX can still support the capsule on a step edge when the sphere probe misses it.
            Grounded = Velocity.y <= 0.01f && (probeSupport || (!profile.surfEnabled && hadGround && body.isGrounded));
            Surfing = false;
            SurfNormal = Vector3.zero;
            if(Grounded&&groundSurface)body.Move(groundSurface.Displacement(FactorySurface.Clock,dt));

            if(!stunned)UpdateStance(command.crouch, dt);
            if (command.jumpPressed) jumpBuffer = Mathf.Max(dt, profile.jumpBufferSeconds);
            bool wantsJump = jumpBuffer >= 0 || (profile.autoBunnyHop && command.jumpHeld);
            bool jumped = Grounded && wantsJump;
            Vector3 velocity = Velocity;
            if(launched||stunned&&Grounded&&!sliding){velocity.x=velocity.z=0;}
            if (jumped)
            {
                velocity.y = Mathf.Sqrt(2 * profile.gravity * profile.jumpHeight);
                Grounded = false;
                jumpBuffer = -1;
                JumpCount++;
            }
            else if (Grounded)
            {
                velocity = ApplyGroundFriction(velocity, sliding?.45f:profile.groundFriction, profile.stopSpeed, dt);
                velocity.y = -profile.groundStickSpeed;
            }

            Vector2 input = Vector2.ClampMagnitude(command.move, 1);
            Vector3 wish = Quaternion.Euler(0, command.yaw, 0) * new Vector3(input.x, 0, input.y);
            float speed = IsCrouched ? profile.crouchSpeed : profile.moveSpeed;
            float wishSpeed = speed * input.magnitude;
            if (wish.sqrMagnitude > 0)
            {
                Vector3 direction = wish.normalized;
                float cap = Grounded ? wishSpeed : Mathf.Min(wishSpeed, profile.airWishSpeedCap);
                float acceleration = Grounded ? profile.groundAcceleration : profile.airAcceleration;
                // Air acceleration caps the directional projection, preserving perpendicular momentum.
                velocity = Accelerate(velocity, direction, cap, acceleration * wishSpeed * dt);
            }
            if (!Grounded && profile.maximumAirSpeed > 0)
            {
                Vector3 horizontal = Vector3.ClampMagnitude(new Vector3(velocity.x, 0, velocity.z), profile.maximumAirSpeed);
                velocity.x = horizontal.x;
                velocity.z = horizontal.z;
            }
            if (!Grounded) velocity.y = Mathf.Max(velocity.y - profile.gravity * dt * 0.5f, -profile.terminalFallSpeed);

            body.stepOffset = Grounded ? Mathf.Min(profile.stepHeight, body.height - body.radius * 2) : 0;
            contactCount = 0;
            Vector3 displacementVelocity = velocity;
            if (Grounded && groundNormal.y < 0.999f)
            {
                Vector3 horizontal = new Vector3(velocity.x, 0, velocity.z);
                Vector3 tangent = Vector3.ProjectOnPlane(horizontal, groundNormal);
                if (tangent.sqrMagnitude > 0) tangent = tangent.normalized * horizontal.magnitude;
                displacementVelocity = tangent - Vector3.up * profile.groundStickSpeed;
            }
            CollisionFlags flags = body.Move(displacementVelocity * dt);
            float walkableY = Mathf.Cos(profile.slopeLimit * Mathf.Deg2Rad);
            bool walkableContact = false;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < contactCount; i++)
                {
                    Vector3 normal = contactNormals[i];
                    if (normal.y >= walkableY) { walkableContact = true; continue; }
                    if (profile.surfEnabled && normal.y > .05f) { Surfing = true; SurfNormal = normal; }
                    float into = Vector3.Dot(velocity, normal);
                    if (into < 0) velocity -= normal * into;
                }
            }
            if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0) velocity.y = 0;

            bool nearGround = !jumped && velocity.y <= 0.01f && ProbeGround(out gap, out groundNormal);
            bool canSnap = nearGround && gap <= (Grounded ? profile.groundSnapDistance : 0.065f);
            bool supportedByMove = !jumped && velocity.y <= 0.01f && walkableContact && (flags & CollisionFlags.Below) != 0;
            if (canSnap || supportedByMove)
            {
                if (canSnap && gap > 0.003f) body.Move(Vector3.down * (gap + 0.005f));
                Grounded = true;
                velocity.y = -profile.groundStickSpeed;
            }
            else
            {
                Grounded = false;
                velocity.y = Mathf.Max(velocity.y - profile.gravity * dt * 0.5f, -profile.terminalFallSpeed);
            }
            if(launched||stunned&&Grounded&&!sliding){velocity.x=velocity.z=0;}
            Velocity = velocity;
            if (Grounded) Surfing = false;
            if (Surfing) SurfContactTicks++;
            jumpBuffer -= dt;
            CurrentEyePosition = transform.position + Vector3.up * EyeHeight;
        }

        public static Vector3 Accelerate(Vector3 velocity, Vector3 direction, float directionalLimit, float increment)
        {
            float room = directionalLimit - Vector3.Dot(velocity, direction);
            return room > 0 ? velocity + direction * Mathf.Min(room, increment) : velocity;
        }

        public static Vector3 ApplyGroundFriction(Vector3 velocity, float friction, float stopSpeed, float dt)
        {
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            if (speed < 0.0001f) return new Vector3(0, velocity.y, 0);
            float remaining = Mathf.Max(0, speed - Mathf.Max(speed, stopSpeed) * friction * dt);
            return new Vector3(velocity.x * remaining / speed, velocity.y, velocity.z * remaining / speed);
        }

        private bool BlocksPlayer(Collider collider)
        {
            return collider != null && collider != body && !collider.transform.IsChildOf(transform)
                && !Physics.GetIgnoreLayerCollision(gameObject.layer, collider.gameObject.layer)
                && !Physics.GetIgnoreCollision(body, collider);
        }

        private bool ProbeGround(out float gap, out Vector3 normal)
        {
            const float lift = 0.07f;
            float radius = body.radius * 0.95f;
            float offset = lift + body.radius - radius;
            Vector3 origin = transform.position + Vector3.up * (body.radius + lift);
            int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, groundHits,
                offset + profile.groundSnapDistance, profile.collisionMask, QueryTriggerInteraction.Ignore);
            gap = float.PositiveInfinity;
            normal = Vector3.up;
            groundSurface=null;
            float walkable = Mathf.Cos(profile.slopeLimit * Mathf.Deg2Rad);
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (!BlocksPlayer(hit.collider) || hit.normal.y < walkable) continue;
                float candidate = hit.distance - offset;
                if (candidate < gap) { gap = candidate; normal = hit.normal; groundSurface=hit.collider.GetComponent<FactorySurface>(); }
            }
            return !float.IsPositiveInfinity(gap);
        }

        private void UpdateStance(bool crouchRequested, float dt)
        {
            float desired = crouchRequested ? profile.crouchingHeight : profile.standingHeight;
            if (!crouchRequested && !CanOccupyHeight(desired)) desired = body.height;
            float height = Mathf.MoveTowards(body.height, desired,
                (profile.standingHeight - profile.crouchingHeight) * dt / profile.crouchTransitionSeconds);
            if (Mathf.Abs(height - body.height) < 0.0001f) return;
            float shift = !Grounded && profile.preserveHeadDuringAirDuck ? body.height - height : 0;
            body.height = height;
            body.center = Vector3.up * height * 0.5f;
            if (shift != 0) transform.position += Vector3.up * shift;
            Physics.SyncTransforms();
        }

        private bool CanOccupyHeight(float height)
        {
            float shift = !Grounded && profile.preserveHeadDuringAirDuck ? body.height - height : 0;
            Vector3 feet = transform.position + Vector3.up * shift;
            float radius = Mathf.Max(0.05f, body.radius - profile.skinWidth);
            Vector3 bottom = feet + Vector3.up * body.radius;
            Vector3 top = feet + Vector3.up * (height - body.radius);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, clearanceHits,
                profile.collisionMask, QueryTriggerInteraction.Ignore);
            if (count == clearanceHits.Length) return false;
            for (int i = 0; i < count; i++) if (BlocksPlayer(clearanceHits[i])) return false;
            return true;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (contactCount < contactNormals.Length) contactNormals[contactCount++] = hit.normal;
        }

        public void AddImpulse(Vector3 velocityChange)
        {
            // Ground adhesion is a collision aid, not real downward momentum.
            // Do not let it cancel a launch away from the floor.
            if(Grounded && velocityChange.y>0)Velocity=new Vector3(Velocity.x,Mathf.Max(0,Velocity.y),Velocity.z);
            Velocity += velocityChange;
            if (velocityChange.y > 0) Grounded = false;
        }
        public void ApplyStagger(float duration){StaggerRemaining=Mathf.Max(StaggerRemaining,duration);jumpBuffer=-1;}
        public bool ApplyStun(float duration,float immunity)
        {
            if(StunImmuneRemaining>0)return false;
            StunRemaining=Mathf.Max(0,duration);StunImmuneRemaining=StunRemaining+Mathf.Max(0,immunity);jumpBuffer=-1;
            if(Grounded)Velocity=new Vector3(0,Velocity.y,0);
            return true;
        }
        public bool ApplyRaceLaunch(float verticalSpeed,float duration)
        {
            if(!ApplyStun(duration,.2f))return false;
            RaceSlipRemaining=0;RaceLaunchRemaining=StunRemaining;
            Velocity=Vector3.up*verticalSpeed;Grounded=false;Surfing=false;
            return true;
        }
        public void ApplyRaceSlip(Vector3 velocity,float duration)
        {
            RaceSlipRemaining=Mathf.Max(0,duration);StunRemaining=Mathf.Max(StunRemaining,RaceSlipRemaining);
            StunImmuneRemaining=Mathf.Max(StunImmuneRemaining,StunRemaining+.2f);jumpBuffer=-1;
            Velocity=new Vector3(velocity.x,Velocity.y,velocity.z);
        }
        public void ClearRaceSlip(){RaceSlipRemaining=0;}
        public void AdvanceStun(float dt){StunRemaining=Mathf.Max(0,StunRemaining-dt);StunImmuneRemaining=Mathf.Max(0,StunImmuneRemaining-dt);RaceSlipRemaining=Mathf.Max(0,RaceSlipRemaining-dt);RaceLaunchRemaining=Mathf.Max(0,RaceLaunchRemaining-dt);}
        public void ClearStun(){StunRemaining=StunImmuneRemaining=RaceSlipRemaining=RaceLaunchRemaining=0;}

        public void Teleport(Vector3 feetPosition)
        {
            Initialize();
            body.enabled = false;
            transform.position = feetPosition;
            body.enabled = true;
            Physics.SyncTransforms();
            Velocity = Vector3.zero;
            StaggerRemaining=0;
            RaceSlipRemaining=RaceLaunchRemaining=0;
            Grounded = false;
            Surfing = false;
            SurfNormal = Vector3.zero;
            jumpBuffer = -1;
            CurrentEyePosition = PreviousEyePosition = transform.position + Vector3.up * EyeHeight;
        }
    }
}
