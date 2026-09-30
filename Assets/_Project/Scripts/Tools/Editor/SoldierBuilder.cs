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

        /// <summary>The rig's bones (MPFB2's game-engine skeleton) as Humanoid's.</summary>
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
            (HumanBodyBones.LeftThumbProximal, "thumb_01_l"), (HumanBodyBones.LeftThumbIntermediate, "thumb_02_l"), (HumanBodyBones.LeftThumbDistal, "thumb_03_l"),
            (HumanBodyBones.LeftIndexProximal, "index_01_l"), (HumanBodyBones.LeftIndexIntermediate, "index_02_l"), (HumanBodyBones.LeftIndexDistal, "index_03_l"),
            (HumanBodyBones.LeftMiddleProximal, "middle_01_l"), (HumanBodyBones.LeftMiddleIntermediate, "middle_02_l"), (HumanBodyBones.LeftMiddleDistal, "middle_03_l"),
            (HumanBodyBones.LeftRingProximal, "ring_01_l"), (HumanBodyBones.LeftRingIntermediate, "ring_02_l"), (HumanBodyBones.LeftRingDistal, "ring_03_l"),
            (HumanBodyBones.LeftLittleProximal, "pinky_01_l"), (HumanBodyBones.LeftLittleIntermediate, "pinky_02_l"), (HumanBodyBones.LeftLittleDistal, "pinky_03_l"),
            (HumanBodyBones.RightThumbProximal, "thumb_01_r"), (HumanBodyBones.RightThumbIntermediate, "thumb_02_r"), (HumanBodyBones.RightThumbDistal, "thumb_03_r"),
            (HumanBodyBones.RightIndexProximal, "index_01_r"), (HumanBodyBones.RightIndexIntermediate, "index_02_r"), (HumanBodyBones.RightIndexDistal, "index_03_r"),
            (HumanBodyBones.RightMiddleProximal, "middle_01_r"), (HumanBodyBones.RightMiddleIntermediate, "middle_02_r"), (HumanBodyBones.RightMiddleDistal, "middle_03_r"),
            (HumanBodyBones.RightRingProximal, "ring_01_r"), (HumanBodyBones.RightRingIntermediate, "ring_02_r"), (HumanBodyBones.RightRingDistal, "ring_03_r"),
            (HumanBodyBones.RightLittleProximal, "pinky_01_r"), (HumanBodyBones.RightLittleIntermediate, "pinky_02_r"), (HumanBodyBones.RightLittleDistal, "pinky_03_r"),
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

            AnimationClip C(string n) => clips.TryGetValue(n, out var c) ? c : throw new System.Exception($"no clip {n}");

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
            var prone = Tree("Prone", ("prone", 0f));
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

            var deaths = new[] { "dead0", "dead1" }.Where(clips.ContainsKey).ToArray();
            for (int k = 0; k < deaths.Length; k++)
            {
                var st = sm.AddState("Dead " + k);
                st.motion = C(deaths[k]);
                var tr = sm.AddAnyStateTransition(st);
                tr.hasExitTime = false; tr.hasFixedDuration = true; tr.duration = 0.35f;
                tr.canTransitionToSelf = false;
                tr.AddCondition(AnimatorConditionMode.If, 0, "Dead");
                tr.AddCondition(AnimatorConditionMode.Equals, k, "DeathIndex");
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
            var pa = aim.AddState("Prone"); pa.motion = C("prone_aim");
            aim.defaultState = sa;
            foreach (var (a, b, p) in new[] { (sa, ka, 1), (sa, pa, 2), (ka, sa, 0), (ka, pa, 2), (pa, sa, 0), (pa, ka, 1) })
            {
                var tr = a.AddTransition(b);
                tr.hasExitTime = false; tr.hasFixedDuration = true; tr.duration = 0.2f;
                tr.AddCondition(AnimatorConditionMode.Equals, p, "Posture");
            }
            EditorUtility.SetDirty(ctrl);
            Debug.Log($"[LOV] controller: stand (idle, walk {speed["walk"]:F2} m/s, run {speed["run"]:F2}), " +
                      $"crouch ({speed["crouch"]:F2}), prone, {deaths.Length} deaths, aim layer");
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
                fig.UpperArmL = t["upperarm_l"]; fig.LowerArmL = t["lowerarm_l"]; fig.HandL = t["hand_l"];
                fig.Chest = t["spine_03"];
                // In the bind pose the left hand is on the handguard: remember where, from the grip.
                fig.GripPos = fig.GripL.InverseTransformPoint(fig.HandL.position);
                fig.GripRot = Quaternion.Inverse(fig.GripL.rotation) * fig.HandL.rotation;
                fig.WalkSpeed = speed["walk"]; fig.RunSpeed = speed["run"]; fig.CrouchSpeed = speed["crouch"];
                fig.CrawlSpeed = speed.TryGetValue("crawl", out var c) ? c : 0.4f;
                fig.Deaths = ctrl.layers[0].stateMachine.states.Count(s => s.state.name.StartsWith("Dead"));

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
                          $"Grip at {fig.GripPos.magnitude * 100:F1} cm from grip_l");
                PrefabUtility.SaveAsPrefabAsset(root, $"{Dir}/{name}.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
