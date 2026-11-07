using System;
using System.Linq;
using PCB;

namespace LcscLoader
{
    internal sealed class FootprintImport
    {
        public IPCB_LibComponent Component;
        public string PatternName = "";
        public bool Skipped;
        public EeFootprint Footprint;
    }

    internal static class FootprintImporter
    {
        public static FootprintImport Import(LcscPart part, bool overwrite)
        {
            var result = new FootprintImport();
            var pcbLib = AdApi.PcbServer.GetCurrentPCBLibrary();
            if (pcbLib == null)
                throw new InvalidOperationException("请先打开一个 PCB 库（PcbLib）文档");

            if (string.IsNullOrWhiteSpace(part.Footprint.Name) && part.Footprint.Shapes.Count == 0)
            {
                ImportLog.Add("该器件没有可用的 PCB 封装，跳过：" + part.LcscId);
                return result;
            }

            var baseName = SafeName(part.Footprint.Name);
            if (string.IsNullOrEmpty(baseName)) baseName = part.LcscId + "_FP";

            var existing = pcbLib.GetComponentByName(baseName);
            if (existing != null)
            {
                if (!overwrite)
                {
                    ImportLog.Add("PCB 封装已存在，跳过：" + baseName);
                    result.Component = existing;
                    result.PatternName = baseName;
                    result.Footprint = part.Footprint;
                    result.Skipped = true;
                    return result;
                }

                object doomed = existing;
                pcbLib.RemoveComponent(ref doomed);
            }

            var component = pcbLib.CreateNewComponent();
            if (component == null)
                throw new InvalidOperationException("无法创建 PCB 封装");

            pcbLib.SetState_CurrentComponent(component);
            var pattern = pcbLib.GetUniqueCompName(baseName);
            component.SetState_Pattern(pattern);
            component.SetState_Description(part.Description);

            var context = new FpContext(part.Footprint, component);
            foreach (var shape in part.Footprint.Shapes)
            {
                try
                {
                    AddShape(context, shape);
                }
                catch (Exception ex)
                {
                    ImportLog.Add("封装图元 " + shape.Kind + " 转换失败：" + ex.Message);
                }
            }

            ImportLog.Add(string.Format("PCB 封装：{0}（{1} 个图元）", pattern, part.Footprint.Shapes.Count));

            result.Component = component;
            result.PatternName = pattern;
            result.Footprint = part.Footprint;
            return result;
        }

        private sealed class FpContext
        {
            public readonly EeFootprint Footprint;
            public readonly IPCB_LibComponent Component;
            private readonly int _offsetX;
            private readonly int _offsetY;

            public FpContext(EeFootprint footprint, IPCB_LibComponent component)
            {
                Footprint = footprint;
                Component = component;
                _offsetX = component.GetState_XLocation();
                _offsetY = component.GetState_YLocation();
            }

            public double X(double x) => x - Footprint.BoxX - Footprint.BoxWidth / 2.0;

            public double Y(double y) => Footprint.BoxHeight - (y - Footprint.BoxY) - Footprint.BoxHeight / 2.0;

            public int CoordX(double mm) => AdApi.MmToCoord(mm) + _offsetX;

            public int CoordY(double mm) => AdApi.MmToCoord(mm) + _offsetY;

            public bool TryLayer(string layerId, out TV6_Layer layer)
            {
                layer = TV6_Layer.eV6_NoLayer;
                if (string.IsNullOrWhiteSpace(layerId)) return false;
                string error;
                return Footprint.Layers.TryGetValue(layerId, out var name)
                    ? LayerMap.TryMap(name, out layer, out error)
                    : LayerMap.TryMap(layerId, out layer, out error);
            }
        }

