using System.Collections.Generic;
using LanesOfVietnam.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace LanesOfVietnam.View.UI
{
    /// <summary>
    /// A tag over a squad: what it is, how many of it are left, and what it is
    /// doing ("M60 TEAM 3/3 · IN A FIREFIGHT"). PLAN §12.18 phase 2: a match a
    /// new player can read. A squad's task is the one thing the simulation
    /// knows about it that the picture does not say.
    ///
    /// Not always there (the owner on the strongpoint plates: "too prominent
    /// and too many"). A squad's tag shows while the pointer is on the squad,
    /// while it is the selected squad, for a few seconds when it arrives, and
    /// for a few seconds when it makes contact, when the tag says so in red:
    /// the mark that a squad has seen the enemy. An enemy squad's tag, on the
    /// pointer only and only while it is in sight, has its name and its number
    /// and nothing of what it means to do.
    /// </summary>
    public sealed class SquadTags
    {
        private readonly GameRoot _root;
        private readonly VisualElement _layer;
        private readonly Dictionary<int, Label> _tags = new Dictionary<int, Label>();
        /// <summary>What each tag last said, so its text is written when it changes and not every frame.</summary>
        private readonly Dictionary<int, (int live, int raised, string doing)> _said = new Dictionary<int, (int, int, string)>();
        /// <summary>Each squad's living men, by id as the simulation has them, gathered once a frame.</summary>
        private readonly List<List<Man>> _rosters = new List<List<Man>>();
        private readonly Dictionary<int, int> _contactAt = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _raisedAt = new Dictionary<int, int>();
        private int _eventsRead;

        public const float HoverRadius = 46f;
        /// <summary>Ticks a tag stays up after its squad arrives, and after it makes contact.</summary>
        public const int ArrivedTicks = 50, ContactTicks = 70;

        /// <summary>Where the pointer is, in the panel. Null: the mouse. UIAudit sets it.</summary>
        public Vector2? Pointer;
        /// <summary>Every squad in sight tagged, pointer or no: for a capture (tags=1).</summary>
        public bool ShowAll;
        /// <summary>The selected squad, or -1.</summary>
        public System.Func<int> Selected = () => -1;

        public SquadTags(GameRoot root, VisualElement parent)
        {
            _root = root;
            _layer = new VisualElement { name = "squad-tags", pickingMode = PickingMode.Ignore };
            _layer.style.position = Position.Absolute;
            _layer.style.left = 0; _layer.style.right = 0; _layer.style.top = 0; _layer.style.bottom = 0;
            parent.Add(_layer);
        }

        public Label Of(int squad) => _tags.TryGetValue(squad, out var l) ? l : null;

        /// <summary>What a squad is doing, in the player's words.</summary>
        public static string Doing(SimState st, Squad sq, IReadOnlyList<Man> live)
        {
            double pin = 0;
            for (int i = 0; i < live.Count; i++) pin += live[i].Pin;
            pin = live.Count > 0 ? pin / live.Count : 0;
            if (sq.Order == Order.Fallback) return "FALLING BACK";
            if (pin >= Tune.PinStop) return "PINNED";
            if (!st.Senses) return sq.Order == Order.Hold ? "HOLDING" : sq.Order == Order.Bound ? "BOUNDING" : "ADVANCING";
            bool held = sq.Order == Order.Hold && sq.Target >= 0 && sq.Target < st.Cover.Count
                        && Fieldcraft.LeverOf(st.Cover[sq.Target], sq.Side) == Lever.Hold;
            switch (sq.Task)
            {
                case SquadTask.Contact: return "CONTACT";
                case SquadTask.Firefight: return held ? "HOLDING · IN A FIREFIGHT" : "IN A FIREFIGHT";
                case SquadTask.Close: return "CLOSING";
                case SquadTask.Assault: return "GOING IN";
                case SquadTask.Withdraw: return "FALLING BACK";
                case SquadTask.Regroup: return "SPENT · HOLDING";
                default: return held || sq.Order == Order.Hold ? "HOLDING" : sq.Halted ? "WAITING" : "MOVING UP";
            }
        }

        public void Update()
        {
            var st = _root.Driver?.State;
            var cam = _root.CameraRig != null ? _root.CameraRig.Camera : null;
            if (st == null || cam == null || _layer.panel == null) return;
            float w = _layer.resolvedStyle.width, h = _layer.resolvedStyle.height;
            if (float.IsNaN(w) || w <= 0) return;
            var me = _root.PlayerSide;
            if (_eventsRead > st.Events.Count) { _eventsRead = 0; _contactAt.Clear(); _raisedAt.Clear(); foreach (var t in _tags.Values) t.RemoveFromHierarchy(); _tags.Clear(); _said.Clear(); }
            for (; _eventsRead < st.Events.Count; _eventsRead++)
            {
                var e = st.Events[_eventsRead];
                if (e.Kind == EventKind.Contact) _contactAt[e.Id] = e.Tick;
                else if (e.Kind == EventKind.SquadSpawned && e.Tick > 1) _raisedAt[e.Id] = e.Tick;
            }
            var mouse = Input.mousePosition;
            var pointer = Pointer ?? RuntimePanelUtils.ScreenToPanel(_layer.panel, new Vector2(mouse.x, Screen.height - mouse.y));
            int selected = Selected();

            // Every squad's living men in one pass. (A roster a squad a frame was a list made and sorted
            // for every squad ever raised, the dead ones too.)
            while (_rosters.Count < st.Squads.Count) _rosters.Add(new List<Man>(Tune.SquadMax));
            for (int id = 0; id < st.Squads.Count; id++) _rosters[id].Clear();
            for (int i = 0; i < st.Men.Count; i++) if (st.Men[i].Alive) _rosters[st.Men[i].Squad].Add(st.Men[i]);

            for (int id = 0; id < st.Squads.Count; id++)
            {
                var sq = st.Squads[id];
                var live = _rosters[id];
                _tags.TryGetValue(id, out var tag);
                bool mine = sq.Side == me;
                bool seen = mine;
                if (!mine) for (int i = 0; i < live.Count; i++) seen |= live[i].Seen;
                if (live.Count == 0 || !seen) { if (tag != null) tag.style.display = DisplayStyle.None; continue; }

                // Over the middle of the squad, above its tallest man; and is the pointer on any of them?
                float sx = 0, top = float.MaxValue;
                bool on = false, any = false;
                for (int i = 0; i < live.Count; i++)
                {
                    var (x, z) = _root.ArmyView.Where(_root.Driver, live[i].Id);
                    float y = (float)_root.Ground.HeightAt(x, z);
                    float tall = live[i].Posture == Posture.Prone ? 0.5f : live[i].Posture == Posture.Crouched ? 1.15f : 1.8f;
                    var vp = cam.WorldToViewportPoint(Coords.World(x, z, y + tall));
                    if (vp.z <= 0.5f) continue;
                    any = true;
                    var p = new Vector2(vp.x * w, (1 - vp.y) * h);
                    sx += p.x / live.Count;
                    top = Mathf.Min(top, p.y);
                    var feet = cam.WorldToViewportPoint(Coords.World(x, z, y));
                    var mid = new Vector2(p.x, (p.y + (1 - feet.y) * h) * 0.5f);
                    if (Vector2.Distance(mid, pointer) < HoverRadius) on = true;
                }
                bool contact = mine && _contactAt.TryGetValue(id, out int ct) && st.Tick - ct <= ContactTicks;
                bool arrived = mine && _raisedAt.TryGetValue(id, out int rt) && st.Tick - rt <= ArrivedTicks;
                bool show = any && sx > -40 && sx < w + 40 && (on || contact || arrived || ShowAll || (mine && id == selected));
                if (!show) { if (tag != null) tag.style.display = DisplayStyle.None; continue; }
                if (tag == null)
                {
                    tag = new Label { name = $"squad-tag-{id}", pickingMode = PickingMode.Ignore };
                    tag.AddToClassList("squad-tag");
                    tag.AddToClassList(sq.Side == Side.Us ? "us" : "vc");
                    // It follows its squad by a transform, not by its layout: moved by left and top, a tag
                    // over a squad on the march was the whole panel laid out again every frame.
                    tag.style.left = 0; tag.style.top = 0;
                    tag.usageHints = UsageHints.DynamicTransform;
                    _layer.Add(tag);
                    _tags[id] = tag;
                }
                int raised = 0;
                for (int i = 0; i < st.Men.Count; i++) if (st.Men[i].Squad == id) raised++;
                string doing = !mine ? "" : contact ? "CONTACT" : Doing(st, sq, live);
                if (!_said.TryGetValue(id, out var said) || said.live != live.Count || said.raised != raised || said.doing != doing)
                {
                    string name = CardText.SquadName(sq);
                    tag.text = mine ? $"{name} {live.Count}/{raised} · {doing}" : $"{name} · {live.Count}";
                    _said[id] = (live.Count, raised, doing);
                }
                tag.EnableInClassList("contact", contact);
                tag.style.display = DisplayStyle.Flex;
                tag.style.opacity = on || id == selected || contact ? 1f : 0.8f;
                float tw = tag.resolvedStyle.width;
                if (float.IsNaN(tw) || tw <= 0) tw = 180;
                tag.style.translate = new Translate(Mathf.Round(Mathf.Clamp(sx - tw * 0.5f, 40, w - tw - 12)), Mathf.Round(Mathf.Max(70, top - 30)));
            }
        }
    }
}
