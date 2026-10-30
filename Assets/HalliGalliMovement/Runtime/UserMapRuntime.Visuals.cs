using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class UserMapRuntime
    {
        // Values are shared with the resource shader. One material per finish, not per part.
        enum Finish { Concrete, Steel, Moving, Conveyor, Hazard, Ramp, Surf, Plinth, Waypoint, Press, Rubber, Lamp, Checkpoint }
        static readonly int AccentId = Shader.PropertyToID("_AccentColor");
        static readonly int SizeId = Shader.PropertyToID("_PartSize");
        static readonly int BeltSpeedId = Shader.PropertyToID("_BeltSpeed");
        static readonly int BeltTimeId = Shader.PropertyToID("_BeltTime");
        static readonly int DirectionId = Shader.PropertyToID("_TravelDirection");
        Material[] finishes;
        MaterialPropertyBlock surfaceProperties;
        Texture2D surfaceGrain;
        Mesh boxMesh, lampMesh;
        Mesh itemBoxMesh;
        Material itemBoxSteel, itemBoxDark;
        Material[][] itemBoxPalettes;

        void EnsureCraftedResources()
        {
            // The serialized instanced material retains both shader and instancing variants in players.
            var template = Resources.Load<Material>("UserMapSurfaceTemplate");
            if (!template || !template.shader) throw new InvalidOperationException("The user-map surface material resource is missing.");
            surfaceGrain = MakeSurfaceGrain();
            surfaceProperties = new MaterialPropertyBlock();
            finishes = new Material[(int)Finish.Checkpoint + 1];
            for (int i = 0; i < finishes.Length; i++) {
                var finish = (Finish)i;
                var material = new Material(template) { name = "User map / " + finish, hideFlags = HideFlags.DontSave, enableInstancing = true };
                material.SetFloat("_Finish", i);
                material.SetTexture("_GrainTex", surfaceGrain);
                material.color = finish == Finish.Rubber || finish == Finish.Conveyor ? new Color(.19f, .23f, .25f)
                    : finish == Finish.Steel || finish == Finish.Press || finish == Finish.Plinth ? new Color(.36f, .42f, .46f)
                    : finish == Finish.Surf ? new Color(.66f, .72f, .73f) : new Color(.62f, .65f, .63f);
                material.SetFloat("_Metallic", finish == Finish.Surf || finish == Finish.Steel || finish == Finish.Press || finish == Finish.Plinth ? .55f : .12f);
                material.SetFloat("_Glossiness", finish == Finish.Surf ? .48f : finish == Finish.Concrete || finish == Finish.Checkpoint ? .19f : .32f);
                finishes[i] = material;
            }
            boxMesh = MeshFrom("User manufactured box", new[] {
                new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f), new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
                new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f), new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f)
            }, new[] { 0,3,2, 0,2,1, 4,5,6, 4,6,7, 0,4,7, 0,7,3, 1,2,6, 1,6,5, 3,7,6, 3,6,2, 0,1,5, 0,5,4 });
            lampMesh = MakeLampMesh();
        }

        void ApplyFinish(Renderer renderer, Finish finish, int accent, float beltSpeed = 0, Vector2 direction = default)
        {
            renderer.sharedMaterial = finishes[(int)finish];
            surfaceProperties.Clear();
            surfaceProperties.SetColor(AccentId, PaletteColors[accent]);
            var size = renderer.transform.lossyScale;
            surfaceProperties.SetVector(SizeId, new Vector4(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z), 0));
            surfaceProperties.SetFloat(BeltSpeedId, beltSpeed);
            if (direction.sqrMagnitude < .0001f) direction = Vector2.up;
            else direction.Normalize();
            surfaceProperties.SetVector(DirectionId, new Vector4(direction.x, direction.y, 0, 0));
            renderer.SetPropertyBlock(surfaceProperties);
        }

        Transform MakeItemBox(Transform root, UserMapObject item)
        {
            if (!itemBoxMesh) {
                itemBoxMesh = MakeItemBoxMesh();
                itemBoxSteel = MakeItemBoxMaterial("Item crate steel", new Color(.42f, .5f, .56f), .65f, false);
                itemBoxDark = MakeItemBoxMaterial("Item crate panels", new Color(.045f, .065f, .08f), .2f, false);
                itemBoxPalettes = new Material[PaletteColors.Length][];
            }
            if (itemBoxPalettes[item.color] == null)
                itemBoxPalettes[item.color] = new[] { itemBoxSteel, itemBoxDark, MakeItemBoxMaterial("Item crate accent " + item.color, PaletteColors[item.color], .3f, true) };
            // Same metal core, dark question panels and colored trim as the official race crate.
            // The mesh and materials are shared across all boxes; no gameplay or selection collider.
            var crate = new GameObject("Question crate", typeof(MeshFilter), typeof(MeshRenderer)).transform;
            crate.SetParent(root, false);
            crate.localPosition = Vector3.up * UserMapObject.ItemBoxCenterHeight;
            crate.localRotation = Quaternion.Euler(0, 18, 0);
            crate.GetComponent<MeshFilter>().sharedMesh = itemBoxMesh;
            crate.GetComponent<MeshRenderer>().sharedMaterials = itemBoxPalettes[item.color];
            return crate;
        }

        static Material MakeItemBoxMaterial(string name, Color color, float metallic, bool glow)
        {
            var material = new Material(Shader.Find("Standard")) { name = name, color = color, hideFlags = HideFlags.DontSave, enableInstancing = true };
            material.SetFloat("_Metallic", metallic); material.SetFloat("_Glossiness", .45f);
            if (glow) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * .45f); }
            return material;
        }

        Mesh MakeItemBoxMesh()
        {
            var vertices = new List<Vector3>(2048);
            var normals = new List<Vector3>(2048);
            var uv = new List<Vector2>(2048);
            var triangles = new[] { new List<int>(36), new List<int>(72), new List<int>(2048) };
            var sourceVertices = boxMesh.vertices; var sourceNormals = boxMesh.normals;
            var sourceUV = boxMesh.uv; var sourceTriangles = boxMesh.triangles;
            void AddBox(int material, Vector3 position, Vector3 size)
            {
                int start = vertices.Count;
                for (int i = 0; i < sourceVertices.Length; i++) {
                    vertices.Add(position + Vector3.Scale(sourceVertices[i], size));
                    normals.Add(sourceNormals[i]); uv.Add(sourceUV[i]);
                }
                foreach (int index in sourceTriangles) triangles[material].Add(start + index);
            }
            AddBox(0, Vector3.zero, Vector3.one * .76f);
            // A mesh question mark remains legible without runtime font/atlas dependencies.
            string[] glyph = { "01110", "11011", "00011", "00110", "00100", "00000", "00100" };
            for (int side = -1; side <= 1; side += 2) {
                AddBox(1, new Vector3(0, 0, side * .39f), new Vector3(.64f, .64f, .025f));
                for (int corner = -1; corner <= 1; corner += 2) {
                    AddBox(2, new Vector3(0, corner * .4f, side * .4f), new Vector3(.87f, .09f, .09f));
                    AddBox(2, new Vector3(corner * .4f, 0, side * .4f), new Vector3(.09f, .87f, .09f));
                    AddBox(2, new Vector3(corner * .4f, side * .4f, 0), new Vector3(.09f, .09f, .87f));
                }
                for (int y = 0; y < glyph.Length; y++) for (int x = 0; x < glyph[y].Length;) {
                    if (glyph[y][x] == '0') { x++; continue; }
                    int start = x;
                    while (x < glyph[y].Length && glyph[y][x] == '1') x++;
                    float center = (start + (x - start - 1) * .5f - 2) * .072f;
                    AddBox(2, new Vector3(-side * center, (3 - y) * .072f, side * .415f), new Vector3((x - start) * .072f, .072f, .02f));
                }
            }
            var mesh = new Mesh { name = "Shared question crate", hideFlags = HideFlags.DontSave, subMeshCount = triangles.Length };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv);
            for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        void MakePlatformVisual(Transform root, UserMapObject item)
        {
            var finish = item.kind == UserMapPart.Conveyor ? Finish.Conveyor
                : item.kind == UserMapPart.MovingPlatform ? Finish.Moving
                : item.kind == UserMapPart.Hazard ? Finish.Hazard
                : item.kind == UserMapPart.Checkpoint ? Finish.Checkpoint : Finish.Concrete;
            float deckHeight = Mathf.Min(.14f, item.scale.y * .24f);
            float heelHeight = Mathf.Min(.065f, item.scale.y * .15f);
            float bodyHeight = item.scale.y - deckHeight - heelHeight;
            // These three volumes exactly fill the old cube. No raised trim or hidden corner collisions.
            Box(root, "Rubber isolation heel", new Vector3(0, (heelHeight - item.scale.y) * .5f, 0),
                new Vector3(item.scale.x, heelHeight, item.scale.z), Finish.Rubber, item.color, false);
            Box(root, "Cast body and recessed service panels", new Vector3(0, (heelHeight - deckHeight) * .5f, 0),
                new Vector3(item.scale.x, bodyHeight, item.scale.z), finish == Finish.Conveyor ? Finish.Conveyor
                    : finish == Finish.Concrete || finish == Finish.Checkpoint ? Finish.Concrete : Finish.Steel, item.color, false);
            var travel = root.InverseTransformDirection(item.travel);
            Box(root, "Flush " + finish + " deck", new Vector3(0, (item.scale.y - deckHeight) * .5f, 0),
                new Vector3(item.scale.x, deckHeight, item.scale.z), finish, item.color, false, item.speed, new Vector2(travel.x, travel.z));
        }

        Transform LayeredSolidBox(Transform parent, string name, Vector3 position, Vector3 scale, Finish finish, int accent)
        {
            var solid = new GameObject(name, typeof(BoxCollider)).transform;
            solid.SetParent(parent, false); solid.localPosition = position; solid.localScale = scale;
            solid.GetComponent<BoxCollider>().size = Vector3.one;
            Box(solid, "Machined top cap", new Vector3(0, .43f, 0), new Vector3(1, .14f, 1), Finish.Steel, accent, false);
            Box(solid, "Recessed cast housing", new Vector3(0, -.02f, 0), new Vector3(1, .76f, 1), finish, accent, false);
            Box(solid, finish == Finish.Press ? "Steel crushing shoe" : "Rubber plinth foot", new Vector3(0, -.45f, 0),
                new Vector3(1, .1f, 1), finish == Finish.Press ? Finish.Steel : Finish.Rubber, accent, false);
            return solid;
        }

        Renderer MakePressLamp(Transform root, Vector3 position)
        {
            var housing = new GameObject("Press signal bezel", typeof(MeshFilter), typeof(MeshRenderer)).transform;
            housing.SetParent(root, false); housing.localPosition = position; housing.localScale = new Vector3(.18f, .3f, .1f);
            housing.GetComponent<MeshFilter>().sharedMesh = lampMesh;
            ApplyFinish(housing.GetComponent<MeshRenderer>(), Finish.Steel, 7);
            var lens = new GameObject("Press warning", typeof(MeshFilter), typeof(MeshRenderer)).transform;
            lens.SetParent(root, false); lens.localPosition = position + new Vector3(0, 0, -.0375f); lens.localScale = new Vector3(.13f, .24f, .025f);
            lens.GetComponent<MeshFilter>().sharedMesh = lampMesh;
            var renderer = lens.GetComponent<MeshRenderer>();
            ApplyFinish(renderer, Finish.Lamp, 4);
            return renderer;
        }

        static Mesh MakeLampMesh()
        {
            const int sides = 16;
            var corners = new Vector3[sides * 2 + 2];
            var indices = new int[sides * 12];
            corners[sides * 2] = Vector3.back * .5f; corners[sides * 2 + 1] = Vector3.forward * .5f;
            for (int i = 0; i < sides; i++) {
                float angle = i * Mathf.PI * 2 / sides;
                corners[i] = new Vector3(Mathf.Cos(angle) * .5f, Mathf.Sin(angle) * .5f, -.5f);
                corners[i + sides] = new Vector3(corners[i].x, corners[i].y, .5f);
                int next = (i + 1) % sides, at = i * 12;
                indices[at] = sides * 2; indices[at + 1] = next; indices[at + 2] = i;
                indices[at + 3] = sides * 2 + 1; indices[at + 4] = i + sides; indices[at + 5] = next + sides;
                indices[at + 6] = i; indices[at + 7] = next; indices[at + 8] = next + sides;
                indices[at + 9] = i; indices[at + 10] = next + sides; indices[at + 11] = i + sides;
            }
            return MeshFrom("User press signal lens", corners, indices);
        }

        static Texture2D MakeSurfaceGrain()
        {
            const int side = 128;
            var pixels = new Color32[side * side];
            uint random = 0x6d2b79f5;
            for (int i = 0; i < pixels.Length; i++) {
                random ^= random << 13; random ^= random >> 17; random ^= random << 5;
                byte grain = (byte)(random & 255);
                byte aggregate = (byte)((random >> 8) & 255);
                byte pore = (byte)(grain < 24 ? 45 : 210 + (aggregate >> 3));
                pixels[i] = new Color32(grain, aggregate, pore, 255);
            }
            var texture = new Texture2D(side, side, TextureFormat.RGBA32, true, true) {
                name = "Original cast grain and rubber pores", hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4
            };
            texture.SetPixels32(pixels); texture.Apply(true, true);
            return texture;
        }

        void LateUpdate()
        {
            // One shared material update; the belt pattern follows signed speed without moving collision.
            if (finishes != null) finishes[(int)Finish.Conveyor].SetFloat(BeltTimeId, editing ? 0 : (float)FactorySurface.Clock);
        }

        void ReleaseCraftedResources()
        {
            if (finishes != null) foreach (var material in finishes) if (material) Destroy(material);
            if (surfaceGrain) Destroy(surfaceGrain);
            if (boxMesh) Destroy(boxMesh);
            if (lampMesh) Destroy(lampMesh);
            if (itemBoxPalettes != null) foreach (var palette in itemBoxPalettes) if (palette != null && palette[2]) Destroy(palette[2]);
            if (itemBoxSteel) Destroy(itemBoxSteel); if (itemBoxDark) Destroy(itemBoxDark);
            if (itemBoxMesh) Destroy(itemBoxMesh);
        }
    }
}
