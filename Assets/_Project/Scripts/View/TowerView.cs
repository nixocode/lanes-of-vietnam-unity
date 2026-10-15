using System.Collections.Generic;
using LanesOfVietnam.Sim;
using UnityEngine;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// The watchtower as the Destruction rule leaves it (PLAN §12.51): whole; hit, and darker for every burst it
    /// has taken; a panel of its bracing blown off and down; shuddering at each hit and leaning away from them; and
    /// brought down.
    ///
    /// The owner, 2026-10-12: "Add all visual effects and proper animations. Don't always have it fall the same
    /// way, make it sandboxy." The fall is worked out from the Collapsed event alone (its way and its dice), so the
    /// tower, its dust, its sound and the men on it all fall the same fall, and a match played again falls the same
    /// way. It goes over about the feet of the legs on its falling side as a tall column does, slowly and then fast
    /// (no physics engine: the one equation of a toppling column, stepped from the moment it went), turning about
    /// itself as it goes. Its roof may tear off and fly on, tumbling; its ladder may come away and go its own way.
    /// It strikes the ground at the angle the ground there lets it, bounces once and lies; the sandbags on its deck
    /// spill into a heap. Or it breaks at its first girt (four falls in ten): the stump stands, and all above the
    /// break goes over about it, quicker for being shorter, until its top is in the ground past level; its ladder
    /// is torn away and goes on its own.
    /// </summary>
    public sealed class TowerView : MonoBehaviour
    {
        /// <summary>Drawn whole whatever the rule does to it (the frame-time probe's off=tower).</summary>
        public bool Off;

        private const float G = 9.81f;
        /// <summary>The column's height for the equation of its fall, metres: its legs, deck and cabin.</summary>
        private const float Column = 10.4f;
        /// <summary>Half the width of its foot, and of its legs' heads under the deck, metres (tools/blender/firebase_mesh.py: the legs' feet and heads).</summary>
        public const float FootHalf = 1.45f, HeadHalf = 1.20f;
        /// <summary>The top of its roof and how far its eaves reach out from its middle; the same of the plate its rafters sat on (the same script).</summary>
        private const float RoofTop = 10.6f, RoofHalf = 2.4f, CabinTop = 10.3f, CabinHalf = 1.9f;
        /// <summary>How often a tower brought down breaks at its first girt rather than going over whole.</summary>
        public const float BuckleOdds = 0.4f;
        /// <summary>The angle a tower lying on its side rests at with its roof on, and with its roof gone (the roof's eave reaches past the legs).</summary>
        private const float DownRoofed = 85f * Mathf.Deg2Rad, DownBare = 88.5f * Mathf.Deg2Rad;
        /// <summary>A step of the fall's equation, seconds.</summary>
        private const float Step = 1f / 240f;

        /// <summary>
        /// A tower's fall, from its Collapsed event: the same for every view that draws it.
        /// </summary>
        public readonly struct Fall
        {
            /// <summary>The way it goes over (the world's, level), the point it goes over about (the feet of its legs on that side), and its middle at the foot.</summary>
            public readonly Vector3 Dir, Pivot, Foot;
            /// <summary>
            /// It breaks at its first girt rather than going over whole: the stump stands, and all above the break goes over
            /// about the legs on its falling side there, to lie past level with its top in the ground (or on a truck).
            /// </summary>
            public readonly bool Buckle;
            /// <summary>How high it gives, metres above its foot (0: at its foot), and its middle there: what it goes over about.</summary>
            public readonly float Break;
            public readonly Vector3 Base;
            /// <summary>How far out from the point it goes over about, level, its top comes down (metres): where its dust and its crash are.</summary>
            public readonly float Reach;
            /// <summary>How far it leans as it goes and how hard the burst pushed it (radians, radians a second); the angle it lies at.</summary>
            public readonly float Lean, Kick, Down;
            /// <summary>Degrees it turns about itself by the time it is down.</summary>
            public readonly float Twist;
            /// <summary>Seconds from the event to its striking the ground.</summary>
            public readonly float Hits;
            /// <summary>Its roof tears away: at what angle, and when; how fast it tumbles (degrees a second).</summary>
            public readonly bool RoofOff;
            public readonly float RoofAt, RoofTime, RoofSpin;
            /// <summary>Its ladder may come away (if it is on the side it falls away from): at what angle, when, and the way it goes.</summary>
            public readonly bool LadderOff;
            public readonly float LadderAt, LadderTime, LadderTurn;
            /// <summary>The event's dice, for anything else drawn about it.</summary>
            public readonly int Seed;

            /// <summary>The fall of the tower a dressing built, on its ground (and on the vehicles parked there).</summary>
            public Fall(SimEvent e, WorldDressing w, Ground g) : this(e, w.TowerFoot, g, w.Parked, w.TowerBreaks) { }

            /// <param name="breaks">Its legs are cut at its first girt (WorldDressing.TowerBreaks): it may buckle there.</param>
            public Fall(SimEvent e, Vector3 foot, Ground g, IReadOnlyList<(Vector3 at, Vector3 along, float length, float width, float height)> parked = null,
                        bool breaks = false)
            {
                Seed = e.Rounds;
                float a = (float)(e.Amount ?? 0) * Mathf.Deg2Rad;
                Dir = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));
                Foot = foot;
                Buckle = breaks && Buckles(Seed);
                Break = Buckle ? WorldDressing.TowerBreak : 0f;
                Base = foot + Vector3.up * Break;
                // (Its legs lean in as they rise: at the break they are nearer its middle than at its foot.)
                float half = FootHalf - (FootHalf - HeadHalf) * Break / WorldDressing.TowerDeck;
                float square = 1f / Mathf.Max(Mathf.Abs(Dir.x), Mathf.Abs(Dir.z));
                Pivot = Base + Dir * (half * square);
                _k = 3f * G / (2f * (Column - Break));
                Lean = (2f + 2.5f * H(Seed, 1)) * Mathf.Deg2Rad;
                Kick = 0.12f + 0.45f * H(Seed, 2);
                Twist = (H(Seed, 3) - 0.5f) * 50f;
                RoofOff = H(Seed, 4) < 0.6f;
                RoofAt = (25f + 35f * H(Seed, 5)) * Mathf.Deg2Rad;
                RoofSpin = (H(Seed, 6) < 0.5f ? -1f : 1f) * (140f + 260f * H(Seed, 7));
                // (Broken under its deck, it tears its ladder away as it goes, whichever side the ladder is on.)
                LadderOff = Buckle || H(Seed, 8) < 0.7f;
                LadderAt = Buckle ? 0f : (8f + 22f * H(Seed, 9)) * Mathf.Deg2Rad;
                LadderTurn = (H(Seed, 10) < 0.5f ? -1f : 1f) * (20f + 45f * H(Seed, 11));
                float down, far;
                if (!Buckle)
                {
                    // It lies at the angle the ground where it lands lets it: further over where the ground falls away.
                    down = RoofOff ? DownBare : DownRoofed;
                    Reach = 9.5f;
                    if (g != null)
                    {
                        var land = Pivot + Dir * Reach;
                        float rise = (float)g.HeightAt(land.x, Coords.SimZ(land.z)) - foot.y;
                        down -= Mathf.Atan2(rise, Reach);
                    }
                    far = 11.5f;
                }
                else
                {
                    // Broken, it goes over about the break until the far edge of its top (its roof's eave, or the plate
                    // the rafters sat on) strikes the ground, past level: the ground there, the higher the sooner.
                    float up = (RoofOff ? CabinTop : RoofTop) - Break, wide = ((RoofOff ? CabinHalf : RoofHalf) - half) * square;
                    float r = Mathf.Sqrt(up * up + wide * wide), phi = Mathf.Atan2(wide, up), rise = 0f;
                    down = Mathf.PI * 0.5f;
                    for (int k = 0; k < 2; k++)
                    {
                        down = Mathf.Acos(Mathf.Clamp((rise - Break) / r, -1f, 1f)) - phi;
                        var land = Pivot + Dir * (r * Mathf.Sin(down + phi));
                        if (g != null) rise = (float)g.HeightAt(land.x, Coords.SimZ(land.z)) - foot.y;
                    }
                    Reach = r * Mathf.Sin(down + phi);
                    far = r;
                }
                // And not through a truck: where it comes down across one, it rests on it, its lower side on the roof.
                var across = Vector3.Cross(Vector3.up, Dir);
                if (parked != null)
                    for (float s = 2f; s <= far; s += 0.5f)
                        for (int k = -1; k <= 1; k++)
                        {
                            var p = Pivot + Dir * s + across * (1.8f * k);
                            foreach (var v in parked)
                            {
                                var d = p - v.at;
                                if (Mathf.Abs(Vector3.Dot(d, v.along)) > v.length * 0.5f || Mathf.Abs(Vector3.Dot(d, Vector3.Cross(Vector3.up, v.along))) > v.width * 0.5f) continue;
                                // (Broken, it comes down on a roof below the break: over the level, by as much as the roof is lower.)
                                float rest = Mathf.Atan2(s, Buckle ? v.at.y + v.height - Pivot.y : Mathf.Max(0.1f, v.at.y + v.height - Pivot.y));
                                if (rest < down)
                                {
                                    down = rest;
                                    if (Buckle) Reach = Mathf.Min(Reach, s);
                                }
                            }
                        }
                Down = Mathf.Clamp(down, 50f * Mathf.Deg2Rad, (Buckle ? 125f : 100f) * Mathf.Deg2Rad);
                // When it reaches the angles that matter.
                float th = Lean, w = Kick, t = 0f;
                RoofTime = LadderTime = -1f;
                while (th < Down && t < 8f)
                {
                    if (RoofTime < 0 && th >= RoofAt) RoofTime = t;
                    if (LadderTime < 0 && th >= LadderAt) LadderTime = t;
                    w += _k * Mathf.Sin(th) * Step;
                    th += w * Step;
                    t += Step;
                }
                Hits = t;
                if (RoofTime < 0) RoofTime = t;
                if (LadderTime < 0) LadderTime = t;
            }

            /// <summary>The column's constant: its angle's acceleration is this times the sine of its angle (the shorter the column, the sooner).</summary>
            private readonly float _k;

            /// <summary>Whether a fall's dice break it at its first girt (where its legs can be broken there).</summary>
            public static bool Buckles(int seed) => H(seed, 12) < BuckleOdds;

            /// <summary>Its angle from upright, t seconds after it went, and how fast that angle is growing.</summary>
            public float Angle(float t, out float rate)
            {
                if (t >= Hits)
                {
                    // On the ground: up off it once, a little, and once again less, and still.
                    float u = t - Hits;
                    rate = 0f;
                    if (u < 0.3f) return Down - 0.05f * Mathf.Sin(Mathf.PI * u / 0.3f);
                    if (u < 0.48f) return Down - 0.012f * Mathf.Sin(Mathf.PI * (u - 0.3f) / 0.18f);
                    return Down;
                }
                float th = Lean, w = Kick, s = 0f;
                for (; s + Step <= t; s += Step) { w += _k * Mathf.Sin(th) * Step; th += w * Step; }
                th += w * (t - s);
                rate = w;
                return Mathf.Min(th, Down);
            }

            /// <summary>How far it has turned about itself, degrees, at an angle of its fall.</summary>
            public float TwistAt(float angle) => Twist * Mathf.Pow(Mathf.Clamp01(angle / Down), 1.5f);

            /// <summary>Its rotation at an angle of its fall: over toward its way, turned about itself.</summary>
            public Quaternion Turn(float angle) => Tilt(Dir, angle) * Quaternion.AngleAxis(TwistAt(angle), Vector3.up);

            /// <summary>Where the tower group (or what of it is above the break) is put for that rotation: the point it goes over about stays where it is.</summary>
            public Vector3 Place(Quaternion turn, float angle)
                => Pivot + Tilt(Dir, angle) * (Base - Pivot) - turn * Base;
        }

        /// <summary>
        /// Bring the tower down now, for a capture or the frame-time probe: a shell's burst at its foot from a side
        /// (degrees, the simulation's: 0 is +x, 90 is +z), enough to finish it. Never part of a match the game plays.
        /// Asked to (buckle), its dice are moved on until they break it at its first girt, or until they do not: to
        /// capture or test each kind of fall. True if it fell.
        /// </summary>
        public static bool Bring(SimState st, double from, bool? buckle = null)
        {
            if (!st.Destruction || st.Posts.Count == 0 || st.Posts[0].Fallen) return false;
            var p = st.Posts[0];
            double a = from * System.Math.PI / 180;
            double x = p.X + System.Math.Cos(a) * 2.5, z = p.Z + System.Math.Sin(a) * 2.5;
            st.Events.Add(new SimEvent { Kind = EventKind.Shell, Tick = st.Tick, Side = Side.Vc, Id = -1, X = x, Z = z });
            Destruction.Burst(st, x, z, 0, 5);
            int went = st.Events.FindLastIndex(e => e.Kind == EventKind.Collapsed);
            if (buckle.HasValue && went >= 0)
            {
                var e = st.Events[went];
                while (Fall.Buckles(e.Rounds) != buckle.Value) e.Rounds++;
                st.Events[went] = e;
            }
            return p.Fallen;
        }

        /// <summary>A rotation that takes upright over toward a level way by an angle (radians).</summary>
        public static Quaternion Tilt(Vector3 way, float angle)
            => Quaternion.FromToRotation(Vector3.up, Mathf.Cos(angle) * Vector3.up + Mathf.Sin(angle) * way);

        /// <summary>A stable number in [0, 1) for a fall's dice and a purpose.</summary>
        public static float H(int seed, int salt)
        {
            double h = System.Math.Sin(seed * 0.0001237 + salt * 78.233 + seed % 977 * 12.9898) * 43758.5453;
            return (float)(h - System.Math.Floor(h));
        }

        // --- the pieces -------------------------------------------------------------------

        private enum Kind { Leg, Brace, Deck, Cabin, Roof, Ladder, Bags, Other }

        private sealed class Piece
        {
            public Transform T;
            public Renderer R;
            public Vector3 Origin;          // its place in the tower's group (the group stands at the world's origin)
            public Bounds B;                // its mesh's bounds, in its own space
            public Kind Kind = Kind.Other;
            public int Face = -1, Bay = -1; // a panel of bracing (its face and bay), or a side of the cabin (its face)
            public int Section = -1;        // a timber of a leg (its section from the ground up), or half the ladder
        }

        /// <summary>
        /// How a piece comes down when the tower does (worked out once, from the Collapsed event, when it is read):
        /// carried by the tower's fall until it comes away, then thrown, turning, until it strikes the ground (or a
        /// truck's roof), then a hop and a slide and it lies flat. A tall piece left standing on the ground (a leg of
        /// the stump, the ladder's foot) goes over about its foot instead.
        /// </summary>
        private struct Flight
        {
            public bool Stays, Topples;
            public float From;                  // seconds after the fall began that it comes away
            public Vector3 C0, V0;              // its middle as it comes away, and how fast it is going
            public Quaternion R0;
            public Vector3 Axis; public float Spin;   // turning about this, degrees a second
            public float Land;                  // seconds after it came away that it strikes
            public Vector3 CLand, Rest;         // its middle as it strikes and as it lies
            public Quaternion RLand, Lie;
            public float Hop;
            public Vector3 Foot, Way; public float Over;   // going over about its foot: which way, and how far it goes (radians)
        }

        private GameRoot _root;
        private WorldDressing _dressing;
        private Transform _group;
        private Vector3 _foot;
        private readonly List<Piece> _pieces = new List<Piece>();
        private Flight[] _flights = new Flight[0];
        private MaterialPropertyBlock _block;
        private Color _base = Color.white;
        private float _tinted = -1f;

        // What the log has said about the tower (MatchOptions.Destruction), read as it comes.
        private SimState _of;
        private int _cursor;
        private readonly List<(int piece, int tick)> _gone = new List<(int, int)>();
        private Vector3 _leanWay, _leanWas, _hitWay;
        private float _leanBy, _leanByWas;
        private int _leanTick = -1, _hitTick = -1;
        private int _fallEvent = -1;
        private Fall _fall;
        private bool _shook;
        private readonly List<(float at, Vector3 where, float size)> _landings = new List<(float, Vector3, float)>();

        /// <summary>The fall being drawn, if the tower is down or going (for the tests and the capture).</summary>
        public bool Falling => _fallEvent >= 0;
        public Fall Current => _fall;
        /// <summary>The tower's group (null if the dressing built it as boxes).</summary>
        public Transform Group => _group;
        /// <summary>How it stands now: the rotation of what of it is leaning or going over (all of it, or all above the break).</summary>
        public Quaternion Over { get; private set; } = Quaternion.identity;
        /// <summary>
        /// Where and when each piece of it strikes the ground as it comes down (seconds after the fall began), and how
        /// big it is: for the dust it kicks up (CombatView). Empty until it falls.
        /// </summary>
        public IReadOnlyList<(float at, Vector3 where, float size)> Landings => _landings;
        /// <summary>The pieces it is drawn in (for the tests).</summary>
        public int Pieces => _pieces.Count;

        private bool Find()
        {
            _root ??= GameRoot.Instance;
            if (_root == null || _root.Dressing == null) return false;
            // (Read again when any piece has gone: a world built in this frame has pieces still to be destroyed in it,
            // its members whole before they were cut into their pieces.)
            if (_group != null && _dressing == _root.Dressing && _dressing.Tower == _group && !_pieces.Exists(p => p.T == null)) return true;
            _dressing = _root.Dressing;
            _group = _dressing.Tower;
            _pieces.Clear(); _tinted = -1f; _of = null;
            if (_group == null) return false;
            _foot = _dressing.TowerFoot;
            var names = new List<string>();
            foreach (Transform c in _group) names.Add(c.name);
            foreach (Transform c in _group)
            {
                var mf = c.GetComponent<MeshFilter>();
                var r = c.GetComponent<Renderer>();
                if (mf == null || mf.sharedMesh == null || r == null) continue;
                // (A member whole is gone at the end of the frame it was cut into its pieces.)
                if (names.Exists(n => n.Length > c.name.Length && n.StartsWith(c.name + " "))) continue;
                var p = new Piece { T = c, R = r, Origin = c.localPosition, B = mf.sharedMesh.bounds };
                var parts = c.name.Split(' ');
                int Part(int i) => parts.Length > i && int.TryParse(parts[i], out int v) ? v : -1;
                switch (parts[0])
                {
                    case "tower_legs": p.Kind = Kind.Leg; p.Section = Part(1); break;
                    case "tower_braces": p.Kind = Kind.Brace; p.Face = Part(1); p.Bay = Part(2); break;
                    case "tower_deck": p.Kind = Kind.Deck; break;
                    case "tower_cabin": p.Kind = Kind.Cabin; p.Face = Part(1); break;
                    case "tower_roof": p.Kind = Kind.Roof; break;
                    case "tower_ladder": p.Kind = Kind.Ladder; p.Section = Part(1); break;
                    case "tower": if (parts.Length > 1 && parts[1] == "bags") p.Kind = Kind.Bags; break;
                }
                _pieces.Add(p);
            }
            if (_pieces.Count > 0 && _pieces[0].R.sharedMaterial != null && _pieces[0].R.sharedMaterial.HasProperty("_BaseColor"))
                _base = _pieces[0].R.sharedMaterial.GetColor("_BaseColor");
            return true;
        }

        private void LateUpdate()
        {
            if (!Find()) return;
            var d = _root.Driver;
            if (d == null) return;
            var st = d.State;
            float now = (float)((st.Tick - 1 + d.Alpha) * Tune.Dt);
            Read(st);
            bool drawn = !Off && st.Destruction;
            // Whole: every piece where it was built.
            _group.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Over = Quaternion.identity;
            foreach (var p in _pieces)
            {
                p.T.SetLocalPositionAndRotation(p.Origin, Quaternion.identity);
                p.T.localScale = Vector3.one;
            }
            if (!drawn) { Tint(1f); return; }
            var post = st.Posts.Count > 0 ? st.Posts[0] : null;
            Tint(post == null ? 1f : (float)System.Math.Max(0, post.Strength));

            if (_fallEvent < 0) Standing(now);
            else Down(st, now);
            // A panel of bracing blown off falls on its own, out from its face and down.
            foreach (var (k, tick) in _gone) Panel(_pieces[k], now - (float)(tick * Tune.Dt));
        }

        /// <summary>Darker for the bursts it has taken: whole, as built; at nothing, half as bright and browner.</summary>
        private void Tint(float strength)
        {
            float k = Mathf.Round(strength * 50f) / 50f;
            if (Mathf.Approximately(k, _tinted)) return;
            _tinted = k;
            _block ??= new MaterialPropertyBlock();
            var c = _base * Color.Lerp(new Color(0.48f, 0.43f, 0.38f), Color.white, k);
            c.a = _base.a;
            foreach (var p in _pieces)
            {
                if (k >= 1f) { p.R.SetPropertyBlock(null); continue; }
                p.R.GetPropertyBlock(_block);
                _block.SetColor("_BaseColor", c);
                p.R.SetPropertyBlock(_block);
            }
        }

        /// <summary>Read the log for the tower's damage and its fall, from where it was read to.</summary>
        private void Read(SimState st)
        {
            if (!ReferenceEquals(st, _of) || _cursor > st.Events.Count)
            {
                _of = st; _cursor = 0;
                _gone.Clear(); _landings.Clear(); _leanBy = _leanByWas = 0f; _leanTick = _hitTick = -1; _fallEvent = -1; _shook = false;
            }
            for (; _cursor < st.Events.Count; _cursor++)
            {
                var e = st.Events[_cursor];
                if (e.Id != 0 || e.X == null) continue;
                if (e.Kind == EventKind.Damaged) Damaged(e);
                else if (e.Kind == EventKind.Collapsed) { _fallEvent = _cursor; _fall = new Fall(e, _dressing, _root.Ground); _shook = false; Plan(); }
            }
        }

        private void Damaged(SimEvent e)
        {
            float left = (float)(e.Amount ?? 1);
            var hit = Coords.World(e.X.Value, e.Z.Value, _foot.y);
            var away = _foot - hit; away.y = 0f;
            float far = away.magnitude;
            away = far > 0.2f ? away / far : Vector3.right;
            _hitTick = e.Tick;
            _hitWay = away;
            // It leans once it is badly hurt, away from the burst, more as it weakens (from the lean it had: eased
            // from where that one had got to).
            if (left < 0.45f)
            {
                if (_leanTick >= 0)
                {
                    float k = Mathf.SmoothStep(0f, 1f, (float)((e.Tick - _leanTick) * Tune.Dt) / LeanTime);
                    _leanWas = Vector3.Slerp(_leanByWas > 0 ? _leanWas : _leanWay, _leanWay, k).normalized;
                    _leanByWas = Mathf.Lerp(_leanByWas, _leanBy, k);
                }
                _leanWay = away;
                _leanBy = (1.5f + 5f * (0.45f - left) / 0.45f) * Mathf.Deg2Rad;
                _leanTick = e.Tick;
            }
            // A panel of its bracing goes as it falls past two thirds and past a third of its strength: on the face
            // toward the burst, low for a burst on the ground near it, high for one up at its platform.
            int panels = (left < 0.65f ? 1 : 0) + (left < 0.35f ? 1 : 0);
            while (_gone.Count < panels && _gone.Count < 2)
            {
                int bay = far < Tune.TowerHitRadius ? 0 : WorldDressing.TowerBays - 1;
                int pick = -1;
                float best = float.MinValue;
                for (int k = 0; k < _pieces.Count; k++)
                {
                    var p = _pieces[k];
                    if (p.Kind != Kind.Brace || _gone.Exists(g => g.piece == k)) continue;
                    var n = Normal(p.Face);
                    float score = -Vector3.Dot(n, away) * 4f - Mathf.Abs(p.Bay - bay);
                    if (score > best) { best = score; pick = k; }
                }
                if (pick < 0) break;
                _gone.Add((pick, e.Tick));
            }
        }

        /// <summary>A face's outward normal: faces 0 to 3 are +x, +z, -x, -z.</summary>
        private static Vector3 Normal(int face) => face == 0 ? Vector3.right : face == 1 ? Vector3.forward : face == 2 ? Vector3.left : Vector3.back;

        /// <summary>Seconds a new lean takes to settle.</summary>
        private const float LeanTime = 1.0f;

        /// <summary>The lean it has at a time (its way, and radians), shudder and all.</summary>
        private Vector3 Leaning(float now)
        {
            var lean = Vector3.zero;
            if (_leanTick >= 0)
            {
                float k = Mathf.SmoothStep(0f, 1f, (now - (float)(_leanTick * Tune.Dt)) / LeanTime);
                var way = _leanByWas > 0 ? Vector3.Slerp(_leanWas, _leanWay, k).normalized : _leanWay;
                lean = way * Mathf.Lerp(_leanByWas, _leanBy, k);
            }
            if (_hitTick >= 0)
            {
                // (A shudder at every hit: a degree, gone in a second.)
                float age = now - (float)(_hitTick * Tune.Dt);
                if (age >= 0 && age < 1.2f) lean += _hitWay * (0.016f * Mathf.Sin(age * 22f) * Mathf.Exp(-age * 4f));
            }
            return lean;
        }

        /// <summary>The tower's group leaning by a lean (a way times radians), over the feet of its legs on that side.</summary>
        private (Vector3 at, Quaternion turn) Leant(Vector3 lean)
        {
            float by = lean.magnitude;
            if (by < 1e-5f) return (Vector3.zero, Quaternion.identity);
            var way = lean / by;
            var pivot = _foot + way * (FootHalf / Mathf.Max(Mathf.Abs(way.x), Mathf.Abs(way.z)));
            var turn = Tilt(way, by);
            return (pivot - turn * pivot, turn);
        }

        /// <summary>Standing: leaning as it has been hurt, and shuddering at each hit.</summary>
        private void Standing(float now)
        {
            var (at, turn) = Leant(Leaning(now));
            _group.SetPositionAndRotation(at, turn);
            Over = turn;
        }

        /// <summary>A piece of the stump a tower that buckles leaves standing: its legs and bracing below its first girt.</summary>
        private static bool Stump(Piece p) => p.Kind == Kind.Leg && p.Section == 0 || p.Kind == Kind.Brace && p.Bay == 0;

        /// <summary>Where the fall carries what goes over with it, t seconds after it began (its group's place and turn).</summary>
        private (Vector3 at, Quaternion turn) Carrier(float t)
        {
            float th = _fall.Angle(t, out _);
            var turn = _fall.Turn(th);
            return (_fall.Place(turn, th), turn);
        }

        /// <summary>The ground under a place, or the roof of a truck parked there: what a piece of the tower comes to rest on.</summary>
        private float GroundUnder(Vector3 at)
        {
            float y = _root.Ground != null ? (float)_root.Ground.HeightAt(at.x, Coords.SimZ(at.z)) : _foot.y;
            foreach (var v in _dressing.Parked)
            {
                var d = at - v.at;
                if (Mathf.Abs(Vector3.Dot(d, v.along)) > v.length * 0.5f || Mathf.Abs(Vector3.Dot(d, Vector3.Cross(Vector3.up, v.along))) > v.width * 0.5f) continue;
                y = Mathf.Max(y, v.at.y + v.height);
            }
            return y;
        }

        /// <summary>How far below its middle a piece reaches, turned so (a negative number: its lowest corner).</summary>
        private static float Low(Piece p, Quaternion r)
        {
            var e = p.B.extents;
            float low = 0f;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                low = Mathf.Min(low, (r * c).y);
            }
            return low;
        }

        /// <summary>
        /// How every piece comes down, from the fall's dice (the owner, 2026-10-15: "i want it to crumble and have dust
        /// come up, it just kinda tilts"). Its bracing pops off as it goes over and turns; its roof's sheets tear away if
        /// they are to; its cabin's sides and its legs' upper timbers snap before it strikes; and what is still whole
        /// when it strikes bursts apart there, its pieces thrown on, up and out and tumbling. A tower that broke at its
        /// first girt leaves its stump, of which a leg may yet go over.
        /// </summary>
        private void Plan()
        {
            var f = _fall;
            float T = f.Hits;
            _flights = new Flight[_pieces.Count];
            _landings.Clear();
            var across = Vector3.Cross(Vector3.up, f.Dir);
            for (int k = 0; k < _pieces.Count; k++)
            {
                var p = _pieces[k];
                var fl = new Flight();
                int salt = 400 + 13 * k;
                float Die(int i) => H(f.Seed, salt + i);
                var mid = p.B.center;
                if (_gone.Exists(g => g.piece == k)) { fl.Stays = true; _flights[k] = fl; continue; }
                if (f.Buckle && Stump(p))
                {
                    // The stump stands; a leg of it, cut through at the girt and hit, may go over after.
                    fl.Stays = true;
                    if (p.Kind == Kind.Leg && Die(0) < 0.35f)
                    {
                        fl.Stays = false;
                        Topple(ref fl, p, T + 0.1f + 0.7f * Die(1), Quaternion.identity, Vector3.zero,
                               Quaternion.AngleAxis((Die(2) - 0.5f) * 140f, Vector3.up) * f.Dir);
                    }
                    _flights[k] = fl;
                    continue;
                }
                float from;
                var kick = Vector3.zero;
                // (The way out from the tower's middle, of a piece on one side of it, as the fall has turned it.)
                var outward = new Vector3(mid.x, 0f, mid.z);
                outward = outward.sqrMagnitude > 1e-3f ? outward.normalized : across;
                switch (p.Kind)
                {
                    case Kind.Brace: from = T * (0.3f + 0.6f * Die(0)); kick = (1.2f + 2.2f * Die(1)) * Normal(p.Face) + 0.6f * Vector3.up; break;
                    case Kind.Roof:
                        from = f.RoofOff ? f.RoofTime + 0.15f * Die(0) : T;
                        kick = f.RoofOff ? f.Dir * 1.5f + Vector3.up * 2.2f + across * ((Die(1) - 0.5f) * 2.5f) : Vector3.zero;
                        break;
                    case Kind.Cabin: from = T * (0.6f + 0.45f * Die(0)); kick = (1f + 1.5f * Die(1)) * Normal(p.Face) + 0.5f * Vector3.up; break;
                    case Kind.Leg: from = p.Section >= 2 ? T * (0.55f + 0.5f * Die(0)) : p.Section == 1 ? T * (0.75f + 0.3f * Die(0)) : T; kick = outward * (0.6f + 1.2f * Die(1)); break;
                    case Kind.Ladder: from = f.LadderOff ? f.LadderTime + 0.25f * Mathf.Max(0, p.Section) : T; kick = outward * (0.5f + Die(1)); break;
                    case Kind.Bags: from = T * (0.8f + 0.25f * Die(0)); kick = f.Dir * (0.5f + Die(1)) + Vector3.up * 0.5f; break;
                    default: from = T; break;
                }
                from = Mathf.Min(from, T);
                bool strikes = from >= T - 1e-3f;
                Vector3 C(float t)
                {
                    var (a, r) = Carrier(t);
                    return a + r * (p.Origin + mid);
                }
                var (cat, cturn) = Carrier(from);
                var c0 = cat + cturn * (p.Origin + mid);
                // (Its kick, out from its own side, turned as the tower has turned.)
                kick = cturn * new Vector3(kick.x, 0f, kick.z) + Vector3.up * kick.y;
                var r0 = cturn;
                // Standing on the ground as it comes away (the ladder's foot, a tower broken under its deck): it goes over.
                if (p.B.size.y > 1.5f && c0.y + Low(p, r0) < GroundUnder(c0) + 0.2f && Vector3.Angle(r0 * Vector3.up, Vector3.up) < 30f)
                {
                    Topple(ref fl, p, from, r0, cat, (Quaternion.AngleAxis((Die(2) - 0.5f) * 90f, Vector3.up) * (outward + 0.5f * f.Dir)).normalized);
                    _flights[k] = fl;
                    continue;
                }
                Vector3 v;
                if (strikes)
                {
                    // Bursting apart as it strikes: on the way it was going, a little, and thrown up and out.
                    v = (C(T) - C(T - 0.04f)) / 0.04f;
                    var level = new Vector3(v.x, 0f, v.z);
                    float az = Die(3) * Mathf.PI * 2f;
                    float heavy = p.Kind == Kind.Deck || p.Kind == Kind.Bags ? 0.55f : 1f;
                    v = level * 0.3f + f.Dir * (0.5f + 1.5f * Die(4)) + new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az)) * (1.2f + 2.8f * Die(5)) * heavy
                        + Vector3.up * (1f + 3f * Die(6)) * heavy;
                    // (Out of the ground, if the fall had it in it.)
                    c0.y = Mathf.Max(c0.y, GroundUnder(c0) - Low(p, r0) + 0.02f);
                }
                else v = (C(from + 0.02f) - C(Mathf.Max(0f, from - 0.02f))) / (from >= 0.02f ? 0.04f : 0.02f) + kick;
                fl.From = from;
                fl.C0 = c0; fl.V0 = v; fl.R0 = r0;
                fl.Axis = new Vector3(Die(7) - 0.5f, (Die(8) - 0.5f) * 0.6f, Die(9) - 0.5f).normalized;
                fl.Spin = (Die(10) < 0.5f ? -1f : 1f) * (p.Kind == Kind.Bags || p.Kind == Kind.Deck ? 60f + 120f * Die(11) : 150f + 350f * Die(11));
                // When it strikes: its lowest corner down to the ground under it.
                float u = 0.03f;
                for (; u < 6f; u += 1f / 90f)
                {
                    var c = c0 + v * u - 0.5f * G * u * u * Vector3.up;
                    var r = Quaternion.AngleAxis(fl.Spin * u, fl.Axis) * r0;
                    if (c.y + Low(p, r) <= GroundUnder(c)) break;
                }
                fl.Land = u;
                fl.CLand = c0 + v * u - 0.5f * G * u * u * Vector3.up;
                fl.RLand = Quaternion.AngleAxis(fl.Spin * u, fl.Axis) * r0;
                // It lies flat: its thinnest way upright, the way up it is nearer to; on whatever is under it.
                var e = p.B.extents;
                var thin = e.x <= e.y && e.x <= e.z ? Vector3.right : e.y <= e.z ? Vector3.up : Vector3.forward;
                float half = Mathf.Min(e.x, Mathf.Min(e.y, e.z));
                var w = fl.RLand * thin;
                fl.Lie = Quaternion.FromToRotation(w, w.y >= 0 ? Vector3.up : Vector3.down) * fl.RLand;
                var vl = v - G * u * Vector3.up;
                fl.Rest = fl.CLand + new Vector3(vl.x, 0f, vl.z) * 0.12f;
                fl.Rest.y = GroundUnder(fl.Rest) + half + 0.01f;
                fl.Hop = Mathf.Min(0.45f, 0.06f * Mathf.Abs(vl.y));
                _flights[k] = fl;
                _landings.Add((from + u, fl.CLand, p.B.size.magnitude));
            }
            _landings.Sort((a, b) => a.at.CompareTo(b.at));
            Heap();
        }

        /// <summary>
        /// What comes down lies in a heap, not each piece flat on the ground: in the order they strike, a piece that
        /// comes to rest over one already lying there rests on it, tilted as one timber lies across another (the
        /// pieces do not meet as they fly: each is its own throw).
        /// </summary>
        private void Heap()
        {
            var order = new List<int>();
            for (int k = 0; k < _flights.Length; k++) if (!_flights[k].Stays && !_flights[k].Topples) order.Add(k);
            order.Sort((a, b) => (_flights[a].From + _flights[a].Land).CompareTo(_flights[b].From + _flights[b].Land));
            var lying = new List<(Vector3 at, float reach, float top)>();
            foreach (int k in order)
            {
                var p = _pieces[k];
                var fl = _flights[k];
                var e = p.B.extents;
                float half = Mathf.Min(e.x, Mathf.Min(e.y, e.z)), reach = 0.4f * Mathf.Max(e.x, Mathf.Max(e.y, e.z)) + 0.15f;
                float y = fl.Rest.y;
                bool across = false;
                foreach (var (at, r, top) in lying)
                {
                    var d = fl.Rest - at; d.y = 0f;
                    if (d.magnitude < (reach + r) * 0.6f && top + half > y) { y = top + half; across = true; }
                }
                fl.Rest.y = y;
                // (Across another, it lies tilted; on the ground, near enough flat.)
                float tilt = (across ? 6f + 14f * H(_fall.Seed, 700 + k) : 3f * H(_fall.Seed, 700 + k)) * (H(_fall.Seed, 710 + k) < 0.5f ? -1f : 1f);
                var axis = new Vector3(H(_fall.Seed, 720 + k) - 0.5f, 0f, H(_fall.Seed, 730 + k) - 0.5f);
                if (axis.sqrMagnitude > 1e-4f) fl.Lie = Quaternion.AngleAxis(tilt, axis.normalized) * fl.Lie;
                _flights[k] = fl;
                lying.Add((fl.Rest, reach, y + half));
            }
        }

        /// <summary>A piece standing on the ground that goes over about its foot: from when, from how it stood, which way.</summary>
        private void Topple(ref Flight fl, Piece p, float from, Quaternion stood, Vector3 at, Vector3 way)
        {
            fl.Topples = true;
            fl.From = from;
            fl.R0 = stood;
            var footLocal = new Vector3(p.B.center.x, p.B.min.y, p.B.center.z);
            fl.Foot = at + stood * (p.Origin + footLocal);
            fl.Way = new Vector3(way.x, 0f, way.z).normalized;
            fl.C0 = footLocal;
            // As far as it goes before it lies on the ground: lying, a little short of flat, on its own thickness.
            fl.Over = 86f * Mathf.Deg2Rad;
            float takes = Mathf.Sqrt(fl.Over / 1.1f);
            _landings.Add((from + takes, fl.Foot + fl.Way * p.B.size.y * 0.5f, p.B.size.magnitude));
        }

        /// <summary>Going over, and down: everything that goes with it carried by its fall, and each piece once it is away.</summary>
        private void Down(SimState st, float now)
        {
            var f = _fall;
            float t = now - (float)(st.Events[_fallEvent].Tick * Tune.Dt);
            if (t < 0) return;
            var (at, turn) = Carrier(t);
            var stand = Quaternion.identity;
            var standAt = Vector3.zero;
            // (From the lean it had as it went, over the first moments of going: its lean's way becomes its fall's, and
            // a stump left standing stands up straight as the weight above it goes.)
            if (t < 0.5f)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / 0.5f);
                var (wasAt, was) = Leant(Leaning(now - t));
                turn = Quaternion.Slerp(was, turn, k);
                at = Vector3.Lerp(wasAt, at, k);
                stand = Quaternion.Slerp(was, stand, k);
                standAt = Vector3.Lerp(wasAt, standAt, k);
            }
            Over = turn;
            for (int k = 0; k < _pieces.Count && k < _flights.Length; k++)
            {
                var p = _pieces[k];
                var fl = _flights[k];
                bool stump = f.Buckle && Stump(p);
                if (fl.Stays || t < fl.From)
                {
                    if (stump) p.T.SetLocalPositionAndRotation(standAt + stand * p.Origin, stand);
                    else p.T.SetLocalPositionAndRotation(at + turn * p.Origin, turn);
                    continue;
                }
                float u = t - fl.From;
                var mid = p.B.center;
                if (fl.Topples)
                {
                    // Slowly and then fast, as a column does; down, a little bounce, still.
                    float th = Mathf.Min(fl.Over, 1.1f * u * u + 0.05f);
                    float takes = Mathf.Sqrt(fl.Over / 1.1f);
                    if (u > takes && u < takes + 0.25f) th -= 0.04f * Mathf.Sin(Mathf.PI * (u - takes) / 0.25f);
                    var r = Tilt(fl.Way, th) * fl.R0;
                    var foot = fl.Foot + fl.Way * (0.25f * Mathf.Min(1f, u / takes));
                    p.T.SetPositionAndRotation(foot - r * fl.C0, r);
                    continue;
                }
                Vector3 c;
                Quaternion rot;
                if (u < fl.Land)
                {
                    c = fl.C0 + fl.V0 * u - 0.5f * G * u * u * Vector3.up;
                    rot = Quaternion.AngleAxis(fl.Spin * u, fl.Axis) * fl.R0;
                }
                else
                {
                    // Down: a hop, a slide, and it settles flat.
                    float s = Mathf.Clamp01((u - fl.Land) / 0.4f), e = Mathf.SmoothStep(0f, 1f, s);
                    c = Vector3.Lerp(fl.CLand, fl.Rest, e) + Vector3.up * (fl.Hop * Mathf.Sin(Mathf.PI * s));
                    rot = Quaternion.Slerp(fl.RLand, fl.Lie, e);
                }
                p.T.SetPositionAndRotation(c - rot * mid, rot);
            }
            // It strikes the ground: felt, if it is near.
            if (!_shook && t >= f.Hits)
            {
                _shook = true;
                if (t - f.Hits < 0.3f && _root.CameraRig != null)
                    _root.CameraRig.Shake(0.9f * Mathf.Clamp01(1.2f - Mathf.Abs(_foot.x - _root.CameraRig.X) / 70f));
            }
        }

        private float Ground(Vector3 at) => _root.Ground != null ? (float)_root.Ground.HeightAt(at.x, Coords.SimZ(at.z)) : _foot.y;

        /// <summary>
        /// A panel of bracing blown off: it drops and turns out from its face about its foot, and lies.
        /// </summary>
        private void Panel(Piece p, float u)
        {
            if (u < 0) return;
            var n = Normal(p.Face);
            var footLocal = new Vector3(p.B.center.x, p.B.min.y, p.B.center.z);
            var foot0 = _foot + footLocal;
            float drop = Mathf.Max(0f, foot0.y - (Ground(foot0 + n * 0.6f) + 0.06f));
            var footAt = foot0 + n * (0.6f * Mathf.Min(1f, u / 0.6f));
            footAt.y = foot0.y - Mathf.Min(drop, 0.5f * G * u * u);
            float th = Mathf.Min(86f * Mathf.Deg2Rad, 1.1f * u * u + 0.3f * u);
            var r = Tilt(n, th);
            p.T.SetPositionAndRotation(footAt - r * footLocal, r);
        }
    }
}
