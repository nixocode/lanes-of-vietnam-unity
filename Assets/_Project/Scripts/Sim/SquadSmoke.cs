namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Squad smoke (PLAN §12.8, Part 2, behind <see cref="MatchOptions.SquadSmoke"/>):
    /// "the 2D game's AI pops it when bounding is blocked".
    ///
    /// A squad on the move — advancing or bounding — that the enemy has
    /// pinned to the ground throws its smoke grenade between itself and the
    /// nearest enemy it can see, and the screen breaks the firing lines that
    /// were holding it down (<see cref="Combat.SmokeBlocks"/>), so it can get
    /// up and go on. One canister a squad. No random draws at all: the rule
    /// is a pure function of the state, and with it off nothing here runs.
    /// </summary>
    public static class SquadSmoke
    {
        public static void Tick(SimState st)
        {
            for (int si = 0; si < st.Squads.Count; si++)
            {
                var sq = st.Squads[si];
                if (sq.Smoke <= 0) continue;
                // A bound that has stalled: the one move smoke exists for.
                // (Senses: a squad moving up under fire, or caught in the open with no cover to go to.)
                if (st.Senses
                    ? !(sq.Task == SquadTask.Close || sq.Task == SquadTask.Assault || (sq.Task == SquadTask.Firefight && sq.Target < 0))
                    : sq.Order != Order.Bound) continue;
                var live = Squads.Roster(st, sq.Id);
                if (live.Count == 0) continue;
                double pin = 0;
                for (int i = 0; i < live.Count; i++) pin += live[i].Pin;
                if (pin / live.Count < Tune.SquadSmokePin) continue;

                // The nearest enemy anyone in the squad can see, within rifle range of the anchor.
                Man foe = null;
                double best = Tune.Range * Tune.Range;
                for (int i = 0; i < st.Men.Count; i++)
                {
                    var b = st.Men[i];
                    if (!b.Alive || !b.Seen || b.Side == sq.Side) continue;
                    double dx = b.X - sq.AnchorX, dz = b.Z - sq.AnchorZ;
                    double d2 = dx * dx + dz * dz;
                    if (d2 < best) { best = d2; foe = b; }
                }
                if (foe == null) continue;
                // Already screened: no second canister into the same cloud.
                if (Combat.SmokeBlocks(st, sq.AnchorX, sq.AnchorZ, foe.X, foe.Z)) continue;

                sq.Smoke--;
                double k = Tune.SquadSmokeReach;
                Deck.Push(st, sq.Side, new Area
                {
                    Kind = AreaKind.Smoke,
                    X = sq.AnchorX + (foe.X - sq.AnchorX) * k,
                    Z = sq.AnchorZ + (foe.Z - sq.AnchorZ) * k,
                    Radius = Tune.SquadSmokeRadius, Ticks = Tune.SquadSmokeTicks, Next = 0, Power = 0,
                });
            }
        }
    }
}
