using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LanesOfVietnam.Sim;
using UnityEngine;
using UnityEngine.Networking;

namespace LanesOfVietnam.View
{
    /// <summary>Where sound goes: the browser's Web Audio engine (LovAudio.jslib), or a recorder.</summary>
    public interface ISoundOut
    {
        void Load(string name, string url);
        bool Loaded(string name);
        bool Play(string name, float x, float z, float gain, float rate, float occluded, bool immediate);
        void Listener(float x, float z);
        void Volume(float master, float effects, float ambience);
        void Ambience(string name, float level);
        /// <summary>The music: the piece at this address, at this level (0 stops it). One piece at a time.</summary>
        void Music(string url, float level);
    }

    /// <summary>The Web Audio engine, in a browser build.</summary>
    public sealed class WebSoundOut : ISoundOut
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int LovAudio_Init(int seed);
        [DllImport("__Internal")] private static extern void LovAudio_Load(string name, string url);
        [DllImport("__Internal")] private static extern int LovAudio_Loaded(string name);
        [DllImport("__Internal")] private static extern int LovAudio_Play(string name, float x, float z, float gain, float rate, float occluded, int immediate);
        [DllImport("__Internal")] private static extern void LovAudio_Listener(float x, float z);
        [DllImport("__Internal")] private static extern void LovAudio_Volume(float master, float effects, float ambience);
        [DllImport("__Internal")] private static extern void LovAudio_Ambience(string name, float level);
        [DllImport("__Internal")] private static extern void LovAudio_Music(string url, float level);
        public WebSoundOut() => LovAudio_Init(20260929);
        public void Load(string name, string url) => LovAudio_Load(name, url);
        public bool Loaded(string name) => LovAudio_Loaded(name) != 0;
        public bool Play(string name, float x, float z, float gain, float rate, float occluded, bool immediate)
            => LovAudio_Play(name, x, z, gain, rate, occluded, immediate ? 1 : 0) != 0;
        public void Listener(float x, float z) => LovAudio_Listener(x, z);
        public void Volume(float master, float effects, float ambience) => LovAudio_Volume(master, effects, ambience);
        public void Ambience(string name, float level) => LovAudio_Ambience(name, level);
        public void Music(string url, float level) => LovAudio_Music(url, level);
#else
        public void Load(string name, string url) { }
        public bool Loaded(string name) => false;
        public bool Play(string name, float x, float z, float gain, float rate, float occluded, bool immediate) => false;
        public void Listener(float x, float z) { }
        public void Volume(float master, float effects, float ambience) { }
        public void Ambience(string name, float level) { }
        public void Music(string url, float level) { }
#endif
    }

    /// <summary>Outside a browser: keeps what it was asked to play, for the tests and the log.</summary>
    public sealed class RecordingSoundOut : ISoundOut
    {
        public readonly List<(string name, float x, float z, float gain, bool immediate)> Played = new List<(string, float, float, float, bool)>();
        public float ListenerX, ListenerZ, AmbienceLevel, MusicLevel;
        public string MusicUrl;
        public void Load(string name, string url) { }
        public bool Loaded(string name) => true;
        public bool Play(string name, float x, float z, float gain, float rate, float occluded, bool immediate)
        {
            Played.Add((name, x, z, gain, immediate));
            return true;
        }
        public void Listener(float x, float z) { ListenerX = x; ListenerZ = z; }
        public void Volume(float master, float effects, float ambience) { }
        public void Ambience(string name, float level) => AmbienceLevel = level;
        public void Music(string url, float level) { MusicUrl = url; MusicLevel = level; }
    }

    /// <summary>
    /// The simulation's events, as sound (PLAN §12.6): the three.js game layer
    /// (../Lanes of vietnam/src/audio/game.ts) ported with its measured rules.
    ///
    /// It reads the sim and never writes it, and it never reads the renderer
    /// except for where the camera is. Only what a person would hear gets a
    /// sound: rifles firing, grenades and shells going off. Shots are rationed
    /// — at most three new ones a tick, nearest first, and never closer than
    /// 55 ms apart in real time — because the three.js build measured what the
    /// alternative is: 911 sounds in twenty seconds, a wall of noise pumping a
    /// limiter, every report buried in the one before it. Fewer and louder.
    ///
    /// All of it recorded (the owner: "use assets not a synth"). The sim's men
    /// all carry rifles; the ear is told otherwise, one man in five firing
    /// bursts, the US gunner's Browning .30 cal and the VC's PPSh. A man going
    /// down is the round striking him and, a beat later, him hitting the
    /// ground. Near the listener the misses strike the earth. A barrage is
    /// also the battery that fired it, 105 mm howitzers far behind the line,
    /// one report a salvo; an air strike is a jet going over. And the first
    /// contact of a US player's match comes over the radio.
    /// </summary>
    public sealed class AudioView : MonoBehaviour
    {
        public const int ShotsPerTick = 3;
        public const float MinShotGap = 0.055f;
        /// <summary>How loud a sniper's shot comes back off the treeline, against a rifle's 0.9 and his own 1.2: no shot is played at it, so a test can tell the echo from one.</summary>
        public const float SniperEcho = 0.5f;

        public ISoundOut Out { get; private set; }
        private readonly Dictionary<string, List<string>> _sets = new Dictionary<string, List<string>>();
        private int _cursor;
        private int _lastTick = -1;
        private float _lastShotAt = -1;
        private float _lastCrackAt = -1;
        private float _intensity;
        private System.Random _rng = new System.Random(7);
        private GameRoot _root;
        /// <summary>Sounds owed a moment from now: a body falls after the round that dropped him.</summary>
        private readonly List<(float at, string clip, float x, float z, float gain)> _later = new List<(float, string, float, float, float)>();
        private float _lastDirtAt = -1;
        private int _salvoTick = -1, _salvoArea = -1;
        private bool _radioed;
        /// <summary>Where the US battery stands, behind the firebase: far enough to be a report, not a blast.</summary>
        public static readonly Vector2 Battery = new Vector2(-640f, 40f);

        /// <summary>One man in five fires bursts: an automatic rifleman or a gunner, to the ear.</summary>
        /// <summary>
        /// Does he fire bursts? By his weapon (Arms): the machine guns and the
        /// submachine guns. Without Arms every man is one rifleman, and one in
        /// five is given a burst by his number, as before.
        /// </summary>
        public static bool Automatic(Man m) => m.Weapon == Weapon.Rifle
            ? (m.Id * 7 + 3) % 5 == 0
            : m.Weapon == Weapon.M60 || m.Weapon == Weapon.Rpd || m.Weapon == Weapon.Smg;

        /// <summary>The model a man carries (tools/blender/weapons.py), by his weapon and his side.</summary>
        public static string Model(Man m)
        {
            bool us = m.Side == Side.Us;
            switch (m.Weapon)
            {
                case Weapon.Ak: return "ak";
                case Weapon.Sks: return "sks";
                case Weapon.M60: return "m60";
                case Weapon.Rpd: return "rpd";
                case Weapon.Smg: return us ? "m3" : "ppsh";
                case Weapon.Sniper: return us ? "m40" : "mosin";
                case Weapon.M79: return "m79";
                case Weapon.Rpg: return "rpg";
                case Weapon.Mortar: return "mortar";
                default: return us ? "m16" : "ak";
            }
        }

        /// <summary>The recordings a shot from his weapon is picked from.</summary>
        private string Shot(Man m)
        {
            switch (m.Weapon)
            {
                case Weapon.M16: return Pick("m16");
                case Weapon.Ak: return Pick("ak");
                case Weapon.Sks: return Pick("sks");
                case Weapon.M60: case Weapon.Rpd: return Pick("mg");
                case Weapon.Smg: return Pick("smg");
                // A bolt action each: the Marines' rifle (a .308), the VC marksman's Mosin-Nagant.
                case Weapon.Sniper: return (m.Side == Side.Us ? Pick("sniper") : null) ?? Pick("bolt") ?? Pick("sks");
                default:
                    bool auto = Automatic(m);
                    return (m.Side == Side.Us ? (auto ? Pick("mg") : Pick("m16")) : (auto ? Pick("smg") : Pick("ak", "ak", "sks")))
                           ?? (m.Side == Side.Us ? Pick("m16") : Pick("ak", "sks"));
            }
        }

        /// <summary>Shots played and dropped since the match began, for the log and the tests.</summary>
        public int Played { get; private set; }
        public int Rationed { get; private set; }

        [Serializable] private class FileEntry { public string file; }
        [Serializable] private class Set { public string name; public FileEntry[] files; }
        [Serializable] private class Listing { public Set[] sets; }

        private void Awake()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Out = new WebSoundOut();
#else
            Out = new RecordingSoundOut();
#endif
        }

        private IEnumerator Start()
        {
            string dir = Application.streamingAssetsPath + "/Audio/";
            string json = null;
            if (dir.Contains("://"))
            {
                using var req = UnityWebRequest.Get(dir + "audio.json");
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) json = req.downloadHandler.text;
            }
            else if (System.IO.File.Exists(dir + "audio.json")) json = System.IO.File.ReadAllText(dir + "audio.json");
            if (json == null) { Debug.LogWarning("[LOV] audio: no listing; the game is silent"); yield break; }
            foreach (var set in JsonUtility.FromJson<Listing>(json).sets)
            {
                // The music is streamed when it plays, not fetched and decoded now with the shots.
                if (set.name == "music") { foreach (var f in set.files) _music.Add(dir + f.file); continue; }
                var names = new List<string>();
                foreach (var f in set.files)
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(f.file);
                    Out.Load(name, dir + f.file);
                    names.Add(name);
                }
                _sets[set.name] = names;
            }
        }

        /// <summary>Forget the backlog, on a new match, so it does not replay the last one.</summary>
        public void ResetView()
        {
            _cursor = 0; _lastTick = -1; _intensity = 0; Played = Rationed = 0;
            _later.Clear(); _salvoTick = _salvoArea = -1; _radioed = false;
        }

        /// <summary>One recording of a set, at random; null if there is no such set.</summary>
        private string Pick(string set)
            => _sets.TryGetValue(set, out var l) && l.Count > 0 ? l[_rng.Next(l.Count)] : null;

        /// <summary>One recording out of two or three sets taken together (a set named twice counts twice).</summary>
        private string Pick(string a, string b, string c = null)
        {
            _sets.TryGetValue(a, out var la);
            _sets.TryGetValue(b, out var lb);
            List<string> lc = null;
            if (c != null) _sets.TryGetValue(c, out lc);
            int na = la?.Count ?? 0, nb = lb?.Count ?? 0, nc = lc?.Count ?? 0;
            if (na + nb + nc == 0) return null;
            int k = _rng.Next(na + nb + nc);
            return k < na ? la[k] : k < na + nb ? lb[k - na] : lc[k - na - nb];
        }

        private float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        private UI.Hud _hud;
        private readonly List<(Man m, float d)> _shots = new List<(Man, float)>();
        /// <summary>The pieces of music, by address (audio.json's "music" set). A match plays one of them, by its seed.</summary>
        private readonly List<string> _music = new List<string>();
        /// <summary>How loud the music is against everything else, with its slider all the way up: under the fighting, not over it.</summary>
        public const float MusicGain = 0.32f;

        private void LateUpdate()
        {
            _root ??= GameRoot.Instance;
            if (_root == null || _root.Driver == null || Out == null) return;
            var st = _root.Driver.State;
            var cam = _root.CameraRig != null ? _root.CameraRig.Camera.transform.position : Vector3.zero;
            float lx = cam.x, lz = (float)Coords.SimZ(cam.z);
            Out.Listener(lx, lz);

            // (Found once: looked for every frame, it was a search of the scene a hundred times a second.)
            if (_hud == null) _hud = FindAnyObjectByType<UI.Hud>();
            bool on = _hud == null || _hud.Sound;
            var s = _root.Settings;
            Out.Volume(on ? (s?.Master ?? 0.9f) : 0f, s?.Effects ?? 1f, s?.Ambience ?? 0.8f);
            // The music, while its button is on: one piece a match, the next match the other.
            if (_music.Count > 0)
                Out.Music(_music[Mathf.Abs(_root.Seed) % _music.Count], on && (_hud == null || _hud.Music) ? MusicGain * (s?.Music ?? 0.7f) : 0f);

            if (st.Tick < _lastTick) ResetView();
            _lastTick = st.Tick;
            var ev = st.Events;
            if (_cursor > ev.Count) _cursor = 0;

            var shots = _shots;
            shots.Clear();
            int fired = 0;
            for (int i = _cursor; i < ev.Count; i++)
            {
                var e = ev[i];
                switch (e.Kind)
                {
                    case EventKind.Fire:
                        if (e.Id >= st.Men.Count) break;
                        fired++;
                        var m = st.Men[e.Id];
                        if (m.Weapon == Weapon.Sniper)
                        {
                            // A sniper's shot is one shot, and it is heard: never rationed out behind the
                            // rifles nearer the listener (he fires from the back, so it always was), heavier
                            // than they are, and with its slap back off the treeline a third of a second on.
                            string report = Shot(m);
                            if (report != null && Out.Play(report, (float)m.X, (float)m.Z, 1.2f, Range(0.9f, 0.96f), 0f, false)) Played++;
                            _later.Add((Time.unscaledTime + Range(0.3f, 0.38f), report, (float)m.X + Range(-25, 25), (float)m.Z - 45f, SniperEcho));
                        }
                        else shots.Add((m, Dist(m.X, m.Z, lx, lz)));
                        // A miss near the listener: the round into the earth by its target.
                        if (e.Target is int tg && tg < st.Men.Count && Time.unscaledTime - _lastDirtAt > 0.12f
                            && !(i + 1 < ev.Count && ev[i + 1].Kind == EventKind.Kill && ev[i + 1].Id == tg)
                            && Dist(st.Men[tg].X, st.Men[tg].Z, lx, lz) < 70 && _rng.NextDouble() < 0.4)
                        {
                            _lastDirtAt = Time.unscaledTime;
                            var t = st.Men[tg];
                            // Mostly into the dirt; now and then off something hard, whining away.
                            bool ricochet = _rng.NextDouble() < 0.3;
                            // (Gunnery: where the round came down, which the event carries.)
                            Play(Pick(ricochet ? "crack" : "dirt"), e.X ?? t.X + Range(-3, 3), e.Z ?? t.Z + Range(-2, 2), ricochet ? 0.55f : 0.6f, Range(0.85f, 1.15f), false);
                        }
                        break;
                    case EventKind.Launch:
                        // A launcher or a mortar firing: its own report (an M203's 40 mm thump for the M79, an RPG-7, an
                        // 81 mm mortar). Without those recordings, the battery's howitzer pitched up to its size, as before.
                        if (e.Id < st.Men.Count)
                        {
                            var by = st.Men[e.Id];
                            string own = Pick(by.Weapon == Weapon.Mortar ? "mortar" : by.Weapon == Weapon.Rpg ? "rpg" : "m79");
                            if (own != null) Play(own, by.X, by.Z, by.Weapon == Weapon.M79 ? 0.8f : 1f, Range(0.94f, 1.06f), false);
                            else Play(Pick("howitzer"), by.X, by.Z, by.Weapon == Weapon.Mortar ? 0.7f : 0.5f, by.Weapon == Weapon.Mortar ? Range(1.5f, 1.7f) : Range(2.0f, 2.3f), false);
                        }
                        break;
                    case EventKind.Melee:
                        // Hand to hand: the blow, heard only close.
                        if (e.Id < st.Men.Count && Dist(st.Men[e.Id].X, st.Men[e.Id].Z, lx, lz) < 70)
                            Play(Pick("thump"), st.Men[e.Id].X, st.Men[e.Id].Z, 0.9f, Range(0.75f, 0.9f), false);
                        break;
                    case EventKind.Kill:
                        // A man going down: the round striking him, near enough to hear.
                        if (e.Id < st.Men.Count)
                        {
                            var k = st.Men[e.Id];
                            float kd = Dist(k.X, k.Z, lx, lz);
                            if (kd < 160) Play(Pick("thump"), k.X, k.Z, 0.8f, Range(0.92f, 1.08f), false);
                            if (kd < 110) _later.Add((Time.unscaledTime + Range(0.45f, 0.7f), Pick("bodyfall"), (float)k.X, (float)k.Z, 0.7f));
                        }
                        break;
                    case EventKind.Pinned:
                        // Being pinned is rounds passing close. Near the camera the player
                        // hears them go by, ahead of the report that fired them — the round
                        // is supersonic — so this one skips the travel delay.
                        if (e.Id < st.Men.Count && Time.unscaledTime - _lastCrackAt > 0.1f)
                        {
                            var p = st.Men[e.Id];
                            if (Dist(p.X, p.Z, lx, lz) < 90)
                            {
                                _lastCrackAt = Time.unscaledTime;
                                Play(Pick("crack"), p.X + Range(-3, 3), p.Z + Range(-3, 3), 0.6f, Range(0.9f, 1.2f), true);
                            }
                        }
                        break;
                    case EventKind.GrenadeBlast:
                        Boom(e, "grenade", 0.8f, lx, lz);
                        break;
                    case EventKind.Shell:
                        Boom(e, "shell", 1f, lx, lz);
                        // The guns that fired it, once a salvo: far off, heard at once
                        // (the rounds were in the air long before the sim lands them).
                        if (e.Tick != _salvoTick || e.Id != _salvoArea)
                        {
                            _salvoTick = e.Tick; _salvoArea = e.Id;
                            var area = st.Areas.Find(a => a.Id == e.Id);
                            if (area == null || area.Radius > 14)
                            {
                                float bx = e.Side == Side.Us ? Battery.x : -Battery.x;
                                Play(Pick("howitzer"), bx + Range(-30, 30), Battery.y + Range(-30, 30), 0.9f, Range(0.95f, 1.05f), true);
                            }
                        }
                        break;
                    case EventKind.AreaStart:
                        // The air strike (the tight barrage): a jet low over the target.
                        var strike = st.Areas.Find(a => a.Id == e.Id);
                        if (strike != null && strike.Kind == AreaKind.Barrage && strike.Radius <= 14 && e.X != null)
                            Play(Pick("jet"), e.X.Value, e.Z.Value, 1f, Range(0.97f, 1.03f), false);
                        break;
                    case EventKind.FirstContact:
                        if (!_radioed && _root.PlayerSide == Side.Us)
                        {
                            _radioed = true;
                            Play(Pick("radio"), lx, lz + 2, 0.45f, 1f, true);
                        }
                        break;
                }
            }
            _cursor = ev.Count;
            for (int i = _later.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < _later[i].at) continue;
                var l = _later[i];
                _later.RemoveAt(i);
                Play(l.clip, l.x, l.z, l.gain, Range(0.93f, 1.07f), false);
            }

            // The bed gets out of the way while the shooting is heavy.
            _intensity += (Mathf.Min(1, fired / 8f) - _intensity) * 0.25f;
            Out.Ambience("ambience", 0.22f * (1 - 0.75f * _intensity));

            if (shots.Count == 0) return;
            float now = Time.unscaledTime;
            if (now - _lastShotAt < MinShotGap) { Rationed += shots.Count; return; }
            _lastShotAt = now;
            shots.Sort((a, b) => a.d.CompareTo(b.d));
            for (int i = 0; i < shots.Count; i++)
            {
                if (i >= ShotsPerTick) { Rationed++; continue; }
                var (m, d) = shots[i];
                string clip = Shot(m);
                if (clip == null) continue;
                // Pitch varies per shot: identical impulses at 20 Hz comb against
                // each other and read as a machine, not twenty men with rifles.
                if (Out.Play(clip, (float)m.X, (float)m.Z, 0.9f, Range(0.94f, 1.06f), d > 140 ? 0.5f : 0f, false)) Played++;
            }
        }

        private void Play(string clip, double x, double z, float gain, float rate, bool immediate)
        {
            if (clip != null) Out.Play(clip, (float)x, (float)z, gain, rate, 0f, immediate);
        }

        private void Boom(SimEvent e, string set, float gain, float lx, float lz)
        {
            if (e.X == null) return;
            string clip = Pick(set);
            if (clip != null) Out.Play(clip, (float)e.X.Value, (float)e.Z.Value, gain, Range(0.95f, 1.05f), 0f, false);
        }

        private static float Dist(double x, double z, float lx, float lz)
        {
            double dx = x - lx, dz = z - lz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
