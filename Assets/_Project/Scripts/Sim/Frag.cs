using System;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Grenades (PLAN §12.8, Part 2, behind <see cref="MatchOptions.Frag"/>):
    /// "a close assault breaks a position that small arms cannot".
    ///
    /// Cover is the sim's strongest defence — a man in a trench is hard to hit
    /// and slow to pin — and until now nothing answered it at close range. A
    /// man within 7-20 m of an enemy he can see in cover, not pinned past the
    /// point of exposing himself, throws: the grenade comes down near the man
    /// with scatter that grows with the distance, burns its fuse, and goes
    /// off. Everyone near it is at risk, whoever threw it; cover helps little
    /// against a grenade that lands inside it, lying flat in the open more.
    ///
    /// Every draw comes from <see cref="SimState.FragRng"/>, never the sim's
    /// own stream, and the rule's state is hashed only when it is on: with it
    /// off, the match is the parity baseline to the bit.
    /// </summary>
    public static class Frag
    {
        public static void Tick(SimState st)
        {
            var rng = st.FragRng;

            // Fuses first: what was thrown earlier goes off before anyone throws again.
            for (int i = st.Grenades.Count - 1; i >= 0; i--)
            {
                var g = st.Grenades[i];
                if (--g.Ticks > 0) continue;
                st.Grenades.RemoveAt(i);
                Detonate(st, g, rng);
            }

            // Launched rounds (Arms) burst here too; only the throwing is the grenade rule's own.
            if (!st.Frag) return;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var a = st.Men[i];
                if (!a.Alive) continue;
                if (a.FragCooldown > 0) { a.FragCooldown--; continue; }
                if (a.Grenades <= 0 || a.Pin >= Tune.PinStop || a.Posture == Posture.Prone) continue;
                // Ammo: a squad throws one at a time.
                if (st.Ammo && Ammo.SquadThrowing(st, a)) continue;
                var target = PickTarget(st, a);
                if (target == null) continue;
                if (rng.Next() >= Tune.FragThrowChance) continue;
                Throw(st, a, target, rng);
            }
        }

        /// <summary>The nearest seen enemy in cover within throwing range, or null.</summary>
        public static Man PickTarget(SimState st, Man a)
        {
            Man best = null;
            double far = st.Arms ? Tune.ArmsFragRange : Tune.FragRange, near = st.Arms ? Tune.ArmsFragMin : Tune.FragMin;
            double bestD2 = far * far, min2 = near * near;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var b = st.Men[i];
                if (!b.Alive || !b.Seen || b.Side == a.Side || b.Cover < 0) continue;
                double dx = a.X - b.X, dz = a.Z - b.Z;
                double d2 = dx * dx + dz * dz;
                if (d2 > bestD2 || d2 < min2) continue;
                if (Combat.SmokeBlocks(st, a.X, a.Z, b.X, b.Z)) continue;
                bestD2 = d2; best = b;
            }
            return best;
        }

        private static void Throw(SimState st, Man a, Man target, Rng rng)
        {
            double d = JsMath.Hypot(target.X - a.X, target.Z - a.Z);
            // Triangular scatter from two uniforms: no transcendentals, so the
            // same bits on every platform (the port keeps to fdlibm for that).
            double sd = Tune.FragScatterFixed + Tune.FragScatterPerMetre * d;
            double sx = (rng.Next() + rng.Next() - 1) * sd * 2.45;
            double sz = (rng.Next() + rng.Next() - 1) * sd * 2.45;
            var g = new Grenade
            {
                Id = st.GrenadeId++, Side = a.Side, Thrower = a.Id,
                X = target.X + sx, Z = target.Z + sz,
                Ticks = Tune.FragFuse,
            };
            st.Grenades.Add(g);
            a.Grenades--;
            a.FragCooldown = Tune.FragInterval;
            if (st.Ammo) st.Squads[a.Squad].ThrewAt = st.Tick;
            // Throwing exposes him, and his rifle waits while he does it.
            a.Seen = true;
            a.FiredAt = st.Tick;
            a.Cooldown = Math.Max(a.Cooldown, Tune.Cooldown * 2);
            st.Events.Add(new SimEvent
            {
                Kind = EventKind.GrenadeThrown, Tick = st.Tick, Side = a.Side, Id = a.Id, Target = target.Id,
                X = g.X, Z = g.Z,
            });
        }

        private static void Detonate(SimState st, Grenade g, Rng rng)
        {
            st.Events.Add(new SimEvent { Kind = EventKind.GrenadeBlast, Tick = st.Tick, Side = g.Side, Id = g.Id, X = g.X, Z = g.Z });
            for (int j = 0; j < st.Men.Count; j++)
            {
                var m = st.Men[j];
                if (!m.Alive) continue;
                double d = JsMath.Hypot(m.X - g.X, m.Z - g.Z);
                if (d > Tune.FragPinRadius) continue;
                if (d < g.Radius)
                {
                    double t = 1 - d / g.Radius;
                    double p = g.Kill * t * Math.Sqrt(t);                 // sqrt is exact everywhere; pow is not
                    if (m.Cover >= 0) p *= g.CoverFactor;
                    else if (m.Posture == Posture.Prone) p *= Tune.FragProneFactor;
                    if (rng.Next() < p) { Combat.Kill(st, m); continue; }
                }
                Combat.ApplyPin(st, m, Tune.FragPin * (1 - d / Tune.FragPinRadius));
            }
        }
    }
}
