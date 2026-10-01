using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Drill (Part 2, behind <see cref="MatchOptions.Drill"/>): a squad that
    /// behaves as if somebody were in charge of it.
    ///
    /// The baseline's policy is decided afresh every tick, from nothing, and it
    /// flickers. Measured over 2400 ticks (seed 3, ceiling against ceiling): a
    /// squad's order changed 583 times, one squad's 1,504 times, and a man
    /// reversed direction along the lane 597 times — five times a second —
    /// walking 49 m to gain 29. The worst of it: a broken squad falls back
    /// until it steadies, is then told to advance, is still broken, falls back
    /// again on the next tick, is steady, advances... for the rest of the
    /// match. In the 2D game that was sub-pixel jitter. In 3D every flip is a
    /// man turning round, and the owner's second playtest read it as it is:
    /// "they walk up and down randomly, no military brain".
    ///
    /// Three rules, none of them new tactics, all of them what the policy
    /// meant:
    ///   an order stands  for <see cref="Tune.DrillDwell"/> ticks once given,
    ///                    except the order to fall back, which is obeyed at once;
    ///   a broken squad   that has steadied holds where it is. It does not go
    ///                    forward again, and it breaks again only if it is
    ///                    beaten down again (mean pin past <see cref="Tune.PinStop"/>);
    ///   a man            keeps his own side of the file for life (the baseline
    ///                    deals sides by position among the living, so every
    ///                    casualty sent everyone behind it across the column),
    ///                    and does not shuffle for the last
    ///                    <see cref="Tune.DrillSlack"/> metres to his place.
    ///
    /// No random draws, no transcendentals. Its state (when each order was
    /// given, who has rallied) is hashed only when the rule is on: off, the
    /// match is the parity baseline to the bit.
    /// </summary>
    public static class Drill
    {
        /// <summary>The order the squad acts on, given what the policy wants this tick and what it was doing.</summary>
        public static Order Steady(SimState st, Squad sq, Order prev, Order wanted, IReadOnlyList<Man> live, int original)
        {
            bool broken = (double)live.Count / Math.Max(1, original) < Tune.SquadBreak;
            if (broken)
            {
                if (prev == Order.Fallback && wanted == Order.Advance)
                {
                    // Steadied: it stops, and stays.
                    sq.Rallied = true;
                    wanted = Order.Hold;
                }
                else if (sq.Rallied)
                {
                    if (wanted == Order.Fallback && MeanPin(live) >= Tune.PinStop) sq.Rallied = false;
                    else wanted = Order.Hold;
                }
            }
            if (wanted != prev && wanted != Order.Fallback && st.Tick - sq.OrderSince < Tune.DrillDwell) wanted = prev;
            if (wanted != prev) sq.OrderSince = st.Tick;
            return wanted;
        }

        private static double MeanPin(IReadOnlyList<Man> live)
        {
            double pin = 0;
            for (int i = 0; i < live.Count; i++) pin += live[i].Pin;
            return live.Count == 0 ? 0 : pin / live.Count;
        }

        /// <summary>
        /// The formation with each man on his own side: his place along the
        /// file is still his position among the living (the file closes up when
        /// a man falls), but which side of it he walks on, and how far out, is
        /// his position in the squad as it was raised.
        /// </summary>
        public static void SlotsFor(SimState st, Squad sq, IReadOnlyList<Man> live,
                                    double anchorX, double anchorZ, List<Slot> into)
        {
            into.Clear();
            double dir = Combat.Advance(sq.Side);
            for (int i = 0; i < live.Count; i++)
            {
                int own = 0;
                for (int j = 0; j < st.Men.Count; j++)
                    if (st.Men[j].Squad == sq.Id && st.Men[j].Id < live[i].Id) own++;
                into.Add(new Slot
                {
                    X = anchorX - dir * i * Tune.SlotGap,
                    Z = anchorZ + (own % 2 == 0 ? -1 : 1) * (0.7 + (own % 3) * 0.35),
                });
            }
        }
    }
}
