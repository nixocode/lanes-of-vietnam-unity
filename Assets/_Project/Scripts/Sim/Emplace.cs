namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// Part 2 rule (MatchOptions.Emplace): a mortar fires from where it is set up.
    ///
    /// A 60 mm or 81 mm mortar is not a rifle. Its tube goes on a baseplate on the
    /// ground and a bipod, it is laid, and then it fires; to move it is packed up
    /// again, and carried. Without the rule the mortar man fires as a rifleman does,
    /// whenever he has stopped, and walks off the moment his squad does.
    ///
    ///   set up     he has to have been still for EmplaceTicks (3 s) before the
    ///              first round, and fires only while he stays where it is
    ///   pack up    when his squad moves on, he spends PackTicks (2 s) taking it
    ///              down before he follows, and fires nothing meanwhile
    ///
    /// No random draws. His packing is hashed only when the rule is on.
    /// </summary>
    public static class Emplace
    {
        public static bool Mortar(SimState st, Man m) => st.Emplace && m.Weapon == Weapon.Mortar;

        /// <summary>Set up and laid: still long enough, and not taking it down.</summary>
        public static bool Ready(Man m) => m.Rest >= Tune.EmplaceTicks && m.Pack == 0;

        /// <summary>
        /// He would step this tick: if his tube is set up, he takes it down first. True while he is packing (he
        /// does not step); false when he is free to go (and then it is down, and has to be set up again).
        /// </summary>
        public static bool Packing(SimState st, Man m)
        {
            if (!Mortar(st, m) || m.Rest < Tune.EmplaceTicks) { m.Pack = 0; return false; }
            if (++m.Pack < Tune.PackTicks) return true;
            m.Pack = 0;
            return false;
        }
    }
}
