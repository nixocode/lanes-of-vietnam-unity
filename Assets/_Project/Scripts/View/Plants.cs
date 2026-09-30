using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// One baked species (tools/blender/plant_bake.py): its atlas material and,
    /// per variant, where it sits in the atlas, how big it is and where its root is.
    /// </summary>
    [Serializable]
    public struct PlantSet
    {
        public string Name;
        public TextAsset Layout;
        public Material Material;
    }

    public sealed class PlantSpecies
    {
        [Serializable] private class V { public string key; public float[] rect; public float[] size_m; public float[] root; public float height_m; }
        [Serializable] private class L { public string species; public float pitch_deg; public V[] variants; }

        public readonly string Name;
        public readonly Material Material;
        public readonly Variant[] Variants;

        public struct Variant
        {
            public Rect Uv;              // in the atlas, 0..1 from the bottom-left
            public Vector2 Size;         // metres, at the scale it was baked
            public Vector2 Root;         // where the root is, 0..1 of the image
            public float Height;         // metres from the root to the top
        }

        public PlantSpecies(PlantSet set)
        {
            Name = set.Name;
            Material = set.Material;
            var l = JsonUtility.FromJson<L>(set.Layout.text);
            Variants = new Variant[l.variants.Length];
            for (int i = 0; i < l.variants.Length; i++)
            {
                var v = l.variants[i];
                Variants[i] = new Variant
                {
                    Uv = Rect.MinMaxRect(v.rect[0], v.rect[1], v.rect[2], v.rect[3]),
                    Size = new Vector2(v.size_m[0], v.size_m[1]),
                    Root = new Vector2(v.root[0], v.root[1]),
                    Height = v.height_m,
                };
            }
        }
    }

    /// <summary>
    /// Collects plants and builds them as upright quads, merged into one mesh
    /// per species per stretch of the map. The camera never rotates, so a quad
    /// facing it is always facing it: no billboard maths per frame, and a few
    /// thousand plants cost a handful of draw calls.
    /// </summary>
    public sealed class PlantBatch
    {
        private const float ChunkWidth = 34f;

        private readonly Dictionary<(PlantSpecies, int), Quads> _quads = new Dictionary<(PlantSpecies, int), Quads>();
        public int Count { get; private set; }

        private sealed class Quads
        {
            public readonly List<Vector3> Pos = new List<Vector3>();
            public readonly List<Vector2> Uv = new List<Vector2>();
            public readonly List<Vector2> Plant = new List<Vector2>();
            public readonly List<Color32> Col = new List<Color32>();
        }

        /// <summary>
        /// Add a plant with its root at a world position. Scale multiplies the
        /// size it was baked at; mirrored flips it left to right, which doubles
        /// the variants for free.
        /// </summary>
        public void Add(PlantSpecies s, int variant, Vector3 root, float scale, bool mirror, Color tint, float phase)
        {
            var v = s.Variants[variant % s.Variants.Length];
            int chunk = Mathf.FloorToInt(root.x / ChunkWidth);
            if (!_quads.TryGetValue((s, chunk), out var q)) _quads[(s, chunk)] = q = new Quads();

            float w = v.Size.x * scale, h = v.Size.y * scale;
            float x0 = -v.Root.x * w, x1 = (1 - v.Root.x) * w;
            float y0 = -v.Root.y * h, y1 = (1 - v.Root.y) * h;
            if (mirror) { float t = x0; x0 = -x1; x1 = -t; }
            float u0 = mirror ? v.Uv.xMax : v.Uv.xMin, u1 = mirror ? v.Uv.xMin : v.Uv.xMax;
            float top = Mathf.Max(0.01f, y1);
            var c = new Color32((byte)(Mathf.Clamp01(tint.r) * 255), (byte)(Mathf.Clamp01(tint.g) * 255),
                                (byte)(Mathf.Clamp01(tint.b) * 255), (byte)(Mathf.Repeat(phase, 1f) * 255));
            float m = mirror ? -1 : 1;
            void Vtx(float x, float y, float u, float vv)
            {
                q.Pos.Add(root + new Vector3(x, y, 0));
                q.Uv.Add(new Vector2(u, vv));
                q.Plant.Add(new Vector2(Mathf.Max(0, y) / top, m));
                q.Col.Add(c);
            }
            Vtx(x0, y0, u0, v.Uv.yMin);
            Vtx(x1, y0, u1, v.Uv.yMin);
            Vtx(x1, y1, u1, v.Uv.yMax);
            Vtx(x0, y1, u0, v.Uv.yMax);
            Count++;
        }

        /// <summary>Build the meshes under a parent. Returns the renderers.</summary>
        public List<Renderer> Build(Transform parent)
        {
            var built = new List<Renderer>();
            foreach (var kv in _quads)
            {
                var q = kv.Value;
                int n = q.Pos.Count / 4;
                var idx = new int[n * 6];
                for (int i = 0; i < n; i++)
                {
                    int b = i * 4;
                    // Facing -z (the camera), in Unity's clockwise-front convention.
                    idx[i * 6 + 0] = b; idx[i * 6 + 1] = b + 3; idx[i * 6 + 2] = b + 2;
                    idx[i * 6 + 3] = b; idx[i * 6 + 4] = b + 2; idx[i * 6 + 5] = b + 1;
                }
                var mesh = new Mesh { name = $"plants {kv.Key.Item1.Name} {kv.Key.Item2}" };
                mesh.indexFormat = q.Pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(q.Pos);
                mesh.SetUVs(0, q.Uv);
                mesh.SetUVs(1, q.Plant);
                mesh.SetColors(q.Col);
                mesh.SetTriangles(idx, 0);
                mesh.RecalculateBounds();
                var bnd = mesh.bounds;
                bnd.Expand(new Vector3(1f, 0, 0));          // room for the sway
                mesh.bounds = bnd;
                mesh.UploadMeshData(true);
                var go = new GameObject(mesh.name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = kv.Key.Item1.Material;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
                built.Add(mr);
            }
            return built;
        }
    }
}
