using System;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Part 2 rule (MatchOptions.Shootdown, with Inbound): the medevac can be brought down.
    ///
    /// The owner, 2026-10-15: "have the chopper have a chance to be knocked down by an RPG that turns into it
    /// spinning losing control and crashing into flames."
    ///
    ///   the shot    three seconds out, low and slow over the ground it is coming down on, the helicopter is in
    ///               reach of the enemy's rockets: each of the two nearest RPG men within 60 m of where it is
    ///               coming down, alive and not pinned flat, fires, and hits three times in ten
    ///   the fall    hit, it does not come in: the line is not steadied and nobody's pin is taken away. It goes
    ///               down spinning, a few metres short of where it was coming (back toward its own side) and to
    ///               one side, three seconds after it is hit
    ///   the crash   where it strikes it bursts as a shell does (MatchOptions.Destruction: a structure there is
    ///               hurt too): whoever is near is pinned, and may be killed
    ///
    /// Its draws come from a stream of its own (a fork: no other stream moves), and only when a rocket is in reach.
    /// A ShotDown event (at the end of the enum) says who fired, at which call, where it will come down, when, and
    /// a seed for how it is drawn going down. Off, the match is what it was to the bit.
    /// </summary>
    public static class Shootdown
    {
        /// <summary>The helicopter of a medevac (area a) is three seconds out: the enemy's rocket men in reach of it fire, and one may hit.</summary>
        public static void Try(SimState st, Area a)
        {
            var rng = st.ShootRng;
            if (rng == null) return;
            int fired = 0;
            while (fired < Tune.ShootdownMen)
            {
                // The nearest RPG man of the other side not yet asked, in reach and able.
                Man best = null;
                double bestD = double.MaxValue;
                foreach (var m in st.Men)
                {
                    if (!m.Alive || m.Side == a.Side || m.Weapon != Weapon.Rpg || m.Pin >= Tune.PinStop || m.Climb != 0 || m.Cooldown > Tune.ShootdownReload) continue;
                    double d = JsMath.Hypot(m.X - a.X, m.Z - a.Z);
                    if (d < bestD && d <= Tune.ShootdownRange) { bestD = d; best = m; }
                }
                if (best == null) return;
                fired++;
                // (Once he has fired he is reloading: he is not asked again.)
                best.Cooldown = Tune.ShootdownReload + 1;
                if (rng.Next() >= Tune.ShootdownChance) continue;
                Hit(st, a, best);
                return;
            }
        }

        /// <summary>The rocket of man <paramref name="by"/> hits the helicopter of medevac a: it goes down, a little short of where it was coming and to one side.</summary>
        private static void Hit(SimState st, Area a, Man by)
        {
            var rng = st.ShootRng;
            double dir = Combat.Advance(a.Side);
            double cx = a.X - dir * (4 + 14 * rng.Next()), cz = a.Z + (rng.Next() - 0.5) * 16;
            cz = Math.Max(Tune.Lanes[1] - 8, Math.Min(Tune.Lanes[0] + 8, cz));
            int seed = (int)(rng.Next() * 1000000);
            st.Events.Add(new SimEvent { Kind = EventKind.ShotDown, Tick = st.Tick, Side = a.Side, Id = by.Id, Target = a.Id, X = cx, Z = cz, Amount = Tune.CrashTicks, Rounds = seed });
            // It does not come in: where it comes down is a burst waiting to happen.
            a.Call = Inbound.Crash;
            a.Wait = Tune.CrashTicks;
            a.X = cx; a.Z = cz;
            a.Power = Tune.CrashPower;
        }

        /// <summary>
        /// Bring a medevac on its way in down now, for a capture or a test: the nearest enemy rocket man fires (or, with
        /// none, whoever of the enemy is nearest) and hits, without dice. Never part of a match the game plays. True if
        /// there was one to bring down.
        /// </summary>
        public static bool Force(SimState st, Area a)
        {
            if (st.ShootRng == null || a.Kind != AreaKind.Relief || a.Call != Inbound.Dustoff || a.Wait <= 0) return false;
            Man best = null;
            double bestD = double.MaxValue;
            foreach (var m in st.Men)
            {
                if (!m.Alive || m.Side == a.Side) continue;
                double d = JsMath.Hypot(m.X - a.X, m.Z - a.Z) - (m.Weapon == Weapon.Rpg ? 1000 : 0);
                if (d < bestD) { bestD = d; best = m; }
            }
            if (best == null) return false;
            Hit(st, a, best);
            return true;
        }

        /// <summary>It strikes the ground: a burst there, as a shell's.</summary>
        public static void Crash(SimState st, Area a) => Match.Burst(st, a, a.X, a.Z, st.ShootRng);
    }
}
