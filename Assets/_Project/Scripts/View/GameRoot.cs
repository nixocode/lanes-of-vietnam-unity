using System;
using System.Linq;
using System.Globalization;
using LanesOfVietnam.Sim;
using UnityEngine;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// The game: builds the world from the map, runs the match, hands each
    /// frame to the views.
    ///
    /// The world is built at load from <see cref="Map"/> rather than baked into
    /// the scene — the ground, the cover, and (in the art pass) where every
    /// plant stands. It is all deterministic maths, it costs nothing to
    /// download, and the capture harness renders it through exactly this code
    /// rather than a copy of it: the three.js build's capture once aimed the
    /// camera 2.4 degrees lower than the game and made every band measurement
    /// meaningless.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance { get; private set; }

        public CameraRig CameraRig;
        public GroundView GroundView;
        public CoverView CoverView;
        public ArmyView ArmyView;
        public WorldDressing Dressing;
        public MountainView Mountains;

        /// <summary>The map's own seed: the dressing is part of the map and never changes with the match.</summary>
        public const int MapSeed = 20260929;

        public Side PlayerSide = Side.Us;

        public MatchDriver Driver { get; private set; }
        public MatchOptions Options { get; private set; }
        public GameSettings Settings { get; private set; }

        /// <summary>Raised whenever a new match (or a replay) replaces the old one.</summary>
        public event System.Action MatchStarted;
        public Ground Ground { get; private set; }
        public int Seed { get; private set; }

        /// <summary>Simulation speed: 1x, 2x, 3x. The sim still ticks at 20 Hz of match time.</summary>
        public float Speed = 1f;
        public bool Paused;

        /// <summary>The view's clock: wind, flicker, anything animated. Frozen in a capture.</summary>
        public float ViewTime { get; private set; }

        private static readonly int LovTime = Shader.PropertyToID("_LovTime");

        private void Awake()
        {
            Instance = this;
            var cap = CaptureSettings.Active;
            Application.targetFrameRate = -1;

            Ground = Map.Ground();
            GroundView.Build(Ground);
            var cover = Map.Cover();
            CoverView.Build(cover, Ground);
            if (Mountains != null) Mountains.Build();
            if (Dressing != null)
            {
                Dressing.Bags = CoverView.Bags;
                Dressing.Structures = CoverView.Structures;
                Dressing.Build(Ground, cover, MapSeed);
            }

            Settings = GameSettings.Load();
            Settings.ApplyQuality();

            // A capture or the frame-time probe may name the scenario (scenario=the-wire); else the firebase.
            NewMatch(PlayerSide, MatchLength.Standard, cap?.Seed, scenario: Scenarios.ById(cap?.Scenario ?? PerfProbe.Param("scenario") ?? "firebase"));

            if (cap != null)
            {
                // Call-ins the capture asked for go in at their ticks, as the
                // player's would; the rest of the way is a plain fast-forward.
                int at = 0;
                foreach (var call in cap.Calls.OrderBy(c => c.tick))
                {
                    if (call.tick > at) { Driver.FastForward(call.tick - at); at = call.tick; }
                    var side = call.card.StartsWith("us-") ? Side.Us : Side.Vc;
                    var card = Deck.For(side).FirstOrDefault(c => c.Id == call.card);
                    // (A capture's call is to be looked at, not earned: it is paid for.)
                    if (card != null && Driver.State.Cp[(int)side] < card.Cost) Driver.State.Cp[(int)side] = card.Cost;
                    string blocked = card == null ? "no such card" : Deck.Blocked(Driver.State, side, card);
                    Debug.Log($"[LOV] capture call-in {call.card} lane {call.lane} x {call.x} at tick {call.tick}: {blocked ?? "issued"}");
                    double callX = call.x;
                    int callLane = call.lane;
                    if (double.IsNaN(callX) && card != null)
                    {
                        // Wait for someone with a target, as a player would.
                        for (int w = 0; w < 4000 && double.IsNaN(callX); w++)
                        {
                            foreach (var man in Driver.State.Men)
                            {
                                if (!man.Alive || man.Side != side || man.Grenades <= 0 || man.High > 0) continue;
                                var target = Frag.PickTarget(Driver.State, man);
                                if (target == null) continue;
                                callX = target.X; callLane = Math.Abs(target.Z - Tune.Lanes[0]) < Math.Abs(target.Z - Tune.Lanes[1]) ? 0 : 1;
                                break;
                            }
                            if (double.IsNaN(callX)) { Driver.FastForward(1); at++; }
                        }
                        Debug.Log($"[LOV] capture: grenades played at {callX:F1} in lane {callLane}, tick {Driver.State.Tick}");
                        if (!double.IsNaN(callX)) cap.CameraX = (float)callX;
                        if (Driver.State.Cp[(int)side] < card.Cost) Driver.State.Cp[(int)side] = card.Cost;
                        blocked = Deck.Blocked(Driver.State, side, card) ?? (Frag.Throwers(Driver.State, side, callLane, callX).Count == 0 ? "nobody can reach it" : null);
                        Debug.Log($"[LOV] capture: grenades {blocked ?? "thrown"}");
                    }
                    // (A lane past the last is a post: call=us-sniper:2:0:5 sends a sniper team up the watchtower.)
                    if (blocked == null) Driver.Match.Issue(callLane >= Tune.Lanes.Length ? Command.BuyTo(side, call.card, callLane - Tune.Lanes.Length) : Command.Buy(side, call.card, callLane, callX));
                }
                if (cap.Tick > at) Driver.FastForward(cap.Tick - at);
                ViewTime = cap.ViewTime;
                CameraRig.Focus(cap.CameraX, instant: true);
                CameraRig.SetDolly(cap.Dolly, instant: true);
                if (cap.Arm != null)
                {
                    var held = Deck.For(cap.Arm.StartsWith("us-") ? Side.Us : Side.Vc).FirstOrDefault(c => c.Id == cap.Arm);
                    var dep = FindAnyObjectByType<Deployer>();
                    if (held != null && dep != null) dep.Hold(held, cap.ArmLane, cap.ArmX);
                }
                gameObject.AddComponent<CaptureRunner>();
            }
            else if (PerfProbe.Param("perf") != null) gameObject.AddComponent<PerfProbe>();
        }

        /// <summary>
        /// Start a match on the world that is already built: the ground, cover and
        /// dressing stay; only the simulation is new. With a command log it is a
        /// replay of a match already played, on the same seed.
        /// </summary>
        /// <summary>Command points a second and at the start, and the men each side opens with (at least), in the game.</summary>
        public const double CpRate = Scenario.CpRate, StartCp = Scenario.StartCp;
        /// <summary>One squad a side (the first raised is five men). It was a squad a lane; the owner, 2026-10-02: "still too many soldiers at the start from both sides".</summary>
        public const int OpeningStrength = Scenario.OpeningStrength;

        /// <summary>
        /// How hard the computer is: what a squad costs it (40, 29, 22). Measured with
        /// `tools/simcs/run.sh player 24 N rate=0.8`, against a player who only ever buys line squads
        /// when he can afford one and never uses a call-in or a lever: against a Recruit he wins 23 of
        /// 24 as the Americans and 22 as the VC; against a Veteran 18 and 19; against the Elite 11 and 12
        /// (with ammunition and the pace of playtest 8, PLAN §12.24).
        /// (They were 30, 25 and 21 until the men were slowed, PLAN §12.22: at those he won 15, 14 and 5
        /// as the Americans, whose squads now take longer to come up from their end of the lane.)
        /// With Tactics and Fortune (PLAN §12.26) the same costs hold, measured over 48 seeds with
        /// `player 48 N rate=0.9 tactics fortune`: 46 and 48 of 48 against a Recruit, 40 and 37 against a
        /// Veteran, 21 and 24 against the Elite.
        /// With FireControl (PLAN §12.29) numbers tell for more, and the Elite at 22 was beaten 6 and 11 times in 48: it
        /// is 25. Measured with `player 48 N rate=0.9 tactics fortune firecontrol`: 44 and 46 of 48 against a Recruit, 36
        /// and 33 against a Veteran, 26 and 21 against the Elite.
        /// With the computer's commander (PLAN §12.36) the computer plays its cards and its levers, and a squad is
        /// worth more to it: at 29 the same player won 29 and 22 of 48. Measured over 96 seeds with `player 96 N
        /// rate=0.9 tactics fortune firecontrol ballistics vantage commander`: 78 and 83 of 96 against a Recruit
        /// (40), 62 and 71 against a Veteran (32), 54 and 44 against the Elite (26).
        /// With Cohesion (PLAN §12.37) the computer's squads fight as squads, and at 32 he won 53 and 48 of 96. Measured
        /// with the same and `cohesion`: 86 and 80 of 96 against a Recruit (42), 62 and 56 against a Veteran (34), 55
        /// and 39 against the Elite (28).
        /// With Bearing (PLAN §12.41) men stay down longer and a fight is slower to decide, and at 34 he won 110 and 109
        /// of 192. Measured over 192 seeds with `player 192 N rate=0.95 ... commander cohesion bearing`: 125 and 111
        /// against a Veteran (35), 96 and 76 against the Elite (29); and a Recruit is 44 (at 43, 151 and 139).
        /// </summary>
        /// <remarks>The numbers themselves are kept in Sim/Scenario.cs, where the headless tool reads the ones the game plays at.</remarks>
        public static double CostFor(Difficulty d) => Scenario.CostFor(d);

        /// <summary>
        /// How fast will runs out, against the length setting's own rate. The settings were calibrated
        /// when a standard match ran 204 s; with the game's tempo and the rules since, the three ran 91,
        /// 127 and 306 s against the start screen's 2, 3½ and 8 minutes. At these rates (`simcs player
        /// 24 29 [skirmish|siege] rate=N`: a player who buys, and the two plans left to themselves) a
        /// skirmish runs 95 to 130 s, a standard match 150 to 230, a siege five and a half to nine
        /// minutes. (0.68, 0.55 and 0.6 until the men were slowed, PLAN §12.22; then 0.75, 0.6 and 0.71;
        /// with ammunition and the slower pace of §12.24 a fight takes longer again, and at those the
        /// three ran to 176, 315 and 641 s. Then 1.0, 0.8 and 0.9 until Tactics, PLAN §12.26: a squad that
        /// closes by bounds, with fewer grenades, kills a quarter fewer men a minute, and at those a
        /// standard match with nobody playing ran 256 s. At these a skirmish runs 109 to 142 s, a standard
        /// match 167 to 242, a siege six to ten minutes. With FireControl, PLAN §12.29, the same rates hold:
        /// a skirmish 125 to 144 s, a standard match 200 to 241, a siege seven and a half to eight and a half
        /// minutes, one in six of them to the twelve-minute cap. With the commander, PLAN §12.36, a siege at 1.0 ran
        /// seven and three quarters to ten minutes, more of them to the cap; at 1.15 (`player 48 32 siege rate=1.15
        /// ... commander`) five and a half to nine. The other two hold: a skirmish 119 to 147 s, a standard match
        /// 187 to 226. With Cohesion, PLAN §12.37, squads stay longer where they have gone to ground: a siege at 1.15
        /// ran seven to nine and three quarter minutes again, and at 1.35 (`player 48 34 siege rate=1.35 ... commander
        /// cohesion`) five to eight and a half; a skirmish 127 to 147 s and a standard match 197 to 228 at the rates
        /// they had. With Bearing, PLAN §12.41, men who stay down are hit less and a match ran a tenth longer: the
        /// rates are 1.25, 0.95 and 1.5 (`player 96 35 [skirmish|long] rate=N ... cohesion bearing`), at which a
        /// skirmish runs 126 to 159 s, a standard match 201 to 296 with nobody buying and 201 to 242 with a player
        /// who does, a long one four and three quarters to seven and a half minutes.)
        /// </summary>
        public static double RateFor(MatchLength l) => Scenario.RateFor(l);

        public Difficulty Level { get; private set; } = Difficulty.Veteran;

        /// <summary>The scenario being played (PLAN §12.27, Phase S). The firebase, until another is chosen.</summary>
        public Scenario Scenario { get; private set; } = Scenarios.Firebase;

        public void NewMatch(Side player, MatchLength length, int? seed = null,
                             System.Collections.Generic.IReadOnlyList<LiveMatch.Applied> replay = null,
                             Difficulty level = Difficulty.Veteran, Scenario scenario = null)
        {
            var cap = CaptureSettings.Active;
            Scenario = scenario ?? Scenarios.Firebase;
            // A scenario that is one side's to play is played from that side.
            if (!Scenario.Offers(player)) player = Scenario.Plays.Value;
            PlayerSide = player;
            Level = level;
            Seed = seed ?? ChooseSeed();
            // The match is the scenario's (Sim/Scenario.cs): its map, its mode and clock, what each side has, every
            // Part 2 rule on (grenades and squad smoke, Drill, Fieldcraft, Arms, Senses, Gunnery, Ammo, Tactics,
            // Fortune, FireControl, Ballistics, Vantage, the computer's commander, Cohesion, Bearing) and the game's tempo
            // (the owner, playtest 4: "points are gained too slow. Too many soldiers at the start"): each side
            // opens with one squad, points come in at CpRate with StartCp in hand, and the player's own side
            // raises nothing by itself: it had been spending his points for him, a squad every time he reached
            // 22. A capture and the frame-time probe have no player, so there both sides raise their own.
            // (The computer does not man the watchtower itself: measured, a Vietnamese player's game goes from 17
            // wins in 24 to 5. It does raise a marksman against a tower the player has manned.)
            bool nobody = cap != null || PerfProbe.Param("perf") != null;
            Options = Scenario.Options(Seed, nobody ? (Side?)null : player, level, length);
            // The simulation's default stays the parity baseline; a capture can turn any rule off (frag=0), and
            // give a side a plan by name.
            if (cap != null)
            {
                if (cap.UsPlan != null) Options.Us = Plan.ByName(cap.UsPlan);
                if (cap.VcPlan != null) Options.Vc = Plan.ByName(cap.VcPlan);
                Options.Frag = cap.Frag ?? true;
                Options.SquadSmoke = cap.SquadSmoke ?? true;
                Options.Drill = cap.Drill ?? true;
                Options.Fieldcraft = cap.Fieldcraft ?? true;
                Options.Arms = cap.Arms ?? true;
                Options.Senses = cap.Senses ?? true;
                Options.Gunnery = cap.Gunnery ?? true;
                Options.Ammo = cap.Ammo ?? true;
                Options.Tactics = cap.Tactics ?? true;
                Options.Fortune = cap.Fortune ?? true;
                Options.FireControl = cap.FireControl ?? true;
                Options.Ballistics = cap.Ballistics ?? true;
                Options.Vantage = cap.Vantage ?? true;
                Options.Commander = cap.Commander ?? true;
                Options.Cohesion = cap.Cohesion ?? true;
                Options.Bearing = cap.Bearing ?? true;
                Options.Inbound = cap.Inbound ?? true;
                Options.FragCard = cap.FragCard ?? true;
            }
            Driver = new MatchDriver(Options, replay);
            ArmyView.ResetView();
            Debug.Log($"[LOV] match seed {Seed}, {Scenario.Id}, {player} ({Options.Length}){(replay != null ? ", replay" : "")}");
            MatchStarted?.Invoke();
        }

        /// <summary>
        /// The seed is chosen by the view, never by the simulation, and it is
        /// logged: a match anyone saw can be replayed. <c>?seed=N</c> in the
        /// page address picks one.
        /// </summary>
        private static int ChooseSeed()
        {
            string url = Application.absoluteURL ?? "";
            int q = url.IndexOf("seed=", StringComparison.Ordinal);
            if (q >= 0)
            {
                int end = q + 5;
                while (end < url.Length && char.IsDigit(url[end])) end++;
                if (int.TryParse(url.Substring(q + 5, end - q - 5), NumberStyles.Integer, CultureInfo.InvariantCulture, out int s)) return s;
            }
            return Environment.TickCount & 0x7fffffff;
        }

        /// <summary>Capture only: move the match and the view's clock on by some seconds, as a played frame would.</summary>
        public void AdvanceForCapture(float seconds)
        {
            Driver.Advance(seconds);
            ViewTime += seconds;
        }

        /// <summary>Milliseconds the simulation took in the last frame (none, on a frame with no tick in it). For the frame-time probe.</summary>
        public float LastSimMs { get; private set; }
        private readonly System.Diagnostics.Stopwatch _simClock = new System.Diagnostics.Stopwatch();

        private void Update()
        {
            if (CaptureSettings.Active == null)
            {
                _simClock.Restart();
                if (!Paused) Driver.Advance(Time.deltaTime * Speed);
                LastSimMs = (float)_simClock.Elapsed.TotalMilliseconds;
                ViewTime += Time.deltaTime;
            }
            Shader.SetGlobalFloat(LovTime, ViewTime);
            ArmyView.Draw(Driver, Ground);
        }
    }
}
