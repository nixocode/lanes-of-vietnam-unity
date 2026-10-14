using System.Collections;
using System.Linq;
using LanesOfVietnam.Sim;
using LanesOfVietnam.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LanesOfVietnam.Tests
{
    /// <summary>
    /// The shot (PLAN §12.42; the owner, 2026-10-12: "the actual shots being fired
    /// need some work"): every round of a burst is a flash at the muzzle, a kick
    /// into the shoulder and a case out of the port, at the rate the weapon
    /// fires; every round is seen going; and smoke too faint to see is not drawn.
    ///
    /// <code>tools/unity.sh -runTests -testPlatform PlayMode -testCategory Shots -testResults "$PWD/Logs/shots.xml"</code>
    /// </summary>
    public class ShotTests
    {
        private static GameRoot Root => GameRoot.Instance;

        [Test, Category("Shots")]
        public void Each_weapon_fires_at_its_own_rate()
        {
            // Rounds a minute, from the seconds between two rounds.
            float Rate(Weapon w, Side s, int rounds = 1) => 60f / CombatView.RoundGap(w, s, rounds);
            Assert.AreEqual(550f, Rate(Weapon.M60, Side.Us), 1f);
            Assert.AreEqual(650f, Rate(Weapon.Rpd, Side.Vc), 1f);
            Assert.AreEqual(600f, Rate(Weapon.Ak, Side.Vc), 1f);
            Assert.AreEqual(900f, Rate(Weapon.Smg, Side.Vc), 1f, "a PPSh");
            Assert.AreEqual(450f, Rate(Weapon.Smg, Side.Us), 1f, "an M3");
            // A burst of the usual length is fired at the gun's own rate, and is over before the next one.
            foreach (var w in new[] { Weapon.M60, Weapon.Rpd, Weapon.Smg })
                foreach (var s in new[] { Side.Us, Side.Vc })
                {
                    var load = Ammo.Of(w);
                    Assert.AreEqual(Rate(w, s), Rate(w, s, load.Burst), 0.01f, $"{w}: a burst of {load.Burst} is not at the gun's rate");
                    Assert.LessOrEqual(CombatView.RoundGap(w, s, load.Burst) * load.Burst, load.Gap * (float)Tune.Dt + 1e-4f, $"{w}: a burst runs into the next");
                    Assert.LessOrEqual(CombatView.RoundGap(w, s, load.LongBurst) * load.LongBurst, load.LongGap * (float)Tune.Dt + 1e-4f, $"{w}: a long burst runs into the next");
                }
        }

        /// <summary>
        /// The smoke is soft where it meets the ground by reading the scene's depth, so the depth must be copied before
        /// the smoke is drawn: after the opaque pass, not after the transparent one. Copied after, the smoke read a
        /// depth that was not there yet: on this Mac's renderer that is "far" and the smoke showed, so no capture or
        /// test saw anything wrong, and in the browser it is "near" and every puff faded to nothing. The owner,
        /// 2026-10-14, of the SMOKE card: "does not work and hasn't for a while".
        /// </summary>
        [Test, Category("Shots")]
        public void The_depth_is_copied_before_the_smoke_is_drawn()
        {
            string renderer = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "_Project/Settings/URP-Web_Renderer.asset"));
            StringAssert.Contains("m_CopyDepthMode: 0", renderer, "the depth is copied after the transparent pass: in the browser no smoke is seen");
            string asset = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "_Project/Settings/URP-Web.asset"));
            StringAssert.Contains("m_RequireDepthTexture: 1", asset, "no depth texture: the smoke has nothing to be soft against");
        }

        [UnityTest, Category("Shots")]
        public IEnumerator A_man_is_kicked_by_every_round_of_his_burst()
        {
            yield return UIAuditInterfaceTests.LoadAndDeploy();
            var army = Root.ArmyView;
            var f = Object.Instantiate(army.UsFigures[0], new Vector3(0, 50, 0), Quaternion.identity);
            f.Settle(0f, 0, true, false, 0);
            const int rounds = 7;
            float gap = CombatView.RoundGap(Weapon.M60, Side.Us, rounds);
            f.Fire(rounds, gap);
            int kicks = 0; float was = 0f, first = -1f, last = -1f;
            const float dt = 0.005f;
            for (float t = 0; t < rounds * gap + 0.3f; t += dt)
            {
                f.Step(dt, 0f, 0, true, false, 0);
                if (f.Kick > was + 0.2f) { kicks++; if (first < 0) first = t; last = t; }
                was = f.Kick;
            }
            Assert.AreEqual(rounds, kicks, "a burst of seven rounds did not kick seven times");
            Assert.AreEqual((rounds - 1) * gap, last - first, 0.02f, "the kicks are not at the gun's rate");
            Assert.Less(f.Kick, 0.05f, "he is still being kicked after his burst is over");
            Object.Destroy(f.gameObject);
        }

        [UnityTest, Category("Shots")]
        public IEnumerator Every_round_flashes_is_seen_going_and_throws_its_case()
        {
            yield return UIAuditInterfaceTests.LoadAndDeploy();
            Root.NewMatch(Side.Us, MatchLength.Standard, MotionAuditTests.Seed);
            var st = Root.Driver.State;
            st.Player = null;                           // both sides raise their own squads: a full fight
            int was = Time.captureFramerate;
            Time.captureFramerate = 100;                // a hundredth of a second a frame: a flash is six of them
            Root.Driver.FastForward(700);
            yield return null; yield return null;
            var combat = Object.FindAnyObjectByType<CombatView>();
            Assert.IsNotNull(combat);

            int from = st.Events.Count, fromTick = st.Tick;
            long flashFrames = 0;
            int traces = 0, tracers = 0, cases = 0, faint = 0, puffs = 0, frames = 0;
            for (; frames < 100 * 15 && !st.Over; frames++)
            {
                yield return null;
                flashFrames += combat.Flashes;
                traces = Mathf.Max(traces, combat.Traces); tracers += combat.Tracers;
                cases = Mathf.Max(cases, combat.Cases); faint += combat.Faint;
                puffs = Mathf.Max(puffs, combat.Puffs);
            }
            Time.captureFramerate = was;

            // The rounds fired meanwhile, as the simulation counted them.
            int rounds = 0, shots = 0;
            for (int i = from; i < st.Events.Count; i++)
            {
                var e = st.Events[i];
                if (e.Kind != EventKind.Fire || e.Id >= st.Men.Count || e.Target == null) continue;
                rounds += CombatView.Rounds(e, st.Men[e.Id]);
                shots++;
            }
            Assert.Greater(shots, 20, "hardly anybody fired: nothing was measured");
            // A flash is on the screen for 0.06 s of the match's time, and each frame is this much of it.
            float frame = (st.Tick - fromTick) * (float)Tune.Dt / frames;
            float flashes = flashFrames * frame / 0.06f;
            Debug.Log($"[LOV] shots: {shots} shots of {rounds} rounds in {frames} frames; {flashes:F0} flashes drawn; at most {traces} rounds in the air and {cases} cases at once; "
                      + $"{tracers} tracer-frames; at most {puffs} puffs, {faint} too faint and not drawn");
            Assert.AreEqual(rounds, flashes, 0.15f * rounds, "the flashes drawn are not one a round");
            Assert.Greater(traces, 0, "no round was seen going");
            Assert.Greater(cases, 0, "no round threw its case");
        }
    }
}
