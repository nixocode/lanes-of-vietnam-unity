using System;
using System.Collections.Generic;
using System.Linq;

namespace LanesOfVietnam.Sim
{
    /// <summary>How a side plays. The balance harness brackets a real player with two of these.</summary>
    public sealed class Plan
    {
        public string Name;
        /// <summary>Spend command points on reinforcement at all?</summary>
        public bool Reinforce;
        /// <summary>Use cover, or walk up the lane?</summary>
        public bool UseCover;
        /// <summary>Bound by pairs, or advance everyone at once?</summary>
        public bool Bound;
        /// <summary>Pull broken squads back instead of feeding them in.</summary>
        public bool Withdraw;
        /// <summary>
        /// Take ground, or hold it? With every plan advancing, contact is
        /// guaranteed and both time endings were dead code (§9 finding 4).
        /// </summary>
        public bool Advance;

        /// <summary>The floor: buys men and walks them forward.</summary>
        public static readonly Plan Floor = new Plan
        {
            Name = "floor", Reinforce = true, UseCover = false, Bound = false, Withdraw = false, Advance = true,
        };

        /// <summary>The ceiling: uses everything the brief gives it.</summary>
        public static readonly Plan Ceiling = new Plan
        {
            Name = "ceiling", Reinforce = true, UseCover = true, Bound = true, Withdraw = true, Advance = true,
        };

        /// <summary>Holds what it has and wins only by bleeding the attacker.</summary>
        public static readonly Plan Defend = new Plan
        {
            Name = "defend", Reinforce = true, UseCover = true, Bound = false, Withdraw = true, Advance = false,
        };

        /// <summary>
        /// No replacements: the only plan under which a side can actually be
        /// wiped out, since morale empties at about 36 casualties.
        /// </summary>
        public static readonly Plan NoReinforce = new Plan
        {
            Name = "cut-off", Reinforce = false, UseCover = true, Bound = true, Withdraw = false, Advance = true,
        };

        public static Plan ByName(string name) => name switch
        {
            "floor" => Floor,
            "ceiling" => Ceiling,
            "defend" => Defend,
            "cut-off" => NoReinforce,
            _ => throw new ArgumentException($"unknown plan \"{name}\" (floor | ceiling | defend | cut-off)"),
        };
    }

    /// <summary>
    /// How long a match is meant to run — a setting, by the owner's answer of
    /// 2026-09-28. It scales morale drain, the clock the match runs on.
    /// </summary>
    public enum MatchLength { Skirmish, Standard, Siege }

    public sealed class MatchOptions
    {
        public int Seed;
        public Plan Us = Plan.Ceiling;
        public Plan Vc = Plan.Ceiling;
        public int? MaxTicks;
        public MatchLength Length = MatchLength.Standard;

        /// <summary>
        /// The map's cover. Null lays it out from the seed, as the original
        /// did — which is what the parity check compares against. The game
        /// passes the map's own list, the one its geometry is built from.
        /// </summary>
        public IReadOnlyList<Cover> Cover;

        /// <summary>Part 2: grenades (PLAN §12.8). False is the parity baseline.</summary>
        public bool Frag;
        /// <summary>Part 2: squads pinned on the move pop smoke (PLAN §12.8). False is the parity baseline.</summary>
        public bool SquadSmoke;
        /// <summary>Part 2: orders that stand, broken squads that hold, men who keep their places (Drill). False is the parity baseline.</summary>
        public bool Drill;
        /// <summary>Part 2: files, places in cover, the stand-off, the assault, melee, levers (Fieldcraft). False is the parity baseline.</summary>
        public bool Fieldcraft;
        /// <summary>Part 2: a weapon to every man, a squad to every card, a distance to every fight (Arms). False is the parity baseline.</summary>
        public bool Arms;
        /// <summary>Part 2: squads that spot each other, fire only at what they have spotted, and react to contact (Senses). False is the parity baseline.</summary>
        public bool Senses;

        /// <summary>Command points a second, each side, and what each starts with. The baseline's: 0.9 and nothing.</summary>
        public double CpRate = Tune.CpPerSecond, StartCp = 0;
        /// <summary>What a squad costs a side whose plan raises its own. The baseline's: 22.</summary>
        public double MusterCost = Tune.CpPerSquad;
        /// <summary>Men a side has on the map at the start, at least. The baseline's: 16.</summary>
        public int OpeningStrength = Tune.OpeningStrength;
        /// <summary>
        /// The side a person is playing, if one is: its plan raises no squads
        /// for it, because its points are his to spend. (Left to the plan, a
        /// player's side bought a squad for him every time he reached 22: he
        /// could never save for a card that cost more, and men he had not
        /// asked for kept arriving.) Null: both sides raise their own.
        /// </summary>
        public Side? Player;
    }

    public sealed class MatchResult
    {
        public Side? Winner;
        public string Reason;
        public int Ticks;
        public double Seconds;
        public double[] Morale;
        public int[] Casualties;
        public double[] MoraleLostToCasualties;
        public double[] MoraleLostToGround;
        /// <summary>Count of every event kind that fired, for §9 finding 5.</summary>
        public Dictionary<EventKind, int> EventCounts;
        /// <summary>Kills made by grenade blasts (MatchOptions.Frag): each blast's kills follow it in the log.</summary>
        public int GrenadeKills;
    }

