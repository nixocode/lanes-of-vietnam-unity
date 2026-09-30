using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// Light the scene from the sky it shows: the sun and the ambient come from
    /// the same HDRI as the sky (tools/blender/sky_bake.py), in the same units.
    ///
    /// The three.js build hand-derived its lighting from the tonemap curve up,
    /// and a sky and a light rig chosen separately never quite agree; here
    /// the sun's direction, colour and strength and the ambient's colour and
    /// strength are all measured off the one photograph.
    ///
    /// Units: a directional light of intensity I lights a surface facing it
    /// with albedo x I in URP, which is irradiance / pi. So the sun gets
    /// E_sun / pi, and each patch of sky adds L x dOmega / pi to the ambient
    /// spherical harmonics. One scene scale multiplies all three — sky, sun,
    /// ambient — so their ratios, which are what make it look like that day,
    /// never drift.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class SkyLighting : MonoBehaviour
    {
        public TextAsset SkyJson;
        public Light Sun;
        public Material SkyMaterial;

        /// <summary>The scene's light scale: sky radiance 1 becomes this many units.</summary>
        public float Scale = 2f;
        /// <summary>Shares of the measured sun and sky. 1 is the photograph.</summary>
        [Range(0, 2)] public float SunShare = 1f;
        [Range(0, 2)] public float AmbientShare = 1f;

        [Serializable] private class Win { public float az0, az1, el0, el1; }
        [Serializable] private class SunInfo { public float azimuth, elevation; public float[] dir_to_sun; public float[] irradiance_rgb; }
        [Serializable] private class Grid { public int width, height; public float[] rgb; }
        [Serializable] private class Info { public float exposure_k; public Win window; public SunInfo sun; public Grid radiance_grid; }

        /// <summary>What was applied, for the log and the tests.</summary>
        public float SunIntensity { get; private set; }
        public Color HorizonLinear { get; private set; }
        public float ShCalibration { get; private set; }
        public bool ShDirectionTowardLight { get; private set; }

        private void Awake() => Apply();

        public void Apply()
        {
            if (SkyJson == null) return;
            var info = JsonUtility.FromJson<Info>(SkyJson.text);

            if (SkyMaterial != null)
            {
                SkyMaterial.SetFloat("_K", info.exposure_k);
                SkyMaterial.SetFloat("_Scale", Scale);
                SkyMaterial.SetVector("_WindowAz", new Vector4(info.window.az0, info.window.az1));
                SkyMaterial.SetVector("_WindowEl", new Vector4(info.window.el0, info.window.el1));
                RenderSettings.skybox = SkyMaterial;
            }

            // The sun.
            var toSun = new Vector3(info.sun.dir_to_sun[0], info.sun.dir_to_sun[1], info.sun.dir_to_sun[2]).normalized;
            var E = info.sun.irradiance_rgb;
            float lumE = 0.2126f * E[0] + 0.7152f * E[1] + 0.0722f * E[2];
            if (Sun != null)
            {
                Sun.transform.rotation = Quaternion.LookRotation(-toSun, Vector3.up);
                Sun.color = new Color(E[0] / lumE, E[1] / lumE, E[2] / lumE);
                SunIntensity = lumE / Mathf.PI * Scale * SunShare;
                Sun.intensity = SunIntensity;
                RenderSettings.sun = Sun;
            }

            // The ambient: every patch of sky as a small light, summed by Unity's
            // own SH code — calibrated first, rather than trusting a reading of
            // its documentation for which way "direction" points or how its
            // intensity scales.
            Calibrate();
            var g = info.radiance_grid;
            var sh = new SphericalHarmonicsL2();
            float dAz = 2 * Mathf.PI / g.width, dEl = Mathf.PI / g.height;
            for (int y = 0; y < g.height; y++)
            {
                float el = (90f - (y + 0.5f) * 180f / g.height) * Mathf.Deg2Rad;
                float dOmega = dAz * dEl * Mathf.Cos(el);
                for (int x = 0; x < g.width; x++)
                {
                    float az = (-180f + (x + 0.5f) * 360f / g.width) * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
                    int i = (y * g.width + x) * 3;
                    var L = new Color(g.rgb[i], g.rgb[i + 1], g.rgb[i + 2]);   // stored as radiance, not / k
                    sh.AddDirectionalLight(ShDirectionTowardLight ? dir : -dir, L,
                                           dOmega / Mathf.PI * Scale * AmbientShare * ShCalibration);
                }
            }
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = sh;

            // Fog takes the colour of the horizon it is fading toward.
            HorizonLinear = Horizon(g, info.window) * Scale;
            RenderSettings.fogColor = HorizonLinear.gamma;

            var win = SkyMaterial != null ? SkyMaterial.GetTexture("_Window") : null;
            Debug.Log($"[LOV] sky light: window {(win != null ? $"{win.width}x{win.height}" : "none")}, sun {SunIntensity:F2} from az {info.sun.azimuth:F0} el {info.sun.elevation:F0}, "
                      + $"horizon {HorizonLinear}, SH calibration {ShCalibration:F3} (toward light: {ShDirectionTowardLight})");
        }

        /// <summary>
        /// A uniform sky of radiance 1 must evaluate to 1 at any normal, and a
        /// light from above must brighten an upward normal: find the factor and
        /// the sign that make both true.
        /// </summary>
        private void Calibrate()
        {
            var up = new SphericalHarmonicsL2();
            up.AddDirectionalLight(Vector3.up, Color.white, 1f);
            var dirs = new[] { Vector3.up, Vector3.down };
            var res = new Color[2];
            up.Evaluate(dirs, res);
            ShDirectionTowardLight = res[0].r > res[1].r;

            var uniform = new SphericalHarmonicsL2();
            const int n = 48;
            for (int y = 0; y < n; y++)
            {
                float el = (90f - (y + 0.5f) * 180f / n) * Mathf.Deg2Rad;
                float dOmega = (2 * Mathf.PI / (2 * n)) * (Mathf.PI / n) * Mathf.Cos(el);
                for (int x = 0; x < 2 * n; x++)
                {
                    float az = (-180f + (x + 0.5f) * 360f / (2 * n)) * Mathf.Deg2Rad;
                    var d = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
                    uniform.AddDirectionalLight(ShDirectionTowardLight ? d : -d, Color.white, dOmega / Mathf.PI);
                }
            }
            var one = new Color[1];
            uniform.Evaluate(new[] { Vector3.up }, one);
            ShCalibration = one[0].r > 1e-6f ? 1f / one[0].r : 1f;
        }

        /// <summary>Mean radiance of the band just above the horizon, across the window.</summary>
        private static Color Horizon(Grid g, Win w)
        {
            Color sum = Color.black; int n = 0;
            for (int y = 0; y < g.height; y++)
            {
                float el = 90f - (y + 0.5f) * 180f / g.height;
                if (el < 0f || el > 6f) continue;
                for (int x = 0; x < g.width; x++)
                {
                    float az = -180f + (x + 0.5f) * 360f / g.width;
                    if (az < w.az0 || az > w.az1) continue;
                    int i = (y * g.width + x) * 3;
                    sum += new Color(g.rgb[i], g.rgb[i + 1], g.rgb[i + 2]); n++;
                }
            }
            return n > 0 ? sum / n : Color.grey;
        }
    }
}
