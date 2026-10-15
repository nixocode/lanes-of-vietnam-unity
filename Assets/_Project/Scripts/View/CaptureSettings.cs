using System;
using System.Globalization;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// A frozen moment to render: which match, which tick, where the camera is.
    ///
    /// Brief §10: every visual change is A/B'd from an identical frozen frame,
    /// because half the three.js project's visual arguments were lost to
    /// comparing two different moments. When this is set, the game does not
    /// advance on its own: it fast-forwards the simulation to <see cref="Tick"/>,
    /// holds it there, fixes the view clock (wind, flicker, anything animated)
    /// at <see cref="ViewTime"/>, and renders the same frame every time.
    ///
    /// Set by the capture harness from <c>-lovCapture key=value,...</c>.
    /// </summary>
    public sealed class CaptureSettings
    {
        public int Seed = 3;
        public int Tick = 700;
        public float CameraX;
        public float Dolly;
        /// <summary>Reference aspect: TARGET.jpg is 2772 x 1504.</summary>
        public int Width = 1843;
        public int Height = 1000;
        /// <summary>Frames rendered before the one kept, so temporal effects have converged.</summary>
        public int Warmup = 48;
        public float ViewTime = 10f;
        public string Out = "captures/frame.png";
        /// <summary>Frames to keep: 2 or more for the flicker measurement.</summary>
        public int Frames = 1;
        /// <summary>Render with no post-processing, for isolating a change.</summary>
        public bool NoPost;
        /// <summary>Hide the interface.</summary>
        public bool NoUi = true;
        /// <summary>
        /// Antialiasing for the capture: smaa (the game's), taa, fxaa, none.
        /// "none" is the flicker instrument's zero: two renders of a frozen
        /// scene with nothing temporal on must be identical.
        /// </summary>
        public string Aa = "smaa";
        /// <summary>Select this squad (id) before rendering, to see its rings; -1 for none, -2 for Tab's first pick.</summary>
        public int Select = -1;
        /// <summary>Raise the field glasses toward this viewport point (x,y in 0..1); null for none.</summary>
        public float[] Glasses;
        /// <summary>Find the machine gunner who fired last and raise the field glasses on him (gunner=1): to look at a burst close.</summary>
        public bool Gunner;
        /// <summary>Raise the field glasses on the radio man who answers the last call (radioman=1).</summary>
        public bool RadioMan;
        /// <summary>Run the match on until a mortar is set up, then raise the field glasses on it (mortar=1).</summary>
        public bool Mortar;
        /// <summary>Run the match on until a satchel charge is thrown, then raise the field glasses on where it lands (satchel=1).</summary>
        public bool SatchelThrow;
        /// <summary>
        /// Bring the watchtower down (MatchOptions.Destruction), then run this many ticks on and raise the field glasses on
        /// it (towerfall=30); towerfrom=degrees is the side the burst that brings it down is on (the simulation's: 0 is +x).
        /// With call=us-mg:2:0:5 a squad is posted up it first, and comes down with it. towerbuckle=1 has it break at its
        /// first girt, towerbuckle=0 go over whole; left out, its dice say.
        /// </summary>
        public int TowerFall = -1;
        public float TowerFrom;
        public int TowerBuckle = -1;
        /// <summary>Run the match on until a squad throws its smoke grenade, then raise the field glasses on it (squadthrow=1), plus this many ticks.</summary>
        public int SquadThrow = -1;
        /// <summary>Show a screen for the capture: start, settings or end (the match must be over for end).</summary>
        public string Screen;
        /// <summary>Post exposure in stops, replacing the profile's for this capture; null keeps it. For tuning by measurement.</summary>
        public float? Ev;
        /// <summary>A colour filter (linear r:g:b) replacing the profile's, for tuning white balance; null keeps it.</summary>
        public float[] Tint;
        /// <summary>Tonemapper for this capture: aces or neutral; null keeps the profile's.</summary>
        public string Tone;
        /// <summary>The ground shader's debug view (1 albedo, 2 normal, 3 layers, 4 lit without normal map, 5 grey card); 0 off.</summary>
        public int GroundDebug;
        /// <summary>The ground shader's texture gradient scale (mip bias); 0 keeps the material's.</summary>
        public float GroundGrad;
        /// <summary>The plants' far mip bias (every map); negative keeps the material's.</summary>
        public float PlantBias = -1;
        /// <summary>
        /// Call-ins to play during the fast-forward, through the same command
        /// the player's card goes through: (card id, lane, x, tick). For
        /// looking at shells and smoke, which the computer's plans never call.
        /// </summary>
        public readonly System.Collections.Generic.List<(string card, int lane, double x, int tick)> Calls =
            new System.Collections.Generic.List<(string, int, double, int)>();
        /// <summary>The plans each side plays in the capture's match (Plan.ByName); null keeps the game's ceiling.</summary>
        public string UsPlan, VcPlan;
        /// <summary>The scenario to play, by its id (scenario=first-light); the firebase if not given.</summary>
        public string Scenario;
        /// <summary>Grenades on or off for the capture's match; null keeps the game's (on).</summary>
        public bool? Frag;
        /// <summary>Frames per second of match time between frames, for watching motion; 0 freezes time (the default).</summary>
        public float Movie;
        /// <summary>Lift, gamma, gain (their w: -1..1) for the grade, replacing the profile's; null keeps it.</summary>
        public float[] Lgg;
        /// <summary>Grade saturation (-100..100), replacing the profile's; null keeps it.</summary>
        public float? Sat;
        /// <summary>Fog density (exponential squared), replacing the scene's; null keeps it.</summary>
        public float? Fog;
        /// <summary>Extra stops on the sky alone (a graduated filter), on top of the scene's; null keeps it.</summary>
        public float? SkyEv;
        /// <summary>Squad smoke on or off for the capture's match; null keeps the game's (on).</summary>
        public bool? SquadSmoke;
        /// <summary>Part 2: drill (steady orders and places). Null: the game's default (on).</summary>
        public bool? Drill;
        /// <summary>Part 2: fieldcraft (stand-off, places, melee, levers). Null: the game's default (on).</summary>
        public bool? Fieldcraft;
        /// <summary>Part 2: arms (a weapon to every man, a squad to every card). Null: the game's default (on).</summary>
        public bool? Arms;
        /// <summary>Squads that spot each other and act on it (MatchOptions.Senses); the game's default (on) if not given.</summary>
        public bool? Senses;
        /// <summary>A card held in the hand for the picture (its id), the lane it is on and where along it.</summary>
        /// <summary>Tag every squad in sight, for a picture of the squad tags.</summary>
        public bool Tags;
        public string Arm;
        public int ArmLane;
        public double ArmX;
        /// <summary>Fire down the lane, from a knee or flat, never on the move (MatchOptions.Gunnery); the game's default (on) if not given.</summary>
        public bool? Gunnery;
        /// <summary>Rounds counted, magazines and reloads, bursts (MatchOptions.Ammo); the game's default (on) if not given.</summary>
        public bool? Ammo;
        /// <summary>Fire at sight, bounds by halves, a sniper's distance, fewer grenades (MatchOptions.Tactics); the game's default (on) if not given.</summary>
        public bool? Tactics;
        /// <summary>Men who differ, and luck (MatchOptions.Fortune); the game's default (on) if not given.</summary>
        public bool? Fortune;
        /// <summary>Fire spread and aimed at the man likeliest to be hit, heads kept down, an assault on a beaten enemy (MatchOptions.FireControl); the game's default (on) if not given.</summary>
        public bool? FireControl;
        /// <summary>A round that misses may hit the man beside its target (MatchOptions.Ballistics); the game's default (on) if not given.</summary>
        public bool? Ballistics;
        /// <summary>A squad can be posted up the watchtower (MatchOptions.Vantage); the game's default (on) if not given.</summary>
        public bool? Vantage;
        /// <summary>The computer's side commands (MatchOptions.Commander); the game's default (on) if not given.</summary>
        public bool? Commander;
        /// <summary>A squad keeps together and makes up its mind (MatchOptions.Cohesion); the game's default (on) if not given.</summary>
        public bool? Cohesion;
        /// <summary>A man keeps the way he has gone down, gets up only for a move he can make, and gathers pace (MatchOptions.Bearing); the game's default (on) if not given.</summary>
        public bool? Bearing;
        /// <summary>A call-in takes time to arrive (MatchOptions.Inbound); null: on, as in the game.</summary>
        public bool? Inbound;
        /// <summary>Hand grenades are a card (MatchOptions.FragCard); null: on, as in the game.</summary>
        public bool? FragCard;
        /// <summary>A mortar fires from where it is set up (MatchOptions.Emplace); null: on, as in the game.</summary>
        public bool? Emplace;
        /// <summary>Sappers and engineers carry satchel charges (MatchOptions.Satchel); null: on, as in the game.</summary>
        public bool? Satchel;
        /// <summary>A structure can be broken (MatchOptions.Destruction); null: on, as in the game.</summary>
        public bool? Destruction;
        /// <summary>The medevac can be shot down (MatchOptions.Shootdown); null: on, as in the game.</summary>
        public bool? Shootdown;
        /// <summary>A medevac called in the capture is brought down three seconds out, whatever the dice (downed=1; Shootdown.Force).</summary>
        public bool Downed;
        /// <summary>TAA base blend factor, variance clamp scale, and optionally jitter scale and quality (0-4); null keeps the camera's.</summary>
        public float[] Taa;

        /// <summary>The active capture, or null when the game is being played.</summary>
        public static CaptureSettings Active;

        public static CaptureSettings Parse(string spec)
        {
            var c = new CaptureSettings();
            if (string.IsNullOrWhiteSpace(spec)) return c;
            foreach (var part in spec.Split(','))
            {
                var kv = part.Split(new[] { '=' }, 2);
                if (kv.Length != 2) throw new ArgumentException($"capture: \"{part}\" is not key=value");
                string k = kv[0].Trim(), v = kv[1].Trim();
                var inv = CultureInfo.InvariantCulture;
                switch (k)
                {
                    case "seed": c.Seed = int.Parse(v, inv); break;
                    case "tick": c.Tick = int.Parse(v, inv); break;
                    case "x": c.CameraX = float.Parse(v, inv); break;
                    case "dolly": c.Dolly = float.Parse(v, inv); break;
                    case "w": c.Width = int.Parse(v, inv); break;
                    case "h": c.Height = int.Parse(v, inv); break;
                    case "warmup": c.Warmup = int.Parse(v, inv); break;
                    case "time": c.ViewTime = float.Parse(v, inv); break;
                    case "out": c.Out = v; break;
                    case "frames": c.Frames = int.Parse(v, inv); break;
                    case "ev": c.Ev = float.Parse(v, inv); break;
                    case "ground": c.GroundDebug = int.Parse(v, inv); break;
                    case "groundgrad": c.GroundGrad = float.Parse(v, inv); break;
                    case "plantbias": c.PlantBias = float.Parse(v, inv); break;
                    case "usplan": c.UsPlan = v; break;
                    case "scenario": c.Scenario = v; break;
                    case "frag": c.Frag = v == "1" || v == "true"; break;
                    case "movie": c.Movie = float.Parse(v, inv); break;
                    case "sat": c.Sat = float.Parse(v, inv); break;
                    case "fog": c.Fog = float.Parse(v, inv); break;
                    case "skyev": c.SkyEv = float.Parse(v, inv); break;
                    case "lgg":
                        var lg = v.Split(':');
                        c.Lgg = new[] { float.Parse(lg[0], inv), float.Parse(lg[1], inv), float.Parse(lg[2], inv) };
                        break;
                    case "squadsmoke": c.SquadSmoke = v == "1" || v == "true"; break;
                    case "drill": c.Drill = v == "1" || v == "true"; break;
                    case "fieldcraft": c.Fieldcraft = v == "1" || v == "true"; break;
                    case "arms": c.Arms = v == "1" || v == "true"; break;
                    case "senses": c.Senses = v == "1" || v == "true"; break;
                    case "tags": c.Tags = v == "1" || v == "true"; break;
                    case "arm":
                        // arm=us-rifle:0:-10  a card held on a lane at an x, to look at the lane selector.
                        var arm = v.Split(':');
                        c.Arm = arm[0];
                        c.ArmLane = arm.Length > 1 ? int.Parse(arm[1], inv) : 0;
                        c.ArmX = arm.Length > 2 ? double.Parse(arm[2], inv) : 0;
                        break;
                    case "gunnery": c.Gunnery = v == "1" || v == "true"; break;
                    case "ammo": c.Ammo = v == "1" || v == "true"; break;
                    case "tactics": c.Tactics = v == "1" || v == "true"; break;
                    case "fortune": c.Fortune = v == "1" || v == "true"; break;
                    case "firecontrol": c.FireControl = v == "1" || v == "true"; break;
                    case "ballistics": c.Ballistics = v == "1" || v == "true"; break;
                    case "vantage": c.Vantage = v == "1" || v == "true"; break;
                    case "commander": c.Commander = v == "1" || v == "true"; break;
                    case "cohesion": c.Cohesion = v == "1" || v == "true"; break;
                    case "bearing": c.Bearing = v == "1" || v == "true"; break;
                    case "inbound": c.Inbound = v == "1" || v == "true"; break;
                    case "fragcard": c.FragCard = v == "1" || v == "true"; break;
                    case "emplace": c.Emplace = v == "1" || v == "true"; break;
                    case "satchel": c.Satchel = v == "1" || v == "true"; break;
                    case "destruction": c.Destruction = v == "1" || v == "true"; break;
                    case "shootdown": c.Shootdown = v == "1" || v == "true"; break;
                    case "downed": c.Downed = v == "1" || v == "true"; break;
                    case "vcplan": c.VcPlan = v; break;
                    case "call":
                        // call=card:lane:x:tick, repeatable
                        var cl = v.Split(':');
                        // (x "auto": GRENADES played at an enemy in cover one of the side's men can reach, as simcs plays it.)
                        c.Calls.Add((cl[0], int.Parse(cl[1], inv), cl[2] == "auto" ? double.NaN : double.Parse(cl[2], inv), int.Parse(cl[3], inv)));
                        break;
                    case "taa":
                        var ta = v.Split(':');
                        c.Taa = System.Array.ConvertAll(ta, t => float.Parse(t, inv));
                        break;
                    case "tint":
                        var t = v.Split(':');
                        c.Tint = new[] { float.Parse(t[0], inv), float.Parse(t[1], inv), float.Parse(t[2], inv) };
                        break;
                    case "tone": c.Tone = v; break;
                    case "nopost": c.NoPost = v == "1" || v == "true"; break;
                    case "ui": c.NoUi = !(v == "1" || v == "true"); break;
                    case "sel": c.Select = v == "tab" ? -2 : int.Parse(v, inv); break;
                    case "gunner": c.Gunner = v == "1" || v == "true"; break;
                    case "radioman": c.RadioMan = v == "1" || v == "true"; break;
                    case "mortar": c.Mortar = v == "1" || v == "true"; break;
                    case "satchelthrow": c.SatchelThrow = v == "1" || v == "true"; break;
                    case "squadthrow": c.SquadThrow = int.Parse(v, inv); break;
                    case "towerfall": c.TowerFall = int.Parse(v, inv); break;
                    case "towerfrom": c.TowerFrom = float.Parse(v, inv); break;
                    case "towerbuckle": c.TowerBuckle = int.Parse(v, inv); break;
                    case "glasses":
                    {
                        var xy = v.Split(':');
                        c.Glasses = new[] { float.Parse(xy[0], inv), float.Parse(xy[1], inv) };
                        break;
                    }
                    case "screen":
                        if (v != "start" && v != "settings" && v != "end")
                            throw new ArgumentException($"capture: screen must be start, settings or end, not \"{v}\"");
                        c.Screen = v;
                        break;
                    case "aa":
                        if (v != "taa" && v != "smaa" && v != "fxaa" && v != "none")
                            throw new ArgumentException($"capture: aa must be taa, smaa, fxaa or none, not \"{v}\"");
                        c.Aa = v;
                        break;
                    // An unknown key is an error, not a no-op. The three.js
                    // harness silently dropped "--wind 0" ('0' is falsy) and
                    // three flicker diagnoses in a row were wrong because of it.
                    default: throw new ArgumentException($"capture: unknown key \"{k}\"");
                }
            }
            return c;
        }

        public override string ToString()
            => $"seed={Seed},tick={Tick},x={CameraX.ToString(CultureInfo.InvariantCulture)},"
             + $"w={Width},h={Height},warmup={Warmup},time={ViewTime.ToString(CultureInfo.InvariantCulture)},aa={Aa}";
    }
}