    /// <summary>
    /// The match: setup, the tick, and the win conditions.
    ///
    /// A match is a pure function of (seed, plans, commands). Nothing here
    /// reads a clock, a frame time or the engine. The view steps it at a fixed
    /// 20 Hz and interpolates between ticks.
    ///
    /// §9 finding 4 is why every ending is in one function with a stated
    /// reason: in the 2D game three separate rules silently made two missions
    /// unwinnable, lost 0 times in 24 before anyone found it.
    ///
    /// Ported line for line from the TypeScript original, in its evaluation
    /// order — including the order of random draws inside what were object
    /// literals there — because <c>tools/parity</c> checks this against the
    /// original tick by tick, and a single draw out of place is a different
    /// match from that tick on.
    /// </summary>
    public static class Match
    {
        public static readonly Side[] Sides = { Side.Us, Side.Vc };

        public static double LengthScale(MatchLength l) => l switch
        {
            MatchLength.Skirmish => Tune.LengthSkirmish,
            MatchLength.Siege => Tune.LengthSiege,
            _ => Tune.LengthStandard,
        };

        /// <summary>"us" / "vc", as the original's endings name them.</summary>
        public static string Name(Side s) => s == Side.Us ? "us" : "vc";

        // --- setup -------------------------------------------------------------

        internal static Squad SpawnSquad(SimState st, Side side, int lane, double x, Rng rng,
                                         int? size = null, string card = null)
        {
            // Arms: the squad its card names, or the one the side raises next by itself.
            Kit kit = st.Arms ? ((card != null ? Arms.For(card) : null) ?? Arms.Raised(st, side)) : null;
            if (kit != null) size = kit.Men.Length;
            var sq = new Squad
            {
                Id = st.NextSquadId++, Side = side, Order = Order.Advance,
                AnchorX = x, AnchorZ = Tune.Lanes[lane], Lane = lane,
                Held = 0, Target = -1, Bounding = false, PlayerOrder = null,
            };
            st.Squads.Add(sq);
            if (kit != null) { sq.Reach = kit.Reach; sq.Assaults = kit.Assaults; }
            if (st.Fieldcraft) Fieldcraft.Raised(st, sq);
            int n = size ?? rng.Int(Tune.SquadMin, Tune.SquadMax + 1);
            double dir = Combat.Advance(side);
            for (int i = 0; i < n; i++)
            {
                // Draw order as the original's object literal: z, then cooldown.
                double mx = x - dir * i * Tune.SlotGap;
                double mz = Tune.Lanes[lane] + rng.Range(-1.2, 1.2);
                if (st.Fieldcraft) { mx = Fieldcraft.SpawnX(x, dir, i, n); mz = Fieldcraft.SpawnZ(sq, i); }
                int cd = rng.Int(0, Tune.Cooldown);
                st.Men.Add(new Man
                {
                    Id = st.NextManId++, Squad = sq.Id, Side = side,
                    X = mx, Z = mz, Alive = true, Pin = 0, Posture = Posture.Standing,
                    Cooldown = cd, Cover = -1, Dwell = 0,
                    // The Americans are visible; the VC are concealed until
                    // they fire or are found.
                    // (Senses: nobody is seen until an enemy squad has him in sight.)
                    Seen = side == Side.Us && !st.Senses,
                    Veterancy = 0, DiedAt = -1,
                    Rank = i,
                    Weapon = kit != null ? kit.Men[i] : Weapon.Rifle,
                });
            }
            st.Events.Add(new SimEvent { Kind = EventKind.SquadSpawned, Tick = st.Tick, Side = side, Id = sq.Id });
            return sq;
        }

        public static SimState Create(MatchOptions opts)
        {
            var rng = new Rng(opts.Seed);
            var st = new SimState
            {
                Tick = 0, Phase = Phase.Opening, ContactTick = -1,
                MoraleScale = LengthScale(opts.Length),
                Frag = opts.Frag,
                SquadSmoke = opts.SquadSmoke,
                Drill = opts.Drill,
                Fieldcraft = opts.Fieldcraft,
                Arms = opts.Arms,
                Senses = opts.Senses,
                CpRate = opts.CpRate, MusterCost = opts.MusterCost, Player = opts.Player,
            };
            st.Cp[0] = st.Cp[1] = opts.StartCp;
            // A fork reads the parent's state without drawing from it.
            if (opts.Frag || opts.Arms) st.FragRng = rng.Fork("frag");
            st.Front[(int)Side.Us] = -Tune.HalfLength * 0.6;
            st.Front[(int)Side.Vc] = Tune.HalfLength * 0.6;

            // A fork reads the parent's state without drawing from it, so this
            // is the same whether or not the map supplies its own cover.
            var coverRng = rng.Fork("cover");
            var cover = opts.Cover != null
                ? opts.Cover.Select(c => c.Clone()).ToList()
                : CoverLayout(coverRng);
            for (int i = 0; i < cover.Count; i++) cover[i].Id = i;
            st.Cover.AddRange(cover);

            foreach (var side in Sides)
            {
                int placed = 0, lane = 0;
                while (placed < opts.OpeningStrength)
                {
                    double x = st.Front[(int)side] + Combat.Advance(side) * rng.Range(-6, 6);
                    var sq = SpawnSquad(st, side, lane % Tune.Lanes.Length, x, rng);
                    placed += Squads.Roster(st, sq.Id).Count;
                    lane++;
                    // Senses: the side that opens holding opens in its positions.
                    if (st.Senses && st.Fieldcraft && side == Side.Us && opts.Us.UseCover) Senses.Settle(st, sq);
                }
            }
            return st;
        }

