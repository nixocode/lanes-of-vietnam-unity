using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LanesOfVietnam.View
{
    public enum QualityTier { Low, Medium, High }

    /// <summary>
    /// What the player can set (PLAN §12.5): quality, volumes, subtitles,
    /// camera shake, the HUD. Saved with PlayerPrefs — IndexedDB in a browser —
    /// on a best-effort basis: the game works the same when storage is empty,
    /// blocked or throws.
    /// </summary>
    [Serializable]
    public sealed class GameSettings
    {
        public QualityTier Quality = QualityTier.High;
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

        /// <summary>The render scale the current tier asks for, for the audit.</summary>
        public static float RuntimeRenderScale => _runtimePipeline != null ? _runtimePipeline.renderScale : 1f;
    }
}
