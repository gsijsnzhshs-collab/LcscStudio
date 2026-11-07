using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using LcscLoader;

namespace LcscStudio
{
    internal sealed class FootprintPreview : FrameworkElement
    {
        private EeFootprint _footprint;

        public EeFootprint Footprint
        {
            get => _footprint;
            set { _footprint = value; InvalidateVisual(); }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var width = ActualWidth;
            var height = ActualHeight;
            if (width < 2 || height < 2) return;

            if (_footprint == null)
            {
                PreviewPalette.DrawPlaceholder(dc, width, height, "无封装数据");
                return;
            }

            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));

            var boxWidth = _footprint.BoxWidth > 0.01 ? _footprint.BoxWidth : 10;
            var boxHeight = _footprint.BoxHeight > 0.01 ? _footprint.BoxHeight : 10;
            var originX = _footprint.BoxX + boxWidth / 2.0;
            var originY = _footprint.BoxY + boxHeight / 2.0;

            const double padding = 18;
            var scale = Math.Min((width - padding * 2) / boxWidth, (height - padding * 2) / boxHeight);
            if (scale <= 0 || double.IsInfinity(scale)) scale = 4;

            Point Map(double x, double y) => new Point(
                width / 2 + (x - originX) * scale,
                height / 2 - (originY - y) * scale);

            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            foreach (var shape in _footprint.Shapes)
            {
                var layer = ResolveLayer(shape.LayerId);
                var color = PreviewPalette.LayerColor(layer);
                switch (shape.Kind)
                {
                    case EeFpKind.Track:
                    {
                        var pen = PreviewPalette.Pen(color, shape.StrokeWidth * scale);
                        for (var i = 1; i < shape.Points.Count; i++)
                        {
                            dc.DrawLine(pen,
                                Map(shape.Points[i - 1].X, shape.Points[i - 1].Y),
                                Map(shape.Points[i].X, shape.Points[i].Y));
                        }
                        break;
                    }
                    case EeFpKind.Circle:
                    {
                        var pen = PreviewPalette.Pen(color, shape.StrokeWidth * scale);
                        dc.DrawEllipse(null, pen, Map(shape.X, shape.Y),
                            Math.Max(0.5, shape.Radius * scale), Math.Max(0.5, shape.Radius * scale));
                        break;
                    }
                    case EeFpKind.Arc:
                    {
                        var pen = PreviewPalette.Pen(color, shape.StrokeWidth * scale);
                        DrawArc(dc, shape, Map, pen);
                        break;
                    }
                    case EeFpKind.Rectangle:
                    {
                        var pen = PreviewPalette.Pen(color, shape.StrokeWidth * scale);
                        var a = Map(shape.X, shape.Y);
                        var b = Map(shape.X + shape.Width, shape.Y + shape.Height);
                        dc.DrawRectangle(null, pen, new Rect(a, b));
                        break;
                    }
                    case EeFpKind.Pad:
                        DrawPad(dc, shape, Map, scale, color);
                        break;
                    case EeFpKind.Hole:
                        DrawHole(dc, shape, Map, scale, color);
                        break;
                    case EeFpKind.Via:
                    {
                        var viaCenter = Map(shape.X, shape.Y);
                        dc.DrawEllipse(PreviewPalette.Brush(color, 0.7),
                            PreviewPalette.Pen(color, 1.5), viaCenter,
                            Math.Max(0.5, shape.Diameter * scale / 2), Math.Max(0.5, shape.Diameter * scale / 2));
                        dc.DrawEllipse(Brushes.White, null, viaCenter,
                            Math.Max(0.5, shape.Radius * scale), Math.Max(0.5, shape.Radius * scale));
                        break;
                    }
                    case EeFpKind.Text:
                    {
                        var point = Map(shape.X, shape.Y);
                        var size = Math.Max(7, shape.FontSize * scale * 0.9);
                        PreviewPalette.DrawText(dc, shape.Text, point, size,
                            PreviewPalette.Brush(color), dpi, -shape.Rotation);
                        break;
                    }
                }
            }

            var cross = PreviewPalette.Pen(Color.FromRgb(0x80, 0x80, 0x80), 1.0, 0.6);
            var originPoint = Map(originX, originY);
            dc.DrawLine(cross, new Point(originPoint.X - 8, originPoint.Y), new Point(originPoint.X + 8, originPoint.Y));
            dc.DrawLine(cross, new Point(originPoint.X, originPoint.Y - 8), new Point(originPoint.X, originPoint.Y + 8));

