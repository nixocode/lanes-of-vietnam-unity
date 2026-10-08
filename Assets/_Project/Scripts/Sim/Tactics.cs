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
    ///   125    man-seconds at rest in the open within four metres of cover
    ///          that had a place free
    ///
    /// Traced to single men, they were these. A squad sees the enemy at 28 m
    /// and its rifles reach 20: both sides lie and look at each other, then
    /// both get up and run to 12 m, and nobody fires on the move. A squad that
    /// stops short chooses its cover from its anchor, which runs three metres
    /// ahead of its lead man, so the cover its men are standing in is "behind
    /// it" and is passed over: they lie down in the open in front of it. A man
    /// passing through cover with an enemy four metres off goes for him out of
    /// it and waits for him in it, and at its edge does each on alternate
    /// ticks. And a sniper team fights from 32 m, where no rifle can answer
    /// it, until a rifle squad walks up to its own 13.
    ///
    /// The rules:
    ///
    ///   long fire  a rifle, a machine gun or a submachine gun fires at an
    ///              enemy its squad has in sight out to half as far again as
    ///              its own distance (ten metres further at most). Beyond its
    ///              own distance a round seldom hits and pins less, and he
    ///              fires them slower. A squad opens fire when it sees the enemy
    ///   bounds     in contact, with the enemy in sight, a squad moves by
    ///              halves: the odd ranks go while the even ones fire, four
    ///              seconds at a time. A man who is nearly at his place
    ///              finishes his run. Falling back, everybody runs
    ///   the rush   a squad going in comes on by bounds and rushes only the
    ///              last ten metres (it was twenty-four, at a run, in silence)
    ///   ground     a squad that goes to ground does it where its lead man is,
    ///              not where its anchor has got to, and in the cover most of
    ///              it is already standing in if there is one
    ///   at hand    a man waits in cover for an enemy to come the last three
    ///              metres only if he has a place in it; passing through, he
    ///              goes for him
    ///   keep off   a team that fights from a long way off and does not go in
    ///              (a sniper, the mortar) gives ground, to the cover behind
    ///              it, when an enemy it knows of comes inside its distance.
    ///              A sniper's distance is 28 m: inside a rifle's long shot,
    ///              which pins him and all but never kills him, and a machine
    ///              gun's and another sniper's proper reach
    ///   grenades   a man carries one, a sapper or an engineer two, and a
    ///              squad that has thrown waits ten seconds before it throws
    ///              again
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
        /// (<paramref name="tx"/>, <paramref name="tz"/>: where he would be going.)
        /// </summary>
        public static bool Waits(SimState st, Squad sq, Man m, double tx, double tz)
        {
            if (!Bounds(st, sq)) return false;
            double away = JsMath.Hypot(tx - m.X, tz - m.Z);
            if (away <= Tune.SetOff) return false;
            // On his feet, running, and nearly there: he does not stop two paces short of his place.
            if (m.Posture == Posture.Standing && m.Rest == 0 && away <= Tune.BoundFinish) return false;
            return ((st.Tick + sq.Id * 13) / Tune.BoundTicks + m.Rank) % 2 != 0;
        }

        // --- ground -------------------------------------------------------------------

        /// <summary>The squad stops where its lead man is: the anchor comes back to him.</summary>
        public static void HaltAtLead(Squad sq, IReadOnlyList<Man> live)
        {
            if (live.Count == 0) return;
            double dir = Combat.Advance(sq.Side), lead = double.NegativeInfinity;
            for (int i = 0; i < live.Count; i++) lead = Math.Max(lead, live[i].X * dir);
            if (sq.AnchorX * dir > lead) sq.AnchorX = lead * dir;
        }

        /// <summary>The cover half the squad or more is standing in now, or -1.</summary>
        public static int StandsIn(SimState st, IReadOnlyList<Man> live)
        {
            int best = -1, most = 0;
            for (int i = 0; i < live.Count; i++)
            {
                int c = live[i].Cover;
                if (c < 0 || c == best) continue;
                int n = 0;
                for (int j = 0; j < live.Count; j++) if (live[j].Cover == c) n++;
                if (n > most) { most = n; best = c; }
            }
            return most * 2 >= live.Count ? best : -1;
        }

        // --- keep off -----------------------------------------------------------------

        /// <summary>Is this a team that fights from a long way off and does not go in: a sniper, the mortar?</summary>
        public static bool FarTeam(SimState st, Squad sq) => st.Tactics && st.Arms && !sq.Assaults && sq.Reach >= Tune.FarTeam;

        /// <summary>An enemy it knows of has come inside its distance: it is time to give ground.</summary>
        public static bool Pressed(SimState st, Squad sq)
            => FarTeam(st, sq) && sq.Threat >= 0 && sq.ThreatSeen && sq.ThreatGap < sq.Reach - Tune.KeepOff;

        // --- grenades -----------------------------------------------------------------

        /// <summary>Grenades a man comes on with, by what he carries.</summary>
        public static int Grenades(SimState st, Weapon w)
            => !st.Tactics ? Tune.GrenadesCarried : w == Weapon.Smg ? Tune.TacticsGrenadesClose : Tune.TacticsGrenades;

        /// <summary>Ticks a squad that has thrown waits before it throws again.</summary>
        public static int ThrowGap(SimState st) => st.Tactics ? Tune.TacticsThrowGap : Tune.SquadThrowGap;
    }
}
