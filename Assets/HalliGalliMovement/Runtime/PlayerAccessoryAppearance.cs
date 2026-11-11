using UnityEngine;

namespace HalliGalli.Movement
{
    [DisallowMultipleComponent]
    public sealed class PlayerAccessoryAppearance : MonoBehaviour
    {
        GameObject worn;
        Transform bodyRoot;
        public int Index { get; private set; }
        public bool Worn => worn;

        public static void Apply(CourseParticipant participant, int index)
        {
            if (!participant || !PlayerAccessories.IsValid(index)) return;
            var appearance = participant.GetComponent<PlayerAccessoryAppearance>();
            if (!appearance) {
                if (index == PlayerAccessories.None) return;
                appearance = participant.gameObject.AddComponent<PlayerAccessoryAppearance>();
            }
            appearance.Set(index);
        }

        void Set(int index)
        {
            var driver = GetComponent<CharacterMotionDriver>();
            var body = GetComponent<CourseBodyAnimator>();
            var root = driver && driver.bodyModel ? driver.bodyModel : body ? body.model : null;
            if (!root) return;
            if (Index == index && bodyRoot == root && (index == PlayerAccessories.None || worn)) return;
            if (worn) Destroy(worn);
            worn = null;
            Index = index;
            bodyRoot = root;
            var prefab = PlayerAccessories.Prefab(index);
            if (!prefab) return;
            Transform head = null;
            foreach (var bone in root.GetComponentsInChildren<Transform>(true))
                if (bone.name == "Head") { head = bone; break; }
            if (!head) return;
            worn = Instantiate(prefab, head, false);
            // Follow the body's layer: the local first-person camera hides its own body and accessory together.
            var skin = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            int layer = skin ? skin.gameObject.layer : root.gameObject.layer;
            foreach (var part in worn.GetComponentsInChildren<Transform>(true))
                part.gameObject.layer = layer;
        }

        void OnDestroy() { if (worn) Destroy(worn); }
    }
}
