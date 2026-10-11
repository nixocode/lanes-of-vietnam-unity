using System;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Bearing (Part 2, behind <see cref="MatchOptions.Bearing"/>): how a man
    /// carries himself from one second to the next.
    ///
    /// The owner, 2026-10-11, of the build with every rule to that day:
    /// "improve gameplay. Make it smooth like real life." Measured (`simcs
    /// smooth`, new: twenty matches, every man followed tick by tick), a man,
    /// a minute of the fight:
    ///
    ///   12.5   changes of posture, four in ten of them inside two seconds of
    ///          the last. A man who went flat stayed flat 1.4 s (the middle
    ///          one), and one on a knee 2.2
    ///   1.9    times down, up and down again inside three seconds, most of it
    ///          a pinned man going from flat to a knee and back as his fear
    ///          crossed one line
    ///   0.87   times on his feet and down again having gone nowhere
    ///   0.89   moves of under a metre and a half
    ///   2.4    changes of half a metre a second or more in his speed between
    ///          two steps of one move, and every move begun and ended at full
    ///          speed
    ///
    /// Traced to single men, they were these. A man flat in the open was told
    /// onto a knee whenever his squad's anchor moved and flat again when it
    /// stopped, though he had not stirred. A man put flat by fire came up the
    /// moment his fear dipped under the line and went down at the next round.
    /// A man got to his feet in the last second of his half's turn to move,
    /// and knelt again when it ended. A man in a trench stood behind the man
    /// in front for the four seconds of his turn, every turn, because he
    /// could not pass him.
    ///
    /// The rules:
    ///
    ///   down is down    a man who is flat stays flat until he gets up to go.
    ///                   Only a man with a place in cover comes back onto a
    ///                   knee to fire over it, and only when the fire has let
    ///                   him be and he has been flat three seconds; a man
    ///                   behind a machine gun or a scope stays behind it. A
    ///                   man going to ground in the open where his squad is
    ///                   fighting from lies flat at once, not by way of a
    ///                   knee; and out of contact a halt of under two seconds
    ///                   is not worth a knee at all
    ///   up is to go     a man gets to his feet for a move he can make now:
    ///                   when he has been down two seconds, no round has come
    ///                   near him for one, there is enough of his half's turn
    ///                   left to get up and cover three metres in, and no man
    ///                   of his own at rest bars the way. One who is up and
    ///                   barred gets down where he is. Once on his feet he has
    ///                   his run, though his half's turn ends under him
    ///   for cover       a man lying in the open within six metres of his
    ///                   place in cover waits for neither his turn nor the
    ///                   fire to let up: the fire is why he goes
    ///   pace            a man gathers pace over his first half second and
    ///                   slows into his place; a change of pace is taken over
    ///                   a few steps, not in one; he goes round a man of his
    ///                   own at his own pace, and does not wait his turn again
    ///                   on the far side of a parapet he has just climbed
    ///
    /// Built, measured and left out: a man not getting up for a place a man
    /// of his own was resting on. Men were up for nothing no less often, and
    /// the American player won 89 matches of 192 against a Veteran where he
    /// won 113 without it (`simcs scenario firebase 192 level=veteran`): his
    /// squads, raised one behind another, lay behind each other's places.
    ///
    /// It draws no randomness. Its state (a man's pace, how long he has been
    /// barred) is hashed only when the rule is on: off, the match is what it
    /// was to the bit.
    /// </summary>
    public static class Bearing
    {
        /// <summary>Behind a machine gun or a scope: he fights flat.</summary>
        private static bool Gun(SimState st, Man m)
            => st.Arms && (m.Weapon == Weapon.M60 || m.Weapon == Weapon.Rpd || m.Weapon == Weapon.Sniper);

        /// <summary>
        /// How he carries himself, given what the rules before this one wanted (<paramref name="want"/>) and how
        /// he is now: a man who is flat is not got onto a knee and put flat again. (<paramref name="free"/>:
        /// neither running from the fight, going for a man with his hands, nor a sapper coming up unseen.)
        /// </summary>
        public static Posture Keep(SimState st, Man m, Squad sq, Posture want, bool free)
        {
            if (!st.Bearing || !free || want == m.Posture) return want;
            if (m.Posture == Posture.Standing && want == Posture.Crouched)
            {
                // Out of contact a short halt is not worth a knee: he stands a moment first.
                if (Quiet(st, sq) && m.Pin < Tune.PinDrop && m.Rest < Tune.RestToKneelQuiet) return Posture.Standing;
                // Going to ground in the open where his squad is fighting from: flat, and not a knee first.
                if (m.Cover < 0 && Fighting(st, sq)) return Posture.Prone;
                return want;
            }
            if (m.Posture != Posture.Prone || want != Posture.Crouched) return want;
            // The fire has not let him be.
            if (m.Pin >= Tune.PinDrop) return Posture.Prone;
            if (m.Dwell < Tune.StayFlat) return Posture.Prone;
            if (Gun(st, m)) return Posture.Prone;
            // Onto a knee to fire over the cover he has a place in; or with no enemy to lie flat for.
            bool atPlace = m.Cover >= 0 && m.Place >= 0 && m.PlaceCover == m.Cover;
            return atPlace || Quiet(st, sq) ? Posture.Crouched : Posture.Prone;
        }

        /// <summary>On the march with no enemy it is dealing with.</summary>
        private static bool Quiet(SimState st, Squad sq) => st.Senses && sq.Task == SquadTask.March && !Senses.InContact(sq);

        /// <summary>Gone to ground and fighting from where it is (not closing, going in or running).</summary>
        private static bool Fighting(SimState st, Squad sq)
            => st.Senses && (sq.Task == SquadTask.Contact || sq.Task == SquadTask.Firefight || sq.Task == SquadTask.Regroup);

        /// <summary>
        /// Down, with a place to get up and go to (<paramref name="tx"/>, <paramref name="tz"/>): does he stay
        /// down for now? He does when there is too little of his half's turn left to get up and cover ground
        /// in, or a man of his own bars the way.
        /// </summary>
        public static bool StaysDown(SimState st, Squad sq, Man m, double tx, double tz, bool toCover = false) => WhyDown(st, sq, m, tx, tz, toCover) != Idle.No;

        /// <summary>
        /// In the open, with a place in cover a few paces off (<paramref name="away"/> metres): he makes for it
        /// when he can. He does not wait his half's turn for it, nor for the fire to let up: the fire is why.
        /// </summary>
        public static bool ForCover(Man m, double away, bool toCover) => toCover && m.Cover < 0 && away <= Tune.BoundFinishCover;

        /// <summary>The same, and which of them it is (<see cref="Idle.No"/>: he gets up).</summary>
        public static Idle WhyDown(SimState st, Squad sq, Man m, double tx, double tz, bool toCover = false)
        {
            if (!st.Bearing || m.Posture == Posture.Standing || sq.Order == Order.Fallback) return Idle.No;
            double away = JsMath.Hypot(tx - m.X, tz - m.Z);
            if (away <= Tune.GetUpBeyond) return Idle.No;
            // He has only just got down.
            if (m.Dwell < Tune.StayDown) return Idle.JustDown;
            bool forCover = ForCover(m, away, toCover);
            // Rounds are landing by him.
            if (!forCover && st.FireControl && st.Tick - m.UnderAt <= Tune.RiseClear) return Idle.UnderFire;
            if (!forCover && Tactics.Bounds(st, sq) && !(st.Cohesion && sq.Task == SquadTask.Assault))
            {
                int left = Tune.BoundTicks - (st.Tick + sq.Id * 13) % Tune.BoundTicks;
                int turn = m.Wait > 0 ? m.Wait : m.Wait == 0 ? Gunnery.Stagger(m) : 0;
                int need = Math.Max(0, Tune.PostureDwell - m.Dwell) + turn + Gunnery.RiseTicks(m.Posture) + Tune.BoundWorth;
                if (left < need) return Idle.TurnEnding;
            }
            // A man of his own is in the way.
            return Fieldcraft.Barred(st, m, tx, tz, Gunnery.Pace(sq)) ? Idle.Blocked : Idle.No;
        }

        /// <summary>
        /// On his feet and going to (<paramref name="tx"/>, <paramref name="tz"/>): is he barred, and has he
        /// been for longer than a moment? Then he does not stand there: he is where he is going, for now.
        /// </summary>
        public static bool Balked(SimState st, Squad sq, Man m, double tx, double tz)
        {
            if (!st.Bearing || m.Posture != Posture.Standing || sq.Order == Order.Fallback) { m.Balk = 0; return false; }
            if (JsMath.Hypot(tx - m.X, tz - m.Z) <= Tune.SetOff || !Fieldcraft.Barred(st, m, tx, tz, Gunnery.Pace(sq))) { m.Balk = 0; return false; }
            if (m.Balk < Tune.BalkTicks) m.Balk++;
            return m.Balk >= Tune.BalkTicks;
        }

        /// <summary>
        /// The step he takes this tick toward a place <paramref name="d"/> metres off, at a pace of
        /// <paramref name="speed"/>: he gathers pace, takes a change of pace over a few steps, and slows into
        /// his place.
        /// </summary>
        public static double Stride(Man m, double speed, double d)
        {
            double v = m.Pace < speed ? Math.Min(speed, m.Pace + Tune.Gather * Tune.Dt) : Math.Max(speed, m.Pace - Tune.Ease * Tune.Dt);
            double into = Math.Sqrt(2 * Tune.Ease * d);
            v = Math.Min(v, Math.Max(Tune.PaceLeast, into));
            double step = Math.Min(d, v * Tune.Dt);
            m.Pace = step / Tune.Dt;
            return step;
        }
    }
}
