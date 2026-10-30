using UnityEngine;

namespace HalliGalli.Movement
{
    public enum ClimbJump { Basic, Duck, Hop, Strafe }
    // AI hint only. Human movement is governed solely by the same physical ledges.
    public sealed class ClimbStep : MonoBehaviour
    {
        public ClimbJump jump;
        public bool expert;
        public float arrivalRadius=.85f;
    }
}
