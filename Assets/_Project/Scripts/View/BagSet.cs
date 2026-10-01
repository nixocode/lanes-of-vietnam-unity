using System.Collections.Generic;
using LanesOfVietnam.Sim;
using UnityEngine;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// Sandbag walls as geometry (tools/blender/sandbag_mesh.py): 2.4 m runs of
    /// bags laid in running bond, two tall and two low, on one weathered atlas.
    ///
    /// They were baked cards facing the lens (plant_bake.py). Where two cards
    /// overlapped at one depth they fought for the pixel, which was the flicker
    /// three playtests reported, and a card cannot run away from the lens, so
    /// the firebase's east wall was drawn face-on. A wall of bags has no two
    /// surfaces in one place and runs whichever way it is laid.
    /// </summary>
    [System.Serializable]
    public struct BagSet
    {
        /// <summary>Metres a mesh runs along its x.</summary>
        public const float Run = 2.4f;

        public Mesh[] Tall;
        public Mesh[] Low;
        public Material Material;

        public bool Ready => Material != null && Tall != null && Tall.Length > 0 && Low != null && Low.Length > 0;

        /// <summary>
        /// A wall from one point of the map to another, as runs laid end to
        /// end on the ground between them: each takes the slope under it, is
        /// drawn to the wall's height, and every other one is turned about so
        /// that no two neighbours are the same bags.
        /// </summary>
        public void Wall(Transform parent, List<Renderer> built, Ground g, double x0, double z0, double x1, double z1,
                         float height, bool low, int seed, float lift = 0)
        {
            var set = low ? Low : Tall;
            double length = System.Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
            int n = Mathf.Max(1, Mathf.RoundToInt((float)length / Run));
            for (int k = 0; k < n; k++)
            {
                double ax = x0 + (x1 - x0) * k / n, az = z0 + (z1 - z0) * k / n;
                double bx = x0 + (x1 - x0) * (k + 1) / n, bz = z0 + (z1 - z0) * (k + 1) / n;
                var a = Coords.World(ax, az, (float)g.HeightAt(ax, az));
                var b = Coords.World(bx, bz, (float)g.HeightAt(bx, bz));
                if ((seed + k) % 2 != 0) (a, b) = (b, a);
                var along = b - a;
                var mesh = set[Mathf.Abs(seed * 31 + k * 7) % set.Length];
                var across = Vector3.Cross(along.normalized, Vector3.up).normalized;

                var go = new GameObject("sandbags");
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation((a + b) * 0.5f + Vector3.up * (lift - 0.04f),
                                                    Quaternion.LookRotation(across, Vector3.Cross(across, along)));
                go.transform.localScale = new Vector3(along.magnitude / Run, height / Mathf.Max(0.1f, mesh.bounds.max.y), 1f);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = Material;
                built.Add(r);
            }
        }
    }
}
