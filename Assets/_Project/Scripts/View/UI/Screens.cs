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
        public Difficulty ChosenLevel { get; private set; } = Difficulty.Veteran;
        /// <summary>The operation chosen (PLAN §12.27, Phase S): the firebase, until another is.</summary>
        public Scenario ChosenScenario { get; private set; } = Scenarios.Firebase;

        public VisualElement StartPanel { get; private set; }
        public VisualElement SettingsPanel { get; private set; }
        public VisualElement EndPanel { get; private set; }
        /// <summary>The chosen operation's briefing, beside the start screen: its title, its date line, the briefing and the orders.</summary>
        public VisualElement BriefPanel { get; private set; }
        public Label BriefTitle { get; private set; }
        public Label BriefMeta { get; private set; }
        public Label BriefText { get; private set; }
        public Label BriefOrders { get; private set; }
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
        private Difficulty _lastLevel;
        private Scenario _lastScenario;
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

        /// <summary>
        /// What else the game is made from (ASSETS.md has each item, its licence and its hash). The motion
        /// capture's line is in the words its database asks for; the fonts' licence asks to be named.
        /// </summary>
        public const string Credits =
            "ANIMATION Adobe Mixamo; mocap.cs.cmu.edu (created with funding from NSF EIA-0196217) · " +
            "SOUND Sonniss #GameAudioGDC, The Free Firearm Sound Library (CC0), US armed forces footage (public domain)\n" +
            "GROUND, PLANTS, SKY Poly Haven and ambientCG (CC0) · TERRAIN SRTM (NASA/USGS) via AWS Terrain Tiles · " +
            "BODIES MakeHuman (CC0) · TYPE Black Ops One, Bebas Neue, Courier Prime, Stardos Stencil (SIL Open Font License 1.1)\n" +
            // The weapons: each its author's, under CC BY 4.0, changed here (turned, scaled, thinned, re-baked): ASSETS.md.
            "WEAPONS (Sketchfab, CC BY 4.0, adapted) M16A1 SeveN-Models · AK-47 billyjackman3d · SKS haoliu95 · M60 studiolab.dev · " +
            "RPD Polly_Hermiston · PPSh-41 Artem.Goyko · M3 XxRxX · Hunting Rifle Tnkii · Mosin PU Kazches · M79 denis1.eremin · RPG-7 javadbayat";

        private void BuildStart(VisualElement root)
        {
            StartPanel = new VisualElement { name = "start" };
            StartPanel.AddToClassList("screen");
            StartPanel.AddToClassList("start");
            Text(StartPanel, "LANES OF VIETNAM '65", "title");
            Text(StartPanel, "A FIREBASE IN THE CENTRAL HIGHLANDS · 1965", "kicker");

            // The operations: the firebase as it always was, and the scenarios fought on it. Its briefing is beside.
            Text(StartPanel, "OPERATION", "field");
            var ops = new VisualElement(); ops.AddToClassList("choice-row"); StartPanel.Add(ops);
            foreach (var sc in Scenarios.All)
            {
                var it = sc;
                var b = Btn(ops, $"op-{sc.Id}", $"{sc.Title}\n{Kind(sc)}", () => ChooseScenario(it), "choice");
                b.AddToClassList("op");
            }

            Text(StartPanel, "YOUR SIDE", "field");
            var sides = new VisualElement(); sides.AddToClassList("choice-row"); StartPanel.Add(sides);
            Btn(sides, "side-us", "US / ARVN\nHOLD THE FIREBASE", () => ChooseSide(Side.Us), "choice");
            Btn(sides, "side-vc", "VC / NVA\nTAKE IT", () => ChooseSide(Side.Vc), "choice");

            Text(StartPanel, "LENGTH", "field");
            var lens = new VisualElement(); lens.AddToClassList("choice-row"); StartPanel.Add(lens);
            Btn(lens, "len-skirmish", "SKIRMISH\n~2 MIN", () => ChooseLength(MatchLength.Skirmish), "choice");
            Btn(lens, "len-standard", "STANDARD\n~3½ MIN", () => ChooseLength(MatchLength.Standard), "choice");
            Btn(lens, "len-long", "LONG\n~8 MIN", () => ChooseLength(MatchLength.Long), "choice");

            // The 2D game's three: how fast the other side raises its squads.
            Text(StartPanel, "THE ENEMY", "field");
            var levels = new VisualElement(); levels.AddToClassList("choice-row"); StartPanel.Add(levels);
            Btn(levels, "level-recruit", "RECRUIT\nSLOW TO REINFORCE", () => ChooseLevel(Difficulty.Recruit), "choice");
            Btn(levels, "level-veteran", "VETERAN\nA FAIR FIGHT", () => ChooseLevel(Difficulty.Veteran), "choice");
            Btn(levels, "level-elite", "ELITE\nOUTNUMBERS YOU", () => ChooseLevel(Difficulty.Elite), "choice");

            var view = new VisualElement(); view.AddToClassList("choice-row"); StartPanel.Add(view);
            Btn(view, "full-start", "FULL SCREEN", FullScreen.Toggle, "choice");
            Btn(StartPanel, "deploy", "DEPLOY", () => Deploy(), "deploy");
            Text(StartPanel, "Sound starts when you deploy.", "fineprint");
            root.Add(StartPanel);

            BriefPanel = new VisualElement { name = "brief" };
            BriefPanel.AddToClassList("screen");
            BriefPanel.AddToClassList("brief");
            BriefTitle = Text(BriefPanel, "", "brief-title");
            BriefMeta = Text(BriefPanel, "", "kicker");
            BriefText = Text(BriefPanel, "", "brief-text");
            BriefOrders = Text(BriefPanel, "", "brief-orders");
            root.Add(BriefPanel);
            SyncChoices();
        }

        /// <summary>What a side is there to do in an operation, under its name on its button.</summary>
        public static string Task(Scenario sc, Side side)
        {
            if (sc.Mode == MatchMode.Siege) return side == sc.Attacker ? "BREAK IN" : "HOLD OUT";
            if (sc.Mode == MatchMode.Assault) return side == sc.Attacker ? "TAKE THEIR LINE" : "HOLD THE LINE";
            return side == Side.Us ? "HOLD THE FIREBASE" : "TAKE IT";
        }

        /// <summary>What kind of fight an operation is, under its name on its button.</summary>
        public static string Kind(Scenario sc)
        {
            string side = sc.Plays == Side.Us ? "US" : sc.Plays == Side.Vc ? "VC" : "EITHER SIDE";
            int min = (int)(sc.ClockSeconds / 60);
            return sc.Mode == MatchMode.Siege ? $"SIEGE · {min} MIN · {side}"
                 : sc.Mode == MatchMode.Assault ? $"ASSAULT · {min} MIN · {side}"
                 : side;
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
            Btn(toggles, "full", "FULL SCREEN", FullScreen.Toggle, "choice");

            Text(SettingsPanel,
                "KEYS   A/D or drag: pan · wheel: dolly · F: field glasses · click: select · Tab: next squad\n" +
                "1 advance · 2 hold · 3 fall back · 0 auto · Q W E R: call-ins · Z X C V B: units\n" +
                "P: pause · Esc: cancel / deselect / this menu · Alt+Enter: full screen",
                "keys");
            // The credit its licence asks for, where its author asks for it: with the settings. And the rest
            // of what the game is made from: nothing else in it requires a credit, and all of it is owed one.
            Text(SettingsPanel, MusicCredit, "keys");
            Text(SettingsPanel, Credits, "keys");

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
                case "start": ChooseScenario(Root.Scenario); Enter(FlowState.Start); break;
                case "settings": Enter(FlowState.Playing); Enter(FlowState.Paused); break;
                case "end":
                    if (Root.Driver.State.Over) ShowEnding(Root.Driver.State);
                    break;
            }
        }

        // --- actions ------------------------------------------------------------

        /// <summary>An operation that is one side's is played from that side: the other is not offered.</summary>
        public void ChooseSide(Side s) { if (ChosenScenario.Offers(s)) ChosenSide = s; SyncChoices(); }
        /// <summary>An operation with a length of its own (a clock, or one it names) keeps it.</summary>
        public void ChooseLength(MatchLength l) { if (ChosenScenario.Length == null) ChosenLength = l; SyncChoices(); }
        public void ChooseScenario(Scenario sc)
        {
            ChosenScenario = sc ?? Scenarios.Firebase;
            if (ChosenScenario.Plays.HasValue) ChosenSide = ChosenScenario.Plays.Value;
            SyncChoices();
        }
        public void ChooseLevel(Difficulty d) { ChosenLevel = d; SyncChoices(); }

        /// <summary>Start the chosen match and play the opening. Deploy is also the gesture that lets a browser start audio.</summary>
        public void Deploy()
        {
            Root.NewMatch(ChosenSide, ChosenLength, level: ChosenLevel, scenario: ChosenScenario);
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
            Root.NewMatch(ChosenSide, ChosenLength, level: ChosenLevel, scenario: ChosenScenario);
            Enter(FlowState.Start);
        }

        /// <summary>Play the match just finished again, from its own command log.</summary>
        public void WatchReplay()
        {
            if (_lastLog == null) return;
            Root.NewMatch(_lastSide, _lastLength, _lastSeed, _lastLog, _lastLevel, _lastScenario);
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
            BriefPanel.style.display = s == FlowState.Start ? DisplayStyle.Flex : DisplayStyle.None;
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
            foreach (var sc in Scenarios.All) On($"op-{sc.Id}", sc == ChosenScenario);
            // What the operation settles is not his to choose: the side it is played from, a length of its own.
            void Offer(string id, bool on) { if (Buttons.TryGetValue(id, out var b)) b.SetEnabled(on); }
            Offer("side-us", ChosenScenario.Offers(Side.Us));
            Offer("side-vc", ChosenScenario.Offers(Side.Vc));
            // And what each side is there to do, under its name.
            if (Buttons.TryGetValue("side-us", out var usBtn)) usBtn.text = "US / ARVN\n" + Task(ChosenScenario, Side.Us);
            if (Buttons.TryGetValue("side-vc", out var vcBtn)) vcBtn.text = "VC / NVA\n" + Task(ChosenScenario, Side.Vc);
            bool anyLength = ChosenScenario.Length == null;
            Offer("len-skirmish", anyLength); Offer("len-standard", anyLength); Offer("len-long", anyLength);
            if (BriefTitle != null && _briefFor != ChosenScenario)
            {
                _briefFor = ChosenScenario;
                BriefTitle.text = ChosenScenario.Title;
                BriefMeta.text = ChosenScenario.DateLine;
                BriefText.text = ChosenScenario.Briefing;
                BriefOrders.text = string.Join("\n", ChosenScenario.Orders.Select(o => "◆ " + o));
            }
            On("side-us", ChosenSide == Side.Us);
            On("side-vc", ChosenSide == Side.Vc);
            var length = ChosenScenario.Length ?? ChosenLength;
            On("len-skirmish", anyLength && length == MatchLength.Skirmish);
            On("len-standard", anyLength && length == MatchLength.Standard);
            On("len-long", anyLength && length == MatchLength.Long);
            On("level-recruit", ChosenLevel == Difficulty.Recruit);
            On("level-veteran", ChosenLevel == Difficulty.Veteran);
            On("level-elite", ChosenLevel == Difficulty.Elite);
            On("full-start", FullScreen.On);
            On("full", FullScreen.On);
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

        private bool _fullShown;
        private Scenario _briefFor;

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var st = Root.Driver?.State;
            // Full screen, from any screen: Alt and Enter. (And its buttons lit while it is, however it was left.)
            if (FullScreen.KeyDown) FullScreen.Toggle();
            if (FullScreen.On != _fullShown) { _fullShown = FullScreen.On; SyncChoices(); }
            switch (State)
            {
                case FlowState.Start:
                    // The live scene behind the menu, slowly panning down the line.
                    _panT += dt;
                    Root.CameraRig.Focus(Mathf.Sin(_panT * 0.035f) * 34f);
                    if (Input.GetKeyDown(KeyCode.Return) && !FullScreen.AltHeld) Deploy();
                    break;

                case FlowState.Opening:
                    // The valley, then the firebase: the camera travels, the radio
                    // calls it, the first order comes up. Camera and words only —
                    // the simulation's own opening, the infiltration, is running.
                    _openingT += dt;
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_openingT / OpeningSeconds));
                    Root.CameraRig.Focus(Mathf.Lerp(OpeningFromX, OpeningToX, k), instant: true);
                    Subtitle.text = Root.Settings.Subtitles && _openingT > 1.2f && _openingT < 6.5f
                        ? Radio(Root.Scenario, Root.PlayerSide)
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

        /// <summary>The radio's call as the match opens: the scenario's own, where it has one.</summary>
        public static string Radio(Scenario sc, Side side)
        {
            if (sc == Scenarios.TheWire) return "RADIO — ALL STATIONS, BRAVO SIX. THEY ARE IN THE WIRE. RELIEF IS FIVE MINUTES OUT. HOLD.";
            if (sc == Scenarios.FirstLight) return "RADIO — BRAVO SIX TO ALL PLATOONS. THE LINE IS IN THE TREES AHEAD. MOVE UP. TAKE IT.";
            if (sc == Scenarios.EarthFights) return "RADIO — THEY HAVE THE SKY. WE HAVE THE GROUND. LET THEM COME ON TO IT.";
            return side == Side.Us
                ? "RADIO — ALL STATIONS, THIS IS BRAVO SIX. MOVEMENT IN THE TREELINE. STAND TO."
                : "RADIO — THE AMERICANS ARE ASLEEP IN THEIR HOLES. MOVE UP THROUGH THE GRASS. NO ONE FIRES.";
        }

        /// <summary>
        /// How it ended, in the fight's own terms: a clock that ran out and a line that was taken say so; else
        /// whose will broke, as the side that was coming on or the side that was holding.
        /// </summary>
        public static string OutcomeOf(SimState st)
        {
            if (st.Reason != null && st.Reason.EndsWith("took the line")) return "THE LINE IS TAKEN";
            if (st.Reason != null && st.Reason.EndsWith("held the line")) return "THE ATTACK IS CALLED OFF";
            if (st.Reason != null && st.Reason.EndsWith("held out")) return "THE FIREBASE HOLDS";
            if (st.Winner == null) return "NEITHER SIDE BROKE";
            // The Americans coming on (an assault of theirs): it is the enemy's line that breaks, or their attack.
            if (st.Attacker == Side.Us) return st.Winner == Side.Us ? "THEIR LINE IS BROKEN" : "THE ATTACK IS BROKEN";
            return st.Winner == Side.Us ? "THE LINE HELD" : "THE WIRE IS BREACHED";
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
                _lastScenario = Root.Scenario;
                _lastSeed = Root.Seed;
            }
            Outcome = OutcomeOf(st);
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
