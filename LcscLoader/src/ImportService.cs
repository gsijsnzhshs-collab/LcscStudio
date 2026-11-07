using System;
using System.Collections.Generic;
using System.Linq;
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
    }

    internal static class ImportService
    {
        // Must run on the Altium UI thread.
        public static ImportOutcome Execute(ImportRequest request, Action<int, int> progress = null)
        {
            var outcome = new ImportOutcome();
            var currentDoc = AdApi.Client.GetCurrentView()?.GetOwnerDocument();

            IServerDocument pcbDoc = null;
            IServerDocument schDoc = null;

            if (request.Footprint)
            {
                pcbDoc = AdApi.OpenDocument("PcbLib", request.PcbLibPath);
                if (pcbDoc == null)
                    throw new InvalidOperationException("无法创建或打开 PCB 库：" + request.PcbLibPath);
                AdApi.Client.ShowDocument(pcbDoc);
                if (AdApi.PcbServer.GetCurrentPCBLibrary() == null)
                    throw new InvalidOperationException("无法打开 PCB 库：" + request.PcbLibPath);
            }

            if (request.Symbol)
            {
                schDoc = AdApi.OpenDocument("SchLib", request.SchLibPath);
                if (schDoc == null)
                    throw new InvalidOperationException("无法创建或打开原理图库：" + request.SchLibPath);
                AdApi.Client.ShowDocument(schDoc);
                if (!(AdApi.SchServer.GetCurrentSchDocument() is ISch_Lib))
                    throw new InvalidOperationException("无法打开原理图库：" + request.SchLibPath);
            }

            string lastLibReference = null;
            var total = request.Parts.Count;

            try
            {
                for (var i = 0; i < total; i++)
                {
                    var item = request.Parts[i];
                    lastLibReference = null;

                    try
                    {
                        FootprintImport footprint = null;
                        if (pcbDoc != null)
                        {
                            AdApi.Client.ShowDocument(pcbDoc);
                            AdApi.PcbServer.PreProcess();
                            try
                            {
                                footprint = FootprintImporter.Import(item.Part, request.Overwrite);
                                if (footprint != null && !footprint.Skipped && item.Model != null)
                                    Model3DImporter.AddBody(footprint, item.Part.Footprint.Model, item.Model);
                            }
                            finally
                            {
                                AdApi.PcbServer.PostProcess();
                            }
                            pcbDoc.DoFileSave("PcbLib");
                        }

                        if (schDoc != null)
                        {
                            AdApi.Client.ShowDocument(schDoc);
                            var schLib = AdApi.SchServer.GetCurrentSchDocument() as ISch_Lib;
                            if (schLib == null)
                                throw new InvalidOperationException("当前文档不是原理图库");

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
                }

                if (currentDoc != null)
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
