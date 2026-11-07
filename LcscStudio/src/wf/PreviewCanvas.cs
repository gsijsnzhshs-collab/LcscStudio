using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using LcscLoader;

namespace LcscStudio
{
    internal sealed class SymbolCanvas : Control
    {
        private EeSymbol _symbol;

        public SymbolCanvas()
        {
            DoubleBuffered = true;
            BackColor = Theme.Canvas;
        }

        public EeSymbol Symbol
        {
            get => _symbol;
            set { _symbol = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Canvas);
            DrawGrid(g);
            if (_symbol == null)
            {
                DrawEmpty(g, "无符号数据");
                return;
            }

            var originX = _symbol.OriginX;
            var originY = _symbol.OriginY;
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            void Add(double x, double y)
            {
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }

            double MX(double units) => (units - originX) * EeUnits.MmPerUnit;
            double MY(double units) => (originY - units) * EeUnits.MmPerUnit;

            foreach (var shape in _symbol.Shapes)
            {
                switch (shape.Kind)
                {
                    case EeSymKind.Rectangle:
                        Add(MX(shape.X), MY(shape.Y));
                        Add(MX(shape.X + shape.Width), MY(shape.Y + shape.Height));
                        break;
                    case EeSymKind.Circle:
                    case EeSymKind.Ellipse:
                        Add(MX(shape.X - shape.RadiusX), MY(shape.Y - shape.RadiusY));
                        Add(MX(shape.X + shape.RadiusX), MY(shape.Y + shape.RadiusY));
                        break;
                    case EeSymKind.Arc:
                        if (SvgPath.TryGetCircularArc(shape.Path, out var arc))
                        {
                            var r = Math.Max(arc.RadiusX, arc.RadiusY) * EeUnits.MmPerUnit;
                            var arcX = MX(arc.CenterX); var arcY = MY(arc.CenterY);
                            Add(arcX - r, arcY - r); Add(arcX + r, arcY + r);
                        }
                        break;
                    case EeSymKind.Polyline:
                    case EeSymKind.Polygon:
                        foreach (var p in Conv.ParseUnitPoints(shape.Points)) Add(MX(p.X), MY(p.Y));
                        break;
                    case EeSymKind.Path:
                        try
                        {
                            foreach (var sub in SvgPath.Parse(shape.Path, 0.02))
                                foreach (var p in sub.Points) Add(MX(p.X), MY(p.Y));
                        }
                        catch { }
                        break;
                    case EeSymKind.Text:
                        Add(MX(shape.X), MY(shape.Y));
                        break;
                }
            }

            foreach (var pin in _symbol.Pins)
            {
                var len = pin.Length;
                double bx = pin.X, by = pin.Y;
                switch (((pin.Rotation % 360) + 360) % 360)
                {
                    case 0: bx = pin.X - len; break;
                    case 90: by = pin.Y + len; break;
                    case 180: bx = pin.X + len; break;
                    case 270: by = pin.Y - len; break;
                }
                Add(MX(pin.X), MY(pin.Y));
                Add(MX(bx), MY(by));
            }

            if (minX > maxX) { DrawEmpty(g, "空符号"); return; }

            const float pad = 14f;
            var spanX = Math.Max(0.2, maxX - minX);
            var spanY = Math.Max(0.2, maxY - minY);
            var scale = Math.Min((Width - pad * 2) / spanX, (Height - pad * 2) / spanY);
            if (scale <= 0 || float.IsInfinity((float)scale)) scale = 1;

            var cx = (minX + maxX) / 2;
            var cy = (minY + maxY) / 2;
            PointF Map(double x, double y) => new PointF(
                (float)(Width / 2 + (x - cx) * scale),
                (float)(Height / 2 - (y - cy) * scale));

            foreach (var shape in _symbol.Shapes)
            {
                var color = ParseColor(shape.Stroke, Color.Maroon);
                var fillColor = ParseColor(shape.Fill, Color.FromArgb(255, 255, 176));
                var hasFill = Conv.HasPaint(shape.Fill);
                var pen = new Pen(color, Math.Max(1f, (float)(shape.StrokeWidth * EeUnits.MmPerUnit * scale)));
                using (pen)
                {
                    switch (shape.Kind)
                    {
                        case EeSymKind.Rectangle:
                        {
                            var a = Map(MX(shape.X), MY(shape.Y));
                            var b = Map(MX(shape.X + shape.Width), MY(shape.Y + shape.Height));
                            var rect = RectOf(a, b);
                            if (hasFill) using (var br = new SolidBrush(fillColor)) g.FillRectangle(br, rect);
                            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                            break;
                        }
                        case EeSymKind.Circle:
                        case EeSymKind.Ellipse:
                        {
                            var c = Map(MX(shape.X), MY(shape.Y));
                            var rx = (float)(Math.Abs(shape.RadiusX) * EeUnits.MmPerUnit * scale);
                            var ry = (float)(Math.Abs(shape.RadiusY) * EeUnits.MmPerUnit * scale);
                            var rect = new RectangleF(c.X - rx, c.Y - ry, rx * 2, ry * 2);
                            if (hasFill) using (var br = new SolidBrush(fillColor)) g.FillEllipse(br, rect);
                            g.DrawEllipse(pen, rect);
                            break;
                        }
                        case EeSymKind.Arc:
                            DrawArc(g, shape, Map, MX, MY, pen);
                            break;
                        case EeSymKind.Polyline:
                        case EeSymKind.Polygon:
                        {
                            var points = new List<PointF>();
                            foreach (var p in Conv.ParseUnitPoints(shape.Points))
                                points.Add(Map(MX(p.X), MY(p.Y)));
                            DrawPoly(g, points, shape.Kind == EeSymKind.Polygon, pen,
                                hasFill ? fillColor : Color.Empty);
                            break;
                        }
                        case EeSymKind.Path:
                            try
                            {
                                foreach (var sub in SvgPath.Parse(shape.Path, 0.02))
                                {
                                    var points = new List<PointF>();
                                    foreach (var p in sub.Points) points.Add(Map(MX(p.X), MY(p.Y)));
                                    DrawPoly(g, points, sub.Closed, pen, sub.Closed && hasFill ? fillColor : Color.Empty);
                                }
                            }
                            catch { }
                            break;
                        case EeSymKind.Text:
                        {
                            var p = Map(MX(shape.X), MY(shape.Y));
                            var size = Math.Max(7f, (float)(shape.FontSize * EeUnits.MmPerUnit * scale));
                            using (var br = new SolidBrush(Conv.HasPaint(shape.Stroke) ? color : Theme.CanvasInk))
                            using (var font = new Font("Tahoma", size * 0.55f))
                            {
                                var textSize = g.MeasureString(shape.Text, font);
                                g.DrawString(shape.Text, font, br, p.X - textSize.Width / 2, p.Y - textSize.Height / 2);
                            }
                            break;
                        }
                    }
                }
            }

            using (var pinPen = new Pen(Theme.PinInk, Math.Max(1f, (float)(0.18 * scale))))
            using (var dot = new SolidBrush(Theme.PinInk))
            using (var numBrush = new SolidBrush(Theme.TextMuted))
            using (var nameBrush = new SolidBrush(Theme.Accent))
            using (var numFont = new Font("Tahoma", Math.Max(6f, (float)(0.8 * scale * 0.55))))
            {
                foreach (var pin in _symbol.Pins)
                {
                    var len = pin.Length;
                    double bx = pin.X, by = pin.Y;
                    switch (((pin.Rotation % 360) + 360) % 360)
                    {
                        case 0: bx = pin.X - len; break;
                        case 90: by = pin.Y + len; break;
                        case 180: bx = pin.X + len; break;
                        case 270: by = pin.Y - len; break;
                    }
                    var connection = Map(MX(pin.X), MY(pin.Y));
                    var start = Map(MX(bx), MY(by));
                    g.DrawLine(pinPen, start, connection);
                    g.FillEllipse(dot, connection.X - 2, connection.Y - 2, 4, 4);

                    if (pin.ShowNumber)
                        g.DrawString(pin.Number, numFont, numBrush,
                            (start.X + connection.X) / 2, (start.Y + connection.Y) / 2 - 12);
                    if (pin.ShowName)
                        g.DrawString(pin.Name, numFont, nameBrush, start.X, start.Y - 12);
                }
            }
        }

