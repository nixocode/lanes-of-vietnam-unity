using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LanesOfVietnam.View
{
    /// <summary>The detail the game draws at. Auto (appended: a saved choice keeps its number) is chosen by the frame time.</summary>
    public enum QualityTier { Low, Medium, High, Auto }

    /// <summary>
    /// What the player can set (PLAN §12.5): quality, volumes, subtitles,
    /// camera shake, the HUD. Saved with PlayerPrefs — IndexedDB in a browser —
    /// on a best-effort basis: the game works the same when storage is empty,
    /// blocked or throws.
    /// </summary>
    [Serializable]
    public sealed class GameSettings
    {
        /// <summary>Auto for anyone who has not chosen: the game finds what the machine can draw (AutoQuality).</summary>
        public QualityTier Quality = QualityTier.Auto;
        [Range(0, 1)] public float Master = 0.9f;
        [Range(0, 1)] public float Effects = 1f;
        [Range(0, 1)] public float Music = 0.7f;
        [Range(0, 1)] public float Ambience = 0.8f;
        [Range(0, 1)] public float Voice = 1f;
        public bool Subtitles = true;
        public bool CameraShake = true;
        /// <summary>Blood in quantity, and limbs taken off by a burst. Off: a little blood, and whole bodies.</summary>
        public bool Gore = true;
        public bool HideHud;

        private const string Key = "lov_settings";

        public static GameSettings Load()
        {
            try
            {
                string json = PlayerPrefs.GetString(Key, "");
                if (!string.IsNullOrEmpty(json)) return JsonUtility.FromJson<GameSettings>(json) ?? new GameSettings();
            }
            catch { /* storage blocked or corrupt: defaults */ }
            return new GameSettings();
        }

        public void Save()
        {
            try
            {
                PlayerPrefs.SetString(Key, JsonUtility.ToJson(this));
                PlayerPrefs.Save();
            }
            catch { /* best effort */ }
        }

        private static UniversalRenderPipelineAsset _runtimePipeline;

        /// <summary>
        /// Apply the quality tier to a runtime copy of the pipeline asset. A copy,
        /// because in the Editor changing the asset itself would rewrite the
        /// project file on disk from inside play mode.
        /// </summary>
        public void ApplyQuality()
        {
            if (_runtimePipeline == null)
            {
                var original = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (original == null) return;
                _runtimePipeline = UnityEngine.Object.Instantiate(original);
                _runtimePipeline.name = original.name + " (runtime)";
                QualitySettings.renderPipeline = _runtimePipeline;
            }
            switch (Quality)
            {
                // Auto starts at the top and AutoQuality takes it down from there, a step at a time.
                case QualityTier.Auto:
                    AutoQuality.Reset();
                    SetLevel(AutoQuality.Top);
                    break;
                case QualityTier.Low:
                    _runtimePipeline.renderScale = 0.72f;
                    _runtimePipeline.shadowDistance = 55f;
                    break;
                case QualityTier.Medium:
                    _runtimePipeline.renderScale = 0.86f;
                    _runtimePipeline.shadowDistance = 80f;
                    break;
                default:
                    _runtimePipeline.renderScale = 1f;
                    _runtimePipeline.shadowDistance = 110f;
                    break;
            }
        }

        /// <summary>One of AutoQuality's steps: the share of the screen's pixels drawn, and how far shadows reach.</summary>
        public static void SetLevel(int level)
        {
            if (_runtimePipeline == null) return;
            var (scale, shadows) = AutoQuality.Levels[Mathf.Clamp(level, 0, AutoQuality.Levels.Length - 1)];
            _runtimePipeline.renderScale = scale;
            _runtimePipeline.shadowDistance = shadows;
        }

        /// <summary>The render scale the current tier asks for, for the audit.</summary>
        public static float RuntimeRenderScale => _runtimePipeline != null ? _runtimePipeline.renderScale : 1f;
    }
}
