namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Part 2 rule (MatchOptions.Heights): the higher ground tells (Rice Fields, PLAN §12.56).
    ///
    /// The owner, 2026-10-15, of the paddies stepping up to the Americans' base: "make the rice fields ondulate higher,
    /// like have more of an hieght advantage and make the map taller on that side". The ground was raised (four steps
    /// of 1.1 m); this is the advantage. A man's ground is the level of his lane where he stands (the steps a map gives,
    /// MapDef.Steps; the river's water a metre and a quarter under its banks). Shooting up at a man higher than him, a
    /// man hits less often, the more the higher (the dike in front of the man above is most of what he can see of him);
    /// shooting down, a little more. A man up a post is the post's rule (Vantage), not this one's.
    ///
    /// A map without steps (the firebase) has everyone at one level: nothing changes there, to the bit.
    /// </summary>
    public static class Heights
    {
        /// <summary>How high his ground is: the steps of his lane he stands above, less the river's depth if he is in it.</summary>
        public static double Level(SimState st, Man m)
        {
            if (m.Squad < 0) return 0;
            int lane = st.Squads[m.Squad].Lane;
            double h = 0;
            for (int i = 0; i < st.Steps.Count; i++)
            {
                var s = st.Steps[i];
                // (A step's rise is toward +x; on a map turned end for end, its rise is negative and its foot is passed going -x.)
                if (s.Lane == lane && (s.Rise > 0 ? m.X >= s.X : m.X <= s.X)) h += System.Math.Abs(s.Rise);
            }
            if (st.Water && Water.In(st, m)) h -= 1.25;
            return h;
        }

        /// <summary>What the height between them does to a's chance of hitting b.</summary>
        public static double Factor(SimState st, Man a, Man b)
        {
            double up = Level(st, b) - Level(st, a);
            if (up > 0) return System.Math.Max(Tune.UphillFloor, 1 - Tune.Uphill * up);
            if (up < 0) return System.Math.Min(Tune.DownhillCap, 1 - Tune.Downhill * up);
            return 1;
        }
    }
}
