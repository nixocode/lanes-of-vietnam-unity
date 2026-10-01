using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LanesOfVietnam.Sim;
using LanesOfVietnam.View.UI;
using UnityEngine;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// A frame-time probe for the browser (PLAN §12.3's 4 ms sub-budget is
    /// measured "in a WebGL build, not the Editor"). With <c>?perf=1</c> in the
    /// page address the game deploys the US at once, fast-forwards into the
    /// fight (<c>&amp;ff=</c> ticks, default 600) and logs to the console every
    /// five seconds: frame time (mean, median, 95th percentile), what stepping
    /// the men cost, and how many there are. <c>?perf=sprites</c> draws the
    /// baked sprites instead of the 3D men: the difference between the two
    /// runs is what the 3D men cost, skinning and draws included.
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

        private IEnumerator Start()
        {
            string mode = Param("perf");
            var root = GameRoot.Instance;
            if (mode == "sprites") { root.ArmyView.UsFigures = new SoldierFigure[0]; root.ArmyView.VcFigures = new SoldierFigure[0]; }
            yield return null;
            var screens = FindAnyObjectByType<Screens>();
            if (screens != null) { screens.ChooseSide(Side.Us); screens.Deploy(); screens.Skip(); }
            int ff = int.TryParse(Param("ff"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 600;
            root.Driver.FastForward(ff);
            yield return null;
            string what = root.ArmyView.UsFigures.Length > 0 ? "3D men" : "sprites";
            Debug.Log($"[LOV] perf: {what}, fast-forwarded {ff} ticks; {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceVersion}), " +
                      $"{Screen.width}x{Screen.height}");
            var frames = new List<float>();
            float army = 0, t0 = Time.realtimeSinceStartup;
            int stepped = 0;
            while (true)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000f);
                army += root.ArmyView.LastDrawMs;
                stepped += root.ArmyView.Stepped;
                if (Time.realtimeSinceStartup - t0 < 5f) continue;
                frames.Sort();
                int n = frames.Count;
                var st = root.Driver.State;
                Debug.Log($"[LOV] perf: {what}: {n} frames, mean {frames.Average():F1} ms, median {frames[n / 2]:F1}, " +
                          $"p95 {frames[(int)(n * 0.95f)]:F1}; men {st.Men.Count} ({st.Men.Count(m => m.Alive)} alive), " +
                          $"army view {army / n:F2} ms a frame, {stepped / (float)n:F1} men stepped a frame; tick {st.Tick}");
                frames.Clear();
                army = 0; stepped = 0;
                t0 = Time.realtimeSinceStartup;
            }
        }
    }
}
