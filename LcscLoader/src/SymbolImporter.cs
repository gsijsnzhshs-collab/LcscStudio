using System;
using System.Collections.Generic;
using System.Linq;
using DXP;
using EDP;
using SCH;

namespace LcscLoader
{
    internal static class SymbolImporter
    {
        public static ISch_Component Import(
            LcscPart part,
            ISch_Lib schLib,
            string pcbLibPath,
            string footprintName,
            bool overwrite)
        {
            var libReference = string.IsNullOrWhiteSpace(part.Symbol.Name) ? part.LcscId : part.Symbol.Name;
            var existing = schLib.GetState_SchComponentByLibRef(libReference);
            ISch_Component component = null;
            var created = false;
            if (existing != null)
            {
                if (!overwrite)
                {
                    ImportLog.Add("原理图符号已存在，跳过：" + libReference);
                    return existing;
                }

                // 就地覆盖：清空子对象后复用原组件，名称保持不变，避免累积重复符号。
                component = existing;
                RemoveDuplicateSymbols(schLib, libReference, existing);
                try
                {
                    ((ISch_BasicContainer)component).DeleteAll();
                    Diag.Log("符号覆盖：复用已有组件 " + libReference + "（已清空子对象）");
                }
                catch (Exception ex)
                {
                    Diag.Log("符号清空失败，改为删除重建：" + ex.Message);
                    schLib.RemoveSchComponent(existing);
                    component = null;
                }
            }

            if (component == null)
            {
                component = AdApi.SchServer.SchObjectFactory(
                    TObjectId.eSchComponent, TObjectCreationMode.eCreate_Default) as ISch_Component;
                created = true;
                Diag.Log("符号：新建组件 " + libReference);
            }
            if (component == null) throw new InvalidOperationException("无法创建原理图元件");

            component.SetState_CurrentPartID(1);
            component.SetState_DisplayMode(0);
            component.SetState_LibReference(libReference);
            var designator = component.GetState_SchDesignator();
            if (designator != null)
            {
                designator.SetState_Text(
                    string.IsNullOrWhiteSpace(part.Symbol.Prefix) ? "U?" : part.Symbol.Prefix);
            }
            var smallFont = AdApi.SchServer.GetState_FontManager()
                .GetFontID(6, 0, false, false, false, false, "Tahoma");
            var designatorLabel = designator as ISch_Label;
            if (designatorLabel != null) designatorLabel.SetState_FontId(smallFont);
            try
            {
                var comment = component.GetState_SchComment() as ISch_Label;
                if (comment != null)
                {
                    var container = comment as ISch_BasicContainer;
                    if (container != null) container.SetState_Text(part.Symbol.Name);
                    comment.SetState_FontId(smallFont);
                }
            }
            catch
            {
                // Comment styling is cosmetic.
            }
            component.SetState_ComponentDescription(part.Description);

            var origin = part.Symbol;
            Diag.Log("符号 " + libReference + " 原点 units=(" + Diag.Mm(origin.OriginX) + "," +
                     Diag.Mm(origin.OriginY) + ") 图元=" + part.Symbol.Shapes.Count +
                     " 引脚=" + part.Symbol.Pins.Count);
            Func<double, double> x = value => (value - origin.OriginX) * EeUnits.MmPerUnit;
            Func<double, double> y = value => (origin.OriginY - value) * EeUnits.MmPerUnit;

            foreach (var shape in part.Symbol.Shapes)
                AddShape(component, shape, x, y);

            AddPins(component, part.Symbol.Pins, x, y);
            LogObjectSummary(component);
            PositionFields(component, part.Symbol, x, y);

            foreach (var parameter in part.Parameters)
                AddParameter(component, parameter.Key, parameter.Value);

            if (!string.IsNullOrWhiteSpace(footprintName))
                AssignFootprint(component, pcbLibPath, footprintName);

            if (created) schLib.AddSchComponent(component);
            schLib.SetState_Current_SchComponent(component);
            schLib.GraphicallyInvalidate();

            ImportLog.Add(string.Format("原理图符号：{0}（{1} 个图形，{2} 个引脚）",
                libReference, part.Symbol.Shapes.Count, part.Symbol.Pins.Count));
            return component;
        }

