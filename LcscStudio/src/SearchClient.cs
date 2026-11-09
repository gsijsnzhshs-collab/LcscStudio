using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using LcscLoader;

namespace LcscStudio
{
    internal sealed class SearchItem
    {
        public string LcscId { get; set; } = "";
        public string Mpn { get; set; } = "";
        public string Package { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public int Stock { get; set; }
        public double Price { get; set; }
        public bool Rohs { get; set; }

        public string StockText => Stock > 0 ? Stock.ToString("N0") : "-";
        public string PriceText => Price > 0 ? "¥" + Price.ToString("0.0000") : "-";
    }

    internal sealed class SearchPage
    {
        public int Total;
        public readonly List<SearchItem> Items = new List<SearchItem>();
    }

    internal static class SearchClient
    {
        private static readonly HttpClient Client = CreateClient();

        internal static HttpClient Http => Client;

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
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

        public static async Task<SearchPage> SearchAsync(
            string keyword, int page, int pageSize, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return new SearchPage();

            var config = EeEndpoints.Current;
            Exception last = null;
            foreach (var host in config.SearchHosts)
            {
                var path = config.SearchPath;
                var url = string.Format("{0}{1}{2}keyword={3}&currentPage={4}&pageSize={5}&version={6}",
                    host, path, path.IndexOf('?') >= 0 ? "&" : "?",
                    Uri.EscapeDataString(keyword.Trim()), page, pageSize, config.ApiVersion);
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        request.Headers.TryAddWithoutValidation("Referer", host + "/");
                        using (var response = await Client.SendAsync(request, cancellationToken)
                                   .ConfigureAwait(false))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                last = new EeNetworkError("搜索接口返回 HTTP " + (int)response.StatusCode);
                                continue;
                            }
                            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            try
                            {
                                return Parse(text);
                            }
                            catch (EeFormatError)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                throw new EeFormatError("搜索结果解析失败", ex);
                            }
                        }
                    }
                }
                catch (OperationCanceledException ex)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    last = new EeNetworkError("请求超时", ex);
                }
                catch (EeFormatError)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    last = new EeNetworkError("无法访问立创搜索接口", ex);
                }
            }
            throw new EeNetworkError("无法访问立创搜索接口，请检查网络。", last);
        }

        private static SearchPage Parse(string json)
        {
            var result = new SearchPage();
            var root = JObject.Parse(json);
            if ((int?)root["code"] != 200) return result;

            var payload = JsonUtil.Obj(root["result"]);
            result.Total = (int?)payload["total"] ?? 0;

            foreach (var token in JsonUtil.Arr(payload["productList"]))
            {
                var item = new SearchItem
                {
                    LcscId = JsonUtil.Str(token["number"]),
                    Mpn = JsonUtil.Str(token["mpn"]),
                    Package = JsonUtil.Str(token["package"]),
                    Manufacturer = JsonUtil.Str(token["manufacturer"]),
                    Stock = (int?)token["stock"] ?? 0,
                    Rohs = (bool?)token["ifRoHS"] ?? false,
                };

                var images = token["image"] as JArray;
                if (images != null && images.Count > 0)
                {
                    item.ImageUrl = JsonUtil.Str(images[0]["96x96"]);
                }

                var prices = token["price"] as JArray;
                if (prices != null && prices.Count > 0)
                {
                    var tier = prices[0] as JArray;
                    if (tier != null && tier.Count > 1)
                    {
                        if (double.TryParse(JsonUtil.Str(tier[1]), out var price))
                            item.Price = price;
                    }
                }

                if (!string.IsNullOrWhiteSpace(item.LcscId))
                    result.Items.Add(item);
            }

            return result;
        }
    }

    // 立创商城商品参数：商品详情页内嵌 paramVOList（属性名 + 参数值），
    // 例如 阻值 10kΩ、精度 ±1%、功率 100mW。按编号缓存，避免重复下载整页。
    internal static class LcscProductInfo
    {
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, List<KeyValuePair<string, string>>> Cache =
            new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.OrdinalIgnoreCase);

        public static async Task<List<KeyValuePair<string, string>>> LoadParametersAsync(
            string lcscId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(lcscId))
                return new List<KeyValuePair<string, string>>();

            lock (CacheLock)
            {
                if (Cache.TryGetValue(lcscId, out var cached)) return cached;
            }

            var list = new List<KeyValuePair<string, string>>();
            var url = "https://www.lcsc.com/product-detail/" + Uri.EscapeDataString(lcscId) + ".html";
            // 商城偶尔会返回一次 403/超时，重试一次再放弃。
            for (var attempt = 0; attempt < 2 && list.Count == 0; attempt++)
            {
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
                        using (var response = await SearchClient.Http.SendAsync(request, cancellationToken)
                                   .ConfigureAwait(false))
                        {
                            if (!response.IsSuccessStatusCode) continue;
                            var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            list = Parse(html);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                }
                catch
                {
                    // 参数页拿不到时保持空列表，不影响符号/封装预览。
                }
            }

            if (list.Count == 0) return list;
            lock (CacheLock) Cache[lcscId] = list;
            return list;
        }

        private static List<KeyValuePair<string, string>> Parse(string html)
        {
            var result = new List<KeyValuePair<string, string>>();
            var array = ExtractArray(html, "\"paramVOList\":");
            if (array == null) return result;
            try
            {
                foreach (var token in JArray.Parse(array))
                {
                    var name = JsonUtil.Str(token["paramName"]).Trim();
                    var value = JsonUtil.Str(token["paramValue"]).Trim();
                    if (name.Length == 0 || value.Length == 0) continue;
                    result.Add(new KeyValuePair<string, string>(name, value));
                }
            }
            catch
            {
                // 页面结构变化时宁可不显示，也不把半截 JSON 抛给 UI。
            }
            return result;
        }

        // 从 HTML 中截取紧跟 key 的 JSON 数组文本（按括号配平，跳过字符串里的括号）。
        private static string ExtractArray(string text, string key)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var start = text.IndexOf(key, StringComparison.Ordinal);
            if (start < 0) return null;
            start = text.IndexOf('[', start + key.Length);
            if (start < 0) return null;

            var depth = 0;
            var inString = false;
            var escape = false;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (inString)
                {
                    if (escape) { escape = false; continue; }
                    if (c == '\\') { escape = true; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0) return text.Substring(start, i - start + 1);
                }
            }
            return null;
        }
    }
}
