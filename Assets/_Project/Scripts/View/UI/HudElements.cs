using System;
using System.Collections.Generic;
using LanesOfVietnam.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace LanesOfVietnam.View.UI
{
    /// <summary>A morale bar in segments, lit from the faction's own end. Brief §7.</summary>
    public sealed class MoraleBar : VisualElement
    {
        public const int Segments = 14;
        private readonly VisualElement[] _segs = new VisualElement[Segments];
        private readonly bool _fromRight;

        /// <summary>How many segments are lit — what UIAudit reads.</summary>
        public int Lit { get; private set; } = -1;

        public MoraleBar(Side side)
        {
            AddToClassList("morale");
            _fromRight = side == Side.Vc;
            for (int i = 0; i < Segments; i++)
            {
                var s = new VisualElement();
                s.AddToClassList("morale-seg");
                s.AddToClassList(side == Side.Us ? "us" : "vc");
                _segs[i] = s;
                Add(s);
            }
        }

        public void Set(double morale)
        {
            int lit = Mathf.Clamp(Mathf.RoundToInt((float)morale * Segments), 0, Segments);
            if (lit == Lit) return;
            Lit = lit;
            for (int i = 0; i < Segments; i++)
            {
                int idx = _fromRight ? Segments - 1 - i : i;
                _segs[idx].EnableInClassList("lit", i < lit);
            }
        }
    }

    /// <summary>
    /// The tactical strip: both lanes drawn schematically along the whole map,
    /// with every unit a pip, each side's front traced, and the camera's
    /// window over it (brief §7). Clicking it moves the camera there.
    ///
    /// Pips differ in shape as well as colour — squares for the US, triangles
    /// for the VC — because side is otherwise shown only by green against red,
    /// the classic colour-blindness failure (PLAN §12.4).
    /// </summary>
    public sealed class TacticalStrip : VisualElement
    {
        public static readonly Color Us = new Color32(146, 164, 103, 255);
        public static readonly Color Vc = new Color32(195, 104, 88, 255);
        private static readonly Color Line = new Color32(64, 64, 56, 255);
        private static readonly Color Window = new Color32(200, 196, 180, 200);

        public SimState State;
        public Side Viewer;
        public float CameraX;
        public float CameraHalfWidth = 12f;

        /// <summary>Pips drawn last repaint, for UIAudit.</summary>
        public int PipsDrawn { get; private set; }

        public event Action<float> Clicked;

        public const float Margin = 8f;
        public static float Extent => (float)Tune.HalfLength + Margin;

        public TacticalStrip()
        {
            AddToClassList("strip");
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(e =>
            {
                float t = e.localPosition.x / Mathf.Max(1f, contentRect.width);
                Clicked?.Invoke(Mathf.Lerp(-Extent, Extent, t));
                e.StopPropagation();
            });
        }

        private float X(double worldX, Rect r) => r.xMin + (float)((worldX + Extent) / (2 * Extent)) * r.width;

        private void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            var p = ctx.painter2D;
            if (State == null || r.width < 10) return;
            // Lanes top to bottom as they lie in the picture: far lane higher.
            float farY = r.yMin + r.height * 0.30f, nearY = r.yMin + r.height * 0.58f;
            float traceY = r.yMin + r.height * 0.86f;

            p.lineWidth = 1f;
            p.strokeColor = Line;
            foreach (var y in new[] { farY, nearY })
            {
                p.BeginPath(); p.MoveTo(new Vector2(r.xMin + 2, y)); p.LineTo(new Vector2(r.xMax - 2, y)); p.Stroke();
            }

            // Each side's front, as a bar from its own end to its furthest man.
            double usFront = State.Front[(int)Side.Us], vcFront = State.Front[(int)Side.Vc];
            Bar(p, r.xMin + 1, X(usFront, r), traceY - 3, 4, Us);
            Bar(p, X(vcFront, r), r.xMax - 1, traceY + 1, 4, Vc);

            PipsDrawn = 0;
            foreach (var m in State.Men)
            {
                if (!m.Alive) continue;
                // The strip shows what the viewer's side knows: a concealed
                // enemy is not on it.
                if (m.Side != Viewer && !m.Seen) continue;
                var sq = State.Squads[m.Squad];
                float y = sq.Lane == 0 ? nearY : farY;
                float x = X(m.X, r);
                if (m.Side == Side.Us) Square(p, x, y, 2.6f, Us);
                else Triangle(p, x, y, 3.2f, Vc);
                PipsDrawn++;
            }

            // The camera's window.
            float wx0 = X(CameraX - CameraHalfWidth, r), wx1 = X(CameraX + CameraHalfWidth, r);
            p.strokeColor = Window;
            p.lineWidth = 1.2f;
            p.BeginPath();
            p.MoveTo(new Vector2(wx0, r.yMin + 1)); p.LineTo(new Vector2(wx1, r.yMin + 1));
            p.LineTo(new Vector2(wx1, r.yMax - 1)); p.LineTo(new Vector2(wx0, r.yMax - 1));
            p.ClosePath();
            p.Stroke();
        }

        private static void Bar(Painter2D p, float x0, float x1, float y, float h, Color c)
        {
            if (x1 <= x0) return;
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y)); p.LineTo(new Vector2(x1, y));
            p.LineTo(new Vector2(x1, y + h)); p.LineTo(new Vector2(x0, y + h));
            p.ClosePath(); p.Fill();
        }

        private static void Square(Painter2D p, float x, float y, float s, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(x - s, y - s)); p.LineTo(new Vector2(x + s, y - s));
            p.LineTo(new Vector2(x + s, y + s)); p.LineTo(new Vector2(x - s, y + s));
            p.ClosePath(); p.Fill();
        }

        private static void Triangle(Painter2D p, float x, float y, float s, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(x, y - s)); p.LineTo(new Vector2(x + s, y + s * 0.8f));
            p.LineTo(new Vector2(x - s, y + s * 0.8f));
            p.ClosePath(); p.Fill();
        }
    }

    /// <summary>
    /// A card on the deck: portrait or call-in icon, stencil name, cost, the
    /// composition pips, and its hotkey. Dims when unaffordable; a shade
    /// falls from the top while it cools down (brief §7).
    /// </summary>
    public sealed class CardView : VisualElement
    {
        public readonly Card Card;
        private readonly VisualElement _cd;
        private readonly VisualElement _art;

        /// <summary>What the card shows now, for UIAudit: affordable, cooling, armed.</summary>
        public bool Poor { get; private set; }
        public float Cooling { get; private set; }
        public bool IsArmed { get; private set; }

        public event Action<Card> Clicked;

        public CardView(Card card, string key, Side side)
        {
            Card = card;
            bool call = card.Group == CardGroup.Call;
            AddToClassList("card");
            if (call) AddToClassList("call");

            _art = new VisualElement();
            _art.AddToClassList("card-art");
            _art.generateVisualContent += ctx =>
            {
                if (call) Icons.Draw(ctx.painter2D, card.Id, _art.contentRect);
                else Portraits.Draw(ctx.painter2D, card.Id, side, _art.contentRect);
            };
            Add(_art);

            var name = new Label(card.Name);
            name.AddToClassList("card-name");
            Add(name);
            var cost = new Label(card.Cost.ToString());
            cost.AddToClassList("card-cost");
            Add(cost);
            if (!call)
            {
                var pips = new VisualElement();
                pips.AddToClassList("card-pips");
                for (int i = 0; i < card.Pips; i++) { var pip = new VisualElement(); pip.AddToClassList("pip"); pips.Add(pip); }
                Add(pips);
            }

            _cd = new VisualElement();
            _cd.AddToClassList("card-cd");
            _cd.pickingMode = PickingMode.Ignore;
            Add(_cd);

            var k = new Label(key);
            k.AddToClassList("card-key");
            k.pickingMode = PickingMode.Ignore;
            Add(k);

            RegisterCallback<PointerDownEvent>(e => { Clicked?.Invoke(Card); e.StopPropagation(); });
        }

        public void Set(bool poor, float coolingFraction, bool armed)
        {
            if (poor != Poor) { Poor = poor; EnableInClassList("poor", poor); }
            if (armed != IsArmed) { IsArmed = armed; EnableInClassList("armed", armed); }
            if (Math.Abs(coolingFraction - Cooling) > 0.001f)
            {
                Cooling = coolingFraction;
                _cd.style.height = Length.Percent(Mathf.Clamp01(coolingFraction) * 100f);
            }
        }
    }

    /// <summary>
    /// Line-art call-in icons, drawn rather than loaded. Placeholders in the
    /// reference's style until the owner's Illustrator/Photoshop icons exist
    /// (PLAN §12.4); each is recognisable at card size, which is their job.
    /// </summary>
    public static class Icons
    {
        public static readonly Color Ink = new Color32(208, 198, 178, 255);

        public static void Draw(Painter2D p, string id, Rect r)
        {
            p.strokeColor = Ink;
            p.fillColor = Ink;
            p.lineWidth = 2.2f;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            var c = r.center;
            float s = Mathf.Min(r.width, r.height) * 0.36f;
            switch (id)
            {
                case "vc-punji":          // sharpened stakes in a row
                    for (int i = -2; i <= 2; i++)
                    {
                        float x = c.x + i * s * 0.32f, lean = i * 0.08f * s;
                        Line(p, new Vector2(x - lean, c.y + s * 0.7f), new Vector2(x + lean, c.y - s * 0.6f));
                    }
                    break;
                case "vc-tripwire":       // a grenade burst on a wire
                    p.BeginPath(); p.Arc(c + new Vector2(0, -s * 0.1f), s * 0.34f, 0, 360); p.Fill();
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4f;
                        var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        Line(p, c + new Vector2(0, -s * 0.1f) + d * s * 0.5f, c + new Vector2(0, -s * 0.1f) + d * s * 0.75f);
                    }
                    Line(p, new Vector2(r.xMin + 8, c.y + s * 0.75f), new Vector2(r.xMax - 8, c.y + s * 0.75f));
                    break;
                case "vc-spider":         // a rifle over a hole's lid
                    Line(p, c + new Vector2(-s * 0.9f, s * 0.05f), c + new Vector2(s * 0.8f, -s * 0.25f));
                    Line(p, c + new Vector2(-s * 0.2f, -s * 0.07f), c + new Vector2(-s * 0.1f, s * 0.2f));
                    p.BeginPath(); p.MoveTo(c + new Vector2(-s * 0.9f, s * 0.6f));
                    p.BezierCurveTo(c + new Vector2(-s * 0.4f, s * 0.2f), c + new Vector2(s * 0.4f, s * 0.2f), c + new Vector2(s * 0.9f, s * 0.6f));
                    p.Stroke();
                    break;
                case "vc-tunnel":         // a mound with a mouth in it
                    p.BeginPath(); p.MoveTo(c + new Vector2(-s, s * 0.6f));
                    p.BezierCurveTo(c + new Vector2(-s * 0.5f, -s * 0.8f), c + new Vector2(s * 0.5f, -s * 0.8f), c + new Vector2(s, s * 0.6f));
                    p.Stroke();
                    p.BeginPath(); p.MoveTo(c + new Vector2(-s * 0.3f, s * 0.6f));
                    p.BezierCurveTo(c + new Vector2(-s * 0.3f, s * 0.05f), c + new Vector2(s * 0.3f, s * 0.05f), c + new Vector2(s * 0.3f, s * 0.6f));
                    p.Fill();
                    break;
                case "us-arty":           // a falling shell and its trail
                    p.BeginPath();
                    p.MoveTo(c + new Vector2(s * 0.35f, s * 0.45f));
                    p.LineTo(c + new Vector2(-s * 0.15f, -s * 0.05f));
                    p.LineTo(c + new Vector2(0, -s * 0.2f));
                    p.LineTo(c + new Vector2(s * 0.5f, s * 0.3f));
                    p.ClosePath(); p.Fill();
                    for (int i = 0; i < 3; i++) Line(p, c + new Vector2(-s * (0.3f + i * 0.2f), -s * (0.35f + i * 0.2f) + i * s * 0.1f), c + new Vector2(-s * (0.6f + i * 0.2f), -s * (0.65f + i * 0.2f) + i * s * 0.1f));
                    break;
                case "us-smoke":          // a canister and its cloud
                    Line(p, c + new Vector2(-s * 0.15f, s * 0.8f), c + new Vector2(-s * 0.15f, s * 0.25f));
                    Line(p, c + new Vector2(s * 0.15f, s * 0.8f), c + new Vector2(s * 0.15f, s * 0.25f));
                    Line(p, c + new Vector2(-s * 0.15f, s * 0.8f), c + new Vector2(s * 0.15f, s * 0.8f));
                    foreach (var (dx, dy, rr) in new[] { (-0.35f, -0.1f, 0.28f), (0.05f, -0.35f, 0.34f), (0.42f, -0.05f, 0.26f) })
                    {
                        p.BeginPath(); p.Arc(c + new Vector2(dx * s, dy * s), rr * s, 0, 360); p.Stroke();
                    }
                    break;
                case "us-medevac":        // a cross in a ring
                    p.BeginPath(); p.Arc(c, s * 0.8f, 0, 360); p.Stroke();
                    p.lineWidth = s * 0.28f;
                    Line(p, c + new Vector2(0, -s * 0.45f), c + new Vector2(0, s * 0.45f));
                    Line(p, c + new Vector2(-s * 0.45f, 0), c + new Vector2(s * 0.45f, 0));
                    break;
                case "us-airstrike":      // a swept-wing jet diving in
                    p.BeginPath();
                    p.MoveTo(c + new Vector2(s * 0.9f, s * 0.35f));
                    p.LineTo(c + new Vector2(-s * 0.2f, -s * 0.1f));
                    p.LineTo(c + new Vector2(-s * 0.55f, -s * 0.7f));
                    p.LineTo(c + new Vector2(-s * 0.45f, -s * 0.05f));
                    p.LineTo(c + new Vector2(-s * 0.9f, -s * 0.1f));
                    p.LineTo(c + new Vector2(-s * 0.4f, s * 0.15f));
                    p.LineTo(c + new Vector2(-s * 0.6f, s * 0.75f));
                    p.ClosePath(); p.Fill();
                    break;
            }
        }

        private static void Line(Painter2D p, Vector2 a, Vector2 b)
        {
            p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
        }
    }

    /// <summary>
    /// Stand-in portraits: head and shoulders in the unit's headgear. PLAN
    /// §12.4 has the real ones rendered from the finished soldiers and graded in
    /// Photoshop; until the soldiers exist these say which unit it is — the
    /// M1 helmet, the pith helmet, the bare head — and nothing more.
    /// </summary>
    public static class Portraits
    {
        public static void Draw(Painter2D p, string id, Side side, Rect r)
        {
            var c = r.center;
            float s = r.height * 0.5f;
            var back = side == Side.Us ? new Color32(44, 48, 34, 255) : new Color32(46, 38, 30, 255);
            p.fillColor = back;
            p.BeginPath(); p.MoveTo(new Vector2(r.xMin + 8, r.yMin)); p.LineTo(new Vector2(r.xMax - 8, r.yMin));
            p.LineTo(new Vector2(r.xMax - 8, r.yMax)); p.LineTo(new Vector2(r.xMin + 8, r.yMax)); p.ClosePath(); p.Fill();

            var uniform = side == Side.Us ? new Color32(78, 84, 56, 255)
                        : id == "vc-cell" || id == "vc-sapper" ? new Color32(24, 23, 24, 255) : new Color32(120, 108, 72, 255);
            var skin = new Color32(176, 132, 100, 255);

            // Shoulders.
            p.fillColor = uniform;
            p.BeginPath();
            p.MoveTo(new Vector2(c.x - s * 1.05f, r.yMax));
            p.BezierCurveTo(new Vector2(c.x - s * 1.0f, c.y + s * 0.35f), new Vector2(c.x - s * 0.5f, c.y + s * 0.3f), new Vector2(c.x, c.y + s * 0.3f));
            p.BezierCurveTo(new Vector2(c.x + s * 0.5f, c.y + s * 0.3f), new Vector2(c.x + s * 1.0f, c.y + s * 0.35f), new Vector2(c.x + s * 1.05f, r.yMax));
            p.ClosePath(); p.Fill();
            // Head.
            p.fillColor = skin;
            p.BeginPath(); p.Arc(new Vector2(c.x, c.y - s * 0.02f), s * 0.36f, 0, 360); p.Fill();

            // Headgear by unit.
            if (side == Side.Us)
            {
                p.fillColor = new Color32(88, 96, 62, 255);          // M1 helmet with cover
                p.BeginPath();
                p.Arc(new Vector2(c.x, c.y - s * 0.08f), s * 0.46f, 180, 360);
                p.ClosePath(); p.Fill();
                p.fillColor = new Color32(60, 64, 44, 255);          // the band
                p.BeginPath(); p.MoveTo(new Vector2(c.x - s * 0.46f, c.y - s * 0.12f)); p.LineTo(new Vector2(c.x + s * 0.46f, c.y - s * 0.12f));
                p.LineTo(new Vector2(c.x + s * 0.46f, c.y - s * 0.2f)); p.LineTo(new Vector2(c.x - s * 0.46f, c.y - s * 0.2f)); p.ClosePath(); p.Fill();
            }
            else if (id == "vc-cell" || id == "vc-sapper")
            {
                p.fillColor = new Color32(20, 18, 18, 255);          // bare head, black hair
                p.BeginPath(); p.Arc(new Vector2(c.x, c.y - s * 0.12f), s * 0.36f, 190, 350); p.ClosePath(); p.Fill();
            }
            else
            {
                p.fillColor = new Color32(150, 132, 88, 255);        // pith helmet
                p.BeginPath(); p.Arc(new Vector2(c.x, c.y - s * 0.1f), s * 0.44f, 180, 360); p.ClosePath(); p.Fill();
                p.BeginPath(); p.MoveTo(new Vector2(c.x - s * 0.6f, c.y - s * 0.08f)); p.LineTo(new Vector2(c.x + s * 0.6f, c.y - s * 0.08f));
                p.LineTo(new Vector2(c.x + s * 0.5f, c.y - s * 0.16f)); p.LineTo(new Vector2(c.x - s * 0.5f, c.y - s * 0.16f)); p.ClosePath(); p.Fill();
            }
        }
    }
}
