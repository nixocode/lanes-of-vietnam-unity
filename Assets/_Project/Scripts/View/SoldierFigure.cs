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
    ///   the rifle  in Mixamo's clips, in the grip their men hold it with,
    ///              fixed to the right hand (learned from one clip); in the
    ///              interim clips, laid along the line from the right wrist to the left,
    ///              upright, seated at the right hand, as a rifle held in two
    ///              hands is. Rigid on the right hand it kept the old rig's
    ///              grip, and Mixamo's men, who hold theirs naturally, aimed
    ///              it at the ground; and IK pulling the left hand to the old
    ///              grip twisted the wrist into torn skin. When the hands are
    ///              apart (a man knocked off his feet) it rides on the right
    ///              hand as it last lay.
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
        public Transform HandR, HandL, Chest, LowerArmR;
        /// <summary>In the rifle's own space, from the bind pose: where each wrist holds it, and which way is up.</summary>
        public Vector3 RifleGripR, RifleGripL, RifleUp = Vector3.up;
        /// <summary>The rifle's place in the right hand in Mixamo's clips (learned by SoldierBuilder).</summary>
        public Vector3 RifleInHandPos;
        public Quaternion RifleInHandRot = Quaternion.identity;
        /// <summary>Which postures play Mixamo's clips (the rest are interim, held on the two-hand line).</summary>
        public bool MixamoStand, MixamoCrouch, MixamoProne;
        private int _posture;
        private float _speed;
        /// <summary>Each gait clip's own speed, m/s: the Animator plays it faster or slower to match the man's.</summary>
        public float WalkSpeed = 1f, RunSpeed = 2.3f, CrouchSpeed = 0.8f, CrawlSpeed = 0.4f;
        public int Deaths = 2;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int ScaleId = Animator.StringToHash("SpeedScale");
        private static readonly int PostureId = Animator.StringToHash("Posture");
        private static readonly int DeadId = Animator.StringToHash("Dead");
        private static readonly int DeathId = Animator.StringToHash("DeathIndex");
        private static readonly int HitId = Animator.StringToHash("Hit");
        private static readonly int ReloadId = Animator.StringToHash("Reload");
        private int _reactLayer = -1;

        private float _recoil, _aim, _deadFor = -1f;
        private int _aimLayer = -1;
        private bool _baked;
        private Mesh _fallen;

        private void Awake()
        {
            // Stepped by hand (Step), never by Unity's clock.
            Animator.enabled = false;
            _aimLayer = Animator.GetLayerIndex("Aim");
            _reactLayer = Animator.GetLayerIndex("React");
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
                // Timed by his death clip, not a clock: Mixamo's falls run two to four seconds.
                var fall = Animator.GetCurrentAnimatorStateInfo(0);
                float through = Animator.IsInTransition(0) ? 0f : fall.normalizedTime;
                if ((through > 0.6f || _deadFor > 3f) && Rifle != null && Rifle.transform.parent != transform) DropRifle(deathIndex);
                // Fallen and still: freeze him as a plain mesh.
                if ((through >= 1f && _deadFor > 0.6f) || _deadFor > 6f) { Bake(); return; }
            }
            float native = posture == 2 ? CrawlSpeed : posture == 1 ? CrouchSpeed : speed > (WalkSpeed + RunSpeed) * 0.5f ? RunSpeed : WalkSpeed;
            // Between the walk and the run the blend tree's own speed follows his;
            // outside it, the clip is sped up or slowed so the feet do not skate.
            float lo = posture == 0 ? WalkSpeed : native, hi = posture == 0 ? RunSpeed : native;
            // A crawl may run fast: the sim crawls at 0.45 m/s, Mixamo's man at 0.2.
            float scale = speed < 0.05f ? 1f : Mathf.Clamp(speed / Mathf.Clamp(speed, lo, hi), 0.5f, posture == 2 ? 2.4f : 1.8f);
            Animator.SetFloat(SpeedId, speed);
            Animator.SetFloat(ScaleId, scale);
            Animator.SetInteger(PostureId, posture);
            _posture = posture;
            _speed = speed;
            _aim = Mathf.MoveTowards(_aim, aiming && !dead ? 1f : 0f, dt / 0.2f);
            if (_aimLayer >= 0) Animator.SetLayerWeight(_aimLayer, _aim);
            Animator.Update(dt);

            _recoil *= Mathf.Exp(-dt / 0.06f);
            if (_deadFor < 0) PostPose();
        }

        /// <summary>Settle the Animator at once (a new man, or a capture's first frame); a dead man all the way down.</summary>
        public void Settle(float speed, int posture, bool aiming, bool dead, int deathIndex)
        {
            for (int k = 0; k < (dead ? 16 : 4) && !_baked; k++) Step(k == 0 ? 0f : 0.5f, speed, posture, aiming, dead, deathIndex);
        }

        /// <summary>Rounds close by: he flinches, ducks, is knocked (the React layer, where the clips exist).</summary>
        public void React()
        {
            if (_deadFor < 0 && !_baked) Animator.SetTrigger(HitId);
        }

        /// <summary>A shot: the kick of the rifle into the shoulder; a reload he was in the middle of is over.</summary>
        public void Fire()
        {
            _recoil = 1f;
            if (_reactLayer >= 0 && _deadFor < 0 && !_baked && Animator.GetCurrentAnimatorStateInfo(_reactLayer).IsName("Reload " + _posture))
                Animator.CrossFadeInFixedTime("Calm", 0.12f, _reactLayer);
        }

        /// <summary>A lull after shooting: a fresh magazine (where the posture has a reload clip).</summary>
        public void Reload()
        {
            if (_deadFor < 0 && !_baked) Animator.SetTrigger(ReloadId);
        }

        public Vector3 MuzzlePosition => Muzzle != null ? Muzzle.position : transform.position + Vector3.up * 1.4f;

        private void PostPose()
        {
            if (_recoil > 0.01f && Chest != null)
                Chest.rotation = Quaternion.AngleAxis(-5f * _recoil, transform.right) * Chest.rotation;
            if (Rifle == null || HandR == null || HandL == null || Rifle.transform.parent == transform) return;
            if (_posture == 2 && _speed > 0.05f && LowerArmR != null)
            {
                // Crawling: cradled along the right forearm, muzzle ahead of the hand.
                // (Mixamo's grip stands it on end through the crawl's strokes.)
                Lay(HandR.position - LowerArmR.position, HandR.position);
                return;
            }
            if (_posture == 2 ? MixamoProne : _posture == 1 ? MixamoCrouch : MixamoStand)
            {
                Rifle.transform.SetLocalPositionAndRotation(RifleInHandPos, RifleInHandRot);
                return;
            }
            Vector3 wr = HandR.position, wl = HandL.position;
            float span = (wl - wr).magnitude / Mathf.Max(0.01f, transform.lossyScale.x);
            if (span < 0.12f || span > 0.8f) return;
            Lay(wl - wr, wr);
        }

        /// <summary>The rifle upright along a direction, its right-hand grip at a point.</summary>
        private void Lay(Vector3 along, Vector3 grip)
        {
            var r = Rifle.transform;
            along.Normalize();
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, along);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(transform.forward, along);
            Vector3 aL = (RifleGripL - RifleGripR).normalized;
            Vector3 uL = Vector3.ProjectOnPlane(RifleUp, aL);
            r.rotation = Quaternion.LookRotation(along, up) * Quaternion.Inverse(Quaternion.LookRotation(aL, uL));
            r.position += grip - r.TransformPoint(RifleGripR);
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
