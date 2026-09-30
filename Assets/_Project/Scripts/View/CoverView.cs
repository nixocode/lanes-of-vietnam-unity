using System.Collections.Generic;
using LanesOfVietnam.Sim;
using UnityEngine;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// The cover, built from the same list the simulation shelters men with.
    ///
    /// PLAN §3.3: what you see must be what shelters you. In the three.js
    /// build the simulation generated sixteen to twenty-two pieces of cover a
    /// match and none was ever drawn. Craters, banks and trenches are in the
    /// ground itself (<see cref="Map"/> hands them to <see cref="Ground"/>);
    /// what is built here is what stands on it: sandbag walls, the bunker, and
    /// the parapet along each trench.
    ///
    /// Grey-box: boxes. The art pass swaps in real sandbag and timber pieces
    /// and keeps the placement.
    /// </summary>
    public sealed class CoverView : MonoBehaviour
    {
        public Material SandbagMaterial;
        public Material TimberMaterial;
        /// <summary>Baked sandbag wall segments (plant_bake.py "sandbags"); empty: boxes.</summary>
        public PlantSet Sandbags;

        public readonly List<Renderer> Built = new List<Renderer>();

        private PlantSpecies _bags;
        private PlantBatch _batch;

        /// <summary>
        /// A sandbag wall face-on to the lens, as baked segments repeated along
        /// it (the camera never rotates, so the face is all it ever sees).
        /// Segments are about 3 m wide and overlap a little; full height for
        /// walls, the low ones for parapets.
        /// </summary>
        public static void BagRun(PlantBatch batch, PlantSpecies bags, Ground g, double cx, double cz, double length,
                                  float height, bool low, int seed, float lift = 0)
        {
            int first = low ? 3 : 0, count = low ? 2 : 3;
            int n = Mathf.Max(1, Mathf.CeilToInt((float)length / 2.8f));
            for (int k = 0; k < n; k++)
            {
                double x = cx - length / 2 + (k + 0.5) * length / n;
                int vi = first + Mathf.Abs(seed * 31 + k * 7) % count;
                float scale = height / Mathf.Max(0.1f, bags.Variants[vi].Height);
                var root = Coords.World(x, cz, (float)g.HeightAt(x, cz) - 0.04f + lift);
                batch.Add(bags, vi, root, scale, (seed + k) % 2 == 0, Color.white, 0);
            }
        }

        public void Build(IReadOnlyList<Cover> cover, Ground g)
        {
            foreach (Transform c in transform) Destroy(c.gameObject);
            Built.Clear();
            _bags = Sandbags.Layout != null && Sandbags.Material != null ? new PlantSpecies(Sandbags) : null;
            _batch = new PlantBatch();
            foreach (var c in cover)
            {
                if (_bags != null && c.Kind != CoverKind.Crater && c.Kind != CoverKind.Berm)
                {
                    // The walls as sandbags; the bunker keeps its timber roof.
                    switch (c.Kind)
                    {
                        case CoverKind.Sandbag: BagRun(_batch, _bags, g, c.X, c.Z - 1.3, c.Length, 0.95f, false, c.Id); break;
                        case CoverKind.Bunker:
                            BagRun(_batch, _bags, g, c.X, c.Z - 1.2, c.Length, 1.25f, false, c.Id);
                            // Timbers across, and bags on top of them: overhead cover.
                            Box(g, c.X, c.Z - 0.4, (float)c.Length + 0.6f, 0.2f, 2.6f, TimberMaterial, $"bunker roof {c.Id}", lift: 1.3f);
                            BagRun(_batch, _bags, g, c.X, c.Z + 0.5, c.Length + 0.4, 0.45f, true, c.Id + 7, lift: 1.48f);
                            break;
                        case CoverKind.Trench: BagRun(_batch, _bags, g, c.X, c.Z - 1.05, c.Length, 0.45f, true, c.Id); break;
                    }
                    continue;
                }
                switch (c.Kind)
                {
                    case CoverKind.Sandbag:
                        // The wall on the far side of the position: men crouch in
                        // front of it, where the camera can see them.
                        Box(g, c.X, c.Z - 1.3, (float)c.Length, 0.95f, 0.75f, SandbagMaterial, $"sandbags {c.Id}");
                        Box(g, c.X - c.Length * 0.5, c.Z - 0.4, 0.7f, 0.8f, 1.8f, SandbagMaterial, $"sandbag return {c.Id}");
                        Box(g, c.X + c.Length * 0.5, c.Z - 0.4, 0.7f, 0.8f, 1.8f, SandbagMaterial, $"sandbag return {c.Id}");
                        break;
                    case CoverKind.Bunker:
                        Box(g, c.X, c.Z - 1.2, (float)c.Length, 1.25f, 1.1f, SandbagMaterial, $"bunker wall {c.Id}");
                        Box(g, c.X, c.Z - 0.4, (float)c.Length + 0.6f, 0.35f, 2.6f, TimberMaterial, $"bunker roof {c.Id}", lift: 1.45f);
                        break;
                    case CoverKind.Trench:
                        Box(g, c.X, c.Z - 1.05, (float)c.Length, 0.45f, 0.6f, SandbagMaterial, $"trench parapet {c.Id}");
                        break;
                }
            }
            if (_bags != null) Built.AddRange(_batch.Build(transform));
        }

        private void Box(Ground g, double simX, double simZ, float length, float height, float depth,
                         Material mat, string name, float lift = 0)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(transform, false);
            float y = (float)g.HeightAt(simX, simZ);
            go.transform.position = Coords.World(simX, simZ, y + lift + height * 0.5f - 0.1f);
            go.transform.localScale = new Vector3(length, height, depth);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            Built.Add(r);
        }
    }
}
