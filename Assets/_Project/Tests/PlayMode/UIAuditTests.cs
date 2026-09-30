using System.Collections;
using System.Linq;
using LanesOfVietnam.Sim;
using LanesOfVietnam.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace LanesOfVietnam.Tests
{
    /// <summary>
    /// UIAudit (PLAN §7): drive every control, twice, and assert that each
    /// press changed something the player can see or the simulation acts on.
    ///
    /// Brief §9 finding 7: dead controls are the default failure. The 2D game
    /// shipped a dead message, a dead timer and a dead win condition; the
    /// three.js build shipped eight cards that did nothing and an audit that
    /// cleared a button it never clicked. So every assertion here reads the
    /// state *after* the control, from the thing it is supposed to change —
    /// the simulation's squad for an order, the lens for the glasses — never
    /// from the control's own bookkeeping.
    ///
    /// <code>tools/unity.sh -runTests -testPlatform PlayMode -testCategory UIAudit -testResults "$PWD/Logs/uiaudit.xml"</code>
    /// </summary>
    public class UIAuditTests
    {
        private const string Scene = "Assets/_Project/Scenes/Main.unity";

        /// <summary>Into a match, past the start screen and the opening, as a player gets there.</summary>
        private static IEnumerator Load()
        {
            yield return UIAuditInterfaceTests.LoadAndDeploy();
            Assert.IsNotNull(GameRoot.Instance, "GameRoot did not start");
        }

        /// <summary>Step the match without waiting on the clock.</summary>
        private static void Ticks(int n) => GameRoot.Instance.Driver.FastForward(n);

        private static Commander Cmd => Object.FindAnyObjectByType<Commander>();

        [UnityTest, Category("UIAudit")]
        public IEnumerator Selection_by_click_tab_and_escape()
        {
            yield return Load();
            var root = GameRoot.Instance;
            var cmd = Cmd;
            Assert.IsNotNull(cmd, "no Commander in the scene");
            Ticks(20);
            yield return null;

            // Click: on a man of ours, as projected on screen.
            var man = root.Driver.State.Men.First(m => m.Alive && m.Side == root.PlayerSide);
            root.CameraRig.Focus((float)man.X, instant: true);
            yield return null;
            var p = root.CameraRig.Camera.WorldToScreenPoint(cmd.ChestOf(man.Id));
            Assert.IsTrue(cmd.SelectAt(new Vector2(p.x, p.y)), "clicking a man of ours selected nothing");
            Assert.AreEqual(man.Squad, cmd.Selected, "the click selected the wrong squad");
            yield return null;
            var rings = Object.FindAnyObjectByType<SelectionRings>();
            Assert.AreEqual(Squads.Roster(root.Driver.State, man.Squad).Count, rings.Shown, "rings shown != men in the squad");

            // Click on empty sky selects nothing and keeps the selection.
            Assert.IsFalse(cmd.SelectAt(new Vector2(Screen.width * 0.5f, Screen.height - 2)));
            Assert.AreEqual(man.Squad, cmd.Selected);

            // Escape, twice: clears, then stays cleared.
            cmd.Clear();
            yield return null;
            Assert.AreEqual(-1, cmd.Selected);
            Assert.AreEqual(0, rings.Shown, "rings still drawn after the selection was cleared");

            // Tab, twice: each press selects a squad of ours and moves the camera to it.
            float x0 = root.CameraRig.X;
            Assert.IsTrue(cmd.Cycle());
            int first = cmd.Selected;
            Assert.GreaterOrEqual(first, 0);
            Assert.AreEqual(root.PlayerSide, root.Driver.State.Squads[first].Side);
            // Wait in seconds, not frames: headless frames run in a fraction of
            // a millisecond, and "90 frames" was 20 ms of glide.
            yield return new WaitForSecondsRealtime(0.8f);
            int own = cmd.OwnSquadsByDistance().Count;
            Assert.IsTrue(cmd.Cycle());
            if (own > 1) Assert.AreNotEqual(first, cmd.Selected, "a second Tab stayed on the same squad");
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.AreNotEqual(x0, root.CameraRig.X, "Tab did not move the camera");
        }

        [UnityTest, Category("UIAudit")]
        public IEnumerator Every_order_reaches_the_simulation()
        {
            yield return Load();
            var root = GameRoot.Instance;
            var cmd = Cmd;
            Ticks(20);
            Assert.IsTrue(cmd.Cycle());
            int sq = cmd.Selected;
            var rings = Object.FindAnyObjectByType<SelectionRings>();

            // Each order, then auto — and each one read back from the sim's
            // own squad after the tick that applies it.
            foreach (Order? o in new Order?[] { Order.Hold, Order.Advance, Order.Bound, Order.Fallback, null, Order.Hold })
            {
                Assert.IsTrue(cmd.Give(o));
                Ticks(1);
                var squad = root.Driver.State.Squads[sq];
                // Fallback is honoured and then the squad may break; any other
                // refusal would be a dead key.
                Assert.AreEqual(o, squad.PlayerOrder, $"order {o?.ToString() ?? "auto"} did not reach squad {sq}");
                if (o.HasValue) Assert.AreEqual(o.Value, squad.Order, $"squad {sq} is not following {o}");
                yield return null;
                Assert.AreEqual(o.HasValue ? SelectionRings.PlayerOrdered : SelectionRings.PlanDecided, rings.ShownColor,
                                "the ring does not show who decided the order");
            }

            // An order with nothing selected is refused, not silently dropped.
            cmd.Clear();
            Assert.IsFalse(cmd.Give(Order.Hold));
        }

        [UnityTest, Category("UIAudit")]
        public IEnumerator Camera_pan_dolly_and_field_glasses()
        {
            yield return Load();
            var rig = GameRoot.Instance.CameraRig;
            var cam = rig.Camera;

            // Pan, both ways.
            rig.Focus(-20, instant: true);
            yield return null;
            float a = cam.transform.position.x;
            rig.Focus(20, instant: true);
            yield return null;
            Assert.Greater(cam.transform.position.x, a + 30, "pan did not move the camera");

            // Dolly, in and back out.
            float z0 = cam.transform.position.z;
            rig.SetDolly(1, instant: true);
            yield return null;
            Assert.Greater(cam.transform.position.z, z0 + 5, "dolly did not bring the camera in");
            rig.SetDolly(0, instant: true);
            yield return null;
            Assert.AreEqual(z0, cam.transform.position.z, 0.01f);

            // Field glasses, up and down: the lens narrows toward the aim and returns.
            Assert.AreEqual(Coords.Camera.Fov, cam.fieldOfView, 0.01f);
            rig.AimGlasses(new Vector2(0.75f, 0.5f));
            rig.FieldGlasses = true;
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.Less(cam.fieldOfView, 8f, "the glasses did not narrow the lens");
            Assert.Greater(cam.transform.forward.x, 0.02f, "the glasses did not turn toward the aim");
            rig.FieldGlasses = false;
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.AreEqual(Coords.Camera.Fov, cam.fieldOfView, 0.01f, "the lens did not come back");
        }
    }
}
