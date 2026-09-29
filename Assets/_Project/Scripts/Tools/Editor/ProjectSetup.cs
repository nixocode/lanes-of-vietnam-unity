using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LanesOfVietnam.Tools
{
    /// <summary>
    /// Every project-wide setting this game depends on, applied from code.
    ///
    /// The last session scaffolded "a URP project" and the settings said
    /// otherwise: no pipeline asset was assigned anywhere, so the project was
    /// rendering with the built-in pipeline, and the colour space was Gamma.
    /// Both are one click in a settings window and both are invisible until a
    /// frame looks wrong for reasons nobody can find. So the settings live
    /// here, where a diff shows them and a headless run can apply them:
    ///
    /// <code>tools/unity.sh -nographics -executeMethod LanesOfVietnam.Tools.ProjectSetup.Apply</code>
    ///
    /// Idempotent. Every value set by name is checked, and a name that does
    /// not resolve is reported rather than skipped — a renamed field in a
    /// package update would otherwise turn a setting off in silence.
    /// </summary>
    public static class ProjectSetup
    {
        public const string SettingsDir = "Assets/_Project/Settings";
        public const string PipelinePath = SettingsDir + "/URP-Web.asset";
        public const string RendererPath = SettingsDir + "/URP-Web_Renderer.asset";

        private static readonly List<string> Missing = new List<string>();

        [MenuItem("Lanes of Vietnam/Apply Project Setup")]
        public static void Apply()
        {
            Missing.Clear();
            var pipeline = EnsurePipeline();
            ConfigurePipeline(pipeline);
            ConfigureRenderer(AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath));
            AssignPipeline(pipeline);
            ConfigureQuality(pipeline);
            ConfigurePlayer();
            ConfigurePhysics();
            AssetDatabase.SaveAssets();

            Debug.Log($"[LOV] setup: pipeline {AssetDatabase.GetAssetPath(GraphicsSettings.defaultRenderPipeline)}, "
                      + $"colour space {PlayerSettings.colorSpace}, "
                      + $"quality levels {QualitySettings.names.Length} ({string.Join(",", QualitySettings.names)})");
            if (Missing.Count > 0)
            {
                // Loud, and a failure: a setting that did not land is a
                // setting that is not set.
                Debug.LogError("[LOV] setup: these settings did not resolve and were NOT applied: "
                               + string.Join(", ", Missing));
                if (Application.isBatchMode) EditorApplication.Exit(2);
            }
        }

        // --- the pipeline ----------------------------------------------------

        private static UniversalRenderPipelineAsset EnsurePipeline()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (existing != null) return existing;

            Directory.CreateDirectory(SettingsDir);
            // URP's own creation path, through reflection because it is
            // internal: it is the only one that gives the renderer its default
            // post-processing data, without which bloom and tonemapping
            // silently do nothing.
            var create = typeof(UniversalRenderPipelineAsset).GetMethod(
                "CreateRendererAsset", BindingFlags.NonPublic | BindingFlags.Static);
            ScriptableRendererData renderer;
            if (create != null)
            {
                renderer = (ScriptableRendererData)create.Invoke(null, new object[]
                {
                    RendererPath, RendererType.UniversalRenderer, false, "Renderer",
                });
            }
            else
            {
                Missing.Add("UniversalRenderPipelineAsset.CreateRendererAsset (post-process data will be missing)");
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }
            var asset = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(asset, PipelinePath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        /// <summary>
        /// The pipeline, set for WebGL2 and for this camera.
        ///
        /// The camera sits 37-50 m from the fighting at a 19 degree lens and
        /// looks almost exactly along the ground, so shadows have to reach the
        /// treeline edge and nothing further; everything beyond is haze.
        /// </summary>
        private static void ConfigurePipeline(UniversalRenderPipelineAsset asset)
        {
            var so = new SerializedObject(asset);
            Set(so, "m_SupportsHDR", true);
            // R11G11B10: half the bandwidth of RGBA16F, and enough for a
            // daylight scene whose brightest thing is an overcast sky.
            Set(so, "m_HDRColorBufferPrecision", 0);
            Set(so, "m_MSAA", 1);
            Set(so, "m_RenderScale", 1f);
            Set(so, "m_RequireDepthTexture", true);   // fog, soft particles, AO
            Set(so, "m_RequireOpaqueTexture", false);
            Set(so, "m_EnableLODCrossFade", true);

            Set(so, "m_MainLightRenderingMode", 1);       // per pixel
            Set(so, "m_MainLightShadowsSupported", true);
            Set(so, "m_MainLightShadowmapResolution", 2048);
            Set(so, "m_ShadowDistance", 110f);
            Set(so, "m_ShadowCascadeCount", 2);
            Set(so, "m_Cascade2Split", 0.42f);
            Set(so, "m_SoftShadowsSupported", true);

            // Muzzle flashes are emissive sprites, not lights: in forward
            // rendering every per-pixel light is paid for on every object it
            // touches, and a firefight is forty of them.
            Set(so, "m_AdditionalLightsRenderingMode", 1);
            Set(so, "m_AdditionalLightsPerObjectLimit", 2);
            Set(so, "m_AdditionalLightShadowsSupported", false);

            Set(so, "m_UseSRPBatcher", true);
            Set(so, "m_SupportsDynamicBatching", false);
            Set(so, "m_ColorGradingMode", 1);     // HDR grading
            Set(so, "m_ColorGradingLutSize", 32);
            Set(so, "m_SupportsLightCookies", false);
            Set(so, "m_MixedLightingSupported", true);
            so.ApplyModifiedPropertiesWithoutUndo();

            // No GPU-driven rendering on WebGL2: it needs compute.
            asset.gpuResidentDrawerMode = GPUResidentDrawerMode.Disabled;
            EditorUtility.SetDirty(asset);
        }

        private static void ConfigureRenderer(UniversalRendererData data)
        {
            if (data == null) { Missing.Add("renderer data asset"); return; }
            // Forward, not Forward+: one directional light, and Forward+'s
            // clustered light lists are wasted work on WebGL2.
            data.renderingMode = RenderingMode.Forward;
            data.depthPrimingMode = DepthPrimingMode.Disabled;
            if (data.postProcessData == null) Missing.Add("renderer postProcessData is null");
            EditorUtility.SetDirty(data);
        }

        private static void AssignPipeline(UniversalRenderPipelineAsset asset)
        {
            GraphicsSettings.defaultRenderPipeline = asset;
        }

        /// <summary>
        /// One quality level, called Web, with the pipeline on it.
        ///
        /// Six inherited levels, each with its own null pipeline slot, is six
        /// places for "which pipeline is actually rendering" to have a
        /// different answer. Adaptive quality (brief §2) steps the pipeline
        /// asset's own settings at runtime rather than switching levels.
        /// </summary>
        private static void ConfigureQuality(UniversalRenderPipelineAsset asset)
        {
            var qsObj = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (qsObj.Length == 0) { Missing.Add("QualitySettings.asset"); return; }
            var qs = new SerializedObject(qsObj[0]);
            var levels = qs.FindProperty("m_QualitySettings");
            if (levels == null) { Missing.Add("m_QualitySettings"); return; }
            levels.arraySize = 1;
            var l = levels.GetArrayElementAtIndex(0);
            SetRel(l, "name", "Web");
            SetRel(l, "customRenderPipeline", asset);
            SetRel(l, "pixelLightCount", 2);
            SetRel(l, "shadows", 2);                  // hard and soft
            SetRel(l, "skinWeights", 4);
            // The camera looks along the ground at a fraction of a degree.
            // Without anisotropic filtering the ground texture blurs to a
            // smear a few metres in.
            SetRel(l, "anisotropicTextures", 2);      // forced on
            SetRel(l, "antiAliasing", 0);             // the pipeline owns AA
            SetRel(l, "softParticles", true);
            SetRel(l, "realtimeReflectionProbes", false);
            SetRel(l, "billboardsFaceCameraPosition", true);
            SetRel(l, "vSyncCount", 1);
            SetRel(l, "lodBias", 1f);
            SetRel(l, "maximumLODLevel", 0);
            SetRel(l, "enableLODCrossFade", true);
            SetRel(l, "particleRaycastBudget", 64);
            SetRel(l, "globalTextureMipmapLimit", 0);

            var cur = qs.FindProperty("m_CurrentQuality");
            if (cur != null) cur.intValue = 0; else Missing.Add("m_CurrentQuality");
            var per = qs.FindProperty("m_PerPlatformDefaultQuality");
            if (per != null)
            {
                for (int i = 0; i < per.arraySize; i++)
                {
                    var second = per.GetArrayElementAtIndex(i).FindPropertyRelative("second");
                    if (second != null) second.intValue = 0;
                }
            }
            else Missing.Add("m_PerPlatformDefaultQuality");
            qs.ApplyModifiedPropertiesWithoutUndo();
            QualitySettings.renderPipeline = asset;
        }

        // --- the player --------------------------------------------------------

        private static void ConfigurePlayer()
        {
            PlayerSettings.productName = "Lanes of Vietnam '65";
            // Linear, not Gamma. Physically based materials and a filmic
            // tonemap are defined in linear light; in Gamma every lighting
            // sum is done on encoded values and the frame goes muddy in a way
            // no grading recovers.
            PlayerSettings.colorSpace = ColorSpace.Linear;

            var web = NamedBuildTarget.WebGL;
            PlayerSettings.SetScriptingBackend(web, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(web, ManagedStrippingLevel.High);
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.SetIl2CppCompilerConfiguration(web, Il2CppCompilerConfiguration.Release);
            PlayerSettings.SetIl2CppCodeGeneration(web, Il2CppCodeGeneration.OptimizeSpeed);

            // WebGL2 only for now. WebGPU is a measured decision for later
            // (PLAN §2), not a default.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 });

            // PLAN §6: Brotli, no decompression fallback. The server has to send
            // Content-Encoding: br, which tools/serve.py does; a fallback would
            // hide a misconfigured host behind a slower load.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.WebGL.showDiagnostics = false;
            PlayerSettings.WebGL.threadsSupport = false;
            // WebAssembly 2023: SIMD, bulk memory, native exceptions. Every
            // current desktop browser has had these for years, and SIMD is
            // what skinning and animation sampling lean on.
            PlayerSettings.WebGL.wasm2023 = true;
            PlayerSettings.WebGL.powerPreference = WebGLPowerPreference.HighPerformance;
            PlayerSettings.WebGL.initialMemorySize = 128;
            PlayerSettings.WebGL.maximumMemorySize = 1024;
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            PlayerSettings.runInBackground = false;
        }

        /// <summary>
        /// No physics engine in the build (PLAN §12.9): the simulation owns
        /// every position, selection is ray-against-men arithmetic, and there
        /// is no ragdoll. With PhysX selected the empty floor still linked
        /// eleven physics internal calls — raycasts and collision callbacks
        /// referenced from UI and rendering code — and with them the native
        /// PhysX library. 0xDECAFBAD is Unity's no-op backend, what Project
        /// Settings > Physics calls "None".
        /// </summary>
        private static void ConfigurePhysics()
        {
            const uint NoPhysics = 0xDECAFBADu;
            var objs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset");
            if (objs.Length == 0) { Missing.Add("DynamicsManager.asset"); return; }
            var so = new SerializedObject(objs[0]);
            var p = so.FindProperty("m_CurrentBackendId");
            if (p == null) { Missing.Add("PhysicsManager.m_CurrentBackendId"); return; }
            p.uintValue = NoPhysics;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // --- helpers -----------------------------------------------------------

        private static void Set(SerializedObject so, string name, object value)
        {
            var p = so.FindProperty(name);
            if (p == null) { Missing.Add($"{so.targetObject.GetType().Name}.{name}"); return; }
            Assign(p, value, $"{so.targetObject.GetType().Name}.{name}");
        }

        private static void SetRel(SerializedProperty parent, string name, object value)
        {
            var p = parent.FindPropertyRelative(name);
            if (p == null) { Missing.Add($"quality.{name}"); return; }
            Assign(p, value, $"quality.{name}");
        }

        private static void Assign(SerializedProperty p, object value, string label)
        {
            switch (value)
            {
                case bool b: p.boolValue = b; break;
                // Always the raw value, never enumValueIndex: MsaaQuality.Disabled
                // is 1 at index 0, so an index write of 1 means 2x MSAA.
                case int i: p.intValue = i; break;
                case float f: p.floatValue = f; break;
                case string s: p.stringValue = s; break;
                case UnityEngine.Object o: p.objectReferenceValue = o; break;
                default: Missing.Add($"{label} (unsupported value type {value?.GetType().Name})"); break;
            }
        }
    }
}
