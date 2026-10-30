using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalliGalli.Movement
{
    [DefaultExecutionOrder(-400)]
    public sealed partial class UserMapRuntime : MonoBehaviour
    {
        public const string ScenePath = "Assets/HalliGalliMovement/Demo/UserMapCourse.unity";
        internal static readonly Color[] PaletteColors = { new Color(.12f,.57f,.67f), new Color(.95f,.45f,.16f), new Color(.22f,.72f,.4f), new Color(.47f,.34f,.76f), new Color(.95f,.72f,.18f), new Color(.7f,.84f,.9f), new Color(.86f,.12f,.15f), new Color(.18f,.23f,.28f) };
        public static UserMapData Pending { get; set; }
        public static UserMapRuntime Current { get; private set; }
        public UserMapData Data { get; private set; }
        public CourseSession session;
        public GameObject bellVisualTemplate;
        public SourceMovementProfile movementProfile;
        readonly Dictionary<string, GameObject> roots = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        readonly List<FactorySurface> surfaces = new List<FactorySurface>();
        readonly List<FactoryPress> presses = new List<FactoryPress>();
        readonly List<UserMapHazard> hazards = new List<UserMapHazard>();
        readonly List<GameObject> waypointVisuals = new List<GameObject>();
        readonly List<Vector3> routePositions = new List<Vector3>();
        Mesh rampMesh, surfMesh;
        Transform generated;
        FactoryCourse machinery;
        CourseMapDefinition definition;
        bool editing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Pending = null; Current = null; }
        void Awake()
        {
            Current = this;
            session.userMap = this;
            var pending = Pending; Pending = null;
            Rebuild(pending ?? UserMapStorage.CreateDefault());
        }

        public GameObject RootFor(string objectId) => objectId != null && roots.TryGetValue(objectId, out var root) ? root : null;

        public void Rebuild(UserMapData data)
        {
            var snapshot = UserMapStorage.Clone(data);
            EnsureResources();
            ClearCourse();
            Data = snapshot;
            generated = new GameObject("User map geometry").transform;
            generated.SetParent(transform, false);
            machinery = generated.gameObject.AddComponent<FactoryCourse>();
            machinery.session = session; machinery.presses = Array.Empty<FactoryPress>(); machinery.movingPlatforms = Array.Empty<FactorySurface>();
            session.factory = machinery; session.fallHeight = snapshot.fallHeight;
            var checkpoints = new List<CourseCheckpoints.Point>();
            UserMapObject bell = null;
            foreach (var item in snapshot.objects) {
                var root = new GameObject(item.kind + " " + item.id).transform;
                root.SetParent(generated, false); root.SetPositionAndRotation(item.position, Quaternion.Euler(item.rotation));
                roots.Add(item.id, root.gameObject);
                switch (item.kind) {
                    case UserMapPart.Block: MakeBlock(root, item); break;
                    case UserMapPart.Ramp: MakeSlope(root, item, rampMesh); break;
                    case UserMapPart.Surf: MakeSlope(root, item, surfMesh); break;
                    case UserMapPart.Checkpoint:
                        var landing = MakeBlock(root, item);
                        var spawn = Node(root, "Checkpoint spawn", root.position + Vector3.up * (item.scale.y * .5f + .025f));
                        spawn.rotation = root.rotation;
                        checkpoints.Add(new CourseCheckpoints.Point { landing = landing, spawn = spawn });
                        break;
                    case UserMapPart.MovingPlatform:
                    case UserMapPart.Conveyor:
                        MakeBlock(root, item);
                        var surface = root.gameObject.AddComponent<FactorySurface>();
                        surface.origin = item.position; surface.period = item.period;
                        surface.travel = item.kind == UserMapPart.MovingPlatform ? item.travel : Vector3.zero;
                        surface.beltVelocity = item.kind == UserMapPart.Conveyor ? root.forward * item.speed : Vector3.zero;
                        surface.standingOffset = item.scale.y * .5f + .025f;
                        surfaces.Add(surface);
                        break;
                    case UserMapPart.Press: MakePress(root, item); break;
                    case UserMapPart.Hazard:
                        MakeBlock(root, item);
                        var hazard = root.gameObject.AddComponent<UserMapHazard>(); hazard.session = session;
                        hazard.area = root.gameObject.AddComponent<BoxCollider>(); hazard.area.size = item.scale + Vector3.one * .12f; hazard.area.isTrigger = true;
                        hazards.Add(hazard);
                        break;
                    case UserMapPart.Bell:
                        bell = item; MakeBell(root, item); break;
                    case UserMapPart.Waypoint:
                        var marker = Box(root, "AI waypoint (editor only)", Vector3.zero, item.scale, Finish.Waypoint, item.color, true);
                        waypointVisuals.Add(marker.gameObject); routePositions.Add(item.position);
                        break;
                }
            }
            BuildRoutes(bell, checkpoints);
            session.checkpoints.points = checkpoints.ToArray();
            machinery.presses = presses.ToArray(); machinery.movingPlatforms = surfaces.ToArray();
            if (!definition) {
                definition = ScriptableObject.CreateInstance<CourseMapDefinition>();
                definition.hideFlags = HideFlags.DontSave;
            }
            definition.id = "user:" + snapshot.id; definition.displayName = snapshot.name; definition.description = snapshot.description;
            definition.scenePath = ScenePath; definition.playable = true; definition.style = CourseMapStyle.Jump; definition.movementProfile = movementProfile;
            session.maps.current = definition;
            ApplyRoster(session.roster);
            SetEditing(editing);
            Physics.SyncTransforms();
        }

        void BuildRoutes(UserMapObject bell, List<CourseCheckpoints.Point> checkpoints)
        {
            // Explicit markers retain authoring order. With none, use the ordered playable surfaces.
            if (routePositions.Count == 0) foreach (var item in Data.objects) {
                if (item.kind == UserMapPart.Block || item.kind == UserMapPart.Checkpoint || item.kind == UserMapPart.MovingPlatform || item.kind == UserMapPart.Conveyor)
                    routePositions.Add(item.position + Vector3.up * (item.scale.y * .5f + .025f));
            }
            Vector3 bellLanding = bell.position + Quaternion.Euler(bell.rotation) * new Vector3(0, .025f, -(.38f * bell.scale.z + .47f));
            if (routePositions.Count == 0) routePositions.Add(new Vector3(0, .025f, -.35f));
            routePositions.Add(bellLanding);
            foreach (var checkpoint in checkpoints) {
                float nearest = float.PositiveInfinity;
                for (int i = 0; i < routePositions.Count; i++) {
                    float distance = (routePositions[i] - checkpoint.spawn.position).sqrMagnitude;
                    if (distance < nearest) { nearest = distance; checkpoint.resumeWaypoint = i; }
                }
            }
            var markerLists = new List<Transform>[surfaces.Count];
            for (int i = 0; i < markerLists.Length; i++) markerLists[i] = new List<Transform>();
            foreach (var route in session.roster.routeRoots) {
                for (int i = route.childCount - 1; i >= 0; i--) Retire(route.GetChild(i).gameObject);
                for (int index = 0; index < routePositions.Count; index++) {
                    var node = Node(route, "User landing " + index, routePositions[index]);
                    node.gameObject.AddComponent<ClimbStep>().arrivalRadius = index == routePositions.Count - 1 ? .3f : .65f;
                    for (int i = 0; i < surfaces.Count; i++) {
                        var surface = surfaces[i];
                        if (surface.travel == Vector3.zero) continue;
                        var box = surface.GetComponent<BoxCollider>();
                        Vector3 local = surface.transform.InverseTransformPoint(node.position);
                        if (Mathf.Abs(local.x) <= box.size.x * .5f && Mathf.Abs(local.z) <= box.size.z * .5f && Mathf.Abs(local.y - box.size.y * .5f) < .3f)
                            markerLists[i].Add(node);
                    }
                }
            }
            for (int i = 0; i < surfaces.Count; i++) surfaces[i].routeMarkers = markerLists[i].ToArray();
        }

        public void ApplyRoster(CourseRoster roster)
        {
            if (!roster || Data == null) return;
            for (int i = 0; i < roster.allParticipants.Length; i++) {
                if (roster.firstPlatforms[i]) roster.firstPlatforms[i].gameObject.SetActive(false);
                var bot = roster.allParticipants[i].GetComponent<CourseRival>();
                if (bot) bot.route = roster.RouteForSeat(i);
                roster.allParticipants[i].SetCheckpoint(-1, 0);
            }
            if (roster.mergeSurface) roster.mergeSurface.parent.gameObject.SetActive(false);
        }

        public void SetEditing(bool value)
        {
            editing = value;
            if (!generated) return;
            machinery.enabled = !value; session.checkpoints.enabled = !value;
            foreach (var surface in surfaces) {
                surface.enabled = !value;
                if (value) surface.transform.position = surface.origin;
                else surface.transform.position = surface.PositionAt(FactorySurface.Clock);
                if (surface.routeMarkers != null) foreach (var marker in surface.routeMarkers)
                    marker.position = surface.transform.position + Vector3.up * surface.standingOffset;
            }
            foreach (var press in presses) { press.enabled = !value; press.Apply(value ? 0 : FactorySurface.Clock); }
            foreach (var hazard in hazards) hazard.enabled = !value;
            foreach (var visual in waypointVisuals) visual.SetActive(value);
            Physics.SyncTransforms();
        }

        BoxCollider MakeBlock(Transform root, UserMapObject item)
        {
            MakePlatformVisual(root, item);
            var collider = root.gameObject.AddComponent<BoxCollider>(); collider.size = item.scale;
            return collider;
        }
        void MakeSlope(Transform root, UserMapObject item, Mesh mesh)
        {
            var shape = new GameObject(item.kind + " collision mesh", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            shape.transform.SetParent(root, false); shape.transform.localScale = item.scale;
            shape.GetComponent<MeshFilter>().sharedMesh = mesh;
            ApplyFinish(shape.GetComponent<MeshRenderer>(), item.kind == UserMapPart.Ramp ? Finish.Ramp : Finish.Surf, item.color);
            shape.GetComponent<MeshCollider>().sharedMesh = mesh;
        }
        void MakePress(Transform root, UserMapObject item)
        {
            var press = root.gameObject.AddComponent<FactoryPress>(); press.period = item.period; press.orientedContact = true;
            float height = item.scale.y, width = item.scale.x;
            for (int side = -1; side <= 1; side += 2)
                Box(root, "Press upright", new Vector3(side * (width * .5f + .2f), height * .5f, 0), new Vector3(.25f, height, item.scale.z), Finish.Steel, item.color, true);
            Box(root, "Press crossbeam", new Vector3(0, height + .65f, 0), new Vector3(width + .65f, .25f, item.scale.z), Finish.Steel, item.color, true);
            press.head = LayeredSolidBox(root, "Press head", Vector3.up * (height + .3f), new Vector3(width, .6f, item.scale.z), Finish.Press, item.color);
            press.headCollider = press.head.GetComponent<BoxCollider>(); press.raised = press.head.position; press.stroke = height;
            press.warning = MakePressLamp(root, new Vector3(-width * .5f - .2f, height * .75f, -item.scale.z * .5f - .05f));
            presses.Add(press);
        }
        void MakeBell(Transform root, UserMapObject item)
        {
            root.localScale = item.scale;
            LayeredSolidBox(root, "Bell plinth", new Vector3(0, .35f, 0), new Vector3(.7f, .7f, .7f), Finish.Plinth, item.color);
            var bell = Instantiate(bellVisualTemplate, root, false);
            bell.name = "Service bell"; bell.transform.localPosition = Vector3.up * .7f; bell.transform.localRotation = Quaternion.identity; bell.transform.localScale = Vector3.one;
            bell.SetActive(true);
            session.SetBell(bell.transform, bell.transform.Find("Button"), bell.GetComponent<AudioSource>());
        }
        static Transform Node(Transform parent, string name, Vector3 position)
        {
            var node = new GameObject(name).transform; node.SetParent(parent, false); node.position = position; return node;
        }
        Transform Box(Transform parent, string name, Vector3 position, Vector3 scale, Finish finish, int accent, bool solid, float beltSpeed = 0, Vector2 direction = default)
        {
            var box = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            box.transform.SetParent(parent, false); box.transform.localPosition = position; box.transform.localScale = scale;
            box.GetComponent<MeshFilter>().sharedMesh = boxMesh;
            ApplyFinish(box.GetComponent<MeshRenderer>(), finish, accent, beltSpeed, direction);
            if (solid) box.AddComponent<BoxCollider>();
            return box.transform;
        }
        void EnsureResources()
        {
            if (rampMesh) return;
            EnsureCraftedResources();
            rampMesh = MeshFrom("User ramp", new[] { new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f), new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f), new Vector3(-.5f,.5f,.5f), new Vector3(.5f,.5f,.5f) }, new[] { 0,4,5, 0,5,1, 0,1,3, 0,3,2, 2,3,5, 2,5,4, 0,2,4, 1,5,3 });
            surfMesh = MeshFrom("User two-sided surf", new[] { new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f), new Vector3(0,.5f,-.5f), new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f), new Vector3(0,.5f,.5f) }, new[] { 0,1,4, 0,4,3, 0,3,5, 0,5,2, 1,2,5, 1,5,4, 0,2,1, 3,4,5 });
        }
        static Mesh MeshFrom(string name, Vector3[] corners, int[] indices)
        {
            var vertices = new Vector3[indices.Length]; var triangles = new int[indices.Length]; var uv = new Vector2[indices.Length];
            for (int i = 0; i < indices.Length; i++) { vertices[i] = corners[indices[i]]; triangles[i] = i; }
            for (int i = 0; i < vertices.Length; i += 3) {
                var normal = Vector3.Cross(vertices[i + 1] - vertices[i], vertices[i + 2] - vertices[i]).normalized;
                var axis = Mathf.Abs(normal.y) > .5f ? Vector3.right : Vector3.up;
                var across = Vector3.Cross(normal, axis).normalized;
                var along = Vector3.Cross(across, normal);
                for (int j = 0; j < 3; j++) uv[i + j] = new Vector2(Vector3.Dot(vertices[i + j], along), Vector3.Dot(vertices[i + j], across));
            }
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave, vertices = vertices, triangles = triangles, uv = uv };
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); return mesh;
        }
        static void Retire(GameObject target)
        {
            target.SetActive(false); target.transform.SetParent(null); Destroy(target);
        }
        void ClearCourse()
        {
            if (generated) Retire(generated.gameObject);
            roots.Clear(); surfaces.Clear(); presses.Clear(); hazards.Clear(); waypointVisuals.Clear(); routePositions.Clear();
            session.checkpoints.points = Array.Empty<CourseCheckpoints.Point>();
        }
        void OnDestroy()
        {
            if (Current == this) Current = null;
            ReleaseCraftedResources();
            if (rampMesh) Destroy(rampMesh); if (surfMesh) Destroy(surfMesh); if (definition) Destroy(definition);
        }
    }

    [DefaultExecutionOrder(200)]
    internal sealed class UserMapHazard : MonoBehaviour
    {
        public CourseSession session;
        public BoxCollider area;
        void FixedUpdate()
        {
            if (!CourseNetwork.Authority || !session.RunActive) return;
            foreach (var participant in session.participants) {
                if (!participant.Racing) continue;
                var body = participant.GetComponent<CharacterController>();
                if (body.enabled && area.bounds.Intersects(body.bounds)
                    && Physics.ComputePenetration(area, area.transform.position, area.transform.rotation, body, body.transform.position, body.transform.rotation, out _, out _))
                    participant.BeginFall();
            }
        }
    }
}
