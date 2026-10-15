using System;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Part 2 rule (MatchOptions.Inbound): a call-in takes time to arrive.
    ///
    /// Without it a barrage lands in the tick its card is played: the player
    /// presses, and the ground goes up. Nothing came from anywhere. With it:
    ///
    ///   the guns      3.5 s to lay and fire. A second before the rest, one
    ///                 round lands at the aim point to see where they fall
    ///                 (the ranging round: it hurts nobody), and the men
    ///                 under it have that second
    ///   the jet       5 s to come round onto the target, and its bombs in
    ///                 the one pass: a stick of eight along its line in
    ///                 under a second (they were four seconds of bursts)
    ///   smoke         2.5 s: the shells are quicker laid than the guns' HE
    ///   the medevac   8 s for the helicopter; the line is steadied when it
    ///                 is in, not when it is called
    ///
    /// The call is announced (EventKind.CallIn) so that it is seen and heard
    /// coming. Until it arrives it is not an area: it hits nobody, and smoke
    /// that has not landed hides nobody. The computer's commander plays its
    /// cards through the same door, and waits as long. Traps are laid, not
    /// called, and a squad's own smoke grenade is thrown: neither waits.
    ///
    /// No random draws. Its state (how long each has still to wait, what it
    /// is) is hashed only when the rule is on: off, the match is what it was
    /// to the bit.
    /// </summary>
    public static class Inbound
    {
        /// <summary>What was called: an area's <see cref="Area.Call"/> and a CallIn event's Rounds. Ranging is the event alone.</summary>
        public const int Guns = 1, Jet = 2, SmokeShells = 3, Dustoff = 4, Ranging = 9;
        /// <summary>MatchOptions.Shootdown: a medevac hit on its way in, coming down where it will burst.</summary>
        public const int Crash = 5;

        public static int Ticks(int call)
        {
            switch (call)
            {
                case Guns: return Tune.GunsWait;
                case Jet: return Tune.JetWait;
                case SmokeShells: return Tune.SmokeWait;
                case Dustoff: return Tune.DustoffWait;
                case Crash: return Tune.CrashTicks;
                default: return 0;
            }
        }

        /// <summary>
        /// Where bomb k of a jet's stick lands (the first is 0). The jet comes from behind its own side and
        /// its bombs walk on away from it, a step each, a little to one side and the other of its line. No
        /// random draws: whoever watches it come can know where each will fall before it does.
        /// </summary>
        public static void Stick(Side side, int id, double ax, double az, int k, out double x, out double z)
        {
            double dir = side == Side.Us ? 1 : -1;
            x = ax + dir * (k - (Tune.StickBombs - 1) / 2.0) * Tune.StickStep;
            z = az + ((k * 7 + Math.Abs(id) * 3) % 5 - 2) * Tune.StickSpread;
        }

        /// <summary>One tick of a call on its way (area i of the list). When it is in, a medevac does what it does and is gone; the rest become what they are next tick.</summary>
        public static void Wait(SimState st, Area a, int i)
        {
            a.Wait--;
            if (a.Call == Guns && a.Wait == Tune.RangingAhead)
                st.Events.Add(new SimEvent { Kind = EventKind.CallIn, Tick = st.Tick, Side = a.Side, Id = a.Id, X = a.X, Z = a.Z, Amount = a.Wait, Rounds = Ranging });
            // Shootdown: low and slow as it comes in, the medevac is in reach of the enemy's rockets.
            if (st.Shootdown && a.Kind == AreaKind.Relief && a.Call == Dustoff && a.Wait == Tune.ShootdownAt) Shootdown.Try(st, a);
            if (a.Wait > 0) return;
            if (a.Call == Crash)
            {
                Shootdown.Crash(st, a);
                st.Events.Add(new SimEvent { Kind = EventKind.AreaEnd, Tick = st.Tick, Side = a.Side, Id = a.Id, X = a.X, Z = a.Z });
                st.Areas.RemoveAt(i);
                return;
            }
            // It is in: an area like any other from here, and announced as one.
            if (a.Kind != AreaKind.Relief)
            {
                st.Events.Add(new SimEvent { Kind = EventKind.AreaStart, Tick = st.Tick, Side = a.Side, Id = a.Id, X = a.X, Z = a.Z });
                return;
            }
            st.Morale[(int)a.Side] = Math.Min(1, st.Morale[(int)a.Side] + 0.09);
            foreach (var m in st.Men)
                if (m.Alive && m.Side == a.Side) m.Pin = Math.Max(0, m.Pin - 0.45);
            st.Events.Add(new SimEvent { Kind = EventKind.AreaEnd, Tick = st.Tick, Side = a.Side, Id = a.Id, X = a.X, Z = a.Z });
            st.Areas.RemoveAt(i);
        }
    }
}
