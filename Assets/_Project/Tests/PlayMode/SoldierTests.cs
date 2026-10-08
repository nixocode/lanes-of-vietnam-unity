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
                // A trench is one man wide: a living man in one is drawn on its line.
                if (m.Alive && m.Cover >= 0 && Fieldcraft.Dug(st.Cover[m.Cover])) z = st.Cover[m.Cover].Z;
                var at = Coords.World(x, z, (float)Root.Ground.HeightAt(x, z));
                // A man climbing into a trench or out of one is carried over the parapet from where he
                // stood to where the sim already has him: within the climb, not on the spot.
                float slack = m.Alive && m.Vault > 0 ? 2.5f : 0.01f;
                Assert.Less(Vector3.Distance(f.transform.position, at), slack, $"man {i} is not where the sim has him");
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

        /// <summary>
        /// The owner, playtest 9: "more gore, blood and death animations. Some are too slow, should be sped up
        /// when it's an explosion + body dismemberment". Every man has each death and its mirror; a man a burst
        /// kills is thrown by it and is down well before one who is shot; and a limb it takes off him leaves
        /// his body, flies, lands and lies there, and he is not frozen until it has.
        /// </summary>
        [UnityTest, Category("Soldiers")]
        public IEnumerator A_burst_throws_a_man_fast_and_can_take_a_limb_off_him()
        {
            yield return UIAuditInterfaceTests.LoadAndDeploy();
            var army = Root.ArmyView;
            Root.Paused = true;
            foreach (var proto in army.UsFigures.Concat(army.VcFigures))
            {
                Assert.AreEqual(16, proto.Deaths, $"{proto.name}: eight deaths and their mirrors");
                Assert.IsTrue(proto.MirroredDeaths, $"{proto.name}: the deaths by circumstance have no mirrors");
                Assert.AreEqual(SoldierFigure.BlastCode, proto.DeathCode(2, 0, SoldierFigure.Fall.Blast));
                Assert.AreEqual(SoldierFigure.BlastCode + SoldierFigure.MirroredCode, proto.DeathCode(3, 0, SoldierFigure.Fall.Blast));
                Assert.AreEqual(4, proto.Limbs.Length, $"{proto.name}: a forearm and a lower leg, each side");
                Assert.IsTrue(proto.Limbs.All(l => l.Bone != null && l.Mesh != null && l.Mesh.vertexCount > 100), $"{proto.name}: a limb with no mesh to it");
            }

            (float seconds, SoldierFigure f, Vector3 stood) Dies(SoldierFigure.Fall how, bool burst)
            {
                var f = Object.Instantiate(army.UsFigures[0], new Vector3(0, 50, 0), Quaternion.identity);
                f.Settle(0f, 0, false, false, 3);
                var stood = f.Hips.position;
                if (burst)
                {
                    f.Blown(Vector3.right * 2.5f, 0.8f);
                    Assert.IsTrue(f.Sever(0, new Vector3(3f, 4f, 0.5f)), "nothing came off him");
                    Assert.IsFalse(f.Sever(1, Vector3.up), "a second limb off the same man");
                }
                float t = 0;
                while (!f.Baked && t < 12f) { f.Step(0.05f, 0f, 0, false, true, 3, how); t += 0.05f; }
                Assert.IsTrue(f.Baked, "he never came to rest");
                return (t, f, stood);
            }
            var (shotFor, shot, _) = Dies(SoldierFigure.Fall.Shot, false);
            var (burstFor, blown, stood) = Dies(SoldierFigure.Fall.Blast, true);
            Debug.Log($"[LOV] deaths: shot, down and still in {shotFor:F2} s; thrown by a burst, in {burstFor:F2} s");
            Assert.Less(burstFor, 2.6f, "a man thrown by a burst takes too long to come down");
            Assert.Less(burstFor, shotFor, "a burst's death is no quicker than a round's");
            Assert.Greater(blown.Centre.x - stood.x, 1.5f, "the burst did not throw him");
            Assert.Less(Mathf.Abs(shot.Centre.x), 1.6f, "a man who was shot was thrown");
            Assert.IsFalse(shot.TryLimb(out _), "a man who was shot lost a limb");
            Assert.IsTrue(blown.TryLimb(out var limb), "his limb never landed");
            Assert.AreEqual(50.07f, limb.y, 0.02f, "his limb is not on the ground");
            Assert.Greater(new Vector2(limb.x - stood.x, limb.z - stood.z).magnitude, 1f, "his limb lies where he stood");
            Assert.Less(blown.Limbs[0].Bone.lossyScale.x, 0.01f, "the limb is still on his body");
            Object.Destroy(shot.gameObject);
            Object.Destroy(blown.gameObject);
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
            // In seconds, not frames: headless frames run in a fraction of a millisecond.
            yield return new WaitForSecondsRealtime(0.6f);
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
