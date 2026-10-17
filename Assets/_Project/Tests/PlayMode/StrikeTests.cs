using System.Collections;
using System.Linq;
using LanesOfVietnam.Sim;
using LanesOfVietnam.View;
using LanesOfVietnam.View.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace LanesOfVietnam.Tests
{
    /// <summary>
    /// The air strike, bombs and then napalm, in the game (MatchOptions.Strike, PLAN §12.61; the owner, 2026-10-17: "make
    /// the airstrike more expensive and OP, like it should destory everything in its path"): its card shows its price; its
    /// bombs go off larger than shells and leave craters; the positions they wreck are gone from the field; its napalm is
    /// drawn burning along its line; the men it burns lie charred; it is heard.
    ///
    /// <code>tools/unity.sh -runTests -testPlatform PlayMode -testCategory Strike -testResults "$PWD/Logs/strike.xml"</code>
    /// </summary>
    public class StrikeTests
    {
        private static GameRoot Root => GameRoot.Instance;

        private static IEnumerator Ticks(int n)
        {
            Root.Driver.FastForward(n);
            yield return null;
            yield return null;
        }

        [UnityTest, Category("Strike")]
        public IEnumerator The_air_strike_wrecks_what_it_falls_on_and_burns_along_its_line()
        {
            yield return UIAuditInterfaceTests.LoadAndDeploy();
            Root.Paused = true;
            var st = Root.Driver.State;
            Assert.IsTrue(st.Strike, "the game plays without the strike's rule");
            var combat = Object.FindAnyObjectByType<CombatView>();
            var cover = Object.FindAnyObjectByType<CoverView>();
            var audio = Object.FindAnyObjectByType<AudioView>();
            var rec = audio.Out as RecordingSoundOut;

            // Its card, at its price.
            var hud = Object.FindAnyObjectByType<Hud>();
            var card = hud.GetComponent<UIDocument>().rootVisualElement.Q<CardView>("card-us-airstrike");
            Assert.IsNotNull(card, "no air strike card");
            yield return null;
            Assert.IsTrue(card.Q<Label>(className: "card-cost").text == Tune.StrikeCost.ToString(), $"its card says {card.Q<Label>(className: "card-cost").text}, not {Tune.StrikeCost}");

            // Called on a position of the far lane with men of the VC put in it.
            var c = st.Cover.First(x => !x.Wrecked && x.Kind != CoverKind.Crater && System.Math.Abs(x.Z - Tune.Lanes[Map.Far]) < 4.5 && x.X > 0 && x.X < 30);
            var vc = st.Men.Where(x => x.Alive && x.Side == Side.Vc && x.High <= 0 && x.Climb == 0).Take(2).ToList();
            foreach (var m in vc) { m.X = c.X; m.Z = c.Z; }
            // (Two more squads of theirs, in the other lane at their own end: the bombs can kill all there was of the VC this
            // early, and the match would be over before the napalm.)
            st.Cp[(int)Side.Vc] = 200;
            foreach (var id in new[] { "vc-squad", "vc-cell" }) Assert.IsTrue(Deck.Buy(st, Side.Vc, Deck.Find(Side.Vc, id), Map.Near, new Rng(5)), $"{id}: {Deck.Blocked(st, Side.Vc, Deck.Find(Side.Vc, id))}");
            st.Cp[(int)Side.Us] = Tune.StrikeCost;
            rec?.Played.Clear();
            Root.Driver.Match.Issue(Command.Buy(Side.Us, "us-airstrike", Map.Far, c.X));
            yield return Ticks(2);
            Assert.Less(st.Cp[(int)Side.Us], 1.0, "it did not cost its price");

            // The bombs: bigger than a shell, the position gone from the field.
            yield return Ticks(Tune.JetWait + Tune.StickBombs * Tune.StickGap);
            Assert.IsTrue(st.Events.Any(e => e.Kind == EventKind.Shell && e.Rounds == Inbound.Jet), "no bomb went off");
            Assert.IsTrue(c.Wrecked, "the position the strike was called on was not wrecked");
            yield return null;
            Assert.IsTrue(cover.IsWrecked(c.Id), "a wrecked position is still drawn standing");
            if (rec != null) Assert.IsTrue(rec.Played.Any(p => p.name.StartsWith("bomb")), "no bomb was heard");

            // The napalm: lit, drawn burning; whoever is put in it burns and lies charred.
            yield return Ticks(Tune.NapalmDelay + 4);
            var fire = st.Areas.FirstOrDefault(a => a.Kind == AreaKind.Napalm);
            Assert.IsNotNull(fire, "no napalm");
            Assert.AreEqual(0, fire.Next, "the napalm is not alight");
            int quads = 0;
            for (int i = 0; i < 8; i++) { Root.AdvanceForCapture(0.05f); yield return null; quads = System.Math.Max(quads, combat.Puffs); }
            Assert.Greater(quads, 20, "the napalm is not drawn burning");
            if (rec != null) Assert.IsTrue(rec.Played.Any(p => p.name.StartsWith("fire") || p.name.StartsWith("napalm")), "the napalm was not heard");
            var caught = st.Men.First(x => x.Alive && x.Side == Side.Vc && x.High <= 0 && x.Climb == 0);
            for (int i = 0; i < 60 && caught.Alive; i++) { caught.X = fire.X; caught.Z = fire.Z; Root.AdvanceForCapture(0.05f); yield return null; }
            Assert.IsFalse(caught.Alive, "a man in the napalm did not burn");
            for (int i = 0; i < 6; i++) { Root.AdvanceForCapture(0.05f); yield return null; }
            var figure = Object.FindObjectsByType<SoldierFigure>(FindObjectsSortMode.None).FirstOrDefault(f => f.IsCharred);
            Assert.IsNotNull(figure, "the man it burned is not drawn charred");
        }

        /// <summary>
        /// NAPALM on the forest, the Americans' last resort (PLAN §12.62), on Rice Fields: its card is on the HUD and says it
        /// is a last resort while they are not losing; losing, it is theirs; laid on the forest, the forest burns: the
        /// trees' shader is told where (its globals), the fire is drawn, and the burned stretch stays.
        /// </summary>
        [UnityTest, Category("Strike")]
        public IEnumerator Napalm_on_the_forest_burns_it_and_it_stays_burned()
        {
            yield return RiverTests.LoadRiceFields();
            Root.Paused = true;
            var st = Root.Driver.State;
            var combat = Object.FindAnyObjectByType<CombatView>();
            var hud = Object.FindAnyObjectByType<Hud>();
            var card = hud.Cards.Single(c => c.Card.Id == "us-napalm");
            for (int i = 0; i < 4; i++) { Root.AdvanceForCapture(0.5f); yield return null; }
            Assert.AreEqual(DisplayStyle.Flex, card.resolvedStyle.display, "no napalm card on a map with a forest");
            // (On into the fight: in its opening nobody is losing yet.)
            for (int i = 0; i < 120 && st.Phase != Phase.Fight; i++) Root.Driver.FastForward(Tune.TickHz);
            Assume.That(st.Phase, Is.EqualTo(Phase.Fight), "no fight in two minutes");
            st.Cp[(int)Side.Us] = 200;
            st.Morale[(int)Side.Us] = 0.9f;
            yield return null; yield return null;
            Assert.IsTrue(card.Q<Label>("card-note") != null && card.Q<Label>("card-note").text == "LAST RESORT", "its card does not say it is a last resort");
            Assert.IsTrue(card.Poor, "it is to be had while the Americans are not losing");

            st.Morale[(int)Side.Us] = Tune.LastResortWill - 0.05;
            yield return null; yield return null;
            Assert.IsFalse(card.Poor, "losing, it is not to be had");
            var f = st.Forests[0];
            double aim = (f.X0 + f.X1) * 0.5;
            Root.Driver.Match.Issue(Command.Buy(Side.Us, "us-napalm", Map.Far, aim));
            Root.Driver.FastForward(Tune.JetWait + 4);
            yield return null; yield return null;
            Assert.AreEqual(1, st.Burnt.Count, "the forest did not catch");
            Assert.Greater(Shader.GetGlobalFloat("_LOV_BurnCount"), 0f, "the trees are not told they are burning");
            int quads = 0;
            for (int i = 0; i < 8; i++) { Root.AdvanceForCapture(0.1f); yield return null; quads = System.Math.Max(quads, combat.Puffs); }
            Assert.Greater(quads, 20, "the forest is not drawn burning");
            // A minute on, it is still burned.
            Root.Driver.FastForward(60 * Tune.TickHz);
            yield return null; yield return null;
            if (!st.Over)
            {
                Assert.AreEqual(1, st.Burnt.Count);
                Assert.Greater(Shader.GetGlobalFloat("_LOV_BurnCount"), 0f, "the forest grew back");
            }
        }
    }
}
