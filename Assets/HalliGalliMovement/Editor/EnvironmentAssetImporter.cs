using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HalliGalli.EditorTools
{
    public static class EnvironmentAssetImporter
    {
        public const string Root = MovementProjectBuilder.Root + "/Art/Environment";
        public static readonly string[] Keys = {
            "WorkshopChair", "CardTable", "ServiceBell", "LandingPlatform",
            "ConcreteWall", "ConcreteFloor", "ConcreteColumn", "CeilingLight",
            "UtilityPipes", "RockLedge", "MountainRock", "MountainPine"
        };

        [Serializable] public sealed class TextureInfo { public string file, usage; public int uv; public float scale = 1, strength = 1; }
        [Serializable] public sealed class MaterialInfo {
            public string name, alphaMode;
            public int index;
            public float[] baseColorFactor, emissiveFactor;
            public float metallicFactor, roughnessFactor, alphaCutoff;
            public bool doubleSided;
            public TextureInfo baseColor, metallicRoughness, normal, occlusion, emissive;
        }
        [Serializable] public sealed class MeshInfo { public string name; public string[] materialSlots; }
        [Serializable] public sealed class AssetInfo {
            public string key, flowId, meshNodeId, sourceSha256;
            public int vertices, triangles;
            public MeshInfo[] meshes;
            public MaterialInfo[] materials;
        }

        public static GameObject Prefab(string key) => AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + key + ".prefab");

        [MenuItem("HalliGalli/Environment/Import Prepared VARCO Assets")]
        public static void Import()
        {
            string prepared = Path.GetFullPath("../outputs/varco_environment/prepared");
            foreach (string key in Keys) {
                string directory = Path.Combine(prepared, key);
                if (!File.Exists(Path.Combine(directory, key + ".fbx")) || !File.Exists(Path.Combine(directory, "asset.json")))
                    throw new FileNotFoundException("Prepare all VARCO assets before importing: " + key);
            }
            Directory.CreateDirectory(Root + "/Prefabs");
            foreach (string key in Keys) {
                string target = Root + "/" + key;
                Directory.CreateDirectory(target);
                foreach (string file in Directory.GetFiles(Path.Combine(prepared, key)))
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string key in Keys) ImportOne(key);
            AssetDatabase.SaveAssets();
        }

        internal static void ImportOne(string key,string root=Root)
        {
            string folder = root + "/" + key;
            var info = JsonUtility.FromJson<AssetInfo>(File.ReadAllText(folder + "/asset.json"));
            if (info.key != key || info.materials == null || info.materials.Length == 0)
                throw new InvalidDataException("Missing material provenance for " + key);
            string modelPath = folder + "/" + key + ".fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.SaveAndReimport();
            var materials = new Dictionary<string, Material>();
            foreach (var material in info.materials) materials[material.name] = BuildMaterial(folder, key, material);
            var instance = new GameObject(key);
            // Retain the FBX's axis conversion below an identity placement root.
            var geometry = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), instance.transform, false);
            try {
                var renderers = geometry.GetComponentsInChildren<MeshRenderer>(true);
                foreach (var renderer in renderers) {
                    var filter = renderer.GetComponent<MeshFilter>();
                    var mesh = info.meshes.FirstOrDefault(m => m.name == renderer.name || m.name == filter.sharedMesh.name);
                    if (mesh == null && renderers.Length == 1 && info.meshes.Length == 1) mesh = info.meshes[0];
                    if (mesh == null) throw new InvalidDataException("Unmapped FBX mesh: " + renderer.name);
                    renderer.sharedMaterials = mesh.materialSlots.Select(name => {
                        if (!materials.TryGetValue(name, out var mat)) throw new InvalidDataException("Unmapped material: " + name);
                        return mat;
                    }).ToArray();
                    renderer.receiveShadows = true;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
                PrefabUtility.SaveAsPrefabAsset(instance, root + "/Prefabs/" + key + ".prefab");
            } finally { Object.DestroyImmediate(instance); }
        }

        static Texture2D Texture(string folder, TextureInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.file)) return null;
            if (info.uv != 0) throw new InvalidDataException("Environment texture needs unsupported UV set " + info.uv);
            string path = folder + "/" + info.file;
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool normal = info.usage == "normal";
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = info.usage == "sRGB";
            importer.isReadable = false;
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Texture2D Raw(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!texture.LoadImage(File.ReadAllBytes(path))) {
                Object.DestroyImmediate(texture);
                throw new InvalidDataException("Cannot decode texture " + path);
            }
            return texture;
        }

        static Texture2D PackedMap(string folder, string filename, Texture2D source, Func<Color, Color> convert)
        {
            var map = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            try {
                var pixels = source.GetPixels();
                for (int i = 0; i < pixels.Length; i++) pixels[i] = convert(pixels[i]);
                map.SetPixels(pixels); map.Apply();
                File.WriteAllBytes(folder + "/" + filename, map.EncodeToPNG());
            } finally { Object.DestroyImmediate(map); }
            AssetDatabase.ImportAsset(folder + "/" + filename, ImportAssetOptions.ForceSynchronousImport);
            return Texture(folder, new TextureInfo { file = filename, usage = "linear" });
        }

        static Color Factor(float[] values, bool alpha = false)
        {
            if (values == null || values.Length < 3) return Color.white;
            return new Color(values[0], values[1], values[2], alpha && values.Length > 3 ? values[3] : 1);
        }

        public static Material BuildMaterial(string folder, string key, MaterialInfo info)
        {
            string path = folder + "/" + key + "_Material_" + info.index + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("HalliGalli/EnvironmentPBR");
            if (!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidDataException("Environment PBR shader did not compile");
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            material.SetFloat("_Cull", info.doubleSided ? 0 : 2);
            material.doubleSidedGI = info.doubleSided;
            material.enableInstancing = true;
            material.color = Factor(info.baseColorFactor, true);
            material.mainTexture = Texture(folder, info.baseColor);
            material.SetFloat("_Metallic", info.metallicFactor);
            material.SetFloat("_Glossiness", 1 - info.roughnessFactor);
            if (info.metallicRoughness != null) {
                var source = Raw(folder + "/" + info.metallicRoughness.file);
                try {
                    var packed = PackedMap(folder, key + "_MetalSmooth_" + info.index + ".png", source,
                        c => new Color(c.b * info.metallicFactor, 0, 0, 1 - c.g * info.roughnessFactor));
                    material.SetTexture("_MetallicGlossMap", packed);
                    material.SetFloat("_GlossMapScale", 1);
                    material.EnableKeyword("_METALLICGLOSSMAP");
                } finally { Object.DestroyImmediate(source); }
            } else { material.SetTexture("_MetallicGlossMap", null); material.DisableKeyword("_METALLICGLOSSMAP"); }
            if (info.occlusion != null) {
                var source = Raw(folder + "/" + info.occlusion.file);
                try {
                    material.SetTexture("_OcclusionMap", PackedMap(folder, key + "_Occlusion_" + info.index + ".png", source,
                        c => new Color(1, c.r, 1, 1)));
                    material.SetFloat("_OcclusionStrength", info.occlusion.strength);
                } finally { Object.DestroyImmediate(source); }
            }
            material.SetTexture("_BumpMap", Texture(folder, info.normal));
            if (info.normal != null) { material.EnableKeyword("_NORMALMAP"); material.SetFloat("_BumpScale", info.normal.scale); }
            else material.DisableKeyword("_NORMALMAP");
            if (info.emissive != null || (info.emissiveFactor != null && info.emissiveFactor.Any(v => v > 0))) {
                material.SetTexture("_EmissionMap", Texture(folder, info.emissive));
                material.SetColor("_EmissionColor", Factor(info.emissiveFactor));
                material.EnableKeyword("_EMISSION");
            }
            bool cutout = info.alphaMode == "MASK";
            if (info.alphaMode == "BLEND") throw new InvalidDataException("Review transparent environment material before importing: " + key);
            material.SetFloat("_Mode", cutout ? 1 : 0);
            material.SetFloat("_Cutoff", info.alphaCutoff);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            material.SetInt("_ZWrite", 1);
            material.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : "Opaque");
            if (cutout) material.EnableKeyword("_ALPHATEST_ON"); else material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = cutout ? 2450 : -1;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
