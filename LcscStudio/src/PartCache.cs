using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LcscLoader;

namespace LcscStudio
{
    internal sealed class PartDetails
    {
        public LcscPart Part;
        public string Json = "";
        public string StepPath = "";
        public string ObjPath = "";
        public double AnchorX, AnchorY, AnchorZ;

        public string LibReference => string.IsNullOrWhiteSpace(Part.Symbol.Name)
            ? Part.LcscId
            : Part.Symbol.Name;

        public bool HasModel => !string.IsNullOrWhiteSpace(StepPath);
    }

    internal static class PartCache
    {
        public static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LCSCLoader", "cache");
        private static readonly string ObjDir = Path.Combine(Root, "obj");
        private static readonly string StepDir = Path.Combine(Root, "step");

        public static async Task<PartDetails> LoadAsync(
            string lcscId, bool loadModel, CancellationToken cancellationToken)
        {
            var json = await EasyEdaApi.GetPartJsonAsync(lcscId, cancellationToken).ConfigureAwait(false);
            var part = EasyEdaParser.Parse(lcscId, json);
            var details = new PartDetails { Part = part, Json = json };

            if (!loadModel) return details;
            var model = part.Footprint.Model;
            if (model == null || string.IsNullOrWhiteSpace(model.Uuid)) return details;

            try
            {
                var name = SafeName(model.Uuid);
                Directory.CreateDirectory(ObjDir);
                var objPath = Path.Combine(ObjDir, name + ".obj");

                byte[] obj = null;
                if (File.Exists(objPath))
                {
                    obj = File.ReadAllBytes(objPath);
                }
                else
                {
                    obj = await EasyEdaApi.DownloadObjAsync(model.Uuid, cancellationToken)
                        .ConfigureAwait(false);
                    if (obj != null) File.WriteAllBytes(objPath, obj);
                }

                if (obj != null)
                {
                    details.ObjPath = objPath;
                    if (EasyEdaApi.TryGetObjAnchor(obj, out var ax, out var ay, out var az))
                    {
                        details.AnchorX = ax;
                        details.AnchorY = ay;
                        details.AnchorZ = az;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // A missing or malformed mesh must not block the 2D previews.
            }

            return details;
        }

        public static async Task<bool> EnsureStepAsync(PartDetails details, CancellationToken cancellationToken)
        {
            if (details == null) return false;
            if (!string.IsNullOrWhiteSpace(details.StepPath) && File.Exists(details.StepPath))
                return true;

            var model = details.Part.Footprint.Model;
            if (model == null || string.IsNullOrWhiteSpace(model.Uuid)) return false;

            try
            {
                Directory.CreateDirectory(StepDir);
                var path = Path.Combine(StepDir, SafeName(model.Uuid) + ".step");
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                {
                    var bytes = await EasyEdaApi.DownloadStepAsync(model.Uuid, cancellationToken)
                        .ConfigureAwait(false);
                    if (bytes == null) return false;
                    File.WriteAllBytes(path, bytes);
                }

                details.StepPath = path;
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<string> EnsureObjAsync(PartDetails details, CancellationToken cancellationToken)
        {
            if (details == null) return null;
            if (!string.IsNullOrWhiteSpace(details.ObjPath) && File.Exists(details.ObjPath))
                return details.ObjPath;

            var model = details.Part.Footprint.Model;
            if (model == null || string.IsNullOrWhiteSpace(model.Uuid)) return null;

            try
            {
                Directory.CreateDirectory(ObjDir);
                var path = Path.Combine(ObjDir, SafeName(model.Uuid) + ".obj");
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                {
                    var bytes = await EasyEdaApi.DownloadObjAsync(model.Uuid, cancellationToken)
                        .ConfigureAwait(false);
                    if (bytes == null || bytes.Length == 0) return null;
                    File.WriteAllBytes(path, bytes);
                }

                details.ObjPath = path;
                return path;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        private static string SafeName(string value)
        {
            return new string(value.Trim()
                .Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_')
                .ToArray());
        }
    }
}
