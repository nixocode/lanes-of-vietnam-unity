using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Part 2 rule (MatchOptions.Strike): the air strike, bombs and then napalm (PLAN §12.61).
    ///
    /// The owner, 2026-10-17: "lets make the airstrike more expensive and OP, like it should destory everything in its
    /// path", and, asked what it drops, "bombs then napalm". It costs StrikeCost, not 40, and waits StrikeCooldown.
    ///
    ///   the bombs    the jet's stick (Inbound.Stick), each a 500 lb bomb: a man within BombKill of one is killed, in
    ///                cover or not; out to BombReach he is put flat and may be killed (less in cover); a watchtower
    ///                within BombTower of one falls, and a bridge it lands on or within BombBridge of; a position
    ///                within BombWreck of one is wrecked for the match: what is left of it is a bomb crater, cover of
    ///                the crater's kind
    ///   the napalm   the jet's second pass, NapalmDelay after its last bomb: fire along the stick's line, NapalmHalf
    ///                either way along the lane and NapalmWide across it, for NapalmTicks. A man in it burns (NapalmKill
    ///                a tick); nobody walks into it (a squad holds short of it, a man's place is short of it) and a man
    ///                caught in it runs out of it the near way
    ///
    /// The computer calls it when it is worth StrikeCost (Worth): StrikeMen of the enemy along a stick's line, or
    /// StrikeMenAt with a bridge or a watchtower of his on it; none of its own within StrikeClear.
    ///
    /// And NAPALM, on the forest (the owner, 2026-10-17: "Add the napalm to the plan which burns the forest and changes
    /// the map"; a last resort at first, then, 2026-10-18, "available to buy at 80 points, same as airstrike"): a card of
    /// the Americans' on a map with a forest (MapDef.Forests). The jet comes in with it and the forest burns ForestRun
    /// along the lanes and the whole of its depth (AreaKind.Firestorm), for ForestTicks: whoever is in it burns, the
    /// positions in it are burned out, and the ground it burned hides nobody for the rest of the match (SimState.Burnt;
    /// Senses.Sight). The computer plays it on ForestMenAnyway it knows are in the forest, or ForestMen when it is losing
    /// (LastResort).
    ///
    /// It needs Inbound (the game's): without it the strike is as it was. Its draws are the match's stream's, made only
    /// when it is on; what it changes is hashed only when it is on (a wrecked position's kind and quality are its cover's).
    /// </summary>
    public static class Strike
    {
        /// <summary>Is this area the air strike's jet, and the rule on?</summary>
        public static bool Jet(SimState st, Area a) => st.Strike && st.Inbound && a.Kind == AreaKind.Barrage && a.Call == Inbound.Jet;

        /// <summary>One of the jet's bombs, bursting at bx, bz.</summary>
        public static void Bomb(SimState st, Area a, double bx, double bz, Rng rng)
        {
            st.Events.Add(new SimEvent { Kind = EventKind.Shell, Tick = st.Tick, Side = a.Side, Id = a.Id, X = bx, Z = bz, Rounds = Inbound.Jet });
            // What stands near it comes down: the tower, the bridge.
            if (st.Destruction) Destruction.Flatten(st, bx, bz, Tune.BombTower);
            if (st.Water) Water.Flatten(st, bx, bz, Tune.BombBridge);
            for (int j = 0; j < st.Men.Count; j++)
            {
                var m = st.Men[j];
                if (!m.Alive) continue;
                double d = JsMath.Hypot(m.X - bx, m.Z - bz);
                if (d > Tune.BombReach) continue;
                // (A man up a post is on what the bomb brings down, or above what it reaches.)
                if (st.Vantage && !Vantage.Reaches(0, m) && d > Tune.BombTower) continue;
                if (d <= Tune.BombKill) { Combat.Kill(st, m); continue; }
                double f = 1 - (d - Tune.BombKill) / (Tune.BombReach - Tune.BombKill);
                // (Flat, however steady he is: twice what would pin the greenest man.)
                Combat.ApplyPin(st, m, 2.0);
                double lethal = Tune.BombLethal * f * (m.Cover >= 0 ? Tune.BombCover : 1);
                if (rng.Next() < lethal) Combat.Kill(st, m);
            }
            // The positions it lands among are gone: a crater where each was.
            for (int i = 0; i < st.Cover.Count; i++)
            {
                var c = st.Cover[i];
                if (c.Wrecked) continue;
                double nx = Math.Max(c.X - c.Length * 0.5, Math.Min(c.X + c.Length * 0.5, bx));
                if (JsMath.Hypot(nx - bx, c.Z - bz) > Tune.BombWreck) continue;
                Wreck(st, c, a.Side);
            }
        }

        /// <summary>A position a bomb has landed in: wrecked, a crater where it was.</summary>
        public static void Wreck(SimState st, Cover c, Side by)
        {
            var spec = CoverSpec.For(CoverKind.Crater);
            c.Wrecked = true;
            c.Kind = CoverKind.Crater;
            c.Quality = (spec.qLo + spec.qHi) * 0.5;
            c.Capacity = Math.Max(2, (int)JsMath.Round(c.Length / spec.metresPerMan));
            c.Breached = 0;
            st.Events.Add(new SimEvent { Kind = EventKind.Wrecked, Tick = st.Tick, Side = by, Id = c.Id, X = c.X, Z = c.Z });
        }

        /// <summary>The jet's pass is over (its area is ending): its second pass lays the napalm along the stick's line.</summary>
        public static void Lay(SimState st, Area jet)
        {
            // Along the line its bombs fell (their middle across the lane), lit NapalmDelay on.
            double z = 0;
            for (int k = 0; k < Tune.StickBombs; k++) { Inbound.Stick(st, jet.Side, jet.Id, jet.X, jet.Z, k, out _, out double bz); z += bz; }
            z /= Tune.StickBombs;
            Deck.Push(st, jet.Side, new Area { Kind = AreaKind.Napalm, X = jet.X, Z = z, Radius = Tune.NapalmHalf, Ticks = Tune.NapalmDelay + Tune.NapalmTicks, Next = Tune.NapalmDelay, Power = Tune.NapalmKill });
        }

        /// <summary>Is the napalm alight?</summary>
        public static bool Lit(Area a) => a.Kind == AreaKind.Napalm && a.Next <= 0;

        /// <summary>
        /// Where this fire is (lit or coming): the strike's napalm, along its line on its lane; or napalm on the forest,
        /// along the lanes and across the whole of the forest it is in. False for any other area.
        /// </summary>
        public static bool Band(SimState st, Area a, out double x0, out double x1, out double z0, out double z1)
        {
            x0 = x1 = z0 = z1 = 0;
            if (a.Kind == AreaKind.Napalm)
            {
                x0 = a.X - a.Radius; x1 = a.X + a.Radius; z0 = a.Z - Tune.NapalmWide * 0.5; z1 = a.Z + Tune.NapalmWide * 0.5;
                return true;
            }
            if (a.Kind != AreaKind.Firestorm || a.Wait > 0) return false;
            var f = ForestAt(st, a.X, a.Z);
            if (f == null) return false;
            x0 = Math.Max(f.X0, a.X - a.Radius); x1 = Math.Min(f.X1, a.X + a.Radius); z0 = f.Z0; z1 = f.Z1;
            return x1 > x0;
        }

        /// <summary>Is (x, z) inside this fire (lit or about to be)?</summary>
        public static bool Inside(SimState st, Area a, double x, double z)
            => Band(st, a, out double x0, out double x1, out double z0, out double z1) && x >= x0 && x <= x1 && z >= z0 && z <= z1;

        /// <summary>The fire burning (or about to be lit) at (x, z), or null.</summary>
        public static Area Burning(SimState st, double x, double z)
        {
            if (!st.Strike) return null;
            for (int i = 0; i < st.Areas.Count; i++) if (Inside(st, st.Areas[i], x, z)) return st.Areas[i];
            return null;
        }

        // --- napalm on the forest ------------------------------------------------------------

        /// <summary>The forest (x, z) is in, or null.</summary>
        public static Forest ForestAt(SimState st, double x, double z)
        {
            for (int i = 0; i < st.Forests.Count; i++)
            {
                var f = st.Forests[i];
                if (x >= f.X0 && x <= f.X1 && z >= f.Z0 && z <= f.Z1) return f;
            }
            return null;
        }

        /// <summary>Has the forest burned at (x, z)? Then it hides nobody.</summary>
        public static bool InBurnt(SimState st, double x, double z)
        {
            for (int i = 0; i < st.Burnt.Count; i++)
            {
                var b = st.Burnt[i];
                if (x >= b.X0 && x <= b.X1 && z >= b.Z0 && z <= b.Z1) return true;
            }
            return false;
        }

        /// <summary>
        /// Are the Americans losing (the computer's judgement, for its napalm on the forest)? In the fight (not its
        /// opening), their will under LastResortWill; or under LastResortHeld with the enemy ahead in every lane by
        /// LastResortAhead of the field or more. (Ahead in every lane alone, they were "losing" at the start of most
        /// matches, before anyone had met: whoever has no men in a lane yet is behind in it.)
        /// </summary>
        public static bool LastResort(SimState st)
        {
            if (!st.Strike || st.Phase != Phase.Fight) return false;
            double will = st.Morale[(int)Side.Us];
            if (will < Tune.LastResortWill) return true;
            if (will >= Tune.LastResortHeld) return false;
            var lanes = Match.LaneControl(st);
            for (int i = 0; i < lanes.Length; i++) if (lanes[i].side != Side.Vc || lanes[i].margin < Tune.LastResortAhead) return false;
            return lanes.Length > 0;
        }

        /// <summary>
        /// One tick of napalm on the forest, once it is in: on its first, the forest catches all along its run (what burns
        /// is burned for good, and the positions in it burned out); then whoever is in it burns.
        /// </summary>
        public static void Firestorm(SimState st, Area a, Rng rng)
        {
            if (!Band(st, a, out double x0, out double x1, out double z0, out double z1)) return;
            if (a.Next > 0)
            {
                a.Next--;
                if (a.Next > 0) return;
                st.Burnt.Add(new Burn { X0 = x0, X1 = x1, Z0 = z0, Z1 = z1, Lit = st.Tick });
                st.Events.Add(new SimEvent { Kind = EventKind.NapalmLit, Tick = st.Tick, Side = a.Side, Id = a.Id, X = a.X, Z = a.Z, Amount = a.Radius, Rounds = Inbound.Napalm });
                for (int i = 0; i < st.Cover.Count; i++)
                {
                    var c = st.Cover[i];
                    if (!c.Wrecked && c.X + c.Length * 0.5 >= x0 && c.X - c.Length * 0.5 <= x1 && c.Z >= z0 && c.Z <= z1) Wreck(st, c, a.Side);
                }
            }
            for (int j = 0; j < st.Men.Count; j++)
            {
                var m = st.Men[j];
                if (!m.Alive || m.X < x0 || m.X > x1 || m.Z < z0 || m.Z > z1) continue;
                if (st.Vantage && m.High > Tune.NapalmHigh) continue;
                // (Not pinned: a man in fire runs from it, Short. Put flat by it, he lay in it and burned.)
                if (rng.Next() >= a.Power) continue;
                st.Events.Add(new SimEvent { Kind = EventKind.Burned, Tick = st.Tick, Side = a.Side, Id = m.Id, X = m.X, Z = m.Z, Target = a.Id });
                Combat.Kill(st, m);
            }
        }

        /// <summary>
        /// The computer's napalm on the forest: the enemy it knows of (squads <paramref name="seen"/>) in a forest,
        /// ForestMenAnyway of them or more within the run of where it would lay it (their middle), or ForestMen when it is
        /// losing; none of its own there. The count (0 if nothing), where, which lane.
        /// </summary>
        public static int ForestCrowd(SimState st, Side side, bool[] seen, out double x, out int lane)
        {
            int need = LastResort(st) ? Tune.ForestMen : Tune.ForestMenAnyway;
            int best = 0;
            x = 0; lane = 0;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var b = st.Men[i];
                if (!b.Alive || b.Side == side || b.Squad < 0 || !seen[b.Squad]) continue;
                var f = ForestAt(st, b.X, b.Z);
                if (f == null) continue;
                int n = 0;
                double sum = 0;
                bool own = false;
                for (int j = 0; j < st.Men.Count && !own; j++)
                {
                    var o = st.Men[j];
                    if (!o.Alive || o.Squad < 0 || o.Z < f.Z0 || o.Z > f.Z1 || Math.Abs(o.X - b.X) > Tune.ForestRun * 0.5) continue;
                    if (o.Side == side) own = true;
                    else if (seen[o.Squad] && o.X >= f.X0 && o.X <= f.X1) { n++; sum += o.X; }
                }
                if (own || n <= best) continue;
                best = n; x = sum / n; lane = st.Squads[b.Squad].Lane;
            }
            return best >= need ? best : 0;
        }

        /// <summary>One tick of a napalm area: lit when its jet comes round; then whoever is in it burns.</summary>
        public static void Tick(SimState st, Area a, Rng rng)
        {
            if (a.Next > 0)
            {
                a.Next--;
                if (a.Next == 0) st.Events.Add(new SimEvent { Kind = EventKind.NapalmLit, Tick = st.Tick, Side = a.Side, Id = a.Id, X = a.X, Z = a.Z, Amount = a.Radius });
                return;
            }
            for (int j = 0; j < st.Men.Count; j++)
            {
                var m = st.Men[j];
                if (!m.Alive || !Inside(st, a, m.X, m.Z)) continue;
                if (st.Vantage && m.High > Tune.NapalmHigh) continue;
                // (Not pinned: a man in fire runs from it, Short. Put flat by it, he lay in it and burned.)
                if (rng.Next() >= a.Power) continue;
                st.Events.Add(new SimEvent { Kind = EventKind.Burned, Tick = st.Tick, Side = a.Side, Id = m.Id, X = m.X, Z = m.Z, Target = a.Id });
                Combat.Kill(st, m);
            }
        }

        /// <summary>
        /// A squad does not go into the fire: its anchor is held short of any napalm on its lane ahead of it (lit or about
        /// to be), on its own side of it. (Falling back, short of fire behind it.)
        /// </summary>
        public static void Hold(SimState st, Squad sq, IReadOnlyList<Man> live, double dir)
        {
            if (!st.Strike) return;
            double way = sq.Order == Order.Fallback ? -dir : dir;
            for (int i = 0; i < st.Areas.Count; i++)
            {
                var a = st.Areas[i];
                double lz = Tune.Lanes[sq.Lane];
                if (!Band(st, a, out double x0, out double x1, out double z0, out double z1) || lz < z0 - 1 || lz > z1 + 1) continue;
                double ax = (x0 + x1) * 0.5, half = (x1 - x0) * 0.5;
                // Its near edge, as the squad comes to it; the squad's men are this side of it (the middle of them).
                double near = ax - way * (half + Tune.NapalmShort);
                double mid = 0;
                for (int k = 0; k < live.Count; k++) mid += live[k].X;
                mid = live.Count > 0 ? mid / live.Count : sq.AnchorX;
                if ((mid - ax) * way > half) continue;          // already past it
                // (Not in a river: on its bank, this side of it.)
                near = Dry(st, sq.Lane, near, mid);
                if ((sq.AnchorX - near) * way > 0) { sq.AnchorX = near; sq.Halted = true; }
            }
        }

        /// <summary>
        /// Where a man makes for, with fire about: out of it the near way if he is in it (the nearer edge, along the lane),
        /// short of it if his place is in it or beyond it. Otherwise his place.
        /// </summary>
        public static double Short(SimState st, Man m, double tx, double dir)
        {
            for (int i = 0; i < st.Areas.Count; i++)
            {
                var a = st.Areas[i];
                if (!Band(st, a, out double x0, out double x1, out double z0, out double z1) || m.Z < z0 - 1 || m.Z > z1 + 1) continue;
                double lo = x0 - Tune.NapalmShort, hi = x1 + Tune.NapalmShort;
                if (m.X > lo && m.X < hi) return m.X - lo < hi - m.X ? lo : hi;
                if (m.X <= lo && tx > lo) tx = lo;
                else if (m.X >= hi && tx < hi) tx = hi;
                else continue;
                // (Short of it, but not standing in a river: on its bank, his side of it.)
                if (m.Squad >= 0 && m.Squad < st.Squads.Count) tx = Dry(st, st.Squads[m.Squad].Lane, tx, m.X);
            }
            return tx;
        }

        /// <summary>x on this lane if it is dry; if it is in the river (MatchOptions.Water), its bank on the side of <paramref name="from"/>.</summary>
        private static double Dry(SimState st, int lane, double x, double from)
        {
            if (!st.Water || !Water.Wet(st, lane, x)) return x;
            for (int i = 0; i < st.Fords.Count; i++)
            {
                var f = st.Fords[i];
                if (f.Lane == lane && x > f.X0 && x < f.X1) return from <= x ? f.X0 - 0.8 : f.X1 + 0.8;
            }
            return x;
        }

        /// <summary>
        /// The computer's air strike: is there something worth StrikeCost along a stick's line, and where? Men of the
        /// enemy it has seen (squads <paramref name="seen"/>) within StrikeRadius of one of them along the lane (as the
        /// stick and the fire run): StrikeMen of them, or StrikeMenAt with a bridge or a watchtower on the line; none of
        /// its own within StrikeClear. The most over what it needs wins. The count (0 if nothing), where to aim (their
        /// middle) and on which lane. (With <paramref name="least"/>, that many will do: a prospect worth saving for.)
        /// </summary>
        public static int Worth(SimState st, Side side, bool[] seen, out double x, out int lane, int least = 0)
        {
            int bestN = 0, bestOver = int.MinValue;
            x = 0; lane = 0;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var b = st.Men[i];
                if (!b.Alive || b.Side == side || b.Squad < 0 || !seen[b.Squad]) continue;
                int n = 0;
                double sum = 0;
                bool own = false;
                for (int j = 0; j < st.Men.Count && !own; j++)
                {
                    var o = st.Men[j];
                    if (!o.Alive || o.Squad < 0) continue;
                    double dx = Math.Abs(o.X - b.X), dz = Math.Abs(o.Z - b.Z);
                    if (o.Side == side) { if (dx < Tune.StrikeClear && dz < Tune.NapalmWide + 2) own = true; }
                    else if (dx < Tune.StrikeRadius && dz < Tune.NapalmWide * 0.5 + 1 && seen[o.Squad]) { n++; sum += o.X; }
                }
                if (own || n == 0) continue;
                int bl = st.Squads[b.Squad].Lane;
                double at = sum / n;
                int need = least > 0 ? least : Structure(st, at, Tune.Lanes[bl]) ? Tune.StrikeMenAt : Tune.StrikeMen;
                if (n < need || n - need <= bestOver) continue;
                bestOver = n - need; bestN = n; x = at; lane = bl;
            }
            return bestN;
        }

        /// <summary>Does a standing bridge or watchtower lie on a stick's line aimed at x on the lane at z?</summary>
        private static bool Structure(SimState st, double x, double z)
        {
            double half = (Tune.StickBombs - 1) * 0.5 * Tune.StickStep + Tune.BombBridge;
            for (int i = 0; i < st.Bridges.Count; i++)
            {
                var b = st.Bridges[i];
                if (!b.Fallen && Math.Abs(Tune.Lanes[b.Lane] - z) < 3 && b.X1 > x - half && b.X0 < x + half) return true;
            }
            for (int i = 0; i < st.Posts.Count; i++)
            {
                var p = st.Posts[i];
                if (!p.Fallen && Math.Abs(p.Z - z) < Tune.BombTower + 2 && Math.Abs(p.X - x) < half) return true;
            }
            return false;
        }
    }
}
