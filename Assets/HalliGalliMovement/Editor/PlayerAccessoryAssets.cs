using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    // Writes only the sunglasses mesh, its two materials and the Resources prefab.
    // It never edits the model importer, a scene or the player prefabs.
    public static class PlayerAccessoryAssets
    {
        const string ModelPath = "Assets/HalliGalliMovement/Art/Player_Bald.fbx";
        const string ArtFolder = "Assets/HalliGalliMovement/Art/Accessories";
        const string ResourceFolder = "Assets/HalliGalliMovement/Resources/Accessories";
        public const string PrefabPath = ResourceFolder + "/Sunglasses.prefab";
        // Bridge centre in the model's rest pose (metres, model root space; +Z is the face).
        // Measured on the face mesh: eyes near y 1.672, nose bridge surface z 0.133-0.140.
        static readonly Vector3 Bridge = new Vector3(0, 1.677f, .145f);
        const float LensHalfWidth = .027f, LensHalfHeight = .0205f, Rim = .003f, LensOffset = .0345f;
        const float Wrap = 8, Tilt = 6;
        const int Frame = 0, Lens = 1, Segments = 32;

        sealed class Shape
        {
            public readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            public readonly List<int>[] triangles = { new List<int>(), new List<int>() };
            public Matrix4x4 matrix = Matrix4x4.identity;

            // Flat-shaded; the winding is chosen from the stated outward direction.
            public void Face(int submesh, Vector3 outward, params Vector3[] corners)
            {
                int first = vertices.Count;
                var normal = matrix.MultiplyVector(outward).normalized;
                for (int i = 0; i < corners.Length; i++) { vertices.Add(matrix.MultiplyPoint3x4(corners[i])); normals.Add(normal); }
                bool clockwise = Vector3.Dot(Vector3.Cross(vertices[first + 1] - vertices[first], vertices[first + 2] - vertices[first]), normal) > 0;
                for (int i = 1; i + 1 < corners.Length; i++) {
                    triangles[submesh].Add(first);
                    triangles[submesh].Add(first + (clockwise ? i : i + 1));
                    triangles[submesh].Add(first + (clockwise ? i + 1 : i));
                }
            }

            public void Box(int submesh, Vector3 centre, Vector3 size, Quaternion rotation)
            {
                Vector3 h = size * .5f;
                for (int axis = 0; axis < 3; axis++)
                    for (int sign = -1; sign <= 1; sign += 2) {
                        int u = (axis + 1) % 3, v = (axis + 2) % 3;
                        var corners = new Vector3[4];
                        for (int i = 0; i < 4; i++) {
                            var local = Vector3.zero;
                            local[axis] = h[axis] * sign;
                            local[u] = h[u] * (i == 0 || i == 3 ? -1 : 1);
                            local[v] = h[v] * (i < 2 ? -1 : 1);
                            corners[i] = centre + rotation * local;
                        }
                        var outward = Vector3.zero;
                        outward[axis] = sign;
                        Face(submesh, rotation * outward, corners);
                    }
            }

            public void Beam(int submesh, Vector3 from, Vector3 to, float width, float height)
            {
                Box(submesh, (from + to) * .5f, new Vector3(width, height, Vector3.Distance(from, to)), Quaternion.LookRotation(to - from, Vector3.up));
            }

            public void Wall(int submesh, Vector2[] outline, float back, float front, float facing)
            {
                for (int i = 0; i < outline.Length; i++) {
                    Vector2 a = outline[i], b = outline[(i + 1) % outline.Length], edge = b - a;
                    // The outline runs counter-clockwise, so its right-hand side points away from the centre.
                    Face(submesh, new Vector3(edge.y, -edge.x, 0) * facing,
                        new Vector3(a.x, a.y, front), new Vector3(b.x, b.y, front), new Vector3(b.x, b.y, back), new Vector3(a.x, a.y, back));
                }
            }

            public void Cap(int submesh, Vector2[] outline, float z, float facing)
            {
                var corners = new Vector3[outline.Length];
                for (int i = 0; i < outline.Length; i++) corners[i] = new Vector3(outline[i].x, outline[i].y, z);
                Face(submesh, Vector3.forward * facing, corners);
            }

            public void Band(int submesh, Vector2[] outer, Vector2[] inner, float z, float facing)
            {
                for (int i = 0; i < outer.Length; i++) {
                    int next = (i + 1) % outer.Length;
                    Face(submesh, Vector3.forward * facing, new Vector3(outer[i].x, outer[i].y, z), new Vector3(outer[next].x, outer[next].y, z),
                        new Vector3(inner[next].x, inner[next].y, z), new Vector3(inner[i].x, inner[i].y, z));
                }
            }
        }

        // A soft rectangle that is wider at the brow and cut away beside the nose.
        static Vector2[] Outline(float halfWidth, float halfHeight, float nose)
        {
            var points = new Vector2[Segments];
            for (int i = 0; i < Segments; i++) {
                float angle = i * Mathf.PI * 2 / Segments, c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                float x = halfWidth * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2 / 2.6f);
                float y = halfHeight * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2 / 2.6f);
                x *= 1 + .1f * y / halfHeight;
                if (x * nose > 0 && y < 0) x *= 1 + .35f * y / halfHeight;
                points[i] = new Vector2(x, y);
            }
            return points;
        }

        static Mesh BuildSunglasses()
        {
            var shape = new Shape();
            foreach (int side in new[] { -1, 1 }) {
                shape.matrix = Matrix4x4.TRS(new Vector3(side * LensOffset, -.003f, -.004f), Quaternion.Euler(Tilt, side * Wrap, 0), Vector3.one);
                var inner = Outline(LensHalfWidth, LensHalfHeight, -side);
                var outer = Outline(LensHalfWidth + Rim, LensHalfHeight + Rim, -side);
                shape.Band(Frame, outer, inner, .0025f, 1);
                shape.Band(Frame, outer, inner, -.0025f, -1);
                shape.Wall(Frame, outer, -.0025f, .0025f, 1);
                shape.Wall(Frame, inner, -.0025f, .0025f, -1);
                shape.Cap(Lens, inner, .0012f, 1);
                shape.Cap(Lens, inner, -.0012f, -1);
                Vector3 hinge = shape.matrix.MultiplyPoint3x4(new Vector3(side * (LensHalfWidth + Rim), .009f, -.001f));
                Vector3 ear = new Vector3(side * .083f, .004f, -.132f), tip = new Vector3(side * .082f, -.02f, -.156f);
                shape.matrix = Matrix4x4.identity;
                shape.Box(Frame, hinge, new Vector3(.005f, .009f, .007f), Quaternion.Euler(0, side * Wrap, 0));
                shape.Beam(Frame, hinge, ear, .0026f, .0065f);
                shape.Beam(Frame, ear, tip, .0026f, .005f);
            }
            shape.matrix = Matrix4x4.identity;
            shape.Box(Frame, new Vector3(0, .004f, -.0005f), new Vector3(.022f, .0045f, .0035f), Quaternion.identity);
            var mesh = new Mesh { name = "Sunglasses", subMeshCount = 2 };
            mesh.SetVertices(shape.vertices);
            mesh.SetNormals(shape.normals);
            mesh.SetTriangles(shape.triangles[Frame], Frame);
            mesh.SetTriangles(shape.triangles[Lens], Lens);
            mesh.RecalculateBounds();
            return mesh;
        }

        static T Store<T>(T fresh, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!existing) { AssetDatabase.CreateAsset(fresh, path); return fresh; }
            // Keep the GUID so the prefab keeps its references.
            EditorUtility.CopySerialized(fresh, existing);
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static Material Surface(string name, Color color, float metallic, float smoothness)
        {
            var material = new Material(Shader.Find("Standard")) { name = name, color = color };
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
            return Store(material, ArtFolder + "/" + name + ".mat");
        }

        [MenuItem("HalliGalli/Install Sunglasses Accessory")]
        public static void Install()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Transform head = null;
            foreach (var bone in model.GetComponentsInChildren<Transform>(true))
                if (bone.name == "Head") head = bone;
            if (!head) throw new System.InvalidOperationException("Player model has no Head bone.");
            Directory.CreateDirectory(ArtFolder);
            Directory.CreateDirectory(ResourceFolder);
            AssetDatabase.Refresh();
            var mesh = Store(BuildSunglasses(), ArtFolder + "/Sunglasses.asset");
            var frame = Surface("SunglassesFrame", new Color(.025f, .025f, .03f), 0, .6f);
            var lens = Surface("SunglassesLens", new Color(.03f, .045f, .06f), .65f, .93f);
            var item = new GameObject("Sunglasses");
            try {
                item.AddComponent<MeshFilter>().sharedMesh = mesh;
                item.AddComponent<MeshRenderer>().sharedMaterials = new[] { frame, lens };
                // Baked for direct parenting under the Head bone, whatever scale the rig carries.
                var root = model.transform;
                item.transform.localPosition = head.InverseTransformPoint(root.TransformPoint(Bridge));
                item.transform.localRotation = Quaternion.Inverse(head.rotation) * root.rotation;
                item.transform.localScale = Vector3.one * (root.lossyScale.x / head.lossyScale.x);
                PrefabUtility.SaveAsPrefabAsset(item, PrefabPath);
            } finally { Object.DestroyImmediate(item); }
            AssetDatabase.SaveAssets();
            Debug.Log("SUNGLASSES_INSTALLED vertices=" + mesh.vertexCount + " prefab=" + PrefabPath);
        }
    }
}
