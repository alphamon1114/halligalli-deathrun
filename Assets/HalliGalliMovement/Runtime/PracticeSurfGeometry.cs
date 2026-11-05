using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed class PracticeSurfGeometry : MonoBehaviour
    {
        public struct Stage
        {
            public Vector3 start;
            public Bounds finish, limits, catchZone;
            public float fallY;
        }

        public Stage[] Stages { get; private set; }

        const float BankHalfWidth = 3.2f;
        const float CatchHalfWidth = 4f;
        const float DeckHalfWidth = 6f;
        const float DeckLength = 16f;
        const float Thickness = .45f;
        static readonly float CrossSlope = Mathf.Tan(55f * Mathf.Deg2Rad);
        readonly List<Mesh> meshes = new List<Mesh>(14);
        readonly List<Material> materials = new List<Material>(4);
        Transform geometryRoot;
        Material bankMaterial, deckMaterial, landingMaterial, cueMaterial;

        public void Build(Material surface)
        {
            if (Stages != null) return;
            if (!surface) throw new ArgumentNullException(nameof(surface));

            // Vertices and stage records are world-space; the owning practice area has an identity transform.
            geometryRoot = new GameObject("Practice surf lanes").transform;
            geometryRoot.SetParent(transform, false);
            bankMaterial = Tint(surface, "Practice surf concrete", new Color(.78f, .87f, .94f));
            deckMaterial = Tint(surface, "Practice surf start", new Color(.87f, .89f, .9f));
            landingMaterial = Tint(surface, "Practice surf landing", new Color(.5f, .95f, .65f));
            cueMaterial = Tint(surface, "Practice surf direction", new Color(1f, .82f, .3f));
            Stages = new Stage[3];

            var straight = new[] { new Vector2(0, 16), new Vector2(18, 11.5f) };
            Bank("1 - Straight bank", 30, straight, BankHalfWidth);
            Stages[0] = StageDecks(0, 30, straight[0].y, 18, 11.5f, BankHalfWidth);

            var takeoff = new[] { new Vector2(0, 18), new Vector2(16, 14) };
            var lowerCatch = new[] { new Vector2(18.4f, 12.2f), new Vector2(30.4f, 9.2f) };
            Bank("2 - Approach bank", 52, takeoff, BankHalfWidth);
            Bank("2 - Lower receiving bank", 52, lowerCatch, CatchHalfWidth);
            Stages[1] = StageDecks(1, 52, takeoff[0].y, 30.4f, 9.2f, CatchHalfWidth);
            Stages[1].catchZone = ReceivingBounds(52, lowerCatch);

            // The downhill eases to a level tangent, then rises monotonically to a positive launch tangent.
            // A single closed mesh joins these pieces: no internal cap can obstruct the launch approach.
            var launch = new Vector2[34];
            launch[0] = new Vector2(0, 18);
            launch[1] = new Vector2(14, 13.8f);
            for (int i = 1; i <= 8; i++) {
                float t = i / 8f;
                launch[i + 1] = new Vector2(14 + 2 * t, 13.8f - .6f * t + .3f * t * t);
            }
            for (int i = 1; i <= 24; i++) {
                float t = i / 24f;
                launch[i + 9] = new Vector2(16 + 6 * t, 13.5f + 1.65f * t * t);
            }
            var higherCatch = new[] { new Vector2(24.4f, 15.4f), new Vector2(39.4f, 12.4f) };
            Bank("3 - Acceleration and rising lip", 74, launch, BankHalfWidth);
            Bank("3 - Higher receiving bank", 74, higherCatch, CatchHalfWidth);
            Stages[2] = StageDecks(2, 74, launch[0].y, 39.4f, 12.4f, CatchHalfWidth);
            Stages[2].catchZone = ReceivingBounds(74, higherCatch);
            // Nothing spans z=16..18.4 on lane 2 or z=22..24.4 on lane 3, including decorative meshes.
        }

        Stage StageDecks(int index, float x, float entryY, float exitZ, float exitY, float halfWidth)
        {
            // Start above the highest part of the front cap; walking forward cannot encounter a wall face.
            float startY = entryY + BankHalfWidth * CrossSlope + .45f;
            Deck((index + 1) + " - Start deck", x, startY, -6, 0, deckMaterial);
            Arrow((index + 1) + " - Forward cue", new Vector3(x + .5f, startY + .012f, -1.65f));

            // Even the lowest bank edge clears the deck's front face. Its depth catches normal exit momentum.
            float finishY = exitY - halfWidth * CrossSlope - .45f;
            Deck((index + 1) + " - Landing deck", x, finishY, exitZ, exitZ + DeckLength, landingMaterial);
            AreaLight((index + 1) + " - Entry light", new Vector3(x, startY + 8, 6), 32);
            AreaLight((index + 1) + " - Landing light", new Vector3(x, finishY + 15, exitZ + 5), 30);

            var limits = new Bounds();
            limits.SetMinMax(new Vector3(x - 9, -5.5f, -9), new Vector3(x + 9, startY + 12, exitZ + DeckLength + 6));
            var finish = new Bounds();
            finish.SetMinMax(new Vector3(x - DeckHalfWidth + .35f, finishY - .15f, exitZ + .4f),
                new Vector3(x + DeckHalfWidth - .35f, finishY + 2, exitZ + DeckLength - .35f));
            return new Stage {
                start = new Vector3(x + .5f, startY + .025f, -4),
                finish = finish,
                limits = limits,
                fallY = -4.5f
            };
        }

        static Bounds ReceivingBounds(float x, Vector2[] points)
        {
            var bounds = new Bounds();
            bounds.SetMinMax(new Vector3(x - CatchHalfWidth - .5f, points[points.Length - 1].y - CatchHalfWidth * CrossSlope - .5f, points[0].x - .5f),
                new Vector3(x + CatchHalfWidth + .5f, points[0].y + CatchHalfWidth * CrossSlope + .5f, points[points.Length - 1].x + .5f));
            return bounds;
        }

        void Bank(string name, float x, Vector2[] points, float halfWidth)
        {
            var data = new MeshData((points.Length - 1) * 4 + 2);
            Vector3 down = Vector3.down * Thickness;
            float length = points[points.Length - 1].x - points[0].x;
            for (int i = 0; i < points.Length - 1; i++) {
                Vector3 a = BankPoint(x, -halfWidth, points[i]);
                Vector3 b = BankPoint(x, halfWidth, points[i]);
                Vector3 c = BankPoint(x, halfWidth, points[i + 1]);
                Vector3 d = BankPoint(x, -halfWidth, points[i + 1]);
                data.Quad(a, b, c, d, Vector3.up, (points[i].x - points[0].x) / length, (points[i + 1].x - points[0].x) / length);
                data.Quad(a + down, b + down, c + down, d + down, Vector3.down);
                data.Quad(a, d, d + down, a + down, Vector3.left);
                data.Quad(b, c, c + down, b + down, Vector3.right);
                if (i == 0) data.Quad(a, b, b + down, a + down, Vector3.back);
                if (i == points.Length - 2) data.Quad(d, c, c + down, d + down, Vector3.forward);
            }
            MeshObject(name, data, bankMaterial, true);
        }

        static Vector3 BankPoint(float x, float lateral, Vector2 point)
            => new Vector3(x + lateral, point.y + lateral * CrossSlope, point.x);

        void Deck(string name, float x, float top, float near, float far, Material material)
        {
            var data = new MeshData(6);
            var a = new Vector3(x - DeckHalfWidth, top, near);
            var b = new Vector3(x + DeckHalfWidth, top, near);
            var c = new Vector3(x + DeckHalfWidth, top, far);
            var d = new Vector3(x - DeckHalfWidth, top, far);
            Vector3 down = Vector3.down * Thickness;
            data.Quad(a, b, c, d, Vector3.up);
            data.Quad(a + down, b + down, c + down, d + down, Vector3.down);
            data.Quad(a, d, d + down, a + down, Vector3.left);
            data.Quad(b, c, c + down, b + down, Vector3.right);
            data.Quad(a, b, b + down, a + down, Vector3.back);
            data.Quad(d, c, c + down, d + down, Vector3.forward);
            MeshObject(name, data, material, true);
        }

        void Arrow(string name, Vector3 center)
        {
            var data = new MeshData(2);
            data.Quad(center + new Vector3(-.13f, 0, -.75f), center + new Vector3(.13f, 0, -.75f),
                center + new Vector3(.13f, 0, .1f), center + new Vector3(-.13f, 0, .1f), Vector3.up);
            data.Triangle(center + new Vector3(-.5f, 0, .1f), center + new Vector3(0, 0, .85f), center + new Vector3(.5f, 0, .1f));
            MeshObject(name, data, cueMaterial, false);
        }

        void MeshObject(string name, MeshData data, Material material, bool solid)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(data.vertices);
            mesh.SetTriangles(data.triangles, 0);
            mesh.SetUVs(0, data.uv);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            meshes.Add(mesh);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(geometryRoot, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (solid) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        Material Tint(Material source, string name, Color color)
        {
            var material = new Material(source) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            materials.Add(material);
            return material;
        }

        void AreaLight(string name, Vector3 position, float range)
        {
            var go = new GameObject(name, typeof(Light));
            go.transform.SetParent(geometryRoot, false);
            go.transform.localPosition = position;
            var light = go.GetComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(.94f, .97f, 1);
            light.intensity = 2.8f;
            light.range = range;
            light.shadows = LightShadows.None;
        }

        void OnDestroy()
        {
            if (geometryRoot) {
                geometryRoot.gameObject.SetActive(false);
                Destroy(geometryRoot.gameObject);
            }
            for (int i = 0; i < meshes.Count; i++) if (meshes[i]) Destroy(meshes[i]);
            for (int i = 0; i < materials.Count; i++) if (materials[i]) Destroy(materials[i]);
        }

        sealed class MeshData
        {
            public readonly List<Vector3> vertices;
            public readonly List<int> triangles;
            public readonly List<Vector2> uv;

            public MeshData(int quads)
            {
                vertices = new List<Vector3>(quads * 4);
                triangles = new List<int>(quads * 6);
                uv = new List<Vector2>(quads * 4);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, float v0 = 0, float v1 = 1)
            {
                int first = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                // Reuse the intact concrete top-face atlas island, including across the sampled launch curve.
                uv.Add(Atlas(0, v0)); uv.Add(Atlas(1, v0)); uv.Add(Atlas(1, v1)); uv.Add(Atlas(0, v1));
                bool reverse = Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0;
                triangles.Add(first); triangles.Add(first + (reverse ? 2 : 1)); triangles.Add(first + (reverse ? 1 : 2));
                triangles.Add(first); triangles.Add(first + (reverse ? 3 : 2)); triangles.Add(first + (reverse ? 2 : 3));
            }

            public void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                int first = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                uv.Add(Atlas(0, 0)); uv.Add(Atlas(.5f, 1)); uv.Add(Atlas(1, 0));
                triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            }

            static Vector2 Atlas(float u, float v) => new Vector2(.54f + .44f * u, .54f + .44f * v);
        }
    }
}