        public static void PlaceOnCurrentSheet(string libReference, string schLibPath)
        {
            var sheet = AdApi.SchServer.GetCurrentSchDocument();
            if (sheet == null)
                throw new InvalidOperationException("请先打开一张原理图，再放置元件");
            if (sheet.GetState_ObjectId() == TObjectId.eSchLib)
                throw new InvalidOperationException("当前是原理图库，不能放置元件");

            var placed = AdApi.SchServer.LoadComponentFromLibrary(libReference, schLibPath) as ISch_Component;
            if (placed == null)
                throw new InvalidOperationException("无法从库中加载元件：" + libReference);

            sheet.AddSchObject(placed);
            placed.MoveToXY(0, 0);
            placed.SetState_Orientation(TRotationBy90.eRotate0);
            sheet.GraphicallyInvalidate();
            ImportLog.Add("已放置到当前原理图：" + libReference);
        }

        private static void AddShape(
            ISch_Component component,
            EeSymShape shape,
            Func<double, double> x,
            Func<double, double> y)
        {
            try
            {
                switch (shape.Kind)
                {
                    case EeSymKind.Rectangle:
                        AddRectangle(component, shape, x, y);
                        break;
                    case EeSymKind.Circle:
                    case EeSymKind.Ellipse:
                        AddEllipse(component, shape, x, y);
                        break;
                    case EeSymKind.Arc:
                        AddArc(component, shape, x, y);
                        break;
                    case EeSymKind.Polyline:
                    case EeSymKind.Polygon:
                        AddPoly(component, shape, x, y);
                        break;
                    case EeSymKind.Path:
                        AddPath(component, shape, x, y);
                        break;
                    case EeSymKind.Text:
                        AddLabel(component, shape, x, y);
                        break;
                }
            }
            catch (Exception ex)
            {
                ImportLog.Add("图形转换失败（" + shape.Kind + "）：" + ex.Message);
            }
        }

        private static void AddRectangle(
            ISch_Component component, EeSymShape shape, Func<double, double> x, Func<double, double> y)
        {
            var rectangle = AdApi.SchServer.SchObjectFactory(
                TObjectId.eRectangle, TObjectCreationMode.eCreate_Default) as ISch_Rectangle;
            if (rectangle == null) return;

            var x1 = x(shape.X);
            var y1 = y(shape.Y);
            var x2 = x(shape.X + shape.Width);
            var y2 = y(shape.Y + shape.Height);

            rectangle.SetState_Location(Point(Math.Min(x1, x2), Math.Min(y1, y2)));
            rectangle.SetState_Corner(Point(Math.Max(x1, x2), Math.Max(y1, y2)));
            rectangle.SetState_LineWidth(SchematicLineWidth(shape.StrokeWidth));
            rectangle.SetState_Color(Conv.Color(shape.Stroke, Conv.DefaultStroke));
            rectangle.SetState_AreaColor(Conv.Color(shape.Fill, Conv.DefaultFill));
            rectangle.SetState_IsSolid(Conv.HasPaint(shape.Fill));
            rectangle.SetState_Transparent(false);
            SetOwner(rectangle);
            component.AddSchObject(rectangle);

            var actualRect = rectangle.Internal_GetState_Location();
            Diag.Log("矩形 期望(mm)=(" + Diag.Mm(x1) + "," + Diag.Mm(y1) + ")-(" + Diag.Mm(x2) + "," + Diag.Mm(y2) +
                     ") | 实际 loc(mm)=" + (actualRect == null
                         ? "null"
                         : Diag.Mm(actualRect.GetX()) + "," + Diag.Mm(actualRect.GetY())));
        }

