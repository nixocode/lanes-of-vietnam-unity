using System;
using System.Collections.Generic;
using System.Linq;

namespace LanesOfVietnam.Sim
{
    /// <summary>A squad that came through a battle: the card it was raised with, and each of its living men's veterancy.</summary>
    public sealed class Veteran
    {
        public string Card;
        public double[] Men;

        public double Mean => Men.Length == 0 ? 0 : Men.Average();
    }

    /// <summary>
    /// Where a campaign stands (PLAN §12.76): the battle next, each side's merit, perks and veterans, how many battles each
    /// side has won, and at what level it is fought. Plain data, so the view can keep it as JSON between sessions.
    /// </summary>
    [Serializable]
    public sealed class CampaignState
    {
        /// <summary>The battle to fight next, an index into Campaign.Run; Run.Length when the campaign is over.</summary>
        public int Battle;
        public Difficulty Level = Difficulty.Veteran;
        /// <summary>Merit not yet spent, [side].</summary>
        public int[] Merit = { Campaign.OpeningMerit, Campaign.OpeningMerit };
        /// <summary>Battles won, [side]: what opens the roster's later cards and how far the computer's doctrine has come.</summary>
        public int[] Won = { 0, 0 };
        /// <summary>Times the battle next has been fought and lost.</summary>
        public int Tries;
        public List<string> UsPerks = new List<string>(), VcPerks = new List<string>();
        public List<Veteran> UsVeterans = new List<Veteran>(), VcVeterans = new List<Veteran>();

        public List<string> Perks(Side s) => s == Side.Us ? UsPerks : VcPerks;
        public List<Veteran> Veterans(Side s) => s == Side.Us ? UsVeterans : VcVeterans;
        public bool Over => Battle >= Campaign.Run.Length;
    }

    /// <summary>
    /// The campaign (PLAN §12.57, built with Warfare 1944's in §12.76): the five operations in the order they were fought,
    /// each as the side it is written for. The owner's answers (2026-11-18): one campaign, the sides alternating as the
    /// operations do; a battle lost is fought again, keeping what has been bought; upgrades only here, and the computer
    /// earns its own; the squads that come through carry over.
    ///
    ///   merit      a battle won pays WinMerit, one lost LossMerit; a perk costs its tier, and a tier opens in a branch
    ///              when the branch has as many perks as the tier below is deep (none for the first, one for the second,
    ///              two for the third). Each side's merit is its own
    ///   roster     a side opens with its basic cards (Opening), and earns more by battles won (Earned) and by the
    ///              perks that open a card (Doctrine: the air strike, the guns, napalm; the tunnel, sappers, tripwires).
    ///              Never more than the operation's own deck
    ///   computer   its doctrine is as many perks of a fixed order (Computer) as the player has on his side: it earns
    ///              its own as he does, perk for perk, so a battle won makes the next one no easier
    ///   veterans   after a battle won, the squads still standing (up to VeteranCap, the steadiest first) wait in his
    ///              side's next battle; the first squad he raises with a veteran's card is that squad, its men as steady
    ///              as they were (Veterancy: steadier, never stronger), any it lost made up green. A battle lost leaves
    ///              them as they were before it
    ///
    /// Plain data and pure functions: the view keeps the state and plays the match (Options), and gives back the end
    /// (After). Nothing here runs in a match the game plays outside a campaign.
    /// </summary>
    public static class Campaign
    {
        public const int OpeningMerit = 2, WinMerit = 3, LossMerit = 1, VeteranCap = 6;

        /// <summary>The operations, in their order, each with the side the player fights it as.</summary>
        public static readonly (string Scenario, Side Side)[] Run =
        {
            ("firebase", Side.Us), ("the-wire", Side.Us), ("first-light", Side.Us), ("earth-fights", Side.Vc), ("rice-fields", Side.Vc),
        };

        /// <summary>The cards a side opens a campaign with.</summary>
        public static string[] Opening(Side s) => s == Side.Us
            ? new[] { "us-rifle", "us-weapons", "us-mg", "us-mortar", "us-smoke", "us-medevac", "us-frag" }
            : new[] { "vc-cell", "vc-squad", "vc-rpg", "vc-punji", "vc-spider", "vc-frag" };

        /// <summary>The cards a side earns by battles won: (battles, card).</summary>
        public static (int Won, string Card)[] Earned(Side s) => s == Side.Us
            ? new[] { (1, "us-sniper"), (2, "us-engineer") }
            : new[] { (1, "vc-marksman") };

        /// <summary>The order in which the computer's doctrine grows, a side's.</summary>
        public static string[] Computer(Side s) => s == Side.Us
            ? new[] { "us-marksmanship", "us-dig-in", "us-esprit", "us-sights", "us-fire-movement", "us-airmobile", "us-claymore", "us-m60", "us-hustle" }
            : new[] { "vc-fieldcraft", "vc-cadre", "vc-stick-grenades", "vc-b40", "vc-ambush", "vc-main-force", "vc-night", "vc-sharpshooters", "vc-stakes" };

        /// <summary>The scenario of the battle next.</summary>
        public static Scenario Next(CampaignState c) => c.Over ? null : Scenarios.ById(Run[c.Battle].Scenario);

        /// <summary>The side the player fights the battle next as.</summary>
        public static Side Playing(CampaignState c) => Run[Math.Min(c.Battle, Run.Length - 1)].Side;

