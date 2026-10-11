using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>One piece of a map's cover: what it is, its lane, where along it, how far off the lane's middle, how long.</summary>
    public readonly struct MapPiece
    {
        public readonly CoverKind Kind;
        public readonly int Lane;
        public readonly double X, Dz, Length;
        public MapPiece(CoverKind k, int lane, double x, double dz, double length)
        { Kind = k; Lane = lane; X = x; Dz = dz; Length = length; }
    }

    /// <summary>
    /// A map, as data (PLAN §12.27, S1): where its cover is, the shape of
    /// the ground under it, the old shell holes away from the lanes, and the
    /// places up high a squad can be posted on.
    ///
    /// The firebase was the one map, written into <see cref="Map"/> as code.
    /// A second map needs it to be a thing that can be handed over: the
    /// simulation takes its cover, its bank and its posts from here, the
    /// ground takes its craters and banks, and the scene is built from the
    /// same. The lanes' own positions and the half-length are still the
    /// project's (<see cref="Tune.Lanes"/>, <see cref="Tune.HalfLength"/>):
    /// a map that needs its own pays for that when it comes.
    /// </summary>
    public sealed class MapDef
    {
        public string Id, Name;
        public MapPiece[] Pieces = new MapPiece[0];
        /// <summary>The ground's shape: its folds, its track, the bank between the lanes.</summary>
        public GroundParams GroundShape = new GroundParams();
        /// <summary>The seed its crater holes are drawn from, and the old shell holes away from the lanes: how many, and the lines across the map they lie along.</summary>
        public int CraterSeed = 20260929;
        public int OldCraters = 9;
        public double[] OldCraterBandsZ = { 12.5, -12.5, -17.0 };
        public Post[] Posts = new Post[0];

        /// <summary>The map's cover, in x order, ids assigned. A fresh copy each call.</summary>
        public List<Cover> Cover()
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

        /// <summary>The places up high a squad can be posted on (MatchOptions.Vantage). A fresh copy each call.</summary>
        public List<Post> PostList()
        {
            var list = new List<Post>(Posts.Length);
            foreach (var p in Posts) list.Add(p.Clone());
            return list;
        }

        /// <summary>
        /// Every crater in the ground: the crater cover's own holes, strung
        /// along each piece's length, plus a scatter of old ones away from the
        /// lanes so the shelling reads as having happened to the whole valley
        /// and not only where men happen to fight.
        /// </summary>
        public List<Crater> Craters()
        {
            var outv = new List<Crater>();
            var rng = new Rng(CraterSeed).Fork("craters");
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
            for (int i = 0; i < OldCraters && OldCraterBandsZ.Length > 0; i++)
            {
                double z = OldCraterBandsZ[i % OldCraterBandsZ.Length] + rng.Range(-1.5, 1.5);
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
        public List<Mound> Mounds()
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
        public List<Trench> Trenches()
        {
            var outv = new List<Trench>();
            foreach (var c in Cover())
            {
                if (c.Kind != CoverKind.Trench) continue;
                outv.Add(new Trench { X = c.X, Z = c.Z, Length = c.Length, Depth = 1.05, Width = 1.3 });
            }
            return outv;
        }

        public Ground Ground() => new Ground(GroundShape, Craters(), Mounds(), Trenches());

        /// <summary>
        /// The bank between the lanes, as the simulation needs it (Gunnery): where it runs, how high, and
        /// where along it its gaps fall. The same bank the ground is built with: its offset is the fifth draw
        /// of the ground's own stream.
        /// </summary>
        public (double z, double height, double phase) Bank()
        {
            var rng = new Rng(GroundShape.Seed);
            double o = 0;
            for (int i = 0; i < 5; i++) o = rng.Range(-800, 800);
            return (GroundShape.BermZ, GroundShape.BermHeight, o);
        }
    }

    /// <summary>The maps there are. One so far.</summary>
    public static class Maps
    {
        /// <summary>
        /// The firebase in the Central Highlands, 1965. Authored, not rolled per seed: the brief asks for "one
        /// map, built as well as a map can be built", and a map can only be built (lit, planted, dressed) if it
        /// holds still. The two lanes are meant to play differently, and the list says why: the near lane is the
        /// firebase's own ground, dug in and built up; the far lane is the track verge and the treeline edge,
        /// where the only cover is what the fighting left. West is the firebase (US, advancing +x); east, the jungle.
        /// </summary>
        public static readonly MapDef Firebase = new MapDef
        {
            Id = "firebase", Name = "THE FIREBASE",
            Pieces = new[]
            {
            // --- near lane: the firebase and the ground in front of it --------
            new MapPiece(CoverKind.Sandbag, Map.Near, -39.0, 0.8, 7.0),    // perimeter revetment
            new MapPiece(CoverKind.Bunker, Map.Near, -31.5, 1.2, 4.0),     // the firebase bunker
            new MapPiece(CoverKind.Trench, Map.Near, -23.0, 0.4, 12.0),    // forward trench
            new MapPiece(CoverKind.Sandbag, Map.Near, -13.5, -0.6, 6.0),   // listening post
            new MapPiece(CoverKind.Berm, Map.Near, -3.5, 0.9, 9.0),
            new MapPiece(CoverKind.Crater, Map.Near, 6.5, -0.8, 6.0),
            new MapPiece(CoverKind.Trench, Map.Near, 16.0, 0.6, 8.0),      // an old line, half fallen in
            new MapPiece(CoverKind.Berm, Map.Near, 26.5, -0.4, 9.0),
            new MapPiece(CoverKind.Crater, Map.Near, 36.5, 0.5, 6.0),

            // --- far lane: the track verge and the treeline edge ---------------
            new MapPiece(CoverKind.Berm, Map.Far, -36.0, 0.6, 12.0),
            new MapPiece(CoverKind.Crater, Map.Far, -25.0, -0.5, 6.0),
            new MapPiece(CoverKind.Berm, Map.Far, -13.0, 0.8, 11.0),
            new MapPiece(CoverKind.Crater, Map.Far, -2.0, -0.7, 7.0),
            new MapPiece(CoverKind.Berm, Map.Far, 9.0, 0.3, 10.0),
            new MapPiece(CoverKind.Crater, Map.Far, 19.5, -0.4, 5.0),
            new MapPiece(CoverKind.Trench, Map.Far, 29.0, 0.7, 8.0),       // VC-dug fighting positions
            new MapPiece(CoverKind.Berm, Map.Far, 39.0, -0.2, 10.0),
        },
            // The watchtower: inside the wire behind the far lane, its platform 8.45 m up and its ladder up the
            // front (where WorldDressing stands it, and as tools/blender/plant_bake.py builds it). The Americans'.
            Posts = new[]
            {
                new Post { Id = 0, Name = "WATCHTOWER", Side = Side.Us, X = Map.TowerX, Z = Map.TowerZ, Height = 8.45, FootX = Map.TowerX, FootZ = Map.TowerZ + 0.45, Lane = Map.Far },
            },
        };

        public static readonly MapDef[] All = { Firebase };

        /// <summary>The map of that name, or null.</summary>
        public static MapDef ById(string id)
        {
            foreach (var m in All) if (m.Id == id) return m;
            return null;
        }
    }
}