        private void DrawArc(Graphics g, EeSymShape shape,
            Func<double, double, PointF> map, Func<double, double> mx, Func<double, double> my, Pen pen)
        {
            if (!SvgPath.TryGetCircularArc(shape.Path, out var arc)) return;
            var steps = Math.Max(8, (int)Math.Abs(arc.SweepAngle / 6));
            var points = new List<PointF>();
            for (var i = 0; i <= steps; i++)
            {
                var a = (arc.StartAngle + arc.SweepAngle * i / steps) * Math.PI / 180.0;
                points.Add(map(mx(arc.CenterX + Math.Cos(a) * arc.RadiusX),
                               my(arc.CenterY + Math.Sin(a) * arc.RadiusY)));
            }
            if (points.Count > 1) g.DrawLines(pen, points.ToArray());
        }

        private static void DrawPoly(Graphics g, List<PointF> points, bool closed, Pen pen, Color fill)
        {
            if (points.Count < 2) return;
            if (closed && points.Count >= 3)
            {
                if (fill != Color.Empty)
                    using (var br = new SolidBrush(fill)) g.FillPolygon(br, points.ToArray());
                g.DrawPolygon(pen, points.ToArray());
            }
            else
            {
                g.DrawLines(pen, points.ToArray());
            }
        }

