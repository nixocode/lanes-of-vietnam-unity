using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>The ground's shape parameters. Every length is metres.</summary>
    public sealed class GroundParams
    {
        public int Seed = 20260925;

        /// <summary>Where the track's centre line sits in z, and how much it wanders.</summary>
        public double TrackZ = -0.5;
        public double TrackWander = 2.2;

        /// <summary>Depth of the rutted track below the verge.</summary>
        public double TrackCut = 0.42;

        /// <summary>Amplitude of the broad folds of ground.</summary>
        public double FoldHeight = 1.35;

        /// <summary>The earth bank along the near side of the track.</summary>
        public double BermZ = 3.4;
        public double BermHeight = 0.85;

        /// <summary>Wheel ruts: depth and separation. M35 gauge — the reference's ruts are truck-made.</summary>
        public double RutDepth = 0.16;
        public double RutGauge = 1.75;

        /// <summary>How much the ground lifts toward the treeline, and over what distance.</summary>
        public double FarRise = 2.4;
        public double FarRiseScale = 62;

        /// <summary>
        /// A river across the lanes (Rice Fields; none on the firebase): its banks' x, the height of its water, and of its
        /// bed in the middle of it; the banks go down to it over RiverBank metres.
        /// </summary>
        public double RiverX0 = double.NaN, RiverX1 = double.NaN, RiverWater = -0.85, RiverBed = -1.75, RiverBank = 2.2;

        /// <summary>
        /// Terraced paddies (Rice Fields; none on the firebase): from PaddyX0 to PaddyX1 in steps PaddyStep wide, each
        /// PaddyRise higher than the last toward the east; flat out to PaddyHalfZ either side of the lanes' middle, an earth
        /// dike along each step, and the water standing PaddyFlood deep in each. Past PaddyX1 the last step, dry.
        /// </summary>
        public double PaddyX0 = double.NaN, PaddyX1 = double.NaN, PaddyStep = 6.25, PaddyRise = 0.45, PaddyHalfZ = 17, PaddyMidZ = 0.5;
        public double DikeHeight = 0.34, DikeTop = 0.35, PaddyFlood = 0.12;
    }

    /// <summary>A bridge's deck, for whoever walks on it: along x from X0 to X1, its middle at Z, Width across, level at Y.</summary>
    public struct BridgeDeck
    {
        public double X0, X1, Z, Width, Y;
    }

    /// <summary>A shell hole: a bowl with a raised lip of thrown earth.</summary>
    public struct Crater
    {
        public double X, Z, R, Depth;
    }

    /// <summary>A long low earth bank along the lane, for berm cover.</summary>
    public struct Mound
    {
        public double X, Z, Length, Height, Width;
    }

    /// <summary>A dug trench along the lane: a flat-bottomed cut with spoil thrown up behind it.</summary>
    public struct Trench
    {
        public double X, Z, Length, Depth, Width;
    }

    /// <summary>
    /// The ground's shape, as pure maths: one height function that the terrain
    /// mesh is built from and that anything standing on the ground asks.
    ///
    /// Craters are an input, not random scatter. In the original they were
    /// fourteen random holes independent of the simulation's crater cover, so
    /// a squad could take cover in a crater that was not there and walk
    /// through one that was. Here the map's crater cover and the ground's
    /// craters are the same list.
    ///
    /// View-side maths: it uses <see cref="Math.Exp"/> and friends, whose last
    /// bit is platform-dependent, which is fine for geometry and would not be
    /// for anything the match outcome depends on (see <see cref="JsMath"/>).
    /// </summary>
    public sealed class Ground
    {
        public readonly GroundParams Params;
        private readonly double[] _o = new double[8];
        private readonly Crater[] _craters;
        private readonly Mound[] _mounds;
        private readonly Trench[] _trenches;

        public Ground(GroundParams p, IReadOnlyList<Crater> craters,
                      IReadOnlyList<Mound> mounds = null, IReadOnlyList<Trench> trenches = null, IReadOnlyList<BridgeDeck> decks = null)
        {
            Params = p;
            var rng = new Rng(p.Seed);
            // Fixed offsets so each noise octave is decorrelated but stable.
            for (int i = 0; i < _o.Length; i++) _o[i] = rng.Range(-800, 800);
            _craters = Copy(craters);
            _mounds = Copy(mounds);
            _trenches = Copy(trenches);
            // A bridge's deck lies level, a hand above the higher of its two ends.
            _decks = Copy(decks);
            for (int i = 0; i < _decks.Length; i++)
            {
                var d = _decks[i];
                d.Y = Math.Max(HeightAt(d.X0, d.Z), HeightAt(d.X1, d.Z)) + 0.22;
                _decks[i] = d;
            }
        }

        private readonly BridgeDeck[] _decks;
        public IReadOnlyList<BridgeDeck> Decks => _decks;

        /// <summary>Where a man's feet are at (x, z): on a bridge's deck if he is on one, on the ground if not.</summary>
        public double FootAt(double x, double z)
        {
            double h = HeightAt(x, z);
            for (int i = 0; i < _decks.Length; i++)
            {
                var d = _decks[i];
                if (x >= d.X0 && x <= d.X1 && Math.Abs(z - d.Z) <= d.Width * 0.5) h = Math.Max(h, d.Y);
            }
            return h;
        }

        /// <summary>
        /// Where the river's banks are at z: RiverX0 and RiverX1 where the near lane crosses it, winding a little either side
        /// of the lanes and more out of them, the banks ragged; straight and true where a bridge stands on them.
        /// </summary>
        public (double x0, double x1) RiverAt(double z)
        {
            var p = Params;
            double wind = 2.2 * Math.Sin(z * 0.045 - 0.315) + 4.0 * Smooth(14, 50, Math.Abs(z)) * Math.Sin(z * 0.021 + 0.6);
            double true_ = 1;
            if (_decks != null)
                for (int i = 0; i < _decks.Length; i++) true_ = Math.Min(true_, Smooth(_decks[i].Width * 0.5 + 0.5, _decks[i].Width * 0.5 + 6, Math.Abs(z - _decks[i].Z)));
            double r0 = (Math.Sin(z * 0.31 + _o[5]) * 0.5 + Math.Sin(z * 0.73 + _o[6]) * 0.3) * true_;
            double r1 = (Math.Sin(z * 0.27 + _o[6]) * 0.5 + Math.Sin(z * 0.81 + _o[7]) * 0.3) * true_;
            return (p.RiverX0 + wind + r0, p.RiverX1 + wind + r1);
        }

        /// <summary>Is there a river, and is (x, z) in it (between its banks' feet)?</summary>
        public bool InRiver(double x, double z)
        {
            if (double.IsNaN(Params.RiverX0)) return false;
            var (x0, x1) = RiverAt(z);
            return x > x0 && x < x1;
        }

        /// <summary>Which of the paddies' steps x is on (0 nearest the river), or -1 if it is not in them.</summary>
        public int Terrace(double x)
        {
            var p = Params;
            if (double.IsNaN(p.PaddyX0) || x < p.PaddyX0) return -1;
            return (int)Math.Min(Math.Floor((x - p.PaddyX0) / p.PaddyStep), Math.Round((p.PaddyX1 - p.PaddyX0) / p.PaddyStep));
        }

        /// <summary>The level of a step's floor (its water stands PaddyFlood above it).</summary>
        public double TerraceLevel(int k) => (k + 1) * Params.PaddyRise;

        /// <summary>How many of the paddies' steps are flooded (the last, past PaddyX1, is dry: the base is on it).</summary>
        public int FloodedSteps => double.IsNaN(Params.PaddyX0) ? 0 : (int)Math.Round((Params.PaddyX1 - Params.PaddyX0) / Params.PaddyStep);

        /// <summary>The height of the water at (x, z): the river's, or a flooded paddy's; NaN where there is none.</summary>
        public double WaterAt(double x, double z)
        {
            var p = Params;
            if (!double.IsNaN(p.RiverX0))
            {
                var (r0, r1) = RiverAt(z);
                if (x > r0 - p.RiverBank && x < r1 + p.RiverBank) return p.RiverWater;
            }
            int k = Terrace(x);
            if (k >= 0 && k < FloodedSteps && Math.Abs(z - p.PaddyMidZ) < p.PaddyHalfZ) return TerraceLevel(k) + p.PaddyFlood;
            return double.NaN;
        }

        /// <summary>The top of whatever is there to stand on or float on: a deck, the ground, or the water over it.</summary>
        public double SurfaceAt(double x, double z)
        {
            double h = FootAt(x, z), w = WaterAt(x, z);
            return double.IsNaN(w) ? h : Math.Max(h, w);
        }

        private static T[] Copy<T>(IReadOnlyList<T> src)
        {
            if (src == null) return Array.Empty<T>();
            var a = new T[src.Count];
            for (int i = 0; i < a.Length; i++) a[i] = src[i];
            return a;
        }

        public IReadOnlyList<Crater> Craters => _craters;
        public IReadOnlyList<Mound> Mounds => _mounds;
        public IReadOnlyList<Trench> Trenches => _trenches;

        /// <summary>Smooth 0..1 across a segment [x0 - s, x0] rising and [x1, x1 + s] falling.</summary>
        private static double Span(double x, double centre, double half, double soft)
        {
            double d = Math.Abs(x - centre) - half;
            if (d <= 0) return 1;
            if (d >= soft) return 0;
            double t = 1 - d / soft;
            return t * t * (3 - 2 * t);
        }

        /// <summary>Where the track's centre line sits in z, for a given x.</summary>
        public double TrackCentre(double x)
        {
            var p = Params;
            return p.TrackZ
                + Math.Sin(x * 0.0135 + _o[0]) * p.TrackWander
                + Math.Sin(x * 0.047 + _o[1]) * p.TrackWander * 0.28;
        }

        /// <summary>How far a point is inside the track's cut, 0..1. For the ground shader's dirt mask.</summary>
        public double TrackMask(double x, double z)
        {
            double d = Math.Abs(z - TrackCentre(x)) / 4.6;
            return Math.Exp(-d * d * 1.6);
        }

        /// <summary>0..1 where the berm stands, cut through at crossings.</summary>
        public double BermGate(double x)
        {
            double gaps = Math.Sin(x * 0.019 + _o[4]) * 0.5 + 0.5;
            return Math.Max(0, Math.Min(1, (gaps - 0.34) * 3.2));
        }

        /// <summary>Ground height in metres at a world (x, z).</summary>
        public double HeightAt(double x, double z)
        {
            var p = Params;
            var o = _o;

            // Broad folds: the shape of the ground before anything was built on it.
            double h = Noise.Fbm2D(x * 0.0125 + o[2], z * 0.0125 + o[3], 4, 2.02, 0.5) * p.FoldHeight;

            // A gentle rise away from the camera, so the far lane sits higher
            // in frame and the depth bands stay separate. It saturates: an
            // unbounded rise puts the far ground above the camera.
            h += p.FarRise * (1 - Math.Exp(-Math.Max(0, -z) / p.FarRiseScale));

            // The earth bank between the lanes, cut through at crossings — men
            // cross at a gap in the wire or a dip in the bank, not anywhere.
            // Steep on the near face, where a man gets behind it; gentle behind.
            double bermRaw = z - p.BermZ;
            double bermD = bermRaw < 0 ? bermRaw / 1.7 : bermRaw / 3.4;
            h += Math.Exp(-bermD * bermD) * p.BermHeight * BermGate(x);

            // The track: a shallow cut with raised shoulders.
            double dz = z - TrackCentre(x);
            double d = Math.Abs(dz) / 4.6;
            double cut = Math.Exp(-d * d * 1.6);
            double shoulder = Math.Exp(-Math.Pow(Math.Max(0, d - 1.0), 2) * 5.0) * 0.30;
            h += -cut * p.TrackCut + shoulder * p.TrackCut;

            // Wheel ruts. A dirt track without them reads as a mown stripe.
            // They fade where the track wanders, as real ruts do on a bend.
            double rutFade = 0.55 + 0.45 * Math.Sin(x * 0.031 + o[3]);
            for (int side = -1; side <= 1; side += 2)
            {
                double rd = (dz - side * p.RutGauge * 0.5) / 0.62;
                h -= Math.Exp(-rd * rd) * p.RutDepth * cut * rutFade;
            }

            // Shell craters: a bowl with a lip of thrown earth around it.
            for (int i = 0; i < _craters.Length; i++)
            {
                var c = _craters[i];
                double dist = JsMath.Hypot(x - c.X, z - c.Z) / c.R;
                if (dist > 2.2) continue;
                double bowl = Math.Exp(-dist * dist * 2.2) * c.Depth;
                double lip = Math.Exp(-Math.Pow(dist - 1.05, 2) * 7.0) * c.Depth * 0.42;
                h += lip - bowl;
            }

            // Earth banks for berm cover.
            for (int i = 0; i < _mounds.Length; i++)
            {
                var m = _mounds[i];
                double along = Span(x, m.X, m.Length * 0.5, 2.0);
                if (along <= 0) continue;
                double dzm = (z - m.Z) / m.Width;
                h += Math.Exp(-dzm * dzm * 1.8) * m.Height * along;
            }

            // Dug trenches: a flat-bottomed cut, and the spoil thrown up on the
            // side facing away from the camera, where it is a parapet.
            for (int i = 0; i < _trenches.Length; i++)
            {
                var t = _trenches[i];
                double along = Span(x, t.X, t.Length * 0.5, 0.9);
                if (along <= 0) continue;
                double across = Math.Abs(z - t.Z) / (t.Width * 0.5);
                double dig = across < 0.75 ? 1 : across > 1.25 ? 0 : 1 - (across - 0.75) / 0.5;
                dig = dig * dig * (3 - 2 * dig);
                double ds = (z - (t.Z - t.Width * 1.05)) / 0.7;
                double spoil = Math.Exp(-ds * ds) * t.Depth * 0.45;
                h += (spoil - dig * t.Depth) * along;
            }

            // Small surface detail: hoof prints, tufts, scuffed earth.
            h += Noise.Value2D(x * 0.55 + o[5], z * 0.55 + o[6]) * 0.045;
            h += Noise.Fbm2D(x * 0.17 + o[7], z * 0.17, 3, 2.1, 0.5) * 0.12;

            // Rice Fields: the paddies levelled into their steps, and the river cut through.
            if (!double.IsNaN(p.PaddyX0)) h = Paddies(x, z, h);
            if (!double.IsNaN(p.RiverX0)) h = River(x, z, h);
            return h;
        }

        private static double Smooth(double e0, double e1, double v)
        {
            double t = Math.Max(0, Math.Min(1, (v - e0) / (e1 - e0)));
            return t * t * (3 - 2 * t);
        }

        /// <summary>
        /// The paddies: flat steps rising toward the east, each with a dike along its edge, the ground's own small
        /// unevenness left in their floors (mud standing out of the water here and there) and its banks and cover with it.
        /// Out of their flat they go back to the ground as it was.
        /// </summary>
        private double Paddies(double x, double z, double h)
        {
            var p = Params;
            double into = Smooth(p.PaddyX0 - 3.5, p.PaddyX0 - 0.5, x) * (1 - Smooth(p.PaddyHalfZ, p.PaddyHalfZ + 9, Math.Abs(z - p.PaddyMidZ)));
            if (into <= 0) return h;
            int k = Math.Max(0, Terrace(x));
            double floor = TerraceLevel(k);
            // What of the ground is the paddies' own: its small detail, the banks and cover raised on it (the folds and the rise go).
            double natural = Noise.Fbm2D(x * 0.0125 + _o[2], z * 0.0125 + _o[3], 4, 2.02, 0.5) * p.FoldHeight
                           + p.FarRise * (1 - Math.Exp(-Math.Max(0, -z) / p.FarRiseScale));
            double detail = Noise.Value2D(x * 0.55 + _o[5], z * 0.55 + _o[6]) * 0.045 + Noise.Fbm2D(x * 0.17 + _o[7], z * 0.17, 3, 2.1, 0.5) * 0.12;
            // (The banks and cover at three fifths; the small unevenness at a fifth: a paddy's floor is puddled flat.)
            double level = floor + (h - natural - detail) * 0.6 + detail * 0.2;
            // The dikes: one along the foot of each step, its top a little over the step above it.
            int steps = (int)Math.Round((p.PaddyX1 - p.PaddyX0) / p.PaddyStep);
            for (int i = 0; i <= steps; i++)
            {
                double xb = p.PaddyX0 + i * p.PaddyStep;
                double top = TerraceLevel(Math.Min(i, steps)) + p.DikeHeight;
                double dike = top - Math.Max(0, Math.Abs(x - xb) - p.DikeTop * 0.5) * 1.1;
                level = Math.Max(level, dike);
            }
            // And one along each side, the steps' ends, so their water has an edge to stand against.
            double side = TerraceLevel(Math.Min(k, steps)) + p.DikeHeight - Math.Max(0, Math.Abs(Math.Abs(z - p.PaddyMidZ) - p.PaddyHalfZ) - p.DikeTop * 0.5) * 1.1;
            if (x > p.PaddyX0 - 0.5) level = Math.Max(level, side);
            return h + (level - h) * into;
        }

        /// <summary>The river: its banks going down to its bed, across the whole of the ground.</summary>
        private double River(double x, double z, double h)
        {
            var p = Params;
            var (x0, x1) = RiverAt(z);
            double inside = Math.Min(x - x0, x1 - x);
            if (inside <= -p.RiverBank) return h;
            // From the top of the bank to the bed: the bank's slope, then the bed rounded to its deepest in the middle.
            double half = (x1 - x0) * 0.5;
            double bed = p.RiverBed + (p.RiverWater - 0.15 - p.RiverBed) * Math.Pow(Math.Max(0, 1 - Math.Max(0, inside) / half), 2);
            double t = Smooth(-p.RiverBank, p.RiverBank * 0.4, inside);
            return Math.Min(h, h + (bed - h) * t);
        }
    }
}
