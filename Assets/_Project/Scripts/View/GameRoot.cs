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
                // The grey box's sphere ridges stand in only when there is no real terrain.
                Dressing.GreyBoxRidges = Mountains == null || Mountains.Vertices == 0;
                Dressing.Bags = CoverView.Bags;
                Dressing.Build(Ground, cover, MapSeed);
            }

            Settings = GameSettings.Load();
            Settings.ApplyQuality();

            NewMatch(PlayerSide, MatchLength.Standard, cap?.Seed);

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
                    string blocked = card == null ? "no such card" : Deck.Blocked(Driver.State, side, card);
                    Debug.Log($"[LOV] capture call-in {call.card} lane {call.lane} x {call.x} at tick {call.tick}: {blocked ?? "issued"}");
                    if (blocked == null) Driver.Match.Issue(Command.Buy(side, call.card, call.lane, call.x));
                }
                if (cap.Tick > at) Driver.FastForward(cap.Tick - at);
                ViewTime = cap.ViewTime;
                CameraRig.Focus(cap.CameraX, instant: true);
                CameraRig.SetDolly(cap.Dolly, instant: true);
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
        public const double CpRate = 1.6, StartCp = 20;
        /// <summary>
        /// What a squad costs the computer's side (the baseline's 22 is at the baseline's income). Chosen with
        /// `tools/simcs/run.sh player 24 N`, against a player who only ever buys line squads when he can: at 22
        /// that player wins half his matches, at 28 three in four, at 40 all of them. One who buys nothing loses
        /// in about a minute at any of them. With Senses on, 28 gave that player nine wins in twenty-four as
        /// the Americans (the computer's mixed squads see and outrange a line of riflemen); at 32 it is
        /// nineteen, and twenty-two as the VC. With Gunnery on (the lanes two separate fights, nobody firing
        /// on the move) it is 28 again: sixteen of twenty-four as the Americans, twenty-two as the VC; at 32
        /// he hardly loses.
        /// </summary>
        public const double MusterCost = 28;
        /// <summary>One squad a side (the first raised is five men). It was a squad a lane; the owner, 2026-10-02: "still too many soldiers at the start from both sides".</summary>
        public const int OpeningStrength = 4;

        public void NewMatch(Side player, MatchLength length, int? seed = null,
                             System.Collections.Generic.IReadOnlyList<LiveMatch.Applied> replay = null)
        {
            PlayerSide = player;
            Seed = seed ?? ChooseSeed();
            Options = new MatchOptions
            {
                Seed = Seed,
                Us = CaptureSettings.Active?.UsPlan != null ? Plan.ByName(CaptureSettings.Active.UsPlan) : Plan.Ceiling,
                Vc = CaptureSettings.Active?.VcPlan != null ? Plan.ByName(CaptureSettings.Active.VcPlan) : Plan.Ceiling,
                Cover = Map.Cover(), Length = length,
                // Grenades and squad smoke (PLAN §12.8, Part 2): on in the game. The sim's default
                // stays the parity baseline; a capture can turn it off (frag=0).
                Frag = CaptureSettings.Active?.Frag ?? true,
                SquadSmoke = CaptureSettings.Active?.SquadSmoke ?? true,
                // Drill (Part 2): orders that stand and men who keep their places. On in the game.
                Drill = CaptureSettings.Active?.Drill ?? true,
                // Fieldcraft (Part 2): squads that stop short of the enemy, places in cover, melee, levers. On in the game.
                Fieldcraft = CaptureSettings.Active?.Fieldcraft ?? true,
                // Arms (Part 2): every man his weapon, every card its squad, every fight at its weapons' distance. On in the game.
                Arms = CaptureSettings.Active?.Arms ?? true,
                // Senses (Part 2): squads that spot each other, fire only at what they have spotted, go to
                // ground at contact and hold one task at a time. On in the game.
                Senses = CaptureSettings.Active?.Senses ?? true,
                // Gunnery (Part 2): fire down the lane only, from a knee or flat and never on the move; a
                // man gets up to move and runs; every round lands somewhere. On in the game.
                Gunnery = CaptureSettings.Active?.Gunnery ?? true,
                // The game's tempo (the owner, playtest 4: "points are gained too slow. Too many soldiers at
                // the start"). Each side opens with one squad, not eighteen men; points come in at
                // CpRate with StartCp in hand; and the player's own side raises nothing by itself: it had
                // been spending his points for him, a squad every time he reached 22. A capture and the
                // frame-time probe have no player, so there both sides raise their own.
                CpRate = CpRate, StartCp = StartCp, OpeningStrength = OpeningStrength, MusterCost = MusterCost,
                Player = CaptureSettings.Active != null || PerfProbe.Param("perf") != null ? (Side?)null : player,
            };
            Driver = new MatchDriver(Options, replay);
            ArmyView.ResetView();
            Debug.Log($"[LOV] match seed {Seed}, {player} ({length}){(replay != null ? ", replay" : "")}");
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

        private void Update()
        {
            if (CaptureSettings.Active == null)
            {
                if (!Paused) Driver.Advance(Time.deltaTime * Speed);
                ViewTime += Time.deltaTime;
            }
            Shader.SetGlobalFloat(LovTime, ViewTime);
            ArmyView.Draw(Driver, Ground);
        }
    }
}
