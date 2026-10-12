using System.Collections.Generic;
using LanesOfVietnam.Sim;
using UnityEngine;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// The firebase's built things as geometry (tools/blender/firebase_mesh.py):
    /// the two M35 trucks, the M151 jeep, the watchtower in its members, and the
    /// timbers of a bunker's roof, on two weathered atlases.
    ///
    /// The vehicles and the tower were cards baked from boxes and stood up
    /// facing the lens, and a bunker's roof was one stretched cube. The owner,
    /// 2026-10-12: "the truck and the shed are looking so bad it's dragging the
    /// game's look... they need to be looking realistic and not like polygons".
    /// </summary>
    [System.Serializable]
    public struct StructureSet
    {
        public Mesh[] Meshes;
        /// <summary>What the trucks and the jeep wear, and what the tower's and a bunker's timbers do.</summary>
        public Material Vehicles, Timber;

        /// <summary>Metres a run of a bunker's roof logs or of its beam covers, and how deep the logs are as they are built (they are drawn out to the bunker's depth).</summary>
        public const float Run = 2.4f, LogDepth = 1f;
        /// <summary>
        /// How far behind where its men stand the tower's middle is (away from the lens), metres; and how
        /// far in front of where they stand its ladder is. From the script that builds it (STAND, LADDER),
        /// which sets them by the simulation's post: the men stand half a metre inside the front rail.
        /// </summary>
        public const float TowerBehind = 1.32f, LadderAhead = 0.34f;

        public bool Ready => Vehicles != null && Timber != null && Meshes != null && Meshes.Length > 0;

        public Mesh Of(string name)
        {
            if (Meshes == null) return null;
            foreach (var m in Meshes) if (m != null && m.name == name) return m;
            return null;
        }

        /// <summary>The tower's members: placed together they are the tower, and apart they are what is left of it.</summary>
        public static readonly string[] TowerMembers = { "tower_legs", "tower_braces", "tower_deck", "tower_cabin", "tower_roof", "tower_ladder" };

        /// <summary>
        /// A mesh at a place on the map, turned about the vertical (degrees; 0 is as it was built, which for a
        /// vehicle is its front to the west). Null if there is no such mesh.
        /// </summary>
        public Renderer Place(Transform parent, List<Renderer> built, string name, Vector3 at, float yaw, Vector3? scale = null, Quaternion? tilt = null)
        {
            var mesh = Of(name);
            if (mesh == null) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(at, (tilt ?? Quaternion.identity) * Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = scale ?? Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = name.StartsWith("m35") || name.StartsWith("jeep") ? Vehicles : Timber;
            built.Add(r);
            return r;
        }

        /// <summary>
        /// A vehicle standing on the ground at a place: on its wheels, tipped to the slope under them (the
        /// ground is read under its two ends and its two sides).
        /// </summary>
        public Renderer Park(Transform parent, List<Renderer> built, Ground g, string name, double x, double z, float yaw, float length, float width)
        {
            var turn = Quaternion.Euler(0, yaw, 0);
            Vector3 along = turn * Vector3.right * (length * 0.5f), across = turn * Vector3.forward * (width * 0.5f);
            float H(Vector3 off) => (float)g.HeightAt(x + off.x, z - off.z);
            float front = H(along), back = H(-along), left = H(across), right = H(-across);
            var mid = Coords.World(x, z, (front + back + left + right) * 0.25f);
            var up = Vector3.Cross(across * 2f + Vector3.up * (left - right), along * 2f + Vector3.up * (front - back)).normalized;
            if (up.y < 0) up = -up;
            return Place(parent, built, name, mid, yaw, null, Quaternion.FromToRotation(Vector3.up, up));
        }
    }
}
