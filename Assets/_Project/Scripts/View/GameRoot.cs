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
        }

        /// <summary>
        /// Start a match on the world that is already built: the ground, cover and
        /// dressing stay; only the simulation is new. With a command log it is a
        /// replay of a match already played, on the same seed.
        /// </summary>
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
