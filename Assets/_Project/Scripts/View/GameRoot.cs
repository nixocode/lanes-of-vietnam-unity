using System;
using System.Globalization;
using LanesOfVietnam.Sim;
using UnityEngine;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// The game: builds the world from the map, runs the match, hands each
    /// frame to the views.
    ///
    /// The world is built at load from <see cref="Map"/> rather than baked into
    /// the scene — the ground, the cover, and (in the art pass) where every
    /// plant stands. It is all deterministic maths, it costs nothing to
    /// download, and the capture harness renders it through exactly this code
    /// rather than a copy of it: the three.js build's capture once aimed the
    /// camera 2.4 degrees lower than the game and made every band measurement
    /// meaningless.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance { get; private set; }

        public CameraRig CameraRig;
        public GroundView GroundView;
        public CoverView CoverView;
        public ArmyView ArmyView;

        public Side PlayerSide = Side.Us;

        public MatchDriver Driver { get; private set; }
        public Ground Ground { get; private set; }
        public int Seed { get; private set; }

        /// <summary>Simulation speed: 1x, 2x, 3x. The sim still ticks at 20 Hz of match time.</summary>
        public float Speed = 1f;
        public bool Paused;

        /// <summary>The view's clock: wind, flicker, anything animated. Frozen in a capture.</summary>
        public float ViewTime { get; private set; }

        private static readonly int LovTime = Shader.PropertyToID("_LovTime");

        private void Awake()
        {
            Instance = this;
            var cap = CaptureSettings.Active;
            Application.targetFrameRate = -1;

            Ground = Map.Ground();
            GroundView.Build(Ground);
            var cover = Map.Cover();
            CoverView.Build(cover, Ground);

            Seed = cap?.Seed ?? ChooseSeed();
            Driver = new MatchDriver(new MatchOptions
            {
                Seed = Seed, Us = Plan.Ceiling, Vc = Plan.Ceiling, Cover = cover,
            });
            Debug.Log($"[LOV] match seed {Seed}");

            if (cap != null)
            {
                Driver.FastForward(cap.Tick);
                ViewTime = cap.ViewTime;
                CameraRig.Focus(cap.CameraX, instant: true);
                CameraRig.SetDolly(cap.Dolly, instant: true);
                gameObject.AddComponent<CaptureRunner>();
            }
        }

        /// <summary>
        /// The seed is chosen by the view, never by the simulation, and it is
        /// logged: a match anyone saw can be replayed. <c>?seed=N</c> in the
        /// page address picks one.
        /// </summary>
        private static int ChooseSeed()
        {
            string url = Application.absoluteURL ?? "";
            int q = url.IndexOf("seed=", StringComparison.Ordinal);
            if (q >= 0)
            {
                int end = q + 5;
                while (end < url.Length && char.IsDigit(url[end])) end++;
                if (int.TryParse(url.Substring(q + 5, end - q - 5), NumberStyles.Integer, CultureInfo.InvariantCulture, out int s)) return s;
            }
            return Environment.TickCount & 0x7fffffff;
        }

        private void Update()
        {
            if (CaptureSettings.Active == null)
            {
                if (!Paused) Driver.Advance(Time.deltaTime * Speed);
                ViewTime += Time.deltaTime;
            }
            Shader.SetGlobalFloat(LovTime, ViewTime);
            ArmyView.Draw(Driver, Ground);
        }
    }
}
