using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// A fingerprint of the whole simulation state, and the scripted match the
    /// parity check plays.
    ///
    /// The hash is FNV-1a over every man, squad, piece of cover, area and this
    /// tick's events, with every double hashed by its exact bits. It is
    /// computed identically by <c>tools/parity/trace.ts</c> against the
    /// TypeScript original, which is how the port was shown to be exact: the
    /// same hash at every tick of 22 matches. The tests pin hashes from that
    /// run, so parity stays checked inside Unity without Node or the original.
    ///
    /// It also ships in the build, so a browser can run a match headless and
    /// print its hash — the same number the Editor prints, or determinism
    /// across the two is broken.
    /// </summary>
    public static class Parity
    {
        private static uint Mix(uint h, uint v)
        {
            unchecked
            {
                for (int i = 0; i < 4; i++)
                {
                    h ^= (v >> (i * 8)) & 0xffu;
                    h *= 0x01000193u;
                }
                return h;
            }
        }

        private static uint MixD(uint h, double x)
        {
            long b = BitConverter.DoubleToInt64Bits(x);
            return Mix(Mix(h, (uint)b), (uint)(b >> 32));
        }

        private static uint MixI(uint h, int x) => Mix(h, unchecked((uint)x));
        private static uint MixB(uint h, bool b) => Mix(h, b ? 1u : 0u);

        /// <summary>Hash the whole state, and the events from <paramref name="fromEvent"/> on.</summary>
        public static uint Hash(SimState st, int fromEvent)
        {
            uint h = 0x811c9dc5u;
            h = MixI(h, st.Tick); h = MixI(h, (int)st.Phase); h = MixI(h, st.ContactTick);
            foreach (var m in st.Men)
            {
                h = MixI(h, m.Id); h = MixI(h, m.Squad); h = MixI(h, (int)m.Side);
                h = MixD(h, m.X); h = MixD(h, m.Z); h = MixB(h, m.Alive); h = MixD(h, m.Pin);
                h = MixI(h, (int)m.Posture); h = MixI(h, m.Cooldown); h = MixI(h, m.Cover);
                h = MixI(h, m.Dwell); h = MixB(h, m.Seen); h = MixD(h, m.Veterancy); h = MixI(h, m.DiedAt);
                h = MixI(h, m.Trail.Count);
                foreach (var t in m.Trail) h = MixD(h, t);
            }
            foreach (var s in st.Squads)
            {
                h = MixI(h, s.Id); h = MixI(h, (int)s.Side); h = MixI(h, (int)s.Order);
                h = MixD(h, s.AnchorX); h = MixD(h, s.AnchorZ); h = MixI(h, s.Lane); h = MixI(h, s.Target);
                h = MixB(h, s.Bounding); h = MixI(h, s.PlayerOrder.HasValue ? (int)s.PlayerOrder.Value : -1);
            }
            foreach (var c in st.Cover)
            {
                h = MixI(h, c.Id); h = MixI(h, (int)c.Kind); h = MixD(h, c.X); h = MixD(h, c.Z);
                h = MixD(h, c.Length); h = MixI(h, c.Capacity); h = MixD(h, c.Quality);
                h = MixD(h, c.RangedIn); h = MixI(h, c.HeldBy.HasValue ? (int)c.HeldBy.Value : -1);
            }
            foreach (var a in st.Areas)
            {
                h = MixI(h, a.Id); h = MixI(h, (int)a.Kind); h = MixI(h, (int)a.Side);
                h = MixD(h, a.X); h = MixD(h, a.Z); h = MixD(h, a.Radius); h = MixI(h, a.Ticks);
                h = MixI(h, a.Next); h = MixD(h, a.Power);
            }
            // Part 2 state, hashed only when its rule is on, so the baseline's
            // hashes are the ones the TypeScript original produced.
            if (st.SquadSmoke)
                foreach (var s in st.Squads) h = MixI(h, s.Smoke);
            if (st.Drill)
                foreach (var s in st.Squads) { h = MixI(h, s.OrderSince); h = MixI(h, s.Rallied ? 1 : 0); }
            if (st.Fieldcraft)
            {
                foreach (var m in st.Men) { h = MixI(h, m.Rank); h = MixI(h, m.Place); h = MixI(h, m.PlaceCover); h = MixI(h, m.Vault); h = MixB(h, m.Still); }
                foreach (var s in st.Squads) { h = MixI(h, s.File); h = MixI(h, s.Held); h = MixB(h, s.Halted); h = MixB(h, s.Assault); h = MixI(h, s.Sent); h = MixI(h, s.Charge); h = MixI(h, s.Closing); }
                foreach (var c in st.Cover) { h = MixI(h, (int)c.LeverUs); h = MixI(h, (int)c.LeverVc); h = MixI(h, c.Owner.HasValue ? (int)c.Owner.Value : -1); }
            }
            if (st.Arms)
            {
                foreach (var m in st.Men) h = MixI(h, (int)m.Weapon);
                foreach (var s in st.Squads) { h = MixD(h, s.Reach); h = MixB(h, s.Assaults); }
                foreach (var g in st.Grenades) { h = MixI(h, g.Id); h = MixI(h, (int)g.By); h = MixD(h, g.X); h = MixD(h, g.Z); h = MixI(h, g.Ticks); }
            }
            if (st.Senses)
            {
                foreach (var m in st.Men) h = MixI(h, m.FiredAt);
                foreach (var s in st.Squads)
                {
                    h = MixI(h, (int)s.Task); h = MixI(h, s.TaskSince); h = MixI(h, s.SeenAt); h = MixI(h, s.Threat); h = MixB(h, s.ThreatSeen);
                    h = MixI(h, s.SawAt.Count); foreach (int t in s.SawAt) h = MixI(h, t);
                    h = MixI(h, s.KnownAt.Count); foreach (int t in s.KnownAt) h = MixI(h, t);
                }
            }
            if (st.Gunnery)
                foreach (var m in st.Men) { h = MixI(h, m.Rest); h = MixI(h, (int)m.Before); h = MixI(h, m.Wait); }
            if (st.Ammo)
            {
                foreach (var m in st.Men) { h = MixI(h, m.Rounds); h = MixI(h, m.Reloading); }
                foreach (var s in st.Squads) h = MixI(h, s.ThrewAt);
            }
            if (st.Frag)
            {
                foreach (var m in st.Men) { h = MixI(h, m.Grenades); h = MixI(h, m.FragCooldown); }
                foreach (var g in st.Grenades)
                {
                    h = MixI(h, g.Id); h = MixI(h, (int)g.Side); h = MixI(h, g.Thrower);
                    h = MixD(h, g.X); h = MixD(h, g.Z); h = MixI(h, g.Ticks);
                }
            }
            for (int s = 0; s < 2; s++)
            {
                h = MixD(h, st.Morale[s]); h = MixD(h, st.Cp[s]); h = MixD(h, st.Front[s]);
                h = MixD(h, st.MoraleLostToCasualties[s]); h = MixD(h, st.MoraleLostToGround[s]);
            }
            h = MixI(h, st.Events.Count);
            for (int i = fromEvent; i < st.Events.Count; i++)
            {
                var e = st.Events[i];
                h = MixI(h, (int)e.Kind); h = MixI(h, e.Tick); h = MixI(h, (int)e.Side);
                h = MixI(h, e.Id); h = MixI(h, e.Target ?? -1);
                h = e.X.HasValue ? MixD(h, e.X.Value) : MixI(h, -7);
                h = e.Z.HasValue ? MixD(h, e.Z.Value) : MixI(h, -7);
                h = e.Amount.HasValue ? MixD(h, e.Amount.Value) : MixI(h, -7);
                if (st.Ammo) h = MixI(h, e.Rounds);
            }
            h = MixB(h, st.Over); h = MixI(h, st.Winner.HasValue ? (int)st.Winner.Value : -1);
            return h;
        }

        public struct Act
        {
            public int Tick;
            public Command Command;
        }

        /// <summary>
        /// The scripted scenario, identical to trace.ts: every call-in card,
        /// two squad orders, scheduled against the CP curve so each can go
        /// through. Run it under no-reinforce plans, which leave the CP alone.
        /// </summary>
        public static readonly Act[] Script =
        {
            new Act { Tick = 300, Command = Command.Buy(Side.Us, "us-smoke", 0, 0) },
            new Act { Tick = 300, Command = Command.Buy(Side.Vc, "vc-punji", 0, -5) },
            new Act { Tick = 350, Command = Command.OrderSquad(Side.Us, 0, Order.Hold) },
            new Act { Tick = 500, Command = Command.Buy(Side.Vc, "vc-spider", 1, 8) },
            new Act { Tick = 700, Command = Command.OrderSquad(Side.Us, 0, null) },
            new Act { Tick = 900, Command = Command.Buy(Side.Us, "us-arty", 1, 10) },
            new Act { Tick = 900, Command = Command.Buy(Side.Vc, "vc-tunnel", 1, -20) },
            new Act { Tick = 1300, Command = Command.Buy(Side.Us, "us-medevac", 0, 0) },
            new Act { Tick = 1300, Command = Command.Buy(Side.Vc, "vc-tripwire", 0, 0) },
            new Act { Tick = 2200, Command = Command.Buy(Side.Us, "us-airstrike", 1, 5) },
        };

        /// <summary>Play a match and return the hash after every tick (index 0 is the start).</summary>
        public static List<uint> Trace(int seed, Plan us, Plan vc, bool scripted, out LiveMatch match)
        {
            var lm = new LiveMatch(new MatchOptions { Seed = seed, Us = us, Vc = vc });
            var hashes = new List<uint> { Hash(lm.State, 0) };
            int k = 0;
            while (!lm.State.Over && lm.State.Tick < lm.Cap)
            {
                if (scripted)
                {
                    while (k < Script.Length && Script[k].Tick == lm.State.Tick + 1) lm.Issue(Script[k++].Command);
                }
                lm.Step();
                hashes.Add(Hash(lm.State, lm.TickEventsFrom));
            }
            match = lm;
            return hashes;
        }
    }
}
