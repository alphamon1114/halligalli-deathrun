using UnityEngine;

namespace HalliGalli.Movement
{
    [RequireComponent(typeof(BoxCollider)), DefaultExecutionOrder(200)]
    public sealed class SurfDivider : MonoBehaviour
    {
        public CourseSession session;
        BoxCollider wall;

        void Awake() { wall = GetComponent<BoxCollider>(); }

        void FixedUpdate()
        {
            if (!CourseNetwork.Authority || !session || !session.RunActive || !wall.enabled) return;
            foreach (var participant in session.participants) {
                if (!participant.Racing) continue;
                var body = participant.GetComponent<CharacterController>();
                if (!body.enabled) continue;
                // CharacterController stops one skin width short of a solid wall.
                // Include that contact gap, without extending the hazard into the open turn.
                var contactBounds = wall.bounds;
                contactBounds.Expand(2 * (body.skinWidth + .01f));
                if (contactBounds.Intersects(body.bounds)) participant.BeginFall();
            }
        }
    }
}
