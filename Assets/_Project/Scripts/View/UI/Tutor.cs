using System.Collections.Generic;
using UnityEngine;

namespace LanesOfVietnam.View.UI
{
    /// <summary>
    /// One line of text the first time each system happens to you, on the
    /// frame it happens, and never again — the 2D game's tutor (js/tutor.js),
    /// whose premise holds: a strategy game whose rules are invisible is not
    /// hard, it is opaque, and the moment a rule matters is the only moment
    /// anyone reads it.
    ///
    /// Once ever (kept in PlayerPrefs, best effort: a blocked store only means
    /// the lesson repeats). Never two at once. Never in the first seconds of a
    /// match. It teaches what to do, not what the system is called.
    /// </summary>
    public sealed class Tutor
    {
        public static readonly Dictionary<string, string> Lines = new Dictionary<string, string>
        {
            ["select"] = "SELECTED: ITS TAG SAYS WHAT IT IS DOING. 1 ADVANCE · 2 HOLD · 4 FALL BACK · 0 LET THEM DECIDE",
            ["cover"] = "IN COVER. PACK IT PAST ITS ROOM AND EVERY MAN IN IT IS WORSE OFF",
            ["pinned"] = "PINNED: MEN UNDER FIRE GO FLAT AND STOP. SUPPRESS BACK, SMOKE THEM OUT, OR SEND HELP",
            ["ranged"] = "THAT POSITION IS RANGED IN. MOVE, OR THE NEXT ROUNDS LAND ON YOU",
            ["broken"] = "A SQUAD HAS BROKEN. IT TAKES NO ORDERS UNTIL IT RALLIES",
            ["concealed"] = "NOBODY IS SEEN UNTIL HE IS SPOTTED: A MAN STANDING OR MOVING FROM FAR OFF, A MAN WHO FIRES AT ONCE",
            ["bound"] = "BOUNDING: ONE SQUAD MOVES WHILE ANOTHER KEEPS THEIR HEADS DOWN",
            ["contact"] = "CONTACT: A SQUAD THAT SEES THE ENEMY GOES TO GROUND AND TAKES THE NEAREST COVER",
            ["lanes"] = "EACH LANE IS ITS OWN FIGHT. MEN FIRE FROM A KNEE OR FLAT, DOWN THEIR LANE; ONLY WHAT IS THROWN OR LOBBED CROSSES",
            ["trap"] = "A TRAP HAS SPRUNG. EVERYONE NEAR IT GOES FLAT",
            ["glasses"] = "HOLD F FOR FIELD GLASSES",
            ["lever"] = "HOLD KEEPS A SQUAD IN A STRONGPOINT · GO SENDS IT ON · RIGHT CLICK: ITS OWN JUDGEMENT · NOT EVERYONE FITS",
            ["armed"] = "POINT AT A LANE, OR A SQUAD IN IT · ↑ ↓ THE OTHER LANE · CLICK TO PLACE · RIGHT CLICK OR ESC TO CANCEL",
        };

        private const string Key = "lov_taught";
        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly Queue<string> _queue = new Queue<string>();
        private float _hold;

        /// <summary>The line showing now, or null.</summary>
        public string Showing { get; private set; }

        /// <summary>Seconds of match before anything is taught: the player is deploying, not reading.</summary>
        public const float Quiet = 6f;
        public const float HoldSeconds = 5.2f;

        public Tutor()
        {
            try
            {
                foreach (var k in PlayerPrefs.GetString(Key, "").Split(','))
                    if (k.Length > 0) _seen.Add(k);
            }
            catch { /* storage blocked: lessons repeat, nothing breaks */ }
        }

        /// <summary>Queue a lesson if it has never been given. Safe to call every frame.</summary>
        public void Teach(string key)
        {
            if (_seen.Contains(key) || !Lines.ContainsKey(key) || _queue.Contains(key) || Showing == Lines[key]) return;
            _queue.Enqueue(key);
        }

        public void Update(float dt, float matchSeconds)
        {
            if (_hold > 0)
            {
                _hold -= dt;
                if (_hold <= 0) Showing = null;
                return;
            }
            if (matchSeconds < Quiet || _queue.Count == 0) return;
            string k = _queue.Dequeue();
            _seen.Add(k);
            try
            {
                PlayerPrefs.SetString(Key, string.Join(",", _seen));
                PlayerPrefs.Save();
            }
            catch { /* best effort */ }
            Showing = Lines[k];
            _hold = HoldSeconds;
        }

        /// <summary>Forget everything taught, so the next match teaches it all again.</summary>
        public void Reset()
        {
            _seen.Clear();
            _queue.Clear();
            try { PlayerPrefs.DeleteKey(Key); } catch { }
        }
    }
}
