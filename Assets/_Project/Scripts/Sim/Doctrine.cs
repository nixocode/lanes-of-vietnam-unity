using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>The two trees of a side's doctrine: what its men are trained to, and what it is given to fight with.</summary>
    public enum Branch { Training, Equipment }

    /// <summary>What a perk changes. Appended to, never reordered: the number indexes SimState.Doctrine.</summary>
    public enum Effect
    {
        None,
        // A shot's chance: an M16's or a rifle's, an M60's, an NVA squad's, a scoped rifle's, a squad's on the move.
        RifleHit, M60Hit, NvaHit, MarksmanHit, MoveHit,
        // Flank (MatchOptions.Flank): the flank shot's bonus, and the morale a flank or a turned lane costs.
        FlankHit, FlankMorale,
        // A position's worth to the side's men, and the morale a casualty costs it.
        CoverWorth, MoraleLoss,
        // The deck: line cards' waits, every card's wait, every card's cost; a guerrilla cell's, grenades', traps', smoke's
        // cost; a spider hole's wait.
        LineCooldown, AllCooldown, AllCost, CellCost, GrenadeCost, TrapCost, SmokeCost, SpiderCooldown,
        // A man's pace on his feet; a sapper's.
        March, SapperPace,
        // A mortar's scatter; the chance a rocket brings the side's medevac down (its door gunners), and the side's
        // rockets bring the enemy's down.
        MortarScatter, DoorGunners, RocketDown,
        // What a burst kills: a hand grenade's, a mortar's or an M79's, a rocket's, a satchel's.
        GrenadeKill, BlastKill, RpgKill, SatchelKill,
        // The guns' rounds, the traps' bite.
        ArtyPower, TrapPower,
        // How far the side's men are seen from; what the enemy's shells do to its men close to the enemy.
        Concealment, HugBelt,
        Count,
    }

    /// <summary>One perk of a doctrine: a branch, a tier (and its cost in merit), what it is called and does, and what it opens.</summary>
    public sealed class Perk
    {
        public string Id;
        public Side Side;
        public Branch Branch;
        public int Tier;
        public string Name, Text;
        public Effect Effect;
        public double Value = 1;
        /// <summary>The card it opens in a campaign (Sim.Campaign), or null.</summary>
        public string Unlocks;
        /// <summary>Merit it costs: its tier.</summary>
        public int Cost => Tier;
    }

    /// <summary>
    /// A side's doctrine (PLAN §12.76): Warfare 1944's tree of upgrades, bought between the battles of a campaign, in the
    /// war's own terms and each side's own. Two branches, Training and Equipment, three tiers each; a tier opens when the
    /// branch has as many perks as the tier below it is deep (Sim.Campaign).
    ///
    /// A match is given each side's perks (MatchOptions.UsPerks, VcPerks); none, and nothing here runs: the match is what
    /// it was to the bit. Each perk multiplies a number where the number already lives, through one table made when the
    /// match is (SimState.Doctrine), so a tick costs nothing more for it. The perks that open a card (the air strike, the
    /// guns, napalm; the tunnel, sappers, the tripwire) matter only to a campaign, whose deck bars the card until then.
    /// </summary>
    public static class Doctrine
    {
        private static Perk P(string id, Side side, Branch b, int tier, string name, string text, Effect e = Effect.None, double v = 1, string unlocks = null)
            => new Perk { Id = id, Side = side, Branch = b, Tier = tier, Name = name, Text = text, Effect = e, Value = v, Unlocks = unlocks };

        private const Side U = Side.Us, V = Side.Vc;
        private const Branch T = Branch.Training, E = Branch.Equipment;

        /// <summary>Every perk there is, the Americans' and then the VC's, each tree by tier.</summary>
        public static readonly Perk[] All =
        {
            // --- the Americans: training ---
            P("us-marksmanship", U, T, 1, "MARKSMANSHIP", "Rifles and M16s hit 8% more often.", Effect.RifleHit, 1.08),
            P("us-dig-in", U, T, 1, "DIG IN", "Positions are worth 6% more to your men.", Effect.CoverWorth, 1.06),
            P("us-esprit", U, T, 1, "ESPRIT DE CORPS", "Casualties cost 10% less morale.", Effect.MoraleLoss, 0.90),
            P("us-replacements", U, T, 1, "REPLACEMENTS", "Line squads are ready again 10% sooner.", Effect.LineCooldown, 0.90),
            P("us-fire-movement", U, T, 2, "FIRE AND MOVEMENT", "Squads bounding or assaulting hit 10% more often.", Effect.MoveHit, 1.10),
            P("us-hit-hard", U, T, 2, "HIT 'EM HARD", "Shots from the flank gain twice the bonus.", Effect.FlankHit, 2.0),
            P("us-secure-flanks", U, T, 2, "SECURE FLANKS", "Flanks and turned lanes cost 20% less morale.", Effect.FlankMorale, 0.80),
            P("us-airmobile", U, T, 2, "AIRMOBILE", "Every card is ready again 15% sooner: the Cav's Hueys.", Effect.AllCooldown, 0.85),
            P("us-m60", U, T, 3, "M60 GUNNERY", "M60s hit 40% more often.", Effect.M60Hit, 1.40),
            P("us-hustle", U, T, 3, "HUSTLE", "Men move 20% faster.", Effect.March, 1.20),
            P("us-logistics", U, T, 3, "LOGISTICS", "Every card costs 20% less.", Effect.AllCost, 0.80),
            // --- the Americans: equipment ---
            P("us-cas", U, E, 1, "CLOSE AIR SUPPORT", "Opens the AIR STRIKE.", unlocks: "us-airstrike"),
            P("us-sights", U, E, 1, "MORTAR SIGHTS", "Mortar rounds land 25% closer.", Effect.MortarScatter, 0.75),
            P("us-door-gunners", U, E, 1, "DOOR GUNNERS", "Rockets bring your medevac down 40% less often.", Effect.DoorGunners, 0.60),
            P("us-fire-mission", U, E, 2, "FIRE MISSION", "Opens ARTILLERY.", unlocks: "us-arty"),
            P("us-wp", U, E, 2, "WILLIE PETE", "SMOKE costs 30% less and is ready 30% sooner.", Effect.SmokeCost, 0.70),
            P("us-claymore", U, E, 2, "CLAYMORE DRILL", "Hand grenades kill 25% more.", Effect.GrenadeKill, 1.25),
            P("us-napalm", U, E, 3, "NAPALM", "Opens NAPALM on the forest.", unlocks: "us-napalm"),
            P("us-tot", U, E, 3, "TIME ON TARGET", "The guns' rounds are 30% deadlier.", Effect.ArtyPower, 1.30),
            P("us-ordnance", U, E, 3, "HEAVY ORDNANCE", "Mortar and M79 rounds kill 25% more.", Effect.BlastKill, 1.25),
            // --- the VC: training ---
            P("vc-fieldcraft", V, T, 1, "FIELDCRAFT", "Positions are worth 6% more to your men.", Effect.CoverWorth, 1.06),
            P("vc-cadre", V, T, 1, "POLITICAL CADRE", "Casualties cost 15% less morale.", Effect.MoraleLoss, 0.85),
            P("vc-local-force", V, T, 1, "LOCAL FORCE", "Line squads are ready again 10% sooner.", Effect.LineCooldown, 0.90),
            P("vc-ambush", V, T, 2, "AMBUSH", "Shots from the flank gain twice the bonus.", Effect.FlankHit, 2.0),
            P("vc-hug-belt", V, T, 2, "HUG THE BELT", "Within 10 m of Americans, shells kill 40% fewer of your men.", Effect.HugBelt, 0.60),
            P("vc-main-force", V, T, 2, "MAIN FORCE", "NVA squads hit 15% more often.", Effect.NvaHit, 1.15),
            P("vc-militia", V, T, 3, "MILITIA LEVY", "GUERRILLA CELLS cost half.", Effect.CellCost, 0.50),
            P("vc-sapper-school", V, T, 3, "SAPPER SCHOOL", "Satchels kill 25% more; sappers move 15% faster.", Effect.SatchelKill, 1.25),
            P("vc-sharpshooters", V, T, 3, "SHARPSHOOTERS", "Marksmen hit 20% more often.", Effect.MarksmanHit, 1.20),
            // --- the VC: equipment ---
            P("vc-stick-grenades", V, E, 1, "STICK GRENADES", "GRENADES cost 30% less.", Effect.GrenadeCost, 0.70),
            P("vc-b40", V, E, 1, "B40 ROCKETS", "Rockets kill 20% more and bring a medevac down 30% more often.", Effect.RpgKill, 1.20),
            P("vc-booby-traps", V, E, 1, "BOOBY TRAPS", "PUNJI PITS and TRIPWIRES cost 25% less.", Effect.TrapCost, 0.75),
            P("vc-tunnels", V, E, 2, "TUNNEL NETWORK", "Opens the TUNNEL; SPIDER HOLES are ready 30% sooner.", Effect.SpiderCooldown, 0.70, unlocks: "vc-tunnel"),
            P("vc-sapper-platoon", V, E, 2, "SAPPER PLATOON", "Opens SAPPERS.", unlocks: "vc-sapper"),
            P("vc-tripwire", V, E, 2, "TRIPWIRE BELTS", "Opens the TRIPWIRE.", unlocks: "vc-tripwire"),
            P("vc-stakes", V, E, 3, "POISONED STAKES", "Traps are 25% deadlier.", Effect.TrapPower, 1.25),
            P("vc-trail", V, E, 3, "THE TRAIL", "Every card costs 15% less: supplies down the Ho Chi Minh trail.", Effect.AllCost, 0.85),
            P("vc-night", V, E, 3, "NIGHT INFILTRATION", "Your men are seen 15% less far.", Effect.Concealment, 0.85),
        };

        /// <summary>Second effects some perks have besides their first: B40s against the medevac, sapper school's pace, Willie Pete's wait.</summary>
        private static (Effect e, double v)? Also(Perk p) => p.Id switch
        {
            "vc-b40" => (Effect.RocketDown, 1.30),
            "vc-sapper-school" => (Effect.SapperPace, 1.15),
            _ => null,
        };

        public static Perk Find(string id)
        {
            foreach (var p in All) if (p.Id == id) return p;
            return null;
        }

        public static IEnumerable<Perk> For(Side side)
        {
            foreach (var p in All) if (p.Side == side) yield return p;
        }

        /// <summary>The table a match reads (SimState.Doctrine): each side's multiplier for each effect, or null if neither side has a perk.</summary>
        public static double[][] Table(string[] us, string[] vc)
        {
            if ((us == null || us.Length == 0) && (vc == null || vc.Length == 0)) return null;
            var t = new double[2][];
            for (int s = 0; s < 2; s++)
            {
                t[s] = new double[(int)Effect.Count];
                for (int i = 0; i < t[s].Length; i++) t[s][i] = 1;
                var ids = s == 0 ? us : vc;
                if (ids == null) continue;
                foreach (var id in ids)
                {
                    var p = Find(id) ?? throw new ArgumentException($"no perk called \"{id}\"");
                    if (p.Side != (Side)s) throw new ArgumentException($"perk {id} is the {p.Side}'s, not the {(Side)s}'s");
                    t[s][(int)p.Effect] *= p.Value;
                    if (Also(p) is (Effect e, double v)) t[s][(int)e] *= v;
                }
                t[s][(int)Effect.None] = 1;
            }
            return t;
        }

        /// <summary>A side's multiplier for an effect: 1 without a doctrine.</summary>
        public static double M(SimState st, Side side, Effect e) => st.Doctrine == null ? 1 : st.Doctrine[(int)side][(int)e];

        /// <summary>A shot's chance from man a, by his side's doctrine: his weapon's, his squad's, and on the move.</summary>
        public static double Aim(SimState st, Man a)
        {
            if (st.Doctrine == null) return 1;
            var t = st.Doctrine[(int)a.Side];
            double f = 1;
            if (a.Weapon == Weapon.M16 || a.Weapon == Weapon.Rifle) f *= t[(int)Effect.RifleHit];
            if (a.Weapon == Weapon.M60) f *= t[(int)Effect.M60Hit];
            if (a.Weapon == Weapon.Sniper) f *= t[(int)Effect.MarksmanHit];
            var sq = st.Squads[a.Squad];
            if (sq.Card == "vc-squad") f *= t[(int)Effect.NvaHit];
            if (sq.Bounding || sq.Assault) f *= t[(int)Effect.MoveHit];
            return f;
        }

        /// <summary>A man's pace on his feet, by his side's doctrine (and a sapper's).</summary>
        public static double Pace(SimState st, Side side, Squad sq)
        {
            if (st.Doctrine == null) return 1;
            var t = st.Doctrine[(int)side];
            return t[(int)Effect.March] * (sq.Card == "vc-sapper" ? t[(int)Effect.SapperPace] : 1);
        }

        /// <summary>What a burst kills, by the doctrine of the side that threw, fired or set it.</summary>
        public static double Blast(SimState st, Grenade g)
        {
            if (st.Doctrine == null) return 1;
            var t = st.Doctrine[(int)g.Side];
            if (g.Satchel) return t[(int)Effect.SatchelKill];
            switch (g.By)
            {
                case Weapon.Mortar: case Weapon.M79: return t[(int)Effect.BlastKill];
                case Weapon.Rpg: return t[(int)Effect.RpgKill];
                default: return t[(int)Effect.GrenadeKill];
            }
        }

        /// <summary>A card's cost by its side's doctrine.</summary>
        public static int Cost(SimState st, Card card, int cost)
        {
            if (st.Doctrine == null) return cost;
            var t = st.Doctrine[card.Id.StartsWith("us-") ? 0 : 1];
            double f = t[(int)Effect.AllCost];
            switch (card.Id)
            {
                case "vc-cell": f *= t[(int)Effect.CellCost]; break;
                case "us-frag": case "vc-frag": f *= t[(int)Effect.GrenadeCost]; break;
                case "vc-punji": case "vc-tripwire": f *= t[(int)Effect.TrapCost]; break;
                case "us-smoke": f *= t[(int)Effect.SmokeCost]; break;
            }
            return f == 1 ? cost : Math.Max(1, (int)JsMath.Round(cost * f));
        }

        /// <summary>A card's wait by its side's doctrine.</summary>
        public static int Cooldown(SimState st, Card card, int ticks)
        {
            if (st.Doctrine == null) return ticks;
            var t = st.Doctrine[card.Id.StartsWith("us-") ? 0 : 1];
            double f = t[(int)Effect.AllCooldown];
            if (card.Group == CardGroup.Line) f *= t[(int)Effect.LineCooldown];
            if (card.Id == "us-smoke") f *= t[(int)Effect.SmokeCost];
            if (card.Id == "vc-spider") f *= t[(int)Effect.SpiderCooldown];
            return f == 1 ? ticks : (int)(ticks * f);
        }

        /// <summary>
        /// HugBelt: the share a shell's chance of killing man m keeps, his side's men trained to close with the enemy
        /// so his guns cannot follow (within Tune.HugBelt of one of the enemy).
        /// </summary>
        public static double Belt(SimState st, Man m)
        {
            if (st.Doctrine == null) return 1;
            double f = st.Doctrine[(int)m.Side][(int)Effect.HugBelt];
            if (f == 1) return 1;
            double r2 = Tune.HugBelt * Tune.HugBelt;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var o = st.Men[i];
                if (!o.Alive || o.Side == m.Side) continue;
                double dx = o.X - m.X, dz = o.Z - m.Z;
                if (dx * dx + dz * dz <= r2) return f;
            }
            return 1;
        }
    }
}
