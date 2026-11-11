using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DXP;
using PCB;
using SCH;

namespace LcscLoader
{
    internal sealed class ImportPart
    {
        public LcscPart Part;
        public Model3DData Model;
    }

    internal sealed class ImportRequest
    {
        public string SchLibPath = AdApi.SchLibPath;
        public string PcbLibPath = AdApi.PcbLibPath;
        public bool Symbol = true;
        public bool Footprint = true;
        public bool Overwrite;
        public bool Place;
        public readonly List<ImportPart> Parts = new List<ImportPart>();
    }

    internal sealed class ImportOutcome
    {
        public int Imported;
        public int Failed;
        public bool Cancelled;
    }

    internal static class ImportService
    {
        // 同步入口：用于放置前刷新等单件场景，整个过程留在 AD UI 线程。
        public static ImportOutcome Execute(ImportRequest request, Action<int, int> progress = null)
        {
            return ExecuteCoreAsync(request, progress, CancellationToken.None, false)
                .GetAwaiter().GetResult();
        }

        // 批量导入：每件之间 await Task.Yield()，让 UI 重绘并响应取消；
        // SDK 写入仍在调用线程（AD UI 线程）上执行。
        public static Task<ImportOutcome> ExecuteAsync(
            ImportRequest request, Action<int, int> progress, CancellationToken cancellationToken)
        {
            return ExecuteCoreAsync(request, progress, cancellationToken, true);
        }

