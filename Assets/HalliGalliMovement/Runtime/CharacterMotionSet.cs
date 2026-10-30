using UnityEngine;

namespace HalliGalli.Movement
{
    [CreateAssetMenu(menuName="HalliGalli/Character Motion Set")]
    public sealed class CharacterMotionSet : ScriptableObject
    {
        public AnimationClip[] bodyClips;
        public AnimationClip[] handClips;
        [Range(.03f,.3f)] public float blendTime=.12f;
    }
}
