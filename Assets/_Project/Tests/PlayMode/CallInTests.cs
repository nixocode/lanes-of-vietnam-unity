using System.Collections;
using System.Linq;
using LanesOfVietnam.Sim;
using LanesOfVietnam.View;
using LanesOfVietnam.View.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace LanesOfVietnam.Tests
{
    /// <summary>
    /// A call-in is watched arriving (PLAN §12.45; the owner, 2026-10-13: "Source the jet
    /// and Huey for stage 4"): the jet comes from behind its own side, is low over the
    /// target as its bombs burst and is gone; the helicopter comes down onto the place
    /// it was called to as the simulation's wait runs out, stands, and leaves; and
    /// while the player waits, the HUD says what is coming and in how many seconds.
    ///
    /// <code>tools/unity.sh -runTests -testPlatform PlayMode -testCategory CallIns -testResults "$PWD/Logs/callins.xml"</code>
    /// </summary>
    public class CallInTests
    {
        private static GameRoot Root => GameRoot.Instance;

        private static IEnumerator Ticks(int n)
        {
            Root.Driver.FastForward(n);
            yield return null;
            yield return null;
        }

        [UnityTest, Category("CallIns")]
        public IEnumerator The_jet_is_over_its_target_as_its_bombs_burst()
        {
            yield return UIAuditInterfaceTests.LoadAndDeploy();
            var combat = Object.FindAnyObjectByType<CombatView>();
            Assert.IsNotNull(combat.Jet, "the scene has no jet (tools/blender/aircraft_import.py, then SceneBuilder)");
            Assert.IsNotNull(combat.Aircraft);
            Root.Paused = true;
            var st = Root.Driver.State;
            Assert.IsTrue(st.Inbound, "the game plays with call-ins inbound");
            st.Cp[(int)Side.Us] = 1000;
            const double x = 20;
            Root.Driver.Match.Issue(Command.Buy(Side.Us, "us-airstrike", 0, x));
            yield return Ticks(2);
            var call = st.Events.Last(e => e.Kind == EventKind.CallIn);
            Assert.AreEqual(Inbound.Jet, call.Rounds);
            var hud = Object.FindAnyObjectByType<Hud>();
            if (CaptureSettings.Active == null)
            {
                Assert.AreEqual(DisplayStyle.Flex, hud.CallTags[0].resolvedStyle.display, "nothing says the call went out");
                StringAssert.StartsWith("AIR STRIKE", hud.CallTags[0].text);
            }

            // Coming: behind its own side of the target, and nearer each time it is looked at.
            yield return Ticks(Tune.JetWait - 30);
            Assert.IsTrue(combat.JetSeen, "a second and a half out, no jet");
            float far = combat.JetAt.position.x;
            Assert.Less(far, (float)x - 60f, "the jet is not coming from behind its own side");
            yield return Ticks(10);
            Assert.Greater(combat.JetAt.position.x, far + 30f, "the jet is not flying");
            // Over it as the bombs burst: within what it flies in half a second, and low.
            yield return Ticks(20 + (int)(CombatView.JetOver * Tune.TickHz));
            Assert.Greater(st.Events.Count(e => e.Kind == EventKind.Shell && e.Id == call.Id), 0, "no bomb has burst");
            Assert.AreEqual((float)x, combat.JetAt.position.x, CombatView.JetSpeed * 0.5f, "the jet is not over its target as its bombs burst");
            float ground = (float)Root.Ground.HeightAt(x, call.Z.Value);
            Assert.Less(combat.JetAt.position.y - ground, CombatView.JetHigh + 2f, "the jet is not low over its target");
            if (CaptureSettings.Active == null) Assert.AreEqual(DisplayStyle.None, hud.CallTags[0].resolvedStyle.display, "the call is in and the HUD still counts it");
            // And gone.
            yield return Ticks(12 * Tune.TickHz);
            Assert.IsFalse(combat.JetSeen, "twelve seconds on, the jet is still drawn");
        }

        [UnityTest, Category("CallIns")]
        public IEnumerator The_helicopter_sets_down_where_it_was_called_and_leaves()
        {
            yield return UIAuditInterfaceTests.LoadAndDeploy();
            var combat = Object.FindAnyObjectByType<CombatView>();
            Assert.IsNotNull(combat.Huey, "the scene has no helicopter");
            Assert.IsNotNull(combat.Rotor, "the helicopter's rotor is not a mesh of its own");
            Root.Paused = true;
            var st = Root.Driver.State;
            st.Cp[(int)Side.Us] = 1000;
            Root.Driver.Match.Issue(Command.Buy(Side.Us, "us-medevac", 0, -30));
            yield return Ticks(2);
            var call = st.Events.Last(e => e.Kind == EventKind.CallIn);
            Assert.AreEqual(Inbound.Dustoff, call.Rounds);
            float lx = (float)call.X.Value;
            float ground = (float)Root.Ground.HeightAt(call.X.Value, call.Z.Value);

            yield return Ticks(Tune.DustoffWait / 2);
            Assert.IsTrue(combat.HueySeen, "half way in, no helicopter");
            float high = combat.HueyAt.position.y - ground;
            Assert.Greater(high, 4f, "half way in it is already down");
            Assert.Less(combat.HueyAt.position.x, lx - 10f, "it is not coming from behind its own side");
            var rotor = combat.HueyAt.GetChild(0);
            var turned = rotor.localRotation;
            yield return Ticks(1);
            Assert.Greater(Quaternion.Angle(turned, rotor.localRotation), 5f, "its rotor is not turning");

            // Down, where it was called to, as the wait runs out.
            yield return Ticks(Tune.DustoffWait / 2 + (int)(CombatView.DustoffDown * Tune.TickHz / 2));
            Assert.AreEqual(ground, combat.HueyAt.position.y, 0.05f, "it is not on the ground when the simulation says it is in");
            Assert.AreEqual(lx, combat.HueyAt.position.x, 0.05f, "it did not set down where it was called to");
            Assert.IsFalse(st.Areas.Any(a => a.Kind == AreaKind.Relief), "the simulation's medevac is not in");
            // Up and away, back the way it came.
            yield return Ticks((int)((CombatView.DustoffDown / 2 + CombatView.DustoffAway - 1f) * Tune.TickHz));
            Assert.IsTrue(combat.HueySeen);
            Assert.Greater(combat.HueyAt.position.y - ground, 15f, "it has not lifted");
            Assert.Less(combat.HueyAt.position.x, lx - 40f, "it is not going back the way it came");
            yield return Ticks(3 * Tune.TickHz);
            Assert.IsFalse(combat.HueySeen, "it is gone and still drawn");
        }
    }
}
