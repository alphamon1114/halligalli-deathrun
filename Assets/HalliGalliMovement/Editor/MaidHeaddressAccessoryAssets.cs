using System;
using UnityEditor;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    // Installs only this accessory; does not rebuild scenes or modify the player rig.
    public static class MaidHeaddressAccessoryAssets
    {
        const string Folder = "Assets/HalliGalliMovement/Art/Accessories/MaidHeaddress/";
        [MenuItem("HalliGalli/Install Maid Headdress Accessory")]
        public static void Install()
        {
            AssetDatabase.Refresh();
            var importer = (ModelImporter)AssetImporter.GetAtPath(Folder + "MaidHeaddress.fbx");
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
            Texture("MaidHeaddress_BASE_COLOR.png", true, false);
            Texture("MaidHeaddress_NORMAL_MAP.png", false, true);
            Texture("MaidHeaddress_MetallicSmoothness.png", false, false);
            Texture("MaidHeaddress_Occlusion.png", false, false);
            var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "MaidHeaddress.mat");
            if (!material) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, Folder + "MaidHeaddress.mat"); }
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "MaidHeaddress_BASE_COLOR.png");
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "MaidHeaddress_NORMAL_MAP.png"));
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "MaidHeaddress_MetallicSmoothness.png"));
            material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "MaidHeaddress_Occlusion.png"));
            material.SetFloat("_GlossMapScale", .6f); material.SetFloat("_BumpScale", .5f);
            material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICGLOSSMAP");
            EditorUtility.SetDirty(material);
            var body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HalliGalliMovement/Art/Player_Bald.fbx");
            Transform head = null;
            foreach (var bone in body.GetComponentsInChildren<Transform>(true)) if (bone.name == "Head") head = bone;
            if (!head) throw new InvalidOperationException("Player model has no Head bone.");
            var item = new GameObject("MaidHeaddress");
            try {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "MaidHeaddress.fbx"));
                visual.transform.SetParent(item.transform, false);
                var renderers = visual.GetComponentsInChildren<Renderer>(true);
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) { bounds.Encapsulate(renderer.bounds); renderer.sharedMaterial = material; }
                float scale = .21f / bounds.size.x;
                visual.transform.localPosition = -bounds.center * scale;
                visual.transform.localScale *= scale;
                var root = body.transform;
                // Keep the lower band tips behind the ears without lifting the crown.
                item.transform.localPosition = head.InverseTransformPoint(root.TransformPoint(new Vector3(0, 1.73f, -.025f)));
                item.transform.localRotation = Quaternion.Inverse(head.rotation) * root.rotation;
                item.transform.localScale = Vector3.one * (root.lossyScale.x / head.lossyScale.x);
                PrefabUtility.SaveAsPrefabAsset(item, "Assets/HalliGalliMovement/Resources/Accessories/MaidHeaddress.prefab");
                Debug.Log("MAID_HEADDRESS_INSTALLED bounds=" + bounds + " scale=" + scale);
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
