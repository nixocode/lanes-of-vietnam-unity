using System;
using UnityEngine;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// A theatre (PLAN §12.27, M0; Stage 6 of the plan the owner approved on 2026-10-12): the art and the rules of
    /// a country, which a map names and the world is built in. The firebase's is the Central Highlands: its scanned
    /// ground, its plants and where they grow, its trucks, tower, bunkers and sandbags, its sky and the Chu Pong
    /// massif on its horizon.
    ///
    /// All of it was the views' own, set into each by SceneBuilder and read as constants: the world could be the
    /// firebase and nothing else. Now the views take it from the theatre their map names when the world is built
    /// (GameRoot.BuildWorld), so a second map in a second country brings a second theatre and nothing in the views
    /// changes.
    /// </summary>
    [Serializable]
    public sealed class Theatre
    {
        public string Id;
        [Header("The ground")]
        public Material Ground;
        [Header("The horizon")]
        public TextAsset MountainHeights, MountainLayout;
        public Material Mountains;
        [Header("The sky")]
        public Material Sky;
        public TextAsset SkyJson;
        [Header("What stands on the ground")]
        public Material Foliage, FoliageDark, Wire, Vehicle, Sandbag, Timber;
        public PlantSet[] Plants = new PlantSet[0], Props = new PlantSet[0];
        public BagSet Bags;
        public StructureSet Structures;
        [Header("Where things grow and stand, metres (the simulation's x and z)")]
        /// <summary>The treeline's edge; the scrub's near and far edges; the wire's line; the firebase's east wall; the gap left in the wire where a lane runs out through it.</summary>
        public double TreelineZ = -24, ScrubNearZ = -10.5, ScrubFarZ = -22, WireZ = 3.9, FirebaseEastX = -21, LaneGate = 3.4;

        /// <summary>Give each view what is the theatre's, before the world is built in it.</summary>
        public void Dress(GameRoot root)
        {
            if (root.GroundView != null && Ground != null) root.GroundView.Material = Ground;
            if (root.Mountains != null)
            {
                root.Mountains.Heights = MountainHeights;
                root.Mountains.Layout = MountainLayout;
                root.Mountains.Material = Mountains;
            }
            if (root.CoverView != null)
            {
                root.CoverView.SandbagMaterial = Sandbag;
                root.CoverView.TimberMaterial = Timber;
                root.CoverView.Bags = Bags;
                root.CoverView.Structures = Structures;
            }
            var d = root.Dressing;
            if (d != null)
            {
                d.Foliage = Foliage; d.FoliageDark = FoliageDark; d.Wire = Wire; d.Vehicle = Vehicle;
                d.Timber = Timber; d.Sandbag = Sandbag;
                d.Plants = Plants; d.Props = Props;
                d.TreelineZ = TreelineZ; d.ScrubNearZ = ScrubNearZ; d.ScrubFarZ = ScrubFarZ;
                d.WireZ = WireZ; d.FirebaseEastX = FirebaseEastX; d.LaneGate = LaneGate;
            }
            // The sky, and the light it gives (SkyLighting reads its photograph again).
            if (Sky != null && RenderSettings.skybox != Sky)
            {
                RenderSettings.skybox = Sky;
                var light = UnityEngine.Object.FindAnyObjectByType<SkyLighting>();
                if (light != null) { light.SkyMaterial = Sky; if (SkyJson != null) light.SkyJson = SkyJson; light.Apply(); }
            }
        }
    }
}
