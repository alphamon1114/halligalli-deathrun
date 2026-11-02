using UnityEngine;

namespace HalliGalli.Movement
{
    [CreateAssetMenu(menuName="HalliGalli/Impact Audio Set")]
    public sealed class ImpactAudioSet : ScriptableObject
    {
        public AudioClip[] pushes, strongPushes;
        [Range(0,1)] public float pushVolume=.80f, strongPushVolume=.90f;
    }
}
