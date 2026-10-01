using System.Collections.Generic;
using LanesOfVietnam.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace LanesOfVietnam.View.UI
{
    /// <summary>
    /// The plate over one position: whose it is, how many of the player's men
    /// are in it, and the player's lever on it.
    /// </summary>
    public sealed class PositionPlate : VisualElement
    {
        public readonly int Cover;
        public readonly VisualElement Flag;
        public readonly Label Count;
        public readonly Button LeverButton;
        public Lever Shown { get; private set; } = Lever.Auto;

        public PositionPlate(Cover c, System.Action<int, Lever> pull)
        {
            Cover = c.Id;
            name = $"plate-{c.Id}";
            AddToClassList("plate");
            pickingMode = PickingMode.Ignore;
            Flag = new VisualElement { pickingMode = PickingMode.Ignore }; Flag.AddToClassList("plate-flag");
            Count = new Label("0/0") { pickingMode = PickingMode.Ignore }; Count.AddToClassList("plate-count");
            // Left click flips it between Hold and Go; right click gives it back to the plan.
            LeverButton = new Button(() => pull(Cover, Shown == Lever.Hold ? Lever.Go : Lever.Hold)) { name = $"lever-{c.Id}", text = "AUTO" };
            LeverButton.AddToClassList("plate-lever");
            LeverButton.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 1) return;
                pull(Cover, Lever.Auto);
                e.StopPropagation();
            });
            var body = new VisualElement { pickingMode = PickingMode.Ignore }; body.AddToClassList("plate-body");
            body.Add(Count); body.Add(LeverButton);
            Add(Flag); Add(body);
        }

        public void Set(Side? owner, int mine, int capacity, Lever lever, bool enemyHeld)
        {
            Flag.EnableInClassList("us", owner == Side.Us);
            Flag.EnableInClassList("vc", owner == Side.Vc);
            Count.text = $"{mine}/{capacity}";
            Shown = lever;
            LeverButton.text = lever == Lever.Hold ? "HOLD" : lever == Lever.Go ? "GO ▶" : "AUTO";
            LeverButton.EnableInClassList("hold", lever == Lever.Hold);
            LeverButton.EnableInClassList("go", lever == Lever.Go);
            // The enemy's position shows whose it is; the lever on it is for when it is yours.
            LeverButton.style.opacity = enemyHeld && mine == 0 ? 0.55f : 1f;
        }
    }

    /// <summary>
    /// Warfare 1944's lever, on every trench, wall and bunker
    /// (<see cref="Fieldcraft"/>): a plate floating over each position, placed
    /// from the world every frame, with the player's standing order on it.
    ///
    ///   HOLD   squads that reach it stay in it, and the next squad up the lane
    ///          makes for it
    ///   GO     the squad in it goes over the top, and squads behind pass through
    ///   AUTO   the squad's own judgement, which is where every position starts
    ///
    /// Every pull is a simulation command, applied at the next tick and written
    /// to the match's log, so a replay pulls the same levers.
    /// </summary>
    public sealed class PositionPlates
    {
        public readonly List<PositionPlate> Plates = new List<PositionPlate>();
        /// <summary>Every lever pulled, for UIAudit.</summary>
        public readonly List<string> Log = new List<string>();
        /// <summary>Raised when a lever is pulled, with what it was set to.</summary>
        public event System.Action<Lever> Pulled;

        private readonly GameRoot _root;
        private readonly VisualElement _layer;
        /// <summary>Metres above the ground the plate floats.</summary>
        public const float Height = 2.7f;

        public PositionPlates(GameRoot root, VisualElement parent)
        {
            _root = root;
            _layer = new VisualElement { name = "plates", pickingMode = PickingMode.Ignore };
            _layer.style.position = Position.Absolute;
            _layer.style.left = 0; _layer.style.right = 0; _layer.style.top = 0; _layer.style.bottom = 0;
            parent.Add(_layer);
            var st = root.Driver?.State;
            if (st == null || !st.Fieldcraft) return;
            foreach (var c in st.Cover)
            {
                if (!Fieldcraft.IsPosition(c)) continue;
                var p = new PositionPlate(c, Pull);
                _layer.Add(p);
                Plates.Add(p);
            }
        }

        /// <summary>Set the player's lever on a position.</summary>
        public void Pull(int cover, Lever lever)
        {
            _root.Driver.Match.Issue(Command.SetLever(_root.PlayerSide, cover, lever));
            Log.Add($"lever {cover}: {lever}");
            Pulled?.Invoke(lever);
        }

        /// <summary>The plate under a panel point, or the nearest one within reach of it; null if none.</summary>
        public PositionPlate Near(Vector2 panel, float reach = 120f)
        {
            PositionPlate best = null;
            float bestD = reach;
            foreach (var p in Plates)
            {
                if (p.style.display == DisplayStyle.None) continue;
                float d = Vector2.Distance(p.worldBound.center, panel);
                if (d < bestD) { best = p; bestD = d; }
            }
            return best;
        }

        public PositionPlate Of(int cover) => Plates.Find(p => p.Cover == cover);

        public void Update()
        {
            var st = _root.Driver?.State;
            var cam = _root.CameraRig != null ? _root.CameraRig.Camera : null;
            if (st == null || cam == null || _layer.panel == null) return;
            var me = _root.PlayerSide;
            foreach (var p in Plates)
            {
                var c = st.Cover[p.Cover];
                int mine = 0, theirs = 0;
                for (int i = 0; i < st.Men.Count; i++)
                {
                    var m = st.Men[i];
                    if (!m.Alive || m.Cover != c.Id) continue;
                    if (m.Side == me) mine++; else if (m.Seen) theirs++;
                }
                var owner = c.Owner ?? (mine > 0 ? me : theirs > 0 ? Combat.Other(me) : (Side?)null);
                p.Set(owner, mine, c.Capacity, Fieldcraft.LeverOf(c, me), owner.HasValue && owner != me);

                var world = Coords.World(c.X, c.Z, (float)_root.Ground.HeightAt(c.X, c.Z) + Height);
                var vp = cam.WorldToViewportPoint(world);
                bool seen = vp.z > 0.5f && vp.x > -0.05f && vp.x < 1.05f && vp.y > 0.05f && vp.y < 1.0f;
                p.style.display = seen ? DisplayStyle.Flex : DisplayStyle.None;
                if (!seen) continue;
                // From the viewport, not from screen pixels: the panel is scaled, and a capture's lens draws to its own texture.
                float w = _layer.resolvedStyle.width, h = _layer.resolvedStyle.height;
                if (float.IsNaN(w) || w <= 0) continue;
                p.style.left = vp.x * w - 46;
                p.style.top = (1 - vp.y) * h - 46;
            }
        }
    }
}
