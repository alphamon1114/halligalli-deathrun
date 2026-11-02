using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HalliGalli.EditorTools
{
    // Copies the saved starter scene; never regenerates another official course or its assets.
    public static class RaceCourseBuilder
    {
        public const string Id = "item-race";
        public const string ScenePath = MovementProjectBuilder.Root + "/Demo/RaceCourse.unity";
        public const float FinishZ = 220;
        const float TrackWidth = 14;
        const float TrackEnd = 226;
        const string ThumbnailPath = MovementProjectBuilder.Root + "/Resources/UI/Maps/Race.png";
        static readonly float[] BoxRows = { 20, 46, 72, 98, 124, 150, 176, 202 };
        static readonly float[] BoxLanes = { -4.5f, -1.5f, 1.5f, 4.5f };

        [MenuItem("HalliGalli/Maps/Build Item Race Only")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before building the item race.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var profile = AssetDatabase.LoadAssetAtPath<SourceMovementProfile>(MovementProjectBuilder.Root + "/Settings/DefaultMovement.asset");
            if (!profile || !CourseMapsBuilder.Catalog) throw new InvalidOperationException("The original movement profile and saved map catalog are required.");
            foreach (string key in new[] { "ConcreteFloor", "ConcreteColumn", "ConcreteWall", "CeilingLight", "MountainRock", "MountainPine" })
                if (!EnvironmentAssetImporter.Prefab(key)) throw new FileNotFoundException("The existing environment prefab is missing: " + key);
            var palette = new Palette();
            var scene = EditorSceneManager.OpenScene(CourseBuilder.ScenePath);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Could not create the race scene copy.");
            var session = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CourseSession>(true)).Single();
            if (!session.roster || !session.maps || !session.checkpoints || !session.cardGame || !session.bellTarget || !session.bellButton || !session.bellAudio)
                throw new InvalidOperationException("The saved starter scene is missing its original participants, cards, map controller or service bell.");
            var roster = session.roster;
            if (roster.allParticipants.Length != 6) throw new InvalidOperationException("The original six-seat roster is required.");
            PrepareTemplate(session);

            var root = Node(null, "Item race course", Vector3.zero);
            var course = root.gameObject.AddComponent<RaceCourse>();
            course.session = session;
            course.finishZ = FinishZ;
            BuildTrack(root, palette);
            BuildScenery(root, palette);
            course.itemBoxes = BuildBoxes(root, palette);
            BuildRoutes(roster);
            PlaceBell(session, root, palette);
            session.fallHeight = -12;
            session.maps.current = Register(profile);
            session.maps.catalog = CourseMapsBuilder.Catalog;
            foreach (var participant in roster.allParticipants) participant.GetComponent<SourceMotor>().profile = profile;
            LightScene(scene);
            RenderThumbnail();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the item race scene.");

            // Keep all unrelated build entries intact; only add/enable the new dedicated scene.
            var scenes = EditorBuildSettings.scenes.ToList();
            int entry = scenes.FindIndex(item => item.path == ScenePath);
            if (entry >= 0) scenes[entry] = new EditorBuildSettingsScene(ScenePath, true);
            else scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("ITEM_RACE_CREATED " + ScenePath + " length=" + FinishZ + " boxes=" + course.itemBoxes.Length);
        }

        [MenuItem("HalliGalli/Maps/Build Saved Windows Player")]
        public static void BuildPlayer()
        {
            // Build() is deliberately separate: packaging must not overwrite any saved scene.
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            Directory.CreateDirectory("Build/Windows");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = CourseMapsBuilder.BuildScenePaths(),
                locationPathName = Path.GetFullPath("Build/Windows/HalliGalliMovement.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Windows player build failed: " + report.summary.result);
            Debug.Log("ITEM_RACE_PLAYER_BUILT");
        }

        static void PrepareTemplate(CourseSession session)
        {
            TutorialFerryBuilder.RemoveFromTemplate(session);
            var roster = session.roster;
            var keep = new HashSet<GameObject>();
            Action<Component> preserve = component => { if (component) keep.Add(component.transform.root.gameObject); };
            preserve(session); preserve(roster); preserve(session.maps); preserve(session.checkpoints);
            preserve(session.cardGame); preserve(session.cardGame.display); preserve(session.cardGame.tableView); preserve(session.bellTarget);
            foreach (var participant in roster.allParticipants) {
                preserve(participant); preserve(participant.seatAnchor); preserve(participant.standAnchor); preserve(participant.retryAnchor);
                var bot = participant.GetComponent<CourseRival>();
                if (bot) bot.route = Array.Empty<Transform>();
            }
            foreach (var chair in roster.chairs) preserve(chair);
            foreach (var route in roster.routeRoots) {
                preserve(route);
                foreach (var child in route.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            }
            var scene = session.gameObject.scene;
            foreach (var hud in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CourseHUD>(true))) preserve(hud);
            foreach (var root in scene.GetRootGameObjects()) {
                if (root.name == "VARCO environment") {
                    foreach (var child in root.transform.Cast<Transform>().ToArray())
                        if (child.name != "CardTable") Object.DestroyImmediate(child.gameObject);
                    keep.Add(root);
                }
                if (root.name == "Table deck" || root.name == "Table top" || root.name == "Table felt" || root.name == "Table leg"
                    || root.name == "START" || root.name == "Bell plinth" || root.name == "RING"
                    || root.name == "Platform support" && root.transform.position.z <= .01f)
                    keep.Add(root);
                var light = root.GetComponent<Light>();
                if (light && light.type == LightType.Directional) keep.Add(root);
            }
            foreach (var root in scene.GetRootGameObjects()) if (!keep.Contains(root)) Object.DestroyImmediate(root);

            // Roster layout still expects these transforms, but the race has one continuous collision surface.
            for (int i = 0; i < roster.firstPlatforms.Length; i++) {
                Transform parent = roster.firstPlatforms[i].parent;
                Object.DestroyImmediate(roster.firstPlatforms[i].gameObject);
                roster.firstPlatforms[i] = Node(parent, "Race unused lane anchor " + i, Vector3.zero);
            }
            Transform mergeParent = roster.mergeSurface.parent.parent;
            Object.DestroyImmediate(roster.mergeSurface.parent.gameObject);
            var merge = Node(mergeParent, "Race unused merge anchors", Vector3.zero);
            roster.mergeSurface = Node(merge, "Surface", Vector3.zero);
            foreach (var child in session.checkpoints.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            session.checkpoints.points = Array.Empty<CourseCheckpoints.Point>();
            session.factory = null; session.ferry = null; session.userMap = null;
            roster.branches = null; roster.surf = null;
        }

        static void BuildTrack(Transform parent, Palette p)
        {
            var root = Node(parent, "Continuous raceway", Vector3.zero);
            // A single seamless collider meets the untouched start deck at Z=0, with no gaps or speed modifiers.
            var floor = Box(root, "Continuous running surface", new Vector3(0, -.3f, TrackEnd * .5f), new Vector3(TrackWidth, .6f, TrackEnd + .04f), p.deck, true);
            floor.GetComponent<Renderer>().enabled = false;
            for (float start = 0; start < TrackEnd; start += 8) {
                float length = Mathf.Min(8, TrackEnd - start);
                Prop("ConcreteFloor", root, new Vector3(0, -.14f, start + length * .5f), new Vector3(TrackWidth, .28f, length));
            }
            foreach (int side in new[] { -1, 1 }) {
                // Raised perimeter barriers are deliberate safety collision, never decorative obstructions in a lane.
                Box(root, "Outer safety barrier", new Vector3(side * 7.35f, .55f, TrackEnd * .5f), new Vector3(.5f, 1.1f, TrackEnd), p.steel, true);
                Box(root, "Raised rail cap", new Vector3(side * 7.35f, 1.13f, TrackEnd * .5f), new Vector3(.56f, .1f, TrackEnd), p.dark, false);
                for (int i = 0; i < 28; i++) {
                    float z = 4 + i * 8;
                    Box(root, "Barrier upright", new Vector3(side * 7.35f, .62f, z), new Vector3(.6f, 1.3f, .2f), p.dark, false);
                    Box(root, "Raised direction reflector", new Vector3(side * 7.08f, .75f, z), new Vector3(.025f, .14f, 1.2f), side < 0 ? p.cyan : p.gold, false);
                }
            }
            Box(root, "Finish safety barrier", new Vector3(0, .55f, TrackEnd + .25f), new Vector3(15.2f, 1.1f, .5f), p.steel, true);
        }

        static void BuildScenery(Transform parent, Palette p)
        {
            var art = Node(parent, "Raceway scenery without collision", Vector3.zero);
            // Original textured kit pieces surround the race; the entire 14m running surface remains clear.
            for (int i = 0; i < 9; i++) {
                float z = 7 + i * 26;
                foreach (int side in new[] { -1, 1 }) {
                    Prop("ConcreteColumn", art, new Vector3(side * 8.8f, 2.1f, z), new Vector3(.8f, 4.2f, .8f));
                    Prop("CeilingLight", art, new Vector3(side * 8.8f, 4.35f, z), new Vector3(1.8f, .22f, .55f));
                    Prop("MountainRock", art, new Vector3(side * (17 + i % 3 * 2), -.3f, z + 8), new Vector3(9, 5 + i % 3, 12), side * (i * 19 + 12));
                    Prop("MountainPine", art, new Vector3(side * (12 + i % 2 * 2), 3.6f, z + 6), new Vector3(4.3f, 8, 4.3f), i * 37);
                    Box(art, "Distance marker housing", new Vector3(side * 8.8f, 2.3f, z - .47f), new Vector3(1.15f, 1.3f, .16f), p.dark, false);
                    Label(art, Mathf.Min(220, Mathf.RoundToInt(z)).ToString("000"), new Vector3(side * 8.8f, 2.42f, z - .56f), .38f, p.white.color, p.text);
                    Label(art, "METRES", new Vector3(side * 8.8f, 2.02f, z - .56f), .13f, p.gold.color, p.text);
                }
            }
            Gate(art, 5, "ITEM RUN", "220 M  /  RUN TO THE BELL", p, false);
            Gate(art, 79, "01  /  OVERTAKE", "KEEP MOVING", p, false);
            Gate(art, 157, "02  /  FINAL STRAIGHT", "WATCH YOUR BACK", p, false);
            Gate(art, 218, "BELL", "RING TO CLAIM", p, true);
            // A low landscaped base and distant cliffs avoid an empty floating-block presentation.
            for (int i = 0; i < 8; i++) {
                float z = -9 + i * 34;
                foreach (int side in new[] { -1, 1 })
                    Prop("MountainRock", art, new Vector3(side * 24, -7.8f, z), new Vector3(35, 8, 38), i * 31);
            }
            // Keep the start table open to the sky: the card camera rises above 6m for larger rosters.
            // A low ConcreteFloor canopy hides the cards behind its road texture during table focus.
        }

        static void Gate(Transform parent, float z, string title, string subtitle, Palette p, bool finish)
        {
            var root = Node(parent, title + " arch", Vector3.zero);
            foreach (int side in new[] { -1, 1 }) {
                Prop("ConcreteColumn", root, new Vector3(side * 8, 2.85f, z), new Vector3(.9f, 5.7f, 1.1f));
                Box(root, "Arch vertical accent", new Vector3(side * 7.5f, 3, z - .58f), new Vector3(.13f, 3.8f, .06f), finish ? p.gold : p.cyan, false);
            }
            Prop("ConcreteWall", root, new Vector3(0, 5.25f, z), new Vector3(17, 1.8f, .65f));
            Box(root, "Arch sign face", new Vector3(0, 5.25f, z - .39f), new Vector3(10.5f, 1.3f, .08f), p.dark, false);
            Label(root, title, new Vector3(0, 5.52f, z - .45f), .65f, finish ? p.gold.color : p.white.color, p.text);
            Label(root, subtitle, new Vector3(0, 4.95f, z - .45f), .21f, p.cyan.color, p.text);
        }

        static Transform[] BuildBoxes(Transform parent, Palette p)
        {
            var root = Node(parent, "Random pickup boxes", Vector3.zero);
            var boxes = new List<Transform>();
            for (int row = 0; row < BoxRows.Length; row++) for (int lane = 0; lane < BoxLanes.Length; lane++) {
                var box = Node(root, "Item box " + (row + 1) + "-" + (lane + 1), new Vector3(BoxLanes[lane], 1.05f, BoxRows[row]));
                var shell = Node(box, "Question crate", Vector3.zero);
                shell.localRotation = Quaternion.Euler(0, 18, 0);
                Box(shell, "Metal core", Vector3.zero, Vector3.one * .76f, p.steel, false);
                Material accent = (row + lane) % 2 == 0 ? p.cyan : p.gold;
                foreach (int side in new[] { -1, 1 }) {
                    Box(shell, "Face panel", new Vector3(0, 0, side * .39f), new Vector3(.64f, .64f, .025f), p.dark, false);
                    var label = Label(shell, "?", new Vector3(0, 0, side * .415f), 1.1f, accent.color, p.text);
                    if (side > 0) label.localRotation = Quaternion.Euler(0, 180, 0);
                    foreach (int corner in new[] { -1, 1 }) {
                        Box(shell, "Crate horizontal trim", new Vector3(0, corner * .4f, side * .4f), new Vector3(.87f, .09f, .09f), accent, false);
                        Box(shell, "Crate upright trim", new Vector3(corner * .4f, 0, side * .4f), new Vector3(.09f, .87f, .09f), accent, false);
                        Box(shell, "Crate side trim", new Vector3(corner * .4f, side * .4f, 0), new Vector3(.09f, .09f, .87f), accent, false);
                    }
                }
                boxes.Add(box);
            }
            return boxes.ToArray();
        }

        static void BuildRoutes(CourseRoster roster)
        {
            for (int seat = 0; seat < roster.routeRoots.Length; seat++) {
                var route = roster.routeRoots[seat];
                float startX = (seat - 2.5f) * CourseRoster.SeatSpacing;
                float laneX = BoxLanes[seat % BoxLanes.Length];
                Node(route, "Leave start deck", new Vector3(startX, .025f, 2.3f));
                Node(route, "Join raceway", new Vector3(startX, .025f, 5.7f));
                Node(route, "Choose pickup lane", new Vector3(laneX, .025f, 12));
                for (int row = 0; row < BoxRows.Length; row++) Node(route, "Pickup row " + (row + 1), new Vector3(laneX, .025f, BoxRows[row]));
                Node(route, "Approach original bell", new Vector3(0, .025f, 213));
                Node(route, "Ring original bell", new Vector3(0, .025f, FinishZ - .8f));
                var bot = roster.allParticipants[seat].GetComponent<CourseRival>();
                if (bot) bot.route = route.Cast<Transform>().ToArray();
            }
        }

        static void PlaceBell(CourseSession session, Transform parent, Palette p)
        {
            var roots = session.gameObject.scene.GetRootGameObjects();
            var plinth = roots.Single(root => root.name == "Bell plinth").transform;
            plinth.position = new Vector3(0, .35f, FinishZ);
            session.bellTarget.position = new Vector3(0, .7f, FinishZ);
            var label = roots.FirstOrDefault(root => root.name == "RING");
            if (label) label.transform.SetPositionAndRotation(new Vector3(0, .4f, FinishZ - .34f), Quaternion.identity);
            // Side podiums frame, rather than block, the original reachable service bell.
            foreach (int side in new[] { -1, 1 }) {
                Prop("ConcreteColumn", parent, new Vector3(side * 3.6f, .7f, FinishZ + 1), new Vector3(.8f, 1.4f, .8f));
                Box(parent, "Finish beacon", new Vector3(side * 3.6f, 1.5f, FinishZ + 1), new Vector3(.55f, .2f, .55f), p.gold, false);
            }
        }

        static CourseMapDefinition Register(SourceMovementProfile profile)
        {
            string path = CourseMapsBuilder.Maps + "/" + Id + ".asset";
            var map = AssetDatabase.LoadAssetAtPath<CourseMapDefinition>(path);
            if (!map) {
                map = ScriptableObject.CreateInstance<CourseMapDefinition>();
                AssetDatabase.CreateAsset(map, path);
            }
            map.id = Id; map.displayName = "아이템 레이스"; map.style = CourseMapStyle.Race; map.difficulty = 2;
            map.description = "220m 연속 트랙. 선두는 바나나·바리게이트, 추격자는 미사일 중심의 랜덤 아이템으로 경쟁합니다.";
            map.scenePath = ScenePath; map.movementProfile = profile; map.playable = true; map.thumbnail = "Race";
            var catalog = CourseMapsBuilder.Catalog;
            catalog.maps = (catalog.maps ?? Array.Empty<CourseMapDefinition>()).Where(existing => existing && existing.id != Id).Append(map).ToArray();
            EditorUtility.SetDirty(map); EditorUtility.SetDirty(catalog);
            return map;
        }

        static void LightScene(UnityEngine.SceneManagement.Scene scene)
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.56f, .63f, .68f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(.48f, .63f, .7f);
            RenderSettings.fogStartDistance = 125; RenderSettings.fogEndDistance = 330;
            foreach (var root in scene.GetRootGameObjects()) foreach (var light in root.GetComponentsInChildren<Light>(true)) {
                if (light.type != LightType.Directional) continue;
                light.intensity = light.name == "Key" ? 1.12f : .32f;
                light.color = light.name == "Key" ? new Color(1, .94f, .82f) : new Color(.65f, .82f, 1);
                light.shadows = LightShadows.Soft;
            }
            foreach (var root in scene.GetRootGameObjects()) foreach (var camera in root.GetComponentsInChildren<Camera>(true)) {
                if (camera.clearFlags == CameraClearFlags.Depth || camera.clearFlags == CameraClearFlags.Nothing) continue;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = RenderSettings.fogColor;
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, 360);
            }
        }

        static void RenderThumbnail()
        {
            var camera = new GameObject("Race thumbnail camera").AddComponent<Camera>();
            var target = new RenderTexture(960, 480, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(960, 480, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try {
                camera.enabled = false;
                camera.cullingMask = ~((1 << CharacterMotionDriver.HandsLayer) | (1 << 8));
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = RenderSettings.fogColor;
                camera.fieldOfView = 58; camera.farClipPlane = 360;
                camera.transform.position = new Vector3(12, 9, 7);
                camera.transform.LookAt(new Vector3(0, 1.2f, 53));
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(ThumbnailPath, image.EncodeToPNG());
            } finally {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.DestroyImmediate(image); Object.DestroyImmediate(target); Object.DestroyImmediate(camera.gameObject);
            }
            AssetDatabase.ImportAsset(ThumbnailPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(ThumbnailPath);
            importer.textureType = TextureImporterType.Default; importer.mipmapEnabled = false;
            importer.maxTextureSize = 1024; importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        static Transform Node(Transform parent, string name, Vector3 position)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false); node.localPosition = position;
            return node;
        }

        static Transform Box(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool solid)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name; box.transform.SetParent(parent, false); box.transform.localPosition = position; box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = material;
            if (!solid) Object.DestroyImmediate(box.GetComponent<Collider>());
            return box.transform;
        }

        static Transform Prop(string key, Transform parent, Vector3 center, Vector3 size, float yaw = 0)
        {
            var instance = EnvironmentArtBuilder.Place(key, parent, center, size, Quaternion.Euler(0, yaw, 0));
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            return instance;
        }

        static Transform Label(Transform parent, string text, Vector3 position, float size, Color color, Material material)
        {
            var label = new GameObject(text).AddComponent<TextMesh>();
            label.transform.SetParent(parent, false); label.transform.localPosition = position;
            label.text = text; label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center;
            label.fontSize = 100; label.characterSize = size * .1f; label.color = color;
            label.GetComponent<Renderer>().sharedMaterial = material;
            return label.transform;
        }

        sealed class Palette
        {
            public readonly Material deck = Load("CourseDeck"), dark = Load("CourseDark"), steel = Load("FactorySteel"),
                cyan = Load("CourseCyan"), gold = Load("CourseGold"), white = Load("CourseWhite"), text = Load("CourseText");
            static Material Load(string name)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(MovementProjectBuilder.Root + "/Materials/" + name + ".mat");
                if (!material) throw new FileNotFoundException("Original raceway material is missing: " + name);
                return material;
            }
        }
    }
}
