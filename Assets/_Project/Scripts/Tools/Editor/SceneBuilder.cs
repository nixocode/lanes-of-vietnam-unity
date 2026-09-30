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
            // The sun, the ambient and the fog colour are set at load from the
            // same baked sky the player sees (SkyLighting); the values below are
            // only what the Editor shows before play.
            sunGo.AddComponent<CloudShadows>();
            var skyLight = sunGo.AddComponent<SkyLighting>();
            skyLight.Sun = sun;
            skyLight.SkyMaterial = RenderSettings.skybox;
            skyLight.SkyJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Art/Sky/sky.json");
            if (skyLight.SkyJson == null) throw new System.Exception("sky.json missing — run tools/blender/sky_bake.py");
            // Edit-mode stand-ins only; SkyLighting replaces all four at load.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.70f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.58f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.24f, 0.19f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            // Tropical air: enough that distance lightens (the reference's bands
            // rise from lane to treeline to sky).
            RenderSettings.fogDensity = 0.0035f;
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
            // TAA tuned against Flicker (PLAN §7) once the grass went in: dense
            // alpha-tested blades made the default TAA boil, 1.93 mean |dL*| on
            // a still frame against the 1.30 gate. Very High quality (bicubic
            // history) with the jitter at 0.35 holds it at 0.65; edges on men
            // and poles show no stair-steps at 2x zoom. SMAA reads 0 still but
            // would crawl on grass whenever the camera pans.
            var taa = camData.taaSettings;
            taa.quality = TemporalAAQuality.VeryHigh;
            taa.jitterScale = 0.35f;
            camData.taaSettings = taa;
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
            skyLight.Post = vol;

            // --- the game -----------------------------------------------------------
            var game = new GameObject("Game");
            var root = game.AddComponent<GameRoot>();
            root.CameraRig = rig;

            var groundGo = new GameObject("Ground");
            groundGo.transform.SetParent(game.transform, false);
            var ground = groundGo.AddComponent<GroundView>();
            ground.Material = GroundMaterial();
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
            cover.Sandbags = PropSet("sandbags");
            army.UsSoldiers = SoldierSet("soldier_us");
            army.VcSoldiers = SoldierSet("soldier_vc");
            root.ArmyView = army;

            var dressGo = new GameObject("Dressing");
            dressGo.transform.SetParent(game.transform, false);
            var dress = dressGo.AddComponent<WorldDressing>();
            dress.Foliage = Lit("Foliage", new Color(0.16f, 0.24f, 0.10f), 0.1f);
            dress.FoliageDark = Lit("Foliage Dark", new Color(0.09f, 0.15f, 0.07f), 0.1f);
            dress.Mountain = Lit("Mountain", new Color(0.30f, 0.38f, 0.33f), 0.0f);
            dress.Wire = Lit("Wire", new Color(0.30f, 0.29f, 0.27f), 0.45f);
            dress.Timber = cover.TimberMaterial;
            dress.Sandbag = cover.SandbagMaterial;
            dress.Vehicle = Lit("Vehicle", new Color(0.22f, 0.25f, 0.17f), 0.2f);
            dress.Plants = PlantSets();
            dress.Props = new[] { PropSet("firebase") };
            root.Dressing = dress;

            // The Chu Pong massif, from real elevation data (tools/art/mountains.py).
            var mountGo = new GameObject("Mountains");
            mountGo.transform.SetParent(game.transform, false);
            var mount = mountGo.AddComponent<MountainView>();
            mount.Heights = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Art/Mountains/mountains.bytes");
            mount.Layout = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Art/Mountains/mountains.json");
            mount.Material = Mat("Mountains", Shader.Find("LOV/Mountain"), null);
            mount.Material.SetVector("_Haze", new Vector4(MountainHaze, 600, 0, 0));
            if (mount.Heights == null || mount.Layout == null) Debug.LogWarning("[LOV] no mountain data — the grey-box ridges stand in (run tools/art/mountains.py)");
            root.Mountains = mount;

            // The fighting, drawn from the simulation's events (step 7).
            var combat = game.AddComponent<CombatView>();
            combat.Glow = Mat("FX Glow", Shader.Find("LOV/FX Glow"), null);
            combat.Smoke = Mat("FX Smoke", Shader.Find("LOV/FX Smoke"), null);

            var commander = game.AddComponent<Commander>();
            commander.Root = root;
            var ringsGo = new GameObject("Selection");
            ringsGo.transform.SetParent(game.transform, false);
            var rings = ringsGo.AddComponent<SelectionRings>();
            rings.Commander = commander;
            rings.RingMaterial = Mat("Ring", Shader.Find("LOV/Ring"), null);

            var deployer = game.AddComponent<Deployer>();
            deployer.Root = root;
            deployer.MarkerMaterial = rings.RingMaterial;
            commander.Deployer = deployer;

            var hudGo = new GameObject("HUD");
            var doc = hudGo.AddComponent<UnityEngine.UIElements.UIDocument>();
            doc.panelSettings = PanelSettings();
            hudGo.SetActive(false);   // so OnEnable runs after the references below are set
            var hud = hudGo.AddComponent<LanesOfVietnam.View.UI.Hud>();
            hud.Root = root;
            hud.Commander = commander;
            hud.Deployer = deployer;
            hud.Style = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.StyleSheet>("Assets/_Project/UI/Hud.uss");
            if (hud.Style == null) throw new System.Exception("Hud.uss did not import as a StyleSheet");
            var screens = hudGo.AddComponent<LanesOfVietnam.View.UI.Screens>();
            screens.Root = root;
            screens.Hud = hud;
            screens.Commander = commander;
            screens.Deployer = deployer;
            hudGo.SetActive(true);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[LOV] scene built: {ScenePath}");
        }

        /// <summary>
        /// The panel the HUD draws on: 1920 x 1080 reference, scaled with the
        /// screen, and the runtime theme. The theme file is written here too —
        /// the editor's own "create Panel Settings" menu makes one, and a headless
        /// build has no menu.
        /// </summary>
        private static UnityEngine.UIElements.PanelSettings PanelSettings()
        {
            const string themePath = "Assets/_Project/UI/Runtime.tss";
            const string panelPath = "Assets/_Project/UI/Panel.asset";
            if (!File.Exists(themePath))
            {
                File.WriteAllText(themePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(themePath);
            }
            var theme = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.ThemeStyleSheet>(themePath);
            if (theme == null) throw new System.Exception("Runtime.tss did not import as a ThemeStyleSheet");
            var ps = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(panelPath);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
                AssetDatabase.CreateAsset(ps, panelPath);
            }
            ps.themeStyleSheet = theme;
            ps.scaleMode = UnityEngine.UIElements.PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = UnityEngine.UIElements.PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();
            return ps;
        }

        /// <summary>The photographed sky (tools/blender/sky_bake.py), drawn by LOV/Sky.</summary>
        private static Material SkyMaterial()
        {
            const string dir = "Assets/_Project/Art/Sky/";
            foreach (var f in new[] { "sky_window.png", "sky_full.png" })
                AssetDatabase.ImportAsset(dir + f, ImportAssetOptions.ForceUpdate);
            var m = Mat("Sky", Shader.Find("LOV/Sky"), null);
            m.SetTexture("_Window", AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "sky_window.png"));
            m.SetTexture("_Full", AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "sky_full.png"));
            EditorUtility.SetDirty(m);
            return m;
        }

        [System.Serializable] private class TerrainLayer { public string name; public float tile_m; }
        [System.Serializable] private class TerrainLayers { public int size; public TerrainLayer[] layers; }

        /// <summary>The ground's scanned layers (tools/art/terrain_pack.py), tiled as the pack says.</summary>
        private static Material GroundMaterial()
        {
            const string dir = "Assets/_Project/Art/Terrain/";
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(dir + "ground_layers.json");
            if (json == null) throw new System.Exception("ground_layers.json missing — run tools/art/terrain_pack.py");
            var info = JsonUtility.FromJson<TerrainLayers>(json.text);
            if (info.layers.Length != 4) throw new System.Exception($"the ground shader takes 4 layers, the pack has {info.layers.Length}");

            var m = Mat("Ground", Shader.Find("LOV/Ground"), null);
            var tile = Vector4.zero;
            long bytes = 0;
            for (int i = 0; i < 4; i++)
            {
                foreach (var map in new[] { "albedo", "normal" })
                {
                    string path = $"{dir}ground_{i}_{info.layers[i].name}_{map}.png";
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (tex == null) throw new System.Exception($"{path} missing — run tools/art/terrain_pack.py");
                    m.SetTexture(map == "albedo" ? $"_Albedo{i}" : $"_Normal{i}", tex);
                    bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
                    if (i == 0) Debug.Log($"[LOV] ground {map}: {tex.width}x{tex.height} {tex.graphicsFormat}");
                }
                tile[i] = info.layers[i].tile_m;
            }
            m.SetVector("_Tile", tile);
            // One mip level of bias. Measured on a still frame (TAA on): near-ground
            // shimmer 0.424 mean |dL*| at 1, 0.317 at 1.5, 0.242 at 2; through the
            // field glasses the softening at 2 is barely visible.
            m.SetFloat("_GradScale", 2f);
            EditorUtility.SetDirty(m);
            Debug.Log($"[LOV] ground layers: 4 x (albedo + normal), {bytes / 1048576f:F1} MB in memory");
            return m;
        }

        [System.Serializable] private class PlantLayout { public float pitch_deg; }

        /// <summary>
        /// Every baked species in Art/Plants (tools/blender/plant_bake.py): its
        /// layout and a material made from its two atlases.
        /// </summary>
        private static PlantSet[] PlantSets()
        {
            const string dir = "Assets/_Project/Art/Plants";
            var sets = new System.Collections.Generic.List<PlantSet>();
            if (!AssetDatabase.IsValidFolder(dir)) return sets.ToArray();
            foreach (var guid in AssetDatabase.FindAssets("t:TextAsset", new[] { dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".json")) continue;
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                var json = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{name}_albedo.png");
                var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{name}_normal.png");
                if (albedo == null || normal == null) throw new System.Exception($"{name}: atlas missing beside {path}");
                var m = Mat($"Plant {name}", Shader.Find("LOV/Foliage"), null);
                m.SetTexture("_Albedo", albedo);
                m.SetTexture("_Normal", normal);
                m.SetFloat("_Pitch", JsonUtility.FromJson<PlantLayout>(json.text).pitch_deg);
                m.SetVector("_MipBias", MipBiasFor(name));
                m.SetFloat("_Dither", name.Contains("grass") ? GrassDither : 0f);
                m.SetFloat("_FieldOcclusion", FieldOcclusionFor(name));
                EditorUtility.SetDirty(m);
                sets.Add(new PlantSet { Name = name, Layout = json, Material = m });
            }
            Debug.Log($"[LOV] plant species: {string.Join(", ", sets.ConvertAll(p => p.Name))}");
            return sets.ToArray();
        }

        /// <summary>
        /// Foliage mip bias (near, far, from m, to m), by measurement against
        /// Flicker (PLAN §7) with TAA at Very High, jitter 0.35: the broadleaf
        /// plants hold at 0.5 near; grass blades, a pixel or two wide at 25 m,
        /// need 1.0 (whole frame 0.70, foreground 1.10; at 1.5 the blades blur
        /// into blobs, at 0.5 the foreground shimmers at 1.46).
        /// </summary>
        public static Vector4 GrassBias = new Vector4(1.0f, 1.5f, 40, 90);
        public static float GrassDither = 0f;
        /// <summary>How much a species' neighbours shade its lower parts: grass grows in a dense sward, understory in thickets.</summary>
        public static float GrassFieldOcclusion = 0.7f;
        private static float FieldOcclusionFor(string species)
            => species.Contains("grass") ? GrassFieldOcclusion
             : species is "jacaranda" or "island_tree" ? 0.25f : 0.45f;

        private static Vector4 MipBiasFor(string species)
            => species.Contains("grass") ? GrassBias : new Vector4(0.5f, 1.75f, 40, 90);

        /// <summary>Metres of air for 63% haze on the massif (Mountain.shader).</summary>
        public static float MountainHaze = 2500f;

        /// <summary>A baked prop set (Art/Props): lit like the plants, without wind, leaf glow or field shade.</summary>
        private static PlantSet PropSet(string name)
        {
            const string dir = "Assets/_Project/Art/Props";
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>($"{dir}/{name}.json");
            if (json == null) return default;
            foreach (var f in new[] { "albedo", "normal" }) AssetDatabase.ImportAsset($"{dir}/{name}_{f}.png", ImportAssetOptions.ForceUpdate);
            var m = Mat($"Prop {name}", Shader.Find("LOV/Foliage"), null);
            m.SetTexture("_Albedo", AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{name}_albedo.png"));
            m.SetTexture("_Normal", AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{name}_normal.png"));
            m.SetFloat("_Pitch", JsonUtility.FromJson<PlantLayout>(json.text).pitch_deg);
            m.SetFloat("_Wind", 0f);
            m.SetFloat("_Translucency", 0f);
            m.SetFloat("_FieldOcclusion", 0f);
            m.SetVector("_MipBias", new Vector4(0.25f, 0.75f, 40, 90));
            EditorUtility.SetDirty(m);
            return new PlantSet { Name = name, Layout = json, Material = m };
        }

        /// <summary>A baked soldier set (Art/Soldiers), lit by the foliage shader without wind, leaf glow or field shade.</summary>
        private static PlantSet SoldierSet(string name)
        {
            const string dir = "Assets/_Project/Art/Soldiers";
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>($"{dir}/{name}.json");
            if (json == null) return default;
            foreach (var f in new[] { "albedo", "normal" }) AssetDatabase.ImportAsset($"{dir}/{name}_{f}.png", ImportAssetOptions.ForceUpdate);
            var m = Mat(name, Shader.Find("LOV/Foliage"), null);
            m.SetTexture("_Albedo", AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{name}_albedo.png"));
            m.SetTexture("_Normal", AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{name}_normal.png"));
            m.SetFloat("_Pitch", JsonUtility.FromJson<PlantLayout>(json.text).pitch_deg);
            m.SetFloat("_Wind", 0f);
            m.SetFloat("_Translucency", 0f);
            m.SetFloat("_FieldOcclusion", 0f);
            m.SetVector("_MipBias", new Vector4(0.25f, 0.75f, 40, 90));
            EditorUtility.SetDirty(m);
            return new PlantSet { Name = name, Layout = json, Material = m };
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
            // Neutral, not ACES: with the scanned ground and the photographed
            // sky, ACES pushed every band's saturation past the reference's
            // (sky/treeline/far/near 20/28/37/33 against 11/17/22/34), and
            // Neutral lands near it (14/17/29/29) at the same white balance.
            var tone = p.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var bloom = p.Add<Bloom>(true);
            bloom.threshold.Override(1.15f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.6f);
            var vig = p.Add<Vignette>(true);
            vig.intensity.Override(0.22f);
            vig.smoothness.Override(0.45f);
            var col = p.Add<ColorAdjustments>(true);
            // The grade, found by measurement against TARGET.jpg's bands (the
            // search is in the commit that set it). Exposure for the ground, and
            // the sky held up on its own (SkyLighting.SkyStops, a graduated
            // filter): the reference's sky is far brighter than its ground, more
            // than one physical exposure gives. Sum of band |dL*| 52.4 -> 29.9.
            col.postExposure.Override(0.4f);
            col.contrast.Override(8f);
            col.saturation.Override(-20f);
            var lgg = p.Add<LiftGammaGain>(true);
            lgg.lift.Override(new Vector4(1, 1, 1, 0f));
            lgg.gamma.Override(new Vector4(1, 1, 1, -0.1f));
            lgg.gain.Override(new Vector4(1, 1, 1, 0.05f));
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
            PruneStale(m);
            return m;
        }

        /// <summary>
        /// Drop saved properties the current shader does not declare. A material
        /// keeps every property it ever had, so a rewritten shader leaves texture
        /// references to files that no longer exist.
        /// </summary>
        private static void PruneStale(Material m)
        {
            var so = new SerializedObject(m);
            foreach (var list in new[] { "m_TexEnvs", "m_Floats", "m_Colors", "m_Ints" })
            {
                var props = so.FindProperty("m_SavedProperties." + list);
                if (props == null) continue;
                for (int i = props.arraySize - 1; i >= 0; i--)
                {
                    string name = props.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue;
                    if (!m.HasProperty(name)) props.DeleteArrayElementAtIndex(i);
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
