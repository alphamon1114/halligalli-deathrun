using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HalliGalli.Cards
{
    // The arrows describe reveal order only. Fruit totals never affect this display.
    [DisallowMultipleComponent]
    public sealed class CardTurnDisplay : MonoBehaviour
    {
        public int HighlightedSeat { get; private set; } = -1;
        public int ArrowCount { get; private set; }
        public int DestinationFor(int seat) => destinations[seat];

        CardTableDisplay table;
        Transform root;
        MeshRenderer[] arrows;
        Mesh[] arrowMeshes;
        int[] destinations;
        MeshRenderer outline;
        Mesh outlineMesh;
        Material normalMaterial, activeMaterial;
        int eligibleMask = -1;
        Vector3 center;

        public void Bind(CardTableDisplay display, HalliGalliMatch match)
        {
            Release();
            table = display;
            root = new GameObject("Card reveal order").transform;
            root.SetParent(transform, false);
            normalMaterial = MakeMaterial(new Color(.36f, .59f, .61f));
            activeMaterial = MakeMaterial(new Color(1f, .76f, .25f));
            int count = match.Seats.Count;
            arrows = new MeshRenderer[count];
            arrowMeshes = new Mesh[count];
            destinations = new int[count];
            center = Vector3.zero;
            foreach (var anchor in table.openAnchors) center += anchor.position;
            center /= count;
            center.y += .008f;
            for (int i = 0; i < count; i++)
                arrows[i] = CreateMesh("Reveal arrow from seat " + (i + 1), out arrowMeshes[i]);
            outline = CreateMesh("Current reveal seat", out outlineMesh);
            eligibleMask = -1;
            Refresh(match);
        }

        Material MakeMaterial(Color color)
        {
            // The deck's referenced shader is already included in both editor and player builds.
            var material = new Material(table.deck.edgeMaterial);
            material.name = "Card turn indicator";
            material.mainTexture = null;
            material.color = color;
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * .45f);
            }
            return material;
        }

        MeshRenderer CreateMesh(string name, out Mesh mesh)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false);
            mesh = new Mesh { name = name };
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = normalMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        public void Refresh(HalliGalliMatch match)
        {
            if (!root) return;
            int mask = 0;
            for (int i = 0; i < match.Seats.Count; i++)
                if (!match.Seats[i].Eliminated && match.Seats[i].Draw.Count > 0) mask |= 1 << i;
            if (match.Finished) mask = 0;
            if (mask != eligibleMask)
            {
                eligibleMask = mask;
                ArrowCount = 0;
                for (int i = 0; i < arrows.Length; i++)
                {
                    destinations[i] = -1;
                    if ((mask & (1 << i)) != 0)
                        for (int step = 1; step <= arrows.Length; step++)
                        {
                            int next = (i + step) % arrows.Length;
                            if ((mask & (1 << next)) == 0) continue;
                            destinations[i] = next;
                            BuildArrow(i, next);
                            ArrowCount++;
                            break;
                        }
                    arrows[i].gameObject.SetActive(destinations[i] >= 0);
                }
            }
            HighlightedSeat = match.CanReveal(match.Turn) ? match.Turn : -1;
            for (int i = 0; i < arrows.Length; i++)
                arrows[i].sharedMaterial = HighlightedSeat >= 0 && destinations[i] == HighlightedSeat ? activeMaterial : normalMaterial;
            outline.gameObject.SetActive(HighlightedSeat >= 0);
            if (HighlightedSeat >= 0) BuildOutline(table.openAnchors[HighlightedSeat]);
        }

        float AngleFor(int seat)
        {
            Vector3 delta = table.openAnchors[seat].position - center;
            return Mathf.Atan2(delta.z, delta.x);
        }

        void BuildArrow(int from, int to)
        {
            float start = AngleFor(from), end = AngleFor(to);
            while (end <= start + .001f) end += 2 * Mathf.PI;
            // An inset ring leaves both the fruit pictures and seat labels unobstructed.
            float radius = arrows.Length == 2 ? .28f : .44f;
            float gap = .17f;
            start += gap;
            end -= gap;
            float headLength = .10f / radius;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            int segments = Mathf.Max(4, Mathf.CeilToInt((end - start) * 14));
            for (int n = 0; n < segments; n++)
            {
                float a = Mathf.Lerp(start, end - headLength, n / (float)segments);
                float b = Mathf.Lerp(start, end - headLength, (n + 1) / (float)segments);
                Quad(vertices, triangles, Point(a, radius - .017f), Point(a, radius + .017f),
                    Point(b, radius + .017f), Point(b, radius - .017f));
            }
            Triangle(vertices, triangles, Point(end - headLength, radius - .065f),
                Point(end - headLength, radius + .065f), Point(end, radius));
            SetMesh(arrowMeshes[from], vertices, triangles);
        }

        Vector3 Point(float angle, float radius) =>
            transform.InverseTransformPoint(center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius);

        void BuildOutline(Transform anchor)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            Vector3[] outer = new Vector3[4], inner = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                float x = i == 0 || i == 3 ? -1 : 1, z = i < 2 ? -1 : 1;
                outer[i] = transform.InverseTransformPoint(anchor.TransformPoint(new Vector3(x * .292f, .003f, z * .421f)));
                inner[i] = transform.InverseTransformPoint(anchor.TransformPoint(new Vector3(x * .270f, .003f, z * .399f)));
            }
            for (int i = 0; i < 4; i++) Quad(vertices, triangles, outer[i], outer[(i + 1) % 4], inner[(i + 1) % 4], inner[i]);
            SetMesh(outlineMesh, vertices, triangles);
            outline.sharedMaterial = activeMaterial;
        }

        static void Quad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Triangle(v, t, a, b, c);
            Triangle(v, t, a, c, d);
        }
        static void Triangle(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c)
        {
            // All indicators face the top-down table camera.
            if (Vector3.Cross(b - a, c - a).y < 0) { var swap = b; b = c; c = swap; }
            int index = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            t.Add(index); t.Add(index + 1); t.Add(index + 2);
        }
        static void SetMesh(Mesh mesh, List<Vector3> vertices, List<int> triangles)
        {
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        }
        void Release()
        {
            if (root) { root.gameObject.SetActive(false); Dispose(root.gameObject); }
            if (arrowMeshes != null) foreach (var mesh in arrowMeshes) Dispose(mesh);
            Dispose(outlineMesh); Dispose(normalMaterial); Dispose(activeMaterial);
        }
        static void Dispose(Object value) { if (!value) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        void OnDestroy() { Release(); }
    }
}
