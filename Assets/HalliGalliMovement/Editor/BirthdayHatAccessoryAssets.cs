using System;
using UnityEditor;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    // Only imports the generated hat and writes its head-attachment prefab.
    public static class BirthdayHatAccessoryAssets
    {
        const string Folder = "Assets/HalliGalliMovement/Art/Accessories/BirthdayHat/";
        [MenuItem("HalliGalli/Install Birthday Hat Accessory")]
        public static void Install()
        {
            AssetDatabase.Refresh();
            var importer = (ModelImporter)AssetImporter.GetAtPath(Folder + "BirthdayHat.fbx");
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
            Texture("BirthdayHat_BASE_COLOR.png", true, false);
            Texture("BirthdayHat_NORMAL_MAP.png", false, true);
            Texture("BirthdayHat_MetallicSmoothness.png", false, false);
            Texture("BirthdayHat_Occlusion.png", false, false);
            var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "BirthdayHat.mat");
            if (!material) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, Folder + "BirthdayHat.mat"); }
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "BirthdayHat_BASE_COLOR.png");
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "BirthdayHat_NORMAL_MAP.png"));
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "BirthdayHat_MetallicSmoothness.png"));
            material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "BirthdayHat_Occlusion.png"));
            material.SetFloat("_GlossMapScale", 1); material.SetFloat("_BumpScale", .6f);
            material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICGLOSSMAP");
            EditorUtility.SetDirty(material);
            var pompom = AssetDatabase.LoadAssetAtPath<Material>(Folder + "BirthdayHatPompom.mat");
            if (!pompom) { pompom = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(pompom, Folder + "BirthdayHatPompom.mat"); }
            pompom.color = new Color(.9f, .035f, .055f); pompom.SetFloat("_Glossiness", .15f);
            EditorUtility.SetDirty(pompom);
            var body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HalliGalliMovement/Art/Player_Bald.fbx");
            Transform head = null;
            foreach (var bone in body.GetComponentsInChildren<Transform>(true)) if (bone.name == "Head") head = bone;
            if (!head) throw new InvalidOperationException("Player model has no Head bone.");
            var item = new GameObject("BirthdayHat");
            try {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "BirthdayHat.fbx"));
                visual.transform.SetParent(item.transform, false);
                var renderers = visual.GetComponentsInChildren<Renderer>(true);
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) {
                    bounds.Encapsulate(renderer.bounds);
                    renderer.sharedMaterial = renderer.name.EndsWith("_Pompom", StringComparison.Ordinal) ? pompom : material;
                }
                float scale = .18f / Mathf.Max(bounds.size.x, bounds.size.z);
                // Bottom-centred pivot makes the rolled rim's seating height explicit.
                visual.transform.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
                visual.transform.localScale *= scale;
                var root = body.transform;
                item.transform.localPosition = head.InverseTransformPoint(root.TransformPoint(new Vector3(0, 1.77f, .03f)));
                item.transform.localRotation = Quaternion.Inverse(head.rotation) * root.rotation;
                item.transform.localScale = Vector3.one * (root.lossyScale.x / head.lossyScale.x);
                PrefabUtility.SaveAsPrefabAsset(item,"Assets/HalliGalliMovement/Resources/Accessories/BirthdayHat.prefab");
                Debug.Log("BIRTHDAY_HAT_INSTALLED bounds=" + bounds + " scale=" + scale);
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
