using System.Collections.Generic;

namespace LanesOfVietnam.Sim
{
    public enum CommandKind { Order, Buy, Lever }

    /// <summary>
    /// Something a player asked for. Commands are queued and applied at the
    /// start of the next tick, never in the middle of one, so a match is a
    /// pure function of (seed, plans, commands) and the command log replays it.
    /// </summary>
    public struct Command
    {
        public CommandKind Kind;
        public Side Side;

        /// <summary>Order: the squad, and the order (null hands it back to the plan).</summary>
        public int Squad;
        public Order? Order;

        /// <summary>Buy: which card, into which lane, aimed where along X.</summary>
        public string Card;
        public int Lane;
        public double AimX;

        public static Command OrderSquad(Side side, int squad, Order? order)
            => new Command { Kind = CommandKind.Order, Side = side, Squad = squad, Order = order };

        /// <summary>Lever: the position (a cover id), and where the side's lever on it goes (MatchOptions.Fieldcraft).</summary>
        public int Cover;
        public Lever Lever;

        public static Command Buy(Side side, string card, int lane, double aimX)
            => new Command { Kind = CommandKind.Buy, Side = side, Card = card, Lane = lane, AimX = aimX };

        public static Command SetLever(Side side, int cover, Lever lever)
            => new Command { Kind = CommandKind.Lever, Side = side, Cover = cover, Lever = lever };
    }

    /// <summary>
    /// A match being played: the state, its random stream, and the commands
    /// that have been applied to it. This is what the view steps at 20 Hz.
    /// </summary>
    public sealed class LiveMatch
    {
        public readonly MatchOptions Options;
        public readonly SimState State;
        private readonly Rng _rng;
        private readonly Dictionary<int, int> _original;
        private readonly List<Command> _pending = new List<Command>();

        public struct Applied
        {
            public int Tick;
            public Command Command;
            public bool Accepted;
        }

        /// <summary>Every command applied, with the tick it took effect before.</summary>
        public readonly List<Applied> Log = new List<Applied>();

        public LiveMatch(MatchOptions options)
        {
            Options = options;
            State = Match.Create(options);
            _rng = new Rng(options.Seed).Fork("sim");
            _original = Match.OriginalStrengths(State);
        }

        public int Cap => Options.MaxTicks ?? Tune.MaxTicks;

        /// <summary>
        /// The event count after this tick's commands were applied and before
        /// the tick itself ran: events from here on are the tick's own.
        /// </summary>
        public int TickEventsFrom { get; private set; }

        public void Issue(Command c) => _pending.Add(c);

        /// <summary>Apply any queued commands, then advance one tick.</summary>
        public void Step()
        {
            if (State.Over) { _pending.Clear(); return; }
            for (int i = 0; i < _pending.Count; i++)
            {
                var c = _pending[i];
                Log.Add(new Applied { Tick = State.Tick + 1, Command = c, Accepted = Apply(c) });
            }
            _pending.Clear();
            TickEventsFrom = State.Events.Count;
            Match.Step(State, Options.Us, Options.Vc, _rng, _original, Cap);
        }

        private bool Apply(Command c)
        {
            switch (c.Kind)
            {
                case CommandKind.Lever:
                    return Fieldcraft.SetLever(State, c.Side, c.Cover, c.Lever);

                case CommandKind.Order:
                    if (c.Squad < 0 || c.Squad >= State.Squads.Count) return false;
                    // A player commands his own side only.
                    if (State.Squads[c.Squad].Side != c.Side) return false;
                    return Match.OrderSquad(State, c.Squad, c.Order);

                case CommandKind.Buy:
                    var card = Deck.Find(c.Side, c.Card);
                    if (card == null) return false;
                    int before = State.Squads.Count;
                    bool ok = Deck.Buy(State, c.Side, card, c.Lane, _rng, c.AimX);
                    // A bought squad's strength is recorded when it arrives, so it
                    // can break like any other. The original only caught up on
                    // this at the next reinforcement check, up to six seconds
                    // later, by which time the squad could already be half dead
                    // and would be measured against that.
                    for (int i = before; i < State.Squads.Count; i++)
                    {
                        _original[State.Squads[i].Id] = Squads.Roster(State, State.Squads[i].Id).Count;
                    }
                    return ok;
            }
            return false;
        }

        /// <summary>Replay a match from its seed, plans and command log.</summary>
        public static LiveMatch Replay(MatchOptions options, IReadOnlyList<Applied> log)
        {
            var m = new LiveMatch(options);
            int k = 0;
            while (!m.State.Over && m.State.Tick < m.Cap)
            {
                while (k < log.Count && log[k].Tick == m.State.Tick + 1) m.Issue(log[k++].Command);
                m.Step();
            }
            return m;
        }
    }
}
