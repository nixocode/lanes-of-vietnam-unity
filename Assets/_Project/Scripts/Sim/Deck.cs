using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    public enum CardGroup { Line, Support, Special, Call }

    /// <summary>
    /// A card on §7's deck.
    ///
    /// "The two sides do not share a toolkit and the UI must show that. The US
    /// buys fire support; the VC buys traps, tunnels and infiltration. This
    /// asymmetry is the game's best idea — protect it." So the catalogue is
    /// per side and there is deliberately no shared list to fall back on.
    /// </summary>
    public sealed class Card
    {
        public string Id;
        public CardGroup Group;
        public string Name;
        public int Cost;
        /// <summary>Men in the squad this buys, for the composition pips. 0 for a call-in.</summary>
        public int Pips;
        /// <summary>Ticks before it can be bought again.</summary>
        public int Cooldown;
        /// <summary>Position in its side's deck; indexes <see cref="SimState.CardCooldown"/>.</summary>
        public int Index;
    }

    public static class Deck
    {
        private const int S = Tune.TickHz;

        public static readonly Card[] Us = Index(new[]
        {
            new Card { Id = "us-rifle", Group = CardGroup.Line, Name = "RIFLE SQUAD", Cost = 18, Pips = 5, Cooldown = S * 8 },
            new Card { Id = "us-weapons", Group = CardGroup.Line, Name = "WEAPONS SQUAD", Cost = 24, Pips = 4, Cooldown = S * 12 },
            new Card { Id = "us-mg", Group = CardGroup.Support, Name = "M60 TEAM", Cost = 22, Pips = 3, Cooldown = S * 12 },
            new Card { Id = "us-mortar", Group = CardGroup.Support, Name = "MORTAR TEAM", Cost = 28, Pips = 3, Cooldown = S * 16 },
            new Card { Id = "us-engineer", Group = CardGroup.Special, Name = "ENGINEERS", Cost = 26, Pips = 4, Cooldown = S * 16 },
            // The US toolkit is fire support: things that arrive from off the map.
            new Card { Id = "us-arty", Group = CardGroup.Call, Name = "ARTILLERY", Cost = 30, Pips = 0, Cooldown = S * 30 },
            new Card { Id = "us-smoke", Group = CardGroup.Call, Name = "SMOKE", Cost = 10, Pips = 0, Cooldown = S * 14 },
            new Card { Id = "us-medevac", Group = CardGroup.Call, Name = "MEDEVAC", Cost = 16, Pips = 0, Cooldown = S * 24 },
            new Card { Id = "us-airstrike", Group = CardGroup.Call, Name = "AIR STRIKE", Cost = 40, Pips = 0, Cooldown = S * 45 },
        });

        public static readonly Card[] Vc = Index(new[]
        {
            new Card { Id = "vc-cell", Group = CardGroup.Line, Name = "GUERRILLA CELL", Cost = 14, Pips = 4, Cooldown = S * 8 },
            new Card { Id = "vc-squad", Group = CardGroup.Line, Name = "NVA SQUAD", Cost = 20, Pips = 5, Cooldown = S * 12 },
            new Card { Id = "vc-rpg", Group = CardGroup.Support, Name = "RPG TEAM", Cost = 22, Pips = 2, Cooldown = S * 14 },
            new Card { Id = "vc-marksman", Group = CardGroup.Support, Name = "MARKSMAN", Cost = 18, Pips = 1, Cooldown = S * 16 },
            new Card { Id = "vc-sapper", Group = CardGroup.Special, Name = "SAPPER", Cost = 24, Pips = 3, Cooldown = S * 18 },
            // The VC toolkit is the ground itself: traps, tunnels, infiltration.
            new Card { Id = "vc-punji", Group = CardGroup.Call, Name = "PUNJI PIT", Cost = 12, Pips = 0, Cooldown = S * 12 },
            new Card { Id = "vc-tripwire", Group = CardGroup.Call, Name = "TRIPWIRE", Cost = 18, Pips = 0, Cooldown = S * 16 },
            new Card { Id = "vc-spider", Group = CardGroup.Call, Name = "SPIDER HOLE", Cost = 8, Pips = 0, Cooldown = S * 10 },
            new Card { Id = "vc-tunnel", Group = CardGroup.Call, Name = "TUNNEL", Cost = 15, Pips = 0, Cooldown = S * 20 },
        });

        public static Card[] For(Side side) => side == Side.Us ? Us : Vc;

        public static Card Find(Side side, string id)
        {
            foreach (var c in For(side)) if (c.Id == id) return c;
            return null;
        }

        private static Card[] Index(Card[] cards)
        {
            for (int i = 0; i < cards.Length; i++) cards[i].Index = i;
            return cards;
        }

        /// <summary>Ticks left before a card can be bought again; 0 is ready.</summary>
        public static int CooldownLeft(SimState st, Side side, Card card) => st.CardCooldown[(int)side][card.Index];

        /// <summary>
        /// Tick every card's cooldown. Once per simulation tick, inside
        /// <see cref="Match.Step"/>. The original ticked these once per
        /// rendered frame from the UI, so at 60 fps every cooldown ran out
        /// three times too fast, and a headless match could not see them.
        /// </summary>
        public static void TickCooldowns(SimState st)
        {
            for (int s = 0; s < 2; s++)
            {
                var cd = st.CardCooldown[s];
                for (int i = 0; i < cd.Length; i++) if (cd[i] > 0) cd[i]--;
            }
        }

        /// <summary>Why a card cannot be bought right now, or null if it can.</summary>
        public static string Blocked(SimState st, Side side, Card card)
        {
            if (st.Over) return "match over";
            if (st.Cp[(int)side] < card.Cost) return "not enough CP";
            if (CooldownLeft(st, side, card) > 0) return "cooling down";
            if (card.Pips > 0 && Match.AliveCount(st, side) >= Tune.ForceCap) return "force cap";
            return null;
        }

        /// <summary>
        /// Buy a card. Returns whether it went through.
        ///
        /// The simulation owns this rather than the UI, so a headless run can
        /// exercise the same economy the player does. In the three.js build
        /// eight cards — every CALL card, the group a player actually reaches
        /// for — took the points, set the cooldown and did nothing at all.
        /// Every branch below does something the simulation can see.
        /// </summary>
        public static bool Buy(SimState st, Side side, Card card, int lane, Rng rng, double aimX = 0)
        {
            if (Blocked(st, side, card) != null) return false;
            st.Cp[(int)side] -= card.Cost;
            st.CardCooldown[(int)side][card.Index] = card.Cooldown;
            lane %= Tune.Lanes.Length;

            if (card.Pips > 0)
            {
                double x = Combat.Advance(side) == 1 ? -Tune.HalfLength * 0.92 : Tune.HalfLength * 0.92;
                Match.SpawnSquad(st, side, lane, x, rng);
                return true;
            }

            double z = Tune.Lanes[lane];
            switch (card.Id)
            {
                // Artillery walks a wide beaten zone for a long time and is not
                // very lethal per round; the air strike is short, tight and
                // murderous.
                case "us-arty":
                    Push(st, side, new Area { Kind = AreaKind.Barrage, X = aimX, Z = z, Radius = 17, Ticks = S * 9, Next = 1, Power = 0.30 });
                    return true;
                case "us-airstrike":
                    Push(st, side, new Area { Kind = AreaKind.Barrage, X = aimX, Z = z, Radius = 11, Ticks = S * 4, Next = 1, Power = 0.62 });
                    return true;
                case "us-smoke":
                    Push(st, side, new Area { Kind = AreaKind.Smoke, X = aimX, Z = z, Radius = 14, Ticks = S * 22, Next = 0, Power = 0 });
                    return true;
                // Not an area: the only card that buys back something already
                // lost. It steadies the line rather than healing anyone — there
                // are no hit points, and a mechanic with nothing under it is
                // worse than no mechanic.
                case "us-medevac":
                    st.Morale[(int)side] = Math.Min(1, st.Morale[(int)side] + 0.09);
                    foreach (var m in st.Men)
                    {
                        if (m.Alive && m.Side == side) m.Pin = Math.Max(0, m.Pin - 0.45);
                    }
                    return true;
                // Traps are laid in front of the enemy, not on top of him.
                case "vc-punji":
                    Push(st, side, new Area { Kind = AreaKind.Trap, X = aimX, Z = z, Radius = 3.2, Ticks = S * 90, Next = 0, Power = 0.55 });
                    return true;
                case "vc-tripwire":
                    Push(st, side, new Area { Kind = AreaKind.Trap, X = aimX, Z = z, Radius = 5.0, Ticks = S * 90, Next = 0, Power = 0.75 });
                    return true;
                case "vc-spider":
                    Push(st, side, new Area { Kind = AreaKind.Trap, X = aimX, Z = z, Radius = 2.4, Ticks = S * 120, Next = 0, Power = 0.85 });
                    return true;
                // A tunnel puts men where they should not be able to be: the one
                // card that ignores the spawn edge.
                case "vc-tunnel":
                    Match.SpawnSquad(st, side, lane, aimX + rng.Range(-8, 8), rng);
                    return true;
                default:
                    throw new InvalidOperationException($"card {card.Id} has no effect written for it");
            }
        }

        /// <summary>Add an area and announce it. Internal so squad smoke (Part 2) lays its areas the way a card does.</summary>
        internal static void Push(SimState st, Side side, Area a)
        {
            a.Id = st.AreaId++;
            a.Side = side;
            st.Areas.Add(a);
            st.Events.Add(new SimEvent
            {
                Kind = EventKind.AreaStart, Tick = st.Tick, Side = side, Id = a.Id, X = a.X, Z = a.Z,
            });
        }
    }
}