        /// <summary>Original squad sizes, for the break test.</summary>
        public static Dictionary<int, int> OriginalStrengths(SimState st)
        {
            var m = new Dictionary<int, int>();
            foreach (var sq in st.Squads) m[sq.Id] = Squads.Roster(st, sq.Id).Count;
            return m;
        }

        // --- the tick ------------------------------------------------------------

        /// <summary>
        /// Pick a squad's order: the whole of a side's tactical policy. It takes
        /// the side's own plan because the 2D game ran one shared policy for
        /// two sides with different toolkits, and one side never used its own.
        /// </summary>
        private static Order DecideOrder(SimState st, Squad sq, Plan plan,
                                         Dictionary<int, int> original, Combat.LaneIndex ix)
        {
            var live = Squads.Roster(st, sq.Id);
            if (live.Count == 0) return sq.Order;

            int n0 = original.TryGetValue(sq.Id, out int v) ? v : live.Count;
            if (plan.Withdraw)
            {
                // Already pulling back: rally once steadied, rather than
                // retreating off the map for the rest of the match.
                if (sq.Order == Order.Fallback)
                {
                    double pin = 0;
                    for (int i = 0; i < live.Count; i++) pin += live[i].Pin;
                    if (pin / live.Count < Tune.RallyPin) return Order.Advance;
                    return Order.Fallback;
                }
                if ((double)live.Count / Math.Max(1, n0) < Tune.SquadBreak) return Order.Fallback;
            }

            if (plan.Bound)
            {
                // The tactical heart: do not cross swept ground unless someone
                // is covering you. This branch once read "in cover ? hold :
                // bound", which sent the squad caught in the open with nobody
                // covering it *across* — the opposite of the rule.
                bool swept = Combat.LaneSwept(ix, sq.Side, sq.Lane);
                bool covered = Combat.HasCoveringFire(ix, sq.Side, sq.Lane, sq.Id);
                if (swept && !covered) return Order.Hold;
                if (swept && covered) return Order.Bound;
            }

            // Held too long gets ranged in, so move on — even a defender has to
            // shift, which is what stops digging in being free.
            var c0 = Combat.CoverOf(st, live[0]);
            if (c0 != null && c0.RangedIn >= 0.85) return Order.Advance;
            if (!plan.Advance) return c0 != null ? Order.Hold : Order.Advance;
            return Order.Advance;
        }

