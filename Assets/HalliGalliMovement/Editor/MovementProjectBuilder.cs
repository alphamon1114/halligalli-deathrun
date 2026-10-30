using System.IO;
using HalliGalli.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HalliGalli.EditorTools
{
    public static class MovementProjectBuilder
    {
        public const string Root = "Assets/HalliGalliMovement";
        public const string ScenePath = Root + "/Demo/MovementLab.unity";

        [MenuItem("HalliGalli/Create Movement Lab")]
        public static void Build()
        {
            Directory.CreateDirectory(Root + "/Demo");
            Directory.CreateDirectory(Root + "/Prefabs");
            Directory.CreateDirectory(Root + "/Settings");
            Directory.CreateDirectory(Root + "/Materials");
            AssetDatabase.Refresh();
            var profile = AssetDatabase.LoadAssetAtPath<SourceMovementProfile>(Root + "/Settings/DefaultMovement.asset");
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<SourceMovementProfile>();
                AssetDatabase.CreateAsset(profile, Root + "/Settings/DefaultMovement.asset");
            }
            var floor = Material("Floor", new Color(0.22f, 0.29f, 0.34f));
            var stone = Material("Obstacles", new Color(0.49f, 0.58f, 0.62f));
            var orange = Material("Jump", new Color(0.96f, 0.42f, 0.16f));
            var blue = Material("Crouch", new Color(0.12f, 0.55f, 0.7f));
            var white = Material("Markings", new Color(0.8f, 0.88f, 0.9f));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "MovementLab";
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.4f, 0.43f, 0.47f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.1f, 0.15f, 0.2f);
            RenderSettings.fogStartDistance = 45;
            RenderSettings.fogEndDistance = 100;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.0f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(45, -30, 0);

            Box("Start / acceleration lane", new Vector3(0, -0.5f, 4), new Vector3(20, 1, 20), floor);
            for (int z = -4; z <= 13; z += 2)
                Box("Runway marking", new Vector3(0, 0.008f, z), new Vector3(0.08f, 0.012f, 0.7f), white, false);

            // Left lane: normal stairs and a walkable slope.
            for (int i = 0; i < 5; i++)
            {
                float height = (i + 1) * 0.2f;
                Box("Stair " + (i + 1), new Vector3(-6, height / 2, 4 + i * 0.85f), new Vector3(3, height, 0.85f), stone);
            }
            Box("Stair landing", new Vector3(-6, 0.5f, 9.4f), new Vector3(3, 1, 3), stone);
            var ramp = Box("30 degree ramp", new Vector3(-6, 0.7f, 12.3f), new Vector3(3, 0.22f, 3), stone);
            ramp.transform.rotation = Quaternion.Euler(30, 0, 0);

            // Right lane: the 1.2 m tunnel only admits the crouched capsule.
            Box("Tunnel roof (1.2m clearance)", new Vector3(6, 1.4f, 5), new Vector3(3.4f, 0.4f, 5), blue);
            Box("Tunnel left wall", new Vector3(4.45f, 0.6f, 5), new Vector3(0.3f, 1.2f, 5), blue);
            Box("Tunnel right wall", new Vector3(7.55f, 0.6f, 5), new Vector3(0.3f, 1.2f, 5), blue);
            Box("Tunnel approach marking", new Vector3(6, 0.012f, 1.7f), new Vector3(2.6f, 0.02f, 0.15f), blue, false);

            // Three short jumps, then a slightly wider running jump.
            Box("Jump takeoff stripe", new Vector3(0, 0.015f, 13.4f), new Vector3(4, 0.02f, 0.3f), orange, false);
            Box("Jump 1", new Vector3(0, -0.25f, 16.1f), new Vector3(3, 0.5f, 2.4f), orange);
            Box("Jump 2", new Vector3(1.4f, 0, 20f), new Vector3(3, 1, 2.4f), orange);
            Box("Jump 3", new Vector3(-0.6f, 0.25f, 24f), new Vector3(3, 1.5f, 2.4f), orange);
            Box("Finish deck", new Vector3(0, 0.25f, 29f), new Vector3(7, 1.5f, 4), floor);
            Box("Finish stripe", new Vector3(0, 1.012f, 29.3f), new Vector3(6, 0.02f, 0.2f), orange, false);
            Box("Pit floor", new Vector3(0, -7, 15), new Vector3(50, 1, 80), floor);
            Box("Back wall", new Vector3(0, 1, -6.3f), new Vector3(20, 2, 0.4f), stone);
            Box("Left boundary", new Vector3(-10.2f, 0.8f, 4), new Vector3(0.4f, 1.6f, 20), stone);
            Box("Right boundary", new Vector3(10.2f, 0.8f, 4), new Vector3(0.4f, 1.6f, 20), stone);

            var player = new GameObject("Player");
            player.transform.position = new Vector3(0, 0.08f, -3);
            var cc = player.AddComponent<CharacterController>();
            cc.height = profile.standingHeight;
            cc.radius = profile.radius;
            cc.center = Vector3.up * profile.standingHeight / 2;
            cc.skinWidth = profile.skinWidth;
            cc.slopeLimit = profile.slopeLimit;
            cc.stepOffset = profile.stepHeight;
            cc.minMoveDistance = 0;
            var motor = player.AddComponent<SourceMotor>();
            motor.profile = profile;
            var cameraObject = new GameObject("PlayerCamera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(player.transform, false);
            cameraObject.transform.localPosition = Vector3.up * profile.standingEyeHeight;
            var camera = cameraObject.GetComponent<Camera>();
            camera.nearClipPlane = 0.04f;
            camera.farClipPlane = 150;
            camera.fieldOfView = profile.fieldOfView;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = RenderSettings.fogColor;
            var socket = new GameObject("ViewmodelSocket");
            socket.transform.SetParent(cameraObject.transform, false);
            var controller = player.AddComponent<SourcePlayerController>();
            controller.playerCamera = camera;
            CharacterAssetBuilder.Install(player, camera, socket.transform);
            PrefabUtility.SaveAsPrefabAsset(player, Root + "/Prefabs/SourcePlayer.prefab");
            var demo = new GameObject("Movement Lab HUD").AddComponent<MovementDemo>();
            demo.player = controller;
            demo.spawn = player.transform.position;
            CombatAssetBuilder.AddTrainingTargets(profile);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.productName = "HalliGalli Deathrun - Movement Lab";
            PlayerSettings.companyName = "HalliGalli Prototype";
            PlayerSettings.runInBackground = false;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            var time = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset");
            if (time.Length > 0)
            {
                var serialized = new SerializedObject(time[0]);
                var timestep = serialized.FindProperty("Fixed Timestep");
                if (timestep != null)
                {
                    if (timestep.propertyType == SerializedPropertyType.Float) timestep.floatValue = 0.015f;
                    else
                    {
                        // Unity 6.6 stores time as a rational count instead of a float.
                        var count = timestep.FindPropertyRelative("m_Count");
                        var rate = timestep.FindPropertyRelative("m_Rate");
                        if (count != null && rate != null)
                        {
                            long numerator = rate.FindPropertyRelative("m_Numerator").longValue;
                            long denominator = rate.FindPropertyRelative("m_Denominator").longValue;
                            count.longValue = (long)System.Math.Round(0.015 * numerator / denominator);
                        }
                    }
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("MOVEMENT_LAB_CREATED: " + ScenePath);
        }

        public static void BuildAndValidate()
        {
            Build();
            var validation = new GameObject("Movement validation");
            validation.AddComponent<MovementValidationRunner>();
            EditorApplication.isPlaying = true;
        }

        public static void CaptureAndBuildWindows()
        {
            EditorSceneManager.OpenScene(ScenePath);
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string previews = Path.Combine(root, "Previews");
            Directory.CreateDirectory(previews);
            var camera = GameObject.Find("PlayerCamera").GetComponent<Camera>();
            camera.transform.rotation = Quaternion.Euler(8, 0, 0);
            var driver = GameObject.Find("Player").GetComponent<CharacterMotionDriver>();
            CharacterAssetBuilder.Render(camera, driver.handCamera, Path.Combine(previews, "MovementLab_FirstPerson.png"));
            driver.handCamera.enabled = false;
            camera.transform.SetParent(null, true);
            camera.transform.position = new Vector3(24, 25, -25);
            camera.transform.LookAt(new Vector3(0, 0, 12));
            camera.fieldOfView = 52;
            Render(camera, Path.Combine(previews, "MovementLab_Overview.png"));
            // Reload the saved scene so preview camera placement never enters the build.
            EditorSceneManager.OpenScene(ScenePath);
            string buildFolder = Path.Combine(root, "Build/Windows");
            Directory.CreateDirectory(buildFolder);
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { ScenePath }, locationPathName = Path.Combine(buildFolder, "HalliGalliMovement.exe"),
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            Directory.CreateDirectory(Path.Combine(root, "TestResults"));
            File.WriteAllText(Path.Combine(root, "TestResults/windows-build.txt"), report.summary.result + "\nErrors: " + report.summary.totalErrors + "\nWarnings: " + report.summary.totalWarnings);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new System.Exception("Windows build failed");
            AssetDatabase.ExportPackage(new[]{Root,"Assets/HalliGalliCards"}, Path.Combine(root, "Exports/HalliGalliMovement.unitypackage"), ExportPackageOptions.Recurse);
            Debug.Log("MOVEMENT_WINDOWS_BUILD_SUCCEEDED");
        }

        private static void Render(Camera camera, string path)
        {
            var target = new RenderTexture(1600, 1000, 24);
            var previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            Object.DestroyImmediate(image);
            target.Release();
            Object.DestroyImmediate(target);
        }

        private static Material Material(string name, Color color)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            Shader shader = Shader.Find("Standard");
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
                shader = Shader.Find("Universal Render Pipeline/Lit") ?? shader;
            material = new Material(shader) { color = color };
            material.SetFloat("_Glossiness", 0.15f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static GameObject Box(string name, Vector3 position, Vector3 size, Material material, bool solid = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            go.GetComponent<Collider>().enabled = solid;
            return go;
        }
    }
}
