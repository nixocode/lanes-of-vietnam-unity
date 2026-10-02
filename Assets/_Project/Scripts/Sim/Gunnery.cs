using System;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Gunnery (Part 2, behind <see cref="MatchOptions.Gunnery"/>): where a
    /// round is fired from, where it goes, and what a man's body is doing when
    /// he fires it.
    ///
    /// The owner, playtest 6, on the Senses build: "still too many random shots
    /// shooting up, down and in circles"; "the gunners sometimes shoot and
    /// walk, they should only shoot prone or kneeling. Shooting while standing
    /// should be very inaccurate (and happen 10% of the time or less)"; "lots
    /// of US soldiers sliding and not walking"; "see real impacts, where
    /// bullets go". Measured on that build (six seeds, `simcs aware`): 29% of
    /// shots were across the two lanes, which on a side-on camera is straight
    /// up or down the screen; 28% were fired by a man on the move and 18% by a
    /// man on his feet; and men spent 174 man-seconds a minute moving while the
    /// simulation had them on a knee or flat, which the view could only draw
    /// as a kneeling man gliding (the motion audit: his planted foot travelled
    /// at the whole of his speed).
    ///
    /// The rules:
    ///
    ///   lanes     a rifle, a machine gun and a scope fire down the lane: at a
    ///             man no further across from the firer than half as far as he
    ///             is along it. Thirteen metres between the lanes puts the
    ///             other lane out of a rifle's reach and leaves a machine gun
    ///             or a sniper only the long diagonal. A squad deals with the
    ///             enemy in its own lane. What is thrown or lobbed still
    ///             crosses, and so does a man at arm's length
    ///   stance    nobody fires on the move: a man has been at rest a fifth of
    ///             a second before he fires. On a knee or flat he fires as he
    ///             did. On his feet he fires one chance in ten, and hits a
    ///             third as often
    ///   movement  a man on a knee or flat stays where he is. One who has to
    ///             move gets up (it takes him the time it takes), runs, and
    ///             goes down when he has been still a moment. A squad in
    ///             contact moves at a run, on the march at a walk, and a man
    ///             up with his squad keeps its pace instead of stopping and
    ///             setting off again every pace. A man falling back runs
    ///   impacts   a round that misses lands: past the man it was fired at, on
    ///             its own line, a little wide. The Fire event carries the
    ///             point, so the view draws the round to where it went
    ///
    /// Its random draws (the standing man's one chance in ten, where a miss
    /// lands) come from its own stream. Its state is hashed only when the rule
    /// is on: off, the match is the parity baseline to the bit.
    /// </summary>
    public static class Gunnery
    {
        /// <summary>May <paramref name="a"/> put a bullet at <paramref name="b"/>: is he down the lane from him, or at arm's length?</summary>
        public static bool InArc(Man a, Man b)
        {
            double dx = Math.Abs(a.X - b.X), dz = Math.Abs(a.Z - b.Z);
            if (dx * dx + dz * dz <= Tune.ChargeRange * Tune.ChargeRange) return true;
            return dz <= Tune.ArcSlope * dx;
        }

        /// <summary>Has he been still long enough to fire?</summary>
        public static bool Steady(Man a) => a.Rest >= Tune.SteadyTicks;

        /// <summary>Ticks it takes him to get to his feet from how he was.</summary>
        public static int RiseTicks(Posture from) => from == Posture.Prone ? Tune.RiseFromProne : from == Posture.Crouched ? Tune.RiseFromKnee : 0;

        /// <summary>Is this squad moving at a run: in contact with the enemy, or falling back?</summary>
        public static bool Rushing(Squad sq) => sq.Task != SquadTask.March || sq.Order == Order.Fallback;

        /// <summary>
        /// How he carries himself, given what the rules before this one wanted
        /// (<paramref name="want"/>): up to move, down when he has stopped.
        /// </summary>
        public static Posture Carry(SimState st, Man m, Squad sq, Posture want, bool charging, bool stalking, double away)
        {
            // Running for it, or going in with the bayonet: on his feet, whatever is coming at him.
            if (sq.Order == Order.Fallback || charging) return Posture.Standing;
            if (m.Pin >= Tune.PinDrop || stalking) return want;
            bool contact = st.Senses && (Senses.InContact(sq) || sq.Task != SquadTask.March);
            // On the march and marching: he walks.
            if (!contact && !sq.Halted) return Posture.Standing;
            // (By the distance he would set off at: a man on his feet and at rest a pace from his place
            // is not going anywhere, and stood there for it.)
            bool hasToMove = away > (m.Posture != Posture.Standing ? Tune.GetUpBeyond : m.Still ? Tune.SetOff : Tune.Arrive);
            if (hasToMove) return Posture.Standing;
            // Just stopped, or keeping pace with a squad on the move: not down yet.
            if (m.Posture == Posture.Standing && m.Rest < Tune.RestToKneel) return Posture.Standing;
            if (m.Posture != Posture.Standing) return want == Posture.Standing ? m.Posture : want;
            bool gun = st.Arms && (m.Weapon == Weapon.M60 || m.Weapon == Weapon.Rpd || m.Weapon == Weapon.Sniper);
            return contact && sq.Halted && (gun || m.Cover < 0) ? Posture.Prone : Posture.Crouched;
        }

        /// <summary>
        /// Where a round fired by <paramref name="a"/> at <paramref name="b"/>
        /// that missed him comes down: on past him along its own line, and a
        /// little to one side, the further off the wider.
        /// </summary>
        public static (double x, double z) Miss(SimState st, Man a, Man b)
        {
            double dx = b.X - a.X, dz = b.Z - a.Z, d = Math.Sqrt(dx * dx + dz * dz);
            if (d < 1e-6) return (b.X, b.Z);
            dx /= d; dz /= d;
            double over = Tune.MissOver * st.GunRng.Next();
            double wide = (st.GunRng.Next() * 2 - 1) * (Tune.MissWide + Tune.MissWidePerMetre * d);
            return (b.X + dx * over - dz * wide, b.Z + dz * over + dx * wide);
        }
    }
}
