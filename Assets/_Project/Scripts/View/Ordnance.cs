using System.Collections.Generic;
using UnityEngine;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// What is thrown and fired through the air, as things (PLAN §12.24, the
    /// owner's playtest 8: "no grenades in hand when throwing, or air, or
    /// landing: add them"; "remove the rays in the sky ... show the grenade,
    /// not some flash"). Built here, by turning a profile about an axis, and
    /// coloured from the weapons' own palette (tools/blender/weapons.py: one
    /// texel a material), so they share the weapons' material and need no
    /// file of their own:
    ///
    ///   Lemon    the M26: an olive egg, its fuze on top
    ///   Stick    the Chicom Type 67 the VC threw: an olive can on a wooden handle
    ///   Round    an M79's 40 mm round in flight
    ///   Bomb     a 60 mm mortar bomb
    ///
    /// Each mesh's long axis is +Y, about its middle. They are drawn larger
    /// than life: at this camera a grenade ten centimetres long is five pixels.
    /// </summary>
    public static class Ordnance
    {
        public enum Kind { Lemon, Stick, Round, Bomb }

        /// <summary>How much larger than life they are drawn.</summary>
        public const float Larger = 2.2f;

        /// <summary>Seconds into a throw that the grenade leaves the hand (Mixamo's toss), and that its flight takes.</summary>
        public const float ThrowRelease = 0.45f, ThrowFlight = 1.0f;

        // The palette's texels (weapons.json): steel, black, wood, walnut, olive, glass, brass, canvas.
        private const int Steel = 0, Wood = 2, Olive = 4, Brass = 6, Texels = 8;

        private static readonly Dictionary<Kind, Mesh> Meshes = new Dictionary<Kind, Mesh>();

        public static Mesh Get(Kind kind)
        {
            if (Meshes.TryGetValue(kind, out var m) && m != null) return m;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            switch (kind)
            {
                case Kind.Lemon:
                    Lathe(v, uv, tris, Olive, (0f, 0f), (0.018f, 0.006f), (0.028f, 0.018f), (0.032f, 0.040f), (0.029f, 0.062f), (0.020f, 0.078f), (0.010f, 0.085f), (0f, 0.085f));
                    Lathe(v, uv, tris, Steel, (0f, 0.083f), (0.010f, 0.083f), (0.010f, 0.104f), (0f, 0.104f));
                    break;
                case Kind.Stick:
                    Lathe(v, uv, tris, Wood, (0f, 0f), (0.012f, 0f), (0.0125f, 0.19f), (0f, 0.19f));
                    Lathe(v, uv, tris, Olive, (0f, 0.185f), (0.022f, 0.188f), (0.025f, 0.20f), (0.025f, 0.28f), (0.020f, 0.29f), (0f, 0.29f));
                    break;
                case Kind.Round:
                    Lathe(v, uv, tris, Brass, (0f, 0f), (0.019f, 0f), (0.020f, 0.018f), (0f, 0.018f));
                    Lathe(v, uv, tris, Olive, (0f, 0.016f), (0.020f, 0.016f), (0.020f, 0.032f), (0.015f, 0.046f), (0f, 0.052f));
                    break;
                default:
                    Lathe(v, uv, tris, Steel, (0f, 0f), (0.011f, 0f), (0.011f, 0.07f), (0f, 0.07f));
                    Lathe(v, uv, tris, Olive, (0f, 0.065f), (0.016f, 0.07f), (0.030f, 0.11f), (0.030f, 0.16f), (0.021f, 0.21f), (0.008f, 0.235f), (0f, 0.24f));
                    break;
            }
            // About its middle, so it tumbles about its middle.
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in v) { lo = Mathf.Min(lo, p.y); hi = Mathf.Max(hi, p.y); }
            float mid = (lo + hi) * 0.5f;
            for (int i = 0; i < v.Count; i++) v[i] = new Vector3(v[i].x, v[i].y - mid, v[i].z) * Larger;
            m = new Mesh { name = "ordnance " + kind };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            Meshes[kind] = m;
            return m;
        }

        /// <summary>A surface turned about +Y from a profile of (radius, height) points, one palette texel.</summary>
        private static void Lathe(List<Vector3> v, List<Vector2> uv, List<int> tris, int texel, params (float r, float y)[] profile)
        {
            const int sides = 10;
            var at = new Vector2((texel + 0.5f) / Texels, 0.5f);
            int start = v.Count;
            foreach (var (r, y) in profile)
                for (int s = 0; s <= sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    v.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                    uv.Add(at);
                }
            for (int p = 0; p < profile.Length - 1; p++)
                for (int s = 0; s < sides; s++)
                {
                    int a = start + p * (sides + 1) + s, b = a + sides + 1;
                    // Facing out, in Unity's clockwise-front convention.
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
        }
    }
}
