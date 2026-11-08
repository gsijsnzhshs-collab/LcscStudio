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
            // 新建组件会继承上一个组件的落点（实测为 1270 mm），不归零会让整个封装
            // 离库原点极远；这里固定到原点，图元坐标随之以局部 mm 写入。
            component.SetState_XLocation(0);
            component.SetState_YLocation(0);

            Diag.Log("封装 " + pattern + " 原点(coord)=(" + component.GetState_XLocation() + "," +
                     component.GetState_YLocation() + ") BBox(mm)=(" + Diag.Mm(part.Footprint.BoxX) + "," +
                     Diag.Mm(part.Footprint.BoxY) + ") " + Diag.Mm(part.Footprint.BoxWidth) + "x" +
                     Diag.Mm(part.Footprint.BoxHeight) + " 图元=" + part.Footprint.Shapes.Count);

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
            Diag.Log("组件图元数=" + PrimitiveCount(component) + " 封装名=" + pattern +
                     " 当前组件=" + (pcbLib.GetState_CurrentComponent() == null
                         ? "null"
                         : pcbLib.GetState_CurrentComponent().GetState_Pattern()));

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

            if (string.Equals(shape.Shape, "POLYGON", StringComparison.OrdinalIgnoreCase) &&
                shape.Points.Count >= 3)
            {
                AddPolygonPad(context, shape, layer);
                return;
            }

            var padShape = PadShape(shape);

            var pad = CreatePad(context, shape.X, shape.Y, shape.Width, shape.Height,
                shape.HoleRadius * 2.0, shape.HoleLength, shape.Number, padShape, layer,
                shape.Plated, shape.Rotation, SlotAngle(shape));
            Attach(context, pad);
        }

        // AD 的焊盘没有多边形形状（TShape 最大只有圆角矩形）。用真实轮廓画一块铜皮区域，
        // 再放一个小焊盘承担引脚编号与网络连接，外观与立创一致。
        private static void AddPolygonPad(FpContext context, EeFpShape shape, TV6_Layer layer)
        {
            var region = Create<IPCB_Region>(TObjectId.eRegionObject);
            if (region != null)
            {
                region.SetState_Layer(layer);

                // 必须经 GeometricPolygon 回填：Internal_GetMainContour 取到的是副本，
                // 直接改它不会写回区域（实测面积仍为 0、轮廓未生效）。
                var polygonObject = region.Internal_GetGeometricPolygon();
                var polygon = polygonObject as IPCB_GeometricPolygon;
                var contour = polygon == null ? null : polygon.Internal_AddEmptyContour() as IPCB_Contour;
                if (contour != null)
                {
                    foreach (var point in shape.Points)
                    {
                        // shape.Points 已由解析器换算成 mm，不能再乘 EeUnits.MmPerUnit。
                        contour.AddPoint(context.CoordX(context.X(point.X)),
                                         context.CoordY(context.Y(point.Y)));
                    }
                    region.SetGeometricPolygon(polygonObject);
                }
                Attach(context, region);

                var readback = region.Internal_GetMainContour() as IPCB_Contour;
                var readCount = readback == null ? -1 : readback.GetState_Count();
                // 轮廓点同样是 1 基索引，读第 0 位只会拿到哨兵值。
                var firstPoint = readback != null && readCount > 0
                    ? readback.GetState_PointX(1) + "," + readback.GetState_PointY(1)
                    : "n/a";
                var areaCoord = region.GetState_Area();
                Diag.Log("区域读回 轮廓点=" + readCount + "/" + shape.Points.Count +
                         " 首点(coord)=" + firstPoint +
                         " 面积(mm²)=" + Diag.Mm2(areaCoord) +
                         " 期望(mm²)=" + Diag.Mm(PolygonAreaMm2(shape)));
            }

            const double anchor = 0.25;
            var pad = CreatePad(context, shape.X, shape.Y, anchor, anchor, 0, 0,
                shape.Number, TShape.eRounded, layer, false, shape.Rotation, 0);
            Attach(context, pad);

            Diag.Log("多边形焊盘 '" + shape.Number + "' 轮廓点=" + shape.Points.Count +
                     " bbox(mm)=" + Diag.Mm(shape.Width) + "x" + Diag.Mm(shape.Height) +
                     " layer=" + (int)layer + " 锚点直径=" + anchor + "mm");
        }

        // 用于核对区域轮廓：按轮廓点算期望面积（mm²）。点已是 mm，不能再换算。
        private static double PolygonAreaMm2(EeFpShape shape)
        {
            double sum = 0;
            for (var i = 0; i < shape.Points.Count; i++)
            {
                var a = shape.Points[i];
                var b = shape.Points[(i + 1) % shape.Points.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return Math.Abs(sum) / 2.0;
        }

        // 读回组件自身的图元数量，确认图元是否真的挂进封装（而非只挂到板上）。
        private static string PrimitiveCount(IPCB_LibComponent component)
        {
            try
            {
                return component.GetPrimitiveCount(null).ToString();
            }
            catch (Exception ex)
            {
                return "err:" + ex.Message;
            }
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

            Diag.Log("焊盘 '" + (name ?? "") + "' 期望(mm) " + Diag.Mm(width) + "x" + Diag.Mm(height) +
                     " @(" + Diag.Mm(x) + "," + Diag.Mm(y) + ") layer=" + (int)layer +
                     " rot=" + rotation + " | 实际 " + Diag.Mm(pad.GetState_TopXSize()) + "x" +
                     Diag.Mm(pad.GetState_TopYSize()) + " @(" + Diag.Mm(pad.GetState_XLocation()) + "," +
                     Diag.Mm(pad.GetState_YLocation()) + ") layer=" + pad.Internal_GetState_Layer() +
                     " rot=" + pad.GetState_Rotation());
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

            Diag.Log("线 layer=" + (int)layer + " 期望(mm) (" + Diag.Mm(x1) + "," + Diag.Mm(y1) + ")-(" +
                     Diag.Mm(x2) + "," + Diag.Mm(y2) + ") 宽=" + Diag.Mm(width) + " | 实际 (" +
                     Diag.Mm(track.GetState_X1()) + "," + Diag.Mm(track.GetState_Y1()) + ")-(" +
                     Diag.Mm(track.GetState_X2()) + "," + Diag.Mm(track.GetState_Y2()) + ") 宽=" +
                     Diag.Mm(track.GetState_Width()));
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

            Diag.Log("圆弧 layer=" + (int)layer + " 期望 c=(" + Diag.Mm(centerX) + "," + Diag.Mm(centerY) +
                     ") r=" + Diag.Mm(radius) + " 角=" + startAngle + ".." + endAngle + " | 实际 c=(" +
                     Diag.Mm(arc.GetState_CenterX()) + "," + Diag.Mm(arc.GetState_CenterY()) + ") r=" +
                     Diag.Mm(arc.GetState_Radius()) + " 角=" + arc.GetState_StartAngle() + ".." +
                     arc.GetState_EndAngle());
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
