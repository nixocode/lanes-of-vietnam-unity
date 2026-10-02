using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Line of sight, fire, suppression and cover.
    ///
    /// Suppression is the core mechanic, so the shape of this file is: most
    /// shots miss, and a miss is not nothing — it buys pin on the man it
    /// passed and on everyone near him. Kills are the rare outcome. A fight is
    /// decided by who is pinned, and morale decides the match.
    ///
    /// Concealment: the VC are not visible until they fire or are found, so a
    /// man who has not been <see cref="Man.Seen"/> cannot be targeted, and
    /// firing sets the flag.
    /// </summary>
    public static class Combat
    {
        public static Side Other(Side s) => s == Side.Us ? Side.Vc : Side.Us;

        /// <summary>Which way a side advances along X. US pushes +x, VC pushes -x.</summary>
        public static double Advance(Side s) => s == Side.Us ? 1 : -1;

        public static double Dist(double ax, double az, double bx, double bz)
            => JsMath.Hypot(ax - bx, az - bz);

        public static double Dist(Man a, Man b) => Dist(a.X, a.Z, b.X, b.Z);

        public static bool CanEngage(Man a, Man b)
        {
            if (!a.Alive || !b.Alive) return false;
            if (a.Side == b.Side) return false;
            // A VC in the grass is not a target until he gives himself away.
            if (!b.Seen) return false;
            return Dist(a, b) <= Tune.Range;
        }

        /// <summary>
        /// Is this point behind that cover? Along the lane it is the berm's
        /// length that matters; across it, how close he is tucked in. One
        /// radius for both left a squad's worth of men standing in the open
        /// beside a berm long enough to lie down behind.
        /// </summary>
        public static bool InCover(double x, double z, Cover c)
            => Math.Abs(x - c.X) <= c.Length * 0.5
            && Math.Abs(z - c.Z) <= Tune.CoverRadius;

        public static Cover CoverOf(SimState st, Man m)
            => m.Cover >= 0 && m.Cover < st.Cover.Count ? st.Cover[m.Cover] : null;

        /// <summary>
        /// What a piece of cover is actually worth to the man in it. Falls with
        /// crowding; being ranged in is modelled on the incoming side in
        /// <see cref="HitChance"/>.
        /// </summary>
        public static double CoverValue(SimState st, Cover c)
        {
            int n = 0;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                if (m.Alive && m.Cover == c.Id) n++;
            }
            int over = Math.Max(0, n - c.Capacity);
            double crowded = Math.Max(0, c.Quality * Tune.CoverBoost - over * Tune.CrowdingPenalty);
            return Math.Min(0.85, crowded);
        }

        /// <summary>
        /// Chance a single shot from <paramref name="a"/> connects with
        /// <paramref name="b"/>. Every term is a rule rather than a fudge:
        /// range, the shooter's own suppression, the target's posture and
        /// cover, whether he is moving, whether his position is ranged in.
        /// </summary>
        public static double HitChance(SimState st, Man a, Man b)
        {
            double d = Dist(a, b);
            // Arms: his weapon's range, its accuracy and what is left of it out there (the baseline's rifle otherwise).
            var arm = Arms.Of(st, a);
            double t = Math.Min(1, d / arm.Range);
            double p = Tune.HitBase * (1 - t * (1 - arm.AtRange));
            if (st.Arms) p *= arm.Hit;

            // A suppressed man shoots worse. Veterancy steadies the aim but
            // never raises the ceiling — steadier, not stronger.
            double steadied = a.Pin * (1 - Tune.VetPinResist * a.Veterancy);
            // Gunnery: at a few paces fear does not make a man miss by much. (Two squads that had run
            // into each other lay four metres apart, pinned, for twelve seconds.)
            p *= Math.Max(st.Gunnery && d < Tune.CloseRange ? Tune.CloseSteady : 0.15, 1 - steadied);

            p *= Tune.Exposure(b.Posture);
            // Gunnery: a man shooting from his feet hits a third as often.
            if (st.Gunnery && a.Posture == Posture.Standing) p *= Tune.StandingHit;

            var c = CoverOf(st, b);
            // Fieldcraft: a parapet is no cover from a man standing on it.
            if (c != null && !(st.Fieldcraft && d < Tune.ChargeRange * 0.5))
            {
                p *= 1 - CoverValue(st, c) * (1 - arm.Pierce);
                if (c.RangedIn >= 1) p *= Tune.RangedInBonus;
            }
            if (st.Fieldcraft && d < Tune.CloseRange) p *= 1 + Tune.CloseBonus * (1 - d / Tune.CloseRange);

            // Crossing open ground under fire has to cost something, or there
            // is no value in waiting for someone to cover you. At 1.18 for any
            // movement and nothing for being in the open, bounding measured as
            // neutral against a plan that simply walked.
            if (Squads.IsMoving(b)) p *= c != null ? 1.10 : Tune.MovingInOpen;

            return Math.Max(0, Math.Min(0.9, p));
        }

        /// <summary>
        /// Does the line between two men pass through smoke? Smoke is the only
        /// thing that breaks a firing line without killing anyone. Closest
        /// approach of the segment to the circle centre.
        /// </summary>
        public static bool SmokeBlocks(SimState st, double ax, double az, double bx, double bz)
        {
            if (st.Areas.Count == 0) return false;
            double dx = bx - ax, dz = bz - az;
            double len2 = dx * dx + dz * dz;
            for (int i = 0; i < st.Areas.Count; i++)
            {
                var a = st.Areas[i];
                if (a.Kind != AreaKind.Smoke) continue;
                double t = len2 > 0 ? ((a.X - ax) * dx + (a.Z - az) * dz) / len2 : 0;
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                double cx = ax + dx * t, cz = az + dz * t;
                if (JsMath.Hypot(a.X - cx, a.Z - cz) <= a.Radius) return true;
            }
            return false;
        }

        /// <summary>
        /// The nearest enemy this man can engage, or null. Squared distances:
        /// this runs over every living man and is the hottest loop in the sim.
        /// </summary>
        public static Man PickTarget(SimState st, Man a)
        {
            Man best = null;
            double reach = Arms.Of(st, a).Range;
            double bestD2 = reach * reach;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var b = st.Men[i];
                if (!b.Alive || !b.Seen || b.Side == a.Side) continue;
                if (st.Gunnery && !Gunnery.InArc(a, b)) continue;
                double dx = a.X - b.X, dz = a.Z - b.Z;
                double d2 = dx * dx + dz * dz;
                if (d2 > bestD2) continue;
                // Here rather than in HitChance: a man who cannot see a target
                // should look for another one, not shoot at this one badly.
                if (SmokeBlocks(st, a.X, a.Z, b.X, b.Z)) continue;
                bestD2 = d2; best = b;
            }
            return best;
        }

        /// <summary>
        /// Resolve one man's fire for this tick. Returns true if he fired. A
        /// pinned man fires far less often rather than not at all.
        /// </summary>
        public static bool Fire(SimState st, Man a, Rng rng)
        {
            if (!a.Alive || a.Cooldown > 0) return false;

            if (st.Fieldcraft)
            {
                // Climbing, he does nothing else; within reach of an enemy, he uses his hands.
                if (a.Vault > 0) return false;
                var close = Fieldcraft.InReach(st, a);
                if (close != null) { Fieldcraft.Strike(st, a, close, rng); return true; }
            }

            var arm = Arms.Of(st, a);
            // Gunnery: nobody fires on the move.
            if (st.Gunnery && !Gunnery.Steady(a)) { a.Cooldown = Tune.ScanIdle; return false; }
            // Arms: a launcher or a mortar fires a bursting round, not a bullet (and not while he is pinned flat).
            if (st.Arms && arm.Bursts)
            {
                if (st.Gunnery && a.Posture == Posture.Standing && st.GunRng.Next() > Tune.StandingFire) { a.Cooldown = Tune.Cooldown; return false; }
                return a.Pin < Tune.PinStop && Arms.Launch(st, a, arm, st.FragRng);
            }

            // Senses: only at a squad his own has in sight; a machine gun with nothing in sight keeps
            // bursts on the cover of one it knows was there.
            var target = st.Senses ? Senses.PickTarget(st, a) : PickTarget(st, a);
            bool blind = false;
            if (target == null && st.Senses) { target = Senses.Suppress(st, a); blind = target != null; }
            if (target == null)
            {
                // Nothing to shoot at: wait before looking again, or he rescans
                // every living man every tick for ever (55% of the sim).
                a.Cooldown = Tune.ScanIdle;
                return false;
            }

            double pinFrac = Math.Min(1, a.Pin / Tune.PinStop);
            double rate = 1 - (1 - Tune.PinnedFireRate) * pinFrac;
            // Gunnery: nor does it stop him shooting at a man that near.
            if (st.Gunnery && Dist(a, target) < Tune.CloseRange) rate = Math.Max(rate, Tune.CloseSteady);
            if (rng.Next() > rate) { a.Cooldown = 2; return false; }
            // Gunnery: a man on his feet takes one chance in ten.
            if (st.Gunnery && a.Posture == Posture.Standing && st.GunRng.Next() > Tune.StandingFire) { a.Cooldown = arm.Cooldown; return false; }

            a.Cooldown = arm.Cooldown;
            // Senses: at a man he cannot himself see, on his squad's word, a rifleman fires slower.
            if (st.Senses && !blind && a.Weapon != Weapon.M60 && a.Weapon != Weapon.Rpd && !Senses.Sees(st, a, target))
                a.Cooldown = (int)(arm.Cooldown * Tune.BlindCooldown);
            // Firing gives away concealment.
            a.Seen = true;
            a.FiredAt = st.Tick;
            double p = HitChance(st, a, target);
            bool hit = !blind && rng.Next() < p;
            // Gunnery: the round goes somewhere. A hit is on the man; a miss comes down past him.
            double? atX = null, atZ = null;
            if (st.Gunnery)
            {
                if (hit) { atX = target.X; atZ = target.Z; }
                else { var (mx, mz) = Gunnery.Miss(st, a, target); atX = mx; atZ = mz; }
            }
            st.Events.Add(new SimEvent
            {
                Kind = EventKind.Fire, Tick = st.Tick, Side = a.Side,
                Id = a.Id, Target = target.Id, Amount = blind ? 1 : (double?)null, X = atX, Z = atZ,
            });

            if (hit)
            {
                Kill(st, target);
                if (st.Fieldcraft) Fieldcraft.Through(st, a, target, rng);
                return true;
            }

            // The miss is the point: pin on the man it passed and everyone
            // near him, which is how fire suppresses a position.
            double near = Tune.PinPerNearMiss * arm.Pin * (blind ? Tune.SuppressPin : 1);
            ApplyPin(st, target, near);
            double r2 = Tune.PinSplash * Tune.PinSplash;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var n = st.Men[i];
                if (!n.Alive || n.Side != target.Side || n.Id == target.Id) continue;
                double dx = n.X - target.X, dz = n.Z - target.Z;
                double d2 = dx * dx + dz * dz;
                if (d2 < r2)
                {
                    double d = Math.Sqrt(d2);
                    ApplyPin(st, n, near * (1 - d / Tune.PinSplash) * 0.6);
                }
            }
            return true;
        }

        /// <summary>One death, recorded once, the same way whatever killed him.</summary>
        public static void Kill(SimState st, Man m)
        {
            m.Alive = false;
            m.Cover = -1;
            m.DiedAt = st.Tick;
            st.Events.Add(new SimEvent
            {
                Kind = EventKind.Kill, Tick = st.Tick, Side = m.Side, Id = m.Id,
            });
            // After the kill is written: the view reads it as the event straight after the shot.
            if (st.Fieldcraft) Fieldcraft.Fell(st, m);
        }

        /// <summary>Add suppression, resisted by veterancy, and emit the crossing event.</summary>
        public static void ApplyPin(SimState st, Man m, double amount)
        {
            double before = m.Pin;
            double resist = 1 - Tune.VetPinResist * m.Veterancy;
            m.Pin = Math.Min(1, m.Pin + amount * resist);
            if (before < Tune.PinDrop && m.Pin >= Tune.PinDrop)
            {
                st.Events.Add(new SimEvent
                {
                    Kind = EventKind.Pinned, Tick = st.Tick, Side = m.Side, Id = m.Id,
                });
                // Senses: a man the fire has found drops now, not when he has finished standing there.
                if (st.Senses) m.Dwell = Math.Max(m.Dwell, Tune.PostureDwell);
            }
        }

        /// <summary>
        /// Suppression decays, faster for a man who is flat. The last port
        /// dropped the prone rate; the original has it, and without it a
        /// pinned man recovers as slowly lying down as standing up.
        /// </summary>
        public static void Relax(SimState st, Man m)
        {
            double before = m.Pin;
            double rate = Tune.PinDecay * (m.Posture == Posture.Prone ? Tune.PinDecayProne : 1.0);
            m.Pin = Math.Max(0, m.Pin - rate * Tune.Dt);
            if (before >= Tune.PinDrop && m.Pin < Tune.PinDrop)
            {
                st.Events.Add(new SimEvent
                {
                    Kind = EventKind.Unpinned, Tick = st.Tick, Side = m.Side, Id = m.Id,
                });
            }
        }

        // --- per-tick lane aggregates ---------------------------------------------

        /// <summary>
        /// Per-tick aggregates by side and lane, built in one pass.
        ///
        /// The questions "is this lane swept" and "is anyone covering" used to
        /// each walk every man per squad per tick — S x N x S, which put a full
        /// match at two seconds against "well under one". Combat in a lane
        /// game is one-dimensional enough that the enemy's most advanced
        /// visible man is the right proxy for "is there anyone to shoot at".
        /// </summary>
        public sealed class LaneIndex
        {
            internal readonly double[] PinSum;
            internal readonly int[] Count;
            internal readonly double[] Lead;
            internal readonly bool[] HasLead;
            internal readonly List<int>[] Firing;

            internal LaneIndex(int lanes)
            {
                int n = 2 * lanes;
                PinSum = new double[n];
                Count = new int[n];
                Lead = new double[n];
                HasLead = new bool[n];
                Firing = new List<int>[n];
                for (int i = 0; i < n; i++) Firing[i] = new List<int>(4);
            }

            internal static int Key(Side side, int lane) => (int)side * Tune.Lanes.Length + lane;
        }

        public static LaneIndex BuildLaneIndex(SimState st)
        {
            var ix = new LaneIndex(Tune.Lanes.Length);
            // Squad ids are list indices; see SimState.NextSquadId.
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                if (!m.Alive) continue;
                var sq = st.Squads[m.Squad];
                int k = LaneIndex.Key(sq.Side, sq.Lane);
                ix.PinSum[k] += m.Pin;
                ix.Count[k]++;
                // Only a man who can be seen can be shot at, so only he is a
                // reason to put covering fire down.
                if (m.Seen)
                {
                    double forward = m.X * Advance(sq.Side);
                    if (!ix.HasLead[k] || forward > ix.Lead[k])
                    {
                        ix.Lead[k] = forward;
                        ix.HasLead[k] = true;
                    }
                }
            }

            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                if (!m.Alive) continue;
                var sq = st.Squads[m.Squad];
                if (m.Pin >= Tune.PinDrop) continue;
                var enemy = Other(sq.Side);
                int ek = LaneIndex.Key(enemy, sq.Lane);
                if (!ix.HasLead[ek]) continue;
                double enemyX = ix.Lead[ek] * Advance(enemy);
                if (Math.Abs(enemyX - m.X) > Tune.Range) continue;
                var set = ix.Firing[LaneIndex.Key(sq.Side, sq.Lane)];
                if (!set.Contains(m.Squad)) set.Add(m.Squad);
            }
            return ix;
        }

        /// <summary>
        /// Is this lane being swept? Measured as the mean pin of the side's men
        /// in it — the observable the men themselves have.
        /// </summary>
        public static bool LaneSwept(LaneIndex ix, Side side, int lane)
        {
            int k = LaneIndex.Key(side, lane);
            if (ix.Count[k] == 0) return false;
            return ix.PinSum[k] / ix.Count[k] >= Tune.BoundSweepPin;
        }

        /// <summary>Is a friendly squad in this lane putting fire down to cover a bound?</summary>
        public static bool HasCoveringFire(LaneIndex ix, Side side, int lane, int movingSquad)
        {
            var set = ix.Firing[LaneIndex.Key(side, lane)];
            for (int i = 0; i < set.Count; i++) if (set[i] != movingSquad) return true;
            return false;
        }

        /// <summary>
        /// The best cover a squad can make for, or -1.
        ///
        /// <paramref name="forwardOnly"/> is the difference between taking
        /// ground and holding it. An attacker considers only cover ahead of
        /// him, so moving from one position to the next is his advance. A
        /// defender considers either side, or "hold" still walks him up the
        /// map: defend against defend closed to contact and averaged fifty
        /// casualties a match.
        /// </summary>
        public static int BestCover(SimState st, double fromX, int lane, double dir, bool forwardOnly = true, Side? sentBy = null, int need = 1)
        {
            int best = -1;
            double bestScore = double.NegativeInfinity;
            for (int ci = 0; ci < st.Cover.Count; ci++)
            {
                var c = st.Cover[ci];
                if (Math.Abs(c.Z - Tune.Lanes[lane]) > 4.5) continue;
                // Fieldcraft: a position its side's lever has on Go is passed through, not stopped in.
                if (sentBy.HasValue && Fieldcraft.IsPosition(c) && Fieldcraft.LeverOf(c, sentBy.Value) == Lever.Go) continue;
                double ahead = (c.X - fromX) * dir;
                if (forwardOnly && ahead <= 0.5) continue;
                double reach = Math.Abs(ahead);
                if (!forwardOnly && reach > Tune.HoldRadius) continue;
                int n = 0;
                for (int i = 0; i < st.Men.Count; i++)
                {
                    var m = st.Men[i];
                    if (m.Alive && m.Cover == c.Id) n++;
                }
                int room = Math.Max(0, c.Capacity - n);
                if (room <= 0) continue;
                // Fieldcraft: room for the squad, not for one man of it.
                if (room < Math.Min(need, c.Capacity)) continue;
                // Near, roomy, good and not already ranged in.
                double score = c.Quality * 3 + room * 0.5 - reach * 0.05 - c.RangedIn * 2.5;
                if (score > bestScore) { bestScore = score; best = c.Id; }
            }
            return best;
        }

        /// <summary>
        /// Advance the ranged-in timer on every piece of cover: it runs while a
        /// side sits in it and forgets once the position is abandoned.
        /// </summary>
        public static void UpdateRangedIn(SimState st)
        {
            for (int ci = 0; ci < st.Cover.Count; ci++)
            {
                var c = st.Cover[ci];
                Side? occupant = null;
                int n = 0;
                for (int i = 0; i < st.Men.Count; i++)
                {
                    var m = st.Men[i];
                    if (!m.Alive || m.Cover != c.Id) continue;
                    occupant = m.Side; n++;
                }
                if (n > 0)
                {
                    if (c.HeldBy != occupant) { c.HeldBy = occupant; c.RangedIn = 0; }
                    double before = c.RangedIn;
                    c.RangedIn += Tune.Dt / Tune.RangeInSeconds;
                    if (before < 1 && c.RangedIn >= 1)
                    {
                        st.Events.Add(new SimEvent
                        {
                            Kind = EventKind.RangedIn, Tick = st.Tick,
                            Side = Other(occupant.Value), Id = c.Id,
                        });
                    }
                }
                else
                {
                    c.HeldBy = null;
                    c.RangedIn = Math.Max(0, c.RangedIn - Tune.Dt / Tune.RangeInDecay);
                }
            }
        }
    }
}
