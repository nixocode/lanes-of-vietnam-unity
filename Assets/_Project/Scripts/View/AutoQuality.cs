using UnityEngine;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// QUALITY: AUTO (GameSettings). The owner, 2026-10-14: friends playing the demo had "the frames dropped to very
    /// low"; "add the automatic detail, but also have manual settings".
    ///
    /// Everything is drawn in the player's own browser on his own graphics, so a laptop's chip can take five to ten
    /// times as long as the Mac the game is measured on. What costs most on a weak chip is pixels: the screen's
    /// resolution, and on a Retina or 4K screen there are four times as many. So Auto watches the time each frame
    /// takes and draws fewer pixels when they come too slowly: a step down, from all of them to half in each
    /// direction (a quarter of the pixels), the shadows' reach shortened toward the bottom. When frames come at the
    /// screen's own pace for a while, it tries a step up again; a step that was too much is not tried again for a
    /// time. The picture the camera frames is the same at every step; it is only sharper or softer.
    ///
    /// Manual LOW, MEDIUM and HIGH stay as they are and are never changed by it.
    /// </summary>
    public sealed class AutoQuality : MonoBehaviour
    {
        /// <summary>The steps: the share of the screen's resolution drawn, and shadow distance in metres. Step 0 is the top.</summary>
        public static readonly (float scale, float shadows)[] Levels =
        {
            (1.00f, 110f), (0.90f, 110f), (0.80f, 90f), (0.70f, 75f), (0.60f, 60f), (0.50f, 45f),
        };
        public const int Top = 0;

        /// <summary>
        /// Milliseconds a frame, smoothed, above which it steps down (under 45 frames a second), and below which it may
        /// step up (at a 60 Hz screen's own pace, which a browser holds frames to); and the seconds each must hold.
        /// </summary>
        public const float SlowMs = 22f, EasyMs = 18.5f, SlowFor = 1.5f, EasyFor = 6f;
        /// <summary>Seconds a step that proved too much is not tried again.</summary>
        public const float Burned = 30f;

        public static int Level { get; private set; }
        /// <summary>The share of the screen's resolution drawn now, for the settings' button.</summary>
        public static float Scale => Levels[Level].scale;

        private static float _ema = 16.7f, _slow, _easy;
        private static readonly float[] _failedAt = new float[Levels.Length];

        public static void Reset()
        {
            Level = Top;
            _ema = 16.7f; _slow = _easy = 0;
            for (int i = 0; i < _failedAt.Length; i++) _failedAt[i] = -1000f;
        }

        private void Update()
        {
            var root = GameRoot.Instance;
            if (root == null || root.Settings == null || root.Settings.Quality != QualityTier.Auto) return;
            // (Not while a capture or the frame-time probe is measuring: they set their own.)
            if (CaptureSettings.Active != null || PerfProbe.Running) return;
            Feed(Time.unscaledDeltaTime * 1000f, Time.unscaledTime);
        }

        /// <summary>One frame's time (ms) at a moment (s): the whole of what Auto decides from. Public for the tests.</summary>
        public static void Feed(float ms, float now)
        {
            if (ms <= 0f || ms > 250f) return;               // a hitch (a tab put away, a load) is not the pace
            float dt = ms / 1000f;
            _ema += (ms - _ema) * 0.1f;
            _slow = _ema > SlowMs ? _slow + dt : 0f;
            _easy = _ema < EasyMs ? _easy + dt : 0f;
            if (_slow > SlowFor && Level < Levels.Length - 1)
            {
                _failedAt[Level] = now;
                Step(Level + 1);
            }
            else if (_easy > EasyFor && Level > Top && now - _failedAt[Level - 1] > Burned)
                Step(Level - 1);
        }

        private static void Step(int to)
        {
            Level = to;
            GameSettings.SetLevel(to);
            _slow = _easy = 0f;
            _ema = 16.7f;
            Debug.Log($"[LOV] auto quality: {Levels[to].scale * 100:F0}% of the screen's resolution, shadows {Levels[to].shadows:F0} m");
        }
    }
}
