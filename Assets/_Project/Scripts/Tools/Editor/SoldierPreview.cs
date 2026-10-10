using System.IO;
using System.Linq;
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
            foreach (var side in SoldierBuilder.Us.Concat(SoldierBuilder.Vc)) Sheet(side);
        }

        /// <summary>The upright states, large, to look at legs and kit: captures/soldiers/&lt;side&gt;_close.png.</summary>
        public static void RenderClose()
        {
            Directory.CreateDirectory("captures/soldiers");
            var orig = States;
            States = new[] { ("bind", 0f, 0, false, false, 0, 0f), ("idle", 0f, 0, false, false, 0, 1.0f), ("walk a", 1.35f, 0, false, false, 0, 0.25f), ("walk b", 1.35f, 0, false, false, 0, 0.6f),
                             ("run", 2.0f, 0, false, false, 0, 0.3f), ("aim", 0f, 0, true, false, 0, 1f), ("kneel", 0f, 1, false, false, 0, 1f),
                             ("crouch walk", 1.1f, 1, false, false, 0, 0.4f) };
            foreach (var side in SoldierBuilder.Us.Concat(SoldierBuilder.Vc)) Sheet(side, 1.6f, "_close", 1.15f);
            States = orig;
        }

        /// <summary>One state through time, larger: captures/soldiers/&lt;side&gt;_&lt;name&gt;.png. Default the crawl.</summary>
        public static void RenderCycle()
        {
            Directory.CreateDirectory("captures/soldiers");
            var orig = States;
            States = new[] { ("crawl 0", 0.45f, 2, false, false, 0, 0.1f), ("crawl 1", 0.45f, 2, false, false, 0, 0.45f),
                             ("crawl 2", 0.45f, 2, false, false, 0, 0.8f), ("crawl 3", 0.45f, 2, false, false, 0, 1.15f) };
            Sheet("us_a", 3f, "_crawl");
            States = orig;
        }

        /// <summary>Fieldcraft's acts through time: the two blows, into a trench and out of it: captures/soldiers/&lt;side&gt;_acts.png.</summary>
        public static void RenderActs()
        {
            Directory.CreateDirectory("captures/soldiers");
            var orig = States;
            States = new[] { ("stab 0.15", 0f, 0, true, false, -4, 0.15f), ("stab 0.35", 0f, 0, true, false, -4, 0.35f), ("stab 0.6", 0f, 0, true, false, -4, 0.6f),
                             ("slash 0.2", 0f, 0, true, false, -5, 0.2f), ("slash 0.45", 0f, 0, true, false, -5, 0.45f),
                             ("in 0.2", 0f, 0, false, false, -6, 0.2f), ("in 0.5", 0f, 0, false, false, -6, 0.5f),
                             ("out 0.2", 0f, 0, false, false, -7, 0.2f), ("out 0.5", 0f, 0, false, false, -7, 0.5f), ("out 0.8", 0f, 0, false, false, -7, 0.8f) };
            foreach (var side in new[] { "us_a", "vc_a" }) Sheet(side, 1.6f, "_acts", 1.35f);
            States = orig;
        }

        /// <summary>Every weapon, in the aim, in the game's view: captures/soldiers/&lt;side&gt;_arms.png.</summary>
        public static void RenderArms()
        {
            Directory.CreateDirectory("captures/soldiers");
            var orig = States;
            foreach (var (side, arms) in new[] { ("us_a", new[] { "m16", "m60", "m79", "m3", "m40", "mortar" }), ("vc_a", new[] { "ak", "sks", "rpd", "ppsh", "mosin", "rpg" }) })
            {
                // (The first column's weapon is drawn before its palette is on the GPU in batch mode,
                // and comes out the colour of nothing: a spare column takes that.)
                var shown = new[] { arms[0] }.Concat(arms).ToArray();
                States = shown.Select(a => (a, 0f, 0, true, false, 0, 1f)).ToArray();
                Carry = shown;
                Sheet(side, 1.6f, "_arms", 1.15f);
            }
            Carry = null;
            States = orig;
        }

        /// <summary>
        /// Where a man lying or kneeling looks: the clip's own head, then with his eyes on a point
        /// 20 m to his front (SoldierFigure.Look): captures/soldiers/&lt;side&gt;_look.png.
        /// </summary>
        public static void RenderLook()
        {
            Directory.CreateDirectory("captures/soldiers");
            var orig = States;
            // death -8: Look at his front before he is stepped.
            States = new[] { ("prone", 0f, 2, false, false, 0, 1f), ("prone", 0f, 2, false, false, 0, 1f), ("prone look", 0f, 2, false, false, -8, 1f),
                             ("prone aim look", 0f, 2, true, false, -8, 1f), ("kneel", 0f, 1, false, false, 0, 1f), ("kneel look", 0f, 1, false, false, -8, 1f) };
            foreach (var side in new[] { "us_a", "vc_c" }) Sheet(side, 1.6f, "_look", 1.15f);
            States = orig;
        }

        /// <summary>
        /// When each death clip starts to take him down: its length, when his body's middle has dropped a tenth of its
        /// height, and when it has gone 30 cm along the ground (the clips' own root curves). A clip that stands a
        /// second first is a slow death; SoldierBuilder starts those part way in.
        /// </summary>
        public static void DeathTimes()
        {
            foreach (var (role, _, _) in SoldierBuilder.Mixamo.Where(m => m.role.Contains("death") || m.role == "kneel_grenade" || m.role == "grenade"))
            {
                var clip = AssetDatabase.LoadAllAssetsAtPath($"{SoldierBuilder.MixamoDir}/{role}.fbx").OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__"));
                if (clip == null) continue;
                AnimationCurve Curve(string prop)
                {
                    var b = AnimationUtility.GetCurveBindings(clip).FirstOrDefault(x => x.propertyName == prop);
                    return b.propertyName == null ? null : AnimationUtility.GetEditorCurve(clip, b);
                }
                var y = Curve("RootT.y"); var x = Curve("RootT.x"); var z = Curve("RootT.z");
                // (The right hand's reach, for the throws: its muscle curves are not a position, so the root only.)
                float drop = -1, gone = -1;
                if (y != null)
                    for (float t = 0; t <= clip.length; t += 1f / 30f)
                    {
                        if (drop < 0 && y.Evaluate(t) < y.Evaluate(0) * 0.9f) drop = t;
                        if (gone < 0 && x != null && z != null && new Vector2(x.Evaluate(t) - x.Evaluate(0), z.Evaluate(t) - z.Evaluate(0)).magnitude > 0.3f) gone = t;
                    }
                Debug.Log($"[LOV] {role}: {clip.length:F2} s; down a tenth at {drop:F2} s; 30 cm along at {gone:F2} s");
            }
        }

        /// <summary>For RenderArms: the weapon each column's man is given.</summary>
        private static string[] Carry;

        /// <summary>For RenderDoings: the act each column's man is started in (SoldierFigure.Act), or a blow by its number ("blow 2").</summary>
        private static string[] Play;

        /// <summary>
        /// What a man does besides walk, fire and die (the clips of 2026-10-10), each through its length:
        /// captures/soldiers/us_a_doings_a.png and _b.png.
        /// </summary>
        public static void RenderDoings()
        {
            Directory.CreateDirectory("captures/soldiers");
            var orig = States;
            foreach (var (suffix, acts) in new[]
            {
                ("_doings_a", new (string act, int posture, float[] at)[]
                {
                    (SoldierFigure.BlastFall, 1, new[] { 0.25f, 0.6f, 1.0f, 1.5f, 1.9f, 2.3f }), (SoldierFigure.RubEyes, 0, new[] { 0.4f, 1.0f, 1.8f }),
                    (SoldierFigure.Dodge, 0, new[] { 0.2f, 0.45f, 0.8f }), (SoldierFigure.KneelFlinch, 1, new[] { 0.2f, 0.5f, 0.9f }),
                    (SoldierFigure.Dive, 2, new[] { 0.15f, 0.35f, 0.55f, 0.8f, 1.1f, 1.5f }),
                }),
                ("_doings_b", new (string act, int posture, float[] at)[]
                {
                    (SoldierFigure.SignalFollow, 1, new[] { 0.4f, 0.9f, 1.5f, 2.2f }), (SoldierFigure.SignalStay, 1, new[] { 0.4f, 0.9f, 1.5f, 2.2f }),
                    (SoldierFigure.LookAbout, 0, new[] { 1.0f, 2.5f }), (SoldierFigure.Inspect, 0, new[] { 1.0f, 2.5f }),
                }),
            })
            {
                var states = new System.Collections.Generic.List<(string, float, int, bool, bool, int, float)>();
                var play = new System.Collections.Generic.List<string>();
                foreach (var (act, posture, at) in acts)
                    foreach (float t in at) { states.Add(($"{act} {t:F2}", 0f, posture, false, false, 0, t)); play.Add(act); }
                States = states.ToArray(); Play = play.ToArray();
                Sheet("us_a", 1.4f, suffix, 1.25f);
            }
            States = orig; Play = null;
        }

        /// <summary>For RenderBatch: each column's walk on the march and at the double, and which way what killed him threw him.</summary>
        private static (int gait, int pace, float push)[] Own;

        /// <summary>
        /// The clips fetched on 2026-10-09, as the game plays them: each walk on the march and at the double
        /// through its stride, the kneeling reload and throw, and the new deaths: captures/soldiers/us_a_walks.png,
        /// _kneel.png and _deaths.png.
        /// </summary>
        public static void RenderBatch()
        {
            Directory.CreateDirectory("captures/soldiers");
            var orig = States;
            var states = new System.Collections.Generic.List<(string, float, int, bool, bool, int, float)>();
            var own = new System.Collections.Generic.List<(int, int, float)>();
            for (int g = 0; g < SoldierBuilder.Marches.Length; g++)
                foreach (float t in new[] { 0.35f, 0.65f, 0.95f }) { states.Add(($"{SoldierBuilder.Marches[g]} 0.95 m/s", 0.95f, 0, false, false, 0, t)); own.Add((g, 0, 0f)); }
            for (int p = 0; p < SoldierBuilder.Doubles.Length; p++)
                foreach (float t in new[] { 0.3f, 0.5f, 0.7f }) { states.Add(($"{SoldierBuilder.Doubles[p]} 1.9 m/s", 1.9f, 0, false, false, 0, t)); own.Add((0, p, 0f)); }
            States = states.ToArray(); Own = own.ToArray();
            Sheet("us_a", 1.6f, "_walks", 1.15f);

            // The kneeling reload, and the kneeling throw through its length (to see where the grenade leaves his hand).
            states.Clear();
            foreach (float t in new[] { 0.5f, 1.2f, 2.0f }) states.Add(($"kneel reload {t:F1}", 0f, 1, false, false, -2, t));
            foreach (float t in new[] { 0.1f, 0.25f, 0.4f, 0.5f, 0.6f, 0.8f, 1.1f, 1.5f }) states.Add(($"kneel throw {t:F2}", 0f, 1, false, false, -3, t));
            States = states.ToArray(); Own = null;
            Sheet("us_a", 1.6f, "_kneel", 1.15f);

            // Each death through its fall, framed wide: some carry a man three metres. Seeds 8, 9, 10 are the three new
            // ones any man may fall by; the rest are by how he died: a burst behind him and in front of him, hit from
            // behind at a run, and the second from a knee.
            states.Clear(); own.Clear();
            void Dead(string name, int posture, int death, float push, float speed = 0f)
            {
                foreach (float t in new[] { 0.3f, 0.6f, 1.0f, 5f }) { states.Add((name, speed, posture, false, true, death, t)); own.Add((0, 0, push)); }
            }
            Dead("fall forward", 0, 8, 0f); Dead("left", 0, 9, 0f); Dead("knees", 0, 10, 0f);
            Dead("blast from behind", 0, -1, 1f); Dead("blast from in front", 0, -1, -1f);
            Dead("run, from behind", 0, -2, 1f, 1.9f); Dead("kneeling, second", 1, 2, 0f);
            States = states.ToArray(); Own = own.ToArray();
            Sheet("us_a", 1.2f, "_deaths", 2.6f);
            States = orig; Own = null;
        }

        /// <summary>For RenderGore: what is done to each column's man as he is killed.</summary>
        private static System.Action<SoldierFigure>[] Hurt;

        /// <summary>
        /// What a round and a burst leave on a man (the owner, 2026-10-10: "bullets hitting soldiers and blood
        /// splatters, body disfigurement and gore (with high cal + explosions)"), as the game does it: the hole a
        /// rifle round makes going in and coming out, a machine gun's two, a sniper's round taking his head, a
        /// forearm gone, and a burst close by taking three parts of him: captures/soldiers/us_a_gore_a.png, _b.png.
        /// </summary>
        public static void RenderGore()
        {
            Directory.CreateDirectory("captures/soldiers");
            var orig = States;
            var states = new System.Collections.Generic.List<(string, float, int, bool, bool, int, float)>();
            var own = new System.Collections.Generic.List<(int, int, float)>();
            var hurt = new System.Collections.Generic.List<System.Action<SoldierFigure>>();
            void Killed(string name, int death, float push, float[] at, System.Action<SoldierFigure> how)
            {
                foreach (float t in at) { states.Add((name, 0f, 0, false, true, death, t)); own.Add((0, 0, push)); hurt.Add(how); }
            }
            // He faces +x: a round going -x strikes his front.
            Killed("rifle, from in front", 4, -1f, new[] { 0.15f, 5f }, f => f.Wound(Vector3.left, 0.045f + 0.035f * 1f, 12));
            Killed("rifle, from behind", 8, 1f, new[] { 0.15f, 5f }, f => f.Wound(Vector3.right, 0.045f + 0.035f * 1f, 30));
            Killed("machine gun", 0, -1f, new[] { 0.15f, 5f }, f =>
            {
                f.Wound(Vector3.left, 0.045f + 0.035f * 1.4f, 21);
                f.Wound(Quaternion.Euler(0, 12f, 0) * Vector3.left, 0.045f + 0.035f * 1.4f, 47);
            });
            States = states.ToArray(); Own = own.ToArray(); Hurt = hurt.ToArray();
            Sheet("us_a", 2.4f, "_gore_a", 1.5f);

            states.Clear(); own.Clear(); hurt.Clear();
            Killed("sniper: his head", 4, -1f, new[] { 0.2f, 0.6f, 5f }, f =>
            {
                f.Wound(Vector3.left, 0.045f + 0.035f * 1.9f, 8);
                f.Sever("head", Vector3.left * 2.5f + Vector3.up * 3f + Vector3.forward * 0.4f);
            });
            Killed("machine gun: a forearm", 9, -1f, new[] { 0.3f, 5f }, f =>
            {
                f.Wound(Vector3.left, 0.045f + 0.035f * 1.4f, 21);
                f.Sever("lowerarm_l", Vector3.left * 2.5f + Vector3.up * 3f + Vector3.back * 0.5f);
            });
            Killed("a burst close by", -1, -1f, new[] { 0.25f, 0.6f, 5f }, f =>
            {
                f.Blown(Vector3.left * 2.2f, 0.8f);
                f.Sever("head", Vector3.left * 1.5f + Vector3.up * 5f + Vector3.forward * 1f);
                f.Sever("upperarm_r", Vector3.left * 3f + Vector3.up * 4f + Vector3.back * 1.2f);
                f.Sever("calf_l", Vector3.left * 0.5f + Vector3.up * 3.5f + Vector3.forward * 1.5f);
                f.Wound(Vector3.left, 0.09f, 3);
                f.Wound(Quaternion.Euler(0, 35f, 0) * Vector3.left, 0.1f, 14);
            });
            States = states.ToArray(); Own = own.ToArray(); Hurt = hurt.ToArray();
            Sheet("us_a", 2.4f, "_gore_b", 1.5f);
            States = orig; Own = null; Hurt = null;
        }

        private static void Sheet(string side, float zoom = 1f, string suffix = "", float size = 0f)
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

            int W = (int)(300 * zoom), H = (int)(380 * zoom * (zoom > 1 && size <= 0 ? 0.5f : 1f));
            int cols = States.Length;
            var sheet = new Texture2D(W * cols, H * 3, TextureFormat.RGB24, false);
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var camGo = new GameObject("cam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = size > 0 ? size : zoom > 1 ? 0.75f : 1.15f;
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
                if (Carry != null && !f.Carry(Carry[c])) Debug.LogWarning($"[LOV] {side} cannot carry {Carry[c]}");
                if (Own != null) { f.Gait = Own[c].gait; f.Pace = Own[c].pace; }
                string doing = Play != null ? Play[c] : null;
                if (s.name == "bind") goto shoot;          // as rigged: no Animator at all
                var how = s.dead && s.death == -1 ? SoldierFigure.Fall.Blast : s.dead && s.death == -2 ? SoldierFigure.Fall.Running : SoldierFigure.Fall.Shot;
                int seed = System.Math.Max(0, s.death);
                if (s.dead)
                    for (float t = 0; t < s.t; t += 1f / 30f) f.Step(t == 0 ? 0f : 1f / 30f, s.speed, s.posture, false, false, 0);   // alive first, in his posture
                f.Settle(s.speed, s.posture, s.aim, false, seed);
                if (doing != null && doing.StartsWith("blow ")) f.Strike(int.Parse(doing.Substring(5)));
                else if (doing != null && !f.Act(doing)) Debug.LogWarning($"[LOV] no act {doing}");
                if (!s.dead && s.death == -1) f.React();
                if (!s.dead && s.death == -2) f.Reload();
                if (!s.dead && s.death == -3) f.Throw();
                if (!s.dead && s.death == -8) f.Look(new Vector3(20f, s.posture == 2 ? 0.35f : 1.0f, 0f), 1f);
                if (!s.dead && s.death == -4) f.Strike(0);
                if (!s.dead && s.death == -5) f.Strike(1);
                if (!s.dead && s.death == -6) f.Climb(true);
                if (!s.dead && s.death == -7) f.Climb(false);
                if (Hurt != null && c < Hurt.Length) Hurt[c]?.Invoke(f);
                for (float t = 0; t < s.t; t += 1f / 30f) f.Step(1f / 30f, s.speed, s.posture, s.aim, s.dead, seed, how, Own != null ? Own[c].push : 0f);
                shoot:
                for (int row = 0; row < 3; row++)
                {
                    // Row 0: the game's view, along +z from the -z side. Row 1: from his front.
                    // Row 2: from above, his facing (+x) to the right, +z up the picture.
                    var dir = row == 0 ? Vector3.forward : row == 1 ? Vector3.left : Vector3.down;
                    var centre = new Vector3(0, s.posture == 2 || s.dead ? (zoom > 1 ? 0.3f : 0.5f) : 0.95f, 0);
                    if (size > 0) centre.y = 0.95f;
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
