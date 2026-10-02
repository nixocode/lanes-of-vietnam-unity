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

        /// <summary>The men, three a side (tools/blender/soldier_body.py): prefab soldier_&lt;name&gt;.</summary>
        public static readonly string[] Us = { "us_a", "us_b", "us_c" }, Vc = { "vc_a", "vc_b", "vc_c" };

        [MenuItem("Lanes of Vietnam/Build Soldiers")]
        public static void Build()
        {
            var men = Us.Concat(Vc).Select(n => "soldier_" + n).ToArray();
            // Re-imported under ArtImport's rules: a changed rule does not re-import by itself.
            foreach (var man in men)
            {
                AssetDatabase.ImportAsset($"{Dir}/{man}.fbx", ImportAssetOptions.ForceUpdate);
                foreach (var f in new[] { "albedo", "normal", "mask" })
                    AssetDatabase.ImportAsset($"{Dir}/{man}_{f}.png", ImportAssetOptions.ForceUpdate);
            }
            AssetDatabase.ImportAsset($"{WeaponDir}/weapons.fbx", ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset($"{WeaponDir}/weapons_palette.png", ImportAssetOptions.ForceUpdate);
            var avatars = men.ToDictionary(m => m, BuildAvatar);
            var clips = ImportPoses(avatars[men[0]]);
            var speeds = ClipSpeeds();
            foreach (var (name, clip, speed) in ImportMixamo())
            {
                clips[name] = clip;
                if (speed > 0) speeds[name] = speed;
            }
            var ctrl = BuildController(clips, speeds);
            foreach (var man in men) BuildPrefab(man, avatars[man], ctrl, speeds);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LOV] soldiers built: {string.Join(", ", men)}");
        }

        /// <summary>The built prefabs of one side, for the scene.</summary>
        public static SoldierFigure[] Figures(string[] side) => side
            .Select(n => AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/soldier_{n}.prefab"))
            .Where(g => g != null).Select(g => g.GetComponent<SoldierFigure>()).ToArray();

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
                // The legs stay as rigged: nearly straight, with the knee's slight
                // natural bend. Straightened exactly, a leg has no bend to say
                // which way its knee points; Humanoid then chose one, and every
                // clip played with the shin rolled half a turn against the thigh:
                // knees and hips pinched to a point, feet hanging toe-down (the
                // owner's second playtest: "legs are all janked out of place").
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
            // From the Pro Rifle Pack, exported clip by clip (the pack's own export fails).
            ("rifle_idle", true, false), ("rifle_idle_aim", true, false), ("rifle_walk", true, true), ("rifle_run", true, true),
            ("rifle_sprint", true, true), ("rifle_walk_back", true, true), ("rifle_run_back", true, true),
            ("rifle_crouch_walk", true, true), ("rifle_crouch_walk_back", true, true), ("rifle_crouch_idle", true, false),
            ("rifle_crouch_aim", true, false), ("death_front_head", false, false), ("death_right", false, false),
            ("death_crouch_head", false, false), ("death_back_head", false, false), ("death_back", false, false),
            ("death_front", false, false),
            // Fieldcraft (PLAN §12.15): the blow hand to hand, and into and out of a trench. Their
            // travel stays in the root, where the game leaves it: the simulation says where a man is.
            ("melee_stab", false, true), ("melee_slash", false, true), ("trench_in", false, true), ("trench_out", false, true),
        };

        private static readonly List<string> ImportedMixamo = new List<string>();

        /// <summary>The deaths standing or kneeling, in the order a man's death index picks them.</summary>
        private static readonly string[] MixamoDeaths =
            { "death_front_head", "death_right", "death_back_head", "death_back", "death_front", "death_rifle", "death_fall_back", "death_backwards" };
        /// <summary>DeathIndex codes for the deaths chosen by circumstance (SoldierFigure.DeathCode picks).</summary>
        public const int RunDeath = 100, BlastDeath = 101, CrouchDeath = 102, ProneDeath = 103;

        /// <summary>Which way Unity's rotation offset turns (+1 or -1): set by measurement.</summary>
        private const float OrientationSign = -1f;      // +1 turned the hips 57 degrees the wrong way (measured)

        /// <summary>
        /// The average yaw of the clip's body as Mixamo authored it (its RootQ),
        /// or null for a man not upright (lying down: the yaw of a body facing
        /// the ground means nothing).
        /// </summary>
        private static float? AuthoredYaw(string path)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__"));
            if (clip == null) return null;
            var bind = AnimationUtility.GetCurveBindings(clip);
            AnimationCurve Curve(string prop) { var b = bind.FirstOrDefault(x => x.propertyName == prop); return b.propertyName == null ? null : AnimationUtility.GetEditorCurve(clip, b); }
            var qx = Curve("RootQ.x"); var qy = Curve("RootQ.y"); var qz = Curve("RootQ.z"); var qw = Curve("RootQ.w");
            if (qx == null || qy == null || qz == null || qw == null) return null;
            Vector2 sum = Vector2.zero;
            int n = 0;
            for (float t = 0; t <= clip.length; t += 1f / 30f, n++)
            {
                var q = new Quaternion(qx.Evaluate(t), qy.Evaluate(t), qz.Evaluate(t), qw.Evaluate(t));
                var f = q * Vector3.forward;
                if (new Vector2(f.x, f.z).magnitude < 0.6f) return null;      // not upright
                sum += new Vector2(f.x, f.z).normalized;
            }
            return Mathf.Atan2(sum.x, sum.y) * Mathf.Rad2Deg;
        }

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
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/soldier_us_a.fbx"));
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
                // Upright clips: "Original" orientation turned Mixamo's men 90
                // degrees to their left on our avatar (their own files and ours both
                // read true; the turn comes in on import). Body orientation, offset
                // by the clip's own measured blade, gives back the stance as
                // authored: the rifle along the clip's forward, the hips ~57 degrees
                // to its right. Prone clips read true as they are.
                var blade = AuthoredYaw(path);
                if (blade.HasValue) { c.keepOriginalOrientation = false; c.rotationOffset = blade.Value * OrientationSign; }
                c.lockRootHeightY = true; c.keepOriginalPositionY = true; c.heightFromFeet = false;
                // "Climbing Up Wall" goes up a wall twice a man's height. Its rise is left in the
                // root with its travel: a trench's own depth is the climb, and ArmyView gives it.
                if (role == "trench_out") { c.lockRootHeightY = false; c.heightFromFeet = true; }
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
            var json = File.ReadAllText($"{Dir}/soldier_us_a.json");
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
            foreach (var t in new[] { "Hit", "Reload", "Throw" }) ctrl.AddParameter(t, AnimatorControllerParameterType.Trigger);

            AnimationClip C(string n) => clips.TryGetValue(n, out var c) ? c : throw new System.Exception($"no clip {n}");
            // A role: the first of its clips that exists (Mixamo's first, the interim one last).
            string R(params string[] options) => options.FirstOrDefault(clips.ContainsKey);
            float S(string clip) => clip != null && speed.TryGetValue(clip, out var v) ? v : 0f;
            var log = new List<string>();

            // --- body: posture, and speed within it ---
            ctrl.AddLayer("Body");
            var sm = ctrl.layers[0].stateMachine;
            AnimatorState Tree(string name, params (string clip, float at)[] m)
            {
                var st = ctrl.CreateBlendTreeInController(name, out var bt, 0);
                bt.blendType = BlendTreeType.Simple1D;
                bt.blendParameter = "Speed";
                bt.useAutomaticThresholds = false;
                foreach (var (clip, at) in m.Where(x => x.clip != null)) bt.AddChild(C(clip), at);
                st.speedParameterActive = true;
                st.speedParameter = "SpeedScale";
                log.Add($"{name} ({string.Join(", ", m.Where(x => x.clip != null).Select(x => x.at != 0 ? $"{x.clip} {x.at:F2} m/s" : x.clip))})");
                return st;
            }
            string walk = R("rifle_walk", "walk"), run = R("rifle_run", "run"), cwalk = R("rifle_crouch_walk", "crouch");
            // Backwards (a negative Speed): a man giving ground a few paces keeps his front to the enemy.
            string walkBack = R("rifle_walk_back"), runBack = R("rifle_run_back"), cback = R("rifle_crouch_walk_back");
            var stand = Tree("Stand", (runBack, -S(runBack)), (walkBack, -S(walkBack)), (R("rifle_idle", "idle"), 0f), (walk, S(walk)), (run, S(run)));
            var crouch = Tree("Crouch", (cback, -S(cback)), (R("kneel_idle", "kneel"), 0f), (cwalk, S(cwalk)));
            var prone = Tree("Prone", (R("prone_idle", "prone"), 0f), (R("crawl"), S("crawl")));
            sm.defaultState = stand;
            var posture = new[] { stand, crouch, prone };

            AnimatorStateTransition When(AnimatorState a, AnimatorState b, int p, float blend)
            {
                var tr = a.AddTransition(b);
                tr.hasExitTime = false; tr.hasFixedDuration = true; tr.duration = blend;
                tr.AddCondition(AnimatorConditionMode.Equals, p, "Posture");
                tr.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
                return tr;
            }
            // A posture change through Mixamo's own transition clip, played in the
            // time §5.4 allows (down 0.45 s, up 0.62 s; at most 3x the clip's
            // speed). Without its clip, a crossfade of the same length.
            AnimatorState Between(string name, string clip, float seconds)
            {
                if (clip == null || !clips.ContainsKey(clip)) return null;
                var st = sm.AddState(name);
                st.motion = C(clip);
                st.speed = Mathf.Clamp(C(clip).length / seconds, 1f, 3f);
                log.Add($"{name} ({clip}, {C(clip).length / st.speed:F2} s)");
                return st;
            }
            void Change(int from, int to, AnimatorState via, int next = -1)
            {
                if (via == null) { When(posture[from], posture[to], to, to > from ? 0.25f : 0.62f); return; }
                When(posture[from], via, to, 0.12f);
                // Changed his mind half-way: straight to where he is now told to be.
                for (int p = 0; p < 3; p++)
                    if (p != to) When(via, posture[p], p, 0.25f);
                var done = via.AddTransition(posture[to]);
                done.hasExitTime = true; done.exitTime = 0.92f; done.hasFixedDuration = true; done.duration = 0.12f;
            }
            var s2k = Between("Stand to kneel", "stand_to_kneel", 0.45f);
            var k2s = Between("Kneel to stand", "kneel_to_stand", 0.62f);
            var k2p = Between("Kneel to prone", "kneel_to_prone", 0.5f);
            var p2k = Between("Prone to kneel", "prone_to_kneel", 0.62f);
            var s2p = Between("Stand to prone", "crouch_to_prone", 0.5f);
            Change(0, 1, s2k); Change(1, 0, k2s); Change(1, 2, k2p); Change(2, 1, p2k); Change(0, 2, s2p);
            if (p2k != null && k2s != null)
            {
                // Prone to standing: up to the knee, then up, in one movement.
                When(prone, p2k, 0, 0.12f);
                var on = p2k.AddTransition(k2s);
                on.hasExitTime = true; on.exitTime = 0.9f; on.hasFixedDuration = true; on.duration = 0.1f;
                on.AddCondition(AnimatorConditionMode.Equals, 0, "Posture");
                p2k.transitions = new[] { on }.Concat(p2k.transitions.Where(t => t != on)).ToArray();
            }
            else Change(2, 0, null);

            // --- deaths: chosen by how he died (SoldierFigure.DeathCode) ---
            var mixDeaths = MixamoDeaths.Where(clips.ContainsKey).ToArray();
            var deaths = mixDeaths.Length > 0 ? mixDeaths : new[] { "dead0", "dead1" }.Where(clips.ContainsKey).ToArray();
            void Death(string name, string clip, int code)
            {
                var st = sm.AddState(name);
                st.motion = C(clip);
                var tr = sm.AddAnyStateTransition(st);
                tr.hasExitTime = false; tr.hasFixedDuration = true; tr.duration = 0.15f;
                tr.canTransitionToSelf = false;
                tr.AddCondition(AnimatorConditionMode.If, 0, "Dead");
                tr.AddCondition(AnimatorConditionMode.Equals, code, "DeathIndex");
            }
            for (int k = 0; k < deaths.Length; k++) Death("Dead " + k, deaths[k], k);
            var special = new (string clip, int code)[] { ("death_run", RunDeath), ("death_blast", BlastDeath),
                                                          ("death_crouch_head", CrouchDeath), ("prone_death", ProneDeath) };
            foreach (var (clip, code) in special.Where(x => clips.ContainsKey(x.clip))) Death("Dead " + clip, clip, code);
            log.Add($"{deaths.Length} deaths + {string.Join(", ", special.Where(x => clips.ContainsKey(x.clip)).Select(x => x.clip))}");

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
            var sa = aim.AddState("Stand"); sa.motion = C(R("rifle_idle_aim", "stand_aim"));
            var ka = aim.AddState("Kneel"); ka.motion = C(R("rifle_crouch_aim", "kneel_aim"));   // "Rifle Kneel To Aim" folded him in half over the kneel
            var pa = aim.AddState("Prone"); pa.motion = C(R("prone_fire", "prone_aim"));
            aim.defaultState = sa;
            foreach (var (a, b, p) in new[] { (sa, ka, 1), (sa, pa, 2), (ka, sa, 0), (ka, pa, 2), (pa, sa, 0), (pa, ka, 1) })
            {
                var tr = a.AddTransition(b);
                tr.hasExitTime = false; tr.hasFixedDuration = true; tr.duration = 0.2f;
                tr.AddCondition(AnimatorConditionMode.Equals, p, "Posture");
            }
            log.Add($"aim ({sa.motion.name}, {ka.motion.name}, {pa.motion.name})");

            // --- react: flinch, hit, reload, throw (full body, over the rest; an empty state lets the rest show) ---
            var acts = new (string clip, int posture, string trigger)[]
            {
                (R("flinch", "hit_back"), 0, "Hit"), (R("kneel_hit"), 1, "Hit"), (R("prone_hit"), 2, "Hit"),
                (R("reload"), 0, "Reload"), (R("prone_reload"), 2, "Reload"),
                (R("grenade"), 0, "Throw"), (R("grenade"), 1, "Throw"),
            }.Where(x => x.clip != null).ToArray();
            if (acts.Length > 0)
            {
                ctrl.AddLayer("React");
                var rl = ctrl.layers;
                rl[2].defaultWeight = 1f;
                rl[2].blendingMode = AnimatorLayerBlendingMode.Override;
                ctrl.layers = rl;
                var react = ctrl.layers[2].stateMachine;
                var calm = react.AddState("Calm");
                react.defaultState = calm;
                foreach (var (clip, p, trigger) in acts)
                {
                    var st = react.AddState(trigger + " " + p);
                    st.motion = C(clip);
                    var into = calm.AddTransition(st);
                    into.hasExitTime = false; into.hasFixedDuration = true; into.duration = 0.1f;
                    into.AddCondition(AnimatorConditionMode.If, 0, trigger);
                    into.AddCondition(AnimatorConditionMode.Equals, p, "Posture");
                    into.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
                    var back = st.AddTransition(calm);
                    back.hasExitTime = true; back.exitTime = trigger == "Hit" ? 0.8f : 0.9f; back.hasFixedDuration = true; back.duration = 0.3f;
                    var dead = st.AddTransition(calm);
                    dead.hasExitTime = false; dead.hasFixedDuration = true; dead.duration = 0.1f;
                    dead.AddCondition(AnimatorConditionMode.If, 0, "Dead");
                }
                log.Add($"react ({string.Join(", ", acts.Select(x => x.trigger + " " + x.posture + ": " + x.clip))})");

                // Fieldcraft's acts, started by name from SoldierFigure (Strike, Climb): the blow
                // sped up to the simulation's (a Mixamo stab winds up for a second), the climb
                // out to the 0.9 s the simulation gives it.
                foreach (var (state, clip, rate, leave) in new[]
                {
                    ("Melee 0", R("melee_stab"), 1.9f, 0.82f), ("Melee 1", R("melee_slash"), 1.7f, 0.82f),
                    ("Climb in", R("trench_in"), 1f, 0.9f), ("Climb out", R("trench_out"), 2.1f, 0.92f),
                })
                {
                    if (clip == null) continue;
                    var st = react.AddState(state);
                    st.motion = C(clip);
                    st.speed = rate;
                    var back = st.AddTransition(calm);
                    back.hasExitTime = true; back.exitTime = leave; back.hasFixedDuration = true; back.duration = 0.2f;
                    var dead = st.AddTransition(calm);
                    dead.hasExitTime = false; dead.hasFixedDuration = true; dead.duration = 0.1f;
                    dead.AddCondition(AnimatorConditionMode.If, 0, "Dead");
                    log.Add($"{state}: {clip} x{rate:F1}");
                }
            }

            EditorUtility.SetDirty(ctrl);
            Debug.Log("[LOV] controller: " + string.Join("; ", log));
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
        /// every clip. Learned once from the standing aiming idle, played through
        /// the Animator: the rifle along the line from the firing hand to the
        /// support hand, upright, the grip at the right wrist. (Assuming it
        /// pointed along his facing learned the clip's import error with it.)
        /// </summary>
        public const string WeaponDir = "Assets/_Project/Art/Weapons";

        /// <summary>Every weapon in weapons.fbx: its mesh, and its marker empties' places in its own space.</summary>
        private static SoldierFigure.Carried[] Weapons()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{WeaponDir}/weapons.fbx");
            if (model == null) { Debug.LogWarning("[LOV] no weapons.fbx — every man keeps the rifle he was built with (run tools/blender/weapons.py)"); return new SoldierFigure.Carried[0]; }
            var list = new List<SoldierFigure.Carried>();
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                var w = mf.transform;
                Vector3 At(string key)
                {
                    var c = w.Find($"{w.name}.{key}");
                    if (c == null) throw new System.Exception($"weapons.fbx: {w.name} has no {key}");
                    return Vector3.Scale(c.localPosition, w.localScale);
                }
                var butt = At("butt");
                list.Add(new SoldierFigure.Carried
                {
                    Name = w.name, Mesh = mf.sharedMesh,
                    GripR = At("hold_r"), GripL = At("hold_l"), Muzzle = At("muzzle"), Up = (At("up") - butt).normalized,
                    InHandRot = Quaternion.identity,
                });
            }
            if (list.Count == 0)
                Debug.LogWarning("[LOV] weapons.fbx holds no meshes: " + string.Join(", ", model.GetComponentsInChildren<Transform>(true).Select(x => x.name).Take(12)));
            return list.OrderBy(x => x.Name).ToArray();
        }

        private static Material WeaponMaterial()
        {
            string path = $"{WeaponDir}/Weapons.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Weapons" };
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{WeaponDir}/weapons_palette.png"));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Metallic", 0.25f);
            m.SetFloat("_Smoothness", 0.32f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// Which way each standing death throws him: where his hips come to rest
        /// along his facing, as the game's Animator plays it (metres; behind him is
        /// negative). A man shot from the front is given a death that goes over
        /// backwards, one shot from behind a death that pitches forward.
        /// </summary>
        private static void LearnFalls(SoldierFigure fig, GameObject body)
        {
            var anim = body.GetComponent<Animator>();
            if (fig.Hips == null || fig.Deaths == 0) return;
            var all = body.GetComponentsInChildren<Transform>(true);
            var bind = all.Select(x => (x.localPosition, x.localRotation)).ToArray();
            fig.Falls = new float[fig.Deaths];
            for (int k = 0; k < fig.Deaths; k++)
            {
                anim.Rebind();
                anim.Update(0f);
                anim.Update(0.5f);
                Vector3 stood = body.transform.InverseTransformPoint(fig.Hips.position);
                anim.SetInteger("DeathIndex", k);
                anim.SetBool("Dead", true);
                for (int n = 0; n < 28; n++) anim.Update(0.25f);
                fig.Falls[k] = (body.transform.InverseTransformPoint(fig.Hips.position) - stood).z * body.transform.lossyScale.z;
                for (int i = 0; i < all.Length; i++) all[i].SetLocalPositionAndRotation(bind[i].localPosition, bind[i].localRotation);
            }
            anim.Rebind();
            Debug.Log($"[LOV] {body.transform.parent.name}: his {fig.Deaths} standing deaths carry him " +
                      string.Join(", ", fig.Falls.Select(z => $"{z:+0.00;-0.00} m")) + " along his facing");
        }

        private static void LearnMixamoGrip(SoldierFigure fig, GameObject body, Dictionary<string, Transform> t)
        {
            if (!File.Exists($"{MixamoDir}/rifle_idle_aim.fbx")) return;
            var anim = body.GetComponent<Animator>();
            var all = body.GetComponentsInChildren<Transform>(true);
            var bind = all.Select(x => (x.localPosition, x.localRotation)).ToArray();
            // His aiming idle, as the game's Animator plays it (the Aim layer over the Stand tree).
            anim.Rebind();
            int aim = anim.GetLayerIndex("Aim");
            if (aim >= 0) anim.SetLayerWeight(aim, 1f);
            anim.Update(0f);
            anim.Update(1.0f);
            var hand = t["hand_r"];
            var rifle = t["rifle"];
            // Shouldered, the rifle runs from the firing hand to the support hand, upright.
            Vector3 along = t["hand_l"].position - hand.position;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, along);
            Vector3 aL = (fig.RifleGripL - fig.RifleGripR).normalized, uL = Vector3.ProjectOnPlane(fig.RifleUp, aL);
            var rot = Quaternion.LookRotation(along, up) * Quaternion.Inverse(Quaternion.LookRotation(aL, uL));
            var pos = hand.position - rot * Vector3.Scale(fig.RifleGripR, rifle.lossyScale);
            fig.RifleInHandRot = Quaternion.Inverse(hand.rotation) * rot;
            fig.RifleInHandPos = hand.InverseTransformPoint(pos);
            // The same for every weapon he can be given: each by its own two holds.
            for (int i = 0; i < fig.Arms.Length; i++)
            {
                var a = fig.Arms[i];
                Vector3 wa = (a.GripL - a.GripR).normalized, wu = Vector3.ProjectOnPlane(a.Up, wa);
                var wrot = Quaternion.LookRotation(along, up) * Quaternion.Inverse(Quaternion.LookRotation(wa, wu));
                a.InHandRot = Quaternion.Inverse(hand.rotation) * wrot;
                a.InHandPos = hand.InverseTransformPoint(hand.position - wrot * Vector3.Scale(a.GripR, rifle.lossyScale));
                fig.Arms[i] = a;
                if (a.Name == fig.Carrying) { fig.RifleInHandRot = a.InHandRot; fig.RifleInHandPos = a.InHandPos; }
            }
            float yaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;
            for (int i = 0; i < all.Length; i++) all[i].SetLocalPositionAndRotation(bind[i].localPosition, bind[i].localRotation);
            anim.Rebind();
            // How far each posture's aim points off his facing (the upper body
            // over the legs' blade): he turns by as much the other way to aim.
            var turn = new float[3];
            for (int p = 0; p < 3; p++)
            {
                anim.Rebind();
                anim.SetInteger("Posture", p);
                if (aim >= 0) anim.SetLayerWeight(aim, 1f);
                anim.Update(0f);
                for (int k = 0; k < 8; k++) anim.Update(0.5f);
                var line = t["hand_l"].position - hand.position;
                turn[p] = -Mathf.Atan2(line.x, line.z) * Mathf.Rad2Deg;
            }
            fig.AimTurn = new Vector3(turn[0], turn[1], turn[2]);
            for (int i = 0; i < all.Length; i++) all[i].SetLocalPositionAndRotation(bind[i].localPosition, bind[i].localRotation);
            anim.Rebind();
            Debug.Log($"[LOV] {body.transform.parent.name}: Mixamo's rifle grip learned from rifle_idle_aim (aimed {yaw:F0} deg off his facing); " +
                      $"he turns into his aim {turn[0]:F0} / {turn[1]:F0} / {turn[2]:F0} deg standing / kneeling / prone");
        }

        /// <summary>
        /// The hands closed as on a rifle. The fingers are outside the Humanoid
        /// mapping (Mixamo's curls tore them), so no clip moves them: they keep
        /// what the prefab gives them, and an open hand under a rifle reads as a
        /// man offering it. Each joint is curled about the line across the
        /// knuckles, toward the palm (the side the thumb is on).
        /// </summary>
        private static void Grip(Dictionary<string, Transform> t)
        {
            foreach (var s in new[] { "l", "r" })
            {
                var hand = t["hand_" + s];
                Vector3 across = (t["pinky_01_" + s].position - t["index_01_" + s].position).normalized;
                Vector3 along = (t["middle_01_" + s].position - hand.position).normalized;
                Vector3 normal = Vector3.Cross(across, along).normalized;
                // The palm is on the thumb's side of the hand's plane.
                float palm = Mathf.Sign(Vector3.Dot(t["thumb_03_" + s].position - hand.position, normal));
                foreach (var f in new[] { "index", "middle", "ring", "pinky" })
                {
                    float[] curl = { 52f, 68f, 38f };
                    for (int j = 0; j < 3; j++)
                    {
                        var bone = t[$"{f}_0{j + 1}_{s}"];
                        // Positive about `across` carries `along` toward `normal`; flip for the palm's side.
                        bone.rotation = Quaternion.AngleAxis(curl[j] * palm, across) * bone.rotation;
                    }
                }
                t["thumb_02_" + s].rotation = Quaternion.AngleAxis(25f * palm, across) * t["thumb_02_" + s].rotation;
            }
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
                fig.Hips = t["pelvis"];
                // His face, in the head bone's own space: the bind pose looks along the body's forward.
                fig.Head = t["head"]; fig.Neck = t["neck_01"];
                fig.HeadForward = Quaternion.Inverse(fig.Head.rotation) * body.transform.forward;
                // The bind pose does not hold the rifle (it floats at his chest, 38
                // cm from either wrist; the earlier build posed the arms onto it),
                // so where the hands go comes from the rifle's own shape.
                var rt = rifle.transform;
                var mesh = rifle.GetComponent<MeshFilter>().sharedMesh;
                (fig.RifleGripR, fig.RifleGripL, fig.RifleUp) = Grips(mesh, fig.Muzzle.localPosition - mesh.bounds.center, rt.InverseTransformDirection(Vector3.up));
                float Sp(string mixamo, string interim, float otherwise) =>
                    speed.TryGetValue(mixamo, out var a) ? a : speed.TryGetValue(interim, out var b) ? b : otherwise;
                fig.WalkSpeed = Sp("rifle_walk", "walk", 1f); fig.RunSpeed = Sp("rifle_run", "run", 2.3f);
                fig.CrouchSpeed = Sp("rifle_crouch_walk", "crouch", 0.8f); fig.CrawlSpeed = Sp("crawl", "crawl", 0.4f);
                var have = new HashSet<string>(ImportedMixamo);
                fig.MixamoStand = have.Contains("rifle_idle");
                fig.Blows = (have.Contains("melee_stab") ? 1 : 0) + (have.Contains("melee_slash") ? 1 : 0);
                fig.BlowFirst = have.Contains("melee_stab") ? 0 : 1;
                fig.Climbs = have.Contains("trench_in") && have.Contains("trench_out");
                fig.MixamoCrouch = have.Contains("kneel_idle");
                fig.MixamoProne = have.Contains("prone_idle");
                var states = ctrl.layers[0].stateMachine.states.Select(x => x.state.name).ToArray();
                fig.Deaths = states.Count(n => n.StartsWith("Dead ") && char.IsDigit(n[5]));
                fig.RunDeath = states.Contains("Dead death_run");
                fig.BlastDeath = states.Contains("Dead death_blast");
                fig.CrouchDeath = states.Contains("Dead death_crouch_head");
                fig.ProneDeath = states.Contains("Dead prone_death");

                Grip(t);
                // The weapons (tools/blender/weapons.py), in place of the one rifle the body came with:
                // its transform is kept (the right hand's child), brought to the weapons' own scale.
                fig.Arms = Weapons();
                if (fig.Arms.Length > 0)
                {
                    fig.ArmsMaterial = WeaponMaterial();
                    var ps = rt.parent.lossyScale;
                    rt.localScale = new Vector3(1f / ps.x, 1f / ps.y, 1f / ps.z);
                    fig.Carry(name.Contains("_vc_") ? "ak" : "m16");
                }
                LearnMixamoGrip(fig, body, t);
                LearnFalls(fig, body);

                // The avatar reads him facing +z (its body rotation in the bind pose
                // is the identity; measured), so the body is not turned: a check
                // made on an animated pose read a rifleman's blade as his facing.
                Debug.Log($"[LOV] {name}: grips {Vector3.Distance(fig.RifleGripR, fig.RifleGripL) * 100:F0} cm apart, " +
                          $"muzzle {Vector3.Distance(fig.RifleGripR, fig.Muzzle.localPosition) * 100:F0} cm ahead of the right");
                PrefabUtility.SaveAsPrefabAsset(root, $"{Dir}/{name}.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
