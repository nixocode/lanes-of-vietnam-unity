using UnityEngine;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// Where the simulation's ground plane sits in the Unity scene.
    ///
    /// The simulation works in (x, z) with the camera at z = +44 looking down
    /// −z, which is how the three.js build had it: right-handed, +x to the
    /// camera's right. Unity is left-handed. Carried over literally, +x lands
    /// on the camera's <i>left</i>, and the firebase — the west end, x &lt; 0,
    /// on the left of the reference — would be drawn on the right.
    ///
    /// So one mapping, used by everything that draws: world z is −sim z. The
    /// camera sits at world z = −44 looking along +z; +x is screen right; the
    /// near lane is 37 m in front of the lens and the treeline beyond it. There
    /// is no other conversion anywhere, which is the point.
    /// </summary>
    public static class Coords
    {
        public static Vector3 World(double simX, double simZ, float y)
            => new Vector3((float)simX, y, (float)-simZ);

        public static float WorldZ(double simZ) => (float)-simZ;

        public static double SimZ(float worldZ) => -worldZ;

        /// <summary>
        /// Brief §4's camera, measured against TARGET.jpg by the three.js
        /// build: a 19 degree vertical lens, 5.1 m up, 44 m back from the
        /// middle of the map, aimed 0.275 degrees below level. A long lens
        /// from a long way back is what makes the reference read as a very
        /// good photograph of a diorama rather than as a game camera.
        /// </summary>
        public static class Camera
        {
            public const float Fov = 19f;
            public const float Height = 5.1f;
            public const float SimZ = 44f;
            public const float LookY = 4.6f;
            public const float LookSimZ = -60f;

            /// <summary>The fixed view direction: pan moves it, nothing rotates it.</summary>
            public static Quaternion Rotation
            {
                get
                {
                    var from = World(0, SimZ, Height);
                    var to = World(0, LookSimZ, LookY);
                    return Quaternion.LookRotation(to - from, Vector3.up);
                }
            }
        }
    }
}
