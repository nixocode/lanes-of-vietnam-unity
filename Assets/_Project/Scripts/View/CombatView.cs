using System;
using System.Collections.Generic;
using LanesOfVietnam.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// The fighting, drawn from the simulation's own events (PLAN §12.12 step 7):
    /// every shot a muzzle flash, a third of them a tracer — red for the US,
    /// green for the VC and NVA, as their ammunition was — and where a round
    /// misses, dirt kicked up beside the man it missed; every shell a flash, a
    /// fireball, earth thrown up and a column of smoke that drifts on the wind;
    /// every smoke screen a bank of white.
    ///
    /// Nothing here has a clock of its own. Each effect is a function of how
    /// long ago its event happened, read off the simulation's ticks and the
    /// driver's interpolation, so what is drawn is exactly what the simulation
    /// did — a man is never shown firing on a tick where he did not — and a
    /// frozen capture renders the same frame every time. Shots within one tick
    /// are spread across it by a hash of their place in the log, so a volley
    /// is not a single strobe.
    ///
    /// Two meshes rebuilt each frame: the light (additive, HDR, carried by the
    /// bloom) and the smoke (alpha, sorted far to near, lit by the scene's sun
    /// and sky). A few hundred quads; two draw calls.
    /// </summary>
    public sealed class CombatView : MonoBehaviour
    {
        public Material Glow;
        public Material Smoke;
        /// <summary>Contact shadows and scorch marks (LOV/Ground Mark); optional.</summary>
        public Material Marks;

        /// <summary>How fast a tracer is drawn travelling (m/s): a 5.56 or 7.62 round at combat range, slowed a little so the eye can follow it.</summary>
        public const float TracerSpeed = 620f;
        /// <summary>
        /// The share of an automatic weapon's rounds drawn as tracers: belts were
        /// loaded one in five. Riflemen fire none (a rifleman's magazine held
        /// none), so a firefight is sparks and dust, not beams: the owner's
        /// second playtest, "more of a spark not a laser".
        /// </summary>
        public const double TracerShare = 0.2;
        /// <summary>Rounds in a burst from an automatic weapon, and the time between them.</summary>
        public const int BurstRounds = 3;
        public const float BurstGap = 0.085f;
        /// <summary>The share of misses that glance off something hard and go on, lit.</summary>
        public const double RicochetShare = 0.18;
        private const double MaxAge = 11.0;

        // Hot enough to bloom, not so hot that the tonemapper bleaches the
        // colour out: at 26x the red came out a white line.
        private static readonly Color UsTracer = new Color(1.0f, 0.09f, 0.025f) * 3.2f;
        private static readonly Color VcTracer = new Color(0.14f, 1.0f, 0.16f) * 2.6f;
        private static readonly Color Flash = new Color(1.0f, 0.70f, 0.34f);

        private GameRoot _root;
        private MeshFilter _glowMf, _smokeMf;
        private Mesh _glowMesh, _smokeMesh;
        private readonly List<Vector3> _gp = new List<Vector3>();
        private readonly List<Color> _gc = new List<Color>();
        private readonly List<Vector4> _guv = new List<Vector4>();
        private readonly List<int> _gi = new List<int>();
        private readonly List<Puff> _puffs = new List<Puff>();
        private readonly List<Vector3> _sp = new List<Vector3>();
        private readonly List<Color> _sc = new List<Color>();
        private readonly List<Vector4> _suv = new List<Vector4>();
        private readonly List<int> _si = new List<int>();
        private readonly Light[] _lights = new Light[4];
        private Mesh _markMesh;
        private readonly List<Vector3> _mp = new List<Vector3>();
        private readonly List<Color> _mc = new List<Color>();
        private readonly List<Vector4> _muv = new List<Vector4>();
        private readonly List<int> _mi = new List<int>();
        private readonly List<(Vector3 pos, float intensity)> _flashes = new List<(Vector3, float)>();
        private Vector3 _right, _up, _camPos;
        private int _shaken;

        /// <summary>What was drawn last frame, for the tests and the log.</summary>
        public int Tracers { get; private set; }
        public int Flashes { get; private set; }
        public int Puffs { get; private set; }
        public int Explosions { get; private set; }

        private struct Puff
        {
            public Vector3 Pos;
            public float Size, Rot, Seed, Density;
            public Color Col;
            public float Dist;
        }

        private void Awake()
        {
            _glowMf = Child("combat light", Glow, out _glowMesh);
            _smokeMf = Child("combat smoke", Smoke, out _smokeMesh);
            if (Marks != null) Child("ground marks", Marks, out _markMesh);
            for (int i = 0; i < _lights.Length; i++)
            {
                var go = new GameObject("shell flash");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.62f, 0.3f);
                l.range = 45f;
                l.shadows = LightShadows.None;
                l.enabled = false;
                _lights[i] = l;
            }
        }

        private MeshFilter Child(string name, Material m, out Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            mf.sharedMesh = mesh;
            return mf;
        }

        private void LateUpdate()
        {
            _root ??= GameRoot.Instance;
            if (_root == null || _root.Driver == null || _root.CameraRig == null) return;
            var d = _root.Driver;
            var st = d.State;
            var cam = _root.CameraRig.Camera.transform;
            _right = cam.right; _up = cam.up; _camPos = cam.position;
            Clear();

            double now = (st.Tick - 1 + d.Alpha) * Tune.Dt;
            var ev = st.Events;
            for (int i = ev.Count - 1; i >= 0; i--)
            {
                var e = ev[i];
                if (now - e.Tick * Tune.Dt > MaxAge) break;
                double age = now - (e.Tick - 1 + Hash(i, 0)) * Tune.Dt;
                if (age < 0) continue;
                switch (e.Kind)
                {
                    case EventKind.Fire: Shot(ev, i, (float)age); break;
                    case EventKind.Shell: Explosion(e, (float)age, 1f); break;
                    case EventKind.TrapSprung: Explosion(e, (float)age, 0.45f); break;
                    case EventKind.GrenadeThrown: GrenadeFlight(e, (float)age); break;
                    case EventKind.GrenadeBlast: GrenadeBurst(e, (float)age); break;
                }
            }
            // A blast is felt: the camera shakes with every new shell and grenade, by how near it is.
            if (_shaken > ev.Count) _shaken = 0;
            for (; _shaken < ev.Count; _shaken++)
            {
                var e = ev[_shaken];
                if (e.X == null || (e.Kind != EventKind.Shell && e.Kind != EventKind.GrenadeBlast && e.Kind != EventKind.TrapSprung)) continue;
                if (st.Tick - e.Tick > 3) continue;                 // a fast-forward's backlog is not felt
                float away = Mathf.Abs((float)e.X.Value - _root.CameraRig.X);
                _root.CameraRig.Shake((e.Kind == EventKind.Shell ? 1f : 0.55f) * Mathf.Clamp01(1.2f - away / 60f));
            }
            SmokeScreens(st, now);
            if (_markMesh != null) GroundMarks(st, now);
            Upload();
            Lights();
        }

        // --- shots -------------------------------------------------------------------

        private Vector3 Chest(Man m)
        {
            var (x, z) = _root.Driver.Position(m.Id);
            float h = m.Posture == Posture.Prone ? 0.3f : m.Posture == Posture.Crouched ? 0.95f : 1.35f;
            return Coords.World(x, z, (float)_root.Ground.HeightAt(x, z) + h);
        }

        private void Shot(IReadOnlyList<SimEvent> ev, int i, float age)
        {
            var e = ev[i];
            var men = _root.Driver.State.Men;
            if (e.Target == null || e.Id >= men.Count || e.Target.Value >= men.Count) return;
            var shooter = men[e.Id];
            var target = men[e.Target.Value];
            // The simulation writes the kill straight after the shot that made it.
            bool hit = i + 1 < ev.Count && ev[i + 1].Kind == EventKind.Kill && ev[i + 1].Id == target.Id && ev[i + 1].Tick == e.Tick;

            var a = Chest(shooter);
            var b = Chest(target);
            var aim = (b - a).normalized;
            // The rifle's own muzzle where there is a 3D man holding one.
            var muzzle = _root.ArmyView != null && _root.ArmyView.TryMuzzle(shooter.Id, out var mz) ? mz : a + aim * 0.6f;
            // One sim shot is one round from a rifle, a short burst from an
            // automatic weapon (the same men AudioView gives the burst sounds).
            bool auto = AudioView.Automatic(shooter);
            int rounds = auto ? BurstRounds : 1;
            for (int r = 0; r < rounds; r++)
            {
                float ra = age - r * BurstGap;
                if (ra < 0) break;
                Round(i * 8 + r, ra, muzzle, b, aim, hit && r == rounds - 1, auto, shooter.Side);
            }
        }

        /// <summary>One round: the spark at the muzzle, now and then a tracer, and what it does where it lands.</summary>
        private void Round(int s, float age, Vector3 muzzle, Vector3 b, Vector3 aim, bool hit, bool auto, Side side)
        {
            Vector3 end;
            if (hit) end = b;
            else
            {
                // A miss goes into the ground by him: beside, short or long.
                var flat = new Vector3(aim.x, 0, aim.z).normalized;
                var across = new Vector3(-flat.z, 0, flat.x);
                var p = b + across * (float)(Hash(s, 1) * 3.6 - 1.8) + flat * (float)(Hash(s, 2) * 6.0 - 1.5);
                var (sx, sz) = (p.x, -p.z);
                end = Coords.World(sx, sz, (float)_root.Ground.HeightAt(sx, sz) + 0.05f);
            }
            float dist = Vector3.Distance(muzzle, end);
            var dir = (end - muzzle) / Mathf.Max(dist, 0.01f);

            // The muzzle: a spark for two frames, a breath of smoke after it.
            if (age < 0.04f)
            {
                float k = 1 - age / 0.04f;
                AddGlow(muzzle + dir * 0.12f, 0.17f + 0.1f * (float)Hash(s, 3), Flash * (8f * k), 0, (float)Hash(s, 4));
                Flashes++;
            }
            if (age < 0.7f)
            {
                float f = 1 - age / 0.7f;
                AddPuff(muzzle + dir * (0.25f + 0.5f * age) + Vector3.up * (0.25f * age), 0.16f + 0.5f * age,
                        new Color(0.62f, 0.62f, 0.6f, 0.2f * f * f), (float)Hash(s, 9), 1f);
            }
            // Tracers: automatic weapons only, one round in five, thin and brief.
            if (auto && Hash(s, 5) < TracerShare)
            {
                float head = TracerSpeed * age;
                float tail = Mathf.Max(0, head - 3.5f);
                float h = Mathf.Min(head, dist);
                if (h > tail)
                {
                    AddStreak(muzzle + dir * tail, muzzle + dir * h, 0.04f, side == Side.Us ? UsTracer : VcTracer, (float)Hash(s, 6));
                    Tracers++;
                }
            }
            float ai = age - dist / TracerSpeed;
            if (ai < 0) return;
            if (hit)
            {
                // The round into a man: a dark red mist out of him along its path, gone in half a second.
                if (ai < 0.55f)
                {
                    float f = 1 - ai / 0.55f;
                    for (int k = 0; k < 3; k++)
                        AddPuff(b + dir * (0.15f + (0.5f + 0.5f * k) * ai) + Vector3.up * (0.05f * k - 0.3f * ai * ai),
                                0.16f + (0.5f + 0.2f * k) * ai, new Color(0.22f, 0.025f, 0.02f, 0.7f * f), (float)Hash(s, 7 + k), 1f);
                }
                return;
            }
            // Into the earth: a hard little kick of dirt at once, then the dust it leaves.
            if (ai < 0.22f)
            {
                float f = 1 - ai / 0.22f;
                for (int k = 0; k < 3; k++)
                {
                    float az = (float)(Hash(s, 20 + k) * Math.PI * 2);
                    var v = new Vector3(Mathf.Cos(az) * 1.2f, 4.5f + 2.5f * (float)Hash(s, 23 + k), Mathf.Sin(az) * 1.2f) - dir * 1.5f;
                    AddPuff(end + v * ai + Vector3.down * (9.8f * ai * ai), 0.12f + 0.25f * ai, new Color(0.13f, 0.1f, 0.07f, 0.95f * f), (float)Hash(s, 26 + k), 1f);
                }
            }
            if (ai < 1.5f)
            {
                float f = 1 - ai / 1.5f;
                for (int k = 0; k < 2; k++)
                    AddPuff(end + Vector3.up * (0.1f + 0.5f * ai + 0.15f * k), 0.3f + (0.9f + 0.3f * k) * ai,
                            new Color(0.20f, 0.17f, 0.13f, 0.75f * f * f), (float)Hash(s, 8 + k), 1f);
            }
            // Now and then it glances off and goes on, lit: a ricochet.
            if (Hash(s, 30) < RicochetShare && ai < 0.16f)
            {
                var flat = new Vector3(dir.x, 0, dir.z).normalized;
                var off = (flat * 0.8f + Vector3.up * (0.35f + 0.5f * (float)Hash(s, 31))
                           + new Vector3(-flat.z, 0, flat.x) * (float)(Hash(s, 32) - 0.5)).normalized;
                float head = 190f * ai, tail = Mathf.Max(0, head - 2.2f);
                AddStreak(end + off * tail, end + off * head, 0.03f, new Color(1f, 0.62f, 0.25f) * (3.5f * (1 - ai / 0.16f)), (float)Hash(s, 33));
                if (ai < 0.05f) AddGlow(end + Vector3.up * 0.05f, 0.22f, Flash * (6f * (1 - ai / 0.05f)), 0, (float)Hash(s, 34));
            }
        }

        // --- grenades ---------------------------------------------------------------------

        /// <summary>
        /// A grenade from the thrower's hand to where it lands, on a lob, then
        /// lying there until the blast event takes over: a dark speck, which is
        /// all a grenade is at this distance, and it reads because it moves.
        /// </summary>
        private void GrenadeFlight(SimEvent e, float age)
        {
            const float flight = 1.1f;
            float fuse = Tune.FragFuse * (float)Tune.Dt;
            if (age > fuse || e.X == null) return;
            var men = _root.Driver.State.Men;
            if (e.Id >= men.Count) return;
            var from = Chest(men[e.Id]) + Vector3.up * 0.5f;
            double lx = e.X.Value, lz = e.Z.Value;
            var to = Coords.World(lx, lz, (float)_root.Ground.HeightAt(lx, lz) + 0.08f);
            Vector3 p;
            if (age < flight)
            {
                float t = age / flight;
                float apex = 2.5f + 0.12f * Vector3.Distance(from, to);
                p = Vector3.Lerp(from, to, t) + Vector3.up * (4f * apex * t * (1 - t));
            }
            else p = to;
            AddPuff(p, 0.22f, new Color(0.035f, 0.035f, 0.03f, 1f), 0.5f, 1f);
        }

        /// <summary>
        /// A fragmentation grenade going off: a white-hot flash a frame long,
        /// fragments out in every direction, the earth it lifted thrown three
        /// or four metres up and falling back, a ring of dust along the ground,
        /// and grey-black smoke that hangs for seconds. The owner's second
        /// playtest: "thrown grenades don't explode". They did, at a third of a
        /// shell's scale: two stars the size of a muzzle flash.
        /// </summary>
        private void GrenadeBurst(SimEvent e, float age)
        {
            if (e.X == null || e.Z == null) return;
            double x = e.X.Value, z = e.Z.Value;
            var p0 = Coords.World(x, z, (float)_root.Ground.HeightAt(x, z));
            int seed = e.Id * 6151 + e.Tick;
            Explosions++;

            if (age < 0.09f)
            {
                float k = 1 - age / 0.09f;
                // A soft ball of light with a small hard star in it (the star alone, large, read as a paper cut-out).
                AddGlow(p0 + Vector3.up * 0.5f, 2.6f + 16f * age, Flash * (16f * k), 2, (float)Hash(seed, 0));
                AddGlow(p0 + Vector3.up * 0.5f, 1.1f + 5f * age, Flash * (20f * k), 0, (float)Hash(seed, 2));
            }
            if (age < 0.32f)
            {
                float t = age / 0.32f;
                var col = Color.Lerp(new Color(1f, 0.6f, 0.25f) * 8f, new Color(0.7f, 0.2f, 0.06f) * 1.5f, t) * (1 - t);
                AddGlow(p0 + Vector3.up * (0.6f + 1.6f * age), 1.3f + 3.2f * age, col, 2, (float)Hash(seed, 1));
            }
            if (age < 0.25f) _flashes.Add((p0 + Vector3.up * 1.2f, 55f * Mathf.Exp(-age * 20f)));
            // Fragments: hot for a tenth of a second, out along the ground and up.
            if (age < 0.12f)
                for (int k = 0; k < 10; k++)
                {
                    float az = (float)(Hash(seed, 100 + k) * Math.PI * 2), el = 0.1f + 0.9f * (float)Hash(seed, 110 + k);
                    var d = new Vector3(Mathf.Cos(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(az) * Mathf.Cos(el));
                    float head = 70f * age, tail = Mathf.Max(0, head - 1.6f);
                    AddStreak(p0 + Vector3.up * 0.2f + d * tail, p0 + Vector3.up * 0.2f + d * head, 0.03f,
                              new Color(1f, 0.7f, 0.3f) * (4f * (1 - age / 0.12f)), (float)Hash(seed, 120 + k));
                }
            // The earth it lifted.
            for (int k = 0; k < 14; k++)
            {
                const float life = 1.5f;
                if (age > life) break;
                float az = (float)(Hash(seed, 10 + k) * Math.PI * 2);
                float vh = 1.2f + 3.2f * (float)Hash(seed, 20 + k);
                float vv = 5.5f + 4.5f * (float)Hash(seed, 30 + k);
                var pos = p0 + new Vector3(Mathf.Cos(az) * vh * age, vv * age - 4.9f * age * age, Mathf.Sin(az) * vh * age);
                if (pos.y < p0.y - 0.15f) continue;
                float f = 1 - age / life;
                AddPuff(pos, 0.45f + 0.9f * age, new Color(0.08f, 0.06f, 0.045f, 0.95f * f), (float)Hash(seed, 40 + k), 1f);
            }
            // Dust along the ground, in a ring.
            if (age < 2.2f)
                for (int k = 0; k < 8; k++)
                {
                    float az = k * 0.785f + (float)Hash(seed, 130 + k);
                    float rr = 4.2f * (1 - Mathf.Exp(-age * 3.5f));
                    float f = 1 - age / 2.2f;
                    AddPuff(p0 + new Vector3(Mathf.Cos(az) * rr, 0.25f + 0.3f * age, Mathf.Sin(az) * rr), 1.0f + 1.3f * age,
                            new Color(0.24f, 0.2f, 0.15f, 0.5f * f * f), (float)Hash(seed, 140 + k), 1f);
                }
            // The smoke it leaves: grey-black, rising and leaning downwind.
            for (int k = 0; k < 8; k++)
            {
                float a = age - (0.06f + 0.05f * k);
                const float life = 5.5f;
                if (a < 0 || a > life) continue;
                float rise = 0.8f * a + 1.6f * (1 - Mathf.Exp(-a * 1.8f));
                var off = new Vector3((float)(Hash(seed, 50 + k) - 0.5) * 1.6f + 0.6f * a, rise * (0.5f + 0.6f * (float)Hash(seed, 60 + k)),
                                      (float)(Hash(seed, 70 + k) - 0.5) * 1.6f);
                float op = Mathf.SmoothStep(0, 1, a / 0.25f) * Mathf.Pow(1 - a / life, 1.5f) * 0.6f;
                var col = Color.Lerp(new Color(0.2f, 0.19f, 0.18f), new Color(0.07f, 0.06f, 0.05f), Mathf.Exp(-a * 0.9f));
                col.a = op;
                AddPuff(p0 + Vector3.up * 0.6f + off, 1.5f + 0.8f * a, col, (float)Hash(seed, 80 + k), 1f);
            }
        }

        // --- shells and traps -------------------------------------------------------------

        private void Explosion(SimEvent e, float age, float scale)
        {
            if (e.X == null || e.Z == null) return;
            double x = e.X.Value, z = e.Z.Value;
            var p0 = Coords.World(x, z, (float)_root.Ground.HeightAt(x, z));
            int seed = e.Id * 7919 + e.Tick;
            Explosions++;

            if (age < 0.14f)
            {
                float k = 1 - age / 0.14f;
                // Intensity with size too: at a grenade's scale the full flash
                // saturated into a hard white shape.
                AddGlow(p0 + Vector3.up * 1.2f * scale, (4f + 22f * age) * scale, Flash * (24f * k * scale), 0, (float)Hash(seed, 0));
            }
            if (age < 0.6f)
            {
                float t = age / 0.6f;
                var col = Color.Lerp(new Color(1f, 0.55f, 0.22f) * 9f, new Color(0.75f, 0.2f, 0.06f) * 2f, t) * (1 - t);
                AddGlow(p0 + Vector3.up * (1.1f + 2.5f * age) * scale, (1.8f + 5f * age) * scale, col, 2, (float)Hash(seed, 1));
            }
            if (age < 0.3f) _flashes.Add((p0 + Vector3.up * 2f, 90f * Mathf.Exp(-age * 18f) * scale));

            // Earth thrown up: a handful of clods on their arcs.
            for (int k = 0; k < 8; k++)
            {
                float life = 1.7f;
                if (age > life) break;
                float az = (float)(Hash(seed, 10 + k) * Math.PI * 2);
                float vh = (2f + 5f * (float)Hash(seed, 20 + k)) * scale;
                float vv = (7f + 7f * (float)Hash(seed, 30 + k)) * Mathf.Sqrt(scale);
                var pos = p0 + new Vector3(Mathf.Cos(az) * vh * age, vv * age - 4.9f * age * age, Mathf.Sin(az) * vh * age);
                if (pos.y < p0.y - 0.2f) continue;
                float f = 1 - age / life;
                AddPuff(pos, (0.9f + 1.6f * age) * scale, new Color(0.07f, 0.055f, 0.04f, 0.9f * f), (float)Hash(seed, 40 + k), 1f);
            }
            // The smoke: rising, spreading and drifting downwind (+x), thinning out.
            for (int k = 0; k < 11; k++)
            {
                float a = age - (0.12f + 0.07f * k);
                float life = 9f * Mathf.Sqrt(scale);
                if (a < 0 || a > life) continue;
                float rise = 1.1f * a + 2.2f * (1 - Mathf.Exp(-a * 1.5f));
                var off = new Vector3((float)(Hash(seed, 50 + k) - 0.5) * 2.5f + 0.7f * a, rise * (0.6f + 0.6f * (float)Hash(seed, 60 + k)),
                                      (float)(Hash(seed, 70 + k) - 0.5) * 2.5f) * scale;
                // HE on earth: brown-grey, dark while it is still mostly dirt, thinning as it rises.
                float op = Mathf.SmoothStep(0, 1, a / 0.4f) * Mathf.Pow(1 - a / life, 1.6f) * 0.62f;
                float dirt = Mathf.Exp(-a * 0.6f);
                var col = Color.Lerp(new Color(0.17f, 0.16f, 0.15f), new Color(0.09f, 0.075f, 0.06f), dirt);
                col.a = op;
                AddPuff(p0 + Vector3.up * scale + off, (2.2f + 1.25f * a) * scale, col,
                        (float)Hash(seed, 80 + k), 1f);
            }
        }

        // --- smoke screens -------------------------------------------------------------------

        private void SmokeScreens(SimState st, double now)
        {
            foreach (var ar in st.Areas)
            {
                if (ar.Kind != AreaKind.Smoke) continue;
                float left = (float)(ar.Ticks * Tune.Dt);
                float density = Mathf.Clamp01(left / 4f);
                int n = 16 + (int)(ar.Radius * 2);
                for (int k = 0; k < n; k++)
                {
                    int seed = ar.Id * 1000 + k;
                    double ang = Hash(seed, 0) * Math.PI * 2, r = Math.Sqrt(Hash(seed, 1)) * ar.Radius;
                    float t = (float)now;
                    double x = ar.X + Math.Cos(ang) * r + Math.Sin(t * 0.13 + k) * 1.2;
                    double z = ar.Z + Math.Sin(ang) * r;
                    float y = (float)_root.Ground.HeightAt(x, z) + 1.2f + 2.2f * (float)Hash(seed, 2) + 0.5f * Mathf.Sin(t * 0.21f + k);
                    // Puffs sized to the screen: a fire mission's 14 m bank or a squad's 3.5 m canister.
                    float size = (float)ar.Radius * (0.32f + 0.2f * (float)Hash(seed, 3));
                    y = (float)_root.Ground.HeightAt(x, z) + size * (0.25f + 0.35f * (float)Hash(seed, 2));
                    AddPuff(Coords.World(x, z, y), size, new Color(0.52f, 0.52f, 0.50f, 0.55f * density),
                            (float)Hash(seed, 4), 1f);
                }
            }
        }

        // --- marks on the ground ------------------------------------------------------------

        /// <summary>
        /// A soft shadow under every man, living or dead, and a scorch wherever
        /// a shell or a grenade went off, for the rest of the match. The scorch
        /// darkens in as the blast clears, so it never pops.
        /// </summary>
        private void GroundMarks(SimState st, double now)
        {
            _mp.Clear(); _mc.Clear(); _muv.Clear(); _mi.Clear();
            var d = _root.Driver;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                var (x, z) = d.Position(i);
                bool flat = !m.Alive || m.Posture == Posture.Prone;
                float w = flat ? 1.9f : m.Posture == Posture.Crouched ? 1.0f : 0.9f;
                float h = flat ? 0.8f : 0.55f;
                Flat(x, z, w, h, new Color(0.45f, 0.44f, 0.42f, 0.8f), i * 0.013f, 0f);
            }
            var ev = st.Events;
            for (int i = 0; i < ev.Count; i++)
            {
                var e = ev[i];
                if (e.Kind == EventKind.Kill && e.Id < st.Men.Count)
                {
                    // Blood: where he lies (the body, not the sim's point: a fall
                    // carries him), soaking out over a few seconds, and it stays.
                    float since = (float)(now - e.Tick * Tune.Dt);
                    if (since < 0.6f) continue;
                    var (bx, bz) = d.Position(e.Id);
                    if (_root.ArmyView != null && _root.ArmyView.TryBody(e.Id, out var body)) { bx = body.x; bz = Coords.SimZ(body.z); }
                    float soak = Mathf.SmoothStep(0, 1, (since - 0.6f) / 5f);
                    float pool = 0.45f + 0.75f * soak + 0.3f * (float)Hash(e.Id, 95);
                    Flat(bx + (Hash(e.Id, 96) - 0.5) * 0.3, bz + (Hash(e.Id, 97) - 0.5) * 0.3, pool, pool * 0.8f,
                         new Color(0.34f, 0.045f, 0.035f, 0.9f * Mathf.Clamp01((since - 0.6f) / 1.2f)), (float)Hash(e.Id, 98), 1f);
                    continue;
                }
                if ((e.Kind != EventKind.Shell && e.Kind != EventKind.GrenadeBlast) || e.X == null) continue;
                float age = (float)(now - e.Tick * Tune.Dt);
                if (age < 0) continue;
                float grow = Mathf.Clamp01(age / 1.5f);
                float size = e.Kind == EventKind.Shell ? 5.5f : 3.0f;
                Flat(e.X.Value, e.Z.Value, size, size, new Color(0.22f, 0.19f, 0.16f, 0.85f * grow), (float)Hash(i, 90), 1f);
            }
            _markMesh.Clear();
            if (_mp.Count == 0) return;
            _markMesh.SetVertices(_mp);
            _markMesh.SetColors(_mc);
            _markMesh.SetUVs(0, _muv);
            _markMesh.SetTriangles(_mi, 0, false);
            _markMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4000f);
        }

        /// <summary>A flat quad on the ground at a sim position, following its slope at the corners.</summary>
        private void Flat(double x, double z, float w, float h, Color col, float seed, float kind)
        {
            var g = _root.Ground;
            int k = _mp.Count;
            float hw = w * 0.5f, hh = h * 0.5f;
            foreach (var (dx, dz) in new[] { (-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh) })
            {
                double cx = x + dx, cz = z + dz;
                _mp.Add(Coords.World(cx, cz, (float)g.HeightAt(cx, cz) + 0.04f));
                _mc.Add(col);
            }
            _muv.Add(new Vector4(0, 0, seed, kind)); _muv.Add(new Vector4(1, 0, seed, kind));
            _muv.Add(new Vector4(1, 1, seed, kind)); _muv.Add(new Vector4(0, 1, seed, kind));
            _mi.Add(k); _mi.Add(k + 1); _mi.Add(k + 2); _mi.Add(k); _mi.Add(k + 2); _mi.Add(k + 3);
        }

        // --- geometry ---------------------------------------------------------------------------

        private void Clear()
        {
            _gp.Clear(); _gc.Clear(); _guv.Clear(); _gi.Clear();
            _puffs.Clear(); _flashes.Clear();
            Tracers = Flashes = Explosions = 0;
        }

        private void AddGlow(Vector3 c, float size, Color col, float kind, float seed)
        {
            float rot = seed * 6.2832f;
            var r = (_right * Mathf.Cos(rot) + _up * Mathf.Sin(rot)) * size * 0.5f;
            var u = (-_right * Mathf.Sin(rot) + _up * Mathf.Cos(rot)) * size * 0.5f;
            Quad(_gp, _gc, _guv, _gi, c - r - u, c + r - u, c + r + u, c - r + u, col, seed, kind);
        }

        private void AddStreak(Vector3 a, Vector3 b, float width, Color col, float seed)
        {
            var mid = (a + b) * 0.5f;
            var w = Vector3.Cross(b - a, _camPos - mid).normalized * width;
            // uv.x runs along the streak, so the shader's "across" is uv.y.
            int i = _gp.Count;
            _gp.Add(a - w); _gp.Add(b - w); _gp.Add(b + w); _gp.Add(a + w);
            for (int k = 0; k < 4; k++) _gc.Add(col);
            _guv.Add(new Vector4(0, 0, seed, 1)); _guv.Add(new Vector4(1, 0, seed, 1));
            _guv.Add(new Vector4(1, 1, seed, 1)); _guv.Add(new Vector4(0, 1, seed, 1));
            _gi.Add(i); _gi.Add(i + 1); _gi.Add(i + 2); _gi.Add(i); _gi.Add(i + 2); _gi.Add(i + 3);
        }

        private void AddPuff(Vector3 pos, float size, Color col, float seed, float density)
        {
            _puffs.Add(new Puff { Pos = pos, Size = size, Col = col, Seed = seed, Rot = seed * 6.2832f, Density = density,
                                  Dist = (pos - _camPos).sqrMagnitude });
        }

        private static void Quad(List<Vector3> p, List<Color> c, List<Vector4> uv, List<int> idx,
                                 Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col, float seed, float w)
        {
            int i = p.Count;
            p.Add(a); p.Add(b); p.Add(cc); p.Add(d);
            for (int k = 0; k < 4; k++) c.Add(col);
            uv.Add(new Vector4(0, 0, seed, w)); uv.Add(new Vector4(1, 0, seed, w));
            uv.Add(new Vector4(1, 1, seed, w)); uv.Add(new Vector4(0, 1, seed, w));
            idx.Add(i); idx.Add(i + 1); idx.Add(i + 2); idx.Add(i); idx.Add(i + 2); idx.Add(i + 3);
        }

        private void Upload()
        {
            // Smoke far to near, so each puff blends over what is behind it.
            _puffs.Sort((x, y) => y.Dist.CompareTo(x.Dist));
            _sp.Clear(); _sc.Clear(); _suv.Clear(); _si.Clear();
            foreach (var p in _puffs)
            {
                var r = (_right * Mathf.Cos(p.Rot) + _up * Mathf.Sin(p.Rot)) * p.Size * 0.5f;
                var u = (-_right * Mathf.Sin(p.Rot) + _up * Mathf.Cos(p.Rot)) * p.Size * 0.5f;
                Quad(_sp, _sc, _suv, _si, p.Pos - r - u, p.Pos + r - u, p.Pos + r + u, p.Pos - r + u, p.Col, p.Seed, p.Density);
            }
            Puffs = _puffs.Count;
            Set(_glowMesh, _gp, _gc, _guv, _gi);
            Set(_smokeMesh, _sp, _sc, _suv, _si);
        }

        private static void Set(Mesh m, List<Vector3> p, List<Color> c, List<Vector4> uv, List<int> idx)
        {
            m.Clear();
            if (p.Count == 0) return;
            m.SetVertices(p);
            m.SetColors(c);
            m.SetUVs(0, uv);
            m.SetTriangles(idx, 0, false);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 4000f);
        }

        private void Lights()
        {
            _flashes.Sort((a, b) => b.intensity.CompareTo(a.intensity));
            for (int i = 0; i < _lights.Length; i++)
            {
                bool on = i < _flashes.Count && _flashes[i].intensity > 0.5f;
                _lights[i].enabled = on;
                if (!on) continue;
                _lights[i].transform.position = _flashes[i].pos;
                _lights[i].intensity = _flashes[i].intensity;
            }
        }

        /// <summary>A stable number in [0, 1) for an event and a purpose.</summary>
        private static double Hash(int i, int salt)
        {
            double h = Math.Sin(i * 12.9898 + salt * 78.233) * 43758.5453;
            return h - Math.Floor(h);
        }
    }
}
