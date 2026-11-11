using System;
using UnityEditor;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    // Imports only the captain hat and writes its head-attachment prefab.
    public static class PirateHatAccessoryAssets
    {
        const string Folder = "Assets/HalliGalliMovement/Art/Accessories/PirateHat/";
        [MenuItem("HalliGalli/Install Pirate Hat Accessory")]
        public static void Install()
        {
            AssetDatabase.Refresh();
            var importer = (ModelImporter)AssetImporter.GetAtPath(Folder + "PirateHat.fbx");
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
            Texture("PirateHat_BASE_COLOR.png", true, false);
            Texture("PirateHat_NORMAL_MAP.png", false, true);
            Texture("PirateHat_MetallicSmoothness.png", false, false);
            Texture("PirateHat_Occlusion.png", false, false);
            var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "PirateHat.mat");
            if (!material) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, Folder + "PirateHat.mat"); }
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "PirateHat_BASE_COLOR.png");
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "PirateHat_NORMAL_MAP.png"));
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "PirateHat_MetallicSmoothness.png"));
            material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "PirateHat_Occlusion.png"));
            material.SetFloat("_GlossMapScale", .7f); material.SetFloat("_BumpScale", .5f);
            material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICGLOSSMAP");
            EditorUtility.SetDirty(material);
            var body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HalliGalliMovement/Art/Player_Bald.fbx");
            Transform head = null;
            foreach (var bone in body.GetComponentsInChildren<Transform>(true)) if (bone.name == "Head") head = bone;
            if (!head) throw new InvalidOperationException("Player model has no Head bone.");
            var item = new GameObject("PirateHat");
            try {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "PirateHat.fbx"));
                visual.transform.SetParent(item.transform, false);
                var renderers = visual.GetComponentsInChildren<Renderer>(true);
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) { bounds.Encapsulate(renderer.bounds); renderer.sharedMaterial = material; }
                float scale = .32f / bounds.size.x;
                // Seat the head inside the crown; tip the front brim down over the forehead.
                visual.transform.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
                visual.transform.localScale *= scale;
                var root = body.transform;
                item.transform.localPosition = head.InverseTransformPoint(root.TransformPoint(new Vector3(0, 1.69f, .015f)));
                item.transform.localRotation = Quaternion.Inverse(head.rotation) * root.rotation * Quaternion.Euler(10, 0, 0);
                item.transform.localScale = Vector3.one * (root.lossyScale.x / head.lossyScale.x);
                PrefabUtility.SaveAsPrefabAsset(item, "Assets/HalliGalliMovement/Resources/Accessories/PirateHat.prefab");
                Debug.Log("PIRATE_HAT_INSTALLED bounds=" + bounds + " scale=" + scale);
            } finally { UnityEngine.Object.DestroyImmediate(item); }
            AssetDatabase.SaveAssets();
        }
        static void Texture(string name, bool srgb, bool normal)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(Folder + name);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb; importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }
    }
}
