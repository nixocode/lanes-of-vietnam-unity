using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// The Chu Pong massif behind the treeline, from real elevation data
    /// (tools/art/mountains.py): the view from LZ X-Ray, heading 262 degrees.
    ///
    /// The heights ship as a fan-shaped grid around the lens — columns of
    /// azimuth, rings of distance growing geometrically — because the camera
    /// never rotates: a grid like that is about equally fine on screen near
    /// and far. The mesh is built here, at load, like the ground.
    /// </summary>
    public sealed class MountainView : MonoBehaviour
    {
        public TextAsset Heights;
        public TextAsset Layout;
        public Material Material;

        [Serializable] private class L { public int rings, columns; public float near_m, far_m, az_half_deg; }

        public int Vertices { get; private set; }

        public void Build()
        {
            foreach (Transform c in transform) Destroy(c.gameObject);
            if (Heights == null || Layout == null || Material == null) return;
            var l = JsonUtility.FromJson<L>(Layout.text);
            var raw = Heights.bytes;
            int R = l.rings, C = l.columns;
            if (raw.Length != R * C * 2) throw new Exception($"mountains: {raw.Length} bytes for a {R} x {C} grid");

            var pos = new Vector3[R * C];
            for (int r = 0; r < R; r++)
            {
                double d = l.near_m * Math.Pow(l.far_m / l.near_m, r / (double)(R - 1));
                for (int c = 0; c < C; c++)
                {
                    double az = (-l.az_half_deg + 2.0 * l.az_half_deg * c / (C - 1)) * Math.PI / 180.0;
                    int i = r * C + c;
                    float h = Mathf.HalfToFloat(BitConverter.ToUInt16(raw, i * 2));
                    // Azimuth grows clockwise, which is screen right; forward is -sim z.
                    pos[i] = Coords.World(d * Math.Sin(az), Coords.Camera.SimZ - d * Math.Cos(az), h);
                }
            }
            var idx = new int[(R - 1) * (C - 1) * 6];
            int k = 0;
            for (int r = 0; r < R - 1; r++)
                for (int c = 0; c < C - 1; c++)
                {
                    int a = r * C + c, b = a + 1, n = a + C, m = n + 1;
                    idx[k++] = a; idx[k++] = n; idx[k++] = b;
                    idx[k++] = b; idx[k++] = n; idx[k++] = m;
                }
            var mesh = new Mesh { name = "mountains", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(pos);
            mesh.SetTriangles(idx, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            var go = new GameObject("mountains");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            Vertices = pos.Length;
            Debug.Log($"[LOV] mountains: {R} x {C} grid, {idx.Length / 3:N0} triangles, {l.near_m / 1000:F1}-{l.far_m / 1000:F0} km");
        }
    }
}
