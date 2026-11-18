using System.Collections.Generic;
using System.Linq;
using LanesOfVietnam.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace LanesOfVietnam.View.UI
{
    /// <summary>
    /// The campaign kept between sessions (PLAN §12.76): its state as JSON in PlayerPrefs, which in a WebGL build is the
    /// browser's own storage. Nothing goes to a server.
    /// </summary>
    public static class CampaignStore
    {
        public const string Key = "lov.campaign";

        public static CampaignState Load()
        {
            if (!PlayerPrefs.HasKey(Key)) return null;
            try { return JsonUtility.FromJson<CampaignState>(PlayerPrefs.GetString(Key)); }
            catch (System.Exception) { return null; }
        }

        public static void Save(CampaignState c)
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(c));
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// The campaign's screens (PLAN §12.57, built with Warfare 1944's in §12.76): the five operations in their order and
    /// where the campaign stands, and the requisition, where merit buys the side's doctrine. Like every screen, each
    /// action is a public method the buttons only call.
    /// </summary>
    public sealed partial class Screens
    {
        public VisualElement CampaignPanel { get; private set; }
        public VisualElement RequisitionPanel { get; private set; }
        /// <summary>The five battles, a row each: its number and name, the side, and where it stands.</summary>
        public VisualElement CampaignList { get; private set; }
        private readonly List<Label[]> _battleRows = new List<Label[]>();
        public Label CampaignMerit { get; private set; }
        public Label ReqMerit { get; private set; }
        public Label ReqHave { get; private set; }

        /// <summary>The campaign being fought, once the campaign screen has been opened; kept by CampaignStore.</summary>
        public CampaignState Campaign { get; private set; }

        private readonly Dictionary<string, Button> _perkButtons = new Dictionary<string, Button>();
        private readonly List<(Side side, VisualElement row)> _perkRows = new List<(Side, VisualElement)>();
        /// <summary>The ending last shown was a campaign battle's, and what it said of the campaign.</summary>
        private bool _campaignEnding;
        private SimState _campaignAfterFor;

        private void BuildCampaign(VisualElement root)
        {
            CampaignPanel = new VisualElement { name = "campaign" };
            CampaignPanel.AddToClassList("screen");
            CampaignPanel.AddToClassList("start");
            Text(CampaignPanel, "CAMPAIGN", "title");
            Text(CampaignPanel, "1965 · 1966 · FIVE OPERATIONS IN THE ORDER THEY WERE FOUGHT", "kicker");
            CampaignList = new VisualElement(); CampaignList.AddToClassList("camp-list"); CampaignPanel.Add(CampaignList);
            for (int b = 0; b < Sim.Campaign.Run.Length; b++)
            {
                var r = new VisualElement(); r.AddToClassList("camp-row"); CampaignList.Add(r);
                _battleRows.Add(new[] { Text(r, "", "camp-name"), Text(r, "", "camp-side"), Text(r, "", "camp-state") });
            }
            CampaignMerit = Text(CampaignPanel, "", "kicker");
            Text(CampaignPanel,
                "A battle won pays three merit, one lost pays one and is fought again. Merit buys doctrine; the enemy earns his own " +
                "as you do. The squads that come through a battle wait for you in the next.", "brief-text");
            var row = new VisualElement(); row.AddToClassList("choice-row"); CampaignPanel.Add(row);
            Btn(row, "camp-fight", "FIGHT", FightCampaign, "deploy");
            Btn(row, "camp-req", "REQUISITION", OpenRequisition, "choice").AddToClassList("camp-req");
            var row2 = new VisualElement(); row2.AddToClassList("choice-row"); CampaignPanel.Add(row2);
            Btn(row2, "camp-new", "NEW CAMPAIGN", NewCampaign, "choice");
            Btn(row2, "camp-back", "BACK", ToStart, "choice");
            root.Add(CampaignPanel);

            RequisitionPanel = new VisualElement { name = "requisition" };
            RequisitionPanel.AddToClassList("screen");
            RequisitionPanel.AddToClassList("requisition");
            Text(RequisitionPanel, "REQUISITION", "title");
            ReqMerit = Text(RequisitionPanel, "", "kicker");
            foreach (var side in new[] { Side.Us, Side.Vc })
                foreach (var branch in new[] { Branch.Training, Branch.Equipment })
                {
                    var head = new VisualElement(); head.AddToClassList("choice-row"); RequisitionPanel.Add(head);
                    Text(head, branch == Branch.Training ? "TRAINING" : "EQUIPMENT", "field");
                    _perkRows.Add((side, head));
                    for (int tier = 1; tier <= 3; tier++)
                    {
                        var tr = new VisualElement(); tr.AddToClassList("choice-row"); RequisitionPanel.Add(tr);
                        _perkRows.Add((side, tr));
                        foreach (var p in Doctrine.For(side).Where(x => x.Branch == branch && x.Tier == tier))
                        {
                            var perk = p;
                            var b = Btn(tr, $"perk-{p.Id}", p.Name, () => BuyPerk(perk.Id), "choice");
                            b.AddToClassList("perk");
                            _perkButtons[p.Id] = b;
                        }
                    }
                }
            ReqHave = Text(RequisitionPanel, "", "brief-text");
            Btn(RequisitionPanel, "req-done", "DONE", OpenCampaign, "deploy");
            root.Add(RequisitionPanel);
        }

        // --- actions ------------------------------------------------------------

        /// <summary>The campaign screen: the campaign kept from before, or a new one at the level chosen.</summary>
        public void OpenCampaign()
        {
            Campaign ??= CampaignStore.Load() ?? new CampaignState { Level = ChosenLevel };
            Enter(FlowState.Campaign);
        }

        /// <summary>Begin again: a new campaign at the level chosen, the one before forgotten.</summary>
        public void NewCampaign()
        {
            Campaign = new CampaignState { Level = ChosenLevel };
            CampaignStore.Save(Campaign);
            SyncCampaign();
        }

        public void OpenRequisition()
        {
            if (Campaign == null || Campaign.Over) return;
            Enter(FlowState.Requisition);
        }

        /// <summary>Buy a perk for the side the campaign's next battle is fought as, if it can be.</summary>
        public void BuyPerk(string id)
        {
            if (Campaign == null || Campaign.Over) return;
            if (Sim.Campaign.Buy(Campaign, Sim.Campaign.Playing(Campaign), Doctrine.Find(id))) CampaignStore.Save(Campaign);
            SyncCampaign();
        }

        /// <summary>Fight the campaign's next battle.</summary>
        public void FightCampaign()
        {
            if (Campaign == null || Campaign.Over) return;
            Root.NewMatch(Sim.Campaign.Playing(Campaign), ChosenLength, campaign: Campaign);
            Enter(FlowState.Opening);
        }

        /// <summary>Back to the start screen from the campaign's.</summary>
        public void ToStart() => Enter(FlowState.Start);

        /// <summary>
        /// The end of a campaign battle (once for each match): merit paid, and won, the veterans carried and the campaign on;
        /// kept. What the ending says of it.
        /// </summary>
        private string CampaignAfter(SimState st)
        {
            var side = Sim.Campaign.Playing(Campaign);
            int vetsBefore = Campaign.Veterans(side).Count;
            bool won = Sim.Campaign.After(Campaign, st);
            CampaignStore.Save(Campaign);
            if (!won) return $"MERIT +{Sim.Campaign.LossMerit} · FIGHT IT AGAIN, WITH WHAT YOU HAVE BOUGHT";
            int carried = Campaign.Veterans(side).Count;
            string vets = carried == 0 ? "NO SQUAD CAME THROUGH" : $"{carried} SQUAD{(carried == 1 ? "" : "S")} CARRY ON AS VETERANS";
            return Campaign.Over ? $"MERIT +{Sim.Campaign.WinMerit} · {vets} · THE CAMPAIGN IS WON" : $"MERIT +{Sim.Campaign.WinMerit} · {vets}";
        }

        // --- what the screens show ------------------------------------------------

        private static string SideName(Side s) => s == Side.Us ? "US / ARVN" : "VC / NVA";

        private void SyncCampaign()
        {
            if (CampaignPanel == null || Campaign == null) return;
            var c = Campaign;
            for (int b = 0; b < Sim.Campaign.Run.Length; b++)
            {
                var (id, side) = Sim.Campaign.Run[b];
                var row = _battleRows[b];
                row[0].text = $"{b + 1}  {Scenarios.ById(id).Title}";
                row[1].text = side == Side.Us ? "US" : "VC";
                row[2].text = b < c.Battle ? "WON" : b == c.Battle ? (c.Tries > 0 ? $"NEXT · LOST {c.Tries}" : "NEXT") : "";
                foreach (var l in row) { l.EnableInClassList("won", b < c.Battle); l.EnableInClassList("next", b == c.Battle); }
            }
            CampaignMerit.text = $"MERIT  US {c.Merit[0]}  ·  VC {c.Merit[1]}    ·    {c.Level.ToString().ToUpperInvariant()}";
            if (Buttons.TryGetValue("camp-fight", out var fight))
            {
                fight.text = c.Over ? "THE CAMPAIGN IS WON" : $"FIGHT · {Sim.Campaign.Next(c).Title}";
                fight.SetEnabled(!c.Over);
            }
            if (Buttons.TryGetValue("camp-req", out var req)) req.SetEnabled(!c.Over);
            if (c.Over) return;

            var me = Sim.Campaign.Playing(c);
            ReqMerit.text = $"{SideName(me)} · MERIT {c.Merit[(int)me]} · A PERK COSTS ITS TIER, AND A TIER NEEDS ONE MORE IN ITS BRANCH THAN THE TIER BELOW";
            foreach (var (side, row) in _perkRows) row.style.display = side == me ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var p in Doctrine.For(me))
            {
                if (!_perkButtons.TryGetValue(p.Id, out var b)) continue;
                string why = Sim.Campaign.Blocked(c, me, p);
                bool bought = why == "bought";
                string status = bought ? "BOUGHT" : why ?? $"COST {p.Cost}";
                string text = $"{p.Name} · {p.Tier}\n{p.Text}\n{status.ToUpperInvariant()}";
                if (b.text != text) b.text = text;
                b.EnableInClassList("on", bought);
                b.SetEnabled(bought || why == null);
            }
            var vets = c.Veterans(me);
            string Name(string card) => Deck.Find(me, card)?.Name ?? card;
            string have = "CARDS  " + string.Join(" · ", Sim.Campaign.Roster(c, me).Select(Name));
            string waiting = vets.Count == 0 ? "VETERANS  NONE YET"
                : "VETERANS  " + string.Join(" · ", vets.Select(v => $"{Name(v.Card)} ({v.Men.Length} MEN, {v.Mean * 100:0}% STEADY)"));
            string against = Sim.Campaign.Against(c, me).Length == 0 ? "THE ENEMY  NO DOCTRINE YET"
                : "THE ENEMY  " + string.Join(" · ", Sim.Campaign.Against(c, me).Select(id => Doctrine.Find(id).Name));
            ReqHave.text = $"{have}\n{waiting}\n{against}";
        }
    }
}
