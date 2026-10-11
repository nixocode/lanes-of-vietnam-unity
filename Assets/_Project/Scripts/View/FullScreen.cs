using System.Runtime.InteropServices;
using UnityEngine;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// The game's picture filling the screen (the owner, 2026-10-11: "need a fullscreen mode for chrome").
    ///
    /// In a browser the page itself is asked to go full screen, not Unity's canvas through
    /// <c>Screen.fullScreen</c>: Unity puts that request off until the next click, so a button for it took two.
    /// A page may ask only in the moment after a click or a key (the browser's rule); the game's own frame is
    /// inside that moment. Refused, nothing happens. Esc leaves it, as in any page: the browser's doing.
    /// </summary>
    public static class FullScreen
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void LovInput_Fullscreen(int on);
        [DllImport("__Internal")] private static extern int LovInput_IsFullscreen();

        public static bool On => LovInput_IsFullscreen() != 0;

        public static void Set(bool on) => LovInput_Fullscreen(on ? 1 : 0);
#else
        private static bool _on;

        public static bool On => _on;

        public static void Set(bool on)
        {
            _on = on;
            if (!Application.isEditor) Screen.fullScreen = on;
        }
#endif

        public static void Toggle() => Set(!On);

        /// <summary>Alt (Option) and Enter together, this frame: the keys for it.</summary>
        public static bool KeyDown => Input.GetKeyDown(KeyCode.Return) && AltHeld;

        public static bool AltHeld => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
    }
}
