using System;
using System.Collections.Generic;
using LanesOfVietnam.Sim;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// Steps the simulation at its own fixed rate and keeps what the view
    /// needs to draw between ticks.
    ///
    /// The simulation never sees a frame: real time accumulates here and is
    /// spent in whole 50 ms ticks, and the remainder is how far the view is
    /// between the last two — the interpolation alpha. Stepping the sim from a
    /// frame's variable dt is the one thing brief §3 forbids outright.
    /// </summary>
    public sealed class MatchDriver
    {
        private struct Pose
        {
            public double X, Z;
        }

        public readonly LiveMatch Match;
        public SimState State => Match.State;

        private Pose[] _prev = new Pose[256];
        private Pose[] _curr = new Pose[256];
        private int _prevCount, _currCount;
        private double _accumulator;

        /// <summary>Seconds one frame may hand the simulation. A tab that slept for a minute does not run 1,200 ticks at once.</summary>
        public const double MaxCatchUp = Tune.Dt * 8;

        /// <summary>Ticks dropped by the catch-up clamp — reported, not hidden.</summary>
        public long ClampedTicks { get; private set; }

        /// <summary>0..1: how far between the previous tick and the current one this frame is.</summary>
        public float Alpha => (float)(_accumulator / Tune.Dt);

        /// <summary>Raised once per simulation tick, after it has run.</summary>
        public event Action<MatchDriver> Ticked;

        private readonly IReadOnlyList<LiveMatch.Applied> _replay;
        private int _replayNext;

        /// <summary>Playing back a recorded match: the player's controls are off and the log gives the orders.</summary>
        public bool IsReplay => _replay != null;

        public MatchDriver(MatchOptions options, IReadOnlyList<LiveMatch.Applied> replay = null)
        {
            _replay = replay;
            Match = new LiveMatch(options);
            Snapshot(ref _curr, out _currCount);
            Snapshot(ref _prev, out _prevCount);
        }

        /// <summary>Advance by real elapsed seconds, in whole ticks.</summary>
        public void Advance(double seconds)
        {
            if (seconds < 0) seconds = 0;
            if (seconds > MaxCatchUp)
            {
                ClampedTicks += (long)Math.Round((seconds - MaxCatchUp) * Tune.TickHz);
                seconds = MaxCatchUp;
            }
            _accumulator += seconds;
            while (_accumulator >= Tune.Dt && !State.Over)
            {
                StepOnce();
                _accumulator -= Tune.Dt;
            }
            if (State.Over) _accumulator = 0;
        }

        /// <summary>
        /// Run forward by whole ticks with nothing drawn, for capture and
        /// tests: "tick 600 of seed 3" is a place, and this is how the view
        /// gets there.
        /// </summary>
        public void FastForward(int ticks)
        {
            for (int i = 0; i < ticks && !State.Over; i++) StepOnce();
            _accumulator = 0;
        }

        private void StepOnce()
        {
            (_prev, _curr) = (_curr, _prev);
            _prevCount = _currCount;
            // A replay issues each recorded command before the tick it was
            // applied on, exactly as LiveMatch.Replay does, so the view plays
            // back the same match the player played.
            if (_replay != null)
            {
                while (_replayNext < _replay.Count && _replay[_replayNext].Tick == State.Tick + 1)
                    Match.Issue(_replay[_replayNext++].Command);
            }
            Match.Step();
            Snapshot(ref _curr, out _currCount);
            Ticked?.Invoke(this);
        }

        private void Snapshot(ref Pose[] into, out int count)
        {
            var men = State.Men;
            if (into.Length < men.Count) Array.Resize(ref into, Math.Max(men.Count, into.Length * 2));
            for (int i = 0; i < men.Count; i++) into[i] = new Pose { X = men[i].X, Z = men[i].Z };
            count = men.Count;
        }

        /// <summary>
        /// Where man <paramref name="id"/> is this frame, between ticks. A man
        /// who arrived on the latest tick has no previous pose and is drawn
        /// where he stands rather than sliding in from the origin.
        /// </summary>
        public (double x, double z) Position(int id)
        {
            var c = _curr[id];
            if (id >= _prevCount) return (c.X, c.Z);
            var p = _prev[id];
            double a = Alpha;
            return (p.X + (c.X - p.X) * a, p.Z + (c.Z - p.Z) * a);
        }

        /// <summary>Metres moved over the last tick: what a walk cycle advances by (PLAN §5.4 — distance, not time, or feet skate).</summary>
        public double StepLength(int id)
        {
            if (id >= _prevCount) return 0;
            var c = _curr[id];
            var p = _prev[id];
            double dx = c.X - p.X, dz = c.Z - p.Z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>How man <paramref name="id"/> moved over the last tick (sim x, z), for facing and gait.</summary>
        public (double dx, double dz) LastStep(int id)
        {
            if (id >= _prevCount) return (0, 0);
            return (_curr[id].X - _prev[id].X, _curr[id].Z - _prev[id].Z);
        }

        public IReadOnlyList<SimEvent> Events => State.Events;
    }
}
