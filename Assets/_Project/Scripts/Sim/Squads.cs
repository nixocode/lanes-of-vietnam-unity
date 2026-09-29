using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    public struct Slot { public float X; public float Z; }

    /// <summary>
    /// Squads: who is in one, where each man should stand, and the anchor that
    /// makes the squad go anywhere.
    ///
    /// Three of the four functions here carry a bug that cost real time in the
    /// previous build, written down so it is not rediscovered.
    /// </summary>
    public static class Squads
    {
        /// <summary>
        /// The living men of a squad, stable by id.
        ///
        /// Sorted, because the simulation must not depend on array order
        /// changing underneath it — that is a determinism leak that only shows
        /// up after a few hundred ticks.
        /// </summary>
        public static List<Man> Roster(SimState st, int squadId)
        {
            var outv = new List<Man>();
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                if (m.Squad == squadId && m.Alive) outv.Add(m);
            }
            outv.Sort((a, b) => a.Id.CompareTo(b.Id));
            return outv;
        }

        /// <summary>
        /// Where each man should stand, given who is actually alive.
        ///
        /// Strung out along the lane, one man per rank, staggered either side
        /// of the axis. The obvious wedge — two men per rank separated only in
        /// Z — is a sensible formation and reads terribly from this camera:
        /// the lens looks down the Z axis, so a pair differing only in depth
        /// lands on top of itself and a squad renders as one overlapping
        /// clump. In a lane game the formation's long axis and the camera's
        /// long axis are the same axis, and the spacing has to live on it.
        ///
        /// Recomputed every tick from <paramref name="live"/>, so a four-man
        /// squad that loses two re-forms as a two-man squad rather than
        /// leaving two holes and a displaced anchor.
        /// </summary>
        public static void SlotsFor(Squad sq, IReadOnlyList<Man> live,
                                    float anchorX, float anchorZ,
                                    List<Slot> into)
        {
            into.Clear();
            float dir = Combat.Advance(sq.Side);
            for (int i = 0; i < live.Count; i++)
            {
                into.Add(new Slot
                {
                    X = anchorX - dir * i * Tune.SlotGap,
                    Z = anchorZ + (i % 2 == 0 ? -1f : 1f) * (0.7f + (i % 3) * 0.35f),
                });
            }
        }

        /// <summary>
        /// Correct a squad's anchor against its living men.
        ///
        /// The anchor is <b>persistent state that orders move</b> — see
        /// <see cref="March"/> — and this only leashes it to the men. That
        /// split is the whole point.
        ///
        /// Snapping the anchor onto the lead man instead, which is the obvious
        /// implementation, looks like it respects that and does not: slot 0
        /// sits on the anchor, so the lead man is always already in his slot,
        /// nothing pulls him forward, and the squad never advances. Measured
        /// in the previous build: floor against floor ran twelve matches to
        /// the twelve-minute cap with zero casualties, because neither side
        /// ever moved. The ceiling plan hid it by walking to cover.
        /// </summary>
        public static void Reanchor(Squad sq, IReadOnlyList<Man> live)
        {
            if (live.Count == 0) return;
            float dir = Combat.Advance(sq.Side);
            float sz = 0, lead = float.NegativeInfinity, rear = float.PositiveInfinity;
            for (int i = 0; i < live.Count; i++)
            {
                var m = live[i];
                sz += m.Z;
                float forward = m.X * dir;
                if (forward > lead) lead = forward;
                if (forward < rear) rear = forward;
            }

            // Z is leashed, not overwritten.
            //
            // Setting it to the men's mean every tick erased the Z component
            // of a march toward cover before the squad could ever arrive: the
            // anchor crept sideways a fraction of a metre a tick and never got
            // there, so a cover-using squad wandered out of its own lane —
            // measured drifting from z 7.0 to 15.2 while never reaching the
            // cover it had been targeting all match.
            float meanZ = sz / live.Count;
            sq.AnchorZ = Math.Max(meanZ - Tune.AnchorLeash,
                         Math.Min(meanZ + Tune.AnchorLeash, sq.AnchorZ));

            float ahead = sq.AnchorX * dir;
            float clamped = Math.Min(lead + Tune.AnchorLeash, Math.Max(rear, ahead));
            sq.AnchorX = clamped * dir;
        }

        /// <summary>
        /// Move a squad's anchor under its order. The men chase their slots;
        /// this is the only thing that makes a squad go anywhere.
        /// </summary>
        public static void March(Squad sq, bool hasTarget, float towardX, float towardZ)
        {
            float dir = Combat.Advance(sq.Side);
            if (sq.Order == Order.Hold) return;

            if (sq.Order == Order.Fallback)
            {
                sq.AnchorX -= dir * Tune.MarchSpeed * Tune.Dt;
            }
            else if (hasTarget)
            {
                // Making for cover: the *anchor* goes there and the men keep
                // their slots around it.
                //
                // Sending each man to the cover point itself threw the
                // formation away every time a squad used cover — it collapsed
                // onto one spot, tripped its own crowding penalty so the cover
                // was worth nothing, and bunched up for splash suppression.
                // Measured by ablation against the floor plan: `useCover`
                // alone took a side from 54% to 27%. Using cover made you
                // lose, which is not a tuning value, it is a bug.
                float dx = towardX - sq.AnchorX, dz = towardZ - sq.AnchorZ;
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (d > 0.05f)
                {
                    float stepLen = Math.Min(d, Tune.MarchSpeed * Tune.Dt);
                    sq.AnchorX += (dx / d) * stepLen;
                    sq.AnchorZ += (dz / d) * stepLen;
                }
            }
            else if (sq.Order == Order.Advance || sq.Order == Order.Bound)
            {
                sq.AnchorX += dir * Tune.MarchSpeed * Tune.Dt;
            }

            sq.AnchorX = Math.Max(-Tune.HalfLength,
                         Math.Min(Tune.HalfLength, sq.AnchorX));
        }

        /// <summary>
        /// Has this man made measured progress recently?
        ///
        /// Not "moved this tick", which is true on any sub-pixel jitter and
        /// leaves every stance lock permanently bypassed — that was a real bug
        /// in the 2D game this descends from. This asks whether he has covered
        /// <see cref="Tune.MoveEpsilon"/> metres across the whole window.
        /// </summary>
        public static bool IsMoving(Man m)
        {
            if (m.Trail.Count < Tune.MoveWindow) return false;
            float first = m.Trail[0];
            float last = m.Trail[m.Trail.Count - 1];
            return Math.Abs(last - first) >= Tune.MoveEpsilon;
        }

        public static void PushTrail(Man m)
        {
            m.Trail.Add(m.X);
            if (m.Trail.Count > Tune.MoveWindow) m.Trail.RemoveAt(0);
        }
    }
}
