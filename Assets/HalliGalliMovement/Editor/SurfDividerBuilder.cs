using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using HalliGalli.Movement;
using Object = UnityEngine.Object;

namespace HalliGalli.EditorTools
{
    public static class SurfDividerBuilder
    {
        public const string DividerName = "Surf lane divider";
        const float StartZ = -24, EndZ = 30.25f, Left = -24, Right = 36, Height = -5.4f;

        [MenuItem("HalliGalli/Maps/Install Surf Divider")]
        public static void Install()
        {
            var scene = EditorSceneManager.OpenScene(SurfCourseBuilder.ScenePath);
            string backups = MovementProjectBuilder.Root + "/Demo/Backups";
            Directory.CreateDirectory(backups);
            EditorSceneManager.SaveScene(scene, backups + "/SurfCourse_BeforeDivider_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity", true);
            InstallCurrent();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            SurfCourseBuilder.Capture();
        }

        public static void InstallCurrent()
        {
            var old = GameObject.Find(DividerName);
            if (old) Object.DestroyImmediate(old);
            var surf = Object.FindAnyObjectByType<SurfCourse>();
            if (!surf) throw new InvalidOperationException("Open the surf scene before installing its divider.");
            var root = new GameObject(DividerName).transform;
            root.SetParent(surf.transform, false);
            // One continuous collision surface: panel joints cannot snag a fast capsule.
            var collider = root.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3((Left+Right)/2,Height,(StartZ+EndZ)/2);
            collider.size = new Vector3(Right-Left,.28f,EndZ-StartZ);
            root.gameObject.AddComponent<SurfDivider>().session = Object.FindAnyObjectByType<CourseSession>();

            // A horizontal separator covers the entire right-hand drop area, including the start.
            const int columns = 6, rows = 10;
            float width = (Right-Left)/columns, depth = (EndZ-StartZ)/rows;
            for (int column = 0; column < columns; column++)
                for (int row = 0; row < rows; row++)
                    Panel(root,new Vector3(Left+width*(column+.5f),Height,StartZ+depth*(row+.5f)),new Vector3(width+.015f,.28f,depth+.015f));
            var accent = AssetDatabase.LoadAssetAtPath<Material>(MovementProjectBuilder.Root + "/Materials/FactoryAmber.mat");
            foreach (float z in new[] { StartZ, EndZ })
                Stripe(root,new Vector3((Left+Right)/2,Height+.15f,z),new Vector3(Right-Left,.035f,.15f),accent);
            foreach(float x in new[]{Left,Right})Stripe(root,new Vector3(x,Height+.15f,(StartZ+EndZ)/2),new Vector3(.15f,.035f,EndZ-StartZ),accent);
            InstallElectricity(root);
            SurfBulkheadBuilder.InstallCurrent();
            // The divider ends before the landing. The normal drop/180-degree turn stays open.
        }

