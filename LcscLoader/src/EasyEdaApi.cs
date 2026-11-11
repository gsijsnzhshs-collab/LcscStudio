using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace LcscLoader
{
    internal static class EasyEdaApi
    {
        private static readonly HttpClient Client = CreateClient();
        private static readonly HttpClient DownloadClient = CreateDownloadClient();

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseCookies = true,
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(EeEndpoints.Current.RequestTimeoutSeconds),
            };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
            return client;
        }

        private static HttpClient CreateDownloadClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(EeEndpoints.Current.DownloadTimeoutSeconds),
            };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            return client;
        }

        public static async Task<string> GetPartJsonAsync(string lcscId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(lcscId))
                throw new ArgumentException("未提供立创编号", nameof(lcscId));

            var config = EeEndpoints.Current;
            Exception last = null;
            foreach (var host in config.ApiHosts)
            {
                var path = config.ProductPath.Replace("{lcsc}", Uri.EscapeDataString(lcscId.Trim()));
                var url = host + WithVersion(path, config.ApiVersion);
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        request.Headers.TryAddWithoutValidation("Referer", host + "/");
                        request.Headers.TryAddWithoutValidation("Origin", host);
                        using (var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                        {
                            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            if (!response.IsSuccessStatusCode)
                            {
                                last = (int)response.StatusCode == 404
                                    ? (Exception)new EeNotFoundError("未找到器件 " + lcscId.Trim())
                                    : new EeNetworkError(string.Format("接口返回 HTTP {0}", (int)response.StatusCode));
                                continue;
                            }
                            // easyeda.com / lceda.cn 常在 HTTP 200 里塞 success:false + code:404 表示"查无此元件"
                            try
                            {
                                var root = Newtonsoft.Json.Linq.JObject.Parse(body);
                                var ok = (bool?)root["success"];
                                var code = (int?)root["code"];
                                if (ok == false && code == 404)
                                {
                                    last = (Exception)new EeNotFoundError("未找到器件 " + lcscId.Trim());
                                    continue;
                                }
                                if (ok == false)
                                {
                                    var msg = (string)root["message"] ?? "未知错误";
                                    last = new EeNetworkError(host + " 返回 success=false：" + msg);
                                    continue;
                                }
                            }
                            catch
                            {
                                // 不是 JSON，直接返回
                            }
                            return body;
                        }
                    }
                }
                catch (OperationCanceledException ex)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    last = new EeNetworkError("请求超时", ex);
                }
                catch (Exception ex)
                {
                    last = new EeNetworkError("无法访问立创商城接口", ex);
                }
            }
            if (last is EeNotFoundError) throw last;
            throw new EeNetworkError("无法访问立创商城接口，请检查网络。", last);
        }

        public static async Task<LcscPart> GetPartAsync(string lcscId, CancellationToken cancellationToken)
        {
            var json = await GetPartJsonAsync(lcscId, cancellationToken).ConfigureAwait(false);
            try
            {
                return EasyEdaParser.Parse(lcscId.Trim(), json);
            }
            catch (EeNotFoundError)
            {
                throw;
            }
            catch (EeFormatError)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new EeFormatError("器件数据解析失败", ex);
            }
        }

        // Tries to obtain the STEP model. The identifier may be an EasyEDA 3D library
        // UUID (docType 16) or the asset UUID used by the module host.
        public static async Task<byte[]> DownloadStepAsync(string uuid, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(uuid)) return null;
            uuid = uuid.Trim();
            var config = EeEndpoints.Current;

            var direct = await TryDownloadFromModulesAsync(
                config.StepPath.Replace("{uuid}", Uri.EscapeDataString(uuid)), cancellationToken)
                .ConfigureAwait(false);
            if (IsStep(direct)) return direct;

            var asset = await ResolveAssetUuidAsync(uuid, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(asset) || string.Equals(asset, uuid, StringComparison.OrdinalIgnoreCase))
                return IsStep(direct) ? direct : null;

            var fromAsset = await TryDownloadFromModulesAsync(
                config.StepPath.Replace("{uuid}", Uri.EscapeDataString(asset)), cancellationToken)
                .ConfigureAwait(false);
            if (IsStep(fromAsset)) return fromAsset;

            return IsStep(direct) ? direct : null;
        }

        public static async Task<byte[]> DownloadObjAsync(string uuid, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(uuid)) return null;
            uuid = uuid.Trim();
            var config = EeEndpoints.Current;

            var direct = await TryDownloadFromModulesAsync(
                config.ObjPath.Replace("{uuid}", Uri.EscapeDataString(uuid)),
                cancellationToken).ConfigureAwait(false);
            if (IsObj(direct)) return direct;

            var asset = await ResolveAssetUuidAsync(uuid, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(asset) || string.Equals(asset, uuid, StringComparison.OrdinalIgnoreCase))
                return IsObj(direct) ? direct : null;

            var fromAsset = await TryDownloadFromModulesAsync(
                config.ObjPath.Replace("{uuid}", Uri.EscapeDataString(asset)),
                cancellationToken).ConfigureAwait(false);
            return IsObj(fromAsset) ? fromAsset : (IsObj(direct) ? direct : null);
        }

        private static async Task<string> ResolveAssetUuidAsync(string uuid, CancellationToken cancellationToken)
        {
            var config = EeEndpoints.Current;
            foreach (var host in config.ApiHosts)
            {
                var url = host + config.ComponentPath.Replace("{uuid}", Uri.EscapeDataString(uuid));
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        request.Headers.TryAddWithoutValidation("Referer", host + "/");
                        using (var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                        {
                            if (!response.IsSuccessStatusCode) continue;
                            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            var root = JObject.Parse(text);
                            var result = JsonUtil.Obj(root["result"]);
                            if (!result.HasValues) continue;

                            var docType = (int)JsonUtil.Num(result["docType"]);
                            if (docType != 16 && docType != 0) continue;

                            var document = result["dataStr"];
                            if (document != null && document.Type == JTokenType.String)
                            {
                                try { document = JObject.Parse((string)document); }
                                catch { continue; }
                            }
                            var head = JsonUtil.Obj(document?["head"]);
                            var asset = JsonUtil.Str(head["uuid"]);
                            if (!string.IsNullOrEmpty(asset)) return asset;

                            var fallback = JsonUtil.Str(result["uuid"]);
                            if (!string.IsNullOrEmpty(fallback)) return fallback;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    // 超时：尝试下一个 host。
                }
                catch
                {
                    // Try the next host.
                }
            }
            return null;
        }

        private static async Task<byte[]> TryDownloadFromModulesAsync(string path, CancellationToken cancellationToken)
        {
            foreach (var host in EeEndpoints.Current.ModuleHosts)
            {
                try
                {
                    using (var response = await DownloadClient.GetAsync(host + path, cancellationToken).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode) continue;
                        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        if (bytes != null && bytes.Length > 64) return bytes;
                    }
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    // 超时：尝试下一个 host。
                }
                catch
                {
                    // Try the next host.
                }
            }
            return null;
        }

        private static string WithVersion(string path, string version)
        {
            return path + (path.IndexOf('?') >= 0 ? "&" : "?") + "version=" + version;
        }

        private static bool IsStep(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 64) return false;
            var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(256, bytes.Length));
            return head.IndexOf("ISO-10303", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   head.IndexOf("HEADER;", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsObj(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 32) return false;
            var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(512, bytes.Length));
            return head.IndexOf("\nv ", StringComparison.Ordinal) >= 0 ||
                   head.StartsWith("v ", StringComparison.Ordinal) ||
                   head.IndexOf("\r\nv ", StringComparison.Ordinal) >= 0;
        }

        // The OBJ bounding box supplies the rotation anchor used when placing a
        // model: ((minX+maxX)/2, (minY+maxY)/2, minZ), in the model's units.
        public static bool TryGetObjAnchor(byte[] objBytes, out double x, out double y, out double z)
        {
            x = y = z = 0;
            if (objBytes == null || objBytes.Length == 0) return false;

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            var count = 0;

            using (var reader = new StreamReader(new MemoryStream(objBytes), Encoding.ASCII))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length < 2 || line[0] != 'v' || line[1] != ' ') continue;
                    var fields = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length < 4) continue;
                    if (!double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var vx)) continue;
                    if (!double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var vy)) continue;
                    if (!double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var vz)) continue;

                    if (vx < minX) minX = vx;
                    if (vx > maxX) maxX = vx;
                    if (vy < minY) minY = vy;
                    if (vy > maxY) maxY = vy;
                    if (vz < minZ) minZ = vz;
                    if (vz > maxZ) maxZ = vz;
                    count++;
                }
            }

            if (count == 0) return false;
            x = (minX + maxX) / 2.0;
            y = (minY + maxY) / 2.0;
            z = minZ;
            return true;
        }
    }
}
