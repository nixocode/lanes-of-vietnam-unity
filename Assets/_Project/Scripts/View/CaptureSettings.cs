using System;
using System.Globalization;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// A frozen moment to render: which match, which tick, where the camera is.
    ///
    /// Brief §10: every visual change is A/B'd from an identical frozen frame,
    /// because half the three.js project's visual arguments were lost to
    /// comparing two different moments. When this is set, the game does not
    /// advance on its own: it fast-forwards the simulation to <see cref="Tick"/>,
    /// holds it there, fixes the view clock (wind, flicker, anything animated)
    /// at <see cref="ViewTime"/>, and renders the same frame every time.
    ///
    /// Set by the capture harness from <c>-lovCapture key=value,...</c>.
    /// </summary>
    public sealed class CaptureSettings
    {
        public int Seed = 3;
        public int Tick = 700;
        public float CameraX;
        public float Dolly;
        /// <summary>Reference aspect: TARGET.jpg is 2772 x 1504.</summary>
        public int Width = 1843;
        public int Height = 1000;
        /// <summary>Frames rendered before the one kept, so temporal effects have converged.</summary>
        public int Warmup = 48;
        public float ViewTime = 10f;
        public string Out = "captures/frame.png";
        /// <summary>Frames to keep: 2 or more for the flicker measurement.</summary>
        public int Frames = 1;
        /// <summary>Render with no post-processing, for isolating a change.</summary>
        public bool NoPost;
        /// <summary>Hide the interface.</summary>
        public bool NoUi = true;
        /// <summary>
        /// Antialiasing for the capture: taa (the game's), smaa, fxaa, none.
        /// "none" is the flicker instrument's zero: two renders of a frozen
        /// scene with nothing temporal on must be identical.
        /// </summary>
        public string Aa = "taa";
        /// <summary>Select this squad (id) before rendering, to see its rings; -1 for none, -2 for Tab's first pick.</summary>
        public int Select = -1;
        /// <summary>Raise the field glasses toward this viewport point (x,y in 0..1); null for none.</summary>
        public float[] Glasses;

        /// <summary>The active capture, or null when the game is being played.</summary>
        public static CaptureSettings Active;

        public static CaptureSettings Parse(string spec)
        {
            var c = new CaptureSettings();
            if (string.IsNullOrWhiteSpace(spec)) return c;
            foreach (var part in spec.Split(','))
            {
                var kv = part.Split(new[] { '=' }, 2);
                if (kv.Length != 2) throw new ArgumentException($"capture: \"{part}\" is not key=value");
                string k = kv[0].Trim(), v = kv[1].Trim();
                var inv = CultureInfo.InvariantCulture;
                switch (k)
                {
                    case "seed": c.Seed = int.Parse(v, inv); break;
                    case "tick": c.Tick = int.Parse(v, inv); break;
                    case "x": c.CameraX = float.Parse(v, inv); break;
                    case "dolly": c.Dolly = float.Parse(v, inv); break;
                    case "w": c.Width = int.Parse(v, inv); break;
                    case "h": c.Height = int.Parse(v, inv); break;
                    case "warmup": c.Warmup = int.Parse(v, inv); break;
                    case "time": c.ViewTime = float.Parse(v, inv); break;
                    case "out": c.Out = v; break;
                    case "frames": c.Frames = int.Parse(v, inv); break;
                    case "nopost": c.NoPost = v == "1" || v == "true"; break;
                    case "ui": c.NoUi = !(v == "1" || v == "true"); break;
                    case "sel": c.Select = v == "tab" ? -2 : int.Parse(v, inv); break;
                    case "glasses":
                    {
                        var xy = v.Split(':');
                        c.Glasses = new[] { float.Parse(xy[0], inv), float.Parse(xy[1], inv) };
                        break;
                    }
                    case "aa":
                        if (v != "taa" && v != "smaa" && v != "fxaa" && v != "none")
                            throw new ArgumentException($"capture: aa must be taa, smaa, fxaa or none, not \"{v}\"");
                        c.Aa = v;
                        break;
                    // An unknown key is an error, not a no-op. The three.js
                    // harness silently dropped "--wind 0" ('0' is falsy) and
                    // three flicker diagnoses in a row were wrong because of it.
                    default: throw new ArgumentException($"capture: unknown key \"{k}\"");
                }
            }
            return c;
        }

        public override string ToString()
            => $"seed={Seed},tick={Tick},x={CameraX.ToString(CultureInfo.InvariantCulture)},"
             + $"w={Width},h={Height},warmup={Warmup},time={ViewTime.ToString(CultureInfo.InvariantCulture)},aa={Aa}";
    }
}
