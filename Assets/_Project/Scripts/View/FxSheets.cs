using UnityEngine;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// One sheet of frames in an atlas of the fighting's effects: where it sits and
    /// how its frames are laid out, left to right and top to bottom.
    /// </summary>
    public readonly struct FxSheet
    {
        public readonly int Frames, Cols;
        /// <summary>Its last frame runs into its first: smoke that hangs, a flame that burns. Else it plays once and holds its last frame.</summary>
        public readonly bool Loops;
        private readonly float _u, _v, _w, _h, _pu, _pv;

        /// <summary>In pixels from the atlas's top left, as tools/art/fx_atlas.py lays them: a frame's place and size, the pitch to the next.</summary>
        public FxSheet(int x, int y, int w, int h, int px, int py, int cols, int frames, bool loops, int atlasW, int atlasH)
        {
            Frames = frames; Cols = cols; Loops = loops;
            // Half a texel in from the frame's edge, so that a filtered sample never reaches the frame beside it.
            _u = (x + 0.5f) / atlasW; _w = (w - 1f) / atlasW; _pu = (float)px / atlasW;
            _v = 1f - (y + h - 0.5f) / atlasH; _h = (h - 1f) / atlasH; _pv = (float)py / atlasH;
        }

        /// <summary>A frame's corners in the atlas: left, bottom, right, top.</summary>
        public Vector4 Rect(int frame)
        {
            frame = Loops ? (frame % Frames + Frames) % Frames : Mathf.Clamp(frame, 0, Frames - 1);
            float u = _u + frame % Cols * _pu, v = _v - frame / Cols * _pv;
            return new Vector4(u, v, u + _w, v + _h);
        }
    }

    /// <summary>
    /// The sheets in the two atlases CombatView draws from (Art/FX/, made by
    /// tools/art/fx_atlas.py from Unity Labs' CC0 flipbooks; the numbers here are
    /// that script's SHEETS: change one, change the other).
    /// </summary>
    public static class FxSheets
    {
        private const int SmokeW = 2048, SmokeH = 2048, GlowW = 2048, GlowH = 1024;

        // fx_smoke.png: drawn over the scene.
        /// <summary>A burst of smoke with no fire in it, from nothing to its widest and thinning: dirt kicked up, a grenade's dust, a shell's earth.</summary>
        public static readonly FxSheet Dust = new FxSheet(2, 2, 200, 200, 204, 204, 5, 25, false, SmokeW, SmokeH);
        /// <summary>A ball of fire going to black smoke.</summary>
        public static readonly FxSheet Blast = new FxSheet(1026, 2, 200, 200, 204, 204, 5, 25, false, SmokeW, SmokeH);
        /// <summary>Smoke that hangs and turns over on itself: gun smoke, a shell's column, a smoke screen.</summary>
        public static readonly FxSheet Wisp = new FxSheet(0, 1024, 128, 128, 128, 128, 8, 64, true, SmokeW, SmokeH);
        /// <summary>A thin thread of smoke rising: off a hot barrel.</summary>
        public static readonly FxSheet Curl = new FxSheet(1024, 1024, 51, 128, 51, 128, 20, 80, true, SmokeW, SmokeH);
        /// <summary>Eight specks: four clods of earth (0 to 3), two round drops (4, 5), two drawn out (6, 7).</summary>
        public static readonly FxSheet Speck = new FxSheet(1024, 1536, 64, 64, 64, 64, 8, 8, false, SmokeW, SmokeH);
        /// <summary>A spent case: side on (0), and a little end on (1).</summary>
        public static readonly FxSheet Brass = new FxSheet(1536, 1536, 64, 64, 64, 64, 2, 2, false, SmokeW, SmokeH);

        // fx_glow.png: added to the scene as light.
        /// <summary>The fire of <see cref="Blast"/>, without its smoke.</summary>
        public static readonly FxSheet Fire = new FxSheet(2, 2, 200, 200, 204, 204, 5, 25, false, GlowW, GlowH);
        /// <summary>A ball of flame, burning.</summary>
        public static readonly FxSheet Flame = new FxSheet(1024, 0, 128, 128, 128, 128, 8, 32, true, GlowW, GlowH);
        /// <summary>The gas out of a muzzle from the side, the muzzle at the left: four long and thin (0 to 3), four short and fat (4 to 7).</summary>
        public static readonly FxSheet Tongue = new FxSheet(1024, 512, 128, 64, 128, 64, 8, 8, false, GlowW, GlowH);
        /// <summary>The same down the barrel, or out through a flash hider's slots: a ball with three, four, five or six spikes (by its number, twice over).</summary>
        public static readonly FxSheet Star = new FxSheet(1024, 576, 128, 128, 128, 128, 8, 8, false, GlowW, GlowH);
    }
}
