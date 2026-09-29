using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    public enum Side { Us, Vc }

    public enum Posture { Standing, Crouched, Prone }

    /// <summary>What a squad is doing. The player picks from the same four.</summary>
    public enum Order { Advance, Hold, Bound, Fallback }

    /// <summary>Where the match is in its arc.</summary>
    public enum Phase { Opening, Fight, Over }

    /// <summary>
    /// §5's vocabulary of the ground, in the order a man would rather have it.
    ///
    /// The kind decides quality and capacity — it is not rolled beside them.
    /// In the previous build those were independent and a berm could shelter a
    /// squad better than a trench.
    /// </summary>
    public enum CoverKind { Trench, Sandbag, Bunker, Crater, Berm }

    /// <summary>A called-in effect: a circle on the map with a clock on it.</summary>
    public enum AreaKind { Barrage, Smoke, Trap }

    public sealed class Man
    {
        public int Id;
        public int Squad;
        public Side Side;
        public float X;
        public float Z;
        public bool Alive = true;

        /// <summary>
        /// Suppression, 0..1. Incoming fire builds it; it decays. Past
        /// <c>PinDrop</c> a man goes prone and stops advancing, past
        /// <c>PinStop</c> he mostly stops shooting.
        ///
        /// §8 calls this the core mechanic and it is deliberately the only
        /// thing in the simulation with no hit points behind it. A man is
        /// alive or dead; everything between is whether he can act.
        /// </summary>
        public float Pin;

        public Posture Posture = Posture.Standing;

        /// <summary>Ticks until this man may fire again.</summary>
        public int Cooldown;

        /// <summary>
        /// Recent X, for the "is moving" test.
        ///
        /// Moving means measured progress over a window, not motion this tick.
        /// The old 2D game's flag was true on any sub-pixel jitter, which
        /// bypassed every stance lock it had.
        /// </summary>
        public readonly List<float> Trail = new List<float>();

        /// <summary>Index into <see cref="SimState.Cover"/>, or -1.</summary>
        public int Cover = -1;

        public int Dwell;
        public int DiedAt = -1;

        /// <summary>
        /// Whether the other side knows he is there. Firing sets it, and so
        /// does being found at close range. Concealment is most of what the VC
        /// buy with their opening.
        /// </summary>
        public bool Seen;

        public float Veterancy;
    }

    public sealed class Squad
    {
        public int Id;
        public Side Side;
        public Order Order;

        /// <summary>The point the formation hangs off, recomputed every tick.</summary>
        public float AnchorX;
        public float AnchorZ;

        /// <summary>Which lane this squad works. Index into <see cref="Tune.Lanes"/>.</summary>
        public int Lane;

        /// <summary>Ticks since the squad last changed cover, for the ranged-in timer.</summary>
        public int Held;

        /// <summary>The cover the squad is making for, or -1.</summary>
        public int Target = -1;

        public bool Bounding;

        /// <summary>
        /// An order the player has given, which overrides the plan until
        /// cleared. Null means "the plan decides", which is every squad until
        /// a human touches it — so a headless match and a balance run are
        /// unaffected by this existing.
        ///
        /// Deliberately one of the four orders the simulation already models
        /// rather than a parallel command vocabulary: an order the AI could
        /// not also give would be a second set of rules to get wrong.
        /// </summary>
        public Order? PlayerOrder;
    }

    /// <summary>
    /// A piece of cover: a berm, a bank, a crater, a dug position.
    ///
    /// Capacity is limited and crowding costs, or every squad piles into the
    /// single best piece and the map stops mattering.
    ///
    /// <b>This is also what gets drawn.</b> In the previous build the
    /// simulation generated sixteen to twenty-two of these a match and not one
    /// was ever built in the scene: squads crossed forty metres under fire to
    /// take cover in visibly open grass. In this project a cover prefab
    /// registers itself and there is no second source of truth.
    /// </summary>
    public sealed class Cover
    {
        public int Id;
        public CoverKind Kind;
        public float X;
        public float Z;

        /// <summary>
        /// Extent along the lane, in metres.
        ///
        /// Cover is linear — a berm, a bank, a crater lip, a dug position —
        /// and a line of men gets behind it. Modelling it as a point with a
        /// 2.2 m radius sheltered one man of a squad spread thirteen metres
        /// along the lane: measured, 12% of the men of a side whose whole plan
        /// was taking cover were ever in any.
        /// </summary>
        public float Length;

        public int Capacity;

        /// <summary>Fraction of incoming fire it takes off.</summary>
        public float Quality;

        /// <summary>Ticks the enemy has had to range in on it.</summary>
        public int RangedIn;

        public Side? HeldBy;
    }

    public sealed class Area
    {
        public int Id;
        public AreaKind Kind;

        /// <summary>Who called it in. A trap only catches the other side.</summary>
        public Side Side;

        public float X;
        public float Z;
        public float Radius;

        /// <summary>Ticks left. A trap waits this long to be walked into.</summary>
        public int Ticks;

        /// <summary>Ticks until the next salvo. Barrage only.</summary>
        public int Next;

        public float Power;
    }

    public enum EventKind
    {
        // No 'hit'. A man is alive or dead, there are no wounds, so an event
        // for one could never fire — and an event that cannot happen does not
        // get declared. The old 2D game shipped a dead message, a dead timer
        // and a dead win condition.
        Fire, Kill, Pinned, Unpinned, FirstContact,
        CoverTaken, CoverLeft, RangedIn,
        SquadSpawned, SquadBroke, BoundStart,
        MoraleLost, MatchOver,
        Shell, AreaStart, AreaEnd, TrapSprung
    }

    public struct SimEvent
    {
        public EventKind Kind;
        public int Tick;
        public Side Side;

        /// <summary>Man or squad id, depending on the kind.</summary>
        public int Id;

        public float Amount;

        /// <summary>
        /// Who the shot was aimed at. Set on <see cref="EventKind.Fire"/>, so
        /// the view can draw the shot as a line between two men rather than as
        /// a flash in the dark. The simulation never reads it.
        /// </summary>
        public int Target;

        /// <summary>Where it happened, for the events that are about a place.</summary>
        public float X;
        public float Z;
    }

    public sealed class SimState
    {
        public int Tick;
        public Phase Phase = Phase.Opening;

        /// <summary>Tick the first shot went off, or -1.</summary>
        public int ContactTick = -1;

        /// <summary>
        /// Multiplier on morale drain, from the match-length setting.
        /// §8 makes morale the clock, so a longer match is one where will
        /// lasts longer — not one padded with a timer.
        /// </summary>
        public float MoraleScale = 1f;

        public readonly List<Man> Men = new List<Man>();
        public readonly List<Squad> Squads = new List<Squad>();
        public readonly List<Cover> Cover = new List<Cover>();
        public readonly List<Area> Areas = new List<Area>();
        public int AreaId;

        /// <summary>0..1. When a side's morale empties, that side breaks.</summary>
        public readonly Dictionary<Side, float> Morale = new Dictionary<Side, float>();

        /// <summary>Command points, which fund reinforcement.</summary>
        public readonly Dictionary<Side, float> Cp = new Dictionary<Side, float>();

        /// <summary>How far each side has pushed, in world X. Ground is a morale term.</summary>
        public readonly Dictionary<Side, float> Front = new Dictionary<Side, float>();

        /// <summary>
        /// Where each side's morale actually went.
        ///
        /// Balance without this is guesswork: "the defender loses" does not say
        /// whether he is being shot to pieces or bled by the ground he concedes.
        /// </summary>
        public readonly Dictionary<Side, float> MoraleLostToCasualties
            = new Dictionary<Side, float>();
        public readonly Dictionary<Side, float> MoraleLostToGround
            = new Dictionary<Side, float>();

        public readonly List<SimEvent> Events = new List<SimEvent>();

        public bool Over;
        public Side? Winner;

        /// <summary>Why it ended, so a win condition can be audited.</summary>
        public string Reason = "";
    }
}