        internal static RectangleF RectOf(PointF a, PointF b)
        {
            return new RectangleF(
                Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        internal static Color ParseColor(string value, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(value.Trim(), "none", StringComparison.OrdinalIgnoreCase))
                return fallback;
            var text = value.Trim();
            if (text.StartsWith("#", StringComparison.Ordinal)) text = text.Substring(1);
            if (text.Length == 8) text = text.Substring(2);
            if (text.Length != 6) return fallback;
            try
            {
                var rgb = Convert.ToInt32(text, 16);
                return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            }
            catch { return fallback; }
        }

        private void DrawGrid(Graphics g)
        {
            const int step = 24;
            using (var pen = new Pen(Theme.CanvasGrid))
            {
                for (var x = 0; x < Width; x += step) g.DrawLine(pen, x, 0, x, Height);
                for (var y = 0; y < Height; y += step) g.DrawLine(pen, 0, y, Width, y);
            }
        }

        private void DrawEmpty(Graphics g, string text)
        {
            using (var font = new Font("Microsoft YaHei UI", 10f))
            using (var brush = new SolidBrush(Theme.TextMuted))
            {
                var size = g.MeasureString(text, font);
                g.DrawString(text, font, brush, (Width - size.Width) / 2, (Height - size.Height) / 2);
            }
        }
    }

    internal sealed class FootprintCanvas : Control
    {
        private EeFootprint _footprint;

        public FootprintCanvas()
        {
            DoubleBuffered = true;
            BackColor = Theme.Canvas;
        }

