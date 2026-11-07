using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LcscLoader;

namespace LcscStudio
{
    internal static class PreviewPalette
    {
        public static readonly Color Stroke = Color.FromRgb(0x80, 0x00, 0x00);
        public static readonly Color Fill = Color.FromRgb(0xFF, 0xFF, 0xB0);

        public static Color FromHex(string value, Color fallback)
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
                return Color.FromRgb((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
            }
            catch
            {
                return fallback;
            }
        }

        public static Color LayerColor(string layer)
        {
            switch (layer ?? "")
            {
                case "TopLayer": return Color.FromRgb(0xD0, 0x30, 0x30);
                case "BottomLayer": return Color.FromRgb(0x30, 0x50, 0xD0);
                case "TopSilkLayer": return Color.FromRgb(0xB0, 0x88, 0x00);
                case "BottomSilkLayer": return Color.FromRgb(0x80, 0x70, 0x50);
                case "TopPasteMaskLayer":
                case "BottomPasteMaskLayer": return Color.FromRgb(0x90, 0x90, 0x90);
                case "TopSolderMaskLayer":
                case "BottomSolderMaskLayer": return Color.FromRgb(0xA0, 0x60, 0xC0);
                case "Multi-Layer": return Color.FromRgb(0xC8, 0x96, 0x32);
                case "KeepOutLayer": return Color.FromRgb(0xE0, 0x20, 0xC0);
                case "DrillGuide":
                case "DrillDrawing": return Color.FromRgb(0x70, 0x70, 0x70);
                default: return Color.FromRgb(0x70, 0x70, 0x70);
            }
        }

        public static SolidColorBrush Brush(Color color, double alpha = 1.0)
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        public static Pen Pen(Color color, double thickness, double alpha = 1.0)
        {
            var pen = new Pen(Brush(color, alpha), Math.Max(1.0, thickness));
            pen.Freeze();
            return pen;
        }

        public static void DrawText(DrawingContext dc, string text, Point point, double size,
            Brush brush, double pixelsPerDip, double angle = 0, bool centered = true)
        {
            if (string.IsNullOrWhiteSpace(text) || size < 4) return;
            var formatted = new FormattedText(text, CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, new Typeface("Tahoma"), Math.Min(size, 64),
                brush, pixelsPerDip);

            if (angle != 0) dc.PushTransform(new RotateTransform(angle, point.X, point.Y));
            var origin = centered
                ? new Point(point.X - formatted.Width / 2, point.Y - formatted.Height / 2)
                : point;
            dc.DrawText(formatted, origin);
            if (angle != 0) dc.Pop();
        }

