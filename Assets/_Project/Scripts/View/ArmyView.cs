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
    /// soldier_vc) each man is one lit quad showing a posed frame. The motion
    /// is motion capture (CMU, retargeted in the bake): a walk, a jog and a
    /// crouched walk, each stepped by the distance the man has covered over
    /// that clip's measured stride, so his feet do not skate; an idle,
    /// played back and forth, each man on his own phase; kneeling, prone,
    /// the three aims after he fires, and two ways of lying dead. His speed
    /// is smoothed over a quarter second with a gap between starting and
    /// stopping, so he does not flicker between walking and standing, and he
    /// faces the way he is going (at rest, the enemy), so a squad falling back
    /// does not walk backwards. An interim until the Mixamo-driven 3D men of
    /// PLAN §12.3; it costs one quad a man and two draw calls, where 60
    /// skinned men were the plan's biggest WebGL risk.
    ///
    /// With 3D soldiers (SoldierBuilder: a skinned man per side on a Humanoid
    /// avatar, PLAN §12.3's own step), each man is a <see cref="SoldierFigure"/>
    /// instead, stepped by match time: he turns to where he is going, to the
    /// man he is shooting at, and at rest to the enemy; his gait follows his
    /// smoothed speed; his rifle kicks on every shot the sim fired. They take
    /// precedence over the sprites.
    ///
    /// Without either, the grey box: a capsule per man, coloured by side,
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
        /// <summary>3D soldiers, one prefab per side. When both are set, they are drawn.</summary>
        public SoldierFigure UsFigure;
        public SoldierFigure VcFigure;


        private readonly List<Transform> _men = new List<Transform>();
        private readonly List<MeshRenderer> _renderers = new List<MeshRenderer>();
        private Mesh _capsule;

        public int Drawn { get; private set; }
        /// <summary>Milliseconds the last Draw took: for the 3D men, stepping every Animator and its IK.</summary>
        public float LastDrawMs { get; private set; }
        /// <summary>The 3D man drawn for a man, or null (sprites, capsules, or not drawn yet).</summary>
        public SoldierFigure FigureOf(int id) => id >= 0 && id < _figures.Count ? _figures[id] : null;
        private readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();

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
            foreach (var f in _figures) if (f != null) Destroy(f.gameObject);
            _figures.Clear();
            _matchTime = -1f;
            System.Array.Clear(_vz, 0, _vz.Length);
            System.Array.Clear(_shot, 0, _shot.Length);
            System.Array.Clear(_pinnedAt, 0, _pinnedAt.Length);
            System.Array.Clear(_rounds, 0, _rounds.Length);
            for (int k = 0; k < _target.Length; k++) _target[k] = -1;
            System.Array.Clear(_walked, 0, _walked.Length);
            System.Array.Clear(_last, 0, _last.Length);
            System.Array.Clear(_speed, 0, _speed.Length);
            System.Array.Clear(_vx, 0, _vx.Length);
            System.Array.Clear(_moving, 0, _moving.Length);
            _speedTick = -1;
            for (int k = 0; k < _firedAt.Length; k++) _firedAt[k] = -1000;
            _eventCursor = 0;
        }

        private Sprites _us, _vc;
        private float[] _walked = new float[0];
        private float[] _speed = new float[0], _vx = new float[0], _vz = new float[0], _yaw = new float[0];
        /// <summary>Whom each man last fired at (-1 no one), and whether he fired since the last frame.</summary>
        private int[] _target = new int[0];
        private bool[] _shot = new bool[0];
        /// <summary>Whether the sim pinned each man since the last frame: rounds close enough to put him down.</summary>
        private bool[] _pinnedAt = new bool[0];
        /// <summary>Shots each man has fired since he last reloaded.</summary>
        private int[] _rounds = new int[0];
        /// <summary>A man reloads after this many shots, once he has not fired for LullTicks.</summary>
        public const int ReloadAfter = 6, LullTicks = 50;
        private readonly List<SoldierFigure> _figures = new List<SoldierFigure>();
        private float _matchTime = -1f;
        /// <summary>Match time each figure is owed, when it is stepped less than every frame.</summary>
        private float[] _pending = new float[0];
        private Camera _cam;
        /// <summary>A man drawn this many pixels tall or more is animated every frame.</summary>
        public const float NearPixels = 110f;
        /// <summary>How many figures were stepped in the last Draw.</summary>
        public int Stepped { get; private set; }
        private bool[] _moving = new bool[0], _faceLeft = new bool[0];
        private int _speedTick = -1;
        /// <summary>Metres a second: above this a standing man jogs rather than walks.</summary>
        public const float RunAbove = 1.7f;
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
            public readonly Dictionary<string, (int frames, float stride, float seconds)> Clips = new Dictionary<string, (int, float, float)>();
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
            var layout = JsonUtility.FromJson<Keys>(set.Layout.text);
            var keys = layout.variants;
            for (int i = 0; i < keys.Length; i++) sp.Frame[keys[i].key] = i;
            foreach (var c in layout.clips ?? new ClipInfo[0]) sp.Clips[c.name] = (c.frames, c.stride_m, c.seconds);
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
        [System.Serializable] private class ClipInfo { public string name; public int frames; public float stride_m; public float seconds; }
        [System.Serializable] private class Keys { public Key[] variants; public ClipInfo[] clips; }

        private static float Hash(int id, int salt)
        {
            float h = Mathf.Sin(id * 12.9898f + salt * 78.233f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        /// <summary>A gait clip's frame for this distance covered: whole strides are whole cycles.</summary>
        private static string Gait(Sprites sp, string clip, float walked)
        {
            if (!sp.Clips.TryGetValue(clip, out var c) || c.stride <= 0) return "stand";
            int f = (int)(Mathf.Repeat(walked / c.stride, 1f) * c.frames) % c.frames;
            return clip + f.ToString("00");
        }

        /// <summary>The idle, played forward and back so it never jumps, each man on his own phase.</summary>
        private static string Idle(Sprites sp, float t, int id)
        {
            if (!sp.Clips.TryGetValue("idle", out var c) || c.frames < 2) return "stand";
            float ph = Mathf.Repeat(t / (2f * Mathf.Max(0.5f, c.seconds)) + Hash(id, 4), 1f);
            float p = ph < 0.5f ? ph * 2f : 2f - ph * 2f;
            return "idle" + Mathf.RoundToInt(p * (c.frames - 1)).ToString("00");
        }

        /// <summary>
        /// What the view knows of each man beyond the sim's state: who fired and
        /// at whom, read once from the sim's events; and his speed and heading,
        /// from the sim's own steps, once a tick, smoothed over ~0.3 s and
        /// started and stopped at different speeds.
        /// </summary>
        private void Track(MatchDriver d)
        {
            var st = d.State;
            if (_walked.Length < st.Men.Count)
            {
                int n = st.Men.Count * 2;
                System.Array.Resize(ref _walked, n);
                System.Array.Resize(ref _last, n);
                System.Array.Resize(ref _speed, n);
                System.Array.Resize(ref _vx, n);
                System.Array.Resize(ref _vz, n);
                System.Array.Resize(ref _yaw, n);
                System.Array.Resize(ref _moving, n);
                System.Array.Resize(ref _faceLeft, n);
                System.Array.Resize(ref _shot, n);
                System.Array.Resize(ref _pinnedAt, n);
                System.Array.Resize(ref _rounds, n);
                int old = _firedAt.Length;
                System.Array.Resize(ref _firedAt, n);
                System.Array.Resize(ref _target, n);
                for (int k = old; k < n; k++) { _firedAt[k] = -1000; _target[k] = -1; }
            }
            if (_eventCursor > st.Events.Count) _eventCursor = 0;
            for (; _eventCursor < st.Events.Count; _eventCursor++)
            {
                var e = st.Events[_eventCursor];
                if (e.Kind == EventKind.Pinned && e.Id < _pinnedAt.Length) { _pinnedAt[e.Id] = true; continue; }
                if (e.Kind != EventKind.Fire || e.Id >= _firedAt.Length) continue;
                _firedAt[e.Id] = e.Tick;
                _target[e.Id] = e.Target ?? -1;
                if (e.Id < _rounds.Length) _rounds[e.Id]++;
                _shot[e.Id] = true;
            }
            if (st.Tick != _speedTick)
            {
                int ticks = _speedTick < 0 || st.Tick < _speedTick ? 1 : st.Tick - _speedTick;
                float k = 1f - Mathf.Exp(-ticks * (float)Tune.Dt / 0.3f);
                for (int i = 0; i < st.Men.Count; i++)
                {
                    var (dx, dz) = d.LastStep(i);
                    float v = (float)(System.Math.Sqrt(dx * dx + dz * dz) / Tune.Dt);
                    _speed[i] += (v - _speed[i]) * k;
                    _vx[i] += ((float)(dx / Tune.Dt) - _vx[i]) * k;
                    _vz[i] += ((float)(dz / Tune.Dt) - _vz[i]) * k;
                    if (_moving[i]) { if (_speed[i] < 0.15f) _moving[i] = false; }
                    else if (_speed[i] > 0.35f) _moving[i] = true;
                    // Face the way he is going; at rest, the enemy. A man moving
                    // mostly along the lane's depth keeps the facing he had.
                    if (_moving[i]) { if (Mathf.Abs(_vx[i]) > 0.25f) _faceLeft[i] = _vx[i] < 0; }
                    else _faceLeft[i] = st.Men[i].Side == Side.Vc;
                }
                _speedTick = st.Tick;
            }
        }

        private void DrawSprites(MatchDriver d, Ground g)
        {
            var st = d.State;
            Track(d);
            float viewTime = GameRoot.Instance != null ? GameRoot.Instance.ViewTime : Time.time;
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
                bool moving = _moving[i];
                bool aiming = !moving && st.Tick - _firedAt[i] <= AimTicks;
                string frame;
                if (!m.Alive) frame = Hash(m.Id, 1) < 0.5f ? "dead0" : "dead1";
                else if (m.Posture == Posture.Prone) frame = aiming ? "prone_aim" : "prone";
                else if (m.Posture == Posture.Crouched)
                    frame = moving ? Gait(sp, "crouch", _walked[i]) : aiming ? "kneel_aim" : "kneel";
                else if (moving) frame = Gait(sp, _speed[i] > RunAbove ? "run" : "walk", _walked[i]);
                else if (aiming) frame = "stand_aim";
                else frame = Idle(sp, viewTime, m.Id);
                // Older sets: the aim falls back to its posture, anything else to standing.
                if (!sp.Frame.TryGetValue(frame, out int vi) && !sp.Frame.TryGetValue(frame.Replace("_aim", ""), out vi)
                    && !sp.Frame.TryGetValue("stand", out vi)) continue;
                var v = sp.Species.Variants[vi];
                float scale = 0.94f + 0.1f * Hash(m.Id, 2);
                bool mirror = _faceLeft[i];
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

        /// <summary>The muzzle of a man's rifle, where there is a 3D man to have one.</summary>
        public bool TryMuzzle(int id, out Vector3 p)
        {
            var f = id >= 0 && id < _figures.Count ? _figures[id] : null;
            p = f != null ? f.MuzzlePosition : default;
            return f != null;
        }

        private void DrawFigures(MatchDriver d, Ground g)
        {
            var st = d.State;
            Track(d);
            // The men move on match time: still when paused, faster at 3x. A
            // jump (a fast-forward, a capture) settles them where the sim is.
            float now = (float)((st.Tick + d.Alpha) * Tune.Dt);
            bool jump = _matchTime >= 0 && now - _matchTime > 0.5f;
            float dt = _matchTime < 0 || now < _matchTime ? 0f : Mathf.Min(now - _matchTime, 0.25f);
            _matchTime = now;
            while (_figures.Count < st.Men.Count) _figures.Add(null);
            while (_pending.Length < _figures.Count) System.Array.Resize(ref _pending, _figures.Count * 2);
            _cam ??= Camera.main;
            float pxPerMetre = _cam != null ? Screen.height / (2f * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) : 1000f;
            int frame = Time.frameCount;
            Drawn = 0;
            Stepped = 0;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                var (x, z) = d.Position(i);
                bool aiming = m.Alive && st.Tick - _firedAt[i] <= AimTicks && (!_moving[i] || _speed[i] < RunAbove);
                // Where he faces, in the world's yaw (0 = +z, 90 = +x; the sim's z runs the other way).
                float yaw = _yaw[i];
                if (m.Alive)
                {
                    int tg = _target[i];
                    if (aiming && tg >= 0 && tg < st.Men.Count)
                    {
                        var (tx, tz) = d.Position(tg);
                        yaw = Mathf.Atan2((float)(tx - x), (float)-(tz - z)) * Mathf.Rad2Deg;
                    }
                    else if (_moving[i]) yaw = Mathf.Atan2(_vx[i], -_vz[i]) * Mathf.Rad2Deg;
                    else yaw = m.Side == Side.Us ? 90f : -90f;
                }
                var f = _figures[i];
                bool fresh = f == null;
                if (fresh)
                {
                    f = _figures[i] = Instantiate(m.Side == Side.Us ? UsFigure : VcFigure, transform);
                    f.name = $"man {i}";
                    f.transform.localScale = Vector3.one * (0.95f + 0.08f * Hash(m.Id, 2));
                    _yaw[i] = yaw;
                }
                // Turned at most 300 degrees a second of match time: a man pivots, he does not snap.
                _yaw[i] = jump ? yaw : Mathf.MoveTowardsAngle(_yaw[i], yaw, 300f * dt);
                var at = Coords.World(x, z, (float)g.HeightAt(x, z));
                f.transform.SetPositionAndRotation(at, Quaternion.Euler(0, _yaw[i], 0));
                int posture = m.Posture == Posture.Prone ? 2 : m.Posture == Posture.Crouched ? 1 : 0;
                float speed = _moving[i] ? _speed[i] : 0f;
                int death = (int)(Hash(m.Id, 1) * 16);
                if (_shot[i]) { f.Fire(); _shot[i] = false; }
                // Pinned while still: he flinches (a moving man keeps moving; the clip would slide him).
                if (_pinnedAt[i]) { if (!_moving[i] && !jump) f.React(); _pinnedAt[i] = false; }
                // A lull after shooting: he changes magazines (cosmetic; cut if the sim has him fire).
                if (m.Alive && !_moving[i] && _rounds[i] >= ReloadAfter && st.Tick - _firedAt[i] >= LullTicks)
                {
                    f.Reload();
                    _rounds[i] = 0;
                }
                Drawn++;
                if (fresh || jump) { f.Settle(speed, posture, aiming, !m.Alive, death); _pending[i] = 0; Stepped++; continue; }
                // PLAN §12.3's mitigations 2 and 3: a man small on the screen is
                // stepped every other frame and skinned with two bones a vertex;
                // one off it, every fourth. Staggered by id, so the work is even.
                int every = 1;
                if (_cam != null)
                {
                    var vp = _cam.WorldToViewportPoint(at + Vector3.up);
                    float px = vp.z > 0.1f ? 1.8f * pxPerMetre / vp.z : 0f;
                    bool seen = vp.z > 0.1f && vp.x > -0.08f && vp.x < 1.08f && vp.y > -0.1f && vp.y < 1.2f;
                    every = !seen ? 4 : px < NearPixels ? 2 : 1;
                    f.Body.quality = every == 1 ? SkinQuality.Bone4 : SkinQuality.Bone2;
                }
                _pending[i] += dt;
                if ((frame + i) % every != 0) continue;
                f.Step(_pending[i], speed, posture, aiming, !m.Alive, death);
                _pending[i] = 0;
                Stepped++;
            }
        }

        public void Draw(MatchDriver d, Ground g)
        {
            if (UsFigure != null && VcFigure != null)
            {
                _clock.Restart();
                DrawFigures(d, g);
                LastDrawMs = (float)_clock.Elapsed.TotalMilliseconds;
                return;
            }
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
