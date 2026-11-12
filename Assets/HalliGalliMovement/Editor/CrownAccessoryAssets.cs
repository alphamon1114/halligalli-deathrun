using System;
using UnityEditor;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    // Installs only the crown; approved accessories, player rig and scenes remain unchanged.
    public static class CrownAccessoryAssets
    {
        const string Folder = "Assets/HalliGalliMovement/Art/Accessories/Crown/";
        [MenuItem("HalliGalli/Install Crown Accessory")]
        public static void Install()
        {
            AssetDatabase.Refresh();
            var importer = (ModelImporter)AssetImporter.GetAtPath(Folder + "Crown.fbx");
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
            Texture("Crown_BASE_COLOR.png", true, false);
            Texture("Crown_NORMAL_MAP.png", false, true);
            Texture("Crown_MetallicSmoothness.png", false, false);
            Texture("Crown_Occlusion.png", false, false);
            var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "Crown.mat");
            if (!material) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, Folder + "Crown.mat"); }
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "Crown_BASE_COLOR.png");
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "Crown_NORMAL_MAP.png"));
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "Crown_MetallicSmoothness.png"));
            material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "Crown_Occlusion.png"));
            material.SetFloat("_GlossMapScale", .65f); material.SetFloat("_BumpScale", .4f);
            material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICGLOSSMAP");
            EditorUtility.SetDirty(material);
            var body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HalliGalliMovement/Art/Player_Bald.fbx");
            Transform head = null;
            foreach (var bone in body.GetComponentsInChildren<Transform>(true)) if (bone.name == "Head") head = bone;
            if (!head) throw new InvalidOperationException("Player model has no Head bone.");
            var item = new GameObject("Crown");
            try {
                var fit = new GameObject("Crown fit").transform;
                fit.SetParent(item.transform, false);
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "Crown.fbx"));
                visual.transform.SetParent(fit, false);
                var renderers = visual.GetComponentsInChildren<Renderer>(true);
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) { bounds.Encapsulate(renderer.bounds); renderer.sharedMaterial = material; }
                // Fit the hollow oval band around the forehead, not above the scalp.
                var scale = new Vector3(.19f / bounds.size.x, .115f / bounds.size.y, .23f / bounds.size.z);
                // FBX roots can carry an axis-conversion rotation; size in the parent axes.
                fit.localPosition = -Vector3.Scale(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), scale);
                fit.localScale = scale;
                var root = body.transform;
                item.transform.localPosition = head.InverseTransformPoint(root.TransformPoint(new Vector3(0, 1.71f, .025f)));
                item.transform.localRotation = Quaternion.Inverse(head.rotation) * root.rotation * Quaternion.Euler(0, -16, 0);
                item.transform.localScale = Vector3.one * (root.lossyScale.x / head.lossyScale.x);
                PrefabUtility.SaveAsPrefabAsset(item, "Assets/HalliGalliMovement/Resources/Accessories/Crown.prefab");
                Debug.Log("CROWN_INSTALLED bounds=" + bounds + " scale=" + scale);
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
