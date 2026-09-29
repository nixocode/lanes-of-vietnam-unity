using System.Collections.Generic;
using LanesOfVietnam.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// What bounds the lanes, so a player can point at the screen and say which
    /// lane a man is in (PLAN §3.2). In the three.js build the two lanes were a
    /// z value and nothing else.
    ///
    /// <list type="bullet">
    /// <item>The <b>near lane</b> is the firebase's ground: its revetment line
    /// runs behind it and the wire runs in front of it along the bank, with
    /// gaps where the bank is cut — the crossings.</item>
    /// <item>The <b>track</b> is bare dirt (the ground shader's own mask).</item>
    /// <item>The <b>far lane</b> ends at the treeline's edge: clumps, never a
    /// wall — the three.js build's most repeated mistake.</item>
    /// <item>Beyond: ridges, hazed by distance.</item>
    /// </list>
    ///
    /// Grey-box: boxes and cylinders in flat colours. The art pass replaces
    /// every piece and keeps the placement rules, which are the point.
    /// Placement is seeded (the view's own fork), so the dressing is the same
    /// on every load and never moves the simulation's stream.
    /// </summary>
    public sealed class WorldDressing : MonoBehaviour
    {
        public Material Foliage;
        public Material FoliageDark;
        public Material Mountain;
        public Material Wire;
        public Material Timber;
        public Material Sandbag;
        public Material Vehicle;

        /// <summary>
        /// The treeline begins here (sim z), 68 m from the lens. Derived from
        /// TARGET.jpg rather than chosen: its treeline band tops out about 3
        /// degrees above the horizon, and atan((H - 5.1) / D) = 3 degrees puts
        /// 10-12 m crowns at 80-120 m. At -13.5 the first version filled the
        /// frame with trees, because the whole frame is 19 m tall at 57 m.
        /// </summary>
        public const double TreelineZ = -24;

        /// <summary>Scrub between the far lane and the treeline: bushes and grass, knee to head high.</summary>
        public const double ScrubNearZ = -10.5, ScrubFarZ = -22;

        /// <summary>The wire runs along the bank between the near lane and the track.</summary>
        public const double WireZ = 3.9;

        /// <summary>The firebase compound's east edge (sim x): it holds the west end of the map.</summary>
        public const double FirebaseEastX = -21;

        private readonly List<Renderer> _built = new List<Renderer>();
        public IReadOnlyList<Renderer> Built => _built;

        public void Build(Ground g, IReadOnlyList<Cover> cover, int seed)
        {
            foreach (Transform c in transform) Destroy(c.gameObject);
            _built.Clear();
            var rng = new Rng(seed).Fork("dressing");
            Treeline(g, cover, rng.Fork("treeline"));
            Scrub(g, cover, rng.Fork("scrub"));
            WireLine(g, rng.Fork("wire"));
            Firebase(g);
            Ridges(rng.Fork("ridges"));
            CombineByMaterial();
        }

        /// <summary>
        /// Merge every piece into one mesh per material. Hundreds of grey-box
        /// primitives are hundreds of draw calls, and PLAN §2 allows 400 for the
        /// whole frame; merged, the dressing costs one per material.
        /// </summary>
        private void CombineByMaterial()
        {
            var groups = new Dictionary<Material, List<CombineInstance>>();
            var shadowless = new HashSet<Material>();
            foreach (var r in _built)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (!groups.TryGetValue(r.sharedMaterial, out var list)) groups[r.sharedMaterial] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mf.sharedMesh, transform = r.transform.localToWorldMatrix });
                if (r.shadowCastingMode == ShadowCastingMode.Off) shadowless.Add(r.sharedMaterial);
            }
            foreach (var r in _built) Destroy(r.gameObject);
            _built.Clear();
            foreach (var kv in groups)
            {
                var mesh = new Mesh { name = $"dressing {kv.Key.name}", indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                var go = new GameObject(mesh.name);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = kv.Key;
                mr.shadowCastingMode = shadowless.Contains(kv.Key) ? ShadowCastingMode.Off : ShadowCastingMode.On;
                _built.Add(mr);
            }
        }

        private static bool NearCover(IReadOnlyList<Cover> cover, double x, double z, double margin)
        {
            foreach (var c in cover)
            {
                if (System.Math.Abs(x - c.X) < c.Length * 0.5 + margin && System.Math.Abs(z - c.Z) < 2.5 + margin) return true;
            }
            return false;
        }

        /// <summary>
        /// Clumps, in depth: a ragged front edge of shrubs and bamboo, then
        /// crowns of mixed height behind it, thinning to a dark mass. Gaps are
        /// left on purpose so it never reads as a hedge.
        /// </summary>
        private void Treeline(Ground g, IReadOnlyList<Cover> cover, Rng rng)
        {
            for (double x = -150; x < 150; x += rng.Range(2.5, 5.5))
            {
                // The ragged edge: how far forward this stretch of jungle comes.
                double edge = TreelineZ - rng.Range(0, 4) + System.Math.Sin(x * 0.07) * 2.0;
                if (NearCover(cover, x, edge, 1.5)) continue;
                int depth = rng.Int(2, 6);
                for (int k = 0; k < depth; k++)
                {
                    double z = edge - k * rng.Range(6, 12) - rng.Range(0, 4);
                    double xx = x + rng.Range(-2, 2);
                    float y = (float)g.HeightAt(xx, z);
                    var mat = k == 0 ? Foliage : FoliageDark;
                    if (k == 0)
                    {
                        // The front edge: bushes and understory, head to twice head high.
                        float h = (float)rng.Range(2.5, 5.5), w = (float)rng.Range(2, 4);
                        Prim(PrimitiveType.Sphere, Coords.World(xx, z, y + h * 0.45f), new Vector3(w, h, w * 0.8f), mat, "bush");
                        continue;
                    }
                    // Crown tops from the reference's own angle: its treeline tops
                    // out about 3.5 degrees above the horizon, so a crown's top is
                    // the lens height plus distance x tan(3.5 deg), give or take.
                    double dist = Coords.Camera.SimZ - z;
                    float top = (float)(Coords.Camera.Height + dist * 0.061 + rng.Range(-1.5, 2.0));
                    float cw = (float)rng.Range(5, 10);
                    float ch = cw * (float)rng.Range(0.28, 0.42);    // broad, umbrella-flat
                    float groundTop = top - y;
                    Prim(PrimitiveType.Cylinder, Coords.World(xx, z, y + (groundTop - ch) * 0.5f),
                         new Vector3(0.35f, (groundTop - ch) * 0.5f, 0.35f), Timber, "trunk");
                    Prim(PrimitiveType.Sphere, Coords.World(xx, z, top - ch * 0.5f), new Vector3(cw, ch, cw), mat, "crown");
                }
                // A palm now and then, standing out of the canopy as in the
                // reference: a tall bare trunk and a small crown.
                if (rng.Next() < 0.12)
                {
                    double px = x + rng.Range(-2, 2), pz = edge - rng.Range(2, 25);
                    float ph = (float)rng.Range(14, 20);
                    float py = (float)g.HeightAt(px, pz);
                    Prim(PrimitiveType.Cylinder, Coords.World(px, pz, py + ph * 0.5f), new Vector3(0.3f, ph * 0.5f, 0.3f), Timber, "palm trunk");
                    Prim(PrimitiveType.Sphere, Coords.World(px, pz, py + ph), new Vector3(5.5f, 1.8f, 5.5f), Foliage, "palm crown");
                }
                // Bamboo at the edge now and then: tall thin culms in a clump.
                if (rng.Next() < 0.18)
                {
                    int culms = rng.Int(5, 11);
                    for (int c = 0; c < culms; c++)
                    {
                        double bx = x + rng.Range(-1.2, 1.2), bz = edge + rng.Range(-1.5, 0.5);
                        float bh = (float)rng.Range(6, 11);
                        float by = (float)g.HeightAt(bx, bz);
                        Prim(PrimitiveType.Cylinder, Coords.World(bx, bz, by + bh * 0.5f), new Vector3(0.12f, bh * 0.5f, 0.12f), Foliage, "bamboo");
                    }
                }
            }
        }

        /// <summary>
        /// Knee- to head-high scrub between the far lane and the treeline, so the
        /// far lane sits in front of something rather than on a lawn, and never
        /// on cover.
        /// </summary>
        private void Scrub(Ground g, IReadOnlyList<Cover> cover, Rng rng)
        {
            for (int i = 0; i < 140; i++)
            {
                double x = rng.Range(-150, 150), z = rng.Range(ScrubFarZ, ScrubNearZ);
                if (NearCover(cover, x, z, 1.0)) continue;
                float h = (float)rng.Range(0.8, 2.6), w = (float)rng.Range(0.8, 2.4);
                float y = (float)g.HeightAt(x, z);
                Prim(PrimitiveType.Sphere, Coords.World(x, z, y + h * 0.35f), new Vector3(w, h, w * 0.8f), Foliage, "scrub");
            }
        }

        /// <summary>
        /// Concertina along the bank, broken where the bank is cut: the gaps are
        /// the crossings, which is where the brief says men cross.
        /// </summary>
        private void WireLine(Ground g, Rng rng)
        {
            var ring = Ring(0.45f, 0.022f, 20, 5);
            // In front of the firebase, where it was strung, and a few broken
            // runs further out that the fighting has left.
            var runs = new List<(double from, double to)> { (-52, -17) };
            for (int i = 0; i < 4; i++)
            {
                double a = rng.Range(-8, 34);
                runs.Add((a, a + rng.Range(3, 7)));
            }
            foreach (var (from, to) in runs)
            {
                for (double x = from; x < to; x += 0.55)
                {
                    // A gap where the bank is cut through: the crossings.
                    if (g.BermGate(x) < 0.35) continue;
                    double z = WireZ + System.Math.Sin(x * 0.3) * 0.25;
                    float y = (float)g.HeightAt(x, z);
                    var go = new GameObject("concertina");
                    go.transform.SetParent(transform, false);
                    go.transform.SetPositionAndRotation(Coords.World(x, z, y + 0.42f),
                        Quaternion.Euler((float)rng.Range(-8, 8), 90 + (float)rng.Range(-12, 12), 0));
                    go.AddComponent<MeshFilter>().sharedMesh = ring;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = Wire;
                    _built.Add(r);
                }
            }
        }

        /// <summary>A thin torus: one loop of a concertina coil.</summary>
        private static Mesh Ring(float radius, float tube, int segments, int sides)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                var centre = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0);
                var outward = centre.normalized;
                for (int j = 0; j <= sides; j++)
                {
                    float b = j * Mathf.PI * 2 / sides;
                    var n = outward * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                    verts.Add(centre + n * tube);
                    norms.Add(n);
                }
            }
            for (int i = 0; i < segments; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    int a = i * (sides + 1) + j, b = a + sides + 1;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
            }
            var m = new Mesh { name = "concertina loop" };
            m.SetVertices(verts); m.SetNormals(norms); m.SetTriangles(tris, 0);
            return m;
        }

        /// <summary>
        /// The firebase compound, at the west end and in the middle distance —
        /// as in TARGET.jpg, where the near lane's men stand in front of the
        /// trucks and the tower stands behind them. The first grey-box put it
        /// between the near lane and the lens, and a truck filled the frame.
        /// Its sandbagged front edge and the wire before it are the near lane's
        /// far boundary here.
        /// </summary>
        private void Firebase(Ground g)
        {
            // Perimeter revetment: the front facing the near lane, and the east
            // side facing the track.
            for (double x = -52; x < FirebaseEastX - 3; x += 2.6)
                Wall(g, x, 2.6, 2.6, 1.1, 0.9);
            for (double z = -12; z < 2.6; z += 2.6)
                Wall(g, FirebaseEastX - 3.5, z, 0.9, 1.1, 2.6);

            // The watchtower: real elevation for whoever holds it. Its top sits
            // about 7 degrees above the horizon, as the reference's does.
            double tx = -39, tz = -7;
            float ty = (float)g.HeightAt(tx, tz);
            for (int i = 0; i < 4; i++)
            {
                double lx = tx + (i % 2 == 0 ? -1.3 : 1.3), lz = tz + (i < 2 ? -1.3 : 1.3);
                Prim(PrimitiveType.Cube, Coords.World(lx, lz, ty + 4.2f), new Vector3(0.28f, 8.4f, 0.28f), Timber, "tower leg");
            }
            Prim(PrimitiveType.Cube, Coords.World(tx, tz, ty + 9.0f), new Vector3(3.4f, 1.3f, 3.4f), Timber, "tower cabin");
            Prim(PrimitiveType.Cube, Coords.World(tx, tz, ty + 10.2f), new Vector3(4.0f, 0.22f, 4.0f), Timber, "tower roof");

            // The vehicle park inside the wire: two M35s and the jeep.
            foreach (var (vx, vz, len, h) in new[] { (-33.5, -1.5, 6.7, 2.8), (-27.0, -0.8, 6.7, 2.8), (-45.0, -3.0, 3.4, 1.8) })
            {
                float vy = (float)g.HeightAt(vx, vz);
                Prim(PrimitiveType.Cube, Coords.World(vx, vz, vy + (float)h * 0.5f), new Vector3((float)len, (float)h, 2.4f), Vehicle, "vehicle");
            }
        }

        private void Wall(Ground g, double x, double z, double lenX, double h, double lenZ)
        {
            float y = (float)g.HeightAt(x, z);
            Prim(PrimitiveType.Cube, Coords.World(x, z, y + (float)h * 0.5f - 0.1f),
                 new Vector3((float)lenX, (float)h, (float)lenZ), Sandbag, "revetment");
        }

        /// <summary>
        /// Four ridges, receding. Sized by angle rather than by metres: the
        /// reference's peaks stand about 4-6 degrees above the horizon, so a
        /// ridge's height is its distance times tan of that — the first grey-box
        /// made them hills looming at 20 degrees.
        /// </summary>
        private void Ridges(Rng rng)
        {
            for (int r = 0; r < 4; r++)
            {
                double z = -450 - r * 160;
                double dist = Coords.Camera.SimZ - z;
                for (double x = -1100; x < 1100; x += rng.Range(110, 240))
                {
                    double deg = rng.Range(2.2, 5.8) - r * 0.4;
                    float top = (float)(Coords.Camera.Height + dist * System.Math.Tan(deg * System.Math.PI / 180));
                    float w = (float)rng.Range(180, 380);
                    var t = Prim(PrimitiveType.Sphere, Coords.World(x, z + rng.Range(-40, 40), 0), new Vector3(w, top * 2f, w * 0.6f), Mountain, "ridge");
                    t.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        }

        private Transform Prim(PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, string name)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            _built.Add(r);
            return go.transform;
        }
    }
}
