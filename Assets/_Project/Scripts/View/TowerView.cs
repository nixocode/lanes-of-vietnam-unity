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
    /// spill into a heap.
    /// </summary>
    public sealed class TowerView : MonoBehaviour
    {
        /// <summary>Drawn whole whatever the rule does to it (the frame-time probe's off=tower).</summary>
        public bool Off;

        private const float G = 9.81f;
        /// <summary>The column's height for the equation of its fall, metres: its legs, deck and cabin.</summary>
        private const float Column = 10.4f;
        /// <summary>Half the width of its foot, metres (tools/blender/firebase_mesh.py: the legs' feet).</summary>
        public const float FootHalf = 1.45f;
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
            public Fall(SimEvent e, WorldDressing w, Ground g) : this(e, w.TowerFoot, g, w.Parked) { }

            public Fall(SimEvent e, Vector3 foot, Ground g, IReadOnlyList<(Vector3 at, Vector3 along, float length, float width, float height)> parked = null)
            {
                Seed = e.Rounds;
                float a = (float)(e.Amount ?? 0) * Mathf.Deg2Rad;
                Dir = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));
                Foot = foot;
                float reach = FootHalf / Mathf.Max(Mathf.Abs(Dir.x), Mathf.Abs(Dir.z));
                Pivot = foot + Dir * reach;
                Lean = (2f + 2.5f * H(Seed, 1)) * Mathf.Deg2Rad;
                Kick = 0.12f + 0.45f * H(Seed, 2);
                Twist = (H(Seed, 3) - 0.5f) * 50f;
                RoofOff = H(Seed, 4) < 0.6f;
                RoofAt = (25f + 35f * H(Seed, 5)) * Mathf.Deg2Rad;
                RoofSpin = (H(Seed, 6) < 0.5f ? -1f : 1f) * (140f + 260f * H(Seed, 7));
                LadderOff = H(Seed, 8) < 0.7f;
                LadderAt = (8f + 22f * H(Seed, 9)) * Mathf.Deg2Rad;
                LadderTurn = (H(Seed, 10) < 0.5f ? -1f : 1f) * (20f + 45f * H(Seed, 11));
                // It lies at the angle the ground where it lands lets it: further over where the ground falls away.
                float down = RoofOff ? DownBare : DownRoofed;
                if (g != null)
                {
                    var land = Pivot + Dir * 9.5f;
                    float rise = (float)g.HeightAt(land.x, Coords.SimZ(land.z)) - foot.y;
                    down -= Mathf.Atan2(rise, 9.5f);
                }
                // And not through a truck: where it comes down across one, it rests on it, its lower side on the roof.
                var across = Vector3.Cross(Vector3.up, Dir);
                if (parked != null)
                    for (float s = 2f; s <= 11.5f; s += 0.5f)
                        for (int k = -1; k <= 1; k++)
                        {
                            var p = Pivot + Dir * s + across * (1.8f * k);
                            foreach (var v in parked)
                            {
                                var d = p - v.at;
                                if (Mathf.Abs(Vector3.Dot(d, v.along)) > v.length * 0.5f || Mathf.Abs(Vector3.Dot(d, Vector3.Cross(Vector3.up, v.along))) > v.width * 0.5f) continue;
                                float rest = Mathf.Atan2(s, Mathf.Max(0.1f, v.at.y + v.height - Pivot.y));
                                if (rest < down) down = rest;
                            }
                        }
                Down = Mathf.Clamp(down, 50f * Mathf.Deg2Rad, 100f * Mathf.Deg2Rad);
                // When it reaches the angles that matter.
                float th = Lean, w = Kick, t = 0f;
                RoofTime = LadderTime = -1f;
                while (th < Down && t < 8f)
                {
                    if (RoofTime < 0 && th >= RoofAt) RoofTime = t;
                    if (LadderTime < 0 && th >= LadderAt) LadderTime = t;
                    w += K * Mathf.Sin(th) * Step;
                    th += w * Step;
                    t += Step;
                }
                Hits = t;
                if (RoofTime < 0) RoofTime = t;
                if (LadderTime < 0) LadderTime = t;
            }

            /// <summary>The column's constant: its angle's acceleration is this times the sine of its angle.</summary>
            private const float K = 3f * G / (2f * Column);

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
                for (; s + Step <= t; s += Step) { w += K * Mathf.Sin(th) * Step; th += w * Step; }
                th += w * (t - s);
                rate = w;
                return Mathf.Min(th, Down);
            }

            /// <summary>How far it has turned about itself, degrees, at an angle of its fall.</summary>
            public float TwistAt(float angle) => Twist * Mathf.Pow(Mathf.Clamp01(angle / Down), 1.5f);

            /// <summary>Its rotation at an angle of its fall: over toward its way, turned about itself.</summary>
            public Quaternion Turn(float angle) => Tilt(Dir, angle) * Quaternion.AngleAxis(TwistAt(angle), Vector3.up);

            /// <summary>Where the tower group is put for that rotation: its foot's pivot stays where it is.</summary>
            public Vector3 Place(Quaternion turn, float angle)
                => Pivot + Tilt(Dir, angle) * (Foot - Pivot) - turn * Foot;
        }

        /// <summary>
        /// Bring the tower down now, for a capture or the frame-time probe: a shell's burst at its foot from a side
        /// (degrees, the simulation's: 0 is +x, 90 is +z), enough to finish it. Never part of a match the game plays.
        /// True if it fell.
        /// </summary>
        public static bool Bring(SimState st, double from)
        {
            if (!st.Destruction || st.Posts.Count == 0 || st.Posts[0].Fallen) return false;
            var p = st.Posts[0];
            double a = from * System.Math.PI / 180;
            double x = p.X + System.Math.Cos(a) * 2.5, z = p.Z + System.Math.Sin(a) * 2.5;
            st.Events.Add(new SimEvent { Kind = EventKind.Shell, Tick = st.Tick, Side = Side.Vc, Id = -1, X = x, Z = z });
            Destruction.Burst(st, x, z, 0, 5);
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

        private sealed class Piece
        {
            public Transform T;
            public Renderer R;
            public Vector3 Origin;          // its place in the tower's group (the group stands at the world's origin)
            public Bounds B;                // its mesh's bounds, in its own space
            public int Face = -1, Bay = -1; // a panel of bracing
        }

        private GameRoot _root;
        private WorldDressing _dressing;
        private Transform _group;
        private Vector3 _foot;
        private readonly List<Piece> _pieces = new List<Piece>();
        private Piece _roof, _ladder;
        private readonly List<Piece> _bags = new List<Piece>();
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
        private float _roofLands = -1f;
        /// <summary>The panels of bracing that break away as it strikes the ground: from its upper face and its sides.</summary>
        private readonly List<int> _broken = new List<int>();
        private int _fallEvent = -1;
        private Fall _fall;
        private bool _shook;

        /// <summary>The fall being drawn, if the tower is down or going (for the tests and the capture).</summary>
        public bool Falling => _fallEvent >= 0;
        public Fall Current => _fall;
        /// <summary>The tower's group (null if the dressing built it as boxes).</summary>
        public Transform Group => _group;

        private bool Find()
        {
            _root ??= GameRoot.Instance;
            if (_root == null || _root.Dressing == null) return false;
            // (Read again when any piece has gone: a world built in this frame has pieces still to be destroyed in it,
            // the bracing whole and the sandbags before they were merged.)
            if (_group != null && _dressing == _root.Dressing && _dressing.Tower == _group && !_pieces.Exists(p => p.T == null)) return true;
            _dressing = _root.Dressing;
            _group = _dressing.Tower;
            _pieces.Clear(); _bags.Clear(); _roof = _ladder = null; _tinted = -1f; _of = null;
            if (_group == null) return false;
            _foot = _dressing.TowerFoot;
            foreach (Transform c in _group)
            {
                var mf = c.GetComponent<MeshFilter>();
                var r = c.GetComponent<Renderer>();
                // (The bracing whole is gone at the end of the frame it was cut into its panels.)
                if (mf == null || mf.sharedMesh == null || r == null || c.name == "tower_braces") continue;
                var p = new Piece { T = c, R = r, Origin = c.localPosition, B = mf.sharedMesh.bounds };
                if (c.name.StartsWith("tower_braces "))
                {
                    var parts = c.name.Split(' ');
                    p.Face = int.Parse(parts[1]);
                    p.Bay = int.Parse(parts[2]);
                }
                if (c.name == "tower_roof") _roof = p;
                if (c.name == "tower_ladder") _ladder = p;
                if (c.name.StartsWith("tower bags")) _bags.Add(p);
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
                _gone.Clear(); _broken.Clear(); _leanBy = _leanByWas = 0f; _leanTick = _hitTick = -1; _fallEvent = -1; _shook = false;
            }
            for (; _cursor < st.Events.Count; _cursor++)
            {
                var e = st.Events[_cursor];
                if (e.Id != 0 || e.X == null) continue;
                if (e.Kind == EventKind.Damaged) Damaged(e);
                else if (e.Kind == EventKind.Collapsed) { _fallEvent = _cursor; _fall = new Fall(e, _dressing, _root.Ground); _shook = false; _roofLands = -1f; Breaks(); }
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
                    if (p.Face < 0 || _gone.Exists(g => g.piece == k)) continue;
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
        }

        /// <summary>Going over, and down.</summary>
        private void Down(SimState st, float now)
        {
            var f = _fall;
            float t = now - (float)(st.Events[_fallEvent].Tick * Tune.Dt);
            if (t < 0) return;
            float th = f.Angle(t, out _);
            var turn = f.Turn(th);
            var at = f.Place(turn, th);
            // (From the lean it had as it went, over the first moments of going: its lean's way becomes its fall's.)
            if (t < 0.5f)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / 0.5f);
                var (wasAt, was) = Leant(Leaning(now - t));
                turn = Quaternion.Slerp(was, turn, k);
                at = Vector3.Lerp(wasAt, at, k);
            }
            _group.SetPositionAndRotation(at, turn);

            // It strikes the ground: felt, if it is near.
            if (!_shook && t >= f.Hits)
            {
                _shook = true;
                if (t - f.Hits < 0.3f && _root.CameraRig != null)
                    _root.CameraRig.Shake(0.8f * Mathf.Clamp01(1.2f - Mathf.Abs(_foot.x - _root.CameraRig.X) / 70f));
            }
            if (_roof != null && f.RoofOff && t > f.RoofTime) Roof(f, t);
            if (_ladder != null && f.LadderOff && t > f.LadderTime && Vector3.Dot(f.Dir, SideOf(_ladder)) < 0.2f) Ladder(f, t);
            if (t > f.Hits)
            {
                foreach (var b in _bags) Heap(b, f, t - f.Hits);
                for (int k = 0; k < _broken.Count; k++) Broken(_pieces[_broken[k]], f, t - f.Hits, k);
            }
        }

        /// <summary>Which panels break away as it strikes: three of those on its upper face and its sides, by its dice.</summary>
        private void Breaks()
        {
            _broken.Clear();
            var f = _fall;
            var picks = new List<(float order, int k)>();
            for (int k = 0; k < _pieces.Count; k++)
            {
                var p = _pieces[k];
                if (p.Face < 0 || _gone.Exists(g => g.piece == k)) continue;
                // (Its face toward its way is under it, on the ground: that one stays.)
                if (Vector3.Dot(Normal(p.Face), f.Dir) > 0.5f) continue;
                picks.Add((H(f.Seed, 200 + k), k));
            }
            picks.Sort((a, b) => a.order.CompareTo(b.order));
            for (int k = 0; k < picks.Count && k < 3; k++) _broken.Add(picks[k].k);
        }

        /// <summary>A panel broken away as it strikes: it drops off the wreck to the ground beside it, turning over.</summary>
        private void Broken(Piece p, in Fall f, float u, int n)
        {
            var mid = p.B.center;
            var c0 = Carried(f, p, mid, f.Hits);
            var r0 = f.Turn(f.Down);
            var across = Vector3.Cross(Vector3.up, f.Dir);
            float side = H(f.Seed, 220 + n) < 0.5f ? -1f : 1f;
            float drop = Mathf.Max(0f, c0.y - Ground(c0) - 0.2f);
            float e = Mathf.Min(1f, Mathf.Sqrt(2f * drop / G) > 0 ? u / Mathf.Sqrt(2f * drop / G) : 1f);
            var c = c0 + across * (side * 0.9f * e) - Vector3.up * Mathf.Min(drop, 0.5f * G * u * u);
            var r = Quaternion.AngleAxis(side * (40f + 40f * H(f.Seed, 230 + n)) * Mathf.SmoothStep(0f, 1f, e), f.Dir) * r0;
            p.T.SetPositionAndRotation(c - r * mid, r);
        }

        /// <summary>The level way from the tower's middle to a piece (the side of it the piece is on): its mesh is about the middle.</summary>
        private static Vector3 SideOf(Piece p)
        {
            var w = new Vector3(p.B.center.x, 0f, p.B.center.z);
            return w.sqrMagnitude > 1e-4f ? w.normalized : Vector3.zero;
        }

        /// <summary>A mesh point of a piece where the falling tower has it at a time.</summary>
        private static Vector3 Carried(in Fall f, Piece p, Vector3 v, float t)
        {
            float th = f.Angle(t, out _);
            var turn = f.Turn(th);
            return f.Place(turn, th) + turn * (p.Origin + v);
        }

        private float Ground(Vector3 at) => _root.Ground != null ? (float)_root.Ground.HeightAt(at.x, Coords.SimZ(at.z)) : _foot.y;

        /// <summary>
        /// The roof, torn off: it flies on as it was going, a little up and out, tumbling over, and comes down
        /// where it comes down, and lies.
        /// </summary>
        private void Roof(in Fall f, float t)
        {
            var p = _roof;
            var mid = p.B.center;
            float t0 = f.RoofTime;
            var c0 = Carried(f, p, mid, t0);
            var v = (Carried(f, p, mid, t0 + 0.02f) - Carried(f, p, mid, t0 - 0.02f)) / 0.04f + f.Dir * 1.5f + Vector3.up * 2.2f;
            float th0 = f.Angle(t0, out _);
            var r0 = f.Turn(th0);
            var axis = (Vector3.Cross(Vector3.up, f.Dir) + Vector3.up * (H(f.Seed, 20) - 0.5f)).normalized;
            float u = t - t0;
            // Its landing: when its middle comes down to a little over the ground under it.
            if (_roofLands < 0) _roofLands = Landing(c0, v, 0.25f);
            float land = _roofLands;
            float fly = Mathf.Min(u, land);
            var c = c0 + v * fly - 0.5f * G * fly * fly * Vector3.up;
            var r = Quaternion.AngleAxis(f.RoofSpin * fly, axis) * r0;
            if (u > land)
            {
                // Down: it slides a little, and settles flat, the way up it is nearer to.
                float s = u - land;
                var flat = new Vector3(v.x, 0f, v.z);
                c += flat * (0.22f * (1f - Mathf.Exp(-s / 0.22f)));
                c.y = Ground(c) + 0.12f;
                var up = r * Vector3.up;
                var lie = Quaternion.FromToRotation(up, up.y >= 0 ? Vector3.up : Vector3.down) * r;
                r = Quaternion.Slerp(r, lie, Mathf.SmoothStep(0f, 1f, s / 0.3f));
            }
            p.T.SetPositionAndRotation(c - r * mid, r);
        }

        /// <summary>Seconds until a point thrown from c with velocity v comes down to height over the ground under it.</summary>
        private float Landing(Vector3 c, Vector3 v, float over)
        {
            for (float u = 0f; u < 5f; u += 1f / 60f)
            {
                var at = c + v * u - 0.5f * G * u * u * Vector3.up;
                if (u > 0.05f && at.y <= Ground(at) + over) return u;
            }
            return 5f;
        }

        /// <summary>
        /// The ladder, torn away from the deck: it goes over on its own about its foot, which kicks out behind it,
        /// turning off the tower's way to its own, and lies.
        /// </summary>
        private void Ladder(in Fall f, float t)
        {
            var p = _ladder;
            float t0 = f.LadderTime;
            var footLocal = new Vector3(p.B.center.x, p.B.min.y + 0.1f, p.B.center.z);
            var foot0 = Carried(f, p, footLocal, t0);
            float th0 = f.Angle(t0, out float w0);
            float twist = f.TwistAt(th0);
            var own = Quaternion.AngleAxis(f.LadderTurn, Vector3.up) * f.Dir;
            float u = t - t0;
            // (Its own column: lighter and a little shorter, its own equation.)
            const float k = 3f * G / (2f * 9.6f);
            const float down = 87f * Mathf.Deg2Rad;
            float th = th0, w = w0, s = 0f;
            for (; s + Step <= u && th < down; s += Step) { w += k * Mathf.Sin(th) * Step; th += w * Step; }
            if (th >= down)
            {
                float after = u - s;
                th = down - (after < 0.25f ? 0.04f * Mathf.Sin(Mathf.PI * after / 0.25f) : 0f);
            }
            var way = Vector3.Slerp(f.Dir, own, Mathf.SmoothStep(0f, 1f, u / 0.5f)).normalized;
            var r = Tilt(way, th) * Quaternion.AngleAxis(twist, Vector3.up);
            var footAt = foot0 - way * (0.6f * Mathf.Min(1f, u));
            footAt.y = Mathf.Max(Ground(footAt) + 0.05f, foot0.y - 0.5f * G * u * u);
            p.T.SetPositionAndRotation(footAt - r * footLocal, r);
        }

        /// <summary>The deck's sandbags as it strikes: thrown on and down into a low heap where the deck came down.</summary>
        private void Heap(Piece p, in Fall f, float u)
        {
            var mid = p.B.center;
            var from = Carried(f, p, mid, f.Hits);
            var to = from + f.Dir * 0.8f;
            to.y = Ground(to) + 0.12f;
            float e = Mathf.SmoothStep(0f, 1f, u / 0.5f);
            var r0 = f.Turn(f.Down);
            var r1 = Quaternion.AngleAxis((H(f.Seed, 30) - 0.5f) * 60f, Vector3.up);
            var r = Quaternion.Slerp(r0, r1, e);
            var scale = Vector3.Lerp(Vector3.one, new Vector3(1.1f, 0.45f, 1.1f), e);
            var at = Vector3.Lerp(from, to, e) + Vector3.up * (0.6f * Mathf.Sin(Mathf.PI * e));
            p.T.SetPositionAndRotation(at - r * Vector3.Scale(scale, mid), r);
            p.T.localScale = scale;
        }

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