        private static void MoveMan(SimState st, Man m, Squad sq, Slot slot, Plan plan)
        {
            double dir = Combat.Advance(m.Side);

            // Posture, with one dwell where every input funnels through
            // (§9 finding 3).
            m.Dwell++;
            // Fieldcraft: climbing a parapet, and the enemy he is going for with his hands.
            bool climbing = false;
            Man quarry = null;
            if (st.Fieldcraft)
            {
                if (m.Vault > 0) { m.Vault--; climbing = true; }
                quarry = Fieldcraft.Quarry(st, m, sq);
            }
            // Always the slot. Cover is reached by moving the anchor.
            double tx = slot.X, tz = slot.Z;
            // (Senses: a squad falling back to a strongpoint runs to its places in it, not twelve metres.)
            if (sq.Order == Order.Fallback && !(st.Senses && st.Fieldcraft && sq.Target >= 0)) tx = m.X - dir * 12;
            if (quarry != null)
            {
                // To within a rifle's length of him, and no further.
                double qx = quarry.X - m.X, qz = quarry.Z - m.Z, qd = JsMath.Hypot(qx, qz);
                double reach = Math.Max(0, qd - 1.2);
                tx = qd > 0 ? m.X + qx / qd * reach : m.X;
                tz = qd > 0 ? m.Z + qz / qd * reach : m.Z;
            }
            else if (st.Fieldcraft && sq.Order != Order.Fallback)
            {
                // His place is a few paces behind him: the file comes up to him.
                double behind = (m.X - tx) * dir;
                if (behind > 0 && behind < Tune.FileWait) tx = m.X;
            }

            var want = Posture.Standing;
            if (m.Pin >= Tune.PinStop) want = Posture.Prone;
            else if (m.Pin >= Tune.PinDrop) want = Posture.Crouched;
            else if (sq.Order == Order.Hold && Combat.CoverOf(st, m) != null) want = Posture.Crouched;
            // Arms: sappers come up bent double while they are unseen, which is how they get inside a
            // rifle's reach (a crouching man is found at 16 m, a walking one at 26).
            else if (st.Arms && sq.Reach <= Tune.Stalks && !m.Seen && quarry == null && sq.Order != Order.Fallback) want = Posture.Crouched;
            // Senses: what the squad knows decides how its men carry themselves. With an enemy it is
            // dealing with, nobody stands unless he is running: a squad on the move runs, a man whose
            // place is a few paces off runs to it, and a man at his place is on a knee behind cover,
            // flat in the open, and flat behind a machine gun or a scope either way. Out of contact, a
            // squad paused in cover takes a knee in it.
            else if (st.Senses && st.Fieldcraft && quarry == null && sq.Order != Order.Fallback && (Senses.InContact(sq) || sq.Task != SquadTask.March))
            {
                double away = JsMath.Hypot(tx - m.X, tz - m.Z);
                bool gun = st.Arms && (m.Weapon == Weapon.M60 || m.Weapon == Weapon.Rpd || m.Weapon == Weapon.Sniper);
                bool there = away <= (m.Posture == Posture.Standing ? Tune.SetOff : Tune.KneelWithin);
                // (A squad on the move whose lead man was pinned stood behind him, upright, fifteen
                // metres from the enemy: a man with nowhere to go this second is down, whatever his squad is doing.)
                if (!there) want = Posture.Standing;
                else if (sq.Halted && (gun || m.Cover < 0)) want = Posture.Prone;
                else want = Posture.Crouched;
            }
            else if (st.Senses && st.Fieldcraft && quarry == null && sq.Order != Order.Fallback && sq.Halted
                     && JsMath.Hypot(tx - m.X, tz - m.Z) <= Tune.KneelWithin) want = Posture.Crouched;
            // Fieldcraft: a squad that has halted in contact goes to ground: a knee for a rifleman, flat
            // behind his gun for a machine-gunner or a sniper. On the march, or out of contact, they stand:
            // kneeling at every pause, a man changed posture twenty times a minute.
            else if (st.Fieldcraft && quarry == null && sq.Order != Order.Fallback && sq.Halted && sq.Gap < Tune.Contact)
            {
                double away = JsMath.Hypot(tx - m.X, tz - m.Z);
                bool gun = st.Arms && (m.Weapon == Weapon.M60 || m.Weapon == Weapon.Rpd || m.Weapon == Weapon.Sniper);
                want = away > Tune.KneelWithin ? Posture.Standing : gun && away <= Tune.SetOff ? Posture.Prone : Posture.Crouched;
            }
            if (want != m.Posture && m.Dwell >= Tune.PostureDwell)
            {
                m.Posture = want;
                m.Dwell = 0;
            }

            // Pinned men drop, shoot less and stop advancing. He still has to be
            // placed in whatever he is lying behind, so only movement is skipped.
            bool pinnedDown = m.Pin >= Tune.PinDrop && sq.Order != Order.Fallback;
            if (!pinnedDown && !climbing)
            {
                double speed = Tune.Speed(m.Posture);
                double dx = tx - m.X, dz = tz - m.Z;
                double d = JsMath.Hypot(dx, dz);
                // With drill a man close enough to his place stays put (falling back, he always moves).
                // Fieldcraft: and once at rest he stays at rest until his place has moved a good pace off,
                // then goes all the way to it: no shuffling a step at a time after a slot that drifts.
                double slack = (st.Drill || st.Fieldcraft) && sq.Order != Order.Fallback && quarry == null ? Tune.DrillSlack : 0.05;
                if (st.Fieldcraft && sq.Order != Order.Fallback && quarry == null)
                {
                    slack = m.Still ? Tune.SetOff : Tune.Arrive;
                    m.Still = d <= slack;
                }
                else m.Still = false;
                if (d > slack)
                {
                    double stepLen = Math.Min(d, speed * Tune.Dt);
                    // (Senses: men running to their places in the strongpoint behind go round each other too.)
                    if (st.Fieldcraft && (sq.Order != Order.Fallback || (st.Senses && sq.Target >= 0)))
                    {
                        double nx = m.X + (dx / d) * stepLen, nz = m.Z + (dz / d) * stepLen;
                        bool blocked = Fieldcraft.Clear(st, m, ref nx, ref nz, stepLen);
                        // Senses: a friend is on the spot he is a pace from (his squad's lead man, pinned
                        // where the squad's next place is): he waits beside him. Sliding round him to a
                        // place he could not take, he swung about sixty times a minute.
                        if (blocked && st.Senses && quarry == null && d <= Tune.SetOff + 0.7) { nx = m.X; nz = m.Z; m.Still = true; }
                        m.X = nx; m.Z = nz;
                    }
                    else
                    {
                    m.X += (dx / d) * stepLen;
                    m.Z += (dz / d) * stepLen;
                    }
                }
                m.X = Math.Max(-Tune.HalfLength, Math.Min(Tune.HalfLength, m.X));
            }
            Squads.PushTrail(m);

            // Cover, for every man every tick, pinned or not. This once sat
            // after an early return taken by exactly the men being shot at, so
            // cover was never applied in the one moment it mattered: it cut
            // casualties by 3% where it should have cut them by about 25.
            int was = m.Cover;
            m.Cover = -1;
            if (plan.UseCover)
            {
                for (int i = 0; i < st.Cover.Count; i++)
                {
                    bool inside = st.Fieldcraft ? Fieldcraft.In(m.X, m.Z, st.Cover[i]) : Combat.InCover(m.X, m.Z, st.Cover[i]);
                    if (inside) { m.Cover = st.Cover[i].Id; break; }
                }
            }
            if (st.Fieldcraft && m.Cover != was)
            {
                // Into a trench or out of one is a climb.
                bool into = m.Cover >= 0 && Fieldcraft.Dug(st.Cover[m.Cover]);
                bool outOf = was >= 0 && Fieldcraft.Dug(st.Cover[was]);
                if (into || outOf)
                {
                    m.Vault = into ? Tune.VaultInTicks : Tune.VaultOutTicks;
                    st.Events.Add(new SimEvent
                    {
                        Kind = into ? EventKind.VaultIn : EventKind.VaultOut,
                        Tick = st.Tick, Side = m.Side, Id = m.Id, X = m.X, Z = m.Z,
                    });
                }
            }
            if (m.Cover != was)
            {
                st.Events.Add(new SimEvent
                {
                    Kind = m.Cover >= 0 ? EventKind.CoverTaken : EventKind.CoverLeft,
                    Tick = st.Tick, Side = m.Side, Id = m.Id,
                });
            }
        }

        [ThreadStatic] private static List<Slot> _slots;
        [ThreadStatic] private static List<Man> _shooters;

