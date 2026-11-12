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
            // 原理图库的增删必须包在 SchServer.ProcessControl 事务里，保存时才会写进文档；
            // 不包事务时 RemoveSchObject 只作用于内存，保存后旧引脚仍在（重复引脚）。
            var control = AdApi.ProcessControl;
            if (control == null)
                throw new InvalidOperationException("无法获取 SchServer.ProcessControl");

            try
            {
                control.PreProcess(schLib, "");
            }
            catch (Exception ex)
            {
                PluginLog.Write("sch preprocess failed: " + ex.Message);
                throw new InvalidOperationException("无法启动原理图库修改事务", ex);
            }

            try
            {
                return ImportCore(part, schLib, pcbLibPath, footprintName, overwrite);
            }
            finally
            {
                try
                {
                    control.PostProcess(schLib, "");
                }
                catch (Exception ex)
                {
                    PluginLog.Write("sch postprocess failed: " + ex.Message);
                    throw new InvalidOperationException("无法完成原理图库修改事务", ex);
                }
            }
        }

        private static ISch_Component ImportCore(
            LcscPart part,
            ISch_Lib schLib,
            string pcbLibPath,
            string footprintName,
            bool overwrite)
        {
            var libReference = string.IsNullOrWhiteSpace(part.Symbol.Name) ? part.LcscId : part.Symbol.Name;
            var existing = schLib.GetState_SchComponentByLibRef(libReference);
            ISch_Component component = null;
            if (existing != null)
            {
                if (!overwrite)
                {
                    ImportLog.Add("原理图符号已存在，跳过：" + libReference);
                    return existing;
                }

                // 容器内 RemoveSchObject 不会可靠移除库部件持有的旧引脚；
                // 在 SCH 事务中删除整个旧元件，再从工厂创建干净实例。
                PluginLog.Write("replace sch component: " + libReference);
                schLib.RemoveSchComponent(existing);
                component = null;
            }

            if (component == null)
            {
                component = AdApi.SchServer.SchObjectFactory(
                    TObjectId.eSchComponent, TObjectCreationMode.eCreate_Default) as ISch_Component;
            }
            if (component == null) throw new InvalidOperationException("无法创建原理图元件");

            component.SetState_CurrentPartID(1);
            component.SetState_DisplayMode(0);
            component.SetState_LibReference(libReference);

            // 多部件器件（如 MCU 的独立电源符号）要建出对应的部件数，
            // 每个图形/引脚再按 OwnerPartId 挂到各自部件上。
            // 覆盖导入时部件数可能残留（上次多部件、本次单部件），统一按本次实际数量重置，
            // 否则会留下一个空部件。
            var parts = part.SymbolParts.Count > 0
                ? part.SymbolParts
                : new List<EeSymbol> { part.Symbol };
            if (component.GetState_PartCountNoPart0() != parts.Count)
                component.SetState_PartCountNoPart0(parts.Count);

            // 位号与型号注释同用 AD 默认的 10pt，取 6pt 会比正常符号偏小。
            var fieldFont = AdApi.SchServer.GetState_FontManager()
                .GetFontID(10, 0, false, false, false, false, "Tahoma");
            var designator = component.GetState_SchDesignator();
            if (designator != null)
            {
                designator.SetState_Text(
                    string.IsNullOrWhiteSpace(part.Symbol.Prefix) ? "U?" : part.Symbol.Prefix);
                var designatorLabel = designator as ISch_Label;
                if (designatorLabel != null) designatorLabel.SetState_FontId(fieldFont);
            }
            try
            {
                var comment = component.GetState_SchComment() as ISch_Label;
                if (comment != null)
                {
                    var container = comment as ISch_BasicContainer;
                    if (container != null) container.SetState_Text(part.Symbol.Name);
                    comment.SetState_FontId(fieldFont);
                }
            }
            catch
            {
                // Comment styling is cosmetic.
            }
            component.SetState_ComponentDescription(part.Description);

            for (var index = 0; index < parts.Count; index++)
            {
                var symbol = parts[index];
                var partId = index + 1;
                Func<double, double> x = value => (value - symbol.OriginX) * EeUnits.MmPerUnit;
                Func<double, double> y = value => (symbol.OriginY - value) * EeUnits.MmPerUnit;

                foreach (var shape in symbol.Shapes)
                    AddShape(component, shape, x, y, partId);

                AddPins(component, symbol.Pins, x, y, partId);
                if (partId == 1) PositionFields(component, symbol, x, y);
            }

            foreach (var parameter in part.Parameters)
                AddParameter(component, parameter.Key, parameter.Value);

            if (!string.IsNullOrWhiteSpace(footprintName))
                AssignFootprint(component, pcbLibPath, footprintName);

            // 无论新建还是覆盖，走到这里的 component 都是刚创建的实例；
            // 覆盖时旧元件已删除，新实例同样必须重新加入库，否则保存后整个符号消失。
            schLib.AddSchComponent(component);
            schLib.SetState_Current_SchComponent(component);
            schLib.GraphicallyInvalidate();

            var shapeCount = 0;
            var pinCount = 0;
            foreach (var symbol in parts)
            {
                shapeCount += symbol.Shapes.Count;
                pinCount += symbol.Pins.Count;
            }
            ImportLog.Add(string.Format("原理图符号：{0}（{1} 个图形，{2} 个引脚，{3} 个部件）",
                libReference, shapeCount, pinCount, parts.Count));
            try
            {
                PluginLog.Write(string.Format("symbol imported: {0} adParts={1} sourceParts={2}",
                    libReference, component.GetState_PartCountNoPart0(), parts.Count));
            }
            catch
            {
                // 仅用于诊断，取不到部件数不影响导入。
            }
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
            Func<double, double> y,
            int partId)
        {
            try
            {
                switch (shape.Kind)
                {
                    case EeSymKind.Rectangle:
                        AddRectangle(component, shape, x, y, partId);
                        break;
                    case EeSymKind.Circle:
                    case EeSymKind.Ellipse:
                        AddEllipse(component, shape, x, y, partId);
                        break;
                    case EeSymKind.Arc:
                        AddArc(component, shape, x, y, partId);
                        break;
                    case EeSymKind.Polyline:
                    case EeSymKind.Polygon:
                        AddPoly(component, shape, x, y, partId);
                        break;
                    case EeSymKind.Path:
                        AddPath(component, shape, x, y, partId);
                        break;
                    case EeSymKind.Text:
                        AddLabel(component, shape, x, y, partId);
                        break;
                }
            }
            catch (Exception ex)
            {
                ImportLog.Add("图形转换失败（" + shape.Kind + "）：" + ex.Message);
            }
        }

        private static void AddRectangle(
            ISch_Component component, EeSymShape shape, Func<double, double> x, Func<double, double> y,
            int partId)
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
            SetOwner(rectangle, partId);
            component.AddSchObject(rectangle);
        }

        private static void AddEllipse(
            ISch_Component component, EeSymShape shape, Func<double, double> x, Func<double, double> y,
            int partId)
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
            SetOwner(ellipse, partId);
            component.AddSchObject(ellipse);
        }

        private static void AddArc(
            ISch_Component component, EeSymShape shape, Func<double, double> x, Func<double, double> y,
            int partId)
        {
            if (!SvgPath.TryGetCircularArc(shape.Path, out var arc))
            {
                AddPath(component, shape, x, y, partId);
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
                AddEllipse(component, circle, x, y, partId);
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
            SetOwner(altiumArc, partId);
            component.AddSchObject(altiumArc);
        }

        private static void AddPoly(
            ISch_Component component, EeSymShape shape, Func<double, double> x, Func<double, double> y,
            int partId)
        {
            var points = Conv.ParseUnitPoints(shape.Points)
                .Select(p => (X: x(p.X), Y: y(p.Y)))
                .ToList();
            AddPolyObject(component, shape.Kind == EeSymKind.Polygon, points,
                shape.Stroke, shape.StrokeWidth, shape.Fill, partId);
        }

        private static void AddPath(
            ISch_Component component, EeSymShape shape, Func<double, double> x, Func<double, double> y,
            int partId)
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
                    closed && filled ? shape.Fill : "none", partId);
            }
        }

        private static void AddPolyObject(
            ISch_Component component,
            bool closed,
            List<(double X, double Y)> points,
            string stroke,
            double strokeWidth,
            string fill,
            int partId)
        {
            if (points.Count < 2) return;
            if (closed && points.Count < 3) closed = false;

            var objectId = closed ? TObjectId.ePolygon : TObjectId.ePolyline;
            var container = AdApi.SchServer.SchObjectFactory(
                objectId, TObjectCreationMode.eCreate_Default) as ISch_Polygon;
            if (container == null) return;

            // 先挂载再写几何：未挂载时 SetState_Vertex 不会保留坐标。
            SetOwner(container, partId);
            component.AddSchObject(container);

            // Altium 的顶点索引是 1 基（Delphi 脚本里的 Vertex[1] 起），0 基写入会落空。
            container.SetState_VerticesCount(points.Count);
            for (var i = 0; i < points.Count; i++)
                container.SetState_Vertex(i + 1, Point(points[i].X, points[i].Y));
            ApplyPolyStyle(container, closed, stroke, strokeWidth, fill);

            if (IsSentinel(container, 1))
            {
                // 兜底：若实际是 0 基，就按 0 基重写一次。
                for (var i = 0; i < points.Count; i++)
                    container.SetState_Vertex(i, Point(points[i].X, points[i].Y));
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

        private static bool IsSentinel(ISch_Polygon container, int index)
        {
            var vertex = container.Internal_GetState_Vertex(index);
            return vertex == null || vertex.GetX() == int.MaxValue || vertex.GetY() == int.MaxValue;
        }

        private static void AddLabel(
            ISch_Component component, EeSymShape shape, Func<double, double> x, Func<double, double> y,
            int partId)
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

            SetOwner(label, partId);
            component.AddSchObject(label);
        }

        private static void AddPins(
            ISch_Component component,
            IEnumerable<EePin> pins,
            Func<double, double> x,
            Func<double, double> y,
            int partId)
        {
            var fontManager = AdApi.SchServer.GetState_FontManager();
            // AD 原理图库的默认字体固定为 10pt（Times New Roman），引脚字号按同一量级取 10，
            // 取 5 会只有正常符号的一半，脚号/引脚名明显偏小。
            var fontId = fontManager.GetFontID(10, 0, false, false, false, false, "Tahoma");

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

                    SetOwner(pin, partId);
                    component.AddSchObject(pin);
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
            // AD 自己写入原理图的记录形如：
            // ModelDatafile0=CJ.PcbLib|ModelDatafileEntity0=<封装名>|ModelDatafileKind0=PCBLib
            // 第一个参数是库文件名（不是封装名），缺了它 AD 无法定位库，只会显示 Library: Not found。
            var dataFile = string.IsNullOrWhiteSpace(libraryPath)
                ? ""
                : System.IO.Path.GetFileName(libraryPath);
            implementation.AddDataFileLink(dataFile, modelName, "PCBLib");
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

        private static void SetOwner(ISch_GraphicalObject graphicalObject, int partId)
        {
            graphicalObject.SetState_OwnerPartId(partId);
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

