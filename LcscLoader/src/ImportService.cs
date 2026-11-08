using System;
using System.Collections.Generic;
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

                    try
                    {
                        FootprintImport footprint = null;
                        if (pcbLib != null && pcbDoc != null)
                        {
                            AdApi.PcbServer.PreProcess();
                            try
                            {
                                footprint = FootprintImporter.Import(item.Part, request.Overwrite, pcbLib);
                                if (footprint != null && !footprint.Skipped && item.Model != null)
                                    Model3DImporter.AddBody(footprint, item.Part.Footprint.Model, item.Model);
                            }
                            finally
                            {
                                AdApi.PcbServer.PostProcess();
                            }
                            pcbDoc.DoFileSave("PcbLib");
                        }

                        if (schLib != null && schDoc != null)
                        {
                            SymbolImporter.Import(item.Part, schLib, request.PcbLibPath,
                                footprint?.PatternName, request.Overwrite);
                            schDoc.DoFileSave("SchLib");

                            lastLibReference = string.IsNullOrWhiteSpace(item.Part.Symbol.Name)
                                ? item.Part.LcscId
                                : item.Part.Symbol.Name;
                        }

                        outcome.Imported++;
                    }
                    catch (Exception ex)
                    {
                        outcome.Failed++;
                        ImportLog.Add(item.Part.LcscId + " 导入失败：" + ex.Message);
                    }

                    progress?.Invoke(i + 1, total);
                    if (yieldBetweenItems) await Task.Yield();
                }

                // 正常情况下视图从未切换；个别版本若仍切换，收尾时切回原文档。
                if (currentDoc != null && AdApi.Client.GetCurrentView()?.GetOwnerDocument() != currentDoc)
                    AdApi.Client.ShowDocument(currentDoc);

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
                if (currentDoc != null && AdApi.Client.GetCurrentView()?.GetOwnerDocument() != currentDoc)
                    AdApi.Client.ShowDocument(currentDoc);
            }

            return outcome;
        }
    }
}