        private static void AddEllipse(
            ISch_Component component, EeSymShape shape, Func<double, double> x, Func<double, double> y)
        {
            var ellipse = AdApi.SchServer.SchObjectFactory(
                TObjectId.eEllipse, TObjectCreationMode.eCreate_Default) as ISch_Ellipse;
            if (ellipse == null) return;

            ellipse.SetState_Location(Point(x(shape.X), y(shape.Y)));
            ellipse.SetState_Radius(AdApi.MmToCoord(Math.Abs(shape.RadiusX) * EeUnits.MmPerUnit));
            ellipse.SetState_SecondaryRadius(AdApi.MmToCoord(Math.Abs(shape.RadiusY) * EeUnits.MmPerUnit));
            ellipse.SetState_LineWidth(SchematicLineWidth(shape.StrokeWidth));
            ellipse.SetState_Color(Conv.Color(shape.Stroke, Conv.DefaultStroke));
            ellipse.SetState_AreaColor(Conv.Color(shape.Fill, Conv.DefaultFill));
            ellipse.SetState_IsSolid(Conv.HasPaint(shape.Fill));
            ellipse.SetState_Transparent(false);
            SetOwner(ellipse);
            component.AddSchObject(ellipse);
        }

        private static void AddArc(
            ISch_Component component, EeSymShape shape, Func<double, double> x, Func<double, double> y)
        {
            if (!SvgPath.TryGetCircularArc(shape.Path, out var arc))
            {
                AddPath(component, shape, x, y);
                return;
            }

            if (Math.Abs(arc.SweepAngle) >= 359.5)
            {
                var circle = new EeSymShape
                {
                    Kind = EeSymKind.Circle,
                    X = arc.CenterX, Y = arc.CenterY,
                    RadiusX = arc.RadiusX, RadiusY = arc.RadiusY,
                    Stroke = shape.Stroke, StrokeWidth = shape.StrokeWidth, Fill = "none",
                };
                AddEllipse(component, circle, x, y);
                return;
            }

            var altiumArc = AdApi.SchServer.SchObjectFactory(
                TObjectId.eArc, TObjectCreationMode.eCreate_Default) as ISch_Arc;
            if (altiumArc == null) return;

            var start = Normalize(-arc.StartAngle);
            var end = Normalize(-(arc.StartAngle + arc.SweepAngle));
            if (arc.SweepAngle > 0)
            {
                var swap = start;
                start = end;
                end = swap;
            }

            altiumArc.SetState_Location(Point(x(arc.CenterX), y(arc.CenterY)));
            altiumArc.SetState_Radius(AdApi.MmToCoord(
                (arc.RadiusX + arc.RadiusY) / 2.0 * EeUnits.MmPerUnit));
            altiumArc.SetState_StartAngle(start);
            altiumArc.SetState_EndAngle(end);
            altiumArc.SetState_LineWidth(SchematicLineWidth(shape.StrokeWidth));
            altiumArc.SetState_Color(Conv.Color(shape.Stroke, Conv.DefaultStroke));
            SetOwner(altiumArc);
            component.AddSchObject(altiumArc);
        }

        private static void AddPoly(
            ISch_Component component, EeSymShape shape, Func<double, double> x, Func<double, double> y)
        {
            var points = Conv.ParseUnitPoints(shape.Points)
                .Select(p => (X: x(p.X), Y: y(p.Y)))
                .ToList();
            AddPolyObject(component, shape.Kind == EeSymKind.Polygon, points,
                shape.Stroke, shape.StrokeWidth, shape.Fill);
        }

        private static void AddPath(
            ISch_Component component, EeSymShape shape, Func<double, double> x, Func<double, double> y)
        {
            List<SvgSubPath> subPaths;
            try { subPaths = SvgPath.Parse(shape.Path, 0.02); }
            catch (Exception ex)
            {
                ImportLog.Add("路径解析失败：" + ex.Message);
                return;
            }

            var filled = Conv.HasPaint(shape.Fill);
            foreach (var sub in subPaths)
            {
                var points = sub.Points.Select(p => (X: x(p.X), Y: y(p.Y))).ToList();
                var closed = sub.Closed || filled;
                AddPolyObject(component, closed, points, shape.Stroke, shape.StrokeWidth,
                    closed && filled ? shape.Fill : "none");
            }
        }

