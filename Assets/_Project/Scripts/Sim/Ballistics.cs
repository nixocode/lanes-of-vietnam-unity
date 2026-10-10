using System;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Ballistics (Part 2, behind <see cref="MatchOptions.Ballistics"/>): a
    /// round that misses its man goes somewhere, and whoever is there may be
    /// hit by it.
    ///
    /// The owner, 2026-10-10: "add better shooting mechanics, make it as
    /// realistic as possible", and of the rule this is, named to him as the
    /// thing the simulation did not do: "yes i want the new shooting rule".
    /// Until now a shot was one chance at one man. If it missed, it put his
    /// head down and the heads near him, and was gone: the man a pace behind
    /// him, on the round's own line, was as safe as one in the next lane; and
    /// a machine gun's burst of ten rounds was one chance at one man too.
    ///
    /// The rules:
    ///
    ///   where it goes  a round that misses comes down where the simulation
    ///                  already says a miss does (<see cref="Gunnery.Miss"/>:
    ///                  past the man, on its own line, a little wide). The
    ///                  rounds of a burst are walked along that line, a pace
    ///                  apart, the last of them there: as the view has drawn
    ///                  them since §12.32, now the simulation's own
    ///   whom it finds  over the last four metres before it comes down a
    ///                  round is at the height of a man. The first man of the
    ///                  enemy within 0.6 m of that line, other than the one
    ///                  it was fired at, may be hit by it: with the chance
    ///                  the firer would have had firing at him, shared among
    ///                  the rounds of a burst. A man well down behind cover
    ///                  is as hard for a stray round to find as for an aimed
    ///                  one
    ///   a burst that   the rounds that did not hit the man it killed were
    ///   kills          walked up to him: they may find a man in front of him
    ///   not            a machine gun's bursts onto cover it has lost sight of
    ///                  (they pin and do not kill, as ever); and never a man
    ///                  of the firer's own side
    ///
    /// Its draws come from Gunnery's stream, and only when a round has found
    /// somebody. It has no state: off, the match is what it was to the bit.
    /// </summary>
    public static class Ballistics
    {
        /// <summary>
        /// The rounds of a shot that did not hit its man: <paramref name="strays"/> of them, the last coming
        /// down at (<paramref name="x"/>, <paramref name="z"/>), the others short of it along the shot's line.
        /// Each may find a man. <paramref name="of"/> is the rounds the whole shot fired.
        /// </summary>
        public static void Strays(SimState st, Man a, Man target, int strays, int of, double x, double z)
        {
            double dx = x - a.X, dz = z - a.Z, d = Math.Sqrt(dx * dx + dz * dz);
            if (d < 1e-6 || strays <= 0) return;
            dx /= d; dz /= d;
            for (int r = 0; r < strays; r++)
            {
                // Where this round comes down, and the first man it meets on its way there.
                double px = x - dx * r * Tune.StitchStep, pz = z - dz * r * Tune.StitchStep;
                Man found = null;
                double first = -1;
                for (int i = 0; i < st.Men.Count; i++)
                {
                    var o = st.Men[i];
                    if (!o.Alive || o.Side != target.Side || o == target) continue;
                    double ox = px - o.X, oz = pz - o.Z;
                    double before = ox * dx + oz * dz;
                    if (before < 0 || before > Tune.StrayReach || before <= first) continue;
                    if (Math.Abs(ox * dz - oz * dx) > Tune.StrayWidth) continue;
                    first = before; found = o;
                }
                if (found == null) continue;
                double p = Combat.HitChance(st, a, found) * Tune.StrayShare / Math.Max(1, of);
                if (st.GunRng.Next() >= p) continue;
                st.Events.Add(new SimEvent
                {
                    Kind = EventKind.Stray, Tick = st.Tick, Side = a.Side, Id = a.Id, Target = found.Id, X = found.X, Z = found.Z,
                });
                Combat.Kill(st, found);
            }
        }
    }
}
