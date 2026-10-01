using System.Collections.Generic;
using LanesOfVietnam.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace LanesOfVietnam.View.UI
{
    /// <summary>
    /// The interface of brief §7, built to TARGET.jpg: the command strip above
    /// the play area, the deck below it, a thin frame around it.
    ///
    /// It reads the simulation and never writes it: every control ends in a
    /// command (through <see cref="Commander"/> or <see cref="Deployer"/>) or a
    /// view setting. Each control's state is shown on the control itself — "a
    /// toggle that does not visibly toggle is a bug" — and UIAudit drives each
    /// one both ways.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(-60)]     // before Screens, which shows and hides it from its own OnEnable
    public sealed class Hud : MonoBehaviour
    {
        public GameRoot Root;
        public Commander Commander;
        public Deployer Deployer;
        public StyleSheet Style;

        public MoraleBar UsMorale { get; private set; }
        public MoraleBar VcMorale { get; private set; }
        public TacticalStrip Strip { get; private set; }
        public Label OrdersLine { get; private set; }
        public Label CpValue { get; private set; }
        public Label TutorLine { get; private set; }
        public Label ArmedHint { get; private set; }
        public readonly List<VisualElement> Diamonds = new List<VisualElement>();
        public readonly List<CardView> Cards = new List<CardView>();
        public readonly Dictionary<string, Button> Buttons = new Dictionary<string, Button>();

        /// <summary>The lever over each position (PositionPlates): Warfare 1944's hold and go.</summary>
        public PositionPlates Positions { get; private set; }

        public FieldOrders Orders { get; private set; }
        public Tutor Tutor { get; private set; }

        /// <summary>View toggles the buttons own. Audio reads them once it exists.</summary>
        public bool Sound { get; set; } = true;
        public bool Music { get; set; } = true;

        /// <summary>Raised by the settings button; the screens layer opens the panel.</summary>
        public event System.Action SettingsRequested;

        private VisualElement _root;
        private int _eventsRead;

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            _root = doc.rootVisualElement;
            if (Style != null && !_root.styleSheets.Contains(Style)) _root.styleSheets.Add(Style);
            _container = new VisualElement { name = "hud", pickingMode = PickingMode.Ignore };
            _container.style.position = Position.Absolute;
            _container.style.left = 0; _container.style.right = 0; _container.style.top = 0; _container.style.bottom = 0;
            _root.Insert(0, _container);
            Rebuild();
            Deployer.HudHasPointer = HasPointer;
            Commander.HudHasPointer = HasPointer;
            Deployer.Placed += OnPlaced;
            Root.MatchStarted += Rebuild;
            Commander.SelectionChanged += OnSelection;
        }

        private void OnDisable()
        {
            if (Deployer != null) Deployer.Placed -= OnPlaced;
            if (Root != null) Root.MatchStarted -= Rebuild;
            if (Commander != null) Commander.SelectionChanged -= OnSelection;
        }

        private VisualElement _container;

        private void OnSelection(int id) { if (id >= 0) Tutor?.Teach("select"); }

        /// <summary>
        /// A new match may be a different side: another deck, other orders, the
        /// strip seen from the other end. So the HUD is rebuilt, not patched.
        /// </summary>
        public void Rebuild()
        {
            _container.Clear();
            Diamonds.Clear();
            Cards.Clear();
            Buttons.Clear();
            _eventsRead = 0;
            Build(_container);
        }

        /// <summary>Show or hide the whole HUD (the start screen, the settings' "hide HUD").</summary>
        public void SetVisible(bool on)
        {
            if (_container != null) _container.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public bool Visible => _container != null && _container.style.display != DisplayStyle.None;

        private void Build(VisualElement root)
        {
            Orders = new FieldOrders(Root.PlayerSide);
            Tutor = new Tutor();

            var ui = new VisualElement { name = "lov-root", pickingMode = PickingMode.Ignore };
            ui.AddToClassList("lov-root");
            root.Add(ui);

            // The levers float over the battlefield, under the bars.
            Positions = new PositionPlates(Root, ui);
            Positions.Pulled += lever => { Orders.Mark(lever == Lever.Go ? "lever:go" : lever == Lever.Hold ? "lever:hold" : "lever:auto"); Tutor.Teach("lever"); };

            // --- the command strip ---------------------------------------------
            var top = new VisualElement { name = "topbar" };
            top.AddToClassList("topbar");
            top.style.backgroundImage = new StyleBackground(Gradient(new Color32(39, 44, 36, 245), new Color32(45, 51, 41, 0)));
            ui.Add(top);

            var us = new VisualElement(); us.AddToClassList("faction");
            var usLabel = new Label("★ US / ARVN"); usLabel.AddToClassList("faction-label");
            UsMorale = new MoraleBar(Side.Us);
            us.Add(usLabel); us.Add(UsMorale);
            top.Add(us);

            var centre = new VisualElement(); centre.AddToClassList("centre");
            var diamonds = new VisualElement(); diamonds.AddToClassList("diamonds");
            for (int lane = 0; lane < Tune.Lanes.Length; lane++)
            {
                var d = new VisualElement { name = $"diamond-{lane}" };
                d.AddToClassList("diamond");
                diamonds.Add(d);
                Diamonds.Add(d);
            }
            centre.Add(diamonds);
            OrdersLine = new Label("ORDERS") { name = "orders" };
            OrdersLine.AddToClassList("orders");
            centre.Add(OrdersLine);
            Strip = new TacticalStrip { name = "strip", Viewer = Root.PlayerSide };
            Strip.Clicked += x => Root.CameraRig.Focus(x);
            centre.Add(Strip);
            top.Add(centre);

            var vc = new VisualElement(); vc.AddToClassList("faction"); vc.AddToClassList("right");
            var vcLabel = new Label("VC / NVA ★"); vcLabel.AddToClassList("faction-label"); vcLabel.AddToClassList("right");
            VcMorale = new MoraleBar(Side.Vc);
            vc.Add(vcLabel); vc.Add(VcMorale);
            var buttons = new VisualElement(); buttons.AddToClassList("buttons");
            AddButton(buttons, "pause", "❚❚", TogglePause);
            AddButton(buttons, "speed", "1×", CycleSpeed);
            AddButton(buttons, "snd", "SND", () => Sound = !Sound);
            AddButton(buttons, "mus", "MUS", () => Music = !Music);
            AddButton(buttons, "settings", "⚙", () => SettingsRequested?.Invoke());
            vc.Add(buttons);
            top.Add(vc);

            TutorLine = new Label("") { name = "tutor", pickingMode = PickingMode.Ignore };
            TutorLine.AddToClassList("tutor");
            ui.Add(TutorLine);

            // --- the deck -------------------------------------------------------
            ArmedHint = new Label("") { name = "armed-hint", pickingMode = PickingMode.Ignore };
            ArmedHint.AddToClassList("armed-hint");
            ui.Add(ArmedHint);

            var bottom = new VisualElement { name = "bottombar" };
            bottom.AddToClassList("bottombar");
            ui.Add(bottom);

            var cp = new VisualElement(); cp.AddToClassList("cp");
            CpValue = new Label("0") { name = "cp" }; CpValue.AddToClassList("cp-value");
            var cpLabel = new Label("CP"); cpLabel.AddToClassList("cp-label");
            cp.Add(CpValue); cp.Add(cpLabel);
            bottom.Add(cp);

            var deck = Deck.For(Root.PlayerSide);
            int unitKey = 0, callKey = 0;
            foreach (var (group, title) in new[] { (CardGroup.Line, "LINE"), (CardGroup.Support, "SUPPORT"), (CardGroup.Special, "SPECIAL") })
            {
                var g = Group(bottom, title);
                foreach (var c in deck)
                {
                    if (c.Group != group) continue;
                    AddCard(g, c, Deployer.UnitKeys[unitKey++].ToString());
                }
            }
            // The reference puts no header over the call-ins: they are the dark
            // cards at the right, and the colour says what they are.
            var calls = Group(bottom, "");
            foreach (var c in deck)
            {
                if (c.Group != CardGroup.Call) continue;
                AddCard(calls, c, Deployer.CallKeys[callKey++].ToString());
            }

            var frame = new VisualElement { pickingMode = PickingMode.Ignore };
            frame.AddToClassList("frame");
            ui.Add(frame);
        }

        private VisualElement Group(VisualElement parent, string side)
        {
            var g = new VisualElement(); g.AddToClassList("group");
            var head = new VisualElement(); head.AddToClassList("group-head");
            var title = new Label(side); title.AddToClassList("group-title");
            var rule = new VisualElement(); rule.AddToClassList("group-rule");
            head.Add(title); head.Add(rule);
            var cards = new VisualElement(); cards.AddToClassList("group-cards");
            g.Add(head); g.Add(cards);
            parent.Add(g);
            return cards;
        }

        private void AddCard(VisualElement parent, Card c, string key)
        {
            var v = new CardView(c, key, Root.PlayerSide) { name = $"card-{c.Id}" };
            v.Clicked += card =>
            {
                if (Deployer.Armed == card) Deployer.Disarm();
                else if (Deployer.Arm(card)) Tutor.Teach("armed");
            };
            parent.Add(v);
            Cards.Add(v);
        }

        private void AddButton(VisualElement parent, string id, string text, System.Action onClick)
        {
            var b = new Button(onClick) { name = $"btn-{id}", text = text };
            b.AddToClassList("hud-button");
            parent.Add(b);
            Buttons[id] = b;
        }

        public void TogglePause() => Root.Paused = !Root.Paused;

        public void CycleSpeed() => Root.Speed = Root.Speed >= 3f ? 1f : Root.Speed + 1f;

        private void OnPlaced(Card card, int lane, double x)
            => Orders.Mark(card.Group == CardGroup.Call ? "call:" + card.Id : "deployed");

        /// <summary>Is the pointer over a part of the HUD that takes clicks?</summary>
        public bool HasPointer()
        {
            if (_root?.panel == null) return false;
            var screen = Input.mousePosition;
            var p = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screen.x, Screen.height - screen.y));
            var picked = _root.panel.Pick(p);
            return picked != null && picked.pickingMode == PickingMode.Position && picked != _root && picked != _container;
        }

        private void Update()
        {
            if (Root.Driver == null || !Visible) return;
            var st = Root.Driver.State;

            UsMorale.Set(st.Morale[(int)Side.Us]);
            VcMorale.Set(st.Morale[(int)Side.Vc]);

            var control = Match.LaneControl(st);
            for (int i = 0; i < Diamonds.Count && i < control.Length; i++)
            {
                var d = Diamonds[i];
                d.EnableInClassList("us", control[i].side == Side.Us);
                d.EnableInClassList("vc", control[i].side == Side.Vc);
                d.style.opacity = 0.45f + 0.55f * (float)control[i].margin;
            }

            Orders.Update(st);
            OrdersLine.text = Orders.Line;

            Positions.Update();
            // H holds and G sends: the position under the pointer, else the selected squad's.
            if (CaptureSettings.Active == null && (Input.GetKeyDown(KeyCode.H) || Input.GetKeyDown(KeyCode.G)))
            {
                var mouse = Input.mousePosition;
                var plate = Positions.Near(RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(mouse.x, Screen.height - mouse.y)));
                if (plate == null && Commander.Selected >= 0 && Commander.Selected < st.Squads.Count)
                    plate = Positions.Of(st.Squads[Commander.Selected].Target);
                if (plate != null) Positions.Pull(plate.Cover, Input.GetKeyDown(KeyCode.H) ? Lever.Hold : Lever.Go);
            }

            Strip.State = st;
            Strip.CameraX = Root.CameraRig.X;
            var cam = Root.CameraRig.Camera;
            float dist = Coords.Camera.SimZ - (float)Tune.Lanes[0];
            Strip.CameraHalfWidth = dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cam.aspect;
            Strip.MarkDirtyRepaint();

            double cpNow = st.Cp[(int)Root.PlayerSide];
            CpValue.text = Mathf.FloorToInt((float)cpNow).ToString();
            foreach (var v in Cards)
            {
                int cd = Deck.CooldownLeft(st, Root.PlayerSide, v.Card);
                v.Set(cpNow < v.Card.Cost, v.Card.Cooldown > 0 ? (float)cd / v.Card.Cooldown : 0f, Deployer.Armed == v.Card);
            }

            Buttons["pause"].text = Root.Paused ? "▶" : "❚❚";
            Buttons["pause"].EnableInClassList("on", Root.Paused);
            Buttons["speed"].text = $"{Root.Speed:0}×";
            Buttons["speed"].EnableInClassList("on", Root.Speed > 1f);
            Buttons["snd"].EnableInClassList("on", Sound);
            Buttons["mus"].EnableInClassList("on", Music);

            ArmedHint.text = Deployer.Armed == null ? ""
                : Deployer.HasTarget
                    ? $"{Deployer.Armed.Name} — {(Deployer.TargetLane == 0 ? "NEAR" : "FAR")} LANE · CLICK TO PLACE · RIGHT CLICK TO CANCEL"
                    : $"{Deployer.Armed.Name} — POINT AT THE GROUND";

            TeachFromEvents(st);
            Tutor.Update(Time.unscaledDeltaTime, (float)st.Tick / Tune.TickHz);
            TutorLine.text = Tutor.Showing ?? "";
        }

        /// <summary>Lessons from what has just happened to the player's side.</summary>
        private void TeachFromEvents(SimState st)
        {
            var me = Root.PlayerSide;
            for (; _eventsRead < st.Events.Count; _eventsRead++)
            {
                var e = st.Events[_eventsRead];
                switch (e.Kind)
                {
                    case EventKind.CoverTaken when e.Side == me: Tutor.Teach("cover"); break;
                    case EventKind.Pinned when e.Side == me: Tutor.Teach("pinned"); break;
                    case EventKind.RangedIn when e.Side != me: Tutor.Teach("ranged"); break;
                    case EventKind.SquadBroke when e.Side == me: Tutor.Teach("broken"); break;
                    case EventKind.BoundStart when e.Side == me: Tutor.Teach("bound"); break;
                    case EventKind.TrapSprung: Tutor.Teach("trap"); break;
                    case EventKind.FirstContact: Tutor.Teach("concealed"); Tutor.Teach("glasses"); break;
                }
            }
        }

        /// <summary>A vertical gradient texture: USS has no gradients, a 1 x 64 texture does.</summary>
        private static Texture2D Gradient(Color32 top, Color32 bottom)
        {
            var t = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "hud gradient" };
            for (int y = 0; y < 64; y++)
            {
                float f = y / 63f;
                // Opaque for the upper part, as the reference, then falling off over the sky.
                float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, f));
                t.SetPixel(0, 63 - y, Color.Lerp(top, bottom, k));
            }
            t.Apply(false, true);
            return t;
        }
    }
}
