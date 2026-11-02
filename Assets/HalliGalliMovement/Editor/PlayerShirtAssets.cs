using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HalliGalli.EditorTools
{
    // This installer writes only the new mask/material. It never rebuilds a scene,
    // edits a model importer, regenerates animation or changes the source Body material.
    public static class PlayerShirtAssets
    {
        const string Art = "Assets/HalliGalliMovement/Art";
        const string Resource = "Assets/HalliGalliMovement/Resources";
        const string AtlasPath = Art + "/Textures/Body_BaseColor.png";
        const string MaskPath = Resource + "/PlayerShirtMask.png";
        const string MaterialPath = Resource + "/PlayerShirtBody.mat";
        const string PrefabPath = "Assets/HalliGalliMovement/Prefabs/SourcePlayer.prefab";
        const int TileWidth = 360, TileHeight = 600;

        [Serializable] sealed class AtlasSample
        {
            public string region;
            public int x, yFromTop, mask;
            public bool expectedShirt;
        }
        [Serializable] sealed class MaskReport
        {
            public string utc, source, sourceSha256, method;
            public int width, height, shirtTexels, nonShirtTexels;
            public AtlasSample[] samples;
        }
        [Serializable] sealed class ViewResult
        {
            public int color;
            public string name, view, image;
            public int comparedNonShirtPixels, changedNonShirtPixels, maxNonShirtByteDifference;
            public int originalBodyMaxByteDifference;
        }
        [Serializable] sealed class CaptureReport
        {
            public string utc, atlas, shader, comparison;
            public bool handsUnchanged, kickUnchanged, bodyGeometryUnchanged;
            public int bodyRenderers;
            public ViewResult[] views;
        }
        sealed class BakedBody
        {
            public SkinnedMeshRenderer source;
            public MeshRenderer renderer;
            public Mesh mesh;
            public Material[] originals;
        }

        [MenuItem("HalliGalli/Install Shirt Colors Only")]
        public static void Install()
        {
            var original = AssetDatabase.LoadAssetAtPath<Material>(Art + "/Materials/Body.mat");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Resource + "/PlayerShirt.shader");
            if (!original || !shader) throw new InvalidOperationException("Original Body material / shirt shader is missing.");
            byte[] bytes = File.ReadAllBytes(AtlasPath);
            var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            var mask = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try {
                if (!ImageConversion.LoadImage(atlas, bytes, false) || atlas.width != 4096 || atlas.height != 4096)
                    throw new InvalidOperationException("Shirt mask is authored specifically for the 4096x4096 Player_Bald Body atlas.");
                var source = atlas.GetPixels32();
                var pixels = new Color32[source.Length];
                int count = 0;
                for (int i = 0; i < source.Length; i++) {
                    // The inspected atlas has blue cotton, warm skin and neutral black trousers/shoes.
                    // Bake that authored color separation once. No runtime hue selection or whole-body tint.
                    byte value = IsGarment(source[i]) ? (byte)255 : (byte)0;
                    pixels[i] = new Color32(value, value, value, 255);
                    if (value != 0) count++;
                }
                var samples = EvidenceSamples(pixels, atlas.width, atlas.height);
                if (count < source.Length / 10 || count > source.Length / 2)
                    throw new InvalidOperationException("Unexpected shirt coverage; inspect the atlas before replacing the garment mask.");
                mask.Reinitialize(atlas.width, atlas.height);
                mask.SetPixels32(pixels); mask.Apply();
                Directory.CreateDirectory(Resource);
                File.WriteAllBytes(MaskPath, mask.EncodeToPNG());
                AssetDatabase.ImportAsset(MaskPath, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(MaskPath);
                importer.textureType = TextureImporterType.SingleChannel;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
                importer.SetTextureSettings(settings);
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.mipmapEnabled = true;
                importer.isReadable = false;
                importer.maxTextureSize = 4096;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
                var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                if (!material) {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, MaterialPath);
                }
                material.CopyPropertiesFromMaterial(original);
                material.shader = shader;
                material.name = "PlayerShirtBody";
                material.SetTexture("_ShirtMask", AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath));
                material.SetColor(PlayerShirtAppearance.ColorProperty, PlayerShirtColors.ColorAt(0));
                material.SetFloat("_ShirtAmount", 1);
                if (!material.GetTexture("_ShirtMask") || !material.HasProperty(PlayerShirtAppearance.ColorProperty))
                    throw new InvalidOperationException("Shirt material is missing initialized garment shader properties.");
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                string hash;
                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                WriteReport("TestResults/shirt-mask.json", new MaskReport {
                    utc = DateTime.UtcNow.ToString("O"), source = AtlasPath, sourceSha256 = hash,
                    method = "Offline garment-blue segmentation of the inspected original atlas; immutable runtime R8 mask. No source importer/material/model/clip edits.",
                    width = atlas.width, height = atlas.height, shirtTexels = count,
                    nonShirtTexels = source.Length - count, samples = samples
                });
                Debug.Log("SHIRT_ASSETS_INSTALLED: " + count + " garment texels; Resources material includes shader and original Body textures in player builds.");
            }
            finally { Object.DestroyImmediate(atlas); Object.DestroyImmediate(mask); }
        }

        static bool IsGarment(Color32 pixel) => pixel.b - pixel.r >= 7 &&
            pixel.b - pixel.g >= 3 && pixel.g - pixel.r >= 3 && pixel.b * 100 >= pixel.r * 125;

        static AtlasSample[] EvidenceSamples(Color32[] mask, int width, int height)
        {
            // Coordinates are normalized in the source PNG's top-left convention, not mesh space.
            var points = new[] {
                ("shirt front island", .13f, .43f, true), ("shirt back island", .35f, .43f, true),
                ("shirt upper island", .56f, .32f, true), ("shirt lower island", .32f, .71f, true),
                ("head", .13f, .73f, false), ("forearm", .87f, .31f, false),
                ("hand", .75f, .14f, false), ("trousers upper", .34f, .23f, false),
                ("trousers lower", .13f, .61f, false), ("shoe side", .70f, .43f, false),
                ("shoe sole", .83f, .085f, false)
            };
            var result = new AtlasSample[points.Length];
            for (int i = 0; i < points.Length; i++) {
                int x = Mathf.RoundToInt(points[i].Item2 * (width - 1));
                int y = Mathf.RoundToInt(points[i].Item3 * (height - 1));
                int value = mask[(height - 1 - y) * width + x].r;
                if ((value != 0) != points[i].Item4)
                    throw new InvalidOperationException("Garment mask disagrees with authored atlas sample: " + points[i].Item1);
                result[i] = new AtlasSample { region = points[i].Item1, x = x, yFromTop = y, mask = value, expectedShirt = points[i].Item4 };
            }
            return result;
        }

        [MenuItem("HalliGalli/Capture Eight Shirt Colors")]
        public static void CaptureColors()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!material || !prefab) throw new InvalidOperationException("Install shirt assets before capturing.");
            var preview = new PreviewRenderUtility();
            var root = Object.Instantiate(prefab);
            var maskMaterial = new Material(Shader.Find("Unlit/Texture"));
            var board = new Texture2D(TileWidth * PlayerShirtColors.Count, TileHeight * 2, TextureFormat.RGB24, false);
            var baked = new List<BakedBody>();
            var results = new List<ViewResult>();
            try {
                preview.AddSingleGO(root);
                var driver = root.GetComponent<CharacterMotionDriver>();
                if (!driver || !driver.bodyModel) throw new InvalidOperationException("SourcePlayer has no authored body model.");
                foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
                root.SetActive(true);
                driver.bodyModel.gameObject.SetActive(true);
                if (driver.handsModel) driver.handsModel.gameObject.SetActive(false);
                if (driver.kickModel) driver.kickModel.gameObject.SetActive(false);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (var clip in driver.motions.bodyClips)
                    if (clip && clip.name == "Idle") { clip.SampleAnimation(driver.bodyModel.gameObject, 0); break; }
                var originalHands = MaterialSnapshot(driver.handsModel);
                var originalKick = MaterialSnapshot(driver.kickModel);
                var skins = driver.bodyModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var originalMeshes = new Mesh[skins.Length];
                for (int i = 0; i < skins.Length; i++) {
                    var skin = skins[i]; originalMeshes[i] = skin.sharedMesh;
                    var mesh = new Mesh(); skin.BakeMesh(mesh, true);
                    var go = new GameObject("Shirt capture baked pose", typeof(MeshFilter), typeof(MeshRenderer));
                    go.transform.SetParent(skin.transform, false); go.layer = CharacterMotionDriver.BodyLayer;
                    go.GetComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterials = skin.sharedMaterials;
                    renderer.shadowCastingMode = skin.shadowCastingMode; renderer.receiveShadows = skin.receiveShadows;
                    skin.enabled = false;
                    baked.Add(new BakedBody { source = skin, renderer = renderer, mesh = mesh, originals = skin.sharedMaterials });
                }
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(.065f, .075f, .09f);
                preview.camera.cullingMask = 1 << CharacterMotionDriver.BodyLayer;
                preview.camera.orthographic = true;
                preview.camera.orthographicSize = 1.06f;
                preview.camera.nearClipPlane = .05f; preview.camera.farClipPlane = 20;
                preview.camera.allowHDR = false; preview.camera.allowMSAA = false;
                preview.ambientColor = new Color(.44f, .44f, .44f);
                preview.lights[0].intensity = 1.1f;
                preview.lights[1].intensity = .65f;
                maskMaterial.mainTexture = material.GetTexture("_ShirtMask");
                var participant = root.GetComponent<CourseParticipant>();
                if (!participant) participant = root.AddComponent<CourseParticipant>();
                Directory.CreateDirectory("Previews");
                for (int side = 0; side < 2; side++) {
                    string view = side == 0 ? "Front" : "Back";
                    preview.camera.transform.position = new Vector3(0, .92f, side == 0 ? 4 : -4);
                    preview.camera.transform.LookAt(new Vector3(0, .92f, 0));
                    preview.lights[0].transform.rotation = Quaternion.Euler(28, side == 0 ? 155 : -25, 0);
                    preview.lights[1].transform.rotation = Quaternion.Euler(15, side == 0 ? 35 : 215, 0);
                    foreach (var item in baked) {
                        item.renderer.sharedMaterials = item.originals;
                        for (int slot = 0; slot < item.originals.Length; slot++) item.renderer.SetPropertyBlock(null, slot);
                    }
                    var original = Render(preview);
                    File.WriteAllBytes("Previews/Shirt_Original_" + view + ".png", original.EncodeToPNG());
                    foreach (var item in baked) {
                        var mats = item.renderer.sharedMaterials;
                        for (int slot = 0; slot < mats.Length; slot++) mats[slot] = maskMaterial;
                        item.renderer.sharedMaterials = mats;
                    }
                    Color previousBackground = preview.camera.backgroundColor;
                    preview.camera.backgroundColor = Color.black;
                    var garment = Render(preview);
                    preview.camera.backgroundColor = previousBackground;
                    File.WriteAllBytes("Previews/Shirt_Mask_" + view + ".png", garment.EncodeToPNG());
                    var maskPixels = garment.GetPixels32();
                    var originalPixels = original.GetPixels32();
                    Color32[] baseline = null;
                    for (int index = 0; index < PlayerShirtColors.Count; index++) {
                        PlayerShirtAppearance.Apply(participant, index);
                        SyncBaked(baked);
                        var frame = Render(preview);
                        string path = "Previews/Shirt_" + view + "_" + index + ".png";
                        File.WriteAllBytes(path, frame.EncodeToPNG());
                        var pixels = frame.GetPixels32();
                        if (baseline == null) baseline = pixels;
                        var result = CompareNonShirt(index, view, path, pixels, baseline, originalPixels, maskPixels);
                        results.Add(result);
                        board.SetPixels(index * TileWidth, (1 - side) * TileHeight, TileWidth, TileHeight, frame.GetPixels());
                        var swatch = PlayerShirtColors.ColorAt(index);
                        for (int y = 0; y < 10; y++) for (int x = 0; x < TileWidth; x++)
                            board.SetPixel(index * TileWidth + x, (1 - side) * TileHeight + y, swatch);
                        Object.DestroyImmediate(frame);
                    }
                    Object.DestroyImmediate(original); Object.DestroyImmediate(garment);
                }
                board.Apply();
                File.WriteAllBytes("Previews/Shirt_EightColors_FrontBack.png", board.EncodeToPNG());
                bool geometrySame = true;
                for (int i = 0; i < skins.Length; i++) geometrySame &= skins[i].sharedMesh == originalMeshes[i];
                var finalAppearance = root.GetComponent<PlayerShirtAppearance>();
                var report = new CaptureReport {
                    utc = DateTime.UtcNow.ToString("O"), atlas = AtlasPath, shader = material.shader.name,
                    comparison = "Actual SourcePlayer/Idle geometry through PlayerShirtAppearance and per-renderer MPB. Columns 0..7; front top/back bottom. Non-shirt comparison excludes a 2px garment/AA boundary and background; compared against blue and original Body Standard material. No PlayerPrefs writes.",
                    handsUnchanged = SameMaterials(driver.handsModel, originalHands),
                    kickUnchanged = SameMaterials(driver.kickModel, originalKick), bodyGeometryUnchanged = geometrySame,
                    bodyRenderers = finalAppearance.RendererCount, views = results.ToArray()
                };
                WriteReport("TestResults/shirt-appearance.json", report);
                foreach (var result in results)
                    if (result.changedNonShirtPixels != 0)
                        throw new InvalidOperationException("A shirt color changed non-shirt pixels: " + result.view + " " + result.color);
                if (!report.handsUnchanged || !report.kickUnchanged || !geometrySame || report.bodyRenderers == 0)
                    throw new InvalidOperationException("Shirt appearance changed a protected body/hands/kick resource.");
                Debug.Log("SHIRT_COLORS_CAPTURED: eight actual character colors, front/back and non-shirt evidence.");
            }
            finally {
                foreach (var item in baked) Object.DestroyImmediate(item.mesh);
                Object.DestroyImmediate(maskMaterial); Object.DestroyImmediate(board);
                preview.Cleanup();
            }
        }

        static void SyncBaked(List<BakedBody> baked)
        {
            var block = new MaterialPropertyBlock();
            foreach (var item in baked) {
                var materials = item.source.sharedMaterials;
                item.renderer.sharedMaterials = materials;
                for (int slot = 0; slot < materials.Length; slot++) {
                    item.source.GetPropertyBlock(block, slot);
                    item.renderer.SetPropertyBlock(block.isEmpty ? null : block, slot);
                }
            }
        }
        static Texture2D Render(PreviewRenderUtility preview)
        {
            preview.BeginStaticPreview(new Rect(0, 0, TileWidth, TileHeight));
            preview.Render();
            return preview.EndStaticPreview();
        }
        static Material[] MaterialSnapshot(Transform model)
        {
            var materials = new List<Material>();
            if (model) foreach (var renderer in model.GetComponentsInChildren<Renderer>(true)) materials.AddRange(renderer.sharedMaterials);
            return materials.ToArray();
        }
        static bool SameMaterials(Transform model, Material[] before)
        {
            var after = MaterialSnapshot(model);
            if (after.Length != before.Length) return false;
            for (int i = 0; i < after.Length; i++) if (after[i] != before[i]) return false;
            return true;
        }
        static ViewResult CompareNonShirt(int index, string view, string path, Color32[] pixels,
            Color32[] blue, Color32[] original, Color32[] garment)
        {
            var result = new ViewResult { color = index, name = PlayerShirtColors.NameAt(index), view = view, image = path };
            for (int y = 3; y < TileHeight - 3; y++) for (int x = 3; x < TileWidth - 3; x++) {
                int p = y * TileWidth + x;
                // Exclude background by comparing to a known empty corner of the original render.
                if (Difference(original[p], original[0]) <= 4) continue;
                bool nearShirt = false;
                for (int dy = -2; dy <= 2 && !nearShirt; dy++) for (int dx = -2; dx <= 2; dx++)
                    if (garment[p + dy * TileWidth + dx].r > 0) { nearShirt = true; break; }
                if (nearShirt) continue;
                int difference = Difference(pixels[p], blue[p]);
                result.comparedNonShirtPixels++;
                if (difference > 2) result.changedNonShirtPixels++;
                result.maxNonShirtByteDifference = Math.Max(result.maxNonShirtByteDifference, difference);
                result.originalBodyMaxByteDifference = Math.Max(result.originalBodyMaxByteDifference, Difference(pixels[p], original[p]));
            }
            if (result.comparedNonShirtPixels < 500) throw new InvalidOperationException("Insufficient visible non-shirt pixels for capture evidence.");
            return result;
        }
        static int Difference(Color32 a, Color32 b) => Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b)));
        static void WriteReport(string path, object report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
        }
    }
}
