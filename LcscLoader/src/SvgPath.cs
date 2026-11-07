using System;
using System.Collections.Generic;
using System.Globalization;

namespace LcscLoader
{
    internal sealed class SvgSubPath
    {
        public readonly List<(double X, double Y)> Points = new List<(double, double)>();
        public bool Closed;
    }

    internal sealed class SvgArcSegment
    {
        public double StartX, StartY, EndX, EndY;
        public double RadiusX, RadiusY, Rotation;
        public bool LargeArc, Sweep;
        public double CenterX, CenterY;
        public double StartAngle, EndAngle;
        public double SweepAngle;

        public bool IsCircular =>
            Math.Abs(RadiusX - RadiusY) <= Math.Max(RadiusX, RadiusY) * 0.01;
    }

    // Flattens SVG path data (M/L/H/V/C/S/Q/T/A/Z) into polylines. Coordinates are
    // left in the source space, including its Y direction; callers convert.
    internal static class SvgPath
    {
        public static List<SvgSubPath> Parse(string pathData, double tolerance = 0.05)
        {
            return Parse(pathData, tolerance, null);
        }

        public static List<SvgSubPath> Parse(string pathData, double tolerance, List<SvgArcSegment> arcs)
        {
            var result = new List<SvgSubPath>();
            if (string.IsNullOrWhiteSpace(pathData)) return result;
            if (tolerance <= 0) tolerance = 0.05;

            var lexer = new Lexer(pathData);
            SvgSubPath current = null;
            char command = '\0';
            char lastCommand = '\0';
            double cx = 0, cy = 0, startX = 0, startY = 0;
            double lastCtrlX = 0, lastCtrlY = 0;

            while (true)
            {
                if (lexer.End) break;
                var next = lexer.PeekCommand();
                if (next != '\0') command = lexer.ReadCommand();
                else if (command == '\0') break;

                var upper = char.ToUpperInvariant(command);
                var relative = char.IsLower(command);

                switch (upper)
                {
                    case 'M':
                    {
                        if (!lexer.TryReadPair(out var x, out var y)) return result;
                        if (relative) { x += cx; y += cy; }
                        current = new SvgSubPath();
                        current.Points.Add((x, y));
                        result.Add(current);
                        cx = startX = x;
                        cy = startY = y;
                        command = relative ? 'l' : 'L';
                        lastCommand = 'M';
                        break;
                    }
                    case 'L':
                    {
                        if (!lexer.TryReadPair(out var x, out var y)) return result;
                        if (relative) { x += cx; y += cy; }
                        EnsureCurrent(result, current, cx, cy);
                        current = current ?? result[result.Count - 1];
                        current.Points.Add((x, y));
                        cx = x; cy = y;
                        lastCommand = 'L';
                        break;
                    }
                    case 'H':
                    {
                        if (!lexer.TryReadNumber(out var x)) return result;
                        if (relative) x += cx;
                        EnsureCurrent(result, current, cx, cy);
                        current = current ?? result[result.Count - 1];
                        current.Points.Add((x, cy));
                        cx = x;
                        lastCommand = 'H';
                        break;
                    }
                    case 'V':
                    {
                        if (!lexer.TryReadNumber(out var y)) return result;
                        if (relative) y += cy;
                        EnsureCurrent(result, current, cx, cy);
                        current = current ?? result[result.Count - 1];
                        current.Points.Add((cx, y));
                        cy = y;
                        lastCommand = 'V';
                        break;
                    }
                    case 'C':
                    {
                        if (!lexer.TryReadN(6, out var v)) return result;
                        double x1 = v[0], y1 = v[1], x2 = v[2], y2 = v[3], x = v[4], y = v[5];
                        if (relative)
                        {
                            x1 += cx; y1 += cy; x2 += cx; y2 += cy; x += cx; y += cy;
                        }
                        EnsureCurrent(result, current, cx, cy);
                        current = current ?? result[result.Count - 1];
                        AddCubic(current.Points, cx, cy, x1, y1, x2, y2, x, y, tolerance);
                        lastCtrlX = x2; lastCtrlY = y2;
                        cx = x; cy = y;
                        lastCommand = 'C';
                        break;
                    }
                    case 'S':
                    {
                        if (!lexer.TryReadN(4, out var v)) return result;
                        double x2 = v[0], y2 = v[1], x = v[2], y = v[3];
                        if (relative)
                        {
                            x2 += cx; y2 += cy; x += cx; y += cy;
                        }
                        double x1, y1;
                        if (lastCommand == 'C' || lastCommand == 'S')
                        {
                            x1 = 2 * cx - lastCtrlX;
                            y1 = 2 * cy - lastCtrlY;
                        }
                        else
                        {
                            x1 = cx; y1 = cy;
                        }
                        EnsureCurrent(result, current, cx, cy);
                        current = current ?? result[result.Count - 1];
                        AddCubic(current.Points, cx, cy, x1, y1, x2, y2, x, y, tolerance);
                        lastCtrlX = x2; lastCtrlY = y2;
                        cx = x; cy = y;
                        lastCommand = 'S';
                        break;
                    }
                    case 'Q':
                    {
                        if (!lexer.TryReadN(4, out var v)) return result;
                        double x1 = v[0], y1 = v[1], x = v[2], y = v[3];
                        if (relative)
                        {
                            x1 += cx; y1 += cy; x += cx; y += cy;
                        }
                        EnsureCurrent(result, current, cx, cy);
                        current = current ?? result[result.Count - 1];
                        AddQuadratic(current.Points, cx, cy, x1, y1, x, y, tolerance);
                        lastCtrlX = x1; lastCtrlY = y1;
                        cx = x; cy = y;
                        lastCommand = 'Q';
                        break;
                    }
                    case 'T':
                    {
                        if (!lexer.TryReadPair(out var x, out var y)) return result;
                        if (relative) { x += cx; y += cy; }
                        double x2, y2;
                        if (lastCommand == 'Q' || lastCommand == 'T')
                        {
                            x2 = 2 * cx - lastCtrlX;
                            y2 = 2 * cy - lastCtrlY;
                        }
                        else
                        {
                            x2 = cx; y2 = cy;
                        }
                        EnsureCurrent(result, current, cx, cy);
                        current = current ?? result[result.Count - 1];
                        AddQuadratic(current.Points, cx, cy, x2, y2, x, y, tolerance);
                        lastCtrlX = x2; lastCtrlY = y2;
                        cx = x; cy = y;
                        lastCommand = 'T';
                        break;
                    }
                    case 'A':
                    {
                        if (!lexer.TryReadN(7, out var v)) return result;
                        double rx = v[0], ry = v[1], rotation = v[2];
                        var large = Flag(v[3]);
                        var sweep = Flag(v[4]);
                        double x = v[5], y = v[6];
                        if (relative) { x += cx; y += cy; }
                        EnsureCurrent(result, current, cx, cy);
                        current = current ?? result[result.Count - 1];

                        if (!ComputeArc(cx, cy, x, y, rx, ry, rotation, large, sweep,
                                out var acx, out var acy, out var theta1, out var dtheta))
                        {
                            current.Points.Add((x, y));
                        }
                        else
                        {
                            if (arcs != null)
                            {
                                arcs.Add(new SvgArcSegment
                                {
                                    StartX = cx, StartY = cy, EndX = x, EndY = y,
                                    RadiusX = Math.Abs(rx), RadiusY = Math.Abs(ry),
                                    Rotation = rotation, LargeArc = large, Sweep = sweep,
                                    CenterX = acx, CenterY = acy,
                                    StartAngle = NormalizeDegrees(theta1 * 180.0 / Math.PI),
                                    SweepAngle = dtheta * 180.0 / Math.PI,
                                    EndAngle = NormalizeDegrees((theta1 + dtheta) * 180.0 / Math.PI),
                                });
                            }
                            AddArc(current.Points, acx, acy, Math.Abs(rx), Math.Abs(ry),
                                rotation, theta1, dtheta, tolerance);
                        }
                        cx = x; cy = y;
                        lastCommand = 'A';
                        break;
                    }
                    case 'Z':
                    {
                        if (current != null) current.Closed = true;
                        cx = startX; cy = startY;
                        lastCommand = 'Z';
                        command = '\0';
                        break;
                    }
                    default:
                        return result;
                }
            }

            foreach (var sub in result)
            {
                if (sub.Points.Count > 1 && Near(sub.Points[0], sub.Points[sub.Points.Count - 1]))
                    sub.Closed = true;
            }
            return result;
        }

