using System;
using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    /// <summary>
    /// The firebase, by the names everything has always called it by: its
    /// cover, the shape of its ground, its watchtower.
    ///
    /// It was the one map, written here as code. It is data now
    /// (<see cref="Maps.Firebase"/>, PLAN §12.27 S1), and this is that map's
    /// short name: the tests, the headless tool and every pinned hash read
    /// the firebase through it, as before.
    /// </summary>
    public static class Map
    {
        /// <summary>Near lane, far lane: <see cref="Tune.Lanes"/>.</summary>
        public const int Near = 0, Far = 1;

        /// <summary>Where the watchtower stands.</summary>
        public const double TowerX = -39.0, TowerZ = -11.6;

        /// <summary>The map's cover, in x order, ids assigned. A fresh copy each call.</summary>
        public static List<Cover> Cover() => Maps.Firebase.Cover();

        /// <summary>The places up high a squad can be posted on (MatchOptions.Vantage): on this map one, its watchtower.</summary>
        public static List<Post> Posts() => Maps.Firebase.PostList();

        /// <summary>The ground's shape parameters for this map.</summary>
        public static GroundParams GroundParams() => Maps.Firebase.GroundShape;

        public static List<Crater> Craters() => Maps.Firebase.Craters();

        public static List<Mound> Mounds() => Maps.Firebase.Mounds();

        public static List<Trench> Trenches() => Maps.Firebase.Trenches();

        public static Ground Ground() => Maps.Firebase.Ground();
    }
}
