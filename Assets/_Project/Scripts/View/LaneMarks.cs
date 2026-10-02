using LanesOfVietnam.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// What the player sees while a card is in his hand (the lane selector).
    ///
    /// It was one orange ring hung in the air where the card would land. The
    /// owner, 2026-10-02: "fix the lane selector, come up with something
    /// visually appealing, transparent". Now:
    ///
    ///   both lanes    a ribbon of light on each lane's own ground, faint,
    ///                 so the two choices are both on the screen
    ///   the lane      the one the card would go to is bright where the
    ///                 pointer is, with a rail down each edge
    ///   a squad       chevrons run up its lane the way it will go, and a
    ///                 beam stands where it comes in (a squad arrives at its
    ///                 side's end of the lane, wherever the pointer is)
    ///   a call-in     a disc the size of what it does, laid on the ground,
    ///                 a ring pulsing out across it, and a beam on its middle
    ///   the pointer   a short beam on the lane under it, in both cases
    ///
    /// Everything is drawn with LOV/Lane: clear where it can be seen, and faint
    /// through whatever stands in front of it, so the far lane, whose ground is
    /// mostly behind grass, is on the screen too.
    /// </summary>
    public sealed class LaneMarks : MonoBehaviour
    {
        public Material Material;

        /// <summary>Half the width of a lane's ribbon: the band its three files walk in, and a little.</summary>
        public const float HalfWidth = 2.6f;
        private static readonly Color Gold = new Color(1.0f, 0.74f, 0.26f);
        private static readonly Color Pale = new Color(0.95f, 0.92f, 0.78f);

        private static readonly int ColorId = Shader.PropertyToID("_Color"), ShapeId = Shader.PropertyToID("_Shape"),
            FlowId = Shader.PropertyToID("_Flow"), FocusId = Shader.PropertyToID("_Focus"), ReachId = Shader.PropertyToID("_Reach"),
            OccludedId = Shader.PropertyToID("_Occluded");

        private MeshRenderer[] _ribbons;
        private float[] _lit;
        private MeshRenderer _disc, _beam, _caret;
        private Mesh _discMesh;
        private Vector3[] _discVerts;
        private const int DiscGrid = 20;
        private Vector2 _discAt = new Vector2(float.NaN, 0);
        private float _discR;
        private Ground _ground;
        private MaterialPropertyBlock _mpb;
        private float _shown;

        /// <summary>What is on the screen now, for the tests: the lane lit (or -1), and whether the disc is.</summary>
        public int Lit { get; private set; } = -1;
        public bool DiscShown => _disc != null && _disc.enabled;
        public bool Shown => _shown > 0.01f;

        private MeshRenderer Make(string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = false;
            return mr;
        }

        private void Build(Ground g)
        {
            foreach (Transform c in transform) Destroy(c.gameObject);
            _ground = g;
            _mpb = new MaterialPropertyBlock();
            _ribbons = new MeshRenderer[Tune.Lanes.Length];
            _lit = new float[Tune.Lanes.Length];
            for (int lane = 0; lane < Tune.Lanes.Length; lane++) _ribbons[lane] = Make($"lane {lane} ribbon", Ribbon(g, Tune.Lanes[lane]));

            _discMesh = new Mesh { name = "call-in disc" };
            _discMesh.MarkDynamic();
            int n = DiscGrid + 1;
            _discVerts = new Vector3[n * n];
            var uv = new Vector2[n * n];
            var tris = new int[DiscGrid * DiscGrid * 6];
            for (int j = 0, t = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    uv[j * n + i] = new Vector2(i / (float)DiscGrid * 2 - 1, j / (float)DiscGrid * 2 - 1);
                    if (i == DiscGrid || j == DiscGrid) continue;
                    int a = j * n + i;
                    tris[t++] = a; tris[t++] = a + n; tris[t++] = a + 1;
                    tris[t++] = a + 1; tris[t++] = a + n; tris[t++] = a + n + 1;
                }
            _discMesh.vertices = _discVerts; _discMesh.uv = uv; _discMesh.triangles = tris;
            _disc = Make("call-in disc", _discMesh);
            _discAt = new Vector2(float.NaN, 0);

            var quad = new Mesh { name = "beam" };
            quad.vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0.5f, 1, 0), new Vector3(-0.5f, 1, 0) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _beam = Make("beam", quad);
            _caret = Make("pointer caret", quad);
        }

        /// <summary>A strip of the lane's own ground, a metre a column, lifted a hand's breadth: it lies in the craters and over the banks.</summary>
        private static Mesh Ribbon(Ground g, double laneZ)
        {
            const int rows = 6;
            int cols = (int)(Tune.HalfLength * 2) + 1;
            var verts = new Vector3[cols * (rows + 1)];
            var uv = new Vector2[verts.Length];
            var tris = new int[(cols - 1) * rows * 6];
            for (int i = 0, t = 0; i < cols; i++)
            {
                double x = -Tune.HalfLength + i;
                for (int j = 0; j <= rows; j++)
                {
                    float v = j / (float)rows;
                    double z = laneZ + (v * 2 - 1) * HalfWidth;
                    verts[i * (rows + 1) + j] = Coords.World(x, z, (float)g.HeightAt(x, z) + 0.07f);
                    uv[i * (rows + 1) + j] = new Vector2((float)x, v);
                    if (i == cols - 1 || j == rows) continue;
                    int a = i * (rows + 1) + j, b = a + rows + 1;
                    tris[t++] = a; tris[t++] = a + 1; tris[t++] = b;
                    tris[t++] = b; tris[t++] = a + 1; tris[t++] = b + 1;
                }
            }
            var m = new Mesh { name = "lane ribbon" };
            m.vertices = verts; m.uv = uv; m.triangles = tris;
            m.RecalculateBounds();
            return m;
        }

        private void Paint(MeshRenderer r, Color c, float strength, float shape, float flow, float focus, float reach)
        {
            r.enabled = strength > 0.004f;
            if (!r.enabled) return;
            _mpb.SetColor(ColorId, new Color(c.r, c.g, c.b, strength));
            _mpb.SetFloat(ShapeId, shape);
            _mpb.SetFloat(FlowId, flow);
            _mpb.SetFloat(FocusId, focus);
            _mpb.SetFloat(ReachId, reach);
            // Through what stands in front of it: a ribbon and a beam half as strong, a disc (which is big) a third.
            _mpb.SetFloat(OccludedId, shape > 0.5f && shape < 1.5f ? 0.3f : 0.5f);
            r.SetPropertyBlock(_mpb);
        }

        private void Beam(MeshRenderer r, Transform cam, double x, double z, float width, float height, float strength)
        {
            float y = (float)_ground.HeightAt(x, z);
            // Upright, turned to the lens about its own axis only.
            var face = cam.forward; face.y = 0;
            r.transform.SetPositionAndRotation(Coords.World(x, z, y), Quaternion.LookRotation(face.sqrMagnitude > 1e-6f ? face : Vector3.forward, Vector3.up));
            r.transform.localScale = new Vector3(width, height, 1);
            Paint(r, Gold, strength, 2, 0, 0, 1);
        }

        /// <summary>
        /// Draw for this frame. <paramref name="card"/> null: nothing in hand,
        /// and what was shown fades out. <paramref name="radius"/> over zero:
        /// a call-in with that reach; else a squad, which comes in at
        /// <paramref name="entryX"/> and goes the way of <paramref name="advance"/>.
        /// </summary>
        public void Show(Ground g, Transform cam, bool armed, int lane, double x, float radius, double entryX, float advance, float dt)
        {
            if (Material == null || g == null) return;
            if (_ribbons == null || _ground != g) Build(g);
            _shown = Mathf.MoveTowards(_shown, armed ? 1f : 0f, dt / 0.18f);
            Lit = armed ? lane : -1;
            for (int i = 0; i < _ribbons.Length; i++)
            {
                _lit[i] = Mathf.MoveTowards(_lit[i], armed && i == lane ? 1f : 0f, dt / 0.12f);
                // The lane in hand in gold, the other one pale and faint: still there to be chosen.
                var c = Color.Lerp(Pale, Gold, _lit[i]);
                float strength = _shown * Mathf.Lerp(0.38f, 0.95f, _lit[i]);
                Paint(_ribbons[i], c, strength, 0, radius > 0 ? 0 : advance * _lit[i], (float)x, radius > 0 ? Mathf.Max(10f, radius * 1.2f) : 18f);
            }
            if (!armed || _shown < 0.02f)
            {
                _disc.enabled = _beam.enabled = _caret.enabled = false;
                return;
            }
            double laneZ = Tune.Lanes[lane];
            if (radius > 0)
            {
                PlaceDisc(g, x, laneZ, radius);
                Paint(_disc, Gold, 0.85f * _shown, 1, 0, 0, 1);
                Beam(_beam, cam, x, laneZ, 0.9f, 9f, 0.8f * _shown);
                _caret.enabled = false;
            }
            else
            {
                _disc.enabled = false;
                // Where the squad comes in, and where on its lane the pointer is.
                Beam(_beam, cam, entryX, laneZ, 1.6f, 11f, 0.75f * _shown);
                Beam(_caret, cam, x, laneZ, 0.5f, 3.2f, 0.7f * _shown);
            }
        }

        private void PlaceDisc(Ground g, double x, double z, float r)
        {
            if (!float.IsNaN(_discAt.x) && Mathf.Abs(_discAt.x - (float)x) < 0.05f && Mathf.Abs(_discAt.y - (float)z) < 0.05f && Mathf.Abs(_discR - r) < 0.01f) return;
            _discAt = new Vector2((float)x, (float)z); _discR = r;
            int n = DiscGrid + 1;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    double px = x + (i / (double)DiscGrid * 2 - 1) * r, pz = z + (j / (double)DiscGrid * 2 - 1) * r;
                    _discVerts[j * n + i] = Coords.World(px, pz, (float)g.HeightAt(px, pz) + 0.1f);
                }
            _discMesh.vertices = _discVerts;
            _discMesh.RecalculateBounds();
        }
    }
}
