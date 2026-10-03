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
    ///             move gets up (it takes him the time it takes), goes, and
    ///             goes down when he has been still a moment. A squad in
    ///             contact moves at the double (2.4 m/s; it was a run at 4,
    ///             until the owner saw it: "too quick, almost half"), on the
    ///             march at a walk, and a man up with his squad keeps its
    ///             pace instead of stopping and setting off again every pace.
    ///             A man falling back goes at the double too
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
        /// <summary>
        /// May <paramref name="a"/> put a bullet at <paramref name="b"/>? At arm's length, always; else if he
        /// can see him over the ground between them (<see cref="Sightline"/>). It was "down the lane only":
        /// the lanes were two separate fights, and the owner, playtest 8: "AI soldiers don't shoot each other
        /// across lanes even when directly across one another. Make it so they respect the mountain range and
        /// can only shoot when they see each other."
        /// </summary>
        public static bool InArc(SimState st, Man a, Man b)
        {
            double dx = Math.Abs(a.X - b.X), dz = Math.Abs(a.Z - b.Z);
            if (dx * dx + dz * dz <= Tune.ChargeRange * Tune.ChargeRange) return true;
            return Sightline(a, b);
        }

        // The bank between the lanes (Ground: BermZ, BermHeight, BermGate), in the simulation's own arithmetic:
        // the same bank the terrain is built with, its gaps where the terrain has them.
        private static readonly double BankZ = new GroundParams().BermZ, BankHeight = new GroundParams().BermHeight;
        private static readonly double BankPhase = Phase();

        private static double Phase()
        {
            // Ground's fixed offsets: the fifth draw of its own stream.
            var rng = new Rng(new GroundParams().Seed);
            double o = 0;
            for (int i = 0; i < 5; i++) o = rng.Range(-800, 800);
            return o;
        }

        /// <summary>How tall the bank stands at x: its full height, nothing in a gap (Ground.BermGate).</summary>
        public static double Bank(double x)
        {
            double gaps = JsMath.Sin(x * 0.019 + BankPhase) * 0.5 + 0.5;
            return BankHeight * Math.Max(0, Math.Min(1, (gaps - 0.34) * 3.2));
        }

        /// <summary>A man's eyes above the ground, and the top of him as another man sees him, by how he is.</summary>
        public static double Eyes(Posture p) => p == Posture.Prone ? 0.35 : p == Posture.Crouched ? 1.0 : 1.6;
        public static double Top(Posture p) => p == Posture.Prone ? 0.4 : p == Posture.Crouched ? 1.15 : 1.75;

        /// <summary>
        /// Can <paramref name="a"/> see <paramref name="b"/> over the bank between the lanes? Two men on one
        /// side of it, always. Across it, if the line from his eyes to the top of the other man clears it where
        /// it crosses: two men standing see over it; a man down behind it is out of sight of the other lane,
        /// and so is everyone from him; in a gap it hides nobody.
        /// </summary>
        public static bool Sightline(Man a, Man b)
        {
            double za = a.Z - BankZ, zb = b.Z - BankZ;
            if (za * zb >= 0) return true;
            double t = za / (za - zb);
            double x = a.X + (b.X - a.X) * t;
            double line = Eyes(a.Posture) + (Top(b.Posture) - Eyes(a.Posture)) * t;
            return line > Bank(x);
        }

        /// <summary>Has he been still long enough to fire?</summary>
        public static bool Steady(Man a) => a.Rest >= Tune.SteadyTicks;

        /// <summary>Ticks it takes him to get to his feet from how he was.</summary>
        public static int RiseTicks(Posture from) => from == Posture.Prone ? Tune.RiseFromProne : from == Posture.Crouched ? Tune.RiseFromKnee : 0;

        /// <summary>Is this squad moving at the double: in contact with the enemy, or falling back?</summary>
        public static bool Rushing(Squad sq) => sq.Task != SquadTask.March || sq.Order == Order.Fallback;

        /// <summary>How fast a man on his feet goes, m/s: at the double with his squad in contact, else at a walk.</summary>
        public static double Pace(Squad sq) => Rushing(sq) ? Tune.SpeedRush : Tune.SpeedWalk;

        /// <summary>Ticks a climb into a trench, or out of one, takes: a quarter longer under this rule, and a man's own way of it.</summary>
        public static int VaultTicks(SimState st, bool into, Man m)
            => st.Gunnery ? (into ? Tune.SlowVaultInTicks : Tune.SlowVaultOutTicks) + m.Id % 5 - 2 : (into ? Tune.VaultInTicks : Tune.VaultOutTicks);

        /// <summary>
        /// Ticks a man waits his turn to get up or set off, when his squad goes: the lead man first, each
        /// after the one ahead of him, give or take a tick. The owner, playtest 8: "US soldiers all climb up
        /// exactly at the same time; change that so it's not so coordinated".
        /// </summary>
        public static int Stagger(Man m) => m.Rank * Tune.StaggerTicks + m.Id * 7 % 3;

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
