using System;
using System.Collections.Generic;
using System.Globalization;

namespace LcscLoader
{
    internal static class ImportLog
    {
        private static readonly List<string> Lines = new List<string>();

        public static void Clear()
        {
            Lines.Clear();
        }

        public static void Add(string message)
        {
            if (!string.IsNullOrWhiteSpace(message)) Lines.Add(message);
        }

        public static string Text => string.Join(Environment.NewLine, Lines);
    }

    internal static class Conv
    {
        public static bool HasPaint(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !string.Equals(value.Trim(), "none", StringComparison.OrdinalIgnoreCase);
        }

        public static int Color(string value, int fallback)
        {
            if (!HasPaint(value)) return fallback;
            var text = value.Trim();
            if (text.StartsWith("#", StringComparison.Ordinal)) text = text.Substring(1);
            if (text.Length == 8) text = text.Substring(2);
            if (text.Length != 6 ||
                !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                return fallback;
            var r = (rgb >> 16) & 0xFF;
            var g = (rgb >> 8) & 0xFF;
            var b = rgb & 0xFF;
            return (b << 16) | (g << 8) | r;
        }

        public static int Rgb(int r, int g, int b) => (b << 16) | (g << 8) | r;

        public const int DefaultStroke = 0x000080;   // dark red, as encoded above
        public const int DefaultFill = 0xB0FFFF;     // pale yellow, as encoded above

        public static IEnumerable<(double X, double Y)> ParseUnitPoints(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;
            var fields = text.Split(new[] { ' ', '\t', '\r', '\n', ',' },
                StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i + 1 < fields.Length; i += 2)
            {
                if (double.TryParse(fields[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                    double.TryParse(fields[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                    yield return (x, y);
            }
        }

        public static double Num(string value, double fallback = 0)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                ? result
                : fallback;
        }
    }
}
