using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Line of sight, fire, suppression and cover.
    ///
    /// Suppression is the core mechanic, so the shape of this file is: most
    /// shots miss, and a miss is not nothing — it buys pin on the man it
    /// passed and on anyone near him. Kills are the rare outcome. A fight is
    /// decided by who is pinned, and morale decides the match.
    ///
    /// Concealment: the VC are not visible until they fire or are found, so a
    /// man who has not been <see cref="Man.Seen"/> cannot be targeted, and
    /// firing sets the flag.
    /// </summary>
    public static class Combat
    {
        public static Side Other(Side s) => s == Side.Us ? Side.Vc : Side.Us;

        /// <summary>Which way a side advances along X.</summary>
        public static float Advance(Side s) => s == Side.Us ? 1f : -1f;

        public static float Dist(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public static float Dist(Man a, Man b) => Dist(a.X, a.Z, b.X, b.Z);

        public static bool CanEngage(Man a, Man b)
        {
            if (!a.Alive || !b.Alive) return false;
            if (a.Side == b.Side) return false;
            // A VC in the grass is not a target until he gives himself away.
            if (!b.Seen) return false;
            return Dist(a, b) <= Tune.Range;
        }

        /// <summary>
        /// Is this man behind that cover?
        ///
        /// Along the lane it is the berm's length that matters; across it, how
        /// close he is tucked in. The two are not the same distance, and
        /// treating them as one radius is what left a squad's worth of men
        /// standing in the open beside a berm long enough to lie down behind.
        /// </summary>
        public static bool InCover(float x, float z, Cover c)
            => Math.Abs(x - c.X) <= c.Length * 0.5f
            && Math.Abs(z - c.Z) <= Tune.CoverRadius;

        public static Cover CoverOf(SimState st, Man m)
            => m.Cover >= 0 && m.Cover < st.Cover.Count ? st.Cover[m.Cover] : null;

        /// <summary>
        /// What a piece of cover is actually worth to the man in it.
        ///
        /// Quality falls with crowding — each man past capacity costs everyone
        /// in it — and the position being ranged in is modelled on the
        /// incoming side in <see cref="HitChance"/>, which is what stops
        /// holding ground being a free win.
        /// </summary>
        public static float CoverValue(SimState st, Cover c)
        {
            int n = 0;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                if (m.Alive && m.Cover == c.Id) n++;
            }
            int over = Math.Max(0, n - c.Capacity);
            float crowded = Math.Max(0f, c.Quality * Tune.CoverBoost
                                        - over * Tune.CrowdingPenalty);
            return Math.Min(0.85f, crowded);
        }

        private static float Exposure(Posture p) => p switch
        {
            Posture.Standing => Tune.ExposureStand,
            Posture.Crouched => Tune.ExposureCrouch,
            _ => Tune.ExposureProne,
        };

        /// <summary>
        /// Chance a single shot from <paramref name="a"/> connects with
        /// <paramref name="b"/>.
        ///
        /// Every term is a rule rather than a fudge: range, the shooter's own
        /// suppression, the target's posture and cover, whether he is moving,
        /// and whether his position has been ranged in.
        /// </summary>
        public static float HitChance(SimState st, Man a, Man b)
        {
            float d = Dist(a, b);
            float t = Math.Min(1f, d / Tune.Range);
            float p = Tune.HitBase * (1f - t * (1f - Tune.HitAtRange));

            // A suppressed man shoots worse. Veterancy steadies the aim but
            // never raises the ceiling — steadier, not stronger.
            float steadied = a.Pin * (1f - Tune.VetPinResist * a.Veterancy);
            p *= Math.Max(0.15f, 1f - steadied);

            p *= Exposure(b.Posture);

            var c = CoverOf(st, b);
            if (c != null)
            {
                p *= 1f - CoverValue(st, c);
                if (c.RangedIn >= 1) p *= Tune.RangedInBonus;
            }

            // Crossing open ground under fire has to actually cost something,
            // or there is no value in waiting for someone to cover you. At
            // 1.18 for any movement and nothing for being in the open,
            // bounding measured as *neutral* against a plan that simply
            // walked. A man on his feet in the open is the easiest target on
            // the field; the same man moving cover to cover is only a little
            // worse off than one lying still.
            if (Squads.IsMoving(b)) p *= c != null ? 1.10f : Tune.MovingInOpen;

            return Math.Max(0f, Math.Min(0.9f, p));
        }

        /// <summary>
        /// Does the line between two men pass through smoke?
        ///
        /// Smoke is the only thing in the game that breaks a firing line
        /// without killing anyone. Closest approach of the segment to the
        /// circle centre — cheap, and exact.
        /// </summary>
        public static bool SmokeBlocks(SimState st, float ax, float az,
                                       float bx, float bz)
        {
            if (st.Areas.Count == 0) return false;
            float dx = bx - ax, dz = bz - az;
            float len2 = dx * dx + dz * dz;
            for (int i = 0; i < st.Areas.Count; i++)
            {
                var a = st.Areas[i];
                if (a.Kind != AreaKind.Smoke) continue;
                float t = len2 > 0 ? ((a.X - ax) * dx + (a.Z - az) * dz) / len2 : 0f;
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                float cx = ax + dx * t, cz = az + dz * t;
                if (Dist(a.X, a.Z, cx, cz) <= a.Radius) return true;
            }
            return false;
        }

        /// <summary>
        /// The nearest enemy this man can engage, or null.
        ///
        /// Squared distances, no square root per candidate: this runs over
        /// every living man and is the hottest thing in the simulation.
        /// </summary>
        public static Man PickTarget(SimState st, Man a)
        {
            Man best = null;
            float bestD2 = Tune.Range * Tune.Range;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var b = st.Men[i];
                if (!b.Alive || !b.Seen || b.Side == a.Side) continue;
                float dx = a.X - b.X, dz = a.Z - b.Z;
                float d2 = dx * dx + dz * dz;
                if (d2 > bestD2) continue;
                // Checked here rather than in HitChance: a man who cannot see
                // a target should look for another one, not shoot at this one
                // badly.
                if (SmokeBlocks(st, a.X, a.Z, b.X, b.Z)) continue;
                bestD2 = d2; best = b;
            }
            return best;
        }

        /// <summary>
        /// Resolve one man's fire for this tick. Returns true if he fired.
        ///
        /// A pinned man fires far less often rather than not at all: a hard
        /// cutoff makes a firefight flip between two states, and what is
        /// wanted is pinned men who shoot less, not men who switch off.
        /// </summary>
        public static bool Fire(SimState st, Man a, Rng rng)
        {
            if (!a.Alive || a.Cooldown > 0) return false;

            var target = PickTarget(st, a);
            if (target == null)
            {
                // Nothing to shoot at. Wait before looking again.
                //
                // Without this, a man with no target leaves his cooldown at
                // zero and rescans every living man on every subsequent tick,
                // for ever. Profiled in the previous build: `fire` and the
                // distance calls inside it were 55% of the whole simulation,
                // nearly all of it men out of contact confirming twenty times
                // a second that they were still out of contact.
                a.Cooldown = Tune.ScanIdle;
                return false;
            }

            float pinFrac = Math.Min(1f, a.Pin / Tune.PinStop);
            float rate = 1f - (1f - Tune.PinnedFireRate) * pinFrac;
            if (rng.Next() > rate) { a.Cooldown = 2; return false; }

            a.Cooldown = Tune.Cooldown;
            // Firing gives away concealment. The only thing besides being
            // found at close range that sets this.
            a.Seen = true;
            st.Events.Add(new SimEvent
            {
                Kind = EventKind.Fire, Tick = st.Tick, Side = a.Side,
                Id = a.Id, Target = target.Id,
            });

            float p = HitChance(st, a, target);
            if (rng.Next() < p)
            {
                target.Alive = false;
                target.Cover = -1;
                target.DiedAt = st.Tick;
                st.Events.Add(new SimEvent
                {
                    Kind = EventKind.Kill, Tick = st.Tick,
                    Side = target.Side, Id = target.Id,
                });
                return true;
            }

            // The miss is the point. It buys pin on the man it passed and on
            // everyone near him, which is how fire suppresses a *position*
            // rather than a man.
            ApplyPin(st, target, Tune.PinPerNearMiss);
            float r2 = Tune.PinSplash * Tune.PinSplash;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var n = st.Men[i];
                if (!n.Alive || n.Side != target.Side || n.Id == target.Id) continue;
                float dx = n.X - target.X, dz = n.Z - target.Z;
                float d2 = dx * dx + dz * dz;
                if (d2 < r2)
                {
                    float d = (float)Math.Sqrt(d2);
                    ApplyPin(st, n, Tune.PinPerNearMiss * (1f - d / Tune.PinSplash) * 0.6f);
                }
            }
            return true;
        }

        /// <summary>Add suppression, resisted by veterancy, and emit the crossing event.</summary>
        public static void ApplyPin(SimState st, Man m, float amount)
        {
            float before = m.Pin;
            float resist = 1f - Tune.VetPinResist * m.Veterancy;
            m.Pin = Math.Min(1f, m.Pin + amount * resist);
            if (before < Tune.PinDrop && m.Pin >= Tune.PinDrop)
            {
                st.Events.Add(new SimEvent
                {
                    Kind = EventKind.Pinned, Tick = st.Tick,
                    Side = m.Side, Id = m.Id,
                });
            }
        }

        /// <summary>Suppression decays. Emitted as an event when a man comes out of it.</summary>
        public static void Relax(SimState st, Man m)
        {
            if (m.Pin <= 0) return;
            float before = m.Pin;
            m.Pin = Math.Max(0f, m.Pin - Tune.PinDecay * Tune.Dt);
            if (before >= Tune.PinDrop && m.Pin < Tune.PinDrop)
            {
                st.Events.Add(new SimEvent
                {
                    Kind = EventKind.Unpinned, Tick = st.Tick,
                    Side = m.Side, Id = m.Id,
                });
            }
        }
    }
}