            dc.Pop();
        }

        private string ResolveLayer(string layerId)
        {
            if (string.IsNullOrWhiteSpace(layerId)) return "";
            return _footprint.Layers.TryGetValue(layerId, out var name) ? name : layerId;
        }

        private static void DrawPad(DrawingContext dc, EeFpShape pad,
            Func<double, double, Point> map, double scale, Color color)
        {
            var center = map(pad.X, pad.Y);
            var w = Math.Max(0.6, pad.Width * scale);
            var h = Math.Max(0.6, pad.Height * scale);

            dc.PushTransform(new RotateTransform(-pad.Rotation, center.X, center.Y));

            var fill = PreviewPalette.Brush(color, 0.75);
            var pen = PreviewPalette.Pen(Darken(color), 1.2);
            var rect = new Rect(center.X - w / 2, center.Y - h / 2, w, h);

            switch ((pad.Shape ?? "").ToUpperInvariant())
            {
                case "ELLIPSE":
                case "OVAL":
                    dc.DrawEllipse(fill, pen, center, w / 2, h / 2);
                    break;
                case "POLYGON":
                    dc.DrawGeometry(fill, pen, RoundedRect(rect, Math.Min(w, h) * 0.18));
                    break;
                case "OCT":
                    dc.DrawGeometry(fill, pen, Octagon(center, w / 2, h / 2));
                    break;
                default:
                    dc.DrawRectangle(fill, pen, rect);
                    break;
            }

            var drill = Math.Max(0, pad.HoleRadius) * 2 * scale;
            if (drill > 0.8)
            {
                if (pad.HolePoints.Count >= 2)
                {
                    var slotPen = new Pen(Brushes.White, Math.Max(1.0, drill));
                    slotPen.Freeze();
                    dc.DrawLine(slotPen,
                        map(pad.HolePoints[0].X, pad.HolePoints[0].Y),
                        map(pad.HolePoints[1].X, pad.HolePoints[1].Y));
                }
                else
                {
                    dc.DrawEllipse(Brushes.White, PreviewPalette.Pen(Colors.Gray, 1.0), center,
                        drill / 2, drill / 2);
                }
            }

            dc.Pop();
        }

        private static void DrawHole(DrawingContext dc, EeFpShape hole,
            Func<double, double, Point> map, double scale, Color color)
        {
            var center = map(hole.X, hole.Y);
            dc.DrawEllipse(Brushes.White, PreviewPalette.Pen(color, 1.2), center,
                Math.Max(0.5, hole.Radius * scale), Math.Max(0.5, hole.Radius * scale));
        }

        private static void DrawArc(DrawingContext dc, EeFpShape shape,
            Func<double, double, Point> map, Pen pen)
        {
            if (!SvgPath.TryGetCircularArc(shape.Path, out var arc)) return;

            var steps = Math.Max(8, (int)Math.Abs(arc.SweepAngle / 6));
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                for (var i = 0; i <= steps; i++)
                {
                    var angle = (arc.StartAngle + arc.SweepAngle * i / steps) * Math.PI / 180.0;
                    var point = map(
                        arc.CenterX * EeUnits.MmPerUnit + Math.Cos(angle) * arc.RadiusX * EeUnits.MmPerUnit,
                        arc.CenterY * EeUnits.MmPerUnit + Math.Sin(angle) * arc.RadiusY * EeUnits.MmPerUnit);
                    if (i == 0) context.BeginFigure(point, false, false);
                    else context.LineTo(point, true, false);
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        private static Color Darken(Color color)
        {
            return Color.FromRgb((byte)(color.R * 0.7), (byte)(color.G * 0.7), (byte)(color.B * 0.7));
        }

        private static StreamGeometry RoundedRect(Rect rect, double radius)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
                context.BeginFigure(new Point(rect.Left + r, rect.Top), true, true);
                context.LineTo(new Point(rect.Right - r, rect.Top), true, false);
                context.ArcTo(new Point(rect.Right, rect.Top + r), new Size(r, r), 0, false,
                    SweepDirection.Clockwise, true, false);
                context.LineTo(new Point(rect.Right, rect.Bottom - r), true, false);
                context.ArcTo(new Point(rect.Right - r, rect.Bottom), new Size(r, r), 0, false,
                    SweepDirection.Clockwise, true, false);
                context.LineTo(new Point(rect.Left + r, rect.Bottom), true, false);
                context.ArcTo(new Point(rect.Left, rect.Bottom - r), new Size(r, r), 0, false,
                    SweepDirection.Clockwise, true, false);
                context.LineTo(new Point(rect.Left, rect.Top + r), true, false);
                context.ArcTo(new Point(rect.Left + r, rect.Top), new Size(r, r), 0, false,
                    SweepDirection.Clockwise, true, false);
            }
            geometry.Freeze();
            return geometry;
        }

        private static StreamGeometry Octagon(Point center, double rx, double ry)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var k = 0.4142;
                var points = new[]
                {
                    new Point(center.X - rx * k, center.Y - ry),
                    new Point(center.X + rx * k, center.Y - ry),
                    new Point(center.X + rx, center.Y - ry * k),
                    new Point(center.X + rx, center.Y + ry * k),
                    new Point(center.X + rx * k, center.Y + ry),
                    new Point(center.X - rx * k, center.Y + ry),
                    new Point(center.X - rx, center.Y + ry * k),
                    new Point(center.X - rx, center.Y - ry * k),
                };
                context.BeginFigure(points[0], true, true);
                for (var i = 1; i < points.Length; i++) context.LineTo(points[i], true, false);
            }
            geometry.Freeze();
            return geometry;
        }
    }
}