        private static void AddPolyObject(
            ISch_Component component,
            bool closed,
            List<(double X, double Y)> points,
            string stroke,
            double strokeWidth,
            string fill)
        {
            if (points.Count < 2) return;
            if (closed && points.Count < 3) closed = false;

            var objectId = closed ? TObjectId.ePolygon : TObjectId.ePolyline;
            var container = AdApi.SchServer.SchObjectFactory(
                objectId, TObjectCreationMode.eCreate_Default) as ISch_Polygon;
            if (container == null) return;

            // 先挂载再写几何：诊断显示未挂载时 SetState_Vertex 不会保留坐标。
            SetOwner(container);
            component.AddSchObject(container);

            // Altium 的顶点索引是 1 基（Delphi 脚本里的 Vertex[1] 起），0 基写入会落空。
            container.SetState_VerticesCount(points.Count);
            for (var i = 0; i < points.Count; i++)
                container.SetState_Vertex(i + 1, Point(points[i].X, points[i].Y));
            ApplyPolyStyle(container, closed, stroke, strokeWidth, fill);

            Diag.Log((closed ? "多边形" : "折线") + " 对象号=" + container.Internal_GetState_ObjectId() +
                     " 顶点=" + container.GetState_VerticesCount() +
                     " 第1位=" + VertexText(container, 1) + " 第0位=" + VertexText(container, 0));

            if (IsSentinel(container, 1))
            {
                // 兜底：若实际是 0 基，就按 0 基重写一次。
                for (var i = 0; i < points.Count; i++)
                    container.SetState_Vertex(i, Point(points[i].X, points[i].Y));
                Diag.Log("改按 0 基写入后 第0位=" + VertexText(container, 0) +
                         " 第1位=" + VertexText(container, 1));
            }
        }

        private static void ApplyPolyStyle(
            ISch_Polygon container, bool closed, string stroke, double strokeWidth, string fill)
        {
            container.SetState_LineWidth(SchematicLineWidth(strokeWidth));
            container.SetState_Color(Conv.Color(stroke, Conv.DefaultStroke));
            container.SetState_IsSolid(closed && Conv.HasPaint(fill));
            container.SetState_AreaColor(Conv.Color(fill, Conv.DefaultFill));
            container.SetState_Transparent(false);
        }

        // 清理历史遗留：同名符号只保留准备复用的那一个，其余删除。
        private static void RemoveDuplicateSymbols(
            ISch_Lib schLib, string libReference, ISch_Component keep)
        {
            try
            {
                object iteratorObject = schLib.Internal_SchLibIterator_Create();
                var iterator = iteratorObject as ISch_Iterator;
                if (iterator == null) return;

                var doomed = new List<ISch_Component>();
                object current = iterator.Internal_FirstSchObject();
                while (current != null)
                {
                    var comp = current as ISch_Component;
                    if (comp != null && comp != keep &&
                        string.Equals(comp.GetState_LibReference(), libReference, StringComparison.Ordinal))
                    {
                        doomed.Add(comp);
                    }
                    current = iterator.Internal_NextSchObject();
                }

                foreach (var comp in doomed)
                {
                    schLib.RemoveSchComponent(comp);
                }
                if (doomed.Count > 0) Diag.Log("清理重复符号 " + doomed.Count + " 个：" + libReference);
            }
            catch (Exception ex)
            {
                Diag.Log("清理重复符号失败：" + ex.Message);
            }
        }

