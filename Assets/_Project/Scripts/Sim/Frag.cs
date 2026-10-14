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
                // FragCard: a player's men throw when he plays the card, and not of themselves.
                if (st.FragCard && a.Side == st.Player) continue;
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
            // FireControl: where it will find the most men, and never onto his own.
            if (st.FireControl) return FireControl.Throw(st, a);
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

        /// <summary>
        /// FragCard: who throws when a side plays GRENADES at a place: of the squad whose men are nearest it, those who
        /// have a grenade, are not on a post and have it within throwing range, nearest first, three at the most.
        /// Empty if nobody can reach it.
        /// </summary>
        public static System.Collections.Generic.List<Man> Throwers(SimState st, Side side, int lane, double x)
        {
            double z = Tune.Lanes[lane];
            // (As far as a man throws, 20 m: not the 12 a man throws of himself under Arms, who throws at the man
            // nearest him. This is thrown where it is put.)
            double far = Tune.FragRange;
            Man nearest = null;
            double best = far * far;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                if (!m.Alive || m.Side != side || m.Grenades <= 0 || m.High > 0 || m.Climb != 0) continue;
                double dx = m.X - x, dz = m.Z - z, d2 = dx * dx + dz * dz;
                if (d2 <= best) { best = d2; nearest = m; }
            }
            var them = new System.Collections.Generic.List<Man>();
            if (nearest == null) return them;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                if (!m.Alive || m.Squad != nearest.Squad || m.Grenades <= 0 || m.High > 0 || m.Climb != 0) continue;
                double dx = m.X - x, dz = m.Z - z;
                if (dx * dx + dz * dz <= far * far) them.Add(m);
            }
            them.Sort((p, q) => JsMath.Hypot(p.X - x, p.Z - z).CompareTo(JsMath.Hypot(q.X - x, q.Z - z)));
            if (them.Count > Tune.FragCardThrowers) them.RemoveRange(Tune.FragCardThrowers, them.Count - Tune.FragCardThrowers);
            return them;
        }

        /// <summary>FragCard: GRENADES played at a place: the squad nearest it throws, each man once, at the place.</summary>
        public static void OnCall(SimState st, Side side, int lane, double x)
        {
            double z = Tune.Lanes[lane];
            foreach (var a in Throwers(st, side, lane, x)) Throw(st, a, x, z, -1, st.FragRng);
        }

        private static void Throw(SimState st, Man a, Man target, Rng rng) => Throw(st, a, target.X, target.Z, target.Id, rng);

        private static void Throw(SimState st, Man a, double tx, double tz, int targetId, Rng rng)
        {
            double d = JsMath.Hypot(tx - a.X, tz - a.Z);
            // Triangular scatter from two uniforms: no transcendentals, so the
            // same bits on every platform (the port keeps to fdlibm for that).
            double sd = Tune.FragScatterFixed + Tune.FragScatterPerMetre * d;
            double sx = (rng.Next() + rng.Next() - 1) * sd * 2.45;
            double sz = (rng.Next() + rng.Next() - 1) * sd * 2.45;
            var g = new Grenade
            {
                Id = st.GrenadeId++, Side = a.Side, Thrower = a.Id,
                X = tx + sx, Z = tz + sz,
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
                Kind = EventKind.GrenadeThrown, Tick = st.Tick, Side = a.Side, Id = a.Id, Target = targetId >= 0 ? targetId : (int?)null,
                X = g.X, Z = g.Z,
            });
        }

        private static void Detonate(SimState st, Grenade g, Rng rng)
        {
            var blast = new SimEvent { Kind = EventKind.GrenadeBlast, Tick = st.Tick, Side = g.Side, Id = g.Id, X = g.X, Z = g.Z };
            // Vantage: one that bursts up on a post says how high (Amount, metres), for the view.
            if (st.Vantage && g.High > 0) blast.Amount = g.High;
            st.Events.Add(blast);
            // Satchel: a charge breaches the position it bursts in.
            if (g.Satchel) Satchel.Burst(st, g);
            // Destruction: a rocket's, a mortar's or a charge's burst at a structure.
            if (st.Destruction) Destruction.Burst(st, g.X, g.Z, g.High, Destruction.Power(g));
            for (int j = 0; j < st.Men.Count; j++)
            {
                var m = st.Men[j];
                if (!m.Alive) continue;
                // Vantage: a burst on the ground does not reach a man up on a post, nor one up there the ground.
                if (st.Vantage && !Vantage.Reaches(g.High, m)) continue;
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
