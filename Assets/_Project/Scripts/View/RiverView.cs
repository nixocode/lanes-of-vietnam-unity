using System;
using System.Collections.Generic;
using LanesOfVietnam.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// Rice Fields' water and its bridge (PLAN, Rice Fields): the river across the lanes, brown and opaque, as a lowland
    /// river in the dry season is; the paddies' water standing still on each step, dark and holding the sky; and the
    /// timber bridge that carries the near lane over the river, built of its members: log piles in bents, a cap on each,
    /// stringers, a deck of planks, and a rail each side.
    ///
    /// A first look, to be built on: the water is the engine's lit surface, smooth, not yet the water shader of R3 (flowing,
    /// faded at the banks, reddened by the dead); the bridge stands whole (it comes apart in R5).
    /// </summary>
    public sealed class RiverView : MonoBehaviour
    {
        private Material _river, _paddy, _planks;
        private readonly List<Renderer> _built = new List<Renderer>();
        public IReadOnlyList<Renderer> Built => _built;

        /// <summary>The river's water and the paddies', a lit surface each: a copy of a plain lit material, coloured.</summary>
        private void Materials(Material lit)
        {
            if (_river != null || lit == null) return;
            Material Water(string name, Color colour, float smooth)
            {
                var m = new Material(lit) { name = name };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
                if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
                m.DisableKeyword("_NORMALMAP");
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                return m;
            }
            // Measured by eye against the owner's second picture: a muddy brown river; the paddies darker, olive.
            _river = Water("River", new Color(0.27f, 0.22f, 0.13f), 0.62f);
            _paddy = Water("Paddy", new Color(0.12f, 0.14f, 0.11f), 0.95f);
            // The bridge's timber: weathered, grey-brown, dull (until it is textured, R4).
            _planks = Water("Bridge Timber", new Color(0.33f, 0.26f, 0.19f), 0.12f);
        }

        public void Build(Ground g, MapDef map, Material timber, Material lit, Material riverWater = null, Material paddyWater = null)
        {
            foreach (Transform c in transform) Destroy(c.gameObject);
            _built.Clear();
            _spans.Clear();
            _ground = g;
            var p = g.Params;
            if (double.IsNaN(p.RiverX0) && double.IsNaN(p.PaddyX0)) return;
            Materials(lit);
            // (The theatre's water shader, where it has one: LOV/Water. The lit stand-ins otherwise.)
            if (riverWater != null) _river = riverWater;
            if (paddyWater != null) _paddy = paddyWater;
            if (!double.IsNaN(p.RiverX0)) RiverSheet(g, (float)p.RiverWater);
            for (int k = 0; k < g.FloodedSteps; k++) PaddySheet(g, k);
            // (The theatre's planks where it has them; a plain weathered brown otherwise.)
            bool textured = timber != null && timber.HasProperty("_BaseMap") && timber.GetTexture("_BaseMap") != null;
            for (int i = 0; i < g.Decks.Count; i++) Bridge(g, g.Decks[i], textured ? timber : _planks != null ? _planks : timber, i);
            Debug.Log($"[LOV] river: water {(double.IsNaN(p.RiverX0) ? "none" : $"x {p.RiverX0:F0}..{p.RiverX1:F0} at {p.RiverWater:F2} m")}, {g.FloodedSteps} paddies, {g.Decks.Count} bridge{(g.Decks.Count == 1 ? "" : "s")}");
        }

        /// <summary>The river's water: a level strip following its banks as they wind, a metre or two past each.</summary>
        private void RiverSheet(Ground g, float y)
        {
            var p = g.Params;
            const int across = 6;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            int rows = 0;
            for (double z = -150; z <= 46; z += 1.0, rows++)
            {
                var (x0, x1) = g.RiverAt(z);
                double a = x0 - p.RiverBank, b = x1 + p.RiverBank;
                for (int i = 0; i <= across; i++) verts.Add(Coords.World(a + (b - a) * i / across, z, y));
            }
            for (int j = 0; j + 1 < rows; j++)
                for (int i = 0; i < across; i++)
                {
                    int q = j * (across + 1) + i, r = q + 1, s = q + across + 1, t = s + 1;
                    tris.Add(q); tris.Add(s); tris.Add(r);
                    tris.Add(r); tris.Add(s); tris.Add(t);
                }
            var mesh = new Mesh { name = "river water", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            var n = Vector3.Cross(verts[tris[1]] - verts[tris[0]], verts[tris[2]] - verts[tris[0]]);
            if (n.y < 0) for (int i = 0; i < tris.Count; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
            mesh.SetTriangles(tris, 0);
            var norms = new Vector3[verts.Count];
            var uv = new Vector2[verts.Count];
            for (int i = 0; i < norms.Length; i++) { norms[i] = Vector3.up; uv[i] = new Vector2(verts[i].x * 0.1f, verts[i].z * 0.1f); }
            mesh.normals = norms; mesh.uv = uv;
            mesh.RecalculateBounds();
            Add("river water", mesh, _river, castShadows: false);
        }

        /// <summary>
        /// A step's water: level, from a hand inside its own dike to a hand short of the next one's face, following both
        /// as they wind, across the step's width between its side dikes (its edges are under the earth).
        /// </summary>
        private void PaddySheet(Ground g, int k)
        {
            var p = g.Params;
            float y = (float)(g.TerraceLevel(k) + p.PaddyFlood);
            const int across = 8;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            int rows = 0;
            for (double z = p.PaddyMidZ - p.PaddyHalfZ; z <= p.PaddyMidZ + p.PaddyHalfZ + 1e-6; z += 0.5, rows++)
            {
                double a = g.Boundary(k, z) + 0.2, b = g.Boundary(k + 1, z) - 0.2;
                for (int i = 0; i <= across; i++) verts.Add(Coords.World(a + (b - a) * i / across, z, y));
            }
            for (int j = 0; j + 1 < rows; j++)
                for (int i = 0; i < across; i++)
                {
                    int q = j * (across + 1) + i, r = q + 1, s = q + across + 1, t = s + 1;
                    tris.Add(q); tris.Add(s); tris.Add(r);
                    tris.Add(r); tris.Add(s); tris.Add(t);
                }
            var mesh = new Mesh { name = $"paddy water {k}", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            var n = Vector3.Cross(verts[tris[1]] - verts[tris[0]], verts[tris[2]] - verts[tris[0]]);
            if (n.y < 0) for (int i = 0; i < tris.Count; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
            mesh.SetTriangles(tris, 0);
            var norms = new Vector3[verts.Count];
            var uv = new Vector2[verts.Count];
            for (int i = 0; i < norms.Length; i++) { norms[i] = Vector3.up; uv[i] = new Vector2(verts[i].x * 0.1f, verts[i].z * 0.1f); }
            mesh.normals = norms; mesh.uv = uv;
            mesh.RecalculateBounds();
            Add(mesh.name, mesh, _paddy, castShadows: false);
        }

        /// <summary>A level sheet of water over a rectangle (the simulation's x and z), in cells so it is lit and fogged evenly.</summary>
        private void Sheet(string name, double x0, double x1, double z0, double z1, float y, Material mat, double cell)
        {
            int nx = Mathf.Max(1, (int)System.Math.Ceiling((x1 - x0) / cell)), nz = Mathf.Max(1, (int)System.Math.Ceiling((z1 - z0) / cell));
            var verts = new Vector3[(nx + 1) * (nz + 1)];
            var uv = new Vector2[verts.Length];
            var norms = new Vector3[verts.Length];
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    double x = x0 + (x1 - x0) * i / nx, z = z0 + (z1 - z0) * j / nz;
                    int v = j * (nx + 1) + i;
                    verts[v] = Coords.World(x, z, y);
                    uv[v] = new Vector2((float)x * 0.1f, (float)z * 0.1f);
                    norms[v] = Vector3.up;
                }
            var tris = new int[nx * nz * 6];
            int t = 0;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    // (Wound to face up in the world, whose z is the simulation's turned: Coords.World.)
                    tris[t++] = a; tris[t++] = b; tris[t++] = c;
                    tris[t++] = b; tris[t++] = d; tris[t++] = c;
                }
            var mesh = new Mesh { name = name, indexFormat = verts.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = verts; mesh.uv = uv; mesh.normals = norms; mesh.triangles = tris;
            // A face's winding by its normal: if it came out facing down, turn it over.
            var n = Vector3.Cross(verts[tris[1]] - verts[tris[0]], verts[tris[2]] - verts[tris[0]]);
            if (n.y < 0)
            {
                for (int i = 0; i < tris.Length; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
                mesh.triangles = tris;
            }
            mesh.RecalculateBounds();
            Add(name, mesh, mat, castShadows: false);
        }

        private void Add(string name, Mesh mesh, Material mat, bool castShadows, Transform under = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(under != null ? under : transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            _built.Add(r);
        }

        // --- the bridge ---------------------------------------------------------------------

        /// <summary>
        /// A timber trestle, as in the owner's picture: bents of three log piles every three metres, a cap across each,
        /// three stringers along, planks across them with a finger's gap and a little out of true, a post-and-rail each side.
        /// The bents stand by themselves; the deck is in two halves, each with its rails, hinged at its own bank, so that
        /// brought down it breaks in the middle and each half goes into the river from its bank (MatchOptions.Water).
        /// </summary>
        private void Bridge(Ground g, BridgeDeck d, Material timber, int index)
        {
            var bents = new Builder();
            float deck = (float)d.Y, half = (float)d.Width * 0.5f;
            var rng = new Rng(7919 + (int)(d.X0 * 13));
            // The bents: from the river's bed (and into it) up to under the stringers.
            float capTop = deck - 0.06f - 0.22f;
            for (double x = d.X0 + 1.4; x < d.X1 - 0.6; x += 3.0)
            {
                float cap = capTop - 0.14f;
                foreach (double dz in new[] { -half + 0.35, 0.0, half - 0.35 })
                {
                    double z = d.Z + dz;
                    float foot = (float)g.HeightAt(x, z) - 0.6f;
                    if (foot > cap - 0.3f) continue;
                    bents.Log(W(x + rng.Range(-0.04, 0.04), z, foot), W(x, z, capTop), 0.13f + (float)rng.Range(0, 0.03f));
                }
                bents.Box(W(x, d.Z, cap + 0.07f), new Vector3(0.26f, 0.2f, (float)d.Width + 0.4f), Quaternion.identity);
                // Two braces across the bent, from the cap at one side down to the piles at the other.
                float low = Mathf.Max((float)g.HeightAt(x, d.Z) + 0.2f, cap - 1.6f);
                if (cap - low > 0.6f)
                {
                    bents.Plank(W(x + 0.15, d.Z - half + 0.35, cap), W(x + 0.15, d.Z + half - 0.35, low), 0.18f, 0.06f);
                    bents.Plank(W(x - 0.15, d.Z + half - 0.35, cap), W(x - 0.15, d.Z - half + 0.35, low), 0.18f, 0.06f);
                }
            }
            Add($"bridge {index} bents", bents.Mesh($"bridge {index} bents", Vector3.zero), timber, castShadows: true);

            var span = new Span { Index = index, Deck = d, Seed = 0 };
            double mid = (d.X0 + d.X1) * 0.5;
            for (int side = 0; side < 2; side++)
            {
                double a = side == 0 ? d.X0 : mid, b = side == 0 ? mid : d.X1;
                var pivot = W(side == 0 ? d.X0 : d.X1, d.Z, capTop);
                var floor = new Builder();
                // Stringers, resting on the caps and on the bank at its end.
                foreach (double dz in new[] { -half + 0.3, 0.0, half - 0.3 })
                    floor.Box(W((a + b) * 0.5, d.Z + dz, capTop + 0.11f), new Vector3((float)(b - a), 0.22f, 0.2f), Quaternion.identity);
                // The deck: planks across, each a little out of true.
                for (double x = d.X0 + 0.14; x < d.X1 - 0.1; x += 0.29)
                {
                    var turn = Quaternion.Euler((float)rng.Range(-0.6, 0.6), (float)rng.Range(-1.5, 1.5), 0f);
                    float dy = (float)rng.Range(-0.012, 0.012), wide = (float)d.Width + (float)rng.Range(-0.1, 0.15);
                    if (x >= a && x < b) floor.Box(W(x, d.Z, deck - 0.03f + dy), new Vector3(0.27f, 0.06f, wide), turn);
                }
                // The rails: a post every metre and a half, a rail on top and one half way.
                var rails = new Builder();
                foreach (double dz in new[] { -half + 0.08, half - 0.08 })
                {
                    for (double x = d.X0 + 0.3; x <= d.X1 - 0.2; x += 1.5)
                    {
                        float lean = (float)rng.Range(-2, 2);
                        if (x >= a && x < b) rails.Box(W(x, d.Z + dz, deck + 0.5f), new Vector3(0.12f, 1.0f, 0.12f), Quaternion.Euler(0f, 0f, lean));
                    }
                    double r0 = Math.Max(a, d.X0 + 0.2), r1 = Math.Min(b, d.X1 - 0.2);
                    rails.Box(W((r0 + r1) * 0.5, d.Z + dz, deck + 0.98f), new Vector3((float)(r1 - r0), 0.1f, 0.1f), Quaternion.identity);
                    rails.Box(W((r0 + r1) * 0.5, d.Z + dz, deck + 0.52f), new Vector3((float)(r1 - r0), 0.08f, 0.06f), Quaternion.identity);
                }
                var hinge = new GameObject($"bridge {index} span {side}").transform;
                hinge.SetParent(transform, false);
                hinge.position = pivot;
                Add($"bridge {index} deck {side}", floor.Mesh($"bridge {index} deck {side}", pivot), timber, castShadows: true, under: hinge);
                Add($"bridge {index} rails {side}", rails.Mesh($"bridge {index} rails {side}", pivot), timber, castShadows: true, under: hinge);
                if (side == 0) span.West = hinge; else span.East = hinge;
            }
            span.Drop = deck - ((float)g.Params.RiverWater - 0.45f);
            _spans.Add(span);
        }

        /// <summary>A bridge's two halves as they hang: standing, going down, or down in the river; put back without rails.</summary>
        private sealed class Span
        {
            public int Index, Seed;
            public BridgeDeck Deck;
            public Transform West, East;
            public bool Down, Rough;
            /// <summary>When it went down (or was put back), on the match's clock; -1 while it has stood all along.</summary>
            public float Since = -1f, Drop;
        }

        private readonly List<Span> _spans = new List<Span>();
        private Ground _ground;

        /// <summary>
        /// Bring bridge i down (for a capture and the tests): bursts on the middle of its deck until it falls, as rockets and
        /// charges would. False if the match has no such bridge, or the rule is off.
        /// </summary>
        public static bool Bring(SimState st, int i)
        {
            if (!st.Water || !st.Destruction || i < 0 || i >= st.Bridges.Count) return false;
            var b = st.Bridges[i];
            double x = (b.X0 + b.X1) * 0.5, z = Tune.Lanes[b.Lane];
            for (int n = 0; n < 8 && !b.Fallen; n++)
            {
                st.Events.Add(new SimEvent { Kind = EventKind.Shell, Tick = st.Tick, Side = Side.Vc, Id = -1, X = x, Z = z });
                Destruction.Burst(st, x, z, 0, Tune.TowerSatchel);
            }
            return b.Fallen;
        }

        /// <summary>The bridges as the match has them: brought down, it breaks and goes into the river; put back, it lies level again.</summary>
        private void Update()
        {
            var root = GameRoot.Instance;
            var d = root != null ? root.Driver : null;
            var st = d != null ? d.State : null;
            if (st == null || _ground == null) return;
            // (On the match's own clock, as the tower's fall is: a capture or a paused game sees it where the match is.)
            float now = (float)((st.Tick - 1 + d.Alpha) * Tune.Dt);
            Stain(st, now);
            foreach (var s in _spans)
            {
                bool fallen = st.Water && s.Index < st.Bridges.Count && st.Bridges[s.Index].Fallen;
                if (fallen != s.Down)
                {
                    s.Down = fallen;
                    _ground.SetDeckDown(s.Index, fallen);
                    var kind = fallen ? EventKind.BridgeFell : EventKind.BridgeRebuilt;
                    int k = st.Events.FindLastIndex(e => e.Kind == kind && e.Id == s.Index);
                    s.Since = k >= 0 ? (float)(st.Events[k].Tick * Tune.Dt) : now;
                    if (fallen) s.Seed = k >= 0 ? st.Events[k].Rounds : 0;
                    // Put back, it is a rough crossing: its planks laid again, no rails.
                    else s.Rough = true;
                }
                if (s.Since < 0f) continue;
                Pose(s, Mathf.Max(0f, now - s.Since));
            }
        }

        /// <summary>The water's stains (LOV/Water): up to sixteen, the reddest.</summary>
        public const int Stains = 16;
        private readonly Vector4[] _stains = new Vector4[Stains];
        private readonly List<Vector4> _found = new List<Vector4>();
        private static readonly int StainsId = Shader.PropertyToID("_LOV_Stains"), StainCountId = Shader.PropertyToID("_LOV_StainCount");
        /// <summary>How many stains the water has now, and how red the reddest (for the tests and the captures).</summary>
        public int StainCount { get; private set; }
        public float Reddest { get; private set; }

        /// <summary>
        /// The dead in the water redden it (the owner: "tint of water changes to red if many bodies pile up"): a man killed
        /// where it stands a hand deep or more stains it round him, spreading over twenty seconds, carried down the river by
        /// its current (not a paddy's), and fading over a few minutes; where bodies lie together their stains add up.
        /// </summary>
        private void Stain(SimState st, float now)
        {
            _found.Clear();
            var g = _ground;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                if (m.Alive || m.DiedAt < 0) continue;
                double water = g.WaterAt(m.X, m.Z);
                if (double.IsNaN(water) || water - g.HeightAt(m.X, m.Z) < 0.1) continue;
                float since = Mathf.Max(0f, now - (float)(m.DiedAt * Tune.Dt));
                float red = 0.6f * Mathf.Exp(-since / 150f) * Mathf.Clamp01(since / 2f + 0.15f);
                if (red < 0.02f) continue;
                float radius = Mathf.Lerp(1.2f, 4.2f, Mathf.Clamp01(since / 20f));
                // (Down the river with its current, to fifteen metres; a paddy's water does not go anywhere.)
                float drift = g.InRiver(m.X, m.Z) ? Mathf.Min(15f, since * 0.22f) : 0f;
                var at = Coords.World(m.X, m.Z, 0f) + new Vector3(0f, 0f, -drift);
                _found.Add(new Vector4(at.x, at.z, radius, red));
            }
            _found.Sort((a, b) => b.w.CompareTo(a.w));
            int n = Mathf.Min(Stains, _found.Count);
            for (int k = 0; k < Stains; k++) _stains[k] = k < n ? _found[k] : Vector4.zero;
            Shader.SetGlobalVectorArray(StainsId, _stains);
            Shader.SetGlobalFloat(StainCountId, n);
            StainCount = n;
            Reddest = n > 0 ? _found[0].w : 0f;
        }

        /// <summary>Each half about its own bank, as long after it went (or came back) as this.</summary>
        private void Pose(Span s, float t)
        {
            float half = (float)(s.Deck.X1 - s.Deck.X0) * 0.5f;
            for (int side = 0; side < 2; side++)
            {
                var hinge = side == 0 ? s.West : s.East;
                if (hinge == null) continue;
                // How far its free end goes down: into the river, by its own dice a little more or less, and turned over a little.
                float dice = TowerView.H(s.Seed, 70 + side);
                float angle = Mathf.Asin(Mathf.Clamp01(s.Drop / half)) * Mathf.Rad2Deg * (0.85f + 0.4f * dice);
                float roll = (TowerView.H(s.Seed, 80 + side) - 0.5f) * 16f;
                // Down: under gravity, about its bank, in a second; back: laid level over half a second.
                float k = s.Down ? Mathf.Clamp01(t / 0.95f) : 1f - Mathf.Clamp01(t / 0.5f);
                k = s.Down ? k * k : k;
                hinge.localRotation = Quaternion.AngleAxis((side == 0 ? -angle : angle) * k, Vector3.forward) * Quaternion.AngleAxis(roll * k, Vector3.right);
                // (Its rails gone, once it is put back.)
                var rails = hinge.Find($"bridge {s.Index} rails {side}");
                if (rails != null) rails.gameObject.SetActive(!(s.Rough && !s.Down));
            }
        }

        private static Vector3 W(double x, double z, float y) => Coords.World(x, z, y);

        /// <summary>Boxes and logs into one mesh, each face its own normals; uv along a member's length.</summary>
        private sealed class Builder
        {
            private readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>();
            private readonly List<Vector2> _uv = new List<Vector2>();
            private readonly List<int> _t = new List<int>();

            public void Box(Vector3 c, Vector3 size, Quaternion turn)
            {
                var h = size * 0.5f;
                Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
                for (int a = 0; a < 3; a++)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var n = axes[a] * s;
                        var u = axes[(a + 1) % 3];
                        var w = axes[(a + 2) % 3];
                        float hu = Vector3.Scale(u, h).magnitude, hw = Vector3.Scale(w, h).magnitude, hn = Vector3.Scale(n, h).magnitude;
                        int i0 = _v.Count;
                        for (int k = 0; k < 4; k++)
                        {
                            float su = k == 0 || k == 3 ? -1 : 1, sw = k < 2 ? -1 : 1;
                            _v.Add(c + turn * (n * hn + u * hu * su + w * hw * sw));
                            _n.Add(turn * n);
                            _uv.Add(new Vector2(su * hu, sw * hw));
                        }
                        if (s > 0) { _t.Add(i0); _t.Add(i0 + 2); _t.Add(i0 + 1); _t.Add(i0); _t.Add(i0 + 3); _t.Add(i0 + 2); }
                        else { _t.Add(i0); _t.Add(i0 + 1); _t.Add(i0 + 2); _t.Add(i0); _t.Add(i0 + 2); _t.Add(i0 + 3); }
                    }
            }

            /// <summary>A flat member from one point to another, so wide and so thick (a brace).</summary>
            public void Plank(Vector3 a, Vector3 b, float wide, float thick)
            {
                var along = b - a;
                Box((a + b) * 0.5f, new Vector3(thick, along.magnitude, wide), Quaternion.FromToRotation(Vector3.up, along.normalized));
            }

            /// <summary>A round log from one point to another: eight sides, its ends capped.</summary>
            public void Log(Vector3 a, Vector3 b, float r)
            {
                var along = (b - a).normalized;
                var side = Vector3.Cross(along, Mathf.Abs(along.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                var up = Vector3.Cross(side, along);
                const int sides = 8;
                int i0 = _v.Count;
                float len = (b - a).magnitude;
                for (int k = 0; k <= sides; k++)
                {
                    float ang = k * Mathf.PI * 2f / sides;
                    var n = side * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                    _v.Add(a + n * r); _n.Add(n); _uv.Add(new Vector2(k / (float)sides, 0f));
                    _v.Add(b + n * r); _n.Add(n); _uv.Add(new Vector2(k / (float)sides, len));
                }
                for (int k = 0; k < sides; k++)
                {
                    int p = i0 + k * 2;
                    _t.Add(p); _t.Add(p + 1); _t.Add(p + 2);
                    _t.Add(p + 1); _t.Add(p + 3); _t.Add(p + 2);
                }
                // The top end, capped.
                int c = _v.Count;
                _v.Add(b); _n.Add(along); _uv.Add(Vector2.zero);
                for (int k = 0; k <= sides; k++)
                {
                    float ang = k * Mathf.PI * 2f / sides;
                    _v.Add(b + (side * Mathf.Cos(ang) + up * Mathf.Sin(ang)) * r); _n.Add(along); _uv.Add(new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r);
                }
                for (int k = 0; k < sides; k++) { _t.Add(c); _t.Add(c + 2 + k); _t.Add(c + 1 + k); }
            }

            /// <summary>The mesh, its vertices about an origin (a hinge it turns on).</summary>
            public Mesh Mesh(string name, Vector3 origin)
            {
                var m = new Mesh { name = name, indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                for (int i = 0; i < _v.Count; i++) _v[i] -= origin;
                m.SetVertices(_v); m.SetNormals(_n); m.SetUVs(0, _uv); m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                m.RecalculateTangents();
                return m;
            }
        }
    }
}
