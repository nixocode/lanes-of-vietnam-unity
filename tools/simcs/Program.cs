using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using LanesOfVietnam.Sim;

namespace LanesOfVietnam.SimCs
{
    /// <summary>
    /// The simulation, headless — brief §10's simnode, in C#.
    ///
    ///   parity &lt;trace.json&gt;          tick-by-tick check against the TypeScript original
    ///   run [seed] [us] [vc]           one match
    ///   seeds N [us] [vc] [from]       a block of matches, summarised, with a Wilson interval
    ///   determinism                    same seed twice, identical hashes
    ///   audit                          every event kind and every ending reachable
    ///   bench                          wall clock per match
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0] : "run";
            try
            {
                return cmd switch
                {
                    "parity" => Parity(args[1]),
                    "run" => RunOne(args),
                    "seeds" => Seeds(args),
                    "determinism" => Determinism(),
                    "audit" => Audit(),
                    "bench" => Bench(),
                    "events" => Events(args),
                    "hash" => HashRun(args),
                    "wander" => Wander(args),
                    _ => Usage(),
                };
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                return 2;
            }
        }

        private static int Usage()
        {
            Console.WriteLine("simcs parity <trace.json> | run [seed] [us] [vc] | seeds N [us] [vc] [from] [frag] | determinism | audit | bench | events [seed] [us plan]");
            return 1;
        }

        private static (List<uint> hashes, LiveMatch m) Trace(int seed, Plan us, Plan vc, bool scripted)
        {
            var h = LanesOfVietnam.Sim.Parity.Trace(seed, us, vc, scripted, out var lm);
            return (h, lm);
        }

        private static ulong Bits(string hex) => Convert.ToUInt64(hex, 16);
        private static double D(string hex) => BitConverter.Int64BitsToDouble((long)Bits(hex));

        private static int Parity(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            int bad = 0;

            // The primitives first: if these differ, nothing downstream can agree.
            int hypotBad = 0, trigBad = 0, n = 0;
            long trigUlp = 0;
            long Ulp(double a, string hex) => Math.Abs(BitConverter.DoubleToInt64Bits(a) - (long)Bits(hex));
            foreach (var line in root.GetProperty("hypot").EnumerateArray())
            {
                var p = line.GetString().Split(' ');
                double got = JsMath.Hypot(D(p[0]), D(p[1]));
                if ((ulong)BitConverter.DoubleToInt64Bits(got) != Bits(p[2])) hypotBad++;
                n++;
            }
            foreach (var line in root.GetProperty("trig").EnumerateArray())
            {
                var p = line.GetString().Split(' ');
                double t = D(p[0]);
                long uc = Ulp(JsMath.Cos(t), p[1]), us = Ulp(JsMath.Sin(t), p[2]);
                if (uc != 0) trigBad++;
                if (us != 0) trigBad++;
                trigUlp = Math.Max(trigUlp, Math.Max(uc, us));
            }
            Console.WriteLine($"  hypot   {n - hypotBad}/{n} bit-identical to V8");
            Console.WriteLine($"  sin/cos {2 * n - trigBad}/{2 * n} bit-identical to this V8, worst {trigUlp} ulp");
            if (hypotBad > 0) bad++;
            // Sine and cosine are fdlibm on purpose (see JsMath): the one
            // implementation that is the same on every platform the game runs
            // on. This Node's V8 is built with glibc-derived trig instead, which
            // rounds a fraction of results differently by one unit in the last
            // place. More than one ulp would be a bug in the port, not a choice
            // of libm, and fails.
            if (trigUlp > 1) bad++;

            Console.WriteLine();
            Console.WriteLine("  seed  us        vc        script  ticks   result");
            foreach (var mt in root.GetProperty("matches").EnumerateArray())
            {
                int seed = mt.GetProperty("seed").GetInt32();
                var us = Plan.ByName(mt.GetProperty("us").GetString());
                var vc = Plan.ByName(mt.GetProperty("vc").GetString());
                bool scripted = mt.GetProperty("scripted").GetBoolean();
                var want = mt.GetProperty("hashes").EnumerateArray().Select(x => (uint)x.GetInt64()).ToList();
                var (got, lm) = Trace(seed, us, vc, scripted);

                int first = -1;
                for (int i = 0; i < Math.Min(want.Count, got.Count); i++)
                {
                    if (want[i] != got[i]) { first = i; break; }
                }
                if (first < 0 && want.Count != got.Count) first = Math.Min(want.Count, got.Count);
                string res = first < 0
                    ? $"identical, {got.Count} ticks — {lm.State.Reason}"
                    : $"DIVERGES at tick {first} (lengths {want.Count}/{got.Count})";
                if (first >= 0) bad++;
                Console.WriteLine($"  {seed,4}  {us.Name,-8}  {vc.Name,-8}  {(scripted ? "yes" : "no"),-6}  {lm.State.Tick,5}   {res}");
            }
            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "  parity: EXACT" : $"  parity: {bad} FAILED");
            return bad == 0 ? 0 : 1;
        }

        // --- matches ------------------------------------------------------------------

        /// <summary>
        /// A match as the game starts one (ceiling against ceiling, the map's
        /// cover), tick by tick: when shells land and where the firing is
        /// heaviest. For pointing FrameCapture at a moment worth looking at.
        /// </summary>
        private static int Events(string[] a)
        {
            bool frag = a.Contains("frag"), smoke = a.Contains("smoke"), drill = a.Contains("drill");
            a = a.Where(x => x != "frag" && x != "smoke" && x != "drill").ToArray();
            int seed = a.Length > 1 ? int.Parse(a[1]) : 3;
            var usPlan = Plan.ByName(a.Length > 2 ? a[2] : "ceiling");
            var m = new LiveMatch(new MatchOptions { Seed = seed, Us = usPlan, Vc = Plan.Ceiling, Cover = Map.Cover(), Length = MatchLength.Standard, Frag = frag, SquadSmoke = smoke, Drill = drill });
            var areas = new List<(int tick, double x, double z)>();
            var grenades = new List<(int tick, double x, double z)>();
            var st = m.State;
            var fire = new Dictionary<int, int>();
            var shells = new List<(int tick, double x, double z)>();
            int seen = 0;
            var arty = Deck.For(Side.Us).First(c => c.Id == "us-arty");
            var afford = new List<int>();
            while (!st.Over && st.Tick < 20 * 60 * 12)
            {
                m.Step();
                if (Deck.Blocked(st, Side.Us, arty) == null && (afford.Count == 0 || st.Tick - afford[^1] > 200)) afford.Add(st.Tick);
                if (st.Tick % 400 == 0) Console.WriteLine($"  tick {st.Tick}: US CP {st.Cp[0]:F1}, VC CP {st.Cp[1]:F1}");
                for (; seen < st.Events.Count; seen++)
                {
                    var e = st.Events[seen];
                    if (e.Kind == EventKind.Fire) fire[e.Tick / 20] = fire.GetValueOrDefault(e.Tick / 20) + 1;
                    if (e.Kind == EventKind.Shell) shells.Add((e.Tick, e.X ?? 0, e.Z ?? 0));
                    if (e.Kind == EventKind.GrenadeThrown) grenades.Add((e.Tick, e.X ?? 0, e.Z ?? 0));
                    if (e.Kind == EventKind.AreaStart) areas.Add((e.Tick, e.X ?? 0, e.Z ?? 0));
                }
            }
            Console.WriteLine($"  seed {seed}: {st.Tick} ticks, {shells.Count} shells, {fire.Values.Sum()} shots");
            Console.WriteLine("  busiest seconds (tick: shots): " + string.Join(", ", fire.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Key * 20}: {kv.Value}")));
            Console.WriteLine("  us-arty affordable at ticks: " + string.Join(", ", afford.Take(12)));
            if (smoke) Console.WriteLine("  smoke popped (tick @ x, z): " + string.Join(", ", areas.Take(12).Select(g => $"{g.tick} @ {g.x:F0},{g.z:F0}")));
            if (frag) Console.WriteLine("  grenades thrown (tick @ x, z): " + string.Join(", ", grenades.Take(20).Select(g => $"{g.tick} @ {g.x:F0},{g.z:F0}")));
            Console.WriteLine("  shells (tick @ x, z): " + string.Join(", ", shells.Take(24).Select(s => $"{s.tick} @ {s.x:F0},{s.z:F0}")));
            return 0;
        }

        /// <summary>A match's tick count, reason and hashes at ticks 100, 1000 and the end, for pinning in SimTests.</summary>
        private static int HashRun(string[] a)
        {
            bool frag = a.Contains("frag"), smoke = a.Contains("smoke"), drill = a.Contains("drill");
            a = a.Where(x => x != "frag" && x != "smoke" && x != "drill").ToArray();
            int seed = a.Length > 1 ? int.Parse(a[1]) : 1;
            var m = new LiveMatch(new MatchOptions { Seed = seed, Us = Plan.Ceiling, Vc = Plan.Ceiling, Frag = frag, SquadSmoke = smoke, Drill = drill });
            var h = new List<uint> { LanesOfVietnam.Sim.Parity.Hash(m.State, 0) };
            while (!m.State.Over && m.State.Tick < m.Cap) { m.Step(); h.Add(LanesOfVietnam.Sim.Parity.Hash(m.State, 0)); }
            Console.WriteLine($"  seed {seed}{(frag ? " frag" : "")}{(smoke ? " smoke" : "")}: {m.State.Tick} ticks, \"{m.State.Reason}\", at100 {h[100]}u, at1000 {h[Math.Min(1000, h.Count - 1)]}u, final {h[^1]}u");
            return 0;
        }

        private static int RunOne(string[] a)
        {
            int seed = a.Length > 1 ? int.Parse(a[1]) : 1;
            var us = Plan.ByName(a.Length > 2 ? a[2] : "ceiling");
            var vc = Plan.ByName(a.Length > 3 ? a[3] : "ceiling");
            var sw = Stopwatch.StartNew();
            var r = Match.Run(new MatchOptions { Seed = seed, Us = us, Vc = vc });
            sw.Stop();
            Console.WriteLine($"  seed {seed}, {us.Name} vs {vc.Name}: {r.Reason}, winner {(r.Winner.HasValue ? Match.Name(r.Winner.Value) : "none")}");
            Console.WriteLine($"  {r.Seconds:F1} s of match, casualties us {r.Casualties[0]} / vc {r.Casualties[1]}, morale us {r.Morale[0]:F3} / vc {r.Morale[1]:F3}");
            Console.WriteLine($"  morale lost — us: {r.MoraleLostToCasualties[0]:F3} casualties, {r.MoraleLostToGround[0]:F3} ground; vc: {r.MoraleLostToCasualties[1]:F3}, {r.MoraleLostToGround[1]:F3}");
            Console.WriteLine("  events: " + string.Join(", ", r.EventCounts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}")));
            Console.WriteLine($"  {sw.Elapsed.TotalMilliseconds:F1} ms wall clock");
            return 0;
        }

        /// <summary>
        /// How much a match dithers: order changes a squad, and how far a man
        /// walks along the lane for the ground he gains (the owner's "they walk
        /// up and down randomly"). `wander [seed] [ticks] [frag] [smoke] [drill]`.
        /// </summary>
        private static int Wander(string[] a)
        {
            bool frag = a.Contains("frag"), smoke = a.Contains("smoke"), drill = a.Contains("drill");
            a = a.Where(x => x != "frag" && x != "smoke" && x != "drill").ToArray();
            int seed = a.Length > 1 ? int.Parse(a[1]) : 3, ticks = a.Length > 2 ? int.Parse(a[2]) : 2400;
            var m = new LiveMatch(new MatchOptions { Seed = seed, Us = Plan.Ceiling, Vc = Plan.Ceiling, Cover = Map.Cover(), Length = MatchLength.Standard, Frag = frag, SquadSmoke = smoke, Drill = drill });
            var st = m.State;
            var order = new Dictionary<int, Order>(); var changes = new Dictionary<int, int>();
            var at = new Dictionary<int, (double x, double z)>(); var first = new Dictionary<int, double>();
            var along = new Dictionary<int, double>(); var across = new Dictionary<int, double>();
            var turns = new Dictionary<int, int>(); var heading = new Dictionary<int, int>();
            for (int t = 0; t < ticks && !st.Over; t++)
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
                    if (at.TryGetValue(man.Id, out var p))
                    {
                        double dx = man.X - p.x;
                        along[man.Id] = along.GetValueOrDefault(man.Id) + Math.Abs(dx);
                        across[man.Id] = across.GetValueOrDefault(man.Id) + Math.Abs(man.Z - p.z);
                        int dir = Math.Abs(dx) < 0.01 ? 0 : Math.Sign(dx);
                        if (dir != 0) { if (heading.TryGetValue(man.Id, out var h) && h != dir) turns[man.Id] = turns.GetValueOrDefault(man.Id) + 1; heading[man.Id] = dir; }
                    }
                    else first[man.Id] = man.X;
                    at[man.Id] = (man.X, man.Z);
                }
            }
            double net = along.Keys.Average(id => Math.Abs(at[id].x - first[id]));
            Console.WriteLine($"  seed {seed}{(frag ? " frag" : "")}{(smoke ? " smoke" : "")}{(drill ? " drill" : "")}, {st.Tick} ticks: {st.Men.Count} men, {st.Squads.Count} squads");
            Console.WriteLine($"  order changes a squad  {changes.Values.DefaultIfEmpty(0).Average():F1} (most {changes.Values.DefaultIfEmpty(0).Max()})");
            Console.WriteLine($"  a man walks            {along.Values.Average():F1} m along the lane for {net:F1} m gained, {across.Values.Average():F1} m across it");
            Console.WriteLine($"  a man turns round      {turns.Values.DefaultIfEmpty(0).Average():F1} times (most {turns.Values.DefaultIfEmpty(0).Max()})");
            return 0;
        }

        /// <summary>Wilson score interval, 95%: what a win rate over N matches can and cannot claim.</summary>
        private static (double lo, double hi) Wilson(int wins, int n, double z = 1.96)
        {
            if (n == 0) return (0, 1);
            double p = (double)wins / n;
            double den = 1 + z * z / n;
            double c = (p + z * z / (2 * n)) / den;
            double half = z * Math.Sqrt(p * (1 - p) / n + z * z / (4.0 * n * n)) / den;
            return (Math.Max(0, c - half), Math.Min(1, c + half));
        }

        private static int Seeds(string[] a)
        {
            // Rule flags anywhere after the count: "frag" turns grenades on.
            bool frag = a.Contains("frag"), smoke = a.Contains("smoke"), drill = a.Contains("drill");
            a = a.Where(x => x != "frag" && x != "smoke" && x != "drill").ToArray();
            int count = int.Parse(a[1]);
            long smokes = 0;
            double lostCas = 0, lostGround = 0;
            var us = Plan.ByName(a.Length > 2 ? a[2] : "ceiling");
            var vc = Plan.ByName(a.Length > 3 ? a[3] : "ceiling");
            int from = a.Length > 4 ? int.Parse(a[4]) : 1;
            long thrown = 0, blasts = 0, byGrenade = 0;
            int wu = 0, wv = 0, draw = 0;
            double secs = 0, cas = 0, worst = 0;
            var reasons = new Dictionary<string, int>();
            var sw = Stopwatch.StartNew();
            for (int s = from; s < from + count; s++)
            {
                var t0 = sw.Elapsed.TotalMilliseconds;
                var r = Match.Run(new MatchOptions { Seed = s, Us = us, Vc = vc, Frag = frag, SquadSmoke = smoke, Drill = drill });
                smokes += r.EventCounts.GetValueOrDefault(EventKind.AreaStart);
                lostCas += r.MoraleLostToCasualties[0] + r.MoraleLostToCasualties[1];
                lostGround += r.MoraleLostToGround[0] + r.MoraleLostToGround[1];
                thrown += r.EventCounts.GetValueOrDefault(EventKind.GrenadeThrown);
                blasts += r.EventCounts.GetValueOrDefault(EventKind.GrenadeBlast);
                byGrenade += r.GrenadeKills;
                worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds - t0);
                if (r.Winner == Side.Us) wu++; else if (r.Winner == Side.Vc) wv++; else draw++;
                reasons[r.Reason] = reasons.TryGetValue(r.Reason, out int c) ? c + 1 : 1;
                secs += r.Seconds;
                cas += r.Casualties[0] + r.Casualties[1];
            }
            var (lo, hi) = Wilson(wu, count);
            Console.WriteLine($"  {us.Name} (us) vs {vc.Name} (vc), seeds {from}..{from + count - 1}{(frag ? ", grenades on" : "")}{(smoke ? ", squad smoke on" : "")}");
            Console.WriteLine($"  us / vc / draw   {wu} / {wv} / {draw}");
            Console.WriteLine($"  us win rate      {100.0 * wu / count:F1}%   95% CI {100 * lo:F1}-{100 * hi:F1}%");
            Console.WriteLine($"  mean length      {secs / count:F1} s (cap {Tune.MaxTicks / Tune.TickHz} s)");
            Console.WriteLine($"  mean casualties  {cas / count:F1}");
            Console.WriteLine($"  morale lost      {lostCas / count:F2} to casualties, {lostGround / count:F2} to ground (both sides, per match)");
            if (smoke) Console.WriteLine($"  squad smoke      {smokes / (double)count:F1} canisters per match");
            if (frag) Console.WriteLine($"  grenades        {thrown / (double)count:F1} thrown, {blasts / (double)count:F1} went off, {byGrenade / (double)count:F1} killed, per match");
            Console.WriteLine("  endings:");
            foreach (var kv in reasons.OrderByDescending(kv => kv.Value)) Console.WriteLine($"    {kv.Value,4}  {kv.Key}");
            Console.WriteLine($"  wall clock       {sw.Elapsed.TotalMilliseconds / count:F1} ms per match, worst {worst:F1} ms");
            return 0;
        }

        private static int Determinism()
        {
            int bad = 0;
            foreach (int seed in new[] { 1, 2, 7, 99, 12345 })
            {
                var a = Trace(seed, Plan.Ceiling, Plan.Ceiling, true).hashes;
                var b = Trace(seed, Plan.Ceiling, Plan.Ceiling, true).hashes;
                bool ok = a.SequenceEqual(b);
                if (!ok) bad++;
                Console.WriteLine($"  seed {seed,6}  {a.Count} ticks  {(ok ? "identical" : "DIVERGED")}");
            }
            Console.WriteLine(bad == 0 ? "\n  determinism: ok" : $"\n  determinism: {bad} FAILED");
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// §9 finding 5 and finding 4: every event kind fires in play and every
        /// ending is reachable. The combinations are built so each ending has a
        /// case that can produce it.
        /// </summary>
        private static int Audit()
        {
            var seen = new HashSet<EventKind>();
            var endings = new HashSet<string>();
            var combos = new[]
            {
                (Plan.Ceiling, Plan.Ceiling), (Plan.Floor, Plan.Ceiling), (Plan.Ceiling, Plan.Floor),
                (Plan.Floor, Plan.Floor), (Plan.Defend, Plan.Defend), (Plan.Ceiling, Plan.Defend),
                (Plan.Defend, Plan.Ceiling), (Plan.Ceiling, Plan.NoReinforce), (Plan.NoReinforce, Plan.Ceiling),
                (Plan.NoReinforce, Plan.NoReinforce),
            };
            foreach (var (u, v) in combos)
            {
                for (int seed = 1; seed <= 40; seed++)
                {
                    var r = Match.Run(new MatchOptions { Seed = seed, Us = u, Vc = v });
                    foreach (var k in r.EventCounts.Keys) seen.Add(k);
                    endings.Add(r.Reason);
                }
            }
            // The deck's events need a match where the deck is used.
            for (int seed = 1; seed <= 6; seed++)
            {
                var (_, lm) = Trace(seed, Plan.NoReinforce, Plan.NoReinforce, true);
                foreach (var e in lm.State.Events) seen.Add(e.Kind);
            }
            Console.WriteLine("  events that fired:");
            int missing = 0;
            foreach (EventKind k in Enum.GetValues(typeof(EventKind)))
            {
                bool y = seen.Contains(k);
                if (!y) missing++;
                Console.WriteLine($"    {(y ? "yes" : "NO ")}  {k}");
            }
            var expected = new[] { "us morale broke", "vc morale broke", "us wiped out", "vc wiped out", "time, on morale", "time, drawn" };
            Console.WriteLine("\n  endings:");
            int unreached = 0;
            foreach (var e in expected)
            {
                bool y = endings.Contains(e);
                if (!y) unreached++;
                Console.WriteLine($"    {(y ? "yes" : "NO ")}  {e}");
            }
            Console.WriteLine(missing == 0 ? "\n  audit: every event fires" : $"\n  audit: {missing} event kinds never fired");
            if (unreached > 0) Console.WriteLine($"  {unreached} ending(s) not reached — rare or dead; the audit does not decide which");
            return missing == 0 ? 0 : 1;
        }

        private static int Bench()
        {
            // Warm the JIT, then time. The longest matchup is the one that matters.
            Match.Run(new MatchOptions { Seed = 1 });
            foreach (var (u, v) in new[] { (Plan.Ceiling, Plan.Ceiling), (Plan.Defend, Plan.Defend) })
            {
                var sw = Stopwatch.StartNew();
                double worst = 0; int ticks = 0;
                for (int s = 1; s <= 20; s++)
                {
                    var t0 = sw.Elapsed.TotalMilliseconds;
                    var r = Match.Run(new MatchOptions { Seed = s, Us = u, Vc = v });
                    ticks += r.Ticks;
                    worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds - t0);
                }
                Console.WriteLine($"  {u.Name} vs {v.Name}: {sw.Elapsed.TotalMilliseconds / 20:F1} ms per match, worst {worst:F1} ms, "
                                  + $"{1000.0 * sw.Elapsed.TotalMilliseconds / ticks:F2} µs per tick");
            }
            return 0;
        }
    }
}