        public static void Step(SimState st, Plan us, Plan vc, Rng rng,
                                Dictionary<int, int> original, int cap = Tune.MaxTicks)
        {
            if (st.Over) return;
            st.Tick++;
            int eventsAtTickStart = st.Events.Count;
            var slots = _slots ??= new List<Slot>(Tune.SquadMax);
            var shooters = _shooters ??= new List<Man>(128);

            // --- the opening ---------------------------------------------------
            // The infiltration: the VC move up concealed, the Americans hold,
            // nobody fires. The match proper begins when one side finds the
            // other. `opening` is read once, so the rest of this tick still
            // behaves as the opening even if contact happens now.
            bool opening = st.Phase == Phase.Opening;
            // Part 2 (MatchOptions.Senses): who has whom in sight, before anyone acts on it.
            if (st.Senses) Senses.Look(st);
            if (opening)
            {
                bool spotted = false;
                for (int i = 0; i < st.Men.Count; i++)
                {
                    var m = st.Men[i];
                    if (m.Alive && m.Side == Side.Vc && m.Seen) { spotted = true; break; }
                }
                if (spotted || st.Tick >= Tune.OpeningMaxSeconds * Tune.TickHz)
                {
                    st.Phase = Phase.Fight;
                    st.ContactTick = st.Tick;
                    st.Events.Add(new SimEvent
                    {
                        Kind = EventKind.FirstContact, Tick = st.Tick,
                        Side = spotted ? Side.Us : Side.Vc, Id = -1,
                    });
                }
            }

            // --- squads: anchor, slots, orders ---------------------------------
            var ix = Combat.BuildLaneIndex(st);
            for (int si = 0; si < st.Squads.Count; si++)
            {
                var sq = st.Squads[si];
                var live = Squads.Roster(st, sq.Id);
                if (live.Count == 0) continue;
                var plan = sq.Side == Side.Us ? us : vc;
                var prev = sq.Order;
                // The player's order wins over the plan and over the opening
                // script. It does not survive the squad breaking — see OrderSquad.
                sq.Order = sq.PlayerOrder
                    ?? (opening
                        ? (sq.Side == Side.Us ? Order.Hold : Order.Advance)
                        : st.Senses ? sq.Order : DecideOrder(st, sq, plan, original, ix));
                // Part 2 (MatchOptions.Senses): the squad's own task, from what it has spotted, in place of the policy.
                if (st.Senses && !opening)
                    sq.Order = Senses.Decide(st, sq, live, plan, original.TryGetValue(sq.Id, out int was0) ? was0 : live.Count, prev);
                // Part 2 (MatchOptions.Drill): the policy's order, steadied.
                else if (st.Drill && sq.PlayerOrder == null && !opening)
                    sq.Order = Drill.Steady(st, sq, prev, sq.Order, live, original.TryGetValue(sq.Id, out int raised) ? raised : live.Count);
                if (sq.Order == Order.Fallback && prev != Order.Fallback)
                {
                    st.Events.Add(new SimEvent { Kind = EventKind.SquadBroke, Tick = st.Tick, Side = sq.Side, Id = sq.Id });
                }
                if (sq.Order == Order.Bound && prev != Order.Bound)
                {
                    sq.Bounding = true;
                    st.Events.Add(new SimEvent { Kind = EventKind.BoundStart, Tick = st.Tick, Side = sq.Side, Id = sq.Id });
                }

                if (st.Fieldcraft)
                {
                    // Part 2 (MatchOptions.Fieldcraft): the squad's cover, anchor and places, its own way.
                    Fieldcraft.Move(st, sq, live, plan, prev, slots);
                    for (int i = 0; i < live.Count; i++) MoveMan(st, live[i], sq, slots[i], plan);
                    continue;
                }

                bool needCover = false;
                if (plan.UseCover)
                {
                    var tc = sq.Target >= 0 && sq.Target < st.Cover.Count ? st.Cover[sq.Target] : null;
                    if (tc == null) needCover = true;
                    else
                    {
                        double dx = sq.AnchorX - tc.X, dz = sq.AnchorZ - tc.Z;
                        needCover = dx * dx + dz * dz < Tune.CoverRadius * Tune.CoverRadius;
                    }
                }
                if (needCover)
                {
                    sq.Target = Combat.BestCover(st, sq.AnchorX, sq.Lane, Combat.Advance(sq.Side), plan.Advance);
                }
                // Order, then march, then leash to the live roster (§9 finding 1).
                var coverTarget = plan.UseCover && sq.Target >= 0 ? st.Cover[sq.Target] : null;
                Squads.March(sq, coverTarget);
                Squads.Reanchor(sq, live);
                if (st.Drill) Drill.SlotsFor(st, sq, live, sq.AnchorX, sq.AnchorZ, slots);
                else Squads.SlotsFor(sq, live, sq.AnchorX, sq.AnchorZ, slots);
                for (int i = 0; i < live.Count; i++) MoveMan(st, live[i], sq, slots[i], plan);
            }

            // --- grenades (Part 2, MatchOptions.Frag) -------------------------------
            if ((st.Frag || st.Arms) && !opening) Frag.Tick(st);
            if (st.SquadSmoke && !opening) SquadSmoke.Tick(st);

            // --- fire ------------------------------------------------------------
            // The shooter list is taken once, before anyone fires, as the
            // original does: a man killed earlier in this loop still has his
            // cooldown ticked (harmlessly) and his Fire returns at once.
            shooters.Clear();
            for (int i = 0; i < st.Men.Count; i++) if (st.Men[i].Alive) shooters.Add(st.Men[i]);
            for (int i = 0; i < shooters.Count; i++)
            {
                var m = shooters[i];
                if (m.Cooldown > 0) m.Cooldown--;
                // Nobody fires during the infiltration. That is the whole beat.
                if (!opening) Combat.Fire(st, m, rng);
            }

            // --- suppression, veterancy, concealment -----------------------------
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                if (!m.Alive) continue;
                Combat.Relax(st, m);
                // Steadier, not stronger, and only earned under fire.
                if (m.Pin > 0.05) m.Veterancy = Math.Min(Tune.VetCap, m.Veterancy + Tune.VetPerSecond * Tune.Dt);
                // Found at close range. Staggered across four ticks by id: this
                // is a scan over every enemy for every unseen man, 27% of the
                // sim unstaggered, and a fifth of a second late is not
                // observable.
                if (!st.Senses && !m.Seen && (st.Tick + m.Id) % 4 == 0)
                {
                    // Concealment is a state, not a side: flat and still is hard
                    // to find, up and walking is not.
                    double reach = Tune.SpotRange(m.Posture) + (Squads.IsMoving(m) ? Tune.SpotMovingBonus : 0);
                    double r2 = reach * reach;
                    for (int j = 0; j < st.Men.Count; j++)
                    {
                        var e = st.Men[j];
                        if (!e.Alive || e.Side == m.Side) continue;
                        double dx = e.X - m.X, dz = e.Z - m.Z;
                        if (dx * dx + dz * dz < r2) { m.Seen = true; break; }
                    }
                }
            }

