using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace LcscLoader
{
    internal static class JsonUtil
    {
        public static JObject Obj(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return new JObject();
            if (token is JObject obj) return obj;
            if (token.Type == JTokenType.String)
            {
                var text = (string)token;
                if (string.IsNullOrWhiteSpace(text)) return new JObject();
                try { return JObject.Parse(text); }
                catch { return new JObject(); }
            }
            return new JObject();
        }

        public static JArray Arr(JToken token)
        {
            return token as JArray ?? new JArray();
        }

        public static string Str(JToken token, string fallback = "")
        {
            if (token == null || token.Type == JTokenType.Null) return fallback;
            var value = token.Type == JTokenType.String ? (string)token : token.ToString();
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        public static double Num(JToken token, double fallback = 0)
        {
            if (token == null || token.Type == JTokenType.Null) return fallback;
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                return (double)token;
            var text = Str(token);
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        }

        public static double Num(JObject obj, string name, double fallback = 0)
        {
            return obj == null ? fallback : Num(obj[name], fallback);
        }

        public static string Str(JObject obj, string name, string fallback = "")
        {
            return obj == null ? fallback : Str(obj[name], fallback);
        }

        public static bool Flag(JToken token, bool fallback = false)
        {
            if (token == null || token.Type == JTokenType.Null) return fallback;
            if (token.Type == JTokenType.Boolean) return (bool)token;
            var text = Str(token).ToLowerInvariant();
            if (text == "1" || text == "true" || text == "yes" || text == "y" || text == "show") return true;
            if (text == "0" || text == "false" || text == "no" || text == "n") return false;
            return fallback;
        }

        public static string[] Split(string value, char separator)
        {
            return string.IsNullOrEmpty(value) ? Array.Empty<string>() : value.Split(separator);
        }
    }
}
