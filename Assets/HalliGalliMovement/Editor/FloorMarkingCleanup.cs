using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace HalliGalli.EditorTools
{
    public static class FloorMarkingCleanup
    {
        const string ReportPath = "TestResults/floor-marking-cleanup.json";
        const string Materials = MovementProjectBuilder.Root + "/Materials/";
        static readonly string[] MarkingNames = {
            "Edge stripe", "Front stripe", "Back stripe", "Left stripe", "Right stripe", "Rock lip", "Amber edge"
        };
        static readonly Vector3[] RockLipPoints = {
            new Vector3(-.46f, .515f, -.46f), new Vector3(.46f, .515f, -.46f),
            new Vector3(.46f, .515f, .46f), new Vector3(-.46f, .515f, .46f)
        };

        [Serializable] sealed class Report
        {
            public string utc, error;
            public bool success;
            public List<SceneReport> scenes = new List<SceneReport>();
        }
        [Serializable] sealed class SceneReport
        {
            public string scenePath;
            public string[] mapIds;
            public bool enabledInBuild, containsFerry, changed, saved, invariantsPreserved;
            public Counts before, after;
            public List<Marking> removed = new List<Marking>();
            public List<Marking> retainedCP = new List<Marking>();
        }
        [Serializable] sealed class Marking
        {
            public string name, hierarchy, objectId, checkpointSurface;
            public Vector3 position;
        }
        [Serializable] sealed class Counts
        {
            public int transforms, colliders, enabledColliders, routeRoots, routeAnchors, surfSections;
            public int checkpointPoints, checkpointOutlines, checkpointRenderers, floorMarkings, lineRenderers;
        }
        struct Pose
        {
            public Transform parent;
            public Vector3 localPosition, localScale, position, lossyScale;
            public Quaternion localRotation, rotation;
            public bool active;
            public int layer;
            public string name, tag;
            public static Pose Capture(Transform t) => new Pose {
                parent = t.parent, localPosition = t.localPosition, localScale = t.localScale,
                localRotation = t.localRotation, position = t.position, rotation = t.rotation,
                lossyScale = t.lossyScale, active = t.gameObject.activeSelf,
                layer = t.gameObject.layer, name = t.name, tag = t.tag
            };
        }

        [MenuItem("HalliGalli/Maps/Remove Saved Floor Perimeter Markings")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Floor marking cleanup must run outside Play mode.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save your open scenes before floor marking cleanup.");

            var catalog = CourseMapsBuilder.Catalog;
            if (!catalog || catalog.maps == null) throw new InvalidDataException("Course map catalog is missing.");
            var maps = catalog.maps.Where(m => m && m.playable).ToArray();
            var enabled = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            // The user-map scene is rebuilt from the cleaned first-steps scene by its own builder.
            var paths = maps.Select(m => m.scenePath)
                .Concat(enabled.Where(p => p.StartsWith(MovementProjectBuilder.Root + "/Demo/", StringComparison.Ordinal)))
                .Where(p => p != UserMapRuntime.ScenePath).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
            foreach (var path in paths)
                if (string.IsNullOrEmpty(path) || !AssetDatabase.LoadAssetAtPath<SceneAsset>(path))
                    throw new InvalidDataException("Playable/enabled scene is missing: " + path);

            var setup = EditorSceneManager.GetSceneManagerSetup();
            var report = new Report { utc = DateTime.UtcNow.ToString("O") };
            try {
                foreach (var path in paths) {
                    var item = new SceneReport {
                        scenePath = path, mapIds = maps.Where(m => m.scenePath == path).Select(m => m.id).ToArray(),
                        enabledInBuild = enabled.Contains(path)
                    };
                    report.scenes.Add(item);
                    CleanScene(item);
                }
                report.success = true;
            } catch (Exception exception) {
                report.error = exception.ToString();
                throw;
            } finally {
                Directory.CreateDirectory("TestResults");
                File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
                if (setup.Any(s => s.isLoaded && s.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            Debug.Log("FLOOR_MARKING_CLEANUP_SUCCEEDED " + report.scenes.Sum(s => s.removed.Count) + " removed; " + ReportPath);
        }

        static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

        static void CleanScene(SceneReport report)
        {
            var scene = EditorSceneManager.OpenScene(report.scenePath, OpenSceneMode.Single);
            try {
                var transforms = All<Transform>(scene);
                var checkpoints = All<CourseCheckpoints>(scene);
                var boxes = All<BoxCollider>(scene);
                var removal = new HashSet<Transform>();
                var checkpointVisuals = new HashSet<Transform>(checkpoints.SelectMany(cp => cp.GetComponentsInChildren<Renderer>(true)).Select(r => r.transform));
                report.before = Count(scene);
                report.containsFerry = All<TutorialFerryCourse>(scene).Length > 0;
                foreach (var t in transforms.Where(t => MarkingNames.Contains(t.name))) {
                    // Checkpoint-owned renderers, including any shared perimeter, are never deleted.
                    if (t.GetComponentInParent<CourseCheckpoints>(true)) { checkpointVisuals.Add(t); continue; }
                    if (!VerifiedMarking(t, boxes))
                        throw new InvalidDataException("Unrecognized floor marking; refusing to remove " + Hierarchy(t));
                    var checkpoint = CheckpointAt(t, boxes, checkpoints);
                    // A generic border may be the only visual on an older checkpoint. Preserve that case.
                    if (checkpoint && !checkpoint.GetComponentsInChildren<LineRenderer>(true).Any(l => l.name == "Checkpoint outline")) {
                        checkpointVisuals.Add(t);
                        continue;
                    }
                    removal.Add(t);
                    report.removed.Add(Describe(t, CheckpointSurface(t, boxes, checkpoints)));
                }
                foreach (var t in checkpointVisuals.OrderBy(t => Hierarchy(t), StringComparer.Ordinal))
                    report.retainedCP.Add(Describe(t, CheckpointSurface(t, boxes, checkpoints)));

                // Check every survivor, not just counts: collision, routes, model inlays, electricity,
                // lasso, rails, CP renderers and all gameplay component data must remain identical.
                var poses = transforms.Where(t => !removal.Contains(t)).ToDictionary(t => t, Pose.Capture);
                var components = All<Component>(scene).Where(c => c && !(c is Transform) && !removal.Contains(c.transform))
                    .ToDictionary(c => c, c => EditorJsonUtility.ToJson(c));
                foreach (var t in removal) Object.DestroyImmediate(t.gameObject);
                foreach (var entry in poses)
                    if (!entry.Key || !entry.Value.Equals(Pose.Capture(entry.Key)))
                        throw new InvalidOperationException("Cleanup changed a surviving transform or object.");
                foreach (var entry in components)
                    if (!entry.Key || entry.Value != EditorJsonUtility.ToJson(entry.Key))
                        throw new InvalidOperationException("Cleanup changed a surviving component: " + (entry.Key ? Hierarchy(entry.Key.transform) : "deleted"));

                report.after = Count(scene);
                RequireCounts(report);
                report.invariantsPreserved = true;
                report.changed = removal.Count != 0;
                if (report.changed) {
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save cleaned scene: " + scene.path);
                    report.saved = true;
                }
            } catch {
                // Do not persist a scene if any safety condition failed.
                EditorSceneManager.OpenScene(report.scenePath, OpenSceneMode.Single);
                throw;
            }
        }

        static Counts Count(Scene scene)
        {
            var colliders = All<Collider>(scene);
            var rosters = All<CourseRoster>(scene);
            var roots = rosters.SelectMany(r => r.routeRoots ?? Array.Empty<Transform>()).Where(t => t).Distinct().ToArray();
            var checkpoints = All<CourseCheckpoints>(scene);
            var transforms = All<Transform>(scene);
            return new Counts {
                transforms = transforms.Length, colliders = colliders.Length,
                enabledColliders = colliders.Count(c => c.enabled), routeRoots = roots.Length,
                routeAnchors = roots.Sum(r => r.childCount),
                surfSections = All<SurfCourse>(scene).Sum(s => s.sections == null ? 0 : s.sections.Length),
                checkpointPoints = checkpoints.Sum(cp => cp.points == null ? 0 : cp.points.Length),
                checkpointOutlines = All<LineRenderer>(scene).Count(l => l.name == "Checkpoint outline"),
                checkpointRenderers = checkpoints.Sum(cp => cp.GetComponentsInChildren<Renderer>(true).Length),
                floorMarkings = transforms.Count(t => MarkingNames.Contains(t.name)),
                lineRenderers = All<LineRenderer>(scene).Length
            };
        }

        static void RequireCounts(SceneReport report)
        {
            var a = report.before; var b = report.after;
            int removedLines = report.removed.Count(m => m.name == "Rock lip");
            if (a.colliders != b.colliders || a.enabledColliders != b.enabledColliders
                || a.routeRoots != b.routeRoots || a.routeAnchors != b.routeAnchors || a.surfSections != b.surfSections
                || a.checkpointPoints != b.checkpointPoints || a.checkpointOutlines != b.checkpointOutlines
                || a.checkpointRenderers != b.checkpointRenderers || a.transforms - report.removed.Count != b.transforms
                || a.floorMarkings - report.removed.Count != b.floorMarkings || a.lineRenderers - removedLines != b.lineRenderers)
                throw new InvalidOperationException("Floor marking cleanup changed protected scene counts: " + report.scenePath);
        }

        static bool VerifiedMarking(Transform t, BoxCollider[] boxes)
        {
            if (t.childCount != 0 || PrefabUtility.IsPartOfPrefabInstance(t) || !Near(t.localRotation, Quaternion.identity)) return false;
            var components = t.GetComponents<Component>();
            if (t.name == "Rock lip") {
                var line = t.GetComponent<LineRenderer>();
                return components.Length == 2 && line && t.parent && t.parent.name.StartsWith("Climb landing ", StringComparison.Ordinal)
                    && t.parent.parent && t.parent.parent.name == "Summit climbing course" && t.parent.GetComponent<BoxCollider>()
                    && Near(t.localPosition, Vector3.zero) && Near(t.localScale, Vector3.one)
                    && line.loop && !line.useWorldSpace && line.positionCount == 4 && Near(line.widthMultiplier, .025f)
                    && MaterialIs(line, "SummitTrail") && Enumerable.Range(0, 4).All(i => Near(line.GetPosition(i), RockLipPoints[i]));
            }
            var filter = t.GetComponent<MeshFilter>(); var renderer = t.GetComponent<MeshRenderer>();
            if (components.Length != 3 || !filter || !renderer || !filter.sharedMesh || filter.sharedMesh.name != "Cube") return false;
            if (t.name == "Edge stripe")
                return !t.parent && (MaterialIs(renderer, "CourseCyan") || MaterialIs(renderer, "CourseOrange"))
                    && EdgeSurface(t, boxes);
            if (t.name == "Amber edge") {
                var box = t.parent ? t.parent.GetComponent<BoxCollider>() : null;
                return box && t.parent.parent && t.parent.parent.name == "Factory machinery" && MaterialIs(renderer, "FactoryAmber")
                    && Near(t.localScale, new Vector3(.055f, .02f, box.size.z))
                    && (Near(t.localPosition, box.center + new Vector3(box.size.x / 2 - .05f, box.size.y / 2 + .012f, 0))
                        || Near(t.localPosition, box.center + new Vector3(-box.size.x / 2 + .05f, box.size.y / 2 + .012f, 0)));
            }
            if (!t.parent || !(t.parent.name.StartsWith("First platform ", StringComparison.Ordinal) || t.parent.name == "Wide merge platform")) return false;
            if (!Enumerable.Range(1, 6).Any(i => MaterialIs(renderer, "Seat" + i))) return false;
            var floor = t.parent.Find("Landing surface");
            var landing = floor ? floor.GetComponent<BoxCollider>() : null;
            if (!landing) return false;
            var size = Vector3.Scale(landing.size, floor.localScale);
            var top = floor.localPosition + Vector3.Scale(landing.center, floor.localScale) + Vector3.up * size.y / 2;
            switch (t.name) {
                case "Front stripe": return Strip(t, top, size, false, 1);
                case "Back stripe": return Strip(t, top, size, false, -1);
                case "Left stripe": return Strip(t, top, size, true, -1);
                case "Right stripe": return Strip(t, top, size, true, 1);
                default: return false;
            }
        }

        static bool Strip(Transform stripe, Vector3 top, Vector3 size, bool side, int sign)
        {
            var scale = side ? new Vector3(.1f, .012f, size.z) : new Vector3(size.x, .012f, .1f);
            var offset = side ? new Vector3(sign * (size.x / 2 - .06f), .006f, 0) : new Vector3(0, .006f, sign * (size.z / 2 - .06f));
            return Near(stripe.localScale, scale) && Near(stripe.localPosition, top + offset);
        }

        static BoxCollider EdgeSurface(Transform stripe, BoxCollider[] boxes)
        {
            foreach (var box in boxes) {
                var t = box.transform;
                if (t.parent || !(t.name == "Table deck" || t.name.StartsWith("Shared pad ", StringComparison.Ordinal) || t.name.StartsWith("Lane pad ", StringComparison.Ordinal))) continue;
                if (!Near(t.rotation, Quaternion.identity)) continue;
                var size = Vector3.Scale(box.size, t.lossyScale);
                var top = t.TransformPoint(box.center) + Vector3.up * size.y / 2;
                if (Strip(stripe, top, size, true, -1) || Strip(stripe, top, size, true, 1)
                    || Strip(stripe, top, size, false, -1) || Strip(stripe, top, size, false, 1)) return box;
            }
            return null;
        }

        static BoxCollider Surface(Transform marking, BoxCollider[] boxes)
        {
            if (marking.name == "Edge stripe") return EdgeSurface(marking, boxes);
            if (!marking.parent) return null;
            var box = marking.parent.GetComponent<BoxCollider>();
            if (box) return box;
            var floor = marking.parent.Find("Landing surface");
            return floor ? floor.GetComponent<BoxCollider>() : null;
        }
        static CourseCheckpoints CheckpointAt(Transform marking, BoxCollider[] boxes, CourseCheckpoints[] checkpoints)
        {
            var surface = Surface(marking, boxes);
            return surface ? checkpoints.FirstOrDefault(cp => cp.points != null && cp.points.Any(p => p != null && p.landing == surface)) : null;
        }
        static string CheckpointSurface(Transform marking, BoxCollider[] boxes, CourseCheckpoints[] checkpoints)
        {
            return CheckpointAt(marking, boxes, checkpoints) ? Hierarchy(Surface(marking, boxes).transform) : "";
        }
        static bool MaterialIs(Renderer renderer, string name) => renderer.sharedMaterial
            && AssetDatabase.GetAssetPath(renderer.sharedMaterial) == Materials + name + ".mat";
        static bool Near(float a, float b) => Mathf.Abs(a - b) < .0002f;
        static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < .00000004f;
        static bool Near(Quaternion a, Quaternion b) => Quaternion.Angle(a, b) < .001f;
        static string Hierarchy(Transform t) => t.parent ? Hierarchy(t.parent) + "/" + t.name : t.name;
        static Marking Describe(Transform t, string checkpointSurface) => new Marking {
            name = t.name, hierarchy = Hierarchy(t), position = t.position,
            objectId = GlobalObjectId.GetGlobalObjectIdSlow(t.gameObject).ToString(), checkpointSurface = checkpointSurface
        };
    }
}
