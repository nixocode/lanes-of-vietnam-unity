using System;

namespace LanesOfVietnam.Sim
{
    /// <summary>What a man carries (MatchOptions.Arms). Appended to, never reordered: the number is in replays' hashes.</summary>
    public enum Weapon { Rifle, M16, Ak, Sks, M60, Rpd, Smg, Sniper, M79, Rpg, Mortar }

    /// <summary>What a weapon does, as numbers.</summary>
    public readonly struct Arm
    {
        /// <summary>How far it engages, metres at this map's scale (about a tenth of the ground's).</summary>
        public readonly double Range;
        /// <summary>Nearer than this it is not used (a launcher, a mortar).</summary>
        public readonly double MinRange;
        /// <summary>Ticks between shots.</summary>
        public readonly int Cooldown;
        /// <summary>A shot's chance against the rifle's, and how much of it is left at full range (the rifle keeps 0.22).</summary>
        public readonly double Hit, AtRange;
        /// <summary>What a near miss does to a man against the rifle's.</summary>
        public readonly double Pin;
        /// <summary>How much of a man's cover a round from it ignores (a rifle none; a scoped rifle waits for the head).</summary>
        public readonly double Pierce;
        /// <summary>A bursting round: its lethal radius, the chance at its centre, how much cover is worth against it (0 for a bullet).</summary>
        public readonly double Blast, Kill, CoverFactor;
        /// <summary>Ticks its round is in the air, and how far it lands off per metre of range.</summary>
        public readonly int Flight;
        public readonly double Scatter;

        public Arm(double range, int cooldown, double hit, double pin, double atRange = Tune.HitAtRange, double pierce = 0,
                   double minRange = 0, double blast = 0, double kill = 0, double coverFactor = 1, int flight = 0, double scatter = 0)
        {
            Range = range; Cooldown = cooldown; Hit = hit; Pin = pin; AtRange = atRange; Pierce = pierce;
            MinRange = minRange; Blast = blast; Kill = kill; CoverFactor = coverFactor; Flight = flight; Scatter = scatter;
        }

        public bool Bursts => Blast > 0;
    }

    /// <summary>A squad as a card buys it: who carries what (the lead man first), how far from the enemy it fights, and whether it closes with him.</summary>
    public sealed class Kit
    {
        public string Card;
        public Weapon[] Men;
        public double Reach;
        public bool Assaults;
    }

    /// <summary>
    /// Arms (Part 2, behind <see cref="MatchOptions.Arms"/>): every man carries
    /// a weapon, every card buys the squad its name says, and every fight
    /// happens at the distance its weapons fight at.
    ///
    /// The baseline has one soldier. Every card with men on it buys three to
    /// six of him at random, he engages at 28 m whatever he is, and the M60
    /// team, the sappers and the marksman differ in their names. The owner,
    /// playtest 3: "add all the corresponding gun models to each class. Also
    /// add a sniper class. And fix distance for all gunfights, it needs to be
    /// properly set."
    ///
    /// Distances are the map's and the lens's. The map is 90 m long and
    /// stands for most of a kilometre; the frame at its widest shows 23 m of
    /// the near lane and 31 of the far one, and at the baseline's 28 m the two
    /// sides of a firefight were never in it together. So a rifle's 300 m is
    /// 20 here, a machine gun's 450 is 30, a scoped rifle's 600 is 44 (he is
    /// meant to be off the edge of the frame), a submachine gun's 150 is 13.
    /// A squad holds off the enemy at the distance its kit names
    /// (<see cref="Kit.Reach"/>): sappers close to 6 m, a rifle squad fights
    /// at 13, a machine-gun team from 20, a sniper from 32. Support teams do
    /// not assault.
    ///
    /// Launchers and the mortar fire bursting rounds, which are the grenade
    /// rule's grenades with their own radius and flight (<see cref="Frag"/>).
    /// No transcendentals. State (each man's weapon, each squad's reach) is
    /// hashed only when the rule is on.
    /// </summary>
    public static class Arms
    {
        private static readonly Arm Baseline = new Arm(Tune.Range, Tune.Cooldown, 1, 1);

        public static Arm Of(Weapon w)
        {
            switch (w)
            {
                case Weapon.M16: return new Arm(20, 20, 1.0, 1.0);
                case Weapon.Ak: return new Arm(20, 20, 1.0, 1.05);
                case Weapon.Sks: return new Arm(21, 26, 1.12, 0.9);
                // Belt-fed: a burst every half second, most of it over their heads, all of it felt.
                case Weapon.M60: return new Arm(30, 9, 0.42, 1.5, atRange: 0.3);
                case Weapon.Rpd: return new Arm(28, 10, 0.45, 1.4, atRange: 0.3);
                case Weapon.Smg: return new Arm(15, 7, 0.9, 1.2, atRange: 0.2);
                // One aimed round every four seconds, good all the way out, and a parapet is two thirds the help it was.
                case Weapon.Sniper: return new Arm(44, 80, 1.8, 1.6, atRange: 0.8, pierce: 0.35);
                case Weapon.M79: return new Arm(24, 100, 0, 0, minRange: 7, blast: 4.0, kill: 0.4, coverFactor: 0.8, flight: 12, scatter: 0.07);
                // A rocket into a position: cover is no help at all.
                case Weapon.Rpg: return new Arm(24, 110, 0, 0, minRange: 7, blast: 4.5, kill: 0.6, coverFactor: 1.0, flight: 6, scatter: 0.08);
                case Weapon.Mortar: return new Arm(50, 100, 0, 0, minRange: 16, blast: 6.0, kill: 0.5, coverFactor: 0.6, flight: 50, scatter: 0.10);
                default: return Baseline;
            }
        }