        public static void DrawPlaceholder(DrawingContext dc, double width, double height, string text)
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            DrawText(dc, text, new Point(width / 2, height / 2), 14,
                Brushes.Gray, 1.0);
        }
    }

    internal sealed class SymbolPreview : FrameworkElement
    {
        private EeSymbol _symbol;

        public EeSymbol Symbol
        {
            get => _symbol;
            set { _symbol = value; InvalidateVisual(); }
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

            if (_symbol == null)
            {
                PreviewPalette.DrawPlaceholder(dc, width, height, "无符号数据");
                return;
            }

            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));

            var originX = _symbol.OriginX;
            var originY = _symbol.OriginY;
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            void AddPoint(double x, double y)
            {
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            double MmX(double units) => (units - originX) * EeUnits.MmPerUnit;
            double MmY(double units) => (originY - units) * EeUnits.MmPerUnit;

            foreach (var shape in _symbol.Shapes)
            {
                switch (shape.Kind)
                {
                    case EeSymKind.Rectangle:
                        AddPoint(MmX(shape.X), MmY(shape.Y));
                        AddPoint(MmX(shape.X + shape.Width), MmY(shape.Y + shape.Height));
                        break;
                    case EeSymKind.Circle:
                    case EeSymKind.Ellipse:
                        AddPoint(MmX(shape.X - shape.RadiusX), MmY(shape.Y - shape.RadiusY));
                        AddPoint(MmX(shape.X + shape.RadiusX), MmY(shape.Y + shape.RadiusY));
                        break;
                    case EeSymKind.Arc:
                        if (SvgPath.TryGetCircularArc(shape.Path, out var arc))
                        {
                            var r = Math.Max(arc.RadiusX, arc.RadiusY) * EeUnits.MmPerUnit;
                            minX = Math.Min(minX, (arc.CenterX * EeUnits.MmPerUnit - originX * EeUnits.MmPerUnit) - r);
                            maxX = Math.Max(maxX, (arc.CenterX * EeUnits.MmPerUnit - originX * EeUnits.MmPerUnit) + r);
                            minY = Math.Min(minY, (originY * EeUnits.MmPerUnit - arc.CenterY * EeUnits.MmPerUnit) - r);
                            maxY = Math.Max(maxY, (originY * EeUnits.MmPerUnit - arc.CenterY * EeUnits.MmPerUnit) + r);
                        }
                        break;
                    case EeSymKind.Polyline:
                    case EeSymKind.Polygon:
                        foreach (var point in Conv.ParseUnitPoints(shape.Points))
                            AddPoint(MmX(point.X), MmY(point.Y));
                        break;
                    case EeSymKind.Path:
                        foreach (var sub in SvgPath.Parse(shape.Path, 0.02))
                            foreach (var point in sub.Points)
                                AddPoint(MmX(point.X), MmY(point.Y));
                        break;
                    case EeSymKind.Text:
                        AddPoint(MmX(shape.X), MmY(shape.Y));
                        break;
                }
            }

            foreach (var pin in _symbol.Pins)
            {
                var length = pin.Length;
                double baseX = pin.X, baseY = pin.Y;
                switch (((pin.Rotation % 360) + 360) % 360)
                {
                    case 0: baseX = pin.X - length; break;
                    case 90: baseY = pin.Y + length; break;
                    case 180: baseX = pin.X + length; break;
                    case 270: baseY = pin.Y - length; break;
                }
                AddPoint(MmX(pin.X), MmY(pin.Y));
                AddPoint(MmX(baseX), MmY(baseY));
            }

            if (minX > maxX || minY > maxY)
            {
                PreviewPalette.DrawPlaceholder(dc, width, height, "空符号");
                dc.Pop();
                return;
            }

            const double padding = 18;
            var spanX = Math.Max(0.2, maxX - minX);
            var spanY = Math.Max(0.2, maxY - minY);
            var scale = Math.Min((width - padding * 2) / spanX, (height - padding * 2) / spanY);
            if (scale <= 0 || double.IsInfinity(scale)) scale = 1;

            var centerX = (minX + maxX) / 2;
            var centerY = (minY + maxY) / 2;
            Point Map(double mmX, double mmY) => new Point(
                width / 2 + (mmX - centerX) * scale,
                height / 2 - (mmY - centerY) * scale);

            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            foreach (var shape in _symbol.Shapes)
            {
                var color = PreviewPalette.FromHex(shape.Stroke, PreviewPalette.Stroke);
                var fillColor = PreviewPalette.FromHex(shape.Fill, PreviewPalette.Fill);
                var hasFill = Conv.HasPaint(shape.Fill);
                var thickness = Math.Max(1.0, shape.StrokeWidth * EeUnits.MmPerUnit * scale);
                var pen = PreviewPalette.Pen(color, thickness);
                var brush = hasFill ? PreviewPalette.Brush(fillColor) : null;

                switch (shape.Kind)
                {
                    case EeSymKind.Rectangle:
                    {
                        var a = Map(MmX(shape.X), MmY(shape.Y));
                        var b = Map(MmX(shape.X + shape.Width), MmY(shape.Y + shape.Height));
                        dc.DrawRectangle(brush, pen, new Rect(a, b));
                        break;
                    }
                    case EeSymKind.Circle:
                    case EeSymKind.Ellipse:
                    {
                        var center = Map(MmX(shape.X), MmY(shape.Y));
                        var rx = Math.Abs(shape.RadiusX) * EeUnits.MmPerUnit * scale;
                        var ry = Math.Abs(shape.RadiusY) * EeUnits.MmPerUnit * scale;
                        dc.DrawEllipse(brush, pen, center, Math.Max(0.5, rx), Math.Max(0.5, ry));
                        break;
                    }
                    case EeSymKind.Arc:
                        DrawArc(dc, shape, MmX, MmY, Map, pen, scale);
                        break;
                    case EeSymKind.Polyline:
                    case EeSymKind.Polygon:
                    {
                        var geometry = BuildPolyline(Conv.ParseUnitPoints(shape.Points),
                            MmX, MmY, Map, shape.Kind == EeSymKind.Polygon);
                        if (geometry != null)
                            dc.DrawGeometry(shape.Kind == EeSymKind.Polygon ? brush : null, pen, geometry);
                        break;
                    }
                    case EeSymKind.Path:
                    {
                        foreach (var sub in SvgPath.Parse(shape.Path, 0.02))
                        {
                            var geometry = BuildPolyline(sub.Points, MmX, MmY, Map, sub.Closed);
                            if (geometry != null)
                                dc.DrawGeometry(sub.Closed ? brush : null, pen, geometry);
                        }
                        break;
                    }
                    case EeSymKind.Text:
                    {
                        var point = Map(MmX(shape.X), MmY(shape.Y));
                        var size = Math.Max(7, shape.FontSize * EeUnits.MmPerUnit * scale);
                        var textColor = Conv.HasPaint(shape.Stroke) ? color : Colors.Black;
                        PreviewPalette.DrawText(dc, shape.Text, point, size,
                            PreviewPalette.Brush(textColor), dpi, -shape.Rotation);
                        break;
                    }
                }
            }

            var pinPen = PreviewPalette.Pen(PreviewPalette.Stroke, Math.Max(1.0, 0.18 * scale));
            var pinDot = PreviewPalette.Brush(PreviewPalette.Stroke);
            foreach (var pin in _symbol.Pins)
            {
                var length = pin.Length;
                double baseX = pin.X, baseY = pin.Y;
                switch (((pin.Rotation % 360) + 360) % 360)
                {
                    case 0: baseX = pin.X - length; break;
                    case 90: baseY = pin.Y + length; break;
                    case 180: baseX = pin.X + length; break;
                    case 270: baseY = pin.Y - length; break;
                }

                var connection = Map(MmX(pin.X), MmY(pin.Y));
                var start = Map(MmX(baseX), MmY(baseY));
                dc.DrawLine(pinPen, start, connection);
                dc.DrawEllipse(pinDot, null, connection, 2.2, 2.2);

                var mid = new Point((start.X + connection.X) / 2, (start.Y + connection.Y) / 2);
                var labelSize = Math.Max(7, 0.8 * scale);
                if (pin.ShowNumber)
                    PreviewPalette.DrawText(dc, pin.Number, new Point(mid.X, mid.Y - 7), labelSize,
                        PreviewPalette.Brush(Color.FromRgb(0x60, 0x60, 0x60)), dpi);
                if (pin.ShowName)
                    PreviewPalette.DrawText(dc, pin.Name, new Point(start.X, start.Y - 7), labelSize,
                        PreviewPalette.Brush(Color.FromRgb(0x20, 0x60, 0xA0)), dpi);
            }

            dc.Pop();
        }

        private static void DrawArc(DrawingContext dc, EeSymShape shape,
            Func<double, double> mmX, Func<double, double> mmY,
            Func<double, double, Point> map, Pen pen, double scale)
        {
            if (!SvgPath.TryGetCircularArc(shape.Path, out var arc)) return;

            var steps = Math.Max(8, (int)Math.Abs(arc.SweepAngle / 6));
            var points = new List<(double X, double Y)>();
            for (var i = 0; i <= steps; i++)
            {
                var angle = (arc.StartAngle + arc.SweepAngle * i / steps) * Math.PI / 180.0;
                points.Add((
                    arc.CenterX + Math.Cos(angle) * arc.RadiusX,
                    arc.CenterY + Math.Sin(angle) * arc.RadiusY));
            }

            var geometry = BuildPolyline(points, mmX, mmY, map, false);
            if (geometry != null) dc.DrawGeometry(null, pen, geometry);
        }

        private static StreamGeometry BuildPolyline(
            IEnumerable<(double X, double Y)> units,
            Func<double, double> mmX, Func<double, double> mmY,
            Func<double, double, Point> map, bool closed)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var first = true;
                foreach (var point in units)
                {
                    var mapped = map(mmX(point.X), mmY(point.Y));
                    if (first)
                    {
                        context.BeginFigure(mapped, true, closed);
                        first = false;
                    }
                    else
                    {
                        context.LineTo(mapped, true, false);
                    }
                }
                if (first) return null;
            }
            geometry.Freeze();
            return geometry;
        }
    }
}
