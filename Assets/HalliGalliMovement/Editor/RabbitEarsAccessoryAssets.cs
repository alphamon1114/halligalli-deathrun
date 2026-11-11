using System;
using UnityEditor;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    // Installs only the rabbit headband; leaves approved accessories and the player rig unchanged.
    public static class RabbitEarsAccessoryAssets
    {
        const string Folder = "Assets/HalliGalliMovement/Art/Accessories/RabbitEars/";
        [MenuItem("HalliGalli/Install Rabbit Ear Accessory")]
        public static void Install()
        {
            AssetDatabase.Refresh();
            var importer = (ModelImporter)AssetImporter.GetAtPath(Folder + "RabbitEars.fbx");
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
            Texture("RabbitEars_BASE_COLOR.png", true, false);
            Texture("RabbitEars_NORMAL_MAP.png", false, true);
            Texture("RabbitEars_MetallicSmoothness.png", false, false);
            Texture("RabbitEars_Occlusion.png", false, false);
            var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "RabbitEars.mat");
            if (!material) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, Folder + "RabbitEars.mat"); }
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "RabbitEars_BASE_COLOR.png");
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "RabbitEars_NORMAL_MAP.png"));
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "RabbitEars_MetallicSmoothness.png"));
            material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "RabbitEars_Occlusion.png"));
            material.SetFloat("_GlossMapScale", .5f); material.SetFloat("_BumpScale", .4f);
            material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICGLOSSMAP");
            EditorUtility.SetDirty(material);
            var body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HalliGalliMovement/Art/Player_Bald.fbx");
            Transform head = null;
            foreach (var bone in body.GetComponentsInChildren<Transform>(true)) if (bone.name == "Head") head = bone;
            if (!head) throw new InvalidOperationException("Player model has no Head bone.");
            var item = new GameObject("RabbitEars");
            try {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "RabbitEars.fbx"));
                visual.transform.SetParent(item.transform, false);
                var renderers = visual.GetComponentsInChildren<Renderer>(true);
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) { bounds.Encapsulate(renderer.bounds); renderer.sharedMaterial = material; }
                // The splayed rabbit ears are wider than the band: fit both axes explicitly.
                float widthScale = .30f / bounds.size.x;
                var scale = new Vector3(widthScale, .27f / bounds.size.y, widthScale);
                visual.transform.localPosition = -Vector3.Scale(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), scale);
                visual.transform.localScale = Vector3.Scale(visual.transform.localScale, scale);
                var root = body.transform;
                item.transform.localPosition = head.InverseTransformPoint(root.TransformPoint(new Vector3(0, 1.67f, .035f)));
                item.transform.localRotation = Quaternion.Inverse(head.rotation) * root.rotation;
                item.transform.localScale = Vector3.one * (root.lossyScale.x / head.lossyScale.x);
                PrefabUtility.SaveAsPrefabAsset(item, "Assets/HalliGalliMovement/Resources/Accessories/RabbitEars.prefab");
                Debug.Log("RABBIT_EARS_INSTALLED bounds=" + bounds + " scale=" + scale);
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
