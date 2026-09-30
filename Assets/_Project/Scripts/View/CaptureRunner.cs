using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// Renders the frozen frame <see cref="CaptureSettings.Active"/> describes
    /// and writes it to disk, with the render counters beside it.
    ///
    /// Several warm-up frames are rendered and thrown away first so anything
    /// temporal has converged — TAA history above all. With
    /// <see cref="CaptureSettings.Frames"/> above one, consecutive frames of
    /// the same frozen scene are kept: the difference between them is the
    /// flicker measurement, and a still scene with everything off must
    /// difference to zero or the instrument is broken (PLAN §9 finding 1).
    ///
    /// Every frame is rendered by explicit request, one per frame, with the
    /// camera otherwise disabled. Headless play mode has no Game view, so
    /// nothing renders on its own and <see cref="WaitForEndOfFrame"/> never
    /// returns — the first version waited on it for 300 s and wrote nothing.
    /// </summary>
    public sealed class CaptureRunner : MonoBehaviour
    {
        /// <summary>Set when the capture has been written. The harness waits on it.</summary>
        public static bool Done;
        public static string Error;

        private IEnumerator Start()
        {
            Done = false;
            Error = null;
            var cap = CaptureSettings.Active;
            var root = GameRoot.Instance;
            var cam = root.CameraRig.Camera;
            var rt = new RenderTexture(cap.Width, cap.Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "capture", antiAliasing = 1,
            };
            rt.Create();
            cam.targetTexture = rt;
            cam.aspect = (float)cap.Width / cap.Height;
            cam.enabled = false;
            var camData = cam.GetComponent<UniversalAdditionalCameraData>();
            if (camData != null)
            {
                camData.antialiasing = cap.Aa switch
                {
                    "none" => AntialiasingMode.None,
                    "smaa" => AntialiasingMode.SubpixelMorphologicalAntiAliasing,
                    "fxaa" => AntialiasingMode.FastApproximateAntialiasing,
                    _ => AntialiasingMode.TemporalAntiAliasing,
                };
                if (cap.NoPost) camData.renderPostProcessing = false;
            }
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (!RenderPipeline.SupportsRenderRequest(cam, request))
            {
                Error = "the render pipeline refuses a StandardRequest for this camera";
                Done = true;
                yield break;
            }

            var cmd = FindAnyObjectByType<Commander>();
            if (cmd != null && cap.Select == -2) cmd.Cycle();
            else if (cmd != null && cap.Select >= 0) cmd.SelectSquad(cap.Select);
            if (cap.Glasses != null)
            {
                root.CameraRig.AimGlasses(new Vector2(cap.Glasses[0], cap.Glasses[1]));
                root.CameraRig.FieldGlasses = true;
            }

            // The HUD, when asked for: its panel renders into its own transparent
            // texture (a copy of the panel settings, so the asset is untouched),
            // composited over the scene below. Headless there is no screen for
            // the overlay to draw on.
            var doc = FindAnyObjectByType<UIDocument>();
            RenderTexture uiRt = null;
            if (doc != null)
            {
                if (cap.NoUi) doc.gameObject.SetActive(false);
                else
                {
                    uiRt = new RenderTexture(cap.Width, cap.Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "capture ui" };
                    uiRt.Create();
                    var ps = Instantiate(doc.panelSettings);
                    ps.targetTexture = uiRt;
                    ps.clearColor = true;
                    ps.colorClearValue = new Color(0, 0, 0, 0);
                    doc.panelSettings = ps;
                }
            }

            if (!string.IsNullOrEmpty(cap.Screen))
            {
                var screens = FindAnyObjectByType<UI.Screens>();
                if (screens != null) screens.ShowForCapture(cap.Screen);
            }

            using var stats = new RenderStats();
            for (int i = 0; i < cap.Warmup; i++)
            {
                // One render per frame: TAA's history and jitter advance per frame.
                yield return null;
                RenderPipeline.SubmitRenderRequest(cam, request);
            }

            string dir = Path.GetDirectoryName(cap.Out);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tex = new Texture2D(cap.Width, cap.Height, TextureFormat.RGB24, false, false);
            for (int f = 0; f < cap.Frames; f++)
            {
                yield return null;
                RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, cap.Width, cap.Height), 0, 0);
                tex.Apply(false);
                RenderTexture.active = prev;
                if (uiRt != null) Composite(tex, uiRt);
                File.WriteAllBytes(PathFor(cap.Out, f, cap.Frames), tex.EncodeToPNG());
            }

            var st = root.Driver.State;
            string json = "{\n"
                + $"  \"capture\": \"{cap}\",\n"
                + $"  \"tick\": {st.Tick},\n"
                + $"  \"menAlive\": {Sim.Match.AliveCount(st, Sim.Side.Us) + Sim.Match.AliveCount(st, Sim.Side.Vc)},\n"
                + $"  \"renderCounters\": {(stats.Available ? "true" : "false")},\n"
                + $"  \"drawCalls\": {stats.DrawCalls},\n"
                + $"  \"srpBatcherDraws\": {stats.SrpBatcherDraws},\n"
                + $"  \"setPass\": {stats.SetPass},\n"
                + $"  \"triangles\": {stats.Triangles},\n"
                + $"  \"vertices\": {stats.Vertices},\n"
                + $"  \"shadowCasters\": {stats.ShadowCasters},\n"
                + $"  \"textureBytes\": {stats.TextureBytes},\n"
                + $"  \"videoMemoryBytes\": {stats.VideoMemoryBytes},\n"
                + $"  \"groundTriangles\": {root.GroundView.Triangles}\n"
                + "}\n";
            File.WriteAllText(Path.ChangeExtension(cap.Out, ".json"), json);
            Debug.Log($"[LOV] capture {cap.Out} ({cap}) — {stats}");

            cam.targetTexture = null;
            cam.enabled = true;
            Destroy(tex);
            rt.Release();
            Done = true;
        }

        /// <summary>
        /// Lay the HUD over the scene. The panel's texture holds premultiplied
        /// linear colour stored sRGB-encoded, so the blend happens in linear
        /// light: out = ui + scene * (1 - ui.alpha).
        /// </summary>
        private static void Composite(Texture2D scene, RenderTexture ui)
        {
            var uiTex = new Texture2D(ui.width, ui.height, TextureFormat.RGBA32, false, false);
            var prev = RenderTexture.active;
            RenderTexture.active = ui;
            uiTex.ReadPixels(new Rect(0, 0, ui.width, ui.height), 0, 0);
            RenderTexture.active = prev;
            var a = scene.GetPixels();
            var b = uiTex.GetPixels();
            for (int i = 0; i < a.Length; i++)
            {
                var s = a[i].linear; var u = b[i].linear;
                float k = 1f - b[i].a;
                a[i] = new Color(u.r + s.r * k, u.g + s.g * k, u.b + s.b * k, 1f).gamma;
            }
            scene.SetPixels(a);
            scene.Apply(false);
            Destroy(uiTex);
        }

        private static string PathFor(string path, int f, int frames)
            => frames <= 1 ? path
               : Path.Combine(Path.GetDirectoryName(path) ?? "",
                              $"{Path.GetFileNameWithoutExtension(path)}.{f.ToString(CultureInfo.InvariantCulture)}{Path.GetExtension(path)}");
    }
}
