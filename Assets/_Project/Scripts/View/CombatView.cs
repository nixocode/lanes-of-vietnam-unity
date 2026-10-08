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
        public const double RicochetShare = 0.05;
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
        private Mesh _markMesh, _settledMesh;
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
            if (Marks != null)
            {
                // What has stopped changing under what has not: the same material, the settled marks drawn first.
                Child("ground marks, settled", Marks, out _settledMesh).GetComponent<MeshRenderer>().sortingOrder = -1;
                Child("ground marks", Marks, out _markMesh);
            }
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

        /// <summary>Milliseconds the last frame's effects took to build. For the frame-time probe.</summary>
        public float LastMs { get; private set; }
        private readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();

        private void LateUpdate()
        {
            _clock.Restart();
            Build();
            LastMs = (float)_clock.Elapsed.TotalMilliseconds;
        }

        private void Build()
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
                // At least this old: the tick's end. (Its shots are spread back across the tick.)
                double least = now - e.Tick * Tune.Dt;
                if (least > MaxAge) break;
                // Most of the log is not drawn at all, and what is has its own time: a shot's dust is gone
                // in two seconds, a shell's smoke hangs for ten. (Every shot of the last eleven seconds had
                // its men found and the ground under them measured, every frame, to draw nothing.)
                if (least > Life(e.Kind)) continue;
                double age = now - (e.Tick - 1 + Hash(i, 0)) * Tune.Dt;
                if (age < 0) continue;
                switch (e.Kind)
                {
                    case EventKind.Fire: Shot(ev, i, (float)age); break;
                    case EventKind.Shell: Explosion(e, (float)age, 1f); break;
                    case EventKind.TrapSprung: Explosion(e, (float)age, 0.45f); break;
                    case EventKind.GrenadeThrown: GrenadeFlight(e, (float)age); break;
                    case EventKind.GrenadeBlast: GrenadeBurst(e, (float)age); break;
                    case EventKind.Melee: Blow(ev, i, (float)age); break;
                    case EventKind.Launch: Launched(e, i, (float)age); break;
                    case EventKind.Through: Passed(e, i, (float)age); break;
                    case EventKind.Kill: Blasted(ev, i, (float)age); break;
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
            for (int k = _ordnanceShown; k < _ordnance.Count; k++)
                if (_ordnance[k].gameObject.activeSelf) _ordnance[k].gameObject.SetActive(false);
        }

        /// <summary>
        /// Seconds an event of this kind has anything on the screen, at the most; nothing for a kind
        /// that is not drawn. A shot: the last round of a burst out, across the map, and its dust
        /// settled. A blow or a round through a man: the blood. A grenade: its smoke. The rest, as
        /// long as a shell's smoke hangs.
        /// </summary>
        private static float Life(EventKind kind)
        {
            switch (kind)
            {
                case EventKind.Fire: return 2.6f;
                case EventKind.Melee: case EventKind.Through: case EventKind.Kill: return 1.3f;
                case EventKind.GrenadeThrown: return Tune.FragFuse * (float)Tune.Dt;
                case EventKind.GrenadeBlast: return 6f;
                case EventKind.Shell: case EventKind.TrapSprung: case EventKind.Launch: return (float)MaxAge;
                default: return -1f;
            }
        }

        // --- shots -------------------------------------------------------------------

        /// <summary>Where a man is on the screen (ArmyView.Where): what is drawn about him is drawn on him.</summary>
        private (double x, double z) At(int id)
            => _root.ArmyView != null ? _root.ArmyView.Where(_root.Driver, id) : _root.Driver.Position(id);

        private Vector3 Chest(Man m)
        {
            var (x, z) = At(m.Id);
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
            // A belt-fed gun's burst is longer than a rifleman's on automatic. (Ammo: the rounds the shot spent, as the simulation counted them.)
            int rounds = e.Rounds > 0 ? e.Rounds : !auto ? 1 : shooter.Weapon == Weapon.M60 || shooter.Weapon == Weapon.Rpd ? BurstRounds + 2 : BurstRounds;
            auto |= rounds > 1;
            // Gunnery: the simulation says where a miss came down. What is there: the wall or the
            // parapet the man is behind, if he is behind one and the round was close; else the ground.
            var st = _root.Driver.State;
            Vector3? landed = null;
            int surface = Earth;
            if (!hit && e.X.HasValue && e.Z.HasValue)
            {
                double lx = e.X.Value, lz = e.Z.Value;
                var cover = target.Cover >= 0 && target.Cover < st.Cover.Count ? st.Cover[target.Cover] : null;
                var (tx, tz) = At(target.Id);
                if (cover != null && Fieldcraft.Built(cover) && Hash(i, 40) < 0.55)
                {
                    // Into the bags in front of him: a pace short of him on the round's own line, at the height of the wall.
                    var back = new Vector3(-aim.x, 0, -aim.z).normalized;
                    var face = Coords.World(tx, tz, (float)_root.Ground.HeightAt(tx, tz)) + back * (0.7f + 0.5f * (float)Hash(i, 41))
                               + new Vector3(-back.z, 0, back.x) * (float)(Hash(i, 42) * 1.6 - 0.8);
                    landed = face + Vector3.up * (cover.Kind == CoverKind.Trench ? 0.12f : 0.3f + 0.55f * (float)Hash(i, 43));
                    surface = cover.Kind == CoverKind.Trench ? Earth : Bags;
                }
                else landed = Coords.World(lx, lz, (float)_root.Ground.HeightAt(lx, lz) + 0.05f);
            }
            for (int r = 0; r < rounds; r++)
            {
                float ra = age - r * BurstGap;
                if (ra < 0) break;
                Round(i * 8 + r, ra, muzzle, b, aim, hit && r == rounds - 1, auto, shooter.Side, shooter.Weapon, r == 0 ? landed : Near(landed, i * 8 + r), surface);
            }
        }

        private const int Earth = 0, Bags = 1;

        /// <summary>The later rounds of a burst land round the first: a metre or so, on the ground.</summary>
        private Vector3? Near(Vector3? first, int s)
        {
            if (!first.HasValue) return null;
            var p = first.Value + new Vector3((float)(Hash(s, 44) * 2.4 - 1.2), 0, (float)(Hash(s, 45) * 1.6 - 0.8));
            return p;
        }

        /// <summary>
        /// The flash at the muzzle, by weapon: a ball of burning gas, a tongue
        /// of it down the line of the barrel, and what the flash hider does
        /// with the rest (the M16's birdcage throws a star; the belt-fed guns
        /// and the old bolt rifles a long tongue; a submachine gun a small
        /// ball). Two or three frames, and bright enough to bloom.
        /// </summary>
        private void MuzzleFlash(int s, float age, Vector3 muzzle, Vector3 dir, Weapon w)
        {
            const float life = 0.055f;
            if (age >= life) return;
            float k = 1 - age / life;
            float ball, tongue; int star = 0;
            switch (w)
            {
                case Weapon.M60: case Weapon.Rpd: ball = 0.34f; tongue = 0.75f; break;
                case Weapon.Sniper: ball = 0.40f; tongue = 0.90f; break;
                case Weapon.Smg: ball = 0.22f; tongue = 0.28f; break;
                case Weapon.Ak: case Weapon.Sks: ball = 0.30f; tongue = 0.50f; break;
                default: ball = 0.24f; tongue = 0.38f; star = 3; break;        // the M16 and the baseline's rifle
            }
            float jit = 0.8f + 0.4f * (float)Hash(s, 3);
            // (Hot enough to bloom, no hotter: at 9x the tongue was a white bar, not a flame.)
            var hot = Flash * (4.5f * k);
            AddGlow(muzzle + dir * (0.10f + 0.3f * tongue), ball * jit, hot, 0, (float)Hash(s, 4));
            AddGlow(muzzle + dir * 0.06f, ball * 0.4f, new Color(1f, 0.86f, 0.6f) * (6f * k), 0, (float)Hash(s, 12));
            AddStreak(muzzle, muzzle + dir * (tongue * jit), 0.03f + 0.02f * ball / 0.24f, hot, (float)Hash(s, 13));
            if (star > 0)
            {
                // Out through the slots of the flash hider: short tongues to the sides, seen as a star.
                var side = Vector3.Cross(dir, _camPos - muzzle).normalized;
                var up = Vector3.Cross(side, dir).normalized;
                for (int q = 0; q < star; q++)
                {
                    float ang = (q + (float)Hash(s, 14)) * 6.2832f / star;
                    var o = (side * Mathf.Cos(ang) + up * Mathf.Sin(ang)) * 0.7f + dir * 0.7f;
                    AddStreak(muzzle + dir * 0.04f, muzzle + dir * 0.04f + o.normalized * (0.2f * jit), 0.03f, hot * 0.8f, (float)Hash(s, 15 + q));
                }
            }
            Flashes++;
        }

        /// <summary>One round: the flash at the muzzle, its path through the air, and what it does where it lands.</summary>
        private void Round(int s, float age, Vector3 muzzle, Vector3 b, Vector3 aim, bool hit, bool auto, Side side,
                           Weapon weapon = Weapon.Rifle, Vector3? landed = null, int surface = Earth)
        {
            Vector3 end;
            if (hit) end = b;
            else if (landed.HasValue) end = landed.Value;
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

            MuzzleFlash(s, age, muzzle, dir, weapon);
            if (age < 0.7f)
            {
                float f = 1 - age / 0.7f;
                AddPuff(muzzle + dir * (0.25f + 0.5f * age) + Vector3.up * (0.25f * age), 0.16f + 0.5f * age,
                        new Color(0.62f, 0.62f, 0.6f, 0.2f * f * f), (float)Hash(s, 9), 1f);
            }
            // Its path: one round in five from an automatic weapon is a tracer, lit all the way. The rest
            // are seen where they land. (Every round was drawn going, as a pale line, and with the
            // ricochets it was a sky full of lines: the owner, playtest 8, "too much flash of gunfire
            // that goes up, down and sideways".)
            if (auto && Hash(s, 5) < TracerShare)
            {
                float head = TracerSpeed * age;
                float tail = Mathf.Max(0, head - 3.5f);
                float h = Mathf.Min(head, dist);
                if (h > tail) { AddStreak(muzzle + dir * tail, muzzle + dir * h, 0.04f, side == Side.Us ? UsTracer : VcTracer, (float)Hash(s, 6)); Tracers++; }
            }
            float ai = age - dist / TracerSpeed;
            if (ai < 0) return;
            if (hit)
            {
                Blood(s, ai, b, dir, 1f);
                return;
            }
            if (surface == Bags)
            {
                // Into a sandbag: a slap of pale dust off the face of the wall, and sand running out of the hole.
                if (ai < 0.05f) AddGlow(end, 0.16f, new Color(1f, 0.85f, 0.6f) * (2.5f * (1 - ai / 0.05f)), 0, (float)Hash(s, 34));
                if (ai < 0.9f)
                {
                    float f = 1 - ai / 0.9f;
                    for (int k = 0; k < 3; k++)
                    {
                        var v = -dir * (1.6f + 1.2f * (float)Hash(s, 20 + k)) + Vector3.up * (0.8f + 1.0f * (float)Hash(s, 23 + k))
                                + new Vector3(-dir.z, 0, dir.x) * (float)(Hash(s, 26 + k) - 0.5);
                        AddPuff(end + v * ai * (1f - 0.5f * ai), 0.16f + 0.75f * ai, new Color(0.46f, 0.40f, 0.30f, 0.8f * f * f), (float)Hash(s, 8 + k), 1f);
                    }
                    for (int k = 0; k < 2; k++)
                        AddPuff(end + Vector3.down * (0.5f * ai + 1.6f * ai * ai) + new Vector3(0.03f * k, 0, 0), 0.06f + 0.04f * k,
                                new Color(0.40f, 0.34f, 0.24f, 0.9f * f), (float)Hash(s, 50 + k), 1f);
                }
                return;
            }
            // Into the earth: a spurt of dirt straight up at once, then the dust it leaves hanging.
            if (ai < 0.04f) AddGlow(end + Vector3.up * 0.05f, 0.12f, new Color(1f, 0.8f, 0.55f) * (2f * (1 - ai / 0.04f)), 0, (float)Hash(s, 35));
            if (ai < 0.32f)
            {
                float f = 1 - ai / 0.32f;
                for (int k = 0; k < 5; k++)
                {
                    float az = (float)(Hash(s, 20 + k) * Math.PI * 2);
                    var v = new Vector3(Mathf.Cos(az) * 0.9f, 5.5f + 3.5f * (float)Hash(s, 23 + k), Mathf.Sin(az) * 0.9f) - dir * 1.2f;
                    AddPuff(end + v * ai + Vector3.down * (9.8f * ai * ai), 0.12f + 0.3f * ai, new Color(0.13f, 0.1f, 0.07f, 0.95f * f), (float)Hash(s, 26 + k), 1f);
                }
            }
            if (ai < 1.8f)
            {
                float f = 1 - ai / 1.8f;
                for (int k = 0; k < 2; k++)
                    AddPuff(end + Vector3.up * (0.1f + 0.6f * ai + 0.15f * k), 0.34f + (1.0f + 0.3f * k) * ai,
                            new Color(0.22f, 0.19f, 0.14f, 0.8f * f * f), (float)Hash(s, 8 + k), 1f);
            }
            // Now and then it glances off and goes on, lit, low and onward: a ricochet.
            if (Hash(s, 30) < RicochetShare && ai < 0.16f)
            {
                var flat = new Vector3(dir.x, 0, dir.z).normalized;
                var off = (flat + Vector3.up * (0.08f + 0.17f * (float)Hash(s, 31))
                           + new Vector3(-flat.z, 0, flat.x) * (float)((Hash(s, 32) - 0.5) * 0.4)).normalized;
                float head = 190f * ai, tail = Mathf.Max(0, head - 2.2f);
                AddStreak(end + off * tail, end + off * head, 0.018f, new Color(1f, 0.62f, 0.25f) * (1.8f * (1 - ai / 0.16f)), (float)Hash(s, 33));
                if (ai < 0.05f) AddGlow(end + Vector3.up * 0.05f, 0.22f, Flash * (6f * (1 - ai / 0.05f)), 0, (float)Hash(s, 34));
            }
        }

        /// <summary>
        /// A round through a man: what it throws out of him. A burst at the
        /// wound, gone in a blink; a spray out of his back along the round's
        /// path, thrown two or three metres and falling; and the mist that
        /// hangs where he stood. (The owner: "spit out blood. Impacts need to
        /// be felt.")
        /// </summary>
        private void Blood(int s, float ai, Vector3 at, Vector3 dir, float amount)
        {
            amount *= Gore;
            var flat = new Vector3(dir.x, 0, dir.z).normalized;
            var across = new Vector3(-flat.z, 0, flat.x);
            if (ai < 0.09f)
                AddPuff(at, 0.3f + 2.2f * ai, new Color(0.30f, 0.02f, 0.02f, 0.95f * (1 - ai / 0.09f)), (float)Hash(s, 60), 1f);
            if (ai < 0.75f)
            {
                float f = 1 - ai / 0.75f;
                int drops = Mathf.RoundToInt(10 * amount);
                for (int k = 0; k < drops; k++)
                {
                    // Out of the far side of him: fast, in a narrow cone, and down.
                    float v0 = 3.5f + 5.5f * (float)Hash(s, 61 + k);
                    var v = flat * v0 + across * (float)((Hash(s, 70 + k) - 0.5) * 2.2) + Vector3.up * (0.6f + 2.2f * (float)Hash(s, 80 + k));
                    var p = at + flat * 0.12f + v * ai * (1f - 0.45f * ai) + Vector3.down * (4.9f * ai * ai);
                    AddPuff(p, 0.07f + 0.16f * ai + 0.05f * (float)Hash(s, 90 + k), new Color(0.26f, 0.02f, 0.018f, 0.9f * f), (float)Hash(s, 100 + k), 1f);
                }
                for (int k = 0; k < 2; k++)
                    AddPuff(at + flat * (0.2f + 0.7f * ai) + Vector3.up * (0.08f * k - 0.25f * ai * ai),
                            0.22f + (0.6f + 0.25f * k) * ai, new Color(0.22f, 0.025f, 0.02f, 0.55f * f * amount), (float)Hash(s, 7 + k), 1f);
            }
        }

        /// <summary>How much blood there is, against the game's own: all of it, or with the setting off two fifths.</summary>
        private float Gore => _root != null && _root.Settings != null && !_root.Settings.Gore ? 0.4f : 1f;

        /// <summary>The burst that made a kill: the shell, the grenade or the trap whose event comes before it in its tick.</summary>
        private static bool Burst(IReadOnlyList<SimEvent> ev, int kill, out double x, out double z)
        {
            x = z = 0;
            var e = ev[kill];
            if (kill > 0 && ev[kill - 1].Tick == e.Tick && ev[kill - 1].Target == e.Id
                && (ev[kill - 1].Kind == EventKind.Fire || ev[kill - 1].Kind == EventKind.Through || ev[kill - 1].Kind == EventKind.Melee)) return false;
            for (int k = kill - 1; k >= 0 && ev[k].Tick == e.Tick; k--)
            {
                var b = ev[k];
                if ((b.Kind != EventKind.GrenadeBlast && b.Kind != EventKind.Shell && b.Kind != EventKind.TrapSprung) || b.X == null) continue;
                x = b.X.Value; z = b.Z.Value;
                return true;
            }
            return false;
        }

        /// <summary>A man killed by a burst: the blood it throws out of him, away from it, and a good deal of it.</summary>
        private void Blasted(IReadOnlyList<SimEvent> ev, int i, float age)
        {
            var e = ev[i];
            var st = _root.Driver.State;
            if (e.Id >= st.Men.Count || !Burst(ev, i, out double bx, out double bz)) return;
            var man = st.Men[e.Id];
            var (x, z) = At(man.Id);
            var from = Coords.World(bx, bz, (float)_root.Ground.HeightAt(bx, bz));
            var at = Coords.World(x, z, (float)_root.Ground.HeightAt(x, z) + 0.9f);
            var away = at - from; away.y = 0;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            Blood(i * 8 + 6, age, at, away, 2.2f);
            Blood(i * 8 + 7, age, at + Vector3.up * 0.3f, Quaternion.Euler(0, 55f, 0) * away, 1.2f);
        }

        /// <summary>A blow hand to hand: the scuffle's dust at their feet, and blood if it killed.</summary>
        private void Blow(IReadOnlyList<SimEvent> ev, int i, float age)
        {
            var e = ev[i];
            var men = _root.Driver.State.Men;
            if (e.Target == null || e.Id >= men.Count || e.Target.Value >= men.Count) return;
            var a = Chest(men[e.Id]);
            var b = Chest(men[e.Target.Value]);
            var dir = (b - a).normalized;
            // The blow lands as the lunge reaches him.
            float ai = age - SoldierFigure.LungeSeconds * 0.5f;
            if (ai < 0) return;
            bool killed = i + 1 < ev.Count && ev[i + 1].Kind == EventKind.Kill && ev[i + 1].Id == e.Target.Value && ev[i + 1].Tick == e.Tick;
            if (killed) Blood(i * 8, ai, b, dir, 0.6f);
            if (ai < 0.9f)
            {
                float f = 1 - ai / 0.9f;
                var feet = new Vector3((a.x + b.x) * 0.5f, Mathf.Min(a.y, b.y) - 0.9f, (a.z + b.z) * 0.5f);
                for (int k = 0; k < 2; k++)
                    AddPuff(feet + Vector3.up * (0.15f + 0.4f * ai + 0.2f * k), 0.35f + 0.8f * ai, new Color(0.20f, 0.17f, 0.13f, 0.5f * f * f), (float)Hash(i, 40 + k), 1f);
            }
        }

        /// <summary>
        /// A bursting round on its way (the sim's Launch): the thump and smoke
        /// where it was fired, and the round itself. An M79's grenade is a dark
        /// speck on a low arc; a rocket goes flat and fast on its motor, its
        /// smoke behind it and its backblast behind the man; a mortar bomb goes
        /// up out of the frame and comes down. The burst is the GrenadeBlast
        /// that follows it.
        /// </summary>
        private void Launched(SimEvent e, int i, float age)
        {
            var men = _root.Driver.State.Men;
            if (e.X == null || e.Id >= men.Count) return;
            var by = men[e.Id];
            float flight = (float)((e.Amount ?? 10) * Tune.Dt);
            if (age >= 1.6f && age >= flight) return;           // landed, and the smoke of the shot gone
            var a = Chest(by);
            double lx = e.X.Value, lz = e.Z.Value;
            var to = Coords.World(lx, lz, (float)_root.Ground.HeightAt(lx, lz) + 0.1f);
            var from = _root.ArmyView != null && _root.ArmyView.TryMuzzle(by.Id, out var mz) ? mz : a + (to - a).normalized * 0.6f;
            var dir = (to - from).normalized;
            bool rocket = by.Weapon == Weapon.Rpg, mortar = by.Weapon == Weapon.Mortar;

            if (age < 0.06f) AddGlow(from, rocket ? 0.9f : 0.5f, Flash * (10f * (1 - age / 0.06f)), 0, (float)Hash(i, 50));
            if (age < 1.6f)
            {
                // The smoke of the shot: at the muzzle, and for a rocket a great deal more of it behind him.
                float f = 1 - age / 1.6f;
                AddPuff(from + Vector3.up * (0.3f * age), 0.3f + 0.9f * age, new Color(0.6f, 0.6f, 0.58f, 0.45f * f * f), (float)Hash(i, 51), 1f);
                if (rocket)
                    for (int k = 0; k < 4; k++)
                        AddPuff(a - dir * (0.8f + (1.2f + 0.9f * k) * Mathf.Min(age * 3f, 1f)) + Vector3.up * (0.2f * k * age),
                                0.5f + (1.2f + 0.3f * k) * age, new Color(0.55f, 0.54f, 0.5f, 0.6f * f * f), (float)Hash(i, 52 + k), 1f);
            }
            if (age >= flight) return;

            float t = age / flight;
            float span = Vector3.Distance(from, to);
            float apex = rocket ? 0.15f : mortar ? 14f + 0.2f * span : 0.6f + 0.05f * span;
            var p = Vector3.Lerp(from, to, t) + Vector3.up * (4f * apex * t * (1 - t));
            if (rocket)
            {
                AddGlow(p - dir * 0.3f, 0.45f, new Color(1f, 0.7f, 0.35f) * 6f, 0, (float)Hash(i, 60));
                AddStreak(p - dir * 1.2f, p, 0.07f, new Color(1f, 0.6f, 0.25f) * 3f, (float)Hash(i, 61));
                // The motor's smoke, hanging along the way it came.
                for (int k = 1; k <= 6; k++)
                {
                    float tk = t - k * 0.12f;
                    if (tk < 0) break;
                    var pk = Vector3.Lerp(from, to, tk) + Vector3.up * (4f * apex * tk * (1 - tk) + 0.1f * k);
                    AddPuff(pk, 0.25f + 0.12f * k, new Color(0.7f, 0.7f, 0.68f, 0.5f - 0.07f * k), (float)Hash(i, 62 + k), 1f);
                }
            }
            else
            {
                // An M79's round or a mortar bomb: the round itself, nose along its way.
                float tn = Mathf.Min(1f, t + 0.02f);
                var ahead = Vector3.Lerp(from, to, tn) + Vector3.up * (4f * apex * tn * (1 - tn));
                var way = ahead - p;
                Show(mortar ? Ordnance.Kind.Bomb : Ordnance.Kind.Round, p,
                     way.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(Vector3.up, way) : Quaternion.identity);
            }
        }

        /// <summary>
        /// A round that went through one man into the next (the sim's Through):
        /// the first man's blood out along it, the round itself for an instant,
        /// and the second man's if it killed him.
        /// </summary>
        private void Passed(SimEvent e, int i, float age)
        {
            var men = _root.Driver.State.Men;
            if (e.Target == null || e.Id >= men.Count || e.Target.Value >= men.Count) return;
            var a = Chest(men[e.Id]);
            var b = Chest(men[e.Target.Value]);
            float dist = Vector3.Distance(a, b);
            var dir = (b - a) / Mathf.Max(0.01f, dist);
            Blood(i * 8 + 3, age, a, dir, 1.6f);
            float head = TracerSpeed * age, tail = Mathf.Max(0, head - 2.5f);
            if (tail < dist) AddStreak(a + dir * tail, a + dir * Mathf.Min(head, dist), 0.03f, new Color(0.9f, 0.75f, 0.6f) * 1.5f, (float)Hash(i, 41));
            float ai = age - dist / TracerSpeed;
            if (ai >= 0 && e.Amount == 1) Blood(i * 8 + 5, ai, b, dir, 1f);
        }

        // --- grenades ---------------------------------------------------------------------

        /// <summary>
        /// A grenade from the thrower's hand to where it lands, on a lob, then
        /// lying there until the blast event takes over: the grenade itself
        /// (<see cref="Ordnance"/>), an M26 for the Americans and a stick
        /// grenade for the VC, tumbling end over end. It leaves the hand part
        /// way through the throw (SoldierFigure holds it until then). It was a
        /// puff of smoke with a pale line behind it, and the owner, playtest 8:
        /// "these rays in the sky look bad, remove them; show the grenade".
        /// </summary>
        private void GrenadeFlight(SimEvent e, float age)
        {
            float fuse = Tune.FragFuse * (float)Tune.Dt;
            if (age > fuse || e.X == null || age < Ordnance.ThrowRelease) return;
            var men = _root.Driver.State.Men;
            if (e.Id >= men.Count) return;
            var thrower = men[e.Id];
            var kind = thrower.Side == Side.Us ? Ordnance.Kind.Lemon : Ordnance.Kind.Stick;
            // From where his hand lets it go: above his shoulder, toward where it is going.
            double lx = e.X.Value, lz = e.Z.Value;
            var to = Coords.World(lx, lz, (float)_root.Ground.HeightAt(lx, lz) + 0.06f);
            var chest = Chest(thrower);
            var flat = new Vector3(to.x - chest.x, 0, to.z - chest.z);
            var from = chest + Vector3.up * 0.55f + (flat.sqrMagnitude > 1e-4f ? flat.normalized * 0.25f : Vector3.zero);
            float t = (age - Ordnance.ThrowRelease) / Ordnance.ThrowFlight;
            float seed = (float)Hash(e.Id * 31 + e.Tick, 47);
            if (t < 1f)
            {
                float apex = 2.2f + 0.12f * Vector3.Distance(from, to);
                var p = Vector3.Lerp(from, to, t) + Vector3.up * (4f * apex * t * (1 - t));
                // End over end, about the line across its way.
                var across = flat.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
                Show(kind, p, Quaternion.AngleAxis((age - Ordnance.ThrowRelease) * (540f + 240f * seed), across));
            }
            // Down: on its side, a little turned, until it goes off.
            else Show(kind, to, Quaternion.AngleAxis(seed * 360f, Vector3.up) * Quaternion.Euler(0, 0, 90f));
        }

        // --- what is in the air, as things --------------------------------------------------

        private readonly List<MeshFilter> _ordnance = new List<MeshFilter>();
        private int _ordnanceShown;
        private Material _ordnanceMaterial;

        /// <summary>A grenade, a round or a bomb at a place this frame (pooled; what is not shown again is hidden).</summary>
        private void Show(Ordnance.Kind kind, Vector3 at, Quaternion turn)
        {
            if (_ordnanceMaterial == null)
            {
                var army = _root.ArmyView;
                if (army == null || army.UsFigures.Length == 0 || army.UsFigures[0].ArmsMaterial == null) return;
                _ordnanceMaterial = army.UsFigures[0].ArmsMaterial;
            }
            if (_ordnanceShown == _ordnance.Count)
            {
                var go = new GameObject("ordnance");
                go.transform.SetParent(transform, false);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _ordnanceMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                _ordnance.Add(go.AddComponent<MeshFilter>());
            }
            var mf = _ordnance[_ordnanceShown++];
            var mesh = Ordnance.Get(kind);
            if (mf.sharedMesh != mesh) mf.sharedMesh = mesh;
            mf.transform.SetPositionAndRotation(at, turn);
            if (!mf.gameObject.activeSelf) mf.gameObject.SetActive(true);
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
                    // Puffs sized to the screen: a fire mission's 14 m bank or a squad's 3.5 m canister.
                    float size = (float)ar.Radius * (0.32f + 0.2f * (float)Hash(seed, 3));
                    float y = (float)_root.Ground.HeightAt(x, z) + size * (0.25f + 0.35f * (float)Hash(seed, 2));
                    AddPuff(Coords.World(x, z, y), size, new Color(0.52f, 0.52f, 0.50f, 0.55f * density),
                            (float)Hash(seed, 4), 1f);
                }
            }
        }

        // --- marks on the ground ------------------------------------------------------------

        /// <summary>Four lists that make a mesh of quads on the ground.</summary>
        private sealed class MarkQuads
        {
            public readonly List<Vector3> P = new List<Vector3>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<Vector4> Uv = new List<Vector4>();
            public readonly List<int> I = new List<int>();

            public void Clear() { P.Clear(); C.Clear(); Uv.Clear(); I.Clear(); }

            public void Into(Mesh m)
            {
                m.Clear();
                if (P.Count == 0) return;
                m.SetVertices(P);
                m.SetColors(C);
                m.SetUVs(0, Uv);
                m.SetTriangles(I, 0, false);
                m.bounds = new Bounds(Vector3.zero, Vector3.one * 4000f);
            }
        }

        /// <summary>A kill or a blast whose marks are still coming in: its place in the log, and whether a round made the kill.</summary>
        private struct Fresh { public int Event; public bool Shot, Burst; public double FromX, FromZ; }

        /// <summary>Seconds after which a dead man's marks, and a blast's, have stopped changing: he has fallen and lies still, his blood has soaked out, the scorch has darkened in.</summary>
        private const float KillSettles = 6.6f, BlastSettles = 1.6f;

        private readonly MarkQuads _live = new MarkQuads(), _settled = new MarkQuads();
        private readonly List<Fresh> _fresh = new List<Fresh>();
        private bool[] _lies = new bool[0];
        private SimState _marksOf;
        private int _markCursor;

        /// <summary>
        /// A soft shadow under every man, living or dead, and a scorch wherever
        /// a shell or a grenade went off, for the rest of the match. The scorch
        /// darkens in as the blast clears, so it never pops.
        ///
        /// Two meshes. A mark that has stopped changing (a dead man's shadow
        /// and his blood once he lies still, a scorch once it has darkened) is
        /// built once, into the first, and stays. Only the living men's
        /// shadows and the marks of the last few seconds are built each frame.
        /// (All of them were, from the whole log, every frame: by the end of a
        /// match that was the ground measured some thousands of times a frame
        /// to redraw what had not moved.)
        /// </summary>
        private void GroundMarks(SimState st, double now)
        {
            var ev = st.Events;
            if (!ReferenceEquals(st, _marksOf) || _markCursor > ev.Count)
            {
                // A new match.
                _marksOf = st; _markCursor = 0;
                _fresh.Clear(); _settled.Clear();
                System.Array.Clear(_lies, 0, _lies.Length);
                _settled.Into(_settledMesh);
            }
            if (_lies.Length < st.Men.Count) System.Array.Resize(ref _lies, st.Men.Count * 2);
            for (; _markCursor < ev.Count; _markCursor++)
            {
                var e = ev[_markCursor];
                int i = _markCursor;
                if (e.Kind == EventKind.Kill && e.Id < st.Men.Count)
                {
                    // The simulation writes a kill straight after the shot, or the round through another man, that made it.
                    bool shot = i > 0 && ev[i - 1].Tick == e.Tick && ev[i - 1].Target == e.Id
                                && (ev[i - 1].Kind == EventKind.Fire || ev[i - 1].Kind == EventKind.Through);
                    bool burst = Burst(ev, i, out double fx, out double fz);
                    _fresh.Add(new Fresh { Event = i, Shot = shot, Burst = burst, FromX = fx, FromZ = fz });
                }
                else if ((e.Kind == EventKind.Shell || e.Kind == EventKind.GrenadeBlast) && e.X != null) _fresh.Add(new Fresh { Event = i });
            }

            _live.Clear();
            for (int i = 0; i < st.Men.Count; i++)
            {
                if (_lies[i]) continue;
                Shadow(_live, st.Men[i]);
            }
            int before = _settled.P.Count;
            for (int k = 0; k < _fresh.Count; k++)
            {
                var f = _fresh[k];
                var e = ev[f.Event];
                float age = (float)(now - e.Tick * Tune.Dt);
                bool kill = e.Kind == EventKind.Kill;
                if (age < (kill ? KillSettles : BlastSettles))
                {
                    if (kill) Bled(_live, st, e, f, age); else Scorch(_live, e, f.Event, age);
                    continue;
                }
                if (kill)
                {
                    Shadow(_settled, st.Men[e.Id]);
                    Bled(_settled, st, e, f, age);
                    _lies[e.Id] = true;
                }
                else Scorch(_settled, e, f.Event, age);
                _fresh.RemoveAt(k--);
            }
            if (_settled.P.Count != before) _settled.Into(_settledMesh);
            _live.Into(_markMesh);
        }

        private void Shadow(MarkQuads into, Man m)
        {
            var (x, z) = At(m.Id);
            bool flat = !m.Alive || m.Posture == Posture.Prone;
            float w = flat ? 1.9f : m.Posture == Posture.Crouched ? 1.0f : 0.9f;
            float h = flat ? 0.8f : 0.55f;
            Flat(into, x, z, w, h, new Color(0.45f, 0.44f, 0.42f, 0.8f), m.Id * 0.013f, 0f);
        }

        /// <summary>A dead man's blood: what the round threw out of him, and the pool where he lies.</summary>
        private void Bled(MarkQuads into, SimState st, SimEvent e, Fresh how, float since)
        {
            bool shot = how.Shot;
            float gore = Gore;
            if (since >= 0.2f && how.Burst)
            {
                // A burst: flung out of him away from it, from where he stood to past where it threw him.
                var man = st.Men[e.Id];
                double ax = man.X - how.FromX, az = man.Z - how.FromZ, far = System.Math.Sqrt(ax * ax + az * az);
                if (far < 0.05) { ax = 1; az = 0; far = 1; }
                ax /= far; az /= far;
                float show = Mathf.Clamp01((since - 0.2f) / 0.3f);
                int stains = Mathf.RoundToInt(6 * gore);
                for (int k = 0; k < stains; k++)
                {
                    double reach = 0.4 + 0.75 * k + 0.6 * Hash(e.Id, 130 + k), wide = (Hash(e.Id, 137 + k) - 0.5) * (0.5 + 0.45 * k);
                    float spot = 0.75f - 0.07f * k + 0.3f * (float)Hash(e.Id, 144 + k);
                    Flat(into, man.X + ax * reach - az * wide, man.Z + az * reach + ax * wide, spot, spot * 0.7f,
                         new Color(0.31f, 0.04f, 0.03f, 0.85f * show), (float)Hash(e.Id, 151 + k), 1f);
                }
                // And where what it took off him came down.
                if (_root.ArmyView != null && _root.ArmyView.TryLimb(e.Id, out var limb))
                    Flat(into, limb.x, Coords.SimZ(limb.z), 0.7f, 0.55f, new Color(0.33f, 0.04f, 0.03f, 0.9f * Mathf.Clamp01((since - 1.2f) / 1.5f)), (float)Hash(e.Id, 158), 1f);
            }
            if (since >= 0.25f && shot)
            {
                // Shot: what the round threw out of him lies on the ground beyond
                // where he stood, the way it was going (it came from the other side).
                var man = st.Men[e.Id];
                double go = Combat.Advance(Combat.Other(man.Side));
                float show = Mathf.Clamp01((since - 0.25f) / 0.3f);
                for (int k = 0; k < (gore < 1f ? 2 : 4); k++)
                {
                    double reach = 0.8 + 1.1 * k + 0.9 * Hash(e.Id, 110 + k);
                    float spot = 0.55f - 0.12f * k + 0.2f * (float)Hash(e.Id, 113 + k);
                    Flat(into, man.X + go * reach, man.Z + (Hash(e.Id, 116 + k) - 0.5) * (0.5 + 0.5 * k), spot, spot * 0.6f,
                         new Color(0.30f, 0.04f, 0.03f, 0.8f * show), (float)Hash(e.Id, 119 + k), 1f);
                }
            }
            // Blood: where he lies (the body, not the sim's point: a fall
            // carries him), soaking out over a few seconds, and it stays.
            if (since < 0.6f) return;
            var (bx, bz) = At(e.Id);
            if (_root.ArmyView != null && _root.ArmyView.TryBody(e.Id, out var body)) { bx = body.x; bz = Coords.SimZ(body.z); }
            float soak = Mathf.SmoothStep(0, 1, (since - 0.6f) / 5f);
            // (More of it, the owner, playtest 9; and more again under a man a burst killed.)
            float pool = (0.55f + 1.0f * soak + 0.35f * (float)Hash(e.Id, 95)) * (how.Burst ? 1.35f : 1f) * (0.5f + 0.5f * gore);
            Flat(into, bx + (Hash(e.Id, 96) - 0.5) * 0.3, bz + (Hash(e.Id, 97) - 0.5) * 0.3, pool, pool * 0.8f,
                 new Color(0.34f, 0.045f, 0.035f, 0.9f * Mathf.Clamp01((since - 0.6f) / 1.2f)), (float)Hash(e.Id, 98), 1f);
        }

        /// <summary>Where a shell or a grenade went off: the earth burnt, darkening in as the blast clears.</summary>
        private void Scorch(MarkQuads into, SimEvent e, int index, float age)
        {
            if (age < 0) return;
            float grow = Mathf.Clamp01(age / 1.5f);
            float size = e.Kind == EventKind.Shell ? 5.5f : 3.0f;
            Flat(into, e.X.Value, e.Z.Value, size, size, new Color(0.22f, 0.19f, 0.16f, 0.85f * grow), (float)Hash(index, 90), 1f);
        }

        /// <summary>A flat quad on the ground at a sim position, following its slope at the corners.</summary>
        private void Flat(MarkQuads into, double x, double z, float w, float h, Color col, float seed, float kind)
        {
            int k = into.P.Count;
            float hw = w * 0.5f, hh = h * 0.5f;
            Corner(into, x - hw, z - hh, col); Corner(into, x + hw, z - hh, col);
            Corner(into, x + hw, z + hh, col); Corner(into, x - hw, z + hh, col);
            into.Uv.Add(new Vector4(0, 0, seed, kind)); into.Uv.Add(new Vector4(1, 0, seed, kind));
            into.Uv.Add(new Vector4(1, 1, seed, kind)); into.Uv.Add(new Vector4(0, 1, seed, kind));
            into.I.Add(k); into.I.Add(k + 1); into.I.Add(k + 2); into.I.Add(k); into.I.Add(k + 2); into.I.Add(k + 3);
        }

        private void Corner(MarkQuads into, double x, double z, Color col)
        {
            into.P.Add(Coords.World(x, z, (float)_root.Ground.HeightAt(x, z) + 0.04f));
            into.C.Add(col);
        }

        // --- geometry ---------------------------------------------------------------------------

        private void Clear()
        {
            _gp.Clear(); _gc.Clear(); _guv.Clear(); _gi.Clear();
            _puffs.Clear(); _flashes.Clear();
            Tracers = Flashes = Explosions = 0;
            _ordnanceShown = 0;
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