        // 诊断用：确认图元是否真的挂进了元件（17=多边形、18=折线、37=引脚）。
        private static void LogObjectSummary(ISch_Component component)
        {
            try
            {
                object iteratorObject = component.Internal_SchIterator_Create();
                var iterator = iteratorObject as ISch_Iterator;
                if (iterator == null)
                {
                    Diag.Log("元件对象枚举：迭代器不可用");
                    return;
                }
                var ids = new List<int>();
                object current = iterator.Internal_FirstSchObject();
                while (current != null)
                {
                    var basic = current as ISch_BasicContainer;
                    ids.Add(basic == null ? -1 : basic.Internal_GetState_ObjectId());
                    current = iterator.Internal_NextSchObject();
                }
                component.SchIterator_Destroy(ref iteratorObject);
                Diag.Log("元件内对象数=" + ids.Count + " 对象号=" + string.Join(",", ids));
            }
            catch (Exception ex)
            {
                Diag.Log("元件对象枚举失败：" + ex.Message);
            }
        }

        private static bool IsSentinel(ISch_Polygon container, int index)
        {
            var vertex = container.Internal_GetState_Vertex(index);
            return vertex == null || vertex.GetX() == int.MaxValue || vertex.GetY() == int.MaxValue;
        }

        private static string VertexText(ISch_Polygon container, int index)
        {
            var vertex = container.Internal_GetState_Vertex(index);
            return vertex == null ? "null" : vertex.GetX() + "," + vertex.GetY();
        }

        private static void AddLabel(
            ISch_Component component, EeSymShape shape, Func<double, double> x, Func<double, double> y)
        {
            if (string.IsNullOrWhiteSpace(shape.Text)) return;

            var label = AdApi.SchServer.SchObjectFactory(
                TObjectId.eLabel, TObjectCreationMode.eCreate_Default) as ISch_Label;
            if (label == null) return;

            label.SetState_Location(Point(x(shape.X), y(shape.Y)));
            ((ISch_BasicContainer)label).SetState_Text(shape.Text);
            label.SetState_Color(Conv.Color(shape.Stroke, 0));
            label.SetState_Orientation(RotationToOrientation(shape.Rotation));
            label.SetState_Justification(AnchorToJustification(shape.Anchor));

            var size = (int)Math.Round(Math.Max(4, shape.FontSize));
            var fontName = string.IsNullOrWhiteSpace(shape.Font) ? "Tahoma" : shape.Font;
            label.SetState_FontId(
                AdApi.SchServer.GetState_FontManager().GetFontID(size, 0, false, false, false, false, fontName));

            SetOwner(label);
            component.AddSchObject(label);
        }

