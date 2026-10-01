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
    /// The 3D men (PLAN §12.3): one skinned figure per man, standing where the
    /// sim puts him and in the posture it gives him, moving on match time.
    ///
    /// <code>tools/unity.sh -runTests -testPlatform PlayMode -testCategory Soldiers -testResults "$PWD/Logs/soldiers.xml"</code>
    /// </summary>
    public class SoldierTests
    {
        private static GameRoot Root => GameRoot.Instance;

        [UnityTest, Category("Soldiers")]
        public IEnumerator Every_man_is_a_3D_figure_standing_where_the_sim_puts_him()
        {
            yield return UIAuditInterfaceTests.LoadAndDeploy();
            var army = Root.ArmyView;
            Assert.Greater(army.UsFigures.Length, 1, "no US figure prefabs: run SoldierBuilder.Build, then the scene builder");
            Assert.Greater(army.VcFigures.Length, 1, "no VC figure prefabs");
            Root.Paused = true;
            Root.Driver.FastForward(700);                  // into the fight: men standing, kneeling, down, dead
            yield return null;
            yield return null;
            var st = Root.Driver.State;
            int dead = 0, lying = 0, up = 0;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                var f = army.FigureOf(i);
                Assert.IsNotNull(f, $"man {i} has no figure");
                var (x, z) = Root.Driver.Position(i);
                var at = Coords.World(x, z, (float)Root.Ground.HeightAt(x, z));
                Assert.Less(Vector3.Distance(f.transform.position, at), 0.01f, $"man {i} is not where the sim has him");
                if (!m.Alive) { dead++; continue; }
                var hips = f.Animator.GetBoneTransform(HumanBodyBones.Hips).position.y - at.y;
                var head = f.Animator.GetBoneTransform(HumanBodyBones.Head).position.y - at.y;
                if (m.Posture == Posture.Prone) { lying++; Assert.Less(head, 0.75f, $"man {i} is prone with his head {head:F2} m up"); }
                else
                {
                    up++;
                    Assert.Greater(head, hips + 0.35f, $"man {i} ({m.Posture}, {(army.FigureOf(i) != null ? army.FigureOf(i).State : "-")}, moving {Root.Driver.LastStep(i)}): head {head:F2} m, hips {hips:F2} m — not upright");
                    Assert.That(hips, Is.InRange(0.3f, 1.2f), $"man {i} ({m.Posture}): hips {hips:F2} m above the ground");
                }
            }
            Debug.Log($"[LOV] soldiers: {st.Men.Count} figures ({up} up, {lying} prone, {dead} dead)");
            Assert.Greater(up, 0);
        }

        [UnityTest, Category("Soldiers")]
        public IEnumerator Paused_men_are_still_and_the_match_moves_them()
        {
            yield return UIAuditInterfaceTests.LoadAndDeploy();
            Root.Paused = true;
            Root.Driver.FastForward(300);
            yield return null;
            yield return null;
            var army = Root.ArmyView;
            int id = Enumerable.Range(0, Root.Driver.State.Men.Count).First(i => Root.Driver.State.Men[i].Alive);
            var foot = army.FigureOf(id).Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var before = foot.localRotation;
            for (int k = 0; k < 10; k++) yield return null;
            Assert.Less(Quaternion.Angle(before, foot.localRotation), 0.01f, "a paused man moved");
            Root.Paused = false;
            float moved = 0;
            // Somebody's feet move once the match runs.
            var feet = Enumerable.Range(0, Root.Driver.State.Men.Count).Select(i => army.FigureOf(i))
                .Where(f => f != null).Select(f => f.Animator.GetBoneTransform(HumanBodyBones.LeftFoot)).ToArray();
            var rots = feet.Select(t => t.localRotation).ToArray();
            for (int k = 0; k < 20; k++) yield return null;
            for (int k = 0; k < feet.Length; k++) moved = Mathf.Max(moved, Quaternion.Angle(rots[k], feet[k].localRotation));
            Assert.Greater(moved, 1f, "nobody's feet moved while the match ran");
        }

        /// <summary>
        /// §12.3's risk: skinned men in WebGL. Measured here in the Editor, on
        /// the CPU side only (Animators, IK); the WebGL figure is this times the
        /// Editor-to-WebAssembly factor, and skinning comes on top.
        /// </summary>
        [UnityTest, Category("Soldiers")]
        public IEnumerator The_men_cost_this_much_to_animate()
        {
            yield return UIAuditInterfaceTests.LoadAndDeploy();
            Root.Driver.FastForward(500);
            yield return null;
            var army = Root.ArmyView;
            float sum = 0, worst = 0;
            int n = 0;
            for (int k = 0; k < 120; k++)
            {
                yield return null;
                if (k < 20) continue;
                sum += army.LastDrawMs; worst = Mathf.Max(worst, army.LastDrawMs); n++;
            }
            int alive = Root.Driver.State.Men.Count(m => m.Alive);
            Debug.Log($"[LOV] soldiers: {Root.Driver.State.Men.Count} figures ({alive} alive) step in {sum / n:F2} ms a frame (worst {worst:F2}), " +
                      $"{sum / n / Mathf.Max(1, alive) * 1000:F0} us a live man");
        }
    }
}
