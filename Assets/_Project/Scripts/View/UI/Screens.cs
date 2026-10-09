using System;
using System.Collections.Generic;
using System.Linq;
using LanesOfVietnam.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace LanesOfVietnam.View.UI
{
    public enum FlowState { Start, Opening, Playing, Paused, Over }

    /// <summary>
    /// Match flow (PLAN §12.5): the start screen over the live scene, the
    /// opening, pause and settings, the two endings, and the replay.
    ///
    /// Every screen action is a public method — <see cref="Deploy"/>,
    /// <see cref="Skip"/>, <see cref="OpenSettings"/>, <see cref="Resume"/>,
    /// <see cref="WatchReplay"/>, <see cref="ToMenu"/> — and the buttons only
    /// call them, so UIAudit drives exactly what a click drives.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(-50)]
    public sealed class Screens : MonoBehaviour
    {
        public GameRoot Root;
        public Hud Hud;
        public Commander Commander;
        public Deployer Deployer;

        public FlowState State { get; private set; } = FlowState.Start;

        // What the start screen has chosen.
        public Side ChosenSide { get; private set; } = Side.Us;
        public MatchLength ChosenLength { get; private set; } = MatchLength.Standard;
        public GameRoot.Difficulty ChosenLevel { get; private set; } = GameRoot.Difficulty.Veteran;

        public VisualElement StartPanel { get; private set; }
        public VisualElement SettingsPanel { get; private set; }
        public VisualElement EndPanel { get; private set; }
        public Label Subtitle { get; private set; }
        public Label EndTitle { get; private set; }
        public Label EndReason { get; private set; }
        public Label EndVerdict { get; private set; }
        public Label Report { get; private set; }
        public Label ReplayBanner { get; private set; }
        public readonly Dictionary<string, Button> Buttons = new Dictionary<string, Button>();

        /// <summary>The ending's outcome text, for UIAudit: "THE LINE HELD", "THE WIRE IS BREACHED", ...</summary>
        public string Outcome { get; private set; }

        private float _openingT, _overT, _panT;
        private IReadOnlyList<LiveMatch.Applied> _lastLog;
        private MatchLength _lastLength;
        private GameRoot.Difficulty _lastLevel;
        private Side _lastSide;
        private int _lastSeed;

        /// <summary>The opening: seconds, and where the camera travels (sim x).</summary>
        public const float OpeningSeconds = 7f;
        private const float OpeningFromX = 38f, OpeningToX = -14f;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            BuildStart(root);
            BuildSettings(root);
            BuildEnd(root);
            Subtitle = new Label("") { name = "subtitle", pickingMode = PickingMode.Ignore };
            Subtitle.AddToClassList("subtitle");
            root.Add(Subtitle);
            ReplayBanner = new Label("REPLAY") { name = "replay-banner", pickingMode = PickingMode.Ignore };
            ReplayBanner.AddToClassList("replay-banner");
            root.Add(ReplayBanner);
            Hud.SettingsRequested += OpenSettings;
            if (CaptureSettings.Active != null) Enter(FlowState.Playing);
            else Enter(FlowState.Start);
        }

        // --- building ---------------------------------------------------------

        private Button Btn(VisualElement parent, string id, string text, Action onClick, string cls = "screen-button")
        {
            var b = new Button(onClick) { name = $"btn-{id}", text = text };
            b.AddToClassList(cls);
            parent.Add(b);
            Buttons[id] = b;
            return b;
        }

        private static Label Text(VisualElement parent, string text, string cls)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            parent.Add(l);
            return l;
        }

        /// <summary>The music's credit, in the form its author gives (incompetech.com, "Licenses": CC BY 4.0).</summary>
        public const string MusicCredit =
            "MUSIC   \"Drums of the Deep\" and \"Crypto\", Kevin MacLeod (incompetech.com)\n" +
            "Licensed under Creative Commons: By Attribution 4.0 · https://creativecommons.org/licenses/by/4.0/";

        private void BuildStart(VisualElement root)
        {
            StartPanel = new VisualElement { name = "start" };
            StartPanel.AddToClassList("screen");
            StartPanel.AddToClassList("start");
            Text(StartPanel, "LANES OF VIETNAM '65", "title");
            Text(StartPanel, "A FIREBASE IN THE CENTRAL HIGHLANDS · 1965", "kicker");

            Text(StartPanel, "YOUR SIDE", "field");
            var sides = new VisualElement(); sides.AddToClassList("choice-row"); StartPanel.Add(sides);
            Btn(sides, "side-us", "US / ARVN\nHOLD THE FIREBASE", () => ChooseSide(Side.Us), "choice");
            Btn(sides, "side-vc", "VC / NVA\nTAKE IT", () => ChooseSide(Side.Vc), "choice");

            Text(StartPanel, "LENGTH", "field");
            var lens = new VisualElement(); lens.AddToClassList("choice-row"); StartPanel.Add(lens);
            Btn(lens, "len-skirmish", "SKIRMISH\n~2 MIN", () => ChooseLength(MatchLength.Skirmish), "choice");
            Btn(lens, "len-standard", "STANDARD\n~3½ MIN", () => ChooseLength(MatchLength.Standard), "choice");
            Btn(lens, "len-siege", "SIEGE\n~8 MIN", () => ChooseLength(MatchLength.Siege), "choice");

            // The 2D game's three: how fast the other side raises its squads.
            Text(StartPanel, "THE ENEMY", "field");
            var levels = new VisualElement(); levels.AddToClassList("choice-row"); StartPanel.Add(levels);
            Btn(levels, "level-recruit", "RECRUIT\nSLOW TO REINFORCE", () => ChooseLevel(GameRoot.Difficulty.Recruit), "choice");
            Btn(levels, "level-veteran", "VETERAN\nA FAIR FIGHT", () => ChooseLevel(GameRoot.Difficulty.Veteran), "choice");
            Btn(levels, "level-elite", "ELITE\nOUTNUMBERS YOU", () => ChooseLevel(GameRoot.Difficulty.Elite), "choice");

            Btn(StartPanel, "deploy", "DEPLOY", () => Deploy(), "deploy");
            Text(StartPanel, "Sound starts when you deploy.", "fineprint");
            root.Add(StartPanel);
            SyncChoices();
        }

        private void BuildSettings(VisualElement root)
        {
            SettingsPanel = new VisualElement { name = "settings" };
            SettingsPanel.AddToClassList("screen");
            SettingsPanel.AddToClassList("settings");
            Text(SettingsPanel, "PAUSED", "title");

            var s = Root.Settings ?? GameSettings.Load();
            Text(SettingsPanel, "QUALITY", "field");
            var q = new VisualElement(); q.AddToClassList("choice-row"); SettingsPanel.Add(q);
            foreach (QualityTier t in Enum.GetValues(typeof(QualityTier)))
            {
                var tier = t;
                Btn(q, $"q-{t.ToString().ToLowerInvariant()}", t.ToString().ToUpperInvariant(), () => SetQuality(tier), "choice");
            }

            foreach (var (id, label) in new[] { ("master", "MASTER"), ("effects", "EFFECTS"), ("music", "MUSIC"), ("ambience", "AMBIENCE"), ("voice", "VOICE") })
            {
                var row = new VisualElement(); row.AddToClassList("slider-row");
                Text(row, label, "slider-label");
                var slider = new Slider(0f, 1f) { name = $"vol-{id}" };
                slider.AddToClassList("slider");
                slider.value = id switch { "master" => s.Master, "effects" => s.Effects, "music" => s.Music, "ambience" => s.Ambience, _ => s.Voice };
                slider.RegisterValueChangedCallback(e => SetVolume(id, e.newValue));
                row.Add(slider);
                SettingsPanel.Add(row);
            }

            var toggles = new VisualElement(); toggles.AddToClassList("choice-row"); SettingsPanel.Add(toggles);
            Btn(toggles, "subtitles", "SUBTITLES", () => { Root.Settings.Subtitles = !Root.Settings.Subtitles; Save(); }, "choice");
            Btn(toggles, "shake", "CAMERA SHAKE", () => { Root.Settings.CameraShake = !Root.Settings.CameraShake; Save(); }, "choice");
            Btn(toggles, "gore", "GORE", () => { Root.Settings.Gore = !Root.Settings.Gore; Save(); }, "choice");
            Btn(toggles, "hidehud", "HIDE HUD", () => { Root.Settings.HideHud = !Root.Settings.HideHud; Save(); }, "choice");

            Text(SettingsPanel,
                "KEYS   A/D or drag: pan · wheel: dolly · F: field glasses · click: select · Tab: next squad\n" +
                "1 advance · 2 hold · 3 fall back · 0 auto · Q W E R: call-ins · Z X C V B: units\n" +
                "P: pause · Esc: cancel / deselect / this menu",
                "keys");
            // The credit its licence asks for, where its author asks for it: with the settings.
            Text(SettingsPanel, MusicCredit, "keys");

            var row2 = new VisualElement(); row2.AddToClassList("choice-row"); SettingsPanel.Add(row2);
            Btn(row2, "resume", "RESUME", Resume, "deploy");
            Btn(row2, "menu", "QUIT TO MENU", ToMenu, "choice");
            root.Add(SettingsPanel);
        }

        private void BuildEnd(VisualElement root)
        {
            EndPanel = new VisualElement { name = "end" };
            EndPanel.AddToClassList("screen");
            EndPanel.AddToClassList("end");
            EndTitle = Text(EndPanel, "", "end-title");
            EndReason = Text(EndPanel, "", "kicker");
            EndVerdict = Text(EndPanel, "", "verdict");
            Report = Text(EndPanel, "", "report");
            var row = new VisualElement(); row.AddToClassList("choice-row"); EndPanel.Add(row);
            Btn(row, "replay", "WATCH THE REPLAY", WatchReplay, "choice");
            Btn(row, "again", "PLAY AGAIN", ToMenu, "deploy");
            root.Add(EndPanel);
        }

        /// <summary>
        /// Show a screen over the frozen capture without starting a new match —
        /// a capture is one moment of one match, and a new match would move it.
        /// </summary>
        public void ShowForCapture(string screen)
        {
            switch (screen)
            {
                case "start": Enter(FlowState.Start); break;
                case "settings": Enter(FlowState.Playing); Enter(FlowState.Paused); break;
                case "end":
                    if (Root.Driver.State.Over) ShowEnding(Root.Driver.State);
                    break;
            }
        }

        // --- actions ------------------------------------------------------------

        public void ChooseSide(Side s) { ChosenSide = s; SyncChoices(); }
        public void ChooseLength(MatchLength l) { ChosenLength = l; SyncChoices(); }
        public void ChooseLevel(GameRoot.Difficulty d) { ChosenLevel = d; SyncChoices(); }

        /// <summary>Start the chosen match and play the opening. Deploy is also the gesture that lets a browser start audio.</summary>
        public void Deploy()
        {
            Root.NewMatch(ChosenSide, ChosenLength, level: ChosenLevel);
            Enter(FlowState.Opening);
        }

        /// <summary>Cut the opening short: the camera jumps to where it would have settled.</summary>
        public void Skip()
        {
            if (State != FlowState.Opening) return;
            Root.CameraRig.Focus(OpeningToX, instant: true);
            Enter(FlowState.Playing);
        }

        public void OpenSettings()
        {
            if (State == FlowState.Playing || State == FlowState.Opening) Enter(FlowState.Paused);
        }

        public void Resume()
        {
            if (State == FlowState.Paused) Enter(FlowState.Playing);
        }

        public void ToMenu()
        {
            Root.NewMatch(ChosenSide, ChosenLength, level: ChosenLevel);
            Enter(FlowState.Start);
        }

        /// <summary>Play the match just finished again, from its own command log.</summary>
        public void WatchReplay()
        {
            if (_lastLog == null) return;
            Root.NewMatch(_lastSide, _lastLength, _lastSeed, _lastLog, _lastLevel);
            Enter(FlowState.Playing);
        }

        public void SetQuality(QualityTier t)
        {
            Root.Settings.Quality = t;
            Root.Settings.ApplyQuality();
            Save();
        }

        public void SetVolume(string id, float v)
        {
            var s = Root.Settings;
            switch (id)
            {
                case "master": s.Master = v; break;
                case "effects": s.Effects = v; break;
                case "music": s.Music = v; break;
                case "ambience": s.Ambience = v; break;
                default: s.Voice = v; break;
            }
            Save();
        }

        private void Save()
        {
            Root.Settings.Save();
            SyncChoices();
        }

        // --- the state machine ---------------------------------------------------

        private void Enter(FlowState s)
        {
            State = s;
            StartPanel.style.display = s == FlowState.Start ? DisplayStyle.Flex : DisplayStyle.None;
            SettingsPanel.style.display = s == FlowState.Paused ? DisplayStyle.Flex : DisplayStyle.None;
            EndPanel.style.display = s == FlowState.Over ? DisplayStyle.Flex : DisplayStyle.None;
            bool play = s == FlowState.Playing || s == FlowState.Opening;
            Root.Paused = !play;
            bool replay = Root.Driver != null && Root.Driver.IsReplay;
            // A player does not command a replay, and nothing but the start
            // screen stands over the scene before the match.
            Commander.enabled = play && !replay;
            Deployer.enabled = play && !replay;
            if (!play) Deployer.Disarm();
            Hud.SetVisible(s != FlowState.Start && !Root.Settings.HideHud);
            ReplayBanner.style.display = replay && play ? DisplayStyle.Flex : DisplayStyle.None;
            _openingT = 0;
            _overT = 0;
            if (s == FlowState.Opening) Root.CameraRig.Focus(OpeningFromX, instant: true);
            SyncChoices();
        }

        private void SyncChoices()
        {
            if (Buttons.Count == 0) return;
            void On(string id, bool on) { if (Buttons.TryGetValue(id, out var b)) b.EnableInClassList("on", on); }
            On("side-us", ChosenSide == Side.Us);
            On("side-vc", ChosenSide == Side.Vc);
            On("len-skirmish", ChosenLength == MatchLength.Skirmish);
            On("len-standard", ChosenLength == MatchLength.Standard);
            On("len-siege", ChosenLength == MatchLength.Siege);
            On("level-recruit", ChosenLevel == GameRoot.Difficulty.Recruit);
            On("level-veteran", ChosenLevel == GameRoot.Difficulty.Veteran);
            On("level-elite", ChosenLevel == GameRoot.Difficulty.Elite);
            var st = Root.Settings;
            if (st == null) return;
            On("q-low", st.Quality == QualityTier.Low);
            On("q-medium", st.Quality == QualityTier.Medium);
            On("q-high", st.Quality == QualityTier.High);
            On("subtitles", st.Subtitles);
            On("shake", st.CameraShake);
            On("gore", st.Gore);
            On("hidehud", st.HideHud);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var st = Root.Driver?.State;
            switch (State)
            {
                case FlowState.Start:
                    // The live scene behind the menu, slowly panning down the line.
                    _panT += dt;
                    Root.CameraRig.Focus(Mathf.Sin(_panT * 0.035f) * 34f);
                    if (Input.GetKeyDown(KeyCode.Return)) Deploy();
                    break;

                case FlowState.Opening:
                    // The valley, then the firebase: the camera travels, the radio
                    // calls it, the first order comes up. Camera and words only —
                    // the simulation's own opening, the infiltration, is running.
                    _openingT += dt;
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_openingT / OpeningSeconds));
                    Root.CameraRig.Focus(Mathf.Lerp(OpeningFromX, OpeningToX, k), instant: true);
                    Subtitle.text = Root.Settings.Subtitles && _openingT > 1.2f && _openingT < 6.5f
                        ? (Root.PlayerSide == Side.Us
                            ? "RADIO — ALL STATIONS, THIS IS BRAVO SIX. MOVEMENT IN THE TREELINE. STAND TO."
                            : "RADIO — THE AMERICANS ARE ASLEEP IN THEIR HOLES. MOVE UP THROUGH THE GRASS. NO ONE FIRES.")
                        : "";
                    if (_openingT >= OpeningSeconds || Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0)) Skip();
                    break;

                case FlowState.Playing:
                    Subtitle.text = "";
                    if (Input.GetKeyDown(KeyCode.P)) Hud.TogglePause();
                    if (Input.GetKeyDown(KeyCode.Escape) && Commander.Selected < 0 && Deployer.Armed == null
                        && Deployer.DisarmedFrame != Time.frameCount) OpenSettings();
                    if (st != null && st.Over)
                    {
                        _overT += dt;
                        if (_overT > 1.5f) ShowEnding(st);
                    }
                    break;

                case FlowState.Paused:
                    if (Input.GetKeyDown(KeyCode.Escape)) Resume();
                    break;
            }
        }

        /// <summary>
        /// Two endings, not one card with the winner swapped in: the two sides
        /// were playing for different things. The US holds the firebase or
        /// loses it; the sub-line is the simulation's own reason.
        /// </summary>
        public void ShowEnding(SimState st)
        {
            if (!Root.Driver.IsReplay)
            {
                _lastLog = Root.Driver.Match.Log.ToList();
                _lastSide = Root.PlayerSide;
                _lastLength = Root.Options.Length;
                _lastLevel = Root.Level;
                _lastSeed = Root.Seed;
            }
            Outcome = st.Winner == Side.Us ? "THE LINE HELD"
                    : st.Winner == Side.Vc ? "THE WIRE IS BREACHED"
                    : "NEITHER SIDE BROKE";
            EndTitle.text = Outcome;
            EndTitle.EnableInClassList("us", st.Winner == Side.Us);
            EndTitle.EnableInClassList("vc", st.Winner == Side.Vc);
            EndReason.text = st.Reason.ToUpperInvariant();
            EndVerdict.text = st.Winner == null ? "A DRAW" : st.Winner == Root.PlayerSide ? "VICTORY" : "DEFEAT";
            Report.text = AfterAction(st);
            Enter(FlowState.Over);
        }

        /// <summary>The after-action report: losses, what survived, how steady, what was called, how long.</summary>
        public string AfterAction(SimState st)
        {
            string Row(Side side)
            {
                var men = st.Men.Where(m => m.Side == side).ToList();
                int lost = men.Count(m => !m.Alive), alive = men.Count - lost;
                double vet = alive > 0 ? men.Where(m => m.Alive).Average(m => m.Veterancy) : 0;
                int squads = st.Squads.Count(q => q.Side == side);
                return $"{(side == Side.Us ? "US / ARVN" : "VC / NVA"),-10} LOST {lost,3}   STANDING {alive,3}   " +
                       $"SQUADS {squads,2}   STEADINESS {vet * 100,3:0}%   MORALE {st.Morale[(int)side] * 100,3:0}%";
            }
            int cards = Root.Driver.Match.Log.Count(l => l.Accepted && l.Command.Kind == CommandKind.Buy);
            int orders = Root.Driver.Match.Log.Count(l => l.Accepted && l.Command.Kind == CommandKind.Order);
            int secs = st.Tick / Tune.TickHz;
            return $"{Row(Side.Us)}\n{Row(Side.Vc)}\n\nCARDS PLAYED {cards}   ORDERS GIVEN {orders}   TIME {secs / 60}:{secs % 60:00}";
        }
    }
}
