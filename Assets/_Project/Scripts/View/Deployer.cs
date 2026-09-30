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

        public static readonly KeyCode[] CallKeys = { KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R, KeyCode.T };
        public static readonly KeyCode[] UnitKeys = { KeyCode.Z, KeyCode.X, KeyCode.C, KeyCode.V, KeyCode.B };

        /// <summary>The card in hand, or null.</summary>
        public Card Armed { get; private set; }

        /// <summary>Where the armed card would land now: lane and x. Valid when <see cref="HasTarget"/>.</summary>
        public int TargetLane { get; private set; }
        public double TargetX { get; private set; }
        public bool HasTarget { get; private set; }

        /// <summary>Raised when a card is placed: its id, lane and x. The HUD's field orders listen.</summary>
        public event Action<Card, int, double> Placed;

        private Transform _marker, _area;
        private MaterialPropertyBlock _mpb;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private SimState State => Root.Driver.State;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var mesh = quad.GetComponent<MeshFilter>().sharedMesh;
            Destroy(quad);
            _marker = MakeQuad("aim marker", mesh);
            _area = MakeQuad("aim area", mesh);
        }

        private Transform MakeQuad(string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MarkerMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            return go.transform;
        }

        public Card[] Hand(CardGroup? group = null)
        {
            var all = Deck.For(Root.PlayerSide);
            if (group == null) return all;
            return Array.FindAll(all, c => group == CardGroup.Call ? c.Group == CardGroup.Call : c.Group != CardGroup.Call);
        }

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

        public bool Aim(Vector2 screen)
        {
            HasTarget = GroundAt(screen, out double x, out double z);
            if (!HasTarget) return false;
            TargetLane = LaneNearest(z);
            TargetX = Math.Max(-Tune.HalfLength, Math.Min(Tune.HalfLength, x));
            return true;
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

        private void DrawMarker()
        {
            bool show = Armed != null && HasTarget;
            _marker.gameObject.SetActive(show);
            _area.gameObject.SetActive(show && Radius(Armed) > 0);
            if (!show) return;
            double laneZ = Tune.Lanes[TargetLane];
            float y = (float)Root.Ground.HeightAt(TargetX, laneZ);
            var cam = Root.CameraRig.Camera.transform;
            _mpb.SetColor(ColorId, new Color(1f, 0.74f, 0.26f, 0.95f));
            _marker.SetPositionAndRotation(Coords.World(TargetX, laneZ, y + 1.3f), cam.rotation);
            _marker.localScale = Vector3.one * 1.6f;
            _marker.GetComponent<MeshRenderer>().SetPropertyBlock(_mpb);
            float r = Radius(Armed);
            if (r > 0)
            {
                // The reach on the ground. Seen along the ground it is a thin
                // ellipse — enough to show how far along the line it bites.
                _area.SetPositionAndRotation(Coords.World(TargetX, laneZ, y + 0.15f), Quaternion.Euler(90, 0, 0));
                _area.localScale = new Vector3(r * 2, r * 2, 1);
                _area.GetComponent<MeshRenderer>().SetPropertyBlock(_mpb);
            }
        }
    }
}