        public EeFootprint Footprint
        {
            get => _footprint;
            set { _footprint = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Canvas);
            DrawGrid(g);
            if (_footprint == null)
            {
                DrawEmpty(g, "无封装数据");
                return;
            }

            var boxWidth = _footprint.BoxWidth > 0.01 ? _footprint.BoxWidth : 10;
            var boxHeight = _footprint.BoxHeight > 0.01 ? _footprint.BoxHeight : 10;
            var originX = _footprint.BoxX + boxWidth / 2.0;
            var originY = _footprint.BoxY + boxHeight / 2.0;
            const float pad = 14f;
            var scale = Math.Min((Width - pad * 2) / boxWidth, (Height - pad * 2) / boxHeight);
            if (scale <= 0) scale = 4;
            scale = Math.Max(0.5, scale);

            PointF Map(double x, double y) => new PointF(
                (float)(Width / 2 + (x - originX) * scale),
                (float)(Height / 2 - (originY - y) * scale));

            foreach (var shape in _footprint.Shapes)
            {
                var layer = ResolveLayer(shape.LayerId);
                var color = LayerColor(layer);
                using (var pen = new Pen(color, Math.Max(1f, (float)(shape.StrokeWidth * scale))))
                {
                    switch (shape.Kind)
                    {
                        case EeFpKind.Track:
                            for (var i = 1; i < shape.Points.Count; i++)
                                g.DrawLine(pen, Map(shape.Points[i - 1].X, shape.Points[i - 1].Y),
                                    Map(shape.Points[i].X, shape.Points[i].Y));
                            break;
                        case EeFpKind.Circle:
                        {
                            var c = Map(shape.X, shape.Y);
                            var r = (float)(shape.Radius * scale);
                            g.DrawEllipse(pen, c.X - r, c.Y - r, r * 2, r * 2);
                            break;
                        }
                        case EeFpKind.Arc:
                            DrawArc(g, shape, Map, pen);
                            break;
                        case EeFpKind.Rectangle:
                        {
                            var a = Map(shape.X, shape.Y);
                            var b = Map(shape.X + shape.Width, shape.Y + shape.Height);
                            g.DrawRectangle(pen, Rectangle.Round(SymbolCanvas.RectOf(a, b)));
                            break;
                        }
                        case EeFpKind.Pad:
                            DrawPad(g, shape, Map, scale, color);
                            break;
                        case EeFpKind.Hole:
                        {
                            var c = Map(shape.X, shape.Y);
                            var r = (float)(shape.Radius * scale);
                            using (var hole = new SolidBrush(Theme.Canvas))
                                g.FillEllipse(hole, c.X - r, c.Y - r, r * 2, r * 2);
                            using (var edge = new Pen(Theme.Border))
                                g.DrawEllipse(edge, c.X - r, c.Y - r, r * 2, r * 2);
                            break;
                        }
                        case EeFpKind.Via:
                        {
                            var c = Map(shape.X, shape.Y);
                            var r = (float)(shape.Diameter * scale / 2);
                            using (var fill = new SolidBrush(color))
                                g.FillEllipse(fill, c.X - r, c.Y - r, r * 2, r * 2);
                            var hr = (float)(shape.Radius * scale);
                            using (var hole = new SolidBrush(Theme.Canvas))
                                g.FillEllipse(hole, c.X - hr, c.Y - hr, hr * 2, hr * 2);
                            break;
                        }
                        case EeFpKind.Text:
                        {
                            var p = Map(shape.X, shape.Y);
                            using (var brush = new SolidBrush(color))
                            using (var font = new Font("Tahoma", Math.Max(6f, (float)(shape.FontSize * scale * 0.5))))
                                g.DrawString(shape.Text, font, brush, p);
                            break;
                        }
                    }
                }
            }

            var center = Map(originX, originY);
            using (var cross = new Pen(Theme.TextMuted))
            {
                g.DrawLine(cross, center.X - 8, center.Y, center.X + 8, center.Y);
                g.DrawLine(cross, center.X, center.Y - 8, center.X, center.Y + 8);
            }
        }

        private void DrawPad(Graphics g, EeFpShape pad, Func<double, double, PointF> map, double scale, Color color)
        {
            var c = map(pad.X, pad.Y);
            var w = (float)Math.Max(0.6, pad.Width * scale);
            var h = (float)Math.Max(0.6, pad.Height * scale);
            var shape = (pad.Shape ?? "").ToUpperInvariant();
            var polygon = shape == "POLYGON" && pad.Points.Count >= 3
                ? pad.Points.Select(p => map(p.X, p.Y)).ToArray()
                : null;

            using (var fill = new SolidBrush(color))
            using (var pen = new Pen(Darken(color), 1.2f))
            {
                RectangleF bounds;
                if (polygon != null)
                {
                    // EasyEDA polygon pads carry their final absolute outline; use it verbatim.
                    using (var path = new GraphicsPath())
                    {
                        path.AddPolygon(polygon);
                        g.FillPath(fill, path);
                        g.DrawPath(pen, path);
                        bounds = path.GetBounds();
                    }
                }
                else
                {
                    var state = g.Save();
                    g.TranslateTransform(c.X, c.Y);
                    g.RotateTransform((float)-pad.Rotation);
                    var rect = new RectangleF(-w / 2, -h / 2, w, h);
                    switch (shape)
                    {
                        case "ELLIPSE":
                        case "OVAL":
                            g.FillEllipse(fill, rect);
                            g.DrawEllipse(pen, rect);
                            break;
                        case "OCT":
                            using (var path = Octagon(w / 2, h / 2))
                            {
                                g.FillPath(fill, path);
                                g.DrawPath(pen, path);
                            }
                            break;
                        default:
                            g.FillRectangle(fill, rect);
                            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                            break;
                    }
                    g.Restore(state);
                    bounds = new RectangleF(c.X - w / 2, c.Y - h / 2, w, h);
                }

                if (!string.IsNullOrWhiteSpace(pad.Number))
                {
                    var fontSize = Math.Max(6f, Math.Min(11f, bounds.Height * 0.45f));
                    using (var font = new Font("Microsoft YaHei UI", fontSize, FontStyle.Bold))
                        TextRenderer.DrawText(g, pad.Number, font, Rectangle.Round(bounds), Color.White,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                            TextFormatFlags.SingleLine);
                }

                var center = polygon != null
                    ? new PointF(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2)
                    : c;
                var drill = (float)(Math.Max(0, pad.HoleRadius) * 2 * scale);
                if (drill > 0.8f)
                {
                    if (pad.HolePoints.Count >= 2)
                    {
                        using (var hole = new Pen(Theme.Canvas, drill))
                            g.DrawLine(hole, map(pad.HolePoints[0].X, pad.HolePoints[0].Y),
                                map(pad.HolePoints[1].X, pad.HolePoints[1].Y));
                    }
                    else
                    {
                        using (var hole = new SolidBrush(Theme.Canvas))
                            g.FillEllipse(hole, center.X - drill / 2, center.Y - drill / 2, drill, drill);
                    }
                }
            }
        }

