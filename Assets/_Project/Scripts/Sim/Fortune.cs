using System;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Fortune (Part 2, behind <see cref="MatchOptions.Fortune"/>): men who
    /// are not all the same man, and luck.
    ///
    /// The owner, playtest 9: "sometimes snipers will get killed by squads by
    /// 'lucky shots': add luck element to it", and "add randomness". Until now
    /// the only chance in a firefight was whether a round hit. Every rifleman
    /// shot as well as every other, took fire as well, and fired again after
    /// exactly the same pause; every match opened with the same two squads in
    /// the near lane; and the computer raised its squads in one fixed order.
    ///
    /// The rules:
    ///
    ///   men      each man is drawn as he is raised: how well he shoots, how
    ///            much fire it takes to put his head down, how quick he is
    ///            with his weapon. Most are near the ordinary man; a few are
    ///            a good deal better or worse
    ///   pauses   the time to his next shot, and a reload, are a third
    ///            longer or shorter than their own, by chance, every time
    ///   luck     a long shot (beyond the weapon's own distance, which
    ///            <see cref="Tactics"/> allows) that missed finds its man
    ///            after all, once in a great while. It is what kills a sniper
    ///            at a distance no rifle has any business hitting at
    ///   opening  the lane the first two squads meet in is drawn, and so is
    ///            each squad the computer raises after its first
    ///
    /// Every draw comes from <see cref="SimState.LuckRng"/>, never the
    /// simulation's own stream, so a seed still replays exactly and with the
    /// rule off the match is what it was to the bit. No transcendentals.
    /// </summary>
    public static class Fortune
    {
        /// <summary>Near 1, the ordinary man: the sum of two draws, so the middle is common and the ends are rare.</summary>
        private static double Near(Rng rng, double spread) => 1 + spread * (rng.Next() + rng.Next() - 1);

        /// <summary>A man as he is raised: his aim, his nerve, his quickness.</summary>
        public static void Issue(SimState st, Man m)
        {
            m.Aim = Near(st.LuckRng, Tune.AimSpread);
            m.Nerve = Near(st.LuckRng, Tune.NerveSpread);
            m.Quick = Near(st.LuckRng, Tune.QuickSpread);
        }

        /// <summary>What a burst of fire near him does to this man, against the ordinary man: a steady one takes less of it.</summary>
        public static double Shaken(Man m, double amount) => amount * (2 - m.Nerve);

        /// <summary>A pause of <paramref name="ticks"/> as this man takes it this time: his own quickness, and chance.</summary>
        public static int Pause(SimState st, Man m, int ticks)
        {
            if (ticks <= 1) return ticks;
            double by = 1 + Tune.PauseSpread * (st.LuckRng.Next() * 2 - 1);
            return Math.Max(1, (int)(ticks * by / m.Quick));
        }

        /// <summary>A long shot that missed: does it find him after all?</summary>
        public static bool Lucky(SimState st, Man a, Man target)
        {
            double r = Arms.Of(st, a).Range, dx = a.X - target.X, dz = a.Z - target.Z;
            if (dx * dx + dz * dz <= r * r) return false;
            return st.LuckRng.Next() < Tune.LuckyHit;
        }

        /// <summary>The lane the match opens in: the near one without the rule.</summary>
        public static int OpeningLane(SimState st) => st.Fortune ? st.LuckRng.Int(0, Tune.Lanes.Length) : 0;

        /// <summary>Which of its list a side raises as its <paramref name="n"/>th squad: in order without the rule; with it, the first as listed and the rest drawn.</summary>
        public static int Raised(SimState st, int n, int length)
            => !st.Fortune || n == 0 ? n % length : st.LuckRng.Int(0, length);
    }
}
