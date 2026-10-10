using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// The commander (Part 2, behind <see cref="MatchOptions.Commander"/>):
    /// what the computer's side does with its points, its cards and its levers.
    ///
    /// The owner, 2026-10-09: "focus on firing/gunplay mechanics and AI
    /// smartness", and then, of what that found: "then the computer
    /// commander". What it found: a side without a player raised a squad
    /// whenever it could afford one, the next on a list, into a lane drawn at
    /// random, and did nothing else. It never called artillery, smoke, an air
    /// strike or a medevac; never laid a trap or opened a tunnel; never pulled
    /// a lever. A player has all of those, and was playing against an enemy
    /// that had none of them. Its difficulty was the price of its squads.
    ///
    /// The commander thinks every two seconds, from what its squads know
    /// (<see cref="Senses"/>: the enemy squads they remember, and for anything
    /// that falls from the sky, those they have in sight now). Each thing it
    /// does has a reason a player would give:
    ///
    ///   the lane   a new squad goes to the lane where it is shortest of men
    ///              against the enemy it knows of there, not to one drawn
    ///   the squad  three times in four it raises what that lane wants: a
    ///              marksman where the enemy has a machine gun or a sniper and
    ///              it has none; something that bursts (a mortar, a rocket
    ///              team) where he is dug in, then men who go in after him
    ///              (engineers, sappers); a machine gun where its riflemen
    ///              have none. Otherwise riflemen, and one time in four the
    ///              next on its list, as before
    ///   the sky    (the Americans) an air strike on four of the enemy or more
    ///              in sight close together, artillery on five across a wider
    ///              ground, neither where it would fall on its own men; smoke
    ///              in front of a squad of its own pinned flat in the open; a
    ///              medevac when its will is failing and its men are down
    ///   the ground (the VC) a trap nine metres in front of an enemy squad
    ///              that is coming on, the best it can afford, no two close
    ///              together and one every twenty-five seconds at most; a
    ///              tunnel that brings a squad up behind a position of its own
    ///              that is being gone in on
    ///   levers     a position its men are in, in a lane where the enemy has
    ///              three more men than it has: hold. One they have sat in
    ///              five seconds with nobody known ahead of them: go, on up the
    ///              lane to find him. Else as the squad itself decides
    ///
    /// It pays what a player pays for a card, and for men what its squads
    /// cost it (a tunnel too). With fewer than six men on the map it buys men
    /// before anything else; with something in sight worth a strike and the
    /// strike ready, it saves its points for that and raises its next squad
    /// after it.
    ///
    /// Its draws (one time in four, a tie between lanes) are from its own
    /// stream. No transcendentals. It has no state of its own: off, the match
    /// is what it was to the bit.
    /// </summary>
    public static class Commander
    {
        /// <summary>What a commander sees of one lane: its own men there and the enemy it knows of.</summary>
        public struct LaneView
        {
            /// <summary>Its own men alive in the lane; of them, machine guns, snipers, and weapons that burst.</summary>
            public int Own, OwnGuns, OwnSnipers, OwnBursts, OwnClose;
            /// <summary>May it have another support team there: has it the riflemen for one more (four for each)?</summary>
            public bool Supports => Own - OwnGuns - OwnSnipers - OwnBursts >= Tune.RiflesToSupport * (OwnGuns + OwnSnipers + OwnBursts + 1);
            /// <summary>The enemy it knows of there; of them, in cover, and machine guns and snipers.</summary>
            public int Foe, FoeInCover, FoeGuns;
            /// <summary>Most of the enemy squads it knows of there are fighting from where they are, not coming on.</summary>
            public bool FoeHolding;
            /// <summary>How short of men it is there: the enemy's number less its own.</summary>
            public int Short => Foe - Own;
        }

        [ThreadStatic] private static bool[] _known, _seen;

        private static bool Gun(Weapon w) => w == Weapon.M60 || w == Weapon.Rpd;
        private static bool Bursts(Weapon w) => w == Weapon.M79 || w == Weapon.Rpg || w == Weapon.Mortar;

        /// <summary>
        /// Which enemy squads the side knows of (some squad of its remembers where it was) and which it has
        /// in sight now. Without <see cref="Senses"/>: the squads with a man the baseline calls seen.
        /// </summary>
        private static void Know(SimState st, Side side)
        {
            int n = st.Squads.Count;
            if (_known == null || _known.Length < n) { _known = new bool[Math.Max(64, n * 2)]; _seen = new bool[_known.Length]; }
            for (int i = 0; i < n; i++) { _known[i] = false; _seen[i] = false; }
            if (!st.Senses)
            {
                for (int i = 0; i < st.Men.Count; i++)
                    if (st.Men[i].Alive && st.Men[i].Side != side && st.Men[i].Seen) { _known[st.Men[i].Squad] = true; _seen[st.Men[i].Squad] = true; }
                return;
            }
            for (int a = 0; a < n; a++)
            {
                var sa = st.Squads[a];
                if (sa.Side != side || !Squads.AnyAlive(st, a)) continue;
                for (int b = 0; b < n; b++)
                {
                    if (st.Squads[b].Side == side) continue;
                    if (Senses.Remembers(st, sa, b)) _known[b] = true;
                    if (Senses.Knows(st, sa, b)) _seen[b] = true;
                }
            }
        }

        /// <summary>The lane as the side's commander sees it now.</summary>
        public static LaneView Look(SimState st, Side side, int lane)
        {
            Know(st, side);
            return See(st, side, lane);
        }

        private static LaneView See(SimState st, Side side, int lane)
        {
            var v = new LaneView();
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                if (!m.Alive || st.Squads[m.Squad].Lane != lane) continue;
                if (m.Side == side)
                {
                    v.Own++;
                    if (Gun(m.Weapon)) v.OwnGuns++;
                    if (m.Weapon == Weapon.Sniper) v.OwnSnipers++;
                    if (Bursts(m.Weapon)) v.OwnBursts++;
                    if (m.Weapon == Weapon.Smg) v.OwnClose++;
                }
                else if (_known[m.Squad])
                {
                    v.Foe++;
                    if (m.Cover >= 0) v.FoeInCover++;
                    if (Gun(m.Weapon) || m.Weapon == Weapon.Sniper) v.FoeGuns++;
                }
            }
            int holding = 0, coming = 0;
            for (int i = 0; i < st.Squads.Count; i++)
            {
                var q = st.Squads[i];
                if (q.Side == side || q.Lane != lane || !_known[i] || !Squads.AnyAlive(st, i)) continue;
                if (ComingOn(st, q)) coming++; else holding++;
            }
            v.FoeHolding = holding > 0 && holding >= coming;
            return v;
        }

        /// <summary>Is this squad coming on: marching, closing or going in (as far as its task or its order says)?</summary>
        private static bool ComingOn(SimState st, Squad q)
            => st.Senses ? q.Task == SquadTask.March || q.Task == SquadTask.Close || q.Task == SquadTask.Assault
                         : q.Order == Order.Advance || q.Order == Order.Bound;

        // --- men -------------------------------------------------------------------

        /// <summary>The lane a new squad goes to: where the side is shortest of men against the enemy it knows of; a tie, where it has fewest; still a tie, drawn.</summary>
        public static int Lane(SimState st, Side side)
        {
            Know(st, side);
            int best = -1, ties = 0;
            LaneView bv = default;
            for (int l = 0; l < Tune.Lanes.Length; l++)
            {
                var v = See(st, side, l);
                bool better = best < 0 || v.Short > bv.Short || (v.Short == bv.Short && v.Own < bv.Own);
                bool same = best >= 0 && v.Short == bv.Short && v.Own == bv.Own;
                if (better) { best = l; bv = v; ties = 1; }
                else if (same && st.CommandRng.Int(0, ++ties) == 0) { best = l; bv = v; }
            }
            return best;
        }

        /// <summary>The squad it raises for that lane: the card's id. What the lane wants, three times in four; else the next on its list.</summary>
        public static string Raise(SimState st, Side side, int lane)
        {
            string listed = Arms.Raised(st, side).Card;
            if (st.CommandRng.Next() < Tune.CommandWhim) return listed;
            var v = Look(st, side, lane);
            bool us = side == Side.Us;
            // (A support team goes where there are riflemen for it to support: four of them for each.)
            // A marksman for his guns.
            if (v.Supports && v.FoeGuns > 0 && v.OwnSnipers == 0) return us ? "us-sniper" : "vc-marksman";
            // Dug in: something that bursts, then men who go in after him.
            if (v.FoeHolding && v.FoeInCover >= Tune.DugInMen)
            {
                if (v.Supports && v.OwnBursts == 0) return us ? "us-mortar" : "vc-rpg";
                if (v.OwnClose == 0 && v.Own >= v.Foe) return us ? "us-engineer" : "vc-sapper";
            }
            // Riflemen with no machine gun of their own.
            if (v.Supports && v.OwnGuns == 0) return us ? "us-mg" : "vc-squad";
            return us ? "us-rifle" : st.CommandRng.Next() < 0.35 ? "vc-cell" : "vc-squad";
        }

        // --- the tick ----------------------------------------------------------------

        /// <summary>
        /// The side's thinking, on its tick (every two seconds, the two sides a second apart): its levers,
        /// then one call-in if there is a reason for one and the points. Returns true if it brought men on
        /// (a tunnel), so the match can note their strength.
        /// </summary>
        public static bool Think(SimState st, Side side, Plan plan, Rng rng)
        {
            if ((st.Tick + (int)side * Tune.CommandEvery / 2) % Tune.CommandEvery != 0) return false;
            Know(st, side);
            var lanes = new LaneView[Tune.Lanes.Length];
            for (int l = 0; l < lanes.Length; l++) lanes[l] = See(st, side, l);
            if (st.Fieldcraft && plan.UseCover) Levers(st, side, plan, lanes);
            if (st.Phase != Phase.Fight) return false;
            return side == Side.Us ? Sky(st, side, lanes, rng) : Ground(st, side, lanes, rng);
        }

        private static bool Affords(SimState st, Side side, Card card, double cost)
            => card != null && st.Cp[(int)side] >= cost && Deck.CooldownLeft(st, side, card) == 0;

        /// <summary>With few men on the map it buys men first: a call-in then only from points to spare.</summary>
        private static bool Spare(SimState st, Side side, double cost)
            => Match.AliveCount(st, side) >= Tune.MenFirst || st.Cp[(int)side] >= cost + st.MusterCost;

        /// <summary>
        /// Has it the men to spend points on a strike: as many alive as the enemy it knows of? Behind in men, a
        /// squad is worth more to it than a barrage (over forty matches a barrage killed one man and an air
        /// strike under two, for the price of a squad or more).
        /// </summary>
        private static bool Level(SimState st, Side side)
        {
            int foe = 0;
            for (int i = 0; i < st.Men.Count; i++) if (st.Men[i].Alive && st.Men[i].Side != side && _known[st.Men[i].Squad]) foe++;
            return Match.AliveCount(st, side) >= foe;
        }

        /// <summary>
        /// Is it saving its points: is there something worth a strike in sight, the strike ready, and not yet
        /// the points for it? Then it raises no squad this time (unless it is short of men), and has them next.
        /// </summary>
        public static bool Saving(SimState st, Side side)
        {
            if (side != Side.Us || st.Phase != Phase.Fight || Match.AliveCount(st, side) < Tune.MenFirst) return false;
            Know(st, side);
            if (!Level(st, side)) return false;
            var air = Deck.Find(side, "us-airstrike"); var arty = Deck.Find(side, "us-arty");
            double cp = st.Cp[(int)side];
            if (Deck.CooldownLeft(st, side, air) == 0 && cp < air.Cost && Cluster(st, side, Tune.StrikeRadius, Tune.StrikeClear, false, out _, out _) >= Tune.StrikeMen) return true;
            if (Deck.CooldownLeft(st, side, arty) == 0 && cp < arty.Cost && Cluster(st, side, Tune.BarrageRadius, Tune.BarrageClear, true, out _, out _) >= Tune.BarrageMen) return true;
            return false;
        }

        // --- levers ------------------------------------------------------------------

        /// <summary>
        /// Its standing order on each position its men are in. Hold, where the enemy it knows of in that lane
        /// has three men more than it has (and until he has no more than one). Go, where a squad of an
        /// attacking side has sat five seconds with nobody known ahead of it in the lane. Else the squad's own.
        /// </summary>
        private static void Levers(SimState st, Side side, Plan plan, LaneView[] lanes)
        {
            double dir = Combat.Advance(side);
            for (int ci = 0; ci < st.Cover.Count; ci++)
            {
                var c = st.Cover[ci];
                if (!Fieldcraft.IsPosition(c)) continue;
                int lane = -1;
                for (int l = 0; l < Tune.Lanes.Length; l++) if (Math.Abs(c.Z - Tune.Lanes[l]) <= 4.5) { lane = l; break; }
                if (lane < 0) continue;
                var was = Fieldcraft.LeverOf(c, side);
                Squad held = null;
                for (int i = 0; i < st.Men.Count && held == null; i++)
                {
                    var m = st.Men[i];
                    if (m.Alive && m.Side == side && m.Cover == c.Id) held = st.Squads[m.Squad];
                }
                var want = Lever.Auto;
                if (held != null)
                {
                    int shortBy = lanes[lane].Short;
                    if (shortBy >= Tune.HoldWhenShort || (was == Lever.Hold && shortBy > Tune.HoldUntilShort)) want = Lever.Hold;
                    else if (plan.Advance && held.Held >= Tune.GoAfter && held.PlayerOrder == null && !Ahead(st, side, c, dir)
                             && (st.Phase == Phase.Fight || side == Side.Vc)) want = Lever.Go;
                }
                if (want != was) Fieldcraft.SetLever(st, side, c.Id, want);
            }
        }

        /// <summary>Is there an enemy the side knows of ahead of this position, in its lane, within reach of anything it has?</summary>
        private static bool Ahead(SimState st, Side side, Cover c, double dir)
        {
            for (int i = 0; i < st.Men.Count; i++)
            {
                var e = st.Men[i];
                if (!e.Alive || e.Side == side || !_known[e.Squad]) continue;
                if (Math.Abs(e.Z - c.Z) > 8) continue;
                double ahead = (e.X - c.X) * dir;
                if (ahead > -Tune.GroundClear && ahead < Tune.GoClear) return true;
            }
            return false;
        }

        // --- the sky (the Americans) ------------------------------------------------

        /// <summary>
        /// The most enemy men in sight within <paramref name="radius"/> of one of them, with none of the side's
        /// own within <paramref name="clear"/> of him: how many, and the middle of them along the lane and
        /// their lane. <paramref name="still"/>: only men of squads that are not coming on (a barrage that
        /// takes nine seconds to fall is for an enemy who will be there).
        /// </summary>
        public static int Cluster(SimState st, Side side, double radius, double clear, bool still, out double x, out int lane)
        {
            int best = 0;
            x = 0; lane = 0;
            double r2 = radius * radius, c2 = clear * clear;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var b = st.Men[i];
                if (!b.Alive || b.Side == side || !_seen[b.Squad]) continue;
                if (still && ComingOn(st, st.Squads[b.Squad])) continue;
                int n = 0;
                double sum = 0;
                bool own = false;
                for (int j = 0; j < st.Men.Count && !own; j++)
                {
                    var o = st.Men[j];
                    if (!o.Alive) continue;
                    double dx = o.X - b.X, dz = o.Z - b.Z, d2 = dx * dx + dz * dz;
                    if (o.Side == side) { if (d2 < c2) own = true; }
                    else if (d2 < r2 && _seen[o.Squad]) { n++; sum += o.X; }
                }
                if (own || n <= best) continue;
                best = n; x = sum / n; lane = st.Squads[b.Squad].Lane;
            }
            return best;
        }

        private static bool Sky(SimState st, Side side, LaneView[] lanes, Rng rng)
        {
            // An air strike: short, tight and murderous. Four or more close together.
            var air = Deck.Find(side, "us-airstrike");
            bool level = Level(st, side);
            if (level && Affords(st, side, air, air.Cost) && Spare(st, side, air.Cost)
                && Cluster(st, side, Tune.StrikeRadius, Tune.StrikeClear, false, out double ax, out int al) >= Tune.StrikeMen)
            { Deck.Buy(st, side, air, al, rng, ax); return false; }
            // Artillery: a wide beaten zone for nine seconds. Five or more who are staying where they are.
            var arty = Deck.Find(side, "us-arty");
            if (level && Affords(st, side, arty, arty.Cost) && Spare(st, side, arty.Cost)
                && Cluster(st, side, Tune.BarrageRadius, Tune.BarrageClear, true, out double bx, out int bl) >= Tune.BarrageMen)
            { Deck.Buy(st, side, arty, bl, rng, bx); return false; }
            // A medevac: its will failing, its men down.
            var medevac = Deck.Find(side, "us-medevac");
            if (Affords(st, side, medevac, medevac.Cost) && st.Morale[(int)side] < Tune.MedevacBelow)
            {
                int down = 0;
                for (int i = 0; i < st.Men.Count; i++) if (st.Men[i].Alive && st.Men[i].Side == side && st.Men[i].Pin >= Tune.PinDrop) down++;
                if (down >= Tune.MedevacMen) { Deck.Buy(st, side, medevac, 0, rng); return false; }
            }
            // Smoke: in front of a squad of its own that is pinned flat in the open with the enemy in sight.
            var smoke = Deck.Find(side, "us-smoke");
            if (Affords(st, side, smoke, smoke.Cost) && Spare(st, side, smoke.Cost))
                for (int si = 0; si < st.Squads.Count; si++)
                {
                    var sq = st.Squads[si];
                    if (sq.Side != side || !sq.ThreatSeen || sq.ThreatGap < Tune.SmokeBeyond + 2 || sq.ThreatGap > Tune.Range) continue;
                    int men = 0, open = 0;
                    double pin = 0, lead = double.NegativeInfinity, dir = Combat.Advance(side);
                    for (int i = 0; i < st.Men.Count; i++)
                    {
                        var m = st.Men[i];
                        if (!m.Alive || m.Squad != si) continue;
                        men++; pin += m.Pin;
                        if (m.Cover < 0) open++;
                        lead = Math.Max(lead, m.X * dir);
                    }
                    if (men < 2 || pin / men < Tune.PinStop || open * 2 < men) continue;
                    double at = lead * dir + dir * sq.ThreatGap * 0.5;
                    if (Smoked(st, at, Tune.Lanes[sq.Lane])) continue;
                    Deck.Buy(st, side, smoke, sq.Lane, rng, at);
                    return false;
                }
            return false;
        }

        private static bool Smoked(SimState st, double x, double z)
        {
            for (int i = 0; i < st.Areas.Count; i++)
            {
                var a = st.Areas[i];
                if (a.Kind == AreaKind.Smoke && JsMath.Hypot(a.X - x, a.Z - z) < a.Radius + 6) return true;
            }
            return false;
        }

        // --- the ground (the VC) ----------------------------------------------------

        private static bool Ground(SimState st, Side side, LaneView[] lanes, Rng rng)
        {
            double dir = Combat.Advance(side), theirs = -dir;
            // A tunnel: a squad up behind a position of its own that is being gone in on, where it is short of men.
            var tunnel = Deck.Find(side, "vc-tunnel");
            if (Affords(st, side, tunnel, Math.Max(tunnel.Cost, st.MusterCost)) && Match.AliveCount(st, side) < Tune.ForceCap)
                for (int si = 0; si < st.Squads.Count; si++)
                {
                    var foe = st.Squads[si];
                    if (foe.Side == side || !_seen[si] || foe.Threat < 0 || !Squads.AnyAlive(st, si)) continue;
                    if (st.Senses && foe.Task != SquadTask.Assault && foe.Task != SquadTask.Close) continue;
                    var mine = st.Squads[foe.Threat];
                    if (mine.Side != side || mine.Target < 0 || lanes[mine.Lane].Short < Tune.TunnelWhenShort || !Squads.AnyAlive(st, mine.Id)) continue;
                    double at = Math.Max(-Tune.HalfLength + 2, Math.Min(Tune.HalfLength - 2, mine.AnchorX - dir * Tune.TunnelBehind));
                    // It costs the computer what its squads cost it, not a card's fifteen points.
                    double extra = Math.Max(0, st.MusterCost - tunnel.Cost);
                    if (!Deck.Buy(st, side, tunnel, mine.Lane, rng, at)) continue;
                    st.Cp[(int)side] -= extra;
                    return true;
                }
            // A trap in front of a squad that is coming on: the best it can afford, and no two close together.
            int live = 0;
            for (int i = 0; i < st.Areas.Count; i++) if (st.Areas[i].Kind == AreaKind.Trap && st.Areas[i].Side == side) live++;
            if (live >= Tune.TrapsLaid || st.Tick - st.TrapAt[(int)side] < Tune.TrapEvery) return false;
            for (int si = 0; si < st.Squads.Count; si++)
            {
                var foe = st.Squads[si];
                if (foe.Side == side || !_seen[si] || !ComingOn(st, foe)) continue;
                int men = 0;
                double lead = double.NegativeInfinity;
                for (int i = 0; i < st.Men.Count; i++)
                {
                    var m = st.Men[i];
                    if (!m.Alive || m.Squad != si) continue;
                    men++; lead = Math.Max(lead, m.X * theirs);
                }
                if (men == 0) continue;
                double at = lead * theirs + theirs * Tune.TrapAhead, z = Tune.Lanes[foe.Lane];
                if (Math.Abs(at) > Tune.HalfLength - 4) continue;
                bool near = false;
                for (int i = 0; i < st.Areas.Count && !near; i++)
                    near = st.Areas[i].Kind == AreaKind.Trap && st.Areas[i].Side == side && JsMath.Hypot(st.Areas[i].X - at, st.Areas[i].Z - z) < Tune.TrapApart;
                if (near) continue;
                foreach (string id in men >= 4 ? new[] { "vc-tripwire", "vc-punji", "vc-spider" } : new[] { "vc-punji", "vc-spider", "vc-tripwire" })
                {
                    var card = Deck.Find(side, id);
                    if (!Affords(st, side, card, card.Cost) || !Spare(st, side, card.Cost)) continue;
                    if (Deck.Buy(st, side, card, foe.Lane, rng, at)) st.TrapAt[(int)side] = st.Tick;
                    return false;
                }
            }
            return false;
        }
    }
}
