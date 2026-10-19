using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Part 2 rule (MatchOptions.Soldiering): men who fight more like men (PLAN §12.64).
    ///
    /// The owner, 2026-10-17: "AI soldiers us and enemy need a lot of work", and asked what he sees, all of: they ignore
    /// cover, they move badly, they fight badly, the commander is dumb. Measured first (simcs brain, aware, muddle,
    /// gunplay): one man in ten at rest in the open within a few paces of cover with a place free in it; a fifth of the
    /// time a man had a target and fired nothing, most of it men walking up in the open; nearly half the shots at a man a
    /// squad-mate had fired at in the last two seconds.
    ///
    ///   cover    a man of a squad in contact and holding, or one pinned whatever his squad is doing, takes a free place
    ///            in cover of his lane within SnapCover metres of him (Fieldcraft's places: nobody else makes for it), and
    ///            keeps it while it is so; pinned flat in the open, he crawls to it
    ///
    ///   rushes   a squad closing goes by halves, half of it down and firing while the other runs (Tactics' bounds); a
    ///            man whose half it is to go gets up and goes with rounds landing by him, unless he is pinned (Bearing
    ///            kept him down while any did, and closing squads stalled: of the closes that ended, two in three ended
    ///            for going nowhere)
    ///
    ///   the river a squad come to the water with the enemy covering the crossing waits on the bank and fights from
    ///            there, until he is pinned, gone or screened, or it has waited twenty seconds; going in under his eye, it
    ///            throws its smoke onto the far bank (and the Americans' commander lays its own over it). Going to ground,
    ///            or back, a squad does not choose cover over the water, nor goes in hand to hand across it, and holding,
    ///            its men on the bank do not wade over to their places: only closing does it cross (of the men who waded in
    ///            with their squad in a firefight, half were killed in it). A lane whose bridge is down, or has the enemy on
    ///            it, has the commander raise engineers or sappers for it first, to put it back or bring it down
    ///
    /// Its draws: none. Its state is the places men take, hashed with Fieldcraft's, and each squad's wait at a bank,
    /// hashed when it is on; off, the match is what it was to the bit.
    /// </summary>
    public static class Soldiering
    {
        /// <summary>
        /// Is his squad fighting an enemy: in a firefight with him, closing on him, going in, or holding what it ran back
        /// to (Senses' task), or, without that rule, near enough him. (Not the gap alone: a squad lying at the edge of it
        /// was in contact one tick and out of it the next, and its men went to cover and back on alternate ticks. Nor the
        /// moment of contact, when each man drops where he is and the squad then picks its cover: going for the cover by
        /// him then, he turned about twice.)
        /// </summary>
        private static bool Engaged(SimState st, Squad sq)
            => st.Senses
                ? sq.Threat >= 0 && (sq.Task == SquadTask.Firefight || sq.Task == SquadTask.Close || sq.Task == SquadTask.Assault || sq.Task == SquadTask.Regroup)
                : Senses.InContact(sq);

        /// <summary>Did he take this place himself (not his squad's place for him in its cover)?</summary>
        private static bool Own(Man m, Squad sq) => m.Place >= 0 && m.PlaceCover >= 0 && m.PlaceCover != sq.Target;

        /// <summary>
        /// Cover: where a man should go, or stay: a free place in cover of his lane within SnapCover of him, if his squad
        /// is fighting and holding where it is (or he is pinned flat), and it has no cover of its own that it is on its way
        /// to or that has a place for him. The place is his until neither is so: the squad's other men and the other
        /// squads do not take it (Fieldcraft's places); and with his squad still holding he stays in it till his place in
        /// its line is a few paces off (his pin gone, he did not get up and go back to stand where it had been). True if it
        /// has given him where to go.
        /// </summary>
        public static bool Cover(SimState st, Man m, Squad sq, ref double tx, ref double tz)
        {
            if (!st.Soldiering) return false;
            // His squad has a place for him in its own cover: he goes to that.
            if (m.Place >= 0 && m.PlaceCover == sq.Target) return false;
            bool own = Own(m, sq);
            if (own && m.PlaceCover >= st.Cover.Count) { m.Place = -1; m.PlaceCover = -1; return false; }
            // Another squad of his side is making for it now: its places are for its men, and he finds another. (Kept, they
            // left a squad sent up to a position on the firebase waiting in the open behind it.)
            if (own && Chosen(st, st.Cover[m.PlaceCover], sq)) { m.Place = -1; m.PlaceCover = -1; own = false; }
            double dir = Combat.Advance(st, m.Side), side = Fieldcraft.SideOf(m.Id);
            // (Once in it, a little less keeps him there than took him to it: no in and out as his pin rises and falls.)
            bool pinned = m.Pin >= (own ? Tune.SnapPin * 0.5 : Tune.SnapPin);
            var tc = sq.Target >= 0 && sq.Target < st.Cover.Count ? st.Cover[sq.Target] : null;
            // (Its cover is where his squad is going: he goes with it, and takes a place by him only once it is there with
            // none in it for him. Taking one on the way, he turned about twice: to it, and away to the squad's.)
            bool lies = tc == null || Fieldcraft.Arrived(sq, tc, dir);
            bool stays = sq.Order != Order.Fallback && (pinned || (Engaged(st, sq) && sq.Order == Order.Hold && lies));
            if (!stays)
            {
                if (!own) return false;
                var keep = Fieldcraft.PlaceOf(st.Cover[m.PlaceCover], m.Place, dir, side);
                // (Going on, he goes with it: kept in his place till his own in the line was a few paces off, he held the
                // squad back for its last man, and it stopped where it was.)
                if (sq.Order != Order.Hold || JsMath.Hypot(tx - keep.X, tz - keep.Z) > Tune.SnapLeave)
                {
                    m.Place = -1; m.PlaceCover = -1;
                    return false;
                }
            }
            else if (!own && !Nearest(st, m, sq, dir, side)) return false;
            var at = Fieldcraft.PlaceOf(st.Cover[m.PlaceCover], m.Place, dir, side);
            tx = at.X; tz = at.Z;
            return true;
        }

        /// <summary>
        /// The river: a squad come to the water with an enemy it knows of covering the crossing (in his weapon's reach of
        /// its middle, not pinned flat, no smoke between) waits on its bank, BankBack short of the water, and fights from
        /// there; it goes over when the crossing is clear (the enemy pinned, gone, or screened), or when it has waited
        /// BankWait; and going in under his eye, it throws its smoke onto the far bank between him and the water. (Wading
        /// in under fire, men were killed in it as often as they got across: 529 to 533 over 48 matches.)
        /// </summary>
        public static void Bank(SimState st, Squad sq, IReadOnlyList<Man> live, double dir)
        {
            if (!st.Water || st.Fords.Count == 0) return;
            if (sq.Order == Order.Fallback || Water.AnyIn(st, live)) { sq.Bank = 0; return; }
            double lz = Tune.Lanes[sq.Lane];
            for (int i = 0; i < st.Fords.Count; i++)
            {
                var f = st.Fords[i];
                if (f.Lane != sq.Lane) continue;
                double near = dir > 0 ? f.X0 : f.X1, far = dir > 0 ? f.X1 : f.X0, mid = (f.X0 + f.X1) * 0.5;
                // Its anchor at the water or about to be (the bridge standing, there is no water to cross).
                double to = (near - sq.AnchorX) * dir;
                if (to > Tune.BankBack + 1.0 || to < -1.0 || !Water.Wet(st, sq.Lane, mid)) continue;
                var eye = Covering(st, sq, mid, lz, near, dir);
                if (eye == null) { sq.Bank = 0; return; }
                if (sq.Bank >= Tune.BankWait)
                {
                    // Going in: its smoke between him and the water, if it has some.
                    if (st.SquadSmoke && sq.Smoke > 0)
                    {
                        sq.Smoke--;
                        Deck.Push(st, sq.Side, new Area
                        {
                            Kind = AreaKind.Smoke, X = far + dir * Tune.SquadSmokeRadius * 0.5, Z = lz + (eye.Z - lz) * 0.3,
                            Radius = Tune.SquadSmokeRadius, Ticks = Tune.SquadSmokeTicks, Next = 0, Power = 0,
                        });
                    }
                    return;
                }
                sq.Bank++;
                double stop = near - dir * Tune.BankBack;
                if ((sq.AnchorX - stop) * dir > 0) sq.AnchorX = stop;
                sq.Halted = true;
                return;
            }
            sq.Bank = 0;
        }

        /// <summary>
        /// The river, for a man: his squad holding (or come to the bank and waiting there), he does not go into the water to
        /// get to his place on its far side; he stops on this bank. (A squad that stopped closing with its first men in the
        /// water had the rest follow them in, to the places across it.)
        /// </summary>
        public static double Shore(SimState st, Man m, Squad sq, double tx, double dir)
        {
            if (!st.Water || st.Fords.Count == 0 || (sq.Order != Order.Hold && sq.Bank == 0)) return tx;
            for (int i = 0; i < st.Fords.Count; i++)
            {
                var f = st.Fords[i];
                if (f.Lane != sq.Lane || !Water.Wet(st, sq.Lane, (f.X0 + f.X1) * 0.5)) continue;
                // The bank he is on, and his place over the water from it.
                if (m.X <= f.X0 && tx > f.X0) return Math.Min(tx, f.X0 - 0.5);
                if (m.X >= f.X1 && tx < f.X1) return Math.Max(tx, f.X1 + 0.5);
            }
            return tx;
        }

        /// <summary>
        /// A man in the water whose place is out of it but within a pace of its edge goes a pace up the bank: close enough
        /// to his place to call himself there, he stopped with his feet still in the river, and stood in it.
        /// </summary>
        public static double UpBank(SimState st, Squad sq, double tx)
        {
            for (int i = 0; i < st.Fords.Count; i++)
            {
                var f = st.Fords[i];
                if (f.Lane != sq.Lane) continue;
                if (tx >= f.X1 && tx < f.X1 + 0.8) return f.X1 + 0.8;
                if (tx <= f.X0 && tx > f.X0 - 0.8) return f.X0 - 0.8;
            }
            return tx;
        }

        /// <summary>Is there water to wade in this lane between x0 and x1 (a river, its bridge down or none)?</summary>
        public static bool Across(SimState st, int lane, double x0, double x1)
        {
            if (!st.Water || st.Fords.Count == 0 || double.IsInfinity(x1) || double.IsNaN(x1)) return false;
            double lo = Math.Min(x0, x1), hi = Math.Max(x0, x1);
            for (int i = 0; i < st.Fords.Count; i++)
            {
                var f = st.Fords[i];
                if (f.Lane == lane && lo < f.X1 && hi > f.X0 && Water.Wet(st, lane, (f.X0 + f.X1) * 0.5)) return true;
            }
            return false;
        }

        /// <summary>
        /// For the commander's smoke: a crossing a squad of the side has waited at two seconds or more (Bank), and where
        /// to lay the screen (the middle of the water, its lane). False if there is none.
        /// </summary>
        public static bool Crossing(SimState st, Side side, out double x, out int lane)
        {
            x = 0; lane = -1;
            for (int i = 0; i < st.Squads.Count; i++)
            {
                var sq = st.Squads[i];
                if (sq.Side != side || sq.Bank < Tune.TickHz * 2 || sq.Bank >= Tune.BankWait || !Squads.AnyAlive(st, sq.Id)) continue;
                for (int k = 0; k < st.Fords.Count; k++)
                {
                    var f = st.Fords[k];
                    if (f.Lane != sq.Lane) continue;
                    x = (f.X0 + f.X1) * 0.5; lane = sq.Lane;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// For the commander: does this lane want engineers or sappers for its bridge, none of the side's being there: the
        /// bridge down (they put it back), or standing with an enemy the side has in sight on it (they bring it down)?
        /// </summary>
        public static bool Bridgework(SimState st, Side side, int lane)
        {
            if (!st.Water || st.Bridges.Count == 0) return false;
            for (int i = 0; i < st.Bridges.Count; i++)
            {
                var b = st.Bridges[i];
                if (b.Lane != lane) continue;
                bool want = b.Fallen;
                for (int k = 0; k < st.Men.Count && !want; k++)
                {
                    var e = st.Men[k];
                    want = e.Alive && e.Side != side && e.Seen && e.Squad >= 0 && st.Squads[e.Squad].Lane == lane && e.X > b.X0 && e.X < b.X1;
                }
                if (!want) continue;
                for (int q = 0; q < st.Squads.Count; q++)
                {
                    var sq = st.Squads[q];
                    if (sq.Side == side && sq.Lane == lane && (sq.Card == "us-engineer" || sq.Card == "vc-sapper") && Squads.AnyAlive(st, sq.Id)) return false;
                }
                return true;
            }
            return false;
        }

        /// <summary>An enemy the squad knows of, on the far side of the water's near edge, not pinned flat, with the
        /// crossing's middle in his weapon's reach and no smoke between: the nearest such, or null.</summary>
        private static Man Covering(SimState st, Squad sq, double mx, double mz, double near, double dir)
        {
            Man best = null;
            double bestD = double.PositiveInfinity;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var e = st.Men[i];
                if (!e.Alive || e.Side == sq.Side || e.Pin >= Tune.PinDrop || (e.X - near) * dir <= 0) continue;
                if (!Senses.Knows(st, sq, e.Squad)) continue;
                double d = JsMath.Hypot(e.X - mx, e.Z - mz), reach = Arms.Of(st, e).Range;
                if (d > reach || d >= bestD || Combat.SmokeBlocks(st, e.X, e.Z, mx, mz)) continue;
                best = e; bestD = d;
            }
            return best;
        }

        /// <summary>Is this cover where another squad of his side, with men alive, is going or is?</summary>
        private static bool Chosen(SimState st, Cover c, Squad sq)
        {
            for (int i = 0; i < st.Squads.Count; i++)
            {
                var o = st.Squads[i];
                if (o != sq && o.Side == sq.Side && o.Target == c.Id && Squads.AnyAlive(st, o.Id)) return true;
            }
            return false;
        }

        /// <summary>
        /// Rushes: a man of a squad closing on the enemy, its half his to go, gets up and goes with rounds landing by him
        /// unless they have him pinned (Bearing kept him down for as long as any landed near).
        /// </summary>
        public static bool Rises(SimState st, Squad sq, Man m)
            => st.Soldiering && sq.Task == SquadTask.Close && m.Pin < Tune.PinDrop && Tactics.Bounds(st, sq) && Tactics.Turn(st, sq, m);

        /// <summary>The nearest free place in cover of his lane within SnapCover, taken as his; false if there is none.</summary>
        private static bool Nearest(SimState st, Man m, Squad sq, double dir, double side)
        {
            double lz = Tune.Lanes[sq.Lane], bestD = Tune.SnapCover;
            int bc = -1, bk = -1;
            for (int i = 0; i < st.Cover.Count; i++)
            {
                var c = st.Cover[i];
                if (Math.Abs(c.Z - lz) > 4.5) continue;
                if (st.Strike && c.Wrecked) continue;
                // Not cover another squad of his side is making for or holding: its places are its men's. (Taking them, a
                // man left that squad's men standing behind its cover with no room in it.)
                if (Chosen(st, c, sq)) continue;
                // (A quick look before the places: is any of it near enough?)
                if (Math.Abs(c.X - m.X) > c.Length * 0.5 + bestD) continue;
                for (int k = 0; k < c.Capacity; k++)
                {
                    var at = Fieldcraft.PlaceOf(c, k, dir, side);
                    double ahead = (at.X - m.X) * dir;
                    // Not on toward the enemy, and only a few paces back: holding, his squad will be going on again (sent
                    // six metres back, he was still on his way when it did, and turned about).
                    if (ahead > 1.0 || ahead < -Tune.SnapBack) continue;
                    double d = JsMath.Hypot(at.X - m.X, at.Z - m.Z);
                    if (d >= bestD) continue;
                    if (Fieldcraft.Taken(st, c, m.Side, k, m)) continue;
                    bc = i; bk = k; bestD = d;
                }
            }
            if (bc < 0) return false;
            m.Place = bk; m.PlaceCover = bc;
            return true;
        }
    }
}
