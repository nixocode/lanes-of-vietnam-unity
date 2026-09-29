using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// The one map: where the cover is, and the shape of the ground under it.
    ///
    /// Authored, not rolled per seed. The brief asks for "one map, built as
    /// well as a map can be built", and a map can only be built — lit, planted,
    /// dressed — if it holds still. The seed decides the fight, not the ground.
    ///
    /// This list is the single source of truth PLAN §3.3 asks for: the
    /// simulation takes it as its cover, the ground takes its craters and
    /// banks from it, and the scene builder places every trench and sandbag
    /// wall from it. A test holds the scene to it.
    ///
    /// The two lanes are meant to play differently, and the list says why:
    /// the near lane is the firebase's own ground, dug in and built up; the far
    /// lane is the track verge and the treeline edge, where the only cover is
    /// what the fighting left.
    /// </summary>
    public static class Map
    {
        /// <summary>Near lane, far lane: <see cref="Tune.Lanes"/>.</summary>
        public const int Near = 0, Far = 1;

        private struct Piece
        {
            public CoverKind Kind;
            public int Lane;
            public double X, Dz, Length;
            public Piece(CoverKind k, int lane, double x, double dz, double length)
            { Kind = k; Lane = lane; X = x; Dz = dz; Length = length; }
        }

        // West is the firebase (US, advancing +x). East is the jungle.
        private static readonly Piece[] Pieces =
        {
            // --- near lane: the firebase and the ground in front of it --------
            new Piece(CoverKind.Sandbag, Near, -39.0, 0.8, 7.0),    // perimeter revetment
            new Piece(CoverKind.Bunker, Near, -31.5, 1.2, 4.0),     // the firebase bunker
            new Piece(CoverKind.Trench, Near, -23.0, 0.4, 12.0),    // forward trench
            new Piece(CoverKind.Sandbag, Near, -13.5, -0.6, 6.0),   // listening post
            new Piece(CoverKind.Berm, Near, -3.5, 0.9, 9.0),
            new Piece(CoverKind.Crater, Near, 6.5, -0.8, 6.0),
            new Piece(CoverKind.Trench, Near, 16.0, 0.6, 8.0),      // an old line, half fallen in
            new Piece(CoverKind.Berm, Near, 26.5, -0.4, 9.0),
            new Piece(CoverKind.Crater, Near, 36.5, 0.5, 6.0),

            // --- far lane: the track verge and the treeline edge ---------------
            new Piece(CoverKind.Berm, Far, -36.0, 0.6, 12.0),
            new Piece(CoverKind.Crater, Far, -25.0, -0.5, 6.0),
            new Piece(CoverKind.Berm, Far, -13.0, 0.8, 11.0),
            new Piece(CoverKind.Crater, Far, -2.0, -0.7, 7.0),
            new Piece(CoverKind.Berm, Far, 9.0, 0.3, 10.0),
            new Piece(CoverKind.Crater, Far, 19.5, -0.4, 5.0),
            new Piece(CoverKind.Trench, Far, 29.0, 0.7, 8.0),       // VC-dug fighting positions
            new Piece(CoverKind.Berm, Far, 39.0, -0.2, 10.0),
        };

        /// <summary>The map's cover, in x order, ids assigned. A fresh copy each call.</summary>
        public static List<Cover> Cover()
        {
            var list = new List<Cover>(Pieces.Length);
            foreach (var p in Pieces)
            {
                var spec = CoverSpec.For(p.Kind);
                list.Add(new Cover
                {
                    Kind = p.Kind,
                    X = p.X,
                    Z = Tune.Lanes[p.Lane] + p.Dz,
                    Length = p.Length,
                    Capacity = Math.Max(2, (int)JsMath.Round(p.Length / spec.metresPerMan)),
                    // Mid-range for the kind: a fixed map has no roll to make.
                    Quality = (spec.qLo + spec.qHi) * 0.5,
                });
            }
            list.Sort((a, b) => a.X.CompareTo(b.X));
            for (int i = 0; i < list.Count; i++) list[i].Id = i;
            return list;
        }

        /// <summary>The ground's shape parameters for this map.</summary>
        public static GroundParams GroundParams() => new GroundParams();

        /// <summary>
        /// Every crater in the ground: the crater cover's own holes, strung
        /// along each piece's length, plus a scatter of old ones away from the
        /// lanes so the shelling reads as having happened to the whole valley
        /// and not only where men happen to fight.
        /// </summary>
        public static List<Crater> Craters()
        {
            var outv = new List<Crater>();
            var rng = new Rng(20260929).Fork("craters");
            foreach (var c in Cover())
            {
                if (c.Kind != CoverKind.Crater) continue;
                int n = Math.Max(1, (int)Math.Ceiling(c.Length / 3.2));
                for (int i = 0; i < n; i++)
                {
                    double t = n == 1 ? 0.5 : (double)i / (n - 1);
                    double r = rng.Range(1.5, 2.2);
                    outv.Add(new Crater
                    {
                        X = c.X - c.Length * 0.5 + r + t * (c.Length - 2 * r),
                        Z = c.Z + rng.Range(-0.6, 0.6),
                        R = r,
                        Depth = r * rng.Range(0.26, 0.34),
                    });
                }
            }
            // Old shell holes out of the way of the lanes: behind the near lane,
            // on the track shoulders, and in the scrub before the treeline.
            double[] bandsZ = { 12.5, -12.5, -17.0 };
            for (int i = 0; i < 9; i++)
            {
                double z = bandsZ[i % bandsZ.Length] + rng.Range(-1.5, 1.5);
                double r = rng.Range(1.2, 2.6);
                outv.Add(new Crater
                {
                    X = rng.Range(-60, 60), Z = z, R = r, Depth = r * rng.Range(0.18, 0.28),
                });
            }
            return outv;
        }

        /// <summary>
        /// Earth banks the ground raises for berm cover: long low mounds along
        /// the lane, so a man told to get behind a berm has one to get behind.
        /// </summary>
        public static List<Mound> Mounds()
        {
            var outv = new List<Mound>();
            foreach (var c in Cover())
            {
                if (c.Kind != CoverKind.Berm) continue;
                outv.Add(new Mound { X = c.X, Z = c.Z - 1.1, Length = c.Length, Height = 0.75, Width = 1.4 });
            }
            return outv;
        }

        /// <summary>The trench cover, dug into the ground: deep enough that a standing man shows from the chest up.</summary>
        public static List<Trench> Trenches()
        {
            var outv = new List<Trench>();
            foreach (var c in Cover())
            {
                if (c.Kind != CoverKind.Trench) continue;
                outv.Add(new Trench { X = c.X, Z = c.Z, Length = c.Length, Depth = 1.05, Width = 1.3 });
            }
            return outv;
        }

        public static Ground Ground() => new Ground(GroundParams(), Craters(), Mounds(), Trenches());
    }
}
