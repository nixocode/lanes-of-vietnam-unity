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
                    switch (c.Kind)
                    {
                        case CoverKind.Sandbag: Bags.Wall(transform, Built, g, w, c.Z - 1.3, e, c.Z - 1.3, 0.95f, false, c.Id); break;
                        case CoverKind.Bunker:
                            Bags.Wall(transform, Built, g, w, c.Z - 1.2, e, c.Z - 1.2, 1.25f, false, c.Id);
                            // Timbers across, and bags on top of them: overhead cover.
                            Box(g, c.X, c.Z - 0.4, (float)c.Length + 0.6f, 0.2f, 2.6f, TimberMaterial, $"bunker roof {c.Id}", lift: 1.3f);
                            Bags.Wall(transform, Built, g, w - 0.2, c.Z + 0.5, e + 0.2, c.Z + 0.5, 0.45f, true, c.Id + 7, lift: 1.48f);
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
