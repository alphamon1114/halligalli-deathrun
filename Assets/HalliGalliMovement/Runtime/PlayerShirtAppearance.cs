using System.Collections.Generic;
using UnityEngine;

namespace HalliGalli.Movement
{
    [DisallowMultipleComponent]
    public sealed class PlayerShirtAppearance : MonoBehaviour
    {
        public const string MaterialResource = "PlayerShirtBody";
        public const string ColorProperty = "_ShirtColor";
        static readonly int ColorId = Shader.PropertyToID(ColorProperty);
        static Material shirtMaterial;
        static bool missingMaterialReported;

        sealed class Binding
        {
            public SkinnedMeshRenderer renderer;
            public Material[] originals, installed;
            public int[] slots;
            public MaterialPropertyBlock[] previous;
        }
        readonly List<Binding> bindings = new List<Binding>();
        MaterialPropertyBlock properties;
        Transform bodyRoot;
        bool installed;
        public int ColorIndex { get; private set; } = -1;
        public int RendererCount => installed ? bindings.Count : 0;
        public Color AppliedColor {
            get {
                if (!installed || bindings.Count == 0 || !bindings[0].renderer) return Color.clear;
                bindings[0].renderer.GetPropertyBlock(properties, bindings[0].slots[0]);
                return properties.GetColor(ColorId);
            }
        }

        public static void Apply(CourseParticipant participant, int index)
        {
            if (!participant || !PlayerShirtColors.IsValid(index)) return;
            var appearance = participant.GetComponent<PlayerShirtAppearance>();
            if (!appearance) appearance = participant.gameObject.AddComponent<PlayerShirtAppearance>();
            appearance.SetColor(index);
        }

        void SetColor(int index)
        {
            var driver = GetComponent<CharacterMotionDriver>();
            var body = GetComponent<CourseBodyAnimator>();
            var root = driver && driver.bodyModel ? driver.bodyModel : body ? body.model : null;
            if (!root) return;
            if (bodyRoot != root) {
                Restore();
                bindings.Clear();
                bodyRoot = null;
                if (!Initialize(root)) return;
            }
            if (ColorIndex == index && installed) return;
            ColorIndex = index;
            if (isActiveAndEnabled) Install();
        }

        bool Initialize(Transform root)
        {
            if (!shirtMaterial) shirtMaterial = Resources.Load<Material>(MaterialResource);
            if (!shirtMaterial) {
                if (!missingMaterialReported) {
                    Debug.LogError("Missing PlayerShirtBody material. Run PlayerShirtAssets.Install before building.");
                    missingMaterialReported = true;
                }
                return false;
            }
            if (properties == null) properties = new MaterialPropertyBlock();
            // Only the authored full-body root: the first-person hands and kick-only mesh
            // intentionally retain their original materials, even when currently inactive.
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                var originals = renderer.sharedMaterials;
                int count = 0;
                for (int slot = 0; slot < originals.Length; slot++)
                    if (IsBodyMaterial(originals[slot])) count++;
                if (count == 0) continue;
                var binding = new Binding {
                    renderer = renderer, originals = originals, installed = (Material[])originals.Clone(),
                    slots = new int[count], previous = new MaterialPropertyBlock[count]
                };
                int next = 0;
                for (int slot = 0; slot < originals.Length; slot++) {
                    if (!IsBodyMaterial(originals[slot])) continue;
                    binding.slots[next] = slot;
                    binding.previous[next] = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(binding.previous[next], slot);
                    binding.installed[slot] = shirtMaterial;
                    next++;
                }
                bindings.Add(binding);
            }
            if (bindings.Count == 0) return false;
            bodyRoot = root;
            return true;
        }

        static bool IsBodyMaterial(Material material) => material &&
            material.mainTexture == shirtMaterial.mainTexture &&
            (material.shader.name == "Standard" || material.shader == shirtMaterial.shader);

        void Install()
        {
            if (!bodyRoot || !PlayerShirtColors.IsValid(ColorIndex)) return;
            var color = PlayerShirtColors.ColorAt(ColorIndex);
            foreach (var binding in bindings) {
                if (!binding.renderer) continue;
                if (!installed) binding.renderer.sharedMaterials = binding.installed;
                foreach (int slot in binding.slots) {
                    binding.renderer.GetPropertyBlock(properties, slot);
                    properties.SetColor(ColorId, color);
                    binding.renderer.SetPropertyBlock(properties, slot);
                }
            }
            installed = true;
        }

        void Restore()
        {
            if (!installed) return;
            foreach (var binding in bindings) {
                if (!binding.renderer) continue;
                var current = binding.renderer.sharedMaterials;
                for (int i = 0; i < binding.slots.Length; i++) {
                    int slot = binding.slots[i];
                    if (slot >= current.Length || current[slot] != shirtMaterial) continue;
                    current[slot] = binding.originals[slot];
                    var previous = binding.previous[i];
                    binding.renderer.SetPropertyBlock(previous.isEmpty ? null : previous, slot);
                }
                binding.renderer.sharedMaterials = current;
            }
            installed = false;
        }

        void OnEnable() { Install(); }
        void OnDisable() { Restore(); }
        void OnDestroy() { Restore(); }
    }
}
