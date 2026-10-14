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

        /// <summary>
        /// The SMOKE card, played as the player plays it (armed on the HUD, placed by the Deployer): a screen in the
        /// simulation where it was placed, and smoke drawn there. The owner, 2026-10-14: "the one on the HUD, next to
        /// airstrike, does not work and hasn't for a while".
        /// </summary>
        [UnityTest, Category("CallIns")]
        public IEnumerator The_smoke_card_puts_a_screen_where_it_is_placed()
        {
            yield return UIAuditInterfaceTests.LoadAndDeploy();
            var combat = Object.FindAnyObjectByType<CombatView>();
            var deployer = Object.FindAnyObjectByType<Deployer>();
            Root.Paused = true;
            var st = Root.Driver.State;
            st.Cp[(int)Side.Us] = 1000;
            var card = Deck.Find(Side.Us, "us-smoke");
            Assert.IsNull(Deck.Blocked(st, Side.Us, card), "the smoke card cannot be played");
            Assert.IsTrue(deployer.Arm(card), "the smoke card cannot be picked up");
            const double x = 0;
            Assert.IsTrue(deployer.Place(1, x), "the smoke card was not placed");
            yield return Ticks(2);
            var screen = st.Areas.FirstOrDefault(a => a.Kind == AreaKind.Smoke && a.Side == Side.Us);
            Assert.IsNotNull(screen, "no screen in the simulation");
            Assert.AreEqual(x, screen.X, 0.5);
            Assert.AreEqual(Tune.Lanes[1], screen.Z, 0.01);
            Root.CameraRig.Focus((float)x, instant: true);
            int before = combat.Puffs;
            yield return Ticks(Tune.SmokeWait + 10);
            Assert.AreEqual(0, screen.Wait, "the screen never landed");
            Debug.Log($"[LOV] smoke card: puffs {before} before, {combat.Puffs} after; screen at {screen.X:F1},{screen.Z:F1} radius {screen.Radius} ticks {screen.Ticks}");
            Assert.Greater(combat.Puffs, before + 8, "the screen is in the simulation and no smoke is drawn for it");
        }

        /// <summary>
        /// The radio man (the owner, 2026-10-14, yes to the clip and the set): one man of each US squad carries a
        /// PRC-77 on his back, and when his side calls, the radio man nearest where it is called to puts the handset
        /// to his ear for a few seconds, his legs and his rifle hand doing what they were.
        /// </summary>
        [UnityTest, Category("CallIns")]
        public IEnumerator The_radio_man_nearest_a_call_puts_the_handset_to_his_ear()
        {
            yield return UIAuditInterfaceTests.LoadAndDeploy();
            var army = Root.ArmyView;
            Root.Paused = true;
            yield return Ticks(40);
            var st = Root.Driver.State;
            var rto = st.Men.FirstOrDefault(m => m.Alive && ArmyView.RadioMan(st, m));
            Assert.IsNotNull(rto, "no US squad has a radio man");
            var f = army.FigureOf(rto.Id);
            Assert.IsTrue(f.WearsRadio, "the radio man carries no set");
            var set = f.GetComponentsInChildren<MeshRenderer>(true).First(r => r.name == "radio");
            Assert.IsTrue(set.enabled && set.gameObject.activeInHierarchy, "the set is not drawn");
            var chest = f.Chest.position;
            Debug.Log($"[LOV] radio man {rto.Id}: chest {chest}, set {set.bounds.center} ({set.bounds.size}), facing {f.transform.forward}");
            // (The set's own middle: its bounds take in the metre of antenna above it.)
            var middle = set.transform.position;
            Assert.Less(Vector3.Distance(middle, chest), 0.4f, "the set is not on his back");
            Assert.Less(Vector3.Dot(middle - chest, f.transform.forward), -0.05f, "the set is in front of him, not on his back");
            Assert.IsFalse(st.Men.Any(m => m.Side == Side.Vc && army.FigureOf(m.Id) != null && army.FigureOf(m.Id).WearsRadio), "a VC man carries a PRC-77");

            st.Cp[(int)Side.Us] = 1000;
            Root.Driver.Match.Issue(Command.Buy(Side.Us, "us-smoke", rto.Z > 0 ? 0 : 1, rto.X + 15));
            yield return Ticks(12);
            Assert.AreEqual(rto.Id, army.Talking, "the radio man nearest the call is not the one on the radio");
            Assert.Greater(f.OnTheRadio, 0.5f, "his handset is not up");
            var handset = f.GetComponentsInChildren<MeshRenderer>(true).First(r => r.name == "handset");
            Assert.IsTrue(handset.gameObject.activeInHierarchy, "the handset is not in his hand");
            Assert.Less(Vector3.Distance(handset.bounds.center, f.Head.position), 0.35f, "the handset is not at his head");
            yield return Ticks((int)(ArmyView.CallSeconds * Tune.TickHz) + 20);
            Assert.Less(f.OnTheRadio, 0.5f, "he is still on the radio long after the call went out");
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
