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
                    "aware" => Aware(args),
                    "brain" => Brain(args),
                    "watch" => Watch(args),
                    "lever" => LeverTrace(args),
                    "arms" => ArmsTable(args),
                    "player" => PlayerTable(args),
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
            var usPlan = Plan.ByName(a.Length > 2 && a[2] != "game" ? a[2] : "ceiling");
            // "game": the match as the game (and a capture of it) plays it, every rule on and its own economy.
            var m = a.Contains("game")
                ? new LiveMatch(Game(seed, new[] { "fieldcraft", "arms", "senses", "gunnery", "ammo", "tempo" }.Concat(a.Where(x => x == "tactics" || x == "fortune")).ToArray()))
                : new LiveMatch(new MatchOptions { Seed = seed, Us = usPlan, Vc = Plan.Ceiling, Cover = Map.Cover(), Length = MatchLength.Standard, Frag = frag, SquadSmoke = smoke, Drill = drill, Fieldcraft = craft, Arms = arms });
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
            bool frag = a.Contains("frag"), smoke = a.Contains("smoke"), drill = a.Contains("drill"), craft = a.Contains("fieldcraft"), onMap = a.Contains("map"), arms = a.Contains("arms"), senses = a.Contains("senses"), gunnery = a.Contains("gunnery"), ammo = a.Contains("ammo"), tactics = a.Contains("tactics"), fortune = a.Contains("fortune");
            a = a.Where(x => x != "frag" && x != "smoke" && x != "drill" && x != "fieldcraft" && x != "map" && x != "arms" && x != "senses" && x != "gunnery" && x != "ammo" && x != "tactics" && x != "fortune").ToArray();
            int seed = a.Length > 1 ? int.Parse(a[1]) : 1;
            var m = new LiveMatch(new MatchOptions { Seed = seed, Us = Plan.Ceiling, Vc = Plan.Ceiling, Cover = onMap ? Map.Cover() : null, Frag = frag, SquadSmoke = smoke, Drill = drill, Fieldcraft = craft, Arms = arms, Senses = senses, Gunnery = gunnery, Ammo = ammo, Tactics = tactics, Fortune = fortune });
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
                var m = new LiveMatch(Game(seed, new[] { "fieldcraft", "arms", "senses", "gunnery", "ammo" }.Concat(a.Where(x => x == "tactics" || x == "fortune")).ToArray()));
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

        /// <summary>
        /// The game's tempo against a player who is not the computer: one who
        /// buys nothing, and one who buys a rifle squad (or a cell) whenever he
        /// can afford one. `player [seeds] [us|vc]`: wins, and how long it took.
        /// </summary>
        private static int PlayerTable(string[] a)
        {
            int seeds = a.Length > 1 && int.TryParse(a[1], out int n) ? n : 24;
            double muster = a.Length > 2 && double.TryParse(a[2], out double mc) ? mc : 22;
            Console.WriteLine($"  the computer's squad costs it {muster}:");
            foreach (var side in new[] { Side.Us, Side.Vc })
            foreach (var style in new[] { "buys nothing", "buys line squads", "left to the plan" })
            {
                int wins = 0; double secs = 0, men = 0;
                var each = new List<double>();
                for (int seed = 1; seed <= seeds; seed++)
                {
                    var o = Game(seed, new[] { "fieldcraft", "arms", "senses", "gunnery", "ammo" }.Concat(a.Where(x => x == "skirmish" || x == "siege" || x == "tactics" || x == "fortune" || x.StartsWith("rate="))).ToArray());
                    o.CpRate = 1.6; o.StartCp = 20; o.OpeningStrength = 4; o.MusterCost = muster;      // GameRoot's
                    o.Player = style == "left to the plan" ? (Side?)null : side;
                    var m = new LiveMatch(o);
                    var card = Deck.For(side).First(c => c.Group == CardGroup.Line);
                    while (!m.State.Over && m.State.Tick < m.Cap)
                    {
                        if (style == "buys line squads" && m.State.Tick % 20 == 0 && Deck.Blocked(m.State, side, card) == null)
                            m.Issue(Command.Buy(side, card.Id, m.State.Squads.Count % 2, 0));
                        m.Step();
                    }
                    if (m.State.Winner == side) wins++;
                    secs += m.State.Tick * Tune.Dt;
                    each.Add(m.State.Tick * Tune.Dt);
                    men += m.State.Men.Count(x => x.Side == side);
                }
                double mean = secs / seeds, sd = Math.Sqrt(each.Sum(x => (x - mean) * (x - mean)) / seeds);
                Console.WriteLine($"  {side} player who {style,-18} wins {wins,2}/{seeds}  mean {mean,5:F0} s (from {each.Min():F0} to {each.Max():F0}, sd {sd:F0})  fielded {men / seeds:F0} men");
            }
            return 0;
        }

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

        /// <summary>GameRoot.RateFor: how fast will runs out in the game, by length.</summary>
        private static double GameRate(MatchLength l) => l == MatchLength.Skirmish ? 1.15 : l == MatchLength.Siege ? 1.0 : 0.9;

        private static MatchOptions Game(int seed, string[] a)
        {
            var o = new MatchOptions
            {
                Seed = seed, Us = Plan.Ceiling, Vc = Plan.Ceiling, Cover = Map.Cover(), Length = MatchLength.Standard,
                Frag = !a.Contains("nofrag"), SquadSmoke = !a.Contains("nosmoke"), Drill = !a.Contains("nodrill"),
                Fieldcraft = a.Contains("fieldcraft"), Arms = a.Contains("arms"), Senses = a.Contains("senses"), Gunnery = a.Contains("gunnery"), Ammo = a.Contains("ammo"),
                Tactics = a.Contains("tactics"), Fortune = a.Contains("fortune"),
            };
            // GameRoot's economy: one squad a side to open, points at the game's rate.
            if (a.Contains("skirmish")) o.Length = MatchLength.Skirmish;
            if (a.Contains("siege")) o.Length = MatchLength.Siege;
            // (GameRoot.CostFor(Veteran), GameRoot.RateFor(length).)
            if (a.Contains("tempo")) { o.CpRate = 1.6; o.StartCp = 20; o.OpeningStrength = 4; o.MusterCost = 29; o.MoraleRate = GameRate(o.Length); }
            foreach (var x in a) if (x.StartsWith("rate=")) o.MoraleRate = double.Parse(x.Substring(5), System.Globalization.CultureInfo.InvariantCulture);
            return o;
        }

        /// <summary>The squads tick by tick, as the game plays them: `watch seed from to [every] [flags]`.</summary>
        private static int Watch(string[] a)
        {
            var nums = a.Skip(1).Where(x => int.TryParse(x, out _)).Select(int.Parse).ToArray();
            int seed = nums[0], from = nums[1], to = nums[2], every = nums.Length > 3 ? nums[3] : 20;
            var m = new LiveMatch(Game(seed, a));
            var st = m.State;
            while (st.Tick < to && !st.Over)
            {
                int ev = st.Events.Count;
                m.Step();
                if (st.Tick < from) continue;
                if (a.Contains("events"))
                    for (int i = ev; i < st.Events.Count; i++)
                    {
                        var e = st.Events[i];
                        if (e.Kind == EventKind.Contact || e.Kind == EventKind.SquadBroke || e.Kind == EventKind.SquadSpawned || e.Kind == EventKind.Kill)
                            Console.WriteLine($"      t{e.Tick} {e.Kind} {e.Side} {e.Id}{(e.Target.HasValue ? " -> " + e.Target : "")}");
                    }
                // One man, tick by tick: `man=12`.
                foreach (var arg in a)
                {
                    if (!arg.StartsWith("man=")) continue;
                    var p = st.Men[int.Parse(arg.Substring(4))];
                    var q = st.Squads[p.Squad];
                    var foe = st.Men.Where(e => e.Alive && e.Side != p.Side).OrderBy(e => Combat.Dist(p, e)).FirstOrDefault();
                    var quarry = p.Alive && st.Fieldcraft ? Fieldcraft.Quarry(st, p, q) : null;
                    Console.WriteLine($"  t{st.Tick} man {p.Id} {(p.Alive ? "" : "DEAD ")}{p.X,6:F2},{p.Z,6:F2} {p.Posture,-8} pin {p.Pin:F2} rest {p.Rest,3} cd {p.Cooldown,2} cover {p.Cover,2} place {p.Place}@{p.PlaceCover} rank {p.Rank}{(p.Vault > 0 ? " vault" : "")}{(p.Reloading > 0 ? " reload" : "")} | sq{q.Id} {q.Order} {q.Task} tgt {q.Target} anchor {q.AnchorX:F1},{q.AnchorZ:F1} {(q.Halted ? "halt" : "go")} threat {q.Threat} | quarry {(quarry == null ? "-" : quarry.Id.ToString())} nearest {(foe == null ? "-" : $"{foe.Id} at {foe.X:F1},{foe.Z:F1} {Combat.Dist(p, foe):F1} m")}");
                }
                if (a.Any(x => x.StartsWith("man="))) continue;
                if ((st.Tick - from) % every != 0) continue;
                Console.WriteLine($"  t{st.Tick} {st.Phase} morale {st.Morale[0]:F2}/{st.Morale[1]:F2} smoke {st.Areas.Count(x => x.Kind == AreaKind.Smoke)}");
                foreach (var sq in st.Squads)
                {
                    var men = Squads.Roster(st, sq.Id);
                    if (men.Count == 0) continue;
                    Console.WriteLine($"    sq{sq.Id} {sq.Side} L{sq.Lane} {sq.Order,-8}{Senses.Describe(st, sq)} tgt {sq.Target,2} x {sq.AnchorX,5:F1} gap {(sq.Gap > 1e8 ? "  -" : sq.Gap.ToString("F0"))} {(sq.Halted ? "halt" : "    ")} | "
                        + string.Join(" ", men.Select(x => $"{x.X:F0},{x.Z:F0}{x.Posture.ToString()[0]}{(x.Cover >= 0 ? "c" : "")}{(x.Pin >= Tune.PinStop ? "!!" : x.Pin >= Tune.PinDrop ? "!" : "")}")));
                }
            }
            return 0;
        }

        /// <summary>
        /// What the squads know and what they do about it, as the game plays it
        /// (the owner, playtest 5: "don't see each other, stand around when not
        /// supposed to", "shots should only be fired when a squad spots
        /// another"). `aware [seed] [ticks] [seeds] [flags]`. Sight is
        /// <see cref="Senses.Sight"/>, worked out here whether or not the rule
        /// is on, so before and after are judged by one measure.
        ///   blind shots      fired at a squad the firer's own has not had in sight in the last 3 s
        ///   close, unaware   two enemy squads within 15 m, neither having fired at the other for 4 s
        ///   upright          men standing while a squad theirs has in sight is inside their weapon's range
        ///   idle in the open men at rest out of cover with a free place in cover within 12 m
        ///   contacts         from a squad first having an enemy squad in sight and in reach of its weapons (after 15 s
        ///                    with none) to its first shot, and to half of it being off its feet or in cover
        ///   passed           two enemy squads within 16 m changing which is further up the lane
        /// </summary>
        private static int Aware(string[] a)
        {
            var nums = a.Skip(1).Where(x => int.TryParse(x, out _)).Select(int.Parse).ToArray();
            int seed0 = nums.Length > 0 ? nums[0] : 3, ticks = nums.Length > 1 ? nums[1] : 2400, seeds = nums.Length > 2 ? nums[2] : 6;
            const int Keep = 60, Memory = 300;
            double minutes = 0, shots = 0, blindOwn = 0, blindSide = 0, blindDist = 0;
            double closeUnaware = 0, closeAware = 0;
            double contact = 0, uprightStill = 0, uprightMoving = 0;
            double idle = 0, idleContact = 0, manSeconds = 0;
            double bullets = 0, shotMoving = 0, shotStanding = 0, shotSteep = 0, shotBack = 0, movedDown = 0;
            double rounds = 0, reloads = 0;
            double bursts = 0, waiting = 0, handToHand = 0, passed = 0, unseenMan = 0, unseenBySquad = 0, acrossLanes = 0;
            var quiet = new Dictionary<string, double>(); var upright = new Dictionary<string, double>(); var ready = new Dictionary<string, double>(); var idleBy = new Dictionary<string, double>();
            var toShot = new List<double>(); var toGround = new List<double>();
            int sightings = 0, noShot = 0, noGround = 0;
            var blindBy = new Dictionary<string, double>();
            int shown = 0;
            for (int seed = seed0; seed < seed0 + seeds; seed++)
            {
                var m = new LiveMatch(Game(seed, a));
                var st = m.State;
                var inReach = new Dictionary<(int, int), int>();
                var saw = new Dictionary<(int, int), int>(); var shotAt = new Dictionary<(int, int), int>();
                var begun = new Dictionary<(int, int), int>(); var gotShot = new HashSet<(int, int)>(); var gotDown = new HashSet<(int, int)>();
                var still = new Dictionary<int, int>(); var ahead = new Dictionary<(int, int), int>();
                for (int t = 0; t < ticks && !st.Over; t++)
                {
                    var before = st.Men.Select(x => (x.X, x.Z)).ToArray();
                    int from = st.Events.Count;
                    m.Step();
                    var rosters = st.Squads.Select(q => Squads.Roster(st, q.Id)).ToArray();
                    var alive = Enumerable.Range(0, rosters.Length).Where(i => rosters[i].Count > 0).ToArray();

                    // Who has whom in sight.
                    foreach (int ai in alive) foreach (int bi in alive)
                    {
                        if (st.Squads[ai].Side == st.Squads[bi].Side) continue;
                        if (!Senses.Spots(st, rosters[ai], rosters[bi])) continue;
                        saw[(ai, bi)] = st.Tick;
                        // In sight and in reach of one of its weapons: from here it can do something about it.
                        if (!rosters[ai].Any(p => rosters[bi].Any(q => Combat.Dist(p, q) <= Arms.Of(st, p).Range))) continue;
                        if (!inReach.TryGetValue((ai, -1), out int last) || st.Tick - last > Memory)
                        {
                            begun[(ai, -1)] = st.Tick; gotShot.Remove((ai, -1)); gotDown.Remove((ai, -1)); sightings++;
                        }
                        inReach[(ai, -1)] = st.Tick;
                    }

                    // The shots.
                    for (int i = from; i < st.Events.Count; i++)
                    {
                        var e = st.Events[i];
                        if (e.Kind == EventKind.Reload) reloads++;
                        if (e.Kind != EventKind.Fire && e.Kind != EventKind.Launch && e.Kind != EventKind.Melee) continue;
                        var by = st.Men[e.Id]; var at = st.Men[e.Target.Value];
                        var key = (by.Squad, at.Squad);
                        shotAt[key] = st.Tick;
                        if (begun.TryGetValue((by.Squad, -1), out int b0) && gotShot.Add((by.Squad, -1))) toShot.Add((st.Tick - b0) * Tune.Dt);
                        if (e.Kind == EventKind.Melee) continue;
                        shots++;
                        if (e.Kind == EventKind.Fire && e.Amount.HasValue) bursts++;
                        bool own = saw.TryGetValue(key, out int s0) && st.Tick - s0 <= Keep;
                        bool side = own || st.Squads.Any(q => q.Side == by.Side && saw.TryGetValue((q.Id, at.Squad), out int s1) && st.Tick - s1 <= Keep);
                        if (!own)
                        {
                            blindOwn++; blindDist += Combat.Dist(by, at);
                            string k = $"{by.Side} {by.Weapon}";
                            blindBy[k] = blindBy.GetValueOrDefault(k) + 1;
                            if (a.Contains("why") && shown++ < 16)
                                Console.WriteLine($"    seed {seed} t{st.Tick} {by.Side} man {by.Id} ({by.Weapon}, {by.Posture}) at {by.X:F1},{by.Z:F1} fires at man {at.Id} ({at.Posture}{(at.Cover >= 0 ? ", in cover" : "")}{(Squads.IsMoving(at) ? ", moving" : "")}) at {at.X:F1},{at.Z:F1}: {Combat.Dist(by, at):F1} m, seen from {Senses.Sight(st, by, at):F1}");
                        }
                        if (!side) blindSide++;
                        if (!Senses.Sees(st, by, at)) unseenMan++;
                        if (!rosters[by.Squad].Any(o => Senses.Sees(st, o, at))) unseenBySquad++;
                        if (Math.Abs(by.Z - at.Z) > 6.5) acrossLanes++;
                        // The owner, playtest 6: "shooting up, down and in circles", "shoot and walk".
                        if (e.Kind == EventKind.Fire)
                        {
                            bullets++;
                            rounds += Math.Max(1, e.Rounds);
                            bool onTheMove = by.Id < before.Length && Math.Abs(by.X - before[by.Id].X) + Math.Abs(by.Z - before[by.Id].Z) > 0.01;
                            if (onTheMove) shotMoving++;
                            if (by.Posture == Posture.Standing) shotStanding++;
                            double ax = at.X - by.X, az = at.Z - by.Z;
                            if (Math.Abs(az) > Math.Abs(ax)) shotSteep++;
                            if (ax * Combat.Advance(by.Side) < -2) shotBack++;
                        }
                    }

                    // Squads close to each other.
                    for (int x = 0; x < alive.Length; x++) for (int y = x + 1; y < alive.Length; y++)
                    {
                        int ai = alive[x], bi = alive[y];
                        if (st.Squads[ai].Side == st.Squads[bi].Side) continue;
                        double near = double.PositiveInfinity;
                        foreach (var p in rosters[ai]) foreach (var q in rosters[bi]) near = Math.Min(near, Combat.Dist(p, q));
                        // (Gunnery: the lanes are two fights. Squads abreast in different lanes are not each other's business.)
                        if (st.Gunnery && st.Squads[ai].Lane != st.Squads[bi].Lane) continue;
                        if (near <= 15)
                        {
                            bool fought = (shotAt.TryGetValue((ai, bi), out int f0) && st.Tick - f0 <= 80) || (shotAt.TryGetValue((bi, ai), out int f1) && st.Tick - f1 <= 80);
                            if (fought) closeAware += Tune.Dt;
                            else if (st.Phase == Phase.Fight)
                            {
                                closeUnaware += Tune.Dt;
                                bool seesAB = saw.TryGetValue((ai, bi), out int v0) && st.Tick - v0 <= Keep, seesBA = saw.TryGetValue((bi, ai), out int v1) && st.Tick - v1 <= Keep;
                                double pinA = rosters[ai].Average(p => p.Pin), pinB = rosters[bi].Average(p => p.Pin);
                                string k = !seesAB && !seesBA ? "neither has the other in sight"
                                    : pinA >= Tune.PinStop && pinB >= Tune.PinStop ? "both pinned flat"
                                    : rosters[ai].Concat(rosters[bi]).All(p => !p.Seen || p.Side == Side.Us) && rosters[ai].Concat(rosters[bi]).Any(p => !p.Seen) ? "the VC squad unseen by the baseline's flag"
                                    : st.Squads[ai].Lane != st.Squads[bi].Lane ? "in different lanes"
                                    : st.Squads[ai].Order == Order.Fallback || st.Squads[bi].Order == Order.Fallback ? "one falling back"
                                    : "in sight, same lane, not pinned";
                                quiet[k] = quiet.GetValueOrDefault(k) + Tune.Dt;
                                if ((a.Contains("why2") && k.StartsWith("in sight") || a.Contains("why3") && k.StartsWith("neither") && st.Tick % 20 == 0) && shown++ < 16)
                                    Console.WriteLine($"    seed {seed} t{st.Tick} {near:F1} m: " + string.Join(" | ", new[] { ai, bi }.Select(q => $"sq{q} {st.Squads[q].Side} {st.Squads[q].Order} lane {st.Squads[q].Lane} x {st.Squads[q].AnchorX:F0} " + string.Join(" ", rosters[q].Select(p => $"{p.Weapon}/{p.Posture.ToString()[0]}{(p.Cover >= 0 ? "c" : "")}/pin{p.Pin:F2}/cd{p.Cooldown}{(p.Seen ? "" : "/unseen")}{(p.Vault > 0 ? "/vault" : "")}")))));
                            }
                        }
                        if (near <= 16)
                        {
                            int now = Math.Sign(rosters[ai].Average(p => p.X) - rosters[bi].Average(p => p.X));
                            if (ahead.TryGetValue((ai, bi), out int was) && was != now && now != 0 && was != 0) passed++;
                            if (now != 0) ahead[(ai, bi)] = now;
                        }
                    }

                    // The men.
                    foreach (int ai in alive)
                    {
                        // The squads this one has in sight, and how its men stand to them.
                        var inSight = alive.Where(bi => st.Squads[bi].Side != st.Squads[ai].Side && saw.TryGetValue((ai, bi), out int s0) && st.Tick - s0 <= Keep).ToArray();
                        int down = rosters[ai].Count(p => p.Posture != Posture.Standing || p.Cover >= 0);
                        if (begun.ContainsKey((ai, -1)) && down * 2 >= rosters[ai].Count && gotDown.Add((ai, -1))) toGround.Add((st.Tick - begun[(ai, -1)]) * Tune.Dt);
                        foreach (var p in rosters[ai])
                        {
                            manSeconds += Tune.Dt;
                            bool moved = p.Id < before.Length && Math.Abs(p.X - before[p.Id].X) + Math.Abs(p.Z - before[p.Id].Z) > 0.01;
                            still[seed * 100000 + p.Id] = moved ? 0 : still.GetValueOrDefault(seed * 100000 + p.Id) + 1;
                            if (moved && p.Posture != Posture.Standing) movedDown += Tune.Dt;
                            double reach = Arms.Of(st, p).Range;
                            // (Gunnery: an enemy he could put a round at: down the lane from him, not across it.)
                            bool threatened = inSight.Any(bi => rosters[bi].Any(q => Combat.Dist(p, q) <= reach && (!st.Gunnery || Gunnery.InArc(st, p, q))));
                            if (threatened)
                            {
                                string can = p.Vault > 0 ? "climbing" : p.Pin >= Tune.PinStop ? "pinned flat" : moved ? "moving" : p.Posture == Posture.Standing ? "on his feet, still" : "down and still";
                                ready[can] = ready.GetValueOrDefault(can) + Tune.Dt;
                            }
                            if (threatened)
                            {
                                contact += Tune.Dt;
                                // A man with an enemy inside charging distance is fighting him hand to hand, or about to: on his feet is right.
                                bool hands = alive.Any(bi => st.Squads[bi].Side != p.Side && rosters[bi].Any(q => Combat.Dist(p, q) <= Tune.ChargeRange));
                                if (hands) handToHand += Tune.Dt;
                                else if (p.Posture == Posture.Standing)
                                {
                                    if (moved) uprightMoving += Tune.Dt; else uprightStill += Tune.Dt;
                                    var q = st.Squads[ai];
                                    string k = $"{(moved ? "moving" : "still ")} {q.Side} {(st.Senses ? q.Task.ToString() : q.Order.ToString()),-9} {(q.Halted ? "halted" : "going ")} {(p.Cover >= 0 ? "in cover" : "open    ")} {(p.Vault > 0 ? "climbing" : p.Dwell < Tune.PostureDwell ? "dwell" : p.Cooldown > 0 && st.Tick - p.FiredAt < 30 ? "just fired" : "")}";
                                    upright[k] = upright.GetValueOrDefault(k) + Tune.Dt;
                                }
                            }
                            if (st.Phase == Phase.Fight && p.Cover < 0 && p.Pin < Tune.PinDrop && still.GetValueOrDefault(seed * 100000 + p.Id) >= 20)
                            {
                                bool room = st.Cover.Any(c => Math.Abs(c.Z - Tune.Lanes[st.Squads[ai].Lane]) <= 4.5
                                    && Math.Max(0, Math.Abs(p.X - c.X) - c.Length * 0.5) <= 12
                                    && st.Men.Count(o => o.Alive && o.Cover == c.Id) < c.Capacity);
                                var q = st.Squads[ai];
                                // The owner: "not all can fit at once". A man whose squad is in cover that is full waits behind it; that is not idling.
                                bool noRoom = q.Target >= 0 && p.Place < 0 && rosters[ai].Any(o => o.PlaceCover == q.Target);
                                if (room && noRoom) waiting += Tune.Dt;
                                else if (room)
                                {
                                    idle += Tune.Dt; if (threatened) idleContact += Tune.Dt;
                                    string k = $"{q.Side} {(st.Senses ? q.Task.ToString() : q.Order.ToString()),-9} {(q.Halted ? "halted" : "going ")} {p.Posture,-8} {(q.Target < 0 ? "no cover chosen" : p.Place < 0 ? "no place in its cover" : "has a place")} {(threatened ? "enemy in range" : "")}";
                                    idleBy[k] = idleBy.GetValueOrDefault(k) + Tune.Dt;
                                    if (a.Contains("why6") && k.Contains("March") && st.Tick % 20 == 0 && shown++ < 24)
                                        Console.WriteLine($"    seed {seed} t{st.Tick} man {p.Id} at {p.X:F1},{p.Z:F1} rank {p.Rank} place {p.Place}@{p.PlaceCover} still {p.Still} | sq{q.Id} {q.Side} L{q.Lane} {q.Order} {q.Task} tgt {q.Target} x {q.AnchorX:F1} held {q.Held} {(q.Halted ? "halted" : "going")} gap {q.Gap:F0} threat {q.Threat} n {rosters[ai].Count} | squad at " + string.Join(" ", rosters[ai].Select(o => $"{o.X:F0}")));
                                }
                            }
                        }
                    }
                }
                foreach (var kv in begun)
                {
                    if (st.Tick - kv.Value < 200) continue;         // too late in the match to say
                    if (!gotShot.Contains(kv.Key)) noShot++;
                    if (!gotDown.Contains(kv.Key)) noGround++;
                }
                minutes += st.Tick * Tune.Dt / 60;
            }
            double Pct(List<double> v, double q) => v.Count == 0 ? 0 : v.OrderBy(x => x).ElementAt(Math.Min(v.Count - 1, (int)(q * v.Count)));
            Console.WriteLine($"  seeds {seed0}..{seed0 + seeds - 1}, {minutes:F1} minutes of fighting; a minute:");
            Console.WriteLine($"  shots                     {shots / minutes:F0}, of which at a squad the firer's own has not in sight {blindOwn / minutes:F1} ({100 * blindOwn / Math.Max(1, shots):F0}%; mean {blindDist / Math.Max(1, blindOwn):F1} m), that nobody on his side has {blindSide / minutes:F1} ({100 * blindSide / Math.Max(1, shots):F0}%)");
            if (bursts > 0) Console.WriteLine($"      machine-gun bursts on cover lost sight of {bursts / minutes:F1}");
            foreach (var kv in blindBy.OrderByDescending(k => k.Value).Take(4)) Console.WriteLine($"      {kv.Key,-12} {kv.Value / minutes:F1}");
            Console.WriteLine($"      at a man the firer cannot himself see {unseenMan / minutes:F1} ({100 * unseenMan / Math.Max(1, shots):F0}%), that nobody in his squad can {unseenBySquad / minutes:F1} ({100 * unseenBySquad / Math.Max(1, shots):F0}%); across the lanes {acrossLanes / minutes:F1} ({100 * acrossLanes / Math.Max(1, shots):F0}%)");
            Console.WriteLine($"  rounds                    {rounds / minutes:F0} (a burst is its rounds), reloads {reloads / minutes:F1}");
            Console.WriteLine($"  bullets                   {bullets / minutes:F0}: fired by a man on his feet {100 * shotStanding / Math.Max(1, bullets):F0}%, by a man moving {100 * shotMoving / Math.Max(1, bullets):F0}%, more across the lane than along it {100 * shotSteep / Math.Max(1, bullets):F0}%, back over his shoulder {100 * shotBack / Math.Max(1, bullets):F1}%");
            Console.WriteLine($"  moving on a knee or flat  {movedDown / minutes:F1} man-seconds");
            Console.WriteLine($"  squads within 15 m        {closeUnaware / minutes:F1} squad-seconds with neither firing on the other, {closeAware / minutes:F1} fighting");
            foreach (var kv in quiet.OrderByDescending(k => k.Value)) Console.WriteLine($"      {kv.Key,-44} {kv.Value / minutes:F1}");
            Console.WriteLine($"  enemy squads passed       {passed / minutes:F2}");
            Console.WriteLine($"  men with a seen enemy in range {contact / minutes:F0} man-seconds, of which standing still {uprightStill / minutes:F1} ({100 * uprightStill / Math.Max(1, contact):F0}%), standing and moving {uprightMoving / minutes:F1} ({100 * uprightMoving / Math.Max(1, contact):F0}%), within {Tune.ChargeRange:F0} m of him {handToHand / minutes:F1}");
            Console.WriteLine("      of that time: " + string.Join(", ", ready.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {100 * k.Value / Math.Max(1e-9, contact):F0}%")));
            if (a.Contains("why4")) foreach (var kv in upright.OrderByDescending(k => k.Value).Take(14)) Console.WriteLine($"      {kv.Key,-60} {kv.Value / minutes:F1}");
            Console.WriteLine($"  idle in the open by cover {idle / minutes:F1} man-seconds ({100 * idle / Math.Max(1, manSeconds):F0}% of all), of which with a seen enemy in range {idleContact / minutes:F1}; waiting behind cover their squad has filled {waiting / minutes:F1}");
            if (a.Contains("why5")) foreach (var kv in idleBy.OrderByDescending(k => k.Value).Take(14)) Console.WriteLine($"      {kv.Key,-70} {kv.Value / minutes:F1}");
            Console.WriteLine($"  contacts                  {sightings / minutes:F1}; to the first shot: median {Pct(toShot, 0.5):F1} s, nine in ten within {Pct(toShot, 0.9):F1} s, never {noShot / minutes:F1}");
            Console.WriteLine($"                            to half the squad off its feet or in cover: median {Pct(toGround, 0.5):F1} s, nine in ten within {Pct(toGround, 0.9):F1} s, never {noGround / minutes:F1}");
            return 0;
        }

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
            var flips = new Dictionary<string, double>();
            var kinds = new Dictionary<string, double>();
            double postures = 0, starts = 0, hops = 0, orders = 0, targets = 0, manSeconds = 0, squadSeconds = 0;
            var stillFor = new Dictionary<(int, int), int>(); var movedFor = new Dictionary<(int, int), int>(); var hops0 = new Dictionary<(int, int), int>();
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
                    var postureWas = st.Men.Select(x => x.Posture).ToArray();
                    var orderWas = st.Squads.Select(x => (x.Order, x.Target)).ToArray();
                    m.Step();
                    var live = st.Men.Where(x => x.Alive).ToList();
                    for (int i = 0; i < postureWas.Length; i++)
                    {
                        var man = st.Men[i];
                        if (!man.Alive) continue;
                        manSeconds += Tune.Dt;
                        if (man.Posture != postureWas[i])
                        {
                            postures++;
                            string k = $"{postureWas[i]} -> {man.Posture}" + (man.Pin >= Tune.PinDrop ? " (pinned)" : st.Squads[man.Squad].Halted ? " (halted)" : " (moving)");
                            kinds[k] = kinds.GetValueOrDefault(k) + 1;
                        }
                        bool moving = before.TryGetValue(man.Id, out var b0) && Math.Abs(man.X - b0.x) + Math.Abs(man.Z - b0.z) > 0.01;
                        if (moving && stillFor.GetValueOrDefault((seed, man.Id)) >= 6) starts++;       // off again after 0.3 s or more at rest
                        if (moving) { if (movedFor.GetValueOrDefault((seed, man.Id)) == 0) hops0[(seed, man.Id)] = st.Tick; movedFor[(seed, man.Id)] = movedFor.GetValueOrDefault((seed, man.Id)) + 1; stillFor[(seed, man.Id)] = 0; }
                        else
                        {
                            // A hop: he moved for under a second and stopped again.
                            int run = movedFor.GetValueOrDefault((seed, man.Id));
                            if (run > 0 && run < 20) hops++;
                            movedFor[(seed, man.Id)] = 0;
                            stillFor[(seed, man.Id)] = stillFor.GetValueOrDefault((seed, man.Id)) + 1;
                        }
                    }
                    for (int i = 0; i < orderWas.Length; i++)
                    {
                        if (!st.Men.Any(x => x.Alive && x.Squad == i)) continue;
                        squadSeconds += Tune.Dt;
                        if (st.Squads[i].Order != orderWas[i].Order)
                        {
                            orders++;
                            string k = $"{orderWas[i].Order} -> {st.Squads[i].Order}";
                            flips[k] = flips.GetValueOrDefault(k) + 1;
                        }
                        if (st.Squads[i].Target != orderWas[i].Target) targets++;
                    }
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
                                // `turn=taking` prints the first of a kind ("taking a place in cover", "along the lane", ...).
                                string asked = a.FirstOrDefault(x => x.StartsWith("turn="))?.Substring(5);
                                if (asked != null && why.StartsWith(asked) && shown++ < 14)
                                    Console.WriteLine($"    seed {seed} t{st.Tick} man {p.Id} sq{p.Squad} {psq.Side} {psq.Task} {psq.Order} tgt {psq.Target} anchor {psq.AnchorX:F1} {(psq.Halted ? "halt" : "go")} | r{p.Rank} place {p.Place}@{p.PlaceCover} cover {p.Cover} {p.Posture} pin {p.Pin:F2} ({p0.x:F2},{p0.z:F2})->({p.X:F2},{p.Z:F2}) was going ({h.x:F2},{h.z:F2})");
                                if (a.Contains("why2") && why == (a.Contains("close") ? "in a close fight" : "falling back") && shown++ < 14)
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
            Console.WriteLine($"  a man, a minute: changes posture {postures / manSeconds * 60:F1}, sets off {starts / manSeconds * 60:F1}, of which hops under a second {hops / manSeconds * 60:F1}");
            Console.WriteLine($"  a squad, a minute: order changes {orders / squadSeconds * 60:F1}, changes of cover {targets / squadSeconds * 60:F1}");
            foreach (var kv in flips.OrderByDescending(k => k.Value)) Console.WriteLine($"      {kv.Key,-24} {kv.Value / squadSeconds * 60:F2}");
            Console.WriteLine("  posture changes a man, a minute, by kind:");
            foreach (var kv in kinds.OrderByDescending(k => k.Value).Take(8)) Console.WriteLine($"      {kv.Key,-36} {kv.Value / manSeconds * 60:F2}");
            foreach (var kv in turned.OrderByDescending(k => k.Value)) Console.WriteLine($"      {kv.Key,-24} {kv.Value / minutes:F1}");
            return 0;
        }

        /// <summary>
        /// The three things the owner saw in playtest 9: "sometimes they still
        /// do weird circles, don't fight each other, weird non-logical
        /// positions when fighting". `brain [seed] [ticks] [seeds] [flags]`,
        /// with the Senses rule on.
        ///   round and back  a man walks five metres or more in eight seconds and ends within a metre and a half of where he began
        ///   full turn       his direction of travel turns through a whole circle in eight seconds
        ///   to and fro      the middle of a squad's men travels four metres or more along the lane in eight seconds and ends within one of where it began
        ///   not fighting    a man with a target (the rule's own choice: a squad his has in sight, in reach, in his arc, no smoke between),
        ///                   not pinned flat, climbing or reloading, who has not fired for four seconds (on the march, a target within his
        ///                   weapon's own distance: one far enough off for a long shot only is not a fight he is walking past)
        ///   lanes quiet     two enemy squads in one lane within 30 m, neither having fired on the other for four seconds
        ///                   (eight beyond 20 m, where a rifle's fire is a deliberate round every five seconds or so)
        ///   out of place    a man at rest in the open within four metres of cover in his lane that has a place free
        ///   strung out      a squad whose men are spread over more than its file's length and half again
        /// </summary>
        private static int Brain(string[] a)
        {
            var nums = a.Skip(1).Where(x => int.TryParse(x, out _)).Select(int.Parse).ToArray();
            int seed0 = nums.Length > 0 ? nums[0] : 3, ticks = nums.Length > 1 ? nums[1] : 2400, seeds = nums.Length > 2 ? nums[2] : 6;
            const int Window = 160, Quiet = 80;
            double minutes = 0, manSeconds = 0, squadSeconds = 0;
            double loops = 0, turns = 0, fro = 0, silent = 0, armed = 0, laneQuiet = 0, laneFight = 0, beside = 0, besideFight = 0, besideCovering = 0, strung = 0;
            var loopBy = new Dictionary<string, double>(); var turnBy = new Dictionary<string, double>(); var froBy = new Dictionary<string, double>();
            var silentBy = new Dictionary<string, double>(); var quietBy = new Dictionary<string, double>(); var besideBy = new Dictionary<string, double>(); var strungBy = new Dictionary<string, double>();
            // Grenades, and who kills whom with what (the owner: "too many grenades"; snipers "safe but prone to suppression").
            double thrown = 0, launched = 0, shots = 0, longShots = 0, kills = 0, longKills = 0;
            double sniperSeconds = 0, sniperPinned = 0, sniperDeaths = 0, sniperClose = 0, sniperKills = 0;
            var killsBy = new Dictionary<string, double>(); var sniperBy = new Dictionary<string, double>();
            var lengths = new List<double>();
            int shown = 0;
            for (int seed = seed0; seed < seed0 + seeds; seed++)
            {
                var m = new LiveMatch(Game(seed, a));
                var st = m.State;
                var path = new Dictionary<int, List<(double x, double z)>>();
                var anchors = new Dictionary<int, List<double>>();
                var tasks = new Dictionary<int, List<(int tick, SquadTask task)>>();
                var shotAt = new Dictionary<(int, int), int>();
                // The tasks a squad has had in the window, in order: what it was doing while its man went round.
                string Tasks(int sq)
                {
                    if (!tasks.TryGetValue(sq, out var l)) return "";
                    var seen = new List<string>();
                    for (int i = 0; i < l.Count; i++)
                        if (i == l.Count - 1 || l[i + 1].tick > st.Tick - Window) seen.Add(l[i].task.ToString());
                    return string.Join(">", seen.Skip(Math.Max(0, seen.Count - 5)));
                }
                for (int t = 0; t < ticks && !st.Over; t++)
                {
                    // `snipers`: each side buys a sniper team whenever it can, so there are some to measure.
                    if (a.Contains("snipers") && st.Tick % 20 == 0)
                        foreach (var side in Match.Sides)
                        {
                            var card = Deck.For(side).First(c => c.Id == (side == Side.Us ? "us-sniper" : "vc-marksman"));
                            if (Deck.Blocked(st, side, card) == null) m.Issue(Command.Buy(side, card.Id, st.Tick / 20 % 2, 0));
                        }
                    int from = st.Events.Count;
                    m.Step();
                    string cause = "something else";
                    bool far = false;
                    for (int i = from; i < st.Events.Count; i++)
                    {
                        var e = st.Events[i];
                        if (e.Kind == EventKind.GrenadeThrown) thrown++;
                        if (e.Kind == EventKind.Launch) launched++;
                        // What killed him is the event before his death: the shot, the blast, the shell, the blow.
                        if (e.Kind == EventKind.Fire)
                        {
                            var by = st.Men[e.Id]; var at = st.Men[e.Target.Value];
                            shots++;
                            far = Combat.Dist(by, at) > Arms.Of(st, by).Range;
                            if (far) longShots++;
                            cause = by.Weapon == Weapon.M60 || by.Weapon == Weapon.Rpd ? "a machine gun" : by.Weapon == Weapon.Sniper ? "a sniper" : by.Weapon == Weapon.Smg ? "a submachine gun" : "a rifle";
                            if (far) cause += ", a long shot";
                        }
                        else if (e.Kind == EventKind.GrenadeBlast) { cause = "a burst (grenade, launcher, mortar)"; far = false; }
                        else if (e.Kind == EventKind.Shell) { cause = "a shell"; far = false; }
                        else if (e.Kind == EventKind.Melee) { cause = "hand to hand"; far = false; }
                        else if (e.Kind == EventKind.Through) { cause = "a round through the man in front"; far = false; }
                        else if (e.Kind == EventKind.TrapSprung) { cause = "a trap"; far = false; }
                        if (e.Kind == EventKind.Kill)
                        {
                            kills++;
                            if (cause.StartsWith("a sniper")) sniperKills++;
                            if (far) longKills++;
                            killsBy[cause] = killsBy.GetValueOrDefault(cause) + 1;
                            if (st.Men[e.Id].Weapon == Weapon.Sniper) { sniperDeaths++; sniperBy[cause] = sniperBy.GetValueOrDefault(cause) + 1; }
                        }
                        if (e.Kind != EventKind.Fire && e.Kind != EventKind.Launch && e.Kind != EventKind.Melee) continue;
                        shotAt[(st.Men[e.Id].Squad, st.Men[e.Target.Value].Squad)] = st.Tick;
                    }
                    foreach (var p in st.Men)
                    {
                        if (!p.Alive || p.Weapon != Weapon.Sniper || st.Phase != Phase.Fight) continue;
                        sniperSeconds += Tune.Dt;
                        if (p.Pin >= Tune.PinDrop) sniperPinned += Tune.Dt;
                        double nearest = st.Men.Where(e => e.Alive && e.Side != p.Side && Math.Abs(e.Z - p.Z) < 6.5).Select(e => Combat.Dist(p, e)).DefaultIfEmpty(99).Min();
                        if (nearest < 20) sniperClose += Tune.Dt;
                    }
                    var rosters = st.Squads.Select(q => Squads.Roster(st, q.Id)).ToArray();
                    var alive = Enumerable.Range(0, rosters.Length).Where(i => rosters[i].Count > 0).ToArray();
                    bool fight = st.Phase == Phase.Fight;

                    foreach (int si in alive)
                    {
                        var sq = st.Squads[si];
                        squadSeconds += Tune.Dt;
                        if (!tasks.TryGetValue(si, out var tl)) tasks[si] = tl = new List<(int, SquadTask)>();
                        if (tl.Count == 0 || tl[^1].task != sq.Task) tl.Add((st.Tick, sq.Task));

                        // To and fro: the squad itself going up the lane and back (its men's middle, not its anchor,
                        // which runs three metres ahead of the lead man and is pulled back to him).
                        if (!anchors.TryGetValue(si, out var al)) anchors[si] = al = new List<double>();
                        al.Add(rosters[si].Average(p => p.X));
                        if (al.Count > Window + 1) al.RemoveAt(0);
                        double went = 0;
                        for (int i = 1; i < al.Count; i++) went += Math.Abs(al[i] - al[i - 1]);
                        if (went >= 4 && Math.Abs(al[^1] - al[0]) <= 1)
                        {
                            fro++;
                            string k = $"{sq.Side} {Tasks(si)}";
                            froBy[k] = froBy.GetValueOrDefault(k) + 1;
                            if (a.Contains("why3") && shown++ < 16) Console.WriteLine($"    seed {seed} t{st.Tick} sq{si} {sq.Side} L{sq.Lane} {Tasks(si)} middle {al[0]:F1} -> {al.Min():F1}..{al.Max():F1} -> {al[^1]:F1}");
                            al.Clear();
                        }

                        // Strung out.
                        double lo = rosters[si].Min(p => p.X), hi = rosters[si].Max(p => p.X);
                        if (rosters[si].Count > 1 && hi - lo > (rosters[si].Count - 1) * Tune.SlotGap * 1.5 + 2)
                        {
                            strung += Tune.Dt;
                            string k = $"{sq.Side} {sq.Task,-9} {(Math.Abs(sq.AnchorX) > Tune.HalfLength - 12 ? "at its own end" : "up the lane")}";
                            strungBy[k] = strungBy.GetValueOrDefault(k) + Tune.Dt;
                        }

                        foreach (var p in rosters[si])
                        {
                            manSeconds += Tune.Dt;
                            if (!path.TryGetValue(p.Id, out var pl)) path[p.Id] = pl = new List<(double, double)>();
                            pl.Add((p.X, p.Z));
                            if (pl.Count > Window + 1) pl.RemoveAt(0);
                            if (st.Tick % 5 == 0 && pl.Count > 20)
                            {
                                double walked = 0, wound = 0, hx = 0, hz = 0;
                                bool heading = false;
                                for (int i = 1; i < pl.Count; i++)
                                {
                                    double vx = pl[i].x - pl[i - 1].x, vz = pl[i].z - pl[i - 1].z, v = Math.Sqrt(vx * vx + vz * vz);
                                    walked += v;
                                    if (v < 0.02) continue;
                                    if (heading) wound += Math.Abs(Math.Atan2(hx * vz - hz * vx, hx * vx + hz * vz));
                                    hx = vx; hz = vz; heading = true;
                                }
                                double net = Math.Sqrt((pl[^1].x - pl[0].x) * (pl[^1].x - pl[0].x) + (pl[^1].z - pl[0].z) * (pl[^1].z - pl[0].z));
                                string what = sq.Order == Order.Fallback ? "falling back"
                                    : st.Men.Any(e => e.Alive && e.Side != p.Side && Combat.Dist(p, e) < 7) ? "in a close fight"
                                    : Tasks(si);
                                bool loop = walked >= 5 && net <= 1.5, turn = !loop && walked >= 3 && wound >= 2 * Math.PI;
                                if (loop) { loops++; loopBy[$"{sq.Side} {what}"] = loopBy.GetValueOrDefault($"{sq.Side} {what}") + 1; }
                                if (turn) { turns++; turnBy[$"{sq.Side} {what}"] = turnBy.GetValueOrDefault($"{sq.Side} {what}") + 1; }
                                if ((loop && a.Contains("why") || turn && a.Contains("why2")) && shown++ < 16)
                                    Console.WriteLine($"    seed {seed} t{st.Tick - pl.Count + 1}..{st.Tick} man {p.Id} sq{si} {sq.Side} L{sq.Lane} {what}: walked {walked:F1} m, turned {wound * 180 / Math.PI:F0} deg, ends {net:F1} m from where he began ({pl[0].x:F1},{pl[0].z:F1} -> {pl[^1].x:F1},{pl[^1].z:F1}) rank {p.Rank} place {p.Place}@{p.PlaceCover}");
                                if (loop || turn) pl.Clear();
                            }

                            if (!fight) continue;

                            // Not fighting: he has somebody to shoot at, and has not for four seconds.
                            bool bullets = !Arms.Of(st, p).Bursts;
                            var target = st.Senses && bullets ? Senses.PickTarget(st, p) : null;
                            // (On the march, a man far enough off for a long shot only is not a fight he is walking past.)
                            if (target != null && sq.Task == SquadTask.March && Combat.Dist(p, target) > Arms.Of(st, p).Range) target = null;
                            bool can = target != null && p.Vault == 0 && p.Pin < Tune.PinStop && p.Reloading == 0;
                            if (can)
                            {
                                armed += Tune.Dt;
                                // (A long shot is a deliberate one, one every five seconds or so: twice as long before he counts.)
                                if (st.Tick - p.FiredAt > (Combat.Dist(p, target) > Arms.Of(st, p).Range ? Quiet * 2 : Quiet))
                                {
                                    silent += Tune.Dt;
                                    string doing = p.Rest == 0 ? "moving" : p.Posture == Posture.Standing ? "on his feet, at rest" : "down, at rest";
                                    string k = $"{doing,-21} {sq.Side} {sq.Task,-9} {(sq.Halted ? "halted" : "going ")} {(p.Cover >= 0 ? "in cover" : "open")}";
                                    silentBy[k] = silentBy.GetValueOrDefault(k) + Tune.Dt;
                                    if (a.Contains("why4") && doing.StartsWith(a.Contains("down") ? "down" : "moving") && st.Tick % 20 == 0 && shown++ < 20)
                                        Console.WriteLine($"    seed {seed} t{st.Tick} man {p.Id} sq{si} {sq.Side} L{sq.Lane} {sq.Task} {sq.Order} {(sq.Halted ? "halted" : "going")} tgt {sq.Target} anchor {sq.AnchorX:F1} | at {p.X:F1},{p.Z:F1} {p.Posture} rest {p.Rest} pin {p.Pin:F2} cd {p.Cooldown} place {p.Place}@{p.PlaceCover} cover {p.Cover} | target man {target.Id} at {target.X:F1},{target.Z:F1} {Combat.Dist(p, target):F1} m, last fired {(st.Tick - p.FiredAt) * Tune.Dt:F0} s ago");
                                }
                            }

                            // Out of place: at rest in the open, beside cover with a place free in it.
                            if (p.Cover < 0 && p.Pin < Tune.PinDrop && p.Rest >= 20)
                            {
                                Cover by = null;
                                foreach (var c in st.Cover)
                                {
                                    if (Math.Abs(c.Z - Tune.Lanes[sq.Lane]) > 4.5) continue;
                                    if (Math.Max(0, Math.Abs(p.X - c.X) - c.Length * 0.5) > 4) continue;
                                    if (st.Men.Count(o => o.Alive && o.Cover == c.Id) >= c.Capacity) continue;
                                    if (st.Men.Any(o => o.Alive && o.Side != p.Side && o.Cover == c.Id)) continue;
                                    by = c; break;
                                }
                                if (by != null)
                                {
                                    beside += Tune.Dt;
                                    if (target != null) besideFight += Tune.Dt;
                                    if (Tactics.Bounds(st, sq) && !Tactics.Turn(st, sq, p)) besideCovering += Tune.Dt;
                                    double ahead = (p.X - by.X) * Combat.Advance(p.Side);
                                    string where = Math.Abs(p.X - by.X) <= by.Length * 0.5 ? "alongside it" : ahead > 0 ? "in front of it" : "behind it";
                                    string k = $"{sq.Side} {sq.Task,-9} {(sq.Halted ? "halted" : "going ")} {p.Posture,-8} {where,-14} {(sq.Target == by.Id ? "its squad's cover" : sq.Target >= 0 ? "squad has other cover" : "squad has no cover")}{(target != null ? ", enemy in range" : "")}";
                                    besideBy[k] = besideBy.GetValueOrDefault(k) + Tune.Dt;
                                    if (a.Contains("why5") && sq.Task != SquadTask.March && st.Tick % 20 == 0 && shown++ < 20)
                                        Console.WriteLine($"    seed {seed} t{st.Tick} man {p.Id} sq{si} {sq.Side} L{sq.Lane} {sq.Task} {sq.Order} {(sq.Halted ? "halted" : "going")} tgt {sq.Target} anchor {sq.AnchorX:F1},{sq.AnchorZ:F1} | at {p.X:F1},{p.Z:F1} {p.Posture} rank {p.Rank} place {p.Place}@{p.PlaceCover} | cover {by.Id} {by.Kind} x {by.X:F1} z {by.Z:F1} len {by.Length:F0} cap {by.Capacity} in it {st.Men.Count(o => o.Alive && o.Cover == by.Id)} | squad at " + string.Join(" ", rosters[si].Select(o => $"{o.X:F0},{o.Z:F0}{(o.Cover >= 0 ? "c" : "")}")));
                                }
                            }
                        }
                    }

                    // Lanes quiet: two enemy squads in one lane, near enough for a rifle and more, and no fire between them.
                    if (fight)
                        for (int x = 0; x < alive.Length; x++) for (int y = x + 1; y < alive.Length; y++)
                        {
                            int ai = alive[x], bi = alive[y];
                            var qa = st.Squads[ai]; var qb = st.Squads[bi];
                            if (qa.Side == qb.Side || qa.Lane != qb.Lane) continue;
                            double near = double.PositiveInfinity;
                            foreach (var p in rosters[ai]) foreach (var q in rosters[bi]) near = Math.Min(near, Combat.Dist(p, q));
                            if (near > 30) continue;
                            // (Beyond a rifle's own distance fire is deliberate, a round every five seconds or so from each man: eight seconds there.)
                            int lull = near > 20 ? Quiet * 2 : Quiet;
                            bool fought = (shotAt.TryGetValue((ai, bi), out int f0) && st.Tick - f0 <= lull) || (shotAt.TryGetValue((bi, ai), out int f1) && st.Tick - f1 <= lull);
                            if (fought) { laneFight += Tune.Dt; continue; }
                            laneQuiet += Tune.Dt;
                            bool ka = Senses.Knows(st, qa, bi), kb = Senses.Knows(st, qb, ai);
                            string band = near <= 10 ? "under 10 m" : near <= 20 ? "10 to 20 m" : "20 to 30 m";
                            string k = $"{band}  {(ka && kb ? "each has the other in sight" : ka || kb ? "one has the other in sight" : "neither has the other in sight"),-30} {(qa.Side == Side.Us ? qa.Task : qb.Task)}/{(qa.Side == Side.Us ? qb.Task : qa.Task)}";
                            quietBy[k] = quietBy.GetValueOrDefault(k) + Tune.Dt;
                            if (a.Contains("why6") && (ka || kb) && st.Tick % 20 == 0 && shown++ < 20)
                                Console.WriteLine($"    seed {seed} t{st.Tick} {near:F1} m: " + string.Join(" | ", new[] { ai, bi }.Select(q => $"sq{q} {st.Squads[q].Side} {st.Squads[q].Task} {st.Squads[q].Order} x {st.Squads[q].AnchorX:F0} reach {st.Squads[q].Reach:F0} threat {st.Squads[q].Threat} " + string.Join(" ", rosters[q].Select(p => $"{p.Weapon}/{p.Posture.ToString()[0]}{(p.Cover >= 0 ? "c" : "")}/{p.X:F0}/pin{p.Pin:F2}/rest{p.Rest}{(p.Reloading > 0 ? "/reload" : "")}")))));
                        }
                }
                minutes += st.Tick * Tune.Dt / 60;
                lengths.Add(st.Tick * Tune.Dt);
            }
            void Top(Dictionary<string, double> d, int n = 8) { foreach (var kv in d.OrderByDescending(k => k.Value).Take(n)) Console.WriteLine($"      {kv.Key,-78} {kv.Value / minutes:F1}"); }
            Console.WriteLine($"  seeds {seed0}..{seed0 + seeds - 1}, {minutes:F1} minutes of fighting; a minute:");
            Console.WriteLine($"  round and back            {loops / minutes:F1} (a man walks 5 m or more in 8 s and ends within 1.5 m of where he began)");
            Top(loopBy);
            Console.WriteLine($"  full turn                 {turns / minutes:F1} (his direction of travel turns through a whole circle in 8 s)");
            Top(turnBy, 5);
            Console.WriteLine($"  squads to and fro         {fro / minutes:F1} (its men's middle goes 4 m or more in 8 s and ends within 1 m)");
            Top(froBy, 5);
            Console.WriteLine($"  not fighting              {silent / minutes:F1} man-seconds of {armed / minutes:F0} with a target and able to fire ({100 * silent / Math.Max(1e-9, armed):F0}%): no shot for 4 s");
            Top(silentBy, 10);
            Console.WriteLine($"  lanes quiet               {laneQuiet / minutes:F1} squad-seconds of two enemy squads within 30 m in one lane with no fire between them for 4 s (8 s beyond 20 m); {laneFight / minutes:F1} fighting");
            Top(quietBy, 10);
            Console.WriteLine($"  out of place              {beside / minutes:F1} man-seconds at rest in the open within 4 m of cover with a place free ({100 * beside / Math.Max(1e-9, manSeconds):F0}% of all), {besideFight / minutes:F1} of them with a target, {besideCovering / minutes:F1} covering the other half's bound");
            Top(besideBy, 12);
            Console.WriteLine($"  strung out                {strung / minutes:F1} squad-seconds ({100 * strung / Math.Max(1e-9, squadSeconds):F0}% of all)");
            Top(strungBy, 6);
            Console.WriteLine($"  grenades thrown           {thrown / minutes:F1}; launcher and mortar rounds {launched / minutes:F1}");
            Console.WriteLine($"  shots                     {shots / minutes:F0}, of them long shots (beyond the weapon's own distance) {longShots / minutes:F1} ({100 * longShots / Math.Max(1, shots):F0}%)");
            Console.WriteLine($"  men killed                {kills / minutes:F1}, of them by a long shot {longKills / minutes:F2}");
            foreach (var kv in killsBy.OrderByDescending(k => k.Value)) Console.WriteLine($"      {kv.Key,-40} {kv.Value / minutes:F2}  ({100 * kv.Value / Math.Max(1, kills):F0}%)");
            Console.WriteLine($"  snipers                   {sniperSeconds / minutes:F0} man-seconds in the fight, {100 * sniperPinned / Math.Max(1e-9, sniperSeconds):F0}% of them pinned; killed {sniperDeaths / minutes:F2} a minute ({sniperDeaths / Math.Max(1e-9, sniperSeconds) * 60:F2} a sniper-minute); {100 * sniperClose / Math.Max(1e-9, sniperSeconds):F0}% of their time with an enemy inside 20 m in their own lane; they kill {sniperKills / Math.Max(1e-9, sniperSeconds) * 60:F2} a sniper-minute");
            foreach (var kv in sniperBy.OrderByDescending(k => k.Value)) Console.WriteLine($"      {kv.Key,-40} {kv.Value / minutes:F2}");
            double mean = lengths.Average(), sd = Math.Sqrt(lengths.Sum(x => (x - mean) * (x - mean)) / lengths.Count);
            Console.WriteLine($"  matches                   mean {mean:F0} s, shortest {lengths.Min():F0}, longest {lengths.Max():F0}, spread (sd) {sd:F0}");
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
            bool frag = a.Contains("frag"), smoke = a.Contains("smoke"), drill = a.Contains("drill"), craft = a.Contains("fieldcraft"), onMap = a.Contains("map"), arms = a.Contains("arms"), senses = a.Contains("senses"), tempo = a.Contains("tempo"), gunnery = a.Contains("gunnery"), ammo = a.Contains("ammo"), tactics = a.Contains("tactics"), fortune = a.Contains("fortune");
            string lengthName = a.Contains("skirmish") ? "skirmish" : a.Contains("siege") ? "siege" : "standard";
            a = a.Where(x => x != "frag" && x != "smoke" && x != "drill" && x != "fieldcraft" && x != "map" && x != "arms" && x != "senses" && x != "tempo" && x != "gunnery" && x != "ammo" && x != "tactics" && x != "fortune" && x != "skirmish" && x != "siege").ToArray();
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
                var mo = new MatchOptions { Seed = s, Us = us, Vc = vc, Cover = onMap ? Map.Cover() : null, Frag = frag, SquadSmoke = smoke, Drill = drill, Fieldcraft = craft, Arms = arms, Senses = senses, Gunnery = gunnery, Ammo = ammo, Tactics = tactics, Fortune = fortune };
                if (lengthName == "skirmish") mo.Length = MatchLength.Skirmish; else if (lengthName == "siege") mo.Length = MatchLength.Siege;
                if (tempo) { mo.CpRate = 1.6; mo.StartCp = 20; mo.OpeningStrength = 4; mo.MusterCost = 29; mo.MoraleRate = GameRate(mo.Length); }
                var r = Match.Run(mo);
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
