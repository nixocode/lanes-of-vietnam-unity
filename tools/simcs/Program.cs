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
                    "muddle" => Muddle(args),
                    "lever" => LeverTrace(args),
                    "arms" => ArmsTable(args),
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
            bool frag = a.Contains("frag"), smoke = a.Contains("smoke"), drill = a.Contains("drill"), craft = a.Contains("fieldcraft"), onMap = a.Contains("map"), arms = a.Contains("arms");
            a = a.Where(x => x != "frag" && x != "smoke" && x != "drill" && x != "fieldcraft" && x != "map" && x != "arms").ToArray();
            int seed = a.Length > 1 ? int.Parse(a[1]) : 3;
            var usPlan = Plan.ByName(a.Length > 2 ? a[2] : "ceiling");
            var m = new LiveMatch(new MatchOptions { Seed = seed, Us = usPlan, Vc = Plan.Ceiling, Cover = Map.Cover(), Length = MatchLength.Standard, Frag = frag, SquadSmoke = smoke, Drill = drill, Fieldcraft = craft, Arms = arms });
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
            bool frag = a.Contains("frag"), smoke = a.Contains("smoke"), drill = a.Contains("drill"), craft = a.Contains("fieldcraft"), onMap = a.Contains("map"), arms = a.Contains("arms");
            a = a.Where(x => x != "frag" && x != "smoke" && x != "drill" && x != "fieldcraft" && x != "map" && x != "arms").ToArray();
            int seed = a.Length > 1 ? int.Parse(a[1]) : 1;
            var m = new LiveMatch(new MatchOptions { Seed = seed, Us = Plan.Ceiling, Vc = Plan.Ceiling, Cover = onMap ? Map.Cover() : null, Frag = frag, SquadSmoke = smoke, Drill = drill, Fieldcraft = craft, Arms = arms });
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
            bool frag = a.Contains("frag"), smoke = a.Contains("smoke"), drill = a.Contains("drill"), craft = a.Contains("fieldcraft"), onMap = a.Contains("map"), arms = a.Contains("arms");
            a = a.Where(x => x != "frag" && x != "smoke" && x != "drill" && x != "fieldcraft" && x != "map" && x != "arms").ToArray();
            int seed = a.Length > 1 ? int.Parse(a[1]) : 3, ticks = a.Length > 2 ? int.Parse(a[2]) : 2400;
            var m = new LiveMatch(new MatchOptions { Seed = seed, Us = Plan.Ceiling, Vc = Plan.Ceiling, Cover = Map.Cover(), Length = MatchLength.Standard, Frag = frag, SquadSmoke = smoke, Drill = drill, Fieldcraft = craft, Arms = arms });
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

        /// <summary>
        /// What each weapon does in play, as the game plays it: men who carried
        /// it, shots or rounds, the distance they were fired at, kills. `arms [seed] [seeds]`.
        /// </summary>
        private static int ArmsTable(string[] a)
        {
            var nums = a.Skip(1).Where(x => int.TryParse(x, out _)).Select(int.Parse).ToArray();
            int seed0 = nums.Length > 0 ? nums[0] : 1, seeds = nums.Length > 1 ? nums[1] : 12;
            var carried = new Dictionary<Weapon, int>(); var shots = new Dictionary<Weapon, int>();
            var dist = new Dictionary<Weapon, double>(); var far = new Dictionary<Weapon, double>(); var kills = new Dictionary<Weapon, int>();
            for (int seed = seed0; seed < seed0 + seeds; seed++)
            {
                var m = new LiveMatch(Game(seed, new[] { "fieldcraft", "arms" }));
                var st = m.State;
                int from = 0;
                while (!st.Over && st.Tick < m.Cap)
                {
                    m.Step();
                    for (int i = from; i < st.Events.Count; i++)
                    {
                        var e = st.Events[i];
                        if (e.Kind == EventKind.Fire || e.Kind == EventKind.Launch)
                        {
                            var by = st.Men[e.Id]; var at = st.Men[e.Target.Value];
                            double d = e.Kind == EventKind.Launch ? Combat.Dist(by.X, by.Z, e.X.Value, e.Z.Value) : Combat.Dist(by, at);
                            shots[by.Weapon] = shots.GetValueOrDefault(by.Weapon) + 1;
                            dist[by.Weapon] = dist.GetValueOrDefault(by.Weapon) + d;
                            far[by.Weapon] = Math.Max(far.GetValueOrDefault(by.Weapon), d);
                            if (e.Kind == EventKind.Fire && i + 1 < st.Events.Count && st.Events[i + 1].Kind == EventKind.Kill && st.Events[i + 1].Id == at.Id)
                                kills[by.Weapon] = kills.GetValueOrDefault(by.Weapon) + 1;
                        }
                        if (e.Kind == EventKind.GrenadeBlast)
                        {
                            // The kills a burst makes follow it in the log; whose it was is the round's own record.
                            int k = 0;
                            for (int j = i + 1; j < st.Events.Count && st.Events[j].Tick == e.Tick && st.Events[j].Kind != EventKind.GrenadeBlast; j++)
                                if (st.Events[j].Kind == EventKind.Kill) k++;
                            var w = blasts.TryGetValue((seed, e.Id), out var bw) ? bw : Weapon.Rifle;
                            if (w != Weapon.Rifle) kills[w] = kills.GetValueOrDefault(w) + k;
                        }
                    }
                    from = st.Events.Count;
                    foreach (var g in st.Grenades) blasts[(seed, g.Id)] = g.By;
                }
                foreach (var man in st.Men) carried[man.Weapon] = carried.GetValueOrDefault(man.Weapon) + 1;
            }
            Console.WriteLine($"  seeds {seed0}..{seed0 + seeds - 1}, a match:");
            Console.WriteLine("  weapon    men   shots  at (mean m)  furthest  range  kills  kills a man");
            foreach (Weapon w in Enum.GetValues(typeof(Weapon)))
            {
                if (!carried.ContainsKey(w)) continue;
                int n = shots.GetValueOrDefault(w);
                Console.WriteLine($"  {w,-8} {carried[w] / (double)seeds,5:F1} {n / (double)seeds,7:F1} {(n > 0 ? dist[w] / n : 0),10:F1} {far.GetValueOrDefault(w),9:F1} {Arms.Of(w).Range,6:F0} {kills.GetValueOrDefault(w) / (double)seeds,6:F1} {kills.GetValueOrDefault(w) / (double)Math.Max(1, carried[w]),8:F2}");
            }
            return 0;
        }
        private static readonly Dictionary<(int, int), Weapon> blasts = new Dictionary<(int, int), Weapon>();

        /// <summary>A squad called to a held trench and then sent out of it, tick by tick: `lever [seed]`.</summary>
        private static int LeverTrace(string[] a)
        {
            int seed = a.Length > 1 ? int.Parse(a[1]) : 2;
            int trench = Map.Cover().First(c => c.Kind == CoverKind.Trench && c.X < 0 && c.Z > 0).Id;
            var o = Game(seed, new[] { "fieldcraft" });
            o.Vc = Plan.Defend;
            var m = new LiveMatch(o);
            var st = m.State;
            m.Issue(Command.SetLever(Side.Us, trench, Lever.Hold));
            for (int t = 0; t < 1400 && !st.Over; t++)
            {
                if (t == 1000) m.Issue(Command.SetLever(Side.Us, trench, Lever.Go));
                m.Step();
                if (t % 50 != 0 && !(t > 1000 && t < 1012)) continue;
                foreach (var sq in st.Squads.Where(q => q.Side == Side.Us && q.Lane == 0))
                {
                    var men = Squads.Roster(st, sq.Id);
                    Console.WriteLine($"  t{st.Tick} sq{sq.Id} {sq.Order} target {sq.Target} anchor {sq.AnchorX:F1} held {sq.Held} sent {sq.Sent} halted {sq.Halted} | " + string.Join(" ", men.Select(x => $"{x.X:F1}{(x.Cover == trench ? "T" : x.Cover >= 0 ? "c" : "")}{(x.Vault > 0 ? "v" : "")}{(a.Contains("more") ? $"[r{x.Rank} p{x.Place} pin{x.Pin:F2} z{x.Z:F2} {x.Posture.ToString()[0]}]" : "")}")));
                }
            }
            return 0;
        }

        private static MatchOptions Game(int seed, string[] a) => new MatchOptions
        {
            Seed = seed, Us = Plan.Ceiling, Vc = Plan.Ceiling, Cover = Map.Cover(), Length = MatchLength.Standard,
            Frag = !a.Contains("nofrag"), SquadSmoke = !a.Contains("nosmoke"), Drill = !a.Contains("nodrill"),
            Fieldcraft = a.Contains("fieldcraft"), Arms = a.Contains("arms"),
        };

        /// <summary>
        /// How much a match looks like a muddle rather than a fight, as the
        /// game plays it (the owner, playtest 3: "they run circles around
        /// themselves, cross each other"). `muddle [seed] [ticks] [flags]`.
        ///   through   a living man walks past a living enemy within 5 m of him
        ///   on top    man-seconds spent within 0.6 m of a friend
        ///   crossed   two men of a squad, within 2 m along the lane, change sides
        ///             (clear of each other by 0.3 m one way, then the other)
        ///   about     a man's direction of travel swings by over 120 degrees
        ///   point blank  enemy pairs within 3 m, in man-seconds
        /// </summary>
        private static int Muddle(string[] a)
        {
            var nums = a.Skip(1).Where(x => int.TryParse(x, out _)).Select(int.Parse).ToArray();
            int seed0 = nums.Length > 0 ? nums[0] : 3, ticks = nums.Length > 1 ? nums[1] : 2400, seeds = nums.Length > 2 ? nums[2] : 1;
            double through = 0, onTop = 0, crossed = 0, about = 0, blank = 0, minutes = 0, men = 0;
            var where = new Dictionary<string, double>();
            var turned = new Dictionary<string, double>();
            int shown = 0;
            for (int seed = seed0; seed < seed0 + seeds; seed++)
            {
                var m = new LiveMatch(Game(seed, a));
                var st = m.State;
                var prev = new Dictionary<int, (double x, double z)>();
                var sides = new Dictionary<long, int>();
                var head = new Dictionary<int, (double x, double z)>();
                for (int t = 0; t < ticks && !st.Over; t++)
                {
                    var before = new Dictionary<int, (double x, double z)>();
                    foreach (var man in st.Men) if (man.Alive) before[man.Id] = (man.X, man.Z);
                    m.Step();
                    var live = st.Men.Where(x => x.Alive).ToList();
                    for (int i = 0; i < live.Count; i++)
                    {
                        var p = live[i];
                        if (!before.TryGetValue(p.Id, out var p0)) continue;
                        double vx = p.X - p0.x, vz = p.Z - p0.z;
                        if (vx * vx + vz * vz > 0.02 * 0.02)
                        {
                            if (head.TryGetValue(p.Id, out var h) && h.x * vx + h.z * vz < -0.5 * Math.Sqrt((h.x * h.x + h.z * h.z) * (vx * vx + vz * vz)))
                            {
                                about++;
                                var psq = st.Squads[p.Squad];
                                string why = psq.Order == Order.Fallback ? "falling back"
                                    : live.Any(e => e.Side != p.Side && (e.X - p.X) * (e.X - p.X) + (e.Z - p.Z) * (e.Z - p.Z) < 49) ? "in a close fight"
                                    : live.Any(e => e.Side == p.Side && e != p && (e.X - p.X) * (e.X - p.X) + (e.Z - p.Z) * (e.Z - p.Z) < 1.0) ? "getting round a friend"
                                    : p.Place >= 0 ? "taking a place in cover"
                                    : Math.Abs(vx) < Math.Abs(vz) ? "across the lane" : "along the lane";
                                turned[why] = turned.GetValueOrDefault(why) + 1;
                                if (a.Contains("why2") && why == "along the lane" && shown++ < 16)
                                {
                                    var fr = live.Where(e => e.Side == p.Side && e != p).OrderBy(e => (e.X - p.X) * (e.X - p.X) + (e.Z - p.Z) * (e.Z - p.Z)).First();
                                    before.TryGetValue(fr.Id, out var f0);
                                    Console.WriteLine($"    t{st.Tick} sq{p.Squad} {psq.Order} tgt {psq.Target} | man {p.Id} r{p.Rank} p{p.Place} cover {p.Cover} pin {p.Pin:F2} ({p0.x:F2},{p0.z:F2})->({p.X:F2},{p.Z:F2}) was ({h.x:F2},{h.z:F2}) | friend {fr.Id} sq{fr.Squad} r{fr.Rank} p{fr.Place} pin {fr.Pin:F2} ({f0.x:F2},{f0.z:F2})->({fr.X:F2},{fr.Z:F2})");
                                }
                            }
                            head[p.Id] = (vx, vz);
                        }
                        for (int j = i + 1; j < live.Count; j++)
                        {
                            var q = live[j];
                            if (!before.TryGetValue(q.Id, out var q0)) continue;
                            double dx = p.X - q.X, dz = p.Z - q.Z, d2 = dx * dx + dz * dz;
                            if (p.Side != q.Side)
                            {
                                if (d2 < 9) blank += Tune.Dt;
                                if (Math.Abs(dz) < 5 && d2 < 36 && Math.Sign(p0.x - q0.x) != Math.Sign(dx) && Math.Sign(p0.x - q0.x) != 0) through++;
                            }
                            else
                            {
                                if (d2 < 0.36)
                                {
                                    onTop += Tune.Dt;
                                    string why = st.Squads[p.Squad].Order == Order.Fallback || st.Squads[q.Squad].Order == Order.Fallback ? "falling back"
                                        : Math.Abs(p.X) > Tune.HalfLength * 0.85 ? "at the map's end"
                                        : live.Any(e => e.Side != p.Side && (e.X - p.X) * (e.X - p.X) + (e.Z - p.Z) * (e.Z - p.Z) < 49) ? "in a close fight"
                                        : p.Cover >= 0 || q.Cover >= 0 ? (p.Squad == q.Squad ? "in cover, one squad" : "in cover, two squads")
                                        : p.Squad == q.Squad ? "in the open, one squad" : "in the open, two squads";
                                    where[why] = where.GetValueOrDefault(why) + Tune.Dt;
                                    if (a.Contains("why") && why.StartsWith("in cover, one") && shown++ < 12)
                                        Console.WriteLine($"    t{st.Tick} sq{p.Squad} {st.Squads[p.Squad].Order} target {st.Squads[p.Squad].Target} anchor {st.Squads[p.Squad].AnchorX:F1} | man {p.Id} rank {p.Rank} place {p.Place}@{p.PlaceCover} cover {p.Cover} pin {p.Pin:F2} ({p.X:F2},{p.Z:F2}) | man {q.Id} rank {q.Rank} place {q.Place}@{q.PlaceCover} cover {q.Cover} pin {q.Pin:F2} ({q.X:F2},{q.Z:F2})");
                                }
                                // A change of sides: clear of each other across the lane (0.3 m) one way, then the other.
                                if (p.Squad == q.Squad && Math.Abs(dx) < 2 && Math.Abs(dz) > 0.3)
                                {
                                    long pair = ((long)p.Id << 20) | (uint)q.Id;
                                    int now = Math.Sign(dz);
                                    if (sides.TryGetValue(pair, out int was) && was != now)
                                    {
                                        crossed++;
                                        if (a.Contains("why3") && shown++ < 14)
                                            Console.WriteLine($"    t{st.Tick} sq{p.Squad} {st.Squads[p.Squad].Order} tgt {st.Squads[p.Squad].Target} | man {p.Id} r{p.Rank} p{p.Place} cover {p.Cover} pin {p.Pin:F2} ({p.X:F2},{p.Z:F2}) | man {q.Id} r{q.Rank} p{q.Place} cover {q.Cover} pin {q.Pin:F2} ({q.X:F2},{q.Z:F2})");
                                    }
                                    sides[pair] = now;
                                }
                            }
                        }
                    }
                }
                if (a.Contains("when"))
                    foreach (var e in st.Events.Where(e => e.Kind == EventKind.Melee || e.Kind == EventKind.VaultOut || e.Kind == EventKind.PositionTaken).Take(24))
                        Console.WriteLine($"    seed {seed} t{e.Tick} {e.Kind} at x {(e.X ?? st.Men[e.Id].X):F1} z {(e.Z ?? st.Men[e.Id].Z):F1}");
                minutes += st.Tick * Tune.Dt / 60;
                men += st.Men.Count;
            }
            Console.WriteLine($"  seeds {seed0}..{seed0 + seeds - 1}, {minutes:F1} minutes of fighting, {men / seeds:F0} men a match; a minute:");
            Console.WriteLine($"  walked through the enemy  {through / minutes:F1}");
            Console.WriteLine($"  point blank (under 3 m)   {blank / minutes:F1} man-seconds");
            Console.WriteLine($"  stood on a friend         {onTop / minutes:F1} man-seconds");
            foreach (var kv in where.OrderByDescending(k => k.Value)) Console.WriteLine($"      {kv.Key,-24} {kv.Value / minutes:F1}");
            Console.WriteLine($"  crossed a squadmate       {crossed / minutes:F1}");
            Console.WriteLine($"  turned about              {about / minutes:F1}");
            foreach (var kv in turned.OrderByDescending(k => k.Value)) Console.WriteLine($"      {kv.Key,-24} {kv.Value / minutes:F1}");
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
            bool frag = a.Contains("frag"), smoke = a.Contains("smoke"), drill = a.Contains("drill"), craft = a.Contains("fieldcraft"), onMap = a.Contains("map"), arms = a.Contains("arms");
            a = a.Where(x => x != "frag" && x != "smoke" && x != "drill" && x != "fieldcraft" && x != "map" && x != "arms").ToArray();
            int count = int.Parse(a[1]);
            long smokes = 0;
            double lostCas = 0, lostGround = 0;
            var us = Plan.ByName(a.Length > 2 ? a[2] : "ceiling");
            var vc = Plan.ByName(a.Length > 3 ? a[3] : "ceiling");
            int from = a.Length > 4 ? int.Parse(a[4]) : 1;
            long thrown = 0, blasts = 0, byGrenade = 0;
            long melees = 0, vaults = 0, taken = 0, through = 0;
            int wu = 0, wv = 0, draw = 0;
            double secs = 0, cas = 0, worst = 0;
            var reasons = new Dictionary<string, int>();
            var sw = Stopwatch.StartNew();
            for (int s = from; s < from + count; s++)
            {
                var t0 = sw.Elapsed.TotalMilliseconds;
                var r = Match.Run(new MatchOptions { Seed = s, Us = us, Vc = vc, Cover = onMap ? Map.Cover() : null, Frag = frag, SquadSmoke = smoke, Drill = drill, Fieldcraft = craft, Arms = arms });
                smokes += r.EventCounts.GetValueOrDefault(EventKind.AreaStart);
                lostCas += r.MoraleLostToCasualties[0] + r.MoraleLostToCasualties[1];
                lostGround += r.MoraleLostToGround[0] + r.MoraleLostToGround[1];
                thrown += r.EventCounts.GetValueOrDefault(EventKind.GrenadeThrown);
                melees += r.EventCounts.GetValueOrDefault(EventKind.Melee);
                vaults += r.EventCounts.GetValueOrDefault(EventKind.VaultIn) + r.EventCounts.GetValueOrDefault(EventKind.VaultOut);
                taken += r.EventCounts.GetValueOrDefault(EventKind.PositionTaken);
                through += r.EventCounts.GetValueOrDefault(EventKind.Through);
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
            if (craft) Console.WriteLine($"  fieldcraft       {melees / (double)count:F1} blows hand to hand, {vaults / (double)count:F1} climbs, {taken / (double)count:F1} positions taken, {through / (double)count:F1} rounds through a man into another, per match");
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
