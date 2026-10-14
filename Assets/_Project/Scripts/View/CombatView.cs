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
    ///
    /// What is drawn on those quads is real fire and real smoke: frames of
    /// sheets filmed or simulated (<see cref="FxSheets"/>), each fading into
    /// the next by the effect's age. They were shapes computed in the shader,
    /// a star for a flash and a blob of noise for everything else, and the
    /// owner, 2026-10-12: "the actual shots being fired need some work".
    /// </summary>
    public sealed class CombatView : MonoBehaviour
    {
        public Material Glow;
        public Material Smoke;
        /// <summary>Contact shadows and scorch marks (LOV/Ground Mark); optional.</summary>
        public Material Marks;
        /// <summary>
        /// The air strike's jet and the medevac's helicopter (Art/Structures/aircraft.fbx, from
        /// tools/blender/aircraft_import.py: an F-100 and a UH-1B, sourced), on one material; the
        /// helicopter's main rotor apart, to be turned, and where its mast stands from the helicopter's
        /// own origin. Optional: without them a call comes as it did, heard and not seen.
        /// </summary>
        public Mesh Jet, Huey, Rotor;
        public Material Aircraft;
        public Vector3 RotorHub;
        /// <summary>Nothing that flies is drawn (the frame-time probe's <c>off=aircraft</c>).</summary>
        public bool NoAircraft;

        /// <summary>How fast a tracer is drawn travelling (m/s): a 5.56 or 7.62 round at combat range, slowed a little so the eye can follow it.</summary>
        public const float TracerSpeed = 620f;
        /// <summary>
        /// The share of an automatic weapon's rounds drawn as tracers: belts were
        /// loaded one in five. Riflemen fire none (a rifleman's magazine held
        /// none), so a firefight is sparks and dust, not beams: the owner's
        /// second playtest, "more of a spark not a laser".
        /// </summary>
        public const double TracerShare = 0.2;
        /// <summary>
        /// The share of the rounds that are not tracers whose way through the air is drawn: a thin, dim
        /// line from the muzzle to where the simulation says the round went, and no further. All of
        /// them. (They were not drawn at all after playtest 8, when every round was a pale line as
        /// bright as a tracer and the ricochets went "up, down and sideways"; with none, a rifleman's
        /// shot was a flash here and dust there with nothing between: the owner, 2026-10-12, asked
        /// to see the rounds go.)
        /// </summary>
        public const double TraceShare = 1.0;
        /// <summary>Rounds in a burst from an automatic weapon when the simulation does not count them (MatchOptions.Ammo off).</summary>
        public const int BurstRounds = 3;
        /// <summary>
        /// Seconds from one round of a burst to the next: the weapon's own rate of fire. An M60 fires
        /// about 550 rounds a minute, an RPD 650, an AK 600, an M16 700, a PPSh 900 and an M3 "grease
        /// gun" 450. The flash, the kick, the case and the report of each round are all timed by it.
        /// (It was 0.085 s for every weapon.)
        ///
        /// One departure: a long burst close in is squeezed to fit before the simulation's next one
        /// (ten rounds from an M60 every 0.7 s is 860 a minute, more than the gun fires; drawn at its
        /// own rate the two bursts would be two guns firing at once).
        /// </summary>
        public static float RoundGap(Weapon w, Side side, int rounds = 1)
        {
            float gap;
            switch (w)
            {
                case Weapon.M60: gap = 60f / 550f; break;
                case Weapon.Rpd: gap = 60f / 650f; break;
                case Weapon.Smg: gap = side == Side.Us ? 60f / 450f : 60f / 900f; break;
                case Weapon.Ak: gap = 60f / 600f; break;
                default: gap = 60f / 700f; break;
            }
            var load = Ammo.Of(w);
            if (load.Automatic && rounds > load.Burst) gap = Mathf.Min(gap, load.LongGap * (float)Tune.Dt / rounds);
            return gap;
        }
        /// <summary>
        /// How hard a round strikes, against an AK's: what it throws up where it lands and out of a man it goes
        /// through. The owner, 2026-10-10: "see rounds hit soldiers, ground (and spit up dust: more prominent for
        /// large caliber like snipers and especially gunners)". A submachine gun fires a pistol's round; an M16 a
        /// light, fast one; the belt-fed guns and a sniper's rifle the full-power rounds, and a machine gun puts
        /// five or ten of them down in a row.
        /// </summary>
        public static float Calibre(Weapon w)
        {
            switch (w)
            {
                case Weapon.Smg: return 0.7f;
                case Weapon.M16: return 0.85f;
                case Weapon.M60: return 1.4f;
                case Weapon.Rpd: return 1.3f;
                case Weapon.Sniper: return 1.9f;
                default: return 1f;
            }
        }
        /// <summary>Metres between one round of a burst and the next where they land: a burst is walked along its own line (the simulation's own step, MatchOptions.Ballistics).</summary>
        public const float StitchStep = (float)Tune.StitchStep;
        /// <summary>The share of misses that glance off something hard and go on, lit.</summary>
        public const double RicochetShare = 0.05;
        private const double MaxAge = 11.0;
        /// <summary>Seconds a call-in has anything on the screen: the helicopter's eight coming, three down and seven going.</summary>
        private const float CallLife = 19f;

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
        private readonly List<Vector4> _guv = new List<Vector4>(), _guv2 = new List<Vector4>();
        private readonly List<int> _gi = new List<int>();
        private readonly List<Puff> _puffs = new List<Puff>();
        private readonly List<Vector3> _sp = new List<Vector3>();
        private readonly List<Color> _sc = new List<Color>();
        private readonly List<Vector4> _suv = new List<Vector4>(), _suv2 = new List<Vector4>();
        private readonly List<int> _si = new List<int>();
        private readonly Light[] _lights = new Light[4];
        private Mesh _markMesh, _settledMesh;
        private readonly List<(Vector3 pos, float intensity)> _flashes = new List<(Vector3, float)>();
        private Vector3 _right, _up, _camPos;
        private float _perPixel;
        private int _shaken;

        /// <summary>What was drawn last frame, for the tests and the log.</summary>
        public int Tracers { get; private set; }
        public int Flashes { get; private set; }
        public int Puffs { get; private set; }
        public int Explosions { get; private set; }
        /// <summary>Rounds whose way through the air was drawn as a thin line, and spent cases in the air or on the ground.</summary>
        public int Traces { get; private set; }
        public int Cases { get; private set; }
        /// <summary>Puffs asked for and not drawn, too faint to see: what a barrage's old smoke costs nothing for now.</summary>
        public int Faint { get; private set; }

        /// <summary>The sheets a puff can be a frame of (fx_smoke.png).</summary>
        private enum Sheet : byte { Dust, Blast, Wisp, Curl, Speck, Brass }
        private static readonly FxSheet[] SmokeSheets = { FxSheets.Dust, FxSheets.Blast, FxSheets.Wisp, FxSheets.Curl, FxSheets.Speck, FxSheets.Brass };

        private struct Puff
        {
            public Vector3 Pos;
            /// <summary>Metres across and up, a turn about the line of sight (radians), and the metres it fades into what is behind it over.</summary>
            public float Wide, Tall, Rot, Soft;
            public Sheet Sheet;
            /// <summary>Which frame of its sheet, and how far into the next.</summary>
            public float Frame;
            public bool Flip;
            public Color Col;
            public float Dist;
        }

        /// <summary>Below this a puff cannot be seen and is not drawn (its alpha, of 1).</summary>
        public const float FaintBelow = 0.02f;

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
                l.range = FlashReach;
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
            var lens = _root.CameraRig.Camera;
            // Metres a pixel covers, for each metre from the lens.
            _perPixel = 2f * Mathf.Tan(lens.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, lens.pixelHeight);
            Clear();
            if (!ReferenceEquals(st, _firedOf)) { _firedOf = st; Array.Clear(_firedEvent, 0, _firedEvent.Length); }

            double now = (st.Tick - 1 + d.Alpha) * Tune.Dt;
            var ev = st.Events;
            for (int i = ev.Count - 1; i >= 0; i--)
            {
                var e = ev[i];
                // At least this old: the tick's end. (Its shots are spread back across the tick.)
                double least = now - e.Tick * Tune.Dt;
                if (least > CallLife) break;
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
                    // Inbound: the guns' ranging round, a second before the rest: one small burst on the aim point.
                    case EventKind.CallIn: if (e.Rounds == Inbound.Ranging) Explosion(e, (float)age, 0.4f); else Called(e, now); break;
                    case EventKind.GrenadeThrown: GrenadeFlight(e, (float)age); break;
                    case EventKind.GrenadeBlast: GrenadeBurst(e, (float)age); break;
                    case EventKind.Melee: Blow(ev, i, (float)age); break;
                    case EventKind.Launch: Launched(e, i, (float)age); break;
                    case EventKind.Through: Passed(e, i, (float)age); break;
                    case EventKind.Stray: Strayed(e, i, (float)age); break;
                    case EventKind.Kill: Blasted(ev, i, (float)age); Bleeding(e, i, (float)age); break;
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
            if (_jet != null && _jet.gameObject.activeSelf != _jetShown) _jet.gameObject.SetActive(_jetShown);
            if (_huey != null && _huey.gameObject.activeSelf != _hueyShown) _huey.gameObject.SetActive(_hueyShown);
        }

        /// <summary>
        /// Seconds an event of this kind has anything on the screen, at the most; nothing for a kind
        /// that is not drawn. A shot: the last round of a burst out, across the map, its dust
        /// settled, its smoke thinned away and its case lying where it fell. A blow or a round
        /// through a man: the blood. A grenade: its smoke. The rest, as long as a shell's smoke hangs.
        /// </summary>
        private static float Life(EventKind kind)
        {
            switch (kind)
            {
                case EventKind.Fire: return 4.2f;
                case EventKind.Melee: case EventKind.Through: case EventKind.Stray: return 1.3f;
                case EventKind.Kill: return BleedsFor + 0.2f;
                case EventKind.GrenadeThrown: return Tune.FragFuse * (float)Tune.Dt;
                case EventKind.GrenadeBlast: return 6f;
                case EventKind.Shell: case EventKind.TrapSprung: case EventKind.Launch: return (float)MaxAge;
                case EventKind.CallIn: return CallLife;
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
            // (Up a post, he is that much higher: MatchOptions.Vantage.)
            return Coords.World(x, z, (float)_root.Ground.HeightAt(x, z) + h + (float)m.High);
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
            int rounds = Rounds(e, shooter);
            auto |= rounds > 1;
            float gap = RoundGap(shooter.Weapon, shooter.Side, rounds);
            float stance = shooter.Posture == Posture.Prone ? 0.3f : shooter.Posture == Posture.Crouched ? 0.95f : 1.35f;
            var by = new Fired
            {
                Muzzle = muzzle, From = FiredFrom(i, muzzle), Weapon = shooter.Weapon, Side = shooter.Side, Auto = auto,
                Prone = shooter.Posture == Posture.Prone && shooter.High < 1, Ground = a.y - stance - (float)shooter.High, Cal = Calibre(shooter.Weapon),
            };
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
            // A burst is walked along its own line: each round lands a pace beyond the one before, the last where
            // the simulation says the shot went (or, if it killed, in the man: a belt-fed gun's last two).
            bool gun = shooter.Weapon == Weapon.M60 || shooter.Weapon == Weapon.Rpd;
            int inHim = hit ? (gun && rounds > 2 ? 2 : 1) : 0;
            var flat = new Vector3(aim.x, 0, aim.z).normalized;
            for (int r = 0; r < rounds; r++)
            {
                float ra = age - r * gap;
                if (ra < 0) break;
                int s = i * 16 + r;
                bool strikes = r >= rounds - inHim;
                Vector3? at = landed;
                if (!strikes && rounds > 1)
                {
                    // Short of the last by a pace for each round still to come, and a little to one side.
                    var last = hit ? new Vector3(b.x, 0, b.z) : landed ?? new Vector3(b.x, 0, b.z);
                    int toCome = rounds - inHim - 1 - r + (hit ? 1 : 0);
                    if (surface == Bags && landed.HasValue)
                        // Along the wall, not through it.
                        at = landed.Value + new Vector3(-flat.z, 0, flat.x) * ((r - (rounds - 1) * 0.5f) * 0.35f + (float)(Hash(s, 45) - 0.5) * 0.2f) + Vector3.up * (float)((Hash(s, 44) - 0.5) * 0.3);
                    else if (toCome > 0 || hit)
                    {
                        var p = last - flat * (toCome * StitchStep * (0.8f + 0.4f * (float)Hash(s, 44))) + new Vector3(-flat.z, 0, flat.x) * (float)((Hash(s, 45) - 0.5) * 0.5);
                        at = Coords.World(p.x, -p.z, (float)_root.Ground.HeightAt(p.x, -p.z) + 0.05f);
                    }
                }
                Round(s, ra, by, b, aim, strikes, at, strikes || !landed.HasValue ? Earth : surface);
            }
        }

        private const int Earth = 0, Bags = 1;

        /// <summary>
        /// The rounds a shot is: as the simulation counted them (MatchOptions.Ammo); without that rule, one from a
        /// rifle, three from an automatic weapon and five from a belt-fed gun. The flashes, the kicks and the
        /// reports are this many (ArmyView, AudioView).
        /// </summary>
        public static int Rounds(SimEvent e, Man shooter)
            => e.Rounds > 0 ? e.Rounds : !AudioView.Automatic(shooter) ? 1
             : shooter.Weapon == Weapon.M60 || shooter.Weapon == Weapon.Rpd ? BurstRounds + 2 : BurstRounds;

        /// <summary>What a round is drawn from: who fired it, with what, and where from.</summary>
        private struct Fired
        {
            /// <summary>His muzzle now (the flash and the round leave it), and where it was when the shot was first drawn (its smoke and its case stay there).</summary>
            public Vector3 Muzzle, From;
            public Weapon Weapon;
            public Side Side;
            public bool Auto, Prone;
            /// <summary>The height of the ground under him, and how hard his round strikes (<see cref="Calibre"/>).</summary>
            public float Ground, Cal;
        }

        private const int Remembered = 2048;
        private readonly int[] _firedEvent = new int[Remembered];
        private readonly Vector3[] _firedFrom = new Vector3[Remembered];
        private SimState _firedOf;

        /// <summary>
        /// Where a shot was fired from: the muzzle as it was when the shot was first drawn. Its smoke hangs there
        /// and its case falls from there; drawn from the muzzle as it is now, a man who fired and got up took
        /// his smoke with him.
        /// </summary>
        private Vector3 FiredFrom(int i, Vector3 muzzle)
        {
            int k = i & (Remembered - 1);
            if (_firedEvent[k] == i + 1) return _firedFrom[k];
            _firedEvent[k] = i + 1;
            return _firedFrom[k] = muzzle;
        }

        private static float H(int i, int salt) => (float)Hash(i, salt);

        /// <summary>
        /// The flash at the muzzle, by weapon: a tongue of burning gas down the
        /// line of the barrel, what the muzzle does with the rest of it, and
        /// the light the two throw about them. The M16 of 1965 has a
        /// three-pronged flash hider and throws a small star; an AK's and an
        /// SKS's bare muzzle a fat tongue and a ball; the belt-fed guns a long
        /// tongue through a slotted hider; a sniper's rifle and the old bolt
        /// actions the longest; a submachine gun a short fat one. Three or four
        /// frames, and bright enough to bloom.
        /// </summary>
        private void MuzzleFlash(int s, float age, Vector3 muzzle, Vector3 dir, Weapon w)
        {
            const float life = 0.06f;
            if (age >= life) return;
            float k = 1 - age / life;
            // The tongue's length (m), which of the eight it is (0 to 3 thin, 4 to 7 fat), and the star: its size and its spikes.
            float tongue, star; bool fat; int spikes;
            switch (w)
            {
                case Weapon.M60: tongue = 0.80f; fat = false; star = 0.50f; spikes = 5; break;
                case Weapon.Rpd: tongue = 0.75f; fat = false; star = 0.34f; spikes = 4; break;
                case Weapon.Sniper: tongue = 0.95f; fat = false; star = 0.46f; spikes = 4; break;
                case Weapon.Smg: tongue = 0.34f; fat = true; star = 0.30f; spikes = 6; break;
                case Weapon.Ak: tongue = 0.55f; fat = true; star = 0.40f; spikes = 4; break;
                case Weapon.Sks: tongue = 0.60f; fat = false; star = 0.34f; spikes = 4; break;
                default: tongue = 0.40f; fat = false; star = 0.50f; spikes = 3; break;        // the M16 and the baseline's rifle
            }
            float jit = 0.8f + 0.4f * H(s, 3);
            float len = tongue * jit;
            // (Hot enough to bloom, no hotter: at 9x the tongue was a white bar, not a flame.)
            AddFlare(FxSheets.Tongue, (fat ? 4 : 0) + (int)(H(s, 13) * 4f) % 4, muzzle - dir * 0.03f, muzzle + dir * len, len * (fat ? 0.30f : 0.24f), Lit(Flash, 5.2f * k));
            // (The stars are in the sheet by their spikes: three, four, five, six, and the same again, tighter.)
            AddSprite(FxSheets.Star, spikes - 3 + (H(s, 14) < 0.5f ? 0 : 4), muzzle + dir * 0.05f, star * jit, Lit(Flash, 4.2f * k), H(s, 15) * 6.2832f);
            // The light of it on the air about the muzzle.
            AddGlow(muzzle + dir * (0.3f * len), (0.5f + 1.1f * tongue) * jit, Lit(Flash, 1.1f * k), 2, H(s, 4));
            Flashes++;
        }

        /// <summary>
        /// The smoke of a round: out of the muzzle with the gas, then hanging where it was fired and drifting off
        /// downwind for a second or two. One round's is a wisp; a machine gun's burst leaves a bank of it over the
        /// gun. A man lying flat kicks dust up off the ground in front of his muzzle as well.
        /// </summary>
        private void GunSmoke(int s, float age, Vector3 dir, in Fired by)
        {
            float hangs = 1.5f + 0.5f * by.Cal;
            if (age < hangs)
            {
                float t = age / hangs;
                // Thrown forward half a metre in its first tenth of a second, then up a little and off on the wind.
                float thrown = 0.55f * by.Cal * (1 - Mathf.Exp(-age * 22f));
                var at = by.From + dir * (0.12f + thrown) + Vector3.up * (0.05f + 0.28f * age) + Vector3.right * (0.45f * age * age);
                Hang(at, (0.30f + 1.0f * Mathf.Sqrt(t)) * (0.7f + 0.3f * by.Cal), new Color(0.66f, 0.66f, 0.64f, 0.24f * Mathf.Min(1f, age / 0.03f) * (1 - t) * (1 - t)), H(s, 9), age);
            }
            // Every other round of a burst, so a gun's ten rounds are not ten clouds of dust.
            if (!by.Prone || s % 2 != 0 || age >= 1.1f) return;
            float d = age / 1.1f;
            var flat = new Vector3(dir.x, 0, dir.z).normalized;
            var low = new Vector3(by.From.x, by.Ground + 0.12f, by.From.z) + flat * (0.35f + 0.5f * d);
            Dust(low + Vector3.up * (0.25f * d), (0.9f + 0.9f * d) * (0.6f + 0.4f * by.Cal), new Color(0.40f, 0.34f, 0.25f, 0.30f * (1 - d) * (1 - d)), H(s, 17), 0.12f + 0.7f * (1 - (1 - d) * (1 - d)));
        }

        /// <summary>How long a spent case lies where it fell before it is no longer drawn, seconds.</summary>
        public const float CaseLies = 2.4f;

        /// <summary>
        /// The spent case of a round: out of the port to the firer's right, tumbling, down to the ground, and lying
        /// there for a couple of seconds. (A bolt action keeps its case until the bolt is worked; a belt-fed gun
        /// drops a link with every case.)
        /// </summary>
        private void Brass(int s, float age, Vector3 dir, in Fired by)
        {
            if (by.Weapon == Weapon.Sniper) return;
            bool gun = by.Weapon == Weapon.M60 || by.Weapon == Weapon.Rpd;
            var flat = new Vector3(dir.x, 0, dir.z).normalized;
            var right = Vector3.Cross(Vector3.up, flat);
            var port = by.From - dir * (gun ? 0.62f : by.Weapon == Weapon.Smg ? 0.30f : 0.50f);
            var v = right * (1.8f + 1.6f * H(s, 70)) + Vector3.up * (1.4f + 1.3f * H(s, 71)) - flat * (0.2f + 0.9f * H(s, 72));
            // When it comes down: the ground under the man, a little lower or higher on a slope.
            float fall = Mathf.Max(0.02f, port.y - by.Ground - 0.045f);
            float land = (v.y + Mathf.Sqrt(v.y * v.y + 19.6f * fall)) / 9.8f;
            if (age > land + CaseLies) return;
            float t = Mathf.Min(age, land);
            var p = port + v * t + Vector3.down * (4.9f * t * t);
            bool flying = age < land;
            float left = flying ? 1f : 1f - (age - land) / CaseLies;
            AddPuff(Sheet.Brass, flying && ((int)(age * 40f) & 1) == 1 ? 1 : 0, p, 0.11f, 0.11f, new Color(1f, 1f, 1f, Mathf.Min(1f, left * 3f)),
                    flying ? age * (16f + 14f * H(s, 73)) : H(s, 73) * 6.2832f, false, 0.02f);
            Cases++;
            if (gun && flying) Clod(p - right * 0.12f + Vector3.up * 0.03f, 0.035f, new Color(0.10f, 0.10f, 0.10f, 0.95f), H(s, 74));
        }

        /// <summary>How much of a round's way through the air is lit behind it, metres, and how wide the line is.</summary>
        public const float TraceLength = 3.0f, TraceWidth = 0.014f;
        private static readonly Color Trace = new Color(0.78f, 0.72f, 0.58f, 1f);

        /// <summary>One round: the flash at the muzzle, its smoke and its case, its path through the air, and what it does where it lands.</summary>
        private void Round(int s, float age, in Fired by, Vector3 b, Vector3 aim, bool hit, Vector3? landed = null, int surface = Earth)
        {
            Vector3 muzzle = by.Muzzle;
            float cal = by.Cal;
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

            MuzzleFlash(s, age, muzzle, dir, by.Weapon);
            GunSmoke(s, age, dir, by);
            Brass(s, age, dir, by);
            // Its path. One round in five from an automatic weapon is a tracer, lit all the way. Every other
            // round is a thin dim line, three metres of it, from the muzzle to where it lands and no further:
            // enough to see which way the fire is going, not a sky full of beams. (Playtest 8: "too much flash
            // of gunfire that goes up, down and sideways" was every round as bright as a tracer, and the
            // ricochets. Then none were drawn; the owner, 2026-10-12, asked to see the rounds go.)
            float head = TracerSpeed * age;
            if (by.Auto && Hash(s, 5) < TracerShare)
            {
                float tail = Mathf.Max(0, head - 3.5f), h = Mathf.Min(head, dist);
                if (h > tail) { AddStreak(muzzle + dir * tail, muzzle + dir * h, 0.04f, by.Side == Side.Us ? UsTracer : VcTracer, H(s, 6)); Tracers++; }
            }
            else if (Hash(s, 16) < TraceShare)
            {
                float tail = Mathf.Max(0.4f, head - TraceLength), h = Mathf.Min(head, dist);
                if (h > tail) { AddStreak(muzzle + dir * tail, muzzle + dir * h, TraceWidth, Trace, H(s, 6), TracePixels); Traces++; }
            }
            float ai = age - dist / TracerSpeed;
            if (ai < 0) return;
            if (hit)
            {
                // Into a man: the slap of it on his kit (dust off the cloth, pale, gone in a moment), and what it
                // throws out of him, by the round.
                if (ai < 0.25f)
                    Dust(b - dir * 0.12f + Vector3.up * (0.25f * ai), (0.45f + 1.6f * ai) * (0.8f + 0.3f * cal), new Color(0.50f, 0.46f, 0.38f, 0.75f * (1 - ai / 0.25f)), H(s, 58), 0.1f + 2.2f * ai);
                Blood(s, ai, b, dir, Mathf.Clamp(cal, 0.75f, 1.8f));
                return;
            }
            if (surface == Bags)
            {
                // Into a sandbag: a slap of pale dust off the face of the wall, chips of it, and sand running out of the hole.
                if (ai < 0.05f) AddGlow(end, 0.16f * cal, new Color(1f, 0.85f, 0.6f) * (2.5f * (1 - ai / 0.05f)), 0, H(s, 34));
                if (ai < 0.9f)
                {
                    float t = ai / 0.9f, f = 1 - t;
                    Dust(end - dir * ((0.2f + 0.8f * ai) * (0.7f + 0.3f * cal)) + Vector3.up * (0.1f + 0.5f * ai), (0.7f + 1.1f * t) * (0.7f + 0.3f * cal),
                         new Color(0.50f, 0.44f, 0.33f, 0.85f * f * f), H(s, 8), 0.1f + 0.8f * (1 - f * f));
                    // (A heavier round bursts the bag: more of it, thrown further.)
                    int chips = cal >= 1.25f ? 6 : 3;
                    for (int k = 0; k < chips; k++)
                    {
                        var v = -dir * (1.6f + 1.2f * H(s, 20 + k)) * (0.7f + 0.3f * cal) + Vector3.up * (0.8f + 1.0f * H(s, 23 + k)) * cal
                                + new Vector3(-dir.z, 0, dir.x) * (H(s, 26 + k) - 0.5f);
                        Clod(end + v * ai + Vector3.down * (4.9f * ai * ai), 0.07f + 0.05f * H(s, 29 + k), new Color(0.44f, 0.38f, 0.28f, 0.95f * f), H(s, 44 + k));
                    }
                    for (int k = 0; k < 2; k++)
                        Drop(end + Vector3.down * (0.5f * ai + 1.6f * ai * ai) + new Vector3(0.03f * k, 0, 0), 0.07f + 0.04f * k, new Color(0.46f, 0.39f, 0.27f, 0.9f * f), H(s, 50 + k));
                }
                return;
            }
            // Into the earth: a spit of dry dirt straight up at once, clods thrown about it, and the dust it leaves
            // hanging. (By the round: a pistol's from a submachine gun kicks up a handful; a rifle's a spurt as high
            // as a man's knee; a machine gun's or a sniper's a fountain to his waist and a cloud that hangs.)
            if (ai < 0.04f) AddGlow(end + Vector3.up * 0.05f, 0.12f * cal, new Color(1f, 0.8f, 0.55f) * (2f * (1 - ai / 0.04f)), 0, H(s, 35));
            float spurt = 0.34f * (0.75f + 0.25f * cal);
            if (ai < spurt)
            {
                float f = 1 - ai / spurt;
                // The spit of it: narrow and tall, pale against the grass, there and gone. (Clods alone were dark on
                // dark ground: looked for in a capture, a machine gun's burst could not be found.)
                float top = (0.55f + 0.4f * H(s, 36)) * cal * Mathf.Min(1f, ai / 0.06f);
                float wide = (0.34f + 0.5f * ai) * (0.7f + 0.3f * cal);
                Dust(end + Vector3.up * (0.42f * top) - new Vector3(dir.x, 0, dir.z) * 0.08f, wide, new Color(0.43f, 0.36f, 0.25f, 0.92f * f),
                     H(s, 37), 0.06f + 0.45f * (1 - f), Mathf.Max(1f, 1.5f * top / wide));
                int clods = Mathf.Clamp(Mathf.RoundToInt(4 * cal), 3, 8);
                for (int k = 0; k < clods; k++)
                {
                    float az = H(s, 20 + k) * 6.2832f;
                    var v = new Vector3(Mathf.Cos(az) * 0.9f * cal, (5.5f + 3.5f * H(s, 23 + k)) * (0.6f + 0.4f * cal), Mathf.Sin(az) * 0.9f * cal) - dir * 1.2f;
                    Clod(end + v * ai + Vector3.down * (9.8f * ai * ai), (0.06f + 0.05f * H(s, 29 + k)) * (0.7f + 0.3f * cal), new Color(0.13f, 0.1f, 0.07f, 0.95f * Mathf.Min(1f, f * 2f)), H(s, 26 + k));
                }
            }
            float hangs = 1.8f * (0.6f + 0.4f * cal);
            if (ai < hangs)
            {
                float t = ai / hangs, f = 1 - t;
                // One burst of dust from a tight ball to a cloud, lifting and going with the round a little.
                Dust(end + Vector3.up * ((0.18f + 0.55f * ai) * (0.7f + 0.3f * cal)) + new Vector3(dir.x, 0, dir.z) * (0.3f * ai),
                     (0.85f + 0.75f * t) * (0.55f + 0.45f * cal), new Color(0.41f, 0.35f, 0.26f, Mathf.Min(0.95f, 0.85f * (0.75f + 0.25f * cal)) * f * Mathf.Sqrt(f)),
                     H(s, 8), 0.05f + 0.9f * (1 - f * f));
            }
            // Now and then it glances off and goes on, lit, low and onward: a ricochet.
            if (Hash(s, 30) < RicochetShare && ai < 0.16f)
            {
                var flat = new Vector3(dir.x, 0, dir.z).normalized;
                var off = (flat + Vector3.up * (0.08f + 0.17f * H(s, 31))
                           + new Vector3(-flat.z, 0, flat.x) * ((H(s, 32) - 0.5f) * 0.4f)).normalized;
                float rhead = 190f * ai, rtail = Mathf.Max(0, rhead - 2.2f);
                AddStreak(end + off * rtail, end + off * rhead, 0.018f, new Color(1f, 0.62f, 0.25f) * (1.8f * (1 - ai / 0.16f)), H(s, 33));
                if (ai < 0.05f) AddGlow(end + Vector3.up * 0.05f, 0.22f, Flash * (6f * (1 - ai / 0.05f)), 0, H(s, 34));
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
                Dust(at, 0.6f + 3.6f * ai, new Color(0.30f, 0.02f, 0.02f, 0.95f * (1 - ai / 0.09f)), H(s, 60), 0.08f + 4f * ai);
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
                    Drop(p, 0.07f + 0.16f * ai + 0.05f * H(s, 90 + k), new Color(0.26f, 0.02f, 0.018f, 0.9f * f), H(s, 100 + k));
                }
                for (int k = 0; k < 2; k++)
                    Dust(at + flat * (0.2f + 0.7f * ai) + Vector3.up * (0.08f * k - 0.25f * ai * ai),
                         0.45f + (1.0f + 0.4f * k) * ai, new Color(0.22f, 0.025f, 0.02f, 0.55f * f * amount), H(s, 7 + k), 0.15f + 0.75f * (1 - f));
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
                && (ev[kill - 1].Kind == EventKind.Fire || ev[kill - 1].Kind == EventKind.Through || ev[kill - 1].Kind == EventKind.Stray || ev[kill - 1].Kind == EventKind.Melee)) return false;
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
            if (age >= 1.3f) return;
            Blood(i * 8 + 6, age, at, away, 2.2f);
            Blood(i * 8 + 7, age, at + Vector3.up * 0.3f, Quaternion.Euler(0, 55f, 0) * away, 1.2f);
            // Close to it, it throws more than blood out of him: pieces, dark, flung wide and falling. (With the
            // setting off, none.)
            float far = away.magnitude;
            if (far < 2.2f && Gore >= 1f && age < 1.1f)
            {
                var out_ = away / Mathf.Max(0.05f, far);
                float f = 1 - age / 1.1f;
                int pieces = far < 1.2f ? 12 : 7;
                for (int k = 0; k < pieces; k++)
                {
                    float az = (float)(Hash(i, 170 + k) * Math.PI * 2);
                    var v = out_ * (2.5f + 4f * (float)Hash(i, 185 + k)) + new Vector3(Mathf.Cos(az), 0, Mathf.Sin(az)) * 2.2f + Vector3.up * (3f + 3.5f * (float)Hash(i, 200 + k));
                    var p = at + v * age + Vector3.down * (4.9f * age * age);
                    if (p.y < at.y - 0.85f) continue;       // it has come down
                    Clod(p, 0.09f + 0.07f * H(i, 215 + k), new Color(0.24f, 0.02f, 0.018f, 0.95f * Mathf.Min(1f, f * 2.5f)), H(i, 230 + k));
                }
            }
        }

        /// <summary>Seconds a man bleeds from where a part of him came off.</summary>
        public const float BleedsFor = 3.4f;

        /// <summary>
        /// A man a part of whom has come off (a burst's doing, or a heavy round's): the blood from where it was,
        /// in spurts, weaker each time, for a few seconds. (The owner, 2026-10-10: "blood splatters, body
        /// disfigurement and gore".)
        /// </summary>
        private void Bleeding(SimEvent e, int i, float age)
        {
            if (_root.ArmyView == null || age > BleedsFor || Gore < 1f) return;
            for (int k = 0; k < SoldierFigure.MostOff; k++)
            {
                if (!_root.ArmyView.TryStump(e.Id, k, out var stump)) break;
                // A spurt every half second, each a little weaker than the last.
                float beat = age / 0.5f, phase = beat - Mathf.Floor(beat), left = 1f - age / BleedsFor;
                if (phase > 0.55f) continue;
                int s = i * 16 + 8 + k, n = (int)Mathf.Floor(beat);
                float az = (float)(Hash(s, 240 + n) * Math.PI * 2);
                var jet = new Vector3(Mathf.Cos(az) * 0.9f, 2.4f * left + 0.6f, Mathf.Sin(az) * 0.9f);
                float t = phase * 0.5f;
                for (int d = 0; d < 4; d++)
                {
                    float td = Mathf.Max(0f, t - 0.03f * d);
                    Drop(stump + jet * td + Vector3.down * (4.9f * td * td), 0.06f + 0.05f * left, new Color(0.30f, 0.02f, 0.018f, 0.9f * left), H(s, 250 + d));
                }
            }
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
                    Dust(feet + Vector3.up * (0.15f + 0.4f * ai + 0.2f * k), 0.7f + 1.0f * ai, new Color(0.30f, 0.26f, 0.20f, 0.5f * f * f), H(i, 40 + k), 0.15f + 0.8f * (1 - f));
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
            if (age >= 1.8f && age >= flight + 1.6f) return;    // landed, and the smoke of the shot and of its flight gone
            var a = Chest(by);
            double lx = e.X.Value, lz = e.Z.Value;
            // (Fired at a man up on a post, it goes up there.)
            float aloft = e.Target.HasValue && e.Target.Value < men.Count ? (float)men[e.Target.Value].High : 0f;
            var to = Coords.World(lx, lz, (float)_root.Ground.HeightAt(lx, lz) + 0.1f + aloft);
            var from = FiredFrom(i, _root.ArmyView != null && _root.ArmyView.TryMuzzle(by.Id, out var mz) ? mz : a + (to - a).normalized * 0.6f);
            var dir = (to - from).normalized;
            bool rocket = by.Weapon == Weapon.Rpg, mortar = by.Weapon == Weapon.Mortar;

            if (age < 0.07f)
            {
                float k = 1 - age / 0.07f;
                AddSprite(FxSheets.Flame, H(i, 49) * 32f + age * 40f, from + dir * 0.15f, rocket ? 1.3f : 0.7f, Lit(Flash, 5f * k), H(i, 48) * 6.2832f);
                AddGlow(from, rocket ? 1.8f : 1.0f, Lit(Flash, 1.6f * k), 2, H(i, 50));
            }
            if (age < 1.8f)
            {
                // The smoke of the shot: at the muzzle, and for a rocket a great deal more of it behind him.
                float f = 1 - age / 1.8f;
                Hang(from + dir * (0.3f * (1 - Mathf.Exp(-age * 12f))) + Vector3.up * (0.3f * age), 0.6f + 1.3f * Mathf.Sqrt(age), new Color(0.62f, 0.62f, 0.6f, 0.5f * f * f), H(i, 51), age);
                if (rocket)
                    // The backblast: out of the venturi behind him, and the dust it lifts.
                    for (int k = 0; k < 3; k++)
                        Dust(a - dir * (0.9f + (1.3f + 0.9f * k) * Mathf.Min(age * 3f, 1f)) + Vector3.up * (0.2f * k * age),
                             1.3f + (1.7f + 0.4f * k) * age, new Color(0.58f, 0.56f, 0.52f, 0.62f * f * f), H(i, 52 + k), 0.1f + 0.85f * (1 - f * f));
            }

            float t = age / flight;
            float span = Vector3.Distance(from, to);
            float apex = rocket ? 0.15f : mortar ? 14f + 0.2f * span : 0.6f + 0.05f * span;
            if (rocket)
                // The motor's smoke, hanging along the way it came for a second and a half after it has gone by.
                for (int k = 1; k <= 7; k++)
                {
                    float tk = k / 8f, since = age - tk * flight;
                    if (since < 0 || since > 1.6f) continue;
                    float fk = 1 - since / 1.6f;
                    var pk = Vector3.Lerp(from, to, tk) + Vector3.up * (4f * apex * tk * (1 - tk) + 0.25f * since) + Vector3.right * (0.3f * since);
                    Hang(pk, 0.45f + 0.9f * Mathf.Sqrt(since), new Color(0.7f, 0.7f, 0.68f, 0.5f * fk * fk), H(i, 62 + k), since);
                }
            if (age >= flight) return;

            var p = Vector3.Lerp(from, to, t) + Vector3.up * (4f * apex * t * (1 - t));
            if (rocket)
            {
                AddSprite(FxSheets.Flame, H(i, 60) * 32f + age * 30f, p - dir * 0.35f, 0.6f, Lit(new Color(1f, 0.75f, 0.45f), 4.5f), H(i, 60) * 6.2832f);
                AddStreak(p - dir * 1.2f, p, 0.07f, new Color(1f, 0.6f, 0.25f) * 3f, H(i, 61));
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

        /// <summary>
        /// A round fired at one man that found another (the sim's Stray, MatchOptions.Ballistics): the slap of it
        /// on him and what it throws out of him, as a round that was meant for him would. (Its flash and its
        /// flight are the shot's own, drawn with the shot.)
        /// </summary>
        private void Strayed(SimEvent e, int i, float age)
        {
            var men = _root.Driver.State.Men;
            if (e.Target == null || e.Id >= men.Count || e.Target.Value >= men.Count) return;
            var a = Chest(men[e.Id]);
            var b = Chest(men[e.Target.Value]);
            float dist = Vector3.Distance(a, b);
            var dir = (b - a) / Mathf.Max(0.01f, dist);
            float ai = age - dist / TracerSpeed;
            if (ai < 0) return;
            float cal = Calibre(men[e.Id].Weapon);
            if (ai < 0.22f)
            {
                float f = 1 - ai / 0.22f;
                Dust(b - dir * 0.12f + Vector3.up * (0.25f * ai), (0.45f + 1.6f * ai) * (0.8f + 0.3f * cal), new Color(0.50f, 0.46f, 0.38f, 0.75f * f), H(i, 58), 0.1f + 2.2f * ai);
            }
            Blood(i * 16 + 7, ai, b, dir, Mathf.Clamp(cal, 0.75f, 1.8f));
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

        // --- what was called for, coming -----------------------------------------------------

        /// <summary>
        /// The jet: metres a second (an F-100 came in at twice that; at this the eye has it for a second
        /// and a half across the screen), metres over the ground at its lowest, the seconds after its
        /// first bomb bursts that it is over the middle of them, and the seconds a bomb falls. And how far
        /// beyond the lane it flies, away from the lens, and how far up the picture it is held as it
        /// crosses (0 the bottom, 1 the top): over the lane itself, at any height an aircraft flies at,
        /// it is above the top of the picture, and what was called is to be seen coming. Its height is
        /// whatever puts it there at this zoom, and never under JetLow nor over JetHigh.
        /// </summary>
        public const float JetSpeed = 120f, JetLow = 12f, JetHigh = 45f, JetOver = 0.6f, BombFalls = 0.9f, JetBeyond = 60f, JetUpPicture = 0.86f;
        /// <summary>How much larger than a mortar's bomb the jet's are drawn: a 750 pound bomb is over two metres long.</summary>
        public const float BombLarger = 3.2f;
        /// <summary>The helicopter: seconds it stands on the ground, seconds it takes to be gone, and how far off and how high it is when it is called.</summary>
        public const float DustoffDown = 3f, DustoffAway = 7f, DustoffFrom = 170f, DustoffHigh = 42f;
        /// <summary>Turns a second of a UH-1's main rotor (324 a minute).</summary>
        public const float RotorTurns = 324f / 60f;

        private Transform _jet, _huey, _rotor;
        private bool _jetShown, _hueyShown;
        /// <summary>Whether an aircraft was drawn last frame, for the tests.</summary>
        public bool JetSeen => _jet != null && _jet.gameObject.activeSelf;
        public bool HueySeen => _huey != null && _huey.gameObject.activeSelf;
        public Transform JetAt => _jet;
        public Transform HueyAt => _huey;
        // (The meshes are built with their noses to -x.)
        private static readonly Quaternion NoseAhead = Quaternion.Inverse(Quaternion.LookRotation(Vector3.left, Vector3.up));

        private Transform Craft(string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Aircraft;
            mr.shadowCastingMode = ShadowCastingMode.On;
            return go.transform;
        }

        /// <summary>
        /// A call on its way (MatchOptions.Inbound), by how long ago it was made: the simulation says what
        /// was called, where, and how many ticks it had to come. Like everything here it has no clock of
        /// its own: the jet is where the call's age puts it.
        /// </summary>
        private void Called(SimEvent e, double now)
        {
            if (e.X == null || e.Z == null) return;
            // (Its own time, not spread within its tick as a volley's shots are: it is one thing.)
            float t = (float)(now - e.Tick * Tune.Dt);
            float lands = (float)((e.Amount ?? 0) * Tune.Dt);
            if (t < 0) return;
            if (e.Rounds == Inbound.Jet) JetPass(e, t, lands);
            else if (e.Rounds == Inbound.Dustoff) Dustoff(e, t, lands);
        }

        /// <summary>Coloured smoke on the ground where something is wanted: a column of it, a few puffs each rising and thinning and begun again.</summary>
        private void MarkSmoke(Vector3 at, Color col, float t, float until, int seed)
        {
            float ends = Mathf.Clamp01(t / 0.6f) * Mathf.Clamp01((until - t) / 1.5f);
            if (ends <= 0) return;
            const int puffs = 8;
            for (int k = 0; k < puffs; k++)
            {
                float phase = Mathf.Repeat(t * 0.25f + k / (float)puffs, 1f);
                float a = phase * 4f;
                if (a > t) continue;                      // not begun yet
                col.a = 0.5f * ends * (1 - phase) * Mathf.Clamp01(a / 0.3f);
                Hang(at + new Vector3(0.8f * a + (H(seed, 300 + k) - 0.5f) * 0.4f, 0.4f + 1.3f * a, (H(seed, 310 + k) - 0.5f) * 0.4f),
                     1.4f + 1.0f * a, col, H(seed, 320 + k), a);
            }
        }

        /// <summary>
        /// The air strike: the target marked in white as the forward air controllers marked theirs, the jet
        /// down out of the sky behind its own side, low over the target once, its bombs off it and falling
        /// to where the simulation bursts them, and up and away. One pass.
        /// </summary>
        private void JetPass(SimEvent e, float t, float lands)
        {
            double ax = e.X.Value, az = e.Z.Value;
            float dir = e.Side == Side.Us ? 1f : -1f;
            float ground = (float)_root.Ground.HeightAt(ax, az);
            int seed = e.Id * 7919 + e.Tick;
            MarkSmoke(Coords.World(ax, az, ground), new Color(0.93f, 0.93f, 0.90f), t, lands + 0.4f, seed);
            if (NoAircraft || Jet == null || Aircraft == null) return;
            float over = lands + JetOver;
            var away = _root.CameraRig.Camera.transform.forward; away.y = 0;
            var beyond = away.normalized * JetBeyond;
            var lens = _root.CameraRig.Camera;
            var over0 = Coords.World(ax, az, ground) + beyond;
            float depth = Vector3.Dot(over0 - _camPos, lens.transform.forward);
            float low = Mathf.Clamp(lens.ViewportToWorldPoint(new Vector3(0.5f, JetUpPicture, depth)).y - ground, JetLow, JetHigh);
            Vector3 At(float time)
            {
                float s = JetSpeed * (time - over);
                // A shallow dive onto it, and a hard pull out of it.
                float up = Mathf.Max(0f, time - over);
                return Coords.World(ax + dir * s, az, ground + low + (s < 0 ? -s * 0.10f : 0f) + 16f * up * up) + beyond;
            }
            // Its bombs: each off the jet as it comes, nose along its fall, to where it will burst.
            for (int k = 0; k < Tune.StickBombs; k++)
            {
                float bursts = lands + (float)Tune.Dt * (1 + k * Tune.StickGap);
                float u = (t - (bursts - BombFalls)) / BombFalls;
                if (u < 0 || u >= 1) continue;
                Inbound.Stick(e.Side, e.Id, ax, az, k, out double bx, out double bz);
                var from = At(bursts - BombFalls) + Vector3.down * 1.2f;
                var to = Coords.World(bx, bz, (float)_root.Ground.HeightAt(bx, bz));
                Vector3 Falling(float v)
                {
                    var q = Vector3.Lerp(from, to, v);
                    q.y = to.y + (from.y - to.y) * (1 - v * v);
                    return q;
                }
                var p = Falling(u);
                var way = Falling(Mathf.Min(1f, u + 0.03f)) - p;
                Show(Ordnance.Kind.Bomb, p, way.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(Vector3.up, way) : Quaternion.identity, BombLarger);
            }
            if (Mathf.Abs(t - over) * JetSpeed > 900f || _jetShown) return;
            if (_jet == null) _jet = Craft("aircraft jet", Jet);
            var here = At(t);
            var ahead = At(t + 0.02f) - here;
            // It rolls away as it pulls up.
            float roll = -28f * Mathf.SmoothStep(0f, 1f, (t - over) / 1.2f);
            _jet.SetPositionAndRotation(here, Quaternion.LookRotation(ahead.normalized, Vector3.up) * Quaternion.AngleAxis(roll, Vector3.forward) * NoseAhead);
            _jetShown = true;
        }

        /// <summary>
        /// The medevac: violet smoke where it is wanted, the helicopter in from behind its own side, slowing
        /// and settling onto its skids as the simulation's eight seconds run out, three seconds on the ground
        /// with its rotor turning and the dust blown out from under it, then up, round and back the way it came.
        /// </summary>
        private void Dustoff(SimEvent e, float t, float lands)
        {
            double ax = e.X.Value, az = e.Z.Value;
            float dir = e.Side == Side.Us ? 1f : -1f;
            float ground = (float)_root.Ground.HeightAt(ax, az);
            int seed = e.Id * 7919 + e.Tick;
            var lz = Coords.World(ax, az, ground);
            MarkSmoke(lz + new Vector3(-dir * 9f, 0, 0), new Color(0.56f, 0.24f, 0.78f), t, lands + DustoffDown + 2f, seed);
            if (NoAircraft || Huey == null || Aircraft == null || _hueyShown || t > lands + DustoffDown + DustoffAway) return;
            float s, h, yaw = 0f, nose;
            if (t < lands)
            {
                float left = 1 - t / lands;
                s = -DustoffFrom * left * left;
                h = DustoffHigh * Mathf.Pow(left, 1.6f);
                // Nose down as it comes, up as it slows, level as it sets down.
                nose = Mathf.Lerp(-5f, 9f, Mathf.SmoothStep(0f, 1f, (0.75f - left) / 0.45f)) * Mathf.SmoothStep(0f, 1f, left / 0.12f);
            }
            else if (t < lands + DustoffDown) { s = 0; h = 0; nose = 0; }
            else
            {
                float v = t - lands - DustoffDown;
                h = Mathf.Min(DustoffHigh, 1.7f * v * v);
                yaw = 180f * Mathf.SmoothStep(0f, 1f, v / 2.4f);
                float going = Mathf.Max(0f, v - 1.6f);
                s = -6f * going * going;
                nose = -9f * Mathf.SmoothStep(0f, 1f, going / 1.5f);
            }
            if (_huey == null)
            {
                _huey = Craft("aircraft huey", Huey);
                if (Rotor != null)
                {
                    _rotor = Craft("rotor", Rotor);
                    _rotor.SetParent(_huey, false);
                    _rotor.localPosition = RotorHub;
                }
            }
            var facing = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.LookRotation(new Vector3(dir, 0, 0), Vector3.up);
            _huey.SetPositionAndRotation(lz + new Vector3(dir * s, h, 0), facing * Quaternion.AngleAxis(-nose, Vector3.right) * NoseAhead);
            if (_rotor != null) _rotor.localRotation = Quaternion.AngleAxis(Mathf.Repeat(t * RotorTurns, 1f) * 360f, Vector3.up);
            _hueyShown = true;
            // What its rotor blows out from under it, near the ground.
            float low = 1 - h / 9f;
            if (low <= 0 || s < -25f) return;
            for (int k = 0; k < 9; k++)
            {
                float phase = Mathf.Repeat(t * 0.8f + H(seed, 320 + k), 1f);
                float ang = H(seed, 330 + k) * 6.2832f + t * 0.5f;
                float r = 3.5f + 8f * phase;
                Dust(lz + new Vector3(dir * s + Mathf.Cos(ang) * r, 0.5f + 0.9f * phase, Mathf.Sin(ang) * r), 2.6f + 3.2f * phase,
                     new Color(0.30f, 0.25f, 0.18f, 0.34f * low * (1 - phase) * Mathf.Clamp01(phase / 0.15f)), H(seed, 340 + k), 0.25f + 0.65f * phase);
            }
        }

        // --- what is in the air, as things --------------------------------------------------

        private readonly List<MeshFilter> _ordnance = new List<MeshFilter>();
        private int _ordnanceShown;
        private Material _ordnanceMaterial;

        /// <summary>A grenade, a round or a bomb at a place this frame (pooled; what is not shown again is hidden).</summary>
        private void Show(Ordnance.Kind kind, Vector3 at, Quaternion turn, float larger = 1f)
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
            mf.transform.localScale = new Vector3(larger, larger, larger);
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
            // (A launcher's round fired at a man up on a post bursts up there: the event says how high.)
            var p0 = Coords.World(x, z, (float)_root.Ground.HeightAt(x, z) + (float)(e.Amount ?? 0));
            int seed = e.Id * 6151 + e.Tick;
            Explosions++;

            if (age < 0.09f)
            {
                float k = 1 - age / 0.09f;
                // A soft ball of light with a ball of flame in it (a star, large, read as a paper cut-out).
                AddGlow(p0 + Vector3.up * 0.5f, 2.6f + 16f * age, Flash * (16f * k), 2, H(seed, 0));
                AddSprite(FxSheets.Flame, H(seed, 2) * 32f + age * 40f, p0 + Vector3.up * 0.5f, 1.3f + 9f * age, Lit(Flash, 7f * k), H(seed, 3) * 6.2832f);
            }
            if (age < 0.32f)
            {
                float t = age / 0.32f;
                var col = Color.Lerp(new Color(1f, 0.6f, 0.25f) * 8f, new Color(0.7f, 0.2f, 0.06f) * 1.5f, t) * (1 - t);
                AddGlow(p0 + Vector3.up * (0.6f + 1.6f * age), 1.3f + 3.2f * age, col, 2, H(seed, 1));
            }
            if (age < 0.25f) _flashes.Add((p0 + Vector3.up * 1.2f, 55f * Mathf.Exp(-age * 20f)));
            // Fragments: hot for a tenth of a second, out along the ground and up.
            if (age < 0.12f)
                for (int k = 0; k < 10; k++)
                {
                    float az = H(seed, 100 + k) * 6.2832f, el = 0.1f + 0.9f * H(seed, 110 + k);
                    var d = new Vector3(Mathf.Cos(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(az) * Mathf.Cos(el));
                    float head = 70f * age, tail = Mathf.Max(0, head - 1.6f);
                    AddStreak(p0 + Vector3.up * 0.2f + d * tail, p0 + Vector3.up * 0.2f + d * head, 0.03f,
                              new Color(1f, 0.7f, 0.3f) * (4f * (1 - age / 0.12f)), H(seed, 120 + k));
                }
            // The earth it lifted: a dark burst of it, jets out of that, and clods thrown three or four metres up and falling back.
            if (age < 1.6f)
            {
                float t = age / 1.6f, f = 1 - t;
                for (int k = 0; k < 2; k++)
                    Dust(p0 + Vector3.up * (0.7f + 1.3f * age + 0.3f * k) + new Vector3((H(seed, 150 + k) - 0.5f) * 0.8f, 0, (H(seed, 152 + k) - 0.5f) * 0.8f),
                         (3.4f + 2.2f * t) * (0.85f + 0.3f * k), new Color(0.13f, 0.105f, 0.08f, 0.95f * f * Mathf.Sqrt(f)), H(seed, 154 + k), 0.04f + 0.9f * (1 - f * f), 1.15f);
                for (int k = 0; k < 5; k++)
                {
                    // A jet: tall and narrow, leaning out from the middle of it.
                    float lean = (H(seed, 160 + k) - 0.5f) * 2.0f;
                    float reach = (1.3f + 1.5f * H(seed, 165 + k)) * (1 - Mathf.Exp(-age * 7f));
                    var up = _up * Mathf.Cos(lean) - _right * Mathf.Sin(lean);
                    Dust(p0 + Vector3.up * 0.2f + up * (reach * 0.9f), 0.9f + 0.6f * t, new Color(0.10f, 0.08f, 0.06f, 0.9f * f * f), 0.5f, 0.1f + 0.7f * t, 2.4f, lean);
                }
                for (int k = 0; k < 10; k++)
                {
                    float az = H(seed, 10 + k) * 6.2832f;
                    float vh = 1.2f + 3.2f * H(seed, 20 + k);
                    float vv = 5.5f + 4.5f * H(seed, 30 + k);
                    var pos = p0 + new Vector3(Mathf.Cos(az) * vh * age, vv * age - 4.9f * age * age, Mathf.Sin(az) * vh * age);
                    if (pos.y < p0.y - 0.15f) continue;
                    Clod(pos, 0.10f + 0.14f * H(seed, 40 + k), new Color(0.08f, 0.06f, 0.045f, 0.95f * Mathf.Min(1f, f * 3f)), H(seed, 45 + k));
                }
            }
            // Dust along the ground, in a ring.
            if (age < 2.2f)
                for (int k = 0; k < 6; k++)
                {
                    float az = k * 1.047f + H(seed, 130 + k);
                    float rr = 4.2f * (1 - Mathf.Exp(-age * 3.5f));
                    float f = 1 - age / 2.2f;
                    Dust(p0 + new Vector3(Mathf.Cos(az) * rr, 0.35f + 0.3f * age, Mathf.Sin(az) * rr), 1.8f + 1.6f * age,
                         new Color(0.30f, 0.25f, 0.19f, 0.5f * f * f), H(seed, 140 + k), 0.25f + 0.7f * (1 - f));
                }
            // The smoke it leaves: grey-black, rising and leaning downwind.
            for (int k = 0; k < 5; k++)
            {
                float a = age - (0.06f + 0.07f * k);
                const float life = 5.5f;
                if (a < 0 || a > life) continue;
                float rise = 0.8f * a + 1.6f * (1 - Mathf.Exp(-a * 1.8f));
                var off = new Vector3((H(seed, 50 + k) - 0.5f) * 1.6f + 0.6f * a, rise * (0.5f + 0.6f * H(seed, 60 + k)), (H(seed, 70 + k) - 0.5f) * 1.6f);
                float op = Mathf.SmoothStep(0, 1, a / 0.25f) * Mathf.Pow(1 - a / life, 1.5f) * 0.75f;
                var col = Color.Lerp(new Color(0.2f, 0.19f, 0.18f), new Color(0.07f, 0.06f, 0.05f), Mathf.Exp(-a * 0.9f));
                col.a = op;
                Hang(p0 + Vector3.up * 0.6f + off, 2.4f + 1.2f * a, col, H(seed, 80 + k), a);
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
                AddGlow(p0 + Vector3.up * 1.2f * scale, (5f + 26f * age) * scale, Flash * (20f * k * scale), 2, H(seed, 0));
                AddSprite(FxSheets.Flame, H(seed, 2) * 32f + age * 40f, p0 + Vector3.up * 1.2f * scale, (2.4f + 14f * age) * scale, Lit(Flash, 9f * k), H(seed, 3) * 6.2832f);
            }
            if (age < 0.6f)
            {
                float t = age / 0.6f;
                var col = Color.Lerp(new Color(1f, 0.55f, 0.22f) * 6f, new Color(0.75f, 0.2f, 0.06f) * 1.5f, t) * (1 - t);
                AddGlow(p0 + Vector3.up * (1.1f + 2.5f * age) * scale, (1.8f + 5f * age) * scale, col, 2, H(seed, 1));
            }
            if (age < 0.3f) _flashes.Add((p0 + Vector3.up * 2f, 90f * Mathf.Exp(-age * 18f) * scale));
            // The fire of it: a ball of flame gone to black smoke in under a second, drawn over the scene, and
            // the light of the flame added on top. (High explosive on earth is mostly earth: the fire is brief.
            // Played over a second and a half, flames hung in the air after every shell like burning fuel.)
            float burns = 0.9f * Mathf.Sqrt(scale);
            if (age < burns)
            {
                float t = age / burns;
                var at = p0 + Vector3.up * ((1.5f + 1.8f * age) * scale);
                float size = (4.4f + 3.0f * t) * scale, turn = (H(seed, 4) - 0.5f) * 0.5f;
                AddPuff(Sheet.Blast, t * 24f, at, size, size, new Color(1f, 1f, 1f, Mathf.Clamp01((1 - t) / 0.5f)), turn, false, 1.2f * scale);
                if (t < 0.4f) AddSprite(FxSheets.Fire, t * 24f, at, size, Lit(Color.white, 3.2f * (1 - t / 0.4f)), turn);
            }

            // The earth it throws up: a dark burst of it, jets out of that, and clods on their arcs.
            float lifts = 2.6f * Mathf.Sqrt(scale);
            if (age < lifts)
            {
                float t = age / lifts, f = 1 - t;
                for (int k = 0; k < 2; k++)
                    Dust(p0 + Vector3.up * ((1.2f + 2.0f * age + 0.8f * k) * scale) + new Vector3((H(seed, 150 + k) - 0.5f) * 2.4f, 0, (H(seed, 153 + k) - 0.5f) * 2.4f) * scale,
                         (6.8f + 4.5f * t) * (0.85f + 0.3f * k) * scale, new Color(0.115f, 0.095f, 0.075f, 0.96f * f * Mathf.Sqrt(f)), H(seed, 156 + k), 0.03f + 0.92f * (1 - f * f), 1.2f);
                for (int k = 0; k < 5; k++)
                {
                    float lean = (H(seed, 160 + k) - 0.5f) * 2.2f;
                    float reach = (3.0f + 3.5f * H(seed, 167 + k)) * scale * (1 - Mathf.Exp(-age * 6f));
                    var up = _up * Mathf.Cos(lean) - _right * Mathf.Sin(lean);
                    Dust(p0 + Vector3.up * (0.4f * scale) + up * (reach * 0.85f), (1.9f + 1.2f * t) * scale, new Color(0.09f, 0.075f, 0.06f, 0.92f * f * f), 0.5f, 0.08f + 0.75f * t, 2.6f, lean);
                }
                for (int k = 0; k < 10; k++)
                {
                    float az = H(seed, 10 + k) * 6.2832f;
                    float vh = (2f + 5f * H(seed, 20 + k)) * scale;
                    float vv = (7f + 7f * H(seed, 30 + k)) * Mathf.Sqrt(scale);
                    var pos = p0 + new Vector3(Mathf.Cos(az) * vh * age, vv * age - 4.9f * age * age, Mathf.Sin(az) * vh * age);
                    if (pos.y < p0.y - 0.2f) continue;
                    Clod(pos, (0.18f + 0.3f * H(seed, 40 + k)) * scale, new Color(0.07f, 0.055f, 0.04f, 0.95f * Mathf.Min(1f, f * 3f)), H(seed, 45 + k));
                }
            }
            // The smoke: rising, spreading and drifting downwind (+x), thinning out. Four clouds of it, each
            // the burst's own sheet played slowly through its wide end, so it has a body and an edge. (As eleven
            // soft blobs, and then as six sheets of wisp nineteen metres across, a barrage's smoke was one even
            // brown fog from the grass to the sky.)
            for (int k = 0; k < 4; k++)
            {
                float a = age - (0.12f + 0.16f * k);
                float life = 8f * Mathf.Sqrt(scale);
                if (a < 0 || a > life) continue;
                float rise = 0.7f * a + 2.0f * (1 - Mathf.Exp(-a * 1.5f));
                var off = new Vector3((H(seed, 50 + k) - 0.5f) * 2.5f + 0.7f * a, rise * (0.6f + 0.6f * H(seed, 60 + k)), (H(seed, 70 + k) - 0.5f) * 2.5f) * scale;
                // HE on earth: brown-grey, dark while it is still mostly dirt, thinning as it rises.
                float op = Mathf.SmoothStep(0, 1, a / 0.4f) * Mathf.Pow(1 - a / life, 1.6f) * 0.85f;
                float dirt = Mathf.Exp(-a * 0.6f);
                var col = Color.Lerp(new Color(0.20f, 0.19f, 0.17f), new Color(0.10f, 0.085f, 0.07f), dirt);
                col.a = op;
                Dust(p0 + Vector3.up * scale + off, (4.9f + 0.85f * a) * scale, col, H(seed, 80 + k), 0.32f + 0.6f * a / life);
            }
        }

        // --- smoke screens -------------------------------------------------------------------

        private void SmokeScreens(SimState st, double now)
        {
            foreach (var ar in st.Areas)
            {
                if (ar.Kind != AreaKind.Smoke || ar.Wait > 0) continue;
                float left = (float)(ar.Ticks * Tune.Dt);
                float density = Mathf.Clamp01(left / 4f);
                int n = 8 + (int)(ar.Radius * 0.75);
                for (int k = 0; k < n; k++)
                {
                    int seed = ar.Id * 1000 + k;
                    double ang = Hash(seed, 0) * Math.PI * 2, r = Math.Sqrt(Hash(seed, 1)) * ar.Radius;
                    float t = (float)now;
                    double x = ar.X + Math.Cos(ang) * r + Math.Sin(t * 0.13 + k) * 1.2;
                    double z = ar.Z + Math.Sin(ang) * r;
                    // Puffs sized to the screen: a fire mission's 14 m bank or a squad's 3.5 m canister. Each a
                    // thick cloud from the middle of the burst's sheet, turning over slowly as it hangs.
                    float size = (float)ar.Radius * (0.5f + 0.3f * H(seed, 3));
                    float y = (float)_root.Ground.HeightAt(x, z) + size * (0.2f + 0.3f * H(seed, 2));
                    Dust(Coords.World(x, z, y), size, new Color(0.60f, 0.60f, 0.58f, 0.62f * density), H(seed, 4),
                         0.5f + 0.14f * Mathf.Sin(t * 0.21f + 6.2832f * H(seed, 5)));
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
        private struct Fresh { public int Event; public bool Shot, Burst; public double FromX, FromZ; public float Cal; }

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
                                && (ev[i - 1].Kind == EventKind.Fire || ev[i - 1].Kind == EventKind.Through || ev[i - 1].Kind == EventKind.Stray);
                    bool burst = Burst(ev, i, out double fx, out double fz);
                    // (How heavy a round: the firer's; one that came through another man is a rifle's.)
                    float cal = !shot ? 0f : ev[i - 1].Kind == EventKind.Through || ev[i - 1].Id >= st.Men.Count ? 1f : Calibre(st.Men[ev[i - 1].Id].Weapon);
                    _fresh.Add(new Fresh { Event = i, Shot = shot, Burst = burst, FromX = fx, FromZ = fz, Cal = cal });
                }
                else if ((e.Kind == EventKind.Shell || e.Kind == EventKind.GrenadeBlast) && e.X != null) _fresh.Add(new Fresh { Event = i });
            }

            _live.Clear();
            for (int i = 0; i < st.Men.Count; i++)
            {
                // (A man up on a post has no shadow on the ground under it.)
                if (_lies[i] || st.Men[i].High > 1) continue;
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
                    if (st.Men[e.Id].High <= 1 || st.Men[e.Id].Climb != 0) Shadow(_settled, st.Men[e.Id]);
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
            // (A man killed up on a post bleeds up there, not on the ground under it.)
            if (st.Men[e.Id].High > 1 && st.Men[e.Id].Climb == 0) return;
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
            }
            // Where what came off him came down (a burst's doing, or a heavy round's): each part its own stain.
            for (int k = 0; k < SoldierFigure.MostOff && since >= 1.2f && _root.ArmyView != null; k++)
                if (_root.ArmyView.TryLimb(e.Id, k, out var limb))
                    Flat(into, limb.x, Coords.SimZ(limb.z), 0.7f, 0.55f, new Color(0.33f, 0.04f, 0.03f, 0.9f * Mathf.Clamp01((since - 1.2f) / 1.5f)), (float)Hash(e.Id, 158 + k), 1f);
            if (since >= 0.25f && shot)
            {
                // Shot: what the round threw out of him lies on the ground beyond
                // where he stood, the way it was going (it came from the other side).
                var man = st.Men[e.Id];
                double go = Combat.Advance(Combat.Other(man.Side));
                float show = Mathf.Clamp01((since - 0.25f) / 0.3f);
                // (A heavy round throws more of it, and further: a machine gun's, a sniper's.)
                float cal = Mathf.Max(0.7f, how.Cal);
                int spots = gore < 1f ? 2 : cal >= 1.25f ? 7 : 4;
                for (int k = 0; k < spots; k++)
                {
                    double reach = (0.8 + 1.1 * k + 0.9 * Hash(e.Id, 110 + k)) * (0.75 + 0.3 * cal);
                    float spot = (0.55f - (k < 4 ? 0.12f * k : 0.4f) + 0.2f * (float)Hash(e.Id, 113 + k)) * (0.8f + 0.25f * cal);
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
            _gp.Clear(); _gc.Clear(); _guv.Clear(); _guv2.Clear(); _gi.Clear();
            _puffs.Clear(); _flashes.Clear();
            Tracers = Flashes = Explosions = Traces = Cases = Faint = 0;
            _ordnanceShown = 0;
            _jetShown = _hueyShown = false;
        }

        /// <summary>A colour as light, so many times as bright: what a frame of the sheet of fire is multiplied by.</summary>
        private static Color Lit(Color c, float times) => new Color(c.r * times, c.g * times, c.b * times, 1f);

        private void AddGlow(Vector3 c, float size, Color col, float kind, float seed)
        {
            float rot = seed * 6.2832f;
            var r = (_right * Mathf.Cos(rot) + _up * Mathf.Sin(rot)) * size * 0.5f;
            var u = (-_right * Mathf.Sin(rot) + _up * Mathf.Cos(rot)) * size * 0.5f;
            Quad(c - r - u, c + r - u, c + r + u, c - r + u, col, new Vector4(0, 0, 1, 1), seed, kind, default);
        }

        /// <summary>
        /// The least a round's trace is drawn across, in pixels to each side of its middle. The streak is bright
        /// along its middle third and nothing at its edges, so this leaves a line about a pixel wide. (At its
        /// own 3 cm it was half a pixel from the far end of the zoom, and looked for in a capture there it was
        /// not to be found.)
        /// </summary>
        public const float TracePixels = 1.6f;

        /// <summary>A streak of light from one point to another, so wide to each side; <paramref name="pixels"/>: and never thinner on the screen than this.</summary>
        private void AddStreak(Vector3 a, Vector3 b, float width, Color col, float seed, float pixels = 0f)
        {
            var mid = (a + b) * 0.5f;
            if (pixels > 0f) width = Mathf.Max(width, pixels * _perPixel * Vector3.Distance(mid, _camPos));
            var w = Vector3.Cross(b - a, _camPos - mid).normalized * width;
            // uv.x runs along the streak, so the shader's "across" is uv.y.
            Quad(a - w, b - w, b + w, a + w, col, new Vector4(0, 0, 1, 1), seed, 1, default);
        }

        /// <summary>A frame of the sheet of fire, facing the lens: the star out of a flash hider, a shell's fire.</summary>
        private void AddSprite(in FxSheet sheet, float frame, Vector3 c, float size, Color col, float rot)
        {
            var r = (_right * Mathf.Cos(rot) + _up * Mathf.Sin(rot)) * size * 0.5f;
            var u = (-_right * Mathf.Sin(rot) + _up * Mathf.Cos(rot)) * size * 0.5f;
            int f = Mathf.FloorToInt(frame);
            Quad(c - r - u, c + r - u, c + r + u, c - r + u, col, sheet.Rect(f), frame - f, 3, sheet.Rect(f + 1));
        }

        /// <summary>A frame of the sheet of fire laid along a line, its left edge at <paramref name="a"/>: the gas out of a muzzle.</summary>
        private void AddFlare(in FxSheet sheet, int frame, Vector3 a, Vector3 b, float halfWidth, Color col)
        {
            var w = Vector3.Cross(b - a, _camPos - (a + b) * 0.5f).normalized * halfWidth;
            Quad(a - w, b - w, b + w, a + w, col, sheet.Rect(frame), 0f, 3, sheet.Rect(frame));
        }

        /// <summary>A quad of light: its corners, what is at them in uv (left, bottom, right, top), and for a frame of a sheet the next frame's.</summary>
        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, Vector4 at, float z, float kind, Vector4 next)
        {
            int i = _gp.Count;
            _gp.Add(a); _gp.Add(b); _gp.Add(c); _gp.Add(d);
            for (int k = 0; k < 4; k++) _gc.Add(col);
            _guv.Add(new Vector4(at.x, at.y, z, kind)); _guv.Add(new Vector4(at.z, at.y, z, kind));
            _guv.Add(new Vector4(at.z, at.w, z, kind)); _guv.Add(new Vector4(at.x, at.w, z, kind));
            _guv2.Add(new Vector4(next.x, next.y)); _guv2.Add(new Vector4(next.z, next.y));
            _guv2.Add(new Vector4(next.z, next.w)); _guv2.Add(new Vector4(next.x, next.w));
            _gi.Add(i); _gi.Add(i + 1); _gi.Add(i + 2); _gi.Add(i); _gi.Add(i + 2); _gi.Add(i + 3);
        }

        /// <summary>
        /// A frame of a sheet of smoke at a place: so wide and so tall, turned about the line of
        /// sight, fading into whatever is behind it over <paramref name="soft"/> metres. One too
        /// faint to see is not drawn: a shell's smoke in its last seconds was eleven quads each
        /// half the screen wide, blended in at a fiftieth of nothing.
        /// </summary>
        private void AddPuff(Sheet sheet, float frame, Vector3 pos, float wide, float tall, Color col, float rot, bool flip, float soft)
        {
            if (col.a < FaintBelow) { Faint++; return; }
            _puffs.Add(new Puff { Pos = pos, Wide = wide, Tall = tall, Rot = rot, Soft = soft, Sheet = sheet, Frame = frame, Flip = flip, Col = col,
                                  Dist = (pos - _camPos).sqrMagnitude });
        }

        /// <summary>Frames a second the hanging smoke turns over at: its sixty-four come round in five seconds.</summary>
        private const float WispRate = 12f;

        /// <summary>
        /// Dust or dirt thrown up, or smoke bursting: the sheet that goes from a tight ball to a
        /// wide cloud thinning out, <paramref name="through"/> of the way (0 to 1). Upright, a
        /// little turned, every other one left for right; <paramref name="tall"/> times as high
        /// as wide, for a spurt; <paramref name="lean"/> radians off the upright, for a jet.
        /// </summary>
        private void Dust(Vector3 pos, float size, Color col, float seed, float through, float tall = 1f, float lean = 0f)
            => AddPuff(Sheet.Dust, Mathf.Clamp01(through) * 24f, pos, size, size * tall, col, lean + (seed - 0.5f) * 0.6f, (int)(seed * 64f) % 2 == 1,
                       Mathf.Clamp(size * 0.3f, 0.05f, 1.2f));

        /// <summary>Smoke that hangs, turning over on itself as the seconds go: gun smoke, a shell's column, a screen.</summary>
        private void Hang(Vector3 pos, float size, Color col, float seed, float seconds)
            => AddPuff(Sheet.Wisp, (seed * 64f + Mathf.Max(0f, seconds) * WispRate) % 64f, pos, size, size, col, seed * 6.2832f, (int)(seed * 64f) % 2 == 1,
                       Mathf.Clamp(size * 0.3f, 0.05f, 1.5f));

        /// <summary>A clod of earth, or a piece of anything, thrown.</summary>
        private void Clod(Vector3 pos, float size, Color col, float seed)
            => AddPuff(Sheet.Speck, (int)(seed * 4f) % 4, pos, size, size, col, seed * 6.2832f, false, Mathf.Max(0.03f, size * 0.5f));

        /// <summary>A drop, or a soft spot of anything: blood, sand.</summary>
        private void Drop(Vector3 pos, float size, Color col, float seed)
            => AddPuff(Sheet.Speck, 4 + (int)(seed * 2f) % 2, pos, size, size, col, 0f, false, Mathf.Max(0.03f, size * 0.5f));

        private void Upload()
        {
            // Smoke far to near, so each puff blends over what is behind it.
            _puffs.Sort((x, y) => y.Dist.CompareTo(x.Dist));
            _sp.Clear(); _sc.Clear(); _suv.Clear(); _suv2.Clear(); _si.Clear();
            foreach (var p in _puffs)
            {
                float cos = Mathf.Cos(p.Rot), sin = Mathf.Sin(p.Rot);
                var r = (_right * cos + _up * sin) * (p.Wide * 0.5f);
                var u = (-_right * sin + _up * cos) * (p.Tall * 0.5f);
                var sheet = SmokeSheets[(int)p.Sheet];
                int f = Mathf.FloorToInt(p.Frame);
                float into = p.Frame - f;
                Vector4 at = sheet.Rect(f), next = sheet.Rect(f + 1);
                if (p.Flip) { (at.x, at.z) = (at.z, at.x); (next.x, next.z) = (next.z, next.x); }
                int i = _sp.Count;
                _sp.Add(p.Pos - r - u); _sp.Add(p.Pos + r - u); _sp.Add(p.Pos + r + u); _sp.Add(p.Pos - r + u);
                for (int k = 0; k < 4; k++) _sc.Add(p.Col);
                _suv.Add(new Vector4(at.x, at.y, into, 1f)); _suv.Add(new Vector4(at.z, at.y, into, 1f));
                _suv.Add(new Vector4(at.z, at.w, into, 1f)); _suv.Add(new Vector4(at.x, at.w, into, 1f));
                _suv2.Add(new Vector4(next.x, next.y, p.Soft)); _suv2.Add(new Vector4(next.z, next.y, p.Soft));
                _suv2.Add(new Vector4(next.z, next.w, p.Soft)); _suv2.Add(new Vector4(next.x, next.w, p.Soft));
                _si.Add(i); _si.Add(i + 1); _si.Add(i + 2); _si.Add(i); _si.Add(i + 2); _si.Add(i + 3);
            }
            Puffs = _puffs.Count;
            Set(_glowMesh, _gp, _gc, _guv, _guv2, _gi);
            Set(_smokeMesh, _sp, _sc, _suv, _suv2, _si);
        }

        private static void Set(Mesh m, List<Vector3> p, List<Color> c, List<Vector4> uv, List<Vector4> uv2, List<int> idx)
        {
            m.Clear();
            if (p.Count == 0) return;
            m.SetVertices(p);
            m.SetColors(c);
            m.SetUVs(0, uv);
            m.SetUVs(1, uv2);
            m.SetTriangles(idx, 0, false);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 4000f);
        }

        /// <summary>
        /// How far a burst's flash lights the ground and the men about it, metres, and the brightness below
        /// which its light is put out. A light that is on is a light every lit thing within its reach is
        /// shaded by, for as long as it is on: at 45 m and down to nothing, a shell's was most of the
        /// screen for seventeen frames, and those were the barrage's long frames.
        /// </summary>
        public const float FlashReach = 26f, FlashOut = 4f;

        private void Lights()
        {
            _flashes.Sort((a, b) => b.intensity.CompareTo(a.intensity));
            for (int i = 0; i < _lights.Length; i++)
            {
                bool on = i < _flashes.Count && _flashes[i].intensity > FlashOut;
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