        // Extracts the first circular arc of a path as center/radius/angles.
        public static bool TryGetCircularArc(string pathData, out SvgArcSegment arc)
        {
            arc = null;
            if (string.IsNullOrWhiteSpace(pathData)) return false;
            var segments = new List<SvgArcSegment>();
            try { Parse(pathData, 1.0, segments); }
            catch { return false; }
            if (segments.Count == 0) return false;

            SvgArcSegment best = null;
            foreach (var candidate in segments)
            {
                if (candidate.RadiusX <= 0 || candidate.RadiusY <= 0) continue;
                if (best == null || (candidate.IsCircular && !best.IsCircular)) best = candidate;
            }
            if (best == null) return false;
            arc = best;
            return true;
        }

        private static void EnsureCurrent(List<SvgSubPath> result, SvgSubPath current, double x, double y)
        {
            if (current != null) return;
            var sub = new SvgSubPath();
            sub.Points.Add((x, y));
            result.Add(sub);
        }

        private static bool ComputeArc(
            double x1, double y1, double x2, double y2,
            double rx, double ry, double rotationDegrees,
            bool largeArc, bool sweep,
            out double cx, out double cy, out double theta1, out double deltaTheta)
        {
            cx = cy = theta1 = deltaTheta = 0;
            rx = Math.Abs(rx);
            ry = Math.Abs(ry);
            if (rx < 1e-12 || ry < 1e-12) return false;

            var phi = rotationDegrees * Math.PI / 180.0;
            var cosPhi = Math.Cos(phi);
            var sinPhi = Math.Sin(phi);

            var dx = (x1 - x2) / 2.0;
            var dy = (y1 - y2) / 2.0;
            var x1p = cosPhi * dx + sinPhi * dy;
            var y1p = -sinPhi * dx + cosPhi * dy;

            var lambda = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
            if (lambda > 1)
            {
                var scale = Math.Sqrt(lambda);
                rx *= scale;
                ry *= scale;
            }

            var numerator = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
            var denominator = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
            if (denominator < 1e-24) return false;

            var coefficient = Math.Sqrt(Math.Max(0, numerator / denominator)) * (largeArc == sweep ? -1 : 1);
            var cxp = coefficient * (rx * y1p / ry);
            var cyp = -coefficient * (ry * x1p / rx);

            cx = cosPhi * cxp - sinPhi * cyp + (x1 + x2) / 2.0;
            cy = sinPhi * cxp + cosPhi * cyp + (y1 + y2) / 2.0;

            var ux = (x1p - cxp) / rx;
            var uy = (y1p - cyp) / ry;
            var vx = (-x1p - cxp) / rx;
            var vy = (-y1p - cyp) / ry;

            theta1 = Math.Atan2(uy, ux);
            var delta = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
            if (!sweep && delta > 0) delta -= 2 * Math.PI;
            else if (sweep && delta < 0) delta += 2 * Math.PI;

            deltaTheta = delta;
            return true;
        }

