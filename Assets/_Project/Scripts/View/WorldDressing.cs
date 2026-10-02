using System.Collections.Generic;
using System.Linq;
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
        public Material Wire;
        public Material Timber;
        public Material Sandbag;
        public Material Vehicle;
        /// <summary>Baked plants (tools/blender/plant_bake.py). A species that is missing falls back to its grey-box shape.</summary>
        public PlantSet[] Plants;
        /// <summary>Baked props placed by name (the firebase's tower and vehicles). Missing: grey boxes.</summary>
        public PlantSet[] Props;

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
        /// <summary>Half the width of the gap left in a wall that a lane runs through: its files and a man's length each side.</summary>
        public const double LaneGate = 3.4;

        private readonly List<Renderer> _built = new List<Renderer>();
        public IReadOnlyList<Renderer> Built => _built;

        private Dictionary<string, PlantSpecies> _species;
        private PlantBatch _plants;

        public void Build(Ground g, IReadOnlyList<Cover> cover, int seed)
        {
            foreach (Transform c in transform) Destroy(c.gameObject);
            _built.Clear();
            _species = new Dictionary<string, PlantSpecies>();
            foreach (var p in (Plants ?? System.Array.Empty<PlantSet>()).Concat(Props ?? System.Array.Empty<PlantSet>()))
                if (p.Layout != null && p.Material != null) _species[p.Name] = new PlantSpecies(p);
            _plants = new PlantBatch();
            var rng = new Rng(seed).Fork("dressing");
            // Which plant stands where draws from its own fork, so the layout —
            // every position the rules chose — is the grey box's exactly.
            _plantRng = rng.Fork("plants");
            Treeline(g, cover, rng.Fork("treeline"));
            Scrub(g, cover, rng.Fork("scrub"));
            Foreground(g, cover, rng.Fork("foreground"));
            Grass(g, cover, rng.Fork("grass"));
            WireLine(g, rng.Fork("wire"));
            Firebase(g);
            CombineByMaterial();
            _built.AddRange(_plants.Build(transform));
            if (_plants.Count > 0) Debug.Log($"[LOV] plants: {_plants.Count:N0} of {_species.Count} species");
        }

        private Rng _plantRng;

        private PlantSpecies Species(string name) => _species.TryGetValue(name, out var s) ? s : null;

        /// <summary>Place a baked prop by its variant name, at the size it was built. False if it was not baked.</summary>
        private bool Prop(Ground g, string species, string key, double x, double z, bool mirror = false)
        {
            var sp = Species(species);
            int vi = sp == null ? -1 : System.Array.IndexOf(sp.Keys, key);
            if (vi < 0) return false;
            _plants.Add(sp, vi, Coords.World(x, z, (float)g.HeightAt(x, z) - 0.03f), 1f, mirror, Color.white, 0f);
            return true;
        }

        /// <summary>
        /// Place one of several species at a point, at a height (metres), if any
        /// of them has been baked. Returns false when none has, so the caller can
        /// fall back to the grey box.
        /// </summary>
        private bool Plant(Ground g, double x, double z, float height, params string[] choices)
        {
            var avail = new List<PlantSpecies>();
            foreach (var c in choices) { var s = Species(c); if (s != null) avail.Add(s); }
            if (avail.Count == 0) return false;
            var sp = avail[_plantRng.Int(0, avail.Count)];
            int vi = _plantRng.Int(0, sp.Variants.Length);
            float scale = height / Mathf.Max(0.05f, sp.Variants[vi].Height);
            // A little of each plant's own colour: a leaf's green varies a few
            // percent in brightness and toward yellow or blue.
            float bright = (float)_plantRng.Range(0.86, 1.08);
            float warm = (float)_plantRng.Range(-0.05, 0.05);
            var tint = new Color(bright * (1 + warm), bright, bright * (1 - warm * 1.4f));
            var root = Coords.World(x, z, (float)g.HeightAt(x, z) - 0.03f);
            _plants.Add(sp, vi, root, scale, _plantRng.Next() < 0.5, tint, (float)_plantRng.Next());
            return true;
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
                        if (Plant(g, xx, z, h, "pachira", "ficus"))
                        {
                            // A clump, not a specimen: lower plants around its foot.
                            int n = _plantRng.Int(1, 4);
                            for (int j = 0; j < n; j++)
                                Plant(g, xx + _plantRng.Range(-w * 0.6, w * 0.6), z + _plantRng.Range(-1.0, 1.5),
                                      (float)_plantRng.Range(0.7, 2.0), "anthurium", "fern", "calathea");
                        }
                        else Prim(PrimitiveType.Sphere, Coords.World(xx, z, y + h * 0.45f), new Vector3(w, h, w * 0.8f), mat, "bush");
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
                    // The canopy: the broad umbrella crowns, with the smaller
                    // trees as the lower storey, and never a bare trunk on a lawn:
                    // the jungle's undergrowth closes up to the crowns.
                    if (Plant(g, xx, z, groundTop, "jacaranda", "jacaranda", "island_tree"))
                    {
                        Undergrowth(g, xx, z, groundTop);
                        continue;
                    }
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
                    // 10-14 m to the crown's top: the reference frames its palms
                    // whole; at the grey box's 17-23 m the crowns left the frame.
                    if (!Plant(g, px, pz, (float)_plantRng.Range(10, 14), "coconut_palm"))
                    {
                        Prim(PrimitiveType.Cylinder, Coords.World(px, pz, py + ph * 0.5f), new Vector3(0.3f, ph * 0.5f, 0.3f), Timber, "palm trunk");
                        Prim(PrimitiveType.Sphere, Coords.World(px, pz, py + ph), new Vector3(5.5f, 1.8f, 5.5f), Foliage, "palm crown");
                    }
                }
                // Bamboo at the edge now and then: tall thin culms in a clump.
                if (rng.Next() < 0.18)
                {
                    // The same draws as the grey box's culms, so the layout after
                    // this point is unchanged; one baked clump stands for them.
                    int culms = rng.Int(5, 11);
                    var stems = new List<(double x, double z, float h)>();
                    for (int c = 0; c < culms; c++)
                        stems.Add((x + rng.Range(-1.2, 1.2), edge + rng.Range(-1.5, 0.5), (float)rng.Range(6, 11)));
                    float tallest = 0;
                    foreach (var st in stems) tallest = Mathf.Max(tallest, st.h);
                    if (!Plant(g, x, edge - 0.5, tallest + 1.5f, "bamboo"))
                        foreach (var (bx, bz, bh) in stems)
                        {
                            float by = (float)g.HeightAt(bx, bz);
                            Prim(PrimitiveType.Cylinder, Coords.World(bx, bz, by + bh * 0.5f), new Vector3(0.12f, bh * 0.5f, 0.12f), Foliage, "bamboo");
                        }
                }
                // Thickets toward the firebase end, as in the reference: bamboo
                // walls the camp's side of the clearing.
                if (x < 0 && _plantRng.Next() < 0.22 + 0.25 * Mathf.Clamp01((float)(-x / 60)))
                    Plant(g, x + _plantRng.Range(-1.5, 1.5), edge + _plantRng.Range(-2.0, 1.0), (float)_plantRng.Range(8, 12.5), "bamboo");
            }
        }

        /// <summary>
        /// What fills the space under a canopy tree: saplings and a lower storey
        /// up to about half its height, then plants at their feet, spread across
        /// the crown's width and a little in front of it.
        /// </summary>
        private void Undergrowth(Ground g, double x, double z, float treeHeight)
        {
            if (Species("pachira") == null && Species("ficus") == null && Species("island_tree") == null) return;
            float spread = treeHeight * 0.45f;
            int mid = _plantRng.Int(2, 5);
            for (int j = 0; j < mid; j++)
                Plant(g, x + _plantRng.Range(-spread, spread), z + _plantRng.Range(-3.0, 2.0),
                      (float)_plantRng.Range(treeHeight * 0.25, treeHeight * 0.55), "island_tree", "pachira", "ficus");
            int low = _plantRng.Int(3, 7);
            for (int j = 0; j < low; j++)
                Plant(g, x + _plantRng.Range(-spread * 1.2, spread * 1.2), z + _plantRng.Range(-2.0, 3.0),
                      (float)_plantRng.Range(0.8, 2.4), "anthurium", "fern", "calathea", "pachira");
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
                // The aroid is a cultivated, variegated form: an occasional one only.
                bool placed = h > 1.7f
                    ? Plant(g, x, z, h, "pachira", "ficus")
                    : Plant(g, x, z, h, rng.Next() < 0.12 ? "aroid" : "anthurium", "anthurium", "fern", "calathea");
                if (!placed) Prim(PrimitiveType.Sphere, Coords.World(x, z, y + h * 0.35f), new Vector3(w, h, w * 0.8f), Foliage, "scrub");
            }
        }

        /// <summary>
        /// Low plants between the lens and the near lane, as in the reference's
        /// foreground: under 1.4 m, so a sight line to a man in the near lane
        /// (about 2 m up where it crosses this band) passes over them. In
        /// clumps, by a slow noise, never a carpet. Not in front of the firebase,
        /// which has its own cleared ground.
        /// </summary>
        private void Foreground(Ground g, IReadOnlyList<Cover> cover, Rng rng)
        {
            if (_species.Count == 0) return;
            for (int i = 0; i < 420; i++)
            {
                double x = rng.Range(-150, 150), z = rng.Range(11.5, 22);
                if (x < FirebaseEastX + 1 || NearCover(cover, x, z, 1.0)) continue;
                double clump = System.Math.Sin(x * 0.11 + 1.3) * System.Math.Sin(x * 0.037 + z * 0.21);
                if (rng.Next() > 0.25 + clump * 0.6) continue;
                Plant(g, x, z, (float)rng.Range(0.45, 1.35), "fern", "calathea", "anthurium");
            }
        }

        /// <summary>
        /// Grass over the whole field, as in the reference, where men stand
        /// waist-deep in it: tufts everywhere the ground is not bare, shorter
        /// in the lanes where men have trodden it; elephant grass, two to three
        /// metres, in clumps in the scrub and along the treeline, never in
        /// front of the near lane where it would hide the fight. Never on the
        /// track, in fresh earth or on cover. By a jittered grid and a slow
        /// noise, so it grows in patches rather than as a carpet.
        /// </summary>
        private void Grass(Ground g, IReadOnlyList<Cover> cover, Rng rng)
        {
            if (Species("grass_tuft") == null && Species("elephant_grass") == null) return;
            const double cell = 0.8;
            // Where grass is refused and why, per band: the log line below.
            var why = new int[4, 4];   // band x (placed, track, earth, density)
            for (double x = -150; x < 150; x += cell)
            {
                for (double z = -40; z < 23; z += cell)
                {
                    double px = x + rng.Range(0, cell), pz = z + rng.Range(0, cell);
                    var m = GroundView.Masks(g, px, pz);
                    int bandI = pz > 11 ? 0 : System.Math.Min(System.Math.Abs(pz - Tune.Lanes[0]), System.Math.Abs(pz - Tune.Lanes[1])) < 3.5 ? 1 : pz > -10.5 ? 2 : 3;
                    if (m.r > 90) { why[bandI, 1]++; continue; }             // the track
                    if (m.g > 100) { why[bandI, 2]++; continue; }            // fresh earth
                    if (NearCover(cover, px, pz, 0.6)) continue;
                    double patch = 0.5 + 0.5 * System.Math.Sin(px * 0.13 + System.Math.Sin(pz * 0.29) * 2.1)
                                             * System.Math.Cos(pz * 0.17 - px * 0.05);
                    bool firebase = px < FirebaseEastX && pz > 0;
                    double lane = System.Math.Min(System.Math.Abs(pz - Tune.Lanes[0]), System.Math.Abs(pz - Tune.Lanes[1]));
                    double density;
                    float lo, hi;
                    // Dense everywhere men are not standing on bare earth: the
                    // reference's lanes are grass and fern with dark gaps, not
                    // lawn. Measured: the bare ground between tufts was what
                    // held the lane bands L* 12-17 over TARGET.jpg's.
                    if (pz > 11) { density = 0.95; lo = 0.6f; hi = 1.2f; }                 // foreground
                    else if (lane < 3.5) { density = 1.0; lo = 0.4f; hi = 0.85f; }        // the lanes: knee-high, as the reference's men stand in
                    else if (pz > -10.5) { density = 0.95; lo = 0.45f; hi = 1.0f; }       // between
                    else { density = 0.9; lo = 0.7f; hi = 1.25f; }                       // scrub, treeline edge
                    if (firebase) density *= 0.2;
                    density *= 0.45 + 0.9 * patch;
                    if (rng.Next() > density) { why[bandI, 3]++; continue; }
                    why[bandI, 0]++;
                    bool tall = pz < -10.5 && rng.Next() < 0.1 + 0.25 * patch;
                    if (tall) Plant(g, px, pz, (float)rng.Range(2.0, 3.2), "elephant_grass");
                    else Plant(g, px, pz, (float)rng.Range(lo, hi), "grass_tuft");
                    // Low broadleaf through the grass, under knee height: never
                    // above the sight lines the simulation assumes.
                    if (!tall && pz < 11 && _plantRng.Next() < 0.14 + 0.12 * patch)
                        Plant(g, px + _plantRng.Range(-0.3, 0.3), pz + _plantRng.Range(-0.3, 0.3),
                              (float)_plantRng.Range(0.35, 0.75), "fern", "calathea", "anthurium");
                }
            }
            string[] bands = { "foreground", "lanes", "between", "scrub" };
            var sb = new System.Text.StringBuilder("[LOV] grass placed / refused (track, earth, density):");
            for (int b = 0; b < 4; b++) sb.Append($" {bands[b]} {why[b, 0]} ({why[b, 1]}, {why[b, 2]}, {why[b, 3]})");
            Debug.Log(sb.ToString());
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
            // (With a gate in it where the far lane runs out through the wire: the wall used to stand
            // across the lane, and every squad raised there walked through it.)
            for (double z = -14.6; z < 2.6; z += 2.6)
                if (System.Math.Abs(z - Tune.Lanes[1]) > LaneGate) Wall(g, FirebaseEastX - 3.5, z, 0.9, 1.1, 2.6);

            // The watchtower: real elevation for whoever holds it. Its top sits
            // about 7 degrees above the horizon, as the reference's does.
            // (Behind the far lane, not astride it: its legs stood where that lane's squads form up.)
            double tx = -39, tz = Tune.Lanes[1] - 5.6;
            float ty = (float)g.HeightAt(tx, tz);
            if (!Prop(g, "firebase", "tower", tx, tz))
            for (int i = 0; i < 4; i++)
            {
                double lx = tx + (i % 2 == 0 ? -1.3 : 1.3), lz = tz + (i < 2 ? -1.3 : 1.3);
                Prim(PrimitiveType.Cube, Coords.World(lx, lz, ty + 4.2f), new Vector3(0.28f, 8.4f, 0.28f), Timber, "tower leg");
            }
            if (Species("firebase") == null)
            {
                Prim(PrimitiveType.Cube, Coords.World(tx, tz, ty + 9.0f), new Vector3(3.4f, 1.3f, 3.4f), Timber, "tower cabin");
                Prim(PrimitiveType.Cube, Coords.World(tx, tz, ty + 10.2f), new Vector3(4.0f, 0.22f, 4.0f), Timber, "tower roof");
            }

            // The vehicle park inside the wire: two M35s and the jeep.
            foreach (var (vx, vz, len, h, key) in new[] { (-33.5, -1.5, 6.7, 2.8, "m35_covered"), (-27.0, -0.8, 6.7, 2.8, "m35_open"), (-45.0, -1.0, 3.4, 1.8, "jeep") })
            {
                if (Prop(g, "firebase", key, vx, vz)) continue;
                float vy = (float)g.HeightAt(vx, vz);
                Prim(PrimitiveType.Cube, Coords.World(vx, vz, vy + (float)h * 0.5f), new Vector3((float)len, (float)h, 2.4f), Vehicle, "vehicle");
            }
        }

        /// <summary>The sandbag walls' meshes for the revetments (set by GameRoot from CoverView's); not set: boxes.</summary>
        public BagSet Bags;
        private int _wallSeed;

        private void Wall(Ground g, double x, double z, double lenX, double h, double lenZ)
        {
            if (Bags.Ready)
            {
                // Along whichever way it is longer: the east wall runs away from the lens.
                double dx = lenX >= lenZ ? lenX / 2 : 0, dz = lenX >= lenZ ? 0 : lenZ / 2;
                Bags.Wall(transform, _built, g, x - dx, z - dz, x + dx, z + dz, (float)h, false, 1000 + _wallSeed++);
                return;
            }
            float y = (float)g.HeightAt(x, z);
            Prim(PrimitiveType.Cube, Coords.World(x, z, y + (float)h * 0.5f - 0.1f),
                 new Vector3((float)lenX, (float)h, (float)lenZ), Sandbag, "revetment");
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
