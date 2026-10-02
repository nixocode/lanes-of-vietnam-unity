using System;
using LanesOfVietnam.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// Arm a card, then place it: the 2D game's model, and the reason call-ins
    /// feel aimed rather than cast. Click a card or press its key, see where it
    /// will land, click the ground. Right click or Escape puts it away.
    ///
    /// Keys, laid out on the keyboard the way the cards lie on the screen:
    /// call-ins on Q W E R (the reference's orders line asks for punji with
    /// [Q]), unit cards on Z X C V B along the bottom row. Orders keep 1-4 and
    /// 0 (PLAN §5.1).
    ///
    /// Placing is a command to the simulation like any other: the purchase
    /// happens at the next tick boundary and the simulation decides whether it
    /// can be afforded.
    /// </summary>
    public sealed class Deployer : MonoBehaviour
    {
        public GameRoot Root;
        public Material MarkerMaterial;
        /// <summary>LOV/Lane: the ribbons, the disc and the beams of the lane selector (<see cref="LaneMarks"/>).</summary>
        public Material LaneMaterial;
        public LaneMarks Marks { get; private set; }

        public static readonly KeyCode[] CallKeys = { KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R, KeyCode.T };
        public static readonly KeyCode[] UnitKeys = { KeyCode.Z, KeyCode.X, KeyCode.C, KeyCode.V, KeyCode.B, KeyCode.N };

        /// <summary>The card in hand, or null.</summary>
        public Card Armed { get; private set; }

        /// <summary>Where the armed card would land now: lane and x. Valid when <see cref="HasTarget"/>.</summary>
        public int TargetLane { get; private set; }
        public double TargetX { get; private set; }
        public bool HasTarget { get; private set; }

        /// <summary>Raised when a card is placed: its id, lane and x. The HUD's field orders listen.</summary>
        public event Action<Card, int, double> Placed;

        private SimState State => Root.Driver.State;

        private void Awake()
        {
            var go = new GameObject("lane marks");
            go.transform.SetParent(transform, false);
            Marks = go.AddComponent<LaneMarks>();
        }

        public Card[] Hand(CardGroup? group = null)
        {
            var all = Deck.For(Root.PlayerSide);
            if (group == null) return all;
            // The call-ins, or the squads: sorted out once for each side, not on every frame the keys are read.
            if (_handOf != Root.PlayerSide || _calls == null)
            {
                _handOf = Root.PlayerSide;
                _calls = Array.FindAll(all, c => c.Group == CardGroup.Call);
                _units = Array.FindAll(all, c => c.Group != CardGroup.Call);
            }
            return group == CardGroup.Call ? _calls : _units;
        }

        private Side _handOf;
        private Card[] _calls, _units;

        public bool Arm(Card card)
        {
            if (card == null || State.Over) return false;
            Armed = card;
            return true;
        }

        /// <summary>The frame a card was last put away, so the same Escape or click is not read twice.</summary>
        public int DisarmedFrame { get; private set; } = -1;

        public void Disarm()
        {
            if (Armed != null) DisarmedFrame = Time.frameCount;
            Armed = null;
            HasTarget = false;
            _chosen = -1;
        }

        private void Update()
        {
            if (CaptureSettings.Active == null && !Root.Paused)
            {
                var calls = Hand(CardGroup.Call);
                var units = Hand(CardGroup.Line);
                for (int i = 0; i < CallKeys.Length && i < calls.Length; i++)
                    if (Input.GetKeyDown(CallKeys[i])) Arm(calls[i]);
                for (int i = 0; i < UnitKeys.Length && i < units.Length; i++)
                    if (Input.GetKeyDown(UnitKeys[i])) Arm(units[i]);
                if (Armed != null)
                {
                    // The other lane, by hand: up and down. It stands until the pointer is moved.
                    if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow)) Switch(Input.GetKeyDown(KeyCode.UpArrow) ? 1 : -1);
                    Aim(Input.mousePosition);
                    if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) Disarm();
                    else if (Input.GetMouseButtonDown(0) && !HudHasPointer()) PlaceAt(Input.mousePosition);
                }
            }
            DrawMarker();
        }

        /// <summary>Set by the HUD while the pointer is over it, so a click on a card is not also a click on the ground.</summary>
        public Func<bool> HudHasPointer = () => false;

        /// <summary>Where the ground is under a screen point, by marching the ray over the height function.</summary>
        public bool GroundAt(Vector2 screen, out double x, out double z)
        {
            var ray = Root.CameraRig.Camera.ScreenPointToRay(screen);
            x = z = 0;
            float prevT = 0;
            for (float t = 1f; t < 700f; t += t < 120f ? 1f : 4f)
            {
                var p = ray.GetPoint(t);
                if (p.y <= (float)Root.Ground.HeightAt(p.x, Coords.SimZ(p.z)))
                {
                    // Bisect the last step down to a centimetre.
                    float lo = prevT, hi = t;
                    for (int i = 0; i < 14; i++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        var q = ray.GetPoint(mid);
                        if (q.y <= (float)Root.Ground.HeightAt(q.x, Coords.SimZ(q.z))) hi = mid; else lo = mid;
                    }
                    var hit = ray.GetPoint(hi);
                    x = hit.x;
                    z = Coords.SimZ(hit.z);
                    return true;
                }
                prevT = t;
            }
            return false;
        }

        /// <summary>The lane nearest a ground point: the card goes to the lane you point at.</summary>
        public static int LaneNearest(double z)
        {
            int best = 0;
            for (int i = 1; i < Tune.Lanes.Length; i++)
                if (Math.Abs(z - Tune.Lanes[i]) < Math.Abs(z - Tune.Lanes[best])) best = i;
            return best;
        }

        /// <summary>A lane chosen by key, and where the pointer was when it was: moving the pointer takes the choice back.</summary>
        private int _chosen = -1;
        private Vector2 _chosenAt, _lastAim;
        public const float ChoiceHolds = 36f;

        /// <summary>Take this lane, whatever the pointer is on (the HUD's lane tags).</summary>
        public void Choose(int lane)
        {
            if (Armed == null) return;
            _chosen = Mathf.Clamp(lane, 0, Tune.Lanes.Length - 1);
            _chosenAt = _lastAim;
            TargetLane = _chosen;
        }

        /// <summary>Take the next lane further from the lens (+1) or nearer it (-1), whatever the pointer is on.</summary>
        public void Switch(int step)
        {
            // Lane 0 is the near one; the far lanes follow it.
            int now = _chosen >= 0 ? _chosen : TargetLane;
            _chosen = Mathf.Clamp(now + step, 0, Tune.Lanes.Length - 1);
            _chosenAt = _lastAim;
            TargetLane = _chosen;
        }

        /// <summary>
        /// The lane a screen point is on. A man it is on is in his lane: the
        /// lens sits at eye level, so the head and chest of a man in the near
        /// lane are drawn over the far lane's ground, and by the ground alone
        /// pointing at a squad chose the lane behind it. Off any man, the lane
        /// nearest the ground under the point; over the trees and the sky,
        /// where there is no ground, the far lane.
        /// </summary>
        public int LaneAt(Vector2 screen, out bool onGround, out double groundX, out int groundLane)
        {
            onGround = GroundAt(screen, out groundX, out double z);
            groundLane = onGround ? LaneNearest(z) : -1;
            var cam = Root.CameraRig.Camera;
            var st = State;
            int over = -1;
            float nearest = float.MaxValue;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                if (!m.Alive || (m.Side != Root.PlayerSide && !m.Seen)) continue;
                var (mx, mz) = Root.ArmyView.Where(Root.Driver, i);
                float y = (float)Root.Ground.HeightAt(mx, mz);
                float tall = m.Posture == Posture.Prone ? 0.5f : m.Posture == Posture.Crouched ? 1.15f : 1.8f;
                var feet = cam.WorldToScreenPoint(Coords.World(mx, mz, y));
                var head = cam.WorldToScreenPoint(Coords.World(mx, mz, y + tall));
                if (feet.z <= 0) continue;
                float half = Mathf.Max(6f, (head.y - feet.y) / tall * 0.45f);
                if (screen.x < feet.x - half || screen.x > feet.x + half || screen.y < feet.y - 4f || screen.y > head.y + 4f) continue;
                if (feet.z < nearest) { nearest = feet.z; over = st.Squads[m.Squad].Lane; }
            }
            if (over >= 0) return over;
            return onGround ? groundLane : Tune.Lanes.Length - 1;
        }

        public bool Aim(Vector2 screen)
        {
            _lastAim = screen;
            int lane = LaneAt(screen, out bool onGround, out double gx, out int groundLane);
            if (_chosen >= 0)
            {
                if ((screen - _chosenAt).sqrMagnitude > ChoiceHolds * ChoiceHolds) _chosen = -1;
                else lane = _chosen;
            }
            TargetLane = lane;
            // Along the lane: under the pointer if it is on that lane's ground; else where the line
            // of sight through the pointer crosses the lane (over a man, the trees or the sky).
            double x = gx;
            if (!onGround || groundLane != lane)
            {
                var ray = Root.CameraRig.Camera.ScreenPointToRay(screen);
                float planeZ = Coords.WorldZ(Tune.Lanes[lane]);
                if (Mathf.Abs(ray.direction.z) > 1e-5f)
                {
                    float t = (planeZ - ray.origin.z) / ray.direction.z;
                    if (t > 0) x = ray.origin.x + ray.direction.x * t;
                }
            }
            HasTarget = true;
            TargetX = Math.Max(-Tune.HalfLength, Math.Min(Tune.HalfLength, x));
            return true;
        }

        /// <summary>Hold a card on a lane at an x without the pointer: for a capture and the tests.</summary>
        public void Hold(Card card, int lane, double x)
        {
            Armed = card;
            TargetLane = lane; TargetX = x; HasTarget = card != null;
        }

        /// <summary>Place the armed card at a screen point. Returns whether a purchase was sent.</summary>
        public bool PlaceAt(Vector2 screen)
        {
            if (Armed == null || !Aim(screen)) return false;
            return Place(TargetLane, TargetX);
        }

        /// <summary>Place the armed card in a lane at an x. UIAudit places cards through here.</summary>
        public bool Place(int lane, double x)
        {
            var card = Armed;
            if (card == null) return false;
            Root.Driver.Match.Issue(Command.Buy(Root.PlayerSide, card.Id, lane, x));
            Placed?.Invoke(card, lane, x);
            Disarm();
            return true;
        }

        /// <summary>How far a card's effect reaches, for the marker: the radii Deck.Buy uses.</summary>
        public static float Radius(Card c) => c.Id switch
        {
            "us-arty" => 17f, "us-airstrike" => 11f, "us-smoke" => 14f,
            "vc-punji" => 3.2f, "vc-tripwire" => 5f, "vc-spider" => 2.4f, "vc-tunnel" => 8f,
            _ => 0f,
        };

        /// <summary>Where a squad bought now would come in: its side's end of the lane.</summary>
        public double EntryX => Combat.Advance(Root.PlayerSide) > 0 ? -Tune.HalfLength * 0.92 : Tune.HalfLength * 0.92;

        private void DrawMarker()
        {
            if (Marks == null || Root == null || Root.CameraRig == null) return;
            Marks.Material = LaneMaterial;
            bool show = Armed != null && HasTarget;
            Marks.Show(Root.Ground, Root.CameraRig.Camera.transform, show, TargetLane, TargetX,
                       show ? Radius(Armed) : 0f, EntryX, (float)Combat.Advance(Root.PlayerSide), Time.unscaledDeltaTime);
        }
    }
}
