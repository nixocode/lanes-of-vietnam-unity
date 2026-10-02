using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Senses (Part 2, behind <see cref="MatchOptions.Senses"/>): what a squad
    /// knows, and what it does the moment it knows it.
    ///
    /// The owner, playtest 5: "main focus should be fixing AI character
    /// awareness and combat instincts: still wander around, don't see each
    /// other, stand around when not supposed to", and "a lot of unnecessary
    /// shooting: shots should only be fired when a squad spots another. Most
    /// can be misses but the intent needs to be there."
    ///
    /// Sight in the baseline is one flag a man (<see cref="Man.Seen"/>), and it
    /// is lopsided and permanent: every American is seen from the moment he
    /// arrives, a VC is unseen until he fires or is walked onto, and nobody is
    /// ever lost sight of. A squad's order is decided afresh every tick from
    /// averages over its whole lane, not from anything that squad can see: it
    /// flips between hold, advance and bound every second and a half, and at
    /// every flip its men stand up, walk a pace and kneel again, eight metres
    /// from the enemy. Measured as the game plays (six seeds, `simcs aware`):
    /// a third of all shots fired across the lanes at whoever was nearest; one
    /// man in six with a seen enemy in range standing still, another one in
    /// five standing and walking.
    ///
    /// The rules:
    ///
    ///   sight      nobody is seen for free. A man is seen from further off
    ///              standing than kneeling than flat, further still if he is
    ///              moving, much further for a few seconds after he fires, less
    ///              behind cover; smoke hides him. A man with his face in the
    ///              dirt sees less; a sniper's scope sees more
    ///   contacts   a squad spots a squad. It keeps it in sight for a few
    ///              seconds after it last saw it, and remembers where it was
    ///              for longer. Word passes to the squads beside it
    ///   fire       a man fires only at a squad his own has in sight, and first
    ///              at the one his squad is dealing with; slower at a man of it
    ///              he cannot himself see. A machine gun keeps bursts on the
    ///              cover of a squad it has lost sight of: they pin and do not kill
    ///   contact    at its first sight of the enemy a squad's men drop where
    ///              they are, and then it takes the nearest cover, whichever way
    ///   tasks      a squad has one thing it is doing (<see cref="SquadTask"/>),
    ///              decided from what it knows and held for a minimum time
    ///   withdraw   a broken squad runs to the strongpoint behind it and holds there
    ///
    /// <see cref="Man.Seen"/> is kept, as "an enemy squad has his squad in
    /// sight now", so the rules and the view that read it see what the enemy
    /// sees. No random draws, no transcendentals. Its state is hashed only
    /// when the rule is on: off, the match is the parity baseline to the bit.
    /// </summary>
    public static class Senses
    {
        /// <summary>The tick of something that has not happened.</summary>
        public const int Never = -100000;

        // --- sight ------------------------------------------------------------------

        /// <summary>
        /// From how far off <paramref name="observer"/> sees
        /// <paramref name="target"/>, in metres. A pure function of the state,
        /// read by the rule and by the measurement that judges it.
        /// </summary>
        public static double Sight(SimState st, Man observer, Man target)
        {
            double r = Tune.SpotRange(target.Posture);
            if (Squads.IsMoving(target)) r += Tune.SpotMovingBonus;
            if (target.Cover >= 0 && target.Posture != Posture.Standing) r *= Tune.SightInCover;
            if (st.Tick - target.FiredAt <= Tune.SightFiredTicks) r = Math.Max(r, Tune.SightFired);
            if (observer.Pin >= Tune.PinStop) r *= Tune.SightPinned;
            if (observer.Weapon == Weapon.Sniper) r *= Tune.SightScope;
            return r * st.Sight;
        }

        /// <summary>Does this man see that one, now? Distance, and smoke between them.</summary>
        public static bool Sees(SimState st, Man observer, Man target)
        {
            double r = Sight(st, observer, target);
            double dx = observer.X - target.X, dz = observer.Z - target.Z;
            if (dx * dx + dz * dz > r * r) return false;
            return !Combat.SmokeBlocks(st, observer.X, observer.Z, target.X, target.Z);
        }

        /// <summary>Does any man of one squad see any man of the other, now?</summary>
        public static bool Spots(SimState st, IReadOnlyList<Man> observers, IReadOnlyList<Man> targets)
        {
            for (int i = 0; i < observers.Count; i++)
                for (int j = 0; j < targets.Count; j++)
                    if (Sees(st, observers[i], targets[j])) return true;
            return false;
        }

        private static int At(List<int> l, int i) => i < l.Count ? l[i] : Never;

        private static void Set(List<int> l, int i, int tick)
        {
            while (l.Count <= i) l.Add(Never);
            l[i] = tick;
        }

        /// <summary>Has this squad that enemy squad in sight now: seen by it, or by a squad beside it, within the last few seconds?</summary>
        public static bool Knows(SimState st, Squad sq, int enemy) => st.Tick - At(sq.KnownAt, enemy) <= Tune.SenseKeep;

        /// <summary>Does it still know where that squad was?</summary>
        public static bool Remembers(SimState st, Squad sq, int enemy) => st.Tick - At(sq.KnownAt, enemy) <= Tune.SenseMemory;

        /// <summary>
        /// Is the enemy it knows of near enough to act on: inside its weapons'
        /// reach, or near enough to that? Further off, it marches on.
        /// </summary>
        public static bool InContact(Squad sq, double slack = 0)
            => sq.Threat >= 0 && sq.ThreatGap <= Math.Max(Tune.ContactNear, sq.Reach + Tune.ContactPast) + slack;

        /// <summary>A squad's task, for a trace: empty with the rule off.</summary>
        public static string Describe(SimState st, Squad sq)
            => st.Senses ? $" {sq.Task,-9} threat {(sq.Threat < 0 ? " -" : sq.Threat.ToString().PadLeft(2))}{(sq.ThreatSeen ? "*" : " ")}" : "";

        [ThreadStatic] private static List<List<Man>> _rosters;

        /// <summary>
        /// The tick's looking: who has whom in sight, the word passed along,
        /// each man's <see cref="Man.Seen"/>, and the squad each squad is
        /// dealing with.
        /// </summary>
        public static void Look(SimState st)
        {
            var rosters = _rosters ??= new List<List<Man>>();
            while (rosters.Count < st.Squads.Count) rosters.Add(new List<Man>(Tune.SquadMax));
            for (int i = 0; i < st.Squads.Count; i++) rosters[i].Clear();
            for (int i = 0; i < st.Men.Count; i++) if (st.Men[i].Alive) rosters[st.Men[i].Squad].Add(st.Men[i]);

            // Its own eyes.
            for (int a = 0; a < st.Squads.Count; a++)
            {
                if (rosters[a].Count == 0) continue;
                var sa = st.Squads[a];
                for (int b = 0; b < st.Squads.Count; b++)
                {
                    var sb = st.Squads[b];
                    if (sb.Side == sa.Side || rosters[b].Count == 0) continue;
                    if (!Spots(st, rosters[a], rosters[b])) continue;
                    Set(sa.SawAt, b, st.Tick); Set(sa.KnownAt, b, st.Tick);
                    sb.SeenAt = st.Tick;
                }
            }
            // The word, from the squads beside it: what they see themselves, not what they were told.
            for (int a = 0; a < st.Squads.Count; a++)
            {
                if (rosters[a].Count == 0) continue;
                var sa = st.Squads[a];
                for (int c = 0; c < st.Squads.Count; c++)
                {
                    var sc = st.Squads[c];
                    if (c == a || sc.Side != sa.Side || rosters[c].Count == 0) continue;
                    double dx = sc.AnchorX - sa.AnchorX, dz = sc.AnchorZ - sa.AnchorZ;
                    if (dx * dx + dz * dz > Tune.WordRange * Tune.WordRange) continue;
                    for (int b = 0; b < sc.SawAt.Count; b++)
                        if (sc.SawAt[b] == st.Tick) Set(sa.KnownAt, b, st.Tick);
                }
            }

            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                if (m.Alive) m.Seen = st.Tick - st.Squads[m.Squad].SeenAt <= Tune.SenseKeep;
            }

            // The squad each is dealing with: the nearest it knows of, its own lane's first.
            for (int a = 0; a < st.Squads.Count; a++)
            {
                var sa = st.Squads[a];
                if (rosters[a].Count == 0) { sa.Threat = -1; continue; }
                int best = -1;
                double bestScore = double.PositiveInfinity, bestGap = double.PositiveInfinity;
                for (int b = 0; b < st.Squads.Count; b++)
                {
                    var sb = st.Squads[b];
                    if (sb.Side == sa.Side || rosters[b].Count == 0 || !Remembers(st, sa, b)) continue;
                    // Gunnery: a squad deals with the enemy in its own lane.
                    if (st.Gunnery && sb.Lane != sa.Lane) continue;
                    double near = double.PositiveInfinity;
                    for (int i = 0; i < rosters[a].Count; i++)
                        for (int j = 0; j < rosters[b].Count; j++)
                        {
                            double dx = rosters[a][i].X - rosters[b][j].X, dz = rosters[a][i].Z - rosters[b][j].Z;
                            double d2 = dx * dx + dz * dz;
                            if (d2 < near) near = d2;
                        }
                    near = Math.Sqrt(near);
                    double score = near + (sb.Lane != sa.Lane ? Tune.LanePenalty : 0);
                    if (score < bestScore) { bestScore = score; best = b; bestGap = near; }
                }
                sa.Threat = best; sa.ThreatGap = bestGap; sa.ThreatSeen = best >= 0 && Knows(st, sa, best);
            }
        }

        // --- fire -------------------------------------------------------------------

        /// <summary>
        /// The man he fires at: the nearest in reach of a squad his own has in
        /// sight, and of the squad it is dealing with before any other.
        /// </summary>
        public static Man PickTarget(SimState st, Man a)
        {
            var sq = st.Squads[a.Squad];
            double reach = Arms.Of(st, a).Range;
            Man best = null, bestOther = null;
            double bestD2 = reach * reach, otherD2 = reach * reach;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var b = st.Men[i];
                if (!b.Alive || b.Side == a.Side || !Knows(st, sq, b.Squad)) continue;
                if (st.Gunnery && !Gunnery.InArc(a, b)) continue;
                double dx = a.X - b.X, dz = a.Z - b.Z;
                double d2 = dx * dx + dz * dz;
                bool theirs = b.Squad == sq.Threat;
                if (d2 > (theirs ? bestD2 : otherD2)) continue;
                if (Combat.SmokeBlocks(st, a.X, a.Z, b.X, b.Z)) continue;
                if (theirs) { bestD2 = d2; best = b; } else { otherD2 = d2; bestOther = b; }
            }
            return best ?? bestOther;
        }

        /// <summary>
        /// A machine gun with nothing in sight: the nearest man in cover of a
        /// squad it knows was there, to keep his head down. Null for any other weapon.
        /// </summary>
        public static Man Suppress(SimState st, Man a)
        {
            if (a.Weapon != Weapon.M60 && a.Weapon != Weapon.Rpd) return null;
            var sq = st.Squads[a.Squad];
            double reach = Arms.Of(st, a).Range;
            Man best = null;
            double bestD2 = reach * reach;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var b = st.Men[i];
                if (!b.Alive || b.Side == a.Side || b.Cover < 0 || !Remembers(st, sq, b.Squad)) continue;
                if (st.Gunnery && !Gunnery.InArc(a, b)) continue;
                double dx = a.X - b.X, dz = a.Z - b.Z;
                double d2 = dx * dx + dz * dz;
                if (d2 > bestD2 || Combat.SmokeBlocks(st, a.X, a.Z, b.X, b.Z)) continue;
                bestD2 = d2; best = b;
            }
            return best;
        }

        // --- the squad's task ------------------------------------------------------

        private static double MeanPin(IReadOnlyList<Man> live)
        {
            double pin = 0;
            for (int i = 0; i < live.Count; i++) pin += live[i].Pin;
            return live.Count == 0 ? 0 : pin / live.Count;
        }

        /// <summary>Is it in a position its side's lever says to hold?</summary>
        private static bool Held(SimState st, Squad sq)
        {
            if (!st.Fieldcraft || sq.Target < 0 || sq.Target >= st.Cover.Count) return false;
            var c = st.Cover[sq.Target];
            return Fieldcraft.LeverOf(c, sq.Side) == Lever.Hold && Fieldcraft.Arrived(sq, c, Combat.Advance(sq.Side));
        }

        private static bool LeadPinned(IReadOnlyList<Man> live)
        {
            Man lead = live[0];
            for (int i = 1; i < live.Count; i++) if (live[i].Rank < lead.Rank) lead = live[i];
            return lead.Pin >= Tune.PinDrop;
        }

        /// <summary>
        /// The enemy it knows of in front of it, in its own lane: how many
        /// within <paramref name="reach"/> of its anchor, their pin, and how
        /// far the nearest is along the lane (huge if none ahead at all).
        /// </summary>
        private static void Front(SimState st, Squad sq, double reach, out int enemies, out double pin, out double near)
        {
            double dir = Combat.Advance(sq.Side);
            enemies = 0; pin = 0; near = double.PositiveInfinity;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var e = st.Men[i];
                if (!e.Alive || e.Side == sq.Side || !Remembers(st, sq, e.Squad)) continue;
                if (Math.Abs(e.Z - Tune.Lanes[sq.Lane]) > 6.5) continue;
                double ahead = (e.X - sq.AnchorX) * dir;
                if (ahead < 0) continue;
                if (ahead < near) near = ahead;
                if (ahead > reach) continue;
                enemies++; pin += e.Pin;
            }
        }

        /// <summary>It goes in when the enemy in front of it is beaten down or badly outnumbered, and it is not.</summary>
        private static bool CanAssault(SimState st, Squad sq, IReadOnlyList<Man> live, double pin)
        {
            if (!sq.Assaults || !sq.ThreatSeen || pin >= Tune.PinDrop) return false;
            Front(st, sq, Tune.AssaultReach, out int enemies, out double theirs, out _);
            if (enemies == 0) return false;
            return theirs / enemies >= Tune.PinDrop || live.Count >= Tune.AssaultOdds * enemies;
        }

        /// <summary>
        /// The squad's order for the tick, from its task. A player's order is
        /// obeyed as it is given; otherwise the task is carried on, or changed
        /// for what the squad now knows.
        /// </summary>
        public static Order Decide(SimState st, Squad sq, IReadOnlyList<Man> live, Plan plan, int raised, Order prev)
        {
            if (sq.PlayerOrder.HasValue)
            {
                var given = sq.PlayerOrder.Value;
                var as_ = given == Order.Fallback ? SquadTask.Withdraw : given == Order.Hold ? SquadTask.Firefight : SquadTask.March;
                if (as_ != sq.Task) Take(st, sq, live, plan, as_);
                return given;
            }

            double pin = MeanPin(live);
            bool broken = (double)live.Count / Math.Max(1, raised) < Tune.SquadBreak;
            int held = st.Tick - sq.TaskSince;
            bool threat = sq.Threat >= 0;
            var task = sq.Task;

            // The order to fall back is obeyed at once, as it always was.
            if (plan.Withdraw && broken && !sq.Rallied && task != SquadTask.Withdraw) task = SquadTask.Withdraw;
            else switch (task)
            {
                case SquadTask.March:
                    // Near enough to do something about, or already under its fire.
                    // (Gunnery: and under fire from somewhere it does not know of, the other lane's long diagonal.)
                    if ((threat || st.Gunnery) && (InContact(sq) || pin >= Tune.BoundSweepPin)) task = SquadTask.Contact;
                    break;
                case SquadTask.Contact:
                    if (held >= Tune.ContactTicks) task = SquadTask.Firefight;
                    break;
                case SquadTask.Firefight:
                    if (held < Tune.TaskMin) break;
                    // Out of it only when the enemy is gone, or a good way further off than brought it to ground.
                    if (st.Gunnery ? (!threat || !InContact(sq, Tune.ContactSlack)) && pin < Tune.RallyPin
                                   : !threat || (!InContact(sq, Tune.ContactSlack) && pin < Tune.RallyPin)) { task = SquadTask.March; break; }
                    // With the enemy beyond its weapons it has no fight to stay for: it goes on as soon as it has its breath.
                    // Told to hold the position it is in (its lever), it holds it: it does not close or go in.
                    if (Held(st, sq)) break;
                    if (held < Tune.FirefightMin && !(plan.Advance && sq.ThreatGap > sq.Reach + Tune.ContactPast)) break;
                    if (CanAssault(st, sq, live, pin)) task = SquadTask.Assault;
                    else if (plan.Advance && pin < Tune.PinDrop && !LeadPinned(live) && ShouldClose(st, sq, live, held)) task = SquadTask.Close;
                    break;
                case SquadTask.Close:
                    if (Held(st, sq)) task = SquadTask.Firefight;
                    else if (!threat) task = SquadTask.March;
                    // A squad goes at the pace of its lead man: with him pinned it is going nowhere, and goes to ground.
                    else if (pin >= Tune.PinStop || (held >= Tune.ContactTicks && LeadPinned(live))) task = SquadTask.Firefight;
                    else if (held >= Tune.TaskMin && sq.Halted) task = SquadTask.Firefight;
                    else if (held >= Tune.CloseMax) task = SquadTask.Firefight;
                    break;
                case SquadTask.Assault:
                    if (Held(st, sq)) task = SquadTask.Firefight;
                    else if (!threat) task = SquadTask.March;
                    else if (pin >= (Tune.PinDrop + Tune.PinStop) * 0.5) task = SquadTask.Firefight;      // it falters
                    else if (held >= Tune.AssaultMax && !CanAssault(st, sq, live, pin)) task = SquadTask.Firefight;
                    break;
                case SquadTask.Withdraw:
                    bool there = sq.Target >= 0 && sq.Halted;
                    if (there || held >= Tune.WithdrawMax || (sq.Target < 0 && held >= Tune.TaskMin && pin < Tune.RallyPin))
                    {
                        task = SquadTask.Regroup;
                        sq.Rallied = broken;
                    }
                    break;
                case SquadTask.Regroup:
                    // Spent: it holds what it ran to, and runs again only if it is beaten down again.
                    // (One that was only ordered back, and is whole, goes on when it has its breath.)
                    if (!broken) { if (held >= Tune.FirefightMin) task = SquadTask.March; }
                    else if (plan.Withdraw && held >= Tune.TaskMin && pin >= Tune.PinStop)
                    {
                        task = SquadTask.Withdraw;
                        sq.Rallied = false;
                    }
                    break;
            }
            if (task != sq.Task) Take(st, sq, live, plan, task);

            switch (sq.Task)
            {
                case SquadTask.Withdraw: return Order.Fallback;
                case SquadTask.Contact:
                case SquadTask.Firefight:
                case SquadTask.Regroup: return Order.Hold;
                case SquadTask.Assault:
                    sq.Charge = Math.Max(sq.Charge, 2);
                    return Order.Advance;
                case SquadTask.Close: return Order.Advance;
                default:
                    // On the march: the plan's own tail. Cover that has been ranged in is left; a side that holds, holds.
                    var c0 = Combat.CoverOf(st, live[0]);
                    if (c0 != null && c0.RangedIn >= 0.85) return Order.Advance;
                    if (!plan.Advance) return c0 != null ? Order.Hold : Order.Advance;
                    return Order.Advance;
            }
        }

        /// <summary>
        /// Out of a firefight and forward: to its weapons' distance from an
        /// enemy in front of it that is further off than that; off cover that
        /// has been ranged in; and on, after a while, from an enemy that is not
        /// in front of it at all (the other lane's, or one it has passed).
        /// </summary>
        private static bool ShouldClose(SimState st, Squad sq, IReadOnlyList<Man> live, int held)
        {
            Front(st, sq, Tune.AssaultReach, out _, out _, out double near);
            if (near < 1e9) return near > sq.Reach + 2;
            var c0 = Combat.CoverOf(st, live[0]);
            if (c0 != null && c0.RangedIn >= 0.85) return true;
            return held >= Tune.FirefightMax;
        }

        /// <summary>Take up a task: what the squad does the moment it changes its mind.</summary>
        private static void Take(SimState st, Squad sq, IReadOnlyList<Man> live, Plan plan, SquadTask task)
        {
            var was = sq.Task;
            sq.Task = task;
            sq.TaskSince = st.Tick;
            if (!st.Fieldcraft || !plan.UseCover) return;
            double dir = Combat.Advance(sq.Side);
            var tc = sq.Target >= 0 && sq.Target < st.Cover.Count ? st.Cover[sq.Target] : null;
            bool arrived = tc != null && Fieldcraft.Arrived(sq, tc, dir);
            switch (task)
            {
                case SquadTask.Contact:
                    // Everyone drops where he is, now; a squad already in its cover stays in it.
                    for (int i = 0; i < live.Count; i++) live[i].Dwell = Math.Max(live[i].Dwell, Tune.PostureDwell);
                    if (!arrived) Retarget(sq, live, -1);
                    if (sq.Threat >= 0)
                    {
                        Man lead = null;
                        for (int i = 0; i < st.Men.Count && lead == null; i++)
                            if (st.Men[i].Alive && st.Men[i].Squad == sq.Threat) lead = st.Men[i];
                        st.Events.Add(new SimEvent
                        {
                            Kind = EventKind.Contact, Tick = st.Tick, Side = sq.Side, Id = sq.Id, Target = sq.Threat, X = lead?.X, Z = lead?.Z,
                        });
                    }
                    break;
                case SquadTask.Firefight:
                    if (arrived) break;
                    if (was == SquadTask.Close || was == SquadTask.Assault)
                    {
                        // Stopped short of the cover it was making for: that cover if it is at hand,
                        // or the nearest that is not back the way it came; else where it lies.
                        double d = tc != null ? JsMath.Hypot(Fieldcraft.StopX(tc, dir) - sq.AnchorX, tc.Z - sq.AnchorZ) : double.PositiveInfinity;
                        if (d > Tune.GroundReach || !Clear(st, sq, tc, dir)) Retarget(sq, live, Ground(st, sq, live, dir, Tune.GroundReach, -1.5));
                    }
                    // At first contact: the nearest cover, whichever way; none near, it fights from where it lies.
                    else Retarget(sq, live, Ground(st, sq, live, dir, Tune.GroundReach, double.NegativeInfinity));
                    break;
                case SquadTask.Withdraw:
                    Retarget(sq, live, Ground(st, sq, live, dir, Tune.WithdrawReach, double.NegativeInfinity, -Tune.GroundClear));
                    // Gunnery: they get up and run now.
                    if (st.Gunnery) for (int i = 0; i < live.Count; i++) live[i].Dwell = Math.Max(live[i].Dwell, Tune.PostureDwell);
                    break;
            }
        }

        private static void Retarget(Squad sq, IReadOnlyList<Man> live, int cover)
        {
            if (cover == sq.Target) return;
            Fieldcraft.Release(live);
            sq.Target = cover;
            sq.Held = 0;
        }

        /// <summary>No enemy it knows of within <see cref="Tune.GroundClear"/> of where it would stop in this cover, and none in it.</summary>
        private static bool Clear(SimState st, Squad sq, Cover c, double dir)
        {
            double sx = Fieldcraft.StopX(c, dir);
            for (int i = 0; i < st.Men.Count; i++)
            {
                var e = st.Men[i];
                if (!e.Alive || e.Side == sq.Side) continue;
                if (e.Cover == c.Id) return false;
                if (!Remembers(st, sq, e.Squad)) continue;
                double dx = e.X - sx, dz = e.Z - c.Z;
                if (dx * dx + dz * dz < Tune.GroundClear * Tune.GroundClear) return false;
            }
            return true;
        }

        /// <summary>
        /// The cover a squad goes to ground in: the nearest in its lane within
        /// <paramref name="reach"/> with places for half of it at least and no
        /// enemy at it, no further back than <paramref name="from"/> and no
        /// further on than <paramref name="to"/> (metres ahead of it). -1 if
        /// there is none.
        /// </summary>
        public static int Ground(SimState st, Squad sq, IReadOnlyList<Man> live, double dir, double reach,
                                 double from, double to = double.PositiveInfinity, bool all = false)
        {
            int best = -1;
            double bestD = reach;
            int need = all ? live.Count : (live.Count + 1) / 2;
            for (int ci = 0; ci < st.Cover.Count; ci++)
            {
                var c = st.Cover[ci];
                if (Math.Abs(c.Z - Tune.Lanes[sq.Lane]) > 4.5) continue;
                double sx = Fieldcraft.StopX(c, dir);
                double ahead = (sx - sq.AnchorX) * dir;
                if (ahead < from || ahead > to) continue;
                double d = JsMath.Hypot(sx - sq.AnchorX, c.Z - sq.AnchorZ);
                if (d >= bestD) continue;
                int free = Fieldcraft.FreePlaces(st, c, sq.Side, sq.Id);
                if (free < (all ? need : Math.Min(need, c.Capacity))) continue;
                if (!Clear(st, sq, c, dir)) continue;
                best = c.Id; bestD = d;
            }
            return best;
        }

        /// <summary>
        /// A squad that begins the match holding begins it in its position, not
        /// standing in a file beside it: each man in a place in the nearest cover.
        /// </summary>
        public static void Settle(SimState st, Squad sq)
        {
            var live = Squads.Roster(st, sq.Id);
            double dir = Combat.Advance(sq.Side);
            int cover = Ground(st, sq, live, dir, 20, double.NegativeInfinity, all: true);
            if (cover < 0) cover = Ground(st, sq, live, dir, 20, double.NegativeInfinity);
            if (cover < 0) return;
            var c = st.Cover[cover];
            sq.Target = cover;
            sq.AnchorX = Fieldcraft.StopX(c, dir); sq.AnchorZ = c.Z;
            for (int i = 0; i < live.Count; i++)
            {
                var m = live[i];
                if (i >= c.Capacity)
                {
                    m.X = c.X - dir * (c.Length * 0.5 + 1.5 + (i - c.Capacity) * Tune.SlotGap);
                    continue;
                }
                var at = Fieldcraft.PlaceOf(c, i, dir, Fieldcraft.SideOf(i));
                m.Place = i; m.PlaceCover = cover;
                m.X = at.X; m.Z = at.Z;
                if (Fieldcraft.In(m.X, m.Z, c)) { m.Cover = cover; m.Posture = Posture.Crouched; }
            }
        }
    }
}