        private static void AddShape(FpContext context, EeFpShape shape)
        {
            switch (shape.Kind)
            {
                case EeFpKind.Pad:
                    AddPad(context, shape);
                    break;
                case EeFpKind.Track:
                    AddTrack(context, shape);
                    break;
                case EeFpKind.Hole:
                    AddHole(context, shape);
                    break;
                case EeFpKind.Via:
                    AddVia(context, shape);
                    break;
                case EeFpKind.Circle:
                    AddCircle(context, shape);
                    break;
                case EeFpKind.Arc:
                    AddArc(context, shape);
                    break;
                case EeFpKind.Rectangle:
                    AddRectangle(context, shape);
                    break;
                case EeFpKind.Text:
                    AddText(context, shape);
                    break;
            }
        }

        private static void AddPad(FpContext context, EeFpShape shape)
        {
            if (!context.TryLayer(shape.LayerId, out var layer))
            {
                ImportLog.Add("焊盘 " + shape.Number + " 的层无法识别：" + shape.LayerId);
                return;
            }

            var padShape = PadShape(shape);

            var pad = CreatePad(context, shape.X, shape.Y, shape.Width, shape.Height,
                shape.HoleRadius * 2.0, shape.HoleLength, shape.Number, padShape, layer,
                shape.Plated, shape.Rotation, SlotAngle(shape));
            Attach(context, pad);
        }

        private static void AddHole(FpContext context, EeFpShape shape)
        {
            var diameter = shape.Radius * 2.0;
            var pad = CreatePad(context, shape.X, shape.Y, diameter, diameter, diameter, 0,
                "", TShape.eRounded, TV6_Layer.eV6_MultiLayer, false, 0, 0);
            if (pad == null) return;
            ClearMaskExpansion(pad);
            Attach(context, pad);
        }

        private static void AddVia(FpContext context, EeFpShape shape)
        {
            var via = Create<IPCB_Via>(TObjectId.eViaObject);
            if (via == null) return;

            via.SetState_Mode(TPadMode.ePadMode_Simple);
            via.SetState_XLocation(context.CoordX(context.X(shape.X)));
            via.SetState_YLocation(context.CoordY(context.Y(shape.Y)));
            via.SetState_Size(AdApi.MmToCoord(shape.Diameter));
            via.SetState_HoleSize(AdApi.MmToCoord(shape.Radius * 2.0));
            Attach(context, via);
        }

        private static void AddTrack(FpContext context, EeFpShape shape)
        {
            if (!context.TryLayer(shape.LayerId, out var layer))
            {
                ImportLog.Add("走线层无法识别：" + shape.LayerId);
                return;
            }

            for (var i = 1; i < shape.Points.Count; i++)
            {
                AddLine(context, layer,
                    context.X(shape.Points[i - 1].X), context.Y(shape.Points[i - 1].Y),
                    context.X(shape.Points[i].X), context.Y(shape.Points[i].Y),
                    shape.StrokeWidth);
            }
        }

        private static void AddCircle(FpContext context, EeFpShape shape)
        {
            if (!context.TryLayer(shape.LayerId, out var layer))
            {
                ImportLog.Add("圆环层无法识别：" + shape.LayerId);
                return;
            }

            AddArcCore(context, layer, shape.X, shape.Y, shape.Radius, shape.StrokeWidth, 0, 360);
        }

        private static void AddArc(FpContext context, EeFpShape shape)
        {
            if (!context.TryLayer(shape.LayerId, out var layer))
            {
                ImportLog.Add("圆弧层无法识别：" + shape.LayerId);
                return;
            }

            if (!SvgPath.TryGetCircularArc(shape.Path, out var arc))
            {
                AddPathLines(context, layer, shape);
                return;
            }

            var start = ConvertAngle(arc.Sweep ? arc.EndAngle : arc.StartAngle);
            var end = ConvertAngle(arc.Sweep ? arc.StartAngle : arc.EndAngle);
            AddArcCore(context, layer,
                EeUnits.ToMm(arc.CenterX), EeUnits.ToMm(arc.CenterY),
                EeUnits.ToMm((arc.RadiusX + arc.RadiusY) / 2.0),
                shape.StrokeWidth, start, end);
        }

