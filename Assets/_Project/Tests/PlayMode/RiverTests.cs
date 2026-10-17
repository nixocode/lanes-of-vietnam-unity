using System.Collections;
using System.Linq;
using LanesOfVietnam.Sim;
using LanesOfVietnam.View;
using LanesOfVietnam.View.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace LanesOfVietnam.Tests
{
    /// <summary>
    /// Rice Fields in the game (PLAN §12.55-56): its lanes kept clear of the HUD wherever the camera is (the owner,
    /// 2026-10-15: "the lanes are too close to the HUD ... giving proper space where needed"), its river and its bridge
    /// as the simulation has them.
    ///
    /// <code>tools/unity.sh -runTests -testPlatform PlayMode -testCategory River -testResults "$PWD/Logs/river.xml"</code>
    /// </summary>
    public class RiverTests
    {
        private const string Scene = "Assets/_Project/Scenes/Main.unity";

        /// <summary>The game on Rice Fields, as the player's Americans, playing.</summary>
        public static IEnumerator LoadRiceFields()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Scene, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("Main");
#endif
            yield return null;
            var screens = Object.FindAnyObjectByType<Screens>();
            screens.ChooseScenario(Scenarios.RiceFields);
            screens.ChooseSide(Side.Us);
            screens.Deploy();
            screens.Skip();
            UIAuditInterfaceTests.RaiseFirst(Side.Us);
            yield return null;
            Assert.AreSame(Scenarios.RiceFields, GameRoot.Instance.Scenario);
        }

        /// <summary>Where an element's edge is, as a fraction of the screen's height up from its bottom.</summary>
        private static float Up(VisualElement root, VisualElement e, bool top)
            => 1f - (top ? e.worldBound.yMin : e.worldBound.yMax) / root.worldBound.height;

        [UnityTest, Category("River")]
        public IEnumerator Its_lanes_are_clear_of_the_HUD_wherever_the_camera_is()
        {
            yield return LoadRiceFields();
            var root = GameRoot.Instance;
            root.Paused = true;
            yield return null;
            var ui = Object.FindAnyObjectByType<Hud>().GetComponent<UIDocument>().rootVisualElement;
            var bottom = ui.Q("bottombar");
            var topbar = ui.Q("topbar");
            var strip = ui.Q("strip");
            Assert.IsNotNull(bottom); Assert.IsNotNull(topbar); Assert.IsNotNull(strip);
            float floor = Up(ui, bottom, top: true), ceiling = Up(ui, topbar, top: false), stripFloor = Up(ui, strip, top: false);
            float stripL = strip.worldBound.xMin / ui.worldBound.width, stripR = strip.worldBound.xMax / ui.worldBound.width;
            var cam = root.CameraRig.Camera;
            var g = root.Ground;
            float lowest = 1f, highest = 0f;
            string worstLow = "", worstHigh = "";
            for (float x = -44; x <= 44; x += 4)
            {
                root.CameraRig.Focus(x, instant: true);
                yield return null;
                for (double dx = -14; dx <= 14; dx += 2)
                {
                    // The near lane's men's feet, and the far lane's men's heads (a man is 1.5 m at the game's size).
                    var feet = cam.WorldToViewportPoint(Coords.World(x + dx, Tune.Lanes[0], (float)g.SurfaceAt(x + dx, Tune.Lanes[0])));
                    var head = cam.WorldToViewportPoint(Coords.World(x + dx, Tune.Lanes[1], (float)g.SurfaceAt(x + dx, Tune.Lanes[1]) + 1.5f));
                    if (feet.z > 0 && feet.x > 0.03f && feet.x < 0.97f && feet.y - floor < lowest) { lowest = feet.y - floor; worstLow = $"the near lane at x {x + dx:F0} (camera at {x:F0})"; }
                    if (head.z > 0 && head.x > 0.03f && head.x < 0.97f)
                    {
                        float roof = head.x >= stripL && head.x <= stripR ? Mathf.Min(ceiling, stripFloor) : ceiling;
                        if (roof - head.y < 1f - highest) { highest = 1f - (roof - head.y); worstHigh = $"the far lane at x {x + dx:F0} (camera at {x:F0})"; }
                    }
                }
            }
            Debug.Log($"[LOV] river HUD space: the cards' top {floor:P0} up, the top bar's foot {ceiling:P0}, the strip's {stripFloor:P0}; nearest the cards {lowest:P1} over them ({worstLow}); nearest the top {1f - highest:P1} under it ({worstHigh})");
            Assert.Greater(lowest, 0.03f, $"{worstLow} is within 3% of the screen's height of the cards");
            Assert.Greater(1f - highest, 0.03f, $"{worstHigh} is within 3% of the screen's height of the top bar or the strip");
        }

        /// <summary>
        /// Each side faces the way it fights on this map (the owner, 2026-10-17: "US spawns looking at right side (make them
        /// look left as they are fighting towards the left)"): the Americans, raised in the east, face west up their lane;
        /// the VC face east.
        /// </summary>
        [UnityTest, Category("River")]
        public IEnumerator Each_side_faces_the_way_it_fights()
        {
            yield return LoadRiceFields();
            var root = GameRoot.Instance;
            var st = root.Driver.State;
            var army = Object.FindAnyObjectByType<ArmyView>();
            for (int i = 0; i < 10; i++) { root.AdvanceForCapture(0.05f); yield return null; }
            int us = 0, vc = 0, up = 0;
            foreach (var m in st.Men)
            {
                if (!m.Alive) continue;
                var f = army.FigureOf(m.Id);
                if (f == null) continue;
                // (Nobody faces back toward his own end; most face up the lane, the others the enemy across it or the
                // way they are walking to their places.)
                float ahead = f.transform.forward.x * (float)Combat.Advance(st, m.Side);
                Assert.Greater(ahead, -0.1f, $"man {m.Id} ({m.Side}, at x {m.X:F1}) faces back toward his own end");
                if (ahead > 0.5f) up++;
                if (m.Side == Side.Us) us++; else vc++;
            }
            Assert.Greater(us, 0, "no American raised to look at");
            Assert.Greater(vc, 0, "no VC to look at");
            Assert.GreaterOrEqual(up, (us + vc) * 0.7f, $"only {up} of {us + vc} men face up their lane");
        }

        [UnityTest, Category("River")]
        public IEnumerator Its_bridge_carries_the_near_lane_and_comes_down_into_the_river()
        {
            yield return LoadRiceFields();
            var root = GameRoot.Instance;
            var st = root.Driver.State;
            var g = root.Ground;
            root.Paused = true;
            yield return null;
            Assert.IsTrue(st.Water && st.Bridges.Count == 1, "the match has no bridge");
            var d = g.Decks[0];
            double mid = (d.X0 + d.X1) * 0.5;
            Assert.Greater(g.FootAt(mid, d.Z) - g.Params.RiverWater, 2.0, "the deck does not stand clear over the water");
            var span = root.River.transform.Find("bridge 0 span 0");
            Assert.IsNotNull(span, "the bridge's west half is not there");
            Assert.Less(Quaternion.Angle(span.localRotation, Quaternion.identity), 0.5f, "standing, its deck is not level");

            Assert.IsTrue(RiverView.Bring(st, 0), "the bridge did not come down");
            for (int i = 0; i < 40; i++) { root.AdvanceForCapture(0.05f); yield return null; }
            Assert.Greater(Quaternion.Angle(span.localRotation, Quaternion.identity), 15f, "down, its half did not go into the river");
            Assert.AreEqual(g.HeightAt(mid, d.Z), g.FootAt(mid, d.Z), 1e-6, "men still stand on a deck that is down");
            // The far end of each half under the water.
            var free = span.TransformPoint(new Vector3((float)(mid - d.X0), 0f, 0f) - Vector3.zero);
            Assert.Less(free.y, (float)g.Params.RiverWater + 0.3f, "its broken end is not in the river");
        }
    }
}
