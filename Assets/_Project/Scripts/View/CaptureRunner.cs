using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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

        private static string PathFor(string path, int f, int frames)
            => frames <= 1 ? path
               : Path.Combine(Path.GetDirectoryName(path) ?? "",
                              $"{Path.GetFileNameWithoutExtension(path)}.{f.ToString(CultureInfo.InvariantCulture)}{Path.GetExtension(path)}");
    }
}
