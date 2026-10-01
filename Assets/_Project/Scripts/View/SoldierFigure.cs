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
        public Transform HandR, HandL, Chest, LowerArmR, Hips;
        /// <summary>Where his body is (his hips), which a fall can carry a metre or two from where the sim has him.</summary>
        public Vector3 Centre { get; private set; }
        /// <summary>In the rifle's own space, from the bind pose: where each wrist holds it, and which way is up.</summary>
        public Vector3 RifleGripR, RifleGripL, RifleUp = Vector3.up;
        /// <summary>The rifle's place in the right hand in Mixamo's clips (learned by SoldierBuilder).</summary>
        public Vector3 RifleInHandPos;
        public Quaternion RifleInHandRot = Quaternion.identity;
        /// <summary>Which postures play Mixamo's clips (the rest are interim, held on the two-hand line).</summary>
        public bool MixamoStand, MixamoCrouch, MixamoProne;
        /// <summary>Degrees he turns into his aim, standing, kneeling, prone (measured by SoldierBuilder), so the rifle points where he faces.</summary>
        public Vector3 AimTurn;
        private int _posture;
        private float _speed;
        /// <summary>Each gait clip's own speed, m/s: the Animator plays it faster or slower to match the man's.</summary>
        public float WalkSpeed = 1f, RunSpeed = 2.3f, CrouchSpeed = 0.8f, CrawlSpeed = 0.4f;
        public int Deaths = 2;
        /// <summary>Deaths for circumstances, where Mixamo's clips exist for them.</summary>
        public bool RunDeath, BlastDeath, CrouchDeath, ProneDeath;
        /// <summary>How many clips the Animator has for a blow hand to hand (states "Melee n" from BlowFirst), and whether it has the climb into and out of a trench.</summary>
        public int Blows, BlowFirst;
        public bool Climbs;
        /// <summary>Where in its clip a blow starts (past the wind-up), and how far he is carried in behind it.</summary>
        public const float BlowFrom = 0.18f;
        /// <summary>How a man died, as the view can tell: shot, shot while running, or by a blast.</summary>
        public enum Fall { Shot, Running, Blast }
        public const int RunCode = 100, BlastCode = 101, CrouchCode = 102, ProneCode = 103;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int ScaleId = Animator.StringToHash("SpeedScale");
        private static readonly int PostureId = Animator.StringToHash("Posture");
        private static readonly int DeadId = Animator.StringToHash("Dead");
        private static readonly int DeathId = Animator.StringToHash("DeathIndex");
        private static readonly int HitId = Animator.StringToHash("Hit");
        private static readonly int ReloadId = Animator.StringToHash("Reload");
        private static readonly int ThrowId = Animator.StringToHash("Throw");
        private int _reactLayer = -1;

        private float _recoil, _aim, _deadFor = -1f;
        /// <summary>A blow hand to hand: 1 as it starts, 0 when it is over.</summary>
        private float _lunge;
        private bool _lean;
        /// <summary>Seconds a blow takes, and how far it carries him toward his man.</summary>
        public const float LungeSeconds = 0.42f, LungeReach = 0.55f;
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
        /// when paused); speed is his smoothed ground speed, negative when he is
        /// stepping backwards; aiming whether he holds his rifle to the shoulder.
        /// </summary>
        public void Step(float dt, float speed, int posture, bool aiming, bool dead, int deathIndex, Fall how = Fall.Shot)
        {
            if (_baked) return;
            if (dead && _deadFor < 0)
            {
                _deadFor = 0;
                Animator.SetInteger(DeathId, DeathCode(deathIndex, posture, how));
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
            // Backwards he has his own clips (standing and crouched); a crawl has none.
            float signed = posture == 2 ? Mathf.Abs(speed) : speed;
            speed = Mathf.Abs(speed);
            float native = posture == 2 ? CrawlSpeed : posture == 1 ? CrouchSpeed : speed > (WalkSpeed + RunSpeed) * 0.5f ? RunSpeed : WalkSpeed;
            // Between the walk and the run the blend tree's own speed follows his;
            // outside it, the clip is sped up or slowed so the feet do not skate.
            float lo = posture == 0 ? WalkSpeed : native, hi = posture == 0 ? RunSpeed : native;
            // A crawl may run fast: the sim crawls at 0.45 m/s, Mixamo's man at 0.2.
            float scale = speed < 0.05f ? 1f : Mathf.Clamp(speed / Mathf.Clamp(speed, lo, hi), 0.5f, posture == 2 ? 2.4f : 1.8f);
            Animator.SetFloat(SpeedId, signed);
            Animator.SetFloat(ScaleId, scale);
            Animator.SetInteger(PostureId, posture);
            _posture = posture;
            _speed = speed;
            _aim = Mathf.MoveTowards(_aim, aiming && !dead ? 1f : 0f, dt / 0.2f);
            if (_aimLayer >= 0) Animator.SetLayerWeight(_aimLayer, _aim);
            Animator.transform.localRotation = Quaternion.Euler(0, AimTurn[Mathf.Clamp(posture, 0, 2)] * _aim, 0);
            // The blow: his whole body goes in behind the rifle and comes back.
            _lunge = Mathf.MoveTowards(_lunge, 0f, dt / LungeSeconds);
            Animator.transform.localPosition = dead ? Vector3.zero : Vector3.forward * (LungeReach * Mathf.Sin(Mathf.PI * (1f - _lunge)) * (_lunge > 0 ? 1f : 0f));
            Animator.Update(dt);

            if (Hips != null) Centre = Hips.position;
            _recoil *= Mathf.Exp(-dt / 0.06f);
            if (_deadFor < 0) PostPose();
        }

        /// <summary>
        /// Which death: lying down, the prone death; thrown by a blast, the blast
        /// death; on one knee, the crouched one; running, the run that ends in a
        /// fall; else one of the standing deaths, by the man's own number.
        /// </summary>
        public int DeathCode(int seed, int posture, Fall how)
        {
            if (posture == 2 && ProneDeath) return ProneCode;
            if (how == Fall.Blast && BlastDeath) return BlastCode;
            if (posture == 1 && CrouchDeath) return CrouchCode;
            if (how == Fall.Running && RunDeath) return RunCode;
            return seed % Mathf.Max(1, Deaths);
        }

        /// <summary>The body layer's state, by name, for tests and the log.</summary>
        public string State
        {
            get
            {
                var info = Animator.GetCurrentAnimatorStateInfo(0);
                foreach (var n in new[] { "Stand", "Crouch", "Prone", "Stand to kneel", "Kneel to stand", "Kneel to prone", "Prone to kneel", "Stand to prone" })
                    if (info.IsName(n)) return n + (Animator.IsInTransition(0) ? " (in transition)" : "") + $" t {info.normalizedTime:F2}" + Upper();
                return "other" + Upper();
            }
        }

        private string Upper()
        {
            string r = $", aim {_aim:F2}";
            if (_reactLayer >= 0)
            {
                var info = Animator.GetCurrentAnimatorStateInfo(_reactLayer);
                foreach (var n in new[] { "Calm", "Hit 0", "Hit 1", "Hit 2", "Reload 0", "Reload 2", "Throw 0", "Throw 1" })
                    if (info.IsName(n)) r += $", react {n} t {info.normalizedTime:F2}";
            }
            return r;
        }

        /// <summary>A grenade: the toss (the React layer).</summary>
        public void Throw()
        {
            if (_deadFor < 0 && !_baked) Animator.SetTrigger(ThrowId);
        }

        /// <summary>Settle the Animator at once (a new man, or a capture's first frame); a dead man all the way down.</summary>
        public void Settle(float speed, int posture, bool aiming, bool dead, int deathIndex, Fall how = Fall.Shot)
        {
            for (int k = 0; k < (dead ? 16 : 4) && !_baked; k++) Step(k == 0 ? 0f : 0.5f, speed, posture, aiming, dead, deathIndex, how);
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

        /// <summary>
        /// A blow hand to hand (the sim's Melee): Mixamo's bayonet stab or
        /// slash, by <paramref name="which"/>, and the body carried in behind
        /// it (the clips advance; their travel is left in the root, so the
        /// lunge is given here). Without the clips, the lunge alone with the
        /// trunk over the weapon.
        /// </summary>
        public void Strike(int which = 0)
        {
            if (_deadFor >= 0 || _baked) return;
            _lunge = 1f;
            _lean = Blows == 0;
            if (_reactLayer < 0) return;
            if (Blows > 0) Animator.CrossFadeInFixedTime("Melee " + (BlowFirst + which % Blows), 0.08f, _reactLayer, 0f, BlowFrom);
            else if (Animator.GetCurrentAnimatorStateInfo(_reactLayer).IsName("Reload " + _posture))
                Animator.CrossFadeInFixedTime("Calm", 0.08f, _reactLayer);
        }

        /// <summary>Into a trench or out of it (the sim's VaultIn and VaultOut). False if there is no clip for it.</summary>
        public bool Climb(bool into)
        {
            if (_deadFor >= 0 || _baked || !Climbs || _reactLayer < 0) return false;
            Animator.CrossFadeInFixedTime(into ? "Climb in" : "Climb out", 0.08f, _reactLayer);
            return true;
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
            if (_lunge > 0f && _lean && Chest != null)
                Chest.rotation = Quaternion.AngleAxis(24f * Mathf.Sin(Mathf.PI * (1f - _lunge)), transform.right) * Chest.rotation;
            if (Rifle == null || HandR == null || HandL == null || Rifle.transform.parent == transform) return;
            var r = Rifle.transform;
            if (_posture == 2 && _speed > 0.05f && LowerArmR != null)
            {
                // Crawling: cradled along the right forearm, muzzle ahead of the hand.
                // (Mixamo's grip stands it on end through the crawl's strokes.)
                Lay(HandR.position - LowerArmR.position, HandR.position);
                return;
            }
            bool mixamo = _posture == 2 ? MixamoProne : _posture == 1 ? MixamoCrouch : MixamoStand;
            if (mixamo) r.SetLocalPositionAndRotation(RifleInHandPos, RifleInHandRot);
            Vector3 wr = HandR.position, wl = HandL.position;
            float span = (wl - wr).magnitude / Mathf.Max(0.01f, transform.lossyScale.x);
            if (span < 0.12f || span > 0.8f) return;
            // Aiming (and in the interim clips, always): along the line from the
            // firing hand to the support hand, both of which are on it.
            float w = mixamo ? _aim : 1f;
            if (w < 0.01f) return;
            var held = (r.position, r.rotation);
            Lay(wl - wr, wr);
            if (w < 0.99f) r.SetPositionAndRotation(Vector3.Lerp(held.position, r.position, w), Quaternion.Slerp(held.rotation, r.rotation, w));
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