        private static void AddPins(
            ISch_Component component,
            IEnumerable<EePin> pins,
            Func<double, double> x,
            Func<double, double> y)
        {
            var fontManager = AdApi.SchServer.GetState_FontManager();
            var fontId = fontManager.GetFontID(5, 0, false, false, false, false, "Tahoma");

            foreach (var source in pins)
            {
                try
                {
                    var pin = AdApi.SchServer.SchObjectFactory(
                        TObjectId.ePin, TObjectCreationMode.eCreate_Default) as ISch_Pin;
                    if (pin == null) continue;

                    // AD 的引脚线从 Location 沿 orientation 向外画一个 length；EasyEDA 的
                    // 坐标是这条线的外端。把落点向内平移一个长度，线才会正好接上器件体
                    // （否则引脚与器件体之间会空出一整个引脚长）。
                    var rotation = ((source.Rotation % 360) + 360) % 360;
                    var length = Math.Max(0.1, source.Length) * EeUnits.MmPerUnit;
                    var locationX = x(source.X);
                    var locationY = y(source.Y);
                    switch (rotation)
                    {
                        case 0: locationX -= length; break;
                        case 90: locationY -= length; break;
                        case 180: locationX += length; break;
                        case 270: locationY += length; break;
                    }

                    // EasyEDA rotation 表示引脚「朝外」的方向，转到 AD 坐标系后仍是
                    // 0=右、90=上、180=左、270=下，与 AD 的朝向枚举直映射。
                    TRotationBy90 orientation;
                    switch (rotation)
                    {
                        case 90: orientation = TRotationBy90.eRotate90; break;
                        case 180: orientation = TRotationBy90.eRotate180; break;
                        case 270: orientation = TRotationBy90.eRotate270; break;
                        default: orientation = TRotationBy90.eRotate0; break;
                    }

                    pin.SetState_Location(Point(locationX, locationY));
                    pin.SetState_PinLength(AdApi.MmToCoord(length));
                    pin.SetState_Orientation(orientation);
                    pin.SetState_Designator(source.Number);
                    pin.SetState_Name(source.Name);
                    pin.SetState_Electrical(ElectricalType(source.ElectricalType));
                    pin.SetState_ShowName(source.ShowName);
                    pin.SetState_ShowDesignator(source.ShowNumber);
                    pin.SetState_Color(0);

                    pin.SetState_Name_FontMode(TPinItemMode.ePinItemMode_Custom);
                    pin.SetState_Name_CustomFontID(fontId);
                    pin.SetState_Designator_FontMode(TPinItemMode.ePinItemMode_Custom);
                    pin.SetState_Designator_CustomFontID(fontId);

                    SetOwner(pin);
                    component.AddSchObject(pin);

                    var actual = pin.Internal_GetState_Location();
                    Diag.Log("引脚 " + source.Number +
                             " 坐标(mm)=(" + Diag.Mm(x(source.X)) + "," + Diag.Mm(y(source.Y)) +
                             ") 落点(mm)=(" + Diag.Mm(locationX) + "," + Diag.Mm(locationY) +
                             ") 长度(mm)=" + Diag.Mm(length) +
                             " 方向=" + source.Rotation +
                             " | 实际 loc(mm)=" + (actual == null ? "null" :
                                 Diag.Mm(actual.GetX()) + "," + Diag.Mm(actual.GetY())) +
                             " 长度(mm)=" + Diag.Mm(pin.GetState_PinLength()) +
                             " 方向=" + pin.Internal_GetState_Orientation());
                }
                catch (Exception ex)
                {
                    ImportLog.Add("引脚 " + source.Number + " 创建失败：" + ex.Message);
                }
            }
        }

        private static void PositionFields(
            ISch_Component component,
            EeSymbol symbol,
            Func<double, double> x,
            Func<double, double> y)
        {
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            Action<double, double> include = (px, py) =>
            {
                minX = Math.Min(minX, px);
                maxX = Math.Max(maxX, px);
                minY = Math.Min(minY, py);
                maxY = Math.Max(maxY, py);
            };

            foreach (var shape in symbol.Shapes)
            {
                switch (shape.Kind)
                {
                    case EeSymKind.Rectangle:
                        include(x(shape.X), y(shape.Y));
                        include(x(shape.X + shape.Width), y(shape.Y + shape.Height));
                        break;
                    case EeSymKind.Circle:
                    case EeSymKind.Ellipse:
                        include(x(shape.X - shape.RadiusX), y(shape.Y - shape.RadiusY));
                        include(x(shape.X + shape.RadiusX), y(shape.Y + shape.RadiusY));
                        break;
                    case EeSymKind.Polyline:
                    case EeSymKind.Polygon:
                        foreach (var point in Conv.ParseUnitPoints(shape.Points))
                            include(x(point.X), y(point.Y));
                        break;
                    case EeSymKind.Path:
                        try
                        {
                            foreach (var sub in SvgPath.Parse(shape.Path, 0.05))
                                foreach (var point in sub.Points)
                                    include(x(point.X), y(point.Y));
                        }
                        catch
                        {
                            include(x(shape.X), y(shape.Y));
                        }
                        break;
                    default:
                        include(x(shape.X), y(shape.Y));
                        break;
                }
            }

            foreach (var pin in symbol.Pins)
            {
                include(x(pin.X), y(pin.Y));
                var rotation = ((pin.Rotation % 360) + 360) % 360;
                var dx = rotation == 0 ? pin.Length : rotation == 180 ? -pin.Length : 0;
                var dy = rotation == 90 ? -pin.Length : rotation == 270 ? pin.Length : 0;
                include(x(pin.X + dx), y(pin.Y + dy));
            }

            if (minX > maxX || minY > maxY) return;

            var centerX = (minX + maxX) / 2.0;
            var top = maxY + 0.8;
            var bottom = minY - 0.8;

            try
            {
                var designator = component.GetState_SchDesignator() as ISch_Label;
                if (designator != null)
                {
                    var complex = designator as ISch_ComplexText;
                    if (complex != null) complex.SetState_Autoposition(false);
                    designator.SetState_Location(Point(centerX, top));
                    designator.SetState_Justification(1);
                }
                var comment = component.GetState_SchComment() as ISch_Label;
                if (comment != null)
                {
                    var complex = comment as ISch_ComplexText;
                    if (complex != null) complex.SetState_Autoposition(false);
                    comment.SetState_Location(Point(centerX, bottom));
                    comment.SetState_Justification(1);
                }
            }
            catch (Exception ex)
            {
                ImportLog.Add("字段定位失败：" + ex.Message);
            }
        }

