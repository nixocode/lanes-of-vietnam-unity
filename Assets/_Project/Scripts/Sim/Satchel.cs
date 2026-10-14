namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Part 2 rule (MatchOptions.Satchel): sappers and engineers carry satchel charges.
    ///
    /// A satchel charge is a few pounds of explosive in a canvas bag with a short
    /// fuse: what a VC sapper carried through the wire, and what American
    /// engineers used on bunkers. Without the rule a sapper squad and an engineer
    /// squad fight as any squad of submachine guns and rifles does.
    ///
    ///   carried    every man of a sapper or an engineer squad has two
    ///   thrown     by one who is within 9 m of a position the enemy is in, not
    ///              pinned flat and not up a post, into its middle: one at a
    ///              time a man, four seconds apart
    ///   burst      after two seconds, wider and surer than a grenade's, and the
    ///              position's own protection is little use against a charge
    ///              that lands inside it
    ///   breached   the position it bursts in keeps four tenths of its
    ///              protection for twenty seconds: its parapet and its roof are
    ///              down until they are dug and stacked again
    ///
    /// Every draw comes from the grenades' own stream (SimState.FragRng). Its
    /// state is hashed only when the rule is on.
    /// </summary>
    public static class Satchel
    {
        public static bool Carrier(SimState st, Man m)
            => st.Satchel && m.Squad >= 0 && m.Squad < st.Squads.Count && (st.Squads[m.Squad].Card == "vc-sapper" || st.Squads[m.Squad].Card == "us-engineer");

        public static void Tick(SimState st)
        {
            foreach (var c in st.Cover) if (c.Breached > 0) c.Breached--;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var a = st.Men[i];
                if (!a.Alive) continue;
                if (a.Satchels < 0) a.Satchels = Carrier(st, a) ? Tune.SatchelsCarried : 0;
                if (a.Satchels == 0) continue;
                if (a.SatchelWait > 0) { a.SatchelWait--; continue; }
                if (a.Pin >= Tune.PinStop || a.High > 0 || a.Climb != 0) continue;
                var into = Target(st, a);
                if (into != null) { Throw(st, a, into.X, into.Z, into.Id); continue; }
                // Destruction: a sapper or an engineer within reach of the enemy's tower puts a charge at its foot.
                var tower = Tower(st, a);
                if (tower != null) Throw(st, a, tower.X, tower.Z, null);
            }
        }

        /// <summary>The nearest position within reach that the enemy has men in, or null.</summary>
        public static Cover Target(SimState st, Man a)
        {
            Cover best = null;
            double bd = Tune.SatchelReach * Tune.SatchelReach;
            foreach (var c in st.Cover)
            {
                double dx = c.X - a.X, dz = c.Z - a.Z, d2 = dx * dx + dz * dz;
                if (d2 > bd) continue;
                bool held = false;
                for (int j = 0; j < st.Men.Count && !held; j++)
                {
                    var b = st.Men[j];
                    held = b.Alive && b.Side != a.Side && b.Cover == c.Id;
                }
                if (!held) continue;
                bd = d2; best = c;
            }
            return best;
        }

        /// <summary>Destruction: an enemy post still standing whose foot is within reach, or null.</summary>
        public static Post Tower(SimState st, Man a)
        {
            if (!st.Destruction) return null;
            foreach (var p in st.Posts)
                if (!p.Fallen && p.Side != a.Side && JsMath.Hypot(p.X - a.X, p.Z - a.Z) <= Tune.SatchelReach) return p;
            return null;
        }

        private static void Throw(SimState st, Man a, double x, double z, int? cover)
        {
            var rng = st.FragRng;
            double sx = (rng.Next() + rng.Next() - 1) * Tune.SatchelScatter;
            double sz = (rng.Next() + rng.Next() - 1) * Tune.SatchelScatter;
            var g = new Grenade
            {
                Id = st.GrenadeId++, Side = a.Side, Thrower = a.Id, X = x + sx, Z = z + sz, Ticks = Tune.SatchelFuse,
                Radius = Tune.SatchelRadius, Kill = Tune.SatchelKill, CoverFactor = Tune.SatchelCoverFactor, Satchel = true,
            };
            st.Grenades.Add(g);
            a.Satchels--;
            a.SatchelWait = Tune.SatchelGap;
            a.Seen = true;
            a.FiredAt = st.Tick;
            a.Cooldown = System.Math.Max(a.Cooldown, Tune.Cooldown * 2);
            // (Target: the position it was thrown into; none for a tower's foot.)
            st.Events.Add(new SimEvent { Kind = EventKind.SatchelThrown, Tick = st.Tick, Side = a.Side, Id = a.Id, Target = cover, X = g.X, Z = g.Z, Amount = g.Ticks });
        }

        /// <summary>A charge has gone off (Frag.Detonate): the position it burst in is breached.</summary>
        public static void Burst(SimState st, Grenade g)
        {
            st.Events.Add(new SimEvent { Kind = EventKind.SatchelBlast, Tick = st.Tick, Side = g.Side, Id = g.Id, X = g.X, Z = g.Z });
            Cover hit = null;
            double bd = Tune.SatchelBreachWithin * Tune.SatchelBreachWithin;
            foreach (var c in st.Cover)
            {
                double dx = c.X - g.X, dz = c.Z - g.Z, d2 = dx * dx + dz * dz;
                if (d2 <= bd) { bd = d2; hit = c; }
            }
            if (hit == null) return;
            hit.Breached = Tune.SatchelBreachTicks;
            st.Events.Add(new SimEvent { Kind = EventKind.Breached, Tick = st.Tick, Side = g.Side, Id = hit.Id, X = hit.X, Z = hit.Z, Amount = hit.Breached });
        }
    }
}
