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
            Console.WriteLine("simcs parity <trace.json> | run [seed] [us] [vc] | seeds N [us] [vc] [from] | determinism | audit | bench | events [seed] [us plan]");
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
            int seed = a.Length > 1 ? int.Parse(a[1]) : 3;
            var usPlan = Plan.ByName(a.Length > 2 ? a[2] : "ceiling");
            var m = new LiveMatch(new MatchOptions { Seed = seed, Us = usPlan, Vc = Plan.Ceiling, Cover = Map.Cover(), Length = MatchLength.Standard });
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
                }
            }
            Console.WriteLine($"  seed {seed}: {st.Tick} ticks, {shells.Count} shells, {fire.Values.Sum()} shots");
            Console.WriteLine("  busiest seconds (tick: shots): " + string.Join(", ", fire.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Key * 20}: {kv.Value}")));
            Console.WriteLine("  us-arty affordable at ticks: " + string.Join(", ", afford.Take(12)));
            Console.WriteLine("  shells (tick @ x, z): " + string.Join(", ", shells.Take(24).Select(s => $"{s.tick} @ {s.x:F0},{s.z:F0}")));
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
            int count = int.Parse(a[1]);
            var us = Plan.ByName(a.Length > 2 ? a[2] : "ceiling");
            var vc = Plan.ByName(a.Length > 3 ? a[3] : "ceiling");
            int from = a.Length > 4 ? int.Parse(a[4]) : 1;
            int wu = 0, wv = 0, draw = 0;
            double secs = 0, cas = 0, worst = 0;
            var reasons = new Dictionary<string, int>();
            var sw = Stopwatch.StartNew();
            for (int s = from; s < from + count; s++)
            {
                var t0 = sw.Elapsed.TotalMilliseconds;
                var r = Match.Run(new MatchOptions { Seed = s, Us = us, Vc = vc });
                worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds - t0);
                if (r.Winner == Side.Us) wu++; else if (r.Winner == Side.Vc) wv++; else draw++;
                reasons[r.Reason] = reasons.TryGetValue(r.Reason, out int c) ? c + 1 : 1;
                secs += r.Seconds;
                cas += r.Casualties[0] + r.Casualties[1];
            }
            var (lo, hi) = Wilson(wu, count);
            Console.WriteLine($"  {us.Name} (us) vs {vc.Name} (vc), seeds {from}..{from + count - 1}");
            Console.WriteLine($"  us / vc / draw   {wu} / {wv} / {draw}");
            Console.WriteLine($"  us win rate      {100.0 * wu / count:F1}%   95% CI {100 * lo:F1}-{100 * hi:F1}%");
            Console.WriteLine($"  mean length      {secs / count:F1} s (cap {Tune.MaxTicks / Tune.TickHz} s)");
            Console.WriteLine($"  mean casualties  {cas / count:F1}");
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
