using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// The shadows of the clouds the sky shows: a tiling cloud mask as the
    /// sun's light cookie, drifting on the wind.
    ///
    /// Measured before it went in: with the ground in full sun its bands sat
    /// L* 15-25 over TARGET.jpg's while the sky matched, and no tone curve
    /// could fix it — a curve darkened the treeline long before the lanes,
    /// where in the reference the lanes are the darker of the two. The
    /// reference's sky is cumulus; its ground lies partly in their shadow.
    ///
    /// The mask is generated here (fbm, deterministic, tiling), not shipped.
    /// A cookie is URP's own mechanism, so every shader that takes the main
    /// light with its cookie — the ground, plants, props, men, the stock Lit
    /// materials — shades under the same clouds. It moves on the view's own
    /// clock, so a frozen capture renders the same shadows every time.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class CloudShadows : MonoBehaviour
    {
        /// <summary>One repeat of the mask, in metres: cumulus shadows 40-150 m across.</summary>
        public float TileMetres = 420f;
        /// <summary>Share of the ground under cloud.</summary>
        [Range(0, 1)] public float Cover = 0.42f;
        /// <summary>How much of the sun a cloud takes away.</summary>
        [Range(0, 1)] public float Density = 0.72f;
        /// <summary>Wind, metres a second across the ground.</summary>
        public Vector2 Wind = new Vector2(2.2f, 0.6f);
        /// <summary>Where the clouds start, so the opening frame has lanes in both light and shade.</summary>
        public Vector2 Offset = new Vector2(37f, 112f);

        private const int Size = 256;
        private UniversalAdditionalLightData _data;

        private void Awake()
        {
            var light = GetComponent<Light>();
            light.cookie = Build(Cover, Density);
            _data = GetComponent<UniversalAdditionalLightData>();
            if (_data == null) _data = gameObject.AddComponent<UniversalAdditionalLightData>();
            _data.lightCookieSize = new Vector2(TileMetres, TileMetres);
            Apply(0f);
        }

        private void LateUpdate()
        {
            var root = GameRoot.Instance;
            Apply(root != null ? root.ViewTime : 0f);
        }

        private void Apply(float t) => _data.lightCookieOffset = Offset + Wind * t;

        /// <summary>A tiling cloud mask: 1 in sun, 1 - density under cloud, soft edges.</summary>
        public static Texture2D Build(float cover, float density)
        {
            var px = new Color32[Size * Size];
            var v = new float[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float n = 0, amp = 0.55f, sum = 0;
                    for (int o = 0, f = 3; o < 5; o++, f *= 2)
                    {
                        n += amp * TileNoise(x * f / (float)Size, y * f / (float)Size, f, o);
                        sum += amp; amp *= 0.5f;
                    }
                    v[y * Size + x] = n / sum;
                }
            // Threshold at the cover's quantile, so the share under cloud is exact.
            var sorted = (float[])v.Clone();
            System.Array.Sort(sorted);
            float cut = sorted[(int)((1 - cover) * (sorted.Length - 1))];
            for (int i = 0; i < v.Length; i++)
            {
                float cloud = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cut - 0.035f, cut + 0.035f, v[i]));
                byte b = (byte)Mathf.RoundToInt(255f * (1f - density * cloud));
                px[i] = new Color32(b, b, b, b);
            }
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true)
            {
                name = "cloud shadows", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Value noise on a lattice of period f, so the texture tiles.</summary>
        private static float TileNoise(float x, float y, int f, int octave)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            float ux = fx * fx * (3 - 2 * fx), uy = fy * fy * (3 - 2 * fy);
            float H(int i, int j)
            {
                i = ((i % f) + f) % f; j = ((j % f) + f) % f;
                uint h = (uint)(i * 374761393 + j * 668265263 + octave * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
            }
            return Mathf.Lerp(Mathf.Lerp(H(xi, yi), H(xi + 1, yi), ux), Mathf.Lerp(H(xi, yi + 1), H(xi + 1, yi + 1), ux), uy);
        }
    }
}
