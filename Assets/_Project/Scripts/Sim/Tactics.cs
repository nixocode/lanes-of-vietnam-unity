using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Tactics (Part 2, behind <see cref="MatchOptions.Tactics"/>): what a
    /// squad does with the enemy it has found.
    ///
    /// The owner, playtest 9: "AI brain needs work: sometimes they still do
    /// weird circles, don't fight each other, weird non-logical positions when
    /// fighting", "too many grenades", and "distances for snipers need
    /// tailoring: they should be able to be safe but prone to suppression from
    /// the gunner/squads and other snipers". Measured on that build (six
    /// seeds, `simcs brain`), every minute:
    ///
    ///   139    squad-seconds of two enemy squads within 30 m in one lane with
    ///          no fire between them, against 118 fighting
    ///   173    man-seconds of a man with a target and able to fire who had not
    ///          fired for four seconds (36% of his time), most of it running
    ///   7.9    men whose direction of travel turned through a whole circle
    ///   13.7   grenades thrown, and 47% of all deaths from a burst
    ///
    /// Traced to single men, they were these. A squad sees the enemy at 28 m
    /// and its rifles reach 20: both sides lie and look at each other, then
    /// both get up and run to 12 m, and nobody fires on the move. A squad going
    /// in runs the last 24 m in silence. A man passing through cover with an
    /// enemy four metres off goes for him out of it and waits for him in it,
    /// and at its edge does each on alternate ticks. And a sniper team fights
    /// from 32 m, where no rifle can answer it, until a rifle squad walks up
    /// to its own 13.
    ///
    /// The rules:
    ///
    ///   long fire  a rifle, a machine gun or a submachine gun fires at an
    ///              enemy its squad has in sight out to half as far again as
    ///              its own distance (eight metres further at most). Beyond its
    ///              own distance a round seldom hits and pins less, and he
    ///              fires them slowly. A squad opens fire when it sees the enemy
    ///   bounds     in contact, with the enemy in sight, a squad moves by
    ///              halves: the even ranks go while the odd ones fire, four
    ///              seconds at a time. A man who is nearly at his place
    ///              finishes his run. Falling back, everybody runs
    ///   the rush   a squad going in comes on by bounds and rushes only the
    ///              last ten metres
    ///   at hand    a man waits in cover for an enemy to come the last three
    ///              metres only if he has a place in it; passing through, he
    ///              goes for him
    ///   keep off   a team that fights from a long way off and does not go in
    ///              (a sniper, the mortar) gives ground, to the cover behind
    ///              it, when an enemy it knows of comes inside its distance.
    ///              A sniper's distance is 26 m: inside a rifle's long shot,
    ///              which pins him and all but never kills him, and a machine
    ///              gun's and another sniper's proper reach. For six seconds
    ///              after he fires he is the man a machine gun and another
    ///              sniper shoot at, and a rifleman with nobody nearer, and
    ///              at him a long shot is fired as fast and pins as much as any
    ///   grenades   a man carries one, a sapper or an engineer two, and a
    ///              squad that has thrown waits fifteen seconds before it throws
    ///              again
    ///   the file   a new squad's anchor starts a leash ahead of its lead man,
    ///              so most of the file sets off at once; and a man waiting
    ///              for his file to come up to him stands where he is
    ///
    /// Three more were built, measured and left out (twenty seeds the rule was
    /// not tuned on, each added to the rules above): choosing a squad's cover
    /// from its lead man instead of its anchor; a man with no place in his
    /// squad's cover taking one in cover nearby; and a man ahead of his place
    /// in cover walking back to it. Each sent men back the way they had come
    /// and out again four seconds later: men turning about went from 7.0 a
    /// minute to between 7.3 and 39.
    ///
    /// No random draws, no transcendentals, and no state of its own: off, the
    /// match is what it was to the bit.
    /// </summary>
    public static class Tactics
    {
        // --- long fire ----------------------------------------------------------------

        /// <summary>How far he engages an enemy his squad has in sight: his weapon's distance, and under this rule a long shot beyond it.</summary>
        public static double Reach(SimState st, Man a)
        {
            var arm = Arms.Of(st, a);
            if (!st.Tactics || arm.Bursts || a.Weapon == Weapon.Sniper) return arm.Range;
            return Math.Min(arm.Range * Tune.LongFire, arm.Range + Tune.LongMost);
        }

        /// <summary>Is this a long shot: at a man beyond his weapon's own distance?</summary>
        public static bool Long(SimState st, Man a, Man b)
        {
            if (!st.Tactics) return false;
            double r = Arms.Of(st, a).Range, dx = a.X - b.X, dz = a.Z - b.Z;
            return dx * dx + dz * dz > r * r;
        }

        // --- bounds -------------------------------------------------------------------

        /// <summary>Does this squad move by halves now: in contact, its enemy in sight, and not running from him?</summary>
        public static bool Bounds(SimState st, Squad sq)
            => st.Tactics && st.Senses && st.Gunnery && sq.Order != Order.Fallback && sq.ThreatSeen
               && (sq.Task == SquadTask.Firefight || sq.Task == SquadTask.Close || sq.Task == SquadTask.Assault || sq.Task == SquadTask.Regroup);

        /// <summary>
        /// Is it the other half's turn to move, so that this man stays where he is and fires?
        /// (<paramref name="tx"/>, <paramref name="tz"/>: where he would be going, and whether that is a place in cover.)
        /// </summary>
        public static bool Waits(SimState st, Squad sq, Man m, double tx, double tz, bool toCover)
        {
            if (!Bounds(st, sq)) return false;
            double away = JsMath.Hypot(tx - m.X, tz - m.Z);
            if (away <= Tune.SetOff) return false;
            // On his feet, running, and nearly there: he does not stop two paces short of his place.
            // (Further, when it is a place in cover he is running for: he does not kneel in the open six metres from a wall.)
            if (m.Posture == Posture.Standing && m.Rest == 0 && away <= (toCover ? Tune.BoundFinishCover : Tune.BoundFinish)) return false;
            return !Turn(st, sq, m);
        }

        /// <summary>Whose turn it is to move: the even ranks', then the odd ranks', a bound at a time.</summary>
        public static bool Turn(SimState st, Squad sq, Man m) => ((st.Tick + sq.Id * 13) / Tune.BoundTicks + m.Rank) % 2 == 0;

        // --- keep off -----------------------------------------------------------------

        /// <summary>Is this a team that fights from a long way off and does not go in: a sniper, the mortar?</summary>
        public static bool FarTeam(SimState st, Squad sq) => st.Tactics && st.Arms && !sq.Assaults && sq.Reach >= Tune.FarTeam;

        /// <summary>An enemy it knows of has come inside its distance: it is time to give ground.</summary>
        public static bool Pressed(SimState st, Squad sq)
            => FarTeam(st, sq) && sq.Threat >= 0 && sq.ThreatSeen && sq.ThreatGap < sq.Reach - Tune.KeepOff;

        /// <summary>
        /// Does he leave the man he would have fired at for an enemy sniper who has just given himself away? A
        /// machine gun and a sniper do; a rifleman does when he has nobody inside his rifle's own distance.
        /// (A sniper lay 28 m off, pinned 1% of the time, and was killed only by another sniper.)
        /// </summary>
        public static bool GoesForSniper(SimState st, Man a, Man otherwise)
        {
            if (a.Weapon == Weapon.M60 || a.Weapon == Weapon.Rpd || a.Weapon == Weapon.Sniper || otherwise == null) return true;
            double r = Arms.Of(st, a).Range, dx = a.X - otherwise.X, dz = a.Z - otherwise.Z;
            return dx * dx + dz * dz > r * r;
        }

        // --- grenades -----------------------------------------------------------------

        /// <summary>Grenades a man comes on with, by what he carries.</summary>
        public static int Grenades(SimState st, Weapon w)
            => !st.Tactics ? Tune.GrenadesCarried : w == Weapon.Smg ? Tune.TacticsGrenadesClose : Tune.TacticsGrenades;

        /// <summary>Ticks a squad that has thrown waits before it throws again.</summary>
        public static int ThrowGap(SimState st) => st.Tactics ? Tune.TacticsThrowGap : Tune.SquadThrowGap;
    }
}
