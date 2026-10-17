using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Part 2 rule (MatchOptions.Water): a river across the lanes, and the bridge over it (Rice Fields, PLAN §12.56).
    ///
    /// The owner, 2026-10-15: "crucial fight over colapsable bridge (can also swim but easy to get shot and killed)";
    /// his answers to the plan: the bridge carries the near lane, the other lane swims (both, once it is down), and
    /// engineers and sappers can put it back.
    ///
    ///   in the water   a man on a lane's ford (MapDef.Fords) and not on a standing bridge swims: at a third of a walk,
    ///                  upright, with nothing to fire or throw with and no cover, hit half as often again; his squad
    ///                  does not stop in it, but goes on to the far bank (or back to its own, falling back)
    ///   the bridge     carries its lane over the water while it stands. A rocket's, a mortar bomb's, a satchel
    ///                  charge's, a shell's or a bomb's burst near its deck takes from it (with Destruction, as from the
    ///                  tower); sappers and engineers put a charge on it when the enemy is on it and their own are not.
    ///                  At nothing it falls, and the men on it go into the water: some are killed by the fall
    ///   put back       sappers or engineers at its end on their own side work at it, while they are not pinned, and in
    ///                  twenty-five seconds it stands again, a rough crossing at half its strength
    ///
    /// A map without a river (the firebase) has no fords and no bridges: the rule does nothing there, to the bit. Its
    /// draws come from the grenades' stream (SimState.FragRng), as Destruction's do; its state is hashed only when it is on.
    /// </summary>
    public static class Water
    {
        /// <summary>The fords and bridges of the map, for a match with the rule.</summary>
        public static void Lay(SimState st, MapDef map)
        {
            foreach (var f in map.Fords) st.Fords.Add(new Ford { Lane = f.Lane, X0 = f.X0, X1 = f.X1 });
            for (int i = 0; i < map.Bridges.Length; i++)
            {
                var b = map.Bridges[i];
                st.Bridges.Add(new BridgeState { Id = i, Lane = b.Lane, X0 = b.X0, X1 = b.X1 });
            }
        }

        /// <summary>Does a standing bridge carry this lane at x?</summary>
        public static bool Carried(SimState st, int lane, double x)
        {
            for (int i = 0; i < st.Bridges.Count; i++)
            {
                var b = st.Bridges[i];
                if (!b.Fallen && b.Lane == lane && x >= b.X0 && x <= b.X1) return true;
            }
            return false;
        }

        /// <summary>Is x on this lane in the water (on its ford, and not carried over it)?</summary>
        public static bool Wet(SimState st, int lane, double x)
        {
            for (int i = 0; i < st.Fords.Count; i++)
            {
                var f = st.Fords[i];
                if (f.Lane == lane && x > f.X0 && x < f.X1) return !Carried(st, lane, x);
            }
            return false;
        }

        /// <summary>Is he in the water: swimming?</summary>
        public static bool In(SimState st, Man m)
        {
            if (!st.Water || st.Fords.Count == 0 || !m.Alive || m.High > 0 || m.Climb != 0 || m.Squad < 0) return false;
            return Wet(st, st.Squads[m.Squad].Lane, m.X);
        }

        /// <summary>Is any of these men in the water?</summary>
        public static bool AnyIn(SimState st, IReadOnlyList<Man> live)
        {
            if (!st.Water || st.Fords.Count == 0) return false;
            for (int i = 0; i < live.Count; i++) if (In(st, live[i])) return true;
            return false;
        }

        /// <summary>
        /// A squad with men in the water does not stop there: its anchor goes on through it to the far bank (or, falling
        /// back, to its own), at a swimmer's pace. Sappers or engineers at a fallen bridge's end stay there, working.
        /// </summary>
        public static void Press(SimState st, Squad sq, IReadOnlyList<Man> live, double dir)
        {
            if (!st.Water || st.Fords.Count == 0) return;
            var building = Building(st, sq, dir);
            if (building != null)
            {
                // At its end, on the bank, not in the water.
                double end = dir > 0 ? building.X0 : building.X1;
                if ((sq.AnchorX - end) * dir > -1.0) sq.AnchorX = end - dir * 1.0;
                sq.Halted = true;
                return;
            }
            if (!AnyIn(st, live) && !Wet(st, sq.Lane, sq.AnchorX)) return;
            double way = sq.Order == Order.Fallback ? -dir : dir;
            for (int i = 0; i < st.Fords.Count; i++)
            {
                var f = st.Fords[i];
                if (f.Lane != sq.Lane) continue;
                // The bank it is making for, and a little past it.
                double bank = way > 0 ? f.X1 + Tune.BridgeBank : f.X0 - Tune.BridgeBank;
                double lead = sq.AnchorX;
                for (int k = 0; k < live.Count; k++) if ((live[k].X - lead) * way > 0) lead = live[k].X;
                if ((bank - sq.AnchorX) * way <= 0) continue;
                // On at a swimmer's pace, kept ahead of its men so they have somewhere to go.
                double step = Tune.MarchSpeed * Tune.SwimPace * Tune.Dt;
                double to = Math.Max((sq.AnchorX - lead) * way, 0) < 1.5 ? sq.AnchorX + way * step : sq.AnchorX;
                sq.AnchorX = way > 0 ? Math.Min(bank, to) : Math.Max(bank, to);
                sq.Halted = false;
            }
        }

        /// <summary>
        /// Where a man in the water makes for, if his place is in it too: out, on the bank his squad is going to (the far
        /// one, or its own falling back), a pace up it. Otherwise his place.
        /// </summary>
        public static double Out(SimState st, Squad sq, double tx, double dir)
        {
            if (!Wet(st, sq.Lane, tx)) return tx;
            double way = sq.Order == Order.Fallback ? -dir : dir;
            for (int i = 0; i < st.Fords.Count; i++)
            {
                var f = st.Fords[i];
                if (f.Lane == sq.Lane && tx > f.X0 && tx < f.X1) return way > 0 ? f.X1 + 0.8 : f.X0 - 0.8;
            }
            return tx;
        }

        /// <summary>A fallen bridge this squad (sappers or engineers) is at the end of, on its own side, or null.</summary>
        public static BridgeState Building(SimState st, Squad sq, double dir)
        {
            if (sq.Card != "us-engineer" && sq.Card != "vc-sapper") return null;
            if (sq.Order == Order.Fallback) return null;
            for (int i = 0; i < st.Bridges.Count; i++)
            {
                var b = st.Bridges[i];
                if (!b.Fallen || b.Lane != sq.Lane) continue;
                double end = dir > 0 ? b.X0 : b.X1;
                double short_ = (end - sq.AnchorX) * dir;
                if (short_ >= -1.0 && short_ <= Tune.BridgeWorkReach) return b;
            }
            return null;
        }

        /// <summary>The rebuilding: sappers or engineers at a fallen bridge's end, not pinned, put it back in their time.</summary>
        public static void Tick(SimState st)
        {
            for (int i = 0; i < st.Bridges.Count; i++)
            {
                var b = st.Bridges[i];
                if (!b.Fallen) continue;
                Squad by = null;
                for (int s = 0; s < st.Squads.Count && by == null; s++)
                {
                    var sq = st.Squads[s];
                    if (sq.Post >= 0 || !Squads.AnyAlive(st, sq.Id)) continue;
                    if (Building(st, sq, Combat.Advance(st, sq.Side)) == b) by = sq;
                }
                if (by == null) { b.Builder = -1; continue; }
                if (b.Builder != by.Id)
                {
                    b.Builder = by.Id;
                    st.Events.Add(new SimEvent { Kind = EventKind.BridgeWork, Tick = st.Tick, Side = by.Side, Id = b.Id, Target = by.Id, X = (b.X0 + b.X1) * 0.5, Z = Tune.Lanes[b.Lane] });
                }
                // Under fire, the work stops: it goes on only while the squad's men are, on the whole, not pinned.
                double pin = 0;
                int n = 0;
                for (int k = 0; k < st.Men.Count; k++)
                {
                    var m = st.Men[k];
                    if (!m.Alive || m.Squad != by.Id) continue;
                    pin += m.Pin; n++;
                }
                if (n == 0 || pin / n >= Tune.PinDrop) continue;
                b.Work++;
                if (b.Work < (int)(Tune.BridgeRebuildSeconds * Tune.TickHz)) continue;
                b.Fallen = false;
                b.Work = 0;
                b.Builder = -1;
                b.Strength = Tune.BridgeStrength * Tune.BridgeRebuilt;
                st.Events.Add(new SimEvent { Kind = EventKind.BridgeRebuilt, Tick = st.Tick, Side = by.Side, Id = b.Id, Target = by.Id, X = (b.X0 + b.X1) * 0.5, Z = Tune.Lanes[b.Lane], Amount = b.Strength });
            }
        }

        /// <summary>A burst at x, z of this power at point blank (Destruction.Burst): what it does to a bridge near it.</summary>
        public static void Burst(SimState st, double x, double z, double high, double power)
        {
            if (!st.Water || power <= 0) return;
            for (int i = 0; i < st.Bridges.Count; i++)
            {
                var b = st.Bridges[i];
                if (b.Fallen) continue;
                // From the nearest point of its deck.
                double nx = Math.Max(b.X0, Math.Min(b.X1, x)), d = JsMath.Hypot(x - nx, z - Tune.Lanes[b.Lane]);
                if (d >= Tune.BridgeHitRadius) continue;
                b.Strength -= power * (1 - d / Tune.BridgeHitRadius);
                st.Events.Add(new SimEvent { Kind = EventKind.BridgeHit, Tick = st.Tick, Id = b.Id, X = x, Z = z, Amount = Math.Max(0, b.Strength) });
                if (b.Strength <= 0) Fall(st, b);
            }
        }

        /// <summary>A bomb (MatchOptions.Strike): a standing bridge whose deck it lands on, or within this many metres of, falls.</summary>
        public static void Flatten(SimState st, double x, double z, double reach)
        {
            if (!st.Water) return;
            for (int i = 0; i < st.Bridges.Count; i++)
            {
                var b = st.Bridges[i];
                if (b.Fallen) continue;
                double nx = Math.Max(b.X0, Math.Min(b.X1, x));
                if (JsMath.Hypot(x - nx, z - Tune.Lanes[b.Lane]) > reach) continue;
                b.Strength = 0;
                st.Events.Add(new SimEvent { Kind = EventKind.BridgeHit, Tick = st.Tick, Id = b.Id, X = x, Z = z, Amount = 0 });
                Fall(st, b);
            }
        }

        /// <summary>It falls: into the river, and the men on it with it.</summary>
        private static void Fall(SimState st, BridgeState b)
        {
            var rng = st.FragRng;
            b.Fallen = true;
            b.Strength = 0;
            b.Work = 0;
            b.Builder = -1;
            int seed = (int)(rng.Next() * 1000000);
            st.Events.Add(new SimEvent { Kind = EventKind.BridgeFell, Tick = st.Tick, Id = b.Id, X = (b.X0 + b.X1) * 0.5, Z = Tune.Lanes[b.Lane], Rounds = seed });
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                if (!m.Alive || m.High > 0 || m.Squad < 0 || st.Squads[m.Squad].Lane != b.Lane || m.X < b.X0 || m.X > b.X1) continue;
                if (rng.Next() < Tune.BridgeFallKill) { Combat.Kill(st, m); continue; }
                m.Pin = 1;
            }
        }

        /// <summary>
        /// Satchel: a standing bridge on his lane, within his reach, that the enemy is on and his own side is not: where on
        /// it to put the charge (the nearest of them), or null.
        /// </summary>
        public static (double x, double z)? Charge(SimState st, Man a)
        {
            if (!st.Water || !st.Destruction || st.Bridges.Count == 0 || a.Squad < 0) return null;
            int lane = st.Squads[a.Squad].Lane;
            for (int i = 0; i < st.Bridges.Count; i++)
            {
                var b = st.Bridges[i];
                if (b.Fallen || b.Lane != lane) continue;
                double z = Tune.Lanes[lane];
                if (JsMath.Hypot(Math.Max(b.X0, Math.Min(b.X1, a.X)) - a.X, z - a.Z) > Tune.SatchelReach) continue;
                Man at = null;
                bool ours = false;
                double best = double.PositiveInfinity;
                for (int k = 0; k < st.Men.Count; k++)
                {
                    var m = st.Men[k];
                    if (!m.Alive || m.High > 0 || m.Squad < 0 || st.Squads[m.Squad].Lane != lane || m.X < b.X0 || m.X > b.X1) continue;
                    if (m.Side == a.Side) { ours = true; break; }
                    double d = Math.Abs(m.X - a.X);
                    if (d < best) { best = d; at = m; }
                }
                if (ours || at == null || best > Tune.SatchelReach) continue;
                return (at.X, z);
            }
            return null;
        }
    }
}
