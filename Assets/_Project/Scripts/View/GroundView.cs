using System;
using System.Collections.Generic;
using LanesOfVietnam.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// The terrain, built at load from <see cref="Ground.HeightAt"/> — the same
    /// function anything standing on the ground asks. No mesh is shipped: the
    /// ground is a few hundred lines of maths and costs nothing to download.
    ///
    /// Chunked along x so the camera's pan culls what it cannot see, and in
    /// three resolutions by distance: the playfield is 0.4 m, fine enough for
    /// crater lips and trench edges to hold their silhouette at 40 m; beyond
    /// the far lane it coarsens, because a camera looking along the ground at
    /// a fraction of a degree sees depth foreshortened to almost nothing.
    ///
    /// Vertex colour carries the ground's own masks for the shader — r is the
    /// track, g is disturbed earth (crater bowls, trench cuts, banks), b is
    /// wet low ground — computed from the same features the heights are, so
    /// the dirt is where the track is.
    /// </summary>
    public sealed class GroundView : MonoBehaviour
    {
        public Material Material;

        /// <summary>(near sim z, far sim z, metres per vertex).</summary>
        private static readonly (double near, double far, double step)[] Bands =
        {
            (32, -22, 0.4),
            (-22, -64, 1.0),
            (-64, -170, 2.5),
        };

        public const double XMin = -170, XMax = 170, ChunkWidth = 34;
        private const float Skirt = 0.6f;

        public int Vertices { get; private set; }
        public int Triangles { get; private set; }

        public void Build(Ground g)
        {
            var t0 = Time.realtimeSinceStartupAsDouble;
            foreach (Transform c in transform) Destroy(c.gameObject);
            Vertices = Triangles = 0;
            int chunks = (int)Math.Ceiling((XMax - XMin) / ChunkWidth);
            foreach (var band in Bands)
            {
                for (int i = 0; i < chunks; i++)
                {
                    double x0 = XMin + i * ChunkWidth;
                    BuildChunk(g, x0, Math.Min(XMax, x0 + ChunkWidth), band.near, band.far, band.step);
                }
            }
            Debug.Log($"[LOV] ground: {Vertices:N0} vertices, {Triangles:N0} triangles, "
                      + $"{(Time.realtimeSinceStartupAsDouble - t0) * 1000:F0} ms");
        }

        private void BuildChunk(Ground g, double x0, double x1, double zNear, double zFar, double step)
        {
            int nx = (int)Math.Round((x1 - x0) / step) + 1;
            int nz = (int)Math.Round((zNear - zFar) / step) + 1;
            double dx = (x1 - x0) / (nx - 1), dz = (zNear - zFar) / (nz - 1);

            // Heights with a one-cell border, so normals at the chunk's edge
            // come from real neighbours and adjacent chunks agree.
            var h = new float[(nx + 2) * (nz + 2)];
            for (int j = -1; j <= nz; j++)
            {
                double z = zNear - j * dz;
                for (int i = -1; i <= nx; i++)
                {
                    h[(j + 1) * (nx + 2) + (i + 1)] = (float)g.HeightAt(x0 + i * dx, z);
                }
            }
            float H(int i, int j) => h[(j + 1) * (nx + 2) + (i + 1)];

            int skirtVerts = 2 * (nx + nz) * 2;
            int count = nx * nz + skirtVerts;
            var pos = new Vector3[count];
            var nrm = new Vector3[count];
            var col = new Color32[count];
            var uv = new Vector2[count];
            var idx = new List<int>((nx - 1) * (nz - 1) * 6 + skirtVerts * 6);

            for (int j = 0; j < nz; j++)
            {
                double z = zNear - j * dz;
                for (int i = 0; i < nx; i++)
                {
                    double x = x0 + i * dx;
                    int v = j * nx + i;
                    pos[v] = Coords.World(x, z, H(i, j));
                    // World z is -sim z, so d/dworldz = -d/dsimz.
                    float gx = (H(i + 1, j) - H(i - 1, j)) / (float)(2 * dx);
                    float gzSim = (H(i, j - 1) - H(i, j + 1)) / (float)(2 * dz);
                    nrm[v] = new Vector3(-gx, 1f, gzSim).normalized;
                    col[v] = Masks(g, x, z);
                    uv[v] = new Vector2((float)x, (float)-z);
                }
            }
            for (int j = 0; j < nz - 1; j++)
            {
                for (int i = 0; i < nx - 1; i++)
                {
                    int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                    // Wound for Unity's left-handed, clockwise-front convention
                    // as seen from above.
                    idx.Add(a); idx.Add(c); idx.Add(b);
                    idx.Add(b); idx.Add(c); idx.Add(d);
                }
            }

            // Skirts: a strip hanging down from every edge, so the seams
            // between bands of different resolution never show sky. Both
            // windings, so which way an edge faces never needs working out.
            int s = nx * nz;
            void SkirtEdge(int from, int stride, int n)
            {
                for (int k = 0; k < n; k++)
                {
                    int src = from + k * stride;
                    pos[s + 2 * k] = pos[src];
                    pos[s + 2 * k + 1] = pos[src] - new Vector3(0, Skirt, 0);
                    nrm[s + 2 * k] = nrm[s + 2 * k + 1] = nrm[src];
                    col[s + 2 * k] = col[s + 2 * k + 1] = col[src];
                    uv[s + 2 * k] = uv[s + 2 * k + 1] = uv[src];
                }
                for (int k = 0; k < n - 1; k++)
                {
                    int a = s + 2 * k, b = a + 1, c = a + 2, d = a + 3;
                    idx.Add(a); idx.Add(c); idx.Add(b); idx.Add(b); idx.Add(c); idx.Add(d);
                    idx.Add(a); idx.Add(b); idx.Add(c); idx.Add(b); idx.Add(d); idx.Add(c);
                }
                s += 2 * n;
            }
            SkirtEdge(0, 1, nx);                 // near edge
            SkirtEdge((nz - 1) * nx, 1, nx);     // far edge
            SkirtEdge(0, nx, nz);                // west edge
            SkirtEdge(nx - 1, nx, nz);           // east edge

            var mesh = new Mesh { name = $"ground {x0:F0}..{x1:F0} z{zNear:F0}..{zFar:F0}" };
            mesh.indexFormat = count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetColors(col);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(idx, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mesh.UploadMeshData(true);

            var go = new GameObject(mesh.name);
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Material;
            // The ground receives shadows and casts none: in this light a berm's
            // shadow on the ground behind it is invisible, and the shadow map's
            // fill is better spent on men and plants.
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            Vertices += count;
            Triangles += idx.Count / 3;
        }

        /// <summary>The ground's material masks at a point. See the class comment.</summary>
        public static Color32 Masks(Ground g, double x, double z)
        {
            double track = g.TrackMask(x, z);
            double disturbed = 0;
            foreach (var c in g.Craters)
            {
                double d = JsMath.Hypot(x - c.X, z - c.Z) / c.R;
                if (d < 1.6) disturbed = Math.Max(disturbed, 1 - Math.Max(0, d - 0.9) / 0.7);
            }
            foreach (var t in g.Trenches)
            {
                if (Math.Abs(x - t.X) > t.Length * 0.5 + 1) continue;
                double d = Math.Abs(z - t.Z) / t.Width;
                // The cut and the spoil thrown up behind it are bare earth.
                if (d < 2.2) disturbed = Math.Max(disturbed, 1 - Math.Max(0, d - 1.2));
            }
            foreach (var m in g.Mounds)
            {
                if (Math.Abs(x - m.X) > m.Length * 0.5) continue;
                double d = Math.Abs(z - m.Z) / m.Width;
                if (d < 1.2) disturbed = Math.Max(disturbed, 0.55 * (1 - d / 1.2));
            }
            // Wet where the track is lowest: the ruts hold water.
            double wet = track * Math.Max(0, Math.Min(1, (0.1 - g.HeightAt(x, z) + g.HeightAt(x, z + 3)) * 2));
            return new Color32((byte)(track * 255), (byte)(disturbed * 255), (byte)(Math.Max(0, wet) * 255), 255);
        }
    }
}
