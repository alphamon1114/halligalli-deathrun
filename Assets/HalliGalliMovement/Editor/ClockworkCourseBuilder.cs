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
        public const float FinishZ = 80, CourseLength = 160;
        const float Width = 8;
        const int FerryBoardWaypoint = 21, FerryExitWaypoint = 22;
        const string Art = MovementProjectBuilder.Root + "/Art/Clockwork";
        const string Thumbnail = MovementProjectBuilder.Root + "/Resources/UI/Maps/Clockwork.png";
        static readonly Vector3[] LegOrigins = { Vector3.zero, new Vector3(0, 0, 40), new Vector3(40, 0, 40), new Vector3(40, 0, 80) };
        static readonly float[] LegYaw = { 0, 90, 0, -90 };
        static readonly int[] TrapLeg = { 0, 1, 2, 3, 3, 3 };
        static readonly float[] TrapDistance = { 28, 30, 34, 10, 20, 29 };
        static readonly int[] CheckpointWaypoint = { 9, 18, 25 };
        static readonly Vector3[] RoutePoints = {
            RoutePoint(0, 2.3f), RoutePoint(0, 5.7f), RoutePoint(0, 10.8f),
            RoutePoint(0, 15.2f), RoutePoint(0, 18.3f), RoutePoint(0, 22.7f),
            RoutePoint(0, 25), RoutePoint(0, 31), RoutePoint(0, 35.5f), RoutePoint(1, 0),
            RoutePoint(1, 6), RoutePoint(1, 9), RoutePoint(1, 15), RoutePoint(1, 17),
            RoutePoint(1, 23), RoutePoint(1, 27), RoutePoint(1, 33), RoutePoint(1, 35.5f), RoutePoint(2, 0),
            RoutePoint(2, 6), RoutePoint(2, 8), RoutePoint(2, 12), RoutePoint(2, 28),
            RoutePoint(2, 31), RoutePoint(2, 37), RoutePoint(3, 0),
            RoutePoint(3, 6), RoutePoint(3, 7), RoutePoint(3, 13), RoutePoint(3, 17),
            RoutePoint(3, 23), RoutePoint(3, 26), RoutePoint(3, 32),
            RoutePoint(3, 33.8f), RoutePoint(3, 38), RoutePoint(3, 39.2f)
        };

        static Vector3 RoutePoint(int leg, float distance)
            => LegOrigins[leg] + Quaternion.Euler(0, LegYaw[leg], 0) * new Vector3(0, .025f, distance);

        [MenuItem("HalliGalli/Maps/Build Clockwork Fortress Only")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before building Clockwork Fortress.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var profile = AssetDatabase.LoadAssetAtPath<SourceMovementProfile>(MovementProjectBuilder.Root + "/Settings/DefaultMovement.asset");
            if (!profile || !CourseMapsBuilder.Catalog) throw new InvalidOperationException("The saved starter movement profile and map catalog are required.");
            foreach (string key in new[] { "ConcreteFloor", "ConcreteWall", "ConcreteColumn", "UtilityPipes", "CeilingLight" })
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
            var legs = new Transform[LegOrigins.Length];
            for (int i = 0; i < legs.Length; i++) {
                legs[i] = Node(root, "Leg " + (char)('A' + i) + " / 40 metres", LegOrigins[i]);
                legs[i].localRotation = Quaternion.Euler(0, LegYaw[i], 0);
            }
            BuildFloor(root, legs, session, palette, out var boardingDock, out var arrivalDock);
            BuildFortress(legs, palette, shapes);
            course.traps = new ClockworkTrap[TrapDistance.Length];
            for (int i = 0; i < TrapDistance.Length; i++)
                course.traps[i] = BuildTrap(legs[TrapLeg[i]], session, i, palette, shapes);
            course.presses = new[] {
                BuildPress(legs[1], session, 12, 0, palette, shapes),
                BuildPress(legs[1], session, 20, .5f, palette, shapes)
            };
            var ferry = BuildFerry(legs[2], boardingDock, arrivalDock, palette, shapes);
            course.ferries = new[] { ferry };
            course.movingPlatforms = new[] { ferry.carrier };
            BuildRoutes(session.roster, ferry);
            PlaceBell(session, legs[3], palette, shapes);
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
            Debug.Log("CLOCKWORK_CREATED " + ScenePath + " length=" + CourseLength + " legs=4 traps=6 presses=2 ferries=1 jumpGaps=3 checkpoints=3 seats=6 waypoints=36");
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

        static void BuildFloor(Transform root, Transform[] legs, CourseSession session, Palette p,
            out BoxCollider boardingDock, out BoxCollider arrivalDock)
        {
            // Every slab ends at its visible edge. Gaps have neither hidden floor nor decorative bridge.
            Deck(legs[0], "Start apron", 0, 8, 10, p);
            Deck(legs[0], "First jump approach", 8, 12, Width, p);
            Deck(legs[0], "Double jump middle island", 13.5f, 19.5f, Width, p);
            Deck(legs[0], "Spear approach and clearance", 21, 35, Width, p);
            Deck(legs[1], "First press waiting pad", 5, 10, Width, p);
            Deck(legs[1], "First press threshold", 10, 14, Width, p);
            Deck(legs[1], "Press clearance and second waiting pad", 14, 18, Width, p);
            Deck(legs[1], "Second press threshold", 18, 22, Width, p);
            Deck(legs[1], "Arrow approach and clearance", 22, 35, Width, p);
            boardingDock = Deck(legs[2], "Ferry boarding dock", 5, 10, Width, p);
            arrivalDock = Deck(legs[2], "Ferry arrival dock and arrow hall", 26, 35, Width, p);
            Deck(legs[3], "Final mixed trap run", 5, 35, Width, p);
            Deck(legs[3], "Final jump landing and bell deck", 36.5f, 44, Width, p);
            session.checkpoints.session = session;
            session.checkpoints.points = new CourseCheckpoints.Point[3];
            for (int i = 0; i < 3; i++) {
                var corner = Node(root, "Safe turn plaza " + (i + 1), LegOrigins[i + 1]);
                corner.localRotation = Quaternion.Euler(0, LegYaw[i + 1], 0);
                var landing = Deck(corner, "Checkpoint " + (i + 1) + " safe deck", -5, 5, 10, p);
                session.checkpoints.points[i] = Checkpoint(session.checkpoints.transform, corner, landing,
                    CheckpointWaypoint[i], new[] { "I", "II", "III" }[i], i == 0, p);
            }
            JumpLip(legs[0], 12, "JUMP / DOUBLE GAP", p);
            JumpLip(legs[0], 19.5f, "JUMP / SECOND GAP", p);
            JumpLip(legs[3], 35, "JUMP / THE BELL", p);
        }

        static BoxCollider Deck(Transform parent, string name, float from, float to, float width, Palette p)
        {
            var floor = Node(parent, name, new Vector3(0, -.3f, (from + to) * .5f));
            var collider = floor.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(width, .6f, to - from);
            for (float edge = from; edge < to; edge += 4) {
                float length = Mathf.Min(4, to - edge);
                Prop("ConcreteFloor", floor, new Vector3(0, .15f, edge + length * .5f - (from + to) * .5f), new Vector3(width, .3f, length));
            }
            foreach (int side in new[] { -1, 1 })
                Box(floor, "Bronze deck edge", new Vector3(side * (width * .5f - .06f), .3f, 0), new Vector3(.12f, .025f, to - from), p.bronze);
            return collider;
        }

        static CourseCheckpoints.Point Checkpoint(Transform parent, Transform corner, BoxCollider floor,
            int resume, string title, bool turnRight, Palette p)
        {
            var spawn = Node(parent, "Clockwork checkpoint " + title, Vector3.zero);
            spawn.SetPositionAndRotation(corner.TransformPoint(Vector3.up * .025f), corner.rotation);
            var outline = Node(corner, "Checkpoint " + title + " outline", Vector3.zero).gameObject.AddComponent<LineRenderer>();
            outline.useWorldSpace = false; outline.loop = true; outline.positionCount = 4; outline.widthMultiplier = .045f;
            outline.sharedMaterial = p.checkpoint;
            outline.SetPositions(new[] { new Vector3(-4.8f, .025f, -4.8f), new Vector3(4.8f, .025f, -4.8f),
                new Vector3(4.8f, .025f, 4.8f), new Vector3(-4.8f, .025f, 4.8f) });
            // The outside of the turn holds signage; both incoming and outgoing centerlines remain open.
            float side = turnRight ? -1 : 1;
            Prop("ConcreteColumn", corner, new Vector3(side * 4.3f, .8f, 0), new Vector3(.7f, 1.6f, .7f));
            Box(corner, "Checkpoint plaque", new Vector3(side * 4.3f, 1.9f, 0), new Vector3(.85f, 1, .12f), p.dark);
            Label(corner, title, new Vector3(side * 4.3f, 2.15f, -.08f), .62f, p.safe.color, p.text);
            Label(corner, "SAVED", new Vector3(side * 4.3f, 1.79f, -.08f), .15f, p.safe.color, p.text);
            Label(corner, turnRight ? "TURN RIGHT" : "TURN LEFT", new Vector3(side * 4.3f, 1.59f, -.08f), .13f, p.ivory.color, p.text);
            RouteArrow(corner, 1.2f, p.safe);
            RouteArrow(corner, 3.5f, p.safe);
            return new CourseCheckpoints.Point { spawn = spawn, landing = floor, resumeWaypoint = resume };
        }

        static void RouteArrow(Transform parent, float distance, Material material)
        {
            var root = Node(parent, "Forward route arrow", new Vector3(0, .035f, distance));
            Box(root, "Arrow stem", new Vector3(0, 0, -.4f), new Vector3(.1f, .015f, .8f), material);
            foreach (int side in new[] { -1, 1 }) {
                var stroke = Box(root, "Arrow tip", new Vector3(side * .2f, 0, -.14f), new Vector3(.1f, .015f, .58f), material);
                stroke.localRotation = Quaternion.Euler(0, -side * 45, 0);
            }
        }

        static void JumpLip(Transform parent, float distance, string title, Palette p)
        {
            for (int stripe = -4; stripe <= 4; stripe++)
                Box(parent, "Jump takeoff stripe", new Vector3(stripe * .75f, .025f, distance - .25f), new Vector3(.42f, .025f, .16f), p.ivory);
            RouteArrow(parent, distance - 1.5f, p.ivory);
            Box(parent, "Jump instruction plaque", new Vector3(-3.7f, 1.15f, distance - 1.1f), new Vector3(.6f, .6f, .08f), p.dark);
            Label(parent, title, new Vector3(-3.7f, 1.17f, distance - 1.16f), .1f, p.ivory.color, p.text);
        }

        static void BuildFortress(Transform[] legs, Palette p, Shapes shapes)
        {
            float[][] stations = {
                new[] { 9.5f, 24, 32 }, new[] { 7f, 25, 33 },
                new[] { 7f, 29 }, new[] { 6.5f, 15, 24, 32, 40 }
            };
            for (int leg = 0; leg < legs.Length; leg++) {
                var root = Node(legs[leg], "Open fortress machine housings", Vector3.zero);
                foreach (float distance in stations[leg]) foreach (int side in new[] { -1, 1 }) {
                    Prop("ConcreteWall", root, new Vector3(side * 4.55f, .8f, distance), new Vector3(2.6f, 1.6f, .6f), 90);
                    Prop("ConcreteColumn", root, new Vector3(side * 4.7f, 1.7f, distance), new Vector3(.8f, 3.4f, .9f));
                    Prop("ConcreteColumn", root, new Vector3(side * 4.7f, 3.5f, distance), new Vector3(1.05f, .25f, 1.15f));
                    var gear = MeshObject(root, "Bronze wall gear", new Vector3(side * 4.24f, 2.45f, distance), shapes.gear, p.bronze);
                    gear.localRotation = Quaternion.Euler(0, side * 90, 0); gear.localScale = Vector3.one * .58f;
                    Prop("CeilingLight", root, new Vector3(side * 4.18f, 1.25f, distance), new Vector3(.3f, .55f, .35f), side * 90);
                }
            }
            Gate(legs[0], 8, "01 / LEAP & SPEARS", "TWO JUMPS / WAIT FOR GREEN / TURN RIGHT", p, shapes, ClockworkTrapKind.Spears);
            Gate(legs[1], 6, "02 / HYDRAULIC HALL", "TWO PRESSES / ARROWS / TURN LEFT", p, shapes, ClockworkTrapKind.Arrows);
            Gate(legs[2], 6, "03 / CLOCKWORK FERRY", "WAIT / BOARD / RIDE / TURN LEFT", p, shapes, ClockworkTrapKind.Arrows);
            Gate(legs[3], 6, "04 / THE BELL", "MIXED TRAPS / FINAL JUMP / 160 METRES", p, shapes, ClockworkTrapKind.Spears);
            // Short, non-colliding housings leave the turns, every jump lip and both ferry docks visibly open.
            // No ceiling is built over the original six-seat table/card camera.
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
            bool spears = index == 0 || index == 3 || index == 5;
            var root = Node(parent, (spears ? "Spears " : "Arrows ") + (index + 1), new Vector3(0, 0, TrapDistance[index]));
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

        static FactoryPress BuildPress(Transform parent, CourseSession session, float distance, float phase, Palette p, Shapes shapes)
        {
            var root = Node(parent, "Hydraulic press / " + distance + " metres", new Vector3(0, 0, distance));
            var press = root.gameObject.AddComponent<FactoryPress>();
            press.contactSession = session; press.orientedContact = true; press.period = 4.8f; press.phase = phase;
            foreach (int side in new[] { -1, 1 }) {
                Box(root, "Press upright", new Vector3(side * 4.55f, 2.65f, 0), new Vector3(.45f, 5.3f, .75f), p.steel);
                Prop("UtilityPipes", root, new Vector3(side * 4.65f, 2.6f, .45f), new Vector3(.4f, 4, .45f));
                var gear = MeshObject(root, "Press drive gear", new Vector3(side * 4.55f, 3.2f, -.45f), shapes.gear, p.bronze);
                gear.localScale = Vector3.one * .5f;
            }
            Box(root, "Press crossbeam", new Vector3(0, 5.2f, 0), new Vector3(9.5f, .5f, 1.5f), p.steel);
            press.head = Node(root, "Press head", Vector3.up * 3.8f);
            press.raised = press.head.position; press.stroke = 3.25f;
            press.headCollider = press.head.gameObject.AddComponent<BoxCollider>();
            press.headCollider.size = new Vector3(Width, .8f, 1.4f);
            Box(press.head, "Blackened press plate", Vector3.zero, press.headCollider.size, p.dark);
            Box(press.head, "Press hazard band", new Vector3(0, -.2f, -.72f), new Vector3(Width, .15f, .025f), p.bronze);
            Box(press.head, "Piston", Vector3.up * 1.05f, new Vector3(.45f, 1.4f, .45f), p.steel);
            press.warning = MeshObject(root, "Paired press signals", new Vector3(0, 2.7f, -1), shapes.signals, p.signal).GetComponent<Renderer>();
            for (int stripe = -5; stripe <= 5; stripe++)
                Box(root, "Press hazard threshold", new Vector3(stripe * .65f, .025f, -.95f), new Vector3(.32f, .02f, .14f), p.bronze);
            foreach (float pad in new[] { -2.5f, 2.5f }) {
                Box(root, "Press safe pad marker", new Vector3(-3.65f, .025f, pad), new Vector3(.2f, .025f, 1.1f), p.safe);
                Box(root, "Press safe pad marker", new Vector3(3.65f, .025f, pad), new Vector3(.2f, .025f, 1.1f), p.safe);
            }
            Box(root, "Press wait plaque", new Vector3(-3.7f, 1.35f, -3), new Vector3(.6f, .7f, .08f), p.dark);
            Label(root, "WAIT\nFOR GREEN", new Vector3(-3.7f, 1.37f, -3.06f), .12f, p.ivory.color, p.text);
            press.Apply(0);
            return press;
        }

        static ClockworkCourse.Ferry BuildFerry(Transform parent, BoxCollider boardingDock, BoxCollider arrivalDock, Palette p, Shapes shapes)
        {
            var root = Node(parent, "Mandatory clockwork ferry", new Vector3(0, -.3f, 12));
            var collider = root.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(5, .6f, 4);
            var carrier = root.gameObject.AddComponent<FactorySurface>();
            carrier.origin = parent.TransformPoint(new Vector3(0, -.3f, 18));
            carrier.travel = parent.TransformVector(Vector3.forward * 6);
            carrier.period = 10; carrier.phase = -.25f; carrier.standingOffset = .325f;
            carrier.transform.position = carrier.PositionAt(0);
            Prop("ConcreteFloor", root, new Vector3(0, .15f, 0), new Vector3(5, .3f, 4));
            Box(root, "Ferry hull", new Vector3(0, -.1f, 0), new Vector3(4.8f, .3f, 3.8f), p.dark);
            foreach (int side in new[] { -1, 1 }) {
                Box(root, "Ferry side band", new Vector3(side * 2.44f, .28f, 0), new Vector3(.12f, .05f, 4), p.bronze);
                var gear = MeshObject(root, "Ferry drive gear", new Vector3(side * 2.52f, -.12f, 0), shapes.gear, p.bronze);
                gear.localRotation = Quaternion.Euler(0, 90, 0); gear.localScale = Vector3.one * .38f;
                foreach (float edge in new[] { 9.65f, 26.35f })
                    Box(parent, "Dock berth marker", new Vector3(side * 2.7f, .12f, edge), new Vector3(.18f, .24f, .5f), p.bronze);
            }
            var symbols = Node(root, "Moving ferry deck arrows", Vector3.up * .3f);
            RouteArrow(symbols, .5f, p.safe);
            var wait = Node(parent, "Ferry safe waiting point", new Vector3(0, .025f, 8));
            RouteArrow(parent, 8, p.safe);
            Box(parent, "Ferry wait plaque", new Vector3(-3.55f, 1.3f, 8), new Vector3(.85f, .85f, .12f), p.dark);
            Label(parent, "WAIT\nBOARD & RIDE", new Vector3(-3.55f, 1.32f, 7.92f), .12f, p.ivory.color, p.text);
            return new ClockworkCourse.Ferry {
                carrier = carrier, boardingDock = boardingDock, arrivalDock = arrivalDock, waitingPoint = wait,
                boardWaypoint = FerryBoardWaypoint, exitWaypoint = FerryExitWaypoint
            };
        }

        static void BuildRoutes(CourseRoster roster, ClockworkCourse.Ferry ferry)
        {
            var movingMarkers = new Transform[roster.routeRoots.Length];
            for (int seat = 0; seat < roster.routeRoots.Length; seat++) {
                var route = roster.routeRoots[seat];
                for (int i = 0; i < RoutePoints.Length; i++) {
                    var position = RoutePoints[i];
                    position.x += i == 0 ? (seat - 2.5f) * CourseRoster.SeatSpacing : i == 1 ? (seat - 2.5f) * .5f : 0;
                    var marker = Node(route, "Clockwork waypoint " + i, Vector3.zero);
                    marker.position = position;
                    marker.gameObject.AddComponent<ClimbStep>().arrivalRadius = .45f;
                    if (i == FerryBoardWaypoint) {
                        marker.position = ferry.carrier.transform.position + Vector3.up * ferry.carrier.standingOffset;
                        movingMarkers[seat] = marker;
                    }
                    int leg = i < CheckpointWaypoint[0] ? 0 : i < CheckpointWaypoint[1] ? 1 : i < CheckpointWaypoint[2] ? 2 : 3;
                    marker.rotation = Quaternion.Euler(0, LegYaw[leg], 0);
                }
                var bot = roster.allParticipants[seat].GetComponent<CourseRival>();
                if (bot) bot.route = route.Cast<Transform>().ToArray();
            }
            ferry.carrier.routeMarkers = movingMarkers;
        }

        static void PlaceBell(CourseSession session, Transform root, Palette p, Shapes shapes)
        {
            var roots = session.gameObject.scene.GetRootGameObjects();
            var rotation = Quaternion.Euler(0, -90, 0);
            roots.Single(item => item.name == "Bell plinth").transform.SetPositionAndRotation(new Vector3(0, .35f, FinishZ), rotation);
            session.bellTarget.SetPositionAndRotation(new Vector3(0, .7f, FinishZ), rotation);
            var label = roots.FirstOrDefault(item => item.name == "RING");
            if (label) label.transform.SetPositionAndRotation(new Vector3(.34f, .4f, FinishZ), rotation);
            foreach (int side in new[] { -1, 1 }) {
                Prop("ConcreteColumn", root, new Vector3(side * 3.1f, .8f, 41), new Vector3(.9f, 1.6f, .9f));
                var gear = MeshObject(root, "Bell podium medallion", new Vector3(side * 3.1f, 1, 40.5f), shapes.gear, p.bronze);
                gear.localScale = Vector3.one * .36f;
            }
        }

        static CourseMapDefinition Register(SourceMovementProfile profile)
        {
            string path = CourseMapsBuilder.Maps + "/" + Id + ".asset";
            var map = AssetDatabase.LoadAssetAtPath<CourseMapDefinition>(path);
            if (!map) { map = ScriptableObject.CreateInstance<CourseMapDefinition>(); AssetDatabase.CreateAsset(map, path); }
            map.id = Id; map.displayName = "태엽 요새"; map.style = CourseMapStyle.Factory; map.difficulty = 2;
            map.description = "160m 태엽 요새. 점프 틈과 창·화살, 두 프레스를 지나 좌우로 꺾이는 길을 달리세요. 왕복 발판을 기다려 타고 마지막 점프 너머 종을 울리세요. 세 모서리의 안전 지점에서 재출발합니다.";
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
            bool previousFog = RenderSettings.fog;
            var ferry = course.ferries[0].carrier;
            var previousFerryPosition = ferry.transform.position;
            var press = course.presses[1];
            var previousHeadPosition = press.head.position;
            var spear = course.traps[0].movingRoot;
            var oldPosition = spear.localPosition;
            var signal = course.traps[0].warning;
            var previousSignal = new MaterialPropertyBlock();
            signal.GetPropertyBlock(previousSignal);
            try {
                RenderSettings.fog = false;
                ferry.transform.position = ferry.origin;
                press.head.position = press.raised - Vector3.up * press.stroke * .65f;
                spear.localPosition = Vector3.zero;
                var activeSignal = new MaterialPropertyBlock();
                activeSignal.SetColor("_Color", new Color(1, .08f, .025f));
                activeSignal.SetColor("_EmissionColor", new Color(1, .08f, .025f) * 1.7f);
                signal.SetPropertyBlock(activeSignal);
                camera.enabled = false;
                camera.cullingMask = ~((1 << CharacterMotionDriver.HandsLayer) | (1 << 8));
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = RenderSettings.fogColor;
                camera.orthographic = true; camera.orthographicSize = 50; camera.farClipPlane = 240;
                camera.transform.position = new Vector3(-48, 95, 2);
                camera.transform.LookAt(new Vector3(20, 0, 40));
                camera.targetTexture = target; camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                var png = image.EncodeToPNG();
                File.WriteAllBytes(Thumbnail, png);
                Directory.CreateDirectory("Previews");
                File.WriteAllBytes("Previews/ClockworkMixedOverview.png", png);
            } finally {
                RenderSettings.fog = previousFog;
                ferry.transform.position = previousFerryPosition;
                press.head.position = previousHeadPosition;
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
