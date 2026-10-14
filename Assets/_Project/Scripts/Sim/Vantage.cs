using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// A place up high that a squad can be posted on: on the firebase, its
    /// watchtower. Where it is, how high, where its ladder stands, whose it is.
    /// A map's posts are its own (<see cref="Map.Posts"/>).
    /// </summary>
    public sealed class Post
    {
        public int Id;
        public string Name;
        /// <summary>The side that can man it: it stands on that side's ground.</summary>
        public Side Side;
        /// <summary>The middle of its platform, and metres up.</summary>
        public double X, Z, Height;
        /// <summary>The foot of its ladder.</summary>
        public double FootX, FootZ;
        /// <summary>The lane it stands by: a squad sent to it is raised in that lane and walks over.</summary>
        public int Lane;
        /// <summary>How much of it is left (MatchOptions.Destruction): 1 whole, 0 fallen; where the last burst that hurt it was.</summary>
        public double Strength = 1, LastHitX, LastHitZ;
        public bool Fallen;

        public Post Clone() => (Post)MemberwiseClone();
    }

    /// <summary>
    /// Vantage (Part 2, behind <see cref="MatchOptions.Vantage"/>): a squad
    /// posted up high sees and shoots as nobody on the ground can.
    ///
    /// The owner, 2026-10-10: "add an option to select (with lane selector)
    /// certain objectives per map to place certain soldiers that give massive
    /// advantage (for this map the watchtower, have a climb animation for
    /// gunners, snipers and rifle squads). Gunner very effective at medium,
    /// close range. Sniper covers all maps. Rifles good short, medium. Big
    /// positional and height advantage should reflect in game effectiveness."
    ///
    /// The rules:
    ///
    ///   who            a rifle squad, a machine-gun team or a sniper team of
    ///                  the side whose ground the post is on; one squad on it
    ///                  at a time
    ///   getting up     the squad is raised in the post's lane, walks to the
    ///                  foot of the ladder and goes up it a man at a time,
    ///                  eight seconds each, three seconds apart. A man on the
    ///                  ladder does nothing else and has nothing round him
    ///   from up there  he sees over the bank between the lanes and into
    ///                  cover: sixty metres, a sniper the whole map. His
    ///                  weapon reaches two or three times as far (a rifle 52 m,
    ///                  a machine gun 61, a sniper's the whole map, less surely
    ///                  at its far end) and hits oftener; both lanes are his;
    ///                  cover is six tenths the help against him it is from
    ///                  the ground, and a man lying flat is no smaller than
    ///                  one kneeling
    ///   what he risks  everyone can see him, from as far as he sees them,
    ///                  and a sniper's rifle reaches him from anywhere it
    ///                  can see him, and while a post is manned every squad
    ///                  the computer raises against it brings a marksman. The bags round the platform
    ///                  are some cover from rifles; a rocket or a grenade
    ///                  from a launcher is fired at him from sixty metres and
    ///                  bursts up there, and the bags are none against that. Nothing on the ground
    ///                  reaches him otherwise: no hand grenade, no bayonet,
    ///                  no shell, and nobody goes in at him
    ///   coming down    told to advance or fall back, the squad comes down
    ///                  the ladder and is a squad in its lane again
    ///
    /// A posted squad is not the squad's own master: it holds, and neither
    /// breaks nor closes. It draws no randomness. Off, the match is what it
    /// was to the bit: a man's height and his ladder are zero and no squad
    /// has a post.
    /// </summary>
    public static class Vantage
    {
        /// <summary>The cards whose squads can be posted: rifles, a machine gun's team, a sniper's.</summary>
        public static readonly string[] Cards = { "us-rifle", "us-mg", "us-sniper", "vc-squad", "vc-cell", "vc-marksman" };

        public static bool Takes(string card) => card != null && Array.IndexOf(Cards, card) >= 0;

        /// <summary>The squad's post, or null.</summary>
        public static Post Of(SimState st, Squad sq) => sq.Post >= 0 && sq.Post < st.Posts.Count ? st.Posts[sq.Post] : null;

        /// <summary>Up on a post, and off its ladder.</summary>
        public static bool Up(Man m) => m.High > 0 && m.Climb == 0;

        /// <summary>The squad that has this post (men of it alive, on it or on their way), or -1.</summary>
        public static int Holder(SimState st, int post)
        {
            for (int i = 0; i < st.Squads.Count; i++)
                if (st.Squads[i].Post == post && Squads.AnyAlive(st, st.Squads[i].Id)) return st.Squads[i].Id;
            return -1;
        }

        /// <summary>Why this card's squad cannot be sent to this post now, or null if it can.</summary>
        public static string Blocked(SimState st, Side side, string card, int post)
        {
            if (!st.Vantage || post < 0 || post >= st.Posts.Count) return "no such post";
            if (st.Posts[post].Side != side) return "not ours";
            if (!Takes(card)) return "not for a post";
            if (Holder(st, post) >= 0) return "manned";
            // Destruction: it is down.
            if (st.Posts[post].Fallen) return "fallen";
            return null;
        }

        /// <summary>Send a squad that has just been raised to a post.</summary>
        public static void Send(SimState st, Squad sq, int post)
        {
            sq.Post = post;
            sq.Leaving = false;
            sq.Lane = st.Posts[post].Lane;
        }

        /// <summary>
        /// The computer's side, raising a squad by itself (MatchOptions.ComputerPosts): a machine gun's team or a
        /// sniper's goes up its post if the post is empty. (Its rifles are wanted in the lanes.)
        /// </summary>
        public static void Offer(SimState st, Squad sq)
        {
            if (sq.Card == "us-rifle" || sq.Card == "vc-squad" || sq.Card == "vc-cell") return;
            for (int p = 0; p < st.Posts.Count; p++)
                if (Blocked(st, sq.Side, sq.Card, p) == null) { Send(st, sq, p); return; }
        }

        /// <summary>
        /// The computer's side, raising a squad by itself while the enemy has men up on a post: one of the squad's
        /// riflemen (its last) comes with a sniper's rifle. A post is answered by every squad that comes up after it
        /// is manned, not by one man bought at a squad's price and sent to stand in the open under it. True if
        /// one was armed so.
        /// </summary>
        public static bool Answer(SimState st, Squad sq)
        {
            bool manned = false;
            for (int i = 0; i < st.Men.Count && !manned; i++) manned = st.Men[i].Alive && st.Men[i].Side != sq.Side && Up(st.Men[i]);
            if (!manned) return false;
            Man pick = null;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                if (!m.Alive || m.Squad != sq.Id) continue;
                if (m.Weapon == Weapon.Sniper) return false;
                if (m.Rank > 0 && (m.Weapon == Weapon.M16 || m.Weapon == Weapon.Ak || m.Weapon == Weapon.Sks)) pick = m;
            }
            if (pick == null) return false;
            pick.Weapon = Weapon.Sniper;
            if (st.Ammo) Ammo.Issue(st, pick);
            return true;
        }

        /// <summary>Where the k-th man of a squad stands on the platform.</summary>
        public static (double x, double z) Place(Post post, int k)
            => (post.X + (k % 3 - 1) * Tune.PostSpacing, post.Z - 0.35 - (k / 3) * 0.8);

        /// <summary>His weapon as it is from up there: further, surer, and into cover.</summary>
        public static Arm From(Arm a, Weapon w)
        {
            if (a.Bursts) return a;
            bool sniper = w == Weapon.Sniper, gun = w == Weapon.M60 || w == Weapon.Rpd;
            return new Arm(sniper ? Tune.PostSniperReach : a.Range * (gun ? Tune.PostGunReach : Tune.PostReach), a.Cooldown,
                           a.Hit * (gun ? Tune.PostGunHit : sniper ? Tune.PostSniperHit : Tune.PostHit), a.Pin, sniper ? a.AtRange * Tune.PostSniperFar : a.AtRange,
                           1 - (1 - a.Pierce) * Tune.PostOverCover, a.MinRange, a.Blast, a.Kill, a.CoverFactor, a.Flight, a.Scatter);
        }

        /// <summary>How far one man sees another, given what it was on the ground: further from up there, and a man up there is seen from further.</summary>
        public static double Sight(Man observer, Man target, double r)
        {
            if (Up(observer)) r = Math.Max(r, observer.Weapon == Weapon.Sniper ? Tune.PostSniperReach : Tune.PostSight);
            // (As far as he sees, he is seen: against the sky, with nothing in front of him. A scope finds him anywhere.)
            if (target.High > 0) r = Math.Max(r, observer.Weapon == Weapon.Sniper ? Tune.PostSniperReach : Tune.PostSight);
            return r;
        }

        /// <summary>
        /// A sniper's rifle reaches a man up on a post wherever he can see him: the one answer the ground has
        /// to a post, short of a rocket. (The square of the firer's reach at this man, given what it is at any other.)
        /// </summary>
        public static double Reach2(Man a, Man b, double reach2)
            => Duel(a, b) ? Math.Max(reach2, Tune.PostSniperReach * Tune.PostSniperReach) : reach2;

        /// <summary>A sniper on the ground firing at a man up on a post.</summary>
        public static bool Duel(Man a, Man b) => a.Weapon == Weapon.Sniper && b.High > 0;

        /// <summary>Whether a burst at one height reaches a man at another.</summary>
        public static bool Reaches(double burstHigh, Man m) => Math.Abs(m.High - burstHigh) <= Tune.PostBlastHigh;

        /// <summary>The ladder is clear for the next man: the last to start is far enough along it.</summary>
        private static bool LadderClear(IReadOnlyList<Man> live)
        {
            for (int i = 0; i < live.Count; i++)
                if (Math.Abs(live[i].Climb) > Tune.ClimbTicks - Tune.LadderGap) return false;
            return true;
        }

        /// <summary>
        /// A posted squad's tick, in place of the squad's own: each man walks to the ladder, goes up it, and
        /// holds his place on the platform; or, the squad told to leave, comes down again.
        /// </summary>
        public static void Move(SimState st, Squad sq, IReadOnlyList<Man> live)
        {
            var post = st.Posts[sq.Post];
            // Told to advance or to fall back: it comes down. (Hold, or the plan's own order, leaves it there.)
            if (sq.PlayerOrder == Order.Advance || sq.PlayerOrder == Order.Fallback) sq.Leaving = true;
            sq.Order = Order.Hold;
            sq.Halted = true;
            sq.Target = -1;
            sq.AnchorX = post.X; sq.AnchorZ = post.Z;
            if (st.Senses) sq.Task = SquadTask.March;

            bool anyUp = false;
            for (int i = 0; i < live.Count; i++) if (Up(live[i])) { anyUp = true; break; }
            bool allDown = true;
            for (int i = 0; i < live.Count; i++)
            {
                var m = live[i];
                m.Dwell++;
                m.Cover = -1; m.Place = -1; m.PlaceCover = -1;
                double wasX = m.X, wasZ = m.Z, wasHigh = m.High;
                var want = m.Pin >= Tune.PinStop ? Posture.Prone : m.Pin >= Tune.PinDrop ? Posture.Crouched : Posture.Standing;
                bool stepsOff = false;
                if (m.Climb != 0)
                {
                    // On the ladder: up it, or down it. Nothing else.
                    want = Posture.Standing;
                    bool rising = m.Climb > 0;
                    if (rising) m.Climb--; else m.Climb++;
                    double done = 1 - Math.Abs(m.Climb) / (double)Tune.ClimbTicks;
                    m.High = post.Height * (rising ? done : 1 - done);
                    if (m.Climb == 0)
                    {
                        stepsOff = true;
                        if (rising)
                        {
                            // Onto the platform, and down behind its bags.
                            want = Posture.Crouched;
                            m.High = post.Height;
                            (m.X, m.Z) = Place(post, m.Rank);
                            if (!anyUp)
                            {
                                anyUp = true;
                                st.Events.Add(new SimEvent { Kind = EventKind.Posted, Tick = st.Tick, Side = sq.Side, Id = sq.Id, Target = post.Id, X = post.X, Z = post.Z });
                            }
                        }
                        else { m.High = 0; m.X = post.FootX; m.Z = post.FootZ; }
                    }
                }
                else if (m.High > 0)
                {
                    // Up: behind the bags, on a knee to fire over them, flat when the fire is on him.
                    if (sq.Leaving && LadderClear(live))
                    {
                        m.Climb = -Tune.ClimbTicks;
                        m.X = post.FootX; m.Z = post.FootZ;
                        want = Posture.Standing;
                    }
                    else want = m.Pin >= Tune.PinStop ? Posture.Prone : Posture.Crouched;
                }
                else if (!sq.Leaving)
                {
                    // On the ground: to the ladder, in a line. The next man up goes to its foot and up when it is
                    // clear; the others wait behind him a pace apart, along the lane's own direction and on his
                    // side's end of it. (All of them went to its foot, and a squad of five stood on one spot: the
                    // owner, 2026-10-11.)
                    int ahead = 0;
                    for (int j = 0; j < live.Count; j++)
                        if (live[j] != m && live[j].High <= 0 && live[j].Climb == 0 && live[j].Rank < m.Rank) ahead++;
                    double tx = post.FootX - Combat.Advance(sq.Side) * ahead * Tune.PostQueue, tz = post.FootZ;
                    double dx = tx - m.X, dz = tz - m.Z, d = JsMath.Hypot(dx, dz);
                    if (ahead > 0)
                    {
                        if (d > 0.05 && m.Pin < Tune.PinDrop && m.Posture == Posture.Standing)
                        {
                            double step = Math.Min(d, Tune.SpeedWalk * Tune.Dt);
                            m.X += dx / d * step; m.Z += dz / d * step;
                        }
                    }
                    else if (d <= Tune.PostArrive)
                    {
                        if (m.Pin < Tune.PinDrop && LadderClear(live))
                        {
                            m.Climb = Tune.ClimbTicks;
                            m.X = post.FootX; m.Z = post.FootZ;
                            want = Posture.Standing;
                        }
                    }
                    else if (m.Pin < Tune.PinDrop && m.Posture == Posture.Standing)
                    {
                        double step = Math.Min(d, Tune.SpeedWalk * Tune.Dt);
                        m.X += dx / d * step; m.Z += dz / d * step;
                    }
                }
                if (want != m.Posture && (m.Dwell >= Tune.PostureDwell || m.Climb != 0 || stepsOff))
                {
                    m.Before = m.Posture;
                    m.Posture = want;
                    m.Dwell = 0;
                }
                m.Still = m.X == wasX && m.Z == wasZ && m.High == wasHigh;
                m.Rest = m.Still ? m.Rest + 1 : 0;
                m.Wait = 0;
                Squads.PushTrail(m);
                if (m.High > 0 || m.Climb != 0) allDown = false;
            }

            if (sq.Leaving && allDown)
            {
                // Down, all of them: a squad in its lane again, as one just raised is.
                sq.Post = -1;
                sq.Leaving = false;
                sq.Halted = false;
                sq.Order = sq.PlayerOrder ?? Order.Advance;
                sq.AnchorX = post.FootX + Combat.Advance(sq.Side) * Tune.AnchorLeash;
                sq.AnchorZ = Tune.Lanes[sq.Lane];
                if (st.Fieldcraft) Fieldcraft.Raised(st, sq);
            }
        }
    }
}
