using System.Collections;
using System.Linq;
using LanesOfVietnam.Sim;
using LanesOfVietnam.View;
using LanesOfVietnam.View.UI;
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
    /// UIAudit for the interface (PLAN §12.4-12.5): every element of the HUD,
    /// every card of both decks, every screen — each driven both ways, each
    /// read back from what it is supposed to change.
    /// </summary>
    public class UIAuditInterfaceTests
    {
        private const string Scene = "Assets/_Project/Scenes/Main.unity";

        public static IEnumerator LoadAndDeploy(Side side = Side.Us)
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Scene, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("Main");
#endif
            yield return null;
            var screens = Object.FindAnyObjectByType<Screens>();
            Assert.IsNotNull(screens, "no Screens in the scene");
            Assert.AreEqual(FlowState.Start, screens.State, "the game did not open on the start screen");
            screens.ChooseSide(side);
            screens.Deploy();
            Assert.AreEqual(FlowState.Opening, screens.State);
            screens.Skip();
            Assert.AreEqual(FlowState.Playing, screens.State);
            yield return null;
        }

        private static GameRoot Root => GameRoot.Instance;
        private static Hud HudOf => Object.FindAnyObjectByType<Hud>();

        [UnityTest, Category("UIAudit")]
        public IEnumerator The_hud_shows_the_match()
        {
            yield return LoadAndDeploy();
            var hud = HudOf;
            var st = Root.Driver.State;
            yield return null;

            // Morale bars: lit to morale, and they move when morale does.
            Assert.AreEqual(MoraleBar.Segments, hud.UsMorale.Lit, "a full-morale side should light every segment");
            st.Morale[(int)Side.Us] = 0.5;
            yield return null;
            Assert.AreEqual(7, hud.UsMorale.Lit, "the US bar did not follow morale");
            st.Morale[(int)Side.Vc] = 0.25;
            yield return null;
            Assert.AreEqual(4, hud.VcMorale.Lit, "the VC bar did not follow morale");

            // The tactical strip draws the men, and its window follows the camera.
            Root.Driver.FastForward(40);
            yield return null; yield return null;
            Assert.Greater(hud.Strip.PipsDrawn, 0, "the strip drew no men");
            Root.CameraRig.Focus(-30, instant: true);
            yield return null;
            Assert.AreEqual(-30f, hud.Strip.CameraX, 0.01f, "the strip's window did not follow the camera");

            // CP counts up.
            string cp0 = hud.CpValue.text;
            Root.Driver.FastForward(60);
            yield return null;
            Assert.AreNotEqual(cp0, hud.CpValue.text, "the CP counter did not move as CP accrued");

            // The lane diamonds take the colour of whoever holds the lane.
            foreach (var m in st.Men) if (m.Side == Side.Vc) m.Alive = false;
            yield return null;
            Assert.IsTrue(hud.Diamonds.All(d => d.ClassListContains("us")), "with no VC standing, the US holds both lanes");
        }

        [UnityTest, Category("UIAudit")]
        public IEnumerator Every_hud_button_toggles_and_shows_it()
        {
            yield return LoadAndDeploy();
            var hud = HudOf;
            var b = hud.Buttons;

            Assert.IsFalse(Root.Paused);
            hud.TogglePause(); yield return null;
            Assert.IsTrue(Root.Paused, "pause did not pause");
            Assert.AreEqual("▶", b["pause"].text);
            Assert.IsTrue(b["pause"].ClassListContains("on"));
            hud.TogglePause(); yield return null;
            Assert.IsFalse(Root.Paused);
            Assert.AreEqual("❚❚", b["pause"].text);

            hud.CycleSpeed(); yield return null;
            Assert.AreEqual(2f, Root.Speed); Assert.AreEqual("2×", b["speed"].text);
            hud.CycleSpeed(); hud.CycleSpeed(); yield return null;
            Assert.AreEqual(1f, Root.Speed, "speed did not come back round to 1×"); Assert.AreEqual("1×", b["speed"].text);

            foreach (var id in new[] { "snd", "mus" })
            {
                bool before = b[id].ClassListContains("on");
                using (var e = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { e.target = b[id]; b[id].SendEvent(e); }
                yield return null;
                Assert.AreNotEqual(before, b[id].ClassListContains("on"), $"{id} did not toggle when pressed");
                using (var e = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { e.target = b[id]; b[id].SendEvent(e); }
                yield return null;
                Assert.AreEqual(before, b[id].ClassListContains("on"), $"{id} did not toggle back");
            }

            var screens = Object.FindAnyObjectByType<Screens>();
            using (var e = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { e.target = b["settings"]; b["settings"].SendEvent(e); }
            yield return null;
            Assert.AreEqual(FlowState.Paused, screens.State, "the settings button did not open settings");
            Assert.IsTrue(Root.Paused, "settings open but the match is running");
            screens.Resume(); yield return null;
            Assert.AreEqual(FlowState.Playing, screens.State);
        }

        [UnityTest, Category("UIAudit")]
        public IEnumerator Every_card_of_both_decks_arms_places_and_changes_the_simulation()
        {
            foreach (var side in new[] { Side.Us, Side.Vc })
            {
                yield return LoadAndDeploy(side);
                var hud = HudOf;
                var dep = Object.FindAnyObjectByType<Deployer>();
                var st = Root.Driver.State;
                Root.Driver.FastForward(20);
                foreach (var card in Deck.For(side))
                {
                    var view = hud.Cards.Single(c => c.Card == card);
                    st.Cp[(int)side] = 500;
                    foreach (var m in st.Men) if (m.Alive) m.Pin = 0.6;     // so a medevac has pin to take away
                    yield return null;
                    Assert.IsFalse(view.Poor, $"{card.Id} dimmed with 500 CP");

                    Assert.IsTrue(dep.Arm(card), $"{card.Id} would not arm");
                    yield return null;
                    Assert.IsTrue(view.IsArmed, $"{card.Id} armed but its card does not show it");

                    int men = st.Men.Count, areaEvents = st.Events.Count(e => e.Kind == EventKind.AreaStart);
                    double morale = st.Morale[(int)side], pin = st.Men.Where(m => m.Alive && m.Side == side).Sum(m => m.Pin);
                    Assert.IsTrue(dep.Place(1, side == Side.Us ? 10 : -10), $"{card.Id} would not place");
                    Root.Driver.FastForward(1);
                    yield return null;

                    Assert.AreEqual(500 - card.Cost, st.Cp[(int)side], 1.0, $"{card.Id}: CP not spent");
                    bool changed = st.Men.Count > men
                                   || st.Events.Count(e => e.Kind == EventKind.AreaStart) > areaEvents
                                   || st.Morale[(int)side] > morale
                                   || st.Men.Where(m => m.Alive && m.Side == side).Sum(m => m.Pin) < pin - 0.1;
                    Assert.IsTrue(changed, $"{card.Id} took its CP and changed nothing in the simulation");
                    Assert.Greater(view.Cooling, 0f, $"{card.Id} shows no cooldown after being played");
                    Assert.IsFalse(view.IsArmed, $"{card.Id} still armed after placing");

                    // Poor: with no CP the card dims.
                    st.Cp[(int)side] = 0;
                    yield return null;
                    Assert.IsTrue(view.Poor, $"{card.Id} not dimmed with 0 CP");
                }
            }
        }

        [UnityTest, Category("UIAudit")]
        public IEnumerator Field_orders_advance_on_what_the_player_does()
        {
            yield return LoadAndDeploy(Side.Vc);
            var hud = HudOf;
            var dep = Object.FindAnyObjectByType<Deployer>();
            var st = Root.Driver.State;
            yield return null;
            StringAssert.StartsWith("ORDERS 1/5", hud.OrdersLine.text);

            st.Cp[(int)Side.Vc] = 500;
            dep.Arm(Deck.Find(Side.Vc, "vc-cell"));
            dep.Place(0, -30);
            Root.Driver.FastForward(1);
            yield return null;
            StringAssert.StartsWith("ORDERS 2/5 — SET PUNJI STAKES IN THEIR PATH [Q]", hud.OrdersLine.text,
                "deploying a cell did not complete the first order");

            dep.Arm(Deck.Find(Side.Vc, "vc-punji"));
            dep.Place(0, 0);
            Root.Driver.FastForward(1);
            yield return null;
            StringAssert.StartsWith("ORDERS 3/5", hud.OrdersLine.text, "setting punji did not complete the second order");
        }

        [UnityTest, Category("UIAudit")]
        public IEnumerator The_screens_from_start_to_ending_and_the_replay_is_the_same_match()
        {
            yield return LoadAndDeploy(Side.Us);
            var screens = Object.FindAnyObjectByType<Screens>();

            // Settings: open, change quality both ways, resume.
            screens.OpenSettings(); yield return null;
            Assert.AreEqual(FlowState.Paused, screens.State);
            Assert.AreEqual(UnityEngine.UIElements.DisplayStyle.Flex, screens.SettingsPanel.resolvedStyle.display);
            screens.SetQuality(QualityTier.Low); yield return null;
            Assert.Less(GameSettings.RuntimeRenderScale, 0.8f, "low quality did not lower the render scale");
            screens.SetQuality(QualityTier.High); yield return null;
            Assert.AreEqual(1f, GameSettings.RuntimeRenderScale, 0.001f);
            screens.SetVolume("music", 0.25f);
            Assert.AreEqual(0.25f, Root.Settings.Music, 0.001f, "the music slider changed nothing");
            screens.Resume(); yield return null;
            Assert.AreEqual(FlowState.Playing, screens.State);
            Assert.IsFalse(Root.Paused);

            // Play the match out with an order and a card, then the ending. The
            // CP is earned, not written: anything done to the state outside the
            // command log is, correctly, not in the replay — the first version
            // of this test set CP by hand and the replay could not afford the
            // artillery the original bought.
            var cmd = Object.FindAnyObjectByType<Commander>();
            Root.Driver.FastForward(200);
            cmd.Cycle(); cmd.Give(Order.Hold);
            var smoke = Deck.Find(Side.Us, "us-smoke");
            for (int i = 0; i < 4000 && Root.Driver.State.Cp[(int)Side.Us] < smoke.Cost; i++) Root.Driver.FastForward(1);
            Assert.GreaterOrEqual(Root.Driver.State.Cp[(int)Side.Us], smoke.Cost, "never had 10 CP to spend");
            var dep = Object.FindAnyObjectByType<Deployer>();
            dep.Arm(smoke); dep.Place(1, 10);
            Root.Driver.FastForward(Tune.MaxTicks);
            var st = Root.Driver.State;
            Assert.IsTrue(st.Over, "the match did not end inside the cap");
            uint finalHash = Parity.Hash(st, 0);
            int finalTick = st.Tick;
            screens.ShowEnding(st); yield return null;
            Assert.AreEqual(FlowState.Over, screens.State);
            Assert.AreEqual(st.Winner == Side.Us ? "THE LINE HELD" : st.Winner == Side.Vc ? "THE WIRE IS BREACHED" : "NEITHER SIDE BROKE",
                            screens.Outcome);
            StringAssert.Contains("CARDS PLAYED 1", screens.Report.text, "the after-action report missed the card played");
            StringAssert.Contains("ORDERS GIVEN 1", screens.Report.text);

            // The replay: the same seed and command log must be the same match,
            // to the bit, at the same tick.
            screens.WatchReplay(); yield return null;
            Assert.IsTrue(Root.Driver.IsReplay);
            Assert.AreEqual(FlowState.Playing, screens.State);
            Assert.IsFalse(Object.FindAnyObjectByType<Commander>().enabled, "the player can command a replay");
            Root.Driver.FastForward(Tune.MaxTicks);
            Assert.AreEqual(finalTick, Root.Driver.State.Tick, "the replay ended on a different tick");
            Assert.AreEqual(finalHash, Parity.Hash(Root.Driver.State, 0), "the replay is not the same match");

            screens.ToMenu(); yield return null;
            Assert.AreEqual(FlowState.Start, screens.State);
            Assert.IsFalse(HudOf.Visible, "the HUD shows over the start screen");
        }
    }
}
