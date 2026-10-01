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
            foreach (var name in new[] { "Match.cs", "Combat.cs", "Squads.cs", "Deck.cs", "LiveMatch.cs", "Frag.cs", "SquadSmoke.cs", "Drill.cs", "Fieldcraft.cs" })
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
                    var st = Match.Create(new MatchOptions { Seed = 7 });
                    st.Cp[(int)side] = 999;
                    foreach (var m in st.Men) m.Pin = 0.6;
                    st.Morale[(int)side] = 0.5;
                    int men = st.Men.Count, areas = st.Areas.Count;
                    double morale = st.Morale[(int)side];
                    double pin = st.Men.Where(m => m.Side == side).Sum(m => m.Pin);

                    Assert.IsTrue(Deck.Buy(st, side, card, 1, new Rng(7).Fork("sim"), 5), card.Id);
                    bool changed = st.Men.Count != men || st.Areas.Count != areas
                                   || st.Morale[(int)side] != morale
                                   || st.Men.Where(m => m.Side == side).Sum(m => m.Pin) != pin;
                    Assert.IsTrue(changed, $"{card.Id} took {card.Cost} CP and changed nothing");
                    Assert.AreEqual(999 - card.Cost, st.Cp[(int)side], 1e-9, card.Id);
                    Assert.Greater(Deck.CooldownLeft(st, side, card), 0, card.Id);
                    Assert.IsFalse(Deck.Buy(st, side, card, 1, new Rng(7).Fork("sim"), 5), $"{card.Id} bought twice inside its cooldown");
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
        [TestCase(1, true, 4037, 3326673888u, 217291906u, 638893086u, "us morale broke")]
        [TestCase(7, false, 3373, 807552359u, 4218661143u, 3430317594u, "us morale broke")]
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
            Assert.IsFalse(m.Log[1].Accepted, "a berm took a lever: only trenches, walls and bunkers have one");
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
