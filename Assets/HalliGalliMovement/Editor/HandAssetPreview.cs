using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HalliGalli.EditorTools
{
    // Asset-only studio captures. Never opens/saves a game scene or authors motion clips.
    public static class HandAssetPreview
    {
        const string Output = "Previews/NewHands";
        const string PrefabPath = MovementProjectBuilder.Root + "/Prefabs/SourcePlayer.prefab";
        static readonly string[] Textures = { "Hands_BaseColor", "Hands_Normal", "Hands_ORM", "Hands_AO", "Hands_MetalSmooth" };

        [Serializable] sealed class CaptureRecord
        {
            public string modelPath, prefabPath, scope, error;
            public bool complete;
            public List<MeshRecord> importedMeshes = new();
            public List<MeshRecord> savedPrefabMeshes = new();
            public List<FrameRecord> frames = new();
        }
        [Serializable] sealed class MeshRecord
        {
            public string renderer, mesh, guid;
            public long localId;
            public int vertices, submeshes;
            public string[] bones;
            public Vector3 localScale;
        }
        [Serializable] sealed class FrameRecord
        {
            public string file, source, pose, view;
            public float clipTime;
        }
        sealed class BakedSkin
        {
            public SkinnedMeshRenderer source;
            public Mesh mesh;
        }

        [MenuItem("HalliGalli/Hands/Import and Capture New Hands Only")]
        public static void ImportAndCapture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Capture hand assets outside Play mode.");
            string modelPath = CharacterAssetBuilder.Art + "/FP_Hands.fbx";
            AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
            foreach (string texture in Textures)
                AssetDatabase.ImportAsset(CharacterAssetBuilder.Art + "/Textures/" + texture + ".png", ImportAssetOptions.ForceSynchronousImport);
            CharacterAssetBuilder.ConfigureHandMaterials();
            Directory.CreateDirectory(Output);
            var record = new CaptureRecord {
                modelPath = modelPath, prefabPath = PrefabPath,
                scope = "Isolated editor visual renders only; original motion clips sampled without modification. No gameplay, physics, network, build or release verification."
            };
            try {
                CaptureAnatomy(record);
                CaptureSavedPrefab(record);
                record.complete = true;
                Debug.Log("NEW_HAND_PREVIEWS_COMPLETE " + Path.GetFullPath(Output));
            } catch (Exception exception) {
                record.error = exception.ToString();
                throw;
            } finally {
                File.WriteAllText(Output + "/capture.json", JsonUtility.ToJson(record, true));
            }
        }

        static PreviewRenderUtility Studio()
        {
            var preview = new PreviewRenderUtility();
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(.10f, .13f, .17f);
            preview.camera.cullingMask = 1 << CharacterMotionDriver.HandsLayer;
            preview.camera.nearClipPlane = .01f;
            preview.camera.farClipPlane = 10;
            preview.camera.allowHDR = false;
            preview.camera.allowMSAA = false;
            preview.ambientColor = new Color(.42f, .44f, .47f);
            preview.lights[0].intensity = 1.0f;
            preview.lights[1].intensity = .55f;
            return preview;
        }

        static void CaptureAnatomy(CaptureRecord record)
        {
            var preview = Studio();
            var model = CharacterAssetBuilder.MakeModel(true);
            var baked = new List<BakedSkin>();
            try {
                preview.AddSingleGO(model);
                foreach (var transform in model.GetComponentsInChildren<Transform>())
                    transform.gameObject.layer = CharacterMotionDriver.HandsLayer;
                record.importedMeshes = DescribeMeshes(model);
                var author = new MotionPoseAuthor(model);
                baked = CreateBakedSkins(model);
                preview.camera.orthographic = true;
                preview.camera.orthographicSize = .30f;
                foreach (string pose in new[] { "Open", "Relaxed", "Fist" }) {
                    author.ReviewHands(pose == "Open" ? 0 : pose == "Relaxed" ? .30f : 1);
                    BakePose(baked);
                    foreach (string view in new[] { "Dorsal", "Palm" }) {
                        bool dorsal = view == "Dorsal";
                        preview.camera.transform.position = new Vector3(0, dorsal ? 2 : -2, .025f);
                        preview.camera.transform.LookAt(new Vector3(0, 0, .025f), Vector3.forward);
                        preview.lights[0].transform.rotation = Quaternion.Euler(dorsal ? 45 : -45, -30, 0);
                        preview.lights[1].transform.rotation = Quaternion.Euler(dorsal ? -35 : 35, 145, 0);
                        SaveFrame(preview, record, "Anatomy_" + pose + "_" + view, "Imported FBX; original MotionPoseAuthor.ReviewHands", pose, view, 0, 1600, 1200);
                    }
                }
            } finally {
                ReleaseMeshes(baked);
                preview.Cleanup();
            }
        }

        static void CaptureSavedPrefab(CaptureRecord record)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) throw new InvalidDataException("Missing saved player prefab.");
            var preview = Studio();
            var player = Object.Instantiate(prefab);
            var baked = new List<BakedSkin>();
            try {
                preview.AddSingleGO(player);
                foreach (var behaviour in player.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
                var driver = player.GetComponent<CharacterMotionDriver>();
                if (!driver || !driver.handsModel || !driver.handCamera || !driver.motions)
                    throw new InvalidDataException("Saved player prefab has incomplete hand references.");
                player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                player.SetActive(true);
                driver.handsModel.gameObject.SetActive(true);
                foreach (var renderer in player.GetComponentsInChildren<Renderer>(true))
                    if (!renderer.transform.IsChildOf(driver.handsModel)) renderer.enabled = false;
                var model = driver.handsModel.gameObject;
                record.savedPrefabMeshes = DescribeMeshes(model);
                foreach (var saved in record.savedPrefabMeshes) {
                    var imported = record.importedMeshes.SingleOrDefault(mesh => mesh.renderer == saved.renderer);
                    if (imported == null || saved.guid != imported.guid || saved.localId != imported.localId
                        || !saved.bones.SequenceEqual(imported.bones) || Vector3.Distance(saved.localScale, imported.localScale) > .001f)
                        throw new InvalidDataException("Saved prefab mesh, bone order or renderer scale does not match imported FBX: " + saved.renderer);
                }
                baked = CreateBakedSkins(model);
                preview.camera.orthographic = false;
                preview.camera.fieldOfView = driver.handCamera.fieldOfView;
                preview.lights[0].transform.rotation = Quaternion.Euler(35, -30, 0);
                preview.lights[1].transform.rotation = Quaternion.Euler(-25, 140, 0);
                foreach (string pose in new[] { "Idle", "PushLeft", "PushRight" }) {
                    var clip = driver.motions.handClips.Single(candidate => candidate && candidate.name == pose);
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                        if (binding.type == typeof(Transform) && !string.IsNullOrEmpty(binding.path) && !model.transform.Find(binding.path))
                            throw new InvalidDataException("Missing saved clip binding: " + pose + " / " + binding.path);
                    float time = pose == "Idle" ? clip.length * .14f : .16f;
                    clip.SampleAnimation(model, time);
                    BakePose(baked);
                    preview.camera.transform.SetPositionAndRotation(driver.handCamera.transform.position, driver.handCamera.transform.rotation);
                    SaveFrame(preview, record, "Prefab_" + pose + "_FirstPerson", PrefabPath + "; existing " + AssetDatabase.GetAssetPath(clip), pose, "Saved hands camera", time, 1600, 900);
                    preview.camera.transform.position = model.transform.TransformPoint(new Vector3(.75f, .20f, 1.10f));
                    preview.camera.transform.LookAt(model.transform.TransformPoint(new Vector3(0, -.22f, .48f)));
                    preview.camera.fieldOfView = 40;
                    SaveFrame(preview, record, "Prefab_" + pose + "_Oblique", PrefabPath + "; existing " + AssetDatabase.GetAssetPath(clip), pose, "Studio oblique", time, 1600, 900);
                    preview.camera.fieldOfView = driver.handCamera.fieldOfView;
                }
            } finally {
                ReleaseMeshes(baked);
                preview.Cleanup();
            }
        }

        static List<MeshRecord> DescribeMeshes(GameObject model)
        {
            var result = new List<MeshRecord>();
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                if (!skin.sharedMesh) throw new InvalidDataException("Unresolved hand mesh: " + skin.name);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(skin.sharedMesh, out string guid, out long localId);
                result.Add(new MeshRecord {
                    renderer = skin.name, mesh = skin.sharedMesh.name, guid = guid, localId = localId,
                    vertices = skin.sharedMesh.vertexCount, submeshes = skin.sharedMesh.subMeshCount,
                    localScale = skin.transform.localScale,
                    bones = skin.bones.Select(bone => bone ? bone.name : "<missing>").ToArray()
                });
            }
            if (result.Count != 2) throw new InvalidDataException("Expected the two existing hand renderers.");
            return result;
        }

        static List<BakedSkin> CreateBakedSkins(GameObject model)
        {
            var result = new List<BakedSkin>();
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                var mesh = new Mesh();
                var display = new GameObject("Hand preview baked pose", typeof(MeshFilter), typeof(MeshRenderer));
                display.transform.SetParent(skin.transform, false);
                display.layer = CharacterMotionDriver.HandsLayer;
                display.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = display.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = skin.sharedMaterials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                skin.enabled = false;
                result.Add(new BakedSkin { source = skin, mesh = mesh });
            }
            return result;
        }

        static void BakePose(List<BakedSkin> skins)
        {
            // SampleAnimation followed by Camera.Render may otherwise reuse a prior skinning frame.
            foreach (var skin in skins) {
                skin.source.BakeMesh(skin.mesh, true);
                skin.mesh.RecalculateBounds();
            }
        }

        static void ReleaseMeshes(List<BakedSkin> skins)
        {
            foreach (var skin in skins) Object.DestroyImmediate(skin.mesh);
        }

        static void SaveFrame(PreviewRenderUtility preview, CaptureRecord record, string name, string source, string pose, string view, float time, int width, int height)
        {
            preview.BeginStaticPreview(new Rect(0, 0, width, height));
            preview.Render();
            var image = preview.EndStaticPreview();
            string path = Output + "/" + name + ".png";
            try { File.WriteAllBytes(path, image.EncodeToPNG()); }
            finally { Object.DestroyImmediate(image); }
            record.frames.Add(new FrameRecord { file = path, source = source, pose = pose, view = view, clipTime = time });
        }
    }
}
