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
    /// The firebase's built things (PLAN §12.43, tools/blender/firebase_mesh.py): that they are in the game as
    /// meshes and not the boxes that stand in when the meshes are missing, and that the tower stands where the
    /// simulation's post is, its men inside its rail.
    ///
    /// <code>tools/unity.sh -runTests -testPlatform PlayMode -testCategory Structures -testResults "$PWD/Logs/structures.xml"</code>
    /// </summary>
    public class StructureTests
    {
        [UnityTest, Category("Structures")]
        public IEnumerator The_firebase_is_built_of_its_meshes_and_the_tower_stands_on_its_post()
        {
            yield return UIAuditInterfaceTests.LoadAndDeploy();
            var dressing = Object.FindAnyObjectByType<WorldDressing>();
            Assert.IsTrue(dressing.Structures.Ready, "the firebase's meshes are not in the scene: boxes stand in (tools/blender/firebase_mesh.py, then SceneBuilder)");
            // (The dressing merges what it places by material, so the members are looked for in the set, and the
            // merged meshes by their materials.)
            foreach (string member in StructureSet.TowerMembers.Concat(new[] { "m35_covered", "m35_open", "jeep", "bunker_roof" }))
            {
                var m = dressing.Structures.Of(member);
                Assert.IsNotNull(m, $"there is no {member}");
                Assert.Greater(m.triangles.Length, 0, member);
            }
            var drawn = dressing.Built.Select(r => r.sharedMaterial).ToList();
            Assert.Contains(dressing.Structures.Timber, drawn, "nothing of timber is drawn: no tower");
            Assert.Contains(dressing.Structures.Vehicles, drawn, "nothing is drawn in the vehicles' paint: no trucks, no jeep");
            Assert.IsNull(dressing.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "tower leg"), "a box stands in for the tower");
            var cover = Object.FindAnyObjectByType<CoverView>();
            Assert.IsTrue(cover.Built.Any(r => r != null && r.sharedMaterial == dressing.Structures.Timber), "no bunker has its roof");

            // The tower over its post: where the post's men stand is inside its legs, as the tower is placed.
            var legs = dressing.Structures.Of("tower_legs").bounds;
            var foot = Coords.World(Map.TowerX, Map.TowerZ - StructureSet.TowerBehind, 0f);
            legs.center += foot;
            var post = GameRoot.Instance.Driver.State.Posts[0];
            var stand = Coords.World(post.X, post.Z, legs.center.y);
            Assert.IsTrue(stand.x > legs.min.x && stand.x < legs.max.x && stand.z > legs.min.z && stand.z < legs.max.z,
                          $"the post's men stand at {stand}, outside the tower ({legs.min} to {legs.max})");
        }
    }
}
