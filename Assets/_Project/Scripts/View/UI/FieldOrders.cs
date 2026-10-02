using System;
using System.Collections.Generic;
using LanesOfVietnam.Sim;

namespace LanesOfVietnam.View.UI
{
    /// <summary>
    /// Field orders: brief §7's guided objectives, one line of HUD at a time —
    /// <c>ORDERS 2/5 — SET PUNJI STAKES IN THEIR PATH [Q]</c>.
    ///
    /// Ported from the 2D game's GUIDES (js/data.js), adapted to this
    /// simulation. Two rules carried over, because they are the point:
    ///
    /// <list type="bullet">
    /// <item><b>Guidance, never a gate.</b> Nothing here can block an action
    /// or decide a match. A player who ignores every order can still win.</item>
    /// <item><b>Each completes on something that happened</b> — a mark the
    /// player's own action raised, or a fact read from the simulation — never
    /// on a timer. Marks can arrive out of order and are remembered, so a
    /// player who calls artillery early is not asked to again later.</item>
    /// </list>
    ///
    /// The bracketed key is the hotkey of the card that does the thing.
    /// </summary>
    public sealed class FieldOrders
    {
        public sealed class Step
        {
            public string Text;
            /// <summary>Done when this mark has been raised...</summary>
            public string Mark;
            /// <summary>...or when this is true of the simulation.</summary>
            public Func<SimState, bool> Done;
        }

        public static IReadOnlyList<Step> For(Side side) => side == Side.Us ? Us : Vc;

        private static readonly Step[] Us =
        {
            new Step { Text = "DEPLOY A RIFLE SQUAD INTO A LANE [Z]", Mark = "deployed" },
            new Step { Text = "HOLD A STRONGPOINT — PULL ITS LEVER [H]", Mark = "lever:hold" },
            new Step { Text = "PUT FIRE ON THEIR LEAD ELEMENT", Done = st => Any(st, m => m.Side == Side.Vc && m.Pin >= Tune.PinDrop) },
            new Step { Text = "CALL A FIRE MISSION ON THEIR ADVANCE [Q]", Mark = "call:us-arty" },
            new Step { Text = "SEND THEM OVER THE TOP [G]", Mark = "lever:go" },
            new Step { Text = "BREAK THEM", Done = st => st.Morale[(int)Side.Vc] < 0.4 },
        };

        // The reference image's own orders line is step two of this list.
        private static readonly Step[] Vc =
        {
            new Step { Text = "DEPLOY A CELL INTO A LANE [Z]", Mark = "deployed" },
            new Step { Text = "SET PUNJI STAKES IN THEIR PATH [Q]", Mark = "call:vc-punji" },
            new Step { Text = "BURY A MAN IN A SPIDER HOLE [E]", Mark = "call:vc-spider" },
            new Step { Text = "DIG A TUNNEL PAST THEIR LINE [R]", Mark = "call:vc-tunnel" },
            new Step { Text = "TAKE THE NEAR LANE — BLEED THEIR WILL", Done = st => Match.LaneControl(st)[0].side == Side.Vc },
        };

        private static bool Any(SimState st, Func<Man, bool> f)
        {
            foreach (var m in st.Men) if (m.Alive && f(m)) return true;
            return false;
        }

        private readonly Side _side;
        private readonly HashSet<string> _marks = new HashSet<string>();
        private readonly bool[] _done;

        public FieldOrders(Side side)
        {
            _side = side;
            _done = new bool[For(side).Count];
        }

        public void Mark(string mark) => _marks.Add(mark);

        /// <summary>Re-evaluate against the state. Completed steps stay completed.</summary>
        public void Update(SimState st)
        {
            var steps = For(_side);
            for (int i = 0; i < steps.Count; i++)
            {
                if (_done[i]) continue;
                var s = steps[i];
                if ((s.Mark != null && _marks.Contains(s.Mark)) || (s.Done != null && s.Done(st))) { _done[i] = true; _line = null; }
            }
        }

        public int Completed
        {
            get { int n = 0; foreach (var d in _done) if (d) n++; return n; }
        }

        public int Count => _done.Length;

        /// <summary>The line the HUD shows: the first order not yet done.</summary>
        public string Line
        {
            get
            {
                // Written once for each order, not once a frame.
                if (_line != null) return _line;
                var steps = For(_side);
                for (int i = 0; i < steps.Count; i++)
                {
                    if (!_done[i]) return _line = $"ORDERS {i + 1}/{steps.Count} — {steps[i].Text}";
                }
                return _line = $"ORDERS {steps.Count}/{steps.Count} — CARRY ON";
            }
        }

        private string _line;
    }
}
