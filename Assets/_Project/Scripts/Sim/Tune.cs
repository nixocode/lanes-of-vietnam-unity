namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Every number the simulation balances on, in one place.
    ///
    /// Ported from the three.js build, where each of these was either measured
    /// with the balance harness over 48 seeds or arrived at by an ablation
    /// against a `floor` plan. They are carried across rather than re-guessed:
    /// the RNG is a proven exact port (31,500 draws, zero mismatches), so the
    /// same seeds produce the same matches and the same numbers still apply.
    ///
    /// `const`, not a ScriptableObject, and not serialized. A designer-editable
    /// asset would be lovely and would also mean a match is no longer a pure
    /// function of (seed, plan, orders) — the balance harness would be
    /// measuring whatever happened to be on disk. If these need to be tunable
    /// at runtime, the tunables get passed into the match as a parameter, so
    /// they are part of the input rather than ambient state.
    /// </summary>
    public static class Tune
    {
        /// <summary>Simulation rate. The view interpolates between ticks.</summary>
        public const int TickHz = 20;
        public const float Dt = 1f / TickHz;

        /// <summary>A match cannot run for ever, whatever the plans do.</summary>
        public const int MaxTicks = TickHz * 60 * 14;

        // --- the map -------------------------------------------------------

        /// <summary>
        /// Playable extent along X, each way from the centre.
        ///
        /// 45 m, not the 120 m this started at. A 240 m battlefield is six
        /// frames wide at a 19-degree lens, and two sides that open fire at
        /// 46 m stop closing at 46 m — the whole firefight settled just
        /// outside the picture. Found only once the sim was rendered.
        /// </summary>
        public const float HalfLength = 45f;

        /// <summary>
        /// Z of each lane. <b>Two</b>, per §5: the near lane along the front
        /// of the position, the far lane along the track and the treeline.
        ///
        /// The track itself is not a lane. It is where crossing between the
        /// two happens — a gap in the wire, a dip in the bank.
        /// </summary>
        public static readonly float[] Lanes = { 7.0f, -6.0f };

        // --- squads --------------------------------------------------------

        public const int SquadMin = 3;
        public const int SquadMax = 6;
        public const float SlotGap = 2.6f;
        public const int OpeningStrength = 16;

        public const float SpeedStand = 2.0f;
        public const float SpeedCrouch = 1.1f;
        public const float SpeedProne = 0.45f;

        public const float SlotPull = 1.6f;
        public const float MarchSpeed = 1.35f;
        public const float AnchorLeash = 3.0f;

        /// <summary>
        /// Window for the "is moving" test, in ticks, and the distance that
        /// counts as progress across it. Motion this tick is not movement.
        /// </summary>
        public const int MoveWindow = 6;    // 0.3 s
        public const float MoveEpsilon = 0.12f;

        // --- fire ----------------------------------------------------------

        /// <summary>
        /// Engagement range. 28 m, not the 46 m this started at — see
        /// <see cref="HalfLength"/>; they moved together.
        /// </summary>
        public const float Range = 28f;

        public const int Cooldown = 22;     // 1.1 s

        /// <summary>
        /// How long a man with no target waits before looking again.
        ///
        /// Without this he leaves his cooldown at zero and rescans every
        /// living man on every subsequent tick, for ever. Profiled: `fire` and
        /// the distance calls inside it were 55% of the whole simulation,
        /// nearly all of it men out of contact confirming it twenty times a
        /// second.
        /// </summary>
        public const int ScanIdle = 5;      // 0.25 s

        public const float HitBase = 0.16f;
        public const float HitAtRange = 0.22f;

        /// <summary>
        /// The miss is the point. It buys pin on the man it passed and on
        /// everyone near him, which is how fire suppresses a position rather
        /// than a man.
        /// </summary>
        public const float PinPerNearMiss = 0.20f;
        public const float PinSplash = 3.2f;
        public const float PinDecay = 0.22f;

        /// <summary>Past this a man goes prone and stops advancing.</summary>
        public const float PinDrop = 0.35f;

        /// <summary>Past this he mostly stops shooting.</summary>
        public const float PinStop = 0.72f;

        /// <summary>
        /// A pinned man fires this much as often, rather than not at all. A
        /// hard cutoff makes a firefight flip between two states.
        /// </summary>
        public const float PinnedFireRate = 0.18f;

        public const int PostureDwell = 16; // 0.8 s
        public const float MovingInOpen = 1.4f;

        public const float ExposureStand = 1.0f;
        public const float ExposureCrouch = 0.68f;
        public const float ExposureProne = 0.42f;

        // --- cover ---------------------------------------------------------

        public const float CoverRadius = 2.2f;
        public const float CoverLengthMin = 5f;
        public const float CoverLengthMax = 15f;
        public const float MetresPerManInCover = 2.4f;
        public const float HoldRadius = 18f;

        /// <summary>
        /// Crowding costs, or every squad piles into the single best piece of
        /// cover on the map and the ground stops mattering.
        /// </summary>
        public const float CrowdingPenalty = 0.22f;

        public const float CoverBoost = 1.2f;

        /// <summary>
        /// How long the enemy needs to range in on a position, and what it is
        /// worth once they have. Sitting still in good cover is not free.
        /// </summary>
        public const float RangeInSeconds = 14f;
        public const float RangedInBonus = 1.9f;
        public const float RangeInDecay = 2.5f;

        public const float BoundSweepPin = 0.18f;
        public const float BoundOpen = 9f;

        // --- the arc -------------------------------------------------------

        /// <summary>
        /// The opening cannot last for ever if neither side finds the other.
        /// Contact lands at 8-13 s across seeds; this is the guard.
        /// </summary>
        public const float OpeningMaxSeconds = 45f;

        public const float MoralePerCasualty = 0.028f;
        public const float MoralePerGround = 0.00022f;
        public const float SquadBreak = 0.5f;

        /// <summary>
        /// A squad that has broken rallies once it has steadied, rather than
        /// retreating off the map for the rest of the match.
        /// </summary>
        public const float RallyPin = 0.06f;

        public const float CpPerSecond = 0.9f;
        public const int ForceCap = 40;
        public const float CpPerSquad = 22f;
        public const int ReinforceEvery = TickHz * 6;

        // --- concealment and veterancy --------------------------------------

        public const float SpotRangeProne = 9f;
        public const float SpotRangeCrouch = 16f;
        public const float SpotRangeStand = 26f;
        public const float SpotMovingBonus = 9f;

        public const float VetPerSecond = 0.010f;
        public const float VetCap = 1.0f;

        /// <summary>At full veterancy, pin builds this fraction as fast.</summary>
        public const float VetPinResist = 0.45f;

        // --- called-in effects (§7's deck) ----------------------------------

        /// <summary>Ticks between salvos inside a barrage.</summary>
        public const int SalvoEvery = 24;

        public const float ShellRadius = 9f;
        public const float ShellPin = 0.85f;
        public const float TrapPin = 0.7f;

        // --- match length ---------------------------------------------------
        // §8 makes morale the clock, so a longer match is one where will lasts
        // longer, not one padded with a timer.

        public const float LengthSkirmish = 1.55f;
        public const float LengthStandard = 1.0f;
        public const float LengthSiege = 0.40f;
    }

    /// <summary>
    /// Per-kind cover characteristics. The kind decides these; they are not
    /// rolled independently of it.
    /// </summary>
    public static class CoverSpec
    {
        public static (float qLo, float qHi, float metresPerMan) For(CoverKind k) => k switch
        {
            CoverKind.Trench  => (0.58f, 0.74f, 1.8f),
            CoverKind.Sandbag => (0.48f, 0.64f, 2.1f),
            CoverKind.Bunker  => (0.70f, 0.82f, 3.4f),
            CoverKind.Crater  => (0.34f, 0.52f, 2.6f),
            _                 => (0.25f, 0.44f, 2.4f),
        };
    }
}