            Combat.UpdateRangedIn(st);
            if (st.Fieldcraft) Fieldcraft.Positions(st);

            // --- morale, ground and command points -------------------------------
            foreach (var side in Sides)
            {
                double dir = Combat.Advance(side);
                double lead = double.NegativeInfinity;
                for (int i = 0; i < st.Men.Count; i++)
                {
                    var m = st.Men[i];
                    if (m.Alive && m.Side == side) lead = Math.Max(lead, m.X * dir);
                }
                if (lead > double.NegativeInfinity) st.Front[(int)side] = lead * dir;
                st.Cp[(int)side] += st.CpRate * Tune.Dt;
            }
            // Ground conceded costs morale: how far the enemy's lead man has come
            // past the midline into my half. Comparing the two fronts directly
            // read 144 m of deficit for both sides on tick 0 and drained both
            // to zero before anyone was in range. None is conceded during the
            // infiltration — charging the Americans for it handed the VC five
            // seeds of six.
            foreach (var side in Sides)
            {
                if (st.Phase == Phase.Opening) break;
                var enemy = Combat.Other(side);
                double penetration = Math.Max(0, st.Front[(int)enemy] * Combat.Advance(enemy));
                if (penetration > 0)
                {
                    double loss = penetration * Tune.MoralePerGround * Tune.Dt * st.MoraleScale;
                    st.Morale[(int)side] = Math.Max(0, st.Morale[(int)side] - loss);
                    st.MoraleLostToGround[(int)side] += loss;
                }
            }

            StepAreas(st, rng);

            // Casualties this tick drain morale — from this tick's events only.
            // Scanning the whole history every tick put a long match at 2.7 s.
            for (int i = eventsAtTickStart; i < st.Events.Count; i++)
            {
                var e = st.Events[i];
                if (e.Kind != EventKind.Kill) continue;
                double hit = Tune.MoralePerCasualty * st.MoraleScale;
                st.Morale[(int)e.Side] = Math.Max(0, st.Morale[(int)e.Side] - hit);
                st.MoraleLostToCasualties[(int)e.Side] += hit;
                st.Events.Add(new SimEvent
                {
                    Kind = EventKind.MoraleLost, Tick = st.Tick, Side = e.Side, Id = e.Id, Amount = hit,
                });
            }

            // --- reinforcement ----------------------------------------------------
            if (st.Tick % Tune.ReinforceEvery == 0)
            {
                foreach (var side in Sides)
                {
                    var plan = side == Side.Us ? us : vc;
                    if (!plan.Reinforce || st.Player == side || st.Cp[(int)side] < st.MusterCost) continue;
                    if (AliveCount(st, side) >= Tune.ForceCap) continue;
                    st.Cp[(int)side] -= st.MusterCost;
                    int lane = rng.Int(0, Tune.Lanes.Length);
                    double x = Combat.Advance(side) == 1 ? -Tune.HalfLength * 0.92 : Tune.HalfLength * 0.92;
                    SpawnSquad(st, side, lane, x, rng);
                    foreach (var sq in st.Squads)
                    {
                        if (!original.ContainsKey(sq.Id)) original[sq.Id] = Squads.Roster(st, sq.Id).Count;
                    }
                }
            }

            Deck.TickCooldowns(st);

            CheckOver(st, cap);
            // After, not before: CheckOver is what sets Over, so testing first
            // leaves the phase a tick behind — and on the last tick there is no
            // next one to catch up in.
            if (st.Over) st.Phase = Phase.Over;
        }

        public static int AliveCount(SimState st, Side side)
        {
            int n = 0;
            for (int i = 0; i < st.Men.Count; i++) if (st.Men[i].Alive && st.Men[i].Side == side) n++;
            return n;
        }

