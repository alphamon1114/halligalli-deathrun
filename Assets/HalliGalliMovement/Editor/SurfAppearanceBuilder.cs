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
    // Water-only dressing. Route meshes, collision, checkpoints and runtime components are never rebuilt.
    public static class SurfAppearanceBuilder
    {
        const string MaterialFolder = MovementProjectBuilder.Root + "/Art/Surf";
        const string BulkheadVisual = "Surf broad bulkhead";
        const string DividerVisual = "Surf broad divider";

        [MenuItem("HalliGalli/Maps/Refresh Surf Appearance Only")]
        public static void RefreshAppearance()
        {
            var scene = EditorSceneManager.OpenScene(SurfCourseBuilder.ScenePath);
            string before = ProtectedState(scene);
            string beforeAppearance = AppearanceCounts(scene);
            try {
                ApplyCurrent();
                string after = ProtectedState(scene);
                if (before != after)
                    throw new InvalidOperationException("Surf appearance changed protected gameplay, audio, anchors or movement profile.");
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory("TestResults");
                File.WriteAllText("TestResults/surf-appearance.txt", "Before: " + beforeAppearance + "\nAfter: " + AppearanceCounts(scene)
                    + "\nProtected state SHA-256 before: " + Digest(System.Text.Encoding.UTF8.GetBytes(before))
                    + "\nProtected state SHA-256 after: " + Digest(System.Text.Encoding.UTF8.GetBytes(after))
                    + "\nMovement profile SHA-256: " + Digest(File.ReadAllBytes(SurfCourseBuilder.ProfilePath))
                    + "\nUnchanged: serialized route/checkpoint configuration, colliders, behaviours, rigidbodies, audio, anchors and world transforms."
                    + "\nHazard: continuous dark surface, perimeter warning tape, sparse electrical signs; no floor grid.\n");
                File.WriteAllText("TestResults/surf-appearance-protected-before.txt", before);
                File.WriteAllText("TestResults/surf-appearance-protected-after.txt", after);
            } catch {
                EditorSceneManager.OpenScene(SurfCourseBuilder.ScenePath);
                throw;
            }
        }

        public static void ApplyCurrent()
        {
            var course = Object.FindAnyObjectByType<SurfCourse>();
            var session = Object.FindAnyObjectByType<CourseSession>();
            if (!course || !session || course.gameObject.scene.path != SurfCourseBuilder.ScenePath)
                throw new InvalidOperationException("Open the saved water surf scene before refreshing its appearance.");
            var environment = GameObject.Find("VARCO environment");
            if (!environment) throw new InvalidOperationException("The existing water environment is missing.");

            // Legacy art is safe to remove only when it has no gameplay/audio components.
            foreach (var child in environment.transform.Cast<Transform>().ToArray()) {
                string name = child.name;
                if (name == "ConcreteFloor" || name == "ConcreteWall" || name == "ConcreteColumn" || name == "CeilingLight"
                    || name == "Fixture light" || name.StartsWith("Surf backdrop ", StringComparison.Ordinal) || name == "Surf fixture light")
                    RemoveDecoration(child);
            }
            InstallBackdrop(environment.transform, course, session);

            var bulkheads = course.transform.Find("Surf bulkheads");
            if (bulkheads) foreach (var wall in bulkheads.GetComponentsInChildren<BoxCollider>(true)) {
                if (wall.name == "Surf collision ceiling") continue;
                foreach (var child in wall.transform.Cast<Transform>().ToArray())
                    if (child.name == "Bulkhead panel" || child.name == "Bulkhead seam" || child.name == BulkheadVisual) RemoveDecoration(child);
                InstallBulkheadVisual(wall);
            }
            var divider = course.transform.Find(SurfDividerBuilder.DividerName);
            if (divider) {
                foreach (var child in divider.Cast<Transform>().ToArray())
                    if (child.name == "ConcreteFloor" || child.name == DividerVisual) RemoveDecoration(child);
                InstallDividerVisual(divider.GetComponent<BoxCollider>());
            }
            ApplyRouteMaterials(course);
            ApplyLighting();
        }

        internal static void InstallBackdrop(Transform parent, SurfCourse course, CourseSession session)
        {
            float bottom = session.fallHeight - 2, ceiling = 16;
            var bounds = new Bounds(new Vector3(0, 0, -8), Vector3.one * 16);
            foreach (var section in course.sections) foreach (var point in section.points) bounds.Encapsulate(point);
            bounds.Encapsulate(course.finish.position);
            float x0 = Mathf.Floor((bounds.min.x - 12) / 18) * 18, x1 = Mathf.Ceil((bounds.max.x + 12) / 18) * 18;
            float z0 = Mathf.Floor((bounds.min.z - 12) / 18) * 18, z1 = Mathf.Ceil((bounds.max.z + 12) / 18) * 18;
            float x = (x0 + x1) * .5f, z = (z0 + z1) * .5f, y = (bottom + ceiling) * .5f;
            var backdrop = Material("WaterBackdrop", new Color(.21f, .28f, .30f), .12f);
            var deep = Material("WaterDepth", new Color(.09f, .15f, .17f), .12f);
            Panel(parent, "Surf backdrop floor", new Vector3(x, bottom, z), new Vector3(x1 - x0, .2f, z1 - z0), deep);
            Panel(parent, "Surf backdrop ceiling", new Vector3(x, ceiling, z), new Vector3(x1 - x0, .2f, z1 - z0), backdrop);
            foreach (float side in new[] { x0, x1 })
                Panel(parent, "Surf backdrop wall", new Vector3(side, y, z), new Vector3(.28f, ceiling - bottom, z1 - z0), backdrop);
            foreach (float side in new[] { z0, z1 })
                Panel(parent, "Surf backdrop wall", new Vector3(x, y, side), new Vector3(x1 - x0, ceiling - bottom, .28f), backdrop);

            // Light destinations rather than repeating posts at every bank endpoint.
            Lamp(parent, new Vector3(0, 4.5f, -5), new Color(1, .90f, .76f), 1.5f, 14);
            Lamp(parent, new Vector3(0, -4, 25), new Color(.68f, .83f, 1), 1.2f, 24);
            foreach (var section in course.sections)
                if (section.landing != Vector3.zero) Lamp(parent, section.landing + Vector3.up * 7, new Color(.65f, .86f, 1), 1.4f, 22);
            Lamp(parent, new Vector3(-77, -22, 35), new Color(.62f, .79f, .9f), 1.5f, 35);
            Lamp(parent, new Vector3(18, -34, 89), new Color(.62f, .79f, .9f), 1.5f, 34);
            Lamp(parent, course.finish.position + Vector3.up * 7, new Color(1, .84f, .58f), 1.8f, 20);
        }

        internal static void InstallBulkheadVisual(BoxCollider wall)
        {
            Panel(wall.transform, BulkheadVisual, wall.center, wall.size, Material("WaterBackdrop", new Color(.21f, .28f, .30f), .12f));
        }

        internal static void InstallDividerVisual(BoxCollider divider)
        {
            if (!divider) throw new InvalidOperationException("The water divider collider is missing.");
            Panel(divider.transform, DividerVisual, divider.center, divider.size, Material("WaterDepth", new Color(.09f, .15f, .17f), .12f));
        }

        internal static void ApplyRouteMaterials(SurfCourse course)
        {
            var bank = Material("WaterBank", new Color(.68f, .76f, .77f), .24f);
            var deck = Material("WaterLanding", new Color(.62f, .70f, .67f), .18f);
            var edge = RouteEdgeMaterial();
            // Reuse only restrained concrete normal detail, never its dark tiled albedo.
            var concrete = EnvironmentAssetImporter.Prefab("ConcreteFloor").GetComponentInChildren<MeshRenderer>().sharedMaterial;
            var normal = concrete.GetTexture("_BumpMap");
            foreach (var surface in new[] { bank, deck }) {
                surface.SetTexture("_BumpMap", normal);
                surface.SetFloat("_BumpScale", .18f);
                if (normal) surface.EnableKeyword("_NORMALMAP"); else surface.DisableKeyword("_NORMALMAP");
            }
            foreach (var child in course.transform.Cast<Transform>()) {
                if (child.name.StartsWith("Surf ramp ", StringComparison.Ordinal) && child.GetComponent<MeshCollider>())
                    child.GetComponent<MeshRenderer>().sharedMaterial = bank;
                else if (child.name.StartsWith("Surf ramp ", StringComparison.Ordinal) && child.name.EndsWith(" edge", StringComparison.Ordinal))
                    child.GetComponent<MeshRenderer>().sharedMaterial = edge;
                else if (child.name == "Surf turn landing" || child.name == "Surf midway landing" || child.name == "Surf transfer landing" || child.name == "Surf finish")
                    foreach (var renderer in child.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial = deck;
            }
        }

        internal static Material RouteEdgeMaterial() => Material("SurfEdge", new Color(.16f, .65f, .69f), .45f);

        internal static void ApplyLighting()
        {
            // Readable surfaces even inside the shadowed portal; no extra decorative light geometry.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.45f, .51f, .53f);
            RenderSettings.ambientEquatorColor = new Color(.32f, .38f, .41f);
            RenderSettings.ambientGroundColor = new Color(.18f, .23f, .25f);
            RenderSettings.ambientIntensity = 1;
            RenderSettings.fogColor = new Color(.21f, .28f, .30f);
            RenderSettings.fogStartDistance = 120;
            RenderSettings.fogEndDistance = 240;
            foreach (var light in All<Light>(SceneManager.GetActiveScene()).Where(light => light.type == LightType.Directional))
                light.intensity = light.name == "Key" ? .8f : .3f;
        }

        static Material Material(string name, Color color, float emission = 0)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) {
                Directory.CreateDirectory(MaterialFolder);
                material = new Material(Shader.Find("Standard")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            // These materials belong only to water surf; refreshed defaults must also update saved assets.
            material.color = color;
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_Glossiness", .12f);
            if (emission > 0) {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * emission);
            }
            else {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        static void Panel(Transform parent, string name, Vector3 center, Vector3 size, Material material)
        {
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = name;
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.transform.SetParent(parent, false);
            panel.transform.localPosition = center;
            panel.transform.localScale = size;
            panel.GetComponent<Renderer>().sharedMaterial = material;
        }

        static void Lamp(Transform parent, Vector3 position, Color color, float intensity, float range)
        {
            var light = new GameObject("Surf fixture light").AddComponent<Light>();
            light.transform.SetParent(parent);
            light.transform.position = position;
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
        }

        static void RemoveDecoration(Transform decoration)
        {
            if (decoration.GetComponentsInChildren<Collider>(true).Length != 0
                || decoration.GetComponentsInChildren<MonoBehaviour>(true).Length != 0
                || decoration.GetComponentsInChildren<AudioSource>(true).Length != 0
                || decoration.GetComponentsInChildren<Rigidbody>(true).Length != 0) {
                foreach (var renderer in decoration.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                return;
            }
            Object.DestroyImmediate(decoration.gameObject);
        }

        static IEnumerable<T> All<T>(Scene scene) where T : Component
            => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));

        static string ProtectedState(Scene scene)
        {
            var components = All<Collider>(scene).Cast<Component>().Concat(All<MonoBehaviour>(scene))
                .Concat(All<AudioSource>(scene)).Concat(All<Rigidbody>(scene));
            var lines = components.Where(component => component).OrderBy(component => component.GetEntityId().ToString(), StringComparer.Ordinal).Select(component =>
                component.GetType().FullName + "|" + PathOf(component.transform) + "|" + component.GetEntityId()
                + "|" + component.gameObject.activeInHierarchy + "|" + component.gameObject.layer
                + "|" + component.transform.localToWorldMatrix.ToString("R") + "|" + EditorJsonUtility.ToJson(component)).ToList();
            var anchors = All<CourseCheckpoints>(scene).SelectMany(checkpoints => checkpoints.points.Select(point => point.spawn))
                .Concat(All<SurfCourse>(scene).Select(course => course.finish))
                .Concat(All<CourseSession>(scene).Select(session => session.bellTarget))
                .Concat(All<CourseRoster>(scene).SelectMany(roster => roster.drawAnchors.Concat(roster.openAnchors)
                    .Concat(roster.routeRoots.SelectMany(route => route.Cast<Transform>()))
                    .Concat(roster.allParticipants.SelectMany(participant => new[] { participant.seatAnchor, participant.standAnchor, participant.retryAnchor }))));
            foreach (var anchor in anchors.Where(anchor => anchor).Distinct().OrderBy(anchor => anchor.GetEntityId().ToString(), StringComparer.Ordinal))
                lines.Add("anchor|" + PathOf(anchor) + "|" + anchor.GetEntityId() + "|" + anchor.localToWorldMatrix.ToString("R"));
            lines.Add("movementProfile|" + Digest(File.ReadAllBytes(SurfCourseBuilder.ProfilePath)));
            return string.Join("\n", lines);
        }

        static string PathOf(Transform transform) => transform.parent ? PathOf(transform.parent) + "/" + transform.name : transform.name;

        static string Digest(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        static string AppearanceCounts(Scene scene)
        {
            var transforms = All<Transform>(scene).ToArray();
            var renderers = All<Renderer>(scene).ToArray();
            var counts = new List<string> { "renderers=" + renderers.Count(r => r.enabled && r.gameObject.activeInHierarchy),
                "colliders=" + All<Collider>(scene).Count(), "meshColliders=" + All<MeshCollider>(scene).Count(),
                "checkpointOutlines=" + renderers.Count(r => r.name == "Checkpoint outline" && r.enabled) };
            foreach (string name in new[] { "Bulkhead panel", "Bulkhead seam", "ConcreteColumn", "CeilingLight", "Fixture light", BulkheadVisual, DividerVisual, "Surf fixture light" })
                counts.Add(name + "=" + transforms.Count(t => t.name == name));
            counts.Add("backdropPlanes=" + transforms.Count(t => t.name.StartsWith("Surf backdrop ", StringComparison.Ordinal)));
            return string.Join(", ", counts);
        }
    }
}