        // Must run on the Altium UI thread.
        private static async Task<ImportOutcome> ExecuteCoreAsync(
            ImportRequest request, Action<int, int> progress,
            CancellationToken cancellationToken, bool yieldBetweenItems)
        {
            var outcome = new ImportOutcome();
            var currentDoc = AdApi.Client.GetCurrentView()?.GetOwnerDocument();

            IServerDocument pcbDoc = null;
            IServerDocument schDoc = null;
            IPCB_Library pcbLib = null;
            ISch_Lib schLib = null;

            // 库文件用“隐藏打开 + 按路径取接口”，全程不激活文档，避免界面在
            // PcbLib / SchLib 之间来回切换。
            if (request.Footprint)
            {
                pcbDoc = AdApi.EnsureDocumentHidden("PcbLib", request.PcbLibPath);
                pcbLib = AdApi.PcbServer.Internal_GetPCBLibraryByPath(request.PcbLibPath) as IPCB_Library;
                if (pcbDoc == null || pcbLib == null)
                    throw new InvalidOperationException("无法打开 PCB 库：" + request.PcbLibPath);
            }

            if (request.Symbol)
            {
                schDoc = AdApi.EnsureDocumentHidden("SchLib", request.SchLibPath);
                schLib = AdApi.SchServer.Internal_GetSchDocumentByPath(request.SchLibPath) as ISch_Lib;
                if (schDoc == null || schLib == null)
                    throw new InvalidOperationException("无法打开原理图库：" + request.SchLibPath);
            }

            string lastLibReference = null;
            var total = request.Parts.Count;

            try
            {
                for (var i = 0; i < total; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        outcome.Cancelled = true;
                        break;
                    }

                    var item = request.Parts[i];
                    lastLibReference = null;
                    var pcbLibName = Path.GetFileName(request.PcbLibPath);
                    var schLibName = Path.GetFileName(request.SchLibPath);
                    var itemCaption = item.Part.LcscId;

                    try
                    {
                        FootprintImport footprint = null;
                        if (pcbLib != null && pcbDoc != null)
                        {
                            var footprintName = string.IsNullOrWhiteSpace(item.Part.Footprint.Name)
                                ? item.Part.LcscId
                                : item.Part.Footprint.Name;
                            var footprintWatch = Stopwatch.StartNew();
                            var modelWatch = new Stopwatch();
                            var bodyAdded = false;

                            PluginLog.Write("import: preProcess start " + itemCaption);
                            AdApi.PcbServer.PreProcess();
                            PluginLog.Write("import: preProcess ok " + itemCaption);
                            try
                            {
                                PluginLog.Write("import: footprint start " + itemCaption);
                                footprint = FootprintImporter.Import(item.Part, request.Overwrite, pcbLib);
                                PluginLog.Write("import: footprint ok skipped=" + (footprint?.Skipped ?? true) + " " + itemCaption);
                                if (footprint != null && !footprint.Skipped && item.Model != null)
                                {
                                    modelWatch.Start();
                                    PluginLog.Write("import: 3d start " + itemCaption);
                                    bodyAdded = Model3DImporter.AddBody(
                                        footprint, item.Part.Footprint.Model, item.Model);
                                    modelWatch.Stop();
                                    PluginLog.Write("import: 3d ok bodyAdded=" + bodyAdded + " " + itemCaption);
                                }
                                PluginLog.Write("import: savePcb start " + itemCaption);
                                pcbDoc.DoFileSave("PcbLib");
                                PluginLog.Write("import: savePcb ok " + itemCaption);
                            }
                            catch (Exception ex)
                            {
                                ImportLog.Add(string.Format(
                                    "[封装] 失败 库={0} 元件={1} 耗时={2}ms：{3}",
                                    pcbLibName, footprintName, footprintWatch.ElapsedMilliseconds, ex.Message));
                                PluginLog.Write("import step footprint failed: " + itemCaption + "\n" + ex);
                                throw;
                            }
                            finally
                            {
                                AdApi.PcbServer.PostProcess();
                            }
                            footprintWatch.Stop();

                            var footprintCaption = string.IsNullOrWhiteSpace(footprint.PatternName)
                                ? footprintName
                                : footprint.PatternName;
                            ImportLog.Add(string.Format(
                                "[封装] {0} 库={1} 元件={2} 图元={3} 库字节={4} 耗时={5}ms",
                                footprint.Skipped ? "跳过（已存在）"
                                    : string.IsNullOrWhiteSpace(footprint.PatternName) ? "跳过（无可用封装）" : "成功",
                                pcbLibName, footprintCaption, item.Part.Footprint.Shapes.Count,
                                FileBytes(request.PcbLibPath), footprintWatch.ElapsedMilliseconds));

                            if (item.Model != null && !footprint.Skipped &&
                                !string.IsNullOrWhiteSpace(footprint.PatternName))
                            {
                                ImportLog.Add(string.Format(
                                    "[3D] {0} 元件={1} 模型={2} 模型字节={3} 耗时={4}ms",
                                    bodyAdded ? "成功" : "失败", footprintCaption,
                                    Path.GetFileName(item.Model.StepPath),
                                    FileBytes(item.Model.StepPath), modelWatch.ElapsedMilliseconds));
                            }
                            else if (item.Model == null && !footprint.Skipped &&
                                     item.Part.Footprint.Model != null)
                            {
                                ImportLog.Add("[3D] 跳过 元件=" + footprintCaption + " 原因=模型未下载");
                            }
                            else if (item.Model != null && footprint.Skipped)
                            {
                                ImportLog.Add("[3D] 跳过 元件=" + footprintCaption + " 原因=封装已存在未覆盖");
                            }
                        }

                        if (schLib != null && schDoc != null)
                        {
                            var libReference = string.IsNullOrWhiteSpace(item.Part.Symbol.Name)
                                ? item.Part.LcscId
                                : item.Part.Symbol.Name;
                            var existed = false;
                            try { existed = schLib.GetState_SchComponentByLibRef(libReference) != null; }
                            catch
                            {
                                // 查询失败按“不存在”处理，只影响日志里的跳过标记。
                            }
                            var symbolWatch = Stopwatch.StartNew();
                            try
                            {
                                PluginLog.Write("import: symbol start " + itemCaption);
                                SymbolImporter.Import(item.Part, schLib, request.PcbLibPath,
                                    footprint?.PatternName, request.Overwrite);
                                PluginLog.Write("import: symbol ok " + itemCaption);
                                PluginLog.Write("import: saveSch start " + itemCaption);
                                schDoc.DoFileSave("SchLib");
                                PluginLog.Write("import: saveSch ok " + itemCaption);
                            }
                            catch (Exception ex)
                            {
                                ImportLog.Add(string.Format(
                                    "[符号] 失败 库={0} 元件={1} 耗时={2}ms：{3}",
                                    schLibName, libReference, symbolWatch.ElapsedMilliseconds, ex.Message));
                                PluginLog.Write("import step symbol failed: " + itemCaption + "\n" + ex);
                                throw;
                            }
                            symbolWatch.Stop();

                            var shapeCount = 0;
                            var pinCount = 0;
                            var parts = item.Part.SymbolParts.Count > 0
                                ? item.Part.SymbolParts
                                : new List<EeSymbol> { item.Part.Symbol };
                            foreach (var symbol in parts)
                            {
                                shapeCount += symbol.Shapes.Count;
                                pinCount += symbol.Pins.Count;
                            }
                            ImportLog.Add(string.Format(
                                "[符号] {0} 库={1} 元件={2} 图形={3} 引脚={4} 库字节={5} 耗时={6}ms",
                                existed && !request.Overwrite ? "跳过（已存在）" : "成功",
                                schLibName, libReference, shapeCount, pinCount,
                                FileBytes(request.SchLibPath), symbolWatch.ElapsedMilliseconds));

                            lastLibReference = libReference;
                        }

                        outcome.Imported++;
                    }
                    catch (Exception ex)
                    {
                        outcome.Failed++;
                        ImportLog.Add(item.Part.LcscId + " 导入失败：" + ex.Message);
                        PluginLog.Write("import outer failed: " + item.Part.LcscId + "\n" + ex);
                    }

                    progress?.Invoke(i + 1, total);
                    if (yieldBetweenItems) await Task.Yield();
                }

                // 正常情况下视图从未切换；个别版本若仍切换，收尾时切回原文档。
                // 用 RestoreView 吞异常 —— ShowDocument 对 EnsureDocumentHidden 打开的隐藏文档
                // 在 AD22.0.x 上会抛 RPC_E_SERVERFAULT (0x80010105)，
                // 异常不能冒泡覆盖已经计入的成功结果。
                RestoreView(currentDoc);

                if (request.Place && lastLibReference != null)
                {
                    try
                    {
                        SymbolImporter.PlaceOnCurrentSheet(lastLibReference, request.SchLibPath);
                    }
                    catch (Exception ex)
                    {
                        ImportLog.Add("放置到原理图失败：" + ex.Message);
                    }
                }
            }
            finally
            {
                RestoreView(currentDoc);
            }

            return outcome;
        }

        // 恢复用户原来的视图。纯界面体验，任何异常都不能影响导入结果：
        // AD 对 EnsureDocumentHidden 打开的隐藏文档调用 ShowDocument 会抛 RPC_E_SERVERFAULT。
        private static void RestoreView(IServerDocument currentDoc)
        {
            if (currentDoc == null) return;
            try
            {
                var owner = AdApi.Client.GetCurrentView()?.GetOwnerDocument();
                if (owner != null && ReferenceEquals(owner, currentDoc)) return;
                AdApi.Client.ShowDocument(currentDoc);
            }
            catch (Exception ex)
            {
                PluginLog.Write("restore view skipped: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // 日志里的“字节数”用于确认文件确实落盘，取不到时记 0，不影响导入流程。
        private static long FileBytes(string path)
        {
            try { return string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? 0 : new FileInfo(path).Length; }
            catch { return 0; }
        }
    }
}