        /// <summary>
        /// Every way a match can end, in one place, each naming itself. The
        /// audit asserts each is reachable.
        /// </summary>
        private static void CheckOver(SimState st, int cap)
        {
            foreach (var side in Sides)
            {
                if (st.Morale[(int)side] <= 0)
                {
                    End(st, Combat.Other(side), $"{Name(side)} morale broke");
                    return;
                }
                if (AliveCount(st, side) == 0)
                {
                    End(st, Combat.Other(side), $"{Name(side)} wiped out");
                    return;
                }
            }
            if (st.Tick >= cap)
            {
                // A draw is decided on morale, so the timeout is not a coin flip.
                double d = st.Morale[(int)Side.Us] - st.Morale[(int)Side.Vc];
                Side? w = Math.Abs(d) < 0.02 ? (Side?)null : (d > 0 ? Side.Us : Side.Vc);
                st.Over = true;
                st.Winner = w;
                st.Reason = w != null ? "time, on morale" : "time, drawn";
                st.Events.Add(new SimEvent { Kind = EventKind.MatchOver, Tick = st.Tick, Side = w ?? Side.Us, Id = -1 });
            }
        }

        private static void End(SimState st, Side winner, string reason)
        {
            st.Over = true;
            st.Winner = winner;
            st.Reason = reason;
            st.Events.Add(new SimEvent { Kind = EventKind.MatchOver, Tick = st.Tick, Side = winner, Id = -1 });
        }

        // --- whole matches ------------------------------------------------------

        /// <summary>Run a whole match headless and report it. Pure in (seed, plans).</summary>
        public static MatchResult Run(MatchOptions opts)
        {
            var st = Create(opts);
            var rng = new Rng(opts.Seed).Fork("sim");
            var original = OriginalStrengths(st);
            int cap = opts.MaxTicks ?? Tune.MaxTicks;
            while (!st.Over && st.Tick < cap) Step(st, opts.Us, opts.Vc, rng, original, cap);
            if (!st.Over) { st.Over = true; st.Reason = "tick cap"; }
            return Summarise(st);
        }

        public static MatchResult Summarise(SimState st)
        {
            var counts = new Dictionary<EventKind, int>();
            foreach (var e in st.Events) counts[e.Kind] = counts.TryGetValue(e.Kind, out int c) ? c + 1 : 1;
            int grenadeKills = 0;
            for (int i = 0; i < st.Events.Count; i++)
            {
                if (st.Events[i].Kind != EventKind.GrenadeBlast) continue;
                for (int j = i + 1; j < st.Events.Count && st.Events[j].Kind == EventKind.Kill && st.Events[j].Tick == st.Events[i].Tick; j++)
                    grenadeKills++;
            }
            var cas = new int[2];
            foreach (var m in st.Men) if (!m.Alive) cas[(int)m.Side]++;
            return new MatchResult
            {
                Winner = st.Winner, Reason = st.Reason, Ticks = st.Tick,
                Seconds = (double)st.Tick / Tune.TickHz,
                Morale = (double[])st.Morale.Clone(),
                Casualties = cas,
                MoraleLostToCasualties = (double[])st.MoraleLostToCasualties.Clone(),
                MoraleLostToGround = (double[])st.MoraleLostToGround.Clone(),
                EventCounts = counts,
                GrenadeKills = grenadeKills,
            };
        }

        /// <summary>
        /// Give a squad an order, as the player. Touches no RNG, so orders
        /// cannot shift the simulation's stream. Null hands the squad back to
        /// its plan.
        /// </summary>
        public static bool OrderSquad(SimState st, int squadId, Order? order)
        {
            if (squadId < 0 || squadId >= st.Squads.Count) return false;
            var sq = st.Squads[squadId];
            // A broken squad is not taking orders. Letting the player override
            // a fallback would make morale a suggestion, and §8 makes it the clock.
            if (sq.Order == Order.Fallback && order != Order.Fallback && sq.PlayerOrder == null) return false;
            sq.PlayerOrder = order;
            return true;
        }

        /// <summary>
        /// Cover laid out from one forked stream, as the original generated it.
        ///
        /// The near lane is the firebase's own ground — dug in and built up, so
        /// trenches and sandbags dominate. The far lane is the track and the
        /// treeline, where the only cover is what the fighting left: craters
        /// and banks. That difference is the map's argument for why the two
        /// lanes play differently.
        /// </summary>
        public static List<Cover> CoverLayout(Rng rng)
        {
            var outv = new List<Cover>();
            int coverId = 0;
            for (int lane = 0; lane < Tune.Lanes.Length; lane++)
            {
                int n = rng.Int(7, 11);
                for (int i = 0; i < n; i++)
                {
                    bool near = lane == 0;
                    double roll = rng.Next();
                    CoverKind kind = near
                        ? (roll < 0.34 ? CoverKind.Trench : roll < 0.62 ? CoverKind.Sandbag
                            : roll < 0.74 ? CoverKind.Bunker : roll < 0.88 ? CoverKind.Berm : CoverKind.Crater)
                        : (roll < 0.40 ? CoverKind.Crater : roll < 0.74 ? CoverKind.Berm
                            : roll < 0.90 ? CoverKind.Trench : CoverKind.Sandbag);
                    var spec = CoverSpec.For(kind);
                    // A bunker is a position, not a line: short whatever the roll.
                    double length = kind == CoverKind.Bunker
                        ? rng.Range(3.0, 4.5)
                        : rng.Range(Tune.CoverLengthMin, Tune.CoverLengthMax);
                    // Draw order as the original's literal: x, z, then quality.
                    double x = rng.Range(-Tune.HalfLength * 0.85, Tune.HalfLength * 0.85);
                    double z = Tune.Lanes[lane] + rng.Range(-2.5, 2.5);
                    double quality = rng.Range(spec.qLo, spec.qHi);
                    outv.Add(new Cover
                    {
                        Id = coverId++, Kind = kind, X = x, Z = z, Length = length,
                        Capacity = Math.Max(2, (int)JsMath.Round(length / spec.metresPerMan)),
                        Quality = quality, RangedIn = 0, HeldBy = null,
                    });
                }
            }
            // Stable, as JavaScript's sort is.
            outv = outv.OrderBy(c => c.X).ToList();
            for (int i = 0; i < outv.Count; i++) outv[i].Id = i;
            return outv;
        }

