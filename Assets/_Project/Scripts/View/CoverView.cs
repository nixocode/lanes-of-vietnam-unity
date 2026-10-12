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
    /// The walls are sandbags (<see cref="BagSet"/>); without them, and for
    /// the bunker's roof timbers, boxes.
    /// </summary>
    public sealed class CoverView : MonoBehaviour
    {
        public Material SandbagMaterial;
        public Material TimberMaterial;
        /// <summary>The sandbag walls' meshes (sandbag_mesh.py); not set: boxes.</summary>
        public BagSet Bags;
        /// <summary>The timbers of a bunker's roof (firebase_mesh.py); not set: a box.</summary>
        public StructureSet Structures;

        public readonly List<Renderer> Built = new List<Renderer>();

        public void Build(IReadOnlyList<Cover> cover, Ground g)
        {
            foreach (Transform c in transform) Destroy(c.gameObject);
            Built.Clear();
            foreach (var c in cover)
            {
                if (Bags.Ready && c.Kind != CoverKind.Crater && c.Kind != CoverKind.Berm)
                {
                    // The walls as sandbags; the bunker keeps its timber roof.
                    double w = c.X - c.Length / 2, e = c.X + c.Length / 2;
                    // A wall stands behind the men it shelters, as the lens sees them, and behind where
                    // the lane's files walk too: the three files of a lane march in a band 1.9 m either
                    // side of its middle, and a wall drawn 1.3 m behind its own cover stood in that band
                    // (the owner, playtest 6: "soldiers clipping next to the US spawn (sandbags)"; the
                    // third squad into a lane walked the length of the perimeter wall inside it).
                    double wall = System.Math.Min(c.Z - 1.3, LaneOf(c) - WallBehind);
                    switch (c.Kind)
                    {
                        case CoverKind.Sandbag: Bags.Wall(transform, Built, g, w, wall, e, wall, 0.95f, false, c.Id); break;
                        case CoverKind.Bunker:
                            Bags.Wall(transform, Built, g, w, wall, e, wall, 1.45f, false, c.Id);
                            // Timbers across on posts, and bags on top of them: overhead cover a man can stand under.
                            if (Structures.Ready) { Roof(g, c, w, e, wall); break; }
                            float depth = (float)(c.Z + 0.9 - wall) + 0.4f;
                            double mid = (c.Z + 0.9 + wall) * 0.5 - 0.2;
                            Box(g, c.X, mid, (float)c.Length + 0.6f, 0.2f, depth, TimberMaterial, $"bunker roof {c.Id}", lift: 2.05f);
                            foreach (double px in new[] { w - 0.1, e + 0.1 })
                                Box(g, px, c.Z + 0.9, 0.18f, 2.05f, 0.18f, TimberMaterial, $"bunker post {c.Id}", lift: 0.1f);
                            Bags.Wall(transform, Built, g, w - 0.2, c.Z + 0.6, e + 0.2, c.Z + 0.6, 0.45f, true, c.Id + 7, lift: 2.23f);
                            break;
                        case CoverKind.Trench: Bags.Wall(transform, Built, g, w, c.Z - 1.05, e, c.Z - 1.05, 0.45f, true, c.Id); break;
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
        }

        /// <summary>How high a bunker's posts stand, metres: its roof's logs lie on the beams across their tops.</summary>
        public const float RoofHeight = 2.15f;

        /// <summary>
        /// A bunker's roof, built: a row of posts before the men and a row behind them against the wall, a
        /// beam along the top of each row, logs laid side by side across the two beams, and a course of bags
        /// on the logs. It was one cube stretched to the bunker's size, brown, on two square sticks: "the
        /// shed" of the owner's note of 2026-10-12. The roof is level, a man's height and a bit over the
        /// ground the men stand on; each post is as long as the ground under it asks.
        /// </summary>
        private void Roof(Ground g, Cover c, double w, double e, double wall)
        {
            // Over the men, not over the whole of the ground back to the lane's wall: that may stand four
            // metres behind them, and a roof that deep was a barn.
            double front = c.Z + 0.9, back = System.Math.Max(wall + 0.5, c.Z - 1.7), mid = (front + back) * 0.5;
            double x0 = w - 0.1, x1 = e + 0.1;
            float top = (float)g.HeightAt(c.X, c.Z) + RoofHeight;
            int bays = Mathf.Max(1, Mathf.RoundToInt((float)(x1 - x0) / 2.8f));
            for (int k = 0; k <= bays; k++)
            {
                double px = x0 + (x1 - x0) * k / bays;
                foreach (double pz in new[] { front, back })
                {
                    float foot = (float)g.HeightAt(px, pz);
                    Structures.Place(transform, Built, "bunker_post", Coords.World(px, pz, foot), 53f * (k + c.Id) + (pz == front ? 0f : 140f),
                                     new Vector3(1f, (top - foot) / RoofHeight, 1f));
                }
            }
            // The beams and the logs come in runs, laid end to end and drawn out to the bunker's length.
            float length = (float)(x1 - x0) + 0.5f;
            int runs = Mathf.Max(1, Mathf.RoundToInt(length / StructureSet.Run));
            float each = length / runs, deep = (float)(front - back) + 0.6f;
            for (int k = 0; k < runs; k++)
            {
                double rx = (x0 + x1) * 0.5 - length * 0.5 + each * (k + 0.5);
                bool turned = (k + c.Id) % 2 == 1;
                foreach (double bz in new[] { front, back })
                    Structures.Place(transform, Built, "bunker_beam", Coords.World(rx, bz, top - 0.05f), turned ? 180f : 0f, new Vector3(each / StructureSet.Run, 1f, 1f));
                Structures.Place(transform, Built, "bunker_roof", Coords.World(rx, mid, top + 0.04f), turned ? 180f : 0f,
                                 new Vector3(each / StructureSet.Run, 1f, deep / StructureSet.LogDepth));
            }
            // Bags on the logs, along the front and a second row behind it.
            Bags.Wall(transform, Built, g, w - 0.2, front - 0.15, e + 0.2, front - 0.15, 0.45f, true, c.Id + 7, level: top + 0.2f);
            Bags.Wall(transform, Built, g, w + 0.1, mid - 0.1, e - 0.1, mid - 0.1, 0.45f, true, c.Id + 11, level: top + 0.2f);
        }

        /// <summary>How far behind a lane's middle its walls are drawn: clear of the files, which reach 1.9 m from it.</summary>
        public const double WallBehind = 2.75;

        /// <summary>The middle of the lane a piece of cover is in.</summary>
        private static double LaneOf(Cover c)
        {
            double best = Tune.Lanes[0];
            foreach (double z in Tune.Lanes) if (System.Math.Abs(z - c.Z) < System.Math.Abs(best - c.Z)) best = z;
            return best;
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
