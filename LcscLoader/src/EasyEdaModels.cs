using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace LcscLoader
{
    // EasyEDA stores schematic and footprint coordinates in units of 10 mil.
    internal static class EeUnits
    {
        public const double MmPerUnit = 0.254;
        public static double ToMm(double value) => value * MmPerUnit;
    }

    internal enum EeSymKind { Rectangle, Circle, Ellipse, Arc, Polyline, Polygon, Path, Text }

    internal sealed class EeSymShape
    {
        public EeSymKind Kind;
        public double X, Y, Width, Height, RadiusX, RadiusY;
        public double Rotation;
        public string Points = "";
        public string Path = "";
        public string Text = "";
        public string Stroke = "";
        public string Fill = "";
        public string Font = "Tahoma";
        public double FontSize = 7;
        public double StrokeWidth = 1;
        public string Anchor = "start";
    }

    internal sealed class EePin
    {
        public string Number = "";
        public string Name = "";
        public bool ShowName = true;
        public bool ShowNumber = true;
        public double X, Y;
        public int Rotation;
        public double Length = 10;
        public int ElectricalType;
    }

    internal sealed class EeSymbol
    {
        public string Name = "";
        public string Prefix = "U?";
        public double OriginX;
        public double OriginY;
        public readonly List<EeSymShape> Shapes = new List<EeSymShape>();
        public readonly List<EePin> Pins = new List<EePin>();
    }

    internal enum EeFpKind { Pad, Track, Hole, Via, Circle, Arc, Rectangle, Text }

    internal sealed class EeFpShape
    {
        public EeFpKind Kind;
        public string Shape = "";
        public double X, Y, Width, Height, Rotation;
        public double Radius, StrokeWidth, HoleRadius, HoleLength, Diameter;
        public double FontSize;
        public double PasteExpansion;
        public double SolderExpansion;
        public string LayerId = "";
        public string Number = "";
        public string Net = "";
        public string Path = "";
        public string Text = "";
        public string TextPath = "";
        public bool Plated;
        public bool Mirror;
        public bool Displayed = true;
        public readonly List<(double X, double Y)> Points = new List<(double, double)>();
        public readonly List<(double X, double Y)> HolePoints = new List<(double, double)>();
    }

    internal sealed class EeFootprint
    {
        public string Name = "";
        public double BoxX, BoxY, BoxWidth, BoxHeight;
        public readonly Dictionary<string, string> Layers = new Dictionary<string, string>();
        public readonly List<EeFpShape> Shapes = new List<EeFpShape>();
        public EeModel3D Model;
    }

    internal sealed class EeModel3D
    {
        public string Uuid = "";
        public string Title = "";
        public double OriginX, OriginY, Z;
        public double RotX, RotY, RotZ;
        public double Width, Height;
    }

    internal sealed class LcscPart
    {
        public string LcscId = "";
        public string Title = "";
        public string Description = "";
        public string PackageName = "";
        public string Manufacturer = "";
        public string ManufacturerPart = "";
        public string SupplierPart = "";
        public EeSymbol Symbol = new EeSymbol();
        public EeFootprint Footprint = new EeFootprint();
        public readonly List<KeyValuePair<string, string>> Parameters = new List<KeyValuePair<string, string>>();
    }

    internal static class EasyEdaParser
    {
        public static LcscPart Parse(string lcscId, string json)
        {
            var root = JObject.Parse(json);
            var component = JsonUtil.Obj(root["result"]);
            if (!component.HasValues)
                throw new InvalidOperationException("EasyEDA 返回了空器件数据");

            var part = new LcscPart { LcscId = lcscId };
            part.Title = JsonUtil.Str(component["title"], lcscId);
            part.Description = JsonUtil.Str(component["description"]);
            part.SupplierPart = JsonUtil.Str(component["lcsc"]?["number"], lcscId);

            var symbol = JsonUtil.Obj(ParseDocument(component["dataStr"]));
            var symbolHead = JsonUtil.Obj(symbol["head"]);
            var symbolParams = JsonUtil.Obj(symbolHead["c_para"]);
            part.Symbol.Name = JsonUtil.Str(symbolParams["name"], part.Title);
            part.Symbol.Prefix = JsonUtil.Str(symbolParams["pre"], "U?");
            part.Symbol.OriginX = JsonUtil.Num(symbolHead["x"]);
            part.Symbol.OriginY = JsonUtil.Num(symbolHead["y"]);
            part.Manufacturer = JsonUtil.Str(symbolParams["Manufacturer"]);
            part.ManufacturerPart = JsonUtil.Str(symbolParams["Manufacturer Part"]);
            part.SupplierPart = JsonUtil.Str(symbolParams["Supplier Part"], part.SupplierPart);

            foreach (var token in JsonUtil.Arr(symbol["shape"]))
            {
                var raw = token.Type == JTokenType.String ? (string)token : token.ToString();
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (raw.StartsWith("P~", StringComparison.Ordinal))
                    ParsePin(raw, part.Symbol);
                else
                    ParseSymbolShape(raw, part.Symbol);
            }

            var package = JsonUtil.Obj(component["packageDetail"]);
            var footprintDoc = JsonUtil.Obj(ParseDocument(package["dataStr"]));
            var footprintHead = JsonUtil.Obj(footprintDoc["head"]);
            var footprintParams = JsonUtil.Obj(footprintHead["c_para"]);
            part.PackageName = JsonUtil.Str(footprintParams["package"],
                JsonUtil.Str(package["title"], part.Symbol.Name));

            var box = JsonUtil.Obj(footprintDoc["BBox"]);
            part.Footprint.BoxX = EeUnits.ToMm(JsonUtil.Num(box["x"]));
            part.Footprint.BoxY = EeUnits.ToMm(JsonUtil.Num(box["y"]));
            part.Footprint.BoxWidth = EeUnits.ToMm(JsonUtil.Num(box["width"]));
            part.Footprint.BoxHeight = EeUnits.ToMm(JsonUtil.Num(box["height"]));
            part.Footprint.Name = part.PackageName;

            foreach (var token in JsonUtil.Arr(footprintDoc["layers"]))
            {
                var raw = token.Type == JTokenType.String ? (string)token : token.ToString();
                var fields = JsonUtil.Split(raw, '~');
                if (fields.Length >= 2) part.Footprint.Layers[fields[0]] = fields[1];
            }

            foreach (var token in JsonUtil.Arr(footprintDoc["shape"]))
            {
                var raw = token.Type == JTokenType.String ? (string)token : token.ToString();
                if (string.IsNullOrWhiteSpace(raw)) continue;
                ParseFootprintShape(raw, part.Footprint);
            }

            if (part.Footprint.Model == null)
            {
                var uuid3d = JsonUtil.Str(footprintHead["uuid_3d"]);
                if (!string.IsNullOrEmpty(uuid3d))
                    part.Footprint.Model = new EeModel3D { Uuid = uuid3d, Title = part.Footprint.Name };
            }

            AddParameter(part, "LCSC Part", part.SupplierPart);
            AddParameter(part, "Manufacturer", part.Manufacturer);
            AddParameter(part, "Manufacturer Part", part.ManufacturerPart);
            AddParameter(part, "Description", part.Description);
            AddParameter(part, "Package", part.PackageName);
            AddParameter(part, "EasyEDA UUID", JsonUtil.Str(component["uuid"]));
            AddParameter(part, "Supplier", "LCSC");
            AddParameter(part, "Supplier Part", part.SupplierPart);
            return part;
        }

        private static void AddParameter(LcscPart part, string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                part.Parameters.Add(new KeyValuePair<string, string>(name, value.Trim()));
        }

        private static JToken ParseDocument(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return new JObject();
            if (token.Type != JTokenType.String) return token;
            var text = (string)token;
            if (string.IsNullOrWhiteSpace(text)) return new JObject();
            try { return JObject.Parse(text); }
            catch { return new JObject(); }
        }

        private static void ParsePin(string raw, EeSymbol symbol)
        {
            var segments = raw.Split(new[] { "^^" }, StringSplitOptions.None);
            if (segments.Length < 5) return;
            var head = JsonUtil.Split(segments[0], '~');
            if (head.Length < 8) return;

            var pin = new EePin
            {
                ShowNumber = JsonUtil.Flag(new JValue(head[1]), true),
                ElectricalType = (int)Num(head[2]),
                Number = head[3],
                X = Num(head[4]),
                Y = Num(head[5]),
                Rotation = (int)Num(head[6]),
            };

            var name = JsonUtil.Split(segments[3], '~');
            if (name.Length >= 5)
            {
                pin.ShowName = JsonUtil.Flag(new JValue(name[0]), true);
                pin.Name = name[4];
            }

            var designator = JsonUtil.Split(segments[4], '~');
            if (designator.Length >= 5 && !string.IsNullOrWhiteSpace(designator[4]) && string.IsNullOrWhiteSpace(pin.Number))
                pin.Number = designator[4];

            var path = JsonUtil.Split(segments[2], '~');
            if (path.Length >= 1) pin.Length = PinLength(path[0], pin.Length);
            symbol.Pins.Add(pin);
        }

        private static double PinLength(string path, double fallback)
        {
            if (string.IsNullOrWhiteSpace(path)) return fallback;
            var horizontal = Regex.Match(path, @"[hH]\s*(-?[0-9.]+)");
            if (horizontal.Success && Num(horizontal.Groups[1].Value) != 0) return Math.Abs(Num(horizontal.Groups[1].Value));
            var vertical = Regex.Match(path, @"[vV]\s*(-?[0-9.]+)");
            if (vertical.Success && Num(vertical.Groups[1].Value) != 0) return Math.Abs(Num(vertical.Groups[1].Value));
            var line = Regex.Match(path, @"[lL]\s*(-?[0-9.]+)\s+(-?[0-9.]+)");
            if (line.Success)
            {
                var dx = Num(line.Groups[1].Value);
                var dy = Num(line.Groups[2].Value);
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length > 0) return length;
            }
            return fallback;
        }

        private static void ParseSymbolShape(string raw, EeSymbol symbol)
        {
            var parts = JsonUtil.Split(raw, '~');
            if (parts.Length == 0) return;
            switch (parts[0])
            {
                case "R":
                    if (parts.Length < 12) return;
                    symbol.Shapes.Add(new EeSymShape
                    {
                        Kind = EeSymKind.Rectangle,
                        X = Num(parts[1]), Y = Num(parts[2]),
                        RadiusX = Num(parts[3]), RadiusY = Num(parts[4]),
                        Width = Num(parts[5]), Height = Num(parts[6]),
                        Stroke = parts[7], StrokeWidth = Num(parts[8]), Fill = parts[10],
                    });
                    break;
                case "C":
                    if (parts.Length < 10) return;
                    symbol.Shapes.Add(new EeSymShape
                    {
                        Kind = EeSymKind.Circle,
                        X = Num(parts[1]), Y = Num(parts[2]), RadiusX = Num(parts[3]), RadiusY = Num(parts[3]),
                        Stroke = parts[4], StrokeWidth = Num(parts[5]), Fill = parts[7],
                    });
                    break;
                case "E":
                    if (parts.Length < 11) return;
                    symbol.Shapes.Add(new EeSymShape
                    {
                        Kind = EeSymKind.Ellipse,
                        X = Num(parts[1]), Y = Num(parts[2]),
                        RadiusX = Num(parts[3]), RadiusY = Num(parts[4]),
                        Stroke = parts[5], StrokeWidth = Num(parts[6]), Fill = parts[8],
                    });
                    break;
                case "A":
                    if (parts.Length < 4) return;
                    symbol.Shapes.Add(new EeSymShape
                    {
                        Kind = EeSymKind.Arc, Path = parts[1],
                        Stroke = parts[3], StrokeWidth = Num(parts[4]), Fill = "none",
                    });
                    break;
                case "PL":
                case "PG":
                    if (parts.Length < 7) return;
                    symbol.Shapes.Add(new EeSymShape
                    {
                        Kind = parts[0] == "PL" ? EeSymKind.Polyline : EeSymKind.Polygon,
                        Points = parts[1], Stroke = parts[2], StrokeWidth = Num(parts[3]), Fill = parts[5],
                    });
                    break;
                case "PT":
                    if (parts.Length < 6) return;
                    symbol.Shapes.Add(new EeSymShape
                    {
                        Kind = EeSymKind.Path, Path = parts[1],
                        Stroke = parts[2], StrokeWidth = Num(parts[3]), Fill = parts[5],
                    });
                    break;
                case "T":
                    if (parts.Length < 13) return;
                    symbol.Shapes.Add(new EeSymShape
                    {
                        Kind = EeSymKind.Text,
                        X = Num(parts[2]), Y = Num(parts[3]), Rotation = Num(parts[4]),
                        Stroke = parts[5], Font = parts[6], FontSize = ParseFontSize(parts[7]),
                        Text = parts[12], Anchor = parts.Length > 14 ? parts[14] : "start",
                    });
                    break;
            }
        }

        private static double ParseFontSize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 7;
            var text = raw.Replace("pt", "").Trim();
            return Num(text, 7);
        }

        private static void ParseFootprintShape(string raw, EeFootprint footprint)
        {
            var parts = JsonUtil.Split(raw, '~');
            if (parts.Length == 0) return;
            switch (parts[0])
            {
                case "PAD":
                    if (parts.Length < 12) return;
                    {
                        var pad = new EeFpShape
                        {
                            Kind = EeFpKind.Pad, Shape = parts[1],
                            X = EeUnits.ToMm(Num(parts[2])), Y = EeUnits.ToMm(Num(parts[3])),
                            Width = EeUnits.ToMm(Num(parts[4])), Height = EeUnits.ToMm(Num(parts[5])),
                            LayerId = parts[6], Net = parts[7], Number = parts[8],
                            HoleRadius = EeUnits.ToMm(Num(parts[9])),
                            Rotation = Num(parts[11]),
                            HoleLength = parts.Length > 13 ? EeUnits.ToMm(Num(parts[13])) : 0,
                            // PAD 记录 18/19 项依次为助焊（钢网）与阻焊扩展，单位同为 10mil。
                            PasteExpansion = ReadExpansion(parts, 17, 0),
                            SolderExpansion = ReadExpansion(parts, 18, DefaultSolderExpansionMm),
                            Plated = parts.Length > 15 ? JsonUtil.Flag(new JValue(parts[15]), true) : true,
                        };
                        pad.Points.AddRange(ParsePoints(parts[10]));
                        if (parts.Length > 14) pad.HolePoints.AddRange(ParsePoints(parts[14]));
                        footprint.Shapes.Add(pad);
                    }
                    break;
                case "TRACK":
                    if (parts.Length < 5) return;
                    {
                        var track = new EeFpShape
                        {
                            Kind = EeFpKind.Track, StrokeWidth = EeUnits.ToMm(Num(parts[1])),
                            LayerId = parts[2], Net = parts[3],
                        };
                        track.Points.AddRange(ParsePoints(parts[4]));
                        footprint.Shapes.Add(track);
                    }
                    break;
                case "HOLE":
                    if (parts.Length < 4) return;
                    footprint.Shapes.Add(new EeFpShape
                    {
                        Kind = EeFpKind.Hole,
                        X = EeUnits.ToMm(Num(parts[1])), Y = EeUnits.ToMm(Num(parts[2])),
                        Radius = EeUnits.ToMm(Num(parts[3])),
                    });
                    break;
                case "VIA":
                    if (parts.Length < 6) return;
                    footprint.Shapes.Add(new EeFpShape
                    {
                        Kind = EeFpKind.Via,
                        X = EeUnits.ToMm(Num(parts[1])), Y = EeUnits.ToMm(Num(parts[2])),
                        Diameter = EeUnits.ToMm(Num(parts[3])), Radius = EeUnits.ToMm(Num(parts[5])),
                    });
                    break;
                case "CIRCLE":
                    if (parts.Length < 6) return;
                    footprint.Shapes.Add(new EeFpShape
                    {
                        Kind = EeFpKind.Circle,
                        X = EeUnits.ToMm(Num(parts[1])), Y = EeUnits.ToMm(Num(parts[2])),
                        Radius = EeUnits.ToMm(Num(parts[3])), StrokeWidth = EeUnits.ToMm(Num(parts[4])),
                        LayerId = parts[5],
                    });
                    break;
                case "ARC":
                    if (parts.Length < 5) return;
                    footprint.Shapes.Add(new EeFpShape
                    {
                        Kind = EeFpKind.Arc, StrokeWidth = EeUnits.ToMm(Num(parts[1])),
                        LayerId = parts[2], Net = parts[3], Path = parts[4],
                    });
                    break;
                case "RECT":
                    if (parts.Length < 9) return;
                    footprint.Shapes.Add(new EeFpShape
                    {
                        Kind = EeFpKind.Rectangle,
                        X = EeUnits.ToMm(Num(parts[1])), Y = EeUnits.ToMm(Num(parts[2])),
                        Width = EeUnits.ToMm(Num(parts[3])), Height = EeUnits.ToMm(Num(parts[4])),
                        LayerId = parts[5], StrokeWidth = EeUnits.ToMm(Num(parts[8])),
                    });
                    break;
                case "TEXT":
                    if (parts.Length < 14) return;
                    footprint.Shapes.Add(new EeFpShape
                    {
                        Kind = EeFpKind.Text,
                        X = EeUnits.ToMm(Num(parts[2])), Y = EeUnits.ToMm(Num(parts[3])),
                        StrokeWidth = EeUnits.ToMm(Num(parts[4])), Rotation = Num(parts[5]),
                        Mirror = JsonUtil.Flag(new JValue(parts[6])), LayerId = parts[7],
                        FontSize = EeUnits.ToMm(Num(parts[9])), Text = parts[10],
                        TextPath = parts.Length > 11 ? parts[11] : "",
                        Displayed = parts.Length > 12 ? JsonUtil.Flag(new JValue(parts[12]), true) : true,
                    });
                    break;
                case "SVGNODE":
                    if (parts.Length < 2) return;
                    try
                    {
                        var node = JObject.Parse(raw.Substring(raw.IndexOf('~') + 1));
                        var attrs = JsonUtil.Obj(node["attrs"]);
                        var etype = JsonUtil.Str(attrs["c_etype"]);
                        if (!string.IsNullOrEmpty(etype) && etype != "outline3D") return;
                        var uuid = JsonUtil.Str(attrs["uuid"]);
                        if (string.IsNullOrEmpty(uuid)) return;
                        var origin = JsonUtil.Split(JsonUtil.Str(attrs["c_origin"]), ',');
                        var rotation = JsonUtil.Split(JsonUtil.Str(attrs["c_rotation"]), ',');
                        footprint.Model = new EeModel3D
                        {
                            Uuid = uuid,
                            Title = JsonUtil.Str(attrs["title"], footprint.Name),
                            OriginX = origin.Length > 0 ? Num(origin[0]) : 0,
                            OriginY = origin.Length > 1 ? Num(origin[1]) : 0,
                            Z = Num(JsonUtil.Str(attrs["z"]), 0),
                            RotX = rotation.Length > 0 ? Num(rotation[0]) : 0,
                            RotY = rotation.Length > 1 ? Num(rotation[1]) : 0,
                            RotZ = rotation.Length > 2 ? Num(rotation[2]) : 0,
                            Width = Num(JsonUtil.Str(attrs["c_width"]), 0),
                            Height = Num(JsonUtil.Str(attrs["c_height"]), 0),
                        };
                    }
                    catch
                    {
                        // A malformed model node must not discard the rest of the footprint.
                    }
                    break;
            }
        }

        private static IEnumerable<(double, double)> ParsePoints(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) yield break;
            var values = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i + 1 < values.Length; i += 2)
                yield return (EeUnits.ToMm(Num(values[i])), EeUnits.ToMm(Num(values[i + 1])));
        }

        private static double Num(string value)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
        }

        private static double Num(string value, double fallback)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                ? result
                : fallback;
        }

        // 立创 PAD 记录缺少扩展字段时，阻焊按 4mil（0.1016mm）兜底；有值则优先用器件值。
        private const double DefaultSolderExpansionMm = 0.1016;

        private static double ReadExpansion(string[] parts, int index, double fallbackMm)
        {
            if (parts.Length <= index) return fallbackMm;
            var text = parts[index].Trim();
            if (text.Length == 0) return fallbackMm;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? EeUnits.ToMm(value)
                : fallbackMm;
        }
    }

}
