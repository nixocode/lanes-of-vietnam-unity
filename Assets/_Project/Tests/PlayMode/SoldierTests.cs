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
                    Assert.Greater(head, hips + 0.35f * ArmyView.FigureSize, $"man {i} ({m.Posture}, {(army.FigureOf(i) != null ? army.FigureOf(i).State : "-")}, moving {Root.Driver.LastStep(i)}): head {head:F2} m, hips {hips:F2} m — not upright");
                    Assert.That(hips, Is.InRange(0.3f * ArmyView.FigureSize, 1.2f * ArmyView.FigureSize), $"man {i} ({m.Posture}): hips {hips:F2} m above the ground");
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
                Assert.AreEqual(22, proto.Deaths, $"{proto.name}: eleven deaths and their mirrors");
                Assert.IsTrue(proto.MirroredDeaths, $"{proto.name}: the deaths by circumstance have no mirrors");
                Assert.AreEqual(SoldierFigure.BlastCode, proto.DeathCode(2, 0, SoldierFigure.Fall.Blast));
                Assert.AreEqual(SoldierFigure.BlastCode + SoldierFigure.MirroredCode, proto.DeathCode(3, 0, SoldierFigure.Fall.Blast));
                Assert.AreEqual(9, proto.Limbs.Length, $"{proto.name}: a forearm, an arm, a lower leg and a leg, each side, and his head");
                Assert.AreEqual(proto.Limbs.Length, proto.LimbNames.Length, $"{proto.name}: a part with no name");
                Assert.Contains("head", proto.LimbNames, $"{proto.name}: his head cannot come off");
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
                    Assert.IsTrue(f.Sever("lowerarm_l", new Vector3(3f, 4f, 0.5f)), "nothing came off him");
                    Assert.IsFalse(f.Sever("lowerarm_l", Vector3.up), "the same forearm came off him twice");
                    Assert.IsFalse(f.Sever("upperarm_l", Vector3.up), "the arm came off after its own forearm");
                    Assert.IsTrue(f.Sever("head", new Vector3(2f, 5f, -1f)), "a burst this near did not take his head");
                    Assert.IsTrue(f.Sever("calf_r", new Vector3(1f, 4f, 2f)), "nor a leg");
                    Assert.IsFalse(f.Sever("calf_l", Vector3.up), "a fourth part of one man");
                    Assert.AreEqual(SoldierFigure.MostOff, f.Severed);
                    Assert.IsTrue(f.Wound(Vector3.right, 0.12f, 3), "a burst's splinters left no mark on him");
                }
                else
                {
                    // A round through him: where it went in and where it came out, on his trunk and not on the arms
                    // across it; three of them at most.
                    Assert.IsTrue(f.Wound(Vector3.left, 0.08f, 12), "a round left no mark on him");
                    Assert.IsTrue(f.Wound(Vector3.right, 0.08f, 21), "nor one from behind");
                    Assert.IsTrue(f.Wound(Vector3.forward, 0.08f, 30), "nor one from his side");
                    Assert.IsFalse(f.Wound(Vector3.left, 0.08f, 5), "a fourth wound on one man");
                    Assert.AreEqual(SoldierFigure.MostWounds, f.Wounds);
                    var marks = f.GetComponentsInChildren<MeshFilter>().Where(m => m.name == "wound").ToArray();
                    Assert.AreEqual(4 * SoldierFigure.MostWounds, marks.Length, "a hole in, and a torn one out, for each");
                    foreach (string arm in new[] { "upperarm_l", "upperarm_r" })
                    {
                        var bone = f.Limbs[System.Array.IndexOf(f.LimbNames, arm)].Bone;
                        Assert.IsFalse(marks.Any(m => m.transform.IsChildOf(bone)), $"a wound's patch on his {arm}");
                    }
                    Assert.IsTrue(marks.All(m => (m.transform.position - f.Hips.position).magnitude < 0.9f), "a wound's patch off him");
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
            // Lying still, his wounds are one mesh with him (the patches go at the end of the frame).
            yield return null;
            Assert.AreEqual(0, shot.GetComponentsInChildren<MeshFilter>().Count(m => m.name == "wound"), "his wounds are still things by themselves");
            Assert.AreEqual(1, shot.GetComponentsInChildren<MeshFilter>().Count(m => m.name == "wounds" && m.sharedMesh.vertexCount > 100), "his wounds went when he lay still");
            Assert.IsTrue(blown.TryLimb(out var limb), "his limb never landed");
            for (int k = 0; k < SoldierFigure.MostOff; k++)
            {
                Assert.IsTrue(blown.TryLimb(k, out var part), $"part {k} of him never landed");
                Assert.That(part.y, Is.InRange(50.02f, 50.2f), $"part {k} of him is not on the ground");
                Assert.IsTrue(blown.TryStump(k, out _), $"part {k} came off nowhere");
            }
            Assert.That(limb.y, Is.InRange(50.02f, 50.12f), "his forearm is not on the ground");
            Assert.Greater(new Vector2(limb.x - stood.x, limb.z - stood.z).magnitude, 1f, "his limb lies where he stood");
            Assert.Less(blown.Limbs[0].Bone.lossyScale.x, 0.01f, "the limb is still on his body");
            Object.Destroy(shot.gameObject);
            Object.Destroy(blown.gameObject);
        }

        /// <summary>
        /// The owner, playtest 9: "need more walk variations", and more deaths. Every body has three walks for the
        /// march and three for the double, each near enough the simulation's pace to be played at its own stride,
        /// and a man's legs cover the ground he covers whichever is his; in a squad the point man walks with his
        /// rifle up and the rest do not; and a man dies by how he was killed: thrown forward by a burst behind
        /// him, down on his face when hit from behind at a run, one of two ways from a knee.
        /// </summary>
        [UnityTest, Category("Soldiers")]
        public IEnumerator Each_man_has_his_own_walk_and_dies_by_how_he_was_killed()
        {
            yield return UIAuditInterfaceTests.LoadAndDeploy();
            var army = Root.ArmyView;
            Root.Paused = true;
            foreach (var proto in army.UsFigures.Concat(army.VcFigures))
            {
                Assert.AreEqual(3, proto.MarchSpeeds.Length, $"{proto.name}: the walks for the march");
                Assert.AreEqual(3, proto.DoubleSpeeds.Length, $"{proto.name}: the walks for the double");
                foreach (float own in proto.MarchSpeeds) Assert.That(0.95f / own, Is.InRange(0.75f, 1.3f), $"{proto.name}: a march walk far off the pace of the march");
                foreach (float own in proto.DoubleSpeeds) Assert.That(1.9f / own, Is.InRange(0.7f, 1.3f), $"{proto.name}: a quick walk far off the pace of the double");
                Assert.IsTrue(proto.KneelThrowLeft, $"{proto.name}: no throw from a knee");
                Assert.AreEqual(SoldierFigure.BlastForwardCode, proto.DeathCode(2, 0, SoldierFigure.Fall.Blast, 1f), "a burst behind him does not throw him forward");
                Assert.AreEqual(SoldierFigure.BlastCode, proto.DeathCode(2, 0, SoldierFigure.Fall.Blast, -1f));
                Assert.AreEqual(SoldierFigure.RunFlatCode, proto.DeathCode(2, 0, SoldierFigure.Fall.Running, 1f), "hit from behind at a run");
                Assert.AreEqual(SoldierFigure.RunCode, proto.DeathCode(2, 0, SoldierFigure.Fall.Running, -1f));
                Assert.AreEqual(SoldierFigure.CrouchCode, proto.DeathCode(0, 1, SoldierFigure.Fall.Shot));
                Assert.AreEqual(SoldierFigure.Crouch2Code, proto.DeathCode(2, 1, SoldierFigure.Fall.Shot), "one death from a knee for every man");
            }

            var f = Object.Instantiate(army.UsFigures[0], new Vector3(0, 50, 0), Quaternion.identity);
            for (int g = 0; g < f.MarchSpeeds.Length; g++)
            {
                f.Gait = g;
                f.Settle(0.95f, 0, false, false, 0);
                for (int k = 0; k < 20; k++) f.Step(0.05f, 0.95f, 0, false, false, 0);
                Assert.AreEqual(0.95f, f.Legs, 0.02f, $"march walk {g}: his legs do not cover the ground he covers");
                Assert.AreEqual(SoldierFigure.MarchKey, f.Animator.GetFloat("Speed"), 0.01f, "on the march he is not in the march's walks");
            }
            for (int p = 0; p < f.DoubleSpeeds.Length; p++)
            {
                f.Pace = p;
                f.Settle(1.9f, 0, false, false, 0);
                for (int k = 0; k < 20; k++) f.Step(0.05f, 1.9f, 0, false, false, 0);
                Assert.AreEqual(1.9f, f.Legs, 0.02f, $"quick walk {p}: his legs do not cover the ground he covers");
            }
            Object.Destroy(f.gameObject);

            // In the match: a squad's first man has the walk with the rifle up, and nobody else in it has.
            Root.Driver.FastForward(40);
            yield return null;
            yield return null;
            var st = Root.Driver.State;
            int seen = 0;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var fig = army.FigureOf(i);
                if (fig == null) continue;
                bool point = i == 0 || st.Men[i - 1].Squad != st.Men[i].Squad;
                Assert.AreEqual(point, fig.Gait == fig.MarchSpeeds.Length - 1, $"man {i}: {(point ? "the point man has not his rifle up" : "a man behind the point man has")}");
                seen++;
            }
            Assert.Greater(seen, 3);
        }

        /// <summary>
        /// The owner, 2026-10-10: "focus on adding animations ... as realistic as possible". What a man does
        /// besides walk, fire and die: every body has each act; one that is started plays out and is over; a man
        /// who has to go or to shoot stops what he was doing (but a fall and a dive are played out); and he does
        /// not flinch the same way every time.
        /// </summary>
        [UnityTest, Category("Soldiers")]
        public IEnumerator A_man_is_knocked_over_dives_signals_and_stands_about()
        {
            yield return UIAuditInterfaceTests.LoadAndDeploy();
            var army = Root.ArmyView;
            Root.Paused = true;
            var acts = new[] { SoldierFigure.BlastFall, SoldierFigure.RubEyes, SoldierFigure.Dodge, SoldierFigure.KneelFlinch, SoldierFigure.Dive,
                               SoldierFigure.SignalFollow, SoldierFigure.SignalStay, SoldierFigure.LookAbout, SoldierFigure.Inspect };
            foreach (var proto in army.UsFigures.Concat(army.VcFigures))
                foreach (string act in acts) CollectionAssert.Contains(proto.Acts, act, $"{proto.name} has no \"{act}\"");

            var f = Object.Instantiate(army.UsFigures[0], new Vector3(0, 50, 0), Quaternion.identity);
            f.Settle(0f, 0, false, false, 0);
            Assert.IsFalse(f.Act("no such thing"));
            foreach (string act in acts)
            {
                int posture = act == SoldierFigure.KneelFlinch || act == SoldierFigure.SignalFollow || act == SoldierFigure.SignalStay ? 1 : act == SoldierFigure.Dive ? 2 : 0;
                f.Settle(0f, posture, false, false, 0);
                Assert.IsTrue(f.Act(act), act);
                Assert.AreEqual(act, f.Doing);
                float t = 0;
                for (; t < 12f && f.Doing != null; t += 0.05f) f.Step(0.05f, 0f, posture, false, false, 0);
                Assert.IsNull(f.Doing, $"\"{act}\" is never over");
                Assert.Greater(t, 0.5f, $"\"{act}\" was over as it began");
                Debug.Log($"[LOV] act \"{act}\": {t:F2} s");
            }
            // He has to go: standing about is over, a fall is not.
            f.Settle(0f, 0, false, false, 0);
            f.Act(SoldierFigure.LookAbout);
            for (int k = 0; k < 4; k++) f.Step(0.05f, 1.2f, 0, false, false, 0);
            Assert.IsNull(f.Doing, "he walks off still looking over his shoulder");
            f.Settle(0f, 0, false, false, 0);
            f.Act(SoldierFigure.BlastFall);
            for (int k = 0; k < 4; k++) f.Step(0.05f, 1.2f, 0, false, false, 0);
            Assert.AreEqual(SoldierFigure.BlastFall, f.Doing, "a man knocked over got up to walk");
            // He has to shoot: whatever it was, it is over.
            f.Fire();
            Assert.IsNull(f.Doing, "he fires from flat on his back");
            // Not the same flinch every time.
            f.Settle(0f, 0, false, false, 0);
            f.React(); for (int k = 0; k < 40; k++) f.Step(0.05f, 0f, 0, false, false, 0);
            string first = f.Doing;
            f.React();
            Assert.AreNotEqual(first, f.Doing, "the same flinch twice running");
            Object.Destroy(f.gameObject);
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
