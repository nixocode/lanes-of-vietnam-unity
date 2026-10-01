using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    // Everything here is plain data. The simulation owns no engine objects, no
    // time source and no global randomness: it advances by whole ticks and
    // takes its randomness from a seeded Rng, so a match is a pure function of
    // (seed, plan, orders) and runs headless in milliseconds.
    //
    // Positions and every quantity derived from them are double, not float.
    // The TypeScript original computes in double, and the balance numbers in
    // Tune were measured there over 48 seeds; they carry across only if a
    // seed plays out as the same match, which float arithmetic cannot do —
    // it rounds every position differently and the two diverge within a few
    // ticks. The view converts to float at the boundary.

    public enum Side { Us = 0, Vc = 1 }

    public enum Posture { Standing, Crouched, Prone }

    /// <summary>What a squad is doing. The player picks from the same four.</summary>
    public enum Order { Advance, Hold, Bound, Fallback }

    /// <summary>
    /// Where the match is in its arc: brief §8's "authored opening and a real
    /// ending in both directions".
    ///
    /// The opening is the infiltration — the VC move up through the grass
    /// concealed and holding fire, the Americans sit in position — and the
    /// match proper starts the moment one side finds the other. Every
    /// mechanic in it is one the fight already uses, so it is a beat rather
    /// than a mode with its own rules to get wrong (§9 finding 4).
    /// </summary>
    public enum Phase { Opening, Fight, Over }

    /// <summary>
    /// §5's vocabulary of the ground, in the order a man would rather have it.
    ///
    /// The kind decides quality and capacity — it is not rolled beside them.
    /// Rolled independently, a berm could shelter a squad better than a
    /// trench.
    /// </summary>
    public enum CoverKind { Trench, Sandbag, Bunker, Crater, Berm }

    /// <summary>
    /// The standing order on a position, per side (MatchOptions.Fieldcraft):
    /// the lever over each trench in Warfare 1944. Auto leaves it to the plan,
    /// which is where every position starts and where a plan-driven side
    /// leaves it; Hold keeps the men who reach it in it; Go sends them out of
    /// it and lets the next squad pass through.
    /// </summary>
    public enum Lever { Auto, Hold, Go }

    /// <summary>A called-in effect: a circle on the map with a clock on it.</summary>
    public enum AreaKind { Barrage, Smoke, Trap }

    public sealed class Man
    {
        public int Id;
        public int Squad;
        public Side Side;
        public double X;
        public double Z;
        public bool Alive = true;

        /// <summary>
        /// Suppression, 0..1. Incoming fire builds it; it decays. Past
        /// <see cref="Tune.PinDrop"/> a man drops and stops advancing, past
        /// <see cref="Tune.PinStop"/> he mostly stops shooting.
        ///
        /// §8 calls this the core mechanic and it is deliberately the only
        /// thing in the simulation with no hit points behind it. A man is
        /// alive or dead; everything between is whether he can act.
        /// </summary>
        public double Pin;

        /// <summary>
        /// Simulation state, not animation state: it changes hit chance,
        /// movement speed and how fast pin decays.
        /// </summary>
        public Posture Posture = Posture.Standing;

        /// <summary>Ticks until this man may fire again.</summary>
        public int Cooldown;

        /// <summary>
        /// Recent X, for the "is moving" test.
        ///
        /// Moving means measured progress over a window, not motion this tick
        /// (§9 finding 2). The 2D game's flag was true on any sub-pixel
        /// jitter, which bypassed every stance lock it had.
        /// </summary>
        public readonly List<double> Trail = new List<double>(Tune.MoveWindow + 1);

        /// <summary>Index into <see cref="SimState.Cover"/>, or -1.</summary>
        public int Cover = -1;

        /// <summary>Ticks in the current posture. §9 finding 3: dwell.</summary>
        public int Dwell;

        /// <summary>
        /// Tick this man was killed, or -1. The dead stay in the list; the
        /// renderer needs <i>when</i> to play a fall rather than a vanishing.
        /// </summary>
        public int DiedAt = -1;

        /// <summary>
        /// Whether the other side knows he is there. Firing sets it, and so
        /// does being found at close range. Concealment is most of what the VC
        /// buy with their opening.
        /// </summary>
        public bool Seen;

        /// <summary>0..1. Veterancy makes a man steadier, never stronger.</summary>
        public double Veterancy;

        /// <summary>Grenades left (MatchOptions.Frag). Two, as a rifleman carried.</summary>
        public int Grenades = Tune.GrenadesCarried;

        /// <summary>Ticks until he may throw again (MatchOptions.Frag).</summary>
        public int FragCooldown;

        // --- MatchOptions.Fieldcraft ---------------------------------------------
        /// <summary>His place in the squad's file, 0 the lead man. Closes up when a man ahead falls.</summary>
        public int Rank;
        /// <summary>The place he has taken in a piece of cover (<see cref="Fieldcraft.PlaceOf"/>), and which; -1 for none.</summary>
        public int Place = -1;
        public int PlaceCover = -1;
        /// <summary>Ticks left climbing into or out of a trench: he neither moves nor fires.</summary>
        public int Vault;

        /// <summary>What he carries (MatchOptions.Arms; the baseline's one rifle otherwise).</summary>
        public Weapon Weapon;

        /// <summary>At rest in his place: he sets off again only when it has moved a good pace from him (MatchOptions.Fieldcraft).</summary>
        public bool Still;
    }

    /// <summary>A grenade in the air or on the ground, fuse burning (MatchOptions.Frag).</summary>
    public sealed class Grenade
    {
        public int Id;
        public Side Side;
        public int Thrower;
        /// <summary>Where it came down, where it will go off.</summary>
        public double X, Z;
        /// <summary>Ticks until it detonates.</summary>
        public int Ticks;
        /// <summary>Its burst: a hand grenade's unless a launcher or a mortar fired it (MatchOptions.Arms).</summary>
        public double Radius = Tune.FragRadius, Kill = Tune.FragKill, CoverFactor = Tune.FragCoverFactor;
        public Weapon By;
    }

    public sealed class Squad
    {
        public int Id;
        public Side Side;
        public Order Order;

        /// <summary>Smoke grenades the squad still has (MatchOptions.SquadSmoke).</summary>
        public int Smoke = Tune.SquadSmokeCarried;

        /// <summary>The tick its order last changed, and whether it broke and has steadied (MatchOptions.Drill).</summary>
        public int OrderSince;
        public bool Rallied;

        /// <summary>
        /// The point the formation hangs off. Persistent state that orders
        /// move (<see cref="Squads.March"/>), leashed to the living men every
        /// tick (<see cref="Squads.Reanchor"/>) — never snapped to them.
        /// </summary>
        public double AnchorX;
        public double AnchorZ;

        /// <summary>Which lane this squad works. Index into <see cref="Tune.Lanes"/>.</summary>
        public int Lane;

        /// <summary>Ticks since the squad last changed cover.</summary>
        public int Held;

        /// <summary>How far from an enemy in sight it fights, and whether it closes with him when he is beaten down (MatchOptions.Arms; its card's kit).</summary>
        public double Reach = Tune.StandOff;
        public bool Assaults = true;

        /// <summary>Which of the lane's three files the squad walks in, so two squads are never one column (MatchOptions.Fieldcraft).</summary>
        public int File;
        /// <summary>Stopped short of the enemy this tick, or in its cover: its men kneel and fight (MatchOptions.Fieldcraft).</summary>
        public bool Halted;
        /// <summary>Closing with the enemy this tick (MatchOptions.Fieldcraft).</summary>
        public bool Assault;
        /// <summary>Ticks its decision to assault, and to close to its fighting distance, still stands: made once, not every tick (MatchOptions.Fieldcraft).</summary>
        public int Charge, Closing;
        /// <summary>Metres along the lane to the nearest enemy it can see ahead, this tick; huge if none (MatchOptions.Fieldcraft).</summary>
        public double Gap = double.PositiveInfinity;
        /// <summary>Ticks left of being sent out of a position by its lever; it ends when the squad reaches its next cover (MatchOptions.Fieldcraft).</summary>
        public int Sent;

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
    /// <b>This is also what gets drawn.</b> In the three.js build the
    /// simulation generated sixteen to twenty-two of these a match and not
    /// one was ever built in the scene: squads crossed forty metres under fire
    /// to take cover in visibly open grass. Here the map's cover list is the
    /// single source of truth — the scene's cover geometry is generated from
    /// it, and a test holds the two together.
    /// </summary>
    public sealed class Cover
    {
        public int Id;
        public CoverKind Kind;

        /// <summary>Centre along the lane.</summary>
        public double X;
        public double Z;

        /// <summary>
        /// Extent along the lane, in metres.
        ///
        /// Cover is linear — a berm, a bank, a crater lip, a dug position —
        /// and a line of men gets behind it. Modelled as a point with a 2.2 m
        /// radius it sheltered one man of a squad spread thirteen metres along
        /// the lane: measured, 12% of the men of a side whose whole plan was
        /// taking cover were ever in any.
        /// </summary>
        public double Length;

        public int Capacity;

        /// <summary>Fraction of incoming fire it takes off, before crowding.</summary>
        public double Quality;

        /// <summary>
        /// Rises by <c>Dt / RangeInSeconds</c> while a side sits in it; at 1
        /// the enemy has ranged it in. A fraction, not a tick count — the last
        /// port had it as an int, which could never climb past zero.
        /// </summary>
        public double RangedIn;

        public Side? HeldBy;

        /// <summary>Each side's standing order on it (MatchOptions.Fieldcraft; positions only).</summary>
        public Lever LeverUs, LeverVc;
        /// <summary>Whose position it is: the last side to have it to itself (MatchOptions.Fieldcraft).</summary>
        public Side? Owner;

        public Cover Clone() => (Cover)MemberwiseClone();
    }

    public sealed class Area
    {
        public int Id;
        public AreaKind Kind;

        /// <summary>Who called it in. A trap only catches the other side.</summary>
        public Side Side;

        public double X;
        public double Z;
        public double Radius;

        /// <summary>Ticks left. A trap waits this long to be walked into.</summary>
        public int Ticks;

        /// <summary>Ticks until the next salvo. Barrage only.</summary>
        public int Next;

        /// <summary>How hard it hits. Barrage and trap.</summary>
        public double Power;
    }

    public enum EventKind
    {
        // No 'hit'. A man is alive or dead, there are no wounds, so an event
        // for one could never fire — and an event that cannot happen does not
        // get declared. The 2D game shipped a dead message, a dead timer and a
        // dead win condition (§9 finding 5).
        Fire, Kill, Pinned, Unpinned, FirstContact,
        CoverTaken, CoverLeft, RangedIn,
        SquadSpawned, SquadBroke, BoundStart,
        MoraleLost, MatchOver,
        // Called-in effects. Shell is one detonation, not one barrage: the
        // renderer needs a position per burst and a barrage is a dozen.
        Shell, AreaStart, AreaEnd, TrapSprung,
        // Part 2, behind MatchOptions.Frag (PLAN §12.8). Appended, so every
        // earlier kind keeps its number.
        GrenadeThrown, GrenadeBlast,
        // MatchOptions.Fieldcraft. Melee: Id struck at Target, hand to hand.
        // Vault: a man (Id) climbing into or out of a trench (X, Z: where).
        // PositionTaken: cover Id is now Side's.
        Melee, VaultIn, VaultOut, PositionTaken,
        // Launch (MatchOptions.Arms): man Id fired a bursting round (an M79, a
        // rocket, a mortar bomb) that comes down at X, Z in Amount ticks. Its
        // burst is a GrenadeBlast, as a thrown grenade's is.
        // Through: the round that killed man Id went on into man Target
        // (Amount 1 if it killed him too). The view throws the first man's
        // blood out along it.
        Through, Launch,
    }

    public struct SimEvent
    {
        public EventKind Kind;
        public int Tick;
        public Side Side;

        /// <summary>Man, squad, cover or area id, depending on the kind.</summary>
        public int Id;

        /// <summary>Magnitude where there is one: morale lost.</summary>
        public double? Amount;

        /// <summary>
        /// Who the shot was aimed at. Set on <see cref="EventKind.Fire"/> so
        /// the view can draw a shot between two men rather than a flash in the
        /// dark. The simulation never reads it.
        /// </summary>
        public int? Target;

        /// <summary>Where it happened, for the events that are about a place.</summary>
        public double? X;
        public double? Z;
    }

    public sealed class SimState
    {
        public int Tick;
        public Phase Phase = Phase.Opening;

        /// <summary>Tick the first shot went off, or -1.</summary>
        public int ContactTick = -1;

        /// <summary>
        /// Multiplier on morale drain, from the match-length setting. §8 makes
        /// morale the clock, so a longer match is one where will lasts longer
        /// — not one padded with a timer.
        /// </summary>
        public double MoraleScale = 1;

        public readonly List<Man> Men = new List<Man>();
        public readonly List<Squad> Squads = new List<Squad>();
        public readonly List<Cover> Cover = new List<Cover>();
        public readonly List<Area> Areas = new List<Area>();

        /// <summary>
        /// Part 2 rule: men throw grenades at enemies in cover (PLAN §12.8,
        /// "frag: a close assault breaks a position that small arms cannot").
        /// Off is the parity baseline: with it off, nothing below is read.
        /// </summary>
        public bool Frag;
        /// <summary>Part 2 rule: a squad pinned down on the move pops smoke (PLAN §12.8). Off is the baseline.</summary>
        public bool SquadSmoke;
        /// <summary>Part 2 rule: orders that stand, broken squads that hold, men who keep their places (<see cref="Sim.Drill"/>). Off is the baseline.</summary>
        public bool Drill;
        /// <summary>Part 2 rule: men who fight as a squad — files, places in cover, a stand-off, the assault, melee, levers (<see cref="Sim.Fieldcraft"/>). Off is the baseline.</summary>
        public bool Fieldcraft;
        /// <summary>Part 2 rule: every man carries a weapon, every card buys its squad, every fight is at its weapons' distance (<see cref="Sim.Arms"/>). Off is the baseline.</summary>
        public bool Arms;
        /// <summary>Command points a second, each side (MatchOptions.CpRate), and what a squad costs a plan that raises its own (MatchOptions.MusterCost).</summary>
        public double CpRate = Tune.CpPerSecond, MusterCost = Tune.CpPerSquad;
        /// <summary>The side whose reinforcements are its player's to buy: its plan raises none for it (MatchOptions.Player).</summary>
        public Side? Player;
        /// <summary>The grenades' own random stream, so the rule draws nothing from the sim's.</summary>
        public Rng FragRng;
        public readonly List<Grenade> Grenades = new List<Grenade>();
        public int GrenadeId;

        /// <summary>Next area id. Monotonic, so an id is never reused while one is on screen.</summary>
        public int AreaId;

        /// <summary>
        /// Id counters. Ids are list indices — a man's id is his index in
        /// <see cref="Men"/>, a squad's in <see cref="Squads"/> — because
        /// nothing is ever removed, only marked dead.
        /// </summary>
        public int NextManId;
        public int NextSquadId;

        /// <summary>0..1, per side. When a side's morale empties, that side breaks.</summary>
        public readonly double[] Morale = { 1, 1 };

        /// <summary>Command points, per side, which fund reinforcement and the deck.</summary>
        public readonly double[] Cp = { 0, 0 };

        /// <summary>
        /// Ticks until each card can be bought again, per side, indexed by
        /// <see cref="Card.Index"/>. In the state rather than the UI so a
        /// headless match plays the same economy the player does.
        /// </summary>
        public readonly int[][] CardCooldown = { new int[Deck.Us.Length], new int[Deck.Vc.Length] };

        /// <summary>How far each side has pushed, in world X. Ground is a morale term.</summary>
        public readonly double[] Front = { 0, 0 };

        /// <summary>
        /// Where each side's morale actually went. Balance without this is
        /// guesswork: "the defender loses" does not say whether he is being
        /// shot to pieces or bled by ground, and those want opposite fixes.
        /// </summary>
        public readonly double[] MoraleLostToCasualties = { 0, 0 };
        public readonly double[] MoraleLostToGround = { 0, 0 };

        public readonly List<SimEvent> Events = new List<SimEvent>();

        public bool Over;
        public Side? Winner;

        /// <summary>Why it ended, so a win condition can be audited (§9 finding 4).</summary>
        public string Reason = "";
    }
}
