using System;
using System.Linq;
using System.Threading;
using LcscLoader;

internal static class Harness
{
    private static int Main(string[] args)
    {
        var ids = args.Length > 0 ? args : new[] { "C2040", "C25804", "C1525" };
        foreach (var id in ids)
        {
            Console.WriteLine("===== " + id + " =====");
            try
            {
                var part = EasyEdaApi.GetPartAsync(id, CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine("Title        : " + part.Title);
                Console.WriteLine("Description  : " + part.Description);
                Console.WriteLine("Manufacturer : " + part.Manufacturer + " / " + part.ManufacturerPart);
                Console.WriteLine("Supplier     : " + part.SupplierPart);

                Console.WriteLine("Symbol       : " + part.Symbol.Name + " (" + part.Symbol.Prefix + ")");
                Console.WriteLine("  origin     : " + part.Symbol.OriginX + ", " + part.Symbol.OriginY);
                Console.WriteLine("  shapes     : " + part.Symbol.Shapes.Count + " [" +
                    string.Join(", ", part.Symbol.Shapes.GroupBy(s => s.Kind)
                        .Select(g => g.Key + "=" + g.Count())) + "]");
                Console.WriteLine("  pins       : " + part.Symbol.Pins.Count);
                foreach (var shape in part.Symbol.Shapes.Where(s => s.Kind == EeSymKind.Path))
                {
                    var parsed = SvgPath.Parse(shape.Path, 0.02);
                    Console.WriteLine("    path       : " + parsed.Count + " sub-path(s), " +
                        parsed.Sum(p => p.Points.Count) + " points, closed " +
                        string.Join("/", parsed.Select(p => p.Closed ? "1" : "0")));
                }
                foreach (var pin in part.Symbol.Pins.Take(6))
                    Console.WriteLine("    pin " + pin.Number + " '" + pin.Name + "' at " +
                        pin.X + "," + pin.Y + " rot " + pin.Rotation + " len " + pin.Length);

                var fp = part.Footprint;
                Console.WriteLine("Footprint    : " + fp.Name);
                Console.WriteLine("  box        : " + fp.BoxX + ", " + fp.BoxY + " size " +
                    fp.BoxWidth + " x " + fp.BoxHeight);
                Console.WriteLine("  layers     : " + fp.Layers.Count);
                Console.WriteLine("  shapes     : " + fp.Shapes.Count + " [" +
                    string.Join(", ", fp.Shapes.GroupBy(s => s.Kind)
                        .Select(g => g.Key + "=" + g.Count())) + "]");

                foreach (var pad in fp.Shapes.Where(s => s.Kind == EeFpKind.Pad))
                    Console.WriteLine("    pad '" + pad.Number + "' " + pad.Shape + " at " +
                        pad.X.ToString("0.###") + "," + pad.Y.ToString("0.###") + " size " +
                        pad.Width.ToString("0.###") + "x" + pad.Height.ToString("0.###") +
                        " drill " + (pad.HoleRadius * 2).ToString("0.###") +
                        " layer " + pad.LayerId + " rot " + pad.Rotation);

                var arcs = fp.Shapes.Where(s => s.Kind == EeFpKind.Arc).ToList();
                foreach (var arc in arcs)
                {
                    var ok = SvgPath.TryGetCircularArc(arc.Path, out var a);
                    Console.WriteLine("    arc        : " + (ok
                        ? string.Format("center {0:0.###},{1:0.###} r {2:0.###} {3:0.##}..{4:0.##} sweep {5:0.##}",
                            a.CenterX, a.CenterY, a.RadiusX, a.StartAngle, a.EndAngle, a.SweepAngle)
                        : "NOT CIRCULAR") + "  [" + arc.Path + "]");
                }
                Console.WriteLine("  3d model   : " + (fp.Model == null
                    ? "(none)"
                    : fp.Model.Uuid + " origin " + fp.Model.OriginX + "," + fp.Model.OriginY +
                      " z " + fp.Model.Z + " rot " + fp.Model.RotX + "," + fp.Model.RotY + "," + fp.Model.RotZ));

                if (fp.Model != null)
                {
                    var step = EasyEdaApi.DownloadStepAsync(fp.Model.Uuid, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    Console.WriteLine("  STEP       : " + (step == null ? "FAILED" : step.Length + " bytes"));

                    var obj = EasyEdaApi.DownloadObjAsync(fp.Model.Uuid, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    if (obj != null && EasyEdaApi.TryGetObjAnchor(obj, out var ax, out var ay, out var az))
                        Console.WriteLine("  anchor     : " + ax + ", " + ay + ", " + az);
                    else
                        Console.WriteLine("  anchor     : FAILED");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.GetType().Name + ": " + ex.Message);
            }
            Console.WriteLine();
        }
        return 0;
    }
}