        /// <summary>The cards a side has in a campaign now: its opening, what it has earned and what its perks have opened (not yet cut to an operation's deck).</summary>
        public static List<string> Roster(CampaignState c, Side s)
        {
            var have = new List<string>(Opening(s));
            foreach (var (won, card) in Earned(s)) if (c.Won[(int)s] >= won) have.Add(card);
            foreach (var id in c.Perks(s)) { var p = Doctrine.Find(id); if (p?.Unlocks != null && !have.Contains(p.Unlocks)) have.Add(p.Unlocks); }
            return have;
        }

        /// <summary>The perks the computer has against the player's side s: as many of its order as he has perks on s.</summary>
        public static string[] Against(CampaignState c, Side s)
        {
            var order = Computer(Combat.Other(s));
            return order.Take(Math.Min(order.Length, c.Perks(s).Count)).ToArray();
        }

        /// <summary>Why side s cannot buy perk p now, or null if it can.</summary>
        public static string Blocked(CampaignState c, Side s, Perk p)
        {
            if (p == null || p.Side != s) return "not this side's";
            var have = c.Perks(s);
            if (have.Contains(p.Id)) return "bought";
            int inBranch = have.Count(id => Doctrine.Find(id)?.Branch == p.Branch);
            if (inBranch < p.Tier - 1) return $"needs {p.Tier - 1} in {(p.Branch == Branch.Training ? "TRAINING" : "EQUIPMENT")}";
            if (c.Merit[(int)s] < p.Cost) return "not enough merit";
            return null;
        }

        /// <summary>Buy perk p for side s, if it can be. True if it was bought.</summary>
        public static bool Buy(CampaignState c, Side s, Perk p)
        {
            if (Blocked(c, s, p) != null) return false;
            c.Merit[(int)s] -= p.Cost;
            c.Perks(s).Add(p.Id);
            return true;
        }

        /// <summary>The match of the battle next, as the game plays it: the operation, the player's deck, both doctrines, his veterans.</summary>
        public static MatchOptions Options(CampaignState c, int seed)
        {
            var sc = Next(c) ?? throw new InvalidOperationException("the campaign is over");
            var side = Playing(c);
            var o = sc.Options(seed, side, c.Level);
            var roster = Roster(c, side);
            var deck = (side == Side.Us ? o.UsDeck : o.VcDeck) ?? Deck.For(side).Select(k => k.Id).ToArray();
            deck = deck.Where(roster.Contains).ToArray();
            if (side == Side.Us) o.UsDeck = deck; else o.VcDeck = deck;
            var mine = c.Perks(side).ToArray();
            var theirs = Against(c, side);
            o.UsPerks = side == Side.Us ? mine : theirs;
            o.VcPerks = side == Side.Us ? theirs : mine;
            var vets = c.Veterans(side).Select(v => new Veteran { Card = v.Card, Men = (double[])v.Men.Clone() }).ToList();
            if (side == Side.Us) o.UsVeterans = vets; else o.VcVeterans = vets;
            return o;
        }

        /// <summary>The squads of side s still standing at the end of a match, with the veterans it did not raise.</summary>
        public static List<Veteran> Survivors(SimState st, Side s)
        {
            var list = new List<Veteran>();
            foreach (var sq in st.Squads)
            {
                if (sq.Side != s || sq.Card == null) continue;
                var men = st.Men.Where(m => m.Squad == sq.Id && m.Alive).Select(m => m.Veterancy).ToArray();
                if (men.Length > 0) list.Add(new Veteran { Card = sq.Card, Men = men });
            }
            if (st.Veterans != null) list.AddRange(st.Veterans[(int)s]);
            return list;
        }

        /// <summary>
        /// The battle next has ended in match st: merit paid, and won, the veterans carried and the campaign on to the
        /// next; lost, it is to be fought again with the veterans it began with. True if it was won.
        /// </summary>
        public static bool After(CampaignState c, SimState st)
        {
            var side = Playing(c);
            bool won = st.Winner == side;
            c.Merit[(int)side] += won ? WinMerit : LossMerit;
            if (!won) { c.Tries++; return false; }
            c.Won[(int)side]++;
            var vets = c.Veterans(side);
            vets.Clear();
            vets.AddRange(Survivors(st, side).OrderByDescending(v => v.Mean).ThenByDescending(v => v.Men.Length).Take(VeteranCap));
            c.Battle++;
            c.Tries = 0;
            return true;
        }

        /// <summary>
        /// Match.SpawnSquad: a squad of side raised with card takes the first veteran of that card waiting, if one is:
        /// its men get their veterancy back, rank by rank, and the veteran is no longer waiting.
        /// </summary>
        internal static void Muster(SimState st, Side side, string card, int firstMan, int count)
        {
            var wait = st.Veterans[(int)side];
            int k = wait.FindIndex(v => v.Card == card);
            if (k < 0) return;
            var v = wait[k];
            wait.RemoveAt(k);
            for (int i = 0; i < count && i < v.Men.Length; i++) st.Men[firstMan + i].Veterancy = v.Men[i];
            st.Events.Add(new SimEvent { Kind = EventKind.VeteransJoin, Tick = st.Tick, Side = side, Id = st.Men[firstMan].Squad, Amount = v.Mean });
        }
    }
}
