using UnityEngine;

namespace HalliGalli.Movement
{
    [CreateAssetMenu(menuName="HalliGalli/Impact Audio Set")]
    public sealed class ImpactAudioSet : ScriptableObject
    {
        public AudioClip[] punches, kicks;
        [Range(0,1)] public float punchVolume=.80f, kickVolume=.90f;
    }
}
