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
        public Transform HandR, HandL, Chest, LowerArmR, LowerArmL, Hips;
        /// <summary>From a knee he throws with his left hand (the clip is played left for right, so his rifle hand keeps the rifle): the grenade is drawn in that one.</summary>
        public bool KneelThrowLeft;
        /// <summary>His head and neck, and which way his face points in the head's own space (SoldierBuilder, from the bind pose).</summary>
        public Transform Head, Neck;
        /// <summary>
        /// The radio man's kit (tools/blender/radio_import.py: a PRC-77, sourced, its antenna and handset built): the set
        /// he carries on his back, the handset he talks into, their material; and whether his controller has the
        /// handset to the ear (the "Radio" layer: his left arm and his head, over whatever else he is doing).
        /// </summary>
        public Mesh RadioSet, Handset;
        public Material RadioMaterial;
        public bool Radios;
        /// <summary>The set's middle behind his chest bone, and above it, metres: high on his back, between the shoulder blades, as on its frame.</summary>
        public const float RadioBehind = 0.16f, RadioUp = 0.2f;
        public Vector3 HeadForward = Vector3.forward;
        /// <summary>Where his body is (his hips), which a fall can carry a metre or two from where the sim has him.</summary>
        public Vector3 Centre { get; private set; }
        /// <summary>In the rifle's own space, from the bind pose: where each wrist holds it, and which way is up.</summary>
        public Vector3 RifleGripR, RifleGripL, RifleUp = Vector3.up;
        /// <summary>The rifle's place in the right hand in Mixamo's clips (learned by SoldierBuilder).</summary>
        public Vector3 RifleInHandPos;
        public Quaternion RifleInHandRot = Quaternion.identity;
        /// <summary>
        /// A weapon he can carry (tools/blender/weapons.py): its mesh, and in its
        /// own space where the two wrists hold it, which way is up and where the
        /// muzzle is; and its place in the right hand in Mixamo's clips.
        /// </summary>
        [System.Serializable]
        public struct Carried
        {
            public string Name;
            public Mesh Mesh;
            /// <summary>What it wears, if not the material the weapons built here share: the sourced weapons' atlas.</summary>
            public Material Material;
            public Vector3 GripR, GripL, Up, Muzzle, InHandPos;
            public Quaternion InHandRot;
        }

        /// <summary>Every weapon there is (SoldierBuilder fills it), and the material they share.</summary>
        public Carried[] Arms = new Carried[0];
        public Material ArmsMaterial;
        /// <summary>What he has in his hands now.</summary>
        public string Carrying { get; private set; } = "";

        /// <summary>Put a weapon in his hands, by name ("m16", "ak", "m60", ...). False if there is no such weapon.</summary>
        public bool Carry(string weapon)
        {
            if (weapon == Carrying) return true;
            for (int i = 0; i < Arms.Length; i++)
            {
                if (Arms[i].Name != weapon || Rifle == null) continue;
                var a = Arms[i];
                Rifle.GetComponent<MeshFilter>().sharedMesh = a.Mesh;
                if (a.Material != null) Rifle.sharedMaterial = a.Material;
                else if (ArmsMaterial != null) Rifle.sharedMaterial = ArmsMaterial;
                RifleGripR = a.GripR; RifleGripL = a.GripL; RifleUp = a.Up;
                RifleInHandPos = a.InHandPos; RifleInHandRot = a.InHandRot;
                if (Muzzle != null) Muzzle.localPosition = a.Muzzle;
                Carrying = weapon;
                return true;
            }
            return false;
        }

        /// <summary>Which postures play Mixamo's clips (the rest are interim, held on the two-hand line).</summary>
        public bool MixamoStand, MixamoCrouch, MixamoProne;
        /// <summary>Degrees he turns into his aim, standing, kneeling, prone (measured by SoldierBuilder), so the rifle points where he faces.</summary>
        public Vector3 AimTurn;
        /// <summary>
        /// The most his upper body turns from where his feet point, degrees: standing, kneeling, prone.
        /// The turn into his aim and the turn to his man together; past it, his feet turn too.
        /// </summary>
        public static readonly Vector3 MaxTwist = new Vector3(70f, 62f, 38f);
        /// <summary>How fast his upper body turns, degrees a second.</summary>
        public const float TwistRate = 300f;
        private int _posture;
        private float _speed;
        /// <summary>Each gait clip's own speed, m/s: the Animator plays it faster or slower to match the man's.</summary>
        public float WalkSpeed = 1f, RunSpeed = 2.3f, CrouchSpeed = 0.8f, CrawlSpeed = 0.4f;
        /// <summary>Above this he runs; at it and below he walks (the sim's men march at 1.35, catch up at 2 and rush at 4 m/s; the clips walk at 1.8 and run at 4.5).</summary>
        public const float RunFrom = 2.6f;
        /// <summary>
        /// Each man his own walk (the owner, playtest 9: "need more walk variations"). The walks there are for the
        /// march and for the double, each at its own speed (SoldierBuilder.Marches, Doubles; none: the one walk for
        /// both), and which of each is this man's (ArmyView gives them by his number).
        /// </summary>
        public float[] MarchSpeeds = new float[0], DoubleSpeeds = new float[0];
        public int Gait, Pace;
        /// <summary>The ground his legs are covering now, m/s: his gait's own speed as it is being played (the motion audit holds it to the ground he covers).</summary>
        public float Legs { get; private set; }
        /// <summary>
        /// Below this he is on the march, from it at the double (the simulation's men march at 0.95 to 1.25 m/s and
        /// go at 1.9 in contact); and where the march sits in the Animator's blend, below the walk.
        /// </summary>
        public const float MarchBelow = 1.5f, MarchKey = 1.0f;
        public int Deaths = 2;
        /// <summary>Deaths for circumstances, where Mixamo's clips exist for them.</summary>
        public bool RunDeath, BlastDeath, CrouchDeath, ProneDeath;
        /// <summary>A second death for three of those: hit from behind at a run, thrown forward by a burst behind him, another from a knee.</summary>
        public bool RunFlatDeath, BlastForwardDeath, CrouchDeath2;
        /// <summary>Where each standing death leaves his hips along his facing, metres (SoldierBuilder.LearnFalls): which way it throws him.</summary>
        public float[] Falls = new float[0];
        /// <summary>How many clips the Animator has for a blow hand to hand (states "Melee n" from BlowFirst), and whether it has the climb into and out of a trench.</summary>
        public int Blows, BlowFirst;
        public bool Climbs;
        /// <summary>Where in its clip a blow starts (past the wind-up), and how far he is carried in behind it.</summary>
        public const float BlowFrom = 0.18f;
        /// <summary>
        /// What a man does besides walk, fire and die, each a state of the Animator's React layer started by its
        /// name (<see cref="Act"/>): knocked over by a burst that did not kill him, the dirt out of his eyes, a
        /// dodge, a second flinch from a knee, a dive to the ground, the hand signals of a man on one knee, and
        /// two ways of standing about. <see cref="Acts"/> lists those the controller has (SoldierBuilder).
        /// </summary>
        public const string BlastFall = "Blast fall", RubEyes = "Rub eyes", Dodge = "Dodge", KneelFlinch = "Flinch 1b", Dive = "Dive",
                            SignalFollow = "Signal follow", SignalStay = "Signal stay", LookAbout = "Look about", Inspect = "Inspect";
        public string[] Acts = new string[0];
        /// <summary>How a man died, as the view can tell: shot, shot while running, or by a blast.</summary>
        public enum Fall { Shot, Running, Blast }
        public const int RunCode = 100, BlastCode = 101, CrouchCode = 102, ProneCode = 103, RunFlatCode = 104, BlastForwardCode = 105, Crouch2Code = 106;
        /// <summary>Added to one of those codes: the same death, left for right (SoldierBuilder makes the states).</summary>
        public const int MirroredCode = 10;
        public bool MirroredDeaths;

        /// <summary>A part of him a burst can take off: the bone it hangs from, and its skin as a plain mesh in that bone's space (SoldierBuilder.Limb).</summary>
        [System.Serializable]
        public struct Limb
        {
            public Transform Bone;
            public Mesh Mesh;
        }
        public Limb[] Limbs = new Limb[0];
        /// <summary>Each of them by the name of the bone it hangs from (SoldierBuilder): "lowerarm_l", "calf_r", "head", "upperarm_l", "thigh_r" and the rest.</summary>
        public string[] LimbNames = new string[0];

        /// <summary>
        /// How fast a death plays against its clip. Thrown by a burst, a man is down in a second, not three
        /// (the owner, playtest 9: "some are too slow, should be sped up when it's an explosion"); shot, each
        /// man falls at his own pace, a little quicker than Mixamo's.
        /// </summary>
        public const float BlastPace = 1.8f, FallPace = 1.05f, FallPaceSpread = 0.3f;
        /// <summary>Seconds a burst carries him for.</summary>
        public const float ThrownFor = 0.55f;
        private float _deathPace = 1f, _hop;
        private Vector3 _blown;
        /// <summary>A part of him that has come off: the bone it hung from (drawn down to nothing on his body), and the piece itself, flying or lying.</summary>
        private struct Off
        {
            public Transform Bone, Piece;
            public Vector3 Pace, Spin;
            public bool Flying;
            /// <summary>The middle of the piece and the way it is longest, in its own space; and how high its middle rests off the ground, metres.</summary>
            public Vector3 Mid, Long;
            public float Lift;
        }
        private readonly System.Collections.Generic.List<Off> _off = new System.Collections.Generic.List<Off>(MostOff);
        /// <summary>The most that comes off one man: a burst close by may take three parts of him.</summary>
        public const int MostOff = 3;
        private int _wounds;
        private bool AnyFlying { get { for (int k = 0; k < _off.Count; k++) if (_off[k].Flying) return true; return false; } }

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int ScaleId = Animator.StringToHash("SpeedScale");
        private static readonly int GaitId = Animator.StringToHash("Gait");
        private static readonly int PaceId = Animator.StringToHash("Pace");
        private static readonly int PostureId = Animator.StringToHash("Posture");
        private static readonly int DeadId = Animator.StringToHash("Dead");
        private static readonly int DeathId = Animator.StringToHash("DeathIndex");
        private static readonly int HitId = Animator.StringToHash("Hit");
        private static readonly int ReloadId = Animator.StringToHash("Reload");
        private static readonly int ThrowId = Animator.StringToHash("Throw");
        private static readonly int StandState = Animator.StringToHash("Stand");
        private static readonly int ClimbInState = Animator.StringToHash("Climb in"), ClimbOutState = Animator.StringToHash("Climb out");
        private int _reactLayer = -1;

        private float _recoil, _aim, _deadFor = -1f;
        /// <summary>The rounds of his burst still to kick, the seconds between them and until the next; and how far a long burst has climbed, 0 to 1.</summary>
        private int _kicks;
        private float _kickGap, _kickIn, _climb;
        /// <summary>What his barrel is brought onto (a point in the world), and whether there is anything.</summary>
        private Vector3 _aimAt;
        private bool _aimOn;
        /// <summary>
        /// The most his chest turns and tips to bring his barrel onto his man once his rifle is up, degrees
        /// (standing, kneeling, prone: flat, his elbows are on the ground and his chest hardly tips); and
        /// the most the rifle itself is then turned in his hands for what is left.
        /// </summary>
        public static readonly Vector3 AimAcross = new Vector3(30f, 30f, 22f), AimLift = new Vector3(24f, 22f, 6f);
        public const float AimInHands = 9f;

        /// <summary>
        /// Where his barrel should point: the man he is firing at, or the one he is about to. His upper body
        /// turns to it (<see cref="Twist"/>) and the clip holds his rifle roughly along it, but only roughly:
        /// measured, the barrel was 19 degrees off the round's way at the middle and 44 at nine shots in ten,
        /// and never tipped up or down at all (a man firing up at the tower fired level). The rest is made up
        /// here, after the pose: his chest brings the rifle onto the point and his hands the last of it.
        /// </summary>
        public void Aim(Vector3 worldPoint, bool on = true)
        {
            _aimAt = worldPoint;
            _aimOn = on;
        }
        /// <summary>A blow hand to hand: 1 as it starts, 0 when it is over.</summary>
        private float _lunge;
        private bool _lean;
        /// <summary>Where he is looking (a point in the world) and how much of the turn he makes, 0 to 1.</summary>
        private Vector3 _lookAt;
        private float _lookWant, _look;
        /// <summary>The most his neck and head turn off the pose's own line, degrees.</summary>
        public const float MaxLook = 85f;
        /// <summary>
        /// Lying flat, the most his neck and his head tip back to bring his face up off the ground, degrees.
        /// The prone clips have his face in the dirt, and the 65 degrees that <see cref="MaxLook"/> came to in
        /// practice left it a quarter of the way down still: the owner, 2026-10-12, of a row of men lying with
        /// the crowns of their helmets to the enemy, "how could they see like that?"
        /// </summary>
        public const float ProneNeck = 34f, ProneHead = 66f;
        /// <summary>The way his face points, in the world (the motion audit holds a prone man's to the level).</summary>
        public Vector3 Facing => Head != null ? Head.rotation * HeadForward : transform.forward;

        /// <summary>
        /// What he is looking at: his target, or his front. Mixamo's prone and
        /// kneeling idles look at the ground (the owner, 2026-10-02: "heads look
        /// down when prone, make sure they are looking at what they're shooting
        /// at, unless hiding for cover"). Weight 0 leaves the clip's own head.
        /// </summary>
        public void Look(Vector3 worldPoint, float weight)
        {
            _lookAt = worldPoint;
            _lookWant = Mathf.Clamp01(weight);
        }
        /// <summary>Seconds a blow takes, and how far it carries him toward his man.</summary>
        public const float LungeSeconds = 0.42f, LungeReach = 0.55f;
        private int _aimLayer = -1;
        private bool _baked;
        private Mesh _fallen;
        /// <summary>His spine, hips up: the upper body turns on these, a share each.</summary>
        private Transform[] _spine = new Transform[0];
        private float _twist, _twistWant;

        /// <summary>
        /// Turn his upper body this far from where his feet point (degrees, as the world's yaw), toward
        /// the man he is aiming at. His feet stay planted: turned whole about his root, a man changing
        /// targets, raising his rifle or lowering it slid on the spot (the motion audit, playtest 8:
        /// 6,000 degrees in a minute and a quarter of fighting, the planted foot sliding 41 m).
        /// </summary>
        public void Twist(float degrees) => _twistWant = degrees;

        /// <summary>The most his upper body may be turned toward his man now, besides the turn into his aim.</summary>
        public float TwistRoom(int posture)
        {
            int k = Mathf.Clamp(posture, 0, 2);
            return Mathf.Max(8f, MaxTwist[k] - Mathf.Abs(AimTurn[k]));
        }

        /// <summary>Fallen, still, and a plain mesh: nothing more is to be done for him.</summary>
        public bool Baked => _baked;
        /// <summary>The kick of the round he has just fired, 1 as it goes and dying away in a twentieth of a second (for the tests).</summary>
        public float Kick => _recoil;

        /// <summary>
        /// On his feet: standing, walking or running, or in the last of getting up. Not while he is
        /// still coming up off a knee or off the ground: until he is, he has no legs to go anywhere on.
        /// </summary>
        public bool OnFeet
        {
            get
            {
                if (_baked || _deadFor >= 0) return false;
                if (Animator.GetCurrentAnimatorStateInfo(0).shortNameHash == StandState) return true;
                return Animator.IsInTransition(0) && Animator.GetNextAnimatorStateInfo(0).shortNameHash == StandState;
            }
        }

        private void Awake()
        {
            // Stepped by hand (Step), never by Unity's clock.
            Animator.enabled = false;
            _aimLayer = Animator.GetLayerIndex("Aim");
            _reactLayer = Animator.GetLayerIndex("React");
            _radioLayer = Animator.GetLayerIndex("Radio");
            var spine = new System.Collections.Generic.List<Transform>(3);
            foreach (var b in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest })
            {
                var t = Animator.isHuman ? Animator.GetBoneTransform(b) : null;
                if (t != null) spine.Add(t);
            }
            if (spine.Count == 0 && Chest != null) spine.Add(Chest);
            _spine = spine.ToArray();
        }

        /// <summary>
        /// Pose the man for this frame. dt is match time since the last call (0
        /// when paused); speed is his smoothed ground speed, negative when he is
        /// stepping backwards; aiming whether he holds his rifle to the shoulder.
        /// </summary>
        public void Step(float dt, float speed, int posture, bool aiming, bool dead, int deathIndex, Fall how = Fall.Shot, float push = 0f)
        {
            if (_baked) return;
            if (dead && _deadFor < 0)
            {
                _deadFor = 0;
                Animator.SetInteger(DeathId, DeathCode(deathIndex, posture, how, push));
                Animator.SetBool(DeadId, true);
                _deathPace = how == Fall.Blast ? BlastPace : FallPace + FallPaceSpread * (deathIndex % 8) / 7f;
            }
            if (_deadFor >= 0)
            {
                _deadFor += dt;
                // Timed by his death clip, not a clock: Mixamo's falls run two to four seconds.
                var fall = Animator.GetCurrentAnimatorStateInfo(0);
                float through = Animator.IsInTransition(0) ? 0f : fall.normalizedTime;
                if ((through > 0.6f || _deadFor > 3f) && Rifle != null && Rifle.transform.parent != transform) DropRifle(deathIndex);
                // Fallen and still (and what a burst took off him landed): freeze him as a plain mesh.
                if (((through >= 1f && _deadFor > 0.6f) || _deadFor > 6f) && !AnyFlying) { Bake(); return; }
            }
            // One gait at a time, played at the rate that puts his feet down where he is going: the walk
            // or the run, the crouched walk, the crawl; never a blend of two, whose feet agree with
            // neither. (Half way between the kneeling idle and the crouched walk, which is where the
            // sim's 1.1 m/s put him, he was a kneeling man gliding: the motion audit, playtest 6.)
            // Backwards he has his own clips (standing and crouched); a crawl has none.
            bool back = speed < 0 && posture != 2;
            speed = Mathf.Abs(speed);
            float key = 0f, scale = 1f, legs = 0f;
            if (speed >= 0.05f)
            {
                bool run = posture == 0 && speed > RunFrom;
                key = posture == 2 ? CrawlSpeed : posture == 1 ? CrouchSpeed : run ? RunSpeed : WalkSpeed;
                // Walking forward: his own walk for the march or for the double, played at its own stride.
                float own = key;
                if (posture == 0 && !run && !back)
                {
                    if (MarchSpeeds.Length > 0 && speed < MarchBelow) { key = MarchKey; own = MarchSpeeds[Mathf.Clamp(Gait, 0, MarchSpeeds.Length - 1)]; }
                    else if (DoubleSpeeds.Length > 0) own = DoubleSpeeds[Mathf.Clamp(Pace, 0, DoubleSpeeds.Length - 1)];
                }
                // A crawl may run fast: the sim crawls at 0.45 m/s, Mixamo's man at 0.2.
                scale = Mathf.Clamp(speed / Mathf.Max(0.05f, own), 0.5f, posture == 2 ? 2.4f : 1.8f);
                legs = own * scale;
            }
            Legs = speed >= 0.05f ? legs : 0f;
            // An act is over when the Animator is back at rest; and a man who has to go, goes: only a fall and a dive
            // are played out on the move.
            if (_act != null)
            {
                _actFor += dt;
                if (speed > 0.3f && _act != Dive && _act != BlastFall) Calm();
                else if (_actFor > 0.3f && _reactLayer >= 0 && !Animator.IsInTransition(_reactLayer) && Animator.GetCurrentAnimatorStateInfo(_reactLayer).IsName("Calm")) _act = null;
            }
            // Eased over a tenth of a second, so he goes from standing to walking and from a walk to a run, not snaps.
            float now = Animator.GetFloat(SpeedId), want = back ? -key : key;
            Animator.SetFloat(SpeedId, dt <= 0 ? want : Mathf.MoveTowards(now, want, Mathf.Max(Mathf.Abs(want - now), RunSpeed) * dt / 0.1f));
            Animator.SetFloat(ScaleId, scale);
            Animator.SetFloat(GaitId, Mathf.Clamp(Gait, 0, Mathf.Max(0, MarchSpeeds.Length - 1)));
            Animator.SetFloat(PaceId, Mathf.Clamp(Pace, 0, Mathf.Max(0, DoubleSpeeds.Length - 1)));
            Animator.SetInteger(PostureId, posture);
            _posture = posture;
            _speed = speed;
            _aim = Mathf.MoveTowards(_aim, aiming && !dead ? 1f : 0f, dt / 0.2f);
            if (_aimLayer >= 0) Animator.SetLayerWeight(_aimLayer, _aim);
            _radioW = dt <= 0 ? (_talking && !dead ? 1f : 0f) : Mathf.MoveTowards(_radioW, _talking && !dead ? 1f : 0f, dt / 0.35f);
            if (_radioLayer >= 0) Animator.SetLayerWeight(_radioLayer, _radioW);
            // (The turn into his aim is his upper body's now, with the turn to his man: PostPose.)
            Animator.transform.localRotation = Quaternion.identity;
            _twist = dt <= 0 ? _twistWant : Mathf.MoveTowardsAngle(_twist, dead ? 0f : _twistWant, TwistRate * dt);
            // The blow: his whole body goes in behind the rifle and comes back.
            _lunge = Mathf.MoveTowards(_lunge, 0f, dt / LungeSeconds);
            // Not through a reload, a throw, a blow or a climb: those clips have his eyes on what his hands are doing.
            bool busy = _reactLayer >= 0 && !Animator.GetCurrentAnimatorStateInfo(_reactLayer).IsName("Calm");
            _look = Mathf.MoveTowards(_look, dead || busy ? 0f : _lookWant, dt / 0.3f);
            Animator.transform.localPosition = dead ? Vector3.zero : Vector3.forward * (LungeReach * Mathf.Sin(Mathf.PI * (1f - _lunge)) * (_lunge > 0 ? 1f : 0f));
            // Thrown by a burst: carried away from it, up and over, in the first half second of his fall.
            if (_deadFor >= 0 && _blown != default)
            {
                float t = Mathf.Clamp01(_deadFor / ThrownFor);
                Animator.transform.position = transform.position + _blown * (1f - (1f - t) * (1f - t)) + Vector3.up * (_hop * 4f * t * (1f - t));
            }
            // (A climb is played at the pace the simulation climbs at; a death at its own.)
            Animator.Update(_deadFor >= 0 ? dt * _deathPace : _climbPace != 1f && !dead && Climbing ? dt * _climbPace : dt);
            for (int k = 0; k < _off.Count; k++)
            {
                // The clip does not know his arm is gone: drawn down to nothing again after every pose.
                var o = _off[k];
                o.Bone.localScale = Vector3.one * 0.001f;
                if (o.Flying)
                {
                    o.Pace += Vector3.down * (9.8f * dt);
                    o.Piece.position += o.Pace * dt;
                    o.Piece.Rotate(o.Spin, 620f * dt, Space.World);
                    float ground = transform.position.y + o.Lift;
                    if (o.Piece.TransformPoint(o.Mid).y <= ground && o.Pace.y < 0)
                    {
                        // Down: it lies along the ground, not on end, its middle its own thickness off it.
                        var lies = o.Piece.TransformDirection(o.Long);
                        var level = new Vector3(lies.x, 0, lies.z);
                        if (level.sqrMagnitude > 1e-4f) o.Piece.rotation = Quaternion.FromToRotation(lies, level) * o.Piece.rotation;
                        o.Piece.position += Vector3.up * (ground - o.Piece.TransformPoint(o.Mid).y);
                        o.Flying = false;
                    }
                    _off[k] = o;
                }
            }

            if (Hips != null) Centre = Hips.position;
            // A kick for every round of his burst, at the rate his weapon fires; a long burst climbs.
            if (_kicks > 0)
            {
                _kickIn -= dt;
                while (_kicks > 0 && _kickIn <= 0f)
                {
                    _recoil = 1f;
                    _climb = Mathf.Min(1f, _climb + 0.16f);
                    _kicks--;
                    _kickIn += Mathf.Max(0.03f, _kickGap);
                }
            }
            if (dead) _kicks = 0;
            _recoil *= Mathf.Exp(-dt / 0.05f);
            _climb *= Mathf.Exp(-dt / 0.3f);
            if (_deadFor < 0) PostPose();
            PlaceHandset();
            // (After his upper body has turned: the grenade is where his hand is drawn.)
            if (_inHand != null && _inHand.activeSelf)
            {
                _holding -= dt;
                if (_holding <= 0f || dead) _inHand.SetActive(false);
                else Hold();
            }
        }

        /// <summary>
        /// Which death: lying down, the prone death; thrown by a blast, the blast
        /// death; on one knee, the crouched one; running, the run that ends in a
        /// fall; else one of the standing deaths, by the man's own number, among
        /// those that throw him the way the round did: <paramref name="push"/> is
        /// how far along his facing it was travelling (1 from behind him, -1 into
        /// his front, 0 unknown).
        /// </summary>
        public int DeathCode(int seed, int posture, Fall how, float push = 0f)
        {
            // (Each of these left for right too, for every other man.)
            int other = MirroredDeaths && seed % 2 == 1 ? MirroredCode : 0;
            if (posture == 2 && ProneDeath) return ProneCode + other;
            // (A burst behind him throws him forward; one in front, or one nobody placed, back. Of two deaths from a
            // knee, each man has his own. At a run, hit from behind he goes down on his face; from in front or the
            // side he stumbles on and falls.)
            if (how == Fall.Blast && BlastDeath) return (BlastForwardDeath && push > 0.2f ? BlastForwardCode : BlastCode) + other;
            if (posture == 1 && CrouchDeath) return (CrouchDeath2 && seed / 2 % 2 == 1 ? Crouch2Code : CrouchCode) + other;
            if (how == Fall.Running && RunDeath) return (RunFlatDeath && (push > 0.35f || (push > -0.35f && seed / 2 % 2 == 1)) ? RunFlatCode : RunCode) + other;
            int n = Mathf.Max(1, Deaths);
            if (Mathf.Abs(push) > 0.35f && Falls != null && Falls.Length >= n)
            {
                // The seed-th death that goes his way (at least 15 cm of it), if any does.
                int fit = 0;
                for (int k = 0; k < n; k++) if (Falls[k] * push > 0.15f) fit++;
                if (fit > 0)
                {
                    int want = seed % fit;
                    for (int k = 0; k < n; k++)
                        if (Falls[k] * push > 0.15f && want-- == 0) return k;
                }
            }
            return seed % n;
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

        /// <summary>
        /// A grenade: the toss (the React layer), with the grenade in his hand until it leaves it
        /// (<see cref="Ordnance.ThrowRelease"/>); CombatView has it from there.
        /// </summary>
        public void Throw(Mesh grenade = null)
        {
            if (_deadFor >= 0 || _baked) return;
            Animator.SetTrigger(ThrowId);
            if (grenade == null || HandR == null || ArmsMaterial == null) return;
            if (_inHand == null)
            {
                var go = new GameObject("grenade in hand");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ArmsMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                _inHand = go;
            }
            _inHand.GetComponent<MeshFilter>().sharedMesh = grenade;
            _inHand.SetActive(true);
            _holding = Ordnance.ThrowRelease;
            _holdLeft = KneelThrowLeft && _posture == 1 && HandL != null;
            Hold();
        }

        private GameObject _inHand;
        private float _holding;
        private bool _holdLeft;

        /// <summary>The grenade in his palm: past the wrist, along the forearm, as the hand turns.</summary>
        private void Hold()
        {
            if (_inHand == null || !_inHand.activeSelf) return;
            Transform hand = _holdLeft ? HandL : HandR, arm = _holdLeft ? LowerArmL : LowerArmR;
            var along = arm != null ? (hand.position - arm.position).normalized : transform.forward;
            _inHand.transform.SetPositionAndRotation(hand.position + along * 0.07f, hand.rotation);
        }

        /// <summary>
        /// A burst has killed him: it carries him <paramref name="away"/> (metres, in the world, along the
        /// ground) and <paramref name="hop"/> metres off it at the top. Before his first frame dead.
        /// </summary>
        public void Blown(Vector3 away, float hop)
        {
            if (_baked) return;
            _blown = new Vector3(away.x, 0, away.z);
            _hop = hop;
        }

        /// <summary>Where the first thing to come off him lies, once it has landed.</summary>
        public bool TryLimb(out Vector3 at) => TryLimb(0, out at);

        /// <summary>Where the <paramref name="k"/>th thing to come off him lies, once it has landed.</summary>
        public bool TryLimb(int k, out Vector3 at)
        {
            at = k >= 0 && k < _off.Count ? _off[k].Piece.TransformPoint(_off[k].Mid) : default;
            return k >= 0 && k < _off.Count && !_off[k].Flying;
        }

        /// <summary>Where it came off him: the stump, as his body is posed now.</summary>
        public bool TryStump(int k, out Vector3 at)
        {
            at = k >= 0 && k < _off.Count ? _off[k].Bone.position : default;
            return k >= 0 && k < _off.Count;
        }

        /// <summary>How many parts of him have come off.</summary>
        public int Severed => _off.Count;

        /// <summary>
        /// A part of him comes off (the owner, playtest 9: "body dismemberment"; 2026-10-10: "body disfigurement
        /// and gore (with high cal + explosions)"): one of <see cref="Limbs"/> by <paramref name="which"/>, flung at
        /// <paramref name="pace"/> (m/s, in the world). On his body the bone is drawn down to nothing; the part is
        /// a plain mesh from where it was. False if it is off already, or what it hangs from or what hangs from
        /// it is, or he has lost <see cref="MostOff"/> parts.
        /// </summary>
        public bool Sever(int which, Vector3 pace)
        {
            if (_baked || _off.Count >= MostOff || Limbs == null || Limbs.Length == 0) return false;
            var limb = Limbs[Mathf.Abs(which) % Limbs.Length];
            if (limb.Bone == null || limb.Mesh == null) return false;
            for (int k = 0; k < _off.Count; k++)
                if (_off[k].Bone == limb.Bone || _off[k].Bone.IsChildOf(limb.Bone) || limb.Bone.IsChildOf(_off[k].Bone)) return false;
            // The hand his rifle is in: it falls with him, not with the hand.
            if (Rifle != null && Rifle.transform.IsChildOf(limb.Bone)) DropRifle(which);
            var go = new GameObject("limb");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(limb.Bone.position, limb.Bone.rotation);
            var s = go.transform.lossyScale; var want = limb.Bone.lossyScale;
            go.transform.localScale = new Vector3(want.x / s.x, want.y / s.y, want.z / s.z);
            go.AddComponent<MeshFilter>().sharedMesh = limb.Mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Body.sharedMaterial;
            mr.shadowCastingMode = ShadowCastingMode.On;
            // (How it will lie: along its longest side, half its thinnest off the ground.)
            var box = limb.Mesh.bounds.size;
            var along = box.x >= box.y && box.x >= box.z ? Vector3.right : box.y >= box.z ? Vector3.up : Vector3.forward;
            _off.Add(new Off
            {
                Bone = limb.Bone, Piece = go.transform, Pace = pace, Flying = true,
                Mid = limb.Mesh.bounds.center, Long = along,
                Lift = Mathf.Max(0.04f, 0.5f * Mathf.Min(box.x * want.x, box.y * want.y, box.z * want.z)),
                Spin = Vector3.Cross(Vector3.up, pace).sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, pace).normalized : transform.right,
            });
            // Where it parted, raw: on him, and on the end of the piece (which is his skin and no more: open there).
            float raw = Mathf.Clamp(1.8f * _off[_off.Count - 1].Lift, 0.08f, 0.14f);
            Patch(limb.Bone.parent, limb.Bone.position, Vector3.up, raw, 0.8f);
            Patch(go.transform, limb.Bone.position, Vector3.up, raw, 0.8f);
            limb.Bone.localScale = Vector3.one * 0.001f;
            return true;
        }

        /// <summary>Whether this part of him has come off: the bone, or one it hangs from.</summary>
        private bool Gone(Transform bone)
        {
            for (int k = 0; k < _off.Count; k++)
                if (bone == _off[k].Bone || bone.IsChildOf(_off[k].Bone)) return true;
            return false;
        }

        /// <summary>The same, by the part's name (<see cref="LimbNames"/>: "head", "lowerarm_l", "thigh_r" and the rest).</summary>
        public bool Sever(string part, Vector3 pace)
        {
            int k = System.Array.IndexOf(LimbNames, part);
            return k >= 0 && Sever(k, pace);
        }

        /// <summary>The most wounds drawn on one man.</summary>
        public const int MostWounds = 3;

        // His skin as he is posed at this moment, to find where on it a round struck; whose bone each vertex of a
        // body follows most (Unity lists a vertex's bones heaviest first); and what a wound is drawn with.
        private static Mesh _skin, _ball;
        private static readonly System.Collections.Generic.List<Vector3> _skinAt = new System.Collections.Generic.List<Vector3>();
        private static readonly System.Collections.Generic.Dictionary<Mesh, int[]> _owners = new System.Collections.Generic.Dictionary<Mesh, int[]>();
        private static Material _blood;

        /// <summary>How many wounds are drawn on him.</summary>
        public int Wounds => _wounds;

        /// <summary>
        /// Where a round went through him (the owner, 2026-10-10: "bullets hitting soldiers and blood splatters"):
        /// a patch of blood on him where it went in, and a bigger one where it came out. <paramref name="along"/>
        /// is the way the round was going, in the world; <paramref name="size"/> metres across. The round's line
        /// is taken through his trunk (his chest most often) and the patches are put where that line meets his
        /// skin as he is posed now, each on the bone that part of him follows: an arm held across his chest takes
        /// it, and they stay on him as he falls. False if the line missed him or he has
        /// <see cref="MostWounds"/> already.
        /// </summary>
        public bool Wound(Vector3 along, float size, int seed)
        {
            if (_wounds >= MostWounds || _baked || Body == null || Body.sharedMesh == null || Chest == null) return false;
            along.y = 0;
            if (along.sqrMagnitude < 1e-4f) along = transform.forward;
            along.Normalize();
            seed = Mathf.Abs(seed);
            int pick = seed % 10;
            Vector3 aim = pick < 6 || Hips == null ? Chest.position
                        : pick < 8 ? Vector3.Lerp(Hips.position, Chest.position, 0.45f)
                        : pick == 8 && Head != null ? Head.position + (Head.position - Chest.position).normalized * 0.06f
                        : Chest.position;
            aim += Vector3.Cross(Vector3.up, along) * (0.025f * (seed / 7 % 5 - 2)) + Vector3.up * (0.03f * (seed / 3 % 5 - 2));

            if (_skin == null) _skin = new Mesh { name = "skin" };
            Body.BakeMesh(_skin, true);
            _skin.GetVertices(_skinAt);
            var bones = Body.bones;
            if (!_owners.TryGetValue(Body.sharedMesh, out var owner))
            {
                // Not his arms (-1): they are across his chest as often as not, and a wound's patch on a forearm
                // is a plate strapped to it.
                Transform armL = Part("upperarm_l") ?? Part("lowerarm_l"), armR = Part("upperarm_r") ?? Part("lowerarm_r");
                var arm = new bool[bones.Length];
                for (int b = 0; b < bones.Length; b++)
                    arm[b] = bones[b] == null || (armL != null && bones[b].IsChildOf(armL)) || (armR != null && bones[b].IsChildOf(armR));
                var weights = Body.sharedMesh.boneWeights;
                owner = new int[weights.Length];
                for (int i = 0; i < weights.Length; i++)
                    owner[i] = weights[i].boneIndex0 < arm.Length && !arm[weights[i].boneIndex0] ? weights[i].boneIndex0 : -1;
                _owners[Body.sharedMesh] = owner;
            }
            // The round's line, in the space the skin was baked in. Of the skin within a few centimetres of it:
            // the first it meets, and the last.
            var skin = Body.transform;
            float scale = Mathf.Max(1e-4f, skin.lossyScale.x);
            Vector3 from = skin.InverseTransformPoint(aim), way = skin.InverseTransformDirection(along);
            float near = 0.03f / scale; near *= near;
            int first = -1, last = -1;
            float inAt = float.MaxValue, outAt = float.MinValue;
            int n = Mathf.Min(_skinAt.Count, owner.Length);
            for (int i = 0; i < n; i++)
            {
                if (owner[i] < 0) continue;
                var rel = _skinAt[i] - from;
                float t = Vector3.Dot(rel, way);
                if (rel.sqrMagnitude - t * t > near) continue;
                if (t < inAt) { inAt = t; first = i; }
                if (t > outAt) { outAt = t; last = i; }
            }
            if (first < 0) return false;
            // In: one patch. Out: a bigger one, torn, with more of it to either side.
            Vector3 side = Vector3.Cross(Vector3.up, along), hole = aim + along * (inAt * scale), torn = aim + along * (outAt * scale);
            Patch(bones[owner[first]], hole + along * 0.006f, -along, size);
            Patch(bones[owner[last]], torn - along * 0.006f, along, size * 1.5f);
            float a = 0.1f * (seed % 11), reach = size * 0.75f;
            Patch(bones[owner[last]], torn - along * 0.012f + (side * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)) * reach, along, size * 0.9f);
            Patch(bones[owner[last]], torn - along * 0.012f - (side * Mathf.Cos(a + 0.9f) + Vector3.up * Mathf.Sin(a + 0.9f)) * reach, along, size * 0.7f);
            _wounds++;
            return true;
        }

        /// <summary>One of <see cref="Limbs"/> by its name, or null.</summary>
        private Transform Part(string name)
        {
            int k = System.Array.IndexOf(LimbNames, name);
            return k >= 0 && k < Limbs.Length ? Limbs[k].Bone : null;
        }

        /// <summary>A patch of blood on him: a flattened ball, dark and wet, lying on the part of him that <paramref name="bone"/> moves.</summary>
        private void Patch(Transform bone, Vector3 at, Vector3 facing, float size, float thick = 0.28f)
        {
            // (A part of him that has come off is drawn down to nothing: nothing to put it on.)
            if (bone == null || Gone(bone)) return;
            if (_ball == null) _ball = Ball();
            if (_blood == null)
            {
                // His own body's material, so that the build has it, with no cloth on it.
                _blood = new Material(Body.sharedMaterial) { name = "blood" };
                _blood.SetTexture("_BaseMap", Texture2D.whiteTexture);
                _blood.SetColor("_BaseColor", new Color(0.26f, 0.012f, 0.010f, 1f));
            }
            var go = new GameObject("wound");
            go.transform.SetParent(bone, true);
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(facing, Vector3.up));
            var s = bone.lossyScale;
            go.transform.localScale = new Vector3(size / s.x, size * (thick < 0.6f ? 1.25f : 1f) / s.y, size * thick / s.z);
            go.AddComponent<MeshFilter>().sharedMesh = _ball;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _blood;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            _patches.Add(go.transform);
        }

        // Every patch of blood on him, until he lies still: then they are one mesh with him (a dozen small
        // things to draw for every dead man otherwise, for the rest of the match).
        private readonly System.Collections.Generic.List<Transform> _patches = new System.Collections.Generic.List<Transform>();
        private Mesh _marks;

        private void Gather()
        {
            if (_patches.Count == 0 || _ball == null || _blood == null) return;
            var parts = new CombineInstance[_patches.Count];
            var toMe = transform.worldToLocalMatrix;
            for (int k = 0; k < parts.Length; k++) parts[k] = new CombineInstance { mesh = _ball, transform = toMe * _patches[k].localToWorldMatrix };
            _marks = new Mesh { name = "wounds" };
            _marks.CombineMeshes(parts, true, true);
            var go = new GameObject("wounds");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _marks;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _blood;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            foreach (var p in _patches)
                if (p != null) { if (Application.isPlaying) Destroy(p.gameObject); else DestroyImmediate(p.gameObject); }
            _patches.Clear();
        }

        /// <summary>A ball a metre across, of few faces: a wound is one, flattened.</summary>
        private static Mesh Ball()
        {
            const int around = 10, down = 6;
            var at = new System.Collections.Generic.List<Vector3>();
            var normal = new System.Collections.Generic.List<Vector3>();
            var uv = new System.Collections.Generic.List<Vector2>();
            var tri = new System.Collections.Generic.List<int>();
            for (int j = 0; j <= down; j++)
                for (int i = 0; i <= around; i++)
                {
                    float lat = Mathf.PI * j / down, lon = 2 * Mathf.PI * i / around;
                    var p = new Vector3(Mathf.Sin(lat) * Mathf.Cos(lon), Mathf.Cos(lat), Mathf.Sin(lat) * Mathf.Sin(lon));
                    at.Add(p * 0.5f); normal.Add(p); uv.Add(new Vector2((float)i / around, (float)j / down));
                }
            for (int j = 0; j < down; j++)
                for (int i = 0; i < around; i++)
                {
                    int a = j * (around + 1) + i, b = a + around + 1;
                    tri.Add(a); tri.Add(a + 1); tri.Add(b);
                    tri.Add(a + 1); tri.Add(b + 1); tri.Add(b);
                }
            var m = new Mesh { name = "wound" };
            m.SetVertices(at); m.SetNormals(normal); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// Put him somewhere in his stride, his breath, his idle: a part of a second, his own. A squad raised
        /// together otherwise idles, breathes and steps as one man (the owner, playtest 9: "need more walk variations").
        /// </summary>
        public void Offbeat(float seconds)
        {
            if (!_baked && _deadFor < 0) Animator.Update(Mathf.Max(0f, seconds));
        }

        /// <summary>How he carries himself on the march, his own: degrees his chest leans forward, and rolls to one side.</summary>
        public Vector2 Carriage;

        /// <summary>Settle the Animator at once (a new man, or a capture's first frame); a dead man all the way down.</summary>
        public void Settle(float speed, int posture, bool aiming, bool dead, int deathIndex, Fall how = Fall.Shot, float push = 0f)
        {
            for (int k = 0; k < (dead ? 16 : 4) && !_baked; k++) Step(k == 0 ? 0f : 0.5f, speed, posture, aiming, dead, deathIndex, how, push);
        }

        /// <summary>Rounds close by: he flinches, ducks, is knocked (the React layer, where the clips exist).</summary>
        public void React()
        {
            if (_deadFor >= 0 || _baked) return;
            // Not the same flinch every time: on his feet he dodges every other time, on a knee he has two.
            _flinches++;
            if (_flinches % 2 == 0 && ((_posture == 0 && Act(Dodge)) || (_posture == 1 && Act(KneelFlinch)))) return;
            Animator.SetTrigger(HitId);
        }
        private int _flinches;

        /// <summary>
        /// Start one of the things a man does besides (<see cref="Acts"/>), over whatever his body is doing, from
        /// <paramref name="from"/> seconds into it. False if the controller has no such act, or he is dead,
        /// climbing or in the middle of a blow.
        /// </summary>
        public bool Act(string act, float blend = 0.12f, float from = 0f)
        {
            if (_deadFor >= 0 || _baked || _reactLayer < 0 || System.Array.IndexOf(Acts, act) < 0) return false;
            var now = Animator.GetCurrentAnimatorStateInfo(_reactLayer);
            if (now.shortNameHash == ClimbInState || now.shortNameHash == ClimbOutState || now.IsName("Melee 0") || now.IsName("Melee 1")) return false;
            Animator.CrossFadeInFixedTime(act, blend, _reactLayer, from);
            _act = act; _actFor = 0f;
            return true;
        }

        /// <summary>The act he is in the middle of (<see cref="Act"/>), or null.</summary>
        public string Doing => _act;
        private string _act;
        private float _actFor;

        /// <summary>Whatever he was doing besides, he stops: he has to shoot, or to go.</summary>
        private void Calm(float blend = 0.15f)
        {
            if (_act == null || _reactLayer < 0) return;
            Animator.CrossFadeInFixedTime("Calm", blend, _reactLayer);
            _act = null;
        }

        /// <summary>
        /// A shot: the kick of the rifle into his shoulder, once for each of its rounds, <paramref name="gap"/>
        /// seconds apart (it was one kick however many rounds the burst had: a machine gunner fired ten rounds
        /// with one nod); a reload he was in the middle of is over.
        /// </summary>
        public void Fire(int rounds = 1, float gap = 0f)
        {
            _kicks = Mathf.Max(1, rounds);
            _kickGap = gap;
            _kickIn = 0f;
            Calm();
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

        /// <summary>
        /// Into a trench or out of it (the sim's VaultIn and VaultOut), at <paramref name="pace"/> times
        /// the clip's own speed: the climb takes as long as the simulation has him on the parapet.
        /// False if there is no clip for it.
        /// </summary>
        public bool Climb(bool into, float pace = 1f)
        {
            if (_deadFor >= 0 || _baked || !Climbs || _reactLayer < 0) return false;
            _climbPace = Mathf.Clamp(pace, 0.25f, 2f);
            Animator.CrossFadeInFixedTime(into ? "Climb in" : "Climb out", 0.08f, _reactLayer);
            return true;
        }

        private float _climbPace = 1f;

        /// <summary>The controller has a ladder's climb (the builder says).</summary>
        public bool Ladders;
        public const string LadderState = "Ladder";

        private Transform _radioSet, _handset;
        private int _radioLayer = -1;
        private float _radioW;
        private bool _talking;
        /// <summary>He carries the set (ArmyView: one man a US rifle squad), and how far his handset is to his ear.</summary>
        public bool WearsRadio => _radioSet != null;
        public float OnTheRadio => _radioW;

        /// <summary>Put the set on his back: behind his chest, upright, its back to his, its antenna up over his left shoulder.</summary>
        public void WearRadio()
        {
            if (RadioSet == null || _radioSet != null || Chest == null) return;
            _radioSet = Kit("radio", RadioSet).transform;
            _radioSet.SetPositionAndRotation(Chest.position - transform.forward * (RadioBehind * transform.lossyScale.y) + transform.up * RadioUp, transform.rotation);
            _radioSet.SetParent(Chest, true);
            if (Handset != null && HandL != null)
            {
                _handset = Kit("handset", Handset).transform;
                _handset.gameObject.SetActive(false);
            }
        }

        private GameObject Kit(string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = RadioMaterial;
            mr.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        /// <summary>On the radio, or not: the handset to his ear, over whatever his legs and his rifle hand are doing.</summary>
        public void Talk(bool on) { _talking = on && Radios && _radioSet != null; }

        /// <summary>
        /// The handset in his left hand, its earpiece at his ear: from the hand toward the side of his head, drawn
        /// while his arm is up. (Its cord to the set is not drawn.)
        /// </summary>
        private void PlaceHandset()
        {
            if (_handset == null) return;
            bool up = _radioW > 0.6f && _deadFor < 0 && !_baked;
            if (_handset.gameObject.activeSelf != up) _handset.gameObject.SetActive(up);
            if (!up || Head == null) return;
            float k = transform.lossyScale.y;
            // His left ear: up from the head's pivot (at the base of the skull) and out to his left.
            var ear = Head.position + Head.up * (0.07f * k) - transform.right * (0.075f * k);
            var down = HandL.position - ear;
            if (down.sqrMagnitude < 1e-6f) return;
            down.Normalize();
            // Built with its earpiece toward the mesh's -x and its cups toward +z: the earpiece at his ear, the rest
            // down toward his hand, the cups turned in to his face.
            var face = Head.position - ear;
            var into = (face - Vector3.Dot(face, down) * down).normalized;
            var x = -down;
            _handset.SetPositionAndRotation(ear + down * (0.085f * k), Quaternion.LookRotation(into, Vector3.Cross(into, x)));
        }
        private static readonly int LadderId = Animator.StringToHash("Ladder"), LadderPaceId = Animator.StringToHash("LadderPace");
        private bool _onLadder;

        /// <summary>
        /// On a post's ladder (MatchOptions.Vantage), going up it or coming down: hand over hand, in place (ArmyView
        /// lifts him), until he is told he is off it. Coming down is the climb played backwards.
        /// </summary>
        public void Ladder(bool on, bool down = false)
        {
            if (_baked || _reactLayer < 0 || !Ladders) return;
            if (on) Animator.SetFloat(LadderPaceId, down ? -LadderPace : LadderPace);
            if (on == _onLadder) return;
            _onLadder = on;
            Animator.SetBool(LadderId, on);
            if (on && _deadFor < 0) Animator.CrossFadeInFixedTime(LadderState, 0.2f, _reactLayer);
        }

        /// <summary>The ladder clip's speed: it climbs slower than the simulation's man does (8.45 m in 8 s).</summary>
        public const float LadderPace = 1.3f;

        /// <summary>In the climb's clip, or going into it.</summary>
        private bool Climbing
        {
            get
            {
                if (_reactLayer < 0) return false;
                int now = Animator.GetCurrentAnimatorStateInfo(_reactLayer).shortNameHash;
                if (now == ClimbInState || now == ClimbOutState) return true;
                if (!Animator.IsInTransition(_reactLayer)) return false;
                int next = Animator.GetNextAnimatorStateInfo(_reactLayer).shortNameHash;
                return next == ClimbInState || next == ClimbOutState;
            }
        }

        /// <summary>A lull after shooting: a fresh magazine (where the posture has a reload clip).</summary>
        public void Reload()
        {
            if (_deadFor < 0 && !_baked) Animator.SetTrigger(ReloadId);
        }

        public Vector3 MuzzlePosition => Muzzle != null ? Muzzle.position : transform.position + Vector3.up * 1.4f;
        /// <summary>The way his barrel points, in the world: from the butt of what he carries to its muzzle (the motion audit holds it to the round's own way).</summary>
        public Vector3 Bore => Muzzle != null && Rifle != null && (Muzzle.position - Rifle.transform.position).sqrMagnitude > 1e-6f
            ? (Muzzle.position - Rifle.transform.position).normalized : transform.forward;

        private void PostPose()
        {
            // His upper body: toward his man, and into the aim the clip holds the rifle in, his spine turning
            // a share a bone; his hips and his feet stay where they are.
            int k = Mathf.Clamp(_posture, 0, 2);
            float turn = Mathf.Clamp(_twist + AimTurn[k] * _aim, -MaxTwist[k], MaxTwist[k]);
            if (Mathf.Abs(turn) > 0.05f && _spine.Length > 0)
            {
                var share = Quaternion.AngleAxis(turn / _spine.Length, Vector3.up);
                for (int i = 0; i < _spine.Length; i++) _spine[i].rotation = share * _spine[i].rotation;
            }
            // The kick: his shoulder driven back and up by each round, less of it lying behind the gun.
            float kick = (k == 2 ? 1.6f : 3.4f) * _recoil + (k == 2 ? 1.2f : 3.2f) * _climb;
            if (kick > 0.01f && Chest != null)
                Chest.rotation = Quaternion.AngleAxis(-kick, transform.right) * Chest.rotation;
            // On his feet and walking, rifle down: each man carries himself his own way, a little.
            if (_posture == 0 && _speed > 0.05f && _aim < 0.5f && Chest != null && Carriage != default)
            {
                float much = Mathf.Clamp01(_speed / 0.6f) * (1f - 2f * _aim);
                Chest.rotation = Quaternion.AngleAxis(Carriage.x * much, transform.right) * Quaternion.AngleAxis(Carriage.y * much, transform.forward) * Chest.rotation;
            }
            // His barrel onto his man, as far as his chest will bring it (the rifle goes with his arms).
            Vector3 wantBore = default;
            float aimed = _aimOn && Rifle != null && Muzzle != null && HandR != null && HandL != null && Chest != null ? _aim : 0f;
            if (aimed > 0.01f)
            {
                wantBore = _aimAt - Chest.position;
                Vector3 hands = HandL.position - HandR.position;
                if (wantBore.sqrMagnitude > 1f && hands.sqrMagnitude > 1e-4f)
                {
                    wantBore.Normalize();
                    // Where the barrel will be once the rifle is laid along his hands (Lay).
                    Vector3 bore = LaidAlong(hands) * Muzzle.localPosition.normalized;
                    float across = Mathf.DeltaAngle(Mathf.Atan2(bore.x, bore.z) * Mathf.Rad2Deg, Mathf.Atan2(wantBore.x, wantBore.z) * Mathf.Rad2Deg);
                    float lift = (Mathf.Asin(Mathf.Clamp(wantBore.y, -1f, 1f)) - Mathf.Asin(Mathf.Clamp(bore.y, -1f, 1f))) * Mathf.Rad2Deg;
                    across = Mathf.Clamp(across, -AimAcross[k], AimAcross[k]) * aimed;
                    lift = Mathf.Clamp(lift, -AimLift[k], AimLift[k]) * aimed;
                    var level = new Vector3(bore.x, 0, bore.z);
                    var axis = level.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, level.normalized) : transform.right;
                    Chest.rotation = Quaternion.AngleAxis(across, Vector3.up) * Quaternion.AngleAxis(-lift, axis) * Chest.rotation;
                }
                else aimed = 0f;
            }
            if (_look > 0.01f && Head != null)
            {
                var want = _lookAt - Head.position;
                if (want.sqrMagnitude > 0.04f)
                {
                    want.Normalize();
                    // Lying flat, his head comes up off the ground: his neck takes a third of it and his head the rest.
                    // On his feet or a knee, the neck two fifths of the turn and the head the rest, each about its own axis to the point.
                    if (k == 2)
                    {
                        if (Neck != null) Turn(Neck, want, ProneNeck, _look);
                        Turn(Head, want, ProneHead, _look);
                    }
                    else
                    {
                        if (Neck != null) Turn(Neck, want, MaxLook * 0.4f, 0.4f * _look);
                        Turn(Head, want, MaxLook * 0.6f, _look);
                    }
                }
            }
            if (_lunge > 0f && _lean && Chest != null)
                Chest.rotation = Quaternion.AngleAxis(24f * Mathf.Sin(Mathf.PI * (1f - _lunge)), transform.right) * Chest.rotation;
            if (Rifle == null || HandR == null || HandL == null || Rifle.transform.parent == transform) return;
            var r = Rifle.transform;
            if (_onLadder && Chest != null && Hips != null)
            {
                // On a ladder his hands are on the rungs: the rifle is slung across his back, muzzle up over
                // his left shoulder. (In his hand it was waved about with every reach.)
                Vector3 up = (Chest.position - Hips.position).normalized, fwd = transform.forward, side = Vector3.Cross(up, fwd).normalized;
                Lay(up * 0.9f - side * 0.45f, Chest.position - fwd * 0.2f - up * 0.28f + side * 0.12f);
                return;
            }
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
            // What his chest could not bring it: the rifle turned in his hands, about his firing hand, a few degrees.
            if (aimed > 0.01f)
            {
                Quaternion.FromToRotation(Bore, wantBore).ToAngleAxis(out float off, out var about);
                if (off > 0.2f && !float.IsNaN(about.x))
                {
                    var grip = r.TransformPoint(RifleGripR);
                    r.rotation = Quaternion.AngleAxis(Mathf.Min(off, AimInHands) * aimed, about) * r.rotation;
                    r.position += grip - r.TransformPoint(RifleGripR);
                }
            }
            // And the kick of each round: back along its own line into his shoulder, the muzzle up a little.
            if (_recoil > 0.01f || _climb > 0.01f)
            {
                Vector3 line = Bore, side = Vector3.Cross(Vector3.up, line);
                if (side.sqrMagnitude > 1e-4f)
                {
                    var grip = r.TransformPoint(RifleGripR);
                    r.rotation = Quaternion.AngleAxis(-(1.4f * _recoil + 1.6f * _climb), side.normalized) * r.rotation;
                    r.position += grip - r.TransformPoint(RifleGripR) - line * (0.03f * _recoil);
                }
            }
        }

        /// <summary>Turn a bone so that his face comes toward a direction: a share of the angle, and no further than a limit.</summary>
        private void Turn(Transform bone, Vector3 toward, float limit, float share)
        {
            var facing = Head.rotation * HeadForward;
            Quaternion.FromToRotation(facing, toward).ToAngleAxis(out float angle, out var axis);
            if (angle < 0.5f || float.IsNaN(axis.x)) return;
            bone.rotation = Quaternion.AngleAxis(Mathf.Min(angle, limit) * share, axis) * bone.rotation;
        }

        /// <summary>The rifle upright along a direction, its right-hand grip at a point.</summary>
        private void Lay(Vector3 along, Vector3 grip)
        {
            var r = Rifle.transform;
            r.rotation = LaidAlong(along);
            r.position += grip - r.TransformPoint(RifleGripR);
        }

        /// <summary>The way the rifle is turned when it lies upright along a direction, its two grips on that line.</summary>
        private Quaternion LaidAlong(Vector3 along)
        {
            along.Normalize();
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, along);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(transform.forward, along);
            Vector3 aL = (RifleGripL - RifleGripR).normalized;
            Vector3 uL = Vector3.ProjectOnPlane(RifleUp, aL);
            return Quaternion.LookRotation(along, up) * Quaternion.Inverse(Quaternion.LookRotation(aL, uL));
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
            Gather();
        }

        private void OnDestroy()
        {
            if (_fallen != null) Destroy(_fallen);
            if (_marks != null) Destroy(_marks);
        }
    }
}
