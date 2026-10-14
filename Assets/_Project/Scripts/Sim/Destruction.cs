using System;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Part 2 rule (MatchOptions.Destruction): a structure can be broken, and the watchtower is the first.
    ///
    /// The owner, 2026-10-12: "the watch tower must be collapsable and fall, to rpgs and heavy mortars. Add all
    /// visual effects and proper animations. Don't always have it fall the same way, make it sandboxy ... we will
    /// add more layers of destruction." So it is a rule for any structure, with the tower the one that uses it.
    ///
    ///   strength   a post starts whole (1). A rocket's burst, a mortar bomb's, a satchel charge's, a shell's
    ///              or a bomb's at its foot or up on its platform takes from it, the more the closer; a rifle
    ///              round, a hand grenade and a 40 mm round do not.
    ///   damage     a tower that has been hit looks hit: the view scorches it, then a brace goes, then it leans
    ///              (it reads the strength left and which way the hits came from)
    ///   the fall   at nothing it falls: away from the burst that finished it, turned by the dice, so no two
    ///              fall alike and one match replays the same. The men on it, and on its ladder, come down with
    ///              it: most are killed; the rest land hurt and flat at its foot. Nobody is posted there again,
    ///              and the squad that held it fights on from the ground.
    ///
    /// Its draws come from the grenades' stream. Its state is hashed only when the rule is on.
    /// </summary>
    public static class Destruction
    {
        /// <summary>What a grenade-like burst does to a structure at its foot: by what fired it.</summary>
        public static double Power(Grenade g)
            => g.Satchel ? Tune.TowerSatchel : g.By == Weapon.Rpg ? Tune.TowerRocket : g.By == Weapon.Mortar ? Tune.TowerMortar : 0;

        /// <summary>A burst at x, z (high metres up: 0 on the ground) of this power at point blank.</summary>
        public static void Burst(SimState st, double x, double z, double high, double power)
        {
            if (!st.Destruction || power <= 0) return;
            for (int i = 0; i < st.Posts.Count; i++)
            {
                var p = st.Posts[i];
                if (p.Fallen) continue;
                double d = JsMath.Hypot(x - p.X, z - p.Z);
                double dmg;
                // Fired at the men up on its platform: what misses them by a few metres still strikes its legs, braces
                // or roof (it is four metres square and nine tall), and has the whole of it.
                if (high > 0 && Math.Abs(high - p.Height) <= Tune.PostBlastHigh && d <= Tune.TowerStruck) dmg = power;
                else if (high <= 0 && d < Tune.TowerHitRadius) dmg = power * (1 - d / Tune.TowerHitRadius);
                else continue;
                p.Strength -= dmg;
                p.LastHitX = x; p.LastHitZ = z;
                st.Events.Add(new SimEvent { Kind = EventKind.Damaged, Tick = st.Tick, Id = p.Id, X = x, Z = z, Amount = Math.Max(0, p.Strength) });
                if (p.Strength <= 0) Fall(st, p);
            }
        }

        private static void Fall(SimState st, Post p)
        {
            var rng = st.FragRng;
            p.Fallen = true;
            p.Strength = 0;
            // Away from the burst that finished it (or any way, if it burst on it square), turned by up to 40 degrees.
            double dx = p.X - p.LastHitX, dz = p.Z - p.LastHitZ;
            double away = JsMath.Hypot(dx, dz) > 0.3 ? Math.Atan2(dz, dx) : rng.Next() * Math.PI * 2;
            double way = away + (rng.Next() - 0.5) * Tune.TowerFallSpread * 2;
            int seed = (int)(rng.Next() * 1000000);
            st.Events.Add(new SimEvent { Kind = EventKind.Collapsed, Tick = st.Tick, Id = p.Id, X = p.X, Z = p.Z, Amount = way * 180 / Math.PI, Rounds = seed });
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                if (!m.Alive || m.Squad < 0 || st.Squads[m.Squad].Post != p.Id || (m.High <= 0 && m.Climb == 0)) continue;
                if (rng.Next() < Tune.TowerFallKill) { Combat.Kill(st, m); continue; }
                // Down with it, a few metres out along its way, flat and shaken.
                double r = 1.5 + rng.Next() * 3.5;
                m.X = p.FootX + Math.Cos(way) * r;
                m.Z = p.FootZ + Math.Sin(way) * r;
                m.High = 0; m.Climb = 0;
                m.Posture = Posture.Prone;
                m.Pin = 1;
            }
            for (int i = 0; i < st.Squads.Count; i++)
                if (st.Squads[i].Post == p.Id) { st.Squads[i].Post = -1; st.Squads[i].Leaving = false; }
        }
    }
}
