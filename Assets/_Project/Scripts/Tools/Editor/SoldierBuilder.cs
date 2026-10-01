using System.Collections.Generic;
using System.IO;
using System.Linq;
using LanesOfVietnam.View;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace LanesOfVietnam.Tools
{
    /// <summary>
    /// Builds the 3D soldiers (PLAN §12.3) from tools/blender/soldier_rig.py's
    /// FBX: a Humanoid avatar per body, the Animator controller, a material
    /// and a prefab per side.
    ///
    /// <code>tools/unity.sh -nographics -executeMethod LanesOfVietnam.Tools.SoldierBuilder.Build</code>
    ///
    /// The avatar is built here rather than by the importer. The bodies are
    /// rigged holding a rifle at the ready, and Humanoid maps a skeleton from
    /// the pose it is given: from that one it would read a man with his arms
    /// at his sides, and every clip made on a T-posed body (Mixamo's all are)
    /// would hold its arms 48 degrees off. So the arms are turned out level,
    /// palms down, fingers straight, the legs straight down, and the avatar
    /// is built from that pose; the mesh keeps its own bind pose.
    ///
    /// Clips: Mixamo's where they have been downloaded (Assets/_Licensed/
    /// Mixamo, ignored by git, the owner's rule for licensed files in a public
    /// repository), else the interim ones soldier_rig.py made from the sprite
    /// bake's poses and CMU motion capture.
    /// </summary>
    public static class SoldierBuilder
    {
        public const string Dir = "Assets/_Project/Art/Soldiers3D";
        public const string MixamoDir = "Assets/_Licensed/Mixamo";
        public const string PosesPath = Dir + "/soldier_poses.fbx";
        public const string ControllerPath = Dir + "/soldier.controller";

        /// <summary>
        /// The rig's bones (MPFB2's game-engine skeleton) as Humanoid's. Not the
        /// fingers: Mixamo's finger curls, played on these, folded them back and
        /// tore the hands (pale shards in the first prone captures); left out,
        /// the hands keep the bind's own grip, which no camera here resolves.
        /// </summary>
        private static readonly (HumanBodyBones bone, string name)[] Bones =
        {
            (HumanBodyBones.Hips, "pelvis"), (HumanBodyBones.Spine, "spine_01"), (HumanBodyBones.Chest, "spine_02"),
            (HumanBodyBones.UpperChest, "spine_03"), (HumanBodyBones.Neck, "neck_01"), (HumanBodyBones.Head, "head"),
            (HumanBodyBones.LeftShoulder, "clavicle_l"), (HumanBodyBones.LeftUpperArm, "upperarm_l"),
            (HumanBodyBones.LeftLowerArm, "lowerarm_l"), (HumanBodyBones.LeftHand, "hand_l"),
            (HumanBodyBones.RightShoulder, "clavicle_r"), (HumanBodyBones.RightUpperArm, "upperarm_r"),
            (HumanBodyBones.RightLowerArm, "lowerarm_r"), (HumanBodyBones.RightHand, "hand_r"),
            (HumanBodyBones.LeftUpperLeg, "thigh_l"), (HumanBodyBones.LeftLowerLeg, "calf_l"),
            (HumanBodyBones.LeftFoot, "foot_l"), (HumanBodyBones.LeftToes, "ball_l"),
            (HumanBodyBones.RightUpperLeg, "thigh_r"), (HumanBodyBones.RightLowerLeg, "calf_r"),
            (HumanBodyBones.RightFoot, "foot_r"), (HumanBodyBones.RightToes, "ball_r"),
        };

        [MenuItem("Lanes of Vietnam/Build Soldiers")]
        public static void Build()
        {
            // Re-imported under ArtImport's rules: a changed rule does not re-import by itself.
            foreach (var side in new[] { "us", "vc" })
            {
                AssetDatabase.ImportAsset($"{Dir}/soldier_{side}.fbx", ImportAssetOptions.ForceUpdate);
                foreach (var f in new[] { "albedo", "normal", "mask" })
                    AssetDatabase.ImportAsset($"{Dir}/soldier_{side}_{f}.png", ImportAssetOptions.ForceUpdate);
            }
            var us = BuildAvatar("soldier_us");
            var vc = BuildAvatar("soldier_vc");
            var clips = ImportPoses(us);
            var speeds = ClipSpeeds();
            foreach (var (name, clip, speed) in ImportMixamo())
            {
                clips[name] = clip;
                if (speed > 0) speeds[name] = speed;
            }
            var ctrl = BuildController(clips, speeds);
            BuildPrefab("soldier_us", us, ctrl, speeds);
            BuildPrefab("soldier_vc", vc, ctrl, speeds);
            AssetDatabase.SaveAssets();
            Debug.Log("[LOV] soldiers built");
        }

        private static Dictionary<string, Transform> Find(Transform root)
            => root.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());

        private static void Align(Transform t, Vector3 from, Vector3 to)
        {
            if (from.sqrMagnitude < 1e-10f) return;
            t.rotation = Quaternion.FromToRotation(from, to) * t.rotation;
        }

        /// <summary>The model's facing, from its hips: his right thigh is on his right.</summary>
        private static Vector3 Facing(Dictionary<string, Transform> t, out Vector3 right)
        {
            right = t["thigh_r"].position - t["thigh_l"].position;
            right.y = 0;
            right.Normalize();
            return Vector3.Cross(right, Vector3.up);
        }

        /// <summary>Turn the instance into Humanoid's T-pose, in place.</summary>
        private static void TPose(Dictionary<string, Transform> t)
        {
            Vector3 fwd = Facing(t, out var right);
            foreach (var s in new[] { "l", "r" })
            {
                Vector3 outward = s == "l" ? -right : right;
                Transform ua = t["upperarm_" + s], la = t["lowerarm_" + s], h = t["hand_" + s];
                Align(ua, la.position - ua.position, outward);
                Align(la, h.position - la.position, outward);
                Align(h, t["middle_01_" + s].position - h.position, outward);
                // Palm down: the index finger's knuckle ahead of the little finger's.
                Vector3 across = t["index_01_" + s].position - t["pinky_01_" + s].position;
                across -= outward * Vector3.Dot(across, outward);
                h.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(across, fwd, outward), outward) * h.rotation;
                foreach (var f in new[] { "index", "middle", "ring", "pinky" })
                {
                    Transform a = t[$"{f}_01_{s}"], b = t[$"{f}_02_{s}"], c = t[$"{f}_03_{s}"];
                    Align(a, b.position - a.position, outward);
                    Align(b, c.position - b.position, outward);
                }
                Transform th = t["thigh_" + s], ca = t["calf_" + s], ft = t["foot_" + s];
                var keep = ft.rotation;
                Align(th, ca.position - th.position, Vector3.down);
                Align(ca, ft.position - ca.position, Vector3.down);
                ft.rotation = keep;
            }
        }

        private static HumanDescription Description(Transform root)
        {
            return new HumanDescription
            {
                human = Bones.Select(b => new HumanBone
                {
                    boneName = b.name, humanName = HumanTrait.BoneName[(int)b.bone],
                    limit = new HumanLimit { useDefaultValues = true },
                }).ToArray(),
                skeleton = root.GetComponentsInChildren<Transform>(true).Select(x => new SkeletonBone
                {
                    name = x.name, position = x.localPosition, rotation = x.localRotation, scale = x.localScale,
                }).ToArray(),
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
            };
        }

        private static Avatar BuildAvatar(string name)
        {
            string fbx = $"{Dir}/{name}.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx) ?? throw new System.Exception($"{fbx} missing — run tools/blender/soldier_rig.py");
            var go = Object.Instantiate(model);
            go.name = model.name;
            try
            {
                var t = Find(go.transform);
                foreach (var b in Bones)
                    if (!t.ContainsKey(b.name)) throw new System.Exception($"{fbx}: no bone {b.name}");
                var fwd = Facing(t, out _);
                TPose(t);
                var desc = Description(go.transform);
                var avatar = AvatarBuilder.BuildHumanAvatar(go, desc);
                if (!avatar.isValid || !avatar.isHuman) throw new System.Exception($"{name}: the avatar is not a valid Humanoid");
                avatar.name = name + "_avatar";
                string path = $"{Dir}/{name}_avatar.asset";
                var old = AssetDatabase.LoadAssetAtPath<Avatar>(path);
                if (old != null) { EditorUtility.CopySerialized(avatar, old); avatar = old; EditorUtility.SetDirty(old); }
                else AssetDatabase.CreateAsset(avatar, path);
                Debug.Log($"[LOV] {name}: Humanoid avatar from a T-pose; the model faces {fwd.ToString("F2")}, " +
                          $"hips {t["pelvis"].position.y:F2} m up");
                return avatar;
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>The interim clips, imported as Humanoid on the US body's avatar description.</summary>
        private static Dictionary<string, AnimationClip> ImportPoses(Avatar us)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(PosesPath) ?? throw new System.Exception($"{PosesPath} missing — soldier_rig.py --clips");
            // The file holds only the armature, and Unity folds a single top node
            // into the model's root: without this its root is the rig, turned 270
            // degrees, where the bodies' root is above it, and every clip played
            // the man lying on his back 100 m underground.
            if (!mi.preserveHierarchy) { mi.preserveHierarchy = true; mi.SaveAndReimport(); }
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(PosesPath);
            var names = new HashSet<string>(src.GetComponentsInChildren<Transform>(true).Select(x => x.name));
            var desc = us.humanDescription;
            // Only the transforms this file has; its root is its own.
            desc.skeleton = desc.skeleton.Skip(1).Where(b => names.Contains(b.name))
                .Prepend(new SkeletonBone { name = src.name, position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one })
                .ToArray();
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.humanDescription = desc;
            mi.importAnimation = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.SaveAndReimport();
            mi.clipAnimations = mi.defaultClipAnimations.Select(c =>
            {
                c.name = c.takeName.Contains("|") ? c.takeName.Substring(c.takeName.LastIndexOf('|') + 1) : c.takeName;
                c.loopTime = true;
                // In place: the simulation owns where a man is (§12.3). The
                // hips keep the height each clip was made at (kneeling, lying).
                c.lockRootRotation = true; c.keepOriginalOrientation = true;
                c.lockRootHeightY = true; c.keepOriginalPositionY = true; c.heightFromFeet = false;
                c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
                return c;
            }).ToArray();
            mi.SaveAndReimport();
            var clips = AssetDatabase.LoadAllAssetsAtPath(PosesPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);
            Debug.Log($"[LOV] interim clips: {string.Join(", ", clips.Keys.OrderBy(k => k))}");
            return clips;
        }

        /// <summary>
        /// Mixamo's clips (PLAN §12.3), each file one clip, named by its role
        /// here: (role, loops, travels). A travelling clip (a crawl, a walk)
        /// keeps its ground motion as root motion, which the Animator never
        /// applies (the sim moves the man) but which measures the clip's own
        /// speed; everything else has its motion baked into the pose, so a man
        /// thrown back by a round falls back, not straight down.
        /// </summary>
        public static readonly (string role, bool loop, bool travels)[] Mixamo =
        {
            ("prone_idle", true, false), ("crawl", true, true), ("prone_fire", true, false), ("prone_death", false, false),
            ("prone_hit", false, false), ("prone_reload", false, false), ("crouch_to_prone", false, false),
            ("prone_to_kneel", false, false), ("kneel_to_prone", false, false), ("stand_to_kneel", false, false),
            ("kneel_to_stand", false, false), ("kneel_idle", true, false), ("kneel_aim", false, false), ("kneel_hit", false, false),
            ("hit_back", false, false), ("flinch", false, false), ("fire", true, false), ("fire_b", true, false),
            ("fire_walk", true, true), ("fire_crouch_walk", true, true), ("reload", false, false), ("grenade", false, false),
            ("death_rifle", false, false), ("death_fall_back", false, false), ("death_blast", false, false),
            ("death_backwards", false, false), ("death_run", false, false),
        };

        private static readonly List<string> ImportedMixamo = new List<string>();

        /// <summary>The deaths standing or kneeling, in the order a man's death index picks them.</summary>
        private static readonly string[] MixamoDeaths = { "death_rifle", "death_fall_back", "death_backwards", "death_run", "death_blast" };

        private static float RootTravel(AnimationClip clip)
        {
            float End(string prop)
            {
                var b = AnimationUtility.GetCurveBindings(clip).FirstOrDefault(x => x.propertyName == prop);
                if (b.propertyName == null) return 0f;
                var c = AnimationUtility.GetEditorCurve(clip, b);
                return c.Evaluate(clip.length) - c.Evaluate(0f);
            }
            return new Vector2(End("RootT.x"), End("RootT.z")).magnitude;
        }

        private static float _hips;
        /// <summary>The US body's hip height in its bind pose: Humanoid's unit of root motion for him.</summary>
        private static float HipHeight()
        {
            if (_hips > 0) return _hips;
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/soldier_us.fbx"));
            _hips = Find(go.transform)["pelvis"].position.y;
            Object.DestroyImmediate(go);
            return _hips;
        }

        private static IEnumerable<(string role, AnimationClip clip, float speed)> ImportMixamo()
        {
            var found = new List<string>();
            ImportedMixamo.Clear();
            foreach (var (role, loop, travels) in Mixamo)
            {
                string path = $"{MixamoDir}/{role}.fbx";
                if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) continue;
                if (mi.animationType != ModelImporterAnimationType.Human) { mi.animationType = ModelImporterAnimationType.Human; mi.SaveAndReimport(); }
                var takes = mi.clipAnimations.Length > 0 ? mi.clipAnimations : mi.defaultClipAnimations;
                if (takes.Length == 0) { Debug.LogWarning($"[LOV] {path}: no take"); continue; }
                var c = takes[0];
                c.name = role;
                c.loopTime = loop;
                c.lockRootRotation = true; c.keepOriginalOrientation = true;
                c.lockRootHeightY = true; c.keepOriginalPositionY = true; c.heightFromFeet = false;
                c.lockRootPositionXZ = !travels; c.keepOriginalPositionXZ = !travels;
                mi.clipAnimations = new[] { c };
                mi.SaveAndReimport();
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__preview__"));
                if (clip == null) continue;
                // Humanoid root motion is in units of the avatar's hip height, so
                // the clip's ground speed on our soldier is its root's travel
                // over its length times his hips (Unity's averageSpeed reads
                // the crawl at 0.09, a quarter of what the curves say).
                float speed = travels ? RootTravel(clip) / clip.length * HipHeight() : 0f;
                found.Add(travels ? $"{role} ({speed:F2} m/s)" : role);
                ImportedMixamo.Add(role);
                yield return (role, clip, speed);
            }
            Debug.Log(found.Count == 0 ? "[LOV] Mixamo: none downloaded yet (Assets/_Licensed/Mixamo); interim clips throughout"
                                       : $"[LOV] Mixamo: {found.Count} of {Mixamo.Length}: {string.Join(", ", found)}");
        }

        [System.Serializable] private class ClipMeta { public string name; public float stride_m; public float seconds; }
        [System.Serializable] private class RigMeta { public ClipMeta[] clips; }

        /// <summary>Each gait clip's own speed: its measured stride over its cycle.</summary>
        private static Dictionary<string, float> ClipSpeeds()
        {
            var json = File.ReadAllText($"{Dir}/soldier_us.json");
            var meta = JsonUtility.FromJson<RigMeta>(json);
            return (meta.clips ?? new ClipMeta[0]).Where(c => c.stride_m > 0 && c.seconds > 0)
                .ToDictionary(c => c.name, c => c.stride_m / c.seconds);
        }

        private static AvatarMask UpperBody()
        {
            var m = new AvatarMask { name = "upper body" };
            for (var p = (AvatarMaskBodyPart)0; p < AvatarMaskBodyPart.LastBodyPart; p++)
                m.SetHumanoidBodyPartActive(p, p is AvatarMaskBodyPart.Body or AvatarMaskBodyPart.Head
                    or AvatarMaskBodyPart.LeftArm or AvatarMaskBodyPart.RightArm
                    or AvatarMaskBodyPart.LeftFingers or AvatarMaskBodyPart.RightFingers
                    or AvatarMaskBodyPart.LeftHandIK or AvatarMaskBodyPart.RightHandIK);
            return m;
        }

        private static AnimatorController BuildController(Dictionary<string, AnimationClip> clips, Dictionary<string, float> speed)
        {
            // Rebuilt in the same file, so its GUID (and every reference to it) holds.
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ctrl.layers = new AnimatorControllerLayer[0];
            ctrl.parameters = new AnimatorControllerParameter[0];
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (o != null && o != ctrl) Object.DestroyImmediate(o, true);

            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter(new AnimatorControllerParameter { name = "SpeedScale", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            ctrl.AddParameter("Posture", AnimatorControllerParameterType.Int);
            ctrl.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("DeathIndex", AnimatorControllerParameterType.Int);
            ctrl.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Reload", AnimatorControllerParameterType.Trigger);

            AnimationClip C(string n) => clips.TryGetValue(n, out var c) ? c : throw new System.Exception($"no clip {n}");
            // A role is Mixamo's clip where it has been downloaded, else the interim one.
            string R(string mixamo, string interim) => clips.ContainsKey(mixamo) ? mixamo : interim;

            // --- body: posture, and speed within it ---
            ctrl.AddLayer("Body");
            var sm = ctrl.layers[0].stateMachine;
            AnimatorState Tree(string name, params (string clip, float at)[] m)
            {
                var st = ctrl.CreateBlendTreeInController(name, out var bt, 0);
                bt.blendType = BlendTreeType.Simple1D;
                bt.blendParameter = "Speed";
                bt.useAutomaticThresholds = false;
                foreach (var (clip, at) in m) bt.AddChild(C(clip), at);
                st.speedParameterActive = true;
                st.speedParameter = "SpeedScale";
                return st;
            }
            var stand = Tree("Stand", ("idle", 0f), ("walk", speed["walk"]), ("run", speed["run"]));
            var crouch = Tree("Crouch", ("kneel", 0f), ("crouch", speed["crouch"]));
            var prone = clips.ContainsKey("crawl") && speed.ContainsKey("crawl")
                ? Tree("Prone", (R("prone_idle", "prone"), 0f), ("crawl", speed["crawl"]))
                : Tree("Prone", (R("prone_idle", "prone"), 0f));
            sm.defaultState = stand;

            // §5.4's timing: down in 0.20 s, up in 0.62 s.
            void Go(AnimatorState a, AnimatorState b, int posture, float seconds)
            {
                var tr = a.AddTransition(b);
                tr.hasExitTime = false; tr.hasFixedDuration = true; tr.duration = seconds;
                tr.AddCondition(AnimatorConditionMode.Equals, posture, "Posture");
                tr.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            }
            Go(stand, crouch, 1, 0.20f); Go(stand, prone, 2, 0.30f);
            Go(crouch, stand, 0, 0.62f); Go(crouch, prone, 2, 0.25f);
            Go(prone, stand, 0, 0.62f); Go(prone, crouch, 1, 0.50f);

            var mixDeaths = MixamoDeaths.Where(clips.ContainsKey).ToArray();
            var deaths = mixDeaths.Length > 0 ? mixDeaths : new[] { "dead0", "dead1" }.Where(clips.ContainsKey).ToArray();
            bool proneDeath = clips.ContainsKey("prone_death");
            for (int k = 0; k < deaths.Length; k++)
            {
                var st = sm.AddState("Dead " + k);
                st.motion = C(deaths[k]);
                var tr = sm.AddAnyStateTransition(st);
                tr.hasExitTime = false; tr.hasFixedDuration = true; tr.duration = 0.2f;
                tr.canTransitionToSelf = false;
                tr.AddCondition(AnimatorConditionMode.If, 0, "Dead");
                tr.AddCondition(AnimatorConditionMode.Equals, k, "DeathIndex");
                if (proneDeath) tr.AddCondition(AnimatorConditionMode.NotEqual, 2, "Posture");
            }
            if (proneDeath)
            {
                // A man shot lying down dies lying down.
                var st = sm.AddState("Dead prone");
                st.motion = C("prone_death");
                var tr = sm.AddAnyStateTransition(st);
                tr.hasExitTime = false; tr.hasFixedDuration = true; tr.duration = 0.2f;
                tr.canTransitionToSelf = false;
                tr.AddCondition(AnimatorConditionMode.If, 0, "Dead");
                tr.AddCondition(AnimatorConditionMode.Equals, 2, "Posture");
            }

            // --- aim: the upper body to the shoulder, over whatever the legs are doing ---
            var mask = UpperBody();
            AssetDatabase.AddObjectToAsset(mask, ctrl);
            ctrl.AddLayer("Aim");
            var layers = ctrl.layers;
            layers[1].avatarMask = mask;
            layers[1].defaultWeight = 0f;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            ctrl.layers = layers;
            var aim = ctrl.layers[1].stateMachine;
            var sa = aim.AddState("Stand"); sa.motion = C("stand_aim");
            var ka = aim.AddState("Kneel"); ka.motion = C("kneel_aim");
            var pa = aim.AddState("Prone"); pa.motion = C(R("prone_fire", "prone_aim"));
            aim.defaultState = sa;
            foreach (var (a, b, p) in new[] { (sa, ka, 1), (sa, pa, 2), (ka, sa, 0), (ka, pa, 2), (pa, sa, 0), (pa, ka, 1) })
            {
                var tr = a.AddTransition(b);
                tr.hasExitTime = false; tr.hasFixedDuration = true; tr.duration = 0.2f;
                tr.AddCondition(AnimatorConditionMode.Equals, p, "Posture");
            }
            // --- react: rounds close by, a man flinches, ducks, is knocked (full body, over the rest) ---
            var hits = new (string clip, int posture)[] { (R("flinch", "hit_back"), 0), ("kneel_hit", 1), ("prone_hit", 2) }
                .Where(h => clips.ContainsKey(h.clip)).ToArray();
            // Reloads: cosmetic, in a lull (ArmyView decides when), cut if he fires.
            var reloads = new (string clip, int posture)[] { ("reload", 0), ("prone_reload", 2) }
                .Where(h => clips.ContainsKey(h.clip)).ToArray();
            if (hits.Length + reloads.Length > 0)
            {
                ctrl.AddLayer("React");
                var rl = ctrl.layers;
                rl[2].defaultWeight = 1f;
                rl[2].blendingMode = AnimatorLayerBlendingMode.Override;
                ctrl.layers = rl;
                var react = ctrl.layers[2].stateMachine;
                // An empty state lets the layers below show through.
                var calm = react.AddState("Calm");
                react.defaultState = calm;
                foreach (var (clip, posture, trigger) in hits.Select(h => (h.clip, h.posture, "Hit"))
                                                          .Concat(reloads.Select(h => (h.clip, h.posture, "Reload"))))
                {
                    var st = react.AddState(trigger + " " + posture);
                    st.motion = C(clip);
                    var into = calm.AddTransition(st);
                    into.hasExitTime = false; into.hasFixedDuration = true; into.duration = 0.1f;
                    into.AddCondition(AnimatorConditionMode.If, 0, trigger);
                    into.AddCondition(AnimatorConditionMode.Equals, posture, "Posture");
                    into.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
                    var back = st.AddTransition(calm);
                    back.hasExitTime = true; back.exitTime = trigger == "Reload" ? 0.9f : 0.8f; back.hasFixedDuration = true; back.duration = 0.3f;
                    var dead = st.AddTransition(calm);
                    dead.hasExitTime = false; dead.hasFixedDuration = true; dead.duration = 0.1f;
                    dead.AddCondition(AnimatorConditionMode.If, 0, "Dead");
                }
            }

            EditorUtility.SetDirty(ctrl);
            Debug.Log($"[LOV] controller: stand (idle, walk {speed["walk"]:F2} m/s, run {speed["run"]:F2}), " +
                      $"crouch ({speed["crouch"]:F2}), prone ({(clips.ContainsKey("crawl") ? "crawl" : "still")}), " +
                      $"{deaths.Length} deaths{(proneDeath ? " + prone" : "")}, aim layer, {hits.Length} reactions, {reloads.Length} reloads");
            return ctrl;
        }

        private static Material Material(string name)
        {
            string path = $"{Dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/{name}_albedo.png"));
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/{name}_normal.png"));
            m.SetFloat("_BumpScale", 1f);
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/{name}_mask.png");
            // One mask, two readers: URP Lit takes metallic from R and smoothness
            // from A of the metallic map, and occlusion from G of the occlusion map.
            m.SetTexture("_MetallicGlossMap", mask);
            m.SetTexture("_OcclusionMap", mask);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_OcclusionStrength", 0.8f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.EnableKeyword("_NORMALMAP");
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.EnableKeyword("_OCCLUSIONMAP");
            m.enableInstancing = false;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// Where two hands hold the rifle, in its own space, from its shape: its
        /// long axis (the vertices' principal direction, toward the muzzle), its
        /// up (the second, turned to the bind pose's up), and along the axis from
        /// the butt, the right wrist under the pistol grip 0.25 m on and the
        /// left under the handguard 0.58 m on (an M16 and an AK alike).
        /// </summary>
        private static (Vector3 gripR, Vector3 gripL, Vector3 up) Grips(Mesh mesh, Vector3 towardMuzzle, Vector3 bindUp)
        {
            var v = mesh.vertices;
            var c = Vector3.zero;
            foreach (var p in v) c += p;
            c /= v.Length;
            var cov = new float[3, 3];
            foreach (var p0 in v)
            {
                var p = p0 - c;
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) cov[i, j] += p[i] * p[j];
            }
            Vector3 Mul(Vector3 x) => new Vector3(cov[0, 0] * x.x + cov[0, 1] * x.y + cov[0, 2] * x.z,
                                                  cov[1, 0] * x.x + cov[1, 1] * x.y + cov[1, 2] * x.z,
                                                  cov[2, 0] * x.x + cov[2, 1] * x.y + cov[2, 2] * x.z);
            var axis = towardMuzzle.normalized;
            for (int k = 0; k < 60; k++) axis = Mul(axis).normalized;
            if (Vector3.Dot(axis, towardMuzzle) < 0) axis = -axis;
            var up = Vector3.ProjectOnPlane(bindUp, axis).normalized;
            for (int k = 0; k < 60; k++) up = Vector3.ProjectOnPlane(Mul(up), axis).normalized;
            if (Vector3.Dot(up, bindUp) < 0) up = -up;
            float butt = v.Min(p => Vector3.Dot(p - c, axis));
            var b0 = c + axis * butt;
            return (b0 + axis * 0.25f - up * 0.08f, b0 + axis * 0.58f - up * 0.06f, up);
        }

        /// <summary>
        /// Mixamo's men hold their rifle in one grip, fixed to the right hand, in
        /// every clip. Learned once from its prone firing clip, where the rifle
        /// lies level along his facing, upright, the grip at the right wrist; then
        /// kept as the rifle's place in the hand for every Mixamo clip.
        /// </summary>
        private static void LearnMixamoGrip(SoldierFigure fig, GameObject body, Dictionary<string, Transform> t)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath($"{MixamoDir}/prone_fire.fbx").OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__"));
            if (clip == null) return;
            var all = body.GetComponentsInChildren<Transform>(true);
            var bind = all.Select(x => (x.localPosition, x.localRotation)).ToArray();
            clip.SampleAnimation(body, clip.length * 0.25f);
            var hand = t["hand_r"];
            var rifle = t["rifle"];
            Vector3 fwd = body.transform.forward, up = Vector3.up;
            Vector3 aL = (fig.RifleGripL - fig.RifleGripR).normalized, uL = Vector3.ProjectOnPlane(fig.RifleUp, aL);
            var rot = Quaternion.LookRotation(fwd, up) * Quaternion.Inverse(Quaternion.LookRotation(aL, uL));
            var pos = hand.position - rot * Vector3.Scale(fig.RifleGripR, rifle.lossyScale);
            fig.RifleInHandRot = Quaternion.Inverse(hand.rotation) * rot;
            fig.RifleInHandPos = hand.InverseTransformPoint(pos);
            for (int i = 0; i < all.Length; i++) all[i].SetLocalPositionAndRotation(bind[i].localPosition, bind[i].localRotation);
            Debug.Log($"[LOV] {body.transform.parent.name}: Mixamo's rifle grip learned from prone_fire");
        }

        private static void BuildPrefab(string name, Avatar avatar, AnimatorController ctrl, Dictionary<string, float> speed)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/{name}.fbx");
            var root = new GameObject(name);
            try
            {
                var body = Object.Instantiate(model, root.transform, false);
                body.name = "body";
                var t = Find(body.transform);
                if (!body.TryGetComponent<Animator>(out var anim)) anim = body.AddComponent<Animator>();
                anim.avatar = avatar;
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;     // stepped by hand, by SoldierFigure
                var mat = Material(name);
                var smr = body.GetComponentInChildren<SkinnedMeshRenderer>();
                smr.sharedMaterial = mat;
                smr.updateWhenOffscreen = false;
                smr.skinnedMotionVectors = false;
                smr.shadowCastingMode = ShadowCastingMode.On;
                var rifle = t["rifle"].GetComponent<MeshRenderer>();
                rifle.sharedMaterial = mat;
                rifle.shadowCastingMode = ShadowCastingMode.On;

                var fig = root.AddComponent<SoldierFigure>();
                fig.Animator = anim;
                fig.Body = smr;
                fig.Rifle = rifle;
                fig.GripL = t["grip_l"];
                fig.Muzzle = t["muzzle"];
                fig.HandR = t["hand_r"]; fig.HandL = t["hand_l"]; fig.LowerArmR = t["lowerarm_r"];
                fig.Chest = t["spine_03"];
                // The bind pose does not hold the rifle (it floats at his chest, 38
                // cm from either wrist; the earlier build posed the arms onto it),
                // so where the hands go comes from the rifle's own shape.
                var rt = rifle.transform;
                var mesh = rifle.GetComponent<MeshFilter>().sharedMesh;
                (fig.RifleGripR, fig.RifleGripL, fig.RifleUp) = Grips(mesh, fig.Muzzle.localPosition - mesh.bounds.center, rt.InverseTransformDirection(Vector3.up));
                fig.WalkSpeed = speed["walk"]; fig.RunSpeed = speed["run"]; fig.CrouchSpeed = speed["crouch"];
                fig.CrawlSpeed = speed.TryGetValue("crawl", out var c) ? c : 0.4f;
                var have = new HashSet<string>(ImportedMixamo);
                fig.MixamoStand = have.Contains("fire") || have.Contains("idle");
                fig.MixamoCrouch = have.Contains("kneel_idle");
                fig.MixamoProne = have.Contains("prone_idle");
                fig.Deaths = ctrl.layers[0].stateMachine.states.Count(s => s.state.name.StartsWith("Dead ") && s.state.name != "Dead prone");

                LearnMixamoGrip(fig, body, t);

                // Humanoid places the body by the avatar's own facing; measure where
                // he ends up facing, then put the bones back in the bind pose.
                var all = body.GetComponentsInChildren<Transform>(true);
                var bind = all.Select(x => (x.localPosition, x.localRotation)).ToArray();
                anim.Rebind();
                anim.Update(0f);
                var face = Facing(Find(body.transform), out _);
                // Snapped to a quarter turn: this catches an axis mistake, not the
                // idle's own stance (its hips stand ~9 degrees off his facing).
                float yaw = Mathf.Round(Vector3.SignedAngle(face, Vector3.forward, Vector3.up) / 90f) * 90f;
                for (int i = 0; i < all.Length; i++) all[i].SetLocalPositionAndRotation(bind[i].localPosition, bind[i].localRotation);
                body.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                Debug.Log($"[LOV] {name}: animated, faces {face.ToString("F2")}; body turned {yaw:F0} deg to face +Z. " +
                          $"grips {Vector3.Distance(fig.RifleGripR, fig.RifleGripL) * 100:F0} cm apart, muzzle {Vector3.Distance(fig.RifleGripR, fig.Muzzle.localPosition) * 100:F0} cm ahead of the right");
                PrefabUtility.SaveAsPrefabAsset(root, $"{Dir}/{name}.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
