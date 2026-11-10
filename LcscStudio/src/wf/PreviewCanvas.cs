using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using LcscLoader;

namespace LcscStudio
{
    // 预览空状态：图标 + 标题 + 一行动作引导。
    internal static class PreviewEmpty
    {
        public static void Draw(Graphics g, Size size, string title, string hint, bool footprint)
        {
            var cx = size.Width / 2f;
            var cy = size.Height / 2f - 26f;
            using (var pen = new Pen(Color.FromArgb(0x4A, 0x4C, 0x50)))
            {
                if (footprint)
                {
                    var pad = new RectangleF(cx - 18, cy - 10, 36, 20);
                    using (var path = Rounded(pad, 5)) g.DrawPath(pen, path);
                    g.DrawEllipse(pen, cx - 5, cy - 5, 10, 10);
                }
                else
                {
                    var body = new RectangleF(cx - 14, cy - 10, 28, 20);
                    using (var path = Rounded(body, 3)) g.DrawPath(pen, path);
                    g.DrawLine(pen, body.Left - 7, cy - 4, body.Left, cy - 4);
                    g.DrawLine(pen, body.Left - 7, cy + 4, body.Left, cy + 4);
                    g.DrawLine(pen, body.Right, cy, body.Right + 7, cy);
                }
            }

            using (var titleFont = new Font("Microsoft YaHei UI", 10f))
            using (var titleBrush = new SolidBrush(Theme.TextMuted))
            using (var hintBrush = new SolidBrush(Theme.TextFaint))
            {
                var titleSize = g.MeasureString(title, titleFont);
                var hintSize = g.MeasureString(hint, Theme.LabelFont);
                g.DrawString(title, titleFont, titleBrush, cx - titleSize.Width / 2f, cy + 22);
                g.DrawString(hint, Theme.LabelFont, hintBrush, cx - hintSize.Width / 2f,
                    cy + 24 + titleSize.Height);
            }
        }

        private static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            var r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
            var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, r * 2, r * 2, 180, 90);
            path.AddArc(rect.Right - r * 2, rect.Top, r * 2, r * 2, 270, 90);
            path.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // 预览画布公共视图：滚轮缩放（以光标为锚点）、左键拖动平移、双击还原。
    internal abstract class ZoomCanvas : Control
    {
        private const float MinZoom = 0.2f;
        private const float MaxZoom = 8f;

        protected float ViewZoom = 1f;
        protected float ViewPanX;
        protected float ViewPanY;

        private bool _dragging;
        private Point _dragPoint;

        protected ZoomCanvas()
        {
            DoubleBuffered = true;
            BackColor = Theme.Canvas;
            SetStyle(ControlStyles.Selectable, true);
            SetStyle(ControlStyles.StandardClick, true);
            SetStyle(ControlStyles.StandardDoubleClick, true);
        }

        public virtual void ResetView()
        {
            ViewZoom = 1f;
            ViewPanX = 0f;
            ViewPanY = 0f;
            Invalidate();
        }

        public void ZoomIn() => ZoomAt(new Point(Width / 2, Height / 2), 1.2f);

        public void ZoomOut() => ZoomAt(new Point(Width / 2, Height / 2), 1f / 1.2f);