        private static void AddPathLines(FpContext context, TV6_Layer layer, EeFpShape shape)
        {
            try
            {
                foreach (var sub in SvgPath.Parse(shape.Path, 0.02))
                {
                    for (var i = 1; i < sub.Points.Count; i++)
                    {
                        AddLine(context, layer,
                            context.X(EeUnits.ToMm(sub.Points[i - 1].X)), context.Y(EeUnits.ToMm(sub.Points[i - 1].Y)),
                            context.X(EeUnits.ToMm(sub.Points[i].X)), context.Y(EeUnits.ToMm(sub.Points[i].Y)),
                            shape.StrokeWidth);
                    }
                }
            }
            catch (Exception ex)
            {
                ImportLog.Add("圆弧路径解析失败：" + ex.Message);
            }
        }

        private static void AddRectangle(FpContext context, EeFpShape shape)
        {
            if (!context.TryLayer(shape.LayerId, out var layer))
            {
                ImportLog.Add("矩形层无法识别：" + shape.LayerId);
                return;
            }

            var x1 = context.X(shape.X);
            var y1 = context.Y(shape.Y);
            var x2 = context.X(shape.X + shape.Width);
            var y2 = context.Y(shape.Y + shape.Height);

            AddLine(context, layer, x1, y1, x2, y1, shape.StrokeWidth);
            AddLine(context, layer, x1, y1, x1, y2, shape.StrokeWidth);
            AddLine(context, layer, x1, y2, x2, y2, shape.StrokeWidth);
            AddLine(context, layer, x2, y1, x2, y2, shape.StrokeWidth);
        }

        private static void AddText(FpContext context, EeFpShape shape)
        {
            if (string.IsNullOrWhiteSpace(shape.Text)) return;
            if (!context.TryLayer(shape.LayerId, out var layer))
            {
                ImportLog.Add("文字层无法识别：" + shape.LayerId);
                return;
            }

            var text = Create<IPCB_Text>(TObjectId.eTextObject);
            if (text == null) return;

            text.SetState_Layer(layer);
            text.SetState_XLocation(context.CoordX(context.X(shape.X)));
            text.SetState_YLocation(context.CoordY(context.Y(shape.Y)));
            text.SetState_Text(shape.Text);
            text.SetState_Size(AdApi.MmToCoord(Math.Max(0.1, shape.FontSize)));
            text.SetState_Width(AdApi.MmToCoord(Math.Max(0.01, shape.StrokeWidth)));
            text.SetState_Rotation(shape.Rotation);
            text.SetState_Mirror(shape.Mirror);
            Attach(context, text);
        }

        private static IPCB_Pad CreatePad(
            FpContext context,
            double x,
            double y,
            double width,
            double height,
            double drill,
            double slotLength,
            string name,
            TShape shape,
            TV6_Layer layer,
            bool plated,
            double rotation,
            double slotAngle)
        {
            var pad = Create<IPCB_Pad>(TObjectId.ePadObject);
            if (pad == null) return null;

            pad.SetState_Mode(TPadMode.ePadMode_Simple);
            pad.SetState_Name(name ?? "");
            pad.SetState_Layer(layer);
            pad.SetState_Rotation(rotation);
            pad.SetState_Plated(plated);
            pad.SetState_TopShape(shape);
            pad.SetState_TopXSize(AdApi.MmToCoord(Math.Max(0.01, width)));
            pad.SetState_TopYSize(AdApi.MmToCoord(Math.Max(0.01, height)));
            pad.SetState_XLocation(context.CoordX(context.X(x)));
            pad.SetState_YLocation(context.CoordY(context.Y(y)));
            pad.SetState_HoleSize(AdApi.MmToCoord(Math.Max(0, drill)));

            if (slotLength > 0)
            {
                pad.SetState_HoleType(TExtendedHoleType.eSlotHole);
                pad.SetState_HoleWidth(AdApi.MmToCoord(slotLength));
                pad.SetState_HoleRotation(slotAngle);
            }
            else
            {
                pad.SetState_HoleType(TExtendedHoleType.eRoundHole);
            }

            var pad2 = pad as IPCB_Pad2;
            if (pad2 != null && shape == TShape.eRoundedRectangular)
                pad2.SetState_StackCRPctOnLayer(pad.GetState_V7Layer(), 25);

            return pad;
        }

