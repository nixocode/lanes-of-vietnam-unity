using UnityEngine;
using UnityEngine.Rendering;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// One man's body (PLAN §12.3): a skinned soldier on a Humanoid avatar,
    /// so Mixamo's clips and the interim ones (tools/blender/soldier_rig.py)
    /// play on both sides' bodies. ArmyView drives it from the simulation,
    /// every frame, and nothing here keeps its own time: the Animator is
    /// stepped by hand, by match time, so a paused match is a still one, 3x
    /// is three times as fast, and a capture that fast-forwards arrives
    /// settled.
    ///
    /// After each step, two things the clips cannot know:
    ///   recoil     each shot kicks the chest (and with it the arms and the
    ///              rifle) back a few degrees, gone in a tenth of a second;
    ///   left hand  on the handguard: two-bone IK to grip_l, which rides on
    ///              the rifle in the right hand. Mixamo's rifle clips hold a
    ///              rifle of their own size; ours is not quite it. Not in the
    ///              aim: shouldered, the handguard grip is beyond this arm's
    ///              reach (measured in the bake, 24 cm short), the IK threw
    ///              the elbow up over the rifle, and the aim clip already puts
    ///              the hand under the handguard where it can reach.
    /// When he falls his rifle leaves his hand and lies beside him; once he is
    /// still, his pose is baked into a plain mesh, so a field of bodies costs
    /// no skinning.
    /// </summary>
    public sealed class SoldierFigure : MonoBehaviour
    {
        public Animator Animator;
        public SkinnedMeshRenderer Body;
        public Renderer Rifle;
        public Transform GripL, Muzzle;
        public Transform UpperArmL, LowerArmL, HandL, Chest;
        /// <summary>The hand's pose relative to grip_l in the bind pose, where it holds the rifle.</summary>
        public Vector3 GripPos;
        public Quaternion GripRot = Quaternion.identity;
        /// <summary>Each gait clip's own speed, m/s: the Animator plays it faster or slower to match the man's.</summary>
        public float WalkSpeed = 1f, RunSpeed = 2.3f, CrouchSpeed = 0.8f, CrawlSpeed = 0.4f;
        public int Deaths = 2;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int ScaleId = Animator.StringToHash("SpeedScale");
        private static readonly int PostureId = Animator.StringToHash("Posture");
        private static readonly int DeadId = Animator.StringToHash("Dead");
        private static readonly int DeathId = Animator.StringToHash("DeathIndex");

        private float _recoil, _aim, _deadFor = -1f;
        private int _aimLayer = -1;
        private bool _baked;
        private Mesh _fallen;

        private void Awake()
        {
            // Stepped by hand (Step), never by Unity's clock.
            Animator.enabled = false;
            _aimLayer = Animator.GetLayerIndex("Aim");
        }

        /// <summary>
        /// Pose the man for this frame. dt is match time since the last call (0
        /// when paused); speed is his smoothed ground speed; aiming whether he
        /// holds his rifle to the shoulder.
        /// </summary>
        public void Step(float dt, float speed, int posture, bool aiming, bool dead, int deathIndex)
        {
            if (_baked) return;
            if (dead && _deadFor < 0)
            {
                _deadFor = 0;
                Animator.SetInteger(DeathId, deathIndex % Mathf.Max(1, Deaths));
                Animator.SetBool(DeadId, true);
            }
            if (_deadFor >= 0)
            {
                _deadFor += dt;
                if (_deadFor > 0.45f && Rifle != null && Rifle.transform.parent != transform) DropRifle(deathIndex);
                // Fallen and still: freeze him as a plain mesh.
                if (_deadFor > 2.5f) { Bake(); return; }
            }
            float native = posture == 2 ? CrawlSpeed : posture == 1 ? CrouchSpeed : speed > (WalkSpeed + RunSpeed) * 0.5f ? RunSpeed : WalkSpeed;
            // Between the walk and the run the blend tree's own speed follows his;
            // outside it, the clip is sped up or slowed so the feet do not skate.
            float lo = posture == 0 ? WalkSpeed : native, hi = posture == 0 ? RunSpeed : native;
            float scale = speed < 0.05f ? 1f : Mathf.Clamp(speed / Mathf.Clamp(speed, lo, hi), 0.5f, 1.8f);
            Animator.SetFloat(SpeedId, speed);
            Animator.SetFloat(ScaleId, scale);
            Animator.SetInteger(PostureId, posture);
            _aim = Mathf.MoveTowards(_aim, aiming && !dead ? 1f : 0f, dt / 0.2f);
            if (_aimLayer >= 0) Animator.SetLayerWeight(_aimLayer, _aim);
            Animator.Update(dt);

            _recoil *= Mathf.Exp(-dt / 0.06f);
            if (_deadFor < 0) PostPose();
        }

        /// <summary>Settle the Animator at once (a new man, or a capture's first frame).</summary>
        public void Settle(float speed, int posture, bool aiming, bool dead, int deathIndex)
        {
            for (int k = 0; k < 4; k++) Step(k == 0 ? 0f : 0.5f, speed, posture, aiming, dead, deathIndex);
        }

        /// <summary>A shot: the kick of the rifle into the shoulder.</summary>
        public void Fire() => _recoil = 1f;

        public Vector3 MuzzlePosition => Muzzle != null ? Muzzle.position : transform.position + Vector3.up * 1.4f;

        private void PostPose()
        {
            if (_recoil > 0.01f && Chest != null)
                Chest.rotation = Quaternion.AngleAxis(-5f * _recoil, transform.right) * Chest.rotation;
            float w = 1f - _aim;
            if (GripL != null && HandL != null && w > 0.01f)
            {
                Vector3 target = Vector3.Lerp(HandL.position, GripL.TransformPoint(GripPos), w);
                var rot = Quaternion.Slerp(HandL.rotation, GripL.rotation * GripRot, w);
                TwoBone(UpperArmL, LowerArmL, HandL, target);
                HandL.rotation = rot;
            }
        }

        /// <summary>Analytic two-bone IK: bend the elbow in its present plane, then swing the arm.</summary>
        private static void TwoBone(Transform a, Transform b, Transform c, Vector3 target)
        {
            Vector3 pa = a.position, pb = b.position, pc = c.position;
            float la = (pb - pa).magnitude, lb = (pc - pb).magnitude;
            float d = Mathf.Clamp((target - pa).magnitude, 0.02f, (la + lb) * 0.999f);
            Vector3 u = pa - pb, v = pc - pb;
            Vector3 n = Vector3.Cross(u, v);
            if (n.sqrMagnitude < 1e-8f) n = Vector3.Cross(u, Vector3.up);
            float want = Mathf.Acos(Mathf.Clamp((la * la + lb * lb - d * d) / (2 * la * lb), -1f, 1f));
            float have = Mathf.Acos(Mathf.Clamp(Vector3.Dot(u.normalized, v.normalized), -1f, 1f));
            b.rotation = Quaternion.AngleAxis((want - have) * Mathf.Rad2Deg, n.normalized) * b.rotation;
            a.rotation = Quaternion.FromToRotation(c.position - pa, target - pa) * a.rotation;
        }

        /// <summary>The rifle out of his hand, flat on the ground beside where it fell.</summary>
        private void DropRifle(int seed)
        {
            var r = Rifle.transform;
            r.SetParent(transform, true);
            if (Muzzle == null) return;
            // Barrel level, turned a little at random about the vertical, and
            // lowered until its lowest end touches the ground.
            Vector3 bar = Muzzle.position - r.position;
            Vector3 flat = new Vector3(bar.x, 0, bar.z);
            if (flat.sqrMagnitude < 1e-4f) flat = transform.forward;
            r.rotation = Quaternion.AngleAxis((seed % 7 - 3) * 9f, Vector3.up) * Quaternion.FromToRotation(bar, flat) * r.rotation;
            float low = Mathf.Min(r.position.y, Muzzle.position.y);
            r.position += Vector3.up * (transform.position.y + 0.04f - low);
        }

        /// <summary>The body as it lies, as a plain mesh: no Animator, no skinning.</summary>
        private void Bake()
        {
            _baked = true;
            var mesh = _fallen = new Mesh { name = "fallen" };
            Body.BakeMesh(mesh, true);                         // in the renderer's space, its scale included
            var go = new GameObject("fallen");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(Body.transform.position, Body.transform.rotation);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Body.sharedMaterial;
            mr.shadowCastingMode = ShadowCastingMode.On;
            Body.enabled = false;
        }

        private void OnDestroy()
        {
            if (_fallen != null) Destroy(_fallen);
        }
    }
}
