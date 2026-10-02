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
                      IReadOnlyList<Mound> mounds = null, IReadOnlyList<Trench> trenches = null)
        {
            Params = p;
            var rng = new Rng(p.Seed);
            // Fixed offsets so each noise octave is decorrelated but stable.
            for (int i = 0; i < _o.Length; i++) _o[i] = rng.Range(-800, 800);
            _craters = Copy(craters);
            _mounds = Copy(mounds);
            _trenches = Copy(trenches);
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
            return h;
        }
    }
}
