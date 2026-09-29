using System.IO;
using System.Linq;
using LanesOfVietnam.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace LanesOfVietnam.Tools
{
    /// <summary>
    /// Builds <c>Main.unity</c> from code.
    ///
    /// <code>tools/unity.sh -nographics -executeMethod LanesOfVietnam.Tools.SceneBuilder.BuildMain</code>
    ///
    /// The scene is small on purpose — a camera, a sun, the post stack and
    /// the game object — because the world itself is built at load from the
    /// map (see <see cref="GameRoot"/>). What lives here is what has to be an
    /// asset: materials, the volume profile, the lighting settings. All of it
    /// is written by this file, so the scene is reproducible from a diff
    /// rather than from a history of clicks nobody can see.
    /// </summary>
    public static class SceneBuilder
    {
        public const string ScenePath = Build.MainScene;
        public const string MaterialDir = "Assets/_Project/Art/Materials/GreyBox";
        public const string PostProfilePath = ProjectSetup.SettingsDir + "/Post.asset";

        [MenuItem("Lanes of Vietnam/Build Main Scene")]
        public static void BuildMain()
        {
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- light and atmosphere ------------------------------------------
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            // Bright, hazy, high: brief §4 wants overcast-lit daylight with soft
            // shadows and no hard raking sun. From behind the camera's left
            // shoulder, so faces toward the lens are lit and the men model.
            sunGo.transform.rotation = Quaternion.Euler(52f, 28f, 0f);
            sun.color = new Color(1.0f, 0.95f, 0.86f);
            sun.intensity = 2.2f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.72f;
            sunGo.AddComponent<UniversalAdditionalLightData>();

            RenderSettings.sun = sun;
            RenderSettings.skybox = SkyMaterial();
            // Ambient as three colours for now: sky above, haze at the horizon,
            // bounced earth below. The art pass derives it from the sky itself.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.70f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.58f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.24f, 0.19f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0042f;
            RenderSettings.fogColor = new Color(0.66f, 0.72f, 0.76f);

            // --- the camera ------------------------------------------------------
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = Coords.Camera.Fov;
            cam.allowHDR = true;
            cam.allowMSAA = false;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.TemporalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;
            camData.renderShadows = true;
            camData.requiresDepthOption = CameraOverrideOption.On;
            var rig = camGo.AddComponent<CameraRig>();
            camGo.AddComponent<AudioListener>();

            // --- post -----------------------------------------------------------
            var postGo = new GameObject("Post");
            var vol = postGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0;
            vol.sharedProfile = PostProfile();

            // --- the game -----------------------------------------------------------
            var game = new GameObject("Game");
            var root = game.AddComponent<GameRoot>();
            root.CameraRig = rig;

            var groundGo = new GameObject("Ground");
            groundGo.transform.SetParent(game.transform, false);
            var ground = groundGo.AddComponent<GroundView>();
            ground.Material = Mat("Ground", Shader.Find("LOV/Ground"), null);
            root.GroundView = ground;

            var coverGo = new GameObject("Cover");
            coverGo.transform.SetParent(game.transform, false);
            var cover = coverGo.AddComponent<CoverView>();
            cover.SandbagMaterial = Lit("Sandbag", new Color(0.46f, 0.40f, 0.29f), 0.1f);
            cover.TimberMaterial = Lit("Timber", new Color(0.30f, 0.23f, 0.16f), 0.1f);
            root.CoverView = cover;

            var armyGo = new GameObject("Army");
            armyGo.transform.SetParent(game.transform, false);
            var army = armyGo.AddComponent<ArmyView>();
            army.UsMaterial = Lit("US", new Color(0.24f, 0.26f, 0.17f), 0.15f);
            army.VcMaterial = Lit("VC", new Color(0.07f, 0.07f, 0.07f), 0.2f);
            army.DeadMaterial = Lit("Dead", new Color(0.16f, 0.14f, 0.12f), 0.1f);
            root.ArmyView = army;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[LOV] scene built: {ScenePath}");
        }

        private static Material SkyMaterial()
        {
            var m = Mat("Sky", Shader.Find("Skybox/Procedural"), null);
            m.SetFloat("_SunSize", 0.02f);
            m.SetFloat("_AtmosphereThickness", 0.72f);
            m.SetColor("_SkyTint", new Color(0.62f, 0.66f, 0.70f));
            m.SetColor("_GroundColor", new Color(0.40f, 0.42f, 0.40f));
            m.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static VolumeProfile PostProfile()
        {
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProfilePath);
            if (p == null)
            {
                p = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(p, PostProfilePath);
            }
            foreach (var c in p.components.ToArray()) { p.Remove(c.GetType()); Object.DestroyImmediate(c, true); }

            // A filmic curve, a bloom with a tight threshold so only the sky and
            // flashes bloom, and a vignette. PLAN §6's stack, kept small: each
            // piece subtle enough that turning it off is noticeable but turning
            // it on is not obvious.
            var tone = p.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = p.Add<Bloom>(true);
            bloom.threshold.Override(1.15f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.6f);
            var vig = p.Add<Vignette>(true);
            vig.intensity.Override(0.22f);
            vig.smoothness.Override(0.45f);
            var col = p.Add<ColorAdjustments>(true);
            col.postExposure.Override(0.0f);
            col.contrast.Override(8f);
            col.saturation.Override(0f);
            foreach (var c in p.components) AssetDatabase.AddObjectToAsset(c, p);
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            return p;
        }

        private static Material Lit(string name, Color c, float smoothness)
        {
            var m = Mat(name, Shader.Find("Universal Render Pipeline/Lit"), null);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Mat(string name, Shader shader, Material _)
        {
            if (shader == null) throw new System.Exception($"shader for {name} not found");
            string path = $"{MaterialDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else m.shader = shader;
            return m;
        }
    }
}