        static void InstallElectricity(Transform root)
        {
            string folder = MovementProjectBuilder.Root + "/Materials";
            var surfaceMaterial = AssetDatabase.LoadAssetAtPath<Material>(folder + "/SurfElectricSurface.mat");
            if (!surfaceMaterial) {
                surfaceMaterial = new Material(Shader.Find("HalliGalli/ElectricHazard"));
                AssetDatabase.CreateAsset(surfaceMaterial, folder + "/SurfElectricSurface.mat");
            }
            surfaceMaterial.SetVector("_Size", new Vector4(Right-Left,EndZ-StartZ,0,0));
            EditorUtility.SetDirty(surfaceMaterial);
            var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plate.name = "Electrical hazard markings";
            plate.transform.SetParent(root, false);
            plate.transform.localPosition = new Vector3((Left+Right)/2, Height+.17f, (StartZ+EndZ)/2);
            plate.transform.localRotation = Quaternion.Euler(90,0,0);
            plate.transform.localScale = new Vector3(Right-Left,EndZ-StartZ,1);
            var plateRenderer = plate.GetComponent<Renderer>();
            plateRenderer.sharedMaterial = surfaceMaterial;
            plateRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Object.DestroyImmediate(plate.GetComponent<Collider>());

            var arcMaterial = AssetDatabase.LoadAssetAtPath<Material>(folder + "/SurfElectricArc.mat");
            if (!arcMaterial) {
                arcMaterial = new Material(Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(arcMaterial, folder + "/SurfElectricArc.mat");
            }
            var effect = root.gameObject.AddComponent<SurfDividerElectricity>();
            effect.surfaceMin = new Vector3(Left,Height+.17f,StartZ);
            effect.surfaceSize = new Vector2(Right-Left,EndZ-StartZ);
            effect.arcs = new LineRenderer[24];
            for (int i=0; i<effect.arcs.Length; i++) {
                var arc = new GameObject("Electrical discharge " + (i+1)).AddComponent<LineRenderer>();
                arc.transform.SetParent(root, false);
                arc.sharedMaterial = arcMaterial; arc.useWorldSpace = false;
                arc.positionCount = 13; arc.numCornerVertices = 2; arc.numCapVertices = 2;
                arc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                arc.receiveShadows = false;
                effect.arcs[i] = arc;
            }
            effect.Apply(1.25f);
            AssetDatabase.SaveAssets();
        }

        public static void CheckVisuals()
        {
            var shader = Shader.Find("HalliGalli/ElectricHazard");
            var report = new System.Text.StringBuilder();
            report.AppendLine("Supported: " + (shader && shader.isSupported));
            if (shader) foreach (var message in ShaderUtil.GetShaderMessages(shader))
                report.AppendLine(message.severity + " line " + message.line + ": " + message.message);
            Directory.CreateDirectory("TestResults");
            File.WriteAllText("TestResults/electric-hazard-shader.txt", report.ToString());
        }

        public static void CaptureElectricity()
        {
            EditorSceneManager.OpenScene(SurfCourseBuilder.ScenePath);
            string folder = Path.GetFullPath("../outputs/electric-preview");
            Directory.CreateDirectory(folder);
            var camera = new GameObject("Electricity preview").AddComponent<Camera>();
            camera.cullingMask = ~(1 << CharacterMotionDriver.HandsLayer); camera.fieldOfView = 75;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = RenderSettings.fogColor;
            camera.transform.position = new Vector3(9,-.5f,9); camera.transform.LookAt(new Vector3(9,-5.2f,18));
            var effect = Object.FindAnyObjectByType<SurfDividerElectricity>();
            try {
                for (int frame=0; frame<24; frame++) {
                    effect.Apply(1.25f+frame/12f);
                    CharacterAssetBuilder.Render(camera,null,Path.Combine(folder,"frame-"+frame.ToString("D2")+".png"));
                }
                CheckVisuals();
            } finally { Object.DestroyImmediate(camera.gameObject); EditorSceneManager.OpenScene(SurfCourseBuilder.ScenePath); }
        }

        static void Panel(Transform parent, Vector3 center, Vector3 size)
        {
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(EnvironmentAssetImporter.Prefab("ConcreteFloor"));
            visual.transform.SetParent(parent, false);
            bool first = true; Bounds bounds = default;
            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>()) {
                var b = filter.sharedMesh.bounds;
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2) {
                    var point = visual.transform.InverseTransformPoint(filter.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z))));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
                }
            }
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = new Vector3(size.x / bounds.size.x, size.y / bounds.size.y, size.z / bounds.size.z);
            visual.transform.localPosition = center - visual.transform.localRotation * Vector3.Scale(bounds.center, visual.transform.localScale);
            foreach (var c in visual.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        }

        static void Stripe(Transform parent, Vector3 center, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "Divider edge";
            go.transform.SetParent(parent, false); go.transform.localPosition = center; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }
    }
}