        private static void AddParameter(ISch_Component component, string name, string value)
        {
            var parameter = component.AddSchParameter();
            parameter.SetState_Name(name);
            parameter.SetState_Text(value);
            parameter.SetState_ShowName(false);
            parameter.SetState_IsHidden(true);
        }

        private static void AssignFootprint(ISch_Component component, string libraryPath, string modelName)
        {
            var implementation = component.AddSchImplementation();
            implementation.SetState_MapAsString("");
            implementation.SetState_ModelName(modelName);
            implementation.SetState_ModelType("PCBLIB");
            implementation.AddDataFileLink(modelName, libraryPath, "PCBLIB");
            implementation.SetState_IsCurrent(true);
        }

        private static TPinElectrical ElectricalType(int easyEdaType)
        {
            switch (easyEdaType)
            {
                case 1: return TPinElectrical.eElectricInput;
                case 2: return TPinElectrical.eElectricOutput;
                case 3: return TPinElectrical.eElectricIO;
                case 4: return TPinElectrical.eElectricPower;
                default: return TPinElectrical.eElectricPassive;
            }
        }

        private static int RotationToOrientation(double degrees)
        {
            var normalized = ((int)Math.Round(degrees / 90.0) * 90) % 360;
            if (normalized < 0) normalized += 360;
            switch (normalized)
            {
                case 90: return (int)TRotationBy90.eRotate90;
                case 180: return (int)TRotationBy90.eRotate180;
                case 270: return (int)TRotationBy90.eRotate270;
                default: return (int)TRotationBy90.eRotate0;
            }
        }

        private static int AnchorToJustification(string anchor)
        {
            if (string.Equals(anchor, "middle", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(anchor, "end", StringComparison.OrdinalIgnoreCase)) return 2;
            return 0;
        }

        private static int SchematicLineWidth(double eeUnits)
        {
            if (eeUnits >= 2.5) return (int)SCH.TSize.eLarge;
            if (eeUnits >= 1.5) return (int)SCH.TSize.eMedium;
            return (int)SCH.TSize.eSmall;
        }

        private static DXP.Point Point(double mmX, double mmY)
        {
            return new DXP.Point { X = AdApi.MmToCoord(mmX), Y = AdApi.MmToCoord(mmY) };
        }

        private static void SetOwner(ISch_GraphicalObject graphicalObject)
        {
            graphicalObject.SetState_OwnerPartId(1);
            graphicalObject.SetState_OwnerPartDisplayMode(0);
        }

        private static double Normalize(double degrees)
        {
            var value = degrees % 360.0;
            if (value < 0) value += 360.0;
            return value;
        }
    }
}

