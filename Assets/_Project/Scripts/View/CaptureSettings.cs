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
        /// <summary>Show a screen for the capture: start, settings or end (the match must be over for end).</summary>
        public string Screen;
        /// <summary>Post exposure in stops, replacing the profile's for this capture; null keeps it. For tuning by measurement.</summary>
        public float? Ev;
        /// <summary>A colour filter (linear r:g:b) replacing the profile's, for tuning white balance; null keeps it.</summary>
        public float[] Tint;
        /// <summary>Tonemapper for this capture: aces or neutral; null keeps the profile's.</summary>
        public string Tone;
        /// <summary>The ground shader's debug view (1 albedo, 2 normal, 3 layers, 4 lit without normal map, 5 grey card); 0 off.</summary>
        public int GroundDebug;
        /// <summary>The ground shader's texture gradient scale (mip bias); 0 keeps the material's.</summary>
        public float GroundGrad;
        /// <summary>The plants' far mip bias (every map); negative keeps the material's.</summary>
        public float PlantBias = -1;
        /// <summary>
        /// Call-ins to play during the fast-forward, through the same command
        /// the player's card goes through: (card id, lane, x, tick). For
        /// looking at shells and smoke, which the computer's plans never call.
        /// </summary>
        public readonly System.Collections.Generic.List<(string card, int lane, double x, int tick)> Calls =
            new System.Collections.Generic.List<(string, int, double, int)>();
        /// <summary>The plans each side plays in the capture's match (Plan.ByName); null keeps the game's ceiling.</summary>
        public string UsPlan, VcPlan;
        /// <summary>Grenades on or off for the capture's match; null keeps the game's (on).</summary>
        public bool? Frag;
        /// <summary>Frames per second of match time between frames, for watching motion; 0 freezes time (the default).</summary>
        public float Movie;
        /// <summary>Lift, gamma, gain (their w: -1..1) for the grade, replacing the profile's; null keeps it.</summary>
        public float[] Lgg;
        /// <summary>Grade saturation (-100..100), replacing the profile's; null keeps it.</summary>
        public float? Sat;
        /// <summary>Fog density (exponential squared), replacing the scene's; null keeps it.</summary>
        public float? Fog;
        /// <summary>Extra stops on the sky alone (a graduated filter), on top of the scene's; null keeps it.</summary>
        public float? SkyEv;
        /// <summary>Squad smoke on or off for the capture's match; null keeps the game's (on).</summary>
        public bool? SquadSmoke;
        /// <summary>TAA base blend factor, variance clamp scale, and optionally jitter scale and quality (0-4); null keeps the camera's.</summary>
        public float[] Taa;

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
                    case "ev": c.Ev = float.Parse(v, inv); break;
                    case "ground": c.GroundDebug = int.Parse(v, inv); break;
                    case "groundgrad": c.GroundGrad = float.Parse(v, inv); break;
                    case "plantbias": c.PlantBias = float.Parse(v, inv); break;
                    case "usplan": c.UsPlan = v; break;
                    case "frag": c.Frag = v == "1" || v == "true"; break;
                    case "movie": c.Movie = float.Parse(v, inv); break;
                    case "sat": c.Sat = float.Parse(v, inv); break;
                    case "fog": c.Fog = float.Parse(v, inv); break;
                    case "skyev": c.SkyEv = float.Parse(v, inv); break;
                    case "lgg":
                        var lg = v.Split(':');
                        c.Lgg = new[] { float.Parse(lg[0], inv), float.Parse(lg[1], inv), float.Parse(lg[2], inv) };
                        break;
                    case "squadsmoke": c.SquadSmoke = v == "1" || v == "true"; break;
                    case "vcplan": c.VcPlan = v; break;
                    case "call":
                        // call=card:lane:x:tick, repeatable
                        var cl = v.Split(':');
                        c.Calls.Add((cl[0], int.Parse(cl[1], inv), double.Parse(cl[2], inv), int.Parse(cl[3], inv)));
                        break;
                    case "taa":
                        var ta = v.Split(':');
                        c.Taa = System.Array.ConvertAll(ta, t => float.Parse(t, inv));
                        break;
                    case "tint":
                        var t = v.Split(':');
                        c.Tint = new[] { float.Parse(t[0], inv), float.Parse(t[1], inv), float.Parse(t[2], inv) };
                        break;
                    case "tone": c.Tone = v; break;
                    case "nopost": c.NoPost = v == "1" || v == "true"; break;
                    case "ui": c.NoUi = !(v == "1" || v == "true"); break;
                    case "sel": c.Select = v == "tab" ? -2 : int.Parse(v, inv); break;
                    case "glasses":
                    {
                        var xy = v.Split(':');
                        c.Glasses = new[] { float.Parse(xy[0], inv), float.Parse(xy[1], inv) };
                        break;
                    }
                    case "screen":
                        if (v != "start" && v != "settings" && v != "end")
                            throw new ArgumentException($"capture: screen must be start, settings or end, not \"{v}\"");
                        c.Screen = v;
                        break;
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
