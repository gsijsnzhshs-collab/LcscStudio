using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using LcscLoader;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// 解析体检工具。
//   默认          联网抓取若干编号，打印解析结果（人工排查用）
//   --offline     读取 fixtures/ 的夹具与 expected.json，断言后返回非 0 表示失败
//   --record      联网录制夹具（脱敏 JSON + OBJ/STEP）到 fixtures/
internal static class Harness
{
    private static int Main(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            PrintUsage();
            return 2;
        }

        if (options.Offline) return RunOffline(options);
        if (options.Record) return RunRecord(options);
        return RunOnline(options.Ids.Count > 0 ? options.Ids : new List<string> { "C2040", "C25804", "C1525" });
    }

    // ---------------------------------------------------------------- 离线断言

    private static int RunOffline(Options options)
    {
        var dir = ResolveFixturesDir(options.Fixtures);
        if (dir == null)
        {
            Console.Error.WriteLine("ERROR: 未找到 fixtures/expected.json，可用 --fixtures <dir> 指定。");
            return 2;
        }

        var specPath = Path.Combine(dir, "expected.json");
        JObject spec;
        try
        {
            spec = JObject.Parse(File.ReadAllText(specPath));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: expected.json 解析失败：" + ex.Message);
            return 2;
        }

        var fixtures = spec["fixtures"] as JArray;
        if (fixtures == null || fixtures.Count == 0)
        {
            Console.Error.WriteLine("ERROR: expected.json 没有 fixtures 条目。");
            return 2;
        }

        Console.WriteLine("fixtures: " + dir);
        var failures = new List<string>();
        var total = 0;

        foreach (var entry in fixtures)
        {
            total++;
            var id = (string)entry["id"] ?? "";
            var kind = (string)entry["kind"] ?? "";
            Console.WriteLine();
            Console.WriteLine("===== " + id + "  (" + kind + ") =====");

            var jsonPath = Path.Combine(dir, id + ".json");
            if (id.Length == 0 || !File.Exists(jsonPath))
            {
                Fail(failures, id, "缺少夹具 " + Path.GetFileName(jsonPath));
                continue;
            }

            LcscPart part;
            EeFormatLog.Reset();
            try
            {
                part = EasyEdaParser.Parse(id, File.ReadAllText(jsonPath));
            }
            catch (Exception ex)
            {
                Fail(failures, id, "解析异常 " + ex.GetType().Name + "：" + ex.Message);
                continue;
            }

            var actual = new Dictionary<string, int>
            {
                ["pins"] = part.Symbol.Pins.Count,
                ["pads"] = part.Footprint.Shapes.Count(s => s.Kind == EeFpKind.Pad),
                ["layers"] = part.Footprint.Layers.Count,
                ["formatSkips"] = EeFormatLog.Skips,
                ["has3d"] = part.Footprint.Model != null ? 1 : 0,
            };

            Console.WriteLine(string.Format("  pins={0} pads={1} layers={2} 3d={3} formatSkips={4}",
                actual["pins"], actual["pads"], actual["layers"], actual["has3d"], actual["formatSkips"]));

            foreach (var field in new[] { "pins", "pads", "layers", "formatSkips", "has3d" })
                Compare(failures, id, field, actual[field], entry[field]);

            CheckAssets(failures, id, dir, part);
        }

        Console.WriteLine();
        if (failures.Count == 0)
        {
            Console.WriteLine("PASS: " + total + " 个夹具全部通过。");
            return 0;
        }

        Console.WriteLine("FAIL: " + failures.Count + " 项断言失败。");
        foreach (var failure in failures) Console.WriteLine("  - " + failure);
        return 1;
    }

    private static void Compare(List<string> failures, string id, string field, int actual, JToken expected)
    {
        if (expected == null || expected.Type == JTokenType.Null) return;
        var want = (int)expected;
        if (want == actual) return;
        Fail(failures, id, string.Format("{0} 期望 {1}，实际 {2}", field, want, actual));
    }

    private static void CheckAssets(List<string> failures, string id, string dir, LcscPart part)
    {
        var objPath = Path.Combine(dir, id + ".obj");
        if (File.Exists(objPath))
        {
            if (!EasyEdaApi.TryGetObjAnchor(File.ReadAllBytes(objPath), out var x, out var y, out var z))
                Fail(failures, id, "OBJ 锚点解析失败：" + Path.GetFileName(objPath));
            else
                Console.WriteLine(string.Format("  obj anchor={0:0.###},{1:0.###},{2:0.###}", x, y, z));
        }

        var stepPath = Path.Combine(dir, id + ".step");
        if (File.Exists(stepPath))
        {
            var bytes = File.ReadAllBytes(stepPath);
            var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(256, bytes.Length));
            if (head.IndexOf("ISO-10303", StringComparison.OrdinalIgnoreCase) < 0 &&
                head.IndexOf("HEADER;", StringComparison.OrdinalIgnoreCase) < 0)
                Fail(failures, id, "STEP 文件头无效：" + Path.GetFileName(stepPath));
            else
                Console.WriteLine("  step bytes=" + bytes.Length);
        }

        if (part.Footprint.Model != null && !File.Exists(objPath) && !File.Exists(stepPath))
            Console.WriteLine("  (footprint 有 3D 引用，但夹具未带 OBJ/STEP)");
    }

    private static void Fail(List<string> failures, string id, string message)
    {
        failures.Add(id + ": " + message);
        Console.WriteLine("  [FAIL] " + message);
    }

    private static string ResolveFixturesDir(string explicitDir)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir))
            return Directory.Exists(explicitDir) ? Path.GetFullPath(explicitDir) : null;

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppDomain.CurrentDomain.BaseDirectory })
        {
            var probe = start;
            for (var i = 0; i < 8 && !string.IsNullOrEmpty(probe); i++)
            {
                var candidate = Path.Combine(probe, "fixtures");
                if (File.Exists(Path.Combine(candidate, "expected.json"))) return candidate;
                probe = Path.GetDirectoryName(probe.TrimEnd(Path.DirectorySeparatorChar));
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- 录制夹具

    private static int RunRecord(Options options)
    {
        if (options.Ids.Count == 0)
        {
            Console.Error.WriteLine("ERROR: --record 需要器件编号。");
            return 2;
        }

        var dir = string.IsNullOrWhiteSpace(options.OutDir)
            ? Path.Combine(RepoRoot(), "fixtures")
            : Path.GetFullPath(options.OutDir);
        Directory.CreateDirectory(dir);

        var kind = string.IsNullOrWhiteSpace(options.Kind) ? "示例器件" : options.Kind;
        foreach (var id in options.Ids)
        {
            Console.WriteLine("record " + id + " …");
            try
            {
                var json = EasyEdaApi.GetPartJsonAsync(id, CancellationToken.None).GetAwaiter().GetResult();
                var part = EasyEdaParser.Parse(id, json);

                File.WriteAllText(Path.Combine(dir, id + ".json"), Sanitize(json, kind),
                    new UTF8Encoding(false));
                Console.WriteLine(string.Format("  json saved  pins={0} pads={1} layers={2} 3d={3}",
                    part.Symbol.Pins.Count,
                    part.Footprint.Shapes.Count(s => s.Kind == EeFpKind.Pad),
                    part.Footprint.Layers.Count,
                    part.Footprint.Model != null));

                if (options.NoAssets) continue;
                var model = part.Footprint.Model;
                if (model == null || string.IsNullOrWhiteSpace(model.Uuid)) continue;

                var obj = EasyEdaApi.DownloadObjAsync(model.Uuid, CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (obj != null)
                {
                    File.WriteAllBytes(Path.Combine(dir, id + ".obj"), obj);
                    Console.WriteLine("  obj saved  " + obj.Length + " bytes");
                }

                var step = EasyEdaApi.DownloadStepAsync(model.Uuid, CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (step != null)
                {
                    File.WriteAllBytes(Path.Combine(dir, id + ".step"), step);
                    Console.WriteLine("  step saved " + step.Length + " bytes");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  ERROR: " + ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
        }
        return 0;
    }

    // 脱敏只动身份文本，结构字段（图元、焊盘、层、3D uuid）原样保留。
    private static string Sanitize(string json, string kind)
    {
        var root = JObject.Parse(json);
        var result = root["result"] as JObject;
        if (result == null) return json;

        var sensitive = new List<string>();
        AddSensitive(sensitive, (string)result["title"]);
        AddSensitive(sensitive, (string)result["description"]);

        result["title"] = "示例器件（" + kind + "）";
        result["description"] = "";
        result["thumb"] = null;

        if (result["lcsc"] is JObject lcsc)
        {
            lcsc["link"] = null;
            lcsc["url"] = null;
        }
        if (result["szlcsc"] is JObject szlcsc)
        {
            szlcsc["url"] = null;
            szlcsc["image"] = null;
        }

        var placed = new List<string>();
        CollectParameters(result["dataStr"], sensitive);
        if (result["packageDetail"] is JObject package) CollectParameters(package["dataStr"], sensitive);
        SanitizeDocument(result, "dataStr", kind, placed);
        if (result["packageDetail"] is JObject package2) SanitizeDocument(package2, "dataStr", kind, placed);

        var text = root.ToString(Formatting.Indented);
        foreach (var literal in sensitive.Concat(placed))
            text = ReplaceIgnoreCase(text, literal, "SANITIZED");
        return text;
    }

    private static string ReplaceIgnoreCase(string text, string from, string to)
    {
        if (string.IsNullOrEmpty(from)) return text;
        var index = text.IndexOf(from, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return text;

        var builder = new StringBuilder(text.Length);
        var start = 0;
        while (index >= 0)
        {
            builder.Append(text, start, index - start).Append(to);
            start = index + from.Length;
            index = text.IndexOf(from, start, StringComparison.OrdinalIgnoreCase);
        }
        builder.Append(text, start, text.Length - start);
        return builder.ToString();
    }

    private static void AddSensitive(List<string> list, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 3) return;
        if (!list.Contains(value.Trim())) list.Add(value.Trim());
    }

    // 参数表里的厂商/型号/供应商编号属于身份信息，收集起来做全文替换。
    private static void CollectParameters(JToken token, List<string> sensitive)
    {
        var document = AsDocument(token);
        var para = (document?["head"] as JObject)?["c_para"] as JObject;
        if (para == null) return;
        foreach (var key in new[] { "Manufacturer", "Manufacturer Part", "Supplier Part", "name" })
            AddSensitive(sensitive, (string)para[key]);

        // 厂商名会以单词形式散落在符号文字或封装名里（如 Raspberry Pi / MICRON），逐词也替换。
        var manufacturer = (string)para["Manufacturer"];
        if (string.IsNullOrWhiteSpace(manufacturer)) return;
        foreach (var word in manufacturer.Split(new[] { ' ', '/', ',', '-', '(', ')' },
                     StringSplitOptions.RemoveEmptyEntries))
            if (word.Length >= 4) AddSensitive(sensitive, word);
    }

    private static JObject AsDocument(JToken token)
    {
        if (token == null) return null;
        if (token.Type == JTokenType.String)
        {
            try { return JObject.Parse((string)token); }
            catch { return null; }
        }
        return token as JObject;
    }

    private static void SanitizeDocument(JObject owner, string property, string kind, List<string> placed)
    {
        var token = owner?[property];
        if (token == null) return;

        var isString = token.Type == JTokenType.String;
        var document = AsDocument(token);
        if (document == null) return;

        var para = (document["head"] as JObject)?["c_para"] as JObject;
        if (para != null)
        {
            foreach (var key in new[] { "Manufacturer", "Manufacturer Part", "Supplier Part", "Contributor" })
                if (para[key] != null) para[key] = "SANITIZED";
            if (para["name"] != null)
            {
                var original = (string)para["name"];
                if (!string.IsNullOrWhiteSpace(original)) placed.Add(original.Trim());
                para["name"] = "示例器件 " + kind;
            }
        }

        if (isString) owner[property] = document.ToString(Formatting.None);
    }

    private static string RepoRoot()
    {
        var probe = AppDomain.CurrentDomain.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(probe); i++)
        {
            if (Directory.Exists(Path.Combine(probe, "LcscLoader", "src"))) return probe;
            probe = Path.GetDirectoryName(probe.TrimEnd(Path.DirectorySeparatorChar));
        }
        return Directory.GetCurrentDirectory();
    }

    // ---------------------------------------------------------------- 联网体检

    private static int RunOnline(List<string> ids)
    {
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

    // ---------------------------------------------------------------- 参数

    private sealed class Options
    {
        public bool Offline;
        public bool Record;
        public bool NoAssets;
        public string Fixtures;
        public string OutDir;
        public string Kind;
        public readonly List<string> Ids = new List<string>();

        public static Options Parse(string[] args)
        {
            var options = new Options();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg)
                {
                    case "--offline":
                        options.Offline = true;
                        break;
                    case "--record":
                        options.Record = true;
                        break;
                    case "--no-assets":
                        options.NoAssets = true;
                        break;
                    case "--fixtures":
                        options.Fixtures = Next(args, ref i, arg);
                        break;
                    case "--out":
                        options.OutDir = Next(args, ref i, arg);
                        break;
                    case "--kind":
                        options.Kind = Next(args, ref i, arg);
                        break;
                    case "--help":
                    case "-h":
                        PrintUsage();
                        Environment.Exit(0);
                        break;
                    default:
                        if (arg.StartsWith("--", StringComparison.Ordinal))
                            throw new ArgumentException("未知参数：" + arg);
                        options.Ids.Add(arg);
                        break;
                }
            }
            return options;
        }

        private static string Next(string[] args, ref int index, string name)
        {
            if (index + 1 >= args.Length) throw new ArgumentException(name + " 缺少取值");
            return args[++index];
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("用法:");
        Console.WriteLine("  Harness.exe [编号...]                          联网体检");
        Console.WriteLine("  Harness.exe --offline [--fixtures <dir>]        离线夹具断言");
        Console.WriteLine("  Harness.exe --record --kind <标签> [--no-assets] [--out <dir>] 编号...");
    }
}