        private static void AddLine(
            FpContext context, TV6_Layer layer,
            double x1, double y1, double x2, double y2, double width)
        {
            var track = Create<IPCB_Track>(TObjectId.eTrackObject);
            if (track == null) return;

            track.SetState_Layer(layer);
            track.SetState_Width(AdApi.MmToCoord(Math.Max(0.01, width)));
            track.SetState_X1(context.CoordX(x1));
            track.SetState_Y1(context.CoordY(y1));
            track.SetState_X2(context.CoordX(x2));
            track.SetState_Y2(context.CoordY(y2));
            Attach(context, track);
        }

        private static void AddArcCore(
            FpContext context, TV6_Layer layer,
            double centerX, double centerY, double radius,
            double width, double startAngle, double endAngle)
        {
            var arc = Create<IPCB_Arc>(TObjectId.eArcObject);
            if (arc == null) return;

            arc.SetState_Layer(layer);
            arc.SetState_CenterX(context.CoordX(context.X(centerX)));
            arc.SetState_CenterY(context.CoordY(context.Y(centerY)));
            arc.SetState_Radius(AdApi.MmToCoord(radius));
            arc.SetState_LineWidth(AdApi.MmToCoord(Math.Max(0.01, width)));
            arc.SetState_StartAngle(startAngle);
            arc.SetState_EndAngle(endAngle);
            Attach(context, arc);
        }

        private static void Attach(FpContext context, IPCB_Primitive primitive)
        {
            if (primitive == null) return;
            context.Component.GetState_Board().AddPCBObject(primitive);
            context.Component.AddPCBObject(primitive);
        }

        private static void ClearMaskExpansion(IPCB_Pad pad)
        {
            var cache = pad.GetState_Cache();
            cache.SolderMaskExpansionValid = TCacheState.eCacheManual;
            cache.UseSeparateExpansions = true;
            cache.SolderMaskExpansion = 0;
            cache.SolderMaskBottomExpansion = 0;
            pad.SetState_Cache(cache);
        }

        private static T Create<T>(TObjectId objectId) where T : class
        {
            return AdApi.PcbServer.Internal_PCBObjectFactory(
                (int)objectId,
                (int)TDimensionKind.eNoDimension,
                (int)TObjectCreationMode.eCreate_Default) as T;
        }

        private static TShape PadShape(EeFpShape shape)
        {
            switch ((shape.Shape ?? "").ToUpperInvariant())
            {
                case "RECT":
                case "RECTANGLE":
                    return TShape.eRectangular;
                case "OCT":
                case "OCTAGON":
                    return TShape.eOctagonal;
                case "ELLIPSE":
                case "OVAL":
                    return TShape.eRounded;
                case "POLYGON":
                    return TShape.eRoundedRectangular;
                default:
                    return TShape.eRounded;
            }
        }

        private static double SlotAngle(EeFpShape shape)
        {
            if (shape.HolePoints.Count >= 2)
            {
                var a = shape.HolePoints[0];
                var b = shape.HolePoints[1];
                var degrees = Math.Atan2(a.Y - b.Y, b.X - a.X) * 180.0 / Math.PI;
                return Normalize(degrees - shape.Rotation);
            }

            return shape.Height > shape.Width ? 90.0 : 0.0;
        }

        private static double ConvertAngle(double degrees) => Normalize(360.0 - degrees);

        private static double Normalize(double degrees)
        {
            var value = degrees % 360.0;
            if (value < 0) value += 360.0;
            return value;
        }

        private static string SafeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var cleaned = value.Trim().Select(c =>
                char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == '(' || c == ')' || c == '+'
                    ? c
                    : '_');
            return new string(cleaned.ToArray());
        }
    }
}
