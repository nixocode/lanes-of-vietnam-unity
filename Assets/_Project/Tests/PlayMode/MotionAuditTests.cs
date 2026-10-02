using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LanesOfVietnam.Sim;
using LanesOfVietnam.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LanesOfVietnam.Tests
{
    /// <summary>
    /// The motion audit (PLAN §12.18 phase 1, §12.19): what the men's bodies do,
    /// measured frame to frame in the real scene, so "sliding" is a number.
    ///
    /// The owner, playtest 6: "lots of US soldiers sliding and not walking".
    /// Every living man, every frame of a fight, is sorted by how the sim has
    /// him (on his feet, on a knee, flat), whether he is travelling and which
    /// way against his facing, and what his Animator is playing; and the foot
    /// he has on the ground is watched. Three things come out that a still
    /// frame cannot show and that are wrong whenever they happen:
    ///
    ///   gliding        travelling on a knee or flat: there is no gait for it
    ///                  that the sim's speeds fit, so he is carried
    ///   on the spot    at rest in the sim with his legs still walking
    ///   crabbing       travelling sideways or backwards to his facing
    ///   carried        the ground he covers that his legs do not account for:
    ///                  his speed over it against the speed his gait is played
    ///                  at (none, while he is getting up or going down), summed
    ///                  as metres. A man moved at a sprint in the tenth of a
    ///                  second before his legs are under him is carried 40 cm
    ///
    /// The planted foot's travel is reported too, by the ball of the foot of
    /// the leg with the lower ankle while that ball is on the ground. It is a
    /// guide, not a gate: a run has no planted foot for most of its stride.
    ///
    /// <code>tools/unity.sh -runTests -testPlatform PlayMode -testCategory Motion -testResults "$PWD/Logs/motion.xml"</code>
    /// The table is written to Logs/motion.txt.
    /// </summary>
    public class MotionAuditTests
    {
        private static GameRoot Root => GameRoot.Instance;

        private struct Sample { public Vector3 Root, Left, Right, LeftToe, RightToe; public bool LeftDown; public float Yaw; public int Posture; }

        /// <summary>The match that is watched.</summary>
        public const int Seed = 3;

        [UnityTest, Category("Motion")]
        public IEnumerator Men_walk_where_they_go_and_stand_where_they_stop()
        {
            yield return UIAuditInterfaceTests.LoadAndDeploy();
            // The same match every run, so one run's numbers can be set against another's.
            Root.NewMatch(Side.Us, MatchLength.Standard, Seed);
            var army = Root.ArmyView;
            var st = Root.Driver.State;
            st.Player = null;                           // both sides raise their own squads: a full fight to watch
            army.StepAll = true;
            int was = Time.captureFramerate;
            Time.captureFramerate = 60;                 // a sixtieth of a second a frame, however fast the frames come
            Root.Driver.FastForward(300);
            yield return null; yield return null;

            var prev = new Dictionary<int, Sample>();
            var skate = new Dictionary<string, float>(); var seconds = new Dictionary<string, float>();
            var moved = new Dictionary<string, float>(); var param = new Dictionary<string, float>();
            float manSeconds = 0, travelling = 0, gliding = 0, crabbing = 0, atRest = 0, onTheSpot = 0;
            float travelled = 0, carried = 0, climbing = 0;
            const float dt = 1f / 60f;
            for (int frame = 0; frame < 60 * 75 && !st.Over; frame++)
            {
                yield return null;
                for (int i = 0; i < st.Men.Count; i++)
                {
                    var m = st.Men[i];
                    var f = army.FigureOf(i);
                    if (!m.Alive || f == null || f.Animator == null) { prev.Remove(i); continue; }
                    var a = f.Animator;
                    var lf = a.GetBoneTransform(HumanBodyBones.LeftFoot); var rf = a.GetBoneTransform(HumanBodyBones.RightFoot);
                    if (lf == null || rf == null) continue;
                    var lt = a.GetBoneTransform(HumanBodyBones.LeftToes) ?? lf; var rt = a.GetBoneTransform(HumanBodyBones.RightToes) ?? rf;
                    var now = new Sample
                    {
                        Root = f.transform.position, Left = lf.position, Right = rf.position, LeftToe = lt.position, RightToe = rt.position,
                        LeftDown = lf.position.y <= rf.position.y, Yaw = f.transform.eulerAngles.y, Posture = (int)m.Posture,
                    };
                    if (prev.TryGetValue(i, out var p) && p.Posture == now.Posture)
                    {
                        Vector3 step = now.Root - p.Root; step.y = 0;
                        float speed = step.magnitude / dt;
                        if (speed < 8f)                 // not a jump in time or a new match
                        {
                            float slid = 0;
                            if (now.LeftDown == p.LeftDown
                                && (now.LeftDown ? now.LeftToe.y : now.RightToe.y) - now.Root.y < 0.06f
                                && (p.LeftDown ? p.LeftToe.y : p.RightToe.y) - p.Root.y < 0.06f)
                            {
                                Vector3 foot = now.LeftDown ? now.LeftToe - p.LeftToe : now.RightToe - p.RightToe;
                                foot.y = 0;
                                slid = foot.magnitude;
                            }
                            float along = speed < 0.1f ? 0f : Vector3.Dot(step.normalized, Quaternion.Euler(0, now.Yaw, 0) * Vector3.forward);
                            string gait = speed < 0.1f ? "at rest" : along > 0.5f ? "forward" : along < -0.5f ? "backward" : "sideways";
                            var info = a.GetCurrentAnimatorStateInfo(0);
                            string state = a.IsInTransition(0) ? "changing state"
                                : info.IsName("Stand") ? "Stand" : info.IsName("Crouch") ? "Crouch" : info.IsName("Prone") ? "Prone" : "between postures";
                            string key = $"{(Posture)now.Posture,-8} {gait,-8} {state}";
                            skate[key] = skate.GetValueOrDefault(key) + slid;
                            seconds[key] = seconds.GetValueOrDefault(key) + dt;
                            moved[key] = moved.GetValueOrDefault(key) + step.magnitude;
                            param[key] = param.GetValueOrDefault(key) + Mathf.Abs(a.GetFloat("Speed")) * dt;
                            manSeconds += dt;
                            // Over a parapet he is carried, by the climb's own clip: that is not a gait, and not a slide.
                            int react = a.GetLayerIndex("React");
                            if (react >= 0 && (a.GetCurrentAnimatorStateInfo(react).IsName("Climb in") || a.GetCurrentAnimatorStateInfo(react).IsName("Climb out")))
                            {
                                climbing += dt;
                                prev[i] = now;
                                continue;
                            }
                            // What his legs are doing: the gait's own speed as it is played, in a gait; nothing between postures.
                            bool gaited = info.IsName("Stand") || info.IsName("Crouch") || info.IsName("Prone");
                            float legs = gaited ? Mathf.Abs(a.GetFloat("Speed")) * a.GetFloat("SpeedScale") : 0f;
                            travelled += step.magnitude;
                            // (Past a quarter of a metre a second: a gait a little fast or slow for the ground is not a slide.)
                            carried += Mathf.Max(0f, Mathf.Abs(speed - legs) - 0.25f) * dt;
                            if (speed >= 0.1f)
                            {
                                travelling += dt;
                                // A sapper coming up bent double is the one man who travels off his feet, and he has a gait for it.
                                if (now.Posture != 0 && !(now.Posture == 1 && state == "Crouch" && speed > 0.8f && along > 0.5f && st.Squads[m.Squad].Reach <= Tune.Stalks)) gliding += dt;
                                else if (along <= 0.5f) crabbing += dt;
                            }
                            else if (state == "Stand" || state == "Crouch")
                            {
                                atRest += dt;
                                if (Mathf.Abs(a.GetFloat("Speed")) > 0.3f) onTheSpot += dt;
                            }
                        }
                    }
                    prev[i] = now;
                }
                // Keep the camera on the fight, so what is measured is what would be watched.
                if (frame % 120 == 0)
                {
                    var live = st.Men.Where(x => x.Alive).ToList();
                    if (live.Count > 0) Root.CameraRig.Focus((float)live.Average(x => x.X), instant: true);
                }
            }
            Time.captureFramerate = was;
            army.StepAll = false;

            var lines = new List<string>
            {
                $"motion audit: {manSeconds:F0} man-seconds to tick {st.Tick}, {travelling:F0} of them travelling",
                $"  gliding (travelling on a knee or flat)        {gliding:F1} man-seconds, {100 * gliding / Mathf.Max(1, travelling):F1}% of travelling",
                $"  crabbing (travelling sideways or backwards)   {crabbing:F1} man-seconds, {100 * crabbing / Mathf.Max(1, travelling):F1}% of travelling",
                $"  walking on the spot (at rest, legs going)     {onTheSpot:F1} man-seconds, {100 * onTheSpot / Mathf.Max(1, atRest):F1}% of standing or kneeling at rest",
                $"  carried (ground covered without the legs)     {carried:F0} m of {travelled:F0} m travelled, {100 * carried / Mathf.Max(1, travelled):F1}%",
                $"  climbing (into a trench or out of one)        {climbing:F1} man-seconds, not counted above",
                "  posture  gait     state                 man-seconds   moving m/s   planted foot cm/s   Speed",
            };
            foreach (var kv in seconds.OrderByDescending(k => k.Value))
                lines.Add($"  {kv.Key,-40} {kv.Value,9:F0} {moved[kv.Key] / kv.Value,12:F2} {100 * skate[kv.Key] / kv.Value,14:F0} {param[kv.Key] / kv.Value,12:F2}");
            Directory.CreateDirectory("Logs");
            File.WriteAllLines("Logs/motion.txt", lines);
            foreach (var l in lines.Take(6)) Debug.Log("[LOV] " + l);

            Assert.Greater(manSeconds, 600, "hardly anyone was watched");
            Assert.Less(gliding / Mathf.Max(1, travelling), 0.02f, "men are travelling on a knee or flat (see Logs/motion.txt)");
            Assert.Less(crabbing / Mathf.Max(1, travelling), 0.06f, "men are travelling sideways or backwards to their facing (see Logs/motion.txt)");
            Assert.Less(onTheSpot / Mathf.Max(1, atRest), 0.03f, "men at rest are walking on the spot (see Logs/motion.txt)");
            // (It was 4.9% before the drawn men followed the simulation's instead of being put where it had them.)
            Assert.Less(carried / Mathf.Max(1, travelled), 0.02f, "men are being carried over ground their legs do not cover (see Logs/motion.txt)");
        }
    }
}
