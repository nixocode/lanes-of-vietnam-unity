using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LanesOfVietnam.Sim;
using NUnit.Framework;
using Match = LanesOfVietnam.Sim.Match;

namespace LanesOfVietnam.Tests
{
    /// <summary>
    /// The simulation's non-negotiables, as assertions rather than comments.
    ///
    /// Each test is a finding from the brief's §9 or a rule from PLAN §4 that
    /// cost a previous build real time. PLAN §10 step 1's gate is this suite
    /// passing headless:
    ///
    /// <code>tools/unity.sh -nographics -runTests -testPlatform EditMode -testResults Logs/tests.xml</code>
    /// </summary>
    public class SimTests
    {
        private static string SimDir =>
            Path.Combine(Directory.GetCurrentDirectory(), "Assets/_Project/Scripts/Sim");

        // --- PLAN §4: the simulation is not the renderer ----------------------------

        [Test]
        public void Sim_assembly_references_nothing_from_the_engine()
        {
            var refs = typeof(Match).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
            Assert.That(refs.Where(n => n.StartsWith("UnityEngine") || n.StartsWith("UnityEditor")),
                        Is.Empty, "Sim references: " + string.Join(", ", refs));
            StringAssert.Contains("\"noEngineReferences\": true",
                File.ReadAllText(Path.Combine(SimDir, "LanesOfVietnam.Sim.asmdef")));
        }

        [Test]
        public void Sim_reads_no_clock_and_no_global_randomness()
        {
            // A match is a pure function of (seed, plans, commands). Any of
            // these makes it a function of the machine it ran on.
            var banned = new Regex(@"\b(System\.Random|new Random\(|DateTime\.(Now|UtcNow)|Environment\.TickCount|Stopwatch|Guid\.NewGuid)\b");
            foreach (var f in Directory.GetFiles(SimDir, "*.cs"))
            {
                foreach (var (line, i) in File.ReadAllLines(f).Select((l, i) => (l, i + 1)))
                {
                    if (line.TrimStart().StartsWith("//") || line.TrimStart().StartsWith("///")) continue;
                    Assert.IsFalse(banned.IsMatch(line), $"{Path.GetFileName(f)}:{i}: {line.Trim()}");
                }
            }
        }

        [Test]
        public void Match_code_calls_no_platform_transcendental()
        {
            // Math.Sin and friends may differ in the last bit between the
            // Editor's libm and the browser's; a different bit is a different
            // match. The match path uses JsMath's fdlibm port instead.
            var banned = new Regex(@"\bMath\.(Sin|Cos|Tan|Asin|Acos|Atan|Atan2|Exp|Log|Log10|Pow|Cbrt|Sinh|Cosh|Tanh)\(");
            foreach (var name in new[] { "Match.cs", "Combat.cs", "Squads.cs", "Deck.cs", "LiveMatch.cs", "Frag.cs", "SquadSmoke.cs", "Drill.cs", "Fieldcraft.cs", "Arms.cs" })
            {
                foreach (var (line, i) in File.ReadAllLines(Path.Combine(SimDir, name)).Select((l, i) => (l, i + 1)))
                {
                    if (line.TrimStart().StartsWith("//")) continue;
                    Assert.IsFalse(banned.IsMatch(line), $"{name}:{i}: {line.Trim()}");
                }
            }
        }

        // --- parity with the TypeScript original --------------------------------------

        /// <summary>
        /// Hashes recorded from the original by tools/parity/trace.ts on
        /// 2026-09-29. If one of these moves, the simulation has changed —
        /// which may be intended, and then the balance numbers carried over
        /// from the three.js build no longer apply and must be re-measured.
        /// </summary>
        [TestCase(1, "ceiling", "ceiling", false, 4390, 3004074049u, 1637499518u, 2884798389u, "us morale broke")]
        [TestCase(1, "floor", "ceiling", false, 1246, 1213854579u, 2552243790u, 1220142971u, "us wiped out")]
        [TestCase(1, "defend", "defend", false, 14400, 61802080u, 3645062854u, 1847938924u, "time, on morale")]
        [TestCase(1, "cut-off", "ceiling", false, 2378, 3004074049u, 2506288653u, 3871280973u, "us wiped out")]
        [TestCase(3, "cut-off", "cut-off", true, 2492, 241283149u, 3723355318u, 736090046u, "vc morale broke")]
        [TestCase(8, "cut-off", "cut-off", true, 2341, 1666148291u, 960282555u, 2729295840u, "us morale broke")]
        public void Matches_the_TypeScript_original_bit_for_bit(int seed, string us, string vc, bool scripted,
            int ticks, uint at100, uint at1000, uint final, string reason)
        {
            var h = Parity.Trace(seed, Plan.ByName(us), Plan.ByName(vc), scripted, out var lm);
            Assert.AreEqual(at100, h[100], "tick 100");
            Assert.AreEqual(at1000, h[1000], "tick 1000");
            Assert.AreEqual(ticks, lm.State.Tick);
            Assert.AreEqual(final, h[h.Count - 1], "final tick");
            Assert.AreEqual(reason, lm.State.Reason);
        }

        [Test]
        public void JsMath_round_is_JavaScript_round()
        {
            Assert.AreEqual(3, JsMath.Round(2.5));
            Assert.AreEqual(-2, JsMath.Round(-2.5));
            Assert.AreEqual(0, JsMath.Round(0.49999999999999994));
            Assert.AreEqual(2, JsMath.Round(1.5));
        }

        [Test]
        public void JsMath_trig_is_sane_everywhere_it_is_used()
        {
            // Bit-parity with V8 is checked by tools/simcs; this is the floor:
            // the port is sine and cosine, to 1e-15, across [0, 2pi).
            var r = new Rng(5);
            for (int i = 0; i < 10000; i++)
            {
                double t = r.Next() * Math.PI * 2;
                Assert.AreEqual(Math.Sin(t), JsMath.Sin(t), 1e-15);
                Assert.AreEqual(Math.Cos(t), JsMath.Cos(t), 1e-15);
            }
        }

        // --- determinism (§10) -----------------------------------------------------------

        [Test]
        public void A_match_is_a_pure_function_of_seed_and_plans()
        {
            foreach (int seed in new[] { 1, 13, 777 })
            {
                var a = Parity.Trace(seed, Plan.Ceiling, Plan.Ceiling, false, out _);
                var b = Parity.Trace(seed, Plan.Ceiling, Plan.Ceiling, false, out _);
                CollectionAssert.AreEqual(a, b, $"seed {seed}");
            }
        }

        [Test]
        public void Different_seeds_give_different_matches()
        {
            var a = Parity.Trace(1, Plan.Ceiling, Plan.Ceiling, false, out _);
            var b = Parity.Trace(2, Plan.Ceiling, Plan.Ceiling, false, out _);
            CollectionAssert.AreNotEqual(a, b);
        }

        [Test]
        public void A_command_log_replays_the_match_exactly()
        {
            Parity.Trace(42, Plan.NoReinforce, Plan.NoReinforce, true, out var played);
            Assert.That(played.Log.Count(l => l.Accepted), Is.GreaterThanOrEqualTo(8));
            var replayed = LiveMatch.Replay(played.Options, played.Log);
            Assert.AreEqual(Parity.Hash(played.State, 0), Parity.Hash(replayed.State, 0));
        }

        [Test]
        public void A_full_match_runs_well_under_a_second()
        {
            Match.Run(new MatchOptions { Seed = 5 });
            var sw = Stopwatch.StartNew();
            Match.Run(new MatchOptions { Seed = 5 });
            Assert.Less(sw.ElapsedMilliseconds, 1000);
        }

        // --- §9 finding 9: seeds are independent ------------------------------------------

        [Test]
        public void Adjacent_seeds_are_independent()
        {
            foreach (int block in new[] { 0, 1000, 2000, 50000, 4000000 })
            {
                double c = Rng.AdjacentSeedCorrelation(block, 4000);
                Assert.Less(Math.Abs(c), 0.05, $"adjacent-seed correlation at block {block}");
            }
            double tenth = Rng.AdjacentSeedCorrelation(1000, 4000, r =>
            {
                for (int i = 0; i < 9; i++) r.Next();
                return r.Next();
            });
            Assert.Less(Math.Abs(tenth), 0.05, "tenth draw");
        }

        [Test]
        public void Rng_is_reproducible_bounded_and_unbiased()
        {
            var a = new Rng(12345);
            var b = new Rng(12345);
            for (int i = 0; i < 64; i++) Assert.AreEqual(a.Next(), b.Next());
            var r = new Rng(99);
            double sum = 0;
            for (int i = 0; i < 200000; i++)
            {
                double v = r.Next();
                Assert.That(v, Is.GreaterThanOrEqualTo(0).And.LessThan(1));
                sum += v;
            }
            Assert.Less(Math.Abs(sum / 200000 - 0.5), 0.005);
        }

        [Test]
        public void Forks_are_independent_and_reproducible()
        {
            var x = new Rng(42).Fork("render");
            var y = new Rng(42).Fork("audio");
            Assert.AreNotEqual(x.Next(), y.Next());
            Assert.AreEqual(new Rng(5).Fork("render").Next(), new Rng(5).Fork("render").Next());
        }

        // --- §9 finding 1: slots come from the live roster -----------------------------------

        private static (Squad sq, List<Man> men) MakeSquad(int n)
        {
            var sq = new Squad { Id = 0, Side = Side.Us, Order = Order.Advance, Target = -1 };
            var men = new List<Man>();
            for (int i = 0; i < n; i++)
            {
                men.Add(new Man { Id = i, Squad = 0, Side = Side.Us, X = -i * Tune.SlotGap, Seen = true });
            }
            return (sq, men);
        }

        [Test]
        public void The_anchor_stays_inside_the_squad_however_many_die()
        {
            // The 2D game's survivors chased a point that ran away from them —
            // traced to x = 12091 in a 2560-wide world.
            var (sq, men) = MakeSquad(6);
            for (int kill = 0; kill < 5; kill++)
            {
                men[kill].Alive = false;
                var live = men.Where(m => m.Alive).ToList();
                for (int t = 0; t < 200; t++) { Squads.March(sq, null); Squads.Reanchor(sq, live); }
                double lead = live.Max(m => m.X);
                Assert.LessOrEqual(sq.AnchorX - lead, Tune.AnchorLeash + 1e-9);
            }
        }

        [Test]
        public void A_lone_survivor_gets_a_slot_on_himself()
        {
            var (sq, men) = MakeSquad(5);
            for (int i = 0; i < 4; i++) men[i].Alive = false;
            var live = men.Where(m => m.Alive).ToList();
            Squads.Reanchor(sq, live);
            var slots = new List<Slot>();
            Squads.SlotsFor(sq, live, sq.AnchorX, sq.AnchorZ, slots);
            Assert.AreEqual(1, slots.Count);
            Assert.LessOrEqual(Math.Abs(slots[0].X - live[0].X), Tune.AnchorLeash + 1e-9);
        }

        [Test]
        public void Slot_count_comes_from_the_living_never_the_spawn_size()
        {
            var (sq, men) = MakeSquad(6);
            men[2].Alive = false; men[4].Alive = false;
            var slots = new List<Slot>();
            Squads.SlotsFor(sq, men.Where(m => m.Alive).ToList(), 0, 0, slots);
            Assert.AreEqual(4, slots.Count);
        }

        // --- §9 finding 2: moving means measured progress ------------------------------------

        [Test]
        public void Jitter_is_not_movement()
        {
            var m = new Man();
            for (int i = 0; i < Tune.MoveWindow * 3; i++)
            {
                m.X += (i % 2 == 0 ? 1 : -1) * 0.001;
                Squads.PushTrail(m);
            }
            Assert.IsFalse(Squads.IsMoving(m));
        }

        [Test]
        public void Walking_is_movement_once_the_window_fills()
        {
            var m = new Man();
            for (int i = 0; i < Tune.MoveWindow - 1; i++) { m.X += 1; Squads.PushTrail(m); }
            Assert.IsFalse(Squads.IsMoving(m), "before the window has filled");
            m.X += 1; Squads.PushTrail(m);
            Assert.IsTrue(Squads.IsMoving(m));
        }

        // --- §8: the match has a shape ----------------------------------------------------------

        [Test]
        public void Matches_produce_casualties_and_end_with_a_reason()
        {
            foreach (var (us, vc) in new[] { (Plan.Ceiling, Plan.Ceiling), (Plan.Floor, Plan.Floor), (Plan.Ceiling, Plan.Floor) })
            {
                var r = Match.Run(new MatchOptions { Seed = 3, Us = us, Vc = vc });
                // Zero casualties is the frozen-anchor bug: twelve seeds ran to
                // the cap and nobody died.
                Assert.Greater(r.Casualties[0] + r.Casualties[1], 0, $"{us.Name} vs {vc.Name}");
                Assert.IsNotEmpty(r.Reason);
            }
        }

        [Test]
        public void Suppression_is_the_mechanic_kills_are_rare()
        {
            var r = Match.Run(new MatchOptions { Seed = 9 });
            int shots = r.EventCounts[EventKind.Fire];
            int kills = r.EventCounts[EventKind.Kill];
            Assert.Greater(shots, 0);
            Assert.Less((double)kills / shots, 0.25);
            Assert.Greater(r.EventCounts[EventKind.Pinned], 0);
        }

        [Test]
        public void Nobody_leaves_the_map()
        {
            var lm = new LiveMatch(new MatchOptions { Seed = 4 });
            for (int i = 0; i < 2000 && !lm.State.Over; i++) lm.Step();
            foreach (var m in lm.State.Men) Assert.LessOrEqual(Math.Abs(m.X), Tune.HalfLength + 1e-9);
        }

        [Test]
        public void The_drawn_time_ending_is_reachable()
        {
            // Rare in play — both morales within 0.02 at the twelve-minute cap —
            // so the audit may never meet it. Rare is fine; dead is the bug, and
            // this proves the branch is live.
            var st = Match.Create(new MatchOptions { Seed = 1, Us = Plan.Defend, Vc = Plan.Defend });
            var original = Match.OriginalStrengths(st);
            st.Phase = Phase.Fight;
            st.Morale[0] = st.Morale[1] = 0.7;
            Match.Step(st, Plan.Defend, Plan.Defend, new Rng(1).Fork("sim"), original, cap: 1);
            Assert.IsTrue(st.Over);
            Assert.AreEqual("time, drawn", st.Reason);
            Assert.IsNull(st.Winner);
        }

        // --- the deck: every card does something ----------------------------------------------

        [Test]
        public void Every_card_changes_the_simulation()
        {
            // The three.js build shipped eight cards that took the points and did
            // nothing. Each card here must leave a mark the simulation can see.
            foreach (var side in Match.Sides)
            {
                foreach (var card in Deck.For(side))
                {
                    // (GRENADES only with its rule, and played a few paces in front of the side's own men.)
                    bool grenades = card == Deck.Grenades(side);
                    var st = Match.Create(new MatchOptions { Seed = 7, Frag = grenades, FragCard = grenades });
                    st.Cp[(int)side] = 999;
                    foreach (var m in st.Men) m.Pin = 0.6;
                    st.Morale[(int)side] = 0.5;
                    int men = st.Men.Count, areas = st.Areas.Count, thrown = st.Grenades.Count;
                    double morale = st.Morale[(int)side];
                    double pin = st.Men.Where(m => m.Side == side).Sum(m => m.Pin);
                    int lane = 1;
                    double x = 5;
                    if (grenades)
                    {
                        var by = st.Men.First(m => m.Alive && m.Side == side && m.Grenades > 0);
                        lane = System.Math.Abs(by.Z - Tune.Lanes[0]) < System.Math.Abs(by.Z - Tune.Lanes[1]) ? 0 : 1;
                        x = by.X + Combat.Advance(side) * 6;
                    }

                    Assert.IsTrue(Deck.Buy(st, side, card, lane, new Rng(7).Fork("sim"), x), card.Id);
                    bool changed = st.Men.Count != men || st.Areas.Count != areas || st.Grenades.Count != thrown
                                   || st.Morale[(int)side] != morale
                                   || st.Men.Where(m => m.Side == side).Sum(m => m.Pin) != pin;
                    Assert.IsTrue(changed, $"{card.Id} took {card.Cost} CP and changed nothing");
                    Assert.AreEqual(999 - card.Cost, st.Cp[(int)side], 1e-9, card.Id);
                    Assert.Greater(Deck.CooldownLeft(st, side, card), 0, card.Id);
                    Assert.IsFalse(Deck.Buy(st, side, card, lane, new Rng(7).Fork("sim"), x), $"{card.Id} bought twice inside its cooldown");
                }
            }
        }

        [Test]
        public void Card_cooldowns_run_on_simulation_ticks()
        {
            var lm = new LiveMatch(new MatchOptions { Seed = 2, Us = Plan.NoReinforce, Vc = Plan.NoReinforce });
            lm.State.Cp[0] = 500;
            var smoke = Deck.Find(Side.Us, "us-smoke");
            lm.Issue(Command.Buy(Side.Us, "us-smoke", 0, 0));
            lm.Step();
            Assert.AreEqual(smoke.Cooldown - 1, Deck.CooldownLeft(lm.State, Side.Us, smoke));
            for (int i = 0; i < smoke.Cooldown - 1; i++) lm.Step();
            Assert.AreEqual(0, Deck.CooldownLeft(lm.State, Side.Us, smoke));
        }

        // --- Part 2: grenades, behind MatchOptions.Frag (PLAN §12.8) ----------------------

        private static (List<uint> hashes, LiveMatch m) FragTrace(int seed, bool frag, bool smoke = false)
        {
            var m = new LiveMatch(new MatchOptions { Seed = seed, Us = Plan.Ceiling, Vc = Plan.Ceiling, Frag = frag, SquadSmoke = smoke });
            var h = new List<uint> { Parity.Hash(m.State, 0) };
            while (!m.State.Over && m.State.Tick < m.Cap) { m.Step(); h.Add(Parity.Hash(m.State, 0)); }
            return (h, m);
        }

        [Test]
        public void Grenades_are_off_by_default_and_the_baseline_draws_nothing_for_them()
        {
            var st = Match.Create(new MatchOptions { Seed = 1 });
            Assert.IsFalse(st.Frag);
            Assert.IsNull(st.FragRng, "the baseline created a grenade stream");
            var (_, m) = FragTrace(1, false);
            Assert.IsFalse(m.State.Events.Any(e => e.Kind == EventKind.GrenadeThrown || e.Kind == EventKind.GrenadeBlast));
            // The baseline's own ending, as the parity cases pin it.
            Assert.AreEqual(4390, m.State.Tick);
            Assert.AreEqual("us morale broke", m.State.Reason);
        }

        /// <summary>
        /// The C# sim is the source of truth for Part 2 (§12.8 item 3): these
        /// pin the rule's own matches. Recorded by `tools/simcs/run.sh hash N frag`.
        /// </summary>
        [TestCase(1, 2938, 4053502112u, 2462005751u, 614838045u, "vc morale broke")]
        [TestCase(5, 4307, 406757276u, 1608758779u, 3366859818u, "us morale broke")]
        public void With_grenades_a_match_is_pinned_and_still_a_pure_function_of_its_seed(
            int seed, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            var (a, m) = FragTrace(seed, true);
            var (b, _) = FragTrace(seed, true);
            CollectionAssert.AreEqual(a, b, "two runs of the same seed diverged");
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(at100, a[100], "tick 100");
            Assert.AreEqual(at1000, a[1000], "tick 1000");
            Assert.AreEqual(final, a[a.Count - 1], "final tick");
            Assert.AreEqual(reason, m.State.Reason);
        }

        [Test]
        public void Grenades_are_thrown_go_off_and_kill_and_never_in_the_opening()
        {
            int thrown = 0, blasts = 0, kills = 0;
            for (int seed = 1; seed <= 6; seed++)
            {
                var (_, m) = FragTrace(seed, true);
                var ev = m.State.Events;
                foreach (var e in ev.Where(e => e.Kind == EventKind.GrenadeThrown))
                {
                    thrown++;
                    Assert.GreaterOrEqual(e.Tick, m.State.ContactTick, "a grenade before first contact");
                    var man = m.State.Men[e.Id];
                    Assert.AreEqual(e.Side, man.Side);
                    Assert.IsTrue(e.X.HasValue && e.Z.HasValue && e.Target.HasValue);
                }
                for (int i = 0; i < ev.Count; i++)
                {
                    if (ev[i].Kind != EventKind.GrenadeBlast) continue;
                    blasts++;
                    for (int j = i + 1; j < ev.Count && ev[j].Kind == EventKind.Kill && ev[j].Tick == ev[i].Tick; j++) kills++;
                }
                Assert.IsTrue(m.State.Men.All(x => x.Grenades >= 0 && x.Grenades <= Tune.GrenadesCarried));
            }
            Assert.Greater(thrown, 0, "nobody threw");
            Assert.Greater(blasts, 0, "nothing went off");
            Assert.LessOrEqual(blasts, thrown, "more blasts than throws");
            Assert.Greater(kills, 0, "grenades never killed anyone");
        }

        [TestCase(1, false, 2123, 1619485321u, 4283461350u, 1653315263u, "vc morale broke")]
        [TestCase(2, true, 3496, 3943026134u, 1248945397u, 3969713137u, "vc morale broke")]
        public void With_squad_smoke_a_match_is_pinned_and_still_a_pure_function_of_its_seed(
            int seed, bool frag, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            var (a, m) = FragTrace(seed, frag, true);
            var (b, _) = FragTrace(seed, frag, true);
            CollectionAssert.AreEqual(a, b, "two runs of the same seed diverged");
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(at100, a[100], "tick 100");
            Assert.AreEqual(at1000, a[1000], "tick 1000");
            Assert.AreEqual(final, a[a.Count - 1], "final tick");
            Assert.AreEqual(reason, m.State.Reason);
        }

        // --- Part 2: drill, behind MatchOptions.Drill ------------------------------------

        private static (List<uint> hashes, LiveMatch m) DrillTrace(int seed, bool frag, bool smoke, bool drill)
        {
            var m = new LiveMatch(new MatchOptions { Seed = seed, Us = Plan.Ceiling, Vc = Plan.Ceiling, Frag = frag, SquadSmoke = smoke, Drill = drill });
            var h = new List<uint> { Parity.Hash(m.State, 0) };
            while (!m.State.Over && m.State.Tick < m.Cap) { m.Step(); h.Add(Parity.Hash(m.State, 0)); }
            return (h, m);
        }

        /// <summary>Recorded by `tools/simcs/run.sh hash N [frag] [smoke] drill`.</summary>
        [TestCase(1, true, true, 2611, 439446229u, 520356264u, 1092527651u, "us morale broke")]
        [TestCase(7, false, false, 5110, 422315189u, 270495071u, 2282753341u, "vc morale broke")]
        public void With_drill_a_match_is_pinned_and_still_a_pure_function_of_its_seed(
            int seed, bool frag, bool smoke, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            var (a, m) = DrillTrace(seed, frag, smoke, true);
            var (b, _) = DrillTrace(seed, frag, smoke, true);
            CollectionAssert.AreEqual(a, b, "two runs of the same seed diverged");
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(at100, a[100], "tick 100");
            Assert.AreEqual(at1000, a[1000], "tick 1000");
            Assert.AreEqual(final, a[a.Count - 1], "final tick");
            Assert.AreEqual(reason, m.State.Reason);
        }

