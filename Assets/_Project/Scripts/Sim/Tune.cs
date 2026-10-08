namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Every number the simulation balances on, in one place.
    ///
    /// Ported from the three.js build, where each of these was either measured
    /// with the balance harness over 48 seeds or found by ablation against a
    /// floor plan. They carry across rather than being re-guessed because the
    /// port is exact: the RNG was parity-checked at 31,500 draws, and the match
    /// itself is checked tick-for-tick against the original by
    /// <c>tools/parity</c>. Same seed, same match, same numbers.
    ///
    /// Constants, not a ScriptableObject. A designer-editable asset would be
    /// lovely and would also mean a match is no longer a pure function of
    /// (seed, plan, orders) — the balance harness would be measuring whatever
    /// happened to be on disk. If these need to vary, they get passed into the
    /// match as a parameter so they are part of the input, not ambient state.
    ///
    /// Integer tick counts are literals with the seconds beside them. The
    /// original computes them with <c>Math.round(seconds * TICK_HZ)</c>, and a
    /// C# cast truncates: <c>0.35 * 20</c> is 6.9999999 in float, which
    /// truncates to 6 where JavaScript rounds to 7. Literals cannot drift.
    /// </summary>
    public static class Tune
    {
        /// <summary>Simulation rate. The view interpolates between ticks.</summary>
        public const int TickHz = 20;

        /// <summary>1/20 s, in double exactly as the original computes it.</summary>
        public const double Dt = 1.0 / TickHz;

        /// <summary>
        /// A match is decided on morale past this, so a stalemate cannot run
        /// for ever. Twelve minutes, as the original: the time endings are
        /// part of what the audit proves reachable, and moving the cap moves
        /// them.
        /// </summary>
        public const int MaxTicks = TickHz * 60 * 12;

        // --- the map ---------------------------------------------------------

        /// <summary>
        /// Playable extent along X, each way from the centre.
        ///
        /// 45 m, not the 120 m this started at. A 240 m battlefield is six
        /// frames wide at a 19 degree lens, and two sides that open fire at
        /// 46 m stop closing at 46 m — the whole firefight settled just
        /// outside the picture. Found only once the sim was rendered.
        /// </summary>
        public const double HalfLength = 45;

        /// <summary>
        /// Z of each lane. <b>Two</b>, per §5: the near lane along the front of
        /// the position, the far lane along the track and the treeline. The
        /// track itself is not a lane; it is where crossing happens.
        /// </summary>
        public static readonly double[] Lanes = { 7.0, -6.0 };

        // --- squads ----------------------------------------------------------

        public const int SquadMin = 3;
        public const int SquadMax = 6;

        /// <summary>Spacing between formation slots, along the lane.</summary>
        public const double SlotGap = 2.6;

        /// <summary>
        /// Men per side at the start, scaled with the lane count. Going from
        /// three lanes to two without touching this made the fighting half
        /// again as dense and the mirror matchup measurably unfair (29%, with
        /// an interval that excluded 50%).
        /// </summary>
        public const int OpeningStrength = 16;

        // --- movement --------------------------------------------------------

        public const double SpeedStand = 2.0;
        public const double SpeedCrouch = 1.1;
        public const double SpeedProne = 0.45;

        public static double Speed(Posture p) => p switch
        {
            Posture.Standing => SpeedStand,
            Posture.Crouched => SpeedCrouch,
            _ => SpeedProne,
        };

        /// <summary>
        /// How fast an anchor advances under orders, m/s. Slower than a man
        /// walks, so the formation keeps together.
        /// </summary>
        public const double MarchSpeed = 1.35;

        /// <summary>How far ahead of its lead man an anchor may get (§9 finding 1).</summary>
        public const double AnchorLeash = 3.0;

        /// <summary>
        /// Window for the "is moving" test, in ticks, and the distance that
        /// counts as progress across it. Motion this tick is not movement.
        /// </summary>
        public const int MoveWindow = 6;        // 0.3 s
        public const double MoveEpsilon = 0.12;

        // --- fire ------------------------------------------------------------

        /// <summary>
        /// Engagement range. 28 m, not the 46 m this started at — it decides
        /// where the fight happens, and at 46 the whole firefight settled just
        /// outside the frame.
        /// </summary>
        public const double Range = 28;

        public const int Cooldown = 22;         // 1.1 s

        /// <summary>
        /// How long a man with no target waits before looking again. Without
        /// it he rescans every living man twenty times a second for ever;
        /// profiled at 55% of the whole simulation.
        /// </summary>
        public const int ScanIdle = 5;          // 0.25 s

        public const double HitBase = 0.16;
        public const double HitAtRange = 0.22;

        /// <summary>
        /// The miss is the point. It buys pin on the man it passed and on
        /// everyone near him, which is how fire suppresses a position rather
        /// than a man.
        /// </summary>
        public const double PinPerNearMiss = 0.20;
        public const double PinSplash = 3.2;
        public const double PinDecay = 0.22;

        /// <summary>Prone men recover faster: getting down is what recovering is.</summary>
        public const double PinDecayProne = 1.45;

        /// <summary>Past this a man goes to ground and stops advancing.</summary>
        public const double PinDrop = 0.35;

        /// <summary>Past this he mostly stops shooting.</summary>
        public const double PinStop = 0.72;

        // --- grenades (MatchOptions.Frag, Part 2) --------------------------------
        // A design translation, not numbers from the 2D game (§12.8 item 5): it
        // gives cover a counter at close range, the way a real assault breaks
        // a position the rifles cannot.
        /// <summary>Carried per man: an M26 or a Chinese stick grenade or two.</summary>
        public const int GrenadesCarried = 2;
        /// <summary>A throw from cover, or from a knee: 20 m, and never at his own feet.</summary>
        public const double FragRange = 20, FragMin = 7;
        /// <summary>With Arms, where every distance is the frame's: a throw is 12 m, and not inside 5.</summary>
        public const double ArmsFragRange = 12, ArmsFragMin = 5;
        /// <summary>Chance a tick, while he has a target, that he throws: about one second's hesitation.</summary>
        public const double FragThrowChance = 0.05;
        /// <summary>Ticks between one man's throws.</summary>
        public const int FragInterval = 160;
        /// <summary>Flight plus fuse: an M26's 4-5 s fuse, less the time he cooks it.</summary>
        public const int FragFuse = 50;
        /// <summary>Scatter of the landing point: a fixed part plus a share of the distance thrown.</summary>
        public const double FragScatterFixed = 0.7, FragScatterPerMetre = 0.1;
        /// <summary>Lethal radius, and the chance at the centre.</summary>
        public const double FragRadius = 5, FragKill = 0.6;
        /// <summary>What the burst suppresses.</summary>
        public const double FragPinRadius = 11, FragPin = 0.55;
        /// <summary>Cover barely helps against a grenade that comes down inside it; lying flat in the open helps more.</summary>
        public const double FragCoverFactor = 0.85, FragProneFactor = 0.55;

        // --- drill (MatchOptions.Drill, Part 2) ----------------------------------------
        /// <summary>Ticks an order stands once given (1.5 s), unless the new order is to fall back.</summary>
        public const int DrillDwell = 30;
        /// <summary>Metres from his place inside which a man does not move.</summary>
        public const double DrillSlack = 0.35;

        // --- fieldcraft (MatchOptions.Fieldcraft, Part 2) ------------------------------
        /// <summary>Metres from the middle of the lane to the file either side of it.</summary>
        public const double FileGap = 1.45;
        /// <summary>Metres between the men of a squad as it arrives.</summary>
        public const double SpawnGap = 1.3;
        /// <summary>Metres a man stands either side of his squad's file.</summary>
        public const double FileStagger = 0.45;
        /// <summary>A man whose place in the file is less than this behind him waits for the file to come up, rather than walk back to it.</summary>
        public const double FileWait = 6;
        /// <summary>A squad comes no nearer than this to an enemy it can see, unless it is assaulting.</summary>
        public const double StandOff = 12;
        /// <summary>How far ahead the enemy is counted when deciding to assault.</summary>
        public const double AssaultReach = 18;
        /// <summary>It assaults an enemy this far outnumbered (or one that is pinned: mean pin past PinDrop).</summary>
        public const double AssaultOdds = 2;
        /// <summary>A man this close to an enemy goes for him; half of it when he cannot see him (smoke, grass).</summary>
        public const double ChargeRange = 6;
        /// <summary>Hand to hand.</summary>
        public const double MeleeRange = 2.0;
        public const int MeleeCooldown = 18;
        /// <summary>The chance a blow kills, before the striker's suppression and the other man's state.</summary>
        public const double MeleeKill = 0.5;
        /// <summary>What a blow that does not kill does to the man it was aimed at.</summary>
        public const double MeleePin = 0.25;
        /// <summary>Inside this a shot is the likelier to hit the nearer it is: by CloseBonus at the muzzle.</summary>
        public const double CloseRange = 10, CloseBonus = 1.5;
        /// <summary>
        /// A rifle round goes through a man. One that kills carries on this far
        /// along its line, and a man within ThroughWidth of that line may be hit:
        /// ThroughChance that it reaches him, ThroughKill that it kills (it
        /// pins him and everyone by him regardless).
        /// </summary>
        public const double ThroughReach = 12, ThroughWidth = 0.6, ThroughChance = 0.5, ThroughKill = 0.5, ThroughPin = 0.35;
        /// <summary>A man killed beside you: every friend within FearRadius takes this much suppression.</summary>
        public const double FearRadius = 6, FearPin = 0.18;
        /// <summary>A man at rest sets off again when his place is this far from him, and is at rest again this near it.</summary>
        public const double SetOff = 0.9, Arrive = 0.12;
        /// <summary>A halted man further than this from his place gets up to go to it; nearer, he goes as he is.</summary>
        public const double KneelWithin = 4;
        /// <summary>How near the enemy has to be for a halted squad to go to ground: in contact, not on the march.</summary>
        public const double Contact = 45;
        /// <summary>Ticks a squad's decision to assault, or to close to its fighting distance, stands once made (3 s, 2 s).</summary>
        public const int ChargeTicks = TickHz * 3, ClosingTicks = TickHz * 2;
        /// <summary>Ticks a squad stays in cover it has reached before it makes for the next (3 s), and in a trench, which it had to climb into (8 s).</summary>
        public const int CoverPause = TickHz * 3, DugPause = TickHz * 8;
        /// <summary>A squad that fights from this near or nearer (sappers) comes up bent double while nobody has seen it.</summary>
        public const double Stalks = 8;
        /// <summary>A squad told to hold in the open makes for cover this near instead.</summary>
        public const double DashToCover = 12;
        /// <summary>Ticks a squad sent out of a position keeps going whatever its plan thinks (20 s), if it finds no cover sooner.</summary>
        public const int SentTicks = TickHz * 20;
        /// <summary>Ticks to climb into a trench, and out of one.</summary>
        public const int VaultInTicks = 14, VaultOutTicks = 18;
        /// <summary>Morale a side loses with a built position (about two men's worth), and with a crater or a bank.</summary>
        public const double PositionMorale = 0.05, GroundMorale = 0.02;

        // --- squad smoke (MatchOptions.SquadSmoke, Part 2) -----------------------------
        /// <summary>An M18 or two per squad.</summary>
        public const int SquadSmokeCarried = 1;
        /// <summary>
        /// A thrown canister: much less than the fire-mission screen (14 m,
        /// 22 s). Smoke here blocks every firing line through it outright, so
        /// it is kept small. Measured over 48 seeds, ceiling v ceiling: 7 m and
        /// 16 s on any advance halved the match (189 -> 84 s) and made ground
        /// the main cause of breaking (0.08 -> 0.79 morale). 3.5 m and 8 s on
        /// a stalled bound takes it to 148 s.
        /// </summary>
        public const double SquadSmokeRadius = 3.5;
        public const int SquadSmokeTicks = TickHz * 8;
        /// <summary>The squad's mean pin before it pops smoke: pinned past firing back, not merely down.</summary>
        public const double SquadSmokePin = PinStop;
        /// <summary>How far toward the enemy it lands, as a share of the distance.</summary>
        public const double SquadSmokeReach = 0.4;

        /// <summary>
        /// A pinned man fires this much as often, rather than not at all. A
        /// hard cutoff makes a firefight flip between two states.
        /// </summary>
        public const double PinnedFireRate = 0.18;

        /// <summary>
        /// Minimum ticks in a posture before it may change. Every input that
        /// picks a stance is a threshold, and in a firefight they chatter on
        /// it; one dwell where they funnel through beats debouncing each.
        /// </summary>
        public const int PostureDwell = 16;     // 0.8 s

        /// <summary>
        /// Hit multiplier on a man moving with no cover. 1.4 by sweep: at 2.2
        /// the tool-using plan beat the floor 98% of the time, which makes the
        /// tools compulsory rather than valuable.
        /// </summary>
        public const double MovingInOpen = 1.4;

        public const double ExposureStand = 1.0;
        public const double ExposureCrouch = 0.68;
        public const double ExposureProne = 0.42;

        public static double Exposure(Posture p) => p switch
        {
            Posture.Standing => ExposureStand,
            Posture.Crouched => ExposureCrouch,
            _ => ExposureProne,
        };

        // --- cover -----------------------------------------------------------

        /// <summary>How close across the lane a man must tuck in to be behind cover.</summary>
        public const double CoverRadius = 2.2;
        public const double CoverLengthMin = 5;
        public const double CoverLengthMax = 15;

        /// <summary>
        /// How far a holding squad will go for better cover. Unbounded, "hold"
        /// walks a defender up the map one position at a time.
        /// </summary>
        public const double HoldRadius = 18;

        /// <summary>
        /// Each man past capacity costs everyone in the cover this much, or
        /// every squad piles into the single best berm.
        /// </summary>
        public const double CrowdingPenalty = 0.22;

        /// <summary>
        /// Multiplier on what cover is worth. 1.2 by sweep against the floor
        /// plan: 1.0 gives 52% (cover buys nothing net), 1.4 gives 90% and is
        /// dominant.
        /// </summary>
        public const double CoverBoost = 1.2;

        /// <summary>
        /// Holding one position this long gets it ranged in — a warning, then
        /// accurate fire — so holding ground is a timing decision, not a free
        /// win.
        /// </summary>
        public const double RangeInSeconds = 14;
        public const double RangedInBonus = 1.9;
        public const double RangeInDecay = 2.5;

        // --- bounding --------------------------------------------------------

        /// <summary>
        /// Nobody crosses swept ground unless someone is covering them. Swept
        /// means the side's mean pin in the lane is at least this.
        /// </summary>
        public const double BoundSweepPin = 0.18;

        // --- the arc ---------------------------------------------------------

        /// <summary>
        /// The opening cannot last for ever if neither side finds the other.
        /// Contact lands at 8-13 s across seeds; this is the guard.
        /// </summary>
        public const double OpeningMaxSeconds = 45;

        public const double MoralePerCasualty = 0.028;
        public const double MoralePerGround = 0.00022;
        public const double SquadBreak = 0.5;

        /// <summary>
        /// A withdrawn squad rallies once its mean pin falls below this.
        /// Without a rally, falling back was a one-way door and measured worse
        /// than never retreating at all.
        /// </summary>
        public const double RallyPin = 0.06;

        public const double CpPerSecond = 0.9;

        /// <summary>
        /// Most men a side may have alive at once. Also what a lane game is:
        /// the map holds what it holds.
        /// </summary>
        public const int ForceCap = 40;
        public const double CpPerSquad = 22;
        public const int ReinforceEvery = TickHz * 6;

        // --- concealment and veterancy ----------------------------------------

        /// <summary>
        /// How close before a concealed man is spotted, by posture. Concealment
        /// is a state a man is in and can lose, not a property of being VC.
        /// </summary>
        public const double SpotRangeProne = 9;
        public const double SpotRangeCrouch = 16;
        public const double SpotRangeStand = 26;
        public const double SpotMovingBonus = 9;

        public static double SpotRange(Posture p) => p switch
        {
            Posture.Standing => SpotRangeStand,
            Posture.Crouched => SpotRangeCrouch,
            _ => SpotRangeProne,
        };

        // --- senses (MatchOptions.Senses) ---------------------------------------

        /// <summary>A man kneeling or flat behind cover is seen from this fraction of the distance.</summary>
        public const double SightInCover = 0.75;
        /// <summary>A man who has just fired is seen from this far, for this long: the flash and the report.</summary>
        public const double SightFired = 40;
        public const int SightFiredTicks = 60;
        /// <summary>A man pinned flat has his face in the dirt; a sniper has a scope.</summary>
        public const double SightPinned = 0.6;
        public const double SightScope = 1.5;
        /// <summary>Ticks a squad keeps another in sight after it last saw it (3 s), and remembers where it was (15 s).</summary>
        public const int SenseKeep = 60, SenseMemory = 300;
        /// <summary>Squads of a side this near each other pass the word of what they see.</summary>
        public const double WordRange = 30;
        /// <summary>An enemy squad in the other lane counts as this much further off when a squad picks the one it deals with.</summary>
        public const double LanePenalty = 6;
        /// <summary>Gunnery: as much, so that an enemy squad in its own lane it knows of is always the one a squad deals with.</summary>
        public const double LaneShun = 1000;
        /// <summary>
        /// A squad acts on an enemy it knows of inside this, or inside its own fighting distance and this much more if that is further.
        /// (At rifle range, 20 m, a squad coming up to a firefight strolled upright through its own
        /// firing line until it was itself that near; 28 is a machine gun's reach, near enough.)
        /// </summary>
        public const double ContactNear = 28, ContactPast = 6;
        /// <summary>How much faster than a march a squad moves between cover with the enemy in sight: a rush, not a walk.</summary>
        public const double RushPace = 1.45;
        /// <summary>Once in contact it stays in it until the enemy is this much further off again.</summary>
        public const double ContactSlack = 8;
        /// <summary>A squad fighting one in the other lane stays level with it until it is this far behind.</summary>
        public const double FlankPast = 4;
        /// <summary>Ticks at first contact before the squad moves: everyone drops where he is.</summary>
        public const int ContactTicks = 20;
        /// <summary>Ticks a task stands before the squad may take another (falling back is obeyed at once).</summary>
        public const int TaskMin = 40;
        /// <summary>Ticks in a firefight before it closes or goes in; ticks before it moves on from an enemy that is not in front of it.</summary>
        public const int FirefightMin = 80, FirefightMax = 240;
        /// <summary>Ticks a move forward under fire may last, and an assault before it is judged again.</summary>
        public const int CloseMax = 200, AssaultMax = 160;
        /// <summary>Ticks a squad runs for before it stops whether or not it has reached anything.</summary>
        public const int WithdrawMax = 300;
        /// <summary>At contact a squad takes cover within this of it, whichever way; falling back, the nearest behind it within this.</summary>
        public const double GroundReach = 10, WithdrawReach = 32;
        /// <summary>It takes no cover this near an enemy it knows of.</summary>
        public const double GroundClear = 6;
        /// <summary>How much faster than a march a squad falls back.</summary>
        public const double WithdrawPace = 1.5;
        /// <summary>A man firing at a squad his own has in sight, at a man of it he cannot himself see, fires this much slower; a machine gun's bursts on cover it has lost sight of pin this fraction.</summary>
        public const double BlindCooldown = 2.0, SuppressPin = 0.6;

        // --- gunnery (MatchOptions.Gunnery) ---------------------------------------

        /// <summary>A bullet is fired at a man no further across from the firer than this times how far he is along the lane.</summary>
        public const double ArcSlope = 0.5;
        /// <summary>Ticks a man has been still before he fires (0.2 s), and before he goes down from his feet (0.4 s).</summary>
        public const int SteadyTicks = 4, RestToKneel = 8;
        /// <summary>A man on his feet takes this share of his chances to fire, and hits this often against a man who is down.</summary>
        public const double StandingFire = 0.1, StandingHit = 0.33;
        /// <summary>A man on a knee or flat whose place is further off than this gets up and goes to it; nearer, he stays where he is.</summary>
        public const double GetUpBeyond = 1.5;
        /// <summary>Ticks to get to his feet: from a knee (0.6 s), from flat (1.2 s).</summary>
        public const int RiseFromKnee = 12, RiseFromProne = 24;
        /// <summary>
        /// A man's speed in contact, m/s, and how much faster than the baseline's march a squad's anchor
        /// goes when its men are at it (2.2 m/s). It was a run: 4.0, and the anchor at 2.8 times its march.
        /// The owner, playtest 7, the first he saw of it: "gameplay seems to go too fast now ... walk run
        /// is too quick (almost half)". At double time, then, not at a sprint: the walk's clip (1.8 m/s)
        /// played a third fast, which is a hurried man and not a run in slow motion. Playtest 8, on 2.4:
        /// "all speeds seem too fast now: running, walking and even gunfights". 1.9: the walk's clip at its
        /// own pace, a man going briskly; the anchor at 1.75.
        /// </summary>
        public const double SpeedRush = 1.9, RushPaceRun = 1.3;
        /// <summary>
        /// On the march: the anchor's pace against the baseline's 1.35 m/s (1.08), and a man's own
        /// speed coming up to his place in the file (it was the baseline's 2.0). The same note: a
        /// squad was in the fight too soon after it came on.
        /// </summary>
        public const double MarchPace = 0.7, SpeedWalk = 1.25;
        /// <summary>
        /// Ticks to climb into a trench, and out of one (0.95 and 1.2 s): a quarter slower than
        /// Fieldcraft's own (the same note: "climb animations need to be slowed by a quarter"). Each man
        /// takes up to two ticks more or less (Gunnery.VaultTicks): playtest 8, "all climb simultaneously".
        /// </summary>
        public const int SlowVaultInTicks = 19, SlowVaultOutTicks = 24;
        /// <summary>Ticks each man waits after the one ahead of him before he gets up or sets off (Gunnery.Stagger).</summary>
        public const int StaggerTicks = 4;
        /// <summary>Inside ten metres a pinned man still fires this share of his chances, and aims this well at worst; and nobody throws smoke at an enemy nearer than SmokeBeyond.</summary>
        public const double CloseSteady = 0.6, SmokeBeyond = 12;
        /// <summary>A miss lands up to this far past the man, and this far to a side plus this much a metre of range.</summary>
        public const double MissOver = 4, MissWide = 0.25, MissWidePerMetre = 0.02;

        // --- ammunition (MatchOptions.Ammo) ---------------------------------------------

        /// <summary>How much likelier a long burst, close in, is to hit than a short one; and ticks a squad leaves between one man's grenade and the next's (3 s).</summary>
        public const double LongBurstHit = 1.4;
        public const int SquadThrowGap = 60;

        // --- tactics (MatchOptions.Tactics) -------------------------------------------

        /// <summary>
        /// How much further than its own distance a rifle, a machine gun or a submachine gun fires at an
        /// enemy its squad has in sight, and by how many metres at most. A squad saw the enemy at 28 m
        /// with rifles that reached 20: both sides lay and looked at each other, then got up and ran to
        /// 12 m without a shot.
        /// </summary>
        public const double LongFire = 1.5, LongMost = 8;
        /// <summary>A long shot's chance against the same shot at the weapon's own distance; what its near miss pins; how much slower a man fires them.</summary>
        public const double LongHit = 0.15, LongPin = 0.35, LongCooldown = 3.5;
        /// <summary>Ticks one half of a squad in contact moves for, while the other half fires (4 s); and how near his place a man already running finishes his run, in the open and when it is a place in cover.</summary>
        public const int BoundTicks = 80;
        public const double BoundFinish = 2.5, BoundFinishCover = 6;
        /// <summary>A squad going in rushes the last of it, each man for his enemy, from this far. Further off it comes on by bounds.</summary>
        public const double RushFrom = 10;
        /// <summary>Grenades a man carries; and a sapper or an engineer, whose trade they are.</summary>
        public const int TacticsGrenades = 1, TacticsGrenadesClose = 2;
        /// <summary>Ticks before a squad that has thrown throws again (15 s).</summary>
        public const int TacticsThrowGap = 300;
        /// <summary>
        /// A team that fights from a long way off and does not go in (a sniper, the mortar: a distance
        /// of this or more) gives ground when an enemy it knows of is this much nearer than that.
        /// </summary>
        public const double FarTeam = 26, KeepOff = 5;
        /// <summary>Where a sniper team fights from, under this rule: inside a rifle's long shot, outside its own distance.</summary>
        public const double SniperReach = 26;
        /// <summary>Ticks after his shot that a sniper is the man a machine gun, another sniper, or a rifleman with nobody nearer fires at (6 s).</summary>
        public const int SniperMarked = 120;
        /// <summary>How much more a near miss pins a sniper than another man.</summary>
        public const double SniperShaken = 2.5;

        // --- fortune (MatchOptions.Fortune) -------------------------------------------

        /// <summary>How far a man's aim, his nerve and his quickness lie either side of the ordinary man's.</summary>
        public const double AimSpread = 0.3, NerveSpread = 0.3, QuickSpread = 0.2;
        /// <summary>
        /// Luck in a long shot: one that would have missed finds its man after all, this often. It is
        /// what kills a sniper at a distance no rifle has any business hitting at.
        /// </summary>
        public const double LuckyHit = 0.006;
        /// <summary>How much longer or shorter than its own time a pause between shots, or a reload, may be.</summary>
        public const double PauseSpread = 0.35;

        public const double VetPerSecond = 0.010;
        public const double VetCap = 1.0;

        /// <summary>At full veterancy, pin builds this fraction as fast.</summary>
        public const double VetPinResist = 0.45;

        // --- called-in effects (§7's deck) ------------------------------------

        /// <summary>Ticks between salvos inside a barrage. 1.2 s.</summary>
        public const int SalvoEvery = 24;

        public const double ShellRadius = 9;
        public const double ShellPin = 0.85;
        public const double TrapPin = 0.7;

        // --- match length -----------------------------------------------------
        // §8 makes morale the clock, so a longer match is one where will lasts
        // longer, not one padded with a timer. Calibrated against the mirror
        // matchup, which averaged 204 s at 1.0.

        public const double LengthSkirmish = 1.55;  // ~2 min
        public const double LengthStandard = 1.0;   // ~3.5 min
        public const double LengthSiege = 0.40;     // ~8 min
    }

    /// <summary>
    /// Per-kind cover characteristics. The kind decides these; they are not
    /// rolled independently of it.
    /// </summary>
    public static class CoverSpec
    {
        public static (double qLo, double qHi, double metresPerMan) For(CoverKind k) => k switch
        {
            CoverKind.Trench  => (0.58, 0.74, 1.8),
            CoverKind.Sandbag => (0.48, 0.64, 2.1),
            CoverKind.Bunker  => (0.70, 0.82, 3.4),
            CoverKind.Crater  => (0.34, 0.52, 2.6),
            _                 => (0.25, 0.44, 2.4),
        };
    }
}