        private static void AddCubic(List<(double X, double Y)> points,
            double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3,
            double tolerance)
        {
            var approx = Distance(x0, y0, x1, y1) + Distance(x1, y1, x2, y2) + Distance(x2, y2, x3, y3);
            var steps = ClampSteps(approx / Math.Max(tolerance, 1e-6));
            for (var i = 1; i <= steps; i++)
            {
                var t = (double)i / steps;
                var mt = 1 - t;
                var a = mt * mt * mt;
                var b = 3 * mt * mt * t;
                var c = 3 * mt * t * t;
                var d = t * t * t;
                points.Add((a * x0 + b * x1 + c * x2 + d * x3, a * y0 + b * y1 + c * y2 + d * y3));
            }
        }

        private static void AddQuadratic(List<(double X, double Y)> points,
            double x0, double y0, double x1, double y1, double x2, double y2,
            double tolerance)
        {
            var approx = Distance(x0, y0, x1, y1) + Distance(x1, y1, x2, y2);
            var steps = ClampSteps(approx / Math.Max(tolerance, 1e-6));
            for (var i = 1; i <= steps; i++)
            {
                var t = (double)i / steps;
                var mt = 1 - t;
                points.Add((mt * mt * x0 + 2 * mt * t * x1 + t * t * x2,
                            mt * mt * y0 + 2 * mt * t * y1 + t * t * y2));
            }
        }