        /// <summary>
        /// Advance every called-in effect by one tick. After movement and fire,
        /// so a barrage lands on where men are now and a squad that walked
        /// into a tripwire this tick springs it this tick.
        /// </summary>
        private static void StepAreas(SimState st, Rng rng)
        {
            for (int i = st.Areas.Count - 1; i >= 0; i--)
            {
                var a = st.Areas[i];
                a.Ticks--;

                if (a.Kind == AreaKind.Barrage)
                {
                    a.Next--;
                    if (a.Next <= 0)
                    {
                        a.Next = Tune.SalvoEvery;
                        // Several rounds walking across the target, not one big
                        // one on the aim point — that is what makes it an area.
                        int rounds = 2 + (int)Math.Floor(rng.Next() * 2);
                        for (int k = 0; k < rounds; k++)
                        {
                            double ang = rng.Next() * Math.PI * 2;
                            double r = a.Radius * Math.Sqrt(rng.Next());
                            double bx = a.X + JsMath.Cos(ang) * r;
                            double bz = a.Z + JsMath.Sin(ang) * r;
                            st.Events.Add(new SimEvent
                            {
                                Kind = EventKind.Shell, Tick = st.Tick, Side = a.Side, Id = a.Id, X = bx, Z = bz,
                            });
                            for (int j = 0; j < st.Men.Count; j++)
                            {
                                var m = st.Men[j];
                                if (!m.Alive) continue;
                                double d = JsMath.Hypot(m.X - bx, m.Z - bz);
                                if (d > Tune.ShellRadius) continue;
                                double f = 1 - d / Tune.ShellRadius;
                                // Cover is most of what artillery is answered with.
                                bool inCover = m.Cover >= 0;
                                Combat.ApplyPin(st, m, Tune.ShellPin * f * (inCover ? 0.55 : 1));
                                double lethal = a.Power * f * f * (inCover ? 0.22 : 1);
                                if (rng.Next() < lethal) Combat.Kill(st, m);
                            }
                        }
                    }
                }

                if (a.Kind == AreaKind.Trap)
                {
                    // Springs on the first enemy inside it, then it is gone.
                    Man hit = null;
                    for (int j = 0; j < st.Men.Count; j++)
                    {
                        var m = st.Men[j];
                        if (!m.Alive || m.Side == a.Side) continue;
                        if (JsMath.Hypot(m.X - a.X, m.Z - a.Z) <= a.Radius) { hit = m; break; }
                    }
                    if (hit != null)
                    {
                        st.Events.Add(new SimEvent
                        {
                            Kind = EventKind.TrapSprung, Tick = st.Tick, Side = a.Side, Id = a.Id, X = a.X, Z = a.Z,
                        });
                        for (int j = 0; j < st.Men.Count; j++)
                        {
                            var m = st.Men[j];
                            if (!m.Alive || m.Side == a.Side) continue;
                            double d = JsMath.Hypot(m.X - a.X, m.Z - a.Z);
                            if (d > a.Radius * 2.2) continue;
                            // A trap's real weapon is the pin: everyone near it
                            // goes flat and stops, which is what an ambush is for.
                            Combat.ApplyPin(st, m, Tune.TrapPin * (1 - d / (a.Radius * 2.2)));
                        }
                        if (rng.Next() < a.Power) Combat.Kill(st, hit);
                        a.Ticks = 0;
                    }
                }

                if (a.Ticks <= 0)
                {
                    st.Events.Add(new SimEvent
                    {
                        Kind = EventKind.AreaEnd, Tick = st.Tick, Side = a.Side, Id = a.Id, X = a.X, Z = a.Z,
                    });
                    st.Areas.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Who holds each lane, and by how much: whose front is further up it.
        /// §7's objective diamonds are coloured by this.
        /// </summary>
        public static (Side? side, double margin)[] LaneControl(SimState st)
        {
            var result = new (Side?, double)[Tune.Lanes.Length];
            for (int lane = 0; lane < Tune.Lanes.Length; lane++)
            {
                double fu = double.NegativeInfinity, fv = double.NegativeInfinity;
                for (int i = 0; i < st.Men.Count; i++)
                {
                    var m = st.Men[i];
                    if (!m.Alive) continue;
                    if (st.Squads[m.Squad].Lane != lane) continue;
                    double f = m.X * Combat.Advance(m.Side);
                    if (m.Side == Side.Us) fu = Math.Max(fu, f); else fv = Math.Max(fv, f);
                }
                if (double.IsNegativeInfinity(fu) && double.IsNegativeInfinity(fv))
                {
                    result[lane] = (null, 0);
                    continue;
                }
                double d = fu - fv;
                double margin = Math.Min(1, Math.Abs(d) / Tune.HalfLength);
                result[lane] = (Math.Abs(d) < 3 ? (Side?)null : (d > 0 ? Side.Us : Side.Vc), margin);
            }
            return result;
        }
    }
}