        /// <summary>
        /// What the rule is for (the owner's second playtest: "they walk up and
        /// down randomly, no military brain"): without it a squad's order
        /// changes hundreds of times a match and a man turns round hundreds of
        /// times; with it, a handful.
        /// </summary>
        [Test]
        public void Drill_is_off_by_default_and_when_on_squads_stop_dithering()
        {
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).Drill);
            (double orders, double turns) Dither(bool drill)
            {
                var m = new LiveMatch(new MatchOptions { Seed = 3, Us = Plan.Ceiling, Vc = Plan.Ceiling, Drill = drill });
                var st = m.State;
                var order = new Dictionary<int, Order>(); var changes = new Dictionary<int, int>();
                var at = new Dictionary<int, double>(); var heading = new Dictionary<int, int>(); var turned = new Dictionary<int, int>();
                for (int t = 0; t < 2000 && !st.Over; t++)
                {
                    m.Step();
                    foreach (var sq in st.Squads)
                    {
                        if (order.TryGetValue(sq.Id, out var o) && o != sq.Order) changes[sq.Id] = changes.GetValueOrDefault(sq.Id) + 1;
                        order[sq.Id] = sq.Order;
                    }
                    foreach (var man in st.Men)
                    {
                        if (!man.Alive) continue;
                        if (at.TryGetValue(man.Id, out double x))
                        {
                            int dir = Math.Abs(man.X - x) < 0.01 ? 0 : Math.Sign(man.X - x);
                            if (dir != 0) { if (heading.TryGetValue(man.Id, out int h) && h != dir) turned[man.Id] = turned.GetValueOrDefault(man.Id) + 1; heading[man.Id] = dir; }
                        }
                        at[man.Id] = man.X;
                    }
                }
                return (changes.Values.DefaultIfEmpty(0).Average(), turned.Values.DefaultIfEmpty(0).Average());
            }
            var loose = Dither(false);
            var drilled = Dither(true);
            Assert.Greater(loose.orders, 100, "the baseline no longer flickers: this test's premise has changed");
            Assert.Less(drilled.orders, 40, "a drilled squad's orders still flicker");
            Assert.Less(drilled.turns, 12, "drilled men still turn round and round");
            Assert.Less(drilled.turns * 10, loose.turns, "drill made no real difference to turning round");
        }

        // --- Part 2: fieldcraft, behind MatchOptions.Fieldcraft ----------------------------

        /// <summary>The match as the game plays it: on the map, grenades, squad smoke and drill on.</summary>
        private static MatchOptions Game(int seed, bool fieldcraft, Plan vc = null) => new MatchOptions
        {
            Seed = seed, Us = Plan.Ceiling, Vc = vc ?? Plan.Ceiling, Cover = Map.Cover(),
            Frag = true, SquadSmoke = true, Drill = true, Fieldcraft = fieldcraft,
        };

        private static (List<uint> hashes, LiveMatch m) Played(MatchOptions o)
        {
            var m = new LiveMatch(o);
            var h = new List<uint> { Parity.Hash(m.State, 0) };
            while (!m.State.Over && m.State.Tick < m.Cap) { m.Step(); h.Add(Parity.Hash(m.State, 0)); }
            return (h, m);
        }

        /// <summary>Recorded by `tools/simcs/run.sh hash N [frag] [smoke] drill fieldcraft [map]`.</summary>
        [TestCase(1, true, 2426, 2150481767u, 3948002490u, 1730117463u, "us morale broke")]
        [TestCase(7, false, 1663, 4101092549u, 568090037u, 1333380308u, "us morale broke")]
        public void With_fieldcraft_a_match_is_pinned_and_still_a_pure_function_of_its_seed(
            int seed, bool asTheGame, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).Fieldcraft, "fieldcraft must be off unless asked for");
            MatchOptions O() => asTheGame ? Game(seed, true)
                : new MatchOptions { Seed = seed, Us = Plan.Ceiling, Vc = Plan.Ceiling, Drill = true, Fieldcraft = true };
            var (a, m) = Played(O());
            var (b, _) = Played(O());
            CollectionAssert.AreEqual(a, b, "two runs of the same seed diverged");
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(at100, a[100], "tick 100");
            Assert.AreEqual(at1000, a[1000], "tick 1000");
            Assert.AreEqual(final, a[a.Count - 1], "final tick");
            Assert.AreEqual(reason, m.State.Reason);
        }

        /// <summary>
        /// What the rule is for (the owner, playtest 3: "they run circles around
        /// themselves, cross each other... not run around like headless
        /// chickens"). Counted over three matches as the game plays them:
        /// a man passing a living enemy within 5 m of him, and man-seconds
        /// spent within 0.6 m of a friend.
        /// </summary>
        [Test]
        public void With_fieldcraft_nobody_walks_through_the_enemy_or_stands_on_a_friend()
        {
            (int through, double onTop) Muddle(bool fieldcraft)
            {
                int through = 0;
                double onTop = 0;
                for (int seed = 6; seed <= 8; seed++)
                {
                    var m = new LiveMatch(Game(seed, fieldcraft));
                    var st = m.State;
                    var before = new Dictionary<int, double>();
                    for (int t = 0; t < 2400 && !st.Over; t++)
                    {
                        before.Clear();
                        foreach (var man in st.Men) if (man.Alive) before[man.Id] = man.X;
                        m.Step();
                        var live = st.Men.Where(x => x.Alive && before.ContainsKey(x.Id)).ToList();
                        for (int i = 0; i < live.Count; i++)
                        for (int j = i + 1; j < live.Count; j++)
                        {
                            var p = live[i]; var q = live[j];
                            double dx = p.X - q.X, dz = p.Z - q.Z, d2 = dx * dx + dz * dz;
                            if (p.Side == q.Side) { if (d2 < 0.36) onTop += Tune.Dt; continue; }
                            int was = Math.Sign(before[p.Id] - before[q.Id]);
                            if (Math.Abs(dz) < 5 && d2 < 36 && was != 0 && was != Math.Sign(dx)) through++;
                        }
                    }
                }
                return (through, onTop);
            }
            var loose = Muddle(false);
            var drilled = Muddle(true);
            Assert.Greater(loose.through, 6, "the baseline no longer walks through the enemy: this test's premise has changed");
            Assert.Greater(loose.onTop, 300, "the baseline no longer stacks men: this test's premise has changed");
            Assert.LessOrEqual(drilled.through, 1, "men still walk through the enemy");
            Assert.Less(drilled.onTop * 5, loose.onTop, "men still stand on each other");
        }

        [Test]
        public void A_lever_holds_a_squad_in_its_position_and_go_sends_it_on()
        {
            int trench = Map.Cover().First(c => c.Kind == CoverKind.Trench && c.X < 0 && c.Z > 0).Id;       // the forward trench
            int berm = Map.Cover().First(c => c.Kind == CoverKind.Berm).Id;
            // The VC sit tight, so the squad in the trench is left alone to obey.
            var m = new LiveMatch(Game(2, true, Plan.Defend));
            var st = m.State;
            m.Issue(Command.SetLever(Side.Us, trench, Lever.Hold));
            m.Issue(Command.SetLever(Side.Us, berm, Lever.Hold));
            m.Issue(Command.SetLever(Side.Us, 999, Lever.Hold));
            m.Step();
            Assert.IsTrue(m.Log[0].Accepted, "the lever on the trench was refused");
            Assert.IsTrue(m.Log[1].Accepted, "the lever on a bank was refused: every piece of cover is a strongpoint, built or natural");
            Assert.AreEqual(Lever.Hold, st.Cover[berm].LeverUs);
            Assert.IsFalse(m.Log[2].Accepted, "a lever was set on cover that does not exist");
            Assert.AreEqual(Lever.Hold, st.Cover[trench].LeverUs);
            Assert.AreEqual(Lever.Auto, st.Cover[trench].LeverVc, "one side's lever moved the other's");

            var c = st.Cover[trench];
            Squad held = null;
            for (int t = 0; t < 300 && held == null; t++)
            {
                m.Step();
                held = st.Squads.FirstOrDefault(q => q.Side == Side.Us && q.Target == trench && q.Order == Order.Hold
                                                     && Math.Abs(q.AnchorX - Fieldcraft.StopX(c, 1)) < 0.6);
            }
            Assert.IsNotNull(held, "no squad came to the held trench");
            for (int t = 0; t < 200; t++)
            {
                m.Step();
                if (held.Order == Order.Fallback) Assert.Inconclusive("the held squad broke; pick another seed");
                Assert.AreEqual(Order.Hold, held.Order, $"tick {st.Tick}: the held squad was given another order");
                Assert.AreEqual(trench, held.Target, $"tick {st.Tick}: the held squad left for other cover");
            }
            var men = Squads.Roster(st, held.Id);
            Assert.IsTrue(men.Count > 0 && men.All(x => x.PlaceCover == trench && x.Place >= 0), "a man of the held squad has no place in the trench");
            Assert.AreEqual(men.Count, men.Select(x => x.Place).Distinct().Count(), "two men hold one place");
            Assert.IsTrue(men.All(x => Math.Abs(x.Z - c.Z) < 0.5 && x.Cover == trench), "the squad is not standing in the trench");

            m.Issue(Command.SetLever(Side.Us, trench, Lever.Go));
            bool left = false;
            for (int t = 0; t < 400 && !left; t++)
            {
                m.Step();
                left = held.Target != trench && Squads.Roster(st, held.Id).All(x => x.Cover != trench);
            }
            Assert.IsTrue(left, "Go did not send the squad out of the trench");
            Assert.IsTrue(st.Events.Any(e => e.Kind == EventKind.VaultOut), "nobody climbed out");

            // And the whole thing replays from its command log.
            var again = LiveMatch.Replay(m.Options, m.Log);
            while (!m.State.Over && m.State.Tick < m.Cap) m.Step();
            Assert.AreEqual(Parity.Hash(m.State, 0), Parity.Hash(again.State, 0), "a match with levers did not replay");

            // With the rule off there are no levers to pull.
            var off = new LiveMatch(Game(2, false));
            off.Issue(Command.SetLever(Side.Us, trench, Lever.Hold));
            off.Step();
            Assert.IsFalse(off.Log[0].Accepted);
        }

        [Test]
        public void With_fieldcraft_men_climb_fight_hand_to_hand_and_a_round_goes_through_a_man()
        {
            var seen = new Dictionary<EventKind, int>();
            for (int seed = 1; seed <= 24 && !(seed > 6 && seen.GetValueOrDefault(EventKind.PositionTaken) > 0); seed++)
            {
                var m = new LiveMatch(Game(seed, true));
                var st = m.State;
                var at = new Dictionary<int, (double x, double z)>();
                int from = 0;
                while (!st.Over && st.Tick < m.Cap)
                {
                    at.Clear();
                    foreach (var man in st.Men) if (man.Alive && man.Vault > 1) at[man.Id] = (man.X, man.Z);
                    m.Step();
                    foreach (var kv in at)
                    {
                        var man = st.Men[kv.Key];
                        if (man.Alive) Assert.AreEqual(kv.Value, (man.X, man.Z), $"seed {seed} tick {st.Tick}: man {man.Id} moved while climbing");
                    }
                    for (int i = from; i < st.Events.Count; i++)
                    {
                        var e = st.Events[i];
                        seen[e.Kind] = seen.GetValueOrDefault(e.Kind) + 1;
                        if (e.Kind == EventKind.Melee)
                        {
                            var a = st.Men[e.Id]; var b = st.Men[e.Target.Value];
                            Assert.AreNotEqual(a.Side, b.Side, "a man struck his own side");
                            Assert.LessOrEqual(Combat.Dist(a, b), Tune.MeleeRange + 2 * Tune.SpeedStand * Tune.Dt, "a blow landed from out of reach");
                        }
                        if (e.Kind == EventKind.Through)
                        {
                            Assert.IsFalse(st.Men[e.Id].Alive, "a round went through a man it had not killed");
                            Assert.AreEqual(st.Men[e.Id].Side, st.Men[e.Target.Value].Side);
                            Assert.AreEqual(e.Amount == 1, !st.Men[e.Target.Value].Alive && st.Men[e.Target.Value].DiedAt == e.Tick);
                        }
                    }
                    from = st.Events.Count;
                }
            }
            foreach (var kind in new[] { EventKind.VaultIn, EventKind.VaultOut, EventKind.Melee, EventKind.Through, EventKind.PositionTaken })
                Assert.Greater(seen.GetValueOrDefault(kind), 0, $"{kind} never happened");
        }

        // --- Part 2: arms, behind MatchOptions.Arms ------------------------------------------

        private static MatchOptions Armed(int seed, bool onMap = true) => new MatchOptions
        {
            Seed = seed, Us = Plan.Ceiling, Vc = Plan.Ceiling, Cover = onMap ? Map.Cover() : null,
            Frag = onMap, SquadSmoke = onMap, Drill = true, Fieldcraft = true, Arms = true,
        };

        /// <summary>Recorded by `tools/simcs/run.sh hash N [frag smoke] drill fieldcraft arms [map]`.</summary>
        [TestCase(1, true, 3636, 1346899756u, 2245466774u, 416579678u, "vc morale broke")]
        [TestCase(7, false, 4243, 3835937325u, 2989996820u, 2710323361u, "us morale broke")]
        public void With_arms_a_match_is_pinned_and_still_a_pure_function_of_its_seed(
            int seed, bool asTheGame, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).Arms, "arms must be off unless asked for");
            var (a, m) = Played(Armed(seed, asTheGame));
            var (b, _) = Played(Armed(seed, asTheGame));
            CollectionAssert.AreEqual(a, b, "two runs of the same seed diverged");
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(at100, a[100], "tick 100");
            Assert.AreEqual(at1000, a[1000], "tick 1000");
            Assert.AreEqual(final, a[a.Count - 1], "final tick");
            Assert.AreEqual(reason, m.State.Reason);
        }

        // --- Part 2: senses, behind MatchOptions.Senses ---------------------------------------

        private static MatchOptions Sensed(int seed, bool onMap = true)
        {
            var o = Armed(seed, onMap);
            o.Senses = true;
            return o;
        }

        /// <summary>Recorded by `tools/simcs/run.sh hash N [frag smoke] drill fieldcraft arms senses [map]`.</summary>
        [TestCase(1, true, 4549, 2659694081u, 3159165556u, 582378346u, "us morale broke")]
        [TestCase(7, false, 1529, 84624186u, 2067593218u, 125784578u, "vc wiped out")]
        public void With_senses_a_match_is_pinned_and_still_a_pure_function_of_its_seed(
            int seed, bool asTheGame, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).Senses, "senses must be off unless asked for");
            var (a, m) = Played(Sensed(seed, asTheGame));
            var (b, _) = Played(Sensed(seed, asTheGame));
            CollectionAssert.AreEqual(a, b, "two runs of the same seed diverged");
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(at100, a[100], "tick 100");
            Assert.AreEqual(at1000, a[1000], "tick 1000");
            Assert.AreEqual(final, a[a.Count - 1], "final tick");
            Assert.AreEqual(reason, m.State.Reason);
        }

        /// <summary>
        /// The owner, playtest 5: "shots should only be fired when a squad spots
        /// another. Most can be misses but the intent needs to be there."
        /// Nobody is seen for free, and every round is fired at a squad the
        /// firer's own has in sight (its own eyes, or word from a squad beside
        /// it), except a machine gun's bursts on cover it knows was held, which
        /// are marked and kill nobody.
        /// </summary>
        [Test]
        public void With_senses_nobody_is_seen_for_free_and_every_shot_is_at_a_squad_the_firers_own_has_in_sight()
        {
            int shots = 0, bursts = 0;
            for (int seed = 1; seed <= 8; seed++)
            {
                var o = Sensed(seed);
                o.CpRate = 1.6; o.StartCp = 20; o.OpeningStrength = 4; o.MusterCost = 29; o.MoraleRate = 0.8;      // as the game plays it
                var m = new LiveMatch(o);
                var st = m.State;
                Assert.IsFalse(st.Men.Any(x => x.Seen), "somebody is seen before anyone has looked");
                while (!st.Over && st.Tick < 3600)
                {
                    int from = st.Events.Count;
                    m.Step();
                    for (int i = from; i < st.Events.Count; i++)
                    {
                        var e = st.Events[i];
                        if (e.Kind != EventKind.Fire) continue;
                        var by = st.Men[e.Id]; var at = st.Men[e.Target.Value];
                        var sq = st.Squads[by.Squad];
                        if (e.Amount.HasValue)
                        {
                            bursts++;
                            Assert.IsTrue(by.Weapon == Weapon.M60 || by.Weapon == Weapon.Rpd, $"a {by.Weapon} fired a suppressing burst");
                            Assert.IsTrue(Senses.Remembers(st, sq, at.Squad), "a burst on cover nobody knew was held");
                            // The event straight after a shot is its kill, if it made one.
                            Assert.IsFalse(i + 1 < st.Events.Count && st.Events[i + 1].Kind == EventKind.Kill && st.Events[i + 1].Id == at.Id,
                                           "a suppressing burst killed a man");
                            continue;
                        }
                        shots++;
                        Assert.IsTrue(Senses.Knows(st, sq, at.Squad), $"seed {seed} tick {st.Tick}: man {by.Id} fired at a squad his own does not have in sight");
                    }
                }
            }
            Assert.Greater(shots, 500, "hardly a shot was fired");
            Assert.Greater(bursts, 0, "no machine gun ever kept a burst on cover it had lost sight of");
        }

        /// <summary>
        /// "Still wander around, don't see each other, stand around when not
        /// supposed to." A squad has one task and it stands: every task is
        /// reached, contact is an event, a squad that has gone to ground stays
        /// there two seconds at least (unless it breaks), a broken squad runs
        /// to cover behind it, and the side that opens holding opens in cover.
        /// </summary>
        [Test]
        public void With_senses_a_squad_has_a_task_and_keeps_it()
        {
            var reached = new HashSet<SquadTask>();
            int contacts = 0, withdrawals = 0, toCover = 0, shortLived = 0, changes = 0;
            for (int seed = 3; seed <= 8; seed++)
            {
                var o = Sensed(seed);
                o.CpRate = 1.6; o.StartCp = 20; o.OpeningStrength = 4; o.MusterCost = 29; o.MoraleRate = 0.8;      // as the game plays it
                var m = new LiveMatch(o);
                var st = m.State;
                foreach (var man in st.Men.Where(x => x.Side == Side.Us && x.Place >= 0))
                    Assert.GreaterOrEqual(man.Cover, 0, "an American who opens the match holding a place is not in its cover");
                Assert.IsTrue(st.Men.Any(x => x.Side == Side.Us && x.Place >= 0), "the Americans do not open in a position");
                var task = new Dictionary<int, (SquadTask task, int since)>();
                while (!st.Over && st.Tick < 3600)
                {
                    int from = st.Events.Count;
                    m.Step();
                    for (int i = from; i < st.Events.Count; i++) if (st.Events[i].Kind == EventKind.Contact) contacts++;
                    foreach (var sq in st.Squads)
                    {
                        if (!st.Men.Any(x => x.Alive && x.Squad == sq.Id)) continue;
                        reached.Add(sq.Task);
                        if (task.TryGetValue(sq.Id, out var was) && was.task != sq.Task)
                        {
                            changes++;
                            bool grounded = was.task == SquadTask.Firefight || was.task == SquadTask.Regroup;
                            if (grounded && sq.Task != SquadTask.Withdraw && st.Tick - was.since < Tune.TaskMin) shortLived++;
                            if (sq.Task == SquadTask.Withdraw)
                            {
                                withdrawals++;
                                if (sq.Target >= 0)
                                {
                                    toCover++;
                                    double back = (Fieldcraft.StopX(st.Cover[sq.Target], Combat.Advance(sq.Side)) - sq.AnchorX) * Combat.Advance(sq.Side);
                                    Assert.Less(back, 0, "a squad fell back to cover in front of it");
                                }
                            }
                            task[sq.Id] = (sq.Task, st.Tick);
                        }
                        else if (!task.ContainsKey(sq.Id)) task[sq.Id] = (sq.Task, st.Tick);
                    }
                }
            }
            foreach (SquadTask t in System.Enum.GetValues(typeof(SquadTask)))
                Assert.IsTrue(reached.Contains(t), $"no squad was ever on the task {t}");
            Assert.Greater(contacts, 20, "hardly a squad ever came into contact");
            Assert.Greater(withdrawals, 5, "hardly a squad ever fell back");
            Assert.Greater(toCover * 2, withdrawals, "most squads that fell back had no strongpoint to fall back to");
            Assert.AreEqual(0, shortLived, $"of {changes} changes of task, some squads got up again inside two seconds of going to ground");
        }

        // --- Part 2: gunnery, behind MatchOptions.Gunnery -------------------------------------

        private static MatchOptions Gunned(int seed, bool onMap = true, bool tempo = false)
        {
            var o = Sensed(seed, onMap);
            o.Gunnery = true;
            if (tempo) { o.CpRate = 1.6; o.StartCp = 20; o.OpeningStrength = 4; o.MusterCost = 29; o.MoraleRate = 0.9; }      // as the game plays it
            return o;
        }

        /// <summary>Recorded by `tools/simcs/run.sh hash N [frag smoke] drill fieldcraft arms senses gunnery [map]`.</summary>
        [TestCase(1, true, 2212, 2873463602u, 456925665u, 2240870933u, "vc morale broke")]
        [TestCase(7, false, 2603, 193823866u, 2308654957u, 890381349u, "us morale broke")]
        public void With_gunnery_a_match_is_pinned_and_still_a_pure_function_of_its_seed(
            int seed, bool asTheGame, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).Gunnery, "gunnery must be off unless asked for");
            var (a, m) = Played(Gunned(seed, asTheGame));
            var (b, _) = Played(Gunned(seed, asTheGame));
            CollectionAssert.AreEqual(a, b, "two runs of the same seed diverged");
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(at100, a[100], "tick 100");
            Assert.AreEqual(at1000, a[1000], "tick 1000");
            Assert.AreEqual(final, a[a.Count - 1], "final tick");
            Assert.AreEqual(reason, m.State.Reason);
        }

        /// <summary>
        /// The owner, playtest 6: "too many random shots shooting up, down and
        /// in circles"; "the gunners sometimes shoot and walk, they should only
        /// shoot prone or kneeling. Shooting while standing should be very
        /// inaccurate (and happen 10% of the time or less)"; "see where bullets
        /// go". Every bullet is fired by a man who is not moving, down the lane
        /// (or at arm's length), and says where it went; fewer than one in ten
        /// is fired from the feet, and those hit less than half as often as the
        /// rest.
        /// </summary>
        [Test]
        public void With_gunnery_bullets_are_fired_at_rest_down_the_lane_and_seldom_from_the_feet()
        {
            int bullets = 0, standing = 0, standingHits = 0, downHits = 0;
            for (int seed = 3; seed <= 8; seed++)
            {
                var m = new LiveMatch(Gunned(seed, tempo: true));
                var st = m.State;
                while (!st.Over && st.Tick < 3600)
                {
                    var before = st.Men.Select(x => (x.X, x.Z)).ToArray();
                    int from = st.Events.Count;
                    m.Step();
                    for (int i = from; i < st.Events.Count; i++)
                    {
                        var e = st.Events[i];
                        if (e.Kind == EventKind.Launch) Assert.IsTrue(e.Id >= before.Length || (st.Men[e.Id].X == before[e.Id].X && st.Men[e.Id].Z == before[e.Id].Z), "a man fired a launcher on the move");
                        if (e.Kind != EventKind.Fire) continue;
                        var by = st.Men[e.Id]; var at = st.Men[e.Target.Value];
                        bullets++;
                        Assert.IsTrue(by.X == before[by.Id].X && by.Z == before[by.Id].Z, $"seed {seed} tick {st.Tick}: man {by.Id} fired while moving");
                        Assert.IsTrue(e.X.HasValue && e.Z.HasValue, "a round that went nowhere");
                        bool hit = i + 1 < st.Events.Count && st.Events[i + 1].Kind == EventKind.Kill && st.Events[i + 1].Id == at.Id;
                        // Where the man he fired at was when he fired (a man hit has not moved since: he is dead).
                        double tx = at.Id < before.Length ? before[at.Id].X : at.X, tz = at.Id < before.Length ? before[at.Id].Z : at.Z;
                        double dx = System.Math.Abs(by.X - tx), dz = System.Math.Abs(by.Z - tz);
                        // Down the lane, at arm's length, or across it at a man he can see over the bank (playtest 8).
                        Assert.IsTrue(Gunnery.Sightline(by, at) || dx * dx + dz * dz <= (Tune.ChargeRange + 0.5) * (Tune.ChargeRange + 0.5),
                                      $"seed {seed} tick {st.Tick}: a bullet fired through the bank, {dz:F1} m across the lane for {dx:F1} m along it");
                        if (hit) Assert.AreEqual(at.X, e.X.Value, 1e-9, "a hit that landed somewhere else");
                        if (by.Posture == Posture.Standing) { standing++; if (hit) standingHits++; }
                        else if (hit) downHits++;
                    }
                    foreach (var man in st.Men)
                    {
                        if (!man.Alive || man.Id >= before.Length || man.Posture == Posture.Standing) continue;
                        bool moved = man.X != before[man.Id].X || man.Z != before[man.Id].Z;
                        bool sapper = st.Squads[man.Squad].Reach <= Tune.Stalks && man.Posture == Posture.Crouched;
                        Assert.IsFalse(moved && !sapper, $"seed {seed} tick {st.Tick}: man {man.Id} travelled {man.Posture}");
                    }
                }
            }
            Assert.Greater(bullets, 1500, "hardly a shot was fired");
            Assert.Less(standing * 10, bullets, $"{standing} of {bullets} bullets were fired by a man on his feet");
            Assert.GreaterOrEqual(standingHits + downHits, 20, "hardly anyone was hit");

            // And from his feet a man hits a third as often as the same man on a knee, at the same target.
            var fresh = Match.Create(Gunned(3));
            var a = fresh.Men.First(x => x.Side == Side.Us); var b = fresh.Men.First(x => x.Side == Side.Vc);
            b.X = a.X + 12; b.Z = a.Z;
            a.Posture = Posture.Crouched;
            double kneeling = Combat.HitChance(fresh, a, b);
            a.Posture = Posture.Standing;
            Assert.AreEqual(Tune.StandingHit * kneeling, Combat.HitChance(fresh, a, b), 1e-12);
            Assert.Less(Tune.StandingHit, 0.5);
        }

        // --- Part 2: ammunition, behind MatchOptions.Ammo; sight across the lanes and a squad's turns, Gunnery -----

        private static MatchOptions Loaded(int seed, bool onMap = true, bool tempo = false)
        {
            var o = Gunned(seed, onMap, tempo);
            o.Ammo = true;
            return o;
        }

        /// <summary>Recorded by `tools/simcs/run.sh hash N [frag smoke] drill fieldcraft arms senses gunnery ammo [map]`.</summary>
        [TestCase(1, true, 2725, 3635094490u, 2616258432u, 988759736u, "us morale broke")]
        public void With_ammo_a_match_is_pinned_and_still_a_pure_function_of_its_seed(
            int seed, bool asTheGame, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).Ammo, "ammunition must be off unless asked for");
            var (a, m) = Played(Loaded(seed, asTheGame));
            var (b, _) = Played(Loaded(seed, asTheGame));
            CollectionAssert.AreEqual(a, b, "two runs of the same seed diverged");
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(at100, a[100], "tick 100");
            Assert.AreEqual(at1000, a[1000], "tick 1000");
            Assert.AreEqual(final, a[a.Count - 1], "final tick");
            Assert.AreEqual(reason, m.State.Reason);
        }

        /// <summary>
        /// The owner, playtest 8: "No reloads ... the M60 or the Viet gunner just spams rounds non-stop. Make
        /// them only full auto when necessary, do short bursts and do reloads (count rounds ...). Same with
        /// grenades: can't be endless spam." Rounds are counted and never go below nothing; nobody fires
        /// while he reloads; a burst is a short one or, close in, a long one, and never more than the
        /// magazine had; men reload; a launcher fires no more rounds than it carried; a squad throws one
        /// grenade at a time.
        /// </summary>
        [Test]
        public void With_ammo_rounds_are_counted_magazines_run_out_and_reloads_take_their_time()
        {
            int reloads = 0, bursts = 0, longBursts = 0, launched = 0;
            for (int seed = 3; seed <= 6; seed++)
            {
                var m = new LiveMatch(Loaded(seed, tempo: true));
                var st = m.State;
                var fired = new Dictionary<int, int>();
                var thrown = new Dictionary<int, int>();
                while (!st.Over && st.Tick < 3600)
                {
                    var reloading = st.Men.Select(x => x.Reloading).ToArray();
                    var held = st.Men.Select(x => x.Rounds).ToArray();
                    int from = st.Events.Count;
                    m.Step();
                    foreach (var man in st.Men) Assert.GreaterOrEqual(man.Rounds, 0, "a man has fewer than no rounds");
                    for (int i = from; i < st.Events.Count; i++)
                    {
                        var e = st.Events[i];
                        if (e.Kind == EventKind.Reload) reloads++;
                        if (e.Kind == EventKind.GrenadeThrown)
                        {
                            int sq = st.Men[e.Id].Squad;
                            if (thrown.TryGetValue(sq, out int at)) Assert.GreaterOrEqual(st.Tick - at, Tune.SquadThrowGap, $"seed {seed}: squad {sq} threw two grenades at once");
                            thrown[sq] = st.Tick;
                        }
                        if (e.Kind == EventKind.Launch)
                        {
                            launched++;
                            fired[e.Id] = fired.GetValueOrDefault(e.Id) + 1;
                            Assert.LessOrEqual(fired[e.Id], Ammo.Of(st.Men[e.Id].Weapon).Magazine, $"seed {seed}: man {e.Id} fired more rounds than he carried");
                        }
                        if (e.Kind != EventKind.Fire) continue;
                        Assert.Greater(e.Rounds, 0, "a shot that spent nothing");
                        // (A reload with a tick left ends on this one, and he may fire on it.)
                        if (e.Id < reloading.Length) Assert.LessOrEqual(reloading[e.Id], 1, $"seed {seed}: man {e.Id} fired while reloading");
                        var load = Ammo.Of(st.Men[e.Id].Weapon);
                        if (e.Id < held.Length)
                            Assert.LessOrEqual(e.Rounds, reloading[e.Id] == 1 ? load.Magazine : held[e.Id], $"seed {seed}: man {e.Id} fired rounds he did not have");
                        if (!load.Automatic) { Assert.AreEqual(1, e.Rounds); continue; }
                        bursts++;
                        Assert.LessOrEqual(e.Rounds, load.LongBurst);
                        if (e.Rounds > load.Burst) longBursts++;
                    }
                }
            }
            Assert.Greater(reloads, 20, "hardly anyone reloaded");
            Assert.Greater(bursts, 100, "hardly a burst was fired");
            Assert.Greater(longBursts, 0, "no machine gun ever went to full automatic");
            Assert.Less(longBursts * 2, bursts, "full automatic is the rule, not the exception");
            Assert.Greater(launched, 0);
            Assert.IsFalse(new LiveMatch(Gunned(3)).State.Men.Any(x => x.Rounds != 0), "rounds are issued without the rule");
        }

        /// <summary>
        /// The owner, playtest 8: "AI soldiers don't shoot each other across lanes even when directly across one
        /// another. Make it so they respect the mountain range and can only shoot when they see each other." The
        /// bank between the lanes hides a man down behind it from the other lane and him from them; two men on
        /// their feet see over it; a gap in it hides nobody; two men on one side of it always see each other.
        /// </summary>
        [Test]
        public void With_gunnery_the_bank_between_the_lanes_hides_men_who_are_down_behind_it()
        {
            Man At(double x, double z, Posture p) => new Man { X = x, Z = z, Posture = p };
            double crest = double.NaN, gap = double.NaN;
            for (double x = -150; x <= 150 && (double.IsNaN(crest) || double.IsNaN(gap)); x += 0.25)
            {
                if (double.IsNaN(crest) && Gunnery.Bank(x) > 0.84) crest = x;
                if (double.IsNaN(gap) && Gunnery.Bank(x) == 0) gap = x;
            }
            Assert.IsFalse(double.IsNaN(crest), "the bank never stands at its full height");
            Assert.IsFalse(double.IsNaN(gap), "the bank has no gaps");
            Assert.IsFalse(Gunnery.Sightline(At(crest, Tune.Lanes[0], Posture.Prone), At(crest, Tune.Lanes[1], Posture.Crouched)), "a man flat behind the bank is seen from the other lane");
            Assert.IsFalse(Gunnery.Sightline(At(crest, Tune.Lanes[1], Posture.Crouched), At(crest, Tune.Lanes[0], Posture.Prone)), "he is seen the other way");
            Assert.IsTrue(Gunnery.Sightline(At(crest, Tune.Lanes[0], Posture.Standing), At(crest, Tune.Lanes[1], Posture.Standing)), "two men on their feet do not see over the bank");
            Assert.IsTrue(Gunnery.Sightline(At(gap, Tune.Lanes[0], Posture.Prone), At(gap, Tune.Lanes[1], Posture.Prone)), "a gap in the bank hides two men");
            Assert.IsTrue(Gunnery.Sightline(At(crest, Tune.Lanes[1], Posture.Prone), At(crest + 10, Tune.Lanes[1], Posture.Prone)), "two men in one lane do not see each other");

            // And in play: men fire across the lanes, and only at a man they can see.
            int across = 0;
            for (int seed = 3; seed <= 5; seed++)
            {
                var m = new LiveMatch(Loaded(seed, tempo: true));
                var st = m.State;
                while (!st.Over && st.Tick < 3000)
                {
                    int from = st.Events.Count;
                    m.Step();
                    for (int i = from; i < st.Events.Count; i++)
                    {
                        var e = st.Events[i];
                        if (e.Kind != EventKind.Fire) continue;
                        Man by = st.Men[e.Id], at = st.Men[e.Target.Value];
                        if (st.Squads[by.Squad].Lane == st.Squads[at.Squad].Lane) continue;
                        across++;
                        Assert.IsTrue(Gunnery.Sightline(by, at) || Combat.Dist(by, at) <= Tune.ChargeRange + 0.5, $"seed {seed} tick {st.Tick}: a man fired through the bank");
                    }
                }
            }
            Assert.Greater(across, 0, "nobody ever fired across the lanes");
        }

        /// <summary>
        /// The map as data (PLAN §12.27, S1). The firebase is a map among the maps now, and everything that read
        /// it as code reads the same from it: its cover, its craters, banks and trenches, its tower. A match
        /// takes its bank from its map, and the bank it takes is the one the ground is built with: where the
        /// ground's bank stands the simulation's stands, to the height, and where the ground's has a gap so has it.
        /// </summary>
        [Test]
        public void The_firebase_is_a_map_among_the_maps_and_a_match_takes_its_bank_and_its_posts_from_its_map()
        {
            Assert.AreSame(Maps.Firebase, Maps.ById("firebase"));
            Assert.IsNull(Maps.ById("no such map"));
            Assert.Contains(Maps.Firebase, Maps.All);

            var cover = Map.Cover(); var its = Maps.Firebase.Cover();
            Assert.AreEqual(17, cover.Count);
            Assert.AreEqual(cover.Count, its.Count);
            for (int i = 0; i < cover.Count; i++)
            {
                Assert.AreEqual(i, its[i].Id);
                Assert.AreEqual(cover[i].Kind, its[i].Kind);
                Assert.AreEqual(cover[i].X, its[i].X);
                Assert.AreEqual(cover[i].Z, its[i].Z);
                Assert.AreEqual(cover[i].Length, its[i].Length);
                Assert.AreEqual(cover[i].Capacity, its[i].Capacity);
                if (i > 0) Assert.GreaterOrEqual(its[i].X, its[i - 1].X, "the map's cover is not in order along the lanes");
            }
            Assert.AreNotSame(Maps.Firebase.Cover()[0], Maps.Firebase.Cover()[0], "a map hands out the same cover twice: a match would change the map");
            Assert.AreEqual(Map.Craters().Count, Maps.Firebase.Craters().Count);
            Assert.AreEqual(Map.Mounds().Count, Maps.Firebase.Mounds().Count);
            Assert.AreEqual(Map.Trenches().Count, Maps.Firebase.Trenches().Count);
            Assert.AreEqual(1, Maps.Firebase.PostList().Count);
            Assert.AreEqual(Map.TowerX, Maps.Firebase.PostList()[0].X);

            // The bank the simulation reads is the bank the ground has.
            var ground = Maps.Firebase.Ground();
            var st = Match.Create(Loaded(3, tempo: true));
            Assert.AreEqual(ground.Params.BermZ, st.BankZ);
            Assert.AreEqual(ground.Params.BermHeight, st.BankHeight);
            for (double x = -60; x <= 60; x += 0.5)
            {
                Assert.AreEqual(ground.Params.BermHeight * ground.BermGate(x), Gunnery.Bank(st, x), 1e-9, $"the bank at {x}");
                Assert.AreEqual(Gunnery.Bank(x), Gunnery.Bank(st, x), $"the firebase's bank at {x}");
            }

            // And a map with another bank gives the match that one: here, none at all, so nobody is hidden by it.
            var flat = new MapDef { Id = "flat", Name = "FLAT", Pieces = Maps.Firebase.Pieces, GroundShape = new GroundParams { BermHeight = 0 } };
            var o = Loaded(3, tempo: true); o.Map = flat; o.Cover = flat.Cover();
            var on = Match.Create(o);
            Assert.AreEqual(0, on.BankHeight);
            Assert.AreEqual(0, on.Posts.Count, "a map with no tower gave the match one");
            Man At(double x, double z, Posture p) => new Man { X = x, Z = z, Posture = p };
            for (double x = -40; x <= 40; x += 2.5)
                Assert.IsTrue(Gunnery.Sightline(on, At(x, Tune.Lanes[0], Posture.Prone), At(x, Tune.Lanes[1], Posture.Prone)), $"no bank, and it hid a man at {x}");
        }

        /// <summary>
        /// The owner, playtest 8: "US soldiers all climb up exactly at the same time ... change that so it's not so
        /// coordinated." When a squad that has been at rest goes, its men get up and set off one after another.
        /// </summary>
        [Test]
        public void With_gunnery_a_squad_sets_off_a_man_at_a_time()
        {
            int squadsWatched = 0, together = 0, staggered = 0;
            for (int seed = 3; seed <= 6; seed++)
            {
                var m = new LiveMatch(Loaded(seed, tempo: true));
                var st = m.State;
                var startedAt = new Dictionary<int, int>();
                while (!st.Over && st.Tick < 2400)
                {
                    var rest = st.Men.Select(x => x.Rest).ToArray();
                    var was = st.Men.Select(x => x.Posture).ToArray();
                    m.Step();
                    // Men who had been at rest a good while and set off, or got up, this tick, by squad.
                    var went = new Dictionary<int, int>();
                    for (int i = 0; i < rest.Length; i++)
                    {
                        var man = st.Men[i];
                        if (!man.Alive || rest[i] < 40 || st.Squads[man.Squad].Order == Order.Fallback) continue;
                        bool rose = was[i] != Posture.Standing && man.Posture == Posture.Standing;
                        bool set = man.Rest == 0;
                        if (rose || set) went[man.Squad] = went.GetValueOrDefault(man.Squad) + 1;
                    }
                    foreach (var kv in went)
                    {
                        squadsWatched++;
                        if (kv.Value >= 3) together++; else staggered++;
                    }
                }
            }
            Assert.Greater(squadsWatched, 20);
            Assert.Less(together * 10, squadsWatched, $"{together} of {squadsWatched} squads had three or more men go on the same tick");
        }

        /// <summary>
        /// The start screen says a skirmish is about two minutes, a standard
        /// match about three and a half, a siege about eight. They had drifted
        /// to a minute and a half, two and five. At the game's settings (its
        /// economy, a Veteran's squad cost, its morale rate for each length),
        /// the two plans left to fight it out run what the screen says, give
        /// or take a third, and the baseline's own rate is untouched.
        /// </summary>
        [TestCase(MatchLength.Skirmish, 1.0, 85, 165)]
        [TestCase(MatchLength.Standard, 0.8, 150, 270)]
        [TestCase(MatchLength.Long, 0.9, 330, 600)]
        public void At_the_games_settings_a_match_runs_as_long_as_the_start_screen_says(MatchLength length, double rate, int atLeast, int atMost)
        {
            Assert.AreEqual(1.0, new MatchOptions().MoraleRate, "the baseline's morale rate moved");
            double seconds = 0;
            const int seeds = 12;
            for (int seed = 1; seed <= seeds; seed++)
            {
                var o = Loaded(seed, tempo: true);
                o.Length = length; o.MoraleRate = rate;
                seconds += Match.Run(o).Seconds;
            }
            Assert.That(seconds / seeds, Is.InRange((double)atLeast, (double)atMost), $"a {length} match runs {seconds / seeds:F0} s on average");
        }

        /// <summary>
        /// The owner: "add all the corresponding gun models to each class. Also
        /// add a sniper class." Every card with men on it buys exactly the squad
        /// its kit names, weapon by weapon, and its pips say how many; without
        /// the rule every man is the baseline's one rifleman.
        /// </summary>
        [Test]
        public void Every_card_with_men_on_it_buys_the_squad_its_kit_names()
        {
            Assert.IsNotNull(Deck.Find(Side.Us, "us-sniper"), "there is no US sniper card");
            foreach (var side in Match.Sides)
            foreach (var card in Deck.For(side).Where(c => c.Pips > 0))
            {
                var kit = Arms.For(card.Id);
                Assert.IsNotNull(kit, $"{card.Id} has no kit");
                Assert.AreEqual(card.Pips, kit.Men.Length, $"{card.Id}: its pips and its kit disagree");

                var m = new LiveMatch(Armed(3));
                m.State.Cp[(int)side] = 99;
                int men = m.State.Men.Count;
                m.Issue(Command.Buy(side, card.Id, 0, 0));
                m.Step();
                Assert.IsTrue(m.Log[0].Accepted, $"{card.Id} was not bought");
                var bought = m.State.Men.Skip(men).ToList();
                CollectionAssert.AreEqual(kit.Men, bought.Select(x => x.Weapon).ToArray(), $"{card.Id} bought the wrong men");
                var sq = m.State.Squads[bought[0].Squad];
                Assert.AreEqual(kit.Reach, sq.Reach, $"{card.Id}: the squad does not fight from its kit's distance");
                Assert.AreEqual(kit.Assaults, sq.Assaults);
            }
            var sniper = Arms.For("us-sniper");
            Assert.AreEqual(Weapon.Sniper, sniper.Men[0]);
            Assert.AreEqual(Weapon.Sniper, Arms.For("vc-marksman").Men[0], "the VC's marksman is not a sniper");

            var plain = new LiveMatch(Game(3, true));
            Assert.IsTrue(plain.State.Men.All(x => x.Weapon == Weapon.Rifle), "without the rule a man carries something other than the one rifle");
        }

        /// <summary>
        /// "Fix distance for all gunfights, it needs to be properly set": nobody
        /// fires from beyond his weapon's range, a sniper engages from further
        /// than a rifleman can and a submachine gun from nearer, a bursting round
        /// bursts, and every weapon the map's squads carry is used.
        /// </summary>
        [Test]
        public void With_arms_every_weapon_fights_at_its_own_distance()
        {
            var furthest = new Dictionary<Weapon, double>();
            var shots = new Dictionary<Weapon, int>();
            int launched = 0, burst = 0;
            for (int seed = 1; seed <= 10; seed++)
            {
                var m = new LiveMatch(Armed(seed));
                var st = m.State;
                // One of every squad a card can buy, so the mortar and the engineers take the field too.
                st.Cp[0] = st.Cp[1] = 400;
                foreach (var side in Match.Sides)
                    foreach (var card in Deck.For(side).Where(c => c.Pips > 0)) m.Issue(Command.Buy(side, card.Id, seed % 2, 0));
                int from = 0;
                while (!st.Over && st.Tick < m.Cap)
                {
                    m.Step();
                    for (int i = from; i < st.Events.Count; i++)
                    {
                        var e = st.Events[i];
                        if (e.Kind == EventKind.GrenadeBlast) burst++;
                        if (e.Kind != EventKind.Fire && e.Kind != EventKind.Launch) continue;
                        var by = st.Men[e.Id]; var at = st.Men[e.Target.Value];
                        var arm = Arms.Of(by.Weapon);
                        if (e.Kind == EventKind.Launch) { launched++; Assert.IsTrue(arm.Bursts, $"{by.Weapon} launched a round"); }
                        else Assert.IsFalse(arm.Bursts, $"{by.Weapon} fired a bullet");
                        // Both men may have moved a step since the range was taken.
                        double d = Combat.Dist(by, at);
                        Assert.LessOrEqual(d, arm.Range + 0.5, $"seed {seed} tick {e.Tick}: a {by.Weapon} fired at {d:F1} m");
                        if (arm.MinRange > 0) Assert.GreaterOrEqual(d, arm.MinRange - 0.5, $"a {by.Weapon} fired at {d:F1} m, inside its minimum");
                        furthest[by.Weapon] = Math.Max(furthest.GetValueOrDefault(by.Weapon), d);
                        shots[by.Weapon] = shots.GetValueOrDefault(by.Weapon) + 1;
                    }
                    from = st.Events.Count;
                }
            }
            foreach (Weapon w in Enum.GetValues(typeof(Weapon)))
                if (w != Weapon.Rifle) Assert.Greater(shots.GetValueOrDefault(w), 0, $"no {w} was ever fired");
            Assert.Greater(launched, 0);
            Assert.GreaterOrEqual(burst, launched - 10, "rounds were launched that never burst");
            Assert.Greater(furthest[Weapon.Sniper], Arms.Of(Weapon.M16).Range + 5, "the sniper never fired from beyond a rifle's range");
            Assert.Less(furthest[Weapon.Smg], Arms.Of(Weapon.M16).Range, "a submachine gun reached as far as a rifle");
            Assert.Greater(Arms.Of(Weapon.M60).Range, Arms.Of(Weapon.M16).Range);
        }

        /// <summary>
        /// The owner, playtest 4: "Points are gained too slow. Too many soldiers
        /// at the start." A match's tempo is its options': how fast points come
        /// in, what a side starts with, how many men it opens with, and whose
        /// reinforcements are a player's to buy. Their defaults are the baseline.
        /// </summary>
        [Test]
        public void A_players_side_raises_nothing_by_itself_and_the_tempo_is_the_options()
        {
            var baseline = new MatchOptions { Seed = 4 };
            Assert.AreEqual(Tune.CpPerSecond, baseline.CpRate);
            Assert.AreEqual(Tune.OpeningStrength, baseline.OpeningStrength);
            Assert.AreEqual(0, baseline.StartCp);
            Assert.AreEqual(Tune.CpPerSquad, baseline.MusterCost);
            Assert.IsNull(baseline.Player);

            MatchOptions Tempo(Side? player)
            {
                var o = Armed(4);
                o.CpRate = 1.6; o.StartCp = 20; o.OpeningStrength = 8; o.Player = player;
                return o;
            }
            var st = Match.Create(Tempo(Side.Us));
            foreach (var side in Match.Sides)
            {
                int men = st.Men.Count(m => m.Side == side);
                Assert.GreaterOrEqual(men, 8, $"{side} opens with {men} men");
                Assert.LessOrEqual(men, 12, $"{side} opens with {men} men: more than two squads");
                Assert.AreEqual(20, st.Cp[(int)side]);
            }
            Assert.Less(st.Men.Count, Match.Create(Armed(4)).Men.Count, "a smaller opening put as many men on the map");

            // Ten seconds on: the points are 20 + 1.6 a second, nobody having spent any yet (the first muster is at six seconds, 22 points).
            var m = new LiveMatch(Tempo(Side.Us));
            for (int t = 0; t < 100; t++) m.Step();
            Assert.AreEqual(20 + 1.6 * 5, m.State.Cp[(int)Side.Us], 1e-9, "the player's points are not his income");

            // Played out: the player's side raises no squad he did not buy, and his points only grow; the other side raises its own.
            int usSquads = m.State.Squads.Count(q => q.Side == Side.Us), vcSquads = m.State.Squads.Count(q => q.Side == Side.Vc);
            double cp = m.State.Cp[(int)Side.Us];
            for (int t = 0; t < 1200 && !m.State.Over; t++)
            {
                m.Step();
                Assert.GreaterOrEqual(m.State.Cp[(int)Side.Us], cp, $"tick {m.State.Tick}: the player's points were spent for him");
                cp = m.State.Cp[(int)Side.Us];
            }
            Assert.AreEqual(usSquads, m.State.Squads.Count(q => q.Side == Side.Us), "the player's side raised a squad by itself");
            Assert.Greater(m.State.Squads.Count(q => q.Side == Side.Vc), vcSquads, "the computer's side raised nothing");

            // With no player, both sides raise their own, as before.
            var auto = new LiveMatch(Tempo(null));
            int before = auto.State.Squads.Count(q => q.Side == Side.Us);
            for (int t = 0; t < 600 && !auto.State.Over; t++) auto.Step();
            Assert.Greater(auto.State.Squads.Count(q => q.Side == Side.Us), before);
        }

        [Test]
        public void Squad_smoke_is_off_by_default_and_when_on_a_stalled_bound_pops_one_canister()
        {
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).SquadSmoke);
            var (_, off) = FragTrace(1, false);
            // The computer's plans buy no cards, so without the rule there is no smoke at all.
            Assert.IsFalse(off.State.Events.Any(e => e.Kind == EventKind.AreaStart));

            int smokes = 0;
            for (int seed = 1; seed <= 6; seed++)
            {
                var (_, m) = FragTrace(seed, false, true);
                var starts = m.State.Events.Where(e => e.Kind == EventKind.AreaStart).ToList();
                smokes += starts.Count;
                foreach (var e in starts) Assert.GreaterOrEqual(e.Tick, m.State.ContactTick, "smoke before first contact");
                Assert.IsTrue(m.State.Squads.All(q => q.Smoke >= 0 && q.Smoke <= Tune.SquadSmokeCarried), "a squad threw more than it carried");
            }
            Assert.Greater(smokes, 0, "no squad ever popped smoke");
        }

        // --- Part 2: tactics, behind MatchOptions.Tactics; fortune, behind MatchOptions.Fortune (playtest 9) -----

        private static MatchOptions Drilled(int seed, bool onMap = true, bool tempo = false, bool fortune = false)
        {
            var o = Loaded(seed, onMap, tempo);
            o.Tactics = true;
            o.Fortune = fortune;
            return o;
        }

        /// <summary>Recorded by `tools/simcs/run.sh hash N [frag smoke] drill fieldcraft arms senses gunnery ammo tactics [map]`.</summary>
        [TestCase(1, true, 3538, 674558516u, 1701773530u, 2103794046u, "vc morale broke")]
        [TestCase(7, false, 3341, 1916778477u, 3459103997u, 2175602128u, "us morale broke")]
        public void With_tactics_a_match_is_pinned_and_still_a_pure_function_of_its_seed(
            int seed, bool asTheGame, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).Tactics, "tactics must be off unless asked for");
            var (a, m) = Played(Drilled(seed, asTheGame));
            var (b, _) = Played(Drilled(seed, asTheGame));
            CollectionAssert.AreEqual(a, b, "two runs of the same seed diverged");
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(at100, a[100], "tick 100");
            Assert.AreEqual(at1000, a[1000], "tick 1000");
            Assert.AreEqual(final, a[a.Count - 1], "final tick");
            Assert.AreEqual(reason, m.State.Reason);
        }

        /// <summary>Recorded by `tools/simcs/run.sh hash N [frag smoke] drill fieldcraft arms senses gunnery ammo tactics fortune [map]`.</summary>
        [TestCase(1, true, 2719, 1213913555u, 4008188405u, 4215652832u, "us morale broke")]
        [TestCase(7, false, 2166, 4141358090u, 1407521725u, 535311614u, "vc morale broke")]
        public void With_fortune_a_match_is_pinned_and_still_a_pure_function_of_its_seed(
            int seed, bool asTheGame, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).Fortune, "fortune must be off unless asked for");
            var (a, m) = Played(Drilled(seed, asTheGame, fortune: true));
            var (b, _) = Played(Drilled(seed, asTheGame, fortune: true));
            CollectionAssert.AreEqual(a, b, "two runs of the same seed diverged");
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(at100, a[100], "tick 100");
            Assert.AreEqual(at1000, a[1000], "tick 1000");
            Assert.AreEqual(final, a[a.Count - 1], "final tick");
            Assert.AreEqual(reason, m.State.Reason);
        }

        /// <summary>
        /// The owner, playtest 9: "AI brain needs work: sometimes they still do weird circles, don't fight each
        /// other, weird non-logical positions when fighting", and "too many grenades". A squad fires at an
        /// enemy it has in sight from beyond its weapons' own distance, never from beyond a long shot, and a
        /// long shot all but never kills; in contact a squad moves by halves, so most of its movement is by the
        /// half whose turn it is; a man throws no more grenades than the one he carries (a sapper's or an
        /// engineer's two), a squad waits fifteen seconds between them, and far fewer are thrown.
        /// </summary>
        [Test]
        public void With_tactics_squads_fire_at_sight_move_by_halves_and_throw_fewer_grenades()
        {
            int shots = 0, longShots = 0, longKills = 0, inTurn = 0, outOfTurn = 0, thrownWith = 0, thrownWithout = 0;
            for (int seed = 3; seed <= 6; seed++)
            {
                var plain = new LiveMatch(Loaded(seed, tempo: true));
                while (!plain.State.Over && plain.State.Tick < 3600) plain.Step();
                thrownWithout += plain.State.Events.Count(e => e.Kind == EventKind.GrenadeThrown);

                var m = new LiveMatch(Drilled(seed, tempo: true));
                var st = m.State;
                var threw = new Dictionary<int, int>(); var squadThrew = new Dictionary<int, int>();
                while (!st.Over && st.Tick < 3600)
                {
                    var was = st.Men.Select(x => (x.X, x.Z)).ToArray();
                    int from = st.Events.Count;
                    m.Step();
                    for (int i = from; i < st.Events.Count; i++)
                    {
                        var e = st.Events[i];
                        if (e.Kind == EventKind.GrenadeThrown)
                        {
                            thrownWith++;
                            var by = st.Men[e.Id];
                            threw[e.Id] = threw.GetValueOrDefault(e.Id) + 1;
                            Assert.LessOrEqual(threw[e.Id], by.Weapon == Weapon.Smg ? Tune.TacticsGrenadesClose : Tune.TacticsGrenades, $"seed {seed}: man {e.Id} threw more grenades than he carried");
                            if (squadThrew.TryGetValue(by.Squad, out int at)) Assert.GreaterOrEqual(st.Tick - at, Tune.TacticsThrowGap, $"seed {seed}: squad {by.Squad} threw again too soon");
                            squadThrew[by.Squad] = st.Tick;
                        }
                        if (e.Kind != EventKind.Fire) continue;
                        var a = st.Men[e.Id]; var b = st.Men[e.Target.Value];
                        double d = Combat.Dist(a, b);
                        // (A machine gun's burst on cover it has lost sight of is Senses' own, at its own distance.)
                        Assert.LessOrEqual(d, Tactics.Reach(st, a) + 1e-9, $"seed {seed} tick {st.Tick}: man {e.Id} fired from beyond a long shot");
                        shots++;
                        if (d <= Arms.Of(st, a).Range) continue;
                        longShots++;
                        if (i + 1 < st.Events.Count && st.Events[i + 1].Kind == EventKind.Kill && st.Events[i + 1].Id == b.Id) longKills++;
                    }
                    foreach (var p in st.Men)
                    {
                        if (!p.Alive || p.Id >= was.Length || !Tactics.Bounds(st, st.Squads[p.Squad])) continue;
                        if (Math.Abs(p.X - was[p.Id].X) + Math.Abs(p.Z - was[p.Id].Z) <= 0.01) continue;
                        if (Tactics.Turn(st, st.Squads[p.Squad], p)) inTurn++; else outOfTurn++;
                    }
                }
            }
            Assert.Greater(longShots, 100, "hardly a long shot was fired: squads are not opening fire at sight");
            Assert.Less(longShots, shots, "every shot was a long one");
            Assert.Less(longKills * 20, longShots, "a long shot kills too often to be a long shot");
            Assert.Greater(inTurn, 1000, "hardly anyone moved in a bound");
            Assert.Less(outOfTurn * 3, inTurn, "men move when it is the other half's turn");
            Assert.Greater(thrownWith, 0, "nobody threw a grenade");
            Assert.Less(thrownWith * 5, thrownWithout * 3, "grenades are not much fewer");

            // A long shot is the same shot at the weapon's own distance, less often on its man; a sniper's rifle has none.
            var fresh = Match.Create(Drilled(3));
            var rifle = fresh.Men.First(x => x.Side == Side.Us && x.Weapon == Weapon.M16); var foe = fresh.Men.First(x => x.Side == Side.Vc);
            rifle.Posture = Posture.Crouched; foe.Z = rifle.Z; foe.Cover = -1;
            foe.X = rifle.X + Arms.Of(Weapon.M16).Range;
            double own = Combat.HitChance(fresh, rifle, foe);
            foe.X = rifle.X + Arms.Of(Weapon.M16).Range + 5;
            Assert.AreEqual(Tune.LongHit * own, Combat.HitChance(fresh, rifle, foe), 1e-12);
            Assert.AreEqual(Arms.Of(Weapon.M16).Range + Tune.LongMost, Tactics.Reach(fresh, rifle), 1e-12);
            rifle.Weapon = Weapon.Sniper;
            Assert.AreEqual(Arms.Of(Weapon.Sniper).Range, Tactics.Reach(fresh, rifle), 1e-12);
            Assert.AreEqual(Arms.Of(Weapon.M16).Range, Tactics.Reach(Match.Create(Loaded(3)), fresh.Men.First(x => x.Weapon == Weapon.M16)), 1e-12, "a long shot without the rule");
        }

        /// <summary>
        /// The owner, playtest 9: "distances for snipers need tailoring: they should be able to be safe but prone
        /// to suppression from the gunner/squads and other snipers". A sniper team fights from 26 m, inside a
        /// rifle's long shot and outside its own distance; a machine gun and another sniper leave whoever they
        /// would have fired at for a sniper who has just fired, and a rifleman does when he has nobody nearer.
        /// And a man passing through cover goes for an enemy four metres off; one with a place in it waits.
        /// </summary>
        [Test]
        public void With_tactics_a_sniper_keeps_his_distance_and_draws_fire_and_a_man_in_cover_knows_what_to_do()
        {
            var lm = new LiveMatch(Drilled(3, tempo: true));
            lm.Issue(Command.Buy(Side.Us, "us-sniper", 0, 0));
            lm.Step();
            var team = lm.State.Squads.Single(q => q.Card == "us-sniper");
            Assert.AreEqual(Tune.SniperReach, team.Reach);
            Assert.Less(Arms.Of(Weapon.M16).Range, team.Reach, "a sniper inside a rifle's own distance");
            Assert.Less(team.Reach, Arms.Of(Weapon.M16).Range + Tune.LongMost, "a sniper beyond a rifle's long shot");
            Assert.IsTrue(Tactics.FarTeam(lm.State, team));
            Assert.IsFalse(Tactics.FarTeam(lm.State, lm.State.Squads.First(q => q.Card == "us-rifle")));
            team.Threat = lm.State.Squads.First(q => q.Side == Side.Vc).Id; team.ThreatSeen = true;
            team.ThreatGap = team.Reach - Tune.KeepOff - 1;
            Assert.IsTrue(Tactics.Pressed(lm.State, team), "an enemy inside its distance does not press it");
            team.ThreatGap = team.Reach;
            Assert.IsFalse(Tactics.Pressed(lm.State, team));

            var st = Match.Create(Drilled(3));
            var gun = new Man { Weapon = Weapon.M60 }; var man = new Man { Weapon = Weapon.M16 };
            var nearby = new Man { X = 10 }; var far = new Man { X = 25 };
            Assert.IsTrue(Tactics.GoesForSniper(st, gun, nearby), "a machine gun does not go for the sniper");
            Assert.IsFalse(Tactics.GoesForSniper(st, man, nearby), "a rifleman leaves a man he can hit for a sniper he cannot");
            Assert.IsTrue(Tactics.GoesForSniper(st, man, far));
            Assert.IsTrue(Tactics.GoesForSniper(st, man, null));

            foreach (bool rule in new[] { true, false })
            {
                var s2 = Match.Create(rule ? Drilled(3) : Loaded(3));
                var sq = s2.Squads.First(q => q.Side == Side.Us);
                var p = s2.Men.First(x => x.Squad == sq.Id); var e = s2.Men.First(x => x.Side == Side.Vc);
                var c = s2.Cover[0];
                sq.Order = Order.Hold;
                p.X = c.X; p.Z = c.Z; p.Cover = c.Id; p.Pin = 0;
                e.X = p.X + 4; e.Z = p.Z; e.Seen = true;
                foreach (var o in s2.Men) if (o != e && o.Side == Side.Vc) o.X = 44;
                p.Place = -1; p.PlaceCover = -1;
                Assert.AreEqual(rule ? e : null, Fieldcraft.Quarry(s2, p, sq), rule ? "passing through cover, he waited for a man four metres off" : "the baseline's rule changed");
                p.Place = 0; p.PlaceCover = c.Id;
                Assert.IsNull(Fieldcraft.Quarry(s2, p, sq), "a man with a place in cover left it for a man four metres off");
            }
        }

        /// <summary>
        /// The owner, playtest 9: "add randomness", and of snipers killed by squads, "add luck element to it".
        /// Men are drawn as they are raised, the same men for the same seed, each within his spread of the
        /// ordinary man, and all the ordinary man without the rule; a pause is a third longer or shorter, by
        /// chance and by the man; only a long shot is ever lucky, about six in a thousand; the match opens in
        /// either lane, both sides in the same one; and the computer does not raise the same squads in the
        /// same order every time.
        /// </summary>
        [Test]
        public void With_fortune_men_differ_pauses_vary_a_long_shot_may_be_lucky_and_the_opening_is_drawn()
        {
            var plain = Match.Create(Drilled(3));
            Assert.IsTrue(plain.Men.All(x => x.Aim == 1 && x.Nerve == 1 && x.Quick == 1), "men differ without the rule");
            Assert.IsNull(plain.LuckRng, "luck has a stream without the rule");

            var a = Match.Create(Drilled(3, fortune: true)); var b = Match.Create(Drilled(3, fortune: true));
            CollectionAssert.AreEqual(a.Men.Select(x => (x.Aim, x.Nerve, x.Quick)).ToList(), b.Men.Select(x => (x.Aim, x.Nerve, x.Quick)).ToList(), "the same seed raised different men");
            Assert.Greater(a.Men.Select(x => x.Aim).Distinct().Count(), a.Men.Count / 2, "the men are all one man");
            foreach (var x in a.Men)
            {
                Assert.That(x.Aim, Is.InRange(1 - Tune.AimSpread, 1 + Tune.AimSpread));
                Assert.That(x.Nerve, Is.InRange(1 - Tune.NerveSpread, 1 + Tune.NerveSpread));
                Assert.That(x.Quick, Is.InRange(1 - Tune.QuickSpread, 1 + Tune.QuickSpread));
            }
            // A steady man takes less of a burst than a nervous one; a good shot hits oftener than a poor one.
            var steady = new Man { Nerve = 1.2 }; var nervous = new Man { Nerve = 0.8 };
            Assert.Less(Fortune.Shaken(steady, 0.2), Fortune.Shaken(nervous, 0.2));
            var shot = a.Men.First(x => x.Side == Side.Us); var at = a.Men.First(x => x.Side == Side.Vc);
            at.X = shot.X + 10; at.Z = shot.Z; at.Cover = -1; shot.Posture = Posture.Crouched;
            shot.Aim = 1; double ordinary = Combat.HitChance(a, shot, at);
            shot.Aim = 1.25; Assert.AreEqual(1.25 * ordinary, Combat.HitChance(a, shot, at), 1e-12);

            var pauses = new HashSet<int>();
            for (int i = 0; i < 40; i++)
            {
                int t = Fortune.Pause(a, a.Men[i % a.Men.Count], 100);
                Assert.That(t, Is.InRange((int)(100 * (1 - Tune.PauseSpread) / (1 + Tune.QuickSpread)), (int)(100 * (1 + Tune.PauseSpread) / (1 - Tune.QuickSpread)) + 1));
                pauses.Add(t);
            }
            Assert.Greater(pauses.Count, 10, "every pause is the same pause");

            // Luck is a long shot's: never inside the weapon's own distance, and seldom beyond it.
            int lucky = 0;
            at.X = shot.X + Arms.Of(a, shot).Range - 1;
            for (int i = 0; i < 2000; i++) Assert.IsFalse(Fortune.Lucky(a, shot, at), "a lucky shot inside the weapon's own distance");
            at.X = shot.X + Arms.Of(a, shot).Range + 5;
            for (int i = 0; i < 20000; i++) if (Fortune.Lucky(a, shot, at)) lucky++;
            Assert.That(lucky, Is.InRange(20000 * Tune.LuckyHit * 0.6, 20000 * Tune.LuckyHit * 1.5), "luck is not what its number says");

            // The opening: both sides in one lane, and not the same lane every match. And what the computer raises next is drawn.
            var lanes = new HashSet<int>(); var raised = new HashSet<string>();
            for (int seed = 1; seed <= 16; seed++)
            {
                var m = new LiveMatch(Drilled(seed, tempo: true, fortune: true));
                Assert.AreEqual(1, m.State.Squads.Select(q => q.Lane).Distinct().Count(), $"seed {seed}: the two sides opened in different lanes");
                lanes.Add(m.State.Squads[0].Lane);
                Assert.AreEqual("us-rifle", m.State.Squads.First(q => q.Side == Side.Us).Card, "a side's first squad is not its line squad");
                while (!m.State.Over && m.State.Tick < 1200) m.Step();
                raised.Add(string.Join(",", m.State.Squads.Where(q => q.Side == Side.Vc).Take(4).Select(q => q.Card)));
            }
            Assert.AreEqual(2, lanes.Count, "every match opened in the same lane");
            Assert.Greater(raised.Count, 3, "the computer raises the same squads in the same order every match");
            Assert.AreEqual(0, new LiveMatch(Drilled(5, tempo: true)).State.Squads[0].Lane, "without the rule a match opens in the near lane");
        }

        // --- Part 2: fire control, behind MatchOptions.FireControl (the owner, 2026-10-09: gunplay and AI smartness) -----

        private static MatchOptions Directed(int seed, bool onMap = true, bool tempo = false)
        {
            var o = Drilled(seed, onMap, tempo, fortune: true);
            o.FireControl = true;
            return o;
        }

        /// <summary>Recorded by `tools/simcs/run.sh hash N [frag smoke] drill fieldcraft arms senses gunnery ammo tactics fortune firecontrol [map]`.</summary>
        [TestCase(1, true, 5070, 4188068015u, 3040016184u, 1731903378u, "us morale broke")]
        [TestCase(7, false, 4220, 2037419726u, 1865812131u, 1248060381u, "vc morale broke")]
        public void With_fire_control_a_match_is_pinned_and_still_a_pure_function_of_its_seed(
            int seed, bool asTheGame, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).FireControl, "fire control must be off unless asked for");
            var (a, m) = Played(Directed(seed, asTheGame));
            var (b, _) = Played(Directed(seed, asTheGame));
            CollectionAssert.AreEqual(a, b, "two runs of the same seed diverged");
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(at100, a[100], "tick 100");
            Assert.AreEqual(at1000, a[1000], "tick 1000");
            Assert.AreEqual(final, a[a.Count - 1], "final tick");
            Assert.AreEqual(reason, m.State.Reason);
        }

        /// <summary>A squad has an enemy squad in sight, as of now (what <see cref="Senses.Look"/> would have written).</summary>
        private static void Know(SimState st, Squad sq, int enemy)
        {
            while (sq.KnownAt.Count <= enemy) sq.KnownAt.Add(Senses.Never);
            sq.KnownAt[enemy] = st.Tick;
        }

        /// <summary>
        /// A state to put a shot together in: an American rifle squad at x -10 in the near lane with every
        /// VC squad in sight, and every VC man off at the far end until the test brings him up.
        /// </summary>
        private static (SimState st, Squad sq, List<Man> own, List<Man> foes) Range(bool rule)
        {
            var st = Match.Create(rule ? Directed(3) : Drilled(3, fortune: true));
            var sq = st.Squads.First(q => q.Side == Side.Us && q.Card == "us-rifle");
            var own = Squads.Roster(st, sq.Id);
            double z = Tune.Lanes[sq.Lane];
            for (int i = 0; i < own.Count; i++) { own[i].X = -10 - i; own[i].Z = z; own[i].Posture = Posture.Crouched; own[i].Cover = -1; own[i].Pin = 0; }
            sq.AnchorX = -10; sq.AnchorZ = z;
            foreach (var o in st.Men.Where(x => x.Side == Side.Us && x.Squad != sq.Id)) o.X = -44;
            var foes = st.Men.Where(x => x.Side == Side.Vc).ToList();
            foreach (var e in foes) { e.X = 44; e.Z = z; e.Cover = -1; e.Posture = Posture.Crouched; e.Pin = 0; e.Weapon = Weapon.Ak; e.Seen = true; }
            foreach (var q in st.Squads.Where(q => q.Side == Side.Vc)) Know(st, sq, q.Id);
            sq.ThreatSeen = true;
            return (st, sq, own, foes);
        }

        /// <summary>
        /// The owner, 2026-10-09: "focus on firing/gunplay mechanics and AI smartness". A man fires at the
        /// enemy he is likeliest to hit, not the nearest; at one his squad is not already on; he stays on his
        /// man; a machine gun moves on from the man it has just fired at; a sniper looks for a crew. Without
        /// the rule he fires at the nearest, as he did.
        /// </summary>
        [Test]
        public void With_fire_control_a_man_fires_at_the_man_worth_most_and_a_squad_spreads_its_fire()
        {
            foreach (bool rule in new[] { true, false })
            {
                var (st, sq, own, foes) = Range(rule);
                Man a = own[0], mate = own[1], near = foes[0], far = foes[1];
                // The nearer man flat behind a wall, the further one on his feet in the open.
                var wall = st.Cover.First(c => c.Kind == CoverKind.Sandbag);
                near.X = 2; near.Posture = Posture.Prone; near.Cover = wall.Id;
                far.X = 5; far.Posture = Posture.Standing;
                Assert.Greater(Combat.HitChance(st, a, far), 2 * Combat.HitChance(st, a, near));
                Assert.AreEqual(rule ? far : near, Senses.PickTarget(st, a), rule ? "he fired at the nearest man, flat behind a wall, with one on his feet in the open" : "without the rule he no longer fires at the nearest");
                if (!rule) continue;

                // Two men alike, a pace apart: the one nobody of his squad is on; with nobody on either, the one he is on.
                near.Posture = far.Posture = Posture.Crouched; near.Cover = -1;
                near.X = far.X = 2; far.Z = near.Z + 1;
                Assert.AreEqual(near, Senses.PickTarget(st, a));
                mate.Mark = near.Id; mate.FiredAt = st.Tick;
                Assert.AreEqual(far, Senses.PickTarget(st, a), "the whole squad fires at one man");
                mate.Mark = far.Id;
                Assert.AreEqual(near, Senses.PickTarget(st, a));
                mate.FiredAt = st.Tick - Tune.MarkTicks - 1;
                a.Mark = far.Id;
                Assert.AreEqual(far, Senses.PickTarget(st, a), "he changes his man with every shot");
                // A machine gun traverses: on from the man it has just fired at.
                a.Weapon = Weapon.M60;
                Assert.AreEqual(near, Senses.PickTarget(st, a), "a machine gun stays on one man");
                // A sniper looks for the crews.
                a.Weapon = Weapon.Sniper; a.Mark = -1;
                far.X = 6; far.Weapon = Weapon.Rpd;
                Assert.AreEqual(far, Senses.PickTarget(st, a), "a sniper fires at a rifleman with a machine gun to fire at");

                // Covering fire: a rifleman's round with next to no chance, fired slowly unless somebody of his is moving under it.
                a.Weapon = Weapon.M16;
                Assert.IsTrue(FireControl.Covering(st, a, Tune.AimedFloor * 0.5));
                Assert.IsFalse(FireControl.Covering(st, a, Tune.AimedFloor * 2), "an aimed shot is covering fire");
                Assert.IsFalse(FireControl.Covering(st, new Man { Weapon = Weapon.M60 }, 0), "a machine gun's burst is slowed");
                Assert.IsFalse(FireControl.Covering(st, new Man { Weapon = Weapon.Sniper }, 0), "a sniper fires covering fire");
                sq.Task = SquadTask.Firefight; sq.Prep = false;
                foreach (var q in st.Squads) if (q != sq) q.Task = SquadTask.March;
                Assert.IsFalse(FireControl.Wanted(st, a), "covering fire is wanted with nobody moving");
                sq.Prep = true;
                Assert.IsTrue(FireControl.Wanted(st, a), "a squad working up to going in does not pour it on");
                sq.Prep = false; sq.Task = SquadTask.Close;
                Assert.IsTrue(FireControl.Wanted(st, a));
                sq.Task = SquadTask.Firefight;
                var beside = st.Squads.First(q => q.Side == Side.Us && q != sq);
                beside.Lane = sq.Lane; beside.AnchorX = sq.AnchorX - 5; beside.Task = SquadTask.Assault;
                Assert.IsTrue(FireControl.Wanted(st, a), "a squad going in beside it gets no covering fire");
                foreach (var o in st.Men.Where(x => x.Squad == beside.Id)) o.Alive = false;
                Assert.IsFalse(FireControl.Wanted(st, a), "a dead squad's assault is still being covered");
            }

            // Kept down: a man does not begin to get over fire while it is still landing by him.
            var held = Match.Create(Directed(3));
            var man = held.Men[0];
            Combat.ApplyPin(held, man, 0.5);
            double pinned = man.Pin;
            Assert.AreEqual(held.Tick, man.UnderAt);
            Combat.Relax(held, man);
            Assert.AreEqual(pinned, man.Pin, "his head came up with the rounds still landing");
            held.Tick += Tune.PinHold + 1;
            Combat.Relax(held, man);
            Assert.Less(man.Pin, pinned, "he never gets over it");
            var loose = Match.Create(Drilled(3, fortune: true));
            Combat.ApplyPin(loose, loose.Men[0], 0.5);
            pinned = loose.Men[0].Pin;
            Combat.Relax(loose, loose.Men[0]);
            Assert.Less(loose.Men[0].Pin, pinned, "without the rule a man is held down");
        }

        /// <summary>
        /// A squad goes in when the enemy in front of it is beaten down man by man, never onto a machine gun
        /// that is still up and never outnumbered, or when it has him three to one; going in, it keeps going
        /// unless it is stopped; stopped, it waits before it tries again. A rocket is not fired at one rifleman
        /// by himself, nor anything that bursts where it would catch its own side.
        /// </summary>
        [Test]
        public void With_fire_control_a_squad_goes_in_on_a_beaten_enemy_and_a_burst_is_for_a_group()
        {
            var (st, sq, own, foes) = Range(true);
            for (int i = 0; i < 3; i++) foes[i].X = 2 + i;
            Assert.AreEqual(5, own.Count);
            FireControl.Front(st, sq, out int enemies, out int down, out bool gunUp);
            Assert.AreEqual((3, 0, false), (enemies, down, gunUp));
            Assert.IsFalse(FireControl.GoesIn(st, sq, own, 0), "it went in on three men with their heads up");
            foes[0].Pin = 0.5;
            Assert.IsFalse(FireControl.GoesIn(st, sq, own, 0), "one man down in three is not a beaten enemy");
            foes[1].Pin = 0.5;
            Assert.IsTrue(FireControl.GoesIn(st, sq, own, 0), "two in three down, and it did not go in");
            Assert.IsFalse(FireControl.GoesIn(st, sq, own, Tune.PinDrop), "it went in with its own heads down");
            foes[2].Weapon = Weapon.Rpd;
            Assert.IsFalse(FireControl.GoesIn(st, sq, own, 0), "it went in onto a machine gun that was still up");
            Assert.IsFalse(FireControl.GoesIn(st, sq, own, 0, committed: true), "going in, it did not stop for a machine gun");
            foes[2].Reloading = 20;
            Assert.IsTrue(FireControl.GoesIn(st, sq, own, 0), "a machine gun changing its belt stopped it");
            foes[2].Weapon = Weapon.Ak; foes[2].Reloading = 0;
            // Going in already, it does not stop because their heads have come up; outnumbered, it never starts.
            foes[0].Pin = foes[1].Pin = 0;
            Assert.IsTrue(FireControl.GoesIn(st, sq, own, 0, committed: true), "half way in, it stopped because a head came up");
            foes[0].Pin = foes[1].Pin = foes[2].Pin = 0.5;
            Assert.IsFalse(FireControl.GoesIn(st, sq, own.Take(2).ToList(), 0), "two men went in on three");
            Assert.IsTrue(FireControl.GoesIn(st, sq, own.Take(2).ToList(), 0, committed: true), "two men half way in on three stopped");
            // Stopped once, it waits.
            sq.StalledAt = st.Tick;
            Assert.IsFalse(FireControl.GoesIn(st, sq, own, 0), "stopped a moment ago, it went in again");
            sq.StalledAt = st.Tick - Tune.AssaultAgain;
            Assert.IsTrue(FireControl.GoesIn(st, sq, own, 0));
            // Three to one, it goes in whatever their heads are doing.
            foes[1].X = foes[2].X = 44; foes[0].Pin = 0; sq.StalledAt = st.Tick;
            Assert.IsTrue(FireControl.GoesIn(st, sq, own, 0), "five men did not go in on one");
            // And it means to go in, and works up to it, only when it is not outnumbered and has had its breath.
            sq.Task = SquadTask.Firefight; sq.StalledAt = Senses.Never;
            Assert.IsTrue(FireControl.MeansTo(st, sq, own, 0, Tune.FirefightMin));
            Assert.IsFalse(FireControl.MeansTo(st, sq, own, 0, Tune.FirefightMin - 1));
            Assert.IsFalse(FireControl.MeansTo(st, sq, own, Tune.PinDrop, Tune.FirefightMin));
            foes[1].X = 3; foes[2].X = 4;
            Assert.IsFalse(FireControl.MeansTo(st, sq, own.Take(2).ToList(), 0, Tune.FirefightMin), "outnumbered, it works up to an assault");

            // Bursts. A rocket: not at one rifleman by himself; at two; at a machine gun by itself; never where it would catch its own.
            var (bt, bsq, mine, theirs) = Range(true);
            var rocket = theirs[0]; rocket.Weapon = Weapon.Rpg; rocket.X = 10;
            foreach (var o in mine) { o.X = -44; o.Seen = true; }
            var rpg = Arms.Of(Weapon.Rpg);
            mine[0].X = -5;
            Assert.IsNull(FireControl.Burst(bt, rocket, rpg), "a rocket at one rifleman by himself");
            mine[1].X = -6;
            Assert.IsNotNull(FireControl.Burst(bt, rocket, rpg), "two men together are not worth a rocket");
            mine[1].X = -44; mine[0].Weapon = Weapon.M60;
            Assert.AreEqual(mine[0], FireControl.Burst(bt, rocket, rpg), "a machine gun by itself is not worth a rocket");
            theirs[1].X = -3;
            Assert.IsNull(FireControl.Burst(bt, rocket, rpg), "a rocket onto its own side");
            // An M79: also at a man by himself whom bullets cannot reach, behind his cover.
            var m79 = Arms.Of(Weapon.M79);
            var grenadier = mine[2]; grenadier.Weapon = Weapon.M79; grenadier.X = -10;
            // (The rocket man himself is a crew, and worth a round: he goes back out of reach.)
            rocket.X = 44; theirs[1].X = 44; theirs[2].X = 5;
            Assert.IsNull(FireControl.Burst(bt, grenadier, m79), "an M79 round at one rifleman in the open");
            theirs[2].Cover = bt.Cover[0].Id;
            Assert.AreEqual(theirs[2], FireControl.Burst(bt, grenadier, m79), "an M79 does not dig a man out of his cover");
            // A grenade: at a man in cover, and never where it would catch the thrower's own side.
            mine[3].X = -3;
            Assert.AreEqual(theirs[2], FireControl.Throw(bt, mine[3]));
            mine[4].X = 3;
            Assert.IsNull(FireControl.Throw(bt, mine[3]), "a grenade thrown onto his own side");
        }

        /// <summary>
        /// And in whole matches, as the game plays them: with the rule a squad's fire is spread (fewer of its
        /// shots go at a man one of its own has just fired at), and fire that is kept up keeps heads down
        /// (men spend more of a fight down).
        /// </summary>
        [Test]
        public void With_fire_control_fire_is_spread_and_men_are_kept_down()
        {
            double[] piled = new double[2], shots = new double[2], down = new double[2], seconds = new double[2];
            for (int rule = 0; rule < 2; rule++)
                for (int seed = 3; seed <= 6; seed++)
                {
                    var m = new LiveMatch(rule == 1 ? Directed(seed, tempo: true) : Drilled(seed, tempo: true, fortune: true));
                    var st = m.State;
                    var last = new Dictionary<(int, int), int>();
                    while (!st.Over && st.Tick < 2400)
                    {
                        int from = st.Events.Count;
                        m.Step();
                        for (int i = from; i < st.Events.Count; i++)
                        {
                            var e = st.Events[i];
                            if (e.Kind != EventKind.Fire) continue;
                            var key = (st.Men[e.Id].Squad, e.Target.Value);
                            shots[rule]++;
                            if (last.TryGetValue(key, out int at) && st.Tick - at <= 40) piled[rule]++;
                            last[key] = st.Tick;
                        }
                        if (st.Phase != Phase.Fight) continue;
                        foreach (var p in st.Men)
                        {
                            if (!p.Alive) continue;
                            var q = st.Squads[p.Squad];
                            if (!q.ThreatSeen || (q.Task != SquadTask.Firefight && q.Task != SquadTask.Close && q.Task != SquadTask.Assault)) continue;
                            seconds[rule]++;
                            if (p.Pin >= Tune.PinDrop) down[rule]++;
                        }
                    }
                }
            Assert.Greater(shots[1], 1000);
            Assert.Less(piled[1] / shots[1], 0.8 * piled[0] / shots[0], "a squad's fire is no more spread than it was");
            Assert.Greater(down[1] / seconds[1], 1.3 * down[0] / seconds[0], "men are kept down no more than they were");
        }

        // --- Part 2: ballistics, behind MatchOptions.Ballistics (the owner, 2026-10-10: "yes i want the new shooting rule") -----

        private static MatchOptions Flown(int seed, bool onMap = true, bool tempo = false)
        {
            var o = Directed(seed, onMap, tempo);
            o.Ballistics = true;
            return o;
        }

        /// <summary>Recorded by `tools/simcs/run.sh hash N [frag smoke] drill fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics [map]`.</summary>
        [TestCase(1, true, 3493, 4188068015u, 3149690676u, 2457736883u, "us morale broke")]
        [TestCase(7, false, 4414, 2037419726u, 1815324906u, 3760706757u, "vc morale broke")]
        public void With_ballistics_a_match_is_pinned_and_still_a_pure_function_of_its_seed(
            int seed, bool asTheGame, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).Ballistics, "ballistics must be off unless asked for");
            var (a, m) = Played(Flown(seed, asTheGame));
            var (b, _) = Played(Flown(seed, asTheGame));
            CollectionAssert.AreEqual(a, b, "two runs of the same seed diverged");
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(at100, a[100], "tick 100");
            Assert.AreEqual(at1000, a[1000], "tick 1000");
            Assert.AreEqual(final, a[a.Count - 1], "final tick");
            Assert.AreEqual(reason, m.State.Reason);
        }

        /// <summary>
        /// The owner, 2026-10-10: "make it as realistic as possible", and "yes i want the new shooting rule". A
        /// round that misses its man may hit the man who is where it goes: one of the enemy within 0.6 m of its
        /// line over its last four metres, with the chance the firer would have had firing at him; never the man
        /// it was fired at, never one of the firer's own side, never a man off its line or short of its reach.
        /// A burst's rounds share that chance, each a pace short of the next.
        /// </summary>
        [Test]
        public void With_ballistics_a_round_that_misses_may_hit_the_man_behind_its_target()
        {
            var st = Match.Create(Flown(3));
            var sq = st.Squads.First(q => q.Side == Side.Us && q.Card == "us-rifle");
            var a = st.Men.First(x => x.Squad == sq.Id);
            double z = Tune.Lanes[sq.Lane];
            foreach (var o in st.Men.Where(x => x != a)) { o.X = o.Side == Side.Us ? -44 : 44; o.Z = z; o.Cover = -1; o.Posture = Posture.Crouched; o.Pin = 0; }
            a.X = -10; a.Z = z; a.Posture = Posture.Crouched; a.Cover = -1; a.Pin = 0; a.Weapon = Weapon.M16;
            var foes = st.Men.Where(x => x.Side == Side.Vc).ToList();
            Man target = foes[0], behind = foes[1];
            target.X = 2; behind.X = 3.5;
            int Found(int shots, int strays, int of, double landX, double landZ)
            {
                int n = 0, before = st.Events.Count;
                for (int k = 0; k < shots; k++)
                {
                    foreach (var o in st.Men) o.Alive = true;
                    Ballistics.Strays(st, a, target, strays, of, landX, landZ);
                    if (!behind.Alive) n++;
                }
                Assert.AreEqual(n, st.Events.Skip(before).Count(e => e.Kind == EventKind.Stray && e.Target == behind.Id), "a stray that killed was not written, or one was written that did not kill");
                Assert.IsTrue(st.Events.Skip(before).Where(e => e.Kind == EventKind.Stray).All(e => e.Id == a.Id && e.Target != target.Id), "a stray round found the man it was fired at");
                return n;
            }
            // The round comes down two and a half metres past its man, and there is a man a pace and a half behind him.
            double chance = Combat.HitChance(st, a, behind) * Tune.StrayShare;
            Assert.Greater(chance, 0.005);
            int hits = Found(4000, 1, 1, target.X + 2.5, z);
            Assert.That(hits, Is.InRange(4000 * chance * 0.6, 4000 * chance * 1.5), "a stray round's chance is not the chance of a round fired at him");
            // A burst of five shares it: for a man all five rounds pass (a pace in front of the one fired at; the
            // rounds come down from 0.1 m to 2.5 m past him), about as many, not five times as many.
            behind.X = target.X - 1;
            double shared = Combat.HitChance(st, a, behind) * Tune.StrayShare;
            int burst = Found(4000, 5, 5, target.X + 2.5, z);
            Assert.That(burst, Is.InRange(4000 * shared * 0.6, 4000 * shared * 1.5), "a burst's stray rounds do not share one chance");
            behind.X = 3.5;
            // Off its line; short of where it is still at a man's height; and behind where it comes down.
            behind.Z = z + Tune.StrayWidth + 0.3;
            Assert.AreEqual(0, Found(2000, 1, 1, target.X + 2.5, z), "a man off the round's line");
            behind.Z = z; behind.X = target.X + 2.5 - Tune.StrayReach - 0.5;
            Assert.AreEqual(0, Found(2000, 1, 1, target.X + 2.5, z), "a man short of where the round comes down to a man's height");
            behind.X = target.X + 3.5;
            Assert.AreEqual(0, Found(2000, 1, 1, target.X + 2.5, z), "a man beyond where the round came down");
            // Never one of his own side.
            behind.X = 44;
            var friend = st.Men.First(x => x.Side == Side.Us && x != a);
            friend.X = 3.5; friend.Z = z;
            for (int k = 0; k < 2000; k++) Ballistics.Strays(st, a, target, 1, 1, target.X + 2.5, z);
            Assert.IsTrue(friend.Alive, "a stray round killed a man of the firer's own side");
        }

        /// <summary>
        /// And in whole matches, as the game plays them: without the rule no round finds anybody it was not fired
        /// at; with it some do, each written before the death it made, and they are a small part of the killing.
        /// </summary>
        [Test]
        public void With_ballistics_stray_rounds_kill_a_few_and_only_with_the_rule()
        {
            int strays = 0, shot = 0;
            for (int seed = 3; seed <= 8; seed++)
            {
                var plain = new LiveMatch(Directed(seed, tempo: true));
                while (!plain.State.Over && plain.State.Tick < 3600) plain.Step();
                Assert.AreEqual(0, plain.State.Events.Count(e => e.Kind == EventKind.Stray), "a stray round without the rule");

                var m = new LiveMatch(Flown(seed, tempo: true));
                var st = m.State;
                while (!st.Over && st.Tick < 3600) m.Step();
                for (int i = 0; i < st.Events.Count; i++)
                {
                    var e = st.Events[i];
                    if (e.Kind == EventKind.Fire && i + 1 < st.Events.Count && st.Events[i + 1].Kind == EventKind.Kill && st.Events[i + 1].Id == e.Target) shot++;
                    if (e.Kind != EventKind.Stray) continue;
                    strays++;
                    Assert.AreEqual(EventKind.Kill, st.Events[i + 1].Kind, "a stray round with no death after it");
                    Assert.AreEqual(e.Target, st.Events[i + 1].Id);
                    Assert.AreNotEqual(st.Men[e.Id].Side, st.Men[e.Target.Value].Side, "a stray round killed a man of the firer's side");
                }
            }
            Assert.Greater(strays, 0, "no round ever found a man it was not fired at");
            Assert.Less(strays * 3, shot, "stray rounds do a third of the killing or more");
        }

        // --- Part 2: a post up high, behind MatchOptions.Vantage (the owner, 2026-10-10: the watchtower) -----

        private static MatchOptions Posted(int seed, bool onMap = true, bool tempo = false, bool computer = false)
        {
            var o = Flown(seed, onMap, tempo);
            o.Vantage = true;
            o.ComputerPosts = computer;
            return o;
        }

        /// <summary>Recorded by `tools/simcs/run.sh hash N [frag smoke] drill fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics vantage aiposts [map]`.</summary>
        [TestCase(1, true, 4236, 2018994123u, 1740777576u, 1179344068u, "vc morale broke")]
        [TestCase(7, false, 4008, 1364659758u, 4075419781u, 3668849121u, "vc morale broke")]
        public void With_vantage_a_match_is_pinned_and_still_a_pure_function_of_its_seed(
            int seed, bool asTheGame, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).Vantage, "vantage must be off unless asked for");
            Assert.AreEqual(0, Match.Create(new MatchOptions { Seed = 1 }).Posts.Count, "a post without the rule");
            var (a, m) = Played(Posted(seed, asTheGame, computer: true));
            var (b, _) = Played(Posted(seed, asTheGame, computer: true));
            CollectionAssert.AreEqual(a, b, "two runs of the same seed diverged");
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(at100, a[100], "tick 100");
            Assert.AreEqual(at1000, a[1000], "tick 1000");
            Assert.AreEqual(final, a[a.Count - 1], "final tick");
            Assert.AreEqual(reason, m.State.Reason);
            Assert.IsTrue(m.State.Events.Any(e => e.Kind == EventKind.Posted), "the computer's Americans never manned their tower");
        }

        /// <summary>
        /// With the rule on and nobody sent to the post, a match is the match it was: the same men in the same
        /// places at the same end. (The computer does not man its tower unless it is told to: ComputerPosts.)
        /// </summary>
        [Test]
        public void With_vantage_and_nobody_posted_a_match_is_what_it_was()
        {
            for (int seed = 3; seed <= 5; seed++)
            {
                var plain = new LiveMatch(Flown(seed, tempo: true));
                while (!plain.State.Over && plain.State.Tick < plain.Cap) plain.Step();
                var m = new LiveMatch(Posted(seed, tempo: true));
                while (!m.State.Over && m.State.Tick < m.Cap) m.Step();
                Assert.AreEqual(plain.State.Tick, m.State.Tick, $"seed {seed}: it ended at another tick");
                Assert.AreEqual(plain.State.Reason, m.State.Reason);
                Assert.AreEqual(plain.State.Men.Count, m.State.Men.Count);
                for (int i = 0; i < m.State.Men.Count; i++)
                {
                    Assert.AreEqual(plain.State.Men[i].X, m.State.Men[i].X, $"seed {seed}: man {i}");
                    Assert.AreEqual(plain.State.Men[i].Alive, m.State.Men[i].Alive, $"seed {seed}: man {i}");
                }
                Assert.AreEqual(0, m.State.Events.Count(e => e.Kind == EventKind.Posted));
                // And without the rule nobody can be sent anywhere: the points stay.
                var none = new LiveMatch(Flown(seed, tempo: true));
                none.State.Cp[0] = 100;
                none.Issue(Command.BuyTo(Side.Us, "us-sniper", 0));
                none.Step();
                Assert.IsFalse(none.Log[0].Accepted, "a squad sent to a post without the rule");
                Assert.GreaterOrEqual(none.State.Cp[0], 100, "and it was paid for");
            }
        }

        /// <summary>
        /// The owner, 2026-10-10: "add an option to select (with lane selector) certain objectives per map to
        /// place certain soldiers that give massive advantage (for this map the watchtower, have a climb animation
        /// for gunners, snipers and rifle squads). Gunner very effective at medium, close range. Sniper covers all
        /// maps. Rifles good short, medium. Big positional and height advantage should reflect in game
        /// effectiveness." A sniper team sent to the firebase's watchtower walks to its ladder, goes up it a man
        /// at a time, and from up there sees and shoots as nobody on the ground can; nothing on the ground
        /// reaches it but a sniper's rifle and a rocket; told to advance, it comes down and is a squad again.
        /// </summary>
        [Test]
        public void With_vantage_a_squad_sent_to_the_tower_climbs_it_and_fights_from_up_there()
        {
            var o = Posted(3, tempo: true);
            o.Player = Side.Us;
            var m = new LiveMatch(o);
            var st = m.State;
            Assert.AreEqual(1, st.Posts.Count, "the firebase has one post: its watchtower");
            var post = st.Posts[0];
            Assert.AreEqual(Side.Us, post.Side);

            // Who may go up it: the side whose ground it is on, with a squad a post takes, one at a time.
            Assert.AreEqual("not ours", Vantage.Blocked(st, Side.Vc, "vc-marksman", 0));
            Assert.AreEqual("not for a post", Vantage.Blocked(st, Side.Us, "us-mortar", 0));
            foreach (string card in new[] { "us-rifle", "us-mg", "us-sniper" }) Assert.IsNull(Vantage.Blocked(st, Side.Us, card, 0), card);
            st.Cp[0] = 100;
            m.Issue(Command.BuyTo(Side.Us, "us-mortar", 0));
            m.Step();
            Assert.IsFalse(m.Log[m.Log.Count - 1].Accepted, "a mortar team was sent up the tower");
            Assert.GreaterOrEqual(st.Cp[0], 100, "and it was paid for");
            m.Issue(Command.BuyTo(Side.Us, "us-sniper", 0));
            m.Step();
            Assert.IsTrue(m.Log[m.Log.Count - 1].Accepted, "the sniper team was not taken");
            var sq = st.Squads[st.Squads.Count - 1];
            Assert.AreEqual("us-sniper", sq.Card);
            Assert.AreEqual(0, sq.Post);
            Assert.AreEqual("manned", Vantage.Blocked(st, Side.Us, "us-mg", 0), "two squads for one post");
            var men = Squads.Roster(st, sq.Id).ToList();
            Assert.AreEqual(2, men.Count);

            // Up the ladder: a man at a time, three seconds apart, and nobody fires from it.
            int firstUp = -1, secondUp = -1;
            for (int t = 0; t < 900 && secondUp < 0; t++)
            {
                int from = st.Events.Count;
                var onLadder = men.Where(x => x.Climb != 0).Select(x => x.Id).ToList();
                m.Step();
                for (int i = from; i < st.Events.Count; i++)
                    if (st.Events[i].Kind == EventKind.Fire) Assert.IsFalse(onLadder.Contains(st.Events[i].Id), "a man fired from the ladder");
                foreach (var x in men) Assert.That(x.High, Is.InRange(0.0, post.Height), "a man above the platform or under the ground");
                int up = men.Count(Vantage.Up);
                if (up >= 1 && firstUp < 0) firstUp = st.Tick;
                if (up >= 2 && secondUp < 0) secondUp = st.Tick;
            }
            Assert.Greater(firstUp, Tune.ClimbTicks, "a man was up the tower before he could have climbed it");
            Assert.Greater(secondUp, 0, "the second man never got up");
            Assert.GreaterOrEqual(secondUp - firstUp, Tune.LadderGap, "two men on the same rungs");
            Assert.AreEqual(1, st.Events.Count(e => e.Kind == EventKind.Posted && e.Id == sq.Id && e.Target == 0), "the post was not reported manned, once");
            foreach (var x in men)
            {
                Assert.AreEqual(post.Height, x.High);
                Assert.Less(Combat.Dist(x.X, x.Z, post.X, post.Z), 2.0, "a man up the tower is not on its platform");
                Assert.AreNotEqual(Posture.Standing, x.Posture, "he stands up there, over the bags");
            }

            // From up there: his weapon reaches further, and he sees a man flat behind a bank in the other lane.
            var sniper = men.First(x => x.Weapon == Weapon.Sniper);
            var mate = men.First(x => x.Weapon != Weapon.Sniper);
            Assert.AreEqual(Tune.PostSniperReach, Arms.Of(st, sniper).Range, "a sniper's rifle from the tower does not cover the map");
            Assert.AreEqual(Arms.Of(Weapon.M16).Range * Tune.PostReach, Arms.Of(st, mate).Range, 1e-9);
            Assert.AreEqual(Arms.Of(Weapon.M60).Range * Tune.PostGunReach, Vantage.From(Arms.Of(Weapon.M60), Weapon.M60).Range, 1e-9);
            foreach (var x in st.Men.Where(x => x.Side == Side.Vc)) { x.X = 44; x.Z = Tune.Lanes[1]; x.Cover = -1; x.Pin = 0; }
            foreach (var x in st.Men.Where(x => x.Side == Side.Us && x.Squad != sq.Id)) { x.X = -44; x.Z = Tune.Lanes[0]; }
            sniper.Pin = 0; mate.Pin = 0;
            var berm = st.Cover.First(c => c.Kind == CoverKind.Berm && c.Z > 0 && c.X > 20);
            var foe = st.Men.First(x => x.Side == Side.Vc && x.Alive);
            foe.X = berm.X; foe.Z = berm.Z; foe.Cover = berm.Id; foe.Posture = Posture.Prone; foe.Weapon = Weapon.Ak; foe.FiredAt = Senses.Never;
            foe.Trail.Clear();
            var below = st.Men.First(x => x.Side == Side.Us && x.Alive && x.Squad != sq.Id);
            below.X = post.X; below.Z = post.Z; below.Weapon = Weapon.Sniper; below.Posture = Posture.Crouched; below.Pin = 0; below.Cover = -1;
            Assert.IsTrue(Senses.Sees(st, sniper, foe), "the sniper up the tower does not see a man across the map");
            Assert.IsFalse(Senses.Sees(st, below, foe), "a sniper at its foot sees him too");
            Assert.Greater(Combat.HitChance(st, sniper, foe), 3 * Combat.HitChance(st, below, foe), "the tower is no better a place to shoot from");
            // A rifle, at a man flat in cover eighteen metres off: twice as likely from up there, and more.
            foe.X = post.X + 18; foe.Z = post.Z; foe.Cover = berm.Id;
            below.Weapon = Weapon.M16;
            Assert.Greater(Combat.HitChance(st, mate, foe), 2 * Combat.HitChance(st, below, foe), "a rifle from the tower");
            // And the man up there is harder to hit than he would be standing at its foot, but anyone can see him.
            foe.Posture = Posture.Crouched; foe.Cover = -1;
            double up0 = Combat.HitChance(st, foe, mate);
            double high = mate.High; mate.High = 0;
            double down0 = Combat.HitChance(st, foe, mate);
            mate.High = high;
            Assert.Less(up0, down0, "the bags round the platform are no cover");
            Assert.Greater(up0, 0);
            foe.X = 10; foe.Z = Tune.Lanes[0];
            Assert.IsTrue(Senses.Sees(st, foe, mate), "a man up the tower cannot be seen from fifty metres off, in the other lane");
            foe.X = 30;
            Assert.IsFalse(Senses.Sees(st, foe, mate), "a rifleman sees him from seventy");
            foe.Weapon = Weapon.Sniper;
            Assert.IsTrue(Senses.Sees(st, foe, mate), "a sniper's scope does not find him");
            foe.Weapon = Weapon.Ak;

            // What reaches him from the ground: a sniper's rifle, from anywhere, as an aimed shot; no burst on the ground.
            double rifle2 = Arms.Of(Weapon.Ak).Range * Arms.Of(Weapon.Ak).Range;
            Assert.AreEqual(rifle2, Vantage.Reach2(foe, mate, rifle2), "a rifle reaches the tower from across the map");
            Assert.IsTrue(Tactics.Long(st, foe, mate));
            foe.Weapon = Weapon.Sniper;
            Assert.AreEqual(Tune.PostSniperReach * Tune.PostSniperReach, Vantage.Reach2(foe, mate, rifle2), "a sniper's rifle does not");
            Assert.IsFalse(Tactics.Long(st, foe, mate), "a sniper's shot at the tower is a long shot");
            // And the computer's answer: while the tower is manned, a squad it raises brings a marksman of its own
            // (one of its riflemen, not its leader), and only one.
            var cell = st.Squads[foe.Squad];
            int wasRank = foe.Rank;
            foe.Weapon = Weapon.Ak; foe.Rank = 1;
            foreach (var x in st.Men.Where(x => x.Squad == cell.Id && x != foe)) if (x.Weapon == Weapon.Sniper) x.Weapon = Weapon.Ak;
            int strength = Squads.Roster(st, cell.Id).Count;
            Assert.IsTrue(Vantage.Answer(st, cell), "a Vietnamese squad raised against a manned tower brings no marksman");
            Assert.AreEqual(1, Squads.Roster(st, cell.Id).Count(x => x.Weapon == Weapon.Sniper), "it brings one");
            Assert.AreEqual(strength, Squads.Roster(st, cell.Id).Count, "and no man the fewer");
            Assert.IsFalse(Vantage.Answer(st, cell), "nor a second");
            foreach (var x in st.Men.Where(x => x.Squad == cell.Id)) if (x.Weapon == Weapon.Sniper) x.Weapon = Weapon.Ak;
            foe.Rank = wasRank;
            Assert.IsFalse(Vantage.Answer(st, st.Squads[below.Squad]), "the Americans answer a tower nobody has manned against them");
            foe.Weapon = Weapon.Ak; foe.X = 44;
            Assert.IsFalse(Vantage.Reaches(0, mate), "a burst on the ground reaches the platform");
            Assert.IsTrue(Vantage.Reaches(post.Height, mate));
            Assert.IsFalse(Vantage.Reaches(post.Height, below), "a burst up there reaches the ground");
            st.Grenades.Add(new Grenade { Id = st.GrenadeId++, Side = Side.Vc, Thrower = foe.Id, X = post.X, Z = post.Z, Ticks = 1, Radius = 8, Kill = 1 });
            m.Step(); m.Step();
            Assert.IsTrue(men.All(x => x.Alive), "a grenade at the tower's foot killed a man on its platform");

            // Told to advance, it comes down, a man at a time, and is a squad in its lane again.
            m.Issue(Command.OrderSquad(Side.Us, sq.Id, Order.Advance));
            for (int t = 0; t < 900 && sq.Post >= 0; t++)
            {
                m.Step();
                foreach (var x in st.Men.Where(x => x.Side == Side.Vc)) { x.X = 44; x.Pin = 0; }      // (kept out of it)
            }
            Assert.AreEqual(-1, sq.Post, "told to advance, it never came down");
            Assert.IsTrue(men.All(x => x.High == 0 && x.Climb == 0), "a man of it is still up there");
            Assert.IsNull(Vantage.Blocked(st, Side.Us, "us-mg", 0), "the post is not free again");
            double from0 = men.Max(x => x.X);
            for (int t = 0; t < 400; t++)
            {
                m.Step();
                foreach (var x in st.Men.Where(x => x.Side == Side.Vc)) { x.X = 44; x.Pin = 0; }
            }
            Assert.Greater(men.Max(x => x.X), from0 + 2, "down from the tower, it does not march");
        }

        // --- Part 2: the computer's commander, behind MatchOptions.Commander (the owner, 2026-10-09: "then the computer commander") -----

        private static MatchOptions Commanded(int seed, bool onMap = true, bool tempo = false)
        {
            var o = Posted(seed, onMap, tempo);
            o.Commander = true;
            return o;
        }

        /// <summary>Recorded by `tools/simcs/run.sh hash N [frag smoke] drill fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics vantage commander [map]`.</summary>
        [TestCase(1, true, 5206, 3822424213u, 2673544029u, 4057359457u, "vc morale broke")]
        [TestCase(7, false, 3133, 3346100974u, 276441402u, 4139978951u, "vc morale broke")]
        public void With_the_commander_a_match_is_pinned_and_still_a_pure_function_of_its_seed(
            int seed, bool asTheGame, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).Commander, "the commander must be off unless asked for");
            var (a, m) = Played(Commanded(seed, asTheGame));
            var (b, _) = Played(Commanded(seed, asTheGame));
            CollectionAssert.AreEqual(a, b, "two runs of the same seed diverged");
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(at100, a[100], "tick 100");
            Assert.AreEqual(at1000, a[1000], "tick 1000");
            Assert.AreEqual(final, a[a.Count - 1], "final tick");
            Assert.AreEqual(reason, m.State.Reason);
        }

        /// <summary>
        /// The owner, 2026-10-09: "focus on firing/gunplay mechanics and AI smartness", and "then the computer
        /// commander". Without it a side with no player raises a squad when it can afford one and does nothing
        /// else. With it the computer plays its side's cards (the Americans' strikes and smoke, the VC's traps),
        /// pulls its levers, sends a new squad to the lane that is shortest of men; never plays the player's
        /// cards for him; does not call a strike down on its own men; and lays a trap no oftener than it may.
        /// </summary>
        [Test]
        public void With_the_commander_the_computer_plays_its_cards_and_its_levers_and_only_its_own()
        {
            int usCards = 0, vcCards = 0, strikes = 0, onItsOwn = 0, leverTicks = 0;
            for (int seed = 3; seed <= 6; seed++)
            {
                // (A squad's own smoke is an area too, and no card: strikes and traps are what is counted.)
                var plain = new LiveMatch(Posted(seed, tempo: true));
                while (!plain.State.Over && plain.State.Tick < 6000)
                {
                    plain.Step();
                    Assert.IsFalse(plain.State.Areas.Any(x => x.Kind != AreaKind.Smoke), "the computer played a card without its commander");
                    Assert.IsTrue(plain.State.Cover.All(c => c.LeverUs == Lever.Auto && c.LeverVc == Lever.Auto), "or pulled a lever");
                }

                var m = new LiveMatch(Commanded(seed, tempo: true));
                var st = m.State;
                int lastTrap = -100000;
                while (!st.Over && st.Tick < 6000)
                {
                    int from = st.Events.Count;
                    m.Step();
                    if (st.Cover.Any(c => c.LeverUs != Lever.Auto || c.LeverVc != Lever.Auto)) leverTicks++;
                    for (int i = from; i < st.Events.Count; i++)
                    {
                        var e = st.Events[i];
                        if (e.Kind != EventKind.AreaStart) continue;
                        var area = st.Areas.FirstOrDefault(x => x.Id == e.Id);
                        if (area == null || area.Kind == AreaKind.Smoke) continue;
                        if (e.Side == Side.Us) usCards++; else vcCards++;
                        if (area.Kind == AreaKind.Barrage)
                        {
                            strikes++;
                            onItsOwn += st.Men.Count(x => x.Alive && x.Side == e.Side && Combat.Dist(x.X, x.Z, area.X, area.Z) < area.Radius);
                        }
                        if (area.Kind == AreaKind.Trap)
                        {
                            Assert.AreEqual(Side.Vc, e.Side, "the Americans laid a trap");
                            Assert.GreaterOrEqual(st.Tick - lastTrap, Tune.TrapEvery, $"seed {seed}: two traps too close together");
                            lastTrap = st.Tick;
                        }
                    }
                }
            }
            Assert.Greater(usCards, 0, "the Americans' commander never played a card");
            Assert.Greater(vcCards, 0, "the VC's commander never played a card");
            Assert.Greater(strikes, 0, "nor called a strike");
            Assert.LessOrEqual(onItsOwn * 4, strikes, "it calls its strikes down on its own men");
            Assert.Greater(leverTicks, 0, "it never pulled a lever");

            // The player's side is his own: the commander plays nothing for it.
            var o = Commanded(4, tempo: true);
            o.Player = Side.Us;
            var mine = new LiveMatch(o);
            int traps = 0;
            while (!mine.State.Over && mine.State.Tick < 4000)
            {
                mine.Step();
                Assert.IsTrue(mine.State.Cover.All(c => c.LeverUs == Lever.Auto), "the commander pulled the player's lever");
                Assert.IsFalse(mine.State.Areas.Any(x => x.Side == Side.Us && x.Kind != AreaKind.Smoke), "the commander played the player's cards");
                if (mine.State.Areas.Any(x => x.Side == Side.Vc && x.Kind == AreaKind.Trap)) traps++;
            }
            Assert.Greater(traps, 0, "and none of its own against him");

            // A new squad: to the lane where it has fewest men.
            var fresh = Match.Create(Commanded(3, tempo: true));
            foreach (var q in fresh.Squads.Where(q => q.Side == Side.Us)) q.Lane = 0;
            Assert.AreEqual(1, Captain.Lane(fresh, Side.Us), "its next squad goes where its men already are");
            foreach (var q in fresh.Squads.Where(q => q.Side == Side.Us)) q.Lane = 1;
            Assert.AreEqual(0, Captain.Lane(fresh, Side.Us));
        }

        // --- Part 2: a squad that keeps together, behind MatchOptions.Cohesion (the owner, 2026-10-11: "tons of wandering, weird formations") -----

        private static MatchOptions Kept(int seed, bool onMap = true, bool tempo = false)
        {
            var o = Commanded(seed, onMap, tempo);
            o.Cohesion = true;
            return o;
        }

        /// <summary>Recorded by `tools/simcs/run.sh hash N [frag smoke] drill fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics vantage commander cohesion [map]`.</summary>
        [TestCase(1, true, 2524, 4003524403u, 2835978446u, 3667431746u, "us morale broke")]
        [TestCase(7, false, 6409, 2203668929u, 2326381529u, 795505337u, "us morale broke")]
        public void With_cohesion_a_match_is_pinned_and_still_a_pure_function_of_its_seed(
            int seed, bool asTheGame, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).Cohesion, "cohesion must be off unless asked for");
            var (a, m) = Played(Kept(seed, asTheGame));
            var (b, _) = Played(Kept(seed, asTheGame));
            CollectionAssert.AreEqual(a, b, "two runs of the same seed diverged");
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(at100, a[100], "tick 100");
            Assert.AreEqual(at1000, a[1000], "tick 1000");
            Assert.AreEqual(final, a[a.Count - 1], "final tick");
            Assert.AreEqual(reason, m.State.Reason);
        }

        /// <summary>A squad's length along its lane, a lead man 5 m out by himself, and a man 6 m behind the middle of his squad: over whole matches.</summary>
        private static (double length, double alone, double behind) Shape(Func<int, MatchOptions> options)
        {
            double length = 0, squadTicks = 0, alone = 0, behind = 0, manTicks = 0;
            for (int seed = 3; seed <= 6; seed++)
            {
                var m = new LiveMatch(options(seed));
                var st = m.State;
                while (!st.Over && st.Tick < 3000)
                {
                    m.Step();
                    if (st.Phase != Phase.Fight) continue;
                    foreach (var sq in st.Squads)
                    {
                        if (sq.Order == Order.Fallback || sq.Post >= 0) continue;
                        var men = st.Men.Where(x => x.Alive && x.Squad == sq.Id).OrderBy(x => x.X * Combat.Advance(sq.Side)).ToList();
                        if (men.Count < 3) continue;
                        squadTicks++; manTicks += men.Count;
                        length += Math.Abs(men[men.Count - 1].X - men[0].X);
                        if (Math.Abs(men[men.Count - 1].X - men[men.Count - 2].X) >= 5) alone++;
                        double mid = men[men.Count / 2].X;
                        behind += men.Count(x => (mid - x.X) * Combat.Advance(sq.Side) > 6);
                    }
                }
            }
            return (length / squadTicks, alone / squadTicks, behind / manTicks);
        }

        /// <summary>
        /// The owner, 2026-10-11: "AI also needs work, still tons of wandering, weird formations". Measured, a squad
        /// of five was strung out over eight to ten metres with its lead man out alone and men walking up one at
        /// a time. With the rule a man follows the man ahead at 1.7 m, not 2.6; the squad goes no faster than its
        /// last man; a man left behind runs to his place; closing by halves, the halves are two groups; a squad
        /// goes in all together, and only when half of it can; and one that is going in and going nowhere stops
        /// saying so. Over whole matches squads are shorter by a quarter and more, and a lead man is out alone and
        /// a man is left behind half as often.
        /// </summary>
        [Test]
        public void With_cohesion_a_squad_keeps_together_and_goes_in_together()
        {
            var plain = Match.Create(Commanded(3, tempo: true));
            var st = Match.Create(Kept(3, tempo: true));
            var sq = st.Squads.First(q => q.Side == Side.Vc);
            sq.Halted = false;
            Assert.AreEqual(Tune.SlotGap, Cohesion.Gap(plain, plain.Squads[0]), "without the rule the file is not what it was");
            Assert.AreEqual(Tune.FileMarch, Cohesion.Gap(st, sq));
            sq.Halted = true;
            Assert.AreEqual(Tune.FileClosed, Cohesion.Gap(st, sq), "halted in the open it does not close up");
            Assert.Less(Tune.FileClosed, Tune.FileMarch);
            Assert.Less(Tune.FileMarch, Tune.SlotGap);

            // By halves: the two men at the head of the file, and the rest. (Without the rule, every other man.)
            var five = Enumerable.Range(0, 5).Select(r => new Man { Rank = r }).ToList();
            Assert.AreEqual(Tactics.Turn(st, sq, five[0]), Tactics.Turn(st, sq, five[1]), "the two at the head do not move together");
            Assert.AreEqual(Tactics.Turn(st, sq, five[2]), Tactics.Turn(st, sq, five[4]));
            Assert.AreEqual(Tactics.Turn(st, sq, five[2]), Tactics.Turn(st, sq, five[3]));
            Assert.AreNotEqual(Tactics.Turn(st, sq, five[1]), Tactics.Turn(st, sq, five[2]), "the two halves move at once");
            Assert.AreNotEqual(Tactics.Turn(plain, plain.Squads[0], five[0]), Tactics.Turn(plain, plain.Squads[0], five[1]));

            // Going in: half of it, and two of them, on their feet or able to be.
            foreach (var x in five) x.Pin = 0;
            Assert.IsTrue(Cohesion.Ready(five));
            five[0].Pin = five[1].Pin = Tune.PinDrop;
            Assert.IsTrue(Cohesion.Ready(five), "three of five up, and it will not go");
            five[2].Pin = Tune.PinDrop;
            Assert.IsFalse(Cohesion.Ready(five), "two of five up, and it goes in");
            Assert.IsFalse(Cohesion.Ready(new List<Man> { new Man { Pin = Tune.PinDrop }, new Man() }), "one of a pair goes in by himself");
            Assert.IsTrue(Cohesion.Ready(new List<Man> { new Man() }));

            // Closing or going in, and going nowhere: after three and a half seconds it says so.
            sq.AnchorX = 10;
            Cohesion.Start(st, sq);
            st.Tick += Tune.StallTicks - 1;
            Assert.IsFalse(Cohesion.Stalled(st, sq));
            st.Tick += 1;
            Assert.IsTrue(Cohesion.Stalled(st, sq), "a squad going nowhere is not stalled");
            sq.AnchorX -= Tune.StallMetres;
            Assert.IsFalse(Cohesion.Stalled(st, sq), "a metre further on, and it is still stalled");

            // The tail: on the march, with its last man kept where he is, the squad does not walk away from him.
            // (Once it meets the enemy it takes cover, where each man has a place of his own, wherever the last is.)
            var gone = new double[2];
            foreach (bool rule in new[] { false, true })
            {
                var o = rule ? Kept(4, tempo: true) : Commanded(4, tempo: true);
                o.Player = Side.Us;                      // (the Americans raise nobody, and stay in their wire through the opening)
                var m = new LiveMatch(o);
                var s2 = m.State;
                var vc = s2.Squads.First(q => q.Side == Side.Vc);
                var men = s2.Men.Where(x => x.Squad == vc.Id).ToList();
                var last = men.OrderBy(x => x.X).Last();     // the VC go toward -x: the last man is the one furthest east
                double held = last.X;
                for (int t = 0; t < 400 && vc.Task == SquadTask.March; t++)
                {
                    last.X = held;
                    m.Step();
                    last.X = held;
                    if (vc.Task == SquadTask.March) gone[rule ? 1 : 0] = Math.Max(gone[rule ? 1 : 0], held - men.Where(x => x.Alive).Min(x => x.X));
                }
            }
            // (A file's own length and the slack, and what a place in the cover it passes may add.)
            Assert.Less(gone[1], (5 - 1) * Tune.FileMarch + Tune.TailSlack + 3.0, "the squad walked away from its last man");
            Assert.Greater(gone[0], gone[1] + 0.8, "the test's premise: without the rule it goes further from him");

            // And over whole matches.
            var was = Shape(seed => Commanded(seed, tempo: true));
            var now = Shape(seed => Kept(seed, tempo: true));
            Assert.Less(now.length, was.length * 0.8, $"squads are no shorter: {was.length:F1} m, {now.length:F1} m");
            Assert.Less(now.alone, was.alone * 0.75, $"a lead man is out alone as often: {was.alone:P1}, {now.alone:P1}");
            Assert.Less(now.behind, was.behind * 0.75, $"men are left behind as often: {was.behind:P1}, {now.behind:P1}");
        }

        // --- Part 2: how a man carries himself, behind MatchOptions.Bearing (the owner, 2026-10-11: "improve gameplay. Make it smooth like real life") -----

        private static MatchOptions Borne(int seed, bool onMap = true, bool tempo = false)
        {
            var o = Kept(seed, onMap, tempo);
            o.Bearing = true;
            return o;
        }

        /// <summary>Recorded by `tools/simcs/run.sh hash N [frag smoke] drill fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics vantage commander cohesion bearing [map]`.</summary>
        [TestCase(1, true, 7104, 2865253007u, 2221089145u, 465850309u, "vc morale broke")]
        [TestCase(7, false, 4723, 587953012u, 409920569u, 2681455398u, "us morale broke")]
        public void With_bearing_a_match_is_pinned_and_still_a_pure_function_of_its_seed(
            int seed, bool asTheGame, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).Bearing, "bearing must be off unless asked for");
            var (a, m) = Played(Borne(seed, asTheGame));
            var (b, _) = Played(Borne(seed, asTheGame));
            CollectionAssert.AreEqual(a, b, "two runs of the same seed diverged");
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(at100, a[100], "tick 100");
            Assert.AreEqual(at1000, a[1000], "tick 1000");
            Assert.AreEqual(final, a[a.Count - 1], "final tick");
            Assert.AreEqual(reason, m.State.Reason);
        }

        /// <summary>
        /// What a man does with himself, a man-minute of the fight, over whole matches: his changes of posture; the
        /// times he is back as he was two changes ago inside three seconds; and the times his speed changes by half
        /// a metre a second or more between two steps of one move.
        /// </summary>
        private static (double changes, double bobs, double lurches) Carriage(Func<int, MatchOptions> options)
        {
            double changes = 0, bobs = 0, lurches = 0, manTicks = 0;
            for (int seed = 3; seed <= 6; seed++)
            {
                var m = new LiveMatch(options(seed));
                var st = m.State;
                var held = new Dictionary<int, (Posture now, int since, Posture before, int beforeSince)>();
                var pace = new Dictionary<int, double>();
                while (!st.Over && st.Tick < 3000)
                {
                    var at = st.Men.Select(x => (x.X, x.Z)).ToArray();
                    m.Step();
                    if (st.Phase != Phase.Fight) continue;
                    foreach (var p in st.Men)
                    {
                        if (!p.Alive || p.Id >= at.Length || st.Squads[p.Squad].Post >= 0) continue;
                        manTicks++;
                        if (!held.TryGetValue(p.Id, out var h)) h = (p.Posture, -1, p.Posture, -1);
                        if (h.now != p.Posture)
                        {
                            changes++;
                            if (p.Posture == h.before && h.beforeSince >= 0 && st.Tick - h.beforeSince <= 3 * Tune.TickHz) bobs++;
                            h = (p.Posture, st.Tick, h.now, h.since);
                        }
                        held[p.Id] = h;
                        double dx = p.X - at[p.Id].X, dz = p.Z - at[p.Id].Z, v = Math.Sqrt(dx * dx + dz * dz) / Tune.Dt;
                        if (pace.TryGetValue(p.Id, out double v0) && v0 > 0.2 && v > 0.2 && Math.Abs(v - v0) >= 0.5) lurches++;
                        pace[p.Id] = v;
                    }
                }
            }
            double minutes = manTicks * Tune.Dt / 60;
            return (changes / minutes, bobs / minutes, lurches / minutes);
        }

        /// <summary>
        /// The owner, 2026-10-11: "improve gameplay. Make it smooth like real life." Measured, a man changed posture
        /// twelve times a minute, went from flat to a knee and back as his fear crossed one line, got to his feet
        /// and knelt again having gone nowhere, and went from rest to full speed in one step. With the rule a man
        /// who is flat stays flat until he gets up to go (only one with a place in cover comes back onto a knee,
        /// when the fire has let him be); he gets up for a move he can make now; once up he has his run; and he
        /// gathers pace and slows into his place.
        /// </summary>
        [Test]
        public void With_bearing_a_man_stays_down_gets_up_only_to_go_and_gathers_pace()
        {
            var st = Match.Create(Borne(3, tempo: true));
            var sq = st.Squads.First(q => q.Side == Side.Us);
            var m = st.Men.Where(x => x.Squad == sq.Id).OrderBy(x => x.Rank).First();
            var mate = st.Men.Where(x => x.Squad == sq.Id).OrderBy(x => x.Rank).ElementAt(1);
            int foe = st.Squads.First(q => q.Side == Side.Vc).Id;
            void InContact(SquadTask task) { sq.Task = task; sq.Threat = foe; sq.ThreatGap = 15; sq.ThreatSeen = true; sq.Order = Order.Advance; }

            // --- down is down. A rifleman flat in the open, his squad in a firefight.
            InContact(SquadTask.Firefight);
            m.Weapon = Weapon.M16; m.Cover = -1; m.Place = -1; m.PlaceCover = -1; m.Posture = Posture.Prone; m.Pin = 0; m.Dwell = 1000;
            Assert.AreEqual(Posture.Prone, Bearing.Keep(st, m, sq, Posture.Crouched, true), "flat in the open, he was got onto a knee");
            Assert.AreEqual(Posture.Standing, Bearing.Keep(st, m, sq, Posture.Standing, true), "he does not get up to go");
            Assert.AreEqual(Posture.Crouched, Bearing.Keep(st, m, sq, Posture.Crouched, false), "a man running, or going in with his hands, is not this rule's");
            st.Bearing = false;
            Assert.AreEqual(Posture.Crouched, Bearing.Keep(st, m, sq, Posture.Crouched, true), "without the rule he is not as he was");
            st.Bearing = true;
            // With a place in cover he comes back onto a knee to fire over it: when the fire has let him be, and not at once.
            int wall = st.Cover.First(c => !Fieldcraft.Dug(c)).Id;
            m.Cover = wall; m.Place = 0; m.PlaceCover = wall;
            Assert.AreEqual(Posture.Crouched, Bearing.Keep(st, m, sq, Posture.Crouched, true), "with a place in cover he never comes back up");
            m.Dwell = Tune.StayFlat - 1;
            Assert.AreEqual(Posture.Prone, Bearing.Keep(st, m, sq, Posture.Crouched, true), "he was flat a second and up again");
            m.Dwell = 1000; m.Pin = Tune.PinDrop;
            Assert.AreEqual(Posture.Prone, Bearing.Keep(st, m, sq, Posture.Crouched, true), "he came up while the fire still had him");
            m.Pin = 0; m.Weapon = Weapon.M60;
            Assert.AreEqual(Posture.Prone, Bearing.Keep(st, m, sq, Posture.Crouched, true), "a machine-gunner got up off his gun");
            m.Weapon = Weapon.M16;
            // Going to ground from his feet: flat in the open where his squad is fighting from, a knee between its bounds.
            m.Cover = -1; m.Place = -1; m.PlaceCover = -1; m.Posture = Posture.Standing; m.Rest = 100;
            Assert.AreEqual(Posture.Prone, Bearing.Keep(st, m, sq, Posture.Crouched, true), "he took a knee in the open before he lay down");
            InContact(SquadTask.Close);
            Assert.AreEqual(Posture.Crouched, Bearing.Keep(st, m, sq, Posture.Crouched, true), "between bounds he lay flat");
            // Out of contact a short halt is not worth a knee.
            sq.Task = SquadTask.March; sq.Threat = -1; sq.ThreatGap = double.PositiveInfinity; sq.ThreatSeen = false;
            m.Rest = Tune.RestToKneelQuiet - 1;
            Assert.AreEqual(Posture.Standing, Bearing.Keep(st, m, sq, Posture.Crouched, true), "he knelt for a halt of a second");
            m.Rest = Tune.RestToKneelQuiet;
            Assert.AreEqual(Posture.Crouched, Bearing.Keep(st, m, sq, Posture.Crouched, true), "he never takes a knee out of contact");

            // --- up is to go. On a knee, his squad closing by halves, his place six metres on, his half's turn just begun.
            InContact(SquadTask.Close);
            Assert.IsTrue(Tactics.Bounds(st, sq), "the test's premise: the squad moves by halves");
            m.Posture = Posture.Crouched; m.Dwell = 1000; m.Pin = 0; m.UnderAt = -100000; m.Wait = -1; m.Rest = 100;
            foreach (var x in st.Men) if (x != m) { x.X = m.X - 20 - x.Id; x.Rest = 100; }       // nobody near him
            double tx = m.X + 6, tz = m.Z;
            st.Tick = 2 * Tune.BoundTicks - sq.Id * 13;
            Assert.IsTrue(Tactics.Turn(st, sq, m), "the test's premise: it is his half's turn");
            Assert.AreEqual(Idle.No, Bearing.WhyDown(st, sq, m, tx, tz), "nothing keeps him, and he stays down");
            Assert.IsFalse(Bearing.StaysDown(st, sq, m, tx, tz));
            m.Dwell = Tune.StayDown - 1;
            Assert.AreEqual(Idle.JustDown, Bearing.WhyDown(st, sq, m, tx, tz), "down a second and up again");
            m.Dwell = 1000; m.UnderAt = st.Tick - Tune.RiseClear;
            Assert.AreEqual(Idle.UnderFire, Bearing.WhyDown(st, sq, m, tx, tz), "he got up with rounds landing by him");
            // In the open with a place in cover a few paces off he does not wait for the fire to let up: the fire is why he goes.
            m.UnderAt = st.Tick;
            Assert.AreEqual(Idle.No, Bearing.WhyDown(st, sq, m, m.X + 4, tz, toCover: true), "he lay in the open four metres from his place in cover");
            Assert.AreEqual(Idle.UnderFire, Bearing.WhyDown(st, sq, m, m.X + Tune.BoundFinishCover + 1, tz, toCover: true), "he got up under fire for cover a long run off");
            Assert.AreEqual(Idle.UnderFire, Bearing.WhyDown(st, sq, m, m.X + 4, tz), "he got up under fire for a place in the open");
            m.UnderAt = st.Tick - Tune.RiseClear - 1;
            Assert.AreEqual(Idle.No, Bearing.WhyDown(st, sq, m, tx, tz));
            // Too little of his turn left to get up and cover ground in: he waits for the next.
            int enough = Tune.RiseFromKnee + Tune.BoundWorth;
            st.Tick += Tune.BoundTicks - enough;
            Assert.AreEqual(Idle.No, Bearing.WhyDown(st, sq, m, tx, tz), "with just time enough he does not go");
            st.Tick += 1;
            Assert.AreEqual(Idle.TurnEnding, Bearing.WhyDown(st, sq, m, tx, tz), "he got up as his turn ended");
            st.Tick = 2 * Tune.BoundTicks - sq.Id * 13;
            // A man of his own at rest in his way, in a trench where there is no way round: he stays down. (One on the move is not in the way.)
            int trench = st.Cover.First(c => Fieldcraft.Dug(c)).Id;
            m.Cover = trench;
            mate.X = m.X + 0.5; mate.Z = m.Z; mate.Rest = 100; mate.Place = -1; mate.PlaceCover = -1;
            Assert.IsTrue(Fieldcraft.Barred(st, m, tx, tz, Tune.SpeedRush), "the test's premise: a man at rest a pace ahead of him in a trench bars his way");
            Assert.AreEqual(Idle.Blocked, Bearing.WhyDown(st, sq, m, tx, tz), "he got up behind a man he cannot pass");
            mate.Rest = 0;
            Assert.AreEqual(Idle.No, Bearing.WhyDown(st, sq, m, tx, tz), "a man who is himself going on bars nobody");
            // On his feet and barred, he does not stand there: after a moment he is where he is going, for now.
            mate.Rest = 100; m.Posture = Posture.Standing; m.Balk = 0;
            for (int k = 1; k < Tune.BalkTicks; k++) Assert.IsFalse(Bearing.Balked(st, sq, m, tx, tz), $"barred {k} ticks and he has given up");
            Assert.IsTrue(Bearing.Balked(st, sq, m, tx, tz), "he stands behind the man in his way");
            mate.Rest = 0;
            Assert.IsFalse(Bearing.Balked(st, sq, m, tx, tz), "the way is clear and he does not go");
            Assert.AreEqual(0, m.Balk);
            m.Cover = -1; mate.X = m.X - 30;

            // --- once up he has his run, though his half's turn ends under him.
            m.Posture = Posture.Standing; m.Before = Posture.Crouched; m.Rest = 5;
            st.Tick = 3 * Tune.BoundTicks - sq.Id * 13;
            Assert.IsFalse(Tactics.Turn(st, sq, m), "the test's premise: it is the other half's turn");
            m.Dwell = enough - 1;
            Assert.IsFalse(Tactics.Waits(st, sq, m, tx, tz, false), "just on his feet, he was stopped where he stood");
            m.Dwell = enough;
            Assert.IsTrue(Tactics.Waits(st, sq, m, tx, tz, false), "he runs on through the other half's turn");
            m.Dwell = enough - 1; st.Bearing = false;
            Assert.IsTrue(Tactics.Waits(st, sq, m, tx, tz, false), "without the rule the turn's end does not stop him");
            st.Bearing = true;
            // Nor does a man down in the open a few paces short of his place in cover wait his half's turn to get to it.
            m.Posture = Posture.Crouched; m.Dwell = 1000; m.Cover = -1;
            Assert.IsFalse(Tactics.Waits(st, sq, m, m.X + 4, tz, true), "he waited his turn in the open four metres from his place in cover");
            Assert.IsTrue(Tactics.Waits(st, sq, m, m.X + 4, tz, false), "the other half's turn, and he does not wait");
            Assert.IsTrue(Tactics.Waits(st, sq, m, m.X + Tune.BoundFinishCover + 1, tz, true), "cover a long run off, and he does not wait his turn for it");

            // --- pace. From rest he gathers it over half a second and more, and never goes faster than his pace.
            var runner = new Man();
            double first = Bearing.Stride(runner, Tune.SpeedRush, 20);
            Assert.AreEqual(Tune.Gather * Tune.Dt * Tune.Dt, first, 1e-12, "his first step is not a short one");
            int steps = 1;
            while (runner.Pace < Tune.SpeedRush && steps < 100) { Bearing.Stride(runner, Tune.SpeedRush, 20); steps++; Assert.LessOrEqual(runner.Pace, Tune.SpeedRush); }
            Assert.That(steps, Is.InRange(8, 20), "how long he takes to get to the double");
            // A slower pace is taken up over a few steps too.
            Bearing.Stride(runner, Tune.SpeedWalk, 20);
            Assert.AreEqual(Tune.SpeedRush - Tune.Ease * Tune.Dt, runner.Pace, 1e-12);
            // He slows into his place, and does not go past it.
            runner.Pace = Tune.SpeedRush;
            Bearing.Stride(runner, Tune.SpeedRush, 0.1);
            Assert.Less(runner.Pace, Tune.SpeedRush * 0.6, "he arrives at a run");
            Assert.GreaterOrEqual(runner.Pace, Tune.PaceLeast);
            runner.Pace = 1;
            Assert.AreEqual(0.01, Bearing.Stride(runner, Tune.SpeedRush, 0.01), 1e-12, "he overshot his place");

            // --- and over whole matches.
            var was = Carriage(seed => Kept(seed, tempo: true));
            var now = Carriage(seed => Borne(seed, tempo: true));
            Assert.Less(now.changes, was.changes * 0.75, $"a man changes posture as often: {was.changes:F1} a minute, {now.changes:F1}");
            Assert.Less(now.bobs, was.bobs * 0.25, $"a man bobs down and up as often: {was.bobs:F2} a minute, {now.bobs:F2}");
            Assert.Less(now.lurches, was.lurches * 0.2, $"a man's speed lurches as often: {was.lurches:F2} a minute, {now.lurches:F2}");
        }

        // --- Part 2: a call-in on its way, behind MatchOptions.Inbound (PLAN §12.45) -----------------------

        private static MatchOptions Called(int seed, bool onMap = true)
        {
            var o = Borne(seed, onMap);
            o.Inbound = true;
            return o;
        }

        /// <summary>Recorded by `tools/simcs/run.sh hash N [frag smoke] drill fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics vantage commander cohesion bearing inbound [map]`.</summary>
        [TestCase(1, true, 5581, 2865253007u, 2221089145u, 591451121u, "vc morale broke")]
        [TestCase(7, false, 5440, 587953012u, 1132564249u, 4590224u, "us morale broke")]
        public void With_call_ins_inbound_a_match_is_pinned_and_still_a_pure_function_of_its_seed(
            int seed, bool asTheGame, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).Inbound, "inbound must be off unless asked for");
            var (a, m) = Played(Called(seed, asTheGame));
            var (b, _) = Played(Called(seed, asTheGame));
            CollectionAssert.AreEqual(a, b, "two runs of the same seed diverged");
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(at100, a[100], "tick 100");
            Assert.AreEqual(at1000, a[1000], "tick 1000");
            Assert.AreEqual(final, a[a.Count - 1], "final tick");
            Assert.AreEqual(reason, m.State.Reason);
        }

        /// <summary>Recorded by `tools/simcs/run.sh hash N frag smoke drill fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics vantage commander cohesion bearing inbound map emplace`.</summary>
        [TestCase(1, 5581, 3590958095u, 3249803481u, 3025940641u, "vc morale broke")]
        [TestCase(7, 5415, 4187739844u, 3236355545u, 3260725662u, "us morale broke")]
        public void With_the_mortar_emplaced_a_match_is_pinned(int seed, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            var o = new MatchOptions
            {
                Seed = seed, Us = Plan.Ceiling, Vc = Plan.Ceiling, Cover = Map.Cover(), Frag = true, SquadSmoke = true, Drill = true, Fieldcraft = true,
                Arms = true, Senses = true, Gunnery = true, Ammo = true, Tactics = true, Fortune = true, FireControl = true, Ballistics = true,
                Vantage = true, Commander = true, Cohesion = true, Bearing = true, Inbound = true, Emplace = true,
            };
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).Emplace, "emplace must be off unless asked for");
            var m = new LiveMatch(o);
            var h = new List<uint> { Parity.Hash(m.State, 0) };
            while (!m.State.Over && m.State.Tick < m.Cap) { m.Step(); h.Add(Parity.Hash(m.State, 0)); }
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(reason, m.State.Reason);
            Assert.AreEqual(at100, h[100]);
            Assert.AreEqual(at1000, h[Math.Min(1000, h.Count - 1)]);
            Assert.AreEqual(final, h[h.Count - 1]);
        }

        /// <summary>Recorded by `tools/simcs/run.sh hash N frag smoke drill fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics vantage commander cohesion bearing inbound map emplace satchel`.</summary>
        [TestCase(1, 5581, 3711546079u, 3555651705u, 1240054123u, "vc morale broke")]
        [TestCase(7, 6520, 3589350900u, 1055445897u, 2851206011u, "us morale broke")]
        public void With_satchel_charges_a_match_is_pinned(int seed, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            var o = new MatchOptions
            {
                Seed = seed, Us = Plan.Ceiling, Vc = Plan.Ceiling, Cover = Map.Cover(), Frag = true, SquadSmoke = true, Drill = true, Fieldcraft = true,
                Arms = true, Senses = true, Gunnery = true, Ammo = true, Tactics = true, Fortune = true, FireControl = true, Ballistics = true,
                Vantage = true, Commander = true, Cohesion = true, Bearing = true, Inbound = true, Emplace = true, Satchel = true,
            };
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).Satchel, "satchel must be off unless asked for");
            var m = new LiveMatch(o);
            var h = new List<uint> { Parity.Hash(m.State, 0) };
            while (!m.State.Over && m.State.Tick < m.Cap) { m.Step(); h.Add(Parity.Hash(m.State, 0)); }
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(reason, m.State.Reason);
            Assert.AreEqual(at100, h[100]);
            Assert.AreEqual(at1000, h[Math.Min(1000, h.Count - 1)]);
            Assert.AreEqual(final, h[h.Count - 1]);
        }

        /// <summary>Recorded by `tools/simcs/run.sh hash N frag smoke drill fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics vantage commander cohesion bearing inbound map emplace satchel destruction`.</summary>
        [TestCase(1, 5581, 1768901934u, 659696368u, 3526591710u, "vc morale broke")]
        [TestCase(7, 6520, 3478108901u, 2888612312u, 3221027986u, "us morale broke")]
        public void With_destruction_a_match_is_pinned(int seed, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            var o = new MatchOptions
            {
                Seed = seed, Us = Plan.Ceiling, Vc = Plan.Ceiling, Cover = Map.Cover(), Frag = true, SquadSmoke = true, Drill = true, Fieldcraft = true,
                Arms = true, Senses = true, Gunnery = true, Ammo = true, Tactics = true, Fortune = true, FireControl = true, Ballistics = true,
                Vantage = true, Commander = true, Cohesion = true, Bearing = true, Inbound = true, Emplace = true, Satchel = true, Destruction = true,
            };
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).Destruction, "destruction must be off unless asked for");
            var m = new LiveMatch(o);
            var h = new List<uint> { Parity.Hash(m.State, 0) };
            while (!m.State.Over && m.State.Tick < m.Cap) { m.Step(); h.Add(Parity.Hash(m.State, 0)); }
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(reason, m.State.Reason);
            Assert.AreEqual(at100, h[100]);
            Assert.AreEqual(at1000, h[Math.Min(1000, h.Count - 1)]);
            Assert.AreEqual(final, h[h.Count - 1]);
        }

        /// <summary>Recorded by `simcs hash N frag smoke drill fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics vantage commander cohesion bearing inbound map emplace satchel destruction shootdown` (seed 1 brings no medevac down: the destruction pin, unmoved).</summary>
        [TestCase(1, 5581, 1768901934u, 659696368u, 3526591710u, "vc morale broke")]
        [TestCase(7, 4870, 3478108901u, 2888612312u, 2300756334u, "us morale broke")]
        public void With_shootdown_a_match_is_pinned(int seed, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            var o = new MatchOptions
            {
                Seed = seed, Us = Plan.Ceiling, Vc = Plan.Ceiling, Cover = Map.Cover(), Frag = true, SquadSmoke = true, Drill = true, Fieldcraft = true,
                Arms = true, Senses = true, Gunnery = true, Ammo = true, Tactics = true, Fortune = true, FireControl = true, Ballistics = true,
                Vantage = true, Commander = true, Cohesion = true, Bearing = true, Inbound = true, Emplace = true, Satchel = true, Destruction = true,
                Shootdown = true,
            };
            Assert.IsFalse(Match.Create(new MatchOptions { Seed = 1 }).Shootdown, "shootdown must be off unless asked for");
            var m = new LiveMatch(o);
            var h = new List<uint> { Parity.Hash(m.State, 0) };
            while (!m.State.Over && m.State.Tick < m.Cap) { m.Step(); h.Add(Parity.Hash(m.State, 0)); }
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(reason, m.State.Reason);
            Assert.AreEqual(at100, h[100]);
            Assert.AreEqual(at1000, h[Math.Min(1000, h.Count - 1)]);
            Assert.AreEqual(final, h[h.Count - 1]);
        }

        /// <summary>
        /// The tower comes down (MatchOptions.Destruction; the owner, 2026-10-12: "the watch tower must be collapsable and
        /// fall, to rpgs and heavy mortars ... don't always have it fall the same way"). Rockets fired at the men up on
        /// it take from it, a hand grenade does not; two bring it down, away from the last of them and turned by the
        /// dice; the men on it come down with it, killed under it or thrown clear and flat; and nobody can be sent up it
        /// again.
        /// Two matches that differ only in their seed do not fall the same way.
        /// </summary>
        [Test]
        public void Rockets_bring_the_tower_down_with_its_men_and_it_is_never_posted_again()
        {
            var ways = new List<double>();
            for (int seed = 1; seed <= 4; seed++)
            {
                var o = Scenarios.Firebase.Options(seed, Side.Us, Difficulty.Veteran);
                Assert.IsTrue(o.Destruction, "the game plays with destruction");
                var lm = new LiveMatch(o);
                var st = lm.State;
                st.Cp[(int)Side.Us] = 200;
                lm.Issue(Command.BuyTo(Side.Us, "us-mg", 0));
                for (int t = 0; t < 600 && !st.Men.Any(x => x.Alive && x.High > 0 && x.Climb == 0); t++) lm.Step();
                var up = st.Men.Where(x => x.Alive && x.High > 0 && x.Climb == 0).ToList();
                Assert.IsNotEmpty(up, "the test's premise: a team up the tower");
                var post = st.Posts[0];
                // A hand grenade at its foot does nothing to it.
                Destruction.Burst(st, post.X, post.Z, 0, 0);
                Assert.AreEqual(1.0, post.Strength, 1e-9);
                // Two rockets at the men up on it, bursting up there a pace off (as Frag.Detonate hands them on).
                int from = st.Events.Count;
                var rocket = new Grenade { By = Weapon.Rpg };
                for (int k = 0; k < 2; k++) Destruction.Burst(st, post.X + 1.0, post.Z + 0.5, post.Height, Destruction.Power(rocket));
                var fell = st.Events.Skip(from).SingleOrDefault(e => e.Kind == EventKind.Collapsed);
                Assert.IsNotNull(fell.Kind == EventKind.Collapsed ? (object)fell : null, "two rockets did not bring it down");
                Assert.IsTrue(post.Fallen);
                Assert.AreEqual(2, st.Events.Skip(from).Count(e => e.Kind == EventKind.Damaged));
                ways.Add(fell.Amount.Value);
                // Away from the rockets (they burst at +x of it), give or take the dice.
                double away = Math.Atan2(post.Z - (post.Z + 0.5), post.X - (post.X + 1.0)) * 180 / Math.PI;
                double off = Math.Abs(((fell.Amount.Value - away) % 360 + 540) % 360 - 180);
                Assert.LessOrEqual(off, Tune.TowerFallSpread * 180 / Math.PI + 1e-6, "it fell toward the rockets");
                // All of them come down, the killed too: those it killed under it, along its way; those who lived thrown
                // clear of it to one side, flat.
                double w = fell.Amount.Value * Math.PI / 180;
                foreach (var m in up)
                {
                    Assert.AreEqual(0.0, m.High, "a man was left up a tower that has fallen");
                    double across = Math.Abs(-(m.X - post.FootX) * Math.Sin(w) + (m.Z - post.FootZ) * Math.Cos(w));
                    if (!m.Alive) { Assert.LessOrEqual(across, 1.0 + 1e-9, "a man it killed is not under it"); continue; }
                    Assert.GreaterOrEqual(across, 2.6 - 1e-9, "a man who lived lies under the tower");
                    Assert.AreEqual(Posture.Prone, m.Posture);
                }
                Assert.IsFalse(st.Squads.Any(q => q.Post == post.Id), "a squad still holds a fallen tower");
                Assert.AreEqual("fallen", Vantage.Blocked(st, Side.Us, "us-mg", post.Id));
            }
            Assert.Greater(ways.Distinct().Count(), 1, "four towers fell the same way");
        }

        /// <summary>
        /// Satchel charges (MatchOptions.Satchel; the plan the owner approved, 2026-10-12: "sappers and engineers carry a
        /// few and throw one into an occupied position they reach; it hurts the men in it and knocks the position's
        /// protection down for a time"). Only sappers and engineers throw them, two a man, only into a position the enemy
        /// is in and within reach; a charge's burst breaches the position it bursts in, its protection down to four
        /// tenths, and up again when the time is out.
        /// </summary>
        [Test]
        public void Sappers_and_engineers_throw_satchels_into_held_positions_and_breach_them()
        {
            int thrown = 0, breached = 0;
            for (int seed = 1; seed <= 8; seed++)
            {
                var o = Scenarios.Firebase.Options(seed, null, Difficulty.Veteran);
                Assert.IsTrue(o.Satchel, "the game plays with satchel charges");
                var lm = new LiveMatch(o);
                var st = lm.State;
                while (!st.Over && st.Tick < 6000)
                {
                    int from = st.Events.Count;
                    lm.Step();
                    foreach (var e in st.Events.Skip(from))
                    {
                        if (e.Kind == EventKind.SatchelThrown)
                        {
                            thrown++;
                            var by = st.Men[e.Id];
                            string card = st.Squads[by.Squad].Card;
                            Assert.IsTrue(card == "vc-sapper" || card == "us-engineer", $"a man of a {card} squad threw a satchel");
                            Assert.GreaterOrEqual(by.Satchels, 0);
                            Assert.Less(by.Satchels, Tune.SatchelsCarried);
                            // (Into a position; or, with Destruction, at a tower's foot: then no position.)
                            if (e.Target.HasValue)
                            {
                                var c = st.Cover[e.Target.Value];
                                Assert.LessOrEqual(JsMath.Hypot(c.X - by.X, c.Z - by.Z), Tune.SatchelReach + 1e-9, "thrown from beyond reach");
                            }
                        }
                        if (e.Kind == EventKind.Breached)
                        {
                            breached++;
                            var c = st.Cover[e.Id];
                            Assert.AreEqual(Tune.SatchelBreachTicks, c.Breached, 1, "(its countdown runs in the tick it bursts)");
                            double was = c.Quality * Tune.CoverBoost;
                            Assert.LessOrEqual(Combat.CoverValue(st, c), System.Math.Min(0.85, was) * Tune.SatchelBreachLeft + 1e-9, "a breached position kept its protection");
                        }
                    }
                }
                foreach (var c in st.Cover) Assert.LessOrEqual(c.Breached, Tune.SatchelBreachTicks);
            }
            Assert.Greater(thrown, 0, "no satchel was thrown in eight matches");
            Assert.Greater(breached, 0, "no satchel ever breached a position");
        }

        /// <summary>
        /// The mortar emplaced (MatchOptions.Emplace; the plan the owner approved, 2026-10-12: "the mortar that sets up and
        /// packs up"). With the rule, no mortar round leaves a tube its man had not stood still at for the time it takes
        /// to set one up, and a mortar man whose squad moves off stays where he is while he packs it up. Without it, he
        /// fires as soon as he is steady, and goes when his squad goes.
        /// </summary>
        [Test]
        public void With_the_mortar_emplaced_it_fires_only_set_up_and_packs_up_before_it_moves()
        {
            int fired = 0, early = 0, packed = 0;
            for (int seed = 1; seed <= 6; seed++)
            {
                var o = Scenarios.Firebase.Options(seed, null, Difficulty.Veteran);
                Assert.IsTrue(o.Emplace, "the game plays with the mortar emplaced");
                var lm = new LiveMatch(o);
                var st = lm.State;
                var still = new Dictionary<int, int>();
                while (!st.Over && st.Tick < 4000)
                {
                    int from = st.Events.Count;
                    var before = st.Men.Where(x => x.Alive && x.Weapon == Weapon.Mortar).ToDictionary(x => x.Id, x => (x.X, x.Z, x.Pack));
                    lm.Step();
                    foreach (var e in st.Events.Skip(from).Where(e => e.Kind == EventKind.Launch && st.Men[e.Id].Weapon == Weapon.Mortar))
                    {
                        fired++;
                        if (st.Men[e.Id].Rest < Tune.EmplaceTicks) early++;
                    }
                    foreach (var kv in before)
                    {
                        var man = st.Men[kv.Key];
                        if (man.Pack > kv.Value.Pack)
                        {
                            packed++;
                            Assert.AreEqual(kv.Value.X, man.X, 1e-9, "a mortar man walked off while he was packing up");
                        }
                    }
                }
            }
            Assert.Greater(fired, 0, "no mortar fired in six matches");
            Assert.AreEqual(0, early, "a mortar fired before it was set up");
            Assert.Greater(packed, 0, "no mortar was ever packed up to move");
        }

        /// <summary>
        /// GRENADES (MatchOptions.FragCard; the owner, 2026-10-14: "add the frag grenades to the HUD, make them cheaper
        /// like smokes and have the closest squad launch them, not automatic for player side"). With the rule off the
        /// card is in no deck. With it on, a player's men throw no grenade of their own (the computer's still do);
        /// played where his squad can reach, up to three of the nearest squad's men throw there, for SMOKE's 10 CP; played
        /// where nobody can, nothing happens and nothing is spent.
        /// </summary>
        [Test]
        public void With_the_grenade_card_the_nearest_squad_throws_where_it_is_played_and_nobody_else_of_the_players()
        {
            var card = Deck.Grenades(Side.Us);
            Assert.Less(card.Cost, Deck.Find(Side.Us, "us-smoke").Cost + 1, "dearer than smoke");
            Assert.IsFalse(Deck.Has(new LiveMatch(new MatchOptions { Seed = 1 }).State, Side.Us, card), "the card is in the deck with its rule off");

            int Thrown(bool rule, Side by)
            {
                int n = 0;
                for (int seed = 1; seed <= 4; seed++)
                {
                    var o = Scenarios.Firebase.Options(seed, Side.Us, Difficulty.Veteran);
                    o.UsMeans = Scenarios.Firebase.Us;      // (a squad to open with: this player raises nobody, PLAN §12.52)
                    o.FragCard = rule;
                    var lm = new LiveMatch(o);
                    while (!lm.State.Over && lm.State.Tick < 4000) lm.Step();
                    n += lm.State.Events.Count(e => e.Kind == EventKind.GrenadeThrown && e.Side == by);
                }
                return n;
            }
            Assert.Greater(Thrown(false, Side.Us), 0, "the test's premise: without the rule the player's men throw");
            Assert.AreEqual(0, Thrown(true, Side.Us), "with the rule a player's men threw grenades of their own");
            Assert.Greater(Thrown(true, Side.Vc), 0, "the computer's men stopped throwing");

            // Played.
            var po = Scenarios.Firebase.Options(2, Side.Us, Difficulty.Veteran);
            po.UsMeans = Scenarios.Firebase.Us;
            var m = new LiveMatch(po);
            var st = m.State;
            Assert.IsTrue(Deck.Has(st, Side.Us, card));
            for (int t = 0; t < 200; t++) m.Step();
            st.Cp[(int)Side.Us] = 100;
            // Where nobody of his can reach: refused, and nothing spent.
            Assert.IsFalse(Deck.Buy(st, Side.Us, card, 0, null, Tune.HalfLength), "the card was played where nobody could throw");
            Assert.AreEqual(100, st.Cp[(int)Side.Us], 1e-9);
            // In front of his nearest man.
            var man = st.Men.First(x => x.Alive && x.Side == Side.Us && x.Grenades > 0 && x.High == 0);
            int lane = System.Math.Abs(man.Z - Tune.Lanes[0]) < System.Math.Abs(man.Z - Tune.Lanes[1]) ? 0 : 1;
            double at = man.X + Combat.Advance(Side.Us) * 9;
            int before = st.Events.Count;
            Assert.IsTrue(Deck.Buy(st, Side.Us, card, lane, null, at));
            Assert.AreEqual(100 - card.Cost, st.Cp[(int)Side.Us], 1e-9);
            var threw = st.Events.Skip(before).Where(e => e.Kind == EventKind.GrenadeThrown).ToList();
            Assert.That(threw.Count, Is.InRange(1, Tune.FragCardThrowers));
            int squad = st.Men[threw[0].Id].Squad;
            foreach (var e in threw)
            {
                Assert.AreEqual(squad, st.Men[e.Id].Squad, "men of two squads threw");
                Assert.AreEqual(at, e.X.Value, 6.0, "a grenade went far from where the card was played");
            }
            Assert.IsNotNull(Deck.Blocked(st, Side.Us, card), "it can be played again at once");
        }

        /// <summary>
        /// The jet makes one pass. With the rule its bombs are a stick: eight of them, a tenth of a second
        /// apart, walking away from its own side along its line, each where <see cref="Inbound.Stick"/> says
        /// (which is how the view can show them falling before they burst). Without it the strike is what
        /// it was: bursts scattered over its circle for four seconds.
        /// </summary>
        [Test]
        public void With_call_ins_inbound_the_jet_drops_its_bombs_in_one_pass()
        {
            foreach (bool inbound in new[] { false, true })
            {
                var lm = new LiveMatch(new MatchOptions { Seed = 2, Us = Plan.NoReinforce, Vc = Plan.NoReinforce, Inbound = inbound });
                var st = lm.State;
                st.Cp[(int)Side.Us] = 1000;
                lm.Issue(Command.Buy(Side.Us, "us-airstrike", 0, 10));
                lm.Step();
                int called = st.Tick;
                var strike = st.Areas.Single(a => a.Kind == AreaKind.Barrage);
                int id = strike.Id; double ax = strike.X, az = strike.Z;
                for (int t = 0; t < 400 && st.Areas.Any(a => a.Id == id); t++) lm.Step();
                Assert.IsFalse(st.Areas.Any(a => a.Id == id), "the strike never ended");
                var bombs = st.Events.Where(e => e.Kind == EventKind.Shell && e.Id == id).ToList();
                if (!inbound)
                {
                    Assert.Greater(bombs.Last().Tick - bombs.First().Tick, Tune.SalvoEvery * 2, "with the rule off the strike is seconds of bursts");
                    continue;
                }
                Assert.AreEqual(Tune.StickBombs, bombs.Count);
                Assert.AreEqual((Tune.StickBombs - 1) * Tune.StickGap, bombs.Last().Tick - bombs.First().Tick, "the stick is not one pass");
                Assert.AreEqual(Tune.JetWait, bombs.First().Tick - called, 2, "the bombs did not wait for the jet");
                for (int k = 0; k < bombs.Count; k++)
                {
                    Inbound.Stick(Side.Us, id, ax, az, k, out double x, out double z);
                    Assert.AreEqual(x, bombs[k].X.Value, 1e-9, $"bomb {k} is not where the stick puts it");
                    Assert.AreEqual(z, bombs[k].Z.Value, 1e-9);
                    Assert.LessOrEqual(System.Math.Abs(x - ax), 11.0, "a bomb fell outside the strike's circle");
                    if (k > 0) Assert.Greater(x, bombs[k - 1].X.Value, "the stick does not walk away from the jet's own side");
                }
            }
        }

        /// <summary>
        /// A call takes its time: the guns 3.5 s with a ranging round a second before, the jet 5 s, smoke 2.5 s,
        /// the medevac 8 s. Until it is in, nothing of it is on the ground: no shell, no smoke to hide behind, no
        /// steadier line. Without the rule each lands in the tick it is called, as it always did.
        /// </summary>
        [Test]
        public void A_call_in_arrives_when_it_arrives_and_not_before()
        {
            foreach (bool inbound in new[] { false, true })
            {
                var lm = new LiveMatch(new MatchOptions { Seed = 2, Us = Plan.NoReinforce, Vc = Plan.NoReinforce, Inbound = inbound });
                var st = lm.State;
                st.Cp[(int)Side.Us] = 1000;
                int Shells() => st.Events.Count(e => e.Kind == EventKind.Shell);
                lm.Issue(Command.Buy(Side.Us, "us-arty", 0, 10));
                lm.Step();
                var guns = st.Areas.Single(a => a.Kind == AreaKind.Barrage);
                if (!inbound)
                {
                    Assert.AreEqual(0, guns.Wait);
                    Assert.IsFalse(st.Events.Any(e => e.Kind == EventKind.CallIn), "a call was announced with the rule off");
                    for (int t = 0; t < 4; t++) lm.Step();
                    Assert.Greater(Shells(), 0, "with the rule off the guns' rounds land at once");
                    continue;
                }
                var call = st.Events.Single(e => e.Kind == EventKind.CallIn);
                Assert.AreEqual(Inbound.Guns, call.Rounds);
                Assert.AreEqual(Tune.GunsWait, (int)call.Amount.Value, "the call does not say how long");
                int at = st.Tick;
                while (Shells() == 0 && st.Tick < at + 400) lm.Step();
                Assert.GreaterOrEqual(st.Tick - at, Tune.GunsWait - 1, "the guns' rounds landed before the guns could have fired them");
                Assert.LessOrEqual(st.Tick - at, Tune.GunsWait + 3);
                var ranging = st.Events.Single(e => e.Kind == EventKind.CallIn && e.Rounds == Inbound.Ranging);
                Assert.AreEqual(Tune.RangingAhead, (int)ranging.Amount.Value, "the ranging round is not a second ahead of the rest");

                // Smoke that has not landed hides nobody; a medevac steadies the line when it is in.
                lm.Issue(Command.Buy(Side.Us, "us-smoke", 1, 0));
                lm.Step();
                var smoke = st.Areas.Single(a => a.Kind == AreaKind.Smoke);
                Assert.AreEqual(Tune.SmokeWait - 1, smoke.Wait, 1);
                int life = smoke.Ticks;
                for (int t = 0; t < Tune.SmokeWait - 5; t++) lm.Step();
                Assert.Greater(smoke.Wait, 0);
                Assert.AreEqual(life, smoke.Ticks, "smoke that has not landed is burning down");
                foreach (var m in st.Men.Where(m => m.Alive && m.Side == Side.Us)) m.Pin = 0.9;
                double was = st.Morale[(int)Side.Us] = 0.5;
                lm.Issue(Command.Buy(Side.Us, "us-medevac", 0, 0));
                lm.Step();
                Assert.AreEqual(was, st.Morale[(int)Side.Us], 0.02, "the medevac steadied the line as it was called");
                for (int t = 0; t < Tune.DustoffWait + 2; t++) { lm.Step(); st.Cp[(int)Side.Us] = 1000; }
                Assert.Greater(st.Morale[(int)Side.Us], was + 0.05, "the medevac never came");
                Assert.IsFalse(st.Areas.Any(a => a.Kind == AreaKind.Relief), "the medevac is still on its way");
            }
        }

        // --- scenarios (PLAN §12.27, Phase S) -----------------------------------------------

        /// <summary>
        /// A scenario is a match, and a match is a function of its seed. Recorded by `simcs scenario hash N`:
        /// each scenario as the game plays it against a Veteran, with nobody playing.
        /// </summary>
        [TestCase("firebase", 1, 4075, 3468518776u, 3500188192u, 3782637812u, "us morale broke")]
        // (The Wire and First Light again 2026-10-15: the Americans open with fewer men, PLAN §12.52.)
        // (The Wire and The Earth Fights again the same day: a medevac in them was brought down, MatchOptions.Shootdown.)
        [TestCase("the-wire", 3, 5089, 2894096662u, 1502400082u, 742317118u, "us morale broke")]
        [TestCase("the-wire", 1, 6000, 2960744763u, 3634959008u, 708840890u, "us held out")]
        [TestCase("first-light", 7, 5971, 4151385837u, 3561230234u, 2227549972u, "us took the line")]
        [TestCase("first-light", 3, 8400, 1396377366u, 4206417747u, 2531584742u, "vc held the line")]
        [TestCase("earth-fights", 1, 7735, 744082594u, 3112911749u, 3312621114u, "us morale broke")]
        public void A_scenario_is_pinned_and_still_a_pure_function_of_its_seed(
            string id, int seed, int ticks, uint at100, uint at1000, uint final, string reason)
        {
            var sc = Scenarios.ById(id);
            Assert.IsNotNull(sc, $"no scenario called {id}");
            var (a, m) = Played(sc.Options(seed, null, Difficulty.Veteran));
            var (b, _) = Played(sc.Options(seed, null, Difficulty.Veteran));
            CollectionAssert.AreEqual(a, b, "two runs of the same seed diverged");
            Assert.AreEqual(ticks, m.State.Tick);
            Assert.AreEqual(at100, a[100], "tick 100");
            Assert.AreEqual(at1000, a[1000], "tick 1000");
            Assert.AreEqual(final, a[a.Count - 1], "final tick");
            Assert.AreEqual(reason, m.State.Reason);
        }

        /// <summary>
        /// The firebase, first on the list, is the match the game had before there were scenarios: every rule
        /// on, the game's economy, both sides alike and taking ground, no clock. And the options a scenario
        /// leaves alone are the baseline's, so a match made without one is what it was.
        /// </summary>
        [Test]
        public void The_firebase_scenario_is_the_match_the_game_always_had()
        {
            Assert.AreSame(Scenarios.Firebase, Scenarios.All[0]);
            Assert.AreSame(Scenarios.Firebase, Scenarios.ById("firebase"));
            Assert.IsNull(Scenarios.ById("no such scenario"));
            foreach (var sc in Scenarios.All)
            {
                Assert.AreSame(sc, Scenarios.ById(sc.Id), $"{sc.Id} is listed twice, or not by its name");
                Assert.IsNotEmpty(sc.Title); Assert.IsNotEmpty(sc.DateLine); Assert.IsNotEmpty(sc.Briefing); Assert.IsNotEmpty(sc.Orders);
                Assert.AreEqual(sc.Timed, sc.ClockSeconds > 0, $"{sc.Id}: a clock and no use for one, or the other way about");
                if (sc.Timed) Assert.Less(sc.ClockSeconds * Tune.TickHz, Tune.MaxTicks, $"{sc.Id}: its clock is longer than a match may run");
                if (sc.Timed) Assert.IsNotNull(sc.Length, $"{sc.Id}: a clock, and a length left to the start screen as well");
            }

            var o = Scenarios.Firebase.Options(5, Side.Vc, Difficulty.Elite, MatchLength.Long);
            Assert.IsTrue(o.Frag && o.SquadSmoke && o.Drill && o.Fieldcraft && o.Arms && o.Senses && o.Gunnery && o.Ammo
                          && o.Tactics && o.Fortune && o.FireControl && o.Ballistics && o.Vantage && o.Commander && o.Cohesion && o.Bearing && o.Inbound && o.FragCard && o.Emplace && o.Satchel && o.Destruction, "a rule the game plays with is off");
            Assert.IsFalse(o.ComputerPosts);
            Assert.AreEqual(Scenario.CpRate, o.CpRate); Assert.AreEqual(Scenario.StartCp, o.StartCp); Assert.AreEqual(Scenario.OpeningStrength, o.OpeningStrength);
            Assert.AreEqual(Scenario.CostFor(Difficulty.Elite), o.MusterCost);
            Assert.AreEqual(MatchLength.Long, o.Length); Assert.AreEqual(Scenario.RateFor(MatchLength.Long), o.MoraleRate);
            Assert.AreEqual(Side.Vc, o.Player);
            Assert.AreSame(Plan.Ceiling, o.Us); Assert.AreSame(Plan.Ceiling, o.Vc);
            Assert.AreEqual(MatchMode.Standard, o.Mode); Assert.AreEqual(Side.Vc, o.Attacker); Assert.AreEqual(0, o.ClockSeconds); Assert.IsNull(o.Objectives);
            Assert.IsNull(o.UsDeck); Assert.IsNull(o.VcDeck);
            Assert.AreSame(Maps.Firebase, o.Map); Assert.AreEqual(17, o.Cover.Count);
            Assert.IsTrue(Scenarios.Firebase.Offers(Side.Us) && Scenarios.Firebase.Offers(Side.Vc), "the firebase is either side's");
            Assert.Greater(Scenario.CostFor(Difficulty.Recruit), Scenario.CostFor(Difficulty.Veteran));
            Assert.Greater(Scenario.CostFor(Difficulty.Veteran), Scenario.CostFor(Difficulty.Elite));

            // Tick for tick the match those rules gave with the game's economy.
            var kept = Borne(5, tempo: true);
            kept.Inbound = true; kept.FragCard = true; kept.Emplace = true; kept.Satchel = true; kept.Destruction = true;
            kept.MusterCost = Scenario.CostFor(Difficulty.Veteran); kept.MoraleRate = Scenario.RateFor(MatchLength.Standard);
            var (was, _) = Played(kept);
            var (now, _) = Played(Scenarios.Firebase.Options(5, null, Difficulty.Veteran));
            CollectionAssert.AreEqual(was, now, "the firebase scenario is not the match the game had");

            // And what a scenario does not touch is the baseline's.
            var baseline = new MatchOptions();
            Assert.AreEqual(MatchMode.Standard, baseline.Mode); Assert.AreEqual(Side.Vc, baseline.Attacker); Assert.AreEqual(0, baseline.ClockSeconds);
            foreach (var has in new[] { baseline.UsMeans, baseline.VcMeans })
            {
                Assert.AreEqual(1, has.Income); Assert.AreEqual(1, has.Purse); Assert.AreEqual(1, has.Will); Assert.AreEqual(0, has.Opening);
            }
            var st = Match.Create(new MatchOptions { Seed = 1 });
            Assert.AreEqual(0, st.ClockTicks); Assert.AreEqual(-1, Match.ClockLeft(st)); Assert.IsEmpty(st.Objectives);
            foreach (var side in Match.Sides)
                foreach (var card in Deck.For(side))
                    // (GRENADES is a card only with its rule, MatchOptions.FragCard: off here, as every rule is.)
                    Assert.AreEqual(card != Deck.Grenades(side), Deck.Has(st, side, card), $"{card.Id} is missing from a match that named no deck, or GRENADES is in one without its rule");
        }

        /// <summary>
        /// Shootdown (MatchOptions.Shootdown; the owner, 2026-10-15: "have the chopper have a chance to be knocked down
        /// by an RPG that turns into it spinning losing control and crashing into flames", PLAN §12.53). A medevac brought
        /// down does not steady the line; it comes down where its event says, when it says, and bursts there as a shell
        /// does. By the dice, The Wire's rocket men bring one down now and then; and the rule draws only from its own
        /// stream, so a match in which none is brought down is the match without it, to the bit.
        /// </summary>
        [Test]
        public void A_medevac_can_be_brought_down_and_crashes_where_its_event_says()
        {
            var m = new LiveMatch(Scenarios.Firebase.Options(4, null, Difficulty.Veteran));
            var st = m.State;
            Assert.IsTrue(st.Shootdown, "the game plays with the rule");
            for (int t = 0; t < 400; t++) m.Step();
            st.Cp[(int)Side.Us] = 100;
            Assert.IsTrue(Deck.Buy(st, Side.Us, Deck.Find(Side.Us, "us-medevac"), 0, null, 0), "the medevac was not called");
            var a = st.Areas.Last(x => x.Kind == AreaKind.Relief);
            while (a.Wait > Tune.ShootdownAt + 1) m.Step();
            double was = st.Morale[(int)Side.Us];
            Assert.IsTrue(Shootdown.Force(st, a), "there was nobody to bring it down");
            var shot = st.Events.Last(e => e.Kind == EventKind.ShotDown);
            Assert.AreEqual(a.Id, shot.Target);
            Assert.AreEqual(Side.Vc, st.Men[shot.Id].Side, "it was brought down by its own side");
            int crash = shot.Tick + (int)shot.Amount.Value;
            while (st.Tick <= crash && !st.Over) m.Step();
            Assert.IsFalse(st.Areas.Any(x => x.Id == a.Id), "it is still on its way");
            Assert.IsTrue(st.Events.Any(e => e.Kind == EventKind.Shell && e.Tick == crash && e.X == shot.X && e.Z == shot.Z), "it did not burst where and when it came down");
            Assert.Less(st.Morale[(int)Side.Us] - was, 0.05, "brought down, it steadied the line anyway");

            // By the dice: in The Wire, now and then.
            int down = 0;
            for (int seed = 1; seed <= 6; seed++)
                down += Played(Scenarios.TheWire.Options(seed, null, Difficulty.Veteran)).m.State.Events.Count(e => e.Kind == EventKind.ShotDown);
            Assert.Greater(down, 0, "the rocket men of six sieges brought down no medevac");

            // Its own stream: a match in which none is brought down is the match without the rule.
            var on = Scenarios.Firebase.Options(1, null, Difficulty.Veteran);
            var off = Scenarios.Firebase.Options(1, null, Difficulty.Veteran);
            off.Shootdown = false;
            var (hOn, mOn) = Played(on);
            var (hOff, _) = Played(off);
            Assert.IsFalse(mOn.State.Events.Any(e => e.Kind == EventKind.ShotDown), "the test's premise: none brought down in this match");
            Assert.AreEqual(hOff[hOff.Count - 1], hOn[hOn.Count - 1], "the rule moved a match in which it brought nothing down");
        }

        /// <summary>
        /// The owner, 2026-10-15: "too lobsided for US ... no or less spawns at the start for US" (PLAN §12.52). In the
        /// firebase a player's Americans open with nobody, and are not wiped out for it: he raises his first squad with
        /// his opening points, and the match goes on to be won or lost. The computer's Americans still open with theirs.
        /// </summary>
        [Test]
        public void A_players_Americans_open_the_firebase_with_nobody_and_raise_their_own()
        {
            var o = Scenarios.Firebase.Options(5, Side.Us, Difficulty.Veteran);
            var m = new LiveMatch(o);
            var st = m.State;
            Assert.AreEqual(0, st.Men.Count(x => x.Side == Side.Us), "the player's Americans opened with men");
            Assert.Greater(st.Men.Count(x => x.Side == Side.Vc), 0, "the VC opened with nobody");
            var card = Deck.For(Side.Us).First(c => c.Group == CardGroup.Line && Deck.Has(st, Side.Us, c));
            Assert.IsNull(Deck.Blocked(st, Side.Us, card), "he cannot raise his first squad with his opening points");
            for (int t = 0; t < 60; t++) m.Step();
            Assert.IsFalse(st.Over, $"the match ended before he had raised anyone: {st.Reason}");
            m.Issue(Command.Buy(Side.Us, card.Id, 0, 0));
            m.Step();
            Assert.Greater(st.Men.Count(x => x.Side == Side.Us && x.Alive), 0, "his first squad was not raised");

            var computer = new LiveMatch(Scenarios.Firebase.Options(5, Side.Vc, Difficulty.Veteran));
            Assert.GreaterOrEqual(computer.State.Men.Count(x => x.Side == Side.Us), Scenario.OpeningStrength, "the computer's Americans opened with nobody");
        }

        /// <summary>
        /// What a scenario gives one side and not the other: the points it opens with, its income, the men it
        /// opens with, and how fast its will runs out. Each is a share of the match's own, so the difficulty and
        /// the length still turn the whole.
        /// </summary>
        [Test]
        public void A_scenario_gives_each_side_its_own_means()
        {
            var o = Scenarios.Firebase.Options(3, null, Difficulty.Veteran);
            o.Us = o.Vc = Plan.NoReinforce;         // nobody spends, so the points can be counted
            o.UsMeans = new Means { Purse = 0.5 };
            o.VcMeans = new Means { Purse = 2, Income = 1.5, Opening = 12, Will = 0.5 };
            var m = new LiveMatch(o);
            var st = m.State;
            Assert.AreEqual(Scenario.StartCp * 0.5, st.Cp[(int)Side.Us]);
            Assert.AreEqual(Scenario.StartCp * 2, st.Cp[(int)Side.Vc]);
            int us = st.Men.Count(x => x.Side == Side.Us), vc = st.Men.Count(x => x.Side == Side.Vc);
            Assert.GreaterOrEqual(vc, 12, "the VC opened with fewer men than the scenario gave them");
            Assert.Less(us, 12, "the Americans opened with the VC's strength");
            Assert.GreaterOrEqual(us, Scenario.OpeningStrength);

            bool usFell = false, vcFell = false;
            int ticks = 0;
            while (!st.Over && st.Tick < 4000 && !(usFell && vcFell))
            {
                int from = st.Events.Count;
                m.Step(); ticks++;
                for (int i = from; i < st.Events.Count; i++)
                {
                    var e = st.Events[i];
                    if (e.Kind != EventKind.MoraleLost) continue;
                    double whole = Tune.MoralePerCasualty * st.MoraleScale;
                    if (e.Side == Side.Us) { usFell = true; Assert.AreEqual(whole, e.Amount, 1e-12, "an American's death cost his side something other than the match's rate"); }
                    else { vcFell = true; Assert.AreEqual(whole * 0.5, e.Amount, 1e-12, "a VC's death did not cost his side half"); }
                }
            }
            Assert.IsTrue(usFell && vcFell, "nobody fell on one side: the test saw nothing");
            Assert.AreEqual(Scenario.StartCp * 0.5 + ticks * Scenario.CpRate * Tune.Dt, st.Cp[(int)Side.Us], 1e-6, "the Americans' income");
            Assert.AreEqual(Scenario.StartCp * 2 + ticks * Scenario.CpRate * 1.5 * Tune.Dt, st.Cp[(int)Side.Vc], 1e-6, "the VC's income is not half as much again");
        }

        /// <summary>
        /// A siege (The Wire): the clock is the defender's. The match never runs past it; a match that reaches it
        /// ends there, his, with his will unbroken; and he can still be broken before it. The side that holds is
        /// given the plan that holds, and the one that comes on the plan that takes ground.
        /// </summary>
        [Test]
        public void In_a_siege_the_clock_is_the_defenders()
        {
            var wire = Scenarios.TheWire;
            Assert.AreEqual(MatchMode.Siege, wire.Mode);
            Assert.IsTrue(wire.Offers(Side.Us)); Assert.IsFalse(wire.Offers(Side.Vc));
            var o = wire.Options(1, Side.Us, Difficulty.Veteran, MatchLength.Skirmish);
            Assert.AreSame(Plan.Defend, o.Us, "the side that holds was not told to");
            Assert.AreSame(Plan.Ceiling, o.Vc);
            Assert.AreEqual(MatchLength.Standard, o.Length, "a clock is its own length: the start screen's was used");

            int held = 0, broke = 0;
            // (Seeds 1 and 3 since the Americans opened with fewer, PLAN §12.52: seed 2 now ends with them wiped out.)
            foreach (int seed in new[] { 1, 3 })
            {
                var m = new LiveMatch(wire.Options(seed, null, Difficulty.Veteran));
                var st = m.State;
                Assert.AreEqual((int)(wire.ClockSeconds * Tune.TickHz), st.ClockTicks);
                Assert.AreEqual(wire.ClockSeconds, Match.ClockLeft(st), 1e-9);
                while (!st.Over && st.Tick < m.Cap)
                {
                    m.Step();
                    Assert.LessOrEqual(st.Tick, st.ClockTicks, $"seed {seed}: the match ran past its clock");
                }
                if (st.Reason == "us held out")
                {
                    held++;
                    Assert.AreEqual(st.ClockTicks, st.Tick, "he held out, and not at the end of the clock");
                    Assert.AreEqual(Side.Us, st.Winner);
                    Assert.Greater(st.Morale[(int)Side.Us], 0);
                    Assert.AreEqual(0, Match.ClockLeft(st));
                    Assert.AreEqual(EventKind.MatchOver, st.Events[st.Events.Count - 1].Kind);
                }
                else
                {
                    broke++;
                    Assert.AreEqual("us morale broke", st.Reason);
                    Assert.Less(st.Tick, st.ClockTicks);
                    Assert.AreEqual(Side.Vc, st.Winner);
                }
            }
            Assert.AreEqual(1, held, "the clock never ran out on a siege");
            Assert.AreEqual(1, broke, "the defender was never broken before his clock");
        }

        /// <summary>
        /// An assault (First Light): the attacker wins the moment the line he was sent for is his in both lanes
        /// at once, and not with one end of it; the clock is the defender's. The line is the position in front
        /// of the defender's last in each lane. And the side that comes on is the one that moves in the opening:
        /// here the Americans, against VC who open in their positions.
        /// </summary>
        [Test]
        public void An_assault_is_won_by_having_the_line_in_both_lanes_at_once()
        {
            var light = Scenarios.FirstLight;
            Assert.AreEqual(MatchMode.Assault, light.Mode);
            var o = light.Options(7, null, Difficulty.Veteran);
            Assert.AreSame(Plan.Ceiling, o.Us); Assert.AreSame(Plan.Defend, o.Vc);
            var m = new LiveMatch(o);
            var st = m.State;
            Assert.AreEqual(Side.Us, st.Attacker);

            // The line: the berm in the near lane and the trench in the far, each one back from the VC's last.
            Assert.AreEqual(2, st.Objectives.Length);
            Cover near = st.Cover[st.Objectives[0]], far = st.Cover[st.Objectives[1]];
            Assert.AreEqual(CoverKind.Berm, near.Kind); Assert.AreEqual(26.5, near.X); Assert.Greater(near.Z, 0);
            Assert.AreEqual(CoverKind.Trench, far.Kind); Assert.AreEqual(29.0, far.X); Assert.Less(far.Z, 0);
            var last = Match.LastLine(st, Side.Vc);
            Assert.AreEqual(36.5, st.Cover[last[0]].X); Assert.AreEqual(39.0, st.Cover[last[1]].X);
            var ours = Match.LastLine(st, Side.Us);
            Assert.AreEqual(-39.0, st.Cover[ours[0]].X); Assert.AreEqual(-36.0, st.Cover[ours[1]].X);
            Assert.IsEmpty(Match.LastLine(st, Side.Vc, 99), "a line further back than the map has positions");

            // Whose it is, is whose both ends are.
            Assert.IsFalse(Match.Taken(st));
            near.Owner = Side.Us; far.Owner = Side.Vc;
            Assert.IsFalse(Match.Taken(st), "one end of the line, and it was called taken");
            far.Owner = Side.Us;
            Assert.IsTrue(Match.Taken(st));
            near.Owner = null; far.Owner = null;

            // The opening: the VC are in their positions and stay; the Americans come on.
            Assert.GreaterOrEqual(st.Men.Count(x => x.Side == Side.Vc && x.Cover >= 0), 4, "the VC did not open dug in");
            Assert.AreEqual(0, st.Men.Count(x => x.Side == Side.Us && x.Cover >= 0), "the Americans opened in positions: they are the ones coming on");
            for (int i = 0; i < 40 && st.Phase == Phase.Opening; i++) m.Step();
            Assert.AreEqual(Phase.Opening, st.Phase, "the test's premise: forty ticks in, nobody has met");
            foreach (var sq in st.Squads)
                Assert.AreEqual(sq.Side == Side.Us ? Order.Advance : Order.Hold, sq.Order, $"a {sq.Side} squad in the opening");

            // Seed 7 (5 until call-ins took their time to land, PLAN §12.45; 3 until the Americans opened with fewer, §12.52):
            // the line is taken. The match ends on the tick it is, and not before.
            while (!st.Over && st.Tick < m.Cap)
            {
                Assert.IsFalse(Match.Taken(st), $"tick {st.Tick}: the line was the Americans' and the match went on");
                m.Step();
            }
            Assert.AreEqual("us took the line", st.Reason);
            Assert.AreEqual(Side.Us, st.Winner);
            Assert.IsTrue(Match.Taken(st));
            Assert.Less(st.Tick, st.ClockTicks);
            Assert.Greater(st.Morale[(int)Side.Vc], 0, "they had broken anyway");

            // Seed 3 (2, as above, then 4 until the jet's bombs fell in one pass, then 8 until §12.52): it is not, and the clock is the defender's.
            var (_, late) = Played(light.Options(3, null, Difficulty.Veteran));
            Assert.AreEqual("vc held the line", late.State.Reason);
            Assert.AreEqual(Side.Vc, late.State.Winner);
            Assert.AreEqual(late.State.ClockTicks, late.State.Tick);
            Assert.IsFalse(Match.Taken(late.State));
        }

        /// <summary>
        /// A scenario's deck: a side has the cards it names and no others. The player cannot buy one it has not
        /// got, and the computer's commander neither raises it nor plays it.
        /// </summary>
        [Test]
        public void A_side_has_only_the_cards_its_scenario_gives_it()
        {
            var o = Scenarios.Firebase.Options(4, Side.Vc, Difficulty.Veteran);
            o.UsDeck = new[] { "us-rifle", "us-smoke" };
            o.VcDeck = new[] { "vc-cell", "vc-squad", "vc-punji" };
            var m = new LiveMatch(o);
            var st = m.State;
            st.Cp[(int)Side.Vc] = 200;
            Assert.AreEqual("not in this operation", Deck.Blocked(st, Side.Vc, Deck.Find(Side.Vc, "vc-tunnel")));
            Assert.IsNull(Deck.Blocked(st, Side.Vc, Deck.Find(Side.Vc, "vc-punji")));
            Assert.IsFalse(Deck.Has(st, Side.Us, Deck.Find(Side.Us, "us-arty")));
            Assert.IsTrue(Deck.Has(st, Side.Us, Deck.Find(Side.Us, "us-rifle")));
            m.Issue(Command.Buy(Side.Vc, "vc-tunnel", 0, 0));
            m.Step();
            Assert.IsFalse(m.Log[0].Accepted, "a card the scenario had not given was bought");

            int opened = st.Squads.Count;
            while (!st.Over && st.Tick < 5000)
            {
                if (st.Tick % 20 == 0 && Deck.Blocked(st, Side.Vc, Deck.Find(Side.Vc, "vc-squad")) == null) m.Issue(Command.Buy(Side.Vc, "vc-squad", st.Squads.Count % 2, 0));
                m.Step();
            }
            var raised = st.Squads.Skip(opened).Where(q => q.Side == Side.Us).ToList();
            Assert.Greater(raised.Count, 3, "the test's premise: the computer raised squads");
            foreach (var q in raised) Assert.AreEqual("us-rifle", q.Card, "the computer raised a squad the scenario had not given it");
            Assert.AreEqual(0, st.Events.Count(e => e.Kind == EventKind.Shell && e.Side == Side.Us), "shells fell, from a side with no artillery and no aircraft");
        }

        [Test]
        public void A_broken_squad_takes_no_orders_and_a_player_commands_only_his_own()
        {
            var st = Match.Create(new MatchOptions { Seed = 1 });
            var sq = st.Squads.First(s => s.Side == Side.Us);
            sq.Order = Order.Fallback;
            Assert.IsFalse(Match.OrderSquad(st, sq.Id, Order.Advance), "broken squad obeyed");
            Assert.IsTrue(Match.OrderSquad(st, sq.Id, Order.Fallback));

            var lm = new LiveMatch(new MatchOptions { Seed = 1 });
            var enemy = lm.State.Squads.First(s => s.Side == Side.Vc);
            lm.Issue(Command.OrderSquad(Side.Us, enemy.Id, Order.Hold));
            lm.Step();
            Assert.IsFalse(lm.Log[0].Accepted, "the US player ordered a VC squad");
        }
    }
}
