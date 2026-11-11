using System;
using UnityEditor;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    // Installs only the VARCO headband assets; never rebuilds maps or edits the character rig.
    public static class CatEarsAccessoryAssets
    {
        const string Folder = "Assets/HalliGalliMovement/Art/Accessories/CatEars/";
        [MenuItem("HalliGalli/Install Cat Ear Accessory")]
        public static void Install()
        {
            AssetDatabase.Refresh();
            var importer = (ModelImporter)AssetImporter.GetAtPath(Folder + "CatEars.fbx");
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
            Texture("CatEars_BASE_COLOR.png", true, false);
            Texture("CatEars_NORMAL_MAP.png", false, true);
            Texture("CatEars_MetallicSmoothness.png", false, false);
            Texture("CatEars_Occlusion.png", false, false);
            var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "CatEars.mat");
            if (!material) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, Folder + "CatEars.mat"); }
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "CatEars_BASE_COLOR.png");
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "CatEars_NORMAL_MAP.png"));
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "CatEars_MetallicSmoothness.png"));
            material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "CatEars_Occlusion.png"));
            material.SetFloat("_GlossMapScale", 1); material.SetFloat("_BumpScale", .6f);
            material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICGLOSSMAP");
            EditorUtility.SetDirty(material);
            var body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HalliGalliMovement/Art/Player_Bald.fbx");
            Transform head = null;
            foreach (var bone in body.GetComponentsInChildren<Transform>(true)) if (bone.name == "Head") head = bone;
            if (!head) throw new InvalidOperationException("Player model has no Head bone.");
            var item = new GameObject("CatEars");
            try {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "CatEars.fbx"));
                visual.transform.SetParent(item.transform, false);
                var renderers = visual.GetComponentsInChildren<Renderer>(true);
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) { bounds.Encapsulate(renderer.bounds); renderer.sharedMaterial = material; }
                float scale = .21f / bounds.size.x;
                visual.transform.localPosition = -bounds.center * scale;
                visual.transform.localScale *= scale;
                var root = body.transform;
                // Rest-pose placement: seat the padded band on the scalp, not above it.
                item.transform.localPosition = head.InverseTransformPoint(root.TransformPoint(new Vector3(0, 1.725f, .01f)));
                item.transform.localRotation = Quaternion.Inverse(head.rotation) * root.rotation;
                item.transform.localScale = Vector3.one * (root.lossyScale.x / head.lossyScale.x);
                PrefabUtility.SaveAsPrefabAsset(item,"Assets/HalliGalliMovement/Resources/Accessories/CatEars.prefab");
                Debug.Log("CAT_EARS_INSTALLED bounds=" + bounds + " scale=" + scale);
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
