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

    /// <summary>Where a lane steps up (MatchOptions.Heights): from X on along it, the ground is Rise metres higher.</summary>
    public sealed class Step
    {
        public int Lane;
        public double X, Rise;
    }

    /// <summary>Where a lane crosses a river (MatchOptions.Water): from X0 to X1 a man on it is in water deep enough to swim.</summary>
    public sealed class Ford
    {
        public int Lane;
        public double X0, X1;
    }

    /// <summary>
    /// Where a map's trees stand thick enough to hide a man (MatchOptions.Strike: the Americans' napalm on the forest):
    /// x0 to x1 along the lanes, z0 to z1 across them.
    /// </summary>
    public sealed class Forest
    {
        public double X0, X1, Z0, Z1;
    }

    /// <summary>What of a forest has burned (MatchOptions.Strike): x0 to x1 of it, z0 to z1, from tick Lit on.</summary>
    public sealed class Burn
    {
        public double X0, X1, Z0, Z1;
        public int Lit;
    }

    /// <summary>A bridge as it stands in a match (MatchOptions.Water): its strength, fallen or not, and its rebuilding.</summary>
    public sealed class BridgeState
    {
        public int Id, Lane;
        public double X0, X1, Strength = Tune.BridgeStrength;
        public bool Fallen;
        /// <summary>Ticks of work done putting it back, and the squad doing it (-1: nobody).</summary>
        public int Work, Builder = -1;
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
    /// same. Its lanes are its own (<see cref="Lanes"/>: the firebase's two,
    /// <see cref="Tune.Lanes"/>, unless it says otherwise); the half-length is
    /// still the project's (<see cref="Tune.HalfLength"/>).
    /// </summary>
    public sealed class MapDef
    {
        public string Id, Name;
        /// <summary>Where each of its lanes runs across it (z), nearest the lens first: the firebase's two unless it has more (W2: Rice Fields' three).</summary>
        public double[] Lanes = Tune.Lanes;
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
        /// <summary>Where its lanes cross a river, measured from its ground (`simcs ground`): none on the firebase.</summary>
        public Ford[] Fords = new Ford[0];
        /// <summary>Where its lanes step up (its terraces, where their feet cross each lane): none on the firebase.</summary>
        public Step[] Steps = new Step[0];
        /// <summary>Its forests (MatchOptions.Strike's napalm on the forest): none on the firebase, where the jungle is behind the lanes.</summary>
        public Forest[] Forests = new Forest[0];
        /// <summary>
        /// The view's, for a map whose ground climbs (Rice Fields): the camera rises and falls with the lanes' ground as
        /// it pans, so high ground is framed as low ground is; and it looks CameraDrop degrees further down, the lanes
        /// higher in the frame, clear of the cards along the bottom (the owner, 2026-10-15: "the lanes are too close to
        /// the HUD ... giving proper space where needed").
        /// </summary>
        public bool CameraRides;
        public double CameraDrop;

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
                    Z = Lanes[p.Lane] + p.Dz,
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
            foreach (var b in Bridges) outv.Add(new BridgeDeck { X0 = b.X0, X1 = b.X1, Z = Lanes[b.Lane], Width = b.Width });
            return outv;
        }

        /// <summary>
        /// The bank between the lanes, as the simulation needs it (Gunnery): where it runs, how high, and
        /// where along it its gaps fall. The same bank the ground is built with: its offset is the fifth draw
        /// of the ground's own stream.
        /// </summary>
        public (double z, double height, double phase) Bank()
        {
            double o = BankOffset();
            return (GroundShape.BermZ, GroundShape.BermHeight, BankMirrored ? Math.PI - o : o);
        }

        /// <summary>The banks between its lanes beyond the first (GroundParams.MoreBermZ), as the simulation needs them: none on the firebase.</summary>
        public (double Z, double Height, double Phase)[] MoreBanks()
        {
            var more = GroundShape.MoreBermZ;
            var outv = new (double, double, double)[more.Length];
            double o = BankOffset();
            for (int i = 0; i < more.Length; i++)
            {
                double ph = o + GroundParams.MoreBermShift * (i + 1);
                outv[i] = (more[i], GroundShape.BermHeight, BankMirrored ? Math.PI - ph : ph);
            }
            return outv;
        }

        /// <summary>The ground's offset for its banks' gaps: the fifth draw of its own stream.</summary>
        private double BankOffset()
        {
            var rng = new Rng(GroundShape.Seed);
            double o = 0;
            for (int i = 0; i < 5; i++) o = rng.Range(-800, 800);
            return o;
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
        /// dike, a sluice gate, the base's wall, and the base's watchtower behind it. The back lane (W2, 2026-10-19), 13 m
        /// behind the far one with a bank between them, has the second bridge: a VC position and fallen trees in the forest,
        /// a trench short of the bridge, the second bridgehead on the first step, a sluice gate, shell holes, a dike, the
        /// base's south wall and its back trench.
        /// </summary>
        public static readonly MapDef RiceFields = new MapDef
        {
            Id = "ricefields", Name = "RICE FIELDS", Theatre = "lowlands", UsEast = true, CameraRides = true, CameraDrop = 3.6,
            // Three lanes (W2, the owner 2026-10-17: "a third lane for this map ... behind, second bridge"): the near lane over
            // the first bridge, the far lane through the water, and the back lane, 13 m behind it, over a second bridge.
            Lanes = new[] { 7.0, -6.0, -19.0 },
            Pieces = new[]
            {
                // --- near lane: over the bridge, and up the steps -------------------
                new MapPiece(CoverKind.Trench, Map.Near, -36.0, 0.5, 9.0),     // a VC position of logs and earth in the forest
                new MapPiece(CoverKind.Berm, Map.Near, -25.0, 0.8, 8.0),       // fallen trees at the forest's edge
                new MapPiece(CoverKind.Trench, Map.Near, -19.0, 0.4, 6.0),     // dug in short of the bridge's ramp
                new MapPiece(CoverKind.Sandbag, Map.Near, 12.3, -0.6, 4.4),    // the bridgehead, behind the first step's dike
                new MapPiece(CoverKind.Berm, Map.Near, 16.6, 0.9, 2.8),        // the second step's dike
                new MapPiece(CoverKind.Crater, Map.Near, 23.6, -0.5, 4.0),     // shell holes, full of water
                new MapPiece(CoverKind.Sandbag, Map.Near, 29.3, 0.8, 4.4),     // bags along the fourth step's dike
                new MapPiece(CoverKind.Sandbag, Map.Near, 35.0, 0.8, 4.0),     // the base's revetment, at the top
                new MapPiece(CoverKind.Bunker, Map.Near, 40.5, 1.2, 4.0),      // the base's bunker

                // --- far lane: through the water, and up the steps ------------------
                new MapPiece(CoverKind.Berm, Map.Far, -34.0, 0.6, 10.0),       // logs in the forest
                new MapPiece(CoverKind.Trench, Map.Far, -22.0, 0.7, 9.0),      // a VC trench at the treeline
                new MapPiece(CoverKind.Berm, Map.Far, -12.5, -0.4, 5.0),       // the reeds and the west bank
                new MapPiece(CoverKind.Berm, Map.Far, 8.4, 0.5, 3.2),          // the first step's dike, over the east bank
                new MapPiece(CoverKind.Sandbag, Map.Far, 17.6, -0.6, 3.6),     // the sluice gate
                new MapPiece(CoverKind.Crater, Map.Far, 24.7, 0.4, 3.0),
                new MapPiece(CoverKind.Sandbag, Map.Far, 29.4, -0.3, 4.4),     // bags along the fourth step's dike
                new MapPiece(CoverKind.Sandbag, Map.Far, 35.0, -0.3, 4.0),     // the base's wall

                // --- back lane: over the second bridge, and up the steps (W2) --------
                new MapPiece(CoverKind.Trench, Map.Back, -37.0, 0.4, 8.0),     // a VC position in the forest
                new MapPiece(CoverKind.Berm, Map.Back, -28.0, 0.7, 7.0),       // fallen trees at the forest's edge
                new MapPiece(CoverKind.Trench, Map.Back, -21.0, 0.4, 6.0),     // dug in short of the second bridge's ramp
                new MapPiece(CoverKind.Sandbag, Map.Back, 10.6, -0.5, 4.0),    // the second bridgehead, on the first step
                new MapPiece(CoverKind.Sandbag, Map.Back, 16.0, -0.4, 3.0),    // a sluice gate behind the second step's dike
                new MapPiece(CoverKind.Crater, Map.Back, 24.5, 0.4, 3.0),      // shell holes in the third step
                new MapPiece(CoverKind.Berm, Map.Back, 29.8, 0.6, 3.0),        // the fourth step's dike
                new MapPiece(CoverKind.Sandbag, Map.Back, 35.8, 0.6, 4.0),     // the base's south wall
                new MapPiece(CoverKind.Trench, Map.Back, 41.0, 0.3, 6.0),      // the base's back trench
            },
            // The river between x -7 and 3, cut deep (its water 1.25 m under its banks); the paddies' four steps from 8 to
            // 34, each 1.1 m over the last, their edges winding; the base on the top step, 5.5 m up. Flatter ground than
            // the highlands', and a raised path along it where the firebase has its track. (The steps' feet at the near
            // lane: 9.7, 15.1, 20.6, 26.8 and 32.9; at the far lane 6.4, 15.6, 22.8, 26.6 and 32.9: the cover is on them.)
            GroundShape = new GroundParams
            {
                Seed = 20261015, FoldHeight = 0.5, TrackCut = -0.16, RutDepth = 0.04, TrackWander = 1.6, BermHeight = 0.7,
                FarRise = 3.2, FarRiseScale = 48,
                RiverX0 = -7, RiverX1 = 3, RiverWater = -1.25, RiverBed = -2.3,
                PaddyX0 = 8, PaddyX1 = 34, PaddyStep = 6.5, PaddyRise = 1.1, PaddyWave = 2.2, PaddyFlood = 0.16,
                DeckRise = 0.9, RampLength = 5,
                // (The bank behind the far lane, as the first is behind the near one; the paddies wide enough for the back lane.)
                MoreBermZ = new[] { -9.6 }, PaddyMidZ = -4.5, PaddyHalfZ = 22,
            },
            OldCraters = 4, OldCraterBandsZ = new[] { 13.5, -28.0 },
            Bridges = new[] { new Bridge { Lane = Map.Near, X0 = -9.4, X1 = 5.4 }, new Bridge { Lane = Map.Back, X0 = -11.2, X1 = 3.6 } },
            // Where its water is half a metre deep or more on each lane (`simcs ground ricefields`, 2026-10-16).
            Fords = new[] { new Ford { Lane = Map.Near, X0 = -5.75, X1 = 1.75 }, new Ford { Lane = Map.Far, X0 = -7.0, X1 = 0.5 }, new Ford { Lane = Map.Back, X0 = -7.75, X1 = -0.25 } },
            // The VC's forest, west of the river (WorldDressing.Forest plants it from the bank 6.5 m back): both lanes run
            // out of it.
            Forests = new[] { new Forest { X0 = -45, X1 = -14, Z0 = -26, Z1 = 12 } },
            // The paddies' steps, each 1.1 m, where their feet cross the lanes (GroundShape's PaddyX0, PaddyStep, PaddyWave).
            Steps = new[]
            {
                new Step { Lane = Map.Near, X = 9.69, Rise = 1.1 }, new Step { Lane = Map.Near, X = 15.11, Rise = 1.1 }, new Step { Lane = Map.Near, X = 20.64, Rise = 1.1 },
                new Step { Lane = Map.Near, X = 26.76, Rise = 1.1 }, new Step { Lane = Map.Near, X = 32.91, Rise = 1.1 },
                new Step { Lane = Map.Far, X = 6.36, Rise = 1.1 }, new Step { Lane = Map.Far, X = 15.63, Rise = 1.1 }, new Step { Lane = Map.Far, X = 22.82, Rise = 1.1 },
                new Step { Lane = Map.Far, X = 26.6, Rise = 1.1 }, new Step { Lane = Map.Far, X = 32.87, Rise = 1.1 },
                new Step { Lane = Map.Back, X = 7.30, Rise = 1.1 }, new Step { Lane = Map.Back, X = 13.69, Rise = 1.1 }, new Step { Lane = Map.Back, X = 20.95, Rise = 1.1 },
                new Step { Lane = Map.Back, X = 29.44, Rise = 1.1 }, new Step { Lane = Map.Back, X = 34.24, Rise = 1.1 },
            },
            Posts = new[]
            {
                // (Bamboo, its deck 6.03 m up, not the timber tower's 8.45: the base stands 5.5 m over the river already, and its
                // men stay clear of the minimap. tools/blender/lowlands_mesh.py.)
                new Post { Id = 0, Name = "WATCHTOWER", Side = Side.Us, X = -Map.TowerX, Z = Map.TowerZ, Height = 6.03, FootX = -Map.TowerX, FootZ = Map.TowerZ + 0.45, Lane = Map.Far },
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
            var steps = new Step[m.Steps.Length];
            for (int i = 0; i < steps.Length; i++) steps[i] = new Step { Lane = m.Steps[i].Lane, X = -m.Steps[i].X, Rise = -m.Steps[i].Rise };
            var fords = new Ford[m.Fords.Length];
            for (int i = 0; i < fords.Length; i++) fords[i] = new Ford { Lane = m.Fords[i].Lane, X0 = -m.Fords[i].X1, X1 = -m.Fords[i].X0 };
            var forests = new Forest[m.Forests.Length];
            for (int i = 0; i < forests.Length; i++) forests[i] = new Forest { X0 = -m.Forests[i].X1, X1 = -m.Forests[i].X0, Z0 = m.Forests[i].Z0, Z1 = m.Forests[i].Z1 };
            return new MapDef
            {
                Id = m.Id + "-mirrored", Name = m.Name, Theatre = m.Theatre, Pieces = pieces, GroundShape = m.GroundShape, Lanes = m.Lanes,
                CraterSeed = m.CraterSeed, OldCraters = m.OldCraters, OldCraterBandsZ = m.OldCraterBandsZ, Posts = posts, UsEast = !m.UsEast,
                BankMirrored = !m.BankMirrored, Bridges = bridges, Fords = fords, Steps = steps, Forests = forests,
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
