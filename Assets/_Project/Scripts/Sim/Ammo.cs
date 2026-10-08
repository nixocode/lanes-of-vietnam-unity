using System;

namespace LanesOfVietnam.Sim
{
    /// <summary>What a weapon is loaded with: its magazine, how long a reload takes, and how it is fired.</summary>
    public readonly struct Load
    {
        /// <summary>Rounds in a magazine, a belt or a drum; for a launcher, rounds carried.</summary>
        public readonly int Magazine;
        /// <summary>Ticks a reload takes (0: it has none, a launcher loads with every shot).</summary>
        public readonly int Reload;
        /// <summary>Rounds in a burst, and ticks from one burst to the next: short, and at full automatic.</summary>
        public readonly int Burst, LongBurst, Gap, LongGap;

        public Load(int magazine, int reload, int burst = 1, int longBurst = 1, int gap = 0, int longGap = 0)
        {
            Magazine = magazine; Reload = reload; Burst = burst; LongBurst = longBurst; Gap = gap; LongGap = longGap;
        }

        public bool Automatic => Burst > 1;
    }

    /// <summary>
    /// Ammunition (Part 2, behind <see cref="MatchOptions.Ammo"/>): rounds are
    /// counted, magazines run out, and a reload takes its time.
    ///
    /// The owner, playtest 8: "No reloads; this needs to be a feature that is
    /// strategic and needs to be timed. The M60 or the Viet gunner just spams
    /// rounds non-stop. Make them only full auto when necessary, do short
    /// bursts and do reloads (count rounds, and assign each squad/gun/soldier
    /// their limited amount of rounds per magazine). Same with grenades: can't
    /// be endless spam." Measured on that build (`simcs aware`, six seeds) the
    /// two sides fired 189 rounds a minute between them and nobody ever
    /// stopped.
    ///
    /// The rules:
    ///
    ///   magazines  every man has his weapon's magazine (an M16's 20, an AK's
    ///              30, an SKS's ten-round clip, a belt or a drum of 100) and
    ///              spends a round a shot, or a burst's worth. Empty, he
    ///              reloads: two and a half seconds for a rifle, seven for the
    ///              M60's belt. He does not fire while he does, and the man he
    ///              was keeping down gets his head up
    ///   timing     with nobody to shoot at and a magazine under two fifths
    ///              full, he changes it: in the lull, not in the fight
    ///   aimed      a rifleman fires an aimed round half again as seldom as his
    ///              rifle's own rate (the owner: "even gunfights are too fast")
    ///   bursts     a machine gun or a submachine gun fires short bursts with
    ///              a pause between them; long bursts, faster, only when the
    ///              enemy is close (ten metres) or going in, either way
    ///   launchers  an M79 carries twelve rounds, an RPG three rockets, the
    ///              mortar ten bombs; then it is done
    ///   grenades   a squad throws one at a time: a man of it who has just
    ///              thrown keeps the rest from throwing for three seconds
    ///
    /// No random draws, no transcendentals. Its state is hashed only when the
    /// rule is on: off, the match is what it was to the bit.
    /// </summary>
    public static class Ammo
    {
        public static Load Of(Weapon w)
        {
            switch (w)
            {
                case Weapon.M16: return new Load(20, 50);
                case Weapon.Ak: return new Load(30, 55);
                case Weapon.Sks: return new Load(10, 60);
                case Weapon.M60: return new Load(100, 140, burst: 5, longBurst: 10, gap: 34, longGap: 14);
                case Weapon.Rpd: return new Load(100, 120, burst: 5, longBurst: 9, gap: 36, longGap: 16);
                case Weapon.Smg: return new Load(30, 45, burst: 4, longBurst: 8, gap: 22, longGap: 10);
                case Weapon.Sniper: return new Load(5, 70);
                case Weapon.M79: return new Load(12, 0);
                case Weapon.Rpg: return new Load(3, 0);
                case Weapon.Mortar: return new Load(10, 0);
                default: return new Load(20, 50);
            }
        }

        public static Load Of(SimState st, Man m) => Of(st.Arms ? m.Weapon : Weapon.Rifle);

        /// <summary>A man as he comes on: a full magazine, or every round his launcher carries.</summary>
        public static void Issue(SimState st, Man m) => m.Rounds = Of(st, m).Magazine;

        /// <summary>The tick's reloading: a man who finishes has a full magazine.</summary>
        public static void Tick(SimState st, Man m)
        {
            if (m.Reloading <= 0) return;
            if (--m.Reloading == 0) m.Rounds = Of(st, m).Magazine;
        }

        /// <summary>Can he fire now? Empty, he starts a reload (and says so); reloading, he waits.</summary>
        public static bool Ready(SimState st, Man m)
        {
            if (m.Reloading > 0) return false;
            if (m.Rounds > 0) return true;
            var load = Of(st, m);
            if (load.Reload > 0) Begin(st, m, load);
            return false;
        }

        /// <summary>Nothing to shoot at: if his magazine is low, he changes it now, while it is quiet.</summary>
        public static void Lull(SimState st, Man m)
        {
            var load = Of(st, m);
            if (m.Reloading > 0 || load.Reload <= 0 || m.Rounds * 5 >= load.Magazine * 2) return;
            Begin(st, m, load);
        }

        private static void Begin(SimState st, Man m, Load load)
        {
            // (Fortune: this man, this time.)
            m.Reloading = st.Fortune ? Fortune.Pause(st, m, load.Reload) : load.Reload;
            st.Events.Add(new SimEvent { Kind = EventKind.Reload, Tick = st.Tick, Side = m.Side, Id = m.Id, Amount = m.Reloading });
        }

        /// <summary>
        /// Full automatic: the enemy he is firing at is within ten metres of him, or one side or the other
        /// is going in (his squad, or the target's).
        /// </summary>
        public static bool Close(SimState st, Man a, Man target)
        {
            double dx = a.X - target.X, dz = a.Z - target.Z;
            if (dx * dx + dz * dz < Tune.CloseRange * Tune.CloseRange) return true;
            return st.Squads[a.Squad].Task == SquadTask.Assault || st.Squads[target.Squad].Task == SquadTask.Assault;
        }

        /// <summary>
        /// Fire: the rounds this shot spends (one, or a burst, never more than are left) and the ticks
        /// until the next. Returns the rounds.
        /// </summary>
        public static int Spend(SimState st, Man a, Man target, int cooldown)
        {
            var load = Of(st, a);
            int rounds = 1;
            if (load.Automatic)
            {
                bool close = Close(st, a, target);
                rounds = Math.Min(a.Rounds, close ? load.LongBurst : load.Burst);
                a.Cooldown = close ? load.LongGap : load.Gap;
            }
            // An aimed round from a rifle every half again as long: a man in a firefight shoots at what he
            // sees, he does not empty his rifle at the rate it will fire.
            else a.Cooldown = cooldown * 3 / 2;
            a.Rounds -= rounds;
            return rounds;
        }

        /// <summary>A launcher's round: one of those carried. False if there are none left.</summary>
        public static bool Launch(Man a)
        {
            if (a.Rounds <= 0) return false;
            a.Rounds--;
            return true;
        }

        /// <summary>A squad throws one grenade at a time: has a man of it thrown in the last few seconds?</summary>
        public static bool SquadThrowing(SimState st, Man a) => st.Tick - st.Squads[a.Squad].ThrewAt < Tactics.ThrowGap(st);
    }
}
