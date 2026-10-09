using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Fire control (Part 2, behind <see cref="MatchOptions.FireControl"/>):
    /// what a squad does with its fire.
    ///
    /// The owner, 2026-10-09: "focus on firing/gunplay mechanics and AI
    /// smartness (combat essentially)". Measured on the build he had (twenty
    /// seeds, `simcs gunplay`), every minute of fighting:
    ///
    ///   279    shots, for 11.3 men killed; three shots in four had less than
    ///          three chances in a hundred, two in five less than one
    ///   71%    of shots went at a man one of the firer's own squad had fired
    ///          at within two seconds, and 66% while another enemy in reach had
    ///          nobody on him: a squad put all its fire on the nearest man
    ///   12%    of a squad's time in a fight its men were down under fire, and
    ///          7% flat: one man was kept on his face and the rest were free
    ///   11%    of aimed shots had a man to fire at with twice the chance or
    ///          more, most often one on his feet or running, further off
    ///   6.5    times a squad went in; two in three ended back in the same
    ///          firefight, one in five with the enemy dead or running
    ///   80%    of a sniper's rounds were fired at riflemen
    ///   24%    of an M79's rounds, and 12% of the rockets (a team carries
    ///          three), were fired at one man with nobody near him
    ///
    /// Every man fired at the nearest enemy his squad had in sight, at his
    /// rifle's rate, whatever the shot was worth; a squad went in when the
    /// enemy's pin, averaged, passed a line, which one flattened man in three
    /// took it past.
    ///
    /// The rules:
    ///
    ///   his man    a man fires at the enemy he is likeliest to hit, not the
    ///              nearest: the one up and running before the one flat behind
    ///              a wall. Each man of his squad already on a man makes him
    ///              worth half as much again less, so a squad's fire is spread
    ///              along the enemy's line and keeps more than one head down.
    ///              He stays on his man rather than change with every shot
    ///   the guns   a machine gun fires where the most heads are and moves on
    ///              from the man it has just fired at: it traverses. A sniper
    ///              looks for the crews first: a machine gun, a launcher, the
    ///              mortar, another sniper
    ///   covering   a rifleman's round with next to no chance is covering
    ///              fire, and he fires it at half his pace, saving his
    ///              magazine, until somebody of his needs it: his own squad
    ///              closing, going in, working up to going in or falling
    ///              back, or one beside it. Then everybody fires as fast as
    ///              he can
    ///   kept down  a man does not begin to get over fire while it is still
    ///              landing by him: fire that is kept up keeps a head down, and
    ///              it comes up a moment after the fire lifts (a reload, a
    ///              squad that gets up to move). One rifle firing at its own
    ///              pace does not do it; two do, or a machine gun
    ///   going in   a squad goes in when the enemy in front of it is beaten
    ///              down man by man (two in three of them with their heads
    ///              down), never onto a machine gun that is still firing and
    ///              never outnumbered; or when it has him three to one. It
    ///              works up to it: its fire quickens first. It goes in by
    ///              halves, one rushing while the other fires over it. Once
    ///              in it keeps going unless it is stopped; stopped, it does
    ///              not try again for twenty seconds
    ///   bursts     a launcher or the mortar fires where its round will find
    ///              the most men, and not at one rifleman by himself: a rocket
    ///              or a bomb is for a crew or a group, an M79's round also for
    ///              a man bullets cannot reach behind his cover. A grenade is
    ///              thrown where it will find the most, and never where it
    ///              would catch the thrower's own side
    ///
    /// No random draws, no transcendentals. Its state (the man each man is on)
    /// is hashed only when the rule is on: off, the match is what it was to
    /// the bit.
    /// </summary>
    public static class FireControl
    {
        /// <summary>A crew-served or special weapon: what a sniper looks for, and what a rocket is not wasted on.</summary>
        public static bool Crew(Weapon w)
            => w == Weapon.M60 || w == Weapon.Rpd || w == Weapon.Sniper || w == Weapon.M79 || w == Weapon.Rpg || w == Weapon.Mortar;

        private static bool Gun(Weapon w) => w == Weapon.M60 || w == Weapon.Rpd;

        [ThreadStatic] private static List<int> _marks;

        /// <summary>
        /// The man he fires at: of those in reach that his squad has in sight, the one worth most to him
        /// (<see cref="Worth"/>). A man in his own lane before one seen over the bank, as ever.
        /// </summary>
        public static Man Pick(SimState st, Man a)
        {
            var sq = st.Squads[a.Squad];
            double reach = Tactics.Reach(st, a), reach2 = reach * reach;
            // The men his squad is on now: each man's mark, if he has fired at him in the last few seconds.
            var marks = _marks ??= new List<int>(Tune.SquadMax);
            marks.Clear();
            for (int i = 0; i < st.Men.Count; i++)
            {
                var o = st.Men[i];
                if (o.Alive && o.Squad == a.Squad && o != a && o.Mark >= 0 && st.Tick - o.FiredAt <= Tune.MarkTicks) marks.Add(o.Mark);
            }

            Man best = null, across = null, sniper = null;
            double bestW = 0, acrossW = 0, sniperD2 = reach2;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var b = st.Men[i];
                if (!b.Alive || b.Side == a.Side || !Senses.Knows(st, sq, b.Squad)) continue;
                if (st.Gunnery && !Gunnery.InArc(st, a, b)) continue;
                double dx = a.X - b.X, dz = a.Z - b.Z, d2 = dx * dx + dz * dz;
                if (d2 > reach2 || Combat.SmokeBlocks(st, a.X, a.Z, b.X, b.Z)) continue;
                // Tactics: a sniper who has just fired has told everyone where he is.
                if (st.Tactics && b.Weapon == Weapon.Sniper && st.Tick - b.FiredAt <= Tune.SniperMarked && d2 <= sniperD2) { sniperD2 = d2; sniper = b; }
                double w = Worth(st, a, b, sq, marks);
                // Gunnery: a man in the other lane, seen over the bank, is shot at only when there is nobody in his own.
                if (st.Gunnery && st.Squads[b.Squad].Lane != sq.Lane) { if (w > acrossW) { acrossW = w; across = b; } }
                else if (w > bestW) { bestW = w; best = b; }
            }
            var pick = best ?? across;
            return sniper != null && Tactics.GoesForSniper(st, a, pick) ? sniper : pick;
        }

        /// <summary>
        /// What a man is worth firing at: the chance of hitting him, less for each of the firer's squad
        /// already on him, more for the man he is on himself, for one of the squad his own is dealing
        /// with, and for what his weapon is for.
        /// </summary>
        public static double Worth(SimState st, Man a, Man b, Squad sq, List<int> marks)
        {
            double w = Combat.HitChance(st, a, b);
            int on = 0;
            for (int i = 0; i < marks.Count; i++) if (marks[i] == b.Id) on++;
            w /= 1 + Tune.SpreadFire * on;
            if (b.Squad == sq.Threat) w *= Tune.ThreatFirst;
            if (Gun(a.Weapon))
            {
                // Where the most heads are; and on from the man it has just fired at.
                int heads = 0;
                double r2 = Tune.PinSplash * Tune.PinSplash;
                for (int i = 0; i < st.Men.Count; i++)
                {
                    var o = st.Men[i];
                    if (!o.Alive || o.Side != b.Side || o == b) continue;
                    double dx = o.X - b.X, dz = o.Z - b.Z;
                    if (dx * dx + dz * dz < r2) heads++;
                }
                w *= 1 + Tune.GunGroup * heads;
                if (a.Mark == b.Id) w *= Tune.Traverse;
            }
            else
            {
                if (a.Mark == b.Id) w *= Tune.HoldMark;
                if (a.Weapon == Weapon.Sniper && Crew(b.Weapon)) w *= Tune.SniperCrew;
            }
            return w;
        }

        /// <summary>Is this round covering fire: a rifleman's, with next to no chance of hitting?</summary>
        public static bool Covering(SimState st, Man a, double chance)
            => a.Weapon != Weapon.Sniper && !Ammo.Of(st, a).Automatic && chance < Tune.AimedFloor;

        private static bool Moving(Squad q) => q.Prep || q.Task == SquadTask.Close || q.Task == SquadTask.Assault || q.Task == SquadTask.Withdraw;

        /// <summary>
        /// Is covering fire wanted now: is his own squad moving under the enemy's fire (closing, going in,
        /// falling back) or working up to going in, or one of his side's beside it in his lane?
        /// </summary>
        public static bool Wanted(SimState st, Man a)
        {
            var sq = st.Squads[a.Squad];
            if (Moving(sq)) return true;
            for (int i = 0; i < st.Squads.Count; i++)
            {
                var o = st.Squads[i];
                if (o == sq || o.Side != sq.Side || o.Lane != sq.Lane || !Moving(o)) continue;
                if (Math.Abs(o.AnchorX - sq.AnchorX) > Tune.WordRange) continue;
                // (A squad with nobody left keeps the task it died in.)
                if (Squads.AnyAlive(st, o.Id)) return true;
            }
            return false;
        }

        /// <summary>The enemy in front of a squad, in its lane and within the assault's reach: how many, how many of them with their heads down, and whether a machine gun of his is still up.</summary>
        public static void Front(SimState st, Squad sq, out int enemies, out int down, out bool gunUp)
        {
            double dir = Combat.Advance(sq.Side);
            enemies = 0; down = 0; gunUp = false;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var e = st.Men[i];
                if (!e.Alive || e.Side == sq.Side || !Senses.Remembers(st, sq, e.Squad)) continue;
                if (Math.Abs(e.Z - Tune.Lanes[sq.Lane]) > 6.5) continue;
                double ahead = (e.X - sq.AnchorX) * dir;
                if (ahead < 0 || ahead > Tune.AssaultReach) continue;
                enemies++;
                if (e.Pin >= Tune.PinDrop) down++;
                else if (Gun(e.Weapon) && e.Reloading == 0) gunUp = true;
            }
        }

        /// <summary>
        /// Does it mean to go in? In a firefight it has had its breath in, its own heads up, and an enemy in
        /// front of it that does not outnumber it: it works up to it, pouring its fire on until he is down.
        /// </summary>
        public static bool MeansTo(SimState st, Squad sq, IReadOnlyList<Man> live, double pin, int held)
        {
            if (sq.Task != SquadTask.Firefight || !sq.Assaults || !sq.ThreatSeen || held < Tune.FirefightMin || pin >= Tune.PinDrop) return false;
            if (st.Tick - sq.StalledAt < Tune.AssaultAgain - Tune.FirefightMax) return false;
            Front(st, sq, out int enemies, out _, out _);
            return enemies > 0 && live.Count >= enemies;
        }

        /// <summary>
        /// Does it go in? The enemy in front of it (as <see cref="Senses"/> counts him: in its lane, ahead,
        /// within the assault's reach) is beaten down man by man, with no machine gun of his still up, and is
        /// not more than twice its number; or it has him three to one. A squad already going in
        /// (<paramref name="committed"/>) keeps going while the last two hold: it does not stop half way
        /// because a head has come up.
        /// </summary>
        public static bool GoesIn(SimState st, Squad sq, IReadOnlyList<Man> live, double pin, bool committed = false)
        {
            if (!sq.Assaults || !sq.ThreatSeen || pin >= Tune.PinDrop) return false;
            Front(st, sq, out int enemies, out int down, out bool gunUp);
            if (enemies == 0) return false;
            if (live.Count >= Tune.AssaultSure * enemies) return true;
            // (Going in already, it stops for a gun or for twice its number, not for having lost a man.)
            if (gunUp || live.Count < (committed ? Tune.AssaultFew * 0.5 : Tune.AssaultFew) * enemies) return false;
            if (committed) return true;
            // Stopped once, it does not go in again until it has had time to change something.
            if (st.Tick - sq.StalledAt < Tune.AssaultAgain) return false;
            return down >= Tune.AssaultDown * enemies;
        }

        /// <summary>
        /// The man he throws a grenade at: one in cover, in sight and in reach of his arm, as ever; of
        /// those, the one with the most of the enemy round him, and none of his own side near enough to
        /// be caught by it.
        /// </summary>
        public static Man Throw(SimState st, Man a)
        {
            double far = st.Arms ? Tune.ArmsFragRange : Tune.FragRange, near = st.Arms ? Tune.ArmsFragMin : Tune.FragMin;
            double max2 = far * far, min2 = near * near, r2 = Tune.FragRadius * Tune.FragRadius, safe = (Tune.FragRadius + 1) * (Tune.FragRadius + 1);
            Man target = null;
            int best = 0;
            double bestD2 = 0;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var b = st.Men[i];
                if (!b.Alive || !b.Seen || b.Side == a.Side || b.Cover < 0) continue;
                double dx = a.X - b.X, dz = a.Z - b.Z, d2 = dx * dx + dz * dz;
                if (d2 > max2 || d2 < min2 || Combat.SmokeBlocks(st, a.X, a.Z, b.X, b.Z)) continue;
                int men = 0;
                bool own = false;
                for (int j = 0; j < st.Men.Count; j++)
                {
                    var o = st.Men[j];
                    if (!o.Alive) continue;
                    double ox = o.X - b.X, oz = o.Z - b.Z, o2 = ox * ox + oz * oz;
                    if (o.Side == a.Side) { if (o2 < safe) { own = true; break; } }
                    else if (o2 < r2) men++;
                }
                if (own) continue;
                if (men > best || (men == best && d2 < bestD2)) { best = men; bestD2 = d2; target = b; }
            }
            return target;
        }

        /// <summary>
        /// The man a launcher or the mortar fires at: the one its round will find the most men round,
        /// and not one rifleman by himself. Null if there is nothing worth a round.
        /// </summary>
        public static Man Burst(SimState st, Man a, Arm arm)
        {
            Man target = null;
            double best = 0, bestD2 = 0, max2 = arm.Range * arm.Range, min2 = arm.MinRange * arm.MinRange, r2 = arm.Blast * arm.Blast;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var b = st.Men[i];
                if (!b.Alive || !b.Seen || b.Side == a.Side) continue;
                double dx = a.X - b.X, dz = a.Z - b.Z, d2 = dx * dx + dz * dz;
                if (d2 > max2 || d2 < min2) continue;
                // A mortar fires over smoke; a launcher has to see what it shoots at.
                if (a.Weapon != Weapon.Mortar && Combat.SmokeBlocks(st, a.X, a.Z, b.X, b.Z)) continue;
                int men = 0;
                bool own = false;
                double safe = (arm.Blast + 2) * (arm.Blast + 2);
                for (int j = 0; j < st.Men.Count; j++)
                {
                    var o = st.Men[j];
                    if (!o.Alive) continue;
                    double ox = o.X - b.X, oz = o.Z - b.Z, o2 = ox * ox + oz * oz;
                    if (o.Side == a.Side) { if (o2 < safe) { own = true; break; } }
                    else if (o2 < r2) men++;
                }
                if (own) continue;
                bool crew = Crew(b.Weapon);
                // One man by himself: a crew is worth it; so, to an M79, is a man bullets cannot reach.
                if (men == 1 && !crew && !(a.Weapon == Weapon.M79 && b.Cover >= 0)) continue;
                double worth = men + (crew ? 1 : 0) + (b.Cover >= 0 ? 0.5 : 0);
                if (worth > best || (worth == best && d2 < bestD2)) { best = worth; bestD2 = d2; target = b; }
            }
            return target;
        }
    }
}