        private void DrawArc(Graphics g, EeFpShape shape, Func<double, double, PointF> map, Pen pen)
        {
            if (!SvgPath.TryGetCircularArc(shape.Path, out var arc)) return;
            var steps = Math.Max(8, (int)Math.Abs(arc.SweepAngle / 6));
            var points = new List<PointF>();
            for (var i = 0; i <= steps; i++)
            {
                var a = (arc.StartAngle + arc.SweepAngle * i / steps) * Math.PI / 180.0;
                points.Add(map(
                    (arc.CenterX + Math.Cos(a) * arc.RadiusX) * EeUnits.MmPerUnit,
                    (arc.CenterY + Math.Sin(a) * arc.RadiusY) * EeUnits.MmPerUnit));
            }
            if (points.Count > 1) g.DrawLines(pen, points.ToArray());
        }

        private string ResolveLayer(string layerId)
        {
            if (string.IsNullOrWhiteSpace(layerId)) return "";
            return _footprint.Layers.TryGetValue(layerId, out var name) ? name : layerId;
        }

        private static Color LayerColor(string layer)
        {
            switch (layer ?? "")
            {
                case "TopLayer": return Color.FromArgb(0xD0, 0x30, 0x30);
                case "BottomLayer": return Color.FromArgb(0x30, 0x50, 0xD0);
                case "TopSilkLayer": return Color.FromArgb(0xB0, 0x88, 0x00);
                case "BottomSilkLayer": return Color.FromArgb(0x80, 0x70, 0x50);
                case "TopPasteMaskLayer":
                case "BottomPasteMaskLayer": return Color.FromArgb(0x90, 0x90, 0x90);
                case "TopSolderMaskLayer":
                case "BottomSolderMaskLayer": return Color.FromArgb(0xA0, 0x60, 0xC0);
                case "Multi-Layer": return Color.FromArgb(0xC8, 0x96, 0x32);
                case "KeepOutLayer": return Color.FromArgb(0xE0, 0x20, 0xC0);
                default: return Color.FromArgb(0x70, 0x70, 0x70);
            }
        }

        private static Color Darken(Color color)
        {
            return Color.FromArgb((int)(color.R * 0.7), (int)(color.G * 0.7), (int)(color.B * 0.7));
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            var r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
            path.AddArc(rect.Left, rect.Top, r * 2, r * 2, 180, 90);
            path.AddArc(rect.Right - r * 2, rect.Top, r * 2, r * 2, 270, 90);
            path.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath Octagon(float rx, float ry)
        {
            var k = 0.4142f;
            var points = new[]
            {
                new PointF(-rx * k, -ry), new PointF(rx * k, -ry),
                new PointF(rx, -ry * k), new PointF(rx, ry * k),
                new PointF(rx * k, ry), new PointF(-rx * k, ry),
                new PointF(-rx, ry * k), new PointF(-rx, -ry * k),
            };
            var path = new GraphicsPath();
            path.AddPolygon(points);
            path.CloseFigure();
            return path;
        }

        private void DrawGrid(Graphics g)
        {
            const int step = 24;
            using (var pen = new Pen(Theme.CanvasGrid))
            {
                for (var x = 0; x < Width; x += step) g.DrawLine(pen, x, 0, x, Height);
                for (var y = 0; y < Height; y += step) g.DrawLine(pen, 0, y, Width, y);
            }
        }

        private void DrawEmpty(Graphics g, string text)
        {
            using (var font = new Font("Microsoft YaHei UI", 10f))
            using (var brush = new SolidBrush(Theme.TextMuted))
            {
                var size = g.MeasureString(text, font);
                g.DrawString(text, font, brush, (Width - size.Width) / 2, (Height - size.Height) / 2);
            }
        }
    }
}
