using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Cohesion (Part 2, behind <see cref="MatchOptions.Cohesion"/>): a squad
    /// keeps together, and makes up its mind.
    ///
    /// The owner, 2026-10-11, of the build with every rule to that day:
    /// "AI also needs work, still tons of wandering, weird formations".
    /// Measured (`simcs shape`, new, twenty matches): a squad of five was 8 m
    /// long on the march and 10 m closing, its lead man out alone 5 m ahead of
    /// the next a tenth of the time, one man walking up by himself while the
    /// rest lay still a seventh of it; squads stood one in another sixteen
    /// squad-seconds a minute; a squad said it was going in and went nowhere
    /// for twenty-seven; and it changed its task every eight seconds.
    ///
    /// The rules:
    ///
    ///   a closer file   a man follows the man ahead at 1.7 m on the move, not
    ///                   2.6, and a squad that halts in the open closes up to
    ///                   1.3
    ///   the tail        the squad goes no faster than its last man: its
    ///                   anchor waits when he is more than a squad's length
    ///                   and three metres behind it. Moving by halves, that
    ///                   is a short bound and the other half up to it, not
    ///                   one half gone on ahead
    ///   a straggler     a man more than three and a half metres from his
    ///                   place runs to it
    ///   by groups       closing by halves, the halves are the two men at the
    ///                   head of the file and the rest, not every other man
    ///                   of it; and going in, everyone who can goes at once,
    ///                   and only when half of the squad can
    ///   its mind        a squad that has gone to ground stays eight seconds
    ///                   before it closes or goes in, not four; one that is
    ///                   spent and has run stays five before it runs again;
    ///                   and one that is closing or going in and has not made
    ///                   a metre in three and a half seconds stops saying so,
    ///                   and goes to ground
    ///
    /// Built, measured and left out: a squad stopping behind a squad of its own
    /// side that had gone to ground ahead of it, in support, and not closing
    /// through it. Squads stood one in another a third as often, and one match
    /// in five ran to the twelve-minute cap with nobody able to mass for the
    /// assault (PLAN §12.37). The longer wait in a firefight does most of what
    /// it did for that anyway.
    ///
    /// It draws no randomness. Off, the match is what it was to the bit.
    /// </summary>
    public static class Cohesion
    {
        /// <summary>Metres from a man to the man behind him in his squad's file.</summary>
        public static double Gap(SimState st, Squad sq)
            => !st.Cohesion ? Tune.SlotGap : sq.Halted ? Tune.FileClosed : Tune.FileMarch;

        /// <summary>The squad's last man, as far forward as he is (along its own way).</summary>
        private static double Rear(IReadOnlyList<Man> live, double dir)
        {
            double rear = double.PositiveInfinity;
            for (int i = 0; i < live.Count; i++) rear = Math.Min(rear, live[i].X * dir);
            return rear;
        }

        /// <summary>Is the squad going in (its task says, or without Senses its charge)?</summary>
        private static bool GoingIn(SimState st, Squad sq) => st.Senses ? sq.Task == SquadTask.Assault : sq.Assault;

        /// <summary>
        /// After the squad's anchor has moved this tick (it was at <paramref name="was"/>): held back for its
        /// tail.
        /// </summary>
        public static void Hold(SimState st, Squad sq, IReadOnlyList<Man> live, double was, double dir)
        {
            if (sq.Order == Order.Fallback || (sq.AnchorX - was) * dir <= 0 || GoingIn(st, sq)) return;
            double at = sq.AnchorX * dir, from = was * dir;
            // Its tail.
            if (live.Count > 1)
            {
                double most = Rear(live, dir) + (live.Count - 1) * Tune.FileMarch + Tune.TailSlack;
                if (at > most) at = Math.Max(from, most);
            }
            sq.AnchorX = at * dir;
        }

        /// <summary>Can the squad go in: are at least half its men (and two of them, if it has two) not kept down?</summary>
        public static bool Ready(IReadOnlyList<Man> live)
        {
            int up = 0;
            for (int i = 0; i < live.Count; i++) if (live[i].Pin < Tune.PinDrop) up++;
            return up * 2 >= live.Count && up >= Math.Min(2, live.Count);
        }

        /// <summary>A squad takes up closing or going in: where it starts from, for <see cref="Stalled"/>.</summary>
        public static void Start(SimState st, Squad sq)
        {
            sq.ProgressX = sq.AnchorX;
            sq.ProgressAt = st.Tick;
        }

        /// <summary>
        /// Closing or going in, and no further on than it was three and a half seconds ago. (Called once a tick
        /// while it is: it keeps the squad's own account of where it last was a metre further on.)
        /// </summary>
        public static bool Stalled(SimState st, Squad sq)
        {
            if (Math.Abs(sq.AnchorX - sq.ProgressX) >= Tune.StallMetres) Start(st, sq);
            return st.Tick - sq.ProgressAt >= Tune.StallTicks;
        }
    }
}
