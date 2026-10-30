using UnityEngine;

namespace HalliGalli.Movement
{
    [CreateAssetMenu(menuName = "HalliGalli/Movement Profile", fileName = "SourceMovementProfile")]
    public sealed class SourceMovementProfile : ScriptableObject
    {
        [Header("Ground movement (metres / seconds)")]
        [UnityEngine.Serialization.FormerlySerializedAs("walkSpeed")]
        [Min(0.1f)] public float moveSpeed = 4.5f;
        [Min(0.1f)] public float crouchSpeed = 2.2f;
        [Min(0)] public float groundAcceleration = 14f;
        [Min(0)] public float groundFriction = 7f;
        [Min(0)] public float stopSpeed = 2.5f;

        [Header("Air movement")]
        [Min(0)] public float airAcceleration = 12f;
        [Tooltip("Limits velocity along the requested direction, not total airborne speed.")]
        [Min(0)] public float airWishSpeedCap = 0.8f;
        [Tooltip("0 allows momentum to build through manual air strafing.")]
        [Min(0)] public float maximumAirSpeed;
        [Tooltip("Let steep surfaces redirect airborne momentum. Walkable ground still uses slopeLimit.")]
        public bool surfEnabled;
        [Min(0.1f)] public float gravity = 20f;
        [Min(0.1f)] public float jumpHeight = 1f;
        [Min(1)] public float terminalFallSpeed = 40f;
        [Tooltip("Default off: release and press Space for each jump.")]
        public bool autoBunnyHop;
        [Range(0, 0.2f)] public float jumpBufferSeconds = 0.08f;

        [Header("Body and ground contact")]
        [Min(0.1f)] public float radius = 0.3f;
        [Min(0.6f)] public float standingHeight = 1.8f;
        [Min(0.6f)] public float crouchingHeight = 1f;
        [Min(0.1f)] public float standingEyeHeight = 1.65f;
        [Min(0.1f)] public float crouchingEyeHeight = 0.85f;
        [Min(0.02f)] public float crouchTransitionSeconds = 0.12f;
        public bool preserveHeadDuringAirDuck = true;
        [Range(0.005f, 0.08f)] public float skinWidth = 0.025f;
        [Range(0, 0.5f)] public float stepHeight = 0.3f;
        [Range(1, 80)] public float slopeLimit = 46f;
        [Range(0.02f, 0.4f)] public float groundSnapDistance = 0.22f;
        [Min(0.1f)] public float groundStickSpeed = 2f;
        public LayerMask collisionMask = ~0;

        [Header("View")]
        [Range(0.01f, 1f)] public float mouseSensitivity = 0.12f;
        [Range(30, 89)] public float pitchLimit = 88f;
        [Range(60, 110)] public float fieldOfView = 85f;

        private void OnValidate()
        {
            standingHeight = Mathf.Max(standingHeight, radius * 2 + 0.1f);
            crouchingHeight = Mathf.Clamp(crouchingHeight, radius * 2 + 0.02f, standingHeight);
            standingEyeHeight = Mathf.Clamp(standingEyeHeight, 0.1f, standingHeight - 0.05f);
            crouchingEyeHeight = Mathf.Clamp(crouchingEyeHeight, 0.1f, crouchingHeight - 0.05f);
        }
    }
}
