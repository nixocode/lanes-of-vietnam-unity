using UnityEngine;
using UnityEngine.UIElements;

namespace LanesOfVietnam.View.UI
{
    /// <summary>
    /// A small sign drawn as a shape, not a character: a star, pause and play, a gear, an arrow. The game's fonts have
    /// none of these, and a WebGL build has no fonts of the machine to fall back on, so a character drew as an empty box
    /// in the browser (found 2026-11-18; the editor falls back on the Mac's fonts and hid it). Drawn in the colour the
    /// element inherits, so a button's hover and on colours and a plate's turned colour reach it; its size is its box's.
    /// </summary>
    public sealed class Glyph : VisualElement
    {
        public enum Shape { None, Star, Pause, Play, Gear, Up, Down, Left, Right }

        private Shape _kind;

        public Shape Kind
        {
            get => _kind;
            set { if (_kind == value) return; _kind = value; MarkDirtyRepaint(); }
        }

        public Glyph(Shape kind = Shape.None, string className = null)
        {
            _kind = kind;
            pickingMode = PickingMode.Ignore;
            if (className != null) AddToClassList(className);
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext mgc) => Paint(mgc, contentRect, _kind, resolvedStyle.color);

        /// <summary>
        /// A sign painted onto an element that has text of its own (a lane's tag, a side's name): as a child it would
        /// stop the element sizing itself to its text, so it is drawn by the element, in its padding, where place says.
        /// </summary>
        public sealed class Mark
        {
            private readonly VisualElement _host;
            private Shape _kind;

            public Mark(VisualElement host, Shape kind, System.Func<Rect, Rect> place)
            {
                _host = host; _kind = kind;
                host.generateVisualContent += mgc => Paint(mgc, place(new Rect(0, 0, host.layout.width, host.layout.height)), _kind, host.resolvedStyle.color);
            }

            public Shape Kind
            {
                get => _kind;
                set { if (_kind == value) return; _kind = value; _host.MarkDirtyRepaint(); }
            }
        }

        public static void Paint(MeshGenerationContext mgc, Rect r, Shape kind, Color color)
        {
            if (kind == Shape.None || r.width < 1f || r.height < 1f || float.IsNaN(r.width)) return;
            var p = mgc.painter2D;
            p.fillColor = color;
            float s = Mathf.Min(r.width, r.height);
            var c = r.center;
            float h = s * 0.5f;
            switch (kind)
            {
                case Shape.Star:
                    p.BeginPath();
                    for (int i = 0; i < 10; i++)
                    {
                        float a = -Mathf.PI / 2f + i * Mathf.PI / 5f, rad = i % 2 == 0 ? h : h * 0.42f;
                        var v = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
                        if (i == 0) p.MoveTo(v); else p.LineTo(v);
                    }
                    p.ClosePath(); p.Fill();
                    break;
                case Shape.Pause:
                    {
                        float bw = s * 0.3f, gap = s * 0.2f, top = c.y - h * 0.9f, height = h * 1.8f;
                        Rect(p, c.x - gap / 2f - bw, top, bw, height);
                        Rect(p, c.x + gap / 2f, top, bw, height);
                    }
                    break;
                case Shape.Gear:
                    {
                        // Eight teeth on a wheel, and the hole through it (filled odd-even, so the hole stays open).
                        const int teeth = 8;
                        float outer = h, inner = h * 0.72f, hole = h * 0.3f;
                        p.BeginPath();
                        for (int i = 0; i < teeth * 4; i++)
                        {
                            float a = (i - 0.5f) * Mathf.PI * 2f / (teeth * 4);
                            float rad = (i % 4 == 1 || i % 4 == 2) ? outer : inner;
                            var v = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
                            if (i == 0) p.MoveTo(v); else p.LineTo(v);
                        }
                        p.ClosePath();
                        p.MoveTo(c + new Vector2(hole, 0));
                        p.Arc(c, hole, 0f, 360f);
                        p.ClosePath();
                        p.Fill(FillRule.OddEven);
                    }
                    break;
                case Shape.Play: Triangle(p, c, h, 0f); break;
                case Shape.Right: Triangle(p, c, h * 0.85f, 0f); break;
                case Shape.Down: Triangle(p, c, h * 0.85f, 90f); break;
                case Shape.Left: Triangle(p, c, h * 0.85f, 180f); break;
                case Shape.Up: Triangle(p, c, h * 0.85f, 270f); break;
            }
        }

        private static void Rect(Painter2D p, float x, float y, float w, float h)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + w, y)); p.LineTo(new Vector2(x + w, y + h)); p.LineTo(new Vector2(x, y + h));
            p.ClosePath(); p.Fill();
        }

        /// <summary>A triangle pointing at the angle given (degrees, 0 to the right, 90 down: the panel's y runs down).</summary>
        private static void Triangle(Painter2D p, Vector2 c, float h, float degrees)
        {
            p.BeginPath();
            for (int i = 0; i < 3; i++)
            {
                float a = (degrees + i * 120f) * Mathf.Deg2Rad;
                var v = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * h;
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
            p.ClosePath(); p.Fill();
        }
    }
}
