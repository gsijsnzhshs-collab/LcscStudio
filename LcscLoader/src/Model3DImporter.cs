using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PCB;

namespace LcscLoader
{
    internal sealed class Model3DData
    {
        public string StepPath = "";
        public double AnchorX, AnchorY, AnchorZ;
    }

    internal static class Model3DImporter
    {
        // Downloads the STEP file and its companion OBJ, which supplies the anchor
        // (XY centre, lowest Z) used by the official placement rule.
        public static async Task<Model3DData> PrepareAsync(EeModel3D model, CancellationToken cancellationToken)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Uuid)) return null;

            var name = SafeName(model.Uuid);
            var stepPath = Path.Combine(AdApi.ModelDir, name + ".step");
            var anchorPath = Path.Combine(AdApi.ModelDir, name + ".anchor");

            if (File.Exists(stepPath) && TryReadAnchor(anchorPath, out var data))
            {
                data.StepPath = stepPath;
                return data;
            }

            try
            {
                if (!File.Exists(stepPath) || new FileInfo(stepPath).Length == 0)
                {
                    var step = await EasyEdaApi.DownloadStepAsync(model.Uuid, cancellationToken)
                        .ConfigureAwait(false);
                    if (step == null)
                    {
                        ImportLog.Add("3D 模型 STEP 下载失败，跳过：" + model.Uuid);
                        return null;
                    }
                    File.WriteAllBytes(stepPath, step);
                }

                var obj = await EasyEdaApi.DownloadObjAsync(model.Uuid, cancellationToken)
                    .ConfigureAwait(false);
                if (obj == null || !EasyEdaApi.TryGetObjAnchor(obj, out var ax, out var ay, out var az))
                {
                    ImportLog.Add("无法获取 3D 模型放置基准（OBJ 缺失），跳过：" + model.Uuid);
                    return null;
                }

                WriteAnchor(anchorPath, ax, ay, az);
                return new Model3DData
                {
                    StepPath = stepPath,
                    AnchorX = ax,
                    AnchorY = ay,
                    AnchorZ = az,
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ImportLog.Add("3D 模型准备失败：" + ex.Message);
                return null;
            }
        }

        public static bool AddBody(FootprintImport footprint, EeModel3D placement, Model3DData data)
        {
            if (footprint == null || footprint.Component == null || placement == null || data == null)
                return false;

            try
            {
                var component = footprint.Component;
                var body = AdApi.PcbServer.Internal_PCBObjectFactory(
                    (int)TObjectId.eComponentBodyObject,
                    (int)TDimensionKind.eNoDimension,
                    (int)TObjectCreationMode.eCreate_Default) as IPCB_ComponentBody;
                if (body == null)
                {
                    ImportLog.Add("无法创建 3D 元件体");
                    return false;
                }

                var model = body.ModelFactory_FromFilename(data.StepPath, false);
                if (model == null)
                {
                    ImportLog.Add("无法加载 STEP 模型：" + data.StepPath);
                    return false;
                }

                var rx = placement.RotX;
                var ry = placement.RotY;
                var rz = placement.RotZ;

                // p_local = t + R * (p_step - C), so the model origin is t - R * C.
                var rotation = RotationZxy(rx, ry, rz);
                var originX = EeUnits.ToMm(placement.OriginX);
                var originY = EeUnits.ToMm(placement.OriginY);
                var originZ = EeUnits.ToMm(placement.Z);

                var positionX = originX - footprint.Footprint.BoxX - footprint.Footprint.BoxWidth / 2.0;
                var positionY = footprint.Footprint.BoxHeight
                                - (originY - footprint.Footprint.BoxY)
                                - footprint.Footprint.BoxHeight / 2.0;

                var offsetX = positionX - (rotation[0][0] * data.AnchorX + rotation[0][1] * data.AnchorY + rotation[0][2] * data.AnchorZ);
                var offsetY = positionY - (rotation[1][0] * data.AnchorX + rotation[1][1] * data.AnchorY + rotation[1][2] * data.AnchorZ);
                var offsetZ = originZ - (rotation[2][0] * data.AnchorX + rotation[2][1] * data.AnchorY + rotation[2][2] * data.AnchorZ);

                model.SetState(rx, ry, rz, AdApi.MmToCoord(offsetZ));
                model.SetOrigin(new CoordPoint
                {
                    X = AdApi.MmToCoord(offsetX) + component.GetState_XLocation(),
                    Y = AdApi.MmToCoord(offsetY) + component.GetState_YLocation(),
                });
                body.SetModel(model);

                try { body.SetState_FromModel(); }
                catch (Exception ex) { ImportLog.Add("3D 元件体外框更新失败：" + ex.Message); }

                body.SetState_Layer(TV6_Layer.eV6_Mechanical13);
                component.GetState_Board().AddPCBObject(body);
                component.AddPCBObject(body);

                ImportLog.Add(string.Format("3D 模型：{0}", Path.GetFileName(data.StepPath)));
                return true;
            }
            catch (Exception ex)
            {
                ImportLog.Add("3D 模型放置失败：" + ex.Message);
                return false;
            }
        }

        // R = Ry(ry) * Rx(rx) * Rz(rz), matching the official placement convention.
        private static double[][] RotationZxy(double rx, double ry, double rz)
        {
            const double toRadians = Math.PI / 180.0;
            double x = rx * toRadians, y = ry * toRadians, z = rz * toRadians;
            double cx = Math.Cos(x), sx = Math.Sin(x);
            double cy = Math.Cos(y), sy = Math.Sin(y);
            double cz = Math.Cos(z), sz = Math.Sin(z);
            return new[]
            {
                new[] { cy * cz + sy * sx * sz, -cy * sz + sy * sx * cz, sy * cx },
                new[] { cx * sz, cx * cz, -sx },
                new[] { -sy * cz + cy * sx * sz, sy * sz + cy * sx * cz, cy * cx },
            };
        }

        private static bool TryReadAnchor(string path, out Model3DData data)
        {
            data = null;
            if (!File.Exists(path)) return false;
            try
            {
                var fields = File.ReadAllText(path)
                    .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 3) return false;
                if (!double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return false;
                if (!double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return false;
                if (!double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) return false;
                data = new Model3DData { AnchorX = x, AnchorY = y, AnchorZ = z };
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteAnchor(string path, double x, double y, double z)
        {
            File.WriteAllText(path, string.Format(CultureInfo.InvariantCulture, "{0:R} {1:R} {2:R}", x, y, z));
        }

        private static string SafeName(string value)
        {
            var cleaned = value.Trim().Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return new string(cleaned.ToArray());
        }
    }
}