        private void ZoomAt(Point anchor, float factor)
        {
            var zoom = Math.Min(MaxZoom, Math.Max(MinZoom, ViewZoom * factor));
            if (Math.Abs(zoom - ViewZoom) < 0.0001f) return;

            // 保持光标下的内容点不动：平移量按缩放比重新换算。
            var cx = anchor.X - Width / 2f;
            var cy = anchor.Y - Height / 2f;
            var ratio = zoom / ViewZoom;
            ViewPanX = cx - (cx - ViewPanX) * ratio;
            ViewPanY = cy - (cy - ViewPanY) * ratio;
            ViewZoom = zoom;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ZoomAt(e.Location, e.Delta > 0 ? 1.15f : 1f / 1.15f);
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            _dragging = true;
            _dragPoint = e.Location;
            Capture = true;
            Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            ViewPanX += e.X - _dragPoint.X;
            ViewPanY += e.Y - _dragPoint.Y;
            _dragPoint = e.Location;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !_dragging) return;
            _dragging = false;
            Capture = false;
            Cursor = Cursors.Default;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left) ResetView();
        }
    }

    internal sealed class SymbolCanvas : ZoomCanvas
    {
        // EasyEDA 符号里引脚名的字高约 6 unit（引脚间距 10 unit），渲染时按同一比例尺换算。
        private const double PinTextUnits = 6.0;

        private readonly List<EeSymbol> _symbols = new List<EeSymbol>();

        // 被截断的引脚名：记录屏幕命中区域，悬停时用 tooltip 给出完整名称。
        private readonly List<KeyValuePair<RectangleF, string>> _nameHits =
            new List<KeyValuePair<RectangleF, string>>();
        private readonly ToolTip _nameTip = new ToolTip();
        private string _tipText;
        private bool _namesSuppressed;

        public EeSymbol Symbol
        {
            get { return _symbols.Count > 0 ? _symbols[0] : null; }
            set
            {
                _symbols.Clear();
                if (value != null) _symbols.Add(value);
                ResetView();
            }
        }

        // 多部件器件（如 MCU 的独立电源符号）按上下顺序全部画出来。
        public void SetSymbols(IList<EeSymbol> symbols)
        {
            _symbols.Clear();
            if (symbols != null)
            {
                foreach (var symbol in symbols)
                    if (symbol != null) _symbols.Add(symbol);
            }
            ResetView();
        }

        private struct PartLayout
        {
            public EeSymbol Symbol;
            public double OffsetY;
            public double MinX, MaxX, MinY, MaxY;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Canvas);
            DrawGrid(g);
            _nameHits.Clear();
            _namesSuppressed = false;
            if (_symbols.Count == 0)
            {
                PreviewEmpty.Draw(g, ClientSize, "还没有符号", "搜索型号或编号后在此预览", false);
                return;
            }

            // 每个部件有各自的坐标原点，先分别测量，再按上下顺序堆叠。
            const double gap = 2.5;
            var layouts = new List<PartLayout>();
            var nextTop = 0.0;
            foreach (var symbol in _symbols)
            {
                double minX, maxX, minY, maxY;
                if (!MeasurePart(symbol, out minX, out maxX, out minY, out maxY)) continue;
                var offset = layouts.Count == 0 ? 0.0 : nextTop - maxY;
                layouts.Add(new PartLayout
                {
                    Symbol = symbol,
                    OffsetY = offset,
                    MinX = minX,
                    MaxX = maxX,
                    MinY = minY + offset,
                    MaxY = maxY + offset,
                });
                nextTop = minY + offset - gap;
            }

            if (layouts.Count == 0)
            {
                PreviewEmpty.Draw(g, ClientSize, "该器件没有符号图形", "可以查看封装或换一个器件", false);
                return;
            }

            var left = layouts[0].MinX;
            var right = layouts[0].MaxX;
            var bottom = layouts[0].MinY;
            var top = layouts[0].MaxY;
            foreach (var layout in layouts)
            {
                left = Math.Min(left, layout.MinX);
                right = Math.Max(right, layout.MaxX);
                bottom = Math.Min(bottom, layout.MinY);
                top = Math.Max(top, layout.MaxY);
            }

            const float pad = 14f;
            var spanX = Math.Max(0.2, right - left);
            var spanY = Math.Max(0.2, top - bottom);
            var fit = Math.Min((Width - pad * 2) / spanX, (Height - pad * 2) / spanY);
            if (fit <= 0 || float.IsInfinity((float)fit)) fit = 1;
            var scale = fit * ViewZoom;

            var centerX = (left + right) / 2;
            var centerY = (bottom + top) / 2;

            PointF Map(double x, double y) => new PointF(
                (float)(Width / 2 + ViewPanX + (x - centerX) * scale),
                (float)(Height / 2 + ViewPanY - (y - centerY) * scale));

            foreach (var layout in layouts)
            {
                var center = Map((layout.MinX + layout.MaxX) / 2, (layout.MinY + layout.MaxY) / 2);
                DrawPart(g, layout, Map, scale, center);
            }

            // 引脚名被隐藏时给一行说明，避免用户以为符号丢了信息。
            if (_namesSuppressed)
            {
                using (var hintBrush = new SolidBrush(Theme.TextFaint))
                    g.DrawString("引脚名过密，滚轮放大或悬停查看", Theme.LabelFont, hintBrush,
                        pad, Height - Theme.LabelFont.Height - 8);
            }
        }

        private static bool MeasurePart(EeSymbol symbol,
            out double minX, out double maxX, out double minY, out double maxY)
        {
            var left = double.MaxValue;
            var right = double.MinValue;
            var bottom = double.MaxValue;
            var top = double.MinValue;

            void Add(double x, double y)
            {
                left = Math.Min(left, x); right = Math.Max(right, x);
                bottom = Math.Min(bottom, y); top = Math.Max(top, y);
            }

            double MX(double units) => (units - symbol.OriginX) * EeUnits.MmPerUnit;
            double MY(double units) => (symbol.OriginY - units) * EeUnits.MmPerUnit;

            foreach (var shape in symbol.Shapes)
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

            foreach (var pin in symbol.Pins)
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

            minX = left;
            maxX = right;
            minY = bottom;
            maxY = top;
            return left <= right && bottom <= top;
        }

        // 引脚文字的实际可用高度取决于最近两个引脚的距离，而不是画布上的绝对像素。
        private static double NearestPinPitchUnits(EeSymbol symbol)
        {
            var pins = symbol.Pins;
            var best = double.MaxValue;
            for (var i = 0; i < pins.Count; i++)
            {
                for (var j = i + 1; j < pins.Count; j++)
                {
                    var dx = pins[i].X - pins[j].X;
                    var dy = pins[i].Y - pins[j].Y;
                    var d = Math.Sqrt(dx * dx + dy * dy);
                    if (d > 0.001 && d < best) best = d;
                }
            }
            return best == double.MaxValue ? 10.0 : best;
        }

        private void DrawPart(Graphics g, PartLayout layout, Func<double, double, PointF> map,
            double scale, PointF centerPt)
        {
            var symbol = layout.Symbol;
            var offset = layout.OffsetY;

            double MX(double units) => (units - symbol.OriginX) * EeUnits.MmPerUnit;
            double MY(double units) => (symbol.OriginY - units) * EeUnits.MmPerUnit;
            PointF Map(double x, double y) => map(x, y + offset);

            foreach (var shape in symbol.Shapes)
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

            // 引脚文字必须和几何用同一把比例尺。旧代码是 Math.Max(6f, 0.8*scale*0.55)：
            // 48 脚 IC fit 到面板后 scale≈4.2px/mm，几何缩到行距 10.7px，字号却被 6pt 下限
            // 钉在 ~9.6px 行高，相邻两行的长引脚名必然叠字（0603 只有两脚、间距大，看不出来）。
            // 现在按 EasyEDA 的 6 unit 字高等比缩放，再用最近邻引脚间距的 62% 封顶。
            var pitchPx = NearestPinPitchUnits(symbol) * EeUnits.MmPerUnit * scale;
            // 两脚小器件（0603/0402）的引脚间距很大，间距封顶失效：6 unit 字高按 fit 放大后
            // 比器件体还高，编号就会飘到体外，看起来像"偏移"。再按本部件最小外廓封顶。
            var spanPx = Math.Min(layout.MaxX - layout.MinX, layout.MaxY - layout.MinY) * scale;
            var textPx = Math.Min(
                Math.Min(PinTextUnits * EeUnits.MmPerUnit * scale, pitchPx * 0.62),
                spanPx * 0.4);
            var pinTextPx = (float)Math.Max(3.0, textPx);

            // 3~5px 的字形经 GDI+ 栅格化后墨迹接近行距，48 脚器件 fit 到窄面板时会糊成一片。
            // 这种字号本来也读不出内容，索性不画引脚名，只保留引脚号与悬停提示；放大后自动恢复。
            var showNames = pinTextPx >= 3.2f;
            if (!showNames) _namesSuppressed = true;

            using (var pinPen = new Pen(Theme.PinInk, Math.Max(1f, (float)(0.18 * scale))))
            using (var dot = new SolidBrush(Theme.PinInk))
            using (var numBrush = new SolidBrush(Theme.TextMuted))
            using (var nameBrush = new SolidBrush(Theme.Accent))
            using (var numFont = new Font("Tahoma", pinTextPx, GraphicsUnit.Pixel))
            {
                // 引脚号的偏移也随字号走；写死 8px 会在小面板里超过半行距，把编号推到上一行。
                var numGap = numFont.GetHeight(g) * 0.6f;
                foreach (var pin in symbol.Pins)
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

                    var midX = (start.X + connection.X) / 2f;
                    var midY = (start.Y + connection.Y) / 2f;
                    var vertical = Math.Abs(connection.Y - start.Y) > Math.Abs(connection.X - start.X);

                    // 引脚号贴着引脚线外侧；竖直引脚旋转 90°，否则会在器件边缘挤成一排。
                    if (pin.ShowNumber)
                    {
                        if (vertical)
                            DrawCenteredText(g, pin.Number, numFont, numBrush, midX - numGap, midY, -90);
                        else
                            DrawCenteredText(g, pin.Number, numFont, numBrush, midX, midY - numGap, 0);
                    }

                    // 引脚名放在器件体内部，沿引脚方向排列；超出本侧可用宽度时截断。
                    // 可用宽度取「引脚根部到器件体中心」再扣掉中缝，左右两侧各占一半，
                    // 因此同一排的左右引脚名不会在器件体中段相遇压字。
                    if (pin.ShowName && !string.IsNullOrEmpty(pin.Name))
                    {
                        // 只沿引脚轴线偏移，不能朝包围盒中心斜着挪：长名字的偏移量大，
                        // 斜向量会把整行文字推向器件中心，上下相邻行就在中部叠在一起。
                        var ix = vertical ? 0f : Math.Sign(centerPt.X - start.X);
                        var iy = vertical ? Math.Sign(centerPt.Y - start.Y) : 0f;
                        if (ix == 0 && iy == 0) iy = 1f;
                        var lineHeight = numFont.GetHeight(g);
                        var halfRun = vertical
                            ? (float)Math.Abs(centerPt.Y - start.Y)
                            : (float)Math.Abs(centerPt.X - start.X);

                        if (!showNames)
                        {
                            // 不画文字，但整行留作命中区域：悬停即可看到完整引脚名。
                            var hitWidth = Math.Max(8f, halfRun - 4f);
                            var hitHeight = (float)Math.Max(4.0, pitchPx * 0.9 - 4.0);
                            AddNameHit(start.X + ix * hitWidth / 2f,
                                start.Y + iy * hitWidth / 2f,
                                hitWidth - 4f, hitHeight, vertical, pin.Name);
                            continue;
                        }

                        var seam = lineHeight * 0.9f + 2f;
                        var available = Math.Max(lineHeight * 2f, halfRun - seam - 4f);
                        bool trimmed;
                        var label = FitText(g, pin.Name, numFont, available, out trimmed);
                        if (label.Length > 0)
                        {
                            var textWidth = g.MeasureString(label, numFont).Width;
                            var labelOffset = 4 + textWidth / 2f;
                            var cx = start.X + ix * labelOffset;
                            var cy = start.Y + iy * labelOffset;
                            DrawCenteredText(g, label, numFont, nameBrush, cx, cy, vertical ? -90 : 0);
                            if (trimmed) AddNameHit(cx, cy, textWidth, lineHeight, vertical, pin.Name);
                        }
                    }
                }
            }
        }

        // 按可用宽度截断文本并用省略号收尾；返回是否发生了截断。
        private static string FitText(Graphics g, string text, Font font, float available, out bool trimmed)
        {
            trimmed = false;
            if (string.IsNullOrEmpty(text)) return text;
            if (g.MeasureString(text, font).Width <= available) return text;

            trimmed = true;
            const string ellipsis = "…";
            var low = 0;
            var high = text.Length;
            while (low < high)
            {
                var mid = (low + high + 1) / 2;
                if (g.MeasureString(text.Substring(0, mid) + ellipsis, font).Width <= available) low = mid;
                else high = mid - 1;
            }
            return low <= 0 ? string.Empty : text.Substring(0, low) + ellipsis;
        }

        private void AddNameHit(float cx, float cy, float width, float lineHeight, bool vertical, string fullText)
        {
            var w = (vertical ? lineHeight : width) + 4f;
            var h = (vertical ? width : lineHeight) + 4f;
            _nameHits.Add(new KeyValuePair<RectangleF, string>(
                new RectangleF(cx - w / 2f, cy - h / 2f, w, h), fullText));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Capture || _nameHits.Count == 0) return;

            foreach (var hit in _nameHits)
            {
                if (!hit.Key.Contains(e.Location)) continue;
                if (_tipText == hit.Value) return;
                _tipText = hit.Value;
                var screen = PointToScreen(e.Location);
                _nameTip.Show(_tipText, this, screen.X + 12, screen.Y + 16, 4000);
                return;
            }

            if (_tipText != null)
            {
                _tipText = null;
                _nameTip.Hide(this);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _tipText = null;
            _nameTip.Hide(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _nameTip.Dispose();
            base.Dispose(disposing);
        }

        private static void DrawCenteredText(Graphics g, string text, Font font, Brush brush,
            float x, float y, float angle)
        {
            var size = g.MeasureString(text, font);
            var state = g.Save();
            g.TranslateTransform(x, y);
            g.RotateTransform(angle);
            g.DrawString(text, font, brush, -size.Width / 2f, -size.Height / 2f);
            g.Restore(state);
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
            using (var pen = new Pen(_symbols.Count == 0 ? Theme.CanvasGridIdle : Theme.CanvasGrid))
            {
                for (var x = 0; x < Width; x += step) g.DrawLine(pen, x, 0, x, Height);
                for (var y = 0; y < Height; y += step) g.DrawLine(pen, 0, y, Width, y);
            }
        }
    }

    internal sealed class FootprintCanvas : ZoomCanvas
    {
        private EeFootprint _footprint;

        public EeFootprint Footprint
        {
            get => _footprint;
            set { _footprint = value; ResetView(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Canvas);
            DrawGrid(g);
            if (_footprint == null)
            {
                PreviewEmpty.Draw(g, ClientSize, "还没有封装", "搜索型号或编号后在此预览", true);
                return;
            }

            var boxWidth = _footprint.BoxWidth > 0.01 ? _footprint.BoxWidth : 10;
            var boxHeight = _footprint.BoxHeight > 0.01 ? _footprint.BoxHeight : 10;
            var originX = _footprint.BoxX + boxWidth / 2.0;
            var originY = _footprint.BoxY + boxHeight / 2.0;
            const float pad = 14f;
            var fit = Math.Min((Width - pad * 2) / boxWidth, (Height - pad * 2) / boxHeight);
            if (fit <= 0) fit = 4;
            fit = Math.Max(0.5, fit);
            var scale = fit * ViewZoom;

            PointF Map(double x, double y) => new PointF(
                (float)(Width / 2 + ViewPanX + (x - originX) * scale),
                (float)(Height / 2 + ViewPanY - (originY - y) * scale));

            // EasyEDA can split one pad number across several primitives (e.g. SOT-89 pad 2);
            // draw the number once, on the largest primitive of that number.
            var numberOwner = new Dictionary<string, int>();
            var numberArea = new Dictionary<string, double>();
            for (var i = 0; i < _footprint.Shapes.Count; i++)
            {
                var candidate = _footprint.Shapes[i];
                if (candidate.Kind != EeFpKind.Pad || string.IsNullOrWhiteSpace(candidate.Number)) continue;
                var area = Math.Abs(candidate.Width * candidate.Height);
                if (!numberOwner.ContainsKey(candidate.Number) || area > numberArea[candidate.Number])
                {
                    numberOwner[candidate.Number] = i;
                    numberArea[candidate.Number] = area;
                }
            }

            for (var index = 0; index < _footprint.Shapes.Count; index++)
            {
                var shape = _footprint.Shapes[index];
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
                            DrawPad(g, shape, Map, scale, color,
                                numberOwner.TryGetValue(shape.Number, out var owner) && owner == index);
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

        private void DrawPad(Graphics g, EeFpShape pad, Func<double, double, PointF> map, double scale,
            Color color, bool showNumber)
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
                            g.FillEllipse(fill, rect);
                            g.DrawEllipse(pen, rect);
                            break;
                        case "OVAL":
                            // EasyEDA OVAL pads are obround (stadium), not ellipses.
                            using (var path = RoundedRect(rect, Math.Min(w, h) / 2))
                            {
                                g.FillPath(fill, path);
                                g.DrawPath(pen, path);
                            }
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

                if (showNumber && !string.IsNullOrWhiteSpace(pad.Number))
                {
                    // 取短边：横放焊盘与竖放焊盘旋转 90° 后短边不变，四边的编号才会一样大。
                    var fontSize = Math.Max(6f, Math.Min(11f,
                        Math.Min(bounds.Width, bounds.Height) * 0.45f));
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
                // 丝印用浅色，和金色焊盘拉开距离，避免两者糊成一片。
                case "TopSilkLayer": return Color.FromArgb(0xE6, 0xEA, 0xF0);
                case "BottomSilkLayer": return Color.FromArgb(0x9C, 0xA8, 0xB8);
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
            using (var pen = new Pen(_footprint == null ? Theme.CanvasGridIdle : Theme.CanvasGrid))
            {
                for (var x = 0; x < Width; x += step) g.DrawLine(pen, x, 0, x, Height);
                for (var y = 0; y < Height; y += step) g.DrawLine(pen, 0, y, Width, y);
            }
        }
    }
}
