using System.Collections.Generic;
using LanesOfVietnam.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// The men, drawn where the simulation says they are, between its ticks.
    ///
    /// With baked soldiers (tools/blender/plant_bake.py, soldier_us and
    /// soldier_vc) each man is one lit quad showing a posed frame: standing,
    /// a six-frame stride chosen by the distance he has walked (a 1.45 m
    /// stride, as the three.js build measured, so his feet do not skate),
    /// kneeling, prone, or one of two ways of lying dead. The US face right and
    /// the VC left, mirrored. An interim until the Mixamo-driven 3D men of
    /// PLAN §12.3; it costs one quad a man and two draw calls, where 60
    /// skinned men were the plan's biggest WebGL risk.
    ///
    /// Without them, the grey box: a capsule per man, coloured by side,
    /// shaped by posture, fallen when dead.
    /// </summary>
    public sealed class ArmyView : MonoBehaviour
    {
        public Material UsMaterial;
        public Material VcMaterial;
        public Material DeadMaterial;
        /// <summary>Baked soldiers, one set per side. Empty: capsules.</summary>
        public PlantSet UsSoldiers;
        public PlantSet VcSoldiers;

        /// <summary>Metres a full stride covers: one walk cycle.</summary>
        public const float Stride = 1.45f;

        private readonly List<Transform> _men = new List<Transform>();
        private readonly List<MeshRenderer> _renderers = new List<MeshRenderer>();
        private Mesh _capsule;

        public int Drawn { get; private set; }

        private void Awake()
        {
            var probe = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _capsule = probe.GetComponent<MeshFilter>().sharedMesh;
            Destroy(probe);
        }

        /// <summary>A new match: every man drawn so far belonged to the old one.</summary>
        public void ResetView()
        {
            foreach (var t in _men) if (t != null) Destroy(t.gameObject);
            _men.Clear();
            _renderers.Clear();
            System.Array.Clear(_walked, 0, _walked.Length);
            System.Array.Clear(_last, 0, _last.Length);
            for (int k = 0; k < _firedAt.Length; k++) _firedAt[k] = -1000;
            _eventCursor = 0;
        }

        private Sprites _us, _vc;
        private float[] _walked = new float[0];
        /// <summary>The tick each man last fired, from the sim's events; -1 never.</summary>
        private int[] _firedAt = new int[0];
        private int _eventCursor;
        /// <summary>How long a man who has fired stays in his aim, in ticks: 3 s.</summary>
        public const int AimTicks = 60;
        private Vector2[] _last = new Vector2[0];

        private sealed class Sprites
        {
            public PlantSpecies Species;
            public readonly Dictionary<string, int> Frame = new Dictionary<string, int>();
            public Mesh Mesh;
            public readonly List<Vector3> P = new List<Vector3>();
            public readonly List<Vector2> Uv = new List<Vector2>();
            public readonly List<Vector2> Plant = new List<Vector2>();
            public readonly List<Color32> C = new List<Color32>();
            public readonly List<int> I = new List<int>();
        }

        private Sprites Load(PlantSet set, string name)
        {
            if (set.Layout == null || set.Material == null) return null;
            var sp = new Sprites { Species = new PlantSpecies(set) };
            var keys = JsonUtility.FromJson<Keys>(set.Layout.text).variants;
            for (int i = 0; i < keys.Length; i++) sp.Frame[keys[i].key] = i;
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            sp.Mesh = new Mesh { name = name };
            sp.Mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = sp.Mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = set.Material;
            mr.shadowCastingMode = ShadowCastingMode.On;
            return sp;
        }

        [System.Serializable] private class Key { public string key; }
        [System.Serializable] private class Keys { public Key[] variants; }

        private static float Hash(int id, int salt)
        {
            float h = Mathf.Sin(id * 12.9898f + salt * 78.233f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        private void DrawSprites(MatchDriver d, Ground g)
        {
            var st = d.State;
            if (_walked.Length < st.Men.Count)
            {
                System.Array.Resize(ref _walked, st.Men.Count * 2);
                System.Array.Resize(ref _last, st.Men.Count * 2);
                int old = _firedAt.Length;
                System.Array.Resize(ref _firedAt, st.Men.Count * 2);
                for (int k = old; k < _firedAt.Length; k++) _firedAt[k] = -1000;
            }
            // Who has fired, and when: the sim's own events, read once each.
            if (_eventCursor > st.Events.Count) _eventCursor = 0;
            for (; _eventCursor < st.Events.Count; _eventCursor++)
            {
                var e = st.Events[_eventCursor];
                if (e.Kind == EventKind.Fire && e.Id < _firedAt.Length) _firedAt[e.Id] = e.Tick;
            }
            foreach (var sp in new[] { _us, _vc })
            {
                sp.P.Clear(); sp.Uv.Clear(); sp.Plant.Clear(); sp.C.Clear(); sp.I.Clear();
            }
            Drawn = 0;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                var sp = m.Side == Side.Us ? _us : _vc;
                var (x, z) = d.Position(i);
                var here = new Vector2((float)x, (float)z);
                float step = _last[i] == Vector2.zero ? 0 : Vector2.Distance(here, _last[i]);
                if (step < 1f) _walked[i] += step;          // a new match or a respawn is not a stride
                _last[i] = here;

                // A man who has just fired, and is not on the move, holds his aim:
                // the fighting reads as men shooting, not men standing about.
                bool moving = d.StepLength(i) > 0.02;
                bool aiming = !moving && st.Tick - _firedAt[i] <= AimTicks;
                string frame;
                if (!m.Alive) frame = Hash(m.Id, 1) < 0.5f ? "dead0" : "dead1";
                else if (m.Posture == Posture.Prone) frame = aiming ? "prone_aim" : "prone";
                else if (m.Posture == Posture.Crouched) frame = aiming ? "kneel_aim" : "kneel";
                else frame = moving ? "walk" + (int)(Mathf.Repeat(_walked[i] / Stride, 1f) * 6) % 6
                           : aiming ? "stand_aim" : "stand";
                // A set baked before the aim frames existed falls back to the plain posture.
                if (!sp.Frame.TryGetValue(frame, out int vi) && !sp.Frame.TryGetValue(frame.Replace("_aim", ""), out vi)) continue;
                var v = sp.Species.Variants[vi];
                float scale = 0.94f + 0.1f * Hash(m.Id, 2);
                bool mirror = m.Side != Side.Us;
                var root = Coords.World(x, z, (float)g.HeightAt(x, z) - 0.02f);
                float w = v.Size.x * scale, h = v.Size.y * scale;
                float x0 = -v.Root.x * w, x1 = (1 - v.Root.x) * w, y0 = -v.Root.y * h, y1 = (1 - v.Root.y) * h;
                if (mirror) { float t = x0; x0 = -x1; x1 = -t; }
                float u0 = mirror ? v.Uv.xMax : v.Uv.xMin, u1 = mirror ? v.Uv.xMin : v.Uv.xMax;
                float b = 0.9f + 0.15f * Hash(m.Id, 3);
                var c = new Color32((byte)(b * 240), (byte)(b * 240), (byte)(b * 240), 0);
                int k = sp.P.Count;
                sp.P.Add(root + new Vector3(x0, y0, 0)); sp.P.Add(root + new Vector3(x1, y0, 0));
                sp.P.Add(root + new Vector3(x1, y1, 0)); sp.P.Add(root + new Vector3(x0, y1, 0));
                sp.Uv.Add(new Vector2(u0, v.Uv.yMin)); sp.Uv.Add(new Vector2(u1, v.Uv.yMin));
                sp.Uv.Add(new Vector2(u1, v.Uv.yMax)); sp.Uv.Add(new Vector2(u0, v.Uv.yMax));
                float ms = mirror ? -1 : 1;
                sp.Plant.Add(new Vector2(0, ms)); sp.Plant.Add(new Vector2(0, ms));
                sp.Plant.Add(new Vector2(1, ms)); sp.Plant.Add(new Vector2(1, ms));
                for (int n = 0; n < 4; n++) sp.C.Add(c);
                sp.I.Add(k); sp.I.Add(k + 3); sp.I.Add(k + 2); sp.I.Add(k); sp.I.Add(k + 2); sp.I.Add(k + 1);
                Drawn++;
            }
            foreach (var sp in new[] { _us, _vc })
            {
                sp.Mesh.Clear();
                if (sp.P.Count == 0) continue;
                sp.Mesh.SetVertices(sp.P);
                sp.Mesh.SetUVs(0, sp.Uv);
                sp.Mesh.SetUVs(1, sp.Plant);
                sp.Mesh.SetColors(sp.C);
                sp.Mesh.SetTriangles(sp.I, 0, false);
                sp.Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            }
        }

        public void Draw(MatchDriver d, Ground g)
        {
            if (_us == null && UsSoldiers.Layout != null) _us = Load(UsSoldiers, "soldiers us");
            if (_vc == null && VcSoldiers.Layout != null) _vc = Load(VcSoldiers, "soldiers vc");
            if (_us != null && _vc != null) { DrawSprites(d, g); return; }

            var st = d.State;
            while (_men.Count < st.Men.Count)
            {
                var go = new GameObject($"man {_men.Count}");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _capsule;
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.On;
                _men.Add(go.transform);
                _renderers.Add(mr);
            }

            Drawn = 0;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                var t = _men[i];
                var (x, z) = d.Position(i);
                float y = (float)g.HeightAt(x, z);
                var mr = _renderers[i];
                mr.sharedMaterial = !m.Alive ? DeadMaterial : m.Side == Side.Us ? UsMaterial : VcMaterial;

                // Unity's capsule is 2 m tall, 1 m across, centred on its pivot.
                float face = m.Side == Side.Us ? 90f : -90f;
                if (!m.Alive)
                {
                    t.SetPositionAndRotation(Coords.World(x, z, y + 0.18f), Quaternion.Euler(0, face, 90));
                    t.localScale = new Vector3(0.36f, 0.85f, 0.36f);
                }
                else if (m.Posture == Posture.Prone)
                {
                    t.SetPositionAndRotation(Coords.World(x, z, y + 0.2f), Quaternion.Euler(0, face, 90));
                    t.localScale = new Vector3(0.4f, 0.85f, 0.4f);
                }
                else
                {
                    float hgt = m.Posture == Posture.Crouched ? 1.15f : 1.77f;
                    t.SetPositionAndRotation(Coords.World(x, z, y + hgt * 0.5f), Quaternion.Euler(0, face, 0));
                    t.localScale = new Vector3(0.45f, hgt * 0.5f, 0.45f);
                }
                Drawn++;
            }
        }
    }
}