        public static Arm Of(SimState st, Man m) => st.Arms ? Of(m.Weapon) : Baseline;

        private static Kit K(string card, double reach, bool assaults, params Weapon[] men)
            => new Kit { Card = card, Men = men, Reach = reach, Assaults = assaults };

        /// <summary>Every squad a card buys. The lead man first; the support weapon behind him.</summary>
        public static readonly Kit[] Kits =
        {
            K("us-rifle", 13, true, Weapon.M16, Weapon.M16, Weapon.M16, Weapon.M16, Weapon.M16),
            K("us-weapons", 16, true, Weapon.M16, Weapon.M60, Weapon.M79, Weapon.M16),
            K("us-mg", 20, false, Weapon.M16, Weapon.M60, Weapon.M16),
            K("us-mortar", 30, false, Weapon.M16, Weapon.Mortar, Weapon.M16),
            K("us-engineer", 9, true, Weapon.Smg, Weapon.Smg, Weapon.M16, Weapon.M16),
            K("us-sniper", 32, false, Weapon.Sniper, Weapon.M16),
            K("vc-cell", 12, true, Weapon.Smg, Weapon.Sks, Weapon.Sks, Weapon.Sks),
            K("vc-squad", 13, true, Weapon.Ak, Weapon.Ak, Weapon.Rpd, Weapon.Ak, Weapon.Ak),
            K("vc-rpg", 16, false, Weapon.Ak, Weapon.Rpg),
            K("vc-marksman", 32, false, Weapon.Sniper),
            K("vc-sapper", 6, true, Weapon.Smg, Weapon.Smg, Weapon.Smg),
        };

        public static Kit For(string card)
        {
            foreach (var k in Kits) if (k.Card == card) return k;
            return null;
        }

        private static readonly string[] UsRaised = { "us-rifle", "us-rifle", "us-mg", "us-rifle", "us-weapons", "us-rifle", "us-sniper" };
        private static readonly string[] VcRaised = { "vc-squad", "vc-cell", "vc-squad", "vc-squad", "vc-rpg", "vc-cell", "vc-squad", "vc-marksman" };
        /// <summary>
        /// With Senses the Americans are no longer shot at from the moment they arrive, but nor are the
        /// VC found only at arm's length: over 96 seeds the list above gave the Americans 36.5%
        /// (27.5-46.4). One NVA squad in four becomes a local-force cell.
        /// </summary>
        private static readonly string[] VcRaisedSenses = { "vc-squad", "vc-cell", "vc-squad", "vc-cell", "vc-rpg", "vc-cell", "vc-squad", "vc-marksman" };

        /// <summary>
        /// The squad a side raises by itself (the opening, and a plan's
        /// reinforcements): by how many it has raised, round a list that is
        /// mostly riflemen with the support a company has.
        /// </summary>
        public static Kit Raised(SimState st, Side side)
        {
            int n = 0;
            for (int i = 0; i < st.Squads.Count; i++) if (st.Squads[i].Side == side) n++;
            // (Gunnery: with nobody firing across the lanes or on the move, the first list again.)
            var list = side == Side.Us ? UsRaised : st.Senses && !st.Gunnery ? VcRaisedSenses : VcRaised;
            return For(list[n % list.Length]);
        }

        /// <summary>
        /// A launcher's or a mortar's round, on its way: the grenade rule's
        /// grenade with this weapon's burst. Not fired onto its own side.
        /// </summary>
        public static bool Launch(SimState st, Man a, Arm arm, Rng rng)
        {
            Man target = null;
            double best = arm.Range * arm.Range, min2 = arm.MinRange * arm.MinRange;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var b = st.Men[i];
                if (!b.Alive || !b.Seen || b.Side == a.Side) continue;
                double dx = a.X - b.X, dz = a.Z - b.Z, d2 = dx * dx + dz * dz;
                if (d2 > best || d2 < min2) continue;
                // A mortar fires over smoke; a launcher has to see what it shoots at.
                if (a.Weapon != Weapon.Mortar && Combat.SmokeBlocks(st, a.X, a.Z, b.X, b.Z)) continue;
                if (Near(st, a.Side, b.X, b.Z, arm.Blast + 2)) continue;
                best = d2; target = b;
            }
            if (target == null) { a.Cooldown = Tune.ScanIdle * 4; return false; }

            double d = Math.Sqrt(best);
            double sd = 0.5 + arm.Scatter * d;
            double sx = (rng.Next() + rng.Next() - 1) * sd * 2.45;
            double sz = (rng.Next() + rng.Next() - 1) * sd * 2.45;
            var g = new Grenade
            {
                Id = st.GrenadeId++, Side = a.Side, Thrower = a.Id,
                X = target.X + sx, Z = target.Z + sz, Ticks = Math.Max(2, arm.Flight),
                Radius = arm.Blast, Kill = arm.Kill, CoverFactor = arm.CoverFactor, By = a.Weapon,
            };
            st.Grenades.Add(g);
            a.Cooldown = arm.Cooldown;
            a.Seen = true;
            a.FiredAt = st.Tick;
            st.Events.Add(new SimEvent
            {
                Kind = EventKind.Launch, Tick = st.Tick, Side = a.Side, Id = a.Id, Target = target.Id,
                X = g.X, Z = g.Z, Amount = g.Ticks,
            });
            return true;
        }

        private static bool Near(SimState st, Side side, double x, double z, double r)
        {
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                if (!m.Alive || m.Side != side) continue;
                double dx = m.X - x, dz = m.Z - z;
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }
    }
}
