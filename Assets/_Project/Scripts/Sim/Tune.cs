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

        public const double SlotPull = 1.6;

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
        public const double MetresPerManInCover = 2.4;

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
        public const double BoundOpen = 9;

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
