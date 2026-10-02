using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    public struct Slot { public double X; public double Z; }

    /// <summary>
    /// Squads: who is in one, where each man should stand, and the anchor that
    /// makes the squad go anywhere.
    ///
    /// Brief §9 finding 1 is the expensive one and it lives entirely here. The
    /// 2D game assigned a man his slot when he spawned and never revised it as
    /// the squad took casualties, while the anchor was the mean of the living.
    /// Lone survivors walked off the map (traced to x = 12091 in a 2560-wide
    /// world). So: <b>slots are recomputed from the live roster every tick and
    /// never stored on a man.</b>
    /// </summary>
    public static class Squads
    {
        /// <summary>
        /// The living men of a squad, stable by id. Sorted, because the
        /// simulation must not depend on list order changing underneath it.
        /// </summary>
        public static List<Man> Roster(SimState st, int squadId)
        {
            var outv = new List<Man>(Tune.SquadMax);
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                if (m.Squad == squadId && m.Alive) outv.Add(m);
            }
            outv.Sort((a, b) => a.Id.CompareTo(b.Id));
            return outv;
        }

        /// <summary>Has this squad anyone left? (A question the view asks every frame: no list is made to answer it.)</summary>
        public static bool AnyAlive(SimState st, int squadId)
        {
            for (int i = 0; i < st.Men.Count; i++)
                if (st.Men[i].Squad == squadId && st.Men[i].Alive) return true;
            return false;
        }

        /// <summary>
        /// Where each man should stand, given who is actually alive.
        ///
        /// Strung out along the lane, one man per rank, staggered either side
        /// of the axis. The obvious wedge — two men per rank separated only in
        /// Z — reads terribly from this camera: the lens looks down Z, so a
        /// pair differing only in depth lands on top of itself and a squad
        /// renders as one clump. In a lane game the formation's long axis and
        /// the camera's long axis are the same axis.
        /// </summary>
        public static void SlotsFor(Squad sq, IReadOnlyList<Man> live,
                                    double anchorX, double anchorZ, List<Slot> into)
        {
            into.Clear();
            double dir = Combat.Advance(sq.Side);
            for (int i = 0; i < live.Count; i++)
            {
                into.Add(new Slot
                {
                    X = anchorX - dir * i * Tune.SlotGap,
                    Z = anchorZ + (i % 2 == 0 ? -1 : 1) * (0.7 + (i % 3) * 0.35),
                });
            }
        }

        /// <summary>
        /// Correct a squad's anchor against its living men.
        ///
        /// The anchor is persistent state that orders move (<see cref="March"/>)
        /// and this only leashes it. Snapping it onto the lead man instead —
        /// the obvious implementation — means slot 0 always sits on the lead
        /// man, nothing pulls him forward, and the squad never advances:
        /// floor against floor ran twelve matches to the cap with zero
        /// casualties.
        /// </summary>
        public static void Reanchor(Squad sq, IReadOnlyList<Man> live)
        {
            if (live.Count == 0) return;
            double dir = Combat.Advance(sq.Side);
            double sz = 0, lead = double.NegativeInfinity, rear = double.PositiveInfinity;
            for (int i = 0; i < live.Count; i++)
            {
                var m = live[i];
                sz += m.Z;
                double forward = m.X * dir;
                if (forward > lead) lead = forward;
                if (forward < rear) rear = forward;
            }

            // Z is leashed, not overwritten. Setting it to the men's mean
            // every tick erased the Z of a march toward cover before the squad
            // could arrive: measured drifting from z 7.0 to 15.2 while never
            // reaching the cover it had targeted all match.
            double meanZ = sz / live.Count;
            sq.AnchorZ = Math.Max(meanZ - Tune.AnchorLeash,
                                  Math.Min(meanZ + Tune.AnchorLeash, sq.AnchorZ));

            double ahead = sq.AnchorX * dir;
            double clamped = Math.Min(lead + Tune.AnchorLeash, Math.Max(rear, ahead));
            sq.AnchorX = clamped * dir;
        }

        /// <summary>
        /// Move a squad's anchor under its order. The men chase their slots;
        /// this is the only thing that makes a squad go anywhere.
        /// </summary>
        public static void March(Squad sq, Cover toward)
        {
            double dir = Combat.Advance(sq.Side);
            if (sq.Order == Order.Hold) return;

            if (sq.Order == Order.Fallback)
            {
                sq.AnchorX -= dir * Tune.MarchSpeed * Tune.Dt;
            }
            else if (toward != null)
            {
                // Making for cover: the anchor goes there and the men keep
                // their slots around it. Sending each man to the cover point
                // collapsed the squad onto one spot, tripped its own crowding
                // penalty and bunched it for splash suppression — by ablation,
                // using cover took a side from 54% to 27%.
                double dx = toward.X - sq.AnchorX, dz = toward.Z - sq.AnchorZ;
                double d = JsMath.Hypot(dx, dz);
                if (d > 0.05)
                {
                    double stepLen = Math.Min(d, Tune.MarchSpeed * Tune.Dt);
                    sq.AnchorX += (dx / d) * stepLen;
                    sq.AnchorZ += (dz / d) * stepLen;
                }
            }
            else if (sq.Order == Order.Advance || sq.Order == Order.Bound)
            {
                sq.AnchorX += dir * Tune.MarchSpeed * Tune.Dt;
            }

            sq.AnchorX = Math.Max(-Tune.HalfLength, Math.Min(Tune.HalfLength, sq.AnchorX));
        }

        /// <summary>
        /// Has this man made measured progress recently? Not "moved this
        /// tick", which is true on any sub-pixel jitter (§9 finding 2).
        /// </summary>
        public static bool IsMoving(Man m)
        {
            if (m.Trail.Count < Tune.MoveWindow) return false;
            double first = m.Trail[0];
            double last = m.Trail[m.Trail.Count - 1];
            return Math.Abs(last - first) >= Tune.MoveEpsilon;
        }

        public static void PushTrail(Man m)
        {
            m.Trail.Add(m.X);
            if (m.Trail.Count > Tune.MoveWindow) m.Trail.RemoveAt(0);
        }
    }
}
