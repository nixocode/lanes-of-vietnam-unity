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

        /// <summary>
        /// PLAN §12.6: the fighting is heard. Outside a browser AudioView plays
        /// into a recorder, so what it asked for can be checked: shots are
        /// heard, never more than three new ones in a frame, each from where a
        /// man of the right side actually stands, and the bed is running.
        /// </summary>
        [UnityTest, Category("UIAudit")]
        public IEnumerator The_fighting_is_heard_rationed_and_from_where_it_happened()
        {
            yield return LoadAndDeploy();
            var audio = Object.FindAnyObjectByType<AudioView>();
            Assert.IsNotNull(audio, "no AudioView in the scene");
            var rec = audio.Out as RecordingSoundOut;
            Assert.IsNotNull(rec, "outside a browser the recorder should be listening");
            var st = Root.Driver.State;
            // Paused, so only FastForward moves the men: the check against where
            // they stand is then exact, not raced by the game's own clock.
            if (!Root.Paused) HudOf.TogglePause();
            Root.Driver.FastForward(560);          // into the fight
            yield return null;
            rec.Played.Clear();

            int shotsHeard = 0;
            for (int f = 0; f < 90; f++)
            {
                int before = rec.Played.Count;
                Root.Driver.FastForward(2);
                yield return new WaitForSecondsRealtime(0.06f);   // past the 55 ms ration
                var fresh = rec.Played.Skip(before).Where(p => p.name.StartsWith("m16") || p.name.StartsWith("ak") || p.name.StartsWith("sks")
                                                               || p.name.StartsWith("mg_") || p.name.StartsWith("smg_") || p.name.StartsWith("bolt")).ToList();
                Assert.LessOrEqual(fresh.Count, AudioView.ShotsPerTick, $"frame {f}: {fresh.Count} new shots");
                foreach (var shot in fresh)
                {
                    // Each recording is some weapons' own (Arms): the report comes from where a man carrying one of them stands.
                    string set = shot.name.Substring(0, shot.name.LastIndexOf('_'));
                    var carried = set == "m16" ? new[] { Weapon.M16 } : set == "ak" ? new[] { Weapon.Ak } : set == "sks" ? new[] { Weapon.Sks }
                                : set == "mg" ? new[] { Weapon.M60, Weapon.Rpd } : set == "smg" ? new[] { Weapon.Smg } : new[] { Weapon.Sniper };
                    Assert.IsTrue(st.Men.Any(m => carried.Contains(m.Weapon)
                                                  && System.Math.Abs(m.X - shot.x) < 0.01 && System.Math.Abs(m.Z - shot.z) < 0.01),
                                  $"a {shot.name} shot from where no man carrying that weapon stands");
                    Assert.IsFalse(shot.immediate, "a rifle report skipped its travel time");
                }
                shotsHeard += fresh.Count;
            }
            Assert.Greater(shotsHeard, 0, "a firefight made no sound");
            Assert.Greater(rec.AmbienceLevel, 0f, "the jungle bed is not running");
            var cam = Root.CameraRig.Camera.transform.position;
            Assert.AreEqual(cam.x, rec.ListenerX, 0.01f, "the listener is not at the camera");
        }

        /// <summary>
        /// The owner: "need to be able to move side to side with mouse as well
        /// as zoom". Zooming narrows the lens and brings the camera in, and
        /// back out again; panning and dragging move it along the line, the
        /// ground following the cursor.
        /// </summary>
        [UnityTest, Category("UIAudit")]
        public IEnumerator The_camera_zooms_and_pans_with_the_mouse()
        {
            yield return LoadAndDeploy();
            var rig = Root.CameraRig;
            var cam = rig.Camera;
            float fov0 = cam.fieldOfView, z0 = cam.transform.position.z, x0 = rig.X;

            rig.ZoomBy(1f);
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.Less(cam.fieldOfView, fov0 - 5f, "zooming in did not narrow the lens");
            Assert.Greater(cam.transform.position.z, z0 + 10f, "zooming in did not bring the camera in");
            rig.ZoomBy(-1f);
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.AreEqual(fov0, cam.fieldOfView, 0.3f, "zooming out did not come back");

            rig.PanBy(20f);
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.AreEqual(x0 + 20f, rig.X, 0.5f, "panning did not move the camera along the line");

            // Dragging the ground to the left moves the view to the right.
            float x1 = rig.X;
            rig.BeginDrag(new Vector2(Screen.width * 0.6f, Screen.height * 0.5f));
            rig.DragTo(new Vector2(Screen.width * 0.3f, Screen.height * 0.5f));
            rig.EndDrag();
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.Greater(rig.X, x1 + 3f, "a drag did not pan");
        }

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
            // (A side opens with one squad now, so a lane may have nobody in it: that one is nobody's.)
            for (int lane = 0; lane < hud.Diamonds.Count; lane++)
            {
                bool held = st.Men.Any(m => m.Alive && m.Side == Side.Us && st.Squads[m.Squad].Lane == lane);
                Assert.AreEqual(held, hud.Diamonds[lane].ClassListContains("us"), $"lane {lane}: with no VC standing, the US holds every lane it has men in");
                Assert.IsFalse(hud.Diamonds[lane].ClassListContains("vc"), $"lane {lane} is shown as the VC's with no VC standing");
            }
            Assert.IsTrue(hud.Diamonds.Any(d => d.ClassListContains("us")));
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

        /// <summary>
        /// Warfare 1944's lever, on every strongpoint: a plate over each piece
        /// of cover, built or natural, and pressing its lever is a simulation
        /// command that the simulation obeys. Pressed here as the player presses it.
        /// </summary>
        [UnityTest, Category("UIAudit")]
        public IEnumerator Every_position_has_a_lever_and_pulling_it_commands_the_simulation()
        {
            yield return LoadAndDeploy();
            var hud = HudOf;
            var st = Root.Driver.State;
            Assert.IsTrue(st.Fieldcraft, "the game is not playing with fieldcraft");
            var positions = st.Cover.Where(Fieldcraft.IsPosition).ToList();
            Assert.Greater(positions.Count, 3);
            Assert.AreEqual(positions.Count, hud.Positions.Plates.Count, "a position has no plate, or cover that is not one has");

            void Press(UnityEngine.UIElements.Button b)
            {
                using (var e = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { e.target = b; b.SendEvent(e); }
            }
            IEnumerator Ticks(int n)
            {
                int until = Root.Driver.State.Tick + n;
                while (Root.Driver.State.Tick < until) yield return null;
            }

            foreach (var c in positions)
            {
                var plate = hud.Positions.Of(c.Id);
                Assert.IsNotNull(plate, $"cover {c.Id} ({c.Kind}) has no plate");
                Assert.AreEqual(Lever.Auto, c.LeverUs);
                Press(plate.LeverButton);
                yield return Ticks(2);
                Assert.AreEqual(Lever.Hold, c.LeverUs, $"pressing the lever on cover {c.Id} did not hold it");
                Assert.AreEqual("HOLD", plate.LeverButton.text);
                Assert.IsTrue(plate.LeverButton.ClassListContains("hold"));
                Press(plate.LeverButton);
                yield return Ticks(2);
                Assert.AreEqual(Lever.Go, c.LeverUs, $"pressing the lever on cover {c.Id} again did not send it");
                Assert.IsTrue(plate.LeverButton.ClassListContains("go"));
                hud.Positions.Pull(c.Id, Lever.Auto);
                yield return Ticks(2);
                Assert.AreEqual(Lever.Auto, c.LeverUs);
                Assert.AreEqual(Lever.Auto, c.LeverVc, "the US player's lever moved the VC's");
            }
            Assert.AreEqual(positions.Count * 3, Root.Driver.Match.Log.Count(l => l.Command.Kind == CommandKind.Lever && l.Accepted),
                            "a lever pulled is not in the match's command log");

            // The plates keep out of the way: faint until the pointer is on one, and a crater or a bank
            // shows none at all unless it holds his men, has a lever set, or is pointed at.
            var bank = positions.First(c => c.Kind == CoverKind.Berm && c.Z > 0);
            Root.CameraRig.Focus((float)bank.X - 5f, instant: true);       // the bank and the listening post's wall both in frame
            hud.Positions.Pointer = new Vector2(-5000, -5000);
            yield return null; yield return null;
            var built = hud.Positions.Plates.First(p => Fieldcraft.Built(st.Cover[p.Cover]) && p.style.display.value == UnityEngine.UIElements.DisplayStyle.Flex);
            Assert.AreEqual(PositionPlates.Faded, built.style.opacity.value, 0.01f, "a plate nobody is pointing at is not faded");
            var natural = hud.Positions.Of(bank.Id);
            Assert.AreEqual(0, st.Men.Count(m => m.Alive && m.Side == Side.Us && m.Cover == bank.Id), "the test's bank has men in it");
            Assert.AreEqual(UnityEngine.UIElements.DisplayStyle.None, natural.style.display.value, "an empty bank with no lever set shows a plate");
            hud.Positions.Pointer = natural.Centre;
            yield return null; yield return null;
            Assert.IsTrue(natural.Hovered);
            Assert.AreEqual(UnityEngine.UIElements.DisplayStyle.Flex, natural.style.display.value, "pointing at a bank did not show its plate");
            Assert.AreEqual(1f, natural.style.opacity.value, 0.01f, "a plate under the pointer is not solid");
            hud.Positions.Pointer = built.Centre;
            yield return null; yield return null;
            Assert.AreEqual(1f, built.style.opacity.value, 0.01f);
            hud.Positions.Pointer = null;

            // Held, the forward trench fills with men and they stay; the plate counts them.
            var trench = positions.First(c => c.Kind == CoverKind.Trench && c.Z > 0);
            hud.Positions.Pull(trench.Id, Lever.Hold);
            yield return null;
            Root.Driver.FastForward(240);
            yield return null; yield return null;
            int inside = st.Men.Count(m => m.Alive && m.Side == Side.Us && m.Cover == trench.Id);
            Assert.Greater(inside, 2, "the held trench did not fill");
            StringAssert.StartsWith($"{inside}/", hud.Positions.Of(trench.Id).Count.text, "the plate does not count the men in it");
            StringAssert.Contains("lever", string.Join(" ", hud.Positions.Log));
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
                    // Room under the force cap, through the sim's own kill: buying
                    // every unit card in a row reaches the cap or not depending on
                    // how many died in this seed's match, which made this test pass
                    // or fail by the clock.
                    if (card.Pips > 0)
                        foreach (var m in st.Men.Where(m => m.Alive && m.Side == side).ToList())
                        {
                            if (Match.AliveCount(st, side) < Tune.ForceCap) break;
                            Combat.Kill(st, m);
                        }
                    yield return null;
                    Assert.IsNull(Deck.Blocked(st, side, card), $"{card.Id} blocked");
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
