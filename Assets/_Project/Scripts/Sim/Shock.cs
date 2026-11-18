using System;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Part 2 rule (MatchOptions.Shock): the men a squad cannot spare.
    ///
    /// Warfare 1944: the officer lost, the side's morale goes with him (the owner had it looked at for what this game
    /// was missing, 2026-11-18). Here, in the war's own terms:
    ///
    ///   the leader  every squad of two or more has one, its lead man (the kit's first: an American squad's leader,
    ///               a VC cell's cadre with his submachine gun). Killed, he costs his side twice a man's morale (a
    ///               cadre, the VC's political spine, two and a half times), and the men of his squad get their
    ///               heads down until somebody takes charge (LeaderPin on each). A LeaderDown event says so
    ///   the radio   the Americans' radio man, the man the view gives the PRC-77 (the second of a squad raised, if
    ///               he carries a rifle), was the man snipers went for: killed, his side's call-ins (the guns, the
    ///               jet, the smoke shells, the dustoff) take NetDownWait times as long to come for NetDownTicks,
    ///               until another man has picked up the handset. A NetDown event says so. No delay for a side that
    ///               has raised nobody with a radio yet: the firebase's own net is the call then
    ///
    /// Draws nothing. With the rule off nothing here runs, and the match is what it was to the bit.
    /// </summary>
    public static class Shock
    {
        /// <summary>
        /// The man of a US squad who carries its radio: the second raised, if he carries a rifle (the first is its lead
        /// man). One a squad. The view's own rule (ArmyView.RadioMan) moved here, so the two cannot disagree.
        /// </summary>
        public static bool RadioMan(SimState st, Man m)
            => m.Side == Side.Us && m.Weapon == Weapon.M16 && m.Id >= 1 && st.Men[m.Id - 1].Squad == m.Squad
               && (m.Id == 1 || st.Men[m.Id - 2].Squad != m.Squad);

        /// <summary>What a man's death costs his side in morale, against an ordinary man's.</summary>
        public static double Weight(Man m)
            => !m.Leader ? 1 : m.Side == Side.Vc ? Tune.CadreMorale : Tune.LeaderMorale;

        /// <summary>Man m has been killed (Match.Step, from this tick's Kill events): his squad's heads go down if he led it, and the net if he was its radio.</summary>
        public static void Fell(SimState st, Man m)
        {
            if (m.Leader)
            {
                for (int i = 0; i < st.Men.Count; i++)
                {
                    var n = st.Men[i];
                    if (n.Alive && n.Squad == m.Squad) Combat.ApplyPin(st, n, Tune.LeaderPin);
                }
                st.Events.Add(new SimEvent { Kind = EventKind.LeaderDown, Tick = st.Tick, Side = m.Side, Id = m.Squad, Target = m.Id, X = m.X, Z = m.Z });
            }
            if (RadioMan(st, m))
            {
                st.NetDown[(int)m.Side] = st.Tick + Tune.NetDownTicks;
                st.Events.Add(new SimEvent { Kind = EventKind.NetDown, Tick = st.Tick, Side = m.Side, Id = m.Squad, Target = m.Id, X = m.X, Z = m.Z, Amount = Tune.NetDownTicks });
            }
        }

        /// <summary>The ticks a call of side's takes to come, its radio net down or not.</summary>
        public static int Wait(SimState st, Side side, int ticks)
            => st.Shock && st.Tick < st.NetDown[(int)side] ? (int)(ticks * Tune.NetDownWait) : ticks;
    }
}
