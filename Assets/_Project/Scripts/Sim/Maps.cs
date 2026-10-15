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

    /// <summary>A bridge (Rice Fields): the lane it carries over the river, from X0 to X1, and how wide its deck is.</summary>
    public sealed class Bridge
    {
        public int Lane;
        public double X0, X1, Width = 3.4;
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
        /// <summary>The theatre it is fought in (the view's art and rules for a country): the firebase's is the Central Highlands.</summary>
        public string Theatre = "highlands";
        public MapPiece[] Pieces = new MapPiece[0];
        /// <summary>The ground's shape: its folds, its track, the bank between the lanes.</summary>
        public GroundParams GroundShape = new GroundParams();
        /// <summary>The seed its crater holes are drawn from, and the old shell holes away from the lanes: how many, and the lines across the map they lie along.</summary>
        public int CraterSeed = 20260929;
        public int OldCraters = 9;
        public double[] OldCraterBandsZ = { 12.5, -12.5, -17.0 };
        public Post[] Posts = new Post[0];
        /// <summary>The Americans start at the east end and advance west (and the VC the other way): Rice Fields. False, as on the firebase: the west.</summary>
        public bool UsEast;
        /// <summary>Its bank's gaps are where its ground's would be turned end for end (Maps.Mirrored): sin(-x k + p) is sin(x k + pi - p).</summary>
        public bool BankMirrored;
        /// <summary>Its bridges (Rice Fields): none on the firebase.</summary>
        public Bridge[] Bridges = new Bridge[0];

        /// <summary>
        /// The map's cover, in order from the Americans' end (x order on the firebase), ids assigned. A fresh copy each
        /// call. (From their end so that a map turned end for end gives each piece the id it had: where two choices tie,
        /// the lower id is taken, and that should not depend on which end a side starts from.)
        /// </summary>
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
            if (UsEast) list.Sort((a, b) => b.X.CompareTo(a.X));
            else list.Sort((a, b) => a.X.CompareTo(b.X));
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

        public Ground Ground() => new Ground(GroundShape, Craters(), Mounds(), Trenches(), Decks());

        /// <summary>Its bridges' decks, for whoever walks on them.</summary>
        public List<BridgeDeck> Decks()
        {
            var outv = new List<BridgeDeck>();
            foreach (var b in Bridges) outv.Add(new BridgeDeck { X0 = b.X0, X1 = b.X1, Z = Tune.Lanes[b.Lane], Width = b.Width });
            return outv;
        }

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
            return (GroundShape.BermZ, GroundShape.BermHeight, BankMirrored ? Math.PI - o : o);
        }
    }

    /// <summary>The maps there are: the firebase, and Rice Fields (being built, PLAN Rice Fields).</summary>
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

        /// <summary>
        /// Rice Fields, the lowlands, 1966 (the owner, 2026-10-15; PLAN, Rice Fields): the VC come out of the forest in the
        /// west; in the middle a river crosses both lanes, the near lane over a timber bridge and the far lane through the
        /// water; east of it the paddies step up, dike by dike, to an American base on the top step. The near lane is the
        /// bridge's: a VC position at the forest's edge and another at the west bank, the bridgehead on the east bank, the
        /// paddies, the base. The far lane fords: a VC trench in the treeline, the reeds on the west bank, the east bank's
        /// dike, a sluice gate, the base's wall, and the base's watchtower behind it.
        /// </summary>
        public static readonly MapDef RiceFields = new MapDef
        {
            Id = "ricefields", Name = "RICE FIELDS", Theatre = "lowlands", UsEast = true,
            Pieces = new[]
            {
                // --- near lane: over the bridge -----------------------------------
                new MapPiece(CoverKind.Trench, Map.Near, -36.0, 0.5, 9.0),     // a VC position of logs and earth in the forest
                new MapPiece(CoverKind.Berm, Map.Near, -25.0, 0.8, 8.0),       // fallen trees at the forest's edge
                new MapPiece(CoverKind.Trench, Map.Near, -13.5, 0.4, 6.0),     // dug in at the west bank, before the bridge
                new MapPiece(CoverKind.Sandbag, Map.Near, 9.0, -0.6, 6.0),     // the bridgehead, on the east bank
                new MapPiece(CoverKind.Berm, Map.Near, 16.0, 0.9, 8.0),        // a dike across the first paddy
                new MapPiece(CoverKind.Crater, Map.Near, 22.5, -0.5, 5.0),     // shell holes, full of water
                new MapPiece(CoverKind.Sandbag, Map.Near, 30.5, 0.8, 7.0),     // the base's revetment
                new MapPiece(CoverKind.Bunker, Map.Near, 38.5, 1.2, 4.0),      // the base's bunker

                // --- far lane: through the water ----------------------------------
                new MapPiece(CoverKind.Berm, Map.Far, -34.0, 0.6, 10.0),       // logs in the forest
                new MapPiece(CoverKind.Trench, Map.Far, -22.0, 0.7, 9.0),      // a VC trench at the treeline
                new MapPiece(CoverKind.Berm, Map.Far, -11.5, -0.4, 7.0),       // the reeds and the west bank
                new MapPiece(CoverKind.Berm, Map.Far, 8.5, 0.5, 8.0),          // the east bank's dike
                new MapPiece(CoverKind.Sandbag, Map.Far, 16.0, -0.6, 5.0),     // the sluice gate
                new MapPiece(CoverKind.Crater, Map.Far, 24.0, 0.4, 5.0),
                new MapPiece(CoverKind.Sandbag, Map.Far, 33.0, -0.3, 8.0),     // the base's wall
            },
            // The river between x -7 and 3, the paddies' four steps from 7 to 32, the base on the top step; flatter
            // ground than the highlands', and a raised path along it where the firebase has its track.
            GroundShape = new GroundParams
            {
                Seed = 20261015, FoldHeight = 0.5, TrackCut = -0.16, RutDepth = 0.04, TrackWander = 1.6, BermHeight = 0.7,
                FarRise = 3.2, FarRiseScale = 48,
                RiverX0 = -7, RiverX1 = 3, PaddyX0 = 7, PaddyX1 = 32, PaddyFlood = 0.16,
            },
            OldCraters = 4,
            Bridges = new[] { new Bridge { Lane = Map.Near, X0 = -9.4, X1 = 5.4 } },
            Posts = new[]
            {
                new Post { Id = 0, Name = "WATCHTOWER", Side = Side.Us, X = -Map.TowerX, Z = Map.TowerZ, Height = 8.45, FootX = -Map.TowerX, FootZ = Map.TowerZ + 0.45, Lane = Map.Far },
            },
        };

        public static readonly MapDef[] All = { Firebase, RiceFields };

        /// <summary>
        /// A map turned end for end: its cover and its posts at the other ends, and the Americans starting where the VC
        /// did (MapDef.UsEast). Not a map anyone plays: it measures that nothing about a side depends on which end of the
        /// map it starts from (PLAN, Rice Fields R1). Its ground is drawn as the map's own, unturned; the bank between the
        /// lanes, which is all of the ground the simulation reads, is turned with it (its gaps).
        /// </summary>
        public static MapDef Mirrored(MapDef m)
        {
            var pieces = new MapPiece[m.Pieces.Length];
            for (int i = 0; i < pieces.Length; i++)
            {
                var p = m.Pieces[i];
                pieces[i] = new MapPiece(p.Kind, p.Lane, -p.X, p.Dz, p.Length);
            }
            var posts = new Post[m.Posts.Length];
            for (int i = 0; i < posts.Length; i++)
            {
                posts[i] = m.Posts[i].Clone();
                posts[i].X = -posts[i].X;
                posts[i].FootX = -posts[i].FootX;
            }
            var bridges = new Bridge[m.Bridges.Length];
            for (int i = 0; i < bridges.Length; i++) bridges[i] = new Bridge { Lane = m.Bridges[i].Lane, X0 = -m.Bridges[i].X1, X1 = -m.Bridges[i].X0, Width = m.Bridges[i].Width };
            return new MapDef
            {
                Id = m.Id + "-mirrored", Name = m.Name, Theatre = m.Theatre, Pieces = pieces, GroundShape = m.GroundShape,
                CraterSeed = m.CraterSeed, OldCraters = m.OldCraters, OldCraterBandsZ = m.OldCraterBandsZ, Posts = posts, UsEast = !m.UsEast,
                BankMirrored = !m.BankMirrored, Bridges = bridges,
            };
        }

        /// <summary>The map of that name, or null.</summary>
        public static MapDef ById(string id)
        {
            foreach (var m in All) if (m.Id == id) return m;
            return null;
        }
    }
}
