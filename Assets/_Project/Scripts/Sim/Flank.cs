using System;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Part 2 rule (MatchOptions.Flank): cover faces the enemy.
    ///
    /// Warfare 1944, which the owner had looked at for what this game was missing (2026-11-18): a flank attack does
    /// more harm, and a side that is flanked loses heart. Until now a man in cover here was as safe from a round that
    /// came from behind him as from one that came from the front (PLAN §12.64: "going round it gains nothing yet").
    ///
    ///   the front   a man in a position faces the enemy's end of his lane, the way his side fights. A round from
    ///               within sixty degrees of that has the whole of the position against it
    ///   the back    from behind, only what the kind of position gives all round: a crater is a bowl (0.7 of it),
    ///               a bunker has a roof (0.5), a trench a parados (0.4), sandbags their two short returns (0.3), a
    ///               berm hardly anything (0.15). Between the two, by the angle
    ///   the harm    a round from the flank at a man in cover finds him a tenth more often
    ///   the shock   a man killed from the flank costs his side half as much heart again as another, and the men of
    ///               his squad get their heads down: they have been taken in enfilade. A Flanked event (once a squad
    ///               in four seconds) says so, for the view
    ///
    /// And the flank as Warfare 1944 has it, by lanes: the bank between the lanes stops most fire across them, so a
    /// round from behind is rare, but a lane broken through is not.
    ///
    ///   outflanked  a man is outflanked while an enemy in the next lane is further back than he is (by OutflankBy):
    ///               the enemy is behind him. His position is worth a quarter less (fire comes from the side and the
    ///               back, and he looks over his shoulder). A lane is turned while its lead man is outflanked
    ///   the shock   the moment it happens the side loses heart, once (again only after OutflankRearm), and an
    ///               Outflanked event says so (Rounds 1; Rounds 0 when the lane is no longer turned)
    ///
    /// The game already goes round: a squad fighting one in the other lane comes up level with it and fires across
    /// (Fieldcraft's stand-off), a tunnel puts the VC behind a line, and fire control picks the man likeliest to be
    /// hit. Draws nothing; with the rule off nothing here runs, and the match is what it was to the bit.
    /// </summary>
    public static class Flank
    {
        /// <summary>Has man b an enemy in the next lane behind him (MatchOptions.Flank)? His position is worth less.</summary>
        public static bool Exposed(SimState st, Man b)
        {
            double deepest = st.FlankLine[(int)b.Side * Tune.MaxLanes + st.Squads[b.Squad].Lane];
            return deepest < double.PositiveInfinity && b.X * Combat.Advance(st, b.Side) > deepest + Tune.OutflankBy;
        }

        /// <summary>
        /// Every OutflankEvery ticks: for each side's lane, the enemy in the next lanes furthest into its ground (along
        /// the way it fights), and whether its lead man is past him, which is the lane turned; the shock of a lane
        /// being turned.
        /// </summary>
        public static void Step(SimState st)
        {
            if (st.Tick % Tune.OutflankEvery != 0) return;
            for (int s = 0; s < 2; s++)
            {
                var side = (Side)s;
                double dir = Combat.Advance(st, side);
                for (int lane = 0; lane < st.Lanes.Length; lane++)
                {
                    double lead = double.NegativeInfinity, deepest = double.PositiveInfinity;
                    for (int i = 0; i < st.Men.Count; i++)
                    {
                        var m = st.Men[i];
                        if (!m.Alive) continue;
                        int ml = st.Squads[m.Squad].Lane;
                        if (m.Side == side) { if (ml == lane) lead = Math.Max(lead, m.X * dir); }
                        else if (ml == lane - 1 || ml == lane + 1) deepest = Math.Min(deepest, m.X * dir);
                    }
                    int k = s * Tune.MaxLanes + lane;
                    st.FlankLine[k] = deepest;
                    bool turned = lead > deepest + Tune.OutflankBy;
                    bool was = st.Outflanked[k] != Sim.Senses.Never;
                    if (turned == was) continue;
                    if (turned)
                    {
                        st.Outflanked[k] = st.Tick;
                        double hit = 0;
                        if (st.Tick - st.OutflankShockAt[k] >= Tune.OutflankRearm)
                        {
                            st.OutflankShockAt[k] = st.Tick;
                            hit = Tune.OutflankMorale * st.MoraleScale * st.Will[s] * Doctrine.M(st, side, Effect.FlankMorale);
                            st.Morale[s] = Math.Max(0, st.Morale[s] - hit);
                            st.MoraleLostToGround[s] += hit;
                        }
                        st.Events.Add(new SimEvent { Kind = EventKind.Outflanked, Tick = st.Tick, Side = side, Id = lane, Amount = hit, Rounds = 1 });
                    }
                    else
                    {
                        st.Outflanked[k] = Sim.Senses.Never;
                        st.Events.Add(new SimEvent { Kind = EventKind.Outflanked, Tick = st.Tick, Side = side, Id = lane, Rounds = 0 });
                    }
                }
            }
        }

        /// <summary>How much of a position's protection is left against a round from shooter a at man b in cover c.</summary>
        public static double Facing(SimState st, Man a, Man b, Cover c)
        {
            double cos = Cos(st, a, b);
            if (cos >= Tune.FlankFront) return 1;
            double round = AllRound(c.Kind);
            if (cos <= Tune.FlankRear) return round;
            double t = (Tune.FlankFront - cos) / (Tune.FlankFront - Tune.FlankRear);
            return 1 - t * (1 - round);
        }

        /// <summary>Is a round from a at b, who is in cover, from his flank (outside his front)?</summary>
        public static bool From(SimState st, Man a, Man b) => Cos(st, a, b) < Tune.FlankFront;

        /// <summary>The cosine between where b faces (his enemy's end of the lane) and the way to a.</summary>
        public static double Cos(SimState st, Man a, Man b)
        {
            double dx = a.X - b.X, dz = a.Z - b.Z, d = Math.Sqrt(dx * dx + dz * dz);
            if (d < 1e-6) return 1;
            return Combat.Advance(st, b.Side) * dx / d;
        }

        /// <summary>What a kind of position keeps of its worth against a round from straight behind.</summary>
        public static double AllRound(CoverKind k) => k switch
        {
            CoverKind.Crater => Tune.FlankCrater,
            CoverKind.Bunker => Tune.FlankBunker,
            CoverKind.Trench => Tune.FlankTrench,
            CoverKind.Sandbag => Tune.FlankSandbag,
            _ => Tune.FlankBerm,
        };

        /// <summary>
        /// Man b, in cover, has been killed by a round from his flank: marked, so that the morale of his side takes
        /// more for him (Match.Step), and his squad gets its head down.
        /// </summary>
        public static void Killed(SimState st, Man a, Man b)
        {
            b.Flanked = true;
            var sq = st.Squads[b.Squad];
            for (int i = 0; i < st.Men.Count; i++)
            {
                var n = st.Men[i];
                if (n.Alive && n.Squad == b.Squad) Combat.ApplyPin(st, n, Tune.FlankPin);
            }
            if (st.Tick - sq.FlankedAt < Tune.FlankEvery) return;
            sq.FlankedAt = st.Tick;
            st.Events.Add(new SimEvent { Kind = EventKind.Flanked, Tick = st.Tick, Side = b.Side, Id = sq.Id, Target = a.Id, X = b.X, Z = b.Z });
        }
    }
}