        private static void AddArc(List<(double X, double Y)> points,
            double cx, double cy, double rx, double ry, double rotationDegrees,
            double theta1, double deltaTheta, double tolerance)
        {
            var phi = rotationDegrees * Math.PI / 180.0;
            var cosPhi = Math.Cos(phi);
            var sinPhi = Math.Sin(phi);
            var arcLength = Math.Abs(deltaTheta) * Math.Max(rx, ry);
            var steps = ClampSteps(arcLength / Math.Max(tolerance, 1e-6));

            for (var i = 1; i <= steps; i++)
            {
                var theta = theta1 + deltaTheta * i / steps;
                var cosTheta = Math.Cos(theta);
                var sinTheta = Math.Sin(theta);
                var x = cx + rx * cosTheta * cosPhi - ry * sinTheta * sinPhi;
                var y = cy + rx * cosTheta * sinPhi + ry * sinTheta * cosPhi;
                points.Add((x, y));
            }
        }

        private static int ClampSteps(double estimate)
        {
            if (double.IsNaN(estimate) || double.IsInfinity(estimate)) return 8;
            return Math.Max(4, Math.Min(720, (int)Math.Ceiling(estimate)));
        }

        private static double Distance(double x0, double y0, double x1, double y1)
        {
            var dx = x1 - x0;
            var dy = y1 - y0;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool Near((double X, double Y) a, (double X, double Y) b)
        {
            return Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;
        }

        private static bool Flag(double value)
        {
            return Math.Abs(value) > 0.5;
        }

        private static double NormalizeDegrees(double degrees)
        {
            var value = degrees % 360.0;
            if (value < 0) value += 360.0;
            return value;
        }

        private sealed class Lexer
        {
            private readonly string _text;
            private int _index;

            public Lexer(string text)
            {
                _text = text;
            }

            public bool End
            {
                get
                {
                    SkipSeparators();
                    return _index >= _text.Length;
                }
            }

            public char PeekCommand()
            {
                SkipSeparators();
                if (_index >= _text.Length) return '\0';
                var c = _text[_index];
                return IsCommand(c) ? c : '\0';
            }

            public char ReadCommand()
            {
                SkipSeparators();
                return _text[_index++];
            }

            public bool TryReadPair(out double x, out double y)
            {
                if (TryReadNumber(out x) && TryReadNumber(out y)) return true;
                y = 0;
                return false;
            }

            public bool TryReadN(int count, out double[] values)
            {
                values = new double[count];
                for (var i = 0; i < count; i++)
                {
                    if (!TryReadNumber(out values[i])) return false;
                }
                return true;
            }

            public bool TryReadNumber(out double value)
            {
                value = 0;
                SkipSeparators();
                var start = _index;
                if (_index < _text.Length && (_text[_index] == '+' || _text[_index] == '-')) _index++;

                var digits = 0;
                while (_index < _text.Length && char.IsDigit(_text[_index])) { _index++; digits++; }
                if (_index < _text.Length && _text[_index] == '.')
                {
                    _index++;
                    while (_index < _text.Length && char.IsDigit(_text[_index])) { _index++; digits++; }
                }
                if (digits == 0)
                {
                    _index = start;
                    return false;
                }

                if (_index < _text.Length && (_text[_index] == 'e' || _text[_index] == 'E'))
                {
                    var exponentStart = _index;
                    _index++;
                    if (_index < _text.Length && (_text[_index] == '+' || _text[_index] == '-')) _index++;
                    var exponentDigits = 0;
                    while (_index < _text.Length && char.IsDigit(_text[_index])) { _index++; exponentDigits++; }
                    if (exponentDigits == 0) _index = exponentStart;
                }

                return double.TryParse(_text.Substring(start, _index - start),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            private void SkipSeparators()
            {
                while (_index < _text.Length &&
                       (char.IsWhiteSpace(_text[_index]) || _text[_index] == ','))
                    _index++;
            }

            private static bool IsCommand(char c)
            {
                switch (c)
                {
                    case 'M': case 'm': case 'Z': case 'z':
                    case 'L': case 'l': case 'H': case 'h': case 'V': case 'v':
                    case 'C': case 'c': case 'S': case 's': case 'Q': case 'q':
                    case 'T': case 't': case 'A': case 'a':
                        return true;
                    default:
                        return false;
                }
            }
        }
    }
}
