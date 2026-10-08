using System.Collections.Generic;
using LanesOfVietnam.Sim;
using UnityEngine;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// The men, drawn where the simulation says they are, between its ticks.
    ///
    /// Each man is a <see cref="SoldierFigure"/> (SoldierBuilder: a skinned
    /// body on a Humanoid avatar, three bodies a side, PLAN §12.3), stepped by
    /// match time: he turns to where he is going, to the man he is shooting
    /// at, and at rest to the enemy; his gait is played at the speed he is
    /// drawn moving at; his rifle kicks on every shot the sim fired.
    ///
    /// (Until the 3D men there were two stand-ins here: a baked sprite a man,
    /// and before that a capsule. Both went in the review of §12.23: nothing
    /// had drawn them since the figures came in.)
    /// </summary>
    public sealed class ArmyView : MonoBehaviour
    {
        /// <summary>Each side's bodies (three a side, SoldierBuilder): a man is one of his side's, by his id.</summary>
        public SoldierFigure[] UsFigures = new SoldierFigure[0];
        public SoldierFigure[] VcFigures = new SoldierFigure[0];
        private bool HasFigures => UsFigures != null && VcFigures != null && UsFigures.Length > 0 && VcFigures.Length > 0;

        public int Drawn { get; private set; }
        /// <summary>Milliseconds the last Draw took: stepping every Animator and its IK.</summary>
        public float LastDrawMs { get; private set; }
        /// <summary>Step every figure every frame, on screen or off: for the motion audit, which measures feet frame to frame.</summary>
        public bool StepAll;

        /// <summary>The figure drawn for a man, or null (not drawn yet).</summary>
        public SoldierFigure FigureOf(int id) => id >= 0 && id < _figures.Count ? _figures[id] : null;
        private readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();

        /// <summary>A new match: every man drawn so far belonged to the old one.</summary>
        public void ResetView()
        {
            foreach (var f in _figures) if (f != null) Destroy(f.gameObject);
            _figures.Clear();
            _matchTime = -1f;
            System.Array.Clear(_vz, 0, _vz.Length);
            System.Array.Clear(_shot, 0, _shot.Length);
            System.Array.Clear(_pinnedAt, 0, _pinnedAt.Length);
            System.Array.Clear(_rounds, 0, _rounds.Length);
            System.Array.Clear(_threw, 0, _threw.Length);
            System.Array.Clear(_reload, 0, _reload.Length);
            System.Array.Clear(_fall, 0, _fall.Length);
            System.Array.Clear(_blown, 0, _blown.Length);
            System.Array.Clear(_struck, 0, _struck.Length);
            System.Array.Clear(_climbUntil, 0, _climbUntil.Length);
            System.Array.Clear(_climbStart, 0, _climbStart.Length);
            System.Array.Clear(_turned, 0, _turned.Length);
            for (int k = 0; k < _target.Length; k++) _target[k] = -1;
            System.Array.Clear(_speed, 0, _speed.Length);
            System.Array.Clear(_vx, 0, _vx.Length);
            System.Array.Clear(_moving, 0, _moving.Length);
            System.Array.Clear(_pace, 0, _pace.Length);
            System.Array.Clear(_going, 0, _going.Length);
            System.Array.Clear(_pivot, 0, _pivot.Length);
            System.Array.Clear(_drawn, 0, _drawn.Length);
            _speedTick = -1;
            for (int k = 0; k < _firedAt.Length; k++) _firedAt[k] = -1000;
            _eventCursor = 0;
        }

        private float[] _speed = new float[0], _vx = new float[0], _vz = new float[0], _yaw = new float[0];
        /// <summary>Whom each man last fired at (-1 no one), and whether he fired since the last frame.</summary>
        private int[] _target = new int[0];
        private bool[] _shot = new bool[0];
        /// <summary>Whether the sim pinned each man since the last frame: rounds close enough to put him down.</summary>
        private bool[] _pinnedAt = new bool[0];
        /// <summary>Shots each man has fired since he last reloaded.</summary>
        private int[] _rounds = new int[0];
        /// <summary>Whether each man threw a grenade since the last frame, and how each man fell.</summary>
        private bool[] _threw = new bool[0];
        /// <summary>Whether the simulation started a reload for each man since the last frame (MatchOptions.Ammo).</summary>
        private bool[] _reload = new bool[0];
        private SoldierFigure.Fall[] _fall = new SoldierFigure.Fall[0];
        /// <summary>For a man shot or struck: how far along his facing the blow was travelling (1 from behind, -1 into his front).</summary>
        private float[] _push = new float[0];
        /// <summary>For a man a burst killed: where it went off (the simulation's x and z), until his body has been told.</summary>
        private Vector2[] _burst = new Vector2[0];
        private bool[] _blown = new bool[0];
        /// <summary>
        /// A burst within this many metres throws a man it kills (further, he falls where he stood); within
        /// <see cref="SeverWithin"/> it may take a limb off him, this often.
        /// </summary>
        public const float ThrowWithin = 7f, SeverWithin = 2.6f, SeverChance = 0.7f;
        /// <summary>A man reloads after this many shots, once he has not fired for LullTicks.</summary>
        public const int ReloadAfter = 6, LullTicks = 50;
        private readonly List<SoldierFigure> _figures = new List<SoldierFigure>();
        private float _matchTime = -1f;
        /// <summary>Match time each figure is owed, when it is stepped less than every frame.</summary>
        private float[] _pending = new float[0];
        private Camera _cam;
        /// <summary>A man drawn this many pixels tall or more is animated every frame; and so is every man in sight unless the frames come faster than this (seconds).</summary>
        public const float NearPixels = 110f, BriskFrame = 1f / 90f;
        /// <summary>How many figures were stepped in the last Draw.</summary>
        public int Stepped { get; private set; }
        private bool[] _moving = new bool[0];
        /// <summary>
        /// Where each man's front is (a yaw): toward the nearest enemy he can see,
        /// else up the lane. And whether he has turned to the way he is walking.
        /// </summary>
        private float[] _front = new float[0];
        private bool[] _turned = new bool[0];
        /// <summary>Whether each man struck a blow since the last frame, and the tick his climb into or out of a trench ends.</summary>
        private bool[] _struck = new bool[0];
        private int[] _climbUntil = new int[0];
        /// <summary>A climb: how long it is (ticks), which way (1 in, -1 out; 0 none to start), and where he was drawn when it began.</summary>
        private int[] _climbTicks = new int[0], _climbStart = new int[0];
        private Vector2[] _climbFrom = new Vector2[0], _drawn = new Vector2[0];
        /// <summary>
        /// How each 3D man is moving as drawn (sim x and z, metres a second), and whether his legs
        /// are going. The simulation moves a man in steps of a twentieth of a second and at one
        /// speed: at rest, then four metres a second, then at rest. Drawn so, he slid: carried at
        /// a sprint while he was still getting off his knee, his legs a tenth of a second behind
        /// his body at every start and still running after it had stopped. The drawn man follows
        /// the simulation's instead: he waits until he is on his feet, gathers speed, catches up,
        /// and slows into his place; and his gait is played at the speed he is drawn moving at,
        /// so his feet go at the speed the ground does.
        /// </summary>
        private Vector2[] _pace = new Vector2[0];
        private bool[] _going = new bool[0];
        /// <summary>Whether each man's feet are turning to where his upper body cannot reach.</summary>
        private bool[] _pivot = new bool[0];
        /// <summary>
        /// How fast his feet turn at rest (degrees a second), and the gait he turns with: a walk, a crouched
        /// walk or a crawl, played slowly (m/s), so he steps round rather than being turned on his heels.
        /// </summary>
        public const float PivotRate = 170f;
        private static readonly float[] PivotStep = { 0.75f, 0.6f, 0.2f };
        /// <summary>
        /// Seconds the drawn man is behind the simulation's (a sprint is half a metre behind; a
        /// march a hand's breadth); the fastest he is drawn, catching up (the run clip plays at up
        /// to 1.8 times its 4.5 m/s); and how far behind he may get waiting to stand before he is
        /// moved anyway.
        /// </summary>
        public const float Follow = 0.13f, MaxPace = 6.5f, LetGo = 2.5f;
        /// <summary>His legs go above this speed, and stop below the other (m/s).</summary>
        public const float GoAbove = 0.4f, StopBelow = 0.3f;
        /// <summary>Each man's height as drawn: it follows the ground, but over a parapet it takes a moment.</summary>
        private float[] _height = new float[0];
        /// <summary>How far an enemy counts as his front, and how fast his drawn height may change (m/s).</summary>
        public const float FrontRange = 45f, ClimbRate = 3.2f;
        /// <summary>Degrees his front has to move before he turns to it, and how fast he turns (degrees a second).</summary>
        public const float FrontSlack = 22f, TurnRate = 240f;
        private int _speedTick = -1;
        /// <summary>Metres a second: above this a standing man jogs rather than walks.</summary>
        public const float RunAbove = 1.7f, RunTurnRate = 600f;
        /// <summary>The tick each man last fired, from the sim's events; -1 never.</summary>
        private int[] _firedAt = new int[0];
        private int _eventCursor;
        /// <summary>How long a man who has fired stays in his aim, in ticks: 3 s.</summary>
        public const int AimTicks = 60;

        private static float Hash(int id, int salt)
        {
            float h = Mathf.Sin(id * 12.9898f + salt * 78.233f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        /// <summary>
        /// What the view knows of each man beyond the sim's state: who fired and
        /// at whom, read once from the sim's events; and his speed and heading,
        /// from the sim's own steps, once a tick, smoothed over ~0.3 s and
        /// started and stopped at different speeds.
        /// </summary>
        private void Track(MatchDriver d)
        {
            var st = d.State;
            if (_speed.Length < st.Men.Count)
            {
                int n = st.Men.Count * 2;
                System.Array.Resize(ref _speed, n);
                System.Array.Resize(ref _vx, n);
                System.Array.Resize(ref _vz, n);
                System.Array.Resize(ref _yaw, n);
                System.Array.Resize(ref _moving, n);
                System.Array.Resize(ref _shot, n);
                System.Array.Resize(ref _pinnedAt, n);
                System.Array.Resize(ref _rounds, n);
                System.Array.Resize(ref _threw, n);
                System.Array.Resize(ref _reload, n);
                System.Array.Resize(ref _fall, n);
                System.Array.Resize(ref _push, n);
                System.Array.Resize(ref _burst, n);
                System.Array.Resize(ref _blown, n);
                System.Array.Resize(ref _front, n);
                System.Array.Resize(ref _turned, n);
                System.Array.Resize(ref _struck, n);
                System.Array.Resize(ref _climbUntil, n);
                System.Array.Resize(ref _climbTicks, n);
                System.Array.Resize(ref _climbStart, n);
                System.Array.Resize(ref _climbFrom, n);
                System.Array.Resize(ref _drawn, n);
                System.Array.Resize(ref _pace, n);
                System.Array.Resize(ref _going, n);
                System.Array.Resize(ref _pivot, n);
                System.Array.Resize(ref _height, n);
                int old = _firedAt.Length;
                System.Array.Resize(ref _firedAt, n);
                System.Array.Resize(ref _target, n);
                for (int k = old; k < n; k++) { _firedAt[k] = -1000; _target[k] = -1; }
            }
            if (_eventCursor > st.Events.Count) _eventCursor = 0;
            for (; _eventCursor < st.Events.Count; _eventCursor++)
            {
                var e = st.Events[_eventCursor];
                if (e.Kind == EventKind.Pinned && e.Id < _pinnedAt.Length) { _pinnedAt[e.Id] = true; continue; }
                if (e.Kind == EventKind.GrenadeThrown && e.Id < _threw.Length) { _threw[e.Id] = true; continue; }
                if (e.Kind == EventKind.Reload && e.Id < _reload.Length) { _reload[e.Id] = true; continue; }
                if ((e.Kind == EventKind.VaultIn || e.Kind == EventKind.VaultOut) && e.Id < _climbUntil.Length)
                {
                    _climbTicks[e.Id] = Gunnery.VaultTicks(st, e.Kind == EventKind.VaultIn, st.Men[e.Id]);
                    _climbUntil[e.Id] = e.Tick + _climbTicks[e.Id];
                    _climbStart[e.Id] = e.Kind == EventKind.VaultIn ? 1 : -1;
                    _climbFrom[e.Id] = _drawn[e.Id];
                    continue;
                }
                if (e.Kind == EventKind.Melee && e.Id < _struck.Length)
                {
                    // A blow: he turns on his man as he would to shoot him.
                    _struck[e.Id] = true;
                    _firedAt[e.Id] = e.Tick;
                    _target[e.Id] = e.Target ?? -1;
                    continue;
                }
                if (e.Kind == EventKind.Kill && e.Id < _fall.Length)
                {
                    // The sim writes a kill straight after the shot, the blow or the
                    // round through another man that made it; a kill with none of
                    // those before it was a shell or a grenade.
                    var prev = _eventCursor > 0 ? st.Events[_eventCursor - 1] : default;
                    bool shot = (prev.Kind == EventKind.Fire || prev.Kind == EventKind.Melee || prev.Kind == EventKind.Through)
                                && prev.Target == e.Id && prev.Tick == e.Tick;
                    // Which way it threw him: from the man who fired (or, for a round that
                    // had already gone through someone, from that man) across his own facing.
                    _push[e.Id] = 0f;
                    if (shot && prev.Id >= 0 && prev.Id < st.Men.Count && e.Id < _yaw.Length)
                    {
                        var by = st.Men[prev.Id]; var him = st.Men[e.Id];
                        var way = new Vector2((float)(him.X - by.X), (float)-(him.Z - by.Z));
                        float yaw = _yaw[e.Id] * Mathf.Deg2Rad;
                        if (way.sqrMagnitude > 0.01f) _push[e.Id] = Vector2.Dot(way.normalized, new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)));
                    }
                    _fall[e.Id] = !shot ? SoldierFigure.Fall.Blast
                                : _moving[e.Id] && _speed[e.Id] > RunAbove ? SoldierFigure.Fall.Running : SoldierFigure.Fall.Shot;
                    // A burst: where it went off (the shell or the grenade whose event comes before its kills, this tick).
                    if (!shot)
                        for (int k = _eventCursor - 1; k >= 0 && st.Events[k].Tick == e.Tick; k--)
                        {
                            var b = st.Events[k];
                            if ((b.Kind != EventKind.GrenadeBlast && b.Kind != EventKind.Shell && b.Kind != EventKind.TrapSprung) || b.X == null) continue;
                            _burst[e.Id] = new Vector2((float)b.X.Value, (float)b.Z.Value);
                            _blown[e.Id] = true;
                            break;
                        }
                    continue;
                }
                if (e.Kind != EventKind.Fire || e.Id >= _firedAt.Length) continue;
                _firedAt[e.Id] = e.Tick;
                _target[e.Id] = e.Target ?? -1;
                if (e.Id < _rounds.Length) _rounds[e.Id]++;
                _shot[e.Id] = true;
            }
            if (st.Tick != _speedTick)
            {
                int ticks = _speedTick < 0 || st.Tick < _speedTick ? 1 : st.Tick - _speedTick;
                // His speed follows the sim's within a tenth of a second. (Over three tenths, a man who
                // set off slid for half a second before his legs caught up, and one who stopped walked
                // on the spot for most of a second: the motion audit's "standing at rest", skating.)
                float k = 1f - Mathf.Exp(-ticks * (float)Tune.Dt / 0.1f);
                for (int i = 0; i < st.Men.Count; i++)
                {
                    var (dx, dz) = d.LastStep(i);
                    float v = (float)(System.Math.Sqrt(dx * dx + dz * dz) / Tune.Dt);
                    _speed[i] += (v - _speed[i]) * k;
                    _vx[i] += ((float)(dx / Tune.Dt) - _vx[i]) * k;
                    _vz[i] += ((float)(dz / Tune.Dt) - _vz[i]) * k;
                    // Stopped in the sim is stopped: no walking on after the ground has.
                    if (v < 1e-3f) { _speed[i] = 0f; _moving[i] = false; }
                    else if (_moving[i]) { if (_speed[i] < 0.15f) _moving[i] = false; }
                    else if (_speed[i] > 0.35f) _moving[i] = true;
                }
                // Each man's front: the nearest enemy he can see, else up the lane.
                for (int i = 0; i < st.Men.Count; i++)
                {
                    var m = st.Men[i];
                    if (!m.Alive) continue;
                    double best = FrontRange * FrontRange;
                    float front = m.Side == Side.Us ? 90f : -90f;
                    // Senses: the squad his own is dealing with comes first, in sight or not: a squad
                    // that has gone to ground faces what put it there.
                    int threat = st.Senses && m.Squad < st.Squads.Count ? st.Squads[m.Squad].Threat : -1;
                    bool onThreat = false;
                    for (int j = 0; j < st.Men.Count; j++)
                    {
                        var o = st.Men[j];
                        if (!o.Alive || o.Side == m.Side) continue;
                        bool theirs = o.Squad == threat;
                        if (!theirs && (!o.Seen || onThreat)) continue;
                        double dx = o.X - m.X, dz = o.Z - m.Z, d2 = dx * dx + dz * dz;
                        if (d2 < 0.01 || d2 >= FrontRange * FrontRange) continue;
                        if (theirs == onThreat && d2 >= best) continue;
                        best = d2; onThreat = theirs;
                        front = Mathf.Atan2((float)dx, (float)-dz) * Mathf.Rad2Deg;
                    }
                    // He does not swing his whole body for a few degrees: the nearest enemy changing
                    // between two men side by side had him twitching from one to the other.
                    if (Mathf.Abs(Mathf.DeltaAngle(_front[i], front)) > FrontSlack || _front[i] == 0f) _front[i] = front;
                }
                _speedTick = st.Tick;
            }
        }

        /// <summary>The muzzle of a man's rifle, where there is a 3D man to have one.</summary>
        public bool TryMuzzle(int id, out Vector3 p)
        {
            var f = id >= 0 && id < _figures.Count ? _figures[id] : null;
            p = f != null ? f.MuzzlePosition : default;
            return f != null;
        }

        /// <summary>
        /// Where a man is on the screen (sim x and z): where his 3D figure is drawn, which follows
        /// the simulation's place for him by a moment; without a figure, the simulation's place.
        /// Everything drawn about a man (his shadow, his tag, his ring, the round that hits him)
        /// is drawn here, so it is on him.
        /// </summary>
        public (double x, double z) Where(MatchDriver d, int id)
        {
            if (id >= 0 && id < _figures.Count && _figures[id] != null && id < _drawn.Length) return (_drawn[id].x, _drawn[id].y);
            return d.Position(id);
        }

        /// <summary>Where a man's body is, where there is a 3D man (a fallen one lies where his fall took him).</summary>
        public bool TryBody(int id, out Vector3 p)
        {
            var f = id >= 0 && id < _figures.Count ? _figures[id] : null;
            p = f != null ? f.Centre : default;
            return f != null && p != default;
        }

        /// <summary>Where what a burst took off a man lies, once it has landed.</summary>
        public bool TryLimb(int id, out Vector3 p)
        {
            p = default;
            var f = id >= 0 && id < _figures.Count ? _figures[id] : null;
            return f != null && f.TryLimb(out p);
        }

        private void DrawFigures(MatchDriver d, Ground g)
        {
            var st = d.State;
            Track(d);
            // The men move on match time: still when paused, faster at 3x. A
            // jump (a fast-forward, a capture) settles them where the sim is.
            float now = (float)((st.Tick + d.Alpha) * Tune.Dt);
            bool jump = _matchTime >= 0 && now - _matchTime > 0.5f;
            float dt = _matchTime < 0 || now < _matchTime ? 0f : Mathf.Min(now - _matchTime, 0.25f);
            _matchTime = now;
            while (_figures.Count < st.Men.Count) _figures.Add(null);
            while (_pending.Length < _figures.Count) System.Array.Resize(ref _pending, _figures.Count * 2);
            _cam ??= Camera.main;
            float pxPerMetre = _cam != null ? Screen.height / (2f * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) : 1000f;
            int frame = Time.frameCount;
            bool brisk = Time.smoothDeltaTime < BriskFrame;
            Drawn = 0;
            Stepped = 0;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                // A dead man who has fallen and lies still is a mesh where he lies: there is nothing to do for him.
                if (!m.Alive && !jump && _figures[i] != null && _figures[i].Baked) { Drawn++; continue; }
                var (x, z) = d.Position(i);
                if (m.Alive)
                {
                    // A trench is one man wide: in it he is drawn on its line, and while he
                    // climbs he is carried over its lip (the simulation holds him at the lip
                    // for the climb, then walks him in).
                    if (m.Cover >= 0 && m.Cover < st.Cover.Count && Fieldcraft.Dug(st.Cover[m.Cover])) z = st.Cover[m.Cover].Z;
                    float left = _climbUntil[i] - (st.Tick + (float)d.Alpha);
                    bool carried = false;
                    // (Only from somewhere a climb could start: after a jump in time the place he was
                    // last drawn is where he stood many seconds ago, and he was carried seven metres.)
                    if (left > 0 && _climbTicks[i] > 0 && !jump
                        && (_climbFrom[i] - new Vector2((float)x, (float)z)).sqrMagnitude < 9f)
                    {
                        float w = Mathf.SmoothStep(0f, 1f, 1f - left / _climbTicks[i]);
                        x = Mathf.Lerp(_climbFrom[i].x, (float)x, w);
                        z = Mathf.Lerp(_climbFrom[i].y, (float)z, w);
                        carried = true;
                    }
                    var place = new Vector2((float)x, (float)z);
                    var body = _figures[i];
                    if (body == null || jump || carried || _drawn[i] == default) { _drawn[i] = place; _pace[i] = Vector2.zero; }
                    else if (dt > 0f)
                    {
                        // He follows his place. Told to be on his feet and not on them yet, he waits
                        // (unless it has got away from him): nobody is carried along while he gets up.
                        bool getting = m.Posture == Posture.Standing && !body.OnFeet && (place - _drawn[i]).sqrMagnitude < LetGo * LetGo;
                        _drawn[i] = Vector2.SmoothDamp(_drawn[i], place, ref _pace[i], Follow, getting ? 0f : MaxPace, dt);
                    }
                    x = _drawn[i].x; z = _drawn[i].y;
                }
                // He lies where he was drawn when he fell (unless this frame is a jump in time).
                else if (jump || _figures[i] == null || _drawn[i] == default) { _drawn[i] = new Vector2((float)x, (float)z); _pace[i] = Vector2.zero; }
                else { x = _drawn[i].x; z = _drawn[i].y; _pace[i] = Vector2.zero; }
                // His legs go when he is drawn moving, and until he has all but stopped. The simulation
                // may have set him off before he is: on his way, he turns to it and takes his rifle down.
                // (A man going down as he arrives finishes his last pace in the going down; only one the
                // simulation is moving on a knee or flat, a sapper coming up bent double, has a gait for it.)
                float pace = _pace[i].magnitude;
                _going[i] = m.Alive && pace > (_going[i] ? StopBelow : GoAbove) && (m.Posture == Posture.Standing || _moving[i]);
                bool going = _going[i], away = going || (m.Alive && _moving[i]);
                var way = going ? _pace[i] : new Vector2(_vx[i], _vz[i]);
                // (Gunnery: nobody fires on the move, and nobody walks with his rifle in his shoulder either.)
                bool aiming = m.Alive && st.Tick - _firedAt[i] <= AimTicks && (st.Gunnery ? !away : !away || pace < RunAbove);
                // Where he looks and points his rifle, in the world's yaw (0 = +z, 90 = +x; the sim's z runs the other way).
                float yaw = _yaw[i];
                int posture = m.Posture == Posture.Prone ? 2 : m.Posture == Posture.Crouched ? 1 : 0;
                if (m.Alive)
                {
                    int tg = _target[i];
                    if (aiming && tg >= 0 && tg < st.Men.Count)
                    {
                        var (tx, tz) = Where(d, tg);
                        yaw = Mathf.Atan2((float)(tx - x), (float)-(tz - z)) * Mathf.Rad2Deg;
                    }
                    else
                    {
                        // His front is the enemy. He turns to the way he is going when he
                        // is going somewhere: running from the fight, or travelling within
                        // a quarter turn of his front. A few paces sideways or back he
                        // takes as he stands, facing the enemy: the baseline's men spun on
                        // the spot for every one of them.
                        yaw = _front[i];
                        if (away)
                        {
                            float travel = Mathf.Atan2(way.x, -way.y) * Mathf.Rad2Deg;
                            float off = Mathf.Abs(Mathf.DeltaAngle(_front[i], travel));
                            bool fleeing = m.Squad < st.Squads.Count && st.Squads[m.Squad].Order == Order.Fallback;
                            // (Gunnery: a man who is moving is on his feet and going somewhere: he faces it.)
                            _turned[i] = st.Gunnery || fleeing || off < (_turned[i] ? 100f : 70f);
                            if (_turned[i]) yaw = travel;
                        }
                        else _turned[i] = false;
                    }
                }
                var f = _figures[i];
                bool fresh = f == null;
                if (fresh)
                {
                    var bodies = m.Side == Side.Us ? UsFigures : VcFigures;
                    f = _figures[i] = Instantiate(bodies[(int)(Hash(m.Id, 5) * bodies.Length) % bodies.Length], transform);
                    f.name = $"man {i}";
                    f.Carry(AudioView.Model(m));
                    // His own height (and with it his stride), his own carriage on the march.
                    f.transform.localScale = Vector3.one * (0.95f + 0.09f * Hash(m.Id, 2));
                    f.Carriage = new Vector2(-2f + 8f * Hash(m.Id, 11), -3f + 6f * Hash(m.Id, 12));
                    _yaw[i] = yaw;
                }
                // His feet. Going somewhere, they face it: turned at most TurnRate degrees a second of match
                // time (a man setting off turns to where he is going at once, not over the first three paces
                // of it). At rest they stay where they are while his upper body turns to his man (SoldierFigure
                // .Twist); only when his man is further round than that do his feet turn, and he steps round.
                float feet = yaw;
                // (Set off by the simulation and still getting up, he is at rest too: he turns to where he is
                // going with his first steps, not on his knees.)
                bool atRest = m.Alive && !going && !fresh && !jump;
                if (atRest)
                {
                    float off = Mathf.DeltaAngle(_yaw[i], yaw);
                    if (Mathf.Abs(off) > f.TwistRoom(posture)) _pivot[i] = true;
                    else if (Mathf.Abs(off) < 4f) _pivot[i] = false;
                    feet = _pivot[i] ? yaw : _yaw[i];
                }
                else _pivot[i] = false;
                float wasYaw = _yaw[i];
                _yaw[i] = jump ? feet : Mathf.MoveTowardsAngle(_yaw[i], feet, (atRest ? PivotRate : st.Gunnery && going ? RunTurnRate : TurnRate) * dt);
                bool stepping = atRest && Mathf.Abs(Mathf.DeltaAngle(wasYaw, _yaw[i])) > PivotRate * dt * 0.25f;
                f.Twist(atRest ? Mathf.Clamp(Mathf.DeltaAngle(_yaw[i], yaw), -f.TwistRoom(posture), f.TwistRoom(posture)) : 0f);
                // Over a parapet his height takes a moment to follow the ground: he climbs, he does not snap.
                float ground = (float)g.HeightAt(x, z);
                _height[i] = fresh || jump || !m.Alive ? (m.Alive || fresh || jump ? ground : _height[i])
                                                       : Mathf.MoveTowards(_height[i], ground, ClimbRate * dt);
                var at = Coords.World(x, z, _height[i]);
                // PLAN §12.3's mitigations 2 and 3: a man small on the screen is
                // stepped every other frame and skinned with two bones a vertex;
                // one off it, every fourth. Staggered by id, so the work is even.
                // (Every other frame only while the frames come fast: at sixty a second
                // that was a man animated thirty times a second.)
                int every = 1;
                if (!fresh && !jump && _cam != null)
                {
                    var vp = _cam.WorldToViewportPoint(at + Vector3.up);
                    float px = vp.z > 0.1f ? 1.8f * pxPerMetre / vp.z : 0f;
                    bool seen = vp.z > 0.1f && vp.x > -0.08f && vp.x < 1.08f && vp.y > -0.1f && vp.y < 1.2f;
                    every = StepAll ? 1 : !seen ? 4 : px < NearPixels && brisk ? 2 : 1;
                    f.Body.quality = px >= NearPixels || StepAll ? SkinQuality.Bone4 : SkinQuality.Bone2;
                }
                bool step = (frame + i) % every == 0;
                // He is moved when he is posed, and not between: moved every frame and posed every
                // other, his body went on without his legs and the foot he had on the ground slid
                // forward and was put back, twenty-five times a second.
                if (step) f.transform.SetPositionAndRotation(at, Quaternion.Euler(0, _yaw[i], 0));
                // Climbing into a trench or out of it: the clip, or without one, down on the parapet.
                bool climbing = m.Alive && st.Tick < _climbUntil[i];
                if (_climbStart[i] != 0)
                {
                    // (At the simulation's pace: the clips were timed to Fieldcraft's own climb, and Gunnery's is a quarter slower.)
                    bool into = _climbStart[i] > 0;
                    float climbPace = (into ? Tune.VaultInTicks : Tune.VaultOutTicks) / (float)Mathf.Max(1, Mathf.Abs(_climbTicks[i]));
                    if (!jump && !fresh && m.Alive && !f.Climb(into, climbPace)) _climbTicks[i] = -_climbTicks[i];
                    _climbStart[i] = 0;
                }
                if (climbing && _climbTicks[i] < 0 && posture == 0) posture = 1;
                // His gait, at the speed he is drawn moving at; and turning where he stands, a few steps round.
                float speed = going ? pace : stepping ? PivotStep[Mathf.Clamp(posture, 0, 2)] : 0f;
                // Stepping back or across while he faces the enemy: the backward clips, not a moonwalk.
                if (going)
                {
                    float travel = Mathf.Atan2(way.x, -way.y) * Mathf.Rad2Deg;
                    float along = Mathf.Cos(Mathf.DeltaAngle(_yaw[i], travel) * Mathf.Deg2Rad);
                    if (along < -0.3f) speed = -speed;
                    else if (along < 0.3f) speed *= 0.6f;
                }
                int death = (int)(Hash(m.Id, 1) * 16);
                if (m.Alive)
                {
                    // His eyes: on the man he is shooting at, else along his front. Not when he is flat and
                    // hiding from fire, nor at a run (he looks where he is going).
                    Vector3 eyes = at + Vector3.up * (posture == 2 ? 0.35f : posture == 1 ? 1.0f : 1.6f);
                    // (Along his front, and about: each man looks a little to one side and the other, in his own time.)
                    float about = 28f * Mathf.Sin(now * (0.5f + 0.5f * Hash(m.Id, 20)) + 6.28f * Hash(m.Id, 21));
                    Vector3 look = eyes + Quaternion.Euler(0, _front[i] + about, 0) * Vector3.forward * 20f;
                    int tg = _target[i];
                    if (aiming && tg >= 0 && tg < st.Men.Count)
                    {
                        var (tx, tz) = Where(d, tg);
                        look = Coords.World(tx, tz, (float)g.HeightAt(tx, tz) + 1.0f);
                    }
                    bool hiding = m.Pin >= Tune.PinStop;
                    bool running = going && pace > RunAbove;
                    f.Look(look, hiding || running ? 0f : posture == 0 ? 0.6f : 1f);
                }
                if (_shot[i]) { f.Fire(); _shot[i] = false; }
                // Pinned while still: he flinches (a moving man keeps moving; the clip would slide him).
                if (_pinnedAt[i]) { if (!away && !jump) f.React(); _pinnedAt[i] = false; }
                // His reload. With the ammunition rule it is the simulation's: his magazine is empty, or low in a
                // lull, and he does not fire until it is done. Without it, a lull after shooting, for the look of it.
                if (st.Ammo) { if (_reload[i]) { if (m.Alive && !jump) f.Reload(); _reload[i] = false; } }
                else if (m.Alive && !away && _rounds[i] >= ReloadAfter && st.Tick - _firedAt[i] >= LullTicks)
                {
                    f.Reload();
                    _rounds[i] = 0;
                }
                Drawn++;
                if (_threw[i]) { if (!jump) f.Throw(Ordnance.Get(m.Side == Side.Us ? Ordnance.Kind.Lemon : Ordnance.Kind.Stick)); _threw[i] = false; }
                if (_struck[i]) { if (!jump) f.Strike((int)(Hash(m.Id + st.Tick, 6) * 2)); _struck[i] = false; }
                // A burst killed him: it throws him, and close enough may take a limb off him.
                if (_blown[i] && !m.Alive)
                {
                    _blown[i] = false;
                    var from = Coords.World(_burst[i].x, _burst[i].y, at.y);
                    var flung = at - from; flung.y = 0;
                    float far = flung.magnitude;
                    if (!jump && far < ThrowWithin)
                    {
                        flung = far > 0.05f ? flung / far : Quaternion.Euler(0, 360f * Hash(m.Id, 13), 0) * Vector3.forward;
                        float near = 1f - far / ThrowWithin;
                        bool gore = GameRoot.Instance == null || GameRoot.Instance.Settings == null || GameRoot.Instance.Settings.Gore;
                        f.Blown(flung * (0.5f + 2.6f * near * (0.7f + 0.6f * Hash(m.Id, 14))), 0.2f + 0.8f * near);
                        if (gore && far < SeverWithin && Hash(m.Id, 15) < SeverChance)
                            f.Sever((int)(Hash(m.Id, 16) * 97), flung * (2.5f + 3f * Hash(m.Id, 17)) + Vector3.up * (3.5f + 2.5f * Hash(m.Id, 18))
                                                               + Vector3.Cross(Vector3.up, flung) * (2f * Hash(m.Id, 19) - 1f));
                    }
                }
                if (fresh || jump)
                {
                    f.Settle(speed, posture, aiming, !m.Alive, death, _fall[i], _push[i]);
                    // (Not all in step: each man a part of a second into his idle and his stride.)
                    if (fresh) f.Offbeat(1.7f * Hash(m.Id, 10));
                    _pending[i] = 0; Stepped++; continue;
                }
                _pending[i] += dt;
                if (!step) continue;
                f.Step(_pending[i], speed, posture, aiming, !m.Alive, death, _fall[i], _push[i]);
                _pending[i] = 0;
                Stepped++;
            }
        }

        public void Draw(MatchDriver d, Ground g)
        {
            // No bodies (SoldierBuilder has not been run): nothing to draw them with.
            if (!HasFigures) return;
            _clock.Restart();
            DrawFigures(d, g);
            LastDrawMs = (float)_clock.Elapsed.TotalMilliseconds;
        }
    }
}
