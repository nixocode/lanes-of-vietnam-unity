using System;
using System.Collections.Generic;
using LanesOfVietnam.Sim;
using UnityEngine;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// The player's hand on the battle: pick a squad, give it an order
    /// (PLAN §5.1).
    ///
    /// Every control is a public method — <see cref="SelectAt"/>,
    /// <see cref="Cycle"/>, <see cref="Clear"/>, <see cref="Give"/> — and the
    /// keyboard and mouse only call them. UIAudit calls the same methods, so a
    /// control the audit proves is a control the player has; the three.js
    /// build's audit "cleared" a button it never clicked.
    ///
    /// Orders go to the simulation as commands, applied at the next tick
    /// boundary, so a match stays a pure function of (seed, plans, commands).
    /// </summary>
    public sealed class Commander : MonoBehaviour
    {
        public GameRoot Root;
        public Deployer Deployer;

        /// <summary>Set by the HUD: a click on the HUD is not a click on the battlefield.</summary>
        public Func<bool> HudHasPointer = () => false;

        /// <summary>The selected squad's id, or -1.</summary>
        public int Selected { get; private set; } = -1;

        /// <summary>Screen radius, in pixels at 1080p, within which a click finds a man.</summary>
        public float PickRadius = 34f;

        /// <summary>Chest height: where a man is picked, and where his ring is drawn.</summary>
        public const float Chest = 1.25f;

        /// <summary>Every action taken, for UIAudit and for anyone reading a replay.</summary>
        public readonly List<string> Log = new List<string>();

        public event Action<int> SelectionChanged;

        private Camera Cam => Root.CameraRig.Camera;
        private SimState State => Root.Driver.State;

        private void Update()
        {
            if (CaptureSettings.Active != null) return;
            // One owner per click: an armed card takes the left click (to place
            // it), the HUD takes clicks on itself, and only then is it a pick.
            bool cardInHand = Deployer != null && (Deployer.Armed != null || Deployer.DisarmedFrame == Time.frameCount);
            if (Input.GetMouseButtonDown(0) && !cardInHand && !HudHasPointer()) SelectAt(Input.mousePosition);
            if (Input.GetKeyDown(KeyCode.Tab)) Cycle(Input.GetKey(KeyCode.LeftShift) ? -1 : 1);
            // Escape puts an armed card away first; only a second press clears the selection.
            if (Input.GetKeyDown(KeyCode.Escape) && !cardInHand) Clear();
            if (Input.GetKeyDown(KeyCode.Alpha1)) Give(Order.Advance);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Give(Order.Hold);
            if (Input.GetKeyDown(KeyCode.Alpha3)) Give(Order.Bound);
            if (Input.GetKeyDown(KeyCode.Alpha4)) Give(Order.Fallback);
            if (Input.GetKeyDown(KeyCode.Alpha0)) Give(null);
            // Press and release, not "is the key down": assigning the key's state
            // every frame overwrote any other source of the glasses (UIAudit
            // caught it; a HUD button would have been dead the same way).
            if (Input.GetKeyDown(KeyCode.F))
            {
                Root.CameraRig.AimGlasses(Cam.ScreenToViewportPoint(Input.mousePosition));
                Root.CameraRig.FieldGlasses = true;
            }
            if (Input.GetKeyUp(KeyCode.F)) Root.CameraRig.FieldGlasses = false;
            if (Selected >= 0 && Squads.Roster(State, Selected).Count == 0) Clear();
        }

        /// <summary>
        /// World position of a man's chest this frame, by posture: a ring at a
        /// standing man's chest floats in the air over a man lying flat.
        /// </summary>
        public Vector3 ChestOf(int manId)
        {
            var (x, z) = Root.Driver.Position(manId);
            var m = State.Men[manId];
            float h = m.Posture == Posture.Prone ? 0.32f : m.Posture == Posture.Crouched ? 0.85f : Chest;
            return Coords.World(x, z, (float)Root.Ground.HeightAt(x, z) + h);
        }

        /// <summary>
        /// Select the squad of the man nearest a screen point, if he is one of
        /// ours and near enough. No physics: the men are a hundred points, and
        /// projecting them is cheaper and exact.
        /// </summary>
        public bool SelectAt(Vector2 screen)
        {
            float best = PickRadius * Screen.height / 1080f;
            int bestSquad = -1;
            foreach (var m in State.Men)
            {
                if (!m.Alive || m.Side != Root.PlayerSide) continue;
                var p = Cam.WorldToScreenPoint(ChestOf(m.Id));
                if (p.z <= 0) continue;
                float d = Vector2.Distance(screen, new Vector2(p.x, p.y));
                if (d < best) { best = d; bestSquad = m.Squad; }
            }
            if (bestSquad < 0) return false;
            SetSelected(bestSquad, $"select squad {bestSquad} by click");
            return true;
        }

        /// <summary>Own squads with men alive, nearest the camera first.</summary>
        public List<int> OwnSquadsByDistance()
        {
            var ids = new List<(int id, float d)>();
            foreach (var sq in State.Squads)
            {
                if (sq.Side != Root.PlayerSide) continue;
                if (Squads.Roster(State, sq.Id).Count == 0) continue;
                ids.Add((sq.Id, Mathf.Abs((float)sq.AnchorX - Root.CameraRig.X)));
            }
            ids.Sort((a, b) => a.d != b.d ? a.d.CompareTo(b.d) : a.id.CompareTo(b.id));
            return ids.ConvertAll(t => t.id);
        }

        /// <summary>
        /// Tab: the first press takes the own squad nearest the camera; each
        /// press after steps along the line (Shift steps back), so every squad
        /// is visited in turn rather than the two nearest ping-ponging as the
        /// camera follows. The camera goes to each.
        /// </summary>
        public bool Cycle(int dir = 1)
        {
            var nearest = OwnSquadsByDistance();
            if (nearest.Count == 0) return false;
            var alongLine = new List<int>(nearest);
            alongLine.Sort((a, b) => State.Squads[a].AnchorX != State.Squads[b].AnchorX
                ? State.Squads[a].AnchorX.CompareTo(State.Squads[b].AnchorX) : a.CompareTo(b));
            int i = alongLine.IndexOf(Selected);
            int next = i < 0 ? nearest[0] : alongLine[((i + dir) % alongLine.Count + alongLine.Count) % alongLine.Count];
            SetSelected(next, $"cycle to squad {next}");
            Root.CameraRig.Focus((float)State.Squads[next].AnchorX);
            return true;
        }

        /// <summary>Select a squad by id (the tactical strip, capture). Own squads only.</summary>
        public bool SelectSquad(int id)
        {
            if (id < 0 || id >= State.Squads.Count || State.Squads[id].Side != Root.PlayerSide) return false;
            SetSelected(id, $"select squad {id}");
            return true;
        }

        public void Clear()
        {
            if (Selected < 0) return;
            SetSelected(-1, "clear selection");
        }

        /// <summary>
        /// Give the selected squad an order, or hand it back to the plan (null).
        /// Returns false when there is nothing selected. Whether the squad
        /// obeys is the simulation's call: a broken squad does not take orders.
        /// </summary>
        public bool Give(Order? order)
        {
            if (Selected < 0) return false;
            Root.Driver.Match.Issue(Command.OrderSquad(Root.PlayerSide, Selected, order));
            Log.Add($"order squad {Selected}: {(order.HasValue ? order.Value.ToString() : "auto")}");
            return true;
        }

        private void SetSelected(int id, string why)
        {
            if (id == Selected) return;
            Selected = id;
            Log.Add(why);
            SelectionChanged?.Invoke(id);
        }
    }
}
