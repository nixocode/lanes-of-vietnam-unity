using System.Collections.Generic;
using System.Linq;
using LanesOfVietnam.Sim;

namespace LanesOfVietnam.View.UI
{
    /// <summary>
    /// What a card buys, in a line, for the HUD's hint and the squad tags: a
    /// card's face says RIFLE SQUAD and 18, and nothing of who is in it, what
    /// they carry or where they fight from. The squads' lines are read off
    /// the simulation's own kits (<see cref="Arms.For"/>), so they cannot say
    /// something the squad is not.
    /// </summary>
    public static class CardText
    {
        private static readonly Dictionary<string, string> Calls = new Dictionary<string, string>
        {
            ["us-arty"] = "A BATTERY ON THE SPOT FOR NINE SECONDS: WIDE, LONG, AND IT PINS MORE THAN IT KILLS",
            ["us-airstrike"] = "ONE PASS: TIGHT, FOUR SECONDS, AND IT KILLS WHAT IS UNDER IT",
            ["us-smoke"] = "A SCREEN FOR TWENTY SECONDS: NOBODY SHOOTS THROUGH IT, EITHER WAY",
            ["us-medevac"] = "STEADIES THE LINE: WILL BACK, AND EVERY MAN OF YOURS LESS PINNED",
            ["vc-punji"] = "A PIT IN THEIR PATH: THE FIRST MAN IN IT, AND EVERYONE NEAR HIM GOES FLAT",
            ["vc-tripwire"] = "A WIRE ACROSS THEIR PATH: WIDER AND SURER THAN A PIT",
            ["vc-spider"] = "ONE MAN IN A HOLE: HE TAKES THE FIRST WHO STEPS ON HIM",
            ["vc-tunnel"] = "A SQUAD COMES UP OUT OF THE GROUND WHERE YOU POINT, NOT AT YOUR END OF THE LANE",
        };

        private static string Name(Weapon w, Side side) => w switch
        {
            Weapon.M16 => "M16", Weapon.Ak => "AK-47", Weapon.Sks => "SKS", Weapon.M60 => "M60", Weapon.Rpd => "RPD",
            Weapon.Smg => side == Side.Us ? "M3" : "PPSH", Weapon.Sniper => side == Side.Us ? "M40" : "MOSIN",
            Weapon.M79 => "M79", Weapon.Rpg => "RPG-7", Weapon.Mortar => "MORTAR", _ => "RIFLE",
        };

        /// <summary>The men and what they carry: "5 MEN: 4 M16, M60".</summary>
        public static string Men(Card card, Side side)
        {
            var kit = Arms.For(card.Id);
            if (kit == null) return card.Pips > 0 ? $"{card.Pips} MEN" : "";
            var counts = kit.Men.GroupBy(w => w).OrderByDescending(g => g.Count())
                .Select(g => g.Count() > 1 ? $"{g.Count()} {Name(g.Key, side)}" : Name(g.Key, side));
            return $"{kit.Men.Length} {(kit.Men.Length == 1 ? "MAN" : "MEN")}: {string.Join(", ", counts)}";
        }

        /// <summary>The whole line for a card: who, with what, from how far, and whether they go in.</summary>
        public static string Line(Card card, Side side)
        {
            if (card.Group == CardGroup.Call) return Calls.TryGetValue(card.Id, out var t) ? t : "";
            var kit = Arms.For(card.Id);
            if (kit == null) return Men(card, side);
            string how = kit.Assaults ? "CLOSES AND GOES IN" : "STAYS BACK AND SHOOTS";
            return $"{Men(card, side)} · FIGHTS FROM {kit.Reach:0} M · {how}";
        }

        /// <summary>What a squad is called: its card's name.</summary>
        public static string SquadName(Squad sq)
        {
            var card = sq.Card != null ? Deck.Find(sq.Side, sq.Card) : null;
            return card != null ? card.Name : sq.Side == Side.Us ? "SQUAD" : "CELL";
        }
    }
}
