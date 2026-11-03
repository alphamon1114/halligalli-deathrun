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
    // Copies the saved starter scene. All newly baked art belongs to Clockwork alone.
    public static class ClockworkCourseBuilder
    {
        public const string Id = "clockwork";
        public const string ScenePath = MovementProjectBuilder.Root + "/Demo/ClockworkCourse.unity";
        public const float FinishZ = 128;
        const float Width = 8, EndZ = 132;
        const string Art = MovementProjectBuilder.Root + "/Art/Clockwork";
        const string Thumbnail = MovementProjectBuilder.Root + "/Resources/UI/Maps/Clockwork.png";
        static readonly float[] TrapZ = { 20, 32, 58, 70, 94, 106 };
        static readonly float[] RouteZ = { 2.3f, 5.7f, 11, 17, 23, 29, 35, 45, 51, 55, 61, 67, 73, 82, 87, 91, 97, 103, 109, 119, 127.2f };

        [MenuItem("HalliGalli/Maps/Build Clockwork Fortress Only")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before building Clockwork Fortress.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var profile = AssetDatabase.LoadAssetAtPath<SourceMovementProfile>(MovementProjectBuilder.Root + "/Settings/DefaultMovement.asset");
            if (!profile || !CourseMapsBuilder.Catalog) throw new InvalidOperationException("The saved starter movement profile and map catalog are required.");
            foreach (string key in new[] { "ConcreteFloor", "ConcreteWall", "ConcreteColumn", "UtilityPipes", "CeilingLight", "MountainRock" })
                if (!EnvironmentAssetImporter.Prefab(key)) throw new FileNotFoundException("Existing environment prefab missing: " + key);
            var palette = new Palette();
            var shapes = new Shapes();
            var scene = EditorSceneManager.OpenScene(CourseBuilder.ScenePath);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Could not copy the saved starter scene.");
            var session = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CourseSession>(true)).Single();
            if (!session.roster || session.roster.allParticipants.Length != 6 || !session.maps || !session.checkpoints
                || !session.cardGame || !session.bellTarget || !session.bellButton || !session.bellAudio)
                throw new InvalidOperationException("Clockwork requires the original six seats, cards, checkpoints and service bell.");
            PrepareTemplate(session);
            var root = Node(null, "Clockwork Fortress", Vector3.zero);
            var course = root.gameObject.AddComponent<ClockworkCourse>();
            course.session = session;
            session.clockwork = course;
            BuildFloor(root, session, palette);
            BuildFortress(root, palette, shapes);
            course.traps = new ClockworkTrap[TrapZ.Length];
            for (int i = 0; i < TrapZ.Length; i++)
                course.traps[i] = BuildTrap(root, session, i, palette, shapes);
            BuildRoutes(session.roster);
            PlaceBell(session, root, palette, shapes);
            session.fallHeight = -5;
            session.maps.current = Register(profile);
            session.maps.catalog = CourseMapsBuilder.Catalog;
            foreach (var participant in session.roster.allParticipants) participant.GetComponent<SourceMotor>().profile = profile;
            session.roster.ApplyLayout(6);
            LightScene(scene);
            RenderThumbnail(course);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Clockwork Fortress.");
            var scenes = EditorBuildSettings.scenes.ToList();
            int entry = scenes.FindIndex(item => item.path == ScenePath);
            if (entry >= 0) scenes[entry] = new EditorBuildSettingsScene(ScenePath, true);
            else scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("CLOCKWORK_CREATED " + ScenePath + " length=128 traps=6 checkpoints=2 seats=6");
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
                    || root.name == "Platform support" && root.transform.position.z <= .01f) keep.Add(root);
                var light = root.GetComponent<Light>();
                if (light && light.type == LightType.Directional) keep.Add(root);
            }
            foreach (var root in scene.GetRootGameObjects()) if (!keep.Contains(root)) Object.DestroyImmediate(root);
            // Retain the layout API's anchors, not the old course's stepping-stone colliders.
            for (int i = 0; i < roster.firstPlatforms.Length; i++) {
                var parent = roster.firstPlatforms[i].parent;
                Object.DestroyImmediate(roster.firstPlatforms[i].gameObject);
                roster.firstPlatforms[i] = Node(parent, "Clockwork lane anchor " + i, Vector3.zero);
            }
            var mergeParent = roster.mergeSurface.parent.parent;
            Object.DestroyImmediate(roster.mergeSurface.parent.gameObject);
            var merge = Node(mergeParent, "Clockwork merge anchors", Vector3.zero);
            roster.mergeSurface = Node(merge, "Surface", Vector3.zero);
            foreach (var child in session.checkpoints.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            session.checkpoints.points = Array.Empty<CourseCheckpoints.Point>();
            session.factory = null; session.ferry = null; session.userMap = null;
            roster.branches = null; roster.surf = null;
        }

        static void BuildFloor(Transform root, CourseSession session, Palette p)
        {
            var floor = Node(root, "Stone causeway and safe landings", Vector3.zero);
            // Separate, flush floor pieces make checkpoint raycasts unambiguous; no duplicate floor under a landing.
            FloorCollider(floor, "Start apron", 0, 8, 10);
            FloorCollider(floor, "Spear hall floor", 8, 42, Width);
            var first = FloorCollider(floor, "Checkpoint I safe deck", 42, 48, Width);
            FloorCollider(floor, "Arrow hall floor", 48, 79, Width);
            var second = FloorCollider(floor, "Checkpoint II safe deck", 79, 85, Width);
            FloorCollider(floor, "Mixed hall and bell floor", 85, EndZ, Width);
            for (float z = 0; z < EndZ; z += 4)
                Prop("ConcreteFloor", floor, new Vector3(0, -.15f, z + 2), new Vector3(z < 8 ? 10 : Width, .3f, 4));
            session.checkpoints.session = session;
            session.checkpoints.points = new[] {
                Checkpoint(session.checkpoints.transform, first, 45, 7, "I", p),
                Checkpoint(session.checkpoints.transform, second, 82, 13, "II", p)
            };
        }

        static BoxCollider FloorCollider(Transform parent, string name, float from, float to, float width)
        {
            var floor = Node(parent, name, new Vector3(0, -.3f, (from + to) * .5f));
            var collider = floor.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(width, .6f, to - from);
            return collider;
        }

        static CourseCheckpoints.Point Checkpoint(Transform parent, BoxCollider floor, float z, int resume, string title, Palette p)
        {
            var spawn = Node(parent, "Clockwork checkpoint " + title, new Vector3(0, .025f, z));
            var outline = Node(parent, "Checkpoint " + title + " outline", Vector3.zero).gameObject.AddComponent<LineRenderer>();
            outline.loop = true; outline.positionCount = 4; outline.widthMultiplier = .045f;
            outline.sharedMaterial = p.checkpoint;
            outline.SetPositions(new[] { new Vector3(-3.5f, .025f, z - 2), new Vector3(3.5f, .025f, z - 2), new Vector3(3.5f, .025f, z + 2), new Vector3(-3.5f, .025f, z + 2) });
            foreach (int side in new[] { -1, 1 }) {
                Box(parent, "Checkpoint plaque", new Vector3(side * 3.65f, 1.65f, z + 1.5f), new Vector3(.65f, .8f, .12f), p.dark);
                Label(parent, title, new Vector3(side * 3.65f, 1.83f, z + 1.42f), .62f, p.safe.color, p.text);
                Label(parent, "SAVED", new Vector3(side * 3.65f, 1.49f, z + 1.42f), .15f, p.safe.color, p.text);
            }
            return new CourseCheckpoints.Point { spawn = spawn, landing = floor, resumeWaypoint = resume };
        }

        static void BuildFortress(Transform parent, Palette p, Shapes shapes)
        {
            var root = Node(parent, "Textured fortress architecture", Vector3.zero);
            foreach (int side in new[] { -1, 1 }) {
                var wall = Node(root, "Solid outer fortress wall", new Vector3(side * 4.45f, 3, 70));
                wall.gameObject.AddComponent<BoxCollider>().size = new Vector3(.9f, 6, 124);
                for (int z = 10; z < EndZ; z += 4)
                    Prop("ConcreteWall", root, new Vector3(side * 4.45f, 2.7f, z), new Vector3(4.02f, 5.4f, .9f), 90);
                for (int z = 12; z <= 132; z += 12) {
                    Prop("ConcreteColumn", root, new Vector3(side * 4.7f, 3, z), new Vector3(1.1f, 6, 1.2f));
                    Prop("ConcreteColumn", root, new Vector3(side * 4.7f, 6.15f, z), new Vector3(1.4f, .3f, 1.5f));
                    var gear = MeshObject(root, "Bronze wall gear", new Vector3(side * 3.97f, 3.7f, z + 4), shapes.gear, p.bronze);
                    gear.localRotation = Quaternion.Euler(0, side * 90, 0);
                    gear.localScale = Vector3.one * .85f;
                    Prop("CeilingLight", root, new Vector3(side * 3.95f, 2.7f, z), new Vector3(.36f, .65f, .45f), side * 90);
                }
                for (int z = 16; z <= 128; z += 24)
                    Prop("MountainRock", root, new Vector3(side * 10, -3.4f, z), new Vector3(14, 6, 26), z * side);
            }
            var end = Node(root, "Solid back wall", new Vector3(0, 3, EndZ + .45f));
            end.gameObject.AddComponent<BoxCollider>().size = new Vector3(9.8f, 6, .9f);
            Prop("ConcreteWall", root, new Vector3(0, 2.7f, EndZ + .45f), new Vector3(9.8f, 5.4f, .9f));
            Gate(root, 12, "01 / SPEARS", "AMBER: WAIT   GREEN: CROSS", p, shapes, ClockworkTrapKind.Spears);
            Gate(root, 50, "02 / ARROWS", "WATCH THE WALLS   WAIT FOR GREEN", p, shapes, ClockworkTrapKind.Arrows);
            Gate(root, 86, "03 / CLOCKWORK", "SAME RHYTHM   TWO TRAP TYPES", p, shapes, ClockworkTrapKind.Spears);
            Gate(root, 121, "THE BELL", "128 METRES / RING TO CLAIM", p, shapes, ClockworkTrapKind.Arrows);
            // Open sky over the untouched table keeps the six-player card camera unobstructed.
        }

        static void Gate(Transform parent, float z, string title, string subtitle, Palette p, Shapes shapes, ClockworkTrapKind icon)
        {
            var root = Node(parent, title + " arch", new Vector3(0, 0, z));
            foreach (int side in new[] { -1, 1 }) {
                Prop("ConcreteColumn", root, new Vector3(side * 4.65f, 2.7f, 0), new Vector3(1.25f, 5.4f, 1.4f));
                Prop("ConcreteColumn", root, new Vector3(side * 4.65f, .3f, 0), new Vector3(1.5f, .6f, 1.65f));
                var iconRoot = Node(root, "Engraved trap emblem", new Vector3(side * 3.45f, 4.6f, -.48f));
                var symbol = MeshObject(iconRoot, "Trap silhouette", Vector3.zero, icon == ClockworkTrapKind.Spears ? shapes.spear : shapes.arrow, p.steel);
                symbol.localScale = Vector3.one * .6f;
                if (icon == ClockworkTrapKind.Spears) symbol.localPosition = Vector3.down * .5f;
            }
            Prop("ConcreteWall", root, new Vector3(0, 4.65f, 0), new Vector3(9.8f, 1.55f, .8f));
            Box(root, "Bronze sign frame", new Vector3(0, 4.65f, -.44f), new Vector3(6.3f, 1.25f, .13f), p.bronze);
            Box(root, "Inset engraved sign", new Vector3(0, 4.65f, -.52f), new Vector3(6, 1.02f, .04f), p.dark);
            Label(root, title, new Vector3(0, 4.88f, -.55f), .58f, p.ivory.color, p.text);
            Label(root, subtitle, new Vector3(0, 4.39f, -.55f), .185f, p.ivory.color, p.text);
            var gear = MeshObject(root, "Crown gear", new Vector3(0, 5.95f, 0), shapes.gear, p.bronze);
            gear.localScale = Vector3.one * .85f;
        }

        static ClockworkTrap BuildTrap(Transform parent, CourseSession session, int index, Palette p, Shapes shapes)
        {
            bool spears = index == 0 || index == 1 || index == 4;
            var root = Node(parent, (spears ? "Spears " : "Arrows ") + (index + 1), new Vector3(0, 0, TrapZ[index]));
            var trap = root.gameObject.AddComponent<ClockworkTrap>();
            trap.session = session; trap.kind = spears ? ClockworkTrapKind.Spears : ClockworkTrapKind.Arrows;
            trap.period = 5.6f; trap.phase = index % 2 == 0 ? 0 : 2.8f;
            trap.warningSeconds = .9f; trap.attackSeconds = 1.2f; trap.zoneSize = new Vector3(Width, 2.4f, 2);
            trap.spearHeight = 1.7f;
            trap.movingRoot = Node(root, spears ? "Retracting spear array" : "Crossing arrow volley", new Vector3(spears ? 0 : -5, spears ? -1.7f : .85f, 0));
            var hitboxes = new List<BoxCollider>();
            if (spears) {
                Box(root, "Recessed spear grate", new Vector3(0, .012f, 0), new Vector3(7.98f, .02f, 2), p.dark);
                MeshObject(root, "Perforated bronze grate", new Vector3(0, .027f, 0), shapes.grate, p.bronze);
                for (int row = -1; row <= 1; row++) for (int column = 0; column < 10; column++) {
                    var position = new Vector3(-3.6f + column * .8f, 0, row * .65f);
                    var spear = MeshObject(trap.movingRoot, "Emerging spear", position, shapes.spear, p.steel);
                    var hitbox = spear.gameObject.AddComponent<BoxCollider>();
                    hitbox.isTrigger = true; hitbox.center = Vector3.up * .85f; hitbox.size = new Vector3(.24f, 1.7f, .24f);
                    hitboxes.Add(hitbox);
                }
            } else {
                for (int row = -1; row <= 1; row++) {
                    var arrow = MeshObject(trap.movingRoot, "Flying arrow", new Vector3(0, 0, row * .65f), shapes.arrow, p.steel);
                    var hitbox = arrow.gameObject.AddComponent<BoxCollider>();
                    hitbox.isTrigger = true; hitbox.center = Vector3.left * .175f; hitbox.size = new Vector3(1.25f, .22f, .22f);
                    hitboxes.Add(hitbox);
                }
                foreach (int side in new[] { -1, 1 }) {
                    Box(root, "Bronze arrow housing", new Vector3(side * 4.12f, .85f, 0), new Vector3(.55f, 1.05f, 2.25f), p.bronze);
                    Box(root, "Dark launcher inset", new Vector3(side * 3.82f, .85f, 0), new Vector3(.025f, .6f, 1.95f), p.dark);
                    for (int row = -1; row <= 1; row++) {
                        var muzzle = MeshObject(root, "Arrow muzzle", new Vector3(side * 3.78f, .85f, row * .65f), shapes.muzzle, p.bronze);
                        muzzle.localRotation = Quaternion.Euler(0, 0, side * 90);
                    }
                    Prop("UtilityPipes", root, new Vector3(side * 4.02f, 1.75f, .25f), new Vector3(.28f, .8f, 1.45f), side * 90);
                }
            }
            trap.hitboxes = hitboxes.ToArray();
            foreach (int side in new[] { -1, 1 }) {
                Box(root, "Signal bronze backing", new Vector3(side * 3.63f, 1.7f, -1.55f), new Vector3(.48f, .66f, .15f), p.bronze);
                Label(root, (index + 1).ToString("00"), new Vector3(side * 3.63f, 2.2f, -1.66f), .3f, p.ivory.color, p.text);
            }
            trap.warning = MeshObject(root, "Paired phase signals", new Vector3(0, 1.7f, -1.65f), shapes.signals, p.signal).GetComponent<Renderer>();
            // Waiting instructions sit on the wall, not on a confusing floor-wide outline grid.
            Box(root, "Wait instruction plaque", new Vector3(-3.74f, 1.35f, -3), new Vector3(.42f, .65f, .08f), p.dark);
            Label(root, "WAIT", new Vector3(-3.74f, 1.37f, -3.06f), .14f, p.ivory.color, p.text);
            return trap;
        }

        static void BuildRoutes(CourseRoster roster)
        {
            for (int seat = 0; seat < roster.routeRoots.Length; seat++) {
                var route = roster.routeRoots[seat];
                for (int i = 0; i < RouteZ.Length; i++) {
                    float x = i == 0 ? (seat - 2.5f) * CourseRoster.SeatSpacing : i == 1 ? (seat - 2.5f) * .5f : 0;
                    var marker = Node(route, "Clockwork waypoint " + i, new Vector3(x, .025f, RouteZ[i]));
                    marker.gameObject.AddComponent<ClimbStep>().arrivalRadius = .45f;
                }
                var bot = roster.allParticipants[seat].GetComponent<CourseRival>();
                if (bot) bot.route = route.Cast<Transform>().ToArray();
            }
        }

        static void PlaceBell(CourseSession session, Transform root, Palette p, Shapes shapes)
        {
            var roots = session.gameObject.scene.GetRootGameObjects();
            roots.Single(item => item.name == "Bell plinth").transform.position = new Vector3(0, .35f, FinishZ);
            session.bellTarget.position = new Vector3(0, .7f, FinishZ);
            var label = roots.FirstOrDefault(item => item.name == "RING");
            if (label) label.transform.SetPositionAndRotation(new Vector3(0, .4f, FinishZ - .34f), Quaternion.identity);
            foreach (int side in new[] { -1, 1 }) {
                Prop("ConcreteColumn", root, new Vector3(side * 3.1f, .8f, 129), new Vector3(.9f, 1.6f, .9f));
                var gear = MeshObject(root, "Bell podium medallion", new Vector3(side * 3.1f, 1, 128.5f), shapes.gear, p.bronze);
                gear.localScale = Vector3.one * .36f;
            }
        }

        static CourseMapDefinition Register(SourceMovementProfile profile)
        {
            string path = CourseMapsBuilder.Maps + "/" + Id + ".asset";
            var map = AssetDatabase.LoadAssetAtPath<CourseMapDefinition>(path);
            if (!map) { map = ScriptableObject.CreateInstance<CourseMapDefinition>(); AssetDatabase.CreateAsset(map, path); }
            map.id = Id; map.displayName = "태엽 요새"; map.style = CourseMapStyle.Factory; map.difficulty = 2;
            map.description = "128m 태엽 요새. 예고등을 보고 솟아나는 창과 가로지르는 화살의 일정한 리듬을 익히세요. 두 안전 지점에서 재출발합니다.";
            map.scenePath = ScenePath; map.movementProfile = profile; map.playable = true; map.thumbnail = "Clockwork";
            var catalog = CourseMapsBuilder.Catalog;
            catalog.maps = (catalog.maps ?? Array.Empty<CourseMapDefinition>()).Where(existing => existing && existing.id != Id).Append(map).ToArray();
            EditorUtility.SetDirty(map); EditorUtility.SetDirty(catalog);
            return map;
        }

        static void LightScene(UnityEngine.SceneManagement.Scene scene)
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.49f, .51f, .51f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(.29f, .35f, .38f);
            RenderSettings.fogStartDistance = 72; RenderSettings.fogEndDistance = 185;
            foreach (var root in scene.GetRootGameObjects()) foreach (var light in root.GetComponentsInChildren<Light>(true)) {
                if (light.type != LightType.Directional) continue;
                light.intensity = light.name == "Key" ? 1.2f : .4f;
                light.color = light.name == "Key" ? new Color(1, .87f, .65f) : new Color(.63f, .78f, 1);
                light.shadows = LightShadows.Soft;
            }
            foreach (var root in scene.GetRootGameObjects()) foreach (var camera in root.GetComponentsInChildren<Camera>(true)) {
                if (camera.clearFlags == CameraClearFlags.Depth || camera.clearFlags == CameraClearFlags.Nothing) continue;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = RenderSettings.fogColor;
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, 220);
            }
        }

        static void RenderThumbnail(ClockworkCourse course)
        {
            var camera = new GameObject("Clockwork thumbnail camera").AddComponent<Camera>();
            var target = new RenderTexture(960, 480, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(960, 480, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var spear = course.traps[0].movingRoot;
            var oldPosition = spear.localPosition;
            var signal = course.traps[0].warning;
            var previousSignal = new MaterialPropertyBlock();
            signal.GetPropertyBlock(previousSignal);
            try {
                spear.localPosition = Vector3.zero;
                var activeSignal = new MaterialPropertyBlock();
                activeSignal.SetColor("_Color", new Color(1, .08f, .025f));
                activeSignal.SetColor("_EmissionColor", new Color(1, .08f, .025f) * 1.7f);
                signal.SetPropertyBlock(activeSignal);
                camera.enabled = false;
                camera.cullingMask = ~((1 << CharacterMotionDriver.HandsLayer) | (1 << 8));
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = RenderSettings.fogColor;
                camera.fieldOfView = 66; camera.farClipPlane = 220;
                camera.transform.position = new Vector3(2.8f, 3.1f, 8.5f);
                camera.transform.LookAt(new Vector3(0, 1.65f, 24));
                camera.targetTexture = target; camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                File.WriteAllBytes(Thumbnail, image.EncodeToPNG());
            } finally {
                spear.localPosition = oldPosition;
                signal.SetPropertyBlock(previousSignal);
                camera.targetTexture = null; RenderTexture.active = previous;
                Object.DestroyImmediate(image); Object.DestroyImmediate(target); Object.DestroyImmediate(camera.gameObject);
            }
            AssetDatabase.ImportAsset(Thumbnail, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(Thumbnail);
            importer.textureType = TextureImporterType.Default; importer.mipmapEnabled = false;
            importer.maxTextureSize = 1024; importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        static Transform Node(Transform parent, string name, Vector3 local)
        {
            var result = new GameObject(name).transform;
            result.SetParent(parent, false); result.localPosition = local;
            return result;
        }

        static Transform Box(Transform parent, string name, Vector3 local, Vector3 size, Material material)
        {
            var result = GameObject.CreatePrimitive(PrimitiveType.Cube);
            result.name = name; result.transform.SetParent(parent, false); result.transform.localPosition = local; result.transform.localScale = size;
            result.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(result.GetComponent<Collider>());
            return result.transform;
        }

        static Transform MeshObject(Transform parent, string name, Vector3 local, Mesh mesh, Material material)
        {
            var result = Node(parent, name, local);
            result.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            result.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            return result;
        }

        static Transform Prop(string key, Transform parent, Vector3 center, Vector3 size, float yaw = 0)
        {
            var result = EnvironmentArtBuilder.Place(key, parent, center, size, Quaternion.Euler(0, yaw, 0));
            foreach (var collider in result.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            return result;
        }

        static void Label(Transform parent, string text, Vector3 position, float size, Color color, Material material)
        {
            var label = Node(parent, text, position).gameObject.AddComponent<TextMesh>();
            label.text = text; label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center;
            label.fontSize = 100; label.characterSize = size * .1f; label.color = color;
            label.GetComponent<Renderer>().sharedMaterial = material;
        }

        sealed class Palette
        {
            public readonly Material bronze, steel, dark, ivory, signal, safe, text, checkpoint;
            public Palette()
            {
                Directory.CreateDirectory(Art + "/Materials"); Directory.CreateDirectory(Art + "/Meshes"); Directory.CreateDirectory(Art + "/Textures");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var texture = MetalTexture();
                bronze = Material("WeatheredBronze", new Color(.64f, .39f, .15f), .72f, texture);
                steel = Material("ForgedSteel", new Color(.64f, .69f, .68f), .82f, texture);
                dark = Material("BlackenedIron", new Color(.11f, .125f, .12f), .48f, texture);
                ivory = Material("Ivory", new Color(.96f, .86f, .64f), .12f, texture);
                signal = Material("PhaseSignal", new Color(.18f, .85f, .42f), .12f, null);
                signal.EnableKeyword("_EMISSION"); signal.SetColor("_EmissionColor", new Color(.08f, .5f, .16f));
                safe = Material("SafeLanding", new Color(.3f, .95f, .7f), .1f, null);
                text = AssetDatabase.LoadAssetAtPath<Material>(MovementProjectBuilder.Root + "/Materials/CourseText.mat");
                checkpoint = AssetDatabase.LoadAssetAtPath<Material>(MovementProjectBuilder.Root + "/Materials/Checkpoint.mat");
                if (!text || !checkpoint) throw new FileNotFoundException("The original course text/checkpoint materials are required.");
            }

            static Material Material(string name, Color color, float metallic, Texture texture)
            {
                string path = Art + "/Materials/" + name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
                material.color = color; material.mainTexture = texture;
                material.SetFloat("_Metallic", metallic); material.SetFloat("_Glossiness", .4f);
                EditorUtility.SetDirty(material);
                return material;
            }

            static Texture2D MetalTexture()
            {
                string path = Art + "/Textures/HammeredMetal.png";
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture) return texture;
                var image = new Texture2D(128, 128, TextureFormat.RGB24, false);
                var pixels = new Color[128 * 128];
                for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) {
                    float grain = Mathf.PerlinNoise(x * .37f, y * .37f);
                    float tarnish = Mathf.PerlinNoise(x * .045f, y * .045f);
                    float value = .64f + grain * .24f + tarnish * .12f;
                    pixels[y * 128 + x] = new Color(value, value, value);
                }
                image.SetPixels(pixels); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); Object.DestroyImmediate(image);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.wrapMode = TextureWrapMode.Repeat; importer.mipmapEnabled = true; importer.anisoLevel = 4;
                importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
        }

        sealed class Shapes
        {
            public readonly Mesh spear, arrow, gear, grate, muzzle, signals;
            public Shapes()
            {
                var data = new MeshData();
                data.Frustum(0, 1.3f, .045f, .045f, 8);
                data.Frustum(1.1f, 1.3f, .045f, .12f, 4);
                data.Frustum(1.3f, 1.7f, .12f, 0, 4);
                spear = data.Save("Spear");
                data = new MeshData();
                data.Frustum(-.8f, .16f, .025f, .025f, 8);
                data.Frustum(0, .45f, .11f, 0, 4);
                for (int side = 0; side < 4; side++) {
                    var rotation = Quaternion.Euler(0, side * 90, 0);
                    var a = rotation * new Vector3(0, -.77f, .025f);
                    var b = rotation * new Vector3(0, -.77f, .105f);
                    var c = rotation * new Vector3(0, -.43f, .025f);
                    data.Triangle(a, b, c); data.Triangle(c, b, a);
                }
                data.Rotate(Quaternion.Euler(0, 0, -90));
                arrow = data.Save("Arrow");
                data = new MeshData(); data.Ring(.34f, 1, .2f, 64, true, Vector3.zero);
                gear = data.Save("Gear");
                data = new MeshData();
                for (int row = -1; row <= 1; row++) for (int column = 0; column < 10; column++)
                    data.Ring(.14f, .24f, .022f, 12, false, new Vector3(-3.6f + column * .8f, row * .65f, 0));
                data.Rotate(Quaternion.Euler(90, 0, 0));
                grate = data.Save("SpearGrate");
                data = new MeshData(); data.Ring(.12f, .21f, .23f, 12, false, Vector3.zero);
                data.Rotate(Quaternion.Euler(90, 0, 0));
                muzzle = data.Save("ArrowMuzzle");
                data = new MeshData();
                foreach (int side in new[] { -1, 1 }) data.Ring(0, .18f, .045f, 20, false, new Vector3(side * 3.63f, 0, 0));
                signals = data.Save("PairedSignals");
            }
        }

        // Flat-shaded baked meshes are shared by every trap; no mesh construction occurs in play mode.
        sealed class MeshData
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> triangles = new List<int>();
            public void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                uv.Add(new Vector2(a.x + a.z, a.y)); uv.Add(new Vector2(b.x + b.z, b.y)); uv.Add(new Vector2(c.x + c.z, c.y));
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            }
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Triangle(a, b, c); Triangle(a, c, d); }
            public void Frustum(float bottom, float top, float radiusBottom, float radiusTop, int sides)
            {
                for (int i = 0; i < sides; i++) {
                    float a = i * Mathf.PI * 2 / sides, b = (i + 1) * Mathf.PI * 2 / sides;
                    var p = new Vector3(Mathf.Cos(a) * radiusBottom, bottom, Mathf.Sin(a) * radiusBottom);
                    var q = new Vector3(Mathf.Cos(b) * radiusBottom, bottom, Mathf.Sin(b) * radiusBottom);
                    var r = new Vector3(Mathf.Cos(b) * radiusTop, top, Mathf.Sin(b) * radiusTop);
                    var s = new Vector3(Mathf.Cos(a) * radiusTop, top, Mathf.Sin(a) * radiusTop);
                    Quad(p, s, r, q);
                    if (radiusBottom > 0) Triangle(new Vector3(0, bottom, 0), p, q);
                    if (radiusTop > 0) Triangle(new Vector3(0, top, 0), r, s);
                }
            }
            public void Ring(float inner, float outer, float depth, int segments, bool teeth, Vector3 center)
            {
                for (int i = 0; i < segments; i++) {
                    float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
                    float ra = teeth && i % 4 >= 2 ? outer * .84f : outer;
                    float rb = teeth && (i + 1) % 4 >= 2 ? outer * .84f : outer;
                    var ia = center + new Vector3(Mathf.Cos(a) * inner, Mathf.Sin(a) * inner, -depth * .5f);
                    var ib = center + new Vector3(Mathf.Cos(b) * inner, Mathf.Sin(b) * inner, -depth * .5f);
                    var oa = center + new Vector3(Mathf.Cos(a) * ra, Mathf.Sin(a) * ra, -depth * .5f);
                    var ob = center + new Vector3(Mathf.Cos(b) * rb, Mathf.Sin(b) * rb, -depth * .5f);
                    var back = Vector3.forward * depth;
                    Quad(ia, ib, ob, oa); Quad(ia + back, oa + back, ob + back, ib + back);
                    Quad(oa, ob, ob + back, oa + back);
                    if (inner > 0) Quad(ib, ia, ia + back, ib + back);
                }
            }
            public void Rotate(Quaternion rotation)
            {
                for (int i = 0; i < vertices.Count; i++) vertices[i] = rotation * vertices[i];
            }
            public Mesh Save(string name)
            {
                string path = Art + "/Meshes/" + name + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (!mesh) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
                mesh.Clear(); mesh.name = name;
                mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
                return mesh;
            }
        }
    }
}
