using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Fieldcraft (Part 2, behind <see cref="MatchOptions.Fieldcraft"/>): men
    /// who fight as a squad instead of as a queue.
    ///
    /// The owner, playtest 3: "they run circles around themselves, cross each
    /// other. Make them fight like it's war." Measured as the game played it
    /// (six seeds, 11.6 minutes of fighting, ceiling against ceiling, grenades,
    /// smoke and drill on), every minute:
    ///
    ///   4.2    men walked through a living enemy
    ///   16     man-seconds within 3 m of an enemy, shooting at 16% a round
    ///   177    man-seconds standing within 0.6 m of a friend
    ///   10.6   times two men of one squad swapped sides
    ///   20     times a man's direction of travel swung through a right angle
    ///
    /// The baseline has no rule against any of it. An advancing squad's anchor
    /// marches at a constant speed whatever is in front of it; every squad in a
    /// lane walks the same column; two squads sent to one berm stand on the
    /// same spots; a man's slot is his position among the living by id, so a
    /// squad that rallies re-sorts itself by walking through itself.
    ///
    /// The rules, each of them what an infantry section does:
    ///
    ///   files      a lane has three files and a squad walks in one; a man has
    ///              a rank in it that closes up when the man ahead falls and is
    ///              dealt again, front to back as they stand, when a squad rallies
    ///   places     cover has places along it, front first, one a man. A squad
    ///              makes for the front of its cover and each man takes a place;
    ///              in a trench that is single file down the trench itself
    ///   pause      a squad that reaches cover stays in it a few seconds; one
    ///              told to hold in the open makes for cover that is near
    ///   stand-off  a squad stops short of an enemy it can see and fights from
    ///              there, kneeling
    ///   assault    unless the enemy in front is pinned or badly outnumbered:
    ///              then it closes
    ///   close in   a shot is likelier to hit the nearer it is; inside six
    ///              metres a man goes for his enemy, and at two they fight hand
    ///              to hand. Smoke hides an enemy from the stand-off, so two
    ///              squads in smoke find each other at arm's length
    ///   levers     a side's standing order on a strongpoint, which is any
    ///              piece of cover, built or natural (Warfare 1944's lever):
    ///              Hold keeps a squad that reaches it and calls the next one
    ///              to it; Go sends it on and lets the next pass through
    ///   vault      climbing into a trench or out of it takes a moment in which
    ///              a man neither moves nor fires
    ///   positions  a trench, wall or bunker belongs to the last side to have
    ///              had it to itself, and losing one costs morale
    ///
    /// No transcendentals. Melee draws from the simulation's own stream, in
    /// the firing loop, so it is ordered like every other shot. All of its
    /// state is hashed only when the rule is on: off, the match is the parity
    /// baseline to the bit.
    /// </summary>
    public static class Fieldcraft
    {
        /// <summary>
        /// Every piece of cover is a strongpoint a squad can be told to hold:
        /// built (a wall, the bunker, a trench) or natural (a crater, a bank).
        /// The owner, 2026-10-01: "don't think of trenches, it's more like a
        /// stronghold for squads to take cover. Not all can fit at once, could
        /// be man made or natural."
        /// </summary>
        public static bool IsPosition(Cover c) => true;

        /// <summary>Built by somebody, and worth more to lose than a hole in the ground.</summary>
        public static bool Built(Cover c)
            => c.Kind == CoverKind.Trench || c.Kind == CoverKind.Sandbag || c.Kind == CoverKind.Bunker;

        /// <summary>Dug into the ground: one man wide, and a man climbs into it and out of it.</summary>
        public static bool Dug(Cover c) => c.Kind == CoverKind.Trench;

        public static Lever LeverOf(Cover c, Side side) => side == Side.Us ? c.LeverUs : c.LeverVc;

        /// <summary>Set a side's lever on a position, as its player. Touches no RNG.</summary>
        public static bool SetLever(SimState st, Side side, int coverId, Lever lever)
        {
            if (!st.Fieldcraft || coverId < 0 || coverId >= st.Cover.Count) return false;
            var c = st.Cover[coverId];
            if (!IsPosition(c)) return false;
            if (side == Side.Us) c.LeverUs = lever; else c.LeverVc = lever;
            return true;
        }

        // --- files and ranks -------------------------------------------------------

        /// <summary>A new squad's file: the next of the lane's three, by how many its side has raised there.</summary>
        public static void Raised(SimState st, Squad sq)
        {
            int n = 0;
            for (int i = 0; i < st.Squads.Count; i++)
                if (st.Squads[i].Side == sq.Side && st.Squads[i].Lane == sq.Lane && st.Squads[i].Id != sq.Id) n++;
            sq.File = n % 3;
        }

        /// <summary>
        /// A file's line across the lane: the first two squads either side of the
        /// middle, the third down it. The middle is where cover's places are, so
        /// a squad passing a held position walks by the men in it, not through them.
        /// </summary>
        private static double FileZ(Squad sq) => sq.File == 0 ? -Tune.FileGap : sq.File == 1 ? Tune.FileGap : 0;

        /// <summary>
        /// Which side of the file a man walks on: his number in the squad as it
        /// was raised, so it is his for life (by rank, every casualty would send
        /// the men behind it across the column).
        /// </summary>
        internal static double SideOf(int number) => (number % 2 == 0 ? -1 : 1) * Tune.FileStagger;

        /// <summary>Where the <paramref name="number"/>th man of a new squad stands across the lane: in its file.</summary>
        public static double SpawnZ(Squad sq, int number) => Tune.Lanes[sq.Lane] + FileZ(sq) + SideOf(number);

        /// <summary>
        /// Where he stands along it: closed up behind the lead man, and all of
        /// them on the map. (The baseline strings a new squad out at marching
        /// distance from the map's edge backwards, and the edge clamps every man
        /// past the second onto one spot.)
        /// </summary>
        public static double SpawnX(double x, double dir, int number, int size)
        {
            double edge = Tune.HalfLength - 0.5, depth = (size - 1) * Tune.SpawnGap;
            double lead = dir > 0 ? Math.Max(x, -edge + depth) : Math.Min(x, edge - depth);
            return lead - dir * number * Tune.SpawnGap;
        }

        /// <summary>
        /// A step, kept clear of friends: a man does not walk onto another. In
        /// the open he goes round him, keeping his distance (the part of the
        /// step that closes on the man is dropped, and what is left slides
        /// past); in a trench there is no round, and he waits behind. A man
        /// falling back stops for nobody.
        /// </summary>
        public static bool Clear(SimState st, Man m, ref double nx, ref double nz, double step)
        {
            const double Room = 0.7;
            Man f = Blocker(st, m, nx, nz, Room);
            if (f == null) return false;
            double sx = nx - m.X, sz = nz - m.Z;
            nx = m.X; nz = m.Z;
            if (m.Cover >= 0 && m.Cover < st.Cover.Count && Dug(st.Cover[m.Cover]))
            {
                // One file down a trench: the man in his way has the place behind
                // his own, so they change places rather than one wait for ever.
                if (f.PlaceCover == m.PlaceCover && m.Place >= 0 && f.Place > m.Place) (m.Place, f.Place) = (f.Place, m.Place);
                return true;
            }
            double fx = f.X - m.X, fz = f.Z - m.Z, fd = Math.Sqrt(fx * fx + fz * fz);
            if (fd < 1e-6) { fx = 0; fz = m.Id < f.Id ? -1 : 1; fd = 1; }
            fx /= fd; fz /= fd;
            double toward = sx * fx + sz * fz;
            if (toward > 0) { sx -= toward * fx; sz -= toward * fz; }
            if (sx * sx + sz * sz < 0.04 * step * step)
            {
                // Dead ahead: round him by the side his number says, so two men meeting do not mirror each other.
                double turn = m.Id < f.Id ? 1 : -1;
                sx = -fz * turn * step; sz = fx * turn * step;
            }
            if (Blocker(st, m, m.X + sx, m.Z + sz, Room) == null) { nx = m.X + sx; nz = m.Z + sz; }
            return true;
        }

        private static Man Blocker(SimState st, Man m, double nx, double nz, double room)
        {
            for (int i = 0; i < st.Men.Count; i++)
            {
                var f = st.Men[i];
                if (!f.Alive || f == m || f.Side != m.Side) continue;
                double dx = nx - f.X, dz = nz - f.Z, d2 = dx * dx + dz * dz;
                if (d2 >= room * room) continue;
                double ox = m.X - f.X, oz = m.Z - f.Z;
                if (d2 < ox * ox + oz * oz) return f;       // closing on him; a step that opens the distance is always allowed
            }
            return null;
        }

        /// <summary>
        /// A man has fallen: the men behind him step up one, his place is free,
        /// and everyone near enough to have seen it flinches. Death beside you
        /// is the thing that puts a section on the ground.
        /// </summary>
        public static void Fell(SimState st, Man m)
        {
            double r2 = Tune.FearRadius * Tune.FearRadius;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var o = st.Men[i];
                if (!o.Alive || o.Side != m.Side) continue;
                if (o.Squad == m.Squad && o.Rank > m.Rank) o.Rank--;
                double dx = o.X - m.X, dz = o.Z - m.Z;
                if (dx * dx + dz * dz < r2) Combat.ApplyPin(st, o, Tune.FearPin);
            }
            m.Place = -1; m.PlaceCover = -1; m.Vault = 0;
        }

        /// <summary>
        /// The round that has just killed <paramref name="hit"/> carries on:
        /// the nearest of his own side behind him, close to its line, may take
        /// it too. A file of men end-on to a rifle is one target.
        /// </summary>
        public static void Through(SimState st, Man shooter, Man hit, Rng rng)
        {
            double lx = hit.X - shooter.X, lz = hit.Z - shooter.Z, len = Math.Sqrt(lx * lx + lz * lz);
            if (len < 1e-6) return;
            lx /= len; lz /= len;
            Man next = null;
            double nearest = Tune.ThroughReach;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var o = st.Men[i];
                if (!o.Alive || o.Side != hit.Side) continue;
                double ox = o.X - hit.X, oz = o.Z - hit.Z;
                double along = ox * lx + oz * lz;
                if (along <= 0.2 || along >= nearest) continue;
                if (Math.Abs(ox * lz - oz * lx) > Tune.ThroughWidth) continue;
                next = o; nearest = along;
            }
            if (next == null || rng.Next() >= Tune.ThroughChance) return;
            bool kills = rng.Next() < Tune.ThroughKill * Tune.Exposure(next.Posture);
            st.Events.Add(new SimEvent
            {
                Kind = EventKind.Through, Tick = st.Tick, Side = shooter.Side, Id = hit.Id, Target = next.Id, Amount = kills ? 1 : 0,
            });
            if (kills) Combat.Kill(st, next);
            else Combat.ApplyPin(st, next, Tune.ThroughPin);
        }

        /// <summary>Ranks dealt again, front to back as the men stand (ties by id), so nobody walks through the squad to his place.</summary>
        private static void Rerank(IReadOnlyList<Man> live, double dir)
        {
            for (int i = 0; i < live.Count; i++)
            {
                int ahead = 0;
                for (int j = 0; j < live.Count; j++)
                {
                    if (i == j) continue;
                    double a = live[j].X * dir, b = live[i].X * dir;
                    if (a > b || (a == b && live[j].Id < live[i].Id)) ahead++;
                }
                live[i].Rank = ahead;
            }
        }

        // --- places ---------------------------------------------------------------

        /// <summary>Where a squad coming from this side stops its anchor in a piece of cover: at the front of it.</summary>
        public static double StopX(Cover c, double dir) => c.X + dir * (c.Length * 0.5 - c.Length / c.Capacity * 0.5);

        /// <summary>Place <paramref name="k"/> of a piece of cover for a side: front first, a man's width apart, down a trench in single file.</summary>
        public static Slot PlaceOf(Cover c, int k, double dir, double side = 0)
        {
            double pitch = c.Length / c.Capacity;
            return new Slot
            {
                X = c.X + dir * (c.Length * 0.5 - (k + 0.5) * pitch),
                // Behind a wall or a bank each man keeps his side of the file; a trench is one man wide.
                Z = c.Z + (Dug(c) ? 0 : side),
            };
        }

        internal static bool Arrived(Squad sq, Cover c, double dir)
            => JsMath.Hypot(StopX(c, dir) - sq.AnchorX, c.Z - sq.AnchorZ) < 0.6;

        /// <summary>
        /// Is this point in that cover? A trench or a bunker is its own width:
        /// a man walking past one is not in it, and does not climb into it.
        /// </summary>
        public static bool In(double x, double z, Cover c)
            => Dug(c)
                ? Math.Abs(x - c.X) <= c.Length * 0.5 && Math.Abs(z - c.Z) <= 0.9
                : Combat.InCover(x, z, c);

        private static bool Taken(SimState st, Cover c, Side side, int k, Man but)
        {
            for (int i = 0; i < st.Men.Count; i++)
            {
                var o = st.Men[i];
                if (o.Alive && o != but && o.Side == side && o.PlaceCover == c.Id && o.Place == k) return true;
            }
            return false;
        }

        private static int FreePlaces(SimState st, Cover c, Side side)
        {
            int n = 0;
            for (int k = 0; k < c.Capacity; k++) if (!Taken(st, c, side, k, null)) n++;
            return n;
        }

        /// <summary>Places in it not taken by another squad of the side: what is there for this one.</summary>
        internal static int FreePlaces(SimState st, Cover c, Side side, int squad)
        {
            int n = c.Capacity;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var o = st.Men[i];
                if (o.Alive && o.Side == side && o.Squad != squad && o.PlaceCover == c.Id && o.Place >= 0) n--;
            }
            return Math.Max(0, n);
        }

        internal static void Release(IReadOnlyList<Man> live)
        {
            for (int i = 0; i < live.Count; i++) { live[i].Place = -1; live[i].PlaceCover = -1; }
        }

        // --- the squad's turn ---------------------------------------------------------

        [ThreadStatic] private static List<Man> _byRank;

        /// <summary>
        /// A squad's whole move for the tick, in place of the baseline's
        /// target-march-slots: its lever, its cover, its anchor, and where each
        /// man should stand. Returns the slots in the order of
        /// <paramref name="live"/>.
        /// </summary>
        public static void Move(SimState st, Squad sq, IReadOnlyList<Man> live, Plan plan, Order prev, List<Slot> slots)
        {
            double dir = Combat.Advance(sq.Side);
            if (prev == Order.Fallback && sq.Order != Order.Fallback) Rerank(live, dir);
            sq.Gap = Gap(st, sq, dir);

            var tc = plan.UseCover && sq.Target >= 0 && sq.Target < st.Cover.Count ? st.Cover[sq.Target] : null;
            if (!plan.UseCover) sq.Target = -1;

            if (sq.Order == Order.Fallback && st.Senses && tc != null)
            {
                // Senses: running to the strongpoint behind, each man for his place in it.
                sq.Sent = 0;
            }
            else if (sq.Order == Order.Fallback)
            {
                // Running: no cover, no places.
                if (tc != null) { sq.Target = -1; sq.Held = 0; tc = null; }
                sq.Sent = 0;
                Release(live);
            }
            else if (plan.UseCover)
            {
                bool arrived = tc != null && Arrived(sq, tc, dir);
                bool free = sq.PlayerOrder == null;
                if (sq.Sent > 0) sq.Sent--;

                // The lever on the position it stands in.
                bool sent = false, held = false;
                if (free && arrived && IsPosition(tc))
                {
                    var lever = LeverOf(tc, sq.Side);
                    if (lever == Lever.Hold) { sq.Order = Order.Hold; held = true; }
                    else if (lever == Lever.Go) { sq.Held = Tune.DugPause; sq.Sent = Tune.SentTicks; sent = true; }      // no pause to wait out
                }
                // Arms: a squad that can see the enemy from beyond its own fighting distance
                // closes to it, unless it is beaten down or has been told to stay: a fight
                // happens where its weapons fight, not wherever the first round fell.
                // The decision is made once and stands for a couple of seconds: taken afresh every
                // tick, on a squad's pin as it crossed the line, it was a man hopping forward and stopping.
                if (sq.Closing > 0) sq.Closing--;
                if (st.Arms && !st.Senses && free && !held && plan.Advance && st.Phase == Phase.Fight && sq.Order == Order.Hold && !sq.Rallied)
                {
                    if (sq.Gap > sq.Reach + 2 && sq.Gap < 1e9 && MeanPin(live) < Tune.PinDrop) sq.Closing = Tune.ClosingTicks;
                    if (sq.Closing > 0 && MeanPin(live) < Tune.PinStop) sq.Order = Order.Advance;
                }
                // Sent out of a position, it goes, until it is in its next cover: it does not stop a pace beyond the parapet.
                if (arrived && !sent) sq.Sent = 0;
                if (free && sq.Sent > 0) sq.Order = Order.Advance;

                // A held position ahead calls the squad to it.
                int call = free ? HeldAhead(st, sq, dir) : -1;
                if (call >= 0)
                {
                    if (call != sq.Target)
                    {
                        Release(live);
                        sq.Target = call; sq.Held = 0; tc = st.Cover[call];
                        arrived = Arrived(sq, tc, dir);
                    }
                    if (!arrived && sq.Order == Order.Hold) sq.Order = Order.Advance;
                }
                // Senses: a squad that has gone to ground has chosen where, cover or none, and stays.
                else if (st.Senses && free && (sq.Task == SquadTask.Contact || sq.Task == SquadTask.Firefight || sq.Task == SquadTask.Regroup))
                {
                }
                // Senses: a squad on the march, out of contact, marches: up the lane, not from one piece
                // of cover to the next with a wait in each (eight seconds in a trench forty metres from
                // anyone). Cover is what it takes when it meets the enemy. With none left ahead of it,
                // it holds the last rather than walk to the end of the map.
                else if (st.Senses && free && plan.Advance && sq.Task == SquadTask.March && st.Phase == Phase.Fight)
                {
                    bool more = Combat.BestCover(st, sq.AnchorX, sq.Lane, dir, true, sq.Side, live.Count) >= 0;
                    if (more && tc != null) { Release(live); sq.Target = -1; sq.Held = 0; tc = null; }
                    else if (!more && tc == null)
                    {
                        sq.Target = Senses.Ground(st, sq, live, dir, Tune.HoldRadius, double.NegativeInfinity);
                        sq.Held = 0;
                        tc = sq.Target >= 0 ? st.Cover[sq.Target] : null;
                    }
                }
                else
                {
                    // No cover yet, or it has had its pause in this one and is to go on.
                    bool moving = sq.Order == Order.Advance || sq.Order == Order.Bound;
                    // Senses: a squad closing has had its pause, in the firefight it is leaving.
                    bool paused = tc != null && (sq.Held >= (Dug(tc) ? Tune.DugPause : Tune.CoverPause)
                                                 || (st.Senses && (sq.Task == SquadTask.Close || sq.Task == SquadTask.Assault)));
                    if (tc == null || (arrived && moving && paused))
                    {
                        // Senses: closing, a short rush to the next cover on, not a walk to the best on the map.
                        int next = st.Senses && sq.Task == SquadTask.Close ? Senses.Ground(st, sq, live, dir, Tune.DashToCover + 4, 2) : -1;
                        if (next < 0) next = Combat.BestCover(st, sq.AnchorX, sq.Lane, dir, plan.Advance, sq.Side, live.Count);
                        // Nothing further on: only a side that is taking ground leaves its cover for none
                        // (Senses: and it does not; a squad with no cover ahead holds the last).
                        if (next < 0 && tc != null && (!plan.Advance || st.Senses)) next = sq.Target;
                        if (next != sq.Target)
                        {
                            Release(live);
                            sq.Target = next; sq.Held = 0;
                            tc = next >= 0 ? st.Cover[next] : null;
                        }
                    }
                }
            }

            March(st, sq, live, tc, dir);
            Squads.Reanchor(sq, live);
            Slots(st, sq, live, tc, dir, slots);
        }

        /// <summary>Metres along the lane to the nearest enemy the squad can see ahead of it; huge if none.</summary>
        private static double Gap(SimState st, Squad sq, double dir)
        {
            double near = double.PositiveInfinity;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var e = st.Men[i];
                if (!e.Alive || e.Side == sq.Side || !(st.Senses ? Senses.Remembers(st, sq, e.Squad) : e.Seen)) continue;
                if (Math.Abs(e.Z - Tune.Lanes[sq.Lane]) > 6.5) continue;
                double ahead = (e.X - sq.AnchorX) * dir;
                if (ahead >= 0 && ahead < near) near = ahead;
            }
            return near;
        }

        private static double MeanPin(IReadOnlyList<Man> live)
        {
            double pin = 0;
            for (int i = 0; i < live.Count; i++) pin += live[i].Pin;
            return live.Count == 0 ? 0 : pin / live.Count;
        }

        /// <summary>The nearest position ahead, or under it, that its side holds by lever and that has a place free for it.</summary>
        private static int HeldAhead(SimState st, Squad sq, double dir)
        {
            int best = -1;
            double bestAhead = double.PositiveInfinity;
            for (int i = 0; i < st.Cover.Count; i++)
            {
                var c = st.Cover[i];
                if (!IsPosition(c) || LeverOf(c, sq.Side) != Lever.Hold) continue;
                if (Math.Abs(c.Z - Tune.Lanes[sq.Lane]) > 4.5) continue;
                double ahead = (StopX(c, dir) - sq.AnchorX) * dir;
                if (ahead < -0.6 || ahead >= bestAhead) continue;
                if (sq.Target != c.Id && FreePlaces(st, c, sq.Side) == 0) continue;
                best = c.Id; bestAhead = ahead;
            }
            return best;
        }

        /// <summary>Senses: with the enemy in contact a squad moves between cover at a rush. (Gunnery: at the double; and on the march, slower than the baseline's.)</summary>
        private static double Pace(SimState st, Squad sq)
            => st.Senses && sq.Task != SquadTask.March ? (st.Gunnery ? Tune.RushPaceRun : Tune.RushPace) : st.Gunnery ? Tune.MarchPace : 1;

        private static void March(SimState st, Squad sq, IReadOnlyList<Man> live, Cover tc, double dir)
        {
            sq.Halted = false; sq.Assault = false;
            if (sq.Charge > 0) sq.Charge--;
            double was = sq.AnchorX;
            if (sq.Order == Order.Fallback && st.Senses && tc != null)
            {
                // Senses: back to the strongpoint behind, faster than it came.
                double dx = StopX(tc, dir) - sq.AnchorX, dz = tc.Z - sq.AnchorZ;
                double d = JsMath.Hypot(dx, dz);
                if (d < 0.6) { sq.Held++; sq.Halted = true; }
                if (d > 0.05)
                {
                    double step = Math.Min(d, Tune.MarchSpeed * (st.Gunnery ? Tune.RushPaceRun : Tune.WithdrawPace) * Tune.Dt);
                    sq.AnchorX += dx / d * step;
                    sq.AnchorZ += dz / d * step;
                }
            }
            else if (sq.Order == Order.Fallback)
            {
                sq.AnchorX -= dir * Tune.MarchSpeed * Tune.Dt;
            }
            else if (tc != null)
            {
                double sx = StopX(tc, dir);
                double dx = sx - sq.AnchorX, dz = tc.Z - sq.AnchorZ;
                double d = JsMath.Hypot(dx, dz);
                // Told to hold in the open, it still makes for cover that is near.
                bool go = sq.Order != Order.Hold || d <= Tune.DashToCover;
                if (d < 0.6) { sq.Held++; sq.Halted = true; }
                else if (!go) sq.Halted = true;
                if (go && d > 0.05)
                {
                    double step = Math.Min(d, Tune.MarchSpeed * Pace(st, sq) * Tune.Dt);
                    sq.AnchorX += dx / d * step;
                    sq.AnchorZ += dz / d * step;
                }
            }
            else if (sq.Order == Order.Advance || sq.Order == Order.Bound)
            {
                sq.AnchorX += dir * Tune.MarchSpeed * Pace(st, sq) * Tune.Dt;
            }
            else sq.Halted = true;

            // The stand-off: no nearer to an enemy it can see, unless it is to close with him.
            // (Senses: a squad going to ground in cover it has chosen goes to it.)
            bool toGround = st.Senses && (sq.Task == SquadTask.Contact || sq.Task == SquadTask.Firefight || sq.Task == SquadTask.Regroup);
            if (sq.Order != Order.Fallback && !toGround && (sq.AnchorX - was) * dir > 0)
            {
                double near = double.PositiveInfinity, pin = 0, flank = double.PositiveInfinity;
                int enemies = 0;
                for (int i = 0; i < st.Men.Count; i++)
                {
                    var e = st.Men[i];
                    if (!e.Alive || e.Side == sq.Side || !(st.Senses ? Senses.Remembers(st, sq, e.Squad) : e.Seen)) continue;
                    if (Math.Abs(e.Z - Tune.Lanes[sq.Lane]) > 6.5)
                    {
                        // Senses: the squad it is fighting is in the other lane. It comes up level with it,
                        // as near along the lane as its weapons want across it, and does not walk on past.
                        if (!st.Senses || e.Squad != sq.Threat) continue;
                        double by = (e.X - was) * dir, across = e.Z - sq.AnchorZ;
                        if (by < -Tune.FlankPast || by > Math.Max(Tune.AssaultReach, sq.Reach + 6)) continue;
                        if (Combat.SmokeBlocks(st, was, sq.AnchorZ, e.X, e.Z)) continue;
                        double along = Math.Sqrt(Math.Max(0, sq.Reach * sq.Reach - across * across));
                        flank = Math.Min(flank, Math.Max(0, by - along));
                        continue;
                    }
                    double ahead = (e.X - was) * dir;
                    if (ahead < 0 || ahead > Math.Max(Tune.AssaultReach, sq.Reach + 6)) continue;
                    if (Combat.SmokeBlocks(st, was, sq.AnchorZ, e.X, e.Z)) continue;
                    enemies++; pin += e.Pin;
                    if (ahead < near) near = ahead;
                }
                if (flank < 1e9 && sq.Charge <= 0)
                {
                    double limit = was + dir * flank;
                    if ((sq.AnchorX - limit) * dir > 0) { sq.AnchorX = limit; sq.Halted = true; }
                }
                if (enemies > 0)
                {
                    // Arms: a squad fights from its own distance, and a support team does not go in.
                    // (Senses: the squad's task says when it goes in.)
                    if (!st.Senses && sq.Assaults && (pin / enemies >= Tune.PinDrop || live.Count >= Tune.AssaultOdds * enemies)) sq.Charge = Tune.ChargeTicks;
                    // Once it goes in it goes in: the enemy's pin crossing the line the other way does not stop it mid-stride.
                    sq.Assault = sq.Charge > 0;
                    if (!sq.Assault)
                    {
                        double limit = was + dir * Math.Max(0, near - sq.Reach);
                        if ((sq.AnchorX - limit) * dir > 0) { sq.AnchorX = limit; sq.Halted = true; }
                    }
                }
            }
            sq.AnchorX = Math.Max(-Tune.HalfLength, Math.Min(Tune.HalfLength, sq.AnchorX));
        }

        private static void Slots(SimState st, Squad sq, IReadOnlyList<Man> live, Cover tc, double dir, List<Slot> into)
        {
            into.Clear();
            // Near enough its cover, each man takes a place in it, the lead man first.
            // (Senses: falling back, each man runs for his place in the strongpoint behind from wherever he is.)
            bool near = tc != null && (Math.Abs(sq.AnchorX - StopX(tc, dir)) < tc.Length + 4 || (st.Senses && sq.Order == Order.Fallback));
            if (near)
            {
                var byRank = _byRank ??= new List<Man>(Tune.SquadMax);
                byRank.Clear();
                for (int i = 0; i < live.Count; i++) byRank.Add(live[i]);
                byRank.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Id.CompareTo(b.Id));
                for (int i = 0; i < byRank.Count; i++)
                {
                    var m = byRank[i];
                    if (m.PlaceCover == tc.Id && m.Place >= 0) continue;
                    m.Place = -1; m.PlaceCover = -1;
                    for (int k = 0; k < tc.Capacity; k++)
                    {
                        if (Taken(st, tc, sq.Side, k, m)) continue;
                        m.Place = k; m.PlaceCover = tc.Id;
                        break;
                    }
                }
            }
            int waiting = 0, first = 0;
            while (first < st.Men.Count && st.Men[first].Squad != sq.Id) first++;
            for (int i = 0; i < live.Count; i++)
            {
                var m = live[i];
                double side = SideOf(m.Id - first);
                if (near && m.PlaceCover == tc.Id && m.Place >= 0)
                {
                    var at = PlaceOf(tc, m.Place, dir, side);
                    at.InCover = true;
                    into.Add(at);
                    continue;
                }
                if (near)
                {
                    // No place left for him: he waits behind it, in his file.
                    into.Add(new Slot
                    {
                        X = tc.X - dir * (tc.Length * 0.5 + 1.5 + waiting * Tune.SlotGap),
                        Z = tc.Z + FileZ(sq) + side,
                    });
                    waiting++;
                    continue;
                }
                into.Add(new Slot { X = sq.AnchorX - dir * m.Rank * Tune.SlotGap, Z = sq.AnchorZ + FileZ(sq) + side });
            }
        }

        // --- the man's turn ------------------------------------------------------------

        /// <summary>
        /// The enemy a man goes for with his hands, or null: one within
        /// <see cref="Tune.ChargeRange"/> that he can see, or half that in any
        /// case. A man holding cover waits for him to come the last three metres.
        /// </summary>
        public static Man Quarry(SimState st, Man m, Squad sq)
        {
            if (sq.Order == Order.Fallback || m.Pin >= Tune.PinDrop) return null;
            bool waits = m.Cover >= 0 && sq.Order == Order.Hold;
            // Tactics: only a man with a place in it. One passing through went for his man out of the
            // cover and waited for him in it, and at its edge did each on alternate ticks.
            if (st.Tactics && waits && !(m.Place >= 0 && m.PlaceCover == m.Cover)) waits = false;
            double reach = waits ? Tune.ChargeRange * 0.5 : Tune.ChargeRange;
            // Senses: a squad that is going in goes in man by man, each for the nearest enemy in
            // front of it that it knows of. (Marching its anchor up, it stood behind its lead man
            // the moment he was pinned.)
            bool goingIn = st.Senses && sq.Task == SquadTask.Assault && sq.Order != Order.Hold;
            // (Tactics: the last ten metres. Further off the squad comes on by bounds, half of it firing.)
            if (goingIn) reach = st.Tactics ? Tune.RushFrom : Tune.AssaultReach + Tune.ChargeRange;
            Man best = null;
            double best2 = reach * reach;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var e = st.Men[i];
                if (!e.Alive || e.Side == m.Side) continue;
                double dx = e.X - m.X, dz = e.Z - m.Z, d2 = dx * dx + dz * dz;
                if (d2 >= best2) continue;
                double half = Tune.ChargeRange * 0.5;
                if (goingIn && d2 > Tune.ChargeRange * Tune.ChargeRange
                    && (Math.Abs(e.Z - Tune.Lanes[sq.Lane]) > 6.5 || !Senses.Remembers(st, sq, e.Squad))) continue;
                if (!goingIn && d2 > half * half && (!e.Seen || Combat.SmokeBlocks(st, m.X, m.Z, e.X, e.Z))) continue;
                best = e; best2 = d2;
            }
            return best;
        }

        /// <summary>The nearest living enemy within arm's reach, or null. Smoke and grass hide nobody at two metres.</summary>
        public static Man InReach(SimState st, Man m)
        {
            Man best = null;
            double best2 = Tune.MeleeRange * Tune.MeleeRange;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var e = st.Men[i];
                if (!e.Alive || e.Side == m.Side) continue;
                double dx = e.X - m.X, dz = e.Z - m.Z, d2 = dx * dx + dz * dz;
                if (d2 < best2) { best = e; best2 = d2; }
            }
            return best;
        }

        /// <summary>One blow. A suppressed man strikes worse; a man flat on the ground or climbing is easier to finish.</summary>
        public static void Strike(SimState st, Man a, Man b, Rng rng)
        {
            a.Cooldown = Tune.MeleeCooldown;
            a.Seen = true; b.Seen = true;
            a.FiredAt = st.Tick;
            st.Events.Add(new SimEvent { Kind = EventKind.Melee, Tick = st.Tick, Side = a.Side, Id = a.Id, Target = b.Id });
            double p = Tune.MeleeKill * (1 - 0.6 * a.Pin) * (1 + 0.3 * (a.Veterancy - b.Veterancy));
            if (b.Pin >= Tune.PinStop || b.Vault > 0) p *= 1.4;
            if (rng.Next() < Math.Max(0.1, Math.Min(0.9, p))) Combat.Kill(st, b);
            else Combat.ApplyPin(st, b, Tune.MeleePin);
        }

        // --- positions --------------------------------------------------------------

        /// <summary>Whose each position is. One that changes hands costs the side that lost it.</summary>
        public static void Positions(SimState st)
        {
            for (int ci = 0; ci < st.Cover.Count; ci++)
            {
                var c = st.Cover[ci];
                if (!IsPosition(c)) continue;
                int us = 0, vc = 0;
                for (int i = 0; i < st.Men.Count; i++)
                {
                    var m = st.Men[i];
                    if (!m.Alive || m.Cover != c.Id) continue;
                    if (m.Side == Side.Us) us++; else vc++;
                }
                if ((us > 0) == (vc > 0)) continue;             // empty, or still being fought over
                var now = us > 0 ? Side.Us : Side.Vc;
                if (c.Owner == now) continue;
                if (c.Owner.HasValue)
                {
                    var lost = c.Owner.Value;
                    double hit = (Built(c) ? Tune.PositionMorale : Tune.GroundMorale) * st.MoraleScale;
                    st.Morale[(int)lost] = Math.Max(0, st.Morale[(int)lost] - hit);
                    st.MoraleLostToGround[(int)lost] += hit;
                    st.Events.Add(new SimEvent { Kind = EventKind.PositionTaken, Tick = st.Tick, Side = now, Id = c.Id, X = c.X, Z = c.Z });
                }
                c.Owner = now;
            }
        }
    }
}
