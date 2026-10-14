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
    ///   hash N [flags] [map]           a match's hash at ticks 100 and 1000 and at its end, for a pin
    ///   player N cost [flags]          the balance table: a player who buys line squads, and the plans left alone
    ///   scenario [id] [seeds] [...]    every scenario (or one), each side it offers, each difficulty: the same player's
    ///                                  wins, how long it took, and how it ended. To try a number before it is written
    ///                                  down: clock=S income=us,vc will=us,vc open=us,vc purse=us,vc back=N attacker=us|vc
    ///                                  mode=standard|siege|assault side=us|vc level=recruit|veteran|elite
    ///   scenario [id] hash [seed]      a scenario's hash, as the game plays it against a Veteran with nobody playing, for a pin
    ///   aware, muddle, brain [seed] [ticks] [seeds] [flags]
    ///                                  what the squads know and do; how much a match looks like a muddle;
    ///                                  circles, men not fighting, men out of place, grenades, snipers
    ///   watch seed from to [every] [flags] [man=N]
    ///                                  the squads tick by tick, or one man
    ///   blasts seed [flags]            where and when a burst kills a man: something to point a capture at
    ///   command [seed] [ticks] [seeds] [flags]
    ///                                  what the computer's commander does: what it raises, the cards it plays and what they hit, its levers
    ///   gunplay [seed] [ticks] [seeds] [flags]
    ///                                  the firing itself: each weapon's shots and what they do, the chance a shot had,
    ///                                  the man it was fired at and the men it could have been, where men die, assaults
    ///
    /// Flags are the rules by name (frag smoke drill fieldcraft arms senses gunnery ammo tactics fortune firecontrol commander ballistics vantage), and
    /// `tempo` for the game's own economy. `scenario=<id>` in their place, wherever a command takes them through the game's match
    /// (watch, aware, muddle, brain, shape, gunplay, command, blasts): that scenario as the game plays it.
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
                    "shape" => Shape(args),
                    "smooth" => Smooth(args),
                    "aware" => Aware(args),
                    "brain" => Brain(args),
                    "blasts" => Blasts(args),
                    "post" => PostTable(args),
                    "scenario" => ScenarioTable(args),
                    "squadsmoke" => SquadSmokeCount(args),
                    "towerfalls" => TowerFalls(args),
                    "gunplay" => Gunplay(args),
                    "command" => Command_(args),
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
                ? new LiveMatch(Game(seed, new[] { "fieldcraft", "arms", "senses", "gunnery", "ammo", "tempo" }.Concat(a.Where(x => x == "tactics" || x == "fortune" || x == "firecontrol" || x == "commander" || x == "ballistics" || x == "vantage" || x == "aiposts" || x == "cohesion" || x == "bearing")).ToArray()))
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
            bool frag = a.Contains("frag"), smoke = a.Contains("smoke"), drill = a.Contains("drill"), craft = a.Contains("fieldcraft"), onMap = a.Contains("map"), arms = a.Contains("arms"), senses = a.Contains("senses"), gunnery = a.Contains("gunnery"), ammo = a.Contains("ammo"), tactics = a.Contains("tactics"), fortune = a.Contains("fortune"), firecontrol = a.Contains("firecontrol"), commander = a.Contains("commander"), ballistics = a.Contains("ballistics"), vantage = a.Contains("vantage"), aiposts = a.Contains("aiposts"), cohesion = a.Contains("cohesion"), bearing = a.Contains("bearing"); bool inbound = a.Contains("inbound"), fragcard = a.Contains("fragcard"), emplace = a.Contains("emplace"), satchel = a.Contains("satchel"), destruction = a.Contains("destruction");
            a = a.Where(x => x != "frag" && x != "smoke" && x != "drill" && x != "fieldcraft" && x != "map" && x != "arms" && x != "senses" && x != "gunnery" && x != "ammo" && x != "tactics" && x != "fortune" && x != "firecontrol" && x != "commander" && x != "ballistics" && x != "vantage" && x != "aiposts" && x != "cohesion" && x != "bearing" && x != "fragcard" && x != "emplace" && x != "satchel" && x != "destruction").ToArray();
            int seed = a.Length > 1 ? int.Parse(a[1]) : 1;
            var m = new LiveMatch(new MatchOptions { Seed = seed, Us = Plan.Ceiling, Vc = Plan.Ceiling, Cover = onMap ? Map.Cover() : null, Frag = frag, SquadSmoke = smoke, Drill = drill, Fieldcraft = craft, Arms = arms, Senses = senses, Gunnery = gunnery, Ammo = ammo, Tactics = tactics, Fortune = fortune, FireControl = firecontrol, Commander = commander, Ballistics = ballistics, Vantage = vantage, ComputerPosts = aiposts, Cohesion = cohesion, Inbound = inbound, FragCard = fragcard, Emplace = emplace, Satchel = satchel, Destruction = destruction, Bearing = bearing });
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
                var m = new LiveMatch(Game(seed, new[] { "fieldcraft", "arms", "senses", "gunnery", "ammo" }.Concat(a.Where(x => x == "tactics" || x == "fortune" || x == "firecontrol" || x == "commander" || x == "ballistics" || x == "vantage" || x == "aiposts" || x == "cohesion" || x == "bearing")).ToArray()));
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
            // `post=us-sniper` (or us-mg, us-rifle): a fourth player, who also keeps that squad up on his side's post (Vantage).
            string posted = a.Where(x => x.StartsWith("post=")).Select(x => x.Substring(5)).FirstOrDefault();
            // `with=vc-marksman`: a player who also keeps one squad of that card alive, in the lanes.
            string also = a.Where(x => x.StartsWith("with=")).Select(x => x.Substring(5)).FirstOrDefault();
            foreach (var side in new[] { Side.Us, Side.Vc })
            foreach (var style in also != null ? new[] { "buys line squads", "buys them and one" } : posted == null ? new[] { "buys nothing", "buys line squads", "left to the plan" } : new[] { "buys line squads", "buys them and posts" })
            {
                int wins = 0; double secs = 0, men = 0, fell = 0;
                var each = new List<double>();
                for (int seed = 1; seed <= seeds; seed++)
                {
                    var o = Game(seed, new[] { "fieldcraft", "arms", "senses", "gunnery", "ammo" }.Concat(a.Where(x => x == "skirmish" || x == "siege" || x == "long" || x == "tactics" || x == "fortune" || x == "firecontrol" || x == "commander" || x == "ballistics" || x == "vantage" || x == "aiposts" || x == "cohesion" || x == "bearing" || x.StartsWith("rate="))).ToArray());
                    o.CpRate = 1.6; o.StartCp = 20; o.OpeningStrength = 4; o.MusterCost = muster;      // GameRoot's
                    o.Player = style == "left to the plan" ? (Side?)null : side;
                    var m = new LiveMatch(o);
                    var card = Deck.For(side).First(c => c.Group == CardGroup.Line);
                    while (!m.State.Over && m.State.Tick < m.Cap)
                    {
                        if (style == "buys them and one" && Deck.Find(side, also) is Card one)
                        {
                            bool none = !m.State.Squads.Any(q => q.Side == side && q.Card == also && Squads.AnyAlive(m.State, q.Id));
                            bool saving = none && Deck.CooldownLeft(m.State, side, one) == 0 && m.State.Squads.Count(q => q.Side == side) >= 2;
                            if (saving) { if (m.State.Tick % 20 == 10 && Deck.Blocked(m.State, side, one) == null) m.Issue(Command.Buy(side, also, m.State.Squads.Count % 2, 0)); m.Step(); continue; }
                        }
                        // (The post first, whenever it is empty: he saves for it after his first line squad.)
                        bool wanted = style == "buys them and posts" && Deck.Find(side, posted) is Card up && Vantage.Blocked(m.State, side, posted, 0) == null
                                      && Deck.CooldownLeft(m.State, side, up) == 0 && m.State.Squads.Count(q => q.Side == side) >= 2;
                        if (wanted && m.State.Tick % 20 == 10 && Deck.Blocked(m.State, side, Deck.Find(side, posted)) == null)
                            m.Issue(Command.BuyTo(side, posted, 0));
                        if (!wanted && style != "buys nothing" && style != "left to the plan" && m.State.Tick % 20 == 0 && Deck.Blocked(m.State, side, card) == null)
                            m.Issue(Command.Buy(side, card.Id, m.State.Squads.Count % 2, 0));
                        m.Step();
                    }
                    if (m.State.Winner == side) wins++;
                    secs += m.State.Tick * Tune.Dt;
                    each.Add(m.State.Tick * Tune.Dt);
                    men += m.State.Men.Count(x => x.Side == side);
                    fell += m.State.Men.Count(x => !x.Alive && x.High > 0);
                }
                double mean = secs / seeds, sd = Math.Sqrt(each.Sum(x => (x - mean) * (x - mean)) / seeds);
                Console.WriteLine($"  {side} player who {style,-18} wins {wins,2}/{seeds}  mean {mean,5:F0} s (from {each.Min():F0} to {each.Max():F0}, sd {sd:F0})  fielded {men / seeds:F0} men{(a.Contains("vantage") ? $"  killed up on a post {fell / seeds:F1}" : "")}");
            }
            return 0;
        }

        /// <summary>
        /// What a squad up on the post does (Vantage): `post seed0 seeds card [flags]`. The American player buys line
        /// squads and keeps <c>card</c>'s squad on the watchtower; a second column is the same squad bought into
        /// the lanes instead, as often. Per match: the men it killed, the men it lost and to what, and who won.
        /// </summary>
        private static int PostTable(string[] a)
        {
            var nums = a.Skip(1).Where(x => int.TryParse(x, out _)).Select(int.Parse).ToArray();
            int seed0 = nums.Length > 0 ? nums[0] : 1, seeds = nums.Length > 1 ? nums[1] : 24;
            string cardId = a.FirstOrDefault(x => x.StartsWith("us-") || x.StartsWith("vc-")) ?? "us-sniper";
            var flags = new[] { "fieldcraft", "arms", "senses", "gunnery", "ammo", "tactics", "fortune", "firecontrol", "ballistics", "tempo", "vantage" }.Concat(a.Where(x => x == "skirmish" || x == "siege" || x == "long" || x == "commander" || x == "cohesion" || x == "bearing")).ToArray();
            Console.WriteLine($"  {cardId}, seeds {seed0} to {seed0 + seeds - 1}, the American player buying line squads besides:");
            foreach (bool up in new[] { true, false })
            {
                int wins = 0, bought = 0, shots = 0, kills = 0, lost = 0, lostRifle = 0, lostGun = 0, lostSniper = 0, lostBurst = 0;
                double secs = 0, upSecs = 0, firstUp = 0, atP = 0, atD = 0; int firstUps = 0, at = 0, atSniper = 0;
                for (int seed = seed0; seed < seed0 + seeds; seed++)
                {
                    var o = Game(seed, flags);
                    o.Player = Side.Us;
                    // `early`: as a capture sends it (`call=card:2:0:5`): nobody playing, the squad bought at tick 5.
                    bool early = a.Contains("early");
                    if (early) o.Player = null;
                    var m = new LiveMatch(o);
                    var st = m.State;
                    if (early && up) { for (int t = 0; t < 5; t++) m.Step(); m.Issue(Command.BuyTo(Side.Us, cardId, 0)); m.Step(); foreach (var q in st.Squads) if (q.Post >= 0) { } }
                    var line = Deck.For(Side.Us).First(c => c.Group == CardGroup.Line);
                    var card = Deck.Find(Side.Us, cardId);
                    var mine = new HashSet<int>();          // the squads bought with the card
                    int first = -1;
                    while (!st.Over && st.Tick < m.Cap)
                    {
                        // The squad first, whenever there is none: he saves for it (after his first line squad) and buys line squads with the rest.
                        bool free = up ? Vantage.Blocked(st, Side.Us, cardId, 0) == null : !st.Squads.Any(q => mine.Contains(q.Id) && Squads.AnyAlive(st, q.Id));
                        bool wanted = free && Deck.CooldownLeft(st, Side.Us, card) == 0 && st.Squads.Count(q => q.Side == Side.Us) >= 2;
                        if (st.Tick % 20 == 10 && wanted && Deck.Blocked(st, Side.Us, card) == null)
                        {
                            m.Issue(up ? Command.BuyTo(Side.Us, cardId, 0) : Command.Buy(Side.Us, cardId, st.Squads.Count % 2, 0));
                            bought++;
                        }
                        else if (st.Tick % 20 == 0 && !wanted && Deck.Blocked(st, Side.Us, line) == null) m.Issue(Command.Buy(Side.Us, line.Id, st.Squads.Count % 2, 0));
                        int nsq = st.Squads.Count, from = st.Events.Count;
                        m.Step();
                        // `trace`: the posted squad's men, twice a second, for the first forty seconds of the first match.
                        if (a.Contains("trace") && up && seed == seed0 && st.Tick <= 800 && st.Tick % 10 == 0 && mine.Count > 0)
                            Console.WriteLine($"    t{st.Tick,4} " + string.Join("  ", st.Men.Where(x => mine.Contains(x.Squad)).Select(x => $"#{x.Id} {(x.Alive ? "" : "dead ")}({x.X:F1},{x.Z:F1}) up {x.High:F1} ladder {x.Climb} {x.Posture}")));
                        for (int i = early ? 0 : nsq; i < st.Squads.Count; i++) if (st.Squads[i].Side == Side.Us && st.Squads[i].Card == cardId && (!up || st.Squads[i].Post >= 0)) mine.Add(st.Squads[i].Id);
                        if (st.Men.Any(x => x.Alive && mine.Contains(x.Squad) && (!up || Vantage.Up(x)))) { upSecs += Tune.Dt; if (first < 0) first = st.Tick; }
                        for (int i = from; i < st.Events.Count; i++)
                        {
                            var e = st.Events[i];
                            if (e.Kind == EventKind.Fire && mine.Contains(st.Men[e.Id].Squad)) shots++;
                            if (e.Kind == EventKind.Fire && e.Target.HasValue && mine.Contains(st.Men[e.Target.Value].Squad))
                            {
                                at++; atP += Combat.HitChance(st, st.Men[e.Id], st.Men[e.Target.Value]); atD += Combat.Dist(st.Men[e.Id], st.Men[e.Target.Value]);
                                if (st.Men[e.Id].Weapon == Weapon.Sniper) atSniper++;
                            }
                            if (e.Kind != EventKind.Kill) continue;
                            var prev = i > 0 ? st.Events[i - 1] : default;
                            bool shot = i > 0 && prev.Tick == e.Tick && prev.Target == e.Id && (prev.Kind == EventKind.Fire || prev.Kind == EventKind.Stray || prev.Kind == EventKind.Through);
                            if (shot && prev.Kind != EventKind.Through && mine.Contains(st.Men[prev.Id].Squad)) kills++;
                            if (!mine.Contains(st.Men[e.Id].Squad)) continue;
                            lost++;
                            if (!shot) lostBurst++;
                            else { var w = st.Men[prev.Id].Weapon; if (w == Weapon.Sniper) lostSniper++; else if (w == Weapon.Rpd || w == Weapon.M60) lostGun++; else lostRifle++; }
                        }
                    }
                    if (st.Winner == Side.Us) wins++;
                    secs += st.Tick * Tune.Dt;
                    if (first >= 0) { firstUp += first * Tune.Dt; firstUps++; }
                }
                Console.WriteLine($"  {(up ? "on the tower" : "in the lanes "),-13} wins {wins,2}/{seeds}  match {secs / seeds,4:F0} s  bought {bought / (double)seeds:F1}  fighting {upSecs / seeds,4:F0} s (from {(firstUps > 0 ? firstUp / firstUps : 0):F0} s)"
                    + $"  shots {shots / (double)seeds,5:F1}  kills {kills / (double)seeds,4:F1}  lost {lost / (double)seeds:F1} men (rifle {lostRifle / (double)seeds:F1}, gun {lostGun / (double)seeds:F1}, sniper {lostSniper / (double)seeds:F1}, burst {lostBurst / (double)seeds:F1})"
                    + $"  fired at {at / (double)seeds:F1} times ({atSniper / (double)seeds:F1} by a sniper), mean chance {(at > 0 ? atP / at : 0):F3} at {(at > 0 ? atD / at : 0):F0} m");
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
        private static double GameRate(MatchLength l) => Scenario.RateFor(l);

        private static double[] Pair(string[] a, string key)
        {
            string v = a.Where(x => x.StartsWith(key + "=")).Select(x => x.Substring(key.Length + 1)).FirstOrDefault();
            if (v == null) return null;
            var p = v.Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            return p.Length == 1 ? new[] { p[0], p[0] } : p;
        }

        /// <summary>A scenario with the numbers given on the command line in place of its own (for trying them, before they are written down).</summary>
        private static Scenario Tried(Scenario s, string[] a)
        {
            string Arg(string key) => a.Where(x => x.StartsWith(key + "=")).Select(x => x.Substring(key.Length + 1)).FirstOrDefault();
            var t = new Scenario
            {
                Id = s.Id, Title = s.Title, DateLine = s.DateLine, Briefing = s.Briefing, Orders = s.Orders, Map = s.Map,
                Mode = s.Mode, ClockSeconds = s.ClockSeconds, Attacker = s.Attacker, LineBack = s.LineBack, Plays = s.Plays, Length = s.Length,
                Us = new Means { Income = s.Us.Income, Purse = s.Us.Purse, Will = s.Us.Will, Opening = s.Us.Opening },
                Vc = new Means { Income = s.Vc.Income, Purse = s.Vc.Purse, Will = s.Vc.Will, Opening = s.Vc.Opening },
                UsDeck = s.UsDeck, VcDeck = s.VcDeck,
            };
            if (Arg("mode") is string mode) t.Mode = mode == "siege" ? MatchMode.Siege : mode == "assault" ? MatchMode.Assault : MatchMode.Standard;
            if (Arg("attacker") is string att) t.Attacker = att == "us" ? Side.Us : Side.Vc;
            if (Arg("clock") is string clock) t.ClockSeconds = double.Parse(clock, System.Globalization.CultureInfo.InvariantCulture);
            if (Arg("back") is string back) t.LineBack = int.Parse(back);
            if (Arg("length") is string len) t.Length = len == "skirmish" ? MatchLength.Skirmish : len == "long" ? MatchLength.Long : MatchLength.Standard;
            if (Pair(a, "income") is double[] inc) { t.Us.Income = inc[0]; t.Vc.Income = inc[1]; }
            if (Pair(a, "purse") is double[] purse) { t.Us.Purse = purse[0]; t.Vc.Purse = purse[1]; }
            if (Pair(a, "will") is double[] will) { t.Us.Will = will[0]; t.Vc.Will = will[1]; }
            if (Pair(a, "open") is double[] open) { t.Us.Opening = (int)open[0]; t.Vc.Opening = (int)open[1]; }
            return t;
        }

        /// <summary>
        /// Every scenario, each side it offers, each difficulty (PLAN §12.27, S4): the player who buys a line squad
        /// whenever he can afford one and does nothing else. How often he wins, how long it took, and how the
        /// matches ended. `scenario [id] [seeds]`, and the numbers to try in place of the scenario's own.
        /// </summary>
        /// <summary>
        /// `squadsmoke N`: how often a squad throws its own smoke grenade, in the firebase as the game plays it against
        /// a Veteran with nobody playing, over N seeds; and where in the match the first one comes.
        /// </summary>
        /// <summary>
        /// `towerfalls N [aiposts]`: how often the watchtower is hurt and brought down (MatchOptions.Destruction), in the
        /// firebase as the game plays it against a Veteran with nobody playing, over N seeds; `aiposts`: with the
        /// computer's side manning it, as a player would (what draws the rockets onto it).
        /// </summary>
        private static int TowerFalls(string[] a)
        {
            int seeds = a.Length > 1 && int.TryParse(a[1], out int n) ? n : 48;
            int hurt = 0, fell = 0, killed = 0, came = 0, rockets = 0; var when = new List<double>(); var off = new List<double>();
            for (int seed = 1; seed <= seeds; seed++)
            {
                var o = Scenarios.Firebase.Options(seed, null, Difficulty.Veteran);
                o.ComputerPosts = a.Contains("aiposts");
                var m = new LiveMatch(o);
                while (!m.State.Over && m.State.Tick < m.Cap) m.Step();
                var ev = m.State.Events;
                if (ev.Any(e => e.Kind == EventKind.Damaged)) hurt++;
                var post = m.State.Posts.Count > 0 ? m.State.Posts[0] : null;
                if (post != null)
                    foreach (var e in ev.Where(e => e.Kind == EventKind.Launch && m.State.Men[e.Id].Weapon == Weapon.Rpg && e.Target.HasValue))
                    {
                        // (Where a rocket at a man up on it came down, against the platform's middle.)
                        bool up = m.State.Men[e.Target.Value].Side != m.State.Men[e.Id].Side && e.X.HasValue
                                  && JsMath.Hypot(e.X.Value - post.X, e.Z.Value - post.Z) < 30 && m.State.Men[e.Target.Value].Squad >= 0;
                        if (!up) continue;
                        rockets++; off.Add(JsMath.Hypot(e.X.Value - post.X, e.Z.Value - post.Z));
                    }
                int at = ev.FindIndex(e => e.Kind == EventKind.Collapsed);
                if (at >= 0)
                {
                    fell++; when.Add(ev[at].Tick * Tune.Dt);
                    for (int k = at + 1; k < ev.Count && ev[k].Tick == ev[at].Tick; k++) if (ev[k].Kind == EventKind.Kill) killed++;
                    came += m.State.Men.Count(x => x.Alive && x.Pin >= 1 && x.High == 0) > 0 ? 0 : 0;
                }
            }
            Console.WriteLine($"  the watchtower{(a.Contains("aiposts") ? " (manned by the computer)" : "")}: hurt in {hurt} of {seeds} matches, brought down in {fell}" +
                              (when.Count > 0 ? $", at {when.Average():F0} s on average; {killed} men killed by its fall" : "") +
                              $"; rockets that came down within 30 m of it: {rockets}" + (off.Count > 0 ? $", landing {off.Average():F1} m from its middle on average, {off.Count(d => d < Tune.TowerStruck)} within {Tune.TowerStruck} m" : ""));
            return 0;
        }

        private static int SquadSmokeCount(string[] a)
        {
            int seeds = a.Length > 1 && int.TryParse(a[1], out int n) ? n : 48;
            int thrown = 0, matches = 0, withOne = 0;
            var first = new List<double>();
            for (int seed = 1; seed <= seeds; seed++)
            {
                var m = new LiveMatch(Scenarios.Firebase.Options(seed, null, Difficulty.Veteran));
                var seen = new HashSet<int>();
                int here = 0;
                while (!m.State.Over && m.State.Tick < m.Cap)
                {
                    m.Step();
                    foreach (var ar in m.State.Areas)
                        if (ar.Kind == AreaKind.Smoke && ar.Radius == Tune.SquadSmokeRadius && seen.Add(ar.Id))
                        {
                            here++;
                            if (here == 1) first.Add(m.State.Tick * Tune.Dt);
                        }
                }
                matches++; thrown += here; if (here > 0) withOne++;
            }
            Console.WriteLine($"  squad smoke: {thrown} grenades in {matches} matches; {withOne} matches with at least one" +
                              (first.Count > 0 ? $"; the first at {first.Average():F0} s on average" : ""));
            return 0;
        }

        private static int ScenarioTable(string[] a)
        {
            var nums = a.Skip(1).Where(x => int.TryParse(x, out _)).Select(int.Parse).ToArray();
            int seeds = nums.Length > 0 ? nums[0] : 48;
            string only = a.Skip(1).FirstOrDefault(x => Scenarios.ById(x) != null);
            string sideArg = a.Where(x => x.StartsWith("side=")).Select(x => x.Substring(5)).FirstOrDefault();
            string levelArg = a.Where(x => x.StartsWith("level=")).Select(x => x.Substring(6)).FirstOrDefault();
            bool idle = a.Contains("idle");
            // `scenario [id] hash [seed]`: the scenario as the game plays it against a Veteran with nobody playing, for a pin.
            if (a.Contains("hash"))
            {
                int seed = nums.Length > 0 ? nums[0] : 1;
                foreach (var listed in Scenarios.All)
                {
                    if (only != null && listed.Id != only) continue;
                    var m = new LiveMatch(listed.Options(seed, null, Difficulty.Veteran));
                    var h = new List<uint> { LanesOfVietnam.Sim.Parity.Hash(m.State, 0) };
                    while (!m.State.Over && m.State.Tick < m.Cap) { m.Step(); h.Add(LanesOfVietnam.Sim.Parity.Hash(m.State, 0)); }
                    Console.WriteLine($"  {listed.Id} seed {seed}: {m.State.Tick} ticks, \"{m.State.Reason}\", at100 {h[100]}u, at1000 {h[Math.Min(1000, h.Count - 1)]}u, final {h[h.Count - 1]}u");
                }
                return 0;
            }
            foreach (var listed in Scenarios.All)
            {
                if (only != null && listed.Id != only) continue;
                var sc = Tried(listed, a);
                string means = $"us income {sc.Us.Income} purse {sc.Us.Purse} will {sc.Us.Will} opens {(sc.Us.Opening > 0 ? sc.Us.Opening : Scenario.OpeningStrength)}; vc income {sc.Vc.Income} purse {sc.Vc.Purse} will {sc.Vc.Will} opens {(sc.Vc.Opening > 0 ? sc.Vc.Opening : Scenario.OpeningStrength)}";
                Console.WriteLine($"  {sc.Id}: {sc.Mode}{(sc.Timed ? $", {sc.ClockSeconds:F0} s, {Match.Name(sc.Attacker)} comes on" : "")}{(sc.Mode == MatchMode.Assault ? $", line {sc.LineBack} back" : "")}; {means}");
                foreach (var side in new[] { Side.Us, Side.Vc })
                {
                    if (sideArg != null ? sideArg != Match.Name(side) : !sc.Offers(side)) continue;
                    foreach (Difficulty level in Enum.GetValues(typeof(Difficulty)))
                    {
                        if (levelArg != null && levelArg != level.ToString().ToLowerInvariant()) continue;
                        int wins = 0, fell = 0, crushed = 0; double secs = 0;
                        var each = new List<double>();
                        var endings = new SortedDictionary<string, int>();
                        for (int seed = 1; seed <= seeds; seed++)
                        {
                            // `card=<id>`: the player buys that squad instead of his first line squad; `nosatchel`, `noemplace`,
                            // `nodestruction`: without that rule.
                            var opts = sc.Options(seed, side, level);
                            if (a.Contains("nosatchel")) opts.Satchel = false;
                            if (a.Contains("noemplace")) opts.Emplace = false;
                            if (a.Contains("nodestruction")) opts.Destruction = false;
                            var m = new LiveMatch(opts);
                            string cardArg = a.Where(x => x.StartsWith("card=")).Select(x => x.Substring(5)).FirstOrDefault();
                            var card = cardArg != null ? Deck.Find(side, cardArg) : Deck.For(side).First(c => c.Group == CardGroup.Line && Deck.Has(m.State, side, c));
                            var frag = Deck.Grenades(side);
                            // `tower=<id>`: he keeps a squad of that card up his side's watchtower (MatchOptions.Vantage), sending
                            // one whenever it stands and nobody of his is on it, before he buys his line squads.
                            string towerArg = a.Where(x => x.StartsWith("tower=")).Select(x => x.Substring(6)).FirstOrDefault();
                            var towerCard = towerArg != null ? Deck.Find(side, towerArg) : null;
                            while (!m.State.Over && m.State.Tick < m.Cap)
                            {
                                if (!idle && towerCard != null && m.State.Tick % 20 == 0 && Vantage.Blocked(m.State, side, towerCard.Id, 0) == null
                                    && Deck.Blocked(m.State, side, towerCard) == null)
                                    m.Issue(Command.BuyTo(side, towerCard.Id, 0));
                                else if (!idle && m.State.Tick % 20 == 0 && Deck.Blocked(m.State, side, card) == null)
                                    m.Issue(Command.Buy(side, card.Id, m.State.Squads.Count % 2, 0));
                                // GRENADES (FragCard), played as his men used to throw of themselves: at an enemy in cover that one
                                // of them could reach. `nofrag` leaves it unplayed: a player who never uses it.
                                else if (!idle && !a.Contains("nofrag") && m.State.Tick % 10 == 5 && Deck.Blocked(m.State, side, frag) == null)
                                    foreach (var man in m.State.Men)
                                    {
                                        if (!man.Alive || man.Side != side || man.Grenades <= 0 || man.High > 0) continue;
                                        var at = Frag.PickTarget(m.State, man);
                                        if (at == null) continue;
                                        int l = Math.Abs(at.Z - Tune.Lanes[0]) < Math.Abs(at.Z - Tune.Lanes[1]) ? 0 : 1;
                                        m.Issue(Command.Buy(side, frag.Id, l, at.X));
                                        break;
                                    }
                                m.Step();
                            }
                            if (m.State.Winner == side) wins++;
                            var ev = m.State.Events;
                            int went = ev.FindIndex(e => e.Kind == EventKind.Collapsed);
                            if (went >= 0)
                            {
                                fell++;
                                for (int k = went + 1; k < ev.Count && ev[k].Tick == ev[went].Tick; k++) if (ev[k].Kind == EventKind.Kill) crushed++;
                            }
                            secs += m.State.Tick * Tune.Dt;
                            each.Add(m.State.Tick * Tune.Dt);
                            string why = m.State.Reason ?? "tick cap";
                            endings[why] = endings.GetValueOrDefault(why) + 1;
                        }
                        Console.WriteLine($"    as {Match.Name(side)}, {level,-7} wins {wins,2}/{seeds}  mean {secs / seeds,4:F0} s ({each.Min():F0} to {each.Max():F0})  {string.Join(", ", endings.Select(e => $"{e.Key} {e.Value}"))}"
                                          + (fell > 0 ? $"; the tower brought down in {fell}, {crushed} men killed by its fall" : ""));
                    }
                }
            }
            return 0;
        }

        private static MatchOptions Game(int seed, string[] a)
        {
            // `scenario=<id>`: the scenario as the game plays it against a Veteran with nobody playing, in place of the flags.
            string named = a.Where(x => x.StartsWith("scenario=")).Select(x => x.Substring(9)).FirstOrDefault();
            if (named != null)
            {
                var sc = Scenarios.ById(named) ?? throw new ArgumentException($"no scenario called \"{named}\" ({string.Join(" | ", Scenarios.All.Select(x => x.Id))})");
                var so = sc.Options(seed, null, Difficulty.Veteran);
                foreach (var x in a) if (x.StartsWith("rate=")) so.MoraleRate = double.Parse(x.Substring(5), System.Globalization.CultureInfo.InvariantCulture);
                return so;
            }
            var o = new MatchOptions
            {
                Seed = seed, Us = Plan.Ceiling, Vc = Plan.Ceiling, Cover = Map.Cover(), Length = MatchLength.Standard,
                Frag = !a.Contains("nofrag"), SquadSmoke = !a.Contains("nosmoke"), Drill = !a.Contains("nodrill"),
                Fieldcraft = a.Contains("fieldcraft"), Arms = a.Contains("arms"), Senses = a.Contains("senses"), Gunnery = a.Contains("gunnery"), Ammo = a.Contains("ammo"),
                Tactics = a.Contains("tactics"), Fortune = a.Contains("fortune"), FireControl = a.Contains("firecontrol"), Commander = a.Contains("commander"), Ballistics = a.Contains("ballistics"), Vantage = a.Contains("vantage"), ComputerPosts = a.Contains("aiposts"), Cohesion = a.Contains("cohesion"), Bearing = a.Contains("bearing"), Inbound = a.Contains("inbound"),
            };
            // GameRoot's economy: one squad a side to open, points at the game's rate.
            if (a.Contains("skirmish")) o.Length = MatchLength.Skirmish;
            if (a.Contains("siege") || a.Contains("long")) o.Length = MatchLength.Long;      // (`siege` is what the long match was called until scenarios)
            // (The game's own dials, as Scenario keeps them: a Veteran's cost, the length's rate.)
            if (a.Contains("tempo")) { o.CpRate = Scenario.CpRate; o.StartCp = Scenario.StartCp; o.OpeningStrength = Scenario.OpeningStrength; o.MusterCost = Scenario.CostFor(Difficulty.Veteran); o.MoraleRate = GameRate(o.Length); }
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
                    if (int.Parse(arg.Substring(4)) >= st.Men.Count) continue;
                    var p = st.Men[int.Parse(arg.Substring(4))];
                    var q = st.Squads[p.Squad];
                    var foe = st.Men.Where(e => e.Alive && e.Side != p.Side).OrderBy(e => Combat.Dist(p, e)).FirstOrDefault();
                    var quarry = p.Alive && st.Fieldcraft ? Fieldcraft.Quarry(st, p, q) : null;
                    // (His place, as near as it can be said from outside: his place in cover, or his rank's behind the anchor.)
                    double pdir = Combat.Advance(p.Side);
                    double sx = p.Place >= 0 && p.PlaceCover >= 0 ? Fieldcraft.PlaceOf(st.Cover[p.PlaceCover], p.Place, pdir).X : q.AnchorX - pdir * p.Rank * Cohesion.Gap(st, q);
                    Console.WriteLine($"  t{st.Tick} man {p.Id} {(p.Alive ? "" : "DEAD ")}{p.X,6:F2},{p.Z,6:F2} {p.Posture,-8} pin {p.Pin:F2} rest {p.Rest,3} cd {p.Cooldown,2} cover {p.Cover,2} place {p.Place}@{p.PlaceCover} rank {p.Rank}{(p.Vault > 0 ? " vault" : "")}{(p.Reloading > 0 ? " reload" : "")} | sq{q.Id} {q.Order} {q.Task} tgt {q.Target} anchor {q.AnchorX:F1},{q.AnchorZ:F1} {(q.Halted ? "halt" : "go")} threat {q.Threat} | quarry {(quarry == null ? "-" : quarry.Id.ToString())} nearest {(foe == null ? "-" : $"{foe.Id} at {foe.X:F1},{foe.Z:F1} {Combat.Dist(p, foe):F1} m")} | place x {sx:F1} ({(sx - p.X) * pdir:+0.0;-0.0} m on) dwell {p.Dwell} wait {p.Wait} still {(p.Still ? 1 : 0)} turn {(Tactics.Turn(st, q, p) ? 1 : 0)} seen {(q.ThreatSeen ? 1 : 0)}");
                }
                if (a.Any(x => x.StartsWith("man="))) continue;
                if ((st.Tick - from) % every != 0) continue;
                Console.WriteLine($"  t{st.Tick} {st.Phase} morale {st.Morale[0]:F2}/{st.Morale[1]:F2} smoke {st.Areas.Count(x => x.Kind == AreaKind.Smoke)} barrages {st.Areas.Count(x => x.Kind == AreaKind.Barrage)} traps {st.Areas.Count(x => x.Kind == AreaKind.Trap)}");
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
        /// <summary>
        /// What a squad looks like and how often it changes its mind (the owner, 2026-10-11: "still tons of
        /// wandering, weird formations"): `shape seed0 ticks seeds [flags]`. How long a squad is strung out along
        /// its lane, by what it is doing; men left behind and a lead man out alone; men walking up singly while
        /// the rest are down; and how often a squad changes its task, and changes it straight back.
        /// </summary>
        private static int Shape(string[] a)
        {
            var nums = a.Skip(1).Where(x => int.TryParse(x, out _)).Select(int.Parse).ToArray();
            int seed0 = nums.Length > 0 ? nums[0] : 3, ticks = nums.Length > 1 ? nums[1] : 2400, seeds = nums.Length > 2 ? nums[2] : 1;
            var length = new Dictionary<string, (double sum, double n, double strung)>();
            double squadSeconds = 0, manSeconds = 0, behind = 0, alone = 0, single = 0, taskChanges = 0, flipBacks = 0, stuck = 0, minutes = 0, mixed = 0;
            var flips = new Dictionary<string, double>();
            var moves = new Dictionary<string, double>();
            var singles = new Dictionary<string, double>();
            var setOffs = new Dictionary<string, double>();
            double offs = 0;
            for (int seed = seed0; seed < seed0 + seeds; seed++)
            {
                var m = new LiveMatch(Game(seed, a));
                var st = m.State;
                var stillFor = new Dictionary<int, int>();
                var taskWas = new Dictionary<int, (SquadTask now, SquadTask before, int since)>();
                var anchorAt = new Dictionary<int, (double x, int tick)>();
                for (int t = 0; t < ticks && !st.Over; t++)
                {
                    var was = st.Men.Where(x => x.Alive).ToDictionary(x => x.Id, x => x.X);
                    m.Step();
                    if (st.Phase != Phase.Fight) continue;
                    minutes += Tune.Dt / 60;
                    // A man sets off: he has been still half a second or more, and now he moves. By what his squad is at.
                    foreach (var x in st.Men)
                    {
                        if (!x.Alive || !was.TryGetValue(x.Id, out double wx0)) continue;
                        bool moved = Math.Abs(x.X - wx0) > 0.02;
                        int still = stillFor.TryGetValue(x.Id, out int sf) ? sf : 0;
                        if (moved && still >= 10)
                        {
                            var q = st.Squads[x.Squad];
                            offs++;
                            string why = $"{(q.Order == Order.Fallback ? "falling back" : q.Task.ToString()),-12} {(q.Halted ? "halted" : "going ")} {(x.Place >= 0 ? "to a place in cover" : x.Cover >= 0 ? "in cover, no place" : "in the open")}";
                            setOffs[why] = (setOffs.TryGetValue(why, out double o0) ? o0 : 0) + 1;
                        }
                        stillFor[x.Id] = moved ? 0 : still + 1;
                    }
                    foreach (var sq in st.Squads)
                    {
                        if (sq.Post >= 0) continue;
                        var men = st.Men.Where(x => x.Alive && x.Squad == sq.Id).OrderBy(x => x.X * Combat.Advance(sq.Side)).ToList();
                        if (men.Count == 0) continue;
                        // Its task, and whether it has just gone back to the one it left under three seconds ago.
                        if (!taskWas.TryGetValue(sq.Id, out var tw)) tw = (sq.Task, sq.Task, st.Tick);
                        if (tw.now != sq.Task)
                        {
                            taskChanges++;
                            string mv = $"{tw.now} > {sq.Task}";
                            moves[mv] = (moves.TryGetValue(mv, out double m0) ? m0 : 0) + 1;
                            if (sq.Task == tw.before && st.Tick - tw.since <= 60)
                            {
                                flipBacks++;
                                string k = $"{sq.Task} > {tw.now} > {sq.Task}";
                                flips[k] = (flips.TryGetValue(k, out double f0) ? f0 : 0) + 1;
                            }
                            tw = (sq.Task, tw.now, st.Tick);
                        }
                        taskWas[sq.Id] = tw;
                        if (men.Count < 2) continue;
                        squadSeconds += Tune.Dt; manSeconds += men.Count * Tune.Dt;
                        double span = Math.Abs(men[men.Count - 1].X - men[0].X);
                        string what = sq.Order == Order.Fallback ? "falling back" : sq.Task.ToString();
                        var l = length.TryGetValue(what, out var l0) ? l0 : default;
                        length[what] = (l.sum + span * Tune.Dt, l.n + Tune.Dt, l.strung + (span > 8 ? Tune.Dt : 0));
                        // Men left behind: further than 6 m back from the middle man. A lead man out alone: 5 m or more ahead of the next.
                        double mid = men[men.Count / 2].X;
                        foreach (var x in men) if ((mid - x.X) * Combat.Advance(sq.Side) > 6) behind += Tune.Dt;
                        if (Math.Abs(men[men.Count - 1].X - men[men.Count - 2].X) >= 5 && sq.Order != Order.Fallback) alone += Tune.Dt;
                        // One man of it walking while every other is still: a man coming up by himself.
                        int moving = men.Count(x => was.TryGetValue(x.Id, out double wx) && Math.Abs(x.X - wx) > 0.02);
                        if (moving == 1 && men.Count >= 3 && sq.Order != Order.Fallback)
                        {
                            single += Tune.Dt;
                            var who = men.First(x => was.TryGetValue(x.Id, out double wx) && Math.Abs(x.X - wx) > 0.02);
                            string why = $"{sq.Task,-9} {(sq.Halted ? "halted" : "going ")} {(who.Place >= 0 ? "to a place in cover" : who.Cover >= 0 ? "in cover" : "in the open")} {(who.Posture == Posture.Prone ? "crawling" : who.Posture == Posture.Crouched ? "crouched" : "on his feet")}";
                            singles[why] = (singles.TryGetValue(why, out double s0) ? s0 : 0) + Tune.Dt;
                        }
                        // Going in, or closing, and its middle not a metre further on in three seconds.
                        if (sq.Task == SquadTask.Assault || sq.Task == SquadTask.Close)
                        {
                            if (!anchorAt.TryGetValue(sq.Id, out var aa) || st.Tick - aa.tick > 60) { if (anchorAt.ContainsKey(sq.Id) && Math.Abs(mid - aa.x) < 1) stuck += 3; anchorAt[sq.Id] = (mid, st.Tick); }
                        }
                        else anchorAt.Remove(sq.Id);
                        // Two squads of one side in one lane, one's men among the other's.
                        foreach (var other in st.Squads)
                        {
                            if (other.Id <= sq.Id || other.Side != sq.Side || other.Lane != sq.Lane || other.Post >= 0) continue;
                            var theirs = st.Men.Where(x => x.Alive && x.Squad == other.Id).ToList();
                            if (theirs.Count < 2) continue;
                            double lo = Math.Max(men.Min(x => x.X), theirs.Min(x => x.X)), hi = Math.Min(men.Max(x => x.X), theirs.Max(x => x.X));
                            if (hi - lo > 2) mixed += Tune.Dt;
                        }
                    }
                }
            }
            Console.WriteLine($"  seeds {seed0}..{seed0 + seeds - 1}, {minutes:F1} minutes of fighting");
            Console.WriteLine($"  a squad's length along its lane, metres (and the share of its time over 8 m):");
            foreach (var kv in length.OrderByDescending(k => k.Value.n))
                Console.WriteLine($"      {kv.Key,-13} {kv.Value.sum / kv.Value.n,5:F1} m   over 8 m {100 * kv.Value.strung / kv.Value.n,3:F0}%   ({100 * kv.Value.n / squadSeconds,3:F0}% of squad time)");
            Console.WriteLine($"  men left 6 m behind      {100 * behind / manSeconds,5:F1}% of man time");
            Console.WriteLine($"  lead man 5 m out alone   {100 * alone / squadSeconds,5:F1}% of squad time");
            Console.WriteLine($"  one man walking alone    {100 * single / squadSeconds,5:F1}% of squad time");
            Console.WriteLine($"  squads one in another    {mixed / minutes,5:F1} squad-seconds a minute");
            Console.WriteLine($"  going in, not moving     {stuck / minutes,5:F1} squad-seconds a minute");
            Console.WriteLine($"  a squad changes its task {taskChanges / (squadSeconds / 60),5:F1} times a minute, {flipBacks / (squadSeconds / 60):F1} of them straight back within 3 s");
            foreach (var kv in flips.OrderByDescending(k => k.Value).Take(6)) Console.WriteLine($"      {kv.Key,-40} {kv.Value / (squadSeconds / 60),5:F2}");
            Console.WriteLine("  its changes of task, a squad-minute:");
            foreach (var kv in moves.OrderByDescending(k => k.Value).Take(12)) Console.WriteLine($"      {kv.Key,-40} {kv.Value / (squadSeconds / 60),5:F2}");
            Console.WriteLine($"  a man sets off (after half a second still) {offs / (manSeconds / 60):F1} times a minute, by what his squad is at:");
            foreach (var kv in setOffs.OrderByDescending(k => k.Value).Take(12)) Console.WriteLine($"      {kv.Key,-60} {kv.Value / (manSeconds / 60),5:F2}");
            Console.WriteLine("  one man walking alone, % of squad time, by what the squad is at:");
            foreach (var kv in singles.OrderByDescending(k => k.Value).Take(10)) Console.WriteLine($"      {kv.Key,-70} {100 * kv.Value / squadSeconds,5:F1}");
            return 0;
        }

        /// <summary>
        /// How a man moves, set against how a man does (the owner, 2026-10-11: "improve gameplay. Make it smooth
        /// like real life"). `smooth [seed] [ticks] [seeds] [flags]`; with `strip`, the men of the first match
        /// drawn a line each, a letter a quarter of a second (- on his feet and moving; on his feet and still S, or
        /// r getting up, c climbing, b barred, t waiting his turn, f waiting for his file, o for the other half,
        /// ! pinned; K on a knee, k moving on it; P flat, p crawling).
        ///   a posture kept   how long he stays as he is before he changes, and how often it is under two seconds
        ///   up for nothing   he gets to his feet and is down again having gone less than a metre and a half
        ///   bobbing          down, up and down again (or flat, a knee, flat) inside three seconds
        ///   a move           from setting off to stopping (a stop under a quarter of a second is not one): how far, how long
        ///   a stutter        a stop of under a second between two moves the same way
        ///   a lurch          his speed changing by half a metre a second or more between two steps of one move
        /// </summary>
        private static int Smooth(string[] a)
        {
            var nums = a.Skip(1).Where(x => int.TryParse(x, out _)).Select(int.Parse).ToArray();
            int seed0 = nums.Length > 0 ? nums[0] : 3, ticks = nums.Length > 1 ? nums[1] : 2400, seeds = nums.Length > 2 ? nums[2] : 1;
            bool strip = a.Contains("strip");
            double manSeconds = 0, changes = 0, shortHolds = 0, holds = 0, nothing = 0, bobs = 0, moves = 0, shortMoves = 0, briefMoves = 0, metres = 0, moveSeconds = 0, stutters = 0, lurches = 0;
            var time = new Dictionary<string, double>();
            var heldFor = new Dictionary<string, List<double>>();
            var whyNothing = new Dictionary<string, double>();
            var whyBob = new Dictionary<string, double>();
            var whyShort = new Dictionary<string, double>();
            var whyStutter = new Dictionary<string, double>();
            var lengths = new List<double>();
            double stood = 0;
            var whyStood = new Dictionary<string, double>();
            var whyBrief = new Dictionary<string, double>();
            var whyLurch = new Dictionary<string, double>();
            double lying = 0, shortOf = 0;
            var whyShortOf = new Dictionary<string, double>();
            var whyLying = new Dictionary<string, double>();
            double halted = 0;
            int shown = 0;
            var whyHalted = new Dictionary<string, double>();
            for (int seed = seed0; seed < seed0 + seeds; seed++)
            {
                var m = new LiveMatch(Game(seed, a));
                var st = m.State;
                // By man: his posture and the tick he took it; the two before it and when; where he was when he got up.
                var held = new Dictionary<int, (Posture p, int since, Posture before, int beforeSince, Posture first, int firstSince)>();
                var rose = new Dictionary<int, (double x, double z, bool on)>();
                // His move: where and when it began, the ticks since he last stepped, the last step's speed, and its way.
                var move = new Dictionary<int, (double x, double z, int from, int idle, double v, double hx, double hz, bool on)>();
                var last = new Dictionary<int, (int tick, double hx, double hz)>();
                var lines = new Dictionary<int, System.Text.StringBuilder>();
                for (int t = 0; t < ticks && !st.Over; t++)
                {
                    var was = st.Men.Where(x => x.Alive).ToDictionary(x => x.Id, x => (x.X, x.Z));
                    m.Step();
                    if (st.Phase != Phase.Fight) continue;
                    foreach (var p in st.Men)
                    {
                        if (!p.Alive || !was.TryGetValue(p.Id, out var w)) continue;
                        var sq = st.Squads[p.Squad];
                        if (sq.Post >= 0) continue;
                        manSeconds += Tune.Dt;
                        double dx = p.X - w.X, dz = p.Z - w.Z, step = Math.Sqrt(dx * dx + dz * dz), v = step / Tune.Dt;
                        bool moving = step > 0.01;
                        string doing = (sq.Order == Order.Fallback ? "falling back" : sq.Task.ToString()) + (p.Pin >= Tune.PinDrop ? ", pinned" : "");
                        string state = (p.Posture == Posture.Standing ? "on his feet" : p.Posture == Posture.Crouched ? "on a knee" : "flat") + (moving ? ", moving" : ", still");
                        time[state] = time.GetValueOrDefault(state) + Tune.Dt;
                        // On his feet and not going anywhere, with the enemy about: why.
                        if (p.Posture == Posture.Standing && !moving && (sq.Task != SquadTask.March || sq.Order == Order.Fallback))
                        {
                            stood += Tune.Dt;
                            string wk = $"{p.Idle,-10} {doing}";
                            whyStood[wk] = whyStood.GetValueOrDefault(wk) + Tune.Dt;
                        }
                        // And on the march, his squad going: a man who stops in the middle of it.
                        else if (p.Posture == Posture.Standing && !moving && !sq.Halted && sq.Order != Order.Hold)
                        {
                            halted += Tune.Dt;
                            string wk = $"{p.Idle,-10} {(p.Place >= 0 ? "to a place in cover" : p.Cover >= 0 ? "in cover" : "in the open")}";
                            // `show=Blocked` names the first of a kind, to be watched (`simcs watch seed from to 1 man=N`).
                            string asked = a.FirstOrDefault(x => x.StartsWith("show="))?.Substring(5);
                            if (asked != null && p.Idle.ToString() == asked && shown++ < 12) Console.WriteLine($"    seed {seed} t{st.Tick} man {p.Id} sq{sq.Id} {sq.Task} cover {p.Cover} place {p.Place}@{p.PlaceCover} rank {p.Rank} {p.X:F1},{p.Z:F1}");
                            whyHalted[wk] = whyHalted.GetValueOrDefault(wk) + Tune.Dt;
                        }
                        if (strip && seed == seed0)
                        {
                            if (!lines.TryGetValue(p.Id, out var sb)) lines[p.Id] = sb = new System.Text.StringBuilder(new string(' ', Math.Max(0, (st.Tick - st.ContactTick) / 5)));
                            if (st.Tick % 5 == 0)
                            {
                                char c = p.Posture == Posture.Standing ? 'S' : p.Posture == Posture.Crouched ? 'K' : 'P';
                                // (On his feet and still: getting up r, climbing c, barred b, waiting his turn t or for his file f or the other half o.)
                                if (c == 'S' && !moving)
                                    c = p.Idle == Idle.Rising ? 'r' : p.Idle == Idle.Climbing ? 'c' : p.Idle == Idle.Blocked ? 'b' : p.Idle == Idle.HisTurn ? 't'
                                      : p.Idle == Idle.FileWait ? 'f' : p.Idle == Idle.OtherHalf ? 'o' : p.Idle == Idle.Pinned ? '!' : 'S';
                                else if (moving) c = c == 'S' ? '-' : char.ToLower(c);
                                sb.Append(c);
                            }
                        }
                        // Down and still while his squad is closing or going in: what keeps him there.
                        if (p.Posture != Posture.Standing && !moving && (sq.Task == SquadTask.Close || sq.Task == SquadTask.Assault) && sq.Order != Order.Fallback)
                        {
                            lying += Tune.Dt;
                            string wk = $"{sq.Task,-8} {p.Idle}";
                            whyLying[wk] = whyLying.GetValueOrDefault(wk) + Tune.Dt;
                        }
                        // Down and still in the open, with a place in cover he has not got to: what keeps him from it.
                        if (p.Posture != Posture.Standing && !moving && p.Cover < 0 && p.Place >= 0 && p.PlaceCover >= 0 && sq.Order != Order.Fallback)
                        {
                            shortOf += Tune.Dt;
                            string wk = $"{(sq.Order == Order.Fallback ? "falling back" : sq.Task.ToString()),-10} {p.Idle}";
                            whyShortOf[wk] = whyShortOf.GetValueOrDefault(wk) + Tune.Dt;
                        }
                        // --- his posture
                        if (!held.TryGetValue(p.Id, out var h)) h = (p.Posture, -1, p.Posture, -1, p.Posture, -1);
                        if (h.p != p.Posture)
                        {
                            changes++;
                            double kept = h.since < 0 ? 99 : (st.Tick - h.since) * Tune.Dt;
                            if (h.since >= 0) holds++;
                            if (kept < 2)
                            {
                                shortHolds++;
                                string shk = $"{h.before} > {h.p} > {p.Posture}  {doing}";
                                whyBrief[shk] = whyBrief.GetValueOrDefault(shk) + 1;
                            }
                            string hk = h.p == Posture.Standing ? "on his feet" : h.p == Posture.Crouched ? "on a knee" : "flat";
                            if (!heldFor.TryGetValue(hk, out var hl)) heldFor[hk] = hl = new List<double>();
                            if (h.since >= 0) hl.Add(kept);
                            bool up = p.Posture == Posture.Standing, wasUp = h.p == Posture.Standing;
                            if (up) rose[p.Id] = (p.X, p.Z, true);
                            else if (wasUp && rose.TryGetValue(p.Id, out var r) && r.on)
                            {
                                // Down again: how far did being up take him?
                                if (Math.Sqrt((p.X - r.x) * (p.X - r.x) + (p.Z - r.z) * (p.Z - r.z)) < 1.5) { nothing++; whyNothing[doing] = whyNothing.GetValueOrDefault(doing) + 1; }
                                rose[p.Id] = (0, 0, false);
                            }
                            // Back to how he was two changes ago, all of it inside three seconds.
                            if (p.Posture == h.before && h.beforeSince >= 0 && (st.Tick - h.beforeSince) * Tune.Dt <= 3 && h.beforeSince != h.since)
                            {
                                bobs++;
                                string bk = $"{h.before} > {h.p} > {p.Posture}  {doing}";
                                whyBob[bk] = whyBob.GetValueOrDefault(bk) + 1;
                            }
                            h = (p.Posture, st.Tick, h.p, h.since, h.before, h.beforeSince);
                        }
                        held[p.Id] = h;
                        // --- his moves
                        if (!move.TryGetValue(p.Id, out var mv)) mv = (p.X, p.Z, st.Tick, 0, 0, 0, 0, false);
                        if (moving)
                        {
                            if (!mv.on)
                            {
                                // Off again under a second after he stopped, the same way: a stutter.
                                if (last.TryGetValue(p.Id, out var l) && (st.Tick - l.tick) * Tune.Dt < 1 && l.hx * dx + l.hz * dz > 0)
                                {
                                    stutters++;
                                    string sk = $"{doing,-22} {(sq.Halted ? "halted" : "going ")} {(p.Place >= 0 ? "to a place in cover" : p.Cover >= 0 ? "in cover" : "in the open")}";
                                    whyStutter[sk] = whyStutter.GetValueOrDefault(sk) + 1;
                                }
                                mv = (w.X, w.Z, st.Tick, 0, v, dx, dz, true);
                            }
                            else if (mv.idle == 0 && Math.Abs(v - mv.v) >= 0.5)
                            {
                                lurches++;
                                string lk = $"{(v > mv.v ? "faster" : "slower")}  {doing,-20} {(p.Cover >= 0 ? "in cover" : "in the open")}";
                                whyLurch[lk] = whyLurch.GetValueOrDefault(lk) + 1;
                                if (a.Contains("show=lurch") && shown++ < 14) Console.WriteLine($"    seed {seed} t{st.Tick} man {p.Id} sq{sq.Id} {sq.Task} {mv.v:F2} > {v:F2} m/s pace {p.Pace:F2} cover {p.Cover} vault {p.Vault} pin {p.Pin:F2}");
                            }
                            mv.idle = 0; mv.v = v; mv.hx = dx; mv.hz = dz;
                        }
                        else if (mv.on && ++mv.idle >= 5)
                        {
                            // The move is over (it ended five ticks ago).
                            double far = Math.Sqrt((p.X - mv.x) * (p.X - mv.x) + (p.Z - mv.z) * (p.Z - mv.z)), took = (st.Tick - 5 - mv.from + 1) * Tune.Dt;
                            moves++; metres += far; moveSeconds += took; lengths.Add(far);
                            if (took < 1) briefMoves++;
                            if (far < 1.5)
                            {
                                shortMoves++;
                                string mk = $"{doing,-22} {(sq.Halted ? "halted" : "going ")} {(p.Place >= 0 ? "to a place in cover" : p.Cover >= 0 ? "in cover" : "in the open")}";
                                whyShort[mk] = whyShort.GetValueOrDefault(mk) + 1;
                            }
                            last[p.Id] = (st.Tick - 5, mv.hx, mv.hz);
                            mv.on = false;
                        }
                        move[p.Id] = mv;
                    }
                }
                if (strip && seed == seed0)
                    foreach (var sq in st.Squads.Take(8))
                    {
                        Console.WriteLine($"  squad {sq.Id} {sq.Side} lane {sq.Lane}, a letter a quarter of a second from first contact:");
                        foreach (var p in st.Men.Where(x => x.Squad == sq.Id)) if (lines.TryGetValue(p.Id, out var sb)) Console.WriteLine($"    {p.Id,3} {sb}");
                    }
            }
            double mm = manSeconds / 60;
            Console.WriteLine($"  seeds {seed0}..{seed0 + seeds - 1}, {mm:F0} man-minutes in the fight; a man, a minute:");
            Console.WriteLine("  his time:");
            foreach (var kv in time.OrderByDescending(k => k.Value)) Console.WriteLine($"      {kv.Key,-22} {100 * kv.Value / manSeconds,5:F1}%");
            Console.WriteLine($"  on his feet and still, in contact   {stood / mm,5:F1} s of a man's minute");
            foreach (var kv in whyStood.OrderByDescending(k => k.Value).Take(12)) Console.WriteLine($"      {kv.Key,-44} {kv.Value / mm,5:F2}");
            Console.WriteLine($"  down and still, his squad closing or going in   {lying / mm,5:F1} s of a man's minute");
            foreach (var kv in whyLying.OrderByDescending(k => k.Value).Take(12)) Console.WriteLine($"      {kv.Key,-44} {kv.Value / mm,5:F2}");
            Console.WriteLine($"  down in the open short of his place in cover   {shortOf / mm,5:F1} s of a man's minute");
            foreach (var kv in whyShortOf.OrderByDescending(k => k.Value).Take(10)) Console.WriteLine($"      {kv.Key,-44} {kv.Value / mm,5:F2}");
            Console.WriteLine($"  on his feet and still, his squad marching   {halted / mm,5:F1} s of a man's minute");
            foreach (var kv in whyHalted.OrderByDescending(k => k.Value).Take(8)) Console.WriteLine($"      {kv.Key,-44} {kv.Value / mm,5:F2}");
            Console.WriteLine($"  changes posture          {changes / mm,5:F1}   ({100 * shortHolds / Math.Max(1, holds):F0}% of them after under 2 s as he was)");
            foreach (var kv in heldFor.OrderBy(k => k.Key))
            {
                var l = kv.Value; l.Sort();
                Console.WriteLine($"      {kv.Key,-12} kept {l[l.Count / 2],5:F1} s (the middle one), a quarter under {l[l.Count / 4]:F1} s, under 2 s {100.0 * l.Count(x => x < 2) / l.Count:F0}%");
            }
            Console.WriteLine($"  as he was under 2 s      {shortHolds / mm,5:F2}   (how he was before > how he was briefly > what he changed to)");
            foreach (var kv in whyBrief.OrderByDescending(k => k.Value).Take(12)) Console.WriteLine($"      {kv.Key,-56} {kv.Value / mm,5:F2}");
            Console.WriteLine($"  up for nothing           {nothing / mm,5:F2}");
            foreach (var kv in whyNothing.OrderByDescending(k => k.Value).Take(6)) Console.WriteLine($"      {kv.Key,-40} {kv.Value / mm,5:F2}");
            Console.WriteLine($"  bobbing                  {bobs / mm,5:F2}");
            foreach (var kv in whyBob.OrderByDescending(k => k.Value).Take(8)) Console.WriteLine($"      {kv.Key,-56} {kv.Value / mm,5:F2}");
            lengths.Sort();
            Console.WriteLine($"  moves                    {moves / mm,5:F1}   {metres / Math.Max(1, moves):F1} m and {moveSeconds / Math.Max(1, moves):F1} s each; the middle one {(lengths.Count > 0 ? lengths[lengths.Count / 2] : 0):F1} m");
            Console.WriteLine($"      under a metre and a half {shortMoves / mm,5:F2}   under a second {briefMoves / mm,5:F2}");
            foreach (var kv in whyShort.OrderByDescending(k => k.Value).Take(8)) Console.WriteLine($"          {kv.Key,-56} {kv.Value / mm,5:F2}");
            Console.WriteLine($"  stutters                 {stutters / mm,5:F2}");
            foreach (var kv in whyStutter.OrderByDescending(k => k.Value).Take(8)) Console.WriteLine($"      {kv.Key,-60} {kv.Value / mm,5:F2}");
            Console.WriteLine($"  lurches                  {lurches / mm,5:F2}");
            foreach (var kv in whyLurch.OrderByDescending(k => k.Value).Take(8)) Console.WriteLine($"      {kv.Key,-60} {kv.Value / mm,5:F2}");
            return 0;
        }

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
                        else if (e.Kind == EventKind.Stray) { cause = "a stray round"; far = false; }
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

        private static string Class(Weapon w)
            => w == Weapon.M60 || w == Weapon.Rpd ? "machine gun" : w == Weapon.Sniper ? "sniper" : w == Weapon.Smg ? "submachine gun"
             : w == Weapon.M79 || w == Weapon.Rpg ? "launcher" : w == Weapon.Mortar ? "mortar" : "rifle";

        /// <summary>
        /// The firing itself, as the game plays it (the owner, 2026-10-09: "focus on firing/gunplay mechanics and
        /// AI smartness"). `gunplay [seed] [ticks] [seeds] [flags]`; the game's own match is
        /// `fieldcraft arms senses gunnery ammo tactics fortune tempo`.
        ///   by weapon        man-seconds in the fight, shots, rounds, men killed, a shot's mean distance and chance
        ///   a shot's chance  <see cref="Combat.HitChance"/> as the tick began, for the man fired at and for every man the
        ///                    firer could have fired at instead (one his squad has in sight, in reach, in his own lane)
        ///   a better target  one of those with twice the chance or more, and two in a hundred at least
        ///   the same man     a shot at a man one of the firer's own squad fired at within the last two seconds, while
        ///                    another he could have fired at had nobody of his squad on him
        ///   bursts           a launcher's, a mortar's or a grenade's: the enemy inside its radius as it went off
        ///   where men die    what the man was doing when he was killed, and his squad
        ///   assaults         a squad going in: what it came to, and what it cost each side
        ///   reloading        squad-seconds in a fight with half its rifles and guns or more out of it at once
        /// </summary>
        private static int Gunplay(string[] a)
        {
            var nums = a.Skip(1).Where(x => int.TryParse(x, out _)).Select(int.Parse).ToArray();
            int seed0 = nums.Length > 0 ? nums[0] : 3, ticks = nums.Length > 1 ? nums[1] : 2400, seeds = nums.Length > 2 ? nums[2] : 6;
            double minutes = 0;
            var classes = new[] { "rifle", "machine gun", "submachine gun", "sniper", "launcher", "mortar" };
            var seconds = classes.ToDictionary(c => c, c => 0.0); var shotsBy = classes.ToDictionary(c => c, c => 0.0);
            var roundsBy = classes.ToDictionary(c => c, c => 0.0); var killsBy = classes.ToDictionary(c => c, c => 0.0);
            var distBy = classes.ToDictionary(c => c, c => 0.0); var chanceBy = classes.ToDictionary(c => c, c => 0.0); var knownBy = classes.ToDictionary(c => c, c => 0.0);
            var blindBy = classes.ToDictionary(c => c, c => 0.0); var pinnedBy = classes.ToDictionary(c => c, c => 0.0);
            string[] bands = { "under 1 in 100", "1 to 3", "3 to 6", "6 to 12", "12 in 100 or more" };
            var band = new double[bands.Length]; var bandKills = new double[bands.Length];
            double shots = 0, known = 0, better = 0, same = 0, sameSpare = 0, kills = 0;
            var atWhat = new Dictionary<string, double>(); var betterWhat = new Dictionary<string, double>();
            var diedHow = new Dictionary<string, double>(); var diedTask = new Dictionary<string, double>(); var diedBy = new Dictionary<string, double>();
            var burstRounds = new Dictionary<string, double>(); var burstEmpty = new Dictionary<string, double>(); var burstMen = new Dictionary<string, double>();
            var burstKills = new Dictionary<string, double>(); var burstOwn = new Dictionary<string, double>(); var burstLone = new Dictionary<string, double>(); var burstCover = new Dictionary<string, double>();
            double assaults = 0, assaultOwn = 0, assaultTheirs = 0, assaultTicks = 0; var assaultEnd = new Dictionary<string, double>();
            var assaultOwnBy = new Dictionary<string, double>(); var assaultTheirsBy = new Dictionary<string, double>();
            var beganN = new Dictionary<string, double>(); var beganOwn = new Dictionary<string, double>(); var beganTheirs = new Dictionary<string, double>();
            double fightSeconds = 0, reloadSeconds = 0, downSeconds = 0, flatSeconds = 0, contactSeconds = 0;
            var sniperAt = new Dictionary<string, double>();

            for (int seed = seed0; seed < seed0 + seeds; seed++)
            {
                var m = new LiveMatch(Game(seed, a));
                var st = m.State;
                var chance = new Dictionary<long, double>();
                var could = new Dictionary<int, List<int>>();
                var lastAt = new Dictionary<(int squad, int target), int>();
                var grenadeBy = new Dictionary<int, string>();
                var going = new Dictionary<int, (int tick, int own, int threat, int theirs)>();
                var began = new Dictionary<int, string>();
                var was = new List<(Posture posture, int cover, bool moving, double x, double z)>();
                var tasks = new List<SquadTask>();
                int Alive(int squad) => squad < 0 ? 0 : st.Men.Count(p => p.Alive && p.Squad == squad);
                // (A man who came on during the tick was not there when it began: as he is now.)
                (Posture posture, int cover, bool moving, double x, double z) Was(int id) => id < was.Count ? was[id] : (st.Men[id].Posture, st.Men[id].Cover, false, st.Men[id].X, st.Men[id].Z);
                for (int t = 0; t < ticks && !st.Over; t++)
                {
                    // As the tick begins: how every man is, and for each man about to fire, the men he could fire at and his chance at each.
                    was.Clear(); tasks.Clear(); chance.Clear(); could.Clear();
                    foreach (var p in st.Men) was.Add((p.Posture, p.Cover, Squads.IsMoving(p), p.X, p.Z));
                    foreach (var q in st.Squads) tasks.Add(q.Task);
                    if (st.Senses && st.Phase == Phase.Fight)
                        foreach (var p in st.Men)
                        {
                            if (!p.Alive || p.Cooldown > 1 || Arms.Of(st, p).Bursts) continue;
                            var sq = st.Squads[p.Squad];
                            double reach = Tactics.Reach(st, p);
                            var list = new List<int>();
                            foreach (var e in st.Men)
                            {
                                if (!e.Alive || e.Side == p.Side || !Senses.Knows(st, sq, e.Squad)) continue;
                                if (st.Gunnery && !Gunnery.InArc(st, p, e)) continue;
                                if (Combat.Dist(p, e) > reach || Combat.SmokeBlocks(st, p.X, p.Z, e.X, e.Z)) continue;
                                list.Add(e.Id);
                                chance[(long)p.Id * 100000 + e.Id] = Combat.HitChance(st, p, e);
                            }
                            could[p.Id] = list;
                        }

                    int from = st.Events.Count;
                    m.Step();
                    bool fight = st.Phase == Phase.Fight;
                    string cause = "something else"; string burstKind = null;
                    for (int i = from; i < st.Events.Count; i++)
                    {
                        var e = st.Events[i];
                        switch (e.Kind)
                        {
                            case EventKind.Fire:
                            {
                                var by = st.Men[e.Id]; var at = st.Men[e.Target.Value];
                                string c = Class(by.Weapon);
                                bool blind = e.Amount == 1;
                                shots++; shotsBy[c]++; roundsBy[c] += Math.Max(1, e.Rounds); distBy[c] += Combat.Dist(by.X, by.Z, Was(at.Id).x, Was(at.Id).z);
                                if (blind) blindBy[c]++;
                                cause = "a " + c; burstKind = null;
                                var w = Was(at.Id);
                                string what = w.cover >= 0 ? (w.posture == Posture.Standing ? "on his feet in cover" : "down in cover")
                                            : w.moving ? "moving in the open" : w.posture == Posture.Standing ? "on his feet in the open, still" : w.posture == Posture.Crouched ? "kneeling in the open" : "flat in the open";
                                atWhat[what] = atWhat.GetValueOrDefault(what) + 1;
                                if (by.Weapon == Weapon.Sniper) { string k = Class(at.Weapon); sniperAt[k] = sniperAt.GetValueOrDefault(k) + 1; }
                                // The same man: a squad-mate fired at him within the last two seconds, and another he could have fired at had nobody on him.
                                bool mate = lastAt.TryGetValue((by.Squad, at.Id), out int la) && st.Tick - la <= 40;
                                if (mate) same++;
                                lastAt[(by.Squad, at.Id)] = st.Tick;
                                if (blind || !chance.TryGetValue((long)by.Id * 100000 + at.Id, out double p)) break;
                                known++; knownBy[c]++; chanceBy[c] += p;
                                int b = p < 0.01 ? 0 : p < 0.03 ? 1 : p < 0.06 ? 2 : p < 0.12 ? 3 : 4;
                                band[b]++;
                                // (His kill, if it is one, is the next event.)
                                if (i + 1 < st.Events.Count && st.Events[i + 1].Kind == EventKind.Kill && st.Events[i + 1].Id == at.Id) bandKills[b]++;
                                bool across = st.Squads[at.Squad].Lane != st.Squads[by.Squad].Lane;
                                double best = p; int bestId = at.Id; bool spare = false;
                                foreach (int o in could[by.Id])
                                {
                                    if (o == at.Id || (!across && st.Squads[st.Men[o].Squad].Lane != st.Squads[by.Squad].Lane)) continue;
                                    double po = chance[(long)by.Id * 100000 + o];
                                    if (po > best) { best = po; bestId = o; }
                                    if (!(lastAt.TryGetValue((by.Squad, o), out int lo) && st.Tick - lo <= 40)) spare = true;
                                }
                                if (mate && spare) sameSpare++;
                                if (best >= 2 * p && best >= 0.02)
                                {
                                    better++;
                                    var bw = Was(bestId);
                                    string k = $"fired at a man {what}; one {(bw.cover >= 0 ? (bw.posture == Posture.Standing ? "on his feet in cover" : "down in cover") : bw.moving ? "moving in the open" : bw.posture == Posture.Standing ? "on his feet in the open, still" : bw.posture == Posture.Crouched ? "kneeling in the open" : "flat in the open")} was {(Combat.Dist(by.X, by.Z, bw.x, bw.z) < Combat.Dist(by.X, by.Z, w.x, w.z) ? "nearer" : "further")}";
                                    betterWhat[k] = betterWhat.GetValueOrDefault(k) + 1;
                                }
                                break;
                            }
                            case EventKind.Launch:
                            case EventKind.GrenadeThrown:
                            {
                                string k = e.Kind == EventKind.GrenadeThrown ? "hand grenade" : st.Men[e.Id].Weapon.ToString();
                                foreach (var g in st.Grenades) if (g.X == e.X && g.Z == e.Z) grenadeBy[g.Id] = k;
                                var at = Was(e.Target.Value);
                                int near = 0;
                                foreach (var o in st.Men) if (o.Alive && o.Side == st.Men[e.Target.Value].Side && o.Id != e.Target.Value && Combat.Dist(Was(o.Id).x, Was(o.Id).z, at.x, at.z) <= (e.Kind == EventKind.GrenadeThrown ? Tune.FragRadius : Arms.Of(st.Men[e.Id].Weapon).Blast)) near++;
                                burstRounds[k] = burstRounds.GetValueOrDefault(k) + 1;
                                if (near == 0) burstLone[k] = burstLone.GetValueOrDefault(k) + 1;
                                if (at.cover >= 0) burstCover[k] = burstCover.GetValueOrDefault(k) + 1;
                                break;
                            }
                            case EventKind.GrenadeBlast:
                            {
                                burstKind = grenadeBy.TryGetValue(e.Id, out string k) ? k : "hand grenade";
                                cause = burstKind == "hand grenade" ? "a hand grenade" : burstKind == "Mortar" ? "a mortar" : "a launcher";
                                double r = burstKind == "hand grenade" ? Tune.FragRadius : Arms.Of((Weapon)Enum.Parse(typeof(Weapon), burstKind)).Blast;
                                int inside = 0;
                                foreach (var o in st.Men) if (o.DiedAt < 0 || o.DiedAt == st.Tick) { if (o.Side != e.Side && Combat.Dist(Was(o.Id).x, Was(o.Id).z, e.X.Value, e.Z.Value) < r) inside++; }
                                burstMen[burstKind] = burstMen.GetValueOrDefault(burstKind) + inside;
                                if (inside == 0) burstEmpty[burstKind] = burstEmpty.GetValueOrDefault(burstKind) + 1;
                                break;
                            }
                            case EventKind.Shell: cause = "a shell"; burstKind = null; break;
                            case EventKind.Melee: cause = "hand to hand"; burstKind = null; break;
                            case EventKind.Through: cause = "a round through the man in front"; burstKind = null; break;
                            case EventKind.Stray: cause = "a stray round, from " + (Class(st.Men[e.Id].Weapon) == "machine gun" ? "a machine gun" : Class(st.Men[e.Id].Weapon) == "sniper" ? "a sniper" : "a rifle or a submachine gun"); burstKind = null; break;
                            case EventKind.TrapSprung: cause = "a trap"; burstKind = null; break;
                            case EventKind.Kill:
                            {
                                var dead = st.Men[e.Id];
                                kills++;
                                diedBy[cause] = diedBy.GetValueOrDefault(cause) + 1;
                                if (cause.StartsWith("a ") && killsBy.ContainsKey(cause.Substring(2)) && burstKind == null) killsBy[cause.Substring(2)]++;
                                if (burstKind != null)
                                {
                                    string c = burstKind == "Mortar" ? "mortar" : burstKind == "hand grenade" ? null : "launcher";
                                    if (c != null) killsBy[c]++;
                                    // (A burst is its thrower's side's: a man of that side it kills is its own.)
                                    var blast = st.Events.Skip(from).Take(i - from).Last(x => x.Kind == EventKind.GrenadeBlast);
                                    if (dead.Side == blast.Side) burstOwn[burstKind] = burstOwn.GetValueOrDefault(burstKind) + 1;
                                    else burstKills[burstKind] = burstKills.GetValueOrDefault(burstKind) + 1;
                                }
                                var w = Was(dead.Id);
                                string how = w.cover >= 0 ? (w.posture == Posture.Standing ? "on his feet in cover" : "down in cover")
                                           : w.moving ? "moving in the open" : w.posture == Posture.Standing ? "on his feet in the open, still" : w.posture == Posture.Crouched ? "kneeling in the open" : "flat in the open";
                                diedHow[how] = diedHow.GetValueOrDefault(how) + 1;
                                string task = $"{dead.Side} {(dead.Squad < tasks.Count ? tasks[dead.Squad] : st.Squads[dead.Squad].Task)}";
                                diedTask[task] = diedTask.GetValueOrDefault(task) + 1;
                                break;
                            }
                        }
                    }

                    // Each weapon's time in the fight, and how much of it down.
                    if (fight)
                        foreach (var p in st.Men)
                        {
                            if (!p.Alive) continue;
                            string c = Class(p.Weapon);
                            seconds[c] += Tune.Dt;
                            if (p.Pin >= Tune.PinDrop) pinnedBy[c] += Tune.Dt;
                        }

                    // Squads: going in, and what it came to; a fight with half its weapons reloading; men down under fire.
                    for (int si = 0; si < st.Squads.Count && fight; si++)
                    {
                        var sq = st.Squads[si];
                        var live = st.Men.Where(p => p.Alive && p.Squad == si).ToList();
                        bool goingIn = sq.Task == SquadTask.Assault && live.Count > 0;
                        if (goingIn && !going.ContainsKey(si))
                        {
                            going[si] = (st.Tick, live.Count, sq.Threat, Alive(sq.Threat));
                            FireControl.Front(st, sq, out int en, out int dn, out bool gu);
                            double gap = st.Men.Where(e => e.Alive && e.Side != sq.Side && Math.Abs(e.Z - Tune.Lanes[sq.Lane]) <= 6.5).Select(e => live.Min(p => Combat.Dist(p, e))).DefaultIfEmpty(99).Min();
                            began[si] = $"{(en == 0 ? "nobody in front" : live.Count >= 3 * en ? "three to one or more" : live.Count >= 2 * en ? "two to one" : live.Count >= en ? "even to two to one" : "outnumbered"),-22} {(en == 0 ? "" : dn * 3 >= en * 2 ? "two in three down" : dn * 3 >= en ? "a third down" : "few down"),-18} {(gu ? "a gun up" : ""),-9} {(gap < 8 ? "under 8 m" : gap < 14 ? "8 to 14 m" : "over 14 m")}";
                        }
                        else if (!goingIn && going.TryGetValue(si, out var g))
                        {
                            going.Remove(si);
                            assaults++;
                            int lost = g.own - live.Count, cost = g.theirs - Alive(g.threat);
                            assaultOwn += lost; assaultTheirs += cost; assaultTicks += st.Tick - g.tick;
                            string end = live.Count == 0 ? "wiped out going in"
                                       : Alive(g.threat) == 0 ? "the enemy squad dead"
                                       : sq.Task == SquadTask.Withdraw ? "broke and ran"
                                       : g.threat >= 0 && st.Squads[g.threat].Task == SquadTask.Withdraw ? "the enemy squad running"
                                       : sq.Task == SquadTask.March ? "the enemy gone from in front of it"
                                       : sq.Task == SquadTask.Firefight ? "back to a firefight: " + (live.Average(p => p.Pin) >= (Tune.PinDrop + Tune.PinStop) * 0.5 ? "its men went down under fire"
                                            : st.Tick - g.tick < Tune.AssaultMax ? "told to hold where it was"
                                            : Alive(g.threat) * 2 > live.Count * 4 ? "it was outnumbered two to one"
                                            : st.Men.Any(e => e.Alive && e.Side != sq.Side && (e.Weapon == Weapon.M60 || e.Weapon == Weapon.Rpd) && e.Pin < Tune.PinDrop && Math.Abs(e.X - sq.AnchorX) <= Tune.AssaultReach && st.Squads[e.Squad].Lane == sq.Lane) ? "a machine gun was up in front of it"
                                            : !st.Men.Any(e => e.Alive && e.Side != sq.Side && Math.Abs(e.Z - Tune.Lanes[sq.Lane]) <= 6.5 && (e.X - sq.AnchorX) * Combat.Advance(sq.Side) >= 0 && (e.X - sq.AnchorX) * Combat.Advance(sq.Side) <= Tune.AssaultReach && Senses.Remembers(st, sq, e.Squad)) ? "nobody left in front of it"
                                            : "stopped short of him, its own heads going down")
                                       : $"back to a {sq.Task.ToString().ToLower()}";
                            assaultEnd[end] = assaultEnd.GetValueOrDefault(end) + 1;
                            assaultOwnBy[end] = assaultOwnBy.GetValueOrDefault(end) + lost; assaultTheirsBy[end] = assaultTheirsBy.GetValueOrDefault(end) + cost;
                            if (began.TryGetValue(si, out string how)) { beganN[how] = beganN.GetValueOrDefault(how) + 1; beganOwn[how] = beganOwn.GetValueOrDefault(how) + lost; beganTheirs[how] = beganTheirs.GetValueOrDefault(how) + cost; began.Remove(si); }
                        }
                        if (live.Count == 0 || !sq.ThreatSeen || (sq.Task != SquadTask.Firefight && sq.Task != SquadTask.Close && sq.Task != SquadTask.Assault)) continue;
                        fightSeconds += Tune.Dt;
                        var guns = live.Where(p => !Arms.Of(st, p).Bursts).ToList();
                        if (guns.Count >= 2 && guns.Count(p => p.Reloading > 0) * 2 >= guns.Count) reloadSeconds += Tune.Dt;
                        foreach (var p in live)
                        {
                            contactSeconds += Tune.Dt;
                            if (p.Pin >= Tune.PinDrop) downSeconds += Tune.Dt;
                            if (p.Pin >= Tune.PinStop) flatSeconds += Tune.Dt;
                        }
                    }
                }
                minutes += st.Tick * Tune.Dt / 60;
            }

            void Top(Dictionary<string, double> d, double of, int n = 8) { foreach (var kv in d.OrderByDescending(k => k.Value).Take(n)) Console.WriteLine($"      {kv.Key,-84} {kv.Value / minutes,6:F1}  ({100 * kv.Value / Math.Max(1, of):F0}%)"); }
            Console.WriteLine($"  seeds {seed0}..{seed0 + seeds - 1}, {minutes:F1} minutes; a minute:");
            Console.WriteLine("  by weapon          men in the fight   shots  rounds   killed   a kill every   mean distance   mean chance   blind   time pinned");
            foreach (var c in classes)
            {
                if (seconds[c] == 0) continue;
                Console.WriteLine($"    {c,-16} {seconds[c] / minutes / 60,14:F1} {shotsBy[c] / minutes,9:F1} {roundsBy[c] / minutes,7:F0} {killsBy[c] / minutes,8:F2} {(killsBy[c] > 0 ? (shotsBy[c] + (c == "launcher" || c == "mortar" ? burstRounds.Where(k => (k.Key == "Mortar") == (c == "mortar") && k.Key != "hand grenade").Sum(k => k.Value) : 0)) / killsBy[c] : 0),10:F0} shots {(shotsBy[c] > 0 ? distBy[c] / shotsBy[c] : 0),11:F1} m {(knownBy[c] > 0 ? 100 * chanceBy[c] / knownBy[c] : 0),12:F1}% {(shotsBy[c] > 0 ? 100 * blindBy[c] / shotsBy[c] : 0),6:F0}% {100 * pinnedBy[c] / seconds[c],12:F0}%");
            }
            Console.WriteLine($"  shots              {shots / minutes:F0}; men killed {kills / minutes:F1}");
            Top(diedBy, kills, 10);
            Console.WriteLine($"  a shot's chance    of {known / minutes:F0} aimed shots whose chance is known:");
            for (int b = 0; b < bands.Length; b++) Console.WriteLine($"      {bands[b],-84} {band[b] / minutes,6:F1}  ({100 * band[b] / Math.Max(1, known):F0}%), killing {bandKills[b] / minutes:F2}");
            Console.WriteLine($"  fired at a man     of {shots / minutes:F0} shots:");
            Top(atWhat, shots);
            Console.WriteLine($"  a better target    {better / minutes:F1} ({100 * better / Math.Max(1, known):F0}% of aimed shots): a man he could have fired at with twice the chance or more");
            Top(betterWhat, better, 8);
            Console.WriteLine($"  the same man       {same / minutes:F1} ({100 * same / Math.Max(1, shots):F0}% of shots) at a man a squad-mate fired at in the last 2 s; {sameSpare / minutes:F1} ({100 * sameSpare / Math.Max(1, shots):F0}%) while another in reach had nobody of the squad on him");
            if (sniperAt.Count > 0) { Console.WriteLine("  a sniper fires at"); Top(sniperAt, sniperAt.Values.Sum()); }
            Console.WriteLine("  bursts             rounds   nobody inside   enemy inside, a round   killed, a round   own men killed   at a lone man   at a man in cover");
            foreach (var k in burstRounds.Keys.OrderBy(x => x))
                Console.WriteLine($"    {k,-16} {burstRounds[k] / minutes,6:F1} {100 * burstEmpty.GetValueOrDefault(k) / burstRounds[k],14:F0}% {burstMen.GetValueOrDefault(k) / burstRounds[k],18:F2} {burstKills.GetValueOrDefault(k) / burstRounds[k],18:F2} {burstOwn.GetValueOrDefault(k) / minutes,16:F2} {100 * burstLone.GetValueOrDefault(k) / burstRounds[k],14:F0}% {100 * burstCover.GetValueOrDefault(k) / burstRounds[k],18:F0}%");
            Console.WriteLine("  where men die");
            Top(diedHow, kills);
            Console.WriteLine("  and their squad was");
            Top(diedTask, kills, 10);
            Console.WriteLine($"  assaults           {assaults / minutes:F2}, {assaultTicks / Math.Max(1, assaults) * Tune.Dt:F1} s each; each cost the squad going in {assaultOwn / Math.Max(1, assaults):F2} men and the enemy squad {assaultTheirs / Math.Max(1, assaults):F2}; they ended (and cost: its own, the enemy's)");
            foreach (var kv in assaultEnd.OrderByDescending(k => k.Value)) Console.WriteLine($"      {kv.Key,-60} {kv.Value / minutes,6:F2}  ({100 * kv.Value / Math.Max(1, assaults):F0}%)   {assaultOwnBy[kv.Key] / kv.Value:F2}, {assaultTheirsBy[kv.Key] / kv.Value:F2}");
            if (a.Contains("why"))
            {
                Console.WriteLine("  an assault as it began (its number against the enemy in front, his heads, a gun, the nearest man)   a minute   cost: its own, the enemy's");
                foreach (var kv in beganN.OrderByDescending(k => k.Value).Take(24)) Console.WriteLine($"      {kv.Key,-70} {kv.Value / minutes,6:F2}   {beganOwn[kv.Key] / kv.Value:F2}, {beganTheirs[kv.Key] / kv.Value:F2}");
            }
            Console.WriteLine($"  in a fight         {fightSeconds / minutes:F0} squad-seconds with the enemy in sight; {reloadSeconds / minutes:F1} of them ({100 * reloadSeconds / Math.Max(1e-9, fightSeconds):F0}%) with half its rifles and guns reloading at once");
            Console.WriteLine($"                     its men down {100 * downSeconds / Math.Max(1e-9, contactSeconds):F0}% of that time, flat and hardly firing {100 * flatSeconds / Math.Max(1e-9, contactSeconds):F0}%");
            return 0;
        }

        /// <summary>
        /// What the computer's commander does, as the game plays it: `command [seed] [ticks] [seeds] [flags]`,
        /// both sides the computer's. A match:
        ///   raised        the squads each side raises, by card
        ///   call-ins      each card played, and the points that went on cards against those that went on men
        ///   from the sky  each barrage: the enemy inside its circle as it began, and whom it killed
        ///   traps         laid, sprung, and whom they killed; tunnels opened
        ///   levers        position-seconds on hold and on go
        /// </summary>
        private static int Command_(string[] a)
        {
            var nums = a.Skip(1).Where(x => int.TryParse(x, out _)).Select(int.Parse).ToArray();
            int seed0 = nums.Length > 0 ? nums[0] : 3, ticks = nums.Length > 1 ? nums[1] : 14400, seeds = nums.Length > 2 ? nums[2] : 6;
            var raised = new Dictionary<string, double>(); var calls = new Dictionary<string, double>();
            var inside = new Dictionary<string, double>(); var killed = new Dictionary<string, double>(); var own = new Dictionary<string, double>();
            double sprung = 0, trapKills = 0, hold = 0, go = 0, onCards = 0, usWins = 0, seconds = 0;
            var played = new Dictionary<string, double>(); var spent = new double[2];
            var holdBy = new double[2]; var goBy = new double[2];
            for (int seed = seed0; seed < seed0 + seeds; seed++)
            {
                var o = Game(seed, a);
                var m = new LiveMatch(o);
                var st = m.State;
                var kind = new Dictionary<int, string>(); var sideOf = new Dictionary<int, Side>();
                int squads = st.Squads.Count;
                while (!st.Over && st.Tick < ticks)
                {
                    int from = st.Events.Count;
                    m.Step();
                    string cause = null; int area = -1;
                    for (int i = from; i < st.Events.Count; i++)
                    {
                        var e = st.Events[i];
                        switch (e.Kind)
                        {
                            case EventKind.AreaStart:
                            {
                                var ar = st.Areas.FirstOrDefault(x => x.Id == e.Id);
                                if (ar == null) break;
                                string k = ar.Kind == AreaKind.Barrage ? (ar.Radius > 14 ? "artillery" : "air strike")
                                         : ar.Kind == AreaKind.Smoke ? (ar.Radius > 10 ? "smoke" : null)
                                         : ar.Radius > 4 ? "tripwire" : ar.Radius > 3 ? "punji pit" : "spider hole";
                                if (k == null) break;        // a squad's own smoke grenade
                                kind[ar.Id] = k; sideOf[ar.Id] = ar.Side;
                                calls[k] = calls.GetValueOrDefault(k) + 1;
                                onCards += k == "artillery" ? 30 : k == "air strike" ? 40 : k == "smoke" ? 10 : k == "tripwire" ? 18 : k == "punji pit" ? 12 : 8;
                                if (ar.Kind == AreaKind.Barrage) inside[k] = inside.GetValueOrDefault(k) + st.Men.Count(p => p.Alive && p.Side != ar.Side && Combat.Dist(p.X, p.Z, ar.X, ar.Z) <= ar.Radius);
                                break;
                            }
                            case EventKind.Shell: cause = "shell"; area = e.Id; break;
                            case EventKind.TrapSprung: cause = "trap"; area = e.Id; sprung++; break;
                            case EventKind.Fire: case EventKind.Melee: case EventKind.GrenadeBlast: case EventKind.Through: case EventKind.Stray: cause = null; break;
                            case EventKind.Kill:
                                if (cause == "trap") trapKills++;
                                else if (cause == "shell" && kind.TryGetValue(area, out string bk))
                                {
                                    if (st.Men[e.Id].Side == sideOf[area]) own[bk] = own.GetValueOrDefault(bk) + 1;
                                    else killed[bk] = killed.GetValueOrDefault(bk) + 1;
                                }
                                break;
                        }
                    }
                    // (A medevac and a tunnel leave no area: each card played, from its own cooldown just set.)
                    foreach (var side in Match.Sides)
                        foreach (var card in Deck.For(side))
                            if (card.Group == CardGroup.Call && Deck.CooldownLeft(st, side, card) == card.Cooldown - 1) { played[card.Name] = played.GetValueOrDefault(card.Name) + 1; spent[(int)side] += card.Cost; }
                    for (; squads < st.Squads.Count; squads++)
                    {
                        var q = st.Squads[squads];
                        string k = $"{q.Side} {q.Card ?? "(baseline)"}";
                        raised[k] = raised.GetValueOrDefault(k) + 1;
                    }
                    foreach (var c in st.Cover)
                    {
                        if (c.LeverUs == Lever.Hold) { hold += Tune.Dt; holdBy[0] += Tune.Dt; } else if (c.LeverUs == Lever.Go) { go += Tune.Dt; goBy[0] += Tune.Dt; }
                        if (c.LeverVc == Lever.Hold) { hold += Tune.Dt; holdBy[1] += Tune.Dt; } else if (c.LeverVc == Lever.Go) { go += Tune.Dt; goBy[1] += Tune.Dt; }
                    }
                }
                seconds += st.Tick * Tune.Dt;
                if (st.Winner == Side.Us) usWins++;
            }
            Console.WriteLine($"  seeds {seed0}..{seed0 + seeds - 1}: a match {seconds / seeds:F0} s; the Americans win {usWins:F0} of {seeds}");
            Console.WriteLine("  raised, a match (the opening's squads apart)");
            foreach (var kv in raised.OrderBy(k => k.Key)) Console.WriteLine($"      {kv.Key,-22} {kv.Value / seeds,6:F2}");
            Console.WriteLine($"  call-ins, a match          ({onCards / seeds:F0} points a match on cards that leave a mark)");
            foreach (var kv in calls.OrderByDescending(k => k.Value))
                Console.WriteLine($"      {kv.Key,-14} {kv.Value / seeds,6:F2}" + (inside.ContainsKey(kv.Key) ? $"   the enemy inside it as it began {inside[kv.Key] / kv.Value:F1}; killed {killed.GetValueOrDefault(kv.Key) / kv.Value:F2}; of its own {own.GetValueOrDefault(kv.Key) / kv.Value:F2}" : ""));
            Console.WriteLine("  every card played, a match: " + string.Join(", ", played.OrderByDescending(k => k.Value).Select(k => $"{k.Key.ToLower()} {k.Value / seeds:F2}")) + $"; points on cards: the Americans {spent[0] / seeds:F0}, the VC {spent[1] / seeds:F0}");
            Console.WriteLine($"  traps sprung {sprung / seeds:F2} a match, killing {trapKills / seeds:F2}");
            Console.WriteLine($"  levers, position-seconds a match: on hold {hold / seeds:F0} (the Americans {holdBy[0] / seeds:F0}, the VC {holdBy[1] / seeds:F0}); on go {go / seeds:F0} ({goBy[0] / seeds:F0}, {goBy[1] / seeds:F0})");
            return 0;
        }

        /// <summary>
        /// Where and when a burst kills a man, as the game plays the seed: something to point a capture at.
        /// `blasts seed [flags]`; the game's own match is `fieldcraft arms senses gunnery ammo tactics fortune tempo`.
        /// </summary>
        private static int Blasts(string[] a)
        {
            int seed = a.Skip(1).Where(x => int.TryParse(x, out _)).Select(int.Parse).DefaultIfEmpty(3).First();
            var m = new LiveMatch(Game(seed, a));
            var st = m.State;
            while (!st.Over && st.Tick < m.Cap)
            {
                int from = st.Events.Count;
                m.Step();
                SimEvent? burst = null;
                for (int i = from; i < st.Events.Count; i++)
                {
                    var e = st.Events[i];
                    // `guns`: a machine gun's bursts and a sniper's rounds too, the first of each second: where to look at what they throw up.
                    if (a.Contains("guns") && e.Kind == EventKind.Fire && st.Tick % 20 == 0 && (st.Men[e.Id].Weapon == Weapon.M60 || st.Men[e.Id].Weapon == Weapon.Rpd || st.Men[e.Id].Weapon == Weapon.Sniper))
                        Console.WriteLine($"  t{st.Tick} {st.Men[e.Id].Weapon} man {e.Id} at {st.Men[e.Id].X:F1},{st.Men[e.Id].Z:F1} fires {e.Rounds} at man {e.Target} at {st.Men[e.Target.Value].X:F1},{st.Men[e.Target.Value].Z:F1}{(i + 1 < st.Events.Count && st.Events[i + 1].Kind == EventKind.Kill ? " and kills him" : "")}");
                    if (e.Kind == EventKind.GrenadeBlast || e.Kind == EventKind.Shell || e.Kind == EventKind.TrapSprung) { burst = e; continue; }
                    if (e.Kind == EventKind.Fire || e.Kind == EventKind.Melee || e.Kind == EventKind.Through || e.Kind == EventKind.Stray) { burst = null; continue; }
                    if (e.Kind != EventKind.Kill || burst == null || burst.Value.X == null) continue;
                    var man = st.Men[e.Id];
                    double d = Combat.Dist(man.X, man.Z, burst.Value.X.Value, burst.Value.Z.Value);
                    Console.WriteLine($"  t{st.Tick} {burst.Value.Kind} at {burst.Value.X:F1},{burst.Value.Z:F1} kills man {man.Id} ({man.Side}, {man.Weapon}, {man.Posture}) at {man.X:F1},{man.Z:F1}: {d:F1} m off");
                }
            }
            Console.WriteLine($"  seed {seed}: {st.Tick} ticks, {st.Reason}");
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
            bool frag = a.Contains("frag"), smoke = a.Contains("smoke"), drill = a.Contains("drill"), craft = a.Contains("fieldcraft"), onMap = a.Contains("map"), arms = a.Contains("arms"), senses = a.Contains("senses"), tempo = a.Contains("tempo"), gunnery = a.Contains("gunnery"), ammo = a.Contains("ammo"), tactics = a.Contains("tactics"), fortune = a.Contains("fortune"), firecontrol = a.Contains("firecontrol"), commander = a.Contains("commander"), ballistics = a.Contains("ballistics"), vantage = a.Contains("vantage"), aiposts = a.Contains("aiposts"), cohesion = a.Contains("cohesion"), bearing = a.Contains("bearing"); bool inbound = a.Contains("inbound");
            string lengthName = a.Contains("skirmish") ? "skirmish" : a.Contains("siege") || a.Contains("long") ? "siege" : "standard";
            a = a.Where(x => x != "frag" && x != "long" && x != "smoke" && x != "drill" && x != "fieldcraft" && x != "map" && x != "arms" && x != "senses" && x != "tempo" && x != "gunnery" && x != "ammo" && x != "tactics" && x != "fortune" && x != "firecontrol" && x != "commander" && x != "ballistics" && x != "vantage" && x != "aiposts" && x != "cohesion" && x != "bearing" && x != "skirmish" && x != "siege").ToArray();
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
                var mo = new MatchOptions { Seed = s, Us = us, Vc = vc, Cover = onMap ? Map.Cover() : null, Frag = frag, SquadSmoke = smoke, Drill = drill, Fieldcraft = craft, Arms = arms, Senses = senses, Gunnery = gunnery, Ammo = ammo, Tactics = tactics, Fortune = fortune, FireControl = firecontrol, Commander = commander, Ballistics = ballistics, Vantage = vantage, ComputerPosts = aiposts, Cohesion = cohesion, Inbound = inbound, Bearing = bearing };
                if (lengthName == "skirmish") mo.Length = MatchLength.Skirmish; else if (lengthName == "siege") mo.Length = MatchLength.Long;
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
            // The scenarios' own endings (a clock that runs out, a line that is taken) need the scenarios, as the game plays them.
            foreach (var sc in Scenarios.All)
            {
                if (!sc.Timed) continue;
                for (int seed = 1; seed <= 16; seed++)
                {
                    var r = Match.Run(sc.Options(seed, null, Difficulty.Veteran));
                    foreach (var k in r.EventCounts.Keys) seen.Add(k);
                    endings.Add(r.Reason);
                }
            }
            // And a squad on a post needs somebody to send one up: the Americans' sniper team, up the watchtower.
            {
                var m = new LiveMatch(Scenarios.Firebase.Options(1, Side.Us, Difficulty.Veteran));
                while (!m.State.Over && m.State.Tick < 1200)
                {
                    if (m.State.Tick == 40) m.Issue(Command.BuyTo(Side.Us, "us-sniper", 0));
                    m.Step();
                }
                foreach (var e in m.State.Events) seen.Add(e.Kind);
            }
            Console.WriteLine("  events that fired:");
            int missing = 0;
            foreach (EventKind k in Enum.GetValues(typeof(EventKind)))
            {
                bool y = seen.Contains(k);
                if (!y) missing++;
                Console.WriteLine($"    {(y ? "yes" : "NO ")}  {k}");
            }
            var expected = new[] { "us morale broke", "vc morale broke", "us wiped out", "vc wiped out", "time, on morale", "time, drawn",
                                   "us held out", "us took the line", "vc held the line" };
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
