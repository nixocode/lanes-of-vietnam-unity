using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>How hard the computer is: what a squad costs it (<see cref="Scenario.CostFor"/>).</summary>
    public enum Difficulty { Recruit, Veteran, Elite }

    /// <summary>
    /// A scenario, as data (PLAN §12.27, S2): something to play. What the
    /// player is told (its title, its date line, the briefing, his orders),
    /// the map it is fought on, how it is won and against what clock, which
    /// side comes on and which side is his, and what each side has that the
    /// other has not: the men it opens with, its points and its income, how
    /// long its will lasts, its cards.
    ///
    /// The game had one match, written inline where it was started
    /// (GameRoot.NewMatch): side, length and difficulty off the start screen,
    /// and every rule switched on there. <see cref="Options"/> gives that match
    /// now, for any scenario, and the headless tool builds the same one from
    /// the same place (<c>simcs scenario</c>), so what is measured is what is
    /// played.
    ///
    /// Weather and time of day are named here and do nothing yet: the rules
    /// they belong to come with the maps that need them (PLAN §12.27, Phase M).
    /// </summary>
    public sealed class Scenario
    {
        public string Id, Title, DateLine, Briefing;
        /// <summary>The player's orders, a line each: what he has to do to win, and what loses it.</summary>
        public string[] Orders = new string[0];

        public MapDef Map = Maps.Firebase;
        public MatchMode Mode;
        /// <summary>A siege's or an assault's clock, in seconds.</summary>
        public double ClockSeconds;
        /// <summary>The side that comes on. In a siege or an assault the other holds.</summary>
        public Side Attacker = Side.Vc;
        /// <summary>An assault's ground: the defender's position this many back from his last, in each lane (0: his last).</summary>
        public int LineBack;

        /// <summary>The side the player takes. Null: his to choose.</summary>
        public Side? Plays;
        /// <summary>How long will lasts. Null: his to choose on the start screen (a standard match); a clock is its own length.</summary>
        public MatchLength? Length;

        /// <summary>What each side has that the other has not.</summary>
        public Means Us = new Means(), Vc = new Means();
        /// <summary>Each side's cards, by id. Null: all of its side's.</summary>
        public string[] UsDeck, VcDeck;

        /// <summary>Named, and nothing yet: the rules come with the maps that need them.</summary>
        public string Weather = "", TimeOfDay = "";

        public bool Timed => Mode != MatchMode.Standard;
        public Side Defender => Combat.Other(Attacker);

        // --- the game's dials ---------------------------------------------------------
        // The numbers the game is played at. They are kept here so the headless tool measures with the
        // ones the game uses; what each was measured at, and when it moved, is written where the game
        // names them (GameRoot.CostFor, GameRoot.RateFor).

        /// <summary>Command points a second and at the start, and the men each side opens with (at least).</summary>
        public const double CpRate = 1.6, StartCp = 20;
        public const int OpeningStrength = 4;

        /// <summary>What a squad costs the computer.</summary>
        public static double CostFor(Difficulty d) => d == Difficulty.Recruit ? 44 : d == Difficulty.Elite ? 29 : 35;

        /// <summary>How fast will runs out, against the length setting's own rate.</summary>
        public static double RateFor(MatchLength l) => l == MatchLength.Skirmish ? 1.25 : l == MatchLength.Long ? 1.5 : 0.95;

        /// <summary>
        /// The match: this scenario, on this seed, with the player on <paramref name="player"/> (null: nobody
        /// plays, and both sides raise their own) against a computer of this level. Every Part 2 rule is on, as
        /// the game has them. <paramref name="length"/> is the start screen's, used where the scenario leaves it
        /// to him.
        /// </summary>
        public MatchOptions Options(int seed, Side? player, Difficulty level, MatchLength length = MatchLength.Standard)
        {
            var len = Length ?? length;
            var o = new MatchOptions
            {
                Seed = seed, Map = Map, Cover = Map.Cover(), Length = len,
                Frag = true, SquadSmoke = true, Drill = true, Fieldcraft = true, Arms = true, Senses = true,
                Gunnery = true, Ammo = true, Tactics = true, Fortune = true, FireControl = true, Ballistics = true,
                Vantage = true, Commander = true, Cohesion = true, Bearing = true, Inbound = true, FragCard = true,
                CpRate = CpRate, StartCp = StartCp, OpeningStrength = OpeningStrength,
                MusterCost = CostFor(level), MoraleRate = RateFor(len),
                Player = player,
                UsMeans = Us, VcMeans = Vc, UsDeck = UsDeck, VcDeck = VcDeck,
                Mode = Mode, Attacker = Attacker, ClockSeconds = Timed ? ClockSeconds : 0,
                // Its role. In a standard match both sides take ground, as they always have. Against a clock
                // the side that comes on takes it and the other holds his: his squads go to cover and stay in it.
                Us = Role(Side.Us), Vc = Role(Side.Vc),
            };
            if (Mode == MatchMode.Assault) o.Objectives = Match.LastLine(o.Cover, Defender, LineBack);
            return o;
        }

        private Plan Role(Side side) => Timed && side != Attacker ? Plan.Defend : Plan.Ceiling;

        /// <summary>May the player take this side in it?</summary>
        public bool Offers(Side side) => Plays == null || Plays == side;
    }

    /// <summary>
    /// The scenarios there are (PLAN §12.27, S6): the firebase as it always was, and three more on the same
    /// ground. Each one's numbers were measured with <c>simcs scenario</c> against the player who buys a line
    /// squad whenever he can afford one and does nothing else: what he won is written by each (of 48 seeds
    /// when they were made, PLAN §12.39; of 192 since Bearing, PLAN §12.41, which moved every one of them).
    /// </summary>
    public static class Scenarios
    {
        /// <summary>
        /// The match the game had before there were scenarios: either side, any length, both sides alike.
        /// (`simcs scenario firebase 192`: as the Americans 170, 125 and 96 of 192 against a Recruit, a Veteran and
        /// the Elite; as the VC 139, 111 and 76.)
        /// </summary>
        public static readonly Scenario Firebase = new Scenario
        {
            Id = "firebase", Title = "THE FIREBASE", DateLine = "CENTRAL HIGHLANDS · 1965",
            Briefing = "A firebase on a red-dirt track, and the jungle that wants it back. Two lanes: the perimeter, dug in and built up, and the track verge under the treeline, where the only cover is what the fighting left.\n\nNo clock but the men's own will. Break theirs before they break yours.",
            Orders = new[] { "Break the enemy's will to fight", "Hold what you take: ground lost costs will", "Follow your field orders at the top of the screen" },
        };

        /// <summary>
        /// A siege, as the Americans: five minutes to hold against an enemy who opens with half as many men
        /// again and is paid half as much again. Both sides' will lasts longer than in a standard match, so
        /// it is the clock that decides. (`simcs scenario the-wire 192`: 173, 141 and 119 of 192.)
        /// </summary>
        public static readonly Scenario TheWire = new Scenario
        {
            Id = "the-wire", Title = "THE WIRE", DateLine = "CENTRAL HIGHLANDS · OCTOBER 1965 · PLAYING: US INFANTRY",
            Briefing = "They came out of the treeline an hour before dawn, and they have not stopped coming. A main-force battalion against one company behind sandbags and a single strand of wire.\n\nThe relief column is on the track and five minutes out. You do not have to beat them. You have to be here when it arrives.",
            Orders = new[] { "Hold until the clock runs out", "If your will breaks first, the firebase falls", "They have the numbers: make every position cost them" },
            Mode = MatchMode.Siege, ClockSeconds = 300, Attacker = Side.Vc, Plays = Side.Us, Length = MatchLength.Standard,
            Us = new Means { Opening = 8, Will = 0.6 },
            Vc = new Means { Opening = 12, Income = 1.5, Will = 0.6 },
        };

        /// <summary>
        /// An assault, as the Americans: seven minutes to take the line in front of the enemy's last, the
        /// trench in the far lane and the berm in the near, and have both at once. They open dug in; the
        /// Americans open with three squads and are paid more. (`simcs scenario first-light 192`: 181, 137 and 117
        /// of 192; against a Veteran 68 by taking the line, 69 by breaking him or leaving him no men.)
        /// </summary>
        public static readonly Scenario FirstLight = new Scenario
        {
            Id = "first-light", Title = "FIRST LIGHT", DateLine = "CENTRAL HIGHLANDS · NOVEMBER 1965 · PLAYING: US INFANTRY",
            Briefing = "The patrols found where the mortars are coming from: a line of fighting positions dug into the treeline, eight hundred metres up the track. Battalion wants it gone before the sun is over the ridge.\n\nThey are dug in and they know you are coming. Take their trench and the berm beside it, and hold both. A line with one end still in their hands is not taken.",
            Orders = new[] { "Take the trench in the far lane and the berm in the near lane: hold BOTH at once", "Or break their will", "When the clock runs out the attack is called off" },
            Mode = MatchMode.Assault, ClockSeconds = 420, Attacker = Side.Us, LineBack = 1, Plays = Side.Us, Length = MatchLength.Standard,
            Us = new Means { Opening = 12, Income = 1.4, Will = 0.5 },
            Vc = new Means { Opening = 8, Income = 0.9, Will = 0.5 },
        };

        /// <summary>
        /// A standard match, as the VC, against an enemy with more of everything: the Americans are paid 1.15
        /// to the VC's 1 (a fifth more until Bearing, PLAN §12.41: at 1.2 the VC player won 108 of 192 against a
        /// Veteran). The VC open with twice the points, for the ground, and their will lasts longer.
        /// (`simcs scenario earth-fights 192`: 141, 125 and 71 of 192.)
        /// </summary>
        public static readonly Scenario EarthFights = new Scenario
        {
            Id = "earth-fights", Title = "THE EARTH FIGHTS", DateLine = "CENTRAL HIGHLANDS · DECEMBER 1965 · PLAYING: LOCAL FORCE VC",
            Briefing = "The Americans have built their firebase on ground your people have farmed, dug and fought over for twenty years. They have artillery on call and aircraft overhead, and more of everything except one thing: they do not know where you are.\n\nYou have no guns to answer theirs. You have the ground. Let them walk onto it.",
            Orders = new[] { "Break the Americans' will", "Lay traps ahead of their advance [Q] [W] [E]", "Dig a tunnel [R] to come up where they do not expect you" },
            Attacker = Side.Vc, Plays = Side.Vc, Length = MatchLength.Standard,
            Us = new Means { Income = 1.15 },
            Vc = new Means { Purse = 2, Will = 0.65 },
        };

        public static readonly Scenario[] All = { Firebase, TheWire, FirstLight, EarthFights };

        /// <summary>The scenario of that name, or null.</summary>
        public static Scenario ById(string id)
        {
            foreach (var s in All) if (s.Id == id) return s;
            return null;
        }
    }
}
