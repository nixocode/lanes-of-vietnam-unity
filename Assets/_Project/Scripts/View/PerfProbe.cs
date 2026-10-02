using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LanesOfVietnam.Sim;
using LanesOfVietnam.View.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// A frame-time probe for the browser (PLAN §12.3's 4 ms sub-budget is
    /// measured "in a WebGL build, not the Editor"). With <c>?perf=1</c> in the
    /// page address the game deploys the US at once, fast-forwards into the
    /// fight (<c>&amp;ff=</c> ticks, default 600) and logs to the console every
    /// five seconds: frame time (mean, median, 95th percentile), what each of
    /// the game's own systems cost (the simulation, the men, the fighting's
    /// effects, the interface), how many frames ran long and how many of those
    /// had a garbage collection or a tick of the simulation in them.
    ///
    /// <c>&amp;off=a,b</c> switches parts of the frame off, so what each costs
    /// is the difference between two runs of one build: <c>post</c>, <c>aa</c>,
    /// <c>bloom</c>, <c>shadows</c>, <c>soft</c> (hard shadows), <c>cookie</c>
    /// (the clouds' shadows), <c>depth</c> (the depth texture), <c>hdr</c>,
    /// <c>aniso</c> (anisotropic filtering; <c>anisotex</c> leaves each texture
    /// its own level, <c>anisoforce</c> forces every texture up), <c>hud</c>, <c>combat</c>, and <c>mat:name</c> (every
    /// renderer whose material's name contains it: <c>mat:Ground</c>, which is
    /// how the ground was found to be most of the frame). <c>&amp;scale=0.8</c>
    /// sets the render scale, <c>&amp;speed=3</c> the match's, and
    /// <c>&amp;card=us-arty</c> holds a card, as a player choosing where to
    /// put it does.
    /// </summary>
    public sealed class PerfProbe : MonoBehaviour
    {
        public static string Param(string key)
        {
            string url = Application.absoluteURL ?? "";
            int q = url.IndexOf('?');
            if (q < 0) return null;
            foreach (var kv in url.Substring(q + 1).Split('&'))
            {
                int e = kv.IndexOf('=');
                if (e > 0 && kv.Substring(0, e) == key) return kv.Substring(e + 1);
                if (e < 0 && kv == key) return "";
            }
            return null;
        }

        private static float Number(string key, float otherwise)
            => float.TryParse(Param(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : otherwise;

        /// <summary>Switch off what <c>off=</c> names. Returns what it did, for the log.</summary>
        private static string SwitchOff(GameRoot root, string list)
        {
            var done = new List<string>();
            var cam = root.CameraRig.Camera;
            var camData = cam.GetComponent<UniversalAdditionalCameraData>();
            var pipeline = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            var sun = RenderSettings.sun != null ? RenderSettings.sun : FindObjectsByType<Light>().FirstOrDefault(l => l.type == LightType.Directional);
            foreach (string what in (list ?? "").Split(',').Where(s => s.Length > 0))
            {
                switch (what)
                {
                    case "post": camData.renderPostProcessing = false; break;
                    case "aa": camData.antialiasing = AntialiasingMode.None; break;
                    case "bloom":
                        foreach (var v in FindObjectsByType<Volume>())
                            if (v.profile.TryGet<Bloom>(out var bloom)) bloom.active = false;
                        break;
                    case "shadows": if (pipeline != null) pipeline.shadowDistance = 0f; break;
                    case "soft": if (sun != null) sun.shadows = LightShadows.Hard; break;
                    case "cookie":
                        foreach (var c in FindObjectsByType<CloudShadows>()) c.enabled = false;
                        if (sun != null) sun.cookie = null;
                        break;
                    case "depth":
                        if (pipeline != null) pipeline.supportsCameraDepthTexture = false;
                        camData.requiresDepthOption = CameraOverrideOption.Off;
                        break;
                    case "hdr": cam.allowHDR = false; break;
                    // Anisotropic filtering: none at all, and each texture's own level rather than every texture's forced up.
                    case "aniso": QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable; break;
                    case "anisotex": QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable; break;
                    case "anisoforce": QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable; break;
                    case "hud": foreach (var h in FindObjectsByType<Hud>()) h.SetVisible(false); break;
                    case "combat":
                        // (The component and what it draws, not its object: that is the game's own.)
                        foreach (var c in FindObjectsByType<CombatView>())
                        {
                            c.enabled = false;
                            foreach (Transform child in c.transform)
                                if (child.name.StartsWith("combat") || child.name.StartsWith("ground marks") || child.name.StartsWith("shell flash"))
                                    child.gameObject.SetActive(false);
                        }
                        break;
                    default:
                        if (what.StartsWith("ui:"))
                        {
                            // One part of the interface, by its element's name: ui:strip, ui:plates, ui:squad-tags, ui:topbar, ui:bottombar.
                            int hidden = 0;
                            foreach (var doc in FindObjectsByType<UnityEngine.UIElements.UIDocument>())
                                foreach (var el in UnityEngine.UIElements.UQueryExtensions.Query(doc.rootVisualElement, what.Substring(3)).ToList())
                                { el.style.display = UnityEngine.UIElements.DisplayStyle.None; hidden++; }
                            done.Add($"{what} ({hidden} elements)");
                            continue;
                        }
                        if (!what.StartsWith("mat:")) continue;
                        string name = what.Substring(4);
                        int n = 0;
                        foreach (var r in FindObjectsByType<Renderer>())
                            if (r.sharedMaterial != null && r.sharedMaterial.name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0) { r.enabled = false; n++; }
                        done.Add($"{what} ({n} renderers)");
                        continue;
                }
                done.Add(what);
            }
            return done.Count == 0 ? "" : " off: " + string.Join(", ", done) + ";";
        }

        private IEnumerator Start()
        {
            var root = GameRoot.Instance;
            yield return null;
            var screens = FindAnyObjectByType<Screens>();
            if (screens != null) { screens.ChooseSide(Side.Us); screens.Deploy(); screens.Skip(); }
            int ff = int.TryParse(Param("ff"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 600;
            root.Driver.FastForward(ff);
            yield return null;
            string off = SwitchOff(root, Param("off"));
            float scale = Number("scale", 0f);
            if (scale > 0 && QualitySettings.renderPipeline is UniversalRenderPipelineAsset urp) { urp.renderScale = scale; off += $" render scale {scale:F2};"; }
            root.Speed = Mathf.Clamp(Number("speed", 1f), 1f, 3f);
            string card = Param("card");
            var deployer = FindAnyObjectByType<Deployer>();
            var held = card != null ? Deck.For(Side.Us).FirstOrDefault(c => c.Id == card) : null;
            var hud = FindAnyObjectByType<Hud>();
            var combat = FindAnyObjectByType<CombatView>();
            Debug.Log($"[LOV] perf: fast-forwarded {ff} ticks;{off} {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceVersion}), " +
                      $"{Screen.width}x{Screen.height}");
            var frames = new List<float>();
            var ticked = new List<bool>();
            var collected = new List<bool>();
            float army = 0, sim = 0, fx = 0, ui = 0, t0 = Time.realtimeSinceStartup;
            int stepped = 0, tick = root.Driver.State.Tick, gcs = System.GC.CollectionCount(0);
            bool tickedLast = false, collectedLast = false;
            while (true)
            {
                // A card in hand, the pointer moving along the lane: what choosing where to put it costs.
                if (held != null && deployer != null) deployer.Hold(held, 0, root.CameraRig.X + Mathf.Sin(Time.realtimeSinceStartup) * 20f);
                yield return null;
                // The time read here is the last frame's, start to start: set it against what that frame did.
                frames.Add(Time.unscaledDeltaTime * 1000f);
                ticked.Add(tickedLast);
                collected.Add(collectedLast);
                army += root.ArmyView.LastDrawMs;
                sim += root.LastSimMs;
                if (combat != null) fx += combat.LastMs;
                if (hud != null) ui += hud.LastMs;
                stepped += root.ArmyView.Stepped;
                tickedLast = root.Driver.State.Tick != tick;
                tick = root.Driver.State.Tick;
                int gc = System.GC.CollectionCount(0);
                collectedLast = gc != gcs;
                gcs = gc;
                if (Time.realtimeSinceStartup - t0 < 5f) continue;
                int n = frames.Count;
                var sorted = new List<float>(frames);
                sorted.Sort();
                float median = sorted[n / 2];
                // A long frame: half as long again as the usual one, and a frame of a 120 Hz screen missed.
                int longFrames = 0, longWithGc = 0, longWithTick = 0, gcFrames = 0;
                for (int i = 0; i < n; i++)
                {
                    if (collected[i]) gcFrames++;
                    if (frames[i] < median * 1.5f) continue;
                    longFrames++;
                    if (collected[i]) longWithGc++;
                    if (ticked[i]) longWithTick++;
                }
                var st = root.Driver.State;
                Debug.Log($"[LOV] perf: {n} frames, mean {frames.Average():F1} ms, median {median:F1}, " +
                          $"p95 {sorted[(int)(n * 0.95f)]:F1}, max {sorted[n - 1]:F0}; long {longFrames} ({longWithGc} with a collection, {longWithTick} with a tick), " +
                          $"collections {gcFrames}; sim {sim / n:F2} ms a frame, army view {army / n:F2}, combat view {fx / n:F2}, hud {ui / n:F2}; " +
                          $"men {st.Men.Count} ({st.Men.Count(m => m.Alive)} alive), {stepped / (float)n:F1} stepped a frame, events {st.Events.Count}; tick {st.Tick}");
                frames.Clear(); ticked.Clear(); collected.Clear();
                army = sim = fx = ui = 0; stepped = 0;
                t0 = Time.realtimeSinceStartup;
            }
        }
    }
}
