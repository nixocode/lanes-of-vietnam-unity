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

        public Ground(GroundParams p, IReadOnlyList<Crater> craters)
        {
            Params = p;
            var rng = new Rng(p.Seed);
            // Fixed offsets so each noise octave is decorrelated but stable.
            for (int i = 0; i < _o.Length; i++) _o[i] = rng.Range(-800, 800);
            _craters = new Crater[craters.Count];
            for (int i = 0; i < craters.Count; i++) _craters[i] = craters[i];
        }

        public IReadOnlyList<Crater> Craters => _craters;

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

            // Small surface detail: hoof prints, tufts, scuffed earth.
            h += Noise.Value2D(x * 0.55 + o[5], z * 0.55 + o[6]) * 0.045;
            h += Noise.Fbm2D(x * 0.17 + o[7], z * 0.17, 3, 2.1, 0.5) * 0.12;
            return h;
        }

        /// <summary>Surface normal, from the same function the mesh is built from.</summary>
        public (double x, double y, double z) NormalAt(double x, double z, double e = 0.4)
        {
            double hx = HeightAt(x + e, z) - HeightAt(x - e, z);
            double hz = HeightAt(x, z + e) - HeightAt(x, z - e);
            double nx = -hx, ny = 2 * e, nz = -hz;
            double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            return (nx / len, ny / len, nz / len);
        }

        /// <summary>Steepness in degrees.</summary>
        public double SlopeAt(double x, double z) => Math.Acos(NormalAt(x, z).y) * (180 / Math.PI);
    }
}
