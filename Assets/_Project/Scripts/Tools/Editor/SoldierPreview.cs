using System.IO;
using LanesOfVietnam.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LanesOfVietnam.Tools
{
    /// <summary>
    /// A contact sheet of the 3D soldiers in every state the game puts them in,
    /// through the real prefab, Animator, aim layer and hand IK: from the game's
    /// side (the camera looks along +z at men facing +x) and from the front.
    ///
    /// <code>tools/unity.sh -executeMethod LanesOfVietnam.Tools.SoldierPreview.Render</code>
    /// (with the GPU: not -nographics). Writes captures/soldiers/&lt;side&gt;.png.
    /// </summary>
    public static class SoldierPreview
    {
        // death: for the dead, the man's number (or -1 blast, -2 running); for the living, -1 a hit, -2 a reload, -3 a throw.
        private static (string name, float speed, int posture, bool aim, bool dead, int death, float t)[] States =
        {
            ("idle", 0, 0, false, false, 0, 1.3f), ("walk", 1.35f, 0, false, false, 0, 0.5f), ("run", 2.0f, 0, false, false, 0, 0.4f),
            ("aim", 0, 0, true, false, 0, 1f), ("walk+aim", 1.35f, 0, true, false, 0, 0.5f), ("flinch", 0, 0, false, false, -1, 0.6f),
            ("reload", 0, 0, false, false, -2, 1.2f), ("throw", 0, 0, false, false, -3, 0.8f),
            ("kneel", 0, 1, false, false, 0, 1f), ("kneel aim", 0, 1, true, false, 0, 1f), ("crouch walk", 1.1f, 1, false, false, 0, 0.5f),
            ("kneel hit", 0, 1, false, false, -1, 0.5f), ("prone", 0, 2, false, false, 0, 1f), ("crawl", 0.45f, 2, false, false, 0, 0.6f),
            ("prone aim", 0, 2, true, false, 0, 1f), ("dead 0", 0, 0, false, true, 0, 5f), ("dead 3", 0, 0, false, true, 3, 5f),
            ("dead 6", 0, 0, false, true, 6, 5f), ("blast", 0, 0, false, true, -1, 5f), ("dead kneeling", 0, 1, false, true, 0, 5f),
            ("dead prone", 0, 2, false, true, 0, 5f),
        };

        public static void Render()
        {
            Directory.CreateDirectory("captures/soldiers");
            foreach (var side in new[] { "us", "vc" }) Sheet(side);
        }

        /// <summary>One state through time, larger: captures/soldiers/&lt;side&gt;_&lt;name&gt;.png. Default the crawl.</summary>
        public static void RenderCycle()
        {
            Directory.CreateDirectory("captures/soldiers");
            var orig = States;
            States = new[] { ("crawl 0", 0.45f, 2, false, false, 0, 0.1f), ("crawl 1", 0.45f, 2, false, false, 0, 0.45f),
                             ("crawl 2", 0.45f, 2, false, false, 0, 0.8f), ("crawl 3", 0.45f, 2, false, false, 0, 1.15f) };
            Sheet("us", 3f, "_crawl");
            States = orig;
        }

        private static void Sheet(string side, float zoom = 1f, string suffix = "")
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{SoldierBuilder.Dir}/soldier_{side}.prefab");
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 2.2f;
            sun.transform.rotation = Quaternion.Euler(45f, 20f, 0f);
            sun.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.70f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.58f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.24f, 0.19f);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 20f;
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.25f, 0.27f, 0.2f) };
            ground.GetComponent<Renderer>().sharedMaterial = gm;

            int W = (int)(300 * zoom), H = (int)(380 * zoom * (zoom > 1 ? 0.5f : 1f));
            int cols = States.Length;
            var sheet = new Texture2D(W * cols, H * 3, TextureFormat.RGB24, false);
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var camGo = new GameObject("cam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = zoom > 1 ? 0.75f : 1.15f;
            cam.targetTexture = rt;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.68f, 0.72f);
            camGo.AddComponent<UniversalAdditionalCameraData>();

            for (int c = 0; c < cols; c++)
            {
                var s = States[c];
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var f = go.GetComponent<SoldierFigure>();
                f.Animator.enabled = false;
                // Faces +x, as a US man does at rest in the game.
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, 90, 0));
                typeof(SoldierFigure).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(f, null);
                var how = s.dead && s.death == -1 ? SoldierFigure.Fall.Blast : s.dead && s.death == -2 ? SoldierFigure.Fall.Running : SoldierFigure.Fall.Shot;
                int seed = System.Math.Max(0, s.death);
                if (s.dead)
                    for (float t = 0; t < s.t; t += 1f / 30f) f.Step(t == 0 ? 0f : 1f / 30f, s.speed, s.posture, false, false, 0);   // alive first, in his posture
                f.Settle(s.speed, s.posture, s.aim, false, seed);
                if (!s.dead && s.death == -1) f.React();
                if (!s.dead && s.death == -2) f.Reload();
                if (!s.dead && s.death == -3) f.Throw();
                for (float t = 0; t < s.t; t += 1f / 30f) f.Step(1f / 30f, s.speed, s.posture, s.aim, s.dead, seed, how);
                for (int row = 0; row < 3; row++)
                {
                    // Row 0: the game's view, along +z from the -z side. Row 1: from his front.
                    // Row 2: from above, his facing (+x) to the right, +z up the picture.
                    var dir = row == 0 ? Vector3.forward : row == 1 ? Vector3.left : Vector3.down;
                    var centre = new Vector3(0, s.posture == 2 || s.dead ? (zoom > 1 ? 0.3f : 0.5f) : 0.95f, 0);
                    cam.transform.SetPositionAndRotation(centre - dir * 6f, row == 2 ? Quaternion.LookRotation(dir, Vector3.forward) : Quaternion.LookRotation(dir));
                    cam.Render();
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, W, H), c * W, (2 - row) * H);
                    RenderTexture.active = null;
                }
                Object.DestroyImmediate(go);
            }
            sheet.Apply();
            File.WriteAllBytes($"captures/soldiers/{side}{suffix}.png", sheet.EncodeToPNG());
            Debug.Log($"[LOV] soldier preview: captures/soldiers/{side}{suffix}.png ({string.Join(", ", System.Array.ConvertAll(States, x => x.name))})");
            Object.DestroyImmediate(rt);
        }
    }
}
